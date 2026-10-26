using UpscaleLab.Domain.Enums;

namespace UpscaleLab.Domain.Entities;

public sealed class WallpaperAssignment : BaseEntity
{
    public Guid DeviceDisplayId { get; set; }
    public DeviceDisplay DeviceDisplay { get; set; } = null!;
    public WallpaperTarget Target { get; set; }
    public Guid? DesiredAssetId { get; set; }
    public Guid? ReportedAssetId { get; set; }
    public WallpaperObservation Observation { get; set; }
    public long Sequence { get; set; }
    public DateTime? LastReportedAt { get; set; }
}
