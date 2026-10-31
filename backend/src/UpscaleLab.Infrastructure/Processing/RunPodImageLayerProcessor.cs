using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using ImageMagick;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Processing;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Storage;
using UpscaleLab.Infrastructure.Upscaling;

namespace UpscaleLab.Infrastructure.Processing;

// Only short-lived, object-scoped URLs reach the GPU worker; AWS credentials stay in the API.
public sealed class RunPodImageLayerProcessor(HttpClient http, RunPodOptions options,
    IAmazonS3 s3, S3StorageOptions storage) : IImageLayerProcessor
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const long MaxFileBytes = 256 * 1024 * 1024;
    private string ApiKey => string.IsNullOrWhiteSpace(options.LayersApiKey) ? options.ApiKey : options.LayersApiKey;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(options.LayersEndpointId) && !string.IsNullOrWhiteSpace(storage.S3BucketName);

    public async Task<IReadOnlyList<ProcessedLayer>> ProcessAsync(LayerProcessingRequest request, CancellationToken ct)
    {
        if (!IsConfigured) throw new ConfigurationException("클라우드 배경 분리 공급자가 아직 연결되지 않았습니다.");
        if (request.JobId == Guid.Empty) throw new ArgumentException("배경 분리 작업 ID가 필요합니다.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.LayersTimeoutSeconds, 60, 3600)));
        var token = deadline.Token;
        Directory.CreateDirectory(request.OutputDirectory);
        var info = new MagickImageInfo(request.InputPath);
        if (info.Width < 1 || info.Height < 1 || info.Width > 32768 || info.Height > 32768 ||
            (long)info.Width * info.Height > 64_000_000) throw new InvalidDataException("분리할 이미지 크기가 올바르지 않습니다.");
        var input = Path.Combine(request.OutputDirectory, "cloud-input.png");
        int width, height;
        using (var image = new MagickImage(request.InputPath))
        {
            image.AutoOrient(); image.Strip(); image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaOption.Remove);
            width = (int)image.Width; height = (int)image.Height;
            await image.WriteAsync(input, MagickFormat.Png, token);
        }
        string hash;
        await using (var content = File.OpenRead(input)) hash = Convert.ToHexString(await SHA256.HashDataAsync(content, token));
        string? maskHash = null;
        var maskMode = request.RefineSelection ? "selection" : null;
        if (request.RefineSelection && request.SubjectMaskPath == null) throw new InvalidDataException("재분리할 선택 영역이 없습니다.");
        if (request.SubjectMaskPath is { } maskPath)
        {
            await using var mask = File.OpenRead(maskPath);
            maskHash = Convert.ToHexString(await SHA256.HashDataAsync(mask, token));
        }
        var prefix = $"cloud-jobs/layers/{request.JobId:N}/";
        var baseUrl = options.ApiBaseUrl.TrimEnd('/') + "/" + Uri.EscapeDataString(options.LayersEndpointId);
        RemoteJob? remote = null;
        try
        {
            remote = await ReadJsonAsync<RemoteJob>(prefix + "remote.json", token);
            if (remote != null && (remote.InputSha256 != hash || remote.EndpointId != options.LayersEndpointId || remote.MaskSha256 != maskHash || remote.MaskMode != maskMode))
                throw new InvalidDataException("원격 분리 작업의 입력 정보가 일치하지 않습니다.");

            // A durable completion marker also survives RunPod's 30-minute result retention.
            var complete = await ReadJsonAsync<LayerCompletion>(prefix + "complete.json", token);
            if (complete == null)
            {
                if (remote == null)
                {
                    await using var content = File.OpenRead(input);
                    await s3.PutObjectAsync(new PutObjectRequest { BucketName = storage.S3BucketName,
                        Key = prefix + "input.png", InputStream = content, ContentType = "image/png" }, token);
                    if (request.SubjectMaskPath is { } path)
                    {
                        await using var mask = File.OpenRead(path);
                        await s3.PutObjectAsync(new PutObjectRequest { BucketName = storage.S3BucketName,
                            Key = prefix + "mask.png", InputStream = mask, ContentType = "image/png" }, token);
                    }
                    var expires = DateTime.UtcNow.AddSeconds(Math.Clamp(options.LayersTimeoutSeconds, 60, 3600) + 600);
                    using var submission = await SendAsync(HttpMethod.Post, baseUrl + "/run", new {
                        input = new { operation = "layers", job_id = request.JobId.ToString("N"), input_sha256 = hash,
                            image_url = Sign(prefix + "input.png", HttpVerb.GET, expires),
                            mask_url = maskHash == null ? null : Sign(prefix + "mask.png", HttpVerb.GET, expires),
                            mask_sha256 = maskHash,
                            mask_mode = maskMode,
                            background_url = Sign(prefix + "background.png", HttpVerb.PUT, expires),
                            foreground_url = Sign(prefix + "foreground.png", HttpVerb.PUT, expires),
                            completion_url = Sign(prefix + "complete.json", HttpVerb.PUT, expires), width, height },
                        policy = new { executionTimeout = Math.Clamp(options.LayersTimeoutSeconds, 60, 3600) * 1000,
                            ttl = Math.Clamp(options.LayersTimeoutSeconds, 60, 3600) * 1000 }
                    }, token);
                    var id = submission.RootElement.GetProperty("id").GetString();
                    if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException("클라우드 작업 ID가 없습니다.");
                    remote = new(id, hash, options.LayersEndpointId, maskHash, maskMode);
                    await s3.PutObjectAsync(new PutObjectRequest { BucketName = storage.S3BucketName,
                        Key = prefix + "remote.json", ContentBody = JsonSerializer.Serialize(remote, Json),
                        ContentType = "application/json" }, token);
                }
                while (complete == null)
                {
                    token.ThrowIfCancellationRequested();
                    using var status = await SendAsync(HttpMethod.Get,
                        baseUrl + "/status/" + Uri.EscapeDataString(remote.Id), null, token);
                    var state = status.RootElement.GetProperty("status").GetString();
                    if (state == "COMPLETED")
                    {
                        complete = await ReadJsonAsync<LayerCompletion>(prefix + "complete.json", token)
                            ?? throw new InvalidDataException("클라우드 분리 결과가 누락되었습니다.");
                    }
                    else if (state is "FAILED" or "CANCELLED" or "TIMED_OUT")
                        throw new InvalidOperationException("클라우드 배경 분리 작업이 실패했습니다.");
                    else if (state is not ("IN_QUEUE" or "IN_PROGRESS"))
                        throw new InvalidDataException("클라우드 작업 상태가 올바르지 않습니다.");
                    else
                    {
                        if (request.ReportProgress != null) await request.ReportProgress(state == "IN_QUEUE" ? 20 : 45, token);
                        await Task.Delay(TimeSpan.FromSeconds(2), token);
                        complete = await ReadJsonAsync<LayerCompletion>(prefix + "complete.json", token);
                    }
                }
            }
            if (complete.JobId != request.JobId.ToString("N") || complete.InputSha256 != hash ||
                complete.Width != width || complete.Height != height || complete.Status != "completed" || complete.MaskSha256 != maskHash || complete.MaskMode != maskMode)
                throw new InvalidDataException("클라우드 분리 결과 규격이 일치하지 않습니다.");
            var background = Path.Combine(request.OutputDirectory, "background.png");
            var foreground = Path.Combine(request.OutputDirectory, "foreground.png");
            await DownloadPngAsync(prefix + "background.png", background, width, height, false, token);
            await DownloadPngAsync(prefix + "foreground.png", foreground, width, height, true, token);
            if (request.ReportProgress != null) await request.ReportProgress(75, token);
            return [new(background, LayerType.Background, 0, 0, 0, 0, 0, 1, 0, 0),
                new(foreground, LayerType.Body, 1, 1, 0, 0, 0, 1, width * .025, height * .025)];
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            if (remote != null)
            {
                try { using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    using var _ = await SendAsync(HttpMethod.Post, baseUrl + "/cancel/" + Uri.EscapeDataString(remote.Id), null, cancel.Token); }
                catch { /* The provider TTL still bounds work if cancellation cannot reach it. */ }
            }
            throw new TimeoutException("클라우드 배경 분리 제한 시간을 초과했습니다.");
        }
        finally { File.Delete(input); }
    }

    private string Sign(string key, HttpVerb verb, DateTime expires) => s3.GetPreSignedURL(new GetPreSignedUrlRequest {
        BucketName = storage.S3BucketName, Key = key, Verb = verb, Expires = expires });

    private async Task<T?> ReadJsonAsync<T>(string key, CancellationToken ct) where T : class
    {
        try
        {
            using var result = await s3.GetObjectAsync(storage.S3BucketName, key, ct);
            if (result.ContentLength > 16384) throw new InvalidDataException("클라우드 작업 정보가 너무 큽니다.");
            return await JsonSerializer.DeserializeAsync<T>(result.ResponseStream, Json, ct)
                ?? throw new InvalidDataException("클라우드 작업 정보가 올바르지 않습니다.");
        }
        catch (AmazonS3Exception error) when (error.StatusCode == HttpStatusCode.NotFound) { return null; }
    }

    private async Task DownloadPngAsync(string key, string path, int width, int height, bool alpha, CancellationToken ct)
    {
        using var result = await s3.GetObjectAsync(storage.S3BucketName, key, ct);
        if (result.ContentLength is <= 0 or > MaxFileBytes) throw new InvalidDataException("분리 결과 파일 크기가 올바르지 않습니다.");
        await using (var file = File.Create(path)) await result.ResponseStream.CopyToAsync(file, ct);
        var info = new MagickImageInfo(path);
        if (info.Format != MagickFormat.Png || info.Width != width || info.Height != height)
            throw new InvalidDataException("분리 PNG의 캔버스 크기가 일치하지 않습니다.");
        using var image = new MagickImage(path);
        if (alpha && (!image.HasAlpha || image.IsOpaque)) throw new InvalidDataException("전경의 투명도가 누락되었습니다.");
        if (!alpha && !image.IsOpaque) throw new InvalidDataException("채워진 배경에 빈 공간이 있습니다.");
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        if (body != null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"클라우드 분리 요청 실패 (HTTP {(int)response.StatusCode}).");
        // Never propagate provider error bodies, signed URLs, or credentials into job errors.
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    private sealed record RemoteJob(string Id, string InputSha256, string EndpointId, string? MaskSha256 = null, string? MaskMode = null);
    private sealed record LayerCompletion(string Status, string JobId, string InputSha256, int Width, int Height, string? MaskSha256 = null, string? MaskMode = null);
}
