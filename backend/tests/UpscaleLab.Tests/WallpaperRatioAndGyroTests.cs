using System.Text.Json;
using ImageMagick;
using UpscaleLab.Application.Images;
using UpscaleLab.Application.Wallpapers;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Wallpapers;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class WallpaperRatioAndGyroTests
{
    [Fact]
    public async Task CharacterOnlyLayersKeepLive2DMotionAndCropAllPartsEqually()
    {
        await using var db = WorkflowData.Db();
        var store = new WorkflowStore();
        var project = WorkflowData.Project(Guid.NewGuid());
        project.OriginalImage.OriginalWidth = 400;
        project.OriginalImage.OriginalHeight = 300;
        var types = new[] { LayerType.Face, LayerType.FrontHair };
        for (var i = 0; i < types.Length; i++)
        {
            using var image = new MagickImage(MagickColors.Transparent, 400, 300);
            store.Files[$"part{i}"] = image.ToByteArray(MagickFormat.Png);
            project.Layers.Add(new() { ObjectKey = $"part{i}", LayerType = types[i], LayerOrder = i });
        }
        project.SavedEditJson = new ImageEditState(70, "original", "portrait", 1, true,
            DateTime.UtcNow, MouseEnabled: true, GyroEnabled: true,
            Rig: new HeadRigSettings(true, 60, 35, 55, 25, 40, false)).ToJson();
        db.Add(project);
        await db.SaveChangesAsync();
        var service = new WallpaperService(db, store, store);
        var asset = await service.CreateAsync(project.UserId, new(project.Id, "Phone", WallpaperMode.Live2D,
            MotionEnabled: true, MouseEnabled: true, GyroEnabled: true,
            Render: new(70, "original", "portrait", 1, true)), default);
        var manifest = JsonSerializer.Deserialize<WallpaperManifest>((await db.WallpaperAssets.FindAsync(asset.Id))!.ManifestJson)!;
        Assert.Equal(WallpaperMode.Live2D, manifest.Mode);
        Assert.Equal(2, manifest.Files.Count);
        Assert.True(manifest.GyroEnabled);
        Assert.True(manifest.MouseEnabled);
        Assert.Equal(169, manifest.Width);
        Assert.Equal(300, manifest.Height);
        // Crop starts at x=116. The original pivot at 25%=100px is outside the new
        // crop and is clipped to the nearest valid edge, rather than staying at 25%.
        Assert.Equal(0, manifest.Rig?.PivotX);
        Assert.Equal(40, manifest.Rig?.PivotY);
        foreach (var file in manifest.Files)
        {
            using var image = new MagickImage(store.Files[file.Source]);
            Assert.Equal(169u, image.Width);
            Assert.Equal(300u, image.Height);
        }

        var framed = await service.SnapshotAsync(project.UserId, asset.Id, new(true, 70, true, true,
            new(70, "original", "original", 1, true, Frame: new(160, 90, true, 1, 1, .5))), default);
        var framedManifest = JsonSerializer.Deserialize<WallpaperManifest>((await db.WallpaperAssets.FindAsync(framed.Id))!.ManifestJson)!;
        Assert.Equal((160, 90), (framedManifest.Width, framedManifest.Height));
        Assert.True(framedManifest.GyroEnabled);
        Assert.True(framedManifest.MouseEnabled);
        Assert.Equal(68, framedManifest.Rig?.PivotX);
        Assert.Equal(40, framedManifest.Rig?.PivotY);
        Assert.Equal(types.Select(type => type.ToString()), framedManifest.Files.Select(file => file.Role));
        foreach (var file in framedManifest.Files)
        {
            using var image = new MagickImage(store.Files[file.Source]);
            Assert.Equal((160u, 90u), (image.Width, image.Height));
            using var pixels = image.GetPixels();
            Assert.Equal(0, pixels.GetPixel(0, 0).ToColor()!.A);
        }
    }

    [Fact]
    public async Task SquareCropRebasesHeadAndBodyPivots()
    {
        await using var db = WorkflowData.Db();
        var store = new WorkflowStore();
        var project = WorkflowData.Project(Guid.NewGuid());
        project.OriginalImage.OriginalWidth = 400;
        project.OriginalImage.OriginalHeight = 300;
        foreach (var (kind, name, order) in new[] { (LayerType.Background,"bg",0), (LayerType.Body,"body",1) })
        {
            using var image = new MagickImage(MagickColors.Transparent, 400, 300);
            store.Files[name] = image.ToByteArray(MagickFormat.Png);
            project.Layers.Add(new() { ObjectKey = name, LayerType = kind, LayerOrder = order });
        }
        project.SavedEditJson = new ImageEditState(70, "original", "original", 1, true,
            DateTime.UtcNow, Rig: new HeadRigSettings(true, 60, 35, 55, 50, 20, false,
                TorsoPivotY: 40, LowerPivotY: 70)).ToJson();
        db.Add(project);
        await db.SaveChangesAsync();
        var asset = await new WallpaperService(db, store, store).CreateAsync(project.UserId,
            new(project.Id, "Square", WallpaperMode.Live2D, Render: new(40,"original","square",1,true)), default);
        var manifest = JsonSerializer.Deserialize<WallpaperManifest>((await db.WallpaperAssets.FindAsync(asset.Id))!.ManifestJson)!;
        Assert.Equal(300, manifest.Width);
        Assert.Equal(300, manifest.Height);
        Assert.Equal(50, manifest.Rig?.PivotX);
        Assert.Equal(20, manifest.Rig?.PivotY);
        Assert.Equal(40, manifest.Rig?.TorsoPivotY);
        Assert.Equal(70, manifest.Rig?.LowerPivotY);
    }
}
