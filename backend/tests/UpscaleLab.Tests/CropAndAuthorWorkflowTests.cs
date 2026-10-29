using ImageMagick;
using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Images;
using UpscaleLab.Application.Wallpapers;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Gallery;
using UpscaleLab.Infrastructure.Projects;
using UpscaleLab.Infrastructure.Wallpapers;
using Xunit;
namespace UpscaleLab.Tests;
public sealed class CropAndAuthorWorkflowTests
{
    [Fact]
    public async Task CropSaveReloadAndLayeredSnapshotUseTheSelectedPixelsOnce()
    {
        await using var db=WorkflowData.Db();var store=new WorkflowStore();var owner=new User{Email="crop@example.invalid",Username="crop",IsEmailVerified=true};
        var project=WorkflowData.Project(owner.Id);project.OriginalImage.OriginalWidth=40;project.OriginalImage.OriginalHeight=30;
        project.Layers.Add(new ImageLayer{Project=project,LayerType=LayerType.Background,LayerOrder=0,ObjectKey="bg.png"});
        project.Layers.Add(new ImageLayer{Project=project,LayerType=LayerType.Body,LayerOrder=1,ObjectKey="fg.png"});
        using var image=new MagickImage(MagickColors.Red,40,30);using var right=new MagickImage(MagickColors.Blue,20,30);image.Composite(right,20,0,CompositeOperator.Over);
        store.Files["bg.png"]=image.ToByteArray(MagickFormat.Png);using var fg=new MagickImage(MagickColors.Transparent,40,30);store.Files["fg.png"]=fg.ToByteArray(MagickFormat.Png);
        db.AddRange(owner,project);await db.SaveChangesAsync();var region=new ImageCropRegion(.5,0,.5,1);
        var state=await new ProjectService(db,store).SaveEditAsync(owner.Id,project.Id,new(35,"original","custom",1,true,CropRegion:region),default);
        db.ChangeTracker.Clear();Assert.Equal(region,(await new ProjectService(db,store).GetAsync(owner.Id,project.Id,default)).Edit!.CropRegion);
        var service=new WallpaperService(db,store,store);
        var asset=await service.CreateAsync(owner.Id,new(project.Id,"Crop",WallpaperMode.Live2D,Render:new(35,"original","custom",1,true,region)),default);
        var manifest=(await db.WallpaperAssets.SingleAsync(x=>x.Id==asset.Id)).ManifestJson;
        using var json=System.Text.Json.JsonDocument.Parse(manifest);Assert.Equal(20,json.RootElement.GetProperty("Width").GetInt32());Assert.Equal(30,json.RootElement.GetProperty("Height").GetInt32());
        var file=json.RootElement.GetProperty("Files")[0].GetProperty("Source").GetString()!;using var output=new MagickImage(store.Files[file]);
        Assert.Equal(20u,output.Width);Assert.Equal(30u,output.Height);using var pixels=output.GetPixels();Assert.Equal(MagickColors.Blue.ToString(),pixels.GetPixel(0,0).ToColor()!.ToString());
        Assert.Null(asset.Edit!.CropRegion);Assert.Equal("original",asset.Edit.Crop);
    }
    [Fact]
    public async Task ExploreResponsesUseCurrentProfilePhotoAndPreserveVisibility()
    {
        await using var db=WorkflowData.Db();var store=new WorkflowStore();var owner=new User{Email="author@example.invalid",Username="작성자",ProfileImageObjectKey="profile-first.png"};
        var image=new UpscaleLab.Domain.Entities.Image{User=owner,OriginalObjectKey="photo.png",FileName="photo.png",ContentType="image/png"};
        var post=new GalleryPost{User=owner,Image=image,Title="Public",IsPublic=true};var hidden=new GalleryPost{User=owner,Image=image,Title="Hidden",IsPublic=false};
        var asset=new WallpaperAsset{User=owner,Title="Shared",IsPublic=true,ManifestJson="{\"Files\":[]}"};db.AddRange(post,hidden,asset);await db.SaveChangesAsync();
        var gallery=new GalleryService(db,store);var wallpapers=new WallpaperService(db,store,store);
        var result=Assert.Single(await gallery.GetAllAsync(null,null,null,default));Assert.Contains("profile-first.png",result.ProfileImageUrl);Assert.Equal("작성자",result.Username);
        var wallpaper=Assert.Single(await wallpapers.ListAsync(null,true,default));Assert.Equal(owner.Id,wallpaper.UserId);Assert.Contains("profile-first.png",wallpaper.ProfileImageUrl);
        owner.ProfileImageObjectKey="profile-second.png";await db.SaveChangesAsync();Assert.Contains("profile-second.png",(await gallery.GetAsync(post.Id,null,default)).Post.ProfileImageUrl);
        owner.ProfileImageObjectKey=null;await db.SaveChangesAsync();Assert.Null(Assert.Single(await gallery.GetAllAsync(null,null,null,default)).ProfileImageUrl);
        Assert.Null(Assert.Single(await wallpapers.ListAsync(null,true,default)).ProfileImageUrl);
    }
}
