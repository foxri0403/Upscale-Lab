using Microsoft.Extensions.Caching.Memory;

namespace UpscaleLab.Infrastructure.Gallery;

public sealed class GalleryThumbnailCache : IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 64 * 1024 * 1024 });
    public bool TryGet(string key, out byte[]? bytes) => cache.TryGetValue(key, out bytes);
    public void Store(string key, byte[] bytes) => cache.Set(key, bytes,
        new MemoryCacheEntryOptions().SetSize(bytes.Length).SetSlidingExpiration(TimeSpan.FromMinutes(30)));
    public void Dispose() => cache.Dispose();
}
