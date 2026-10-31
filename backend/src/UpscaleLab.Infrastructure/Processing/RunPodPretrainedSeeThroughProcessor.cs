using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Processing;
using UpscaleLab.Application.Storage;
using UpscaleLab.Infrastructure.Storage;

namespace UpscaleLab.Infrastructure.Processing;

/// <summary>
/// Runs pretrained character-part inference on a separate RunPod endpoint.
/// Existing RunPod upscaling requests and settings are never used here.
/// </summary>
public sealed class RunPodPretrainedSeeThroughProcessor(
    HttpClient client,
    SeeThroughOptions options,
    IStorageService storage,
    IAmazonS3 s3,
    S3StorageOptions s3Options) : IImageLayerProcessor
{
    public bool IsConfigured => options.Enabled &&
        string.Equals(options.Provider, "RunPod", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(options.RunPodEndpointId) &&
        !string.IsNullOrWhiteSpace(options.RunPodApiKey) &&
        !string.IsNullOrWhiteSpace(s3Options.S3BucketName);

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private const long MaxInputBytes = 16L * 1024 * 1024;
    private const long MaxArchiveBytes = 128L * 1024 * 1024;
    private const long MaxUncompressedBytes = 300L * 1024 * 1024;

    public async Task<IReadOnlyList<ProcessedLayer>> ProcessAsync(
        LayerProcessingRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            throw new ConfigurationException("SeeThrough:Enabled=true 및 Provider=RunPod 설정이 필요합니다.");
        if (string.IsNullOrWhiteSpace(options.RunPodEndpointId) ||
            string.IsNullOrWhiteSpace(options.RunPodApiKey) ||
            string.IsNullOrWhiteSpace(s3Options.S3BucketName))
            throw new ConfigurationException("See-through 전용 RunPod 엔드포인트, API 키와 AWS S3 버킷 설정이 필요합니다.");
        if (!File.Exists(request.InputPath))
            throw new FileNotFoundException("See-through 입력 파일을 찾을 수 없습니다.", request.InputPath);
        if (new FileInfo(request.InputPath).Length is < 1 or > MaxInputBytes)
            throw new InvalidDataException("캐릭터 입력 이미지는 16MB 이하여야 합니다.");

        // The worker receives temporary signed URLs, not AWS credentials.
        var prefix = $"processing-temp/character-parts/{Guid.NewGuid():N}";
        var inputKey = $"{prefix}/input-image";
        var outputKey = $"{prefix}/layers.zip";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.RunPodTimeoutSeconds, 60, 3600)));
        var token = deadline.Token;
        Directory.CreateDirectory(request.OutputDirectory);

        try
        {
            await using (var input = File.OpenRead(request.InputPath))
                await storage.UploadAsync(input, inputKey, "application/octet-stream", token);

            var urlLifetime = TimeSpan.FromHours(2);
            var inputUrl = storage.CreateDownloadUrl(inputKey, urlLifetime);
            var outputUrl = s3.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = s3Options.S3BucketName,
                Key = outputKey,
                Verb = HttpVerb.PUT,
                Expires = DateTime.UtcNow.Add(urlLifetime)
            });

            // Never log signed URLs, full response bodies, or API keys.
            var apiBase = options.RunPodApiBaseUrl.TrimEnd('/');
            var endpoint = Uri.EscapeDataString(options.RunPodEndpointId);
            using var submit = new HttpRequestMessage(HttpMethod.Post, $"{apiBase}/{endpoint}/run")
            {
                Content = JsonContent.Create(new
                {
                    input = new
                    {
                        operation = "character_layers_pretrained",
                        input_url = inputUrl,
                        output_url = outputUrl
                    }
                })
            };
            submit.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.RunPodApiKey);
            using var accepted = await client.SendAsync(submit, token);
            accepted.EnsureSuccessStatusCode();
            using var acceptedJson = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync(token));
            if (!acceptedJson.RootElement.TryGetProperty("id", out var jobIdProperty) ||
                jobIdProperty.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(jobIdProperty.GetString()))
                throw new InvalidDataException("RunPod가 유효한 작업 ID를 반환하지 않았습니다.");
            var jobId = Uri.EscapeDataString(jobIdProperty.GetString()!);

            while (true)
            {
                token.ThrowIfCancellationRequested();
                using var statusRequest = new HttpRequestMessage(HttpMethod.Get, $"{apiBase}/{endpoint}/status/{jobId}");
                statusRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.RunPodApiKey);
                using var response = await client.SendAsync(statusRequest, token);
                response.EnsureSuccessStatusCode();
                using var statusJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                var root = statusJson.RootElement;
                if (!root.TryGetProperty("status", out var stateValue) ||
                    stateValue.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("RunPod가 작업 상태를 반환하지 않았습니다.");
                var state = stateValue.GetString();
                if (state == "COMPLETED")
                {
                    if (!root.TryGetProperty("output", out var output) ||
                        output.ValueKind != JsonValueKind.Object ||
                        !output.TryGetProperty("status", out var workerStatus) ||
                        workerStatus.ValueKind != JsonValueKind.String ||
                        workerStatus.GetString() != "completed")
                        throw new InvalidDataException("See-through 모델이 정상 결과를 반환하지 않았습니다.");
                    break;
                }
                if (state is "FAILED" or "CANCELLED" or "TIMED_OUT")
                    throw new InvalidOperationException($"RunPod 캐릭터 분리 작업이 {state} 상태로 종료되었습니다.");
                if (state is not ("IN_QUEUE" or "IN_PROGRESS"))
                    throw new InvalidDataException("알 수 없는 RunPod 작업 상태입니다.");
                await Task.Delay(PollInterval, token);
            }

            var archivePath = Path.Combine(request.OutputDirectory, "runpod-layers.zip");
            try
            {
                await using (var archive = File.Create(archivePath))
                    await storage.DownloadAsync(outputKey, archive, token);
                ExtractValidatedArchive(archivePath, request.OutputDirectory);
            }
            finally
            {
                if (File.Exists(archivePath)) File.Delete(archivePath);
            }
            return await SeeThroughManifestReader.ReadAsync(request.OutputDirectory, token);
        }
        finally
        {
            // A failed S3 cleanup must not replace the original processing exception.
            foreach (var key in new[] { inputKey, outputKey })
            {
                try { await storage.DeleteAsync(key, CancellationToken.None); }
                catch { /* temporary prefix should also have an S3 lifecycle expiry rule */ }
            }
        }
    }

    public static void ExtractValidatedArchive(string archivePath, string destination)
    {
        if (new FileInfo(archivePath).Length > MaxArchiveBytes)
            throw new InvalidDataException("See-through 결과 ZIP 크기가 128MB를 초과했습니다.");

        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count is < 2 or > 65 ||
            archive.Entries.Count(x => x.FullName == "manifest.json") != 1 ||
            archive.Entries.Select(x => x.FullName).Distinct(StringComparer.Ordinal).Count() != archive.Entries.Count ||
            archive.Entries.Sum(x => x.Length) > MaxUncompressedBytes)
            throw new InvalidDataException("See-through 압축 결과의 파일 개수 또는 크기가 올바르지 않습니다.");

        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name) ||
                (entry.FullName != "manifest.json" &&
                 (!entry.FullName.StartsWith("layers/", StringComparison.Ordinal) ||
                  !entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))) ||
                entry.FullName.Contains('\\') || entry.FullName.Contains("..", StringComparison.Ordinal))
                throw new InvalidDataException("See-through 압축에 허용되지 않은 파일 경로가 있습니다.");
            // Reject symbolic links even if their path looks harmless.
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("See-through 결과에서 심볼릭 링크를 허용하지 않습니다.");
            var filePath = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!filePath.StartsWith(root, StringComparison.Ordinal))
                throw new InvalidDataException("See-through 결과 ZIP 경로가 작업 폴더를 벗어났습니다.");
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            entry.ExtractToFile(filePath, overwrite: false);
        }
    }
}
