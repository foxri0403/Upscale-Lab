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

    public static string[] NormalizeMany(IEnumerable<string>? tags, string? legacyTag = null)
    {
        var values = tags?.Select(Normalize).Distinct(StringComparer.Ordinal).ToArray()
            ?? [Normalize(legacyTag)];
        if (values.Length > All.Count) throw new ValidationException("태그가 너무 많습니다.");
        if (values.Length == 0) return [Unspecified];
        return values.Length > 1 ? values.Where(x => x != Unspecified).ToArray() : values;
    }

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
