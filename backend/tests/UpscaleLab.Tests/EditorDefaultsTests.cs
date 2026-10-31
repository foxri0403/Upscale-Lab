using UpscaleLab.Application.Images;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class EditorDefaultsTests
{
    [Fact]
    public void LegacyDefaultCanBeDistinguishedFromANewlyChosen57AfterSaveAndReload()
    {
        var old = ImageEditState.FromJson("{\"strength\":57,\"filter\":\"original\",\"crop\":\"original\",\"scale\":1,\"foreground\":true,\"savedAt\":\"2026-10-01T00:00:00Z\"}")!;
        Assert.Equal(0, old.MotionDefaultsVersion);
        var chosen = ImageEditState.Create(new(57, "original", "original", 1, true), old);
        var reloaded = ImageEditState.FromJson(chosen.ToJson())!;
        Assert.Equal(1, reloaded.MotionDefaultsVersion); Assert.Equal(57, reloaded.Strength);
    }
}
