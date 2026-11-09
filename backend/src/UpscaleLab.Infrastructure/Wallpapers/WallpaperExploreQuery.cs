using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Gallery;
using UpscaleLab.Application.Images;
using UpscaleLab.Domain.Entities;

namespace UpscaleLab.Infrastructure.Wallpapers;

internal static class WallpaperExploreQuery
{
    public static IQueryable<WallpaperAsset> Filter(IQueryable<WallpaperAsset> query, ExploreQuery options, bool relational)
    {
        if (!string.IsNullOrWhiteSpace(options.Search))
        {
            var name = options.Search.Trim().ToLowerInvariant();
            query = query.Where(asset => asset.Title.ToLower().Contains(name));
        }

        var platforms = GalleryPlatformTags.Normalize(options.PlatformTags);
        if (platforms.Length > 0)
            query = Match(query, "platformTags", platforms, false, relational);
        if (options.Untagged)
            return Match(query, "tags", GalleryTagCatalog.All.Where(tag => tag != GalleryTagCatalog.Unspecified), true, relational);
        if (options.Tags is { Length: > 0 })
            query = Match(query, "tags", GalleryTagCatalog.NormalizeMany(options.Tags), false, relational);
        return query;
    }

    private static IQueryable<WallpaperAsset> Match(IQueryable<WallpaperAsset> query, string field,
        IEnumerable<string> tags, bool negate, bool relational)
    {
        if (!relational)
            return query.Where(asset => HasTag(asset.SavedEditJson, field, tags) != negate);

        var parameter = Expression.Parameter(typeof(WallpaperAsset), "asset");
        var json = Expression.Property(parameter, nameof(WallpaperAsset.SavedEditJson));
        Expression matches = Expression.Constant(false);
        foreach (var tag in tags)
        {
            var filter = JsonSerializer.Serialize(new Dictionary<string, string[]> { [field] = [tag] });
            Expression<Func<string, bool>> contains = value => EF.Functions.JsonContains(value, filter);
            var call = (MethodCallExpression)contains.Body;
            matches = Expression.OrElse(matches, call.Update(null, [call.Arguments[0], json, call.Arguments[2]]));
        }
        matches = Expression.AndAlso(Expression.NotEqual(json, Expression.Constant(null, typeof(string))), matches);
        return query.Where(Expression.Lambda<Func<WallpaperAsset, bool>>(negate ? Expression.Not(matches) : matches, parameter));
    }

    private static bool HasTag(string? json, string field, IEnumerable<string> tags)
    {
        var edit = ImageEditState.FromJson(json);
        return (field == "platformTags" ? edit?.PlatformTags : edit?.Tags)?.Any(tags.Contains) == true;
    }
}
