using System.Drawing;
using UpscaleLab.Companion;
using Xunit;

public sealed class WallpaperPlaybackTests
{
    private static readonly Rectangle Monitor = new(-1920, 0, 1920, 1080);
    private static readonly Rectangle OtherMonitor = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData(nameof(ForegroundWindowMode.Active), false)]
    [InlineData(nameof(ForegroundWindowMode.Maximized), true)]
    [InlineData(nameof(ForegroundWindowMode.FullScreen), true)]
    [InlineData(nameof(ForegroundWindowMode.None), false)]
    public void RulesApplyToTheForegroundMonitorAndUseTheDisplayRuleDuringSleep(string windowMode, bool defaultPause)
    {
        var mode = Enum.Parse<ForegroundWindowMode>(windowMode);
        var foreground = new ForegroundWindow(Monitor, mode);
        var defaults = new PlaybackRules();
        Assert.Equal(defaultPause, defaults.ShouldPause(Monitor, foreground, false));
        Assert.False(defaults.ShouldPause(OtherMonitor, foreground, false));
        Assert.True(defaults.ShouldPause(OtherMonitor, foreground, true));
        Assert.False((defaults with { PauseWhenDisplaySleeps = false }).ShouldPause(Monitor, foreground, true));

        var custom = new PlaybackRules(PauseWhenActive: true, PauseWhenMaximized: false, PauseWhenFullScreen: false);
        Assert.Equal(mode == ForegroundWindowMode.Active, custom.ShouldPause(Monitor, foreground, false));
    }

    [Fact]
    public void WindowClassificationHandlesASecondaryMonitorAndDistinguishesMaximizedFromFullScreen()
    {
        Assert.Equal(ForegroundWindowMode.Active, ForegroundWindow.Classify(new(-1700, 100, 800, 600), Monitor, false, true));
        Assert.Equal(ForegroundWindowMode.Maximized, ForegroundWindow.Classify(Monitor, Monitor, true, true));
        Assert.Equal(ForegroundWindowMode.FullScreen, ForegroundWindow.Classify(Monitor, Monitor, false, true));
        Assert.Equal(ForegroundWindowMode.FullScreen, ForegroundWindow.Classify(Monitor, Monitor, true, false));
    }
}
