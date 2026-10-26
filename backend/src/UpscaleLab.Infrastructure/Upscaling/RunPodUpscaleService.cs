
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace UpscaleLab.Infrastructure.Upscaling;

public sealed class RunPodUpscaleService(
    HttpClient httpClient,
    RunPodOptions options)
{
    public async Task<RunPodUpscaleResult> UpscaleAsync(
        string inputPath,
        int scale,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (scale is not (2 or 4))
            throw new ArgumentException("Scale must be 2 or 4.");

        if (string.IsNullOrWhiteSpace(options.ApiKey) ||
            string.IsNullOrWhiteSpace(options.EndpointId))
            throw new InvalidOperationException(
                "RunPod API configuration is missing.");

        if (!File.Exists(inputPath))
            throw new FileNotFoundException(
                "Input image not found.", inputPath);

        // RunPod v2 테스트 Worker 입력 제한
        var fileInfo = new FileInfo(inputPath);

        if (fileInfo.Length > 2 * 1024 * 1024)
            throw new InvalidOperationException(
                "Input image exceeds RunPod test limit (2MB).");

        using var deadline =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        deadline.CancelAfter(
            TimeSpan.FromSeconds(options.TimeoutSeconds));

        var token = deadline.Token;

        string imageBase64 = Convert.ToBase64String(
            await File.ReadAllBytesAsync(inputPath, token));

        string baseUrl = options.ApiBaseUrl.TrimEnd('/');
        string endpointId = Uri.EscapeDataString(
            options.EndpointId);

        var runUrl = $"{baseUrl}/{endpointId}/run";

        using var runRequest = new HttpRequestMessage(
            HttpMethod.Post, runUrl);

        runRequest.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer", options.ApiKey);

        runRequest.Content = JsonContent.Create(new
        {
            input = new
            {
                operation = "upscale",
                scale,
                image_base64 = imageBase64
            }
        });

        using var runResponse =
            await httpClient.SendAsync(runRequest, token);

        runResponse.EnsureSuccessStatusCode();

        using var runDocument = JsonDocument.Parse(
            await runResponse.Content.ReadAsStringAsync(token));

        var root = runDocument.RootElement;

        var jobId = root.GetProperty("id").GetString();

        if (string.IsNullOrWhiteSpace(jobId))
            throw new InvalidOperationException(
                "RunPod did not return a job ID.");

        var statusUrl =
            $"{baseUrl}/{endpointId}/status/{Uri.EscapeDataString(jobId)}";

        while (true)
        {
            token.ThrowIfCancellationRequested();

            using var statusRequest =
                new HttpRequestMessage(HttpMethod.Get, statusUrl);

            statusRequest.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer", options.ApiKey);

            using var statusResponse =
                await httpClient.SendAsync(statusRequest, token);

            statusResponse.EnsureSuccessStatusCode();

            using var statusDocument = JsonDocument.Parse(
                await statusResponse.Content.ReadAsStringAsync(token));

            var result = statusDocument.RootElement;

            var status = result.GetProperty("status")
                .GetString();

            if (status == "COMPLETED")
            {
                var output = result.GetProperty("output");

                if (output.TryGetProperty("status", out var workerStatus)
                    && workerStatus.GetString() != "completed")
                {
                    throw new InvalidOperationException(
                        "RunPod worker reported an unsuccessful result.");
                }

                var resultBase64 =
                    output.GetProperty("image_base64").GetString();

                if (string.IsNullOrWhiteSpace(resultBase64))
                    throw new InvalidOperationException(
                        "RunPod output image is missing.");

                // 현재 Worker의 PNG 응답 제한: 5MB
                if (resultBase64.Length > 7 * 1024 * 1024)
                    throw new InvalidOperationException(
                        "RunPod output exceeds test size limit.");

                byte[] resultBytes =
                    Convert.FromBase64String(resultBase64);

                if (resultBytes.Length > 5 * 1024 * 1024 ||
                    resultBytes.Length < 8 ||
                    !resultBytes.AsSpan(0, 8).SequenceEqual(
                        new byte[]
                        {
                            137, 80, 78, 71, 13, 10, 26, 10
                        }))
                {
                    throw new InvalidOperationException(
                        "RunPod returned invalid PNG data.");
                }

                // RunPod Worker가 반환한 실제 출력 해상도
                int width = output.GetProperty("width").GetInt32();
                int height = output.GetProperty("height").GetInt32();

                if (width <= 0 || height <= 0)
                {
                    throw new InvalidOperationException(
                        "RunPod returned invalid image dimensions.");
                }

                // PNG 헤더에 기록된 실제 해상도와 비교
                // PNG IHDR width/height: 바이트 16~23
                if (resultBytes.Length < 24)
                {
                    throw new InvalidOperationException(
                        "RunPod returned an incomplete PNG.");
                }

                int pngWidth = System.Buffers.Binary.BinaryPrimitives
                    .ReadInt32BigEndian(resultBytes.AsSpan(16, 4));

                int pngHeight = System.Buffers.Binary.BinaryPrimitives
                    .ReadInt32BigEndian(resultBytes.AsSpan(20, 4));

                if (width != pngWidth || height != pngHeight)
                {
                    throw new InvalidOperationException(
                        "RunPod image dimensions do not match PNG data.");
                }

                Directory.CreateDirectory(
                    Path.GetDirectoryName(
                        Path.GetFullPath(outputPath))!);

                await File.WriteAllBytesAsync(
                    outputPath,
                    resultBytes,
                    token);

                return new RunPodUpscaleResult(
                    outputPath,
                    width,
                    height);
            }

            if (status is "FAILED" or "CANCELLED" or "TIMED_OUT")
            {
                var error = result.TryGetProperty(
                    "error", out var errorElement)
                    ? errorElement.ToString()
                    : "Unknown error";

                throw new InvalidOperationException(
                    $"RunPod job {status}: {error}");
            }

            if (status is not (
                "IN_QUEUE" or "IN_PROGRESS"))
            {
                throw new InvalidOperationException(
                    $"Unexpected RunPod status: {status}");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(2), token);
        }
    }
}
public sealed record RunPodUpscaleResult(
    string FilePath,
    int Width,
    int Height);
