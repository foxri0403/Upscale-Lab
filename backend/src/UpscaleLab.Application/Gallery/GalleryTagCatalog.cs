using UpscaleLab.Application.Common;

namespace UpscaleLab.Application.Gallery;

public static class GalleryTagCatalog
{
    public const string Unspecified = "장르 설정되지 않음";

    public static IReadOnlyList<string> All { get; } =
    [
        "동물",
        "애니",
        "게임",
        "여자",
        "남자",
        "풍경",
        "픽셀 아트",
        "레트로",
        "SF",
        "스포츠",
        "자동차",
        "비행기",
        "밀리터리",
        Unspecified
    ];

    public static string Normalize(string? tag)
    {
        var normalized = string.IsNullOrWhiteSpace(tag) ? Unspecified : tag.Trim();
        if (!All.Contains(normalized, StringComparer.Ordinal))
        {
            throw new ValidationException("지원하지 않는 갤러리 태그입니다.");
        }

        return normalized;
    }
}
