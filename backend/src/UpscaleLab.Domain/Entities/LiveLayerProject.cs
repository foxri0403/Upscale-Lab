using UpscaleLab.Domain.Enums;

namespace UpscaleLab.Domain.Entities;

public sealed class LiveLayerProject : BaseEntity
{
    public Guid UserId { get; set; }
    public Guid OriginalImageId { get; set; }
    public string Title { get; set; } = string.Empty;
    public ProjectStatus Status { get; set; } = ProjectStatus.Uploaded;
    public string? FailureReason { get; set; }

    public User User { get; set; } = null!;
    public Image OriginalImage { get; set; } = null!;
    public ICollection<ImageLayer> Layers { get; set; } = new List<ImageLayer>();
    public ICollection<ProcessingJob> ProcessingJobs { get; set; } = new List<ProcessingJob>();
    public ICollection<GalleryPost> GalleryPosts { get; set; } = new List<GalleryPost>();
}
