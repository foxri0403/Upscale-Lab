using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Gallery;
using UpscaleLab.Application.Storage;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Gallery;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class GalleryServiceTests
{
    [Fact]
    public async Task GetAll_FiltersPublicPostsBySearchAndTag()
    {
        await using var dbContext = CreateDbContext();
        var user = new User
        {
            Email = "artist@example.com",
            Username = "space_artist",
            PasswordHash = "hash"
        };
        var spaceImage = CreateImage(user, "space.png");
        var animalImage = CreateImage(user, "cat.png");
        dbContext.GalleryPosts.AddRange(
            new GalleryPost
            {
                User = user,
                Image = spaceImage,
                Title = "Deep Space",
                Tag = "SF",
                IsPublic = true
            },
            new GalleryPost
            {
                User = user,
                Image = animalImage,
                Title = "Cute Cat",
                Tag = "동물",
                IsPublic = true
            });
        await dbContext.SaveChangesAsync();
        var service = new GalleryService(dbContext, new FakeStorageService());

        var result = await service.GetAllAsync(null, "space", "SF", CancellationToken.None);

        var post = Assert.Single(result);
        Assert.Equal("Deep Space", post.Title);
        Assert.Equal("SF", post.Tag);
        Assert.Equal("https://example.test/space.png", post.ImageUrl);
    }

    [Fact]
    public async Task Create_RejectsUnknownTag()
    {
        await using var dbContext = CreateDbContext();
        var user = new User
        {
            Email = "owner@example.com",
            Username = "owner",
            PasswordHash = "hash"
        };
        var image = CreateImage(user, "image.png");
        dbContext.Images.Add(image);
        await dbContext.SaveChangesAsync();
        var service = new GalleryService(dbContext, new FakeStorageService());

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(
            user.Id,
            new CreateGalleryPostRequest(image.Id, "Image", null, Tag: "unknown"),
            CancellationToken.None));
    }

    private static Image CreateImage(User user, string objectKey) => new()
    {
        User = user,
        OriginalUrl = $"s3://test/{objectKey}",
        OriginalObjectKey = objectKey,
        OriginalWidth = 100,
        OriginalHeight = 100,
        FileName = objectKey,
        ContentType = "image/png"
    };

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class FakeStorageService : IStorageService
    {
        public Task<StoredObject> UploadAsync(
            Stream content,
            string objectKey,
            string contentType,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DownloadAsync(
            string objectKey,
            Stream destination,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public string CreateDownloadUrl(string objectKey, TimeSpan lifetime) =>
            $"https://example.test/{objectKey}";
    }
}
