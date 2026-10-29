using System.Text.Json;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Gallery;

namespace UpscaleLab.Application.Images;

public sealed record SaveImageEditRequest(int Strength, string Filter, string Crop, int Scale, bool Foreground,
    string? Title = null, string[]? Tags = null, bool LiveEnabled = false, bool? MouseEnabled = null, bool? GyroEnabled = null, ImageCropRegion? CropRegion = null);

// Non-destructive library edits are separate from immutable device wallpaper files.
public sealed record ImageEditState(int Strength, string Filter, string Crop, int Scale, bool Foreground, DateTime SavedAt,
    string[]? Tags = null, bool LiveEnabled = false, bool MouseEnabled = false, bool GyroEnabled = false, ImageCropRegion? CropRegion = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ImageEditState Create(SaveImageEditRequest request, ImageEditState? previous = null)
    {
        if (request.Strength is < 0 or > 100 || request.Scale is not (1 or 2 or 4) ||
            request.Filter is not ("original" or "vivid" or "warm" or "mono" or "soft") ||
            request.Crop is not ("original" or "wide" or "square" or "portrait" or "custom"))
            throw new ValidationException("이미지 편집 설정이 올바르지 않습니다.");
        request.CropRegion?.Validate();
        if (request.Crop == "custom" && request.CropRegion == null) throw new ValidationException("사용자 비율의 선택 영역을 지정해 주세요.");
        if (request.Title is not null && (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 160))
            throw new ValidationException("이미지 이름은 1~160자로 입력해 주세요.");
        return new(request.Strength, request.Filter, request.Crop, request.Scale, request.Foreground, DateTime.UtcNow,
            request.Tags is null ? null : GalleryTagCatalog.NormalizeMany(request.Tags).Where(x => x != GalleryTagCatalog.Unspecified).ToArray(),
            request.LiveEnabled, request.MouseEnabled ?? previous?.MouseEnabled ?? false,
            request.GyroEnabled ?? previous?.GyroEnabled ?? false, request.Crop == "original" ? null : request.CropRegion);
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static ImageEditState? FromJson(string? value) => value == null || Projects.ProjectDraftState.IsDraft(value)
        ? null : JsonSerializer.Deserialize<ImageEditState>(value, Json);
}
