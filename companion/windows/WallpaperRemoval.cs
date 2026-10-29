namespace UpscaleLab.Companion;

internal static class WallpaperRemoval
{
    public static void Clear(string monitorPath, Action<string, string> setWallpaper, Func<string, string> getWallpaper, Action detachLive)
    {
        if (string.IsNullOrWhiteSpace(monitorPath)) throw new InvalidOperationException("삭제할 모니터를 찾지 못했습니다.");
        setWallpaper(monitorPath, string.Empty);
        if (!string.IsNullOrEmpty(getWallpaper(monitorPath)))
            throw new InvalidOperationException("Windows 배경화면 삭제를 확인하지 못했습니다.");
        detachLive();
    }
}
