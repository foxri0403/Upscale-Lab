namespace UpscaleLab.Application.Images;

public enum WebpImageKind { Project, Wallpaper }

public sealed record WebpImageView(string Source, string? Background = null, IReadOnlyList<string>? Foregrounds = null);

public interface IWebpImageService
{
    Task<WebpImageView> GetAsync(WebpImageKind kind, Guid id, Guid userId, CancellationToken ct);
}
