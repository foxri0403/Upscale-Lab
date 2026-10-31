using ImageMagick;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Images;

namespace UpscaleLab.Infrastructure.Processing;

internal static class SubjectSelection
{
    // Client RGB: addition, eraser, zero. Durable worker RGB: trusted prior alpha, addition, eraser.
    public static async Task<MemoryStream> ReadAsync(Stream source, Stream foreground, int width, int height, CancellationToken ct)
    {
        using var input = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            if (input.Length + read > ImageFormatPolicy.MaxUploadBytes)
                throw new ValidationException("50MB 이하의 PNG 선택 영역을 사용해 주세요.");
            await input.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        try
        {
            input.Position = 0;
            var info = new MagickImageInfo(input);
            if (info.Format != MagickFormat.Png || width < 1 || height < 1 || width > 32768 || height > 32768 ||
                (long)width * height > 64_000_000 || info.Width != width || info.Height != height)
                throw new ValidationException("선택 영역의 PNG 형식과 원본 캔버스 크기를 확인해 주세요.");
            input.Position = 0;
            using var guide = new MagickImage(input);
            if (!guide.IsOpaque) throw new ValidationException("선택 영역 PNG는 불투명해야 합니다.");
            using var original = new MagickImage(foreground);
            if (original.Width != width || original.Height != height || !original.HasAlpha || original.IsOpaque)
                throw new ConflictException("분리 결과의 크기가 변경되었습니다. 자동 분리를 다시 실행해 주세요.");
            guide.ColorSpace = ColorSpace.sRGB;
            using var gp = guide.GetPixels();
            using var op = original.GetPixels();
            var hints = gp.ToByteArray("RGB")!;
            var prior = op.ToByteArray("A")!;
            bool any = false;
            for (var i = 0; i < prior.Length; i++)
            {
                if (hints[i * 3 + 2] != 0) throw new ValidationException("선택 영역의 색상 채널이 올바르지 않습니다.");
                var add = hints[i * 3]; var erase = hints[i * 3 + 1];
                any |= add > 0 || erase > 0;
                hints[i * 3] = prior[i]; hints[i * 3 + 1] = add; hints[i * 3 + 2] = erase;
            }
            if (!any) throw new ValidationException("브러쉬 또는 올가미로 보완할 영역을 표시해 주세요.");
            using var packed = new MagickImage();
            packed.ReadPixels(hints, new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.RGB));
            packed.Strip();
            return new MemoryStream(packed.ToByteArray(MagickFormat.Png), false);
        }
        catch (MagickException) { throw new ValidationException("선택 영역 PNG를 읽을 수 없습니다."); }
    }
}
