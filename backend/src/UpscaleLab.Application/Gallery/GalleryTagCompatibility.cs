using System.Text.Json;
using UpscaleLab.Application.Common;

namespace UpscaleLab.Application.Gallery;

// Tags created by clients against the legacy single-tag API survive server upgrades.
public static class GalleryTagCompatibility
{
    public const string Marker = "\n[UpscaleLab.Tags:v1]";

    public static IReadOnlyList<string> Resolve(string tag, IReadOnlyList<string> tags, string? description)
    {
        if (tags.Count > 1) return tags;
        var embedded = Read(description);
        if (embedded is { Length: > 0 } && embedded[0] == tag) return embedded;
        return tags.Count == 0 ? [tag] : tags;
    }

    public static string? WithoutMetadata(string? description)
    {
        if (description is null || Read(description) is null) return description;
        return description[..description.LastIndexOf(Marker, StringComparison.Ordinal)];
    }

    private static string[]? Read(string? description)
    {
        var index = description?.LastIndexOf(Marker, StringComparison.Ordinal) ?? -1;
        if (index < 0) return null;
        try
        {
            var tags = JsonSerializer.Deserialize<string[]>(description![(index + Marker.Length)..]);
            if (tags is not { Length: > 0 } || tags.Any(string.IsNullOrWhiteSpace)) return null;
            return GalleryTagCatalog.NormalizeMany(tags);
        }
        catch (JsonException) { return null; }
        catch (ValidationException) { return null; }
    }
}
