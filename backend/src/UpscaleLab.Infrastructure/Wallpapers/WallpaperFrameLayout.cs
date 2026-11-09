using UpscaleLab.Application.Common;
using UpscaleLab.Application.Wallpapers;

namespace UpscaleLab.Infrastructure.Wallpapers;

internal sealed record WallpaperFrameLayout(int Width, int Height, double Scale, double Left, double Top)
{
    public static WallpaperFrameLayout Create(WallpaperFrame frame, int sourceWidth, int sourceHeight)
    {
        if (frame.Width is < 1 or > 32768 || frame.Height is < 1 or > 32768 ||
            !double.IsFinite(frame.Zoom) || frame.Zoom is < 1 or > 3 ||
            !double.IsFinite(frame.X) || frame.X is < 0 or > 1 ||
            !double.IsFinite(frame.Y) || frame.Y is < 0 or > 1)
            throw new ValidationException("배경화면 미리보기의 크기 또는 위치가 올바르지 않습니다.");

        var reduction = Math.Min(1, 4096d / Math.Max(frame.Width, frame.Height));
        var width = Math.Max(1, (int)Math.Round(frame.Width * reduction, MidpointRounding.AwayFromZero));
        var height = Math.Max(1, (int)Math.Round(frame.Height * reduction, MidpointRounding.AwayFromZero));
        var scale = (frame.Contain
            ? Math.Min((double)width / sourceWidth, (double)height / sourceHeight)
            : Math.Max((double)width / sourceWidth, (double)height / sourceHeight)) * frame.Zoom;
        return new(width, height, scale, (width - sourceWidth * scale) * frame.X,
            (height - sourceHeight * scale) * frame.Y);
    }
}
