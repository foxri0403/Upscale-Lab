using System.ComponentModel.DataAnnotations;
using UpscaleLab.Domain.Enums;

namespace UpscaleLab.Application.Devices;

public sealed record AgentIdentity(Guid DeviceId, Guid UserId);
public sealed record AgentTokenResponse(Guid DeviceId, string Token);
public sealed record WallpaperReport(WallpaperTarget Target, WallpaperObservation Observation, Guid? AssetId = null);
public sealed record DisplayInventory(
    [param: Required, MaxLength(200)] string NativeId,
    [param: Required, MaxLength(120)] string Name,
    [param: Range(1, 32768)] int Width,
    [param: Range(1, 32768)] int Height,
    double Scale = 1, int Rotation = 0, int OffsetX = 0, int OffsetY = 0,
    bool IsPrimary = false, bool IsConnected = true, IReadOnlyList<WallpaperReport>? Wallpapers = null,
    [param: Range(1, 1000)] double? RefreshRateHz = null, bool CanApplyWallpaper = true);
public sealed record AgentHeartbeat(
    [param: Range(1, long.MaxValue)] long Revision,
    [param: Required, MaxLength(120)] string OsVersion,
    [param: Required, MaxLength(40)] string AgentVersion,
    bool SupportsLive2D, bool SupportsLockScreen,
    [param: Required, MaxLength(16)] IReadOnlyList<DisplayInventory> Displays,
    bool SupportsWallpaperRemoval = false);
public sealed record ApplyWallpaperRequest(Guid RequestId, Guid DisplayId, Guid AssetId, WallpaperTarget Target);
public sealed record ClearWallpaperRequest(Guid RequestId, Guid DisplayId, WallpaperTarget Target);
public sealed record CommandReport(DeviceCommandStatus Status, [param: MaxLength(1000)] string? Error = null);
public sealed record AssignmentResponse(WallpaperTarget Target, Guid? DesiredAssetId, Guid? ReportedAssetId,
    WallpaperObservation Observation, long Sequence, DateTime? LastReportedAt, bool PendingRemoval = false);
public sealed record DisplayState(Guid Id, string NativeId, string Name, int Width, int Height, double Scale,
    int Rotation, int OffsetX, int OffsetY, bool IsPrimary, bool IsConnected, IReadOnlyList<AssignmentResponse> Wallpapers,
    double? RefreshRateHz = null, bool CanApplyWallpaper = true);
public sealed record DeviceState(Guid Id, DevicePlatform Platform, string Name, string OsVersion, string AgentVersion,
    bool IsLinked, DateTime? LastSeenAt, bool RecentlySeen, long InventoryRevision, bool SupportsLive2D,
    bool SupportsLockScreen, IReadOnlyList<DisplayState> Displays, bool SupportsWallpaperRemoval = false);
public sealed record DeviceCommandResponse(Guid Id, Guid DeviceId, Guid DisplayId, Guid? AssetId, WallpaperTarget Target,
    Guid RequestId, long Sequence, DeviceCommandStatus Status, DateTime ExpiresAt, string? Error, DeviceCommandAction Action = DeviceCommandAction.Apply);

public interface IDeviceControlService
{
    Task<AgentTokenResponse> RotateTokenAsync(Guid userId, Guid deviceId, CancellationToken ct);
    Task RevokeTokenAsync(Guid userId, Guid deviceId, CancellationToken ct);
    Task<AgentIdentity?> AuthenticateAsync(string token, CancellationToken ct);
    Task<DeviceState> GetStateAsync(Guid userId, Guid deviceId, CancellationToken ct);
    Task<DeviceState> HeartbeatAsync(AgentIdentity agent, AgentHeartbeat request, CancellationToken ct);
    Task<DeviceCommandResponse> ApplyAsync(Guid userId, Guid deviceId, ApplyWallpaperRequest request, CancellationToken ct);
    Task<DeviceCommandResponse> ClearAsync(Guid userId, Guid deviceId, ClearWallpaperRequest request, CancellationToken ct);
    Task<IReadOnlyList<DeviceCommandResponse>> GetPendingCommandsAsync(Guid userId, Guid deviceId, CancellationToken ct);
    Task<DeviceCommandResponse> ReportAsync(AgentIdentity agent, Guid commandId, CommandReport report, CancellationToken ct);
    Task<Guid> GetCommandAssetAsync(AgentIdentity agent, Guid commandId, CancellationToken ct);
    Task<DeviceCommandResponse> GetCommandAsync(AgentIdentity agent, Guid commandId, CancellationToken ct);
}

public interface IDeviceEvents
{
    Task ChangedAsync(Guid userId, Guid deviceId, CancellationToken ct, bool notifyAgent = false);
}
