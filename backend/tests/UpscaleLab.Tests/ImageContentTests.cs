using UpscaleLab.Application.Common;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Projects;
using UpscaleLab.Infrastructure.Wallpapers;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class ImageContentTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Theory]
    [InlineData("original")]
    [InlineData("job")]
    [InlineData("layer")]
    public async Task ProjectContentReadsOnlyTheOwnedProjectOriginalOrSuccessfulResultOrLayer(string kind)
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore();
        var project = WorkflowData.Project(Guid.NewGuid());
        var other = WorkflowData.Project(Guid.NewGuid());
        var layer = new ImageLayer { Project = project, ObjectKey = "layer.png", LayerType = LayerType.Background };
        var job = new ProcessingJob { Project = project, Status = ProcessingJobStatus.Completed, ResultObjectKey = "result.png" };
        var failed = new ProcessingJob { Project = project, Status = ProcessingJobStatus.Failed, ResultObjectKey = "failed.png" };
        var foreignLayer = new ImageLayer { Project = other, ObjectKey = "foreign.png", LayerType = LayerType.Background };
        db.AddRange(project, other, layer, job, failed, foreignLayer); await db.SaveChangesAsync();
        store.Files["source.png"] = [1, 2, 3]; store.Files["layer.png"] = [4, 5, 6]; store.Files["result.png"] = [7, 8, 9];
        var service = new ProjectService(db, store);
        var jobId = kind == "job" ? job.Id : (Guid?)null;
        var layerId = kind == "layer" ? layer.Id : (Guid?)null;
        var content = await service.ReadContentAsync(project.UserId, project.Id, jobId, layerId, Ct);
        Assert.Equal(store.Files[kind == "job" ? "result.png" : kind == "layer" ? "layer.png" : "source.png"], content.Bytes);
        Assert.Equal("image/png", content.ContentType);
        await Assert.ThrowsAsync<NotFoundException>(() => service.ReadContentAsync(other.UserId, project.Id, jobId, layerId, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => service.ReadContentAsync(project.UserId, project.Id, null, foreignLayer.Id, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => service.ReadContentAsync(project.UserId, project.Id, failed.Id, null, Ct));
        await Assert.ThrowsAsync<ValidationException>(() => service.ReadContentAsync(project.UserId, project.Id, job.Id, layer.Id, Ct));
    }

    [Fact]
    public async Task WallpaperContentHonorsOwnershipPublicationTheShareTokenAndArchiving()
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore(); store.Files["source.png"] = [1, 2, 3];
        var project = WorkflowData.Project(Guid.NewGuid()); db.Add(project); await db.SaveChangesAsync();
        var service = new WallpaperService(db, store, store);
        var asset = await service.CreateAsync(project.UserId, new(project.Id, "Private", WallpaperMode.Static), Ct);
        var otherUser = Guid.NewGuid();
        Assert.Equal(new byte[] { 1, 2, 3 }, (await service.ReadContentAsync(project.UserId, asset.Id, 0, Ct)).Bytes);
        await Assert.ThrowsAsync<NotFoundException>(() => service.ReadContentAsync(otherUser, asset.Id, 0, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => service.ReadContentAsync(project.UserId, asset.Id, 99, Ct));
        var link = await service.CreateLinkAsync(project.UserId, asset.Id, Ct);
        Assert.Equal(new byte[] { 1, 2, 3 }, (await service.ReadLinkedContentAsync(link.Token, 0, Ct)).Bytes);
        await Assert.ThrowsAsync<NotFoundException>(() => service.ReadLinkedContentAsync(new string('0', 64), 0, Ct));
        await service.SetSharingAsync(project.UserId, asset.Id, true, Ct);
        Assert.Equal(new byte[] { 1, 2, 3 }, (await service.ReadContentAsync(otherUser, asset.Id, 0, Ct)).Bytes);
        await service.ArchiveAsync(project.UserId, asset.Id, Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => service.ReadContentAsync(project.UserId, asset.Id, 0, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => service.ReadLinkedContentAsync(link.Token, 0, Ct));
    }
}
