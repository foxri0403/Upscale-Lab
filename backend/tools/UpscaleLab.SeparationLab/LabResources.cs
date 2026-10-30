using System.Text.Json;
using Amazon.S3;
using UpscaleLab.Application.Processing;
using UpscaleLab.Application.Storage;
using UpscaleLab.Infrastructure.Processing;
using UpscaleLab.Infrastructure.Storage;

namespace UpscaleLab.SeparationLab;

public sealed class LabJournal
{
    public Guid UserId { get; set; } = Guid.NewGuid();
    public HashSet<string> Keys { get; set; } = [];
    public HashSet<Guid> Jobs { get; set; } = [];
    private string file = "";
    private readonly object gate = new();
    public static LabJournal Open(string file) {
        var journal = File.Exists(file) ? JsonSerializer.Deserialize<LabJournal>(File.ReadAllText(file))! : new LabJournal();
        journal.file = Path.GetFullPath(file); journal.Save(); return journal;
    }
    private void Save() {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var next = file + ".next"; File.WriteAllText(next, JsonSerializer.Serialize(this)); File.Move(next, file, true);
    }
    public void Track(string key) { Check(key); lock (gate) { Keys.Add(key); Save(); } }
    public void Track(Guid job) { if (job == Guid.Empty) throw new ArgumentException("Invalid lab job"); lock (gate) { Jobs.Add(job); Save(); } }
    public void Check(string key) {
        if (!key.StartsWith($"users/{UserId:N}/", StringComparison.Ordinal) || key.Contains("..")) throw new InvalidOperationException("Only this lab account's objects are accessible.");
    }
    public async Task CleanupAsync(IAmazonS3 s3, string bucket, CancellationToken ct) {
        List<string> keys; lock (gate) {
            keys = Keys.ToList(); foreach (var key in keys) Check(key);
            foreach (var job in Jobs) foreach (var name in new[] { "input.png", "mask.png", "background.png", "foreground.png", "complete.json", "remote.json" })
                keys.Add($"cloud-jobs/layers/{job:N}/{name}");
        }
        foreach (var key in keys) await s3.DeleteObjectAsync(bucket, key, ct);
        lock (gate) { Keys.Clear(); Jobs.Clear(); Save(); }
    }
}

internal sealed class LabStorage(IAmazonS3 s3, S3StorageOptions options, LabJournal journal) : IStorageService
{
    private readonly S3StorageService storage = new(s3, options);
    public async Task<StoredObject> UploadAsync(Stream content, string key, string type, CancellationToken ct) {
        journal.Track(key); return await storage.UploadAsync(content, key, type, ct);
    }
    public Task DownloadAsync(string key, Stream destination, CancellationToken ct) { journal.Check(key); return storage.DownloadAsync(key, destination, ct); }
    public Task DeleteAsync(string key, CancellationToken ct) { journal.Check(key); return storage.DeleteAsync(key, ct); }
    public string CreateDownloadUrl(string key, TimeSpan lifetime) { journal.Check(key); return storage.CreateDownloadUrl(key, lifetime); }
    public string CreateUploadUrl(string key, string type, TimeSpan lifetime) { journal.Track(key); return storage.CreateUploadUrl(key, type, lifetime); }
}

internal sealed class LabQueue(LabJournal journal) : IProcessingJobQueue
{
    private readonly ProcessingJobQueue queue = new();
    public ValueTask EnqueueAsync(Guid jobId, CancellationToken ct) { journal.Track(jobId); return queue.EnqueueAsync(jobId, ct); }
    public ValueTask<Guid> DequeueAsync(CancellationToken ct) => queue.DequeueAsync(ct);
}
