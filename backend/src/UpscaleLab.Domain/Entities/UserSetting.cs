namespace UpscaleLab.Domain.Entities;

public sealed class UserSetting : BaseEntity
{
    public Guid UserId { get; set; }
    public bool SyncEnabled { get; set; } = true;
    public bool DynamicOrientation { get; set; } = true;
    public bool AutoUpscale { get; set; } = true;

    public User User { get; set; } = null!;
}
