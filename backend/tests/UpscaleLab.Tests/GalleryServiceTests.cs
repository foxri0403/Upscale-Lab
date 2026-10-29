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
    public async Task CompatibilityPosts_RetainSecondaryTagsAfterServerUpgradeAndEdit()
    {
        await using var db = CreateDbContext();
        var user = new User { Email = "legacy@example.com", Username = "legacy", PasswordHash = "hash" };
        var post = new GalleryPost { User = user, Image = CreateImage(user, "legacy.png"),
            Title = "Legacy Cat", Tag = "동물", Tags = ["동물"],
            Description = "Shared\n[UpscaleLab.Tags:v1][\"동물\",\"레트로\"]" };
        db.GalleryPosts.Add(post);
        await db.SaveChangesAsync();
        var service = new GalleryService(db, new FakeStorageService());
        var found = await service.GetAllAsync(null, null, null, CancellationToken.None, ["레트로", "SF"]);
        Assert.Equal(["동물", "레트로"], Assert.Single(found).Tags);
        Assert.Equal(["동물", "레트로"], (await service.GetAsync(post.Id, null, CancellationToken.None)).Post.Tags);
        var edited = await service.UpdateTagAsync(user.Id, post.Id,
            new UpdateGalleryPostTagRequest(Tags: ["동물"]), CancellationToken.None);
        Assert.Equal(["동물"], edited.Tags);
        Assert.Equal("Shared", edited.Description);
        Assert.Empty(await service.GetAllAsync(null, null, "레트로", CancellationToken.None));
    }

    [Fact]
    public async Task MultipleTags_MatchAnySelectedTagAndPreserveLegacyAndPrivacy()
    {
        await using var db = CreateDbContext();
        var user = new User { Email = "multi@example.com", Username = "multi", PasswordHash = "hash" };
        var tagged = new GalleryPost { User = user, Image = CreateImage(user, "multi.png"),
            Title = "Multi", Tag = "동물", Tags = ["동물", "레트로"] };
        var legacy = new GalleryPost { User = user, Image = CreateImage(user, "legacy.png"),
            Title = "Legacy", Tag = "SF" };
        var hidden = new GalleryPost { User = user, Image = CreateImage(user, "private.png"),
            Title = "Private", Tag = "동물", Tags = ["동물"], IsPublic = false };
        db.GalleryPosts.AddRange(tagged, legacy, hidden);
        await db.SaveChangesAsync();
        var service = new GalleryService(db, new FakeStorageService());
        var matches = await service.GetAllAsync(null, null, null, CancellationToken.None, ["레트로", "SF"]);
        Assert.Equal(2, matches.Count);
        Assert.DoesNotContain(matches, x => x.Id == hidden.Id);
        Assert.Equal(["SF"], matches.Single(x => x.Id == legacy.Id).Tags);
        Assert.Single(await service.GetAllAsync(null, null, "레트로", CancellationToken.None));

        var updated = await service.UpdateTagAsync(user.Id, tagged.Id,
            new UpdateGalleryPostTagRequest(Tags: ["풍경", "게임", "풍경"]), CancellationToken.None);
        Assert.Equal(["풍경", "게임"], updated.Tags);
        Assert.Equal("풍경", updated.Tag);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.UpdateTagAsync(Guid.NewGuid(), tagged.Id,
            new UpdateGalleryPostTagRequest(Tags: ["남자", "SF"]), CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateTagAsync(user.Id, tagged.Id,
            new UpdateGalleryPostTagRequest(Tags: ["풍경", "unknown"]), CancellationToken.None));
    }

    [Fact]
    public async Task Create_PersistsAllSelectedTags()
    {
        await using var db = CreateDbContext();
        var user = new User { Email = "create@example.com", Username = "creator", PasswordHash = "hash" };
        var image = CreateImage(user, "new.png");
        db.Images.Add(image);
        await db.SaveChangesAsync();
        var response = await new GalleryService(db, new FakeStorageService()).CreateAsync(user.Id,
            new CreateGalleryPostRequest(image.Id, "New", null, Tags: ["동물", "레트로"]), CancellationToken.None);
        Assert.Equal(["동물", "레트로"], response.Tags);
        db.ChangeTracker.Clear();
        Assert.Equal(["동물", "레트로"], (await db.GalleryPosts.SingleAsync()).Tags);
    }

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

    [Fact]
    public async Task CompletedDownloads_ArePersistedAndReturnedByListAndDetail()
    {
        await using var db = CreateDbContext();
        var user = new User { Email = "downloads@example.com", Username = "artist", PasswordHash = "hash" };
        var post = new GalleryPost { User = user, Image = CreateImage(user, "photo.png"), Title = "Photo" };
        db.GalleryPosts.Add(post);
        await db.SaveChangesAsync();
        var service = new GalleryService(db, new FakeStorageService());

        await service.CreateDownloadUrlAsync(post.Id, null, CancellationToken.None, countDownload: false);
        await service.CreatePreviewUrlAsync(post.Id, null, CancellationToken.None);
        Assert.Equal(0, (await db.GalleryPosts.SingleAsync()).DownloadCount);
        Assert.Equal(1, (await service.RecordDownloadAsync(post.Id, null, CancellationToken.None)).DownloadCount);
        Assert.Equal(2, (await service.RecordDownloadAsync(post.Id, user.Id, CancellationToken.None)).DownloadCount);

        db.ChangeTracker.Clear();
        var saved = await db.GalleryPosts.SingleAsync();
        Assert.Equal(2, saved.DownloadCount);
        Assert.Equal(DateTimeKind.Utc, saved.UpdatedAt.Kind);
        Assert.Equal(2, Assert.Single(await service.GetAllAsync(null, null, null, CancellationToken.None)).DownloadCount);
        Assert.Equal(2, (await service.GetAsync(post.Id, null, CancellationToken.None)).Post.DownloadCount);
        await service.CreateDownloadUrlAsync(post.Id, null, CancellationToken.None);
        Assert.Equal(3, (await service.GetAsync(post.Id, null, CancellationToken.None)).Post.DownloadCount);
    }

    [Fact]
    public async Task DownloadCompletion_CannotCountAnInaccessibleOrMissingPost()
    {
        await using var db = CreateDbContext();
        var user = new User { Email = "private@example.com", Username = "owner", PasswordHash = "hash" };
        var post = new GalleryPost
        {
            User = user, Image = CreateImage(user, "private.png"), Title = "Private", IsPublic = false
        };
        db.GalleryPosts.Add(post);
        await db.SaveChangesAsync();
        var service = new GalleryService(db, new FakeStorageService());
        await Assert.ThrowsAsync<NotFoundException>(() => service.RecordDownloadAsync(post.Id, null, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => service.RecordDownloadAsync(post.Id, Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => service.RecordDownloadAsync(Guid.NewGuid(), user.Id, CancellationToken.None));
        Assert.Equal(0, (await db.GalleryPosts.SingleAsync()).DownloadCount);
        Assert.Equal(1, (await service.RecordDownloadAsync(post.Id, user.Id, CancellationToken.None)).DownloadCount);
    }

    [Fact]
    public async Task Update_ChangesOwnedNameAndTagsTogether()
    {
        await using var db = CreateDbContext();
        var user = new User { Email = "editor@example.com", Username = "editor", PasswordHash = "hash" };
        var image = CreateImage(user, "mountain.png");
        var project = new LiveLayerProject { User = user, OriginalImage = image, Title = "Before" };
        var post = new GalleryPost
        {
            User = user, Image = image, Project = project, Title = "Before", Tag = "SF", Tags = ["SF"],
        };
        db.GalleryPosts.Add(post);
        await db.SaveChangesAsync();
        var createdAt = post.CreatedAt;
        var service = new GalleryService(db, new FakeStorageService());
        var updated = await service.UpdateAsync(user.Id, post.Id,
            new UpdateGalleryPostRequest("  푸른 산맥  ", ["풍경", "게임", "풍경"]), CancellationToken.None);

        Assert.Equal("푸른 산맥", updated.Title);
        Assert.Equal(new[] { "풍경", "게임" }, updated.Tags);
        db.ChangeTracker.Clear();
        var saved = await db.GalleryPosts.Include(x => x.Project).SingleAsync();
        Assert.Equal("푸른 산맥", saved.Title);
        Assert.Equal("푸른 산맥", saved.Project!.Title);
        Assert.Equal(createdAt, saved.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, saved.UpdatedAt.Kind);
        Assert.Equal(post.Id, Assert.Single(await service.GetAllAsync(null, "푸른", null, CancellationToken.None)).Id);
    }

    [Fact]
    public async Task Update_RejectsOtherOwnersAndInvalidInputWithoutChangingMetadata()
    {
        await using var db = CreateDbContext();
        var user = new User { Email = "protected@example.com", Username = "protected", PasswordHash = "hash" };
        var post = new GalleryPost { User = user, Image = CreateImage(user, "cat.png"), Title = "Before", Tags = ["동물"] };
        db.GalleryPosts.Add(post);
        await db.SaveChangesAsync();
        var service = new GalleryService(db, new FakeStorageService());
        await Assert.ThrowsAsync<ForbiddenException>(() => service.UpdateAsync(Guid.NewGuid(), post.Id,
            new UpdateGalleryPostRequest("Changed", ["풍경"]), CancellationToken.None));
        foreach (var title in new[] { "  ", new string('x', 161) })
            await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(user.Id, post.Id,
                new UpdateGalleryPostRequest(title, ["풍경"]), CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(user.Id, post.Id,
            new UpdateGalleryPostRequest("Changed", ["unknown"]), CancellationToken.None));
        db.ChangeTracker.Clear();
        var saved = await db.GalleryPosts.SingleAsync();
        Assert.Equal("Before", saved.Title);
        Assert.Equal(new[] { "동물" }, saved.Tags);
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

    public string CreateUploadUrl(
        string objectKey,
        string contentType,
        TimeSpan lifetime)
    {
        throw new NotSupportedException(
            "Upload URL is not required in this test.");
    }
        public string CreateDownloadUrl(string objectKey, TimeSpan lifetime) =>
            $"https://example.test/{objectKey}";
    }
}
