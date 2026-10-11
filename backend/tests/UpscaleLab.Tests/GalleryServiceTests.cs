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

        var usernameMatches = await service.GetAllAsync(
            null,
            "space_artist",
            null,
            CancellationToken.None);
        Assert.Empty(usernameMatches);
    }

    [Fact]
    public async Task UpdateTag_ChangesOwnedPostOnly()
    {
        await using var dbContext = CreateDbContext();
        var user = new User
        {
            Email = "owner@example.com",
            Username = "owner",
            PasswordHash = "hash"
        };
        var post = new GalleryPost
        {
            User = user,
            Image = CreateImage(user, "car.png"),
            Title = "My Car",
            Tag = "자동차",
            IsPublic = true
        };
        dbContext.GalleryPosts.Add(post);
        await dbContext.SaveChangesAsync();
        var service = new GalleryService(dbContext, new FakeStorageService());

        var updated = await service.UpdateTagAsync(
            user.Id,
            post.Id,
            new UpdateGalleryPostTagRequest("레트로"),
            CancellationToken.None);

        Assert.Equal("레트로", updated.Tag);
        dbContext.ChangeTracker.Clear();
        var filtered = await service.GetAllAsync(null, null, "레트로", CancellationToken.None);
        Assert.Equal(post.Id, Assert.Single(filtered).Id);
        Assert.Empty(await service.GetAllAsync(null, null, "자동차", CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.UpdateTagAsync(
            Guid.NewGuid(),
            post.Id,
            new UpdateGalleryPostTagRequest("SF"),
            CancellationToken.None));
    }

    [Fact]
    public async Task PreviewUrl_PresignsPrivateStorageWithoutCountingADownload()
    {
        await using var dbContext = CreateDbContext();
        var user = new User { Email = "artist@example.com", Username = "artist", PasswordHash = "hash" };
        var post = new GalleryPost
        {
            User = user, Image = CreateImage(user, "cat.png"), Title = "Cat", IsPublic = true
        };
        dbContext.GalleryPosts.Add(post);
        await dbContext.SaveChangesAsync();
        var service = new GalleryService(dbContext, new FakeStorageService());

        var preview = await service.CreatePreviewUrlAsync(post.Id, null, CancellationToken.None);

        Assert.Equal("https://example.test/cat.png", preview.Url);
        Assert.True(preview.ExpiresAt > DateTime.UtcNow);
        Assert.Equal(0, (await dbContext.GalleryPosts.SingleAsync()).DownloadCount);

        post.IsPublic = false;
        await dbContext.SaveChangesAsync();
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.CreatePreviewUrlAsync(post.Id, null, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.CreatePreviewUrlAsync(post.Id, Guid.NewGuid(), CancellationToken.None));
        Assert.Equal("https://example.test/cat.png",
            (await service.CreatePreviewUrlAsync(post.Id, user.Id, CancellationToken.None)).Url);
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
