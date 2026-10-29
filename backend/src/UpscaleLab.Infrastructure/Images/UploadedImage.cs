using ImageMagick;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Images;

namespace UpscaleLab.Infrastructure.Images;

internal sealed record UploadedImage(byte[] Bytes, string FileName, string ContentType, int Width, int Height)
{
    public static async Task<UploadedImage> ReadAsync(Stream source, string fileName, string contentType, CancellationToken ct)
    {
        if (!ImageFormatPolicy.TryResolveContentType(fileName, contentType, out _))
            throw new ValidationException(ImageFormatPolicy.SupportedFormatsMessage);
        await using var input = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            if (input.Length + read > ImageFormatPolicy.MaxUploadBytes)
                throw new ValidationException("50MB 이하의 이미지를 선택해 주세요.");
            await input.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        if (input.Length == 0) throw new ValidationException("빈 파일은 업로드할 수 없습니다.");
        try
        {
            input.Position = 0;
            var info = new MagickImageInfo(input);
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                "Jpeg", "Png", "Png8", "Png24", "Png32", "Png48", "Png64", "Gif", "WebP",
                "Bmp", "Bmp2", "Bmp3", "Tiff", "Heic", "Heif", "Avif", "Dng", "Jp2", "Jpx", "Wbmp"
            };
            if (!allowed.Contains(info.Format.ToString())) throw new ValidationException(ImageFormatPolicy.SupportedFormatsMessage);
            if (info.Width < 1 || info.Height < 1 || info.Width > 32768 || info.Height > 32768 || (long)info.Width * info.Height > 64_000_000)
                throw new ValidationException("이미지는 최대 64메가픽셀, 가로·세로 32,768px까지 사용할 수 있습니다.");
            input.Position = 0;
            using var image = new MagickImage(input, new MagickReadSettings { FrameIndex = 0, FrameCount = 1 });
            image.AutoOrient();
            image.ColorSpace = ColorSpace.sRGB;
            image.Strip();
            // The editor always receives a browser-readable still image, including HEIF and animated inputs.
            var jpeg = info.Format == MagickFormat.Jpeg;
            var bytes = image.ToByteArray(jpeg ? MagickFormat.Jpeg : MagickFormat.Png);
            return new(bytes, Path.ChangeExtension(Path.GetFileName(fileName), jpeg ? ".jpg" : ".png"),
                jpeg ? "image/jpeg" : "image/png", (int)image.Width, (int)image.Height);
        }
        catch (MagickException)
        {
            throw new ValidationException("이미지를 읽을 수 없습니다. 파일 형식과 파일 손상 여부를 확인해 주세요.");
        }
    }
}
