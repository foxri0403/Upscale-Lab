namespace UpscaleLab.Infrastructure.Tokens;

public interface ITokenService
{
    Task ReserveAsync(Guid userId, Guid jobId, int targetLongEdge, CancellationToken ct);
    Task CompleteAsync(Guid jobId, CancellationToken ct);
    Task RefundAsync(Guid jobId, CancellationToken ct);
}
