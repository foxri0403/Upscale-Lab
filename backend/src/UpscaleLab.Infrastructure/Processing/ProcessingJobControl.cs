using System.Collections.Concurrent;

namespace UpscaleLab.Infrastructure.Processing;

// The API and queue worker run in one instance. Serialize terminal transitions,
// but release the gate during cloud inference so cancellation remains responsive.
internal static class ProcessingJobControl
{
    private static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private static readonly ConcurrentDictionary<Guid, Execution> Running = new();

    public static async Task<IDisposable> LockAsync(Guid id, CancellationToken ct)
    {
        var gate = Gates[(uint)id.GetHashCode() % Gates.Length];
        await gate.WaitAsync(ct);
        return new Lease(() => gate.Release());
    }

    public static IDisposable? Register(Guid id, CancellationToken host, out CancellationToken token)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(host);
        token = source.Token;
        if (!Running.TryAdd(id, new Execution(source))) { source.Dispose(); return null; }
        return new Lease(() => { Running.TryRemove(id, out _); source.Dispose(); });
    }

    public static void Cancel(Guid id)
    {
        if (Running.TryGetValue(id, out var execution))
        {
            execution.UserCancelled = true;
            try { execution.Source.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    public static bool IsUserCancelled(Guid id) => Running.TryGetValue(id, out var execution) && execution.UserCancelled;

    private sealed class Execution(CancellationTokenSource source)
    {
        public CancellationTokenSource Source { get; } = source;
        public volatile bool UserCancelled;
    }

    private sealed class Lease(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
