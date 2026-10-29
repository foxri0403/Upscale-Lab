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
