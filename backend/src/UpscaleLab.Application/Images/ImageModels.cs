using System.ComponentModel.DataAnnotations;
using UpscaleLab.Domain.Enums;

namespace UpscaleLab.Application.Images;

public sealed record ImageUploadCommand(
    Stream Content,
    [param: Required, MaxLength(255)] string FileName,
    [param: Required, MaxLength(100)] string ContentType,
    [param: Range(1, 32768)] int OriginalWidth,
    [param: Range(1, 32768)] int OriginalHeight);

public sealed record ImageResponse(
    Guid Id,
    string OriginalUrl,
    int OriginalWidth,
    int OriginalHeight,
    string FileName,
    string ContentType,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<OptimizedImageResponse> OptimizedImages);

public sealed record OptimizedImageResponse(
    Guid Id,
    Guid DeviceId,
    int Width,
    int Height,
    ScreenOrientation Orientation,
    string Url,
    DateTime CreatedAt);

public sealed record DownloadUrlResponse(string Url, DateTime ExpiresAt);
