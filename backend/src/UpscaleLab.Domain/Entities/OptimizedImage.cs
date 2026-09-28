using UpscaleLab.Domain.Enums;

namespace UpscaleLab.Domain.Entities;

public sealed class OptimizedImage : BaseEntity
{
    public Guid ImageId { get; set; }
    public Guid DeviceId { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public ScreenOrientation Orientation { get; set; }
    public string Url { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;

    public Image Image { get; set; } = null!;
    public Device Device { get; set; } = null!;
}
