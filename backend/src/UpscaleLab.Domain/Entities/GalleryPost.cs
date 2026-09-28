namespace UpscaleLab.Domain.Entities;

public sealed class GalleryPost : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid ImageId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long DownloadCount { get; set; }

    public User User { get; set; } = null!;
    public Image Image { get; set; } = null!;
    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public ICollection<GalleryLike> Likes { get; set; } = new List<GalleryLike>();
}
