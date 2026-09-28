namespace UpscaleLab.Application.Settings;

public sealed record UpdateUserSettingRequest(bool SyncEnabled, bool DynamicOrientation, bool AutoUpscale);

public sealed record UserSettingResponse(
    Guid Id,
    bool SyncEnabled,
    bool DynamicOrientation,
    bool AutoUpscale,
    DateTime UpdatedAt);
