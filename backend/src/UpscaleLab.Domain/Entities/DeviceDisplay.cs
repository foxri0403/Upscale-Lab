namespace UpscaleLab.Domain.Entities;

public sealed class DeviceDisplay : BaseEntity
{
    public Guid DeviceId { get; set; }
    public Device Device { get; set; } = null!;
    public string NativeId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public double Scale { get; set; } = 1;
    public int Rotation { get; set; }
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public bool IsPrimary { get; set; }
    public bool IsConnected { get; set; }
    public double? RefreshRateHz { get; set; }
    public bool CanApplyWallpaper { get; set; } = true;
}
