using Microsoft.EntityFrameworkCore;
using UpscaleLab.Application.Images;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Projects;
using UpscaleLab.Infrastructure.Wallpapers;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class ProjectFeatureTests
{
    [Theory]
    [InlineData(ProcessingOperation.CharacterLayers, false, "Live 2D")]
    [InlineData(ProcessingOperation.CharacterLayers, true, "Live 2D")]
    [InlineData(ProcessingOperation.Layers, true, "배경 분리")]
    public async Task ListAndDetail_ClassifyCurrentCompletedLayerOperation(
        ProcessingOperation operation, bool hasBackground, string expected)
    {
        await using var db = WorkflowData.Db();
        var project = WorkflowData.Project(Guid.NewGuid());
        project.Layers.Add(new ImageLayer { LayerType = hasBackground ? LayerType.Background : LayerType.BackPart,
            LayerOrder = 0, ObjectKey = "back.png" });
        project.Layers.Add(new ImageLayer { LayerType = LayerType.Face, LayerOrder = 1, ObjectKey = "face.png" });
        project.ProcessingJobs.Add(new ProcessingJob { Operation = operation, Status = ProcessingJobStatus.Completed });
        project.SavedEditJson = new ImageEditState(50, "original", "original", 1, true, DateTime.UtcNow,
            LiveEnabled: false).ToJson();
        db.Add(project);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = new ProjectService(db, new WorkflowStore());

        Assert.Equal(new[] { expected }, (await service.GetAsync(project.UserId, project.Id, default)).Features);
        Assert.Equal(new[] { expected }, Assert.Single(await service.GetAllAsync(project.UserId, default)).Features);
    }

    [Fact]
    public async Task StaticCharacterExport_KeepsLive2DFeatureAfterReload()
    {
        await using var db = WorkflowData.Db();
        var store = new WorkflowStore();
        var project = WorkflowData.Project(Guid.NewGuid());
        store.Files["source.png"] = [1, 2];
        project.Layers.Add(new ImageLayer { LayerType = LayerType.BackPart, LayerOrder = 0, ObjectKey = "back.png" });
        project.Layers.Add(new ImageLayer { LayerType = LayerType.Face, LayerOrder = 1, ObjectKey = "face.png" });
        db.Add(project);
        await db.SaveChangesAsync();
        var service = new WallpaperService(db, store, store);

        var asset = await service.CreateAsync(project.UserId,
            new(project.Id, "Character", WallpaperMode.Static, MotionEnabled: false), default);
        Assert.Equal(new[] { "Live 2D" }, asset.Features);
        db.ChangeTracker.Clear();
        Assert.Equal(new[] { "Live 2D" }, Assert.Single(await service.ListAsync(project.UserId, false, default)).Features);
    }

    [Fact]
    public async Task NewPhotoSeparation_ReplacesEarlierCharacterClassification()
    {
        await using var db = WorkflowData.Db();
        var project = WorkflowData.Project(Guid.NewGuid());
        project.Layers.Add(new ImageLayer { LayerType = LayerType.Background, LayerOrder = 0, ObjectKey = "back.png" });
        project.Layers.Add(new ImageLayer { LayerType = LayerType.Body, LayerOrder = 1, ObjectKey = "body.png" });
        project.ProcessingJobs.Add(new ProcessingJob { Operation = ProcessingOperation.CharacterLayers,
            Status = ProcessingJobStatus.Completed, CompletedAt = DateTime.UtcNow.AddMinutes(-2) });
        project.ProcessingJobs.Add(new ProcessingJob { Operation = ProcessingOperation.Layers,
            Status = ProcessingJobStatus.Completed, CompletedAt = DateTime.UtcNow });
        db.Add(project);
        await db.SaveChangesAsync();

        var detail = await new ProjectService(db, new WorkflowStore()).GetAsync(project.UserId, project.Id, default);
        Assert.Equal(new[] { "배경 분리" }, detail.Features);
    }
}
