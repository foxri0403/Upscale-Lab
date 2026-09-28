using UpscaleLab.Domain.Enums;

namespace UpscaleLab.Domain.Entities;

public sealed class Device : BaseEntity
{
    public Guid UserId { get; set; }
    public DevicePlatform Platform { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
    public string AspectRatio { get; set; } = string.Empty;
    public ScreenOrientation Orientation { get; set; }

    public User User { get; set; } = null!;
    public ICollection<OptimizedImage> OptimizedImages { get; set; } = new List<OptimizedImage>();
}
