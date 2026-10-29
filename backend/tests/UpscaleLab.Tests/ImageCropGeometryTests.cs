using UpscaleLab.Application.Common;
using UpscaleLab.Application.Images;
using Xunit;
namespace UpscaleLab.Tests;
public sealed class ImageCropGeometryTests
{
    [Theory]
    [InlineData("wide",0,38,400,225)]
    [InlineData("portrait",116,0,169,300)]
    [InlineData("square",50,0,300,300)]
    public void LegacyPresetsResolveCenteredPixels(string preset,int x,int y,int w,int h) => Assert.Equal((x,y,w,h),ImageCropGeometry.Resolve(400,300,preset,null));
    [Fact]
    public void NormalizedSelectionsSurviveUpscaling()
    {
        var region=new ImageCropRegion(.25,.1,.5,.6);
        Assert.Equal((100,30,200,180),ImageCropGeometry.Resolve(400,300,"custom",region));
        Assert.Equal((200,60,400,360),ImageCropGeometry.Resolve(800,600,"custom",region));
    }
    [Fact]
    public void InvalidSelectionsAreRejected()
    {
        Assert.Throws<ValidationException>(()=>new ImageCropRegion(.9,0,.2,1).Validate());
        Assert.Throws<ValidationException>(()=>new ImageCropRegion(0,0,double.NaN,1).Validate());
        Assert.Throws<ValidationException>(()=>ImageCropGeometry.Resolve(400,300,"custom",null));
    }
}
