using UpscaleLab.Domain.Enums;

namespace UpscaleLab.Domain.Entities;

public sealed class Device : BaseEntity
{
    public bool SupportsWallpaperRemoval { get; set; }
    public Guid UserId { get; set; }
    public DevicePlatform Platform { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
    public string AspectRatio { get; set; } = string.Empty;
    public ScreenOrientation Orientation { get; set; }
    public string? AgentTokenHash { get; set; }
    public string OsVersion { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public DateTime? LastSeenAt { get; set; }
    public long InventoryRevision { get; set; }
    public long CommandSequence { get; set; }
    public Guid ControlVersion { get; set; } = Guid.NewGuid();
    public bool SupportsLive2D { get; set; }
    public bool SupportsLockScreen { get; set; }
    public ICollection<DeviceDisplay> Displays { get; set; } = new List<DeviceDisplay>();

    public User User { get; set; } = null!;
    public ICollection<OptimizedImage> OptimizedImages { get; set; } = new List<OptimizedImage>();
}
