namespace UpscaleLab.Application.Storage;

public interface IStorageObjectLookup
{
    Task<bool> ExistsAsync(string objectKey, CancellationToken ct);
}
