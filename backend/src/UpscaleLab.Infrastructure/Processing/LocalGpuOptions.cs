using UpscaleLab.Application.Common;

namespace UpscaleLab.Infrastructure.Processing;

public sealed class LocalGpuOptions
{
    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public int TimeoutMinutes { get; init; } = 35;
    public long MaxArchiveBytes { get; init; } = 256L * 1024 * 1024;
    public long MaxExtractedBytes { get; init; } = 512L * 1024 * 1024;

    public void Validate()
    {
        if (!Enabled) return;
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || ApiKey.Length < 32 ||
            ApiKey.Any(c => c < 33 || c > 126) || TimeoutMinutes is < 1 or > 240 ||
            MaxArchiveBytes <= 0 || MaxExtractedBytes <= 0)
            throw new ConfigurationException("LocalGpu URL, API 키(32자 이상), 시간 및 크기 제한 설정을 확인하세요.");
    }
}
