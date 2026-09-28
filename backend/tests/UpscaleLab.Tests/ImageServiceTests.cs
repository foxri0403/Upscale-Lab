using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Images;
using UpscaleLab.Application.Storage;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Images;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class ImageServiceTests
{
    [Fact]
    public async Task Upload_PersistsMetadataWithoutBinary()
    {
        await using var dbContext = CreateDbContext();
        var storage = new FakeStorageService();
        var service = new ImageService(dbContext, storage);
        var userId = Guid.NewGuid();
        await using var stream = new MemoryStream([1, 2, 3, 4]);

        var result = await service.UploadAsync(
            userId,
            new ImageUploadCommand(stream, "wallpaper.png", "image/png", 1920, 1080),
            CancellationToken.None);

        var stored = await dbContext.Images.SingleAsync();
        Assert.Equal(userId, stored.UserId);
        Assert.Equal(1920, stored.OriginalWidth);
        Assert.Equal("wallpaper.png", stored.FileName);
        Assert.StartsWith("s3://test-bucket/", stored.OriginalUrl);
        Assert.Equal(4, storage.LastUploadLength);
        Assert.DoesNotContain(dbContext.Model.GetEntityTypes(), x => x.GetProperties().Any(p => p.Name == "Binary"));
        Assert.Equal(stored.Id, result.Id);
    }

    [Fact]
    public async Task Get_DoesNotExposeAnotherUsersImage()
    {
        await using var dbContext = CreateDbContext();
        var service = new ImageService(dbContext, new FakeStorageService());
        var ownerId = Guid.NewGuid();
        await using var stream = new MemoryStream([1]);
        var image = await service.UploadAsync(
            ownerId,
            new ImageUploadCommand(stream, "image.jpg", "image/jpeg", 10, 10),
            CancellationToken.None);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(
            Guid.NewGuid(),
            image.Id,
            CancellationToken.None));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class FakeStorageService : IStorageService
    {
        public long LastUploadLength { get; private set; }

        public async Task<StoredObject> UploadAsync(
            Stream content,
            string objectKey,
            string contentType,
            CancellationToken cancellationToken)
        {
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            LastUploadLength = memory.Length;
            return new StoredObject(objectKey, $"s3://test-bucket/{objectKey}");
        }

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) => Task.CompletedTask;

        public string CreateDownloadUrl(string objectKey, TimeSpan lifetime) =>
            $"https://example.test/{objectKey}";
    }
}
