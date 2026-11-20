using ImageMagick;
using UpscaleLab.Application.Processing;
using UpscaleLab.Domain.Enums;

namespace UpscaleLab.Infrastructure.Processing;

internal static class CharacterBackgroundPipeline
{
    private const int ModelCanvas = 1280;
    private const int CharacterStart = 40;

    public static async Task<ProjectProcessingResult> ProcessAsync(IProjectImageProcessor processor,
        ProjectProcessingRequest request, CancellationToken ct)
    {
        var split = await processor.ProcessAsync(request with
        {
            Operation = ProcessingOperation.Layers,
            OutputDirectory = Path.Combine(request.OutputDirectory, "background-separation"),
            ReportProgress = progress(15, CharacterStart - 1)
        }, ct);
        if (split.Width is < 1 or > 32768 || split.Height is < 1 or > 32768 ||
            (long)split.Width * split.Height > 64_000_000)
            throw new InvalidDataException("배경 분리 결과의 캔버스 크기가 올바르지 않습니다.");
        var background = split.Layers.SingleOrDefault(layer => layer.LayerType == LayerType.Background)
            ?? throw new InvalidDataException("유지할 배경 레이어가 없습니다.");
        var subject = split.Layers.SingleOrDefault(layer => layer.LayerType != LayerType.Background)
            ?? throw new InvalidDataException("Live 2D에 사용할 피사체 레이어가 없습니다.");

        ct.ThrowIfCancellationRequested();
        var inputPath = Path.Combine(request.OutputDirectory, "live-subject.png");
        var subjectArea = await PrepareSubjectAsync(subject.FilePath, inputPath, split.Width, split.Height, ct);
        if (request.ReportProgress is not null) await request.ReportProgress(CharacterStart, ct);
        var character = await processor.ProcessAsync(request with
        {
            InputPath = inputPath,
            OutputDirectory = Path.Combine(request.OutputDirectory, "character-separation"),
            ReportProgress = progress(CharacterStart, 75)
        }, ct);
        var parts = character.Layers.Where(layer => layer.LayerType != LayerType.Background)
            .OrderBy(layer => layer.LayerOrder).ToArray();
        if (parts.Length is < 1 or >= 64)
            throw new InvalidDataException("피사체 파츠 개수가 올바르지 않습니다.");
        if (character.Width <= 0 || character.Width != character.Height || character.Width > 8000)
            throw new InvalidDataException("피사체 파츠의 정사각형 캔버스가 올바르지 않습니다.");

        var content = subjectArea.OnCanvas(character.Width);
        var scaleX = (double)split.Width / content.Width;
        var scaleY = (double)split.Height / content.Height;
        var layers = new List<ProcessedLayer> { background with { LayerOrder = 0 } };
        foreach (var part in parts)
        {
            ct.ThrowIfCancellationRequested();
            var info = new MagickImageInfo(part.FilePath);
            if (info.Format != MagickFormat.Png || info.Width != character.Width || info.Height != character.Height)
                throw new InvalidDataException("피사체 파츠의 PNG 규격이 일치하지 않습니다.");
            using var image = new MagickImage(part.FilePath);
            image.Crop(new MagickGeometry(content.Left, content.Top, (uint)content.Width, (uint)content.Height));
            image.ResetPage();
            image.Resize(new MagickGeometry((uint)split.Width, (uint)split.Height) { IgnoreAspectRatio = true });
            var path = Path.Combine(request.OutputDirectory, $"subject-part-{layers.Count:D3}.png");
            await image.WriteAsync(path, MagickFormat.Png, ct);
            layers.Add(part with
            {
                FilePath = path, LayerOrder = layers.Count,
                PositionX = part.PositionX * scaleX, PositionY = part.PositionY * scaleY,
                MovementX = part.MovementX * scaleX, MovementY = part.MovementY * scaleY
            });
        }
        return new ProjectProcessingResult(layers, null, split.Width, split.Height);

        Func<int, CancellationToken, Task>? progress(int start, int end) => request.ReportProgress is { } report
            ? (value, token) => report(start + (Math.Clamp(value, 15, 75) - 15) * (end - start) / 60, token)
            : null;
    }

    // Feed a square to the model so neither cropping nor changing aspect ratio
    // inside the provider can shift the subject relative to the saved background.
    private static async Task<CanvasArea> PrepareSubjectAsync(string subjectPath, string inputPath,
        int sourceWidth, int sourceHeight, CancellationToken ct)
    {
        using var image = new MagickImage(subjectPath);
        if (image.Width != sourceWidth || image.Height != sourceHeight)
            throw new InvalidDataException("피사체와 배경 분리 캔버스 크기가 일치하지 않습니다.");
        var scale = ModelCanvas / (double)Math.Max(image.Width, image.Height);
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));
        var left = (ModelCanvas - width) / 2;
        var top = (ModelCanvas - height) / 2;
        image.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
        using var input = new MagickImage(MagickColors.White, ModelCanvas, ModelCanvas);
        input.Composite(image, left, top, CompositeOperator.Over);
        await input.WriteAsync(inputPath, MagickFormat.Png, ct);
        return new CanvasArea(left, top, width, height);
    }

    private readonly record struct CanvasArea(int Left, int Top, int Width, int Height)
    {
        public CanvasArea OnCanvas(int size)
        {
            int Scale(int value) => (int)Math.Round(value * (double)size / ModelCanvas);
            var left = Math.Min(size - 1, Scale(Left));
            var top = Math.Min(size - 1, Scale(Top));
            return new CanvasArea(left, top, Math.Max(1, Scale(Left + Width) - left),
                Math.Max(1, Scale(Top + Height) - top));
        }
    }
}
