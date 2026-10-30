using UpscaleLab.Infrastructure.Tokens;

namespace UpscaleLab.Tests;

public sealed class FakeTokenService : ITokenService
{
    public int ReservedCount { get; private set; }
    public int CompletedCount { get; private set; }
    public int RefundedCount { get; private set; }

    public Task ReserveAsync(
        Guid userId,
        Guid jobId,
        int targetLongEdge,
        CancellationToken ct)
    {
        ReservedCount++;
        return Task.CompletedTask;
    }

    public Task CompleteAsync(Guid jobId, CancellationToken ct)
    {
        CompletedCount++;
        return Task.CompletedTask;
    }

    public Task RefundAsync(Guid jobId, CancellationToken ct)
    {
        RefundedCount++;
        return Task.CompletedTask;
    }
}
