using UpscaleLab.Companion;
using Xunit;

public sealed class WallpaperRemovalTests
{
    [Fact]
    public void ClearOnlySelectedMonitorAndDetachAfterWindowsConfirmsRemoval()
    {
        var wallpapers = new Dictionary<string, string> { ["one"] = "one.png", ["two"] = "two.png" };
        var detached = false;
        WallpaperRemoval.Clear("one", (monitor, image) => wallpapers[monitor] = image, monitor => wallpapers[monitor], () => detached = true);
        Assert.True(detached); Assert.Equal("", wallpapers["one"]); Assert.Equal("two.png", wallpapers["two"]);
    }
    [Fact]
    public void RejectedRemovalDoesNotDetachTheCurrentLiveWallpaper()
    {
        var detached = false;
        Assert.Throws<InvalidOperationException>(() => WallpaperRemoval.Clear("one", (_, _) => { }, _ => "still-applied.png", () => detached = true));
        Assert.False(detached);
    }
}
