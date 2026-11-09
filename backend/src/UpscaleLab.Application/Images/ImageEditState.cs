using System.Text.Json;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Gallery;

namespace UpscaleLab.Application.Images;

public sealed record SaveImageEditRequest(int Strength, string Filter, string Crop, int Scale, bool Foreground,
    string? Title = null, string[]? Tags = null, bool LiveEnabled = false, bool? MouseEnabled = null, bool? GyroEnabled = null, ImageCropRegion? CropRegion = null, HeadRigSettings? Rig = null,
    string[]? PlatformTags = null);

// The same motion settings are stored with the project and copied into wallpaper manifests.
// Optional parameters preserve compatibility with previously saved head-only rigs.
public sealed record HeadRigSettings(bool Enabled, int HeadTilt, int EyeMove, int HairSway,
    int PivotX, int PivotY, bool Idle, string BodyMode = "fixed", int UpperBody = 40,
    int LowerBody = 20, int Torso = 50, int LeftArm = 35, int RightArm = 35,
    int LeftHand = 25, int RightHand = 25, int LeftLeg = 20, int RightLeg = 20,
    int TorsoPivotY = 40, int LowerPivotY = 68, Dictionary<string, string>? PartAssignments = null)
{
    private static readonly HashSet<string> Parts = new(StringComparer.Ordinal)
    { "fixed", "head", "eye", "frontHair", "backHair", "torso", "leftArm", "rightArm",
      "leftHand", "rightHand", "lowerBody", "leftLeg", "rightLeg" };
    public void Validate()
    {
        if (new[] { HeadTilt, EyeMove, HairSway, PivotX, PivotY, UpperBody, LowerBody,
            Torso, LeftArm, RightArm, LeftHand, RightHand, LeftLeg, RightLeg,
            TorsoPivotY, LowerPivotY }.Any(x => x is < 0 or > 100) ||
            BodyMode is not ("fixed" or "follow") ||
            PartAssignments is { Count: > 128 } ||
            PartAssignments?.Any(kv => kv.Key.Length > 100 || !Parts.Contains(kv.Value)) == true)
            throw new ValidationException("캐릭터 움직임 또는 파츠 지정 설정이 올바르지 않습니다.");
    }
}

// Non-destructive library edits are separate from immutable device wallpaper files.
public sealed record ImageEditState(int Strength, string Filter, string Crop, int Scale, bool Foreground, DateTime SavedAt,
    string[]? Tags = null, bool LiveEnabled = false, bool MouseEnabled = false, bool GyroEnabled = false, ImageCropRegion? CropRegion = null, int MotionDefaultsVersion = 0, HeadRigSettings? Rig = null,
    string[]? PlatformTags = null, bool IsReadOnly = false)
{
    public const int DefaultMotionStrength = 50;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ImageEditState Original() =>
        new(DefaultMotionStrength, "original", "original", 1, true, DateTime.UtcNow, MotionDefaultsVersion: 1);

    public static ImageEditState? RequireEditable(string? savedEdit)
    {
        var state = FromJson(savedEdit);
        state?.EnsureEditable();
        return state;
    }

    public void EnsureEditable()
    {
        if (IsReadOnly)
            throw new ConflictException("다른 사용자의 저장 이미지는 편집하거나 다시 공유할 수 없습니다.");
    }

    public ImageEditState WithTags(string[]? tags, string[]? platformTags) => this with
    {
        Tags = tags is null ? Tags : GalleryTagCatalog.NormalizeMany(tags).Where(x => x != GalleryTagCatalog.Unspecified).ToArray(),
        PlatformTags = platformTags is null ? PlatformTags : GalleryPlatformTags.Normalize(platformTags),
        SavedAt = DateTime.UtcNow
    };

    public static ImageEditState Create(SaveImageEditRequest request, ImageEditState? previous = null)
    {
        if (request.Strength is < 0 or > 100 || request.Scale is not (1 or 2 or 4) ||
            request.Filter is not ("original" or "vivid" or "warm" or "mono" or "soft") ||
            request.Crop is not ("original" or "wide" or "square" or "portrait" or "custom"))
            throw new ValidationException("이미지 편집 설정이 올바르지 않습니다.");
        request.CropRegion?.Validate();
        request.Rig?.Validate();
        if (request.Crop == "custom" && request.CropRegion == null) throw new ValidationException("사용자 비율의 선택 영역을 지정해 주세요.");
        if (request.Title is not null && (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 160))
            throw new ValidationException("이미지 이름은 1~160자로 입력해 주세요.");
        return new(request.Strength, request.Filter, request.Crop, request.Scale, request.Foreground, DateTime.UtcNow,
            request.Tags is null ? null : GalleryTagCatalog.NormalizeMany(request.Tags).Where(x => x != GalleryTagCatalog.Unspecified).ToArray(),
            request.LiveEnabled, request.MouseEnabled ?? previous?.MouseEnabled ?? false,
            request.GyroEnabled ?? previous?.GyroEnabled ?? false, request.Crop == "original" ? null : request.CropRegion, 1, request.Rig ?? previous?.Rig,
            request.PlatformTags is null ? previous?.PlatformTags : GalleryPlatformTags.Normalize(request.PlatformTags));
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);
    public static ImageEditState? FromJson(string? value) => value == null || Projects.ProjectDraftState.IsDraft(value)
        ? null : JsonSerializer.Deserialize<ImageEditState>(value, Json);
}
