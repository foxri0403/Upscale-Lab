namespace UpscaleLab.Infrastructure.Storage;

public sealed class S3StorageOptions
{
    public string Region { get; set; } = "ap-northeast-2";
    public string S3BucketName { get; set; } = string.Empty;
}
