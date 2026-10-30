using ImageMagick;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Images;

namespace UpscaleLab.Infrastructure.Processing;

internal static class SubjectMask
{
    // Opaque grayscale: white keeps the subject, black returns pixels to the background.
    public static async Task<MemoryStream> ReadAsync(Stream source, int width, int height, CancellationToken ct)
    {
        using var input = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            if (input.Length + read > ImageFormatPolicy.MaxUploadBytes)
                throw new ValidationException("50MB 이하의 PNG 마스크를 사용해 주세요.");
            await input.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        try
        {
            input.Position = 0;
            var info = new MagickImageInfo(input);
            if (info.Format != MagickFormat.Png || width < 1 || height < 1 ||
                width > 32768 || height > 32768 || (long)width * height > 64_000_000 ||
                info.Width != width || info.Height != height)
                throw new ValidationException("마스크의 PNG 형식과 원본 캔버스 크기를 확인해 주세요.");
            input.Position = 0;
            using var image = new MagickImage(input);
            if (!image.IsOpaque) throw new ValidationException("마스크는 투명도 없이 흑백으로 지정해 주세요.");
            image.ColorSpace = ColorSpace.Gray;
            image.Strip();
            using var pixels = image.GetPixels();
            var values = pixels.ToByteArray("R")!;
            var selected = values.LongCount(x => x > 127);
            var fraction = selected / (double)values.Length;
            if (fraction <= .001 || fraction >= .98)
                throw new ValidationException("피사체와 배경 영역을 모두 남겨 주세요. 피사체는 전체의 0.1% 초과, 98% 미만이어야 합니다.");
            return new MemoryStream(image.ToByteArray(MagickFormat.Png), false);
        }
        catch (MagickException)
        {
            throw new ValidationException("PNG 마스크를 읽을 수 없습니다.");
        }
    }
}
