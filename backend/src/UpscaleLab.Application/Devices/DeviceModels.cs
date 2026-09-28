using System.ComponentModel.DataAnnotations;
using UpscaleLab.Domain.Enums;

namespace UpscaleLab.Application.Devices;

public sealed record DeviceRequest(
    DevicePlatform Platform,
    [param: Required, MaxLength(120)] string DeviceName,
    [param: Range(1, 32768)] int ScreenWidth,
    [param: Range(1, 32768)] int ScreenHeight,
    [param: Required, MaxLength(32)] string AspectRatio,
    ScreenOrientation Orientation);

public sealed record DeviceResponse(
    Guid Id,
    DevicePlatform Platform,
    string DeviceName,
    int ScreenWidth,
    int ScreenHeight,
    string AspectRatio,
    ScreenOrientation Orientation,
    DateTime CreatedAt,
    DateTime UpdatedAt);
