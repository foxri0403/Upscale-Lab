namespace UpscaleLab.Application.Storage;

public sealed record StoredObject(string ObjectKey, string StorageUrl);

public interface IStorageService
{
    Task<StoredObject> UploadAsync(Stream content, string objectKey, string contentType, CancellationToken cancellationToken);
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);
    string CreateDownloadUrl(string objectKey, TimeSpan lifetime);
}
