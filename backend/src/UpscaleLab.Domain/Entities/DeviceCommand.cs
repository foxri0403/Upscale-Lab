using UpscaleLab.Domain.Enums;

namespace UpscaleLab.Domain.Entities;

public sealed class DeviceCommand : BaseEntity
{
    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;
    public Guid DeviceDisplayId { get; set; }
    public DeviceDisplay DeviceDisplay { get; set; } = null!;
    public Guid? WallpaperAssetId { get; set; }
    public WallpaperAsset? WallpaperAsset { get; set; }
    public DeviceCommandAction Action { get; set; } = DeviceCommandAction.Apply;
    public WallpaperTarget Target { get; set; }
    public Guid RequestId { get; set; }
    public long Sequence { get; set; }
    public DeviceCommandStatus Status { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? Error { get; set; }
}
