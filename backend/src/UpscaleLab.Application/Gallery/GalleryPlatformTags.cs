using UpscaleLab.Application.Common;

namespace UpscaleLab.Application.Gallery;

public static class GalleryPlatformTags
{
    private static readonly string[] Options = ["pc", "mobile", "tablet"];

    public static string[] Normalize(IEnumerable<string>? tags)
    {
        if (tags is null) return [];
        var selected = tags.ToHashSet(StringComparer.Ordinal);
        if (selected.Except(Options).Any())
            throw new ValidationException("상위 태그는 PC, 모바일, 태블릿 중에서 선택해 주세요.");
        return Options.Where(selected.Contains).ToArray();
    }
}

public sealed record ExploreQuery(string? Search = null, string? Sort = null,
    string[]? Tags = null, string[]? PlatformTags = null, bool Untagged = false, int Limit = 100);

public sealed record GalleryEngagement(int LikeCount, bool IsLikedByCurrentUser);
