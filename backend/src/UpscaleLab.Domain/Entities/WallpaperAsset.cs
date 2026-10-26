using UpscaleLab.Domain.Enums;

namespace UpscaleLab.Domain.Entities;

// Each asset is an immutable release. Its files have independent S3 keys.
public sealed class WallpaperAsset : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? SourceProjectId { get; set; }
    public LiveLayerProject? SourceProject { get; set; }
    public string Title { get; set; } = string.Empty;
    public WallpaperMode Mode { get; set; }
    public string ManifestJson { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    public bool IsArchived { get; set; }
}
