namespace UpscaleLab.Domain.Entities;

public sealed class GalleryPost : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid ImageId { get; set; }
    public Guid? ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Tag { get; set; } = "장르 설정되지 않음";
    public string? Description { get; set; }
    public long DownloadCount { get; set; }
    public bool IsPublic { get; set; } = true;

    public User User { get; set; } = null!;
    public Image Image { get; set; } = null!;
    public LiveLayerProject? Project { get; set; }
    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public ICollection<GalleryLike> Likes { get; set; } = new List<GalleryLike>();
}
