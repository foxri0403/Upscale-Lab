using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Storage;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Database;

namespace UpscaleLab.Tests;

internal sealed class WorkflowStore : IStorageService, IObjectCopyService
{
    public Dictionary<string, byte[]> Files { get; } = [];
    public bool FailUpload { get; set; }
    public int? FailCopyAfter { get; set; }
    private int copies;
    private int urls;
    public async Task<StoredObject> UploadAsync(Stream content, string key, string contentType, CancellationToken ct)
    {
        if (FailUpload) throw new IOException("Simulated storage failure");
        using var data = new MemoryStream();
        await content.CopyToAsync(data, ct);
        Files[key] = data.ToArray();
        return new(key, "s3://test/" + key);
    }
    public Task CopyAsync(string sourceKey, string destinationKey, CancellationToken ct)
    {
        if (FailCopyAfter.HasValue && copies++ >= FailCopyAfter.Value) throw new IOException("Simulated copy failure");
        Files[destinationKey] = Files[sourceKey].ToArray();
        return Task.CompletedTask;
    }
    public Task DownloadAsync(string key, Stream destination, CancellationToken ct) => destination.WriteAsync(Files[key], ct).AsTask();
    public Task DeleteAsync(string key, CancellationToken ct) { Files.Remove(key); return Task.CompletedTask; }
    public string CreateUploadUrl(
        string objectKey,
        string contentType,
        TimeSpan lifetime)
    {
        throw new NotSupportedException(
            "Upload URL is not required in this test.");
    }
    public string CreateDownloadUrl(string key, TimeSpan lifetime) => $"https://storage.test/{key}?generation={++urls}";
}

internal sealed class WorkflowClock : TimeProvider
{
    public DateTimeOffset Time { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Time;
}

internal static class WorkflowData
{
    public static ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public static Device Device(Guid userId) => new() { UserId = userId, Platform = DevicePlatform.Windows,
        DeviceName = "Desktop", ScreenWidth = 1920, ScreenHeight = 1080, AspectRatio = "16:9" };
    public static LiveLayerProject Project(Guid userId) => new() { UserId = userId, Title = "Source",
        OriginalImage = new Image { UserId = userId, FileName = "source.png", ContentType = "image/png",
            OriginalObjectKey = "source.png", OriginalUrl = "s3://test/source.png", OriginalWidth = 10, OriginalHeight = 10 } };
}
