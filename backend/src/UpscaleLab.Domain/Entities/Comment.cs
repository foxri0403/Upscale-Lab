namespace UpscaleLab.Domain.Entities;

public sealed class Comment : BaseEntity
{
    public Guid GalleryPostId { get; set; }
    public Guid UserId { get; set; }
    public string Content { get; set; } = string.Empty;

    public GalleryPost GalleryPost { get; set; } = null!;
    public User User { get; set; } = null!;
}
