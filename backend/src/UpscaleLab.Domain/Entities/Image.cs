namespace UpscaleLab.Domain.Entities;

public sealed class Image : BaseEntity
{
    public Guid UserId { get; set; }
    public string OriginalUrl { get; set; } = string.Empty;
    public string OriginalObjectKey { get; set; } = string.Empty;
    public int OriginalWidth { get; set; }
    public int OriginalHeight { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;

    public User User { get; set; } = null!;
    public ICollection<OptimizedImage> OptimizedImages { get; set; } = new List<OptimizedImage>();
    public ICollection<GalleryPost> GalleryPosts { get; set; } = new List<GalleryPost>();
}
