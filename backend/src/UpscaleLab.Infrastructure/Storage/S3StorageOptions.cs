namespace UpscaleLab.Infrastructure.Storage;

public sealed class S3StorageOptions
{
    public string Region { get; set; } = "ap-northeast-2";
    public string BucketName { get; set; } = string.Empty;
}
