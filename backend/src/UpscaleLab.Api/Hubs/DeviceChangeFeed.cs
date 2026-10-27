using Microsoft.Extensions.Caching.Memory;

namespace UpscaleLab.Api.Hubs;

// A cursor survives idle requests; subscribe/check happen under the same lock so changes cannot be lost.
// The cache is bounded. Eviction/restart changes the cursor and makes clients re-read authoritative state.
public sealed class DeviceChangeFeed : IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 4096 });
    private readonly object gate = new();
    private sealed class Entry
    {
        public readonly object Gate = new();
        public Guid Cursor = Guid.NewGuid();
        public TaskCompletionSource<Guid> Changed = Next();
        public static TaskCompletionSource<Guid> Next() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private Entry Get(Guid userId, Guid? deviceId)
    {
        lock (gate)
        {
            var key = (userId, deviceId);
            if (cache.TryGetValue<Entry>(key, out var entry)) return entry!;
            return cache.Set(key, new Entry(), new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = TimeSpan.FromMinutes(5) });
        }
    }
    public async Task<Guid> WaitAsync(Guid userId, Guid? deviceId, Guid? after, CancellationToken ct)
    {
        var entry = Get(userId, deviceId); Task<Guid> wait;
        lock (entry.Gate)
        {
            if (after != entry.Cursor) return entry.Cursor;
            wait = entry.Changed.Task;
        }
        try { return await wait.WaitAsync(TimeSpan.FromSeconds(20), ct); }
        catch (TimeoutException) { lock (entry.Gate) return entry.Cursor; }
    }
    public void Publish(Guid userId, Guid deviceId, bool notifyAgent = true)
    {
        foreach (var target in notifyAgent ? new Guid?[] { null, deviceId } : new Guid?[] { null })
        {
            var entry = Get(userId, target);
            lock (entry.Gate)
            {
                entry.Cursor = Guid.NewGuid(); var pending = entry.Changed; entry.Changed = Entry.Next();
                pending.TrySetResult(entry.Cursor);
            }
        }
    }
    public void Dispose() => cache.Dispose();
}
