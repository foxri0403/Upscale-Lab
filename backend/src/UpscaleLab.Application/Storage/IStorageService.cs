namespace UpscaleLab.Application.Storage;

public sealed record StoredObject(
    string ObjectKey,
    string StorageUrl
);

public interface IStorageService
{
    Task<StoredObject> UploadAsync(
        Stream content,
        string objectKey,
        string contentType,
        CancellationToken cancellationToken
    );

    Task DownloadAsync(
        string objectKey,
        Stream destination,
        CancellationToken cancellationToken
    );

    Task DeleteAsync(
        string objectKey,
        CancellationToken cancellationToken
    );

    string CreateDownloadUrl(
        string objectKey,
        TimeSpan lifetime
    );

    // RunPod GPU가 결과 이미지를 S3에 직접 업로드
    string CreateUploadUrl(
        string objectKey,
        string contentType,
        TimeSpan lifetime
    );
}