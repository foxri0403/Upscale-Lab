namespace UpscaleLab.Application.Images;

public static class ImageFormatPolicy
{
    private static readonly IReadOnlyDictionary<string, string> ContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = "image/jpeg",
            ["image/jpg"] = "image/jpeg",
            ["image/pjpeg"] = "image/jpeg",
            ["image/png"] = "image/png",
            ["image/x-png"] = "image/png",
            ["image/webp"] = "image/webp",
            ["image/gif"] = "image/gif",
            ["image/bmp"] = "image/bmp",
            ["image/x-bmp"] = "image/bmp",
            ["image/x-ms-bmp"] = "image/bmp",
            ["image/tiff"] = "image/tiff",
            ["image/heic"] = "image/heic",
            ["image/heic-sequence"] = "image/heic-sequence",
            ["image/heif"] = "image/heif",
            ["image/heif-sequence"] = "image/heif-sequence",
            ["image/avif"] = "image/avif",
            ["image/x-adobe-dng"] = "image/x-adobe-dng",
            ["image/jp2"] = "image/jp2",
            ["image/jpx"] = "image/jpx",
            ["image/vnd.wap.wbmp"] = "image/vnd.wap.wbmp"
        };

    private static readonly IReadOnlyDictionary<string, string> Extensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".jpe"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp",
            [".gif"] = "image/gif",
            [".bmp"] = "image/bmp",
            [".dib"] = "image/bmp",
            [".tif"] = "image/tiff",
            [".tiff"] = "image/tiff",
            [".heic"] = "image/heic",
            [".heics"] = "image/heic-sequence",
            [".heif"] = "image/heif",
            [".heifs"] = "image/heif-sequence",
            [".hif"] = "image/heif",
            [".avif"] = "image/avif",
            [".dng"] = "image/x-adobe-dng",
            [".jp2"] = "image/jp2",
            [".jpx"] = "image/jpx",
            [".wbmp"] = "image/vnd.wap.wbmp"
        };

    public const string SupportedFormatsMessage =
        "JPEG, PNG, WebP, GIF, BMP, TIFF, HEIC/HEIF, AVIF, DNG, JPEG 2000, WBMP 이미지만 업로드할 수 있습니다.";

    public static bool TryResolveContentType(
        string fileName,
        string? declaredContentType,
        out string contentType)
    {
        var mediaType = declaredContentType?.Split(';', 2)[0].Trim();
        if (!string.IsNullOrWhiteSpace(mediaType) && ContentTypes.TryGetValue(mediaType, out contentType!))
        {
            return true;
        }

        var extension = Path.GetExtension(fileName);
        if ((string.IsNullOrWhiteSpace(mediaType) ||
             mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)) &&
            Extensions.TryGetValue(extension, out contentType!))
        {
            return true;
        }

        contentType = string.Empty;
        return false;
    }
}
