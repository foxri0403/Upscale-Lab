using Amazon.S3;
using Amazon.S3.Model;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Storage;

namespace UpscaleLab.Infrastructure.Storage;

public sealed class S3StorageService(IAmazonS3 s3Client, S3StorageOptions options) : IStorageService
{
    public async Task<StoredObject> UploadAsync(
        Stream content,
        string objectKey,
        string contentType,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var request = new PutObjectRequest
        {
            BucketName = options.BucketName,
            Key = objectKey,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false
        };

        await s3Client.PutObjectAsync(request, cancellationToken);
        return new StoredObject(objectKey, $"s3://{options.BucketName}/{objectKey}");
    }

    public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await s3Client.DeleteObjectAsync(options.BucketName, objectKey, cancellationToken);
    }

    public string CreateDownloadUrl(string objectKey, TimeSpan lifetime)
    {
        EnsureConfigured();
        return s3Client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = options.BucketName,
            Key = objectKey,
            Expires = DateTime.UtcNow.Add(lifetime),
            Verb = HttpVerb.GET
        });
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(options.BucketName))
        {
            throw new ConfigurationException("AWS:S3BucketName 설정이 필요합니다.");
        }
    }
}
