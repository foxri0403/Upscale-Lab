
using Amazon.S3;
using Amazon.S3.Model;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Storage;

namespace UpscaleLab.Infrastructure.Storage;

public sealed class S3StorageService(
    IAmazonS3 s3Client,
    S3StorageOptions options
) : IStorageService, IObjectCopyService
{
    // S3 내부 파일 복사
    public async Task CopyAsync(
        string sourceKey,
        string destinationKey,
        CancellationToken ct)
    {
        EnsureConfigured();

        await s3Client.CopyObjectAsync(
            new CopyObjectRequest
            {
                SourceBucket = options.S3BucketName,
                SourceKey = sourceKey,
                DestinationBucket = options.S3BucketName,
                DestinationKey = destinationKey
            },
            ct);
    }

    // S3에 파일 업로드
    public async Task<StoredObject> UploadAsync(
        Stream content,
        string objectKey,
        string contentType,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var request = new PutObjectRequest
        {
            BucketName = options.S3BucketName,
            Key = objectKey,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false
        };

        await s3Client.PutObjectAsync(request, cancellationToken);

        return new StoredObject(
            objectKey,
            $"s3://{options.S3BucketName}/{objectKey}");
    }

    // S3 파일 삭제
    public async Task DeleteAsync(
        string objectKey,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        await s3Client.DeleteObjectAsync(
            options.S3BucketName,
            objectKey,
            cancellationToken);
    }

    // S3 파일 다운로드
    public async Task DownloadAsync(
        string objectKey,
        Stream destination,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var response = await s3Client.GetObjectAsync(
            options.S3BucketName,
            objectKey,
            cancellationToken);

        await response.ResponseStream.CopyToAsync(
            destination,
            cancellationToken);
    }

    // S3 다운로드 전용 임시 URL 생성
    public string CreateDownloadUrl(
        string objectKey,
        TimeSpan lifetime)
    {
        EnsureConfigured();

        return s3Client.GetPreSignedURL(
            new GetPreSignedUrlRequest
            {
                BucketName = options.S3BucketName,
                Key = objectKey,
                Expires = DateTime.UtcNow.Add(lifetime),
                Verb = HttpVerb.GET
            });
    }

    // RunPod GPU 결과 업로드 전용 임시 URL 생성
    public string CreateUploadUrl(
        string objectKey,
        string contentType,
        TimeSpan lifetime)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(objectKey))
            throw new ArgumentException(
                "Object key is required.");

        if (contentType != "image/png")
            throw new ArgumentException(
                "Only PNG uploads are supported.");

        if (lifetime <= TimeSpan.Zero ||
            lifetime > TimeSpan.FromMinutes(30))
            throw new ArgumentOutOfRangeException(
                nameof(lifetime));

        return s3Client.GetPreSignedURL(
            new GetPreSignedUrlRequest
            {
                BucketName = options.S3BucketName,
                Key = objectKey,
                Verb = HttpVerb.PUT,
                ContentType = contentType,
                Expires = DateTime.UtcNow.Add(lifetime)
            });
    }

    // S3 설정 확인
    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(options.S3BucketName))
        {
            throw new ConfigurationException(
                "AWS:S3BucketName 설정이 필요합니다.");
        }
    }
}
