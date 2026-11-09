namespace UpscaleLab.Domain.Entities;

public sealed class WallpaperLike : BaseEntity
{
    public Guid WallpaperAssetId { get; set; }
    public Guid UserId { get; set; }
}
