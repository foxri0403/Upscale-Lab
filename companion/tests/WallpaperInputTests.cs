using UpscaleLab.Companion;
using Xunit;
public sealed class WallpaperInputTests
{
    [Theory]
    [InlineData(true,null,null,true)]
    [InlineData(false,true,true,false)]
    [InlineData(true,true,false,true)]
    [InlineData(true,false,true,false)]
    public void WindowsUsesOnlyItsSelectedMouseInputWithLegacyCompatibility(bool motion,bool? mouse,bool? gyro,bool expected)
    {Assert.Equal(expected,new WallpaperManifest(1,Guid.NewGuid(),"Live2D",10,10,[],motion,mouse,gyro).UsesMouse);}
}

public sealed class CharacterWallpaperInputTests
{
    [Fact]
    public void TransparentCharacterPartsAreValidLive2DManifest()
    {
        var files = new[] { new WallpaperFile("face", "image/png", 0, "Face"),
            new WallpaperFile("body", "image/png", 1, "Body") };
        WallpaperCache.Validate(new WallpaperManifest(1, Guid.NewGuid(), "Live2D", 500, 1000, files));
    }
    [Fact]
    public void RigMapsManualLayerAndSeparatelyControlsBodyAndHead()
    {
        var rig = new CharacterRig(true, BodyMode: "follow", PartAssignments: new() { ["arm-1"] = "leftArm" });
        var layer = new WallpaperFile("arm", "image/png", 1, "Other", LayerId: "arm-1");
        Assert.Equal("leftArm", RigMotion.Part(layer, rig));
        Assert.True(RigMotion.AngleDegrees("leftArm", rig, 1) > 0);
        Assert.Equal(0, RigMotion.AngleDegrees("leftArm", rig with { BodyMode = "fixed" }, 1));
    }
}
