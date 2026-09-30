namespace UpscaleLab.Application.Settings;

using System.ComponentModel.DataAnnotations;

public sealed record UpdateUserSettingRequest(
    bool SyncEnabled,
    bool DynamicOrientation,
    bool AutoUpscale,
    [param: Range(0, 2)] double SensorSensitivity = 1);

public sealed record UserSettingResponse(
    Guid Id,
    bool SyncEnabled,
    bool DynamicOrientation,
    bool AutoUpscale,
    double SensorSensitivity,
    DateTime UpdatedAt);
