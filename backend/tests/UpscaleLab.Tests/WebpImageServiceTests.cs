using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Text.Json;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Images;
using UpscaleLab.Application.Projects;
using UpscaleLab.Application.Storage;
using UpscaleLab.Application.Wallpapers;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Images;
using UpscaleLab.Infrastructure.Projects;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class WebpImageServiceTests
{
    [Fact]
    public async Task WebpFilesArePersistedWithAlphaAndReusedAcrossRequests()
    {
        var storage = new MemoryStorage();
        using var original = new MagickImage(MagickColors.Transparent, 64, 40);
        var png = original.ToByteArray(MagickFormat.Png);
        storage.Seed("original.png", png);
        var store = new WebpVariantStore(storage, storage);

        var keys = await Task.WhenAll(
            store.EnsureAsync("original.png", "image/png", default),
            store.EnsureAsync("original.png", "image/png", default));
        Assert.Equal(keys[0], keys[1]);
        Assert.EndsWith(".webp", keys[0]);
        Assert.Equal(1, storage.Uploads);
        var encoded = storage.Objects[keys[0]];
        Assert.Equal("image/webp", encoded.Type);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(encoded.Bytes, 0, 4));
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(encoded.Bytes, 8, 4));
        using var decoded = new MagickImage(encoded.Bytes);
        Assert.Equal(64u, decoded.Width);
        Assert.Equal(40u, decoded.Height);
        Assert.True(decoded.HasAlpha);
        Assert.Equal(png, storage.Objects["original.png"].Bytes);

        var restarted = new WebpVariantStore(storage, storage);
        Assert.Equal(keys[0], await restarted.EnsureAsync("original.png", "image/png", default));
        Assert.Equal("existing.webp", await restarted.EnsureAsync("existing.webp", "image/webp", default));
        Assert.Equal(1, storage.Uploads);
    }

    [Fact]
    public async Task BackfillUsesLatestResultAndOwnedLayersWithAuthorization()
    {
        await using var db = NewDatabase();
        var owner = User("owner");
        var viewer = User("viewer");
        var original = new Image { User = owner, OriginalObjectKey = "original.png", ContentType = "image/png" };
        var project = new LiveLayerProject { User = owner, OriginalImage = original, Title = "Project" };
        var job = new ProcessingJob { Project = project, ResultObjectKey = "upscale.png", Status = ProcessingJobStatus.Completed };
        var wallpaper = new WallpaperAsset { User = owner, Title = "Layers", IsPublic = true, Mode = WallpaperMode.Live2D };
        wallpaper.ManifestJson = JsonSerializer.Serialize(new WallpaperManifest(1, wallpaper.Id, wallpaper.Mode, 40, 30,
            [new("background.png", "image/png", 0, "Background"), new("subject.png", "image/png", 1, "Subject")]));
        db.AddRange(owner, viewer, project, job, wallpaper);
        await db.SaveChangesAsync();
        using var image = new MagickImage(MagickColors.Red, 40, 30);
        var bytes = image.ToByteArray(MagickFormat.Png);
        var storage = new MemoryStorage();
        foreach (var key in new[] { "original.png", "upscale.png", "background.png", "subject.png" }) storage.Seed(key, bytes);
        var service = new WebpImageService(db, new WebpVariantStore(storage, storage), storage,
            NullLogger<WebpImageService>.Instance);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(WebpImageKind.Project, project.Id, viewer.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(WebpImageKind.Wallpaper, wallpaper.Id, viewer.Id, default));
        Assert.Equal(0, storage.Uploads);
        Assert.Equal((2, 0), await service.BackfillAsync(default));
        Assert.Equal(3, storage.Uploads);

        var processed = await service.GetAsync(WebpImageKind.Project, project.Id, owner.Id, default);
        Assert.Contains("upscale.png.web-v1.webp", processed.Source);
        var owned = await service.GetAsync(WebpImageKind.Wallpaper, wallpaper.Id, owner.Id, default);
        Assert.Contains("background.png.web-v1.webp", owned.Background);
        Assert.Contains("subject.png.web-v1.webp", Assert.Single(owned.Foregrounds!));
        Assert.False(storage.Objects.ContainsKey("original.png.web-v1.webp"));
        Assert.Equal(3, storage.Uploads);
    }

    [Fact]
    public async Task SavingADraftSchedulesItsStoredWebp()
    {
        await using var db = NewDatabase();
        var user = User("save");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var storage = new MemoryStorage();
        var queue = new WebpImageQueue();
        var service = new ProjectService(db, storage, webp: queue);
        using var png = new MagickImage(MagickColors.White, 32, 24);
        await using var input = new MemoryStream(png.ToByteArray(MagickFormat.Png));
        var draft = await service.CreateAsync(user.Id, new(input, "picture.png", "image/png", "Picture", 32, 24, IsDraft: true), default);
        await service.SaveEditAsync(user.Id, draft.Id, new(50, "original", "original", 1, true), default);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var reader = queue.ReadAllAsync(cancellation.Token).GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(new WebpImageWork(WebpImageKind.Project, draft.Id), reader.Current);
        var webp = new WebpImageService(db, new WebpVariantStore(storage, storage), storage,
            NullLogger<WebpImageService>.Instance);
        Assert.True(await webp.PrepareAsync(reader.Current, cancellation.Token));
        var variant = Assert.Single(storage.Objects, item => item.Value.Type == "image/webp");
        Assert.EndsWith(".webp", variant.Key);
    }

    private static ApplicationDbContext NewDatabase() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static User User(string name) => new() { Username = name, Email = name + "@example.invalid", PasswordHash = "test" };

    private sealed class MemoryStorage : IStorageService, IStorageObjectLookup
    {
        private int uploads;
        public int Uploads => uploads;
        public ConcurrentDictionary<string, (byte[] Bytes, string Type)> Objects { get; } = new();

        public void Seed(string key, byte[] bytes) => Objects[key] = (bytes, "image/png");
        public Task<bool> ExistsAsync(string key, CancellationToken ct) => Task.FromResult(Objects.ContainsKey(key));
        public Task DownloadAsync(string key, Stream destination, CancellationToken ct) =>
            destination.WriteAsync(Objects[key].Bytes, ct).AsTask();
        public async Task<StoredObject> UploadAsync(Stream source, string key, string type, CancellationToken ct)
        {
            await using var copy = new MemoryStream();
            await source.CopyToAsync(copy, ct);
            Objects[key] = (copy.ToArray(), type);
            Interlocked.Increment(ref uploads);
            return new(key, "s3://test/" + key);
        }
        public Task DeleteAsync(string key, CancellationToken ct) { Objects.TryRemove(key, out _); return Task.CompletedTask; }
        public string CreateDownloadUrl(string key, TimeSpan ttl) => "https://example.invalid/" + key;
        public string CreateUploadUrl(string key, string type, TimeSpan ttl) => throw new NotSupportedException();
    }
}
