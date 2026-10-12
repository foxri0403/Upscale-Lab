using Amazon.S3;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Storage;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Gallery;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class GalleryThumbnailServiceTests
{
    [Fact]
    public async Task Thumbnail_ResizesPersistsCachesAndChecksAccessBeforeUsingCache()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var owner = new User { Email = "preview@example.com", Username = "preview", PasswordHash = "hash" };
        var post = new GalleryPost { User = owner, Title = "Large", Image = new Image {
            User = owner, OriginalObjectKey = "large.png", OriginalUrl = "s3://test/large.png",
            FileName = "large.png", ContentType = "image/png", OriginalWidth = 2400, OriginalHeight = 1600
        }};
        db.GalleryPosts.Add(post);
        await db.SaveChangesAsync();
        using var original = new MagickImage(MagickColors.Red, 2400, 1600);
        var storage = new MemoryStorage(original.ToByteArray(MagickFormat.Png));
        using var cache = new GalleryThumbnailCache();
        var service = new GalleryThumbnailService(db, storage, cache);
        var bytes = await service.GetAsync(post.Id, null, CancellationToken.None);
        Assert.NotNull(bytes);
        using var preview = new MagickImage(bytes!);
        Assert.Equal(480u, preview.Width);
        Assert.Equal(320u, preview.Height);
        Assert.True(storage.Objects.ContainsKey("large.png.gallery-480.jpg"));
        var requests = storage.Reads;
        Assert.Equal(bytes, await service.GetAsync(post.Id, null, CancellationToken.None));
        Assert.Equal(requests, storage.Reads);
        Assert.Equal(0, (await db.GalleryPosts.SingleAsync()).DownloadCount);
        post.IsPublic = false;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(post.Id, null, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(post.Id, Guid.NewGuid(), CancellationToken.None));
        Assert.NotNull(await service.GetAsync(post.Id, owner.Id, CancellationToken.None));
        // A fresh process can load the persisted derivative without reading the original.
        using var coldCache = new GalleryThumbnailCache();
        Assert.NotNull(await new GalleryThumbnailService(db, storage, coldCache)
            .GetAsync(post.Id, owner.Id, CancellationToken.None));
        Assert.Equal(requests + 1, storage.Reads);
    }

    private sealed class MemoryStorage(byte[] original) : IStorageService
    {
        public Dictionary<string, byte[]> Objects { get; } = new() { ["large.png"] = original };
        public int Reads { get; private set; }
        public async Task DownloadAsync(string key, Stream destination, CancellationToken token)
        {
            Reads++;
            if (!Objects.TryGetValue(key, out var bytes))
                throw new AmazonS3Exception("Missing") { StatusCode = System.Net.HttpStatusCode.NotFound };
            await destination.WriteAsync(bytes, token);
        }
        public async Task<StoredObject> UploadAsync(Stream content, string key, string type, CancellationToken token)
        {
            using var output = new MemoryStream();
            await content.CopyToAsync(output, token);
            Objects[key] = output.ToArray();
            return new StoredObject(key, $"s3://test/{key}");
        }
        public Task DeleteAsync(string key, CancellationToken token) => Task.CompletedTask;
        public string CreateDownloadUrl(string key, TimeSpan lifetime) => $"https://test/{key}";
    }
}
