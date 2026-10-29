using UpscaleLab.Application.Images;
using UpscaleLab.Application.Storage;

namespace UpscaleLab.Infrastructure.Images;

internal static class ImageContentReader
{
    public static async Task<ImageContentResponse> ReadAsync(IStorageService storage, string key, string contentType, CancellationToken ct)
    {
        await using var content = new MemoryStream();
        await storage.DownloadAsync(key, content, ct);
        return new(content.ToArray(), contentType);
    }
}
