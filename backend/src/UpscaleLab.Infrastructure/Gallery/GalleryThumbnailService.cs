using Microsoft.EntityFrameworkCore;
using ImageMagick;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Gallery;
using UpscaleLab.Application.Storage;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Infrastructure.Gallery;

// Persist derivatives in S3; a bounded gate avoids decoding many large originals at once.
public sealed class GalleryThumbnailService(ApplicationDbContext db, IStorageService storage,
    GalleryThumbnailCache cache) : IGalleryThumbnailService
{
    private static readonly SemaphoreSlim Gate = new(2);

    public async Task<byte[]?> GetAsync(Guid postId, Guid? userId, CancellationToken cancellationToken)
    {
        var post = await db.GalleryPosts.AsNoTracking()
            .Where(x => x.Id == postId && (x.IsPublic || x.UserId == userId))
            .Select(x => new { x.Image.OriginalObjectKey, x.Image.ContentType })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("갤러리 게시글을 찾을 수 없습니다.");
        // Choose a raster decoder explicitly; mislabeled files must not invoke other coders/delegates.
        MagickFormat? format = post.ContentType switch {
            "image/jpeg" => MagickFormat.Jpeg, "image/png" => MagickFormat.Png,
            "image/webp" => MagickFormat.WebP, "image/gif" => MagickFormat.Gif,
            "image/bmp" => MagickFormat.Bmp, "image/tiff" => MagickFormat.Tiff,
            "image/heic" or "image/heif" or "image/heic-sequence" or "image/heif-sequence" => MagickFormat.Heic,
            "image/avif" => MagickFormat.Avif,
            _ => null
        };
        if (format is null) return null;
        var key = post.OriginalObjectKey + ".gallery-480.jpg";
        if (cache.TryGet(key, out var cached)) return cached;
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGet(key, out cached)) return cached;
            using var output = new MemoryStream();
            try
            {
                await storage.DownloadAsync(key, output, cancellationToken);
                var existing = output.ToArray();
                cache.Store(key, existing);
                return existing;
            }
            catch (Amazon.S3.AmazonS3Exception error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                output.SetLength(0);
            }
            using var source = new MemoryStream();
            await storage.DownloadAsync(post.OriginalObjectKey, source, cancellationToken);
            source.Position = 0;
            var settings = new MagickReadSettings { Format = format.Value, FrameIndex = 0, FrameCount = 1 };
            using (var probe = new MagickImage())
            {
                await probe.PingAsync(source, settings, cancellationToken);
                if ((long)probe.Width * probe.Height > 64_000_000) return null;
            }
            source.Position = 0;
            using var image = new MagickImage(source, settings);
            image.AutoOrient();
            image.Resize(new MagickGeometry(480, 480) { Greater = true });
            image.Strip();
            image.Quality = 78;
            await image.WriteAsync(output, MagickFormat.Jpeg, cancellationToken);
            output.Position = 0;
            await storage.UploadAsync(output, key, "image/jpeg", cancellationToken);
            var bytes = output.ToArray();
            cache.Store(key, bytes);
            return bytes;
        }
        catch (MagickException) { return null; }
        finally { Gate.Release(); }
    }
}
