namespace UpscaleLab.Domain.Entities;

public sealed class UserSetting : BaseEntity
{
    public Guid UserId { get; set; }
    public bool SyncEnabled { get; set; } = true;
    public bool DynamicOrientation { get; set; } = true;
    public bool AutoUpscale { get; set; } = true;
    public double SensorSensitivity { get; set; } = 1;

    public User User { get; set; } = null!;
}
