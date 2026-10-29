using ImageMagick;
using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Storage;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Gallery;
using UpscaleLab.Infrastructure.Projects;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class GalleryImportServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Import_PublicPostCreatesAnIndependentPrivateProjectForTheViewer()
    {
        await using var db = CreateDb();
        var post = Post();
        db.GalleryPosts.Add(post);
        await db.SaveChangesAsync();
        var viewer = Guid.NewGuid();
        var storage = new Store(post.Image.OriginalObjectKey);
        var projects = new ProjectService(db, storage);
        var service = new GalleryImportService(db, storage, projects);

        await Assert.ThrowsAsync<NotFoundException>(() => projects.GetAsync(viewer, post.Project!.Id, Ct));
        var result = await service.ImportAsync(viewer, post.Id, Ct);

        var copy = await db.LiveLayerProjects.Include(x => x.OriginalImage).SingleAsync(x => x.Id == result.Id);
        Assert.Equal(viewer, copy.UserId);
        Assert.Equal(viewer, copy.OriginalImage.UserId);
        Assert.NotEqual(post.Project!.Id, copy.Id);
        Assert.NotEqual(post.ImageId, copy.OriginalImageId);
        Assert.NotEqual(post.Image.OriginalObjectKey, copy.OriginalImage.OriginalObjectKey);
        Assert.StartsWith($"users/{viewer:N}/projects/{copy.Id:N}/", copy.OriginalImage.OriginalObjectKey);
        Assert.Equal(post.Title, result.Title);
        Assert.False(result.IsDraft);
        Assert.Equal(10, result.OriginalWidth);
        Assert.Equal(6, result.OriginalHeight);
        Assert.Equal("image/png", result.OriginalContentType);
        Assert.Equal(DateTimeKind.Utc, copy.CreatedAt.Kind);
        Assert.Equal(7, post.DownloadCount);
        Assert.Single(db.GalleryPosts);
        Assert.Empty(db.WallpaperAssets);
        Assert.Single(await projects.GetAllAsync(viewer, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => projects.GetAsync(post.UserId, copy.Id, Ct));
        Assert.True(storage.Files.ContainsKey(post.Image.OriginalObjectKey));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Import_PrivateOrMissingPostDoesNotReadOrWriteStorage(bool missing)
    {
        await using var db = CreateDb();
        var post = Post(); post.IsPublic = false;
        db.GalleryPosts.Add(post); await db.SaveChangesAsync();
        var storage = new Store(post.Image.OriginalObjectKey);
        var service = new GalleryImportService(db, storage, new ProjectService(db, storage));

        await Assert.ThrowsAsync<NotFoundException>(() => service.ImportAsync(
            Guid.NewGuid(), missing ? Guid.NewGuid() : post.Id, Ct));

        Assert.Equal(0, storage.Reads);
        Assert.Single(storage.Files);
        Assert.Single(db.LiveLayerProjects);
        Assert.Single(db.Images);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Import_StorageFailureDoesNotCreateLibraryRecordsAndCanBeRetried(bool uploading)
    {
        await using var db = CreateDb();
        var post = Post(); db.GalleryPosts.Add(post); await db.SaveChangesAsync();
        var storage = new Store(post.Image.OriginalObjectKey) { FailRead = !uploading, FailUpload = uploading };
        var service = new GalleryImportService(db, storage, new ProjectService(db, storage));
        var viewer = Guid.NewGuid();

        await Assert.ThrowsAsync<IOException>(() => service.ImportAsync(viewer, post.Id, Ct));
        Assert.Single(db.LiveLayerProjects);
        Assert.Single(db.Images);
        Assert.Single(storage.Files);
        Assert.Equal(7, post.DownloadCount);

        storage.FailRead = storage.FailUpload = false;
        var result = await service.ImportAsync(viewer, post.Id, Ct);
        Assert.Equal(result.Id, (await db.LiveLayerProjects.SingleAsync(x => x.UserId == viewer)).Id);
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static GalleryPost Post()
    {
        var author = new User { Email = "author@example.invalid", Username = "Author", PasswordHash = "test-only" };
        var image = new UpscaleLab.Domain.Entities.Image { User = author, UserId = author.Id,
            FileName = "original.png", ContentType = "image/png", OriginalObjectKey = $"users/{author.Id:N}/original.png",
            OriginalUrl = "s3://test/original.png", OriginalWidth = 10, OriginalHeight = 6 };
        var project = new LiveLayerProject { User = author, UserId = author.Id, OriginalImage = image,
            OriginalImageId = image.Id, Title = "Author's private project" };
        return new GalleryPost { User = author, UserId = author.Id, Image = image, ImageId = image.Id,
            Project = project, ProjectId = project.Id, Title = "공개 이미지 제목", IsPublic = true, DownloadCount = 7 };
    }

    private sealed class Store : IStorageService
    {
        public Dictionary<string, byte[]> Files { get; } = new();
        public int Reads { get; private set; }
        public bool FailRead { get; set; }
        public bool FailUpload { get; set; }
        public Store(string originalKey)
        {
            using var image = new MagickImage(MagickColors.Blue, 10, 6);
            Files[originalKey] = image.ToByteArray(MagickFormat.Png);
        }
        public async Task DownloadAsync(string key, Stream destination, CancellationToken ct)
        {
            Reads++;
            if (FailRead) throw new IOException("Read failed");
            await destination.WriteAsync(Files[key], ct);
        }
        public async Task<StoredObject> UploadAsync(Stream content, string key, string type, CancellationToken ct)
        {
            if (FailUpload) throw new IOException("Upload failed");
            using var bytes = new MemoryStream(); await content.CopyToAsync(bytes, ct);
            Files[key] = bytes.ToArray(); return new(key, $"s3://test/{key}");
        }
        public Task DeleteAsync(string key, CancellationToken ct) { Files.Remove(key); return Task.CompletedTask; }
        public string CreateDownloadUrl(string key, TimeSpan lifetime) => $"https://example.invalid/{key}";
        public string CreateUploadUrl(string key, string type, TimeSpan lifetime) => throw new NotSupportedException();
    }
}
