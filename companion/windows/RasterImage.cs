using ImageMagick;

namespace UpscaleLab.Companion;

internal static class RasterImage
{
    public static MagickImage Read(WallpaperFile file)
    {
        var format = file.ContentType.ToLowerInvariant() switch {
            "image/jpeg" => MagickFormat.Jpeg, "image/png" => MagickFormat.Png, "image/webp" => MagickFormat.WebP,
            "image/gif" => MagickFormat.Gif, "image/bmp" => MagickFormat.Bmp, "image/tiff" => MagickFormat.Tiff,
            "image/heic" or "image/heif" => MagickFormat.Heic, "image/avif" => MagickFormat.Avif,
            _ => throw new InvalidDataException("지원하지 않는 이미지 형식입니다.")
        };
        var settings = new MagickReadSettings { Format = format, FrameIndex = 0, FrameCount = 1 };
        using var probe = new MagickImage(); probe.Ping(file.Source, settings);
        if (probe.Width == 0 || probe.Height == 0 || (long)probe.Width * probe.Height > 64_000_000)
            throw new InvalidDataException("이미지 해상도가 너무 큽니다.");
        return new MagickImage(file.Source, settings);
    }
}
