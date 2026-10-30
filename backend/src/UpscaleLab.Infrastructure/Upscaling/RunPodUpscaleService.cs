
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ImageMagick;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Upscaling;

namespace UpscaleLab.Infrastructure.Upscaling;

public sealed class RunPodUpscaleService(
    HttpClient httpClient,
    RunPodOptions options)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.ApiKey) &&
        !string.IsNullOrWhiteSpace(options.EndpointId);

    public async Task<RunPodUpscaleResult> UpscaleToResolutionAsync(string inputPath, int targetLongEdge,
        string outputPath, CancellationToken cancellationToken,
        Func<int, CancellationToken, Task>? reportProgress = null)
    {
        if (!IsConfigured) throw new ConfigurationException("클라우드 업스케일링 공급자가 아직 연결되지 않았습니다.");
        if (!UpscaleResolution.IsSupported(targetLongEdge))
            throw new ValidationException("FHD, QHD, 4K 중 목표 해상도를 선택해 주세요.");
        var info = new MagickImageInfo(inputPath);
        if (info.Width < 1 || info.Height < 1 || Math.Max(info.Width, info.Height) >= targetLongEdge)
            throw new ValidationException("현재 이미지보다 높은 해상도를 선택해 주세요.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        var token = deadline.Token;
        using var original = new MagickImage(inputPath);
        original.AutoOrient();
        var width = checked((int)original.Width);
        var height = checked((int)original.Height);
        var target = UpscaleResolution.Dimensions(width, height, targetLongEdge);
        var scale = UpscaleResolution.NativeScale(width, height, targetLongEdge);

        // The existing CUDA worker accepts <=1MP inputs, <=4MP outputs and <=5MB PNGs.
        // At most 1024x1024 output pixels also bound even uncompressed RGBA tile payloads.
        const int overlap = 32;
        var tileEdge = 1024 / scale;
        var coreEdge = width <= tileEdge && height <= tileEdge ? tileEdge : tileEdge - overlap * 2;
        var total = ((width + coreEdge - 1) / coreEdge) * ((height + coreEdge - 1) / coreEdge);
        var completed = 0;
        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        Directory.CreateDirectory(outputDirectory);
        var tileDirectory = Path.Combine(outputDirectory, ".tiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tileDirectory);

        try
        {
            using var canvas = new MagickImage(MagickColors.Transparent,
                checked((uint)(width * scale)), checked((uint)(height * scale)));
            for (var y = 0; y < height; y += coreEdge)
            for (var x = 0; x < width; x += coreEdge)
            {
                token.ThrowIfCancellationRequested();
                var coreWidth = Math.Min(coreEdge, width - x);
                var coreHeight = Math.Min(coreEdge, height - y);
                var left = Math.Max(0, x - overlap);
                var top = Math.Max(0, y - overlap);
                var right = Math.Min(width, x + coreWidth + overlap);
                var bottom = Math.Min(height, y + coreHeight + overlap);
                var inputTile = Path.Combine(tileDirectory, $"{completed:D4}-input.png");
                var outputTile = Path.Combine(tileDirectory, $"{completed:D4}-output.png");
                using (var tile = new MagickImage(original))
                {
                    tile.Crop(new MagickGeometry(left, top, (uint)(right - left), (uint)(bottom - top)));
                    tile.ResetPage();
                    tile.Strip();
                    await tile.WriteAsync(inputTile, MagickFormat.Png, token);
                }

                // Every tile must succeed on the configured remote GPU. There is no local inference fallback.
                var result = await UpscaleAsync(inputTile, scale, outputTile, token);
                if (result.Width != (right - left) * scale || result.Height != (bottom - top) * scale)
                    throw new InvalidDataException("클라우드 타일 결과 해상도가 요청과 일치하지 않습니다.");
                using (var tile = new MagickImage(outputTile))
                {
                    tile.Crop(new MagickGeometry((x - left) * scale, (y - top) * scale,
                        (uint)(coreWidth * scale), (uint)(coreHeight * scale)));
                    tile.ResetPage();
                    canvas.Composite(tile, x * scale, y * scale, CompositeOperator.Over);
                }
                completed++;
                if (reportProgress != null) await reportProgress(15 + completed * 60 / total, token);
                File.Delete(inputTile);
                File.Delete(outputTile);
            }

            // Real-ESRGAN's arbitrary output scale uses Lanczos after native inference too.
            // Resize the combined image once so adjacent tiles share the same sampling grid.
            canvas.FilterType = FilterType.Lanczos;
            canvas.Resize(new MagickGeometry((uint)target.Width, (uint)target.Height) { IgnoreAspectRatio = true });
            canvas.Strip();
            await canvas.WriteAsync(outputPath, MagickFormat.Png, token);
            return new(outputPath, target.Width, target.Height);
        }
        finally
        {
            var resolved = Path.GetFullPath(tileDirectory);
            if (resolved.StartsWith(outputDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }

    public async Task<RunPodUpscaleResult> UpscaleAsync(
        string inputPath,
        int scale,
        string outputPath,
        CancellationToken cancellationToken,
        int? targetLongEdge = null)
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
                target_long_edge = targetLongEdge,
                resolution = targetLongEdge switch
                {
                    1920 => "FHD",
                    2560 => "QHD",
                    3840 => "4K",
                    _ => throw new ArgumentOutOfRangeException(nameof(targetLongEdge))
                },
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

                // RunPod 출력 PNG 최대 허용 크기: 32MiB
                if (resultBase64.Length > ((32 * 1024 * 1024 + 2) / 3) * 4)
                    throw new InvalidOperationException(
                        "RunPod output exceeds test size limit.");

                byte[] resultBytes =
                    Convert.FromBase64String(resultBase64);

                if (resultBytes.Length > 32 * 1024 * 1024 ||
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
