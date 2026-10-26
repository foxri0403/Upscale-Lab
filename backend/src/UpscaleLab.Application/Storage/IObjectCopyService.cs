namespace UpscaleLab.Application.Storage;

public interface IObjectCopyService
{
    Task CopyAsync(string sourceKey, string destinationKey, CancellationToken ct);
}
