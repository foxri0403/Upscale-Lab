using ImageMagick;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Images;
using UpscaleLab.Application.Storage;

namespace UpscaleLab.Infrastructure.Images;

public sealed class WebpVariantStore(IStorageService storage, IStorageObjectLookup lookup)
{
    private const string ContentType = "image/webp";
    private static readonly SemaphoreSlim Conversion = new(1);

    public static string VariantKey(string source) => source + ".web-v1.webp";

    public async Task<string> EnsureAsync(string source, string contentType, CancellationToken ct)
    {
        if (contentType == ContentType) return source;
        var key = VariantKey(source);
        if (await lookup.ExistsAsync(key, ct)) return key;
        await Conversion.WaitAsync(ct);
        try
        {
            if (await lookup.ExistsAsync(key, ct)) return key;
            await ConvertAsync(source, contentType, key, ct);
            return key;
        }
        finally { Conversion.Release(); }
    }

    private async Task ConvertAsync(string source, string contentType, string key, CancellationToken ct)
    {
        ImageFormatPolicy.TryResolveContentType(Path.GetFileName(source), contentType, out var resolvedType);
        var format = resolvedType switch
        {
            "image/jpeg" or "image/jpg" => MagickFormat.Jpeg,
            "image/png" or "image/x-png" => MagickFormat.Png,
            "image/gif" => MagickFormat.Gif,
            "image/webp" => MagickFormat.WebP,
            "image/bmp" or "image/x-ms-bmp" => MagickFormat.Bmp,
            "image/tiff" => MagickFormat.Tiff,
            "image/heic" or "image/heif" or "image/heic-sequence" or "image/heif-sequence" => MagickFormat.Heic,
            "image/avif" => MagickFormat.Avif,
            "image/jp2" or "image/jpx" => MagickFormat.Jp2,
            "image/x-adobe-dng" => MagickFormat.Dng,
            "image/vnd.wap.wbmp" => MagickFormat.Wbmp,
            _ => throw new ValidationException("이 이미지 형식은 WebP로 변환할 수 없습니다.")
        };
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"upscale-webp-{Guid.NewGuid():N}");
        await using var input = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        await storage.DownloadAsync(source, input, ct);
        input.Position = 0;
        var settings = new MagickReadSettings { Format = format, FrameIndex = 0, FrameCount = 1 };
        using (var dimensions = new MagickImage())
        {
            await dimensions.PingAsync(input, settings, ct);
            if (dimensions.Width == 0 || dimensions.Height == 0 || (long)dimensions.Width * dimensions.Height > 64_000_000)
                throw new ValidationException("WebP로 변환할 수 있는 최대 이미지 크기를 초과했습니다.");
        }
        input.Position = 0;
        await using var output = new MemoryStream();
        using (var image = new MagickImage(input, settings))
        {
            image.AutoOrient();
            image.ColorSpace = ColorSpace.sRGB;
            image.Resize(new MagickGeometry(4096, 4096) { Greater = true });
            image.Strip();
            image.Quality = 90;
            await image.WriteAsync(output, MagickFormat.WebP, ct);
        }
        output.Position = 0;
        await storage.UploadAsync(output, key, ContentType, ct);
        // If deletion won the race with conversion, don't leave a usable copy behind.
        if (await lookup.ExistsAsync(source, ct)) return;
        await storage.DeleteAsync(key, CancellationToken.None);
        throw new NotFoundException("이미지를 찾을 수 없습니다.");
    }
}
