using ImageMagick;
using UpscaleLab.Application.Upscaling;
using UpscaleLab.Application.Processing;
using UpscaleLab.Application.Storage;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Upscaling;

namespace UpscaleLab.Infrastructure.Processing;

public sealed class RunPodProjectImageProcessor(
    ExistingLayerProjectProcessor layerProcessor,
    RunPodUpscaleService base64Service,
    RunPodS3UpscaleService s3Service,
    RunPodOptions options,
    IStorageService storageService
) : IProjectImageProcessor
{
    public bool Supports(ProcessingOperation operation)
    {
        return (operation == ProcessingOperation.Layers && layerProcessor.Supports(operation)) ||
            (operation == ProcessingOperation.Upscale && base64Service.IsConfigured);
    }

    public async Task<ProjectProcessingResult> ProcessAsync(
        ProjectProcessingRequest request,
        CancellationToken cancellationToken)
    {
        // 기존 레이어 분해 기능 유지
        if (request.Operation == ProcessingOperation.Layers)
        {
            return await layerProcessor.ProcessAsync(
                request,
                cancellationToken);
        }

        if (request.Operation != ProcessingOperation.Upscale)
        {
            throw new NotSupportedException(
                $"Unsupported operation: {request.Operation}");
        }

        Directory.CreateDirectory(request.OutputDirectory);

        string outputPath = Path.Combine(
            request.OutputDirectory,
            $"upscaled_{request.JobId:N}.png");

                if (request.TargetLongEdge is not (1920 or 2560 or 3840))
        {
            throw new ArgumentException(
                "FHD, QHD, 4K 중 하나를 선택해야 합니다.");
        }

        // Base64 전송 방식: RunPod v5가 FHD/QHD/4K 전체 이미지를 직접 처리
        if (!options.UseS3Transfer)
        {
            string? temporaryInputPath = null;
            try
            {
                temporaryInputPath = PrepareRunPodInput(
                    request.InputPath,
                    request.OutputDirectory,
                    request.UpscaleFactor);

                var workerInputPath =
                    temporaryInputPath ?? request.InputPath;

                var result = await base64Service.UpscaleAsync(
                    workerInputPath,
                    request.UpscaleFactor,
                    outputPath,
                    cancellationToken,
                    request.TargetLongEdge);

                return CreateUpscaleResult(
                    request, result.FilePath, result.Width, result.Height);
            }
            finally
            {
                if (temporaryInputPath != null &&
                    File.Exists(temporaryInputPath))
                {
                    File.Delete(temporaryInputPath);
                }
            }
        }

        // 신규 S3 전송 방식
        return await ProcessWithS3Async(
            request,
            outputPath,
            cancellationToken);
    }

    private async Task<ProjectProcessingResult> ProcessWithS3Async(
        ProjectProcessingRequest request,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(request.InputPath))
        {
            throw new FileNotFoundException(
                "Input image not found.",
                request.InputPath);
        }

        // Python Worker의 입력 용량 제한과 일치시킴
        const long maxInputBytes = 20L * 1024 * 1024;

        if (new FileInfo(request.InputPath).Length > maxInputBytes)
        {
            throw new InvalidOperationException(
                "Input image exceeds 20MB limit.");
        }

        string temporaryId = Guid.NewGuid().ToString("N");

        string prefix =
            $"temporary/runpod/{request.JobId:N}/{temporaryId}";

        string inputKey = $"{prefix}/input";
        string outputKey = $"{prefix}/output.png";

        var lifetime = TimeSpan.FromMinutes(30);

        try
        {
            // 1. 입력 이미지를 S3 임시 위치에 업로드
            await using (var input = File.OpenRead(request.InputPath))
            {
                await storageService.UploadAsync(
                    input,
                    inputKey,
                    "application/octet-stream",
                    cancellationToken);
            }

            // 2. S3 다운로드 URL 생성
            string inputUrl = storageService.CreateDownloadUrl(
                inputKey,
                lifetime);

            // 3. S3 결과 업로드 URL 생성
            string uploadUrl = storageService.CreateUploadUrl(
                outputKey,
                "image/png",
                lifetime);

            // 4. RunPod 클라우드 GPU 처리 요청
            var result = await s3Service.UpscaleAsync(
                inputUrl,
                uploadUrl,
                request.UpscaleFactor,
                cancellationToken,
                request.TargetLongEdge);

            // 5. RunPod가 업로드한 결과를 다운로드
            await using (var output = File.Create(outputPath))
            {
                await storageService.DownloadAsync(
                    outputKey,
                    output,
                    cancellationToken);
            }

            // 6. 결과 이미지 존재 여부 확인
            if (new FileInfo(outputPath).Length == 0)
            {
                throw new InvalidDataException(
                    "RunPod returned an empty output image.");
            }

            // 최종 PNG/해상도 검증과 영구 S3 저장은
            // ProjectProcessingService에서 수행
            return CreateUpscaleResult(
                request, outputPath, result.Width, result.Height);
        }
        finally
        {
            // 성공/실패 시 임시 S3 파일 정리 시도
            await TryDeleteTemporaryAsync(inputKey);
            await TryDeleteTemporaryAsync(outputKey);
        }
    }

    private static string? PrepareRunPodInput(
    string inputPath,
    string outputDirectory,
    int upscaleFactor)
{
    const long maxInputPixels = 1_000_000;
    const long maxInputBytes = 2L * 1024 * 1024;

    if (!File.Exists(inputPath))
        throw new FileNotFoundException(
            "Input image not found.", inputPath);

    var info = new MagickImageInfo(inputPath);

    var originalPixels = (long)info.Width * info.Height;
    var originalBytes = new FileInfo(inputPath).Length;

    if (originalPixels <= 0)
        throw new InvalidDataException(
            "원본 이미지 해상도가 올바르지 않습니다.");

    if (originalPixels <= maxInputPixels &&
        originalBytes <= maxInputBytes)
        return null;

    Directory.CreateDirectory(outputDirectory);

    var temporaryPath = Path.Combine(
        outputDirectory,
        $"runpod_input_{Guid.NewGuid():N}.jpg");

    try
    {
        using var image = new MagickImage(inputPath);

        image.AutoOrient();

        if ((long)image.Width * image.Height > maxInputPixels)
        {
            var ratio = Math.Sqrt(
                (double)maxInputPixels /
                ((long)image.Width * image.Height));

            var width = Math.Max(1,
                (int)Math.Floor(image.Width * ratio));

            var height = Math.Max(1,
                (int)Math.Floor(image.Height * ratio));

            image.Resize(new MagickGeometry(
                (uint)width,
                (uint)height)
            {
                IgnoreAspectRatio = true
            });
        }

        image.Format = MagickFormat.Jpeg;
        image.Quality = 85;
        image.Strip();
        image.Write(temporaryPath);

        if (new FileInfo(temporaryPath).Length > maxInputBytes)
            throw new InvalidOperationException(
                "RunPod 입력 이미지가 2MB를 초과합니다.");

        return temporaryPath;
    }
    catch
    {
        if (File.Exists(temporaryPath))
            File.Delete(temporaryPath);

        throw;
    }
}


private static ProjectProcessingResult CreateUpscaleResult(
    ProjectProcessingRequest request,
    string outputPath,
    int resultWidth,
    int resultHeight)
{
    if (request.TargetLongEdge is int target)
    {
        using var original = new MagickImage(request.InputPath);
        original.AutoOrient();

        var expected = UpscaleResolution.Dimensions(
            (int)original.Width,
            (int)original.Height,
            target);

        if (resultWidth != expected.Width ||
            resultHeight != expected.Height)
        {
            throw new InvalidDataException(
                $"RunPod 결과 해상도 불일치: " +
                $"예상 {expected.Width}x{expected.Height}, " +
                $"실제 {resultWidth}x{resultHeight}");
        }
    }

    return new ProjectProcessingResult(
        Array.Empty<ProcessedLayer>(),
        outputPath,
        resultWidth,
        resultHeight);
}


    private async Task TryDeleteTemporaryAsync(string objectKey)
    {
        try
        {
            await storageService.DeleteAsync(
                objectKey,
                CancellationToken.None);
        }
        catch
        {
            // 임시 파일 정리 실패가 원래 처리 결과를
            // 덮어쓰지 않도록 함.
            // 운영 환경에서는 로깅 및 정기 정리 작업 필요.
        }
    }
}
