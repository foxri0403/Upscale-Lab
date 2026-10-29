using ImageMagick;
using System.Text.Json;
using UpscaleLab.Application.Images;
using UpscaleLab.Application.Wallpapers;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Wallpapers;
using Xunit;

namespace UpscaleLab.Tests;
public sealed class WallpaperMotionDeliveryTests
{
    [Fact]
    public async Task CropFilterAndScaleKeepTwoLayersAndTransparentSubjectWithIndependentInputs()
    {
        await using var db=WorkflowData.Db();var store=new WorkflowStore();var project=WorkflowData.Project(Guid.NewGuid());
        project.OriginalImage.OriginalWidth=12;project.OriginalImage.OriginalHeight=8;
        foreach(var (type,name,color) in new[]{(LayerType.Background,"bg",MagickColors.Blue),(LayerType.Body,"fg",MagickColors.Transparent)}){
            using var image=new MagickImage(color,12,8);store.Files[name]=image.ToByteArray(MagickFormat.Png);
            project.Layers.Add(new(){ObjectKey=name,LayerType=type,LayerOrder=type==LayerType.Background?0:1});
        }
        db.Add(project);await db.SaveChangesAsync();var service=new WallpaperService(db,store,store);
        var request=new CreateWallpaperRequest(project.Id,"Edited layers",WallpaperMode.Live2D,MouseEnabled:true,GyroEnabled:true,Render:new(40,"mono","square",2,true));
        var asset=await service.CreateAsync(project.UserId,request,default);
        var stored=JsonSerializer.Deserialize<WallpaperManifest>((await db.WallpaperAssets.FindAsync(asset.Id))!.ManifestJson)!;
        Assert.Equal(16,stored.Width);Assert.Equal(16,stored.Height);Assert.Equal(2,stored.Files.Count);
        Assert.True(stored.MouseEnabled);Assert.True(stored.GyroEnabled);
        using var foreground=new MagickImage(store.Files[stored.Files[1].Source]);Assert.True(foreground.HasAlpha);Assert.Equal(16u,foreground.Width);
        var raw=foreground.ToByteArray(MagickFormat.Rgba);Assert.All(raw.Where((_,i)=>i%4==3),alpha=>Assert.Equal(0,alpha));
        Assert.Equal(.224,stored.Files[1].MovementX,6);Assert.Equal(0,stored.Files[0].MovementX);
        Assert.Equal(0,project.Layers.Last().MovementX);
        var gyroOnly=await service.SnapshotAsync(project.UserId,asset.Id,new(true,40,false,true,new(40,"warm","original",1,true)),default);
        Assert.Equal("original",gyroOnly.Edit!.Filter);Assert.Equal("original",gyroOnly.Edit.Crop);
        var manifest=await service.GetManifestAsync(project.UserId,gyroOnly.Id,false,default);
        Assert.False(manifest.MouseEnabled);Assert.True(manifest.GyroEnabled);Assert.True(manifest.MotionEnabled);
        Assert.True((await service.GetManifestAsync(project.UserId,asset.Id,false,default)).MouseEnabled);
    }
    [Fact]
    public async Task ExistingSavedMouseSelectionIsDeliveredWithoutRewritingItsImmutableManifest()
    {
        await using var db=WorkflowData.Db();var store=new WorkflowStore();
        var id=Guid.NewGuid();var user=Guid.NewGuid();var original=JsonSerializer.Serialize(new WallpaperManifest(1,id,WallpaperMode.Live2D,10,10,[new("bg","image/png",0,"Background"),new("fg","image/png",1,"Body",MovementX:4)]));
        var asset=new WallpaperAsset{Id=id,UserId=user,Mode=WallpaperMode.Live2D,ManifestJson=original,SavedEditJson=new ImageEditState(70,"original","original",1,true,DateTime.UtcNow,MouseEnabled:true).ToJson()};db.Add(asset);await db.SaveChangesAsync();
        var manifest=await new WallpaperService(db,store,store).GetManifestAsync(user,id,true,default);
        Assert.True(manifest.MouseEnabled);Assert.False(manifest.GyroEnabled);Assert.Equal(original,asset.ManifestJson);
    }
    [Fact]
    public async Task UneditedHighResolutionLayersKeepActualDimensionsAndZeroStrengthStaysStill()
    {
        await using var db=WorkflowData.Db();var store=new WorkflowStore();var p=WorkflowData.Project(Guid.NewGuid());p.OriginalImage.OriginalWidth=6000;p.OriginalImage.OriginalHeight=4000;
        foreach(var (type,key) in new[]{(LayerType.Background,"bg"),(LayerType.Body,"fg")}){store.Files[key]=[1];p.Layers.Add(new(){ObjectKey=key,LayerType=type,LayerOrder=type==LayerType.Background?0:1});}
        db.Add(p);await db.SaveChangesAsync();var service=new WallpaperService(db,store,store);
        var asset=await service.CreateAsync(p.UserId,new(p.Id,"Large",WallpaperMode.Live2D,MouseEnabled:true,Render:new(0,"original","original",1,true)),default);
        var manifest=await service.GetManifestAsync(p.UserId,asset.Id,false,default);Assert.Equal(6000,manifest.Width);Assert.Equal(4000,manifest.Height);Assert.All(manifest.Files,f=>Assert.Equal(0,f.MovementX));
    }
}
