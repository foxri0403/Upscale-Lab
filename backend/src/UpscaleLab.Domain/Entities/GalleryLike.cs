namespace UpscaleLab.Domain.Entities;

public sealed class GalleryLike : BaseEntity
{
    public Guid GalleryPostId { get; set; }
    public Guid UserId { get; set; }

    public GalleryPost GalleryPost { get; set; } = null!;
    public User User { get; set; } = null!;
}
