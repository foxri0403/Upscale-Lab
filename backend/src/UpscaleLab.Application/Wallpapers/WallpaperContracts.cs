using System.ComponentModel.DataAnnotations;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Application.Images;

namespace UpscaleLab.Application.Wallpapers;

public sealed record CreateWallpaperRequest(Guid ProjectId,
    [param: Required, MaxLength(160)] string Title, WallpaperMode Mode, Guid? ProcessingJobId = null,
    Guid? OriginProjectId = null, bool ReuseExisting = false, WallpaperRenderSettings? Render = null);
public sealed record WallpaperRenderSettings(int Strength, string Filter, string Crop, int Scale, bool Foreground, ImageCropRegion? CropRegion = null);
public sealed record RenameWallpaperRequest([param: Required, MaxLength(160)] string Title);
public sealed record ShareWallpaperRequest(bool IsPublic);
public sealed record WallpaperSummary(Guid Id, string Title, WallpaperMode Mode, bool IsPublic, DateTime CreatedAt,
    Guid? SourceProjectId = null, Guid? UserId = null, string? Username = null, string? ProfileImageUrl = null);
// Stored manifests use object keys; network manifests replace keys with fresh URLs.
public sealed record WallpaperFile(string Source, string ContentType, int Order, string Role,
    double Depth = 0, double X = 0, double Y = 0, double Rotation = 0, double Scale = 1,
    double MovementX = 0, double MovementY = 0);
public sealed record WallpaperManifest(int SchemaVersion, Guid AssetId, WallpaperMode Mode, int Width, int Height,
    IReadOnlyList<WallpaperFile> Files, DateTime? UrlsExpireAt = null);

public interface IWallpaperService
{
    Task RenameAsync(Guid userId, Guid assetId, RenameWallpaperRequest request, CancellationToken ct);
    Task<WallpaperSummary> CreateAsync(Guid userId, CreateWallpaperRequest request, CancellationToken ct);
    Task<WallpaperSummary> ImportAsync(Guid userId, Guid assetId, CancellationToken ct);
    Task<IReadOnlyList<WallpaperSummary>> ListAsync(Guid? userId, bool shared, CancellationToken ct);
    Task<WallpaperManifest> GetManifestAsync(Guid? userId, Guid assetId, bool agentAccess, CancellationToken ct);
    Task SetSharingAsync(Guid userId, Guid assetId, bool isPublic, CancellationToken ct);
    Task ArchiveAsync(Guid userId, Guid assetId, CancellationToken ct);
}
