using Microsoft.EntityFrameworkCore;
using ImageMagick;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Projects;
using UpscaleLab.Application.Storage;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Database;
using UpscaleLab.Infrastructure.Projects;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class ProjectServiceTests
{
    [Fact]
    public async Task LibraryAndDetailKeepTheLatestSuccessfulUpscaleAfterReloadOrALaterFailure()
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore();
        var project = WorkflowData.Project(Guid.NewGuid());
        var completed = new ProcessingJob { Project = project, Operation = ProcessingOperation.Upscale,
            Status = ProcessingJobStatus.Completed, ResultObjectKey = "verified-4k.png", ResultWidth = 3840,
            ResultHeight = 2160, TargetLongEdge = 3840, CreatedAt = DateTime.UtcNow.AddMinutes(-2) };
        var failed = new ProcessingJob { Project = project, Operation = ProcessingOperation.Upscale,
            Status = ProcessingJobStatus.Failed, CreatedAt = DateTime.UtcNow.AddMinutes(-1) };
        project.ProcessingJobs.Add(completed);
        db.Add(project); await db.SaveChangesAsync();
        db.ProcessingJobs.Add(failed);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var service = new ProjectService(db, store);
        var summary = Assert.Single(await service.GetAllAsync(project.UserId, CancellationToken.None));
        Assert.Equal(completed.Id, summary.ImageResult!.JobId);
        Assert.Equal(3840, summary.ImageResult.Width);
        Assert.Equal(2160, summary.ImageResult.Height);
        Assert.Contains("verified-4k.png", summary.ImageResult.ImageUrl);
        Assert.Contains("source.png", summary.OriginalImageUrl);
        var detail = await service.GetAsync(project.UserId, project.Id, CancellationToken.None);
        Assert.Equal(completed.Id, detail.ImageResult!.JobId);
        Assert.Equal(failed.Id, detail.LatestJob!.JobId);
    }

    [Fact]
    public async Task Create_ReusesImageMetadataAndUsesProjectS3Prefix()
    {
        await using var dbContext = CreateDbContext();
        var storage = new FakeStorageService();
        var service = new ProjectService(dbContext, storage);
        var userId = Guid.NewGuid();
        using var fixture = new MagickImage(MagickColors.Blue, 1280, 720);
        await using var stream = new MemoryStream(fixture.ToByteArray(MagickFormat.Png));

        var result = await service.CreateAsync(
            userId,
            new CreateProjectCommand(stream, "character.png", "image/png", "My Layer", 1280, 720),
            CancellationToken.None);

        var project = await dbContext.LiveLayerProjects.Include(x => x.OriginalImage).SingleAsync();
        Assert.Equal(result.Id, project.Id);
        Assert.Equal(ProjectStatus.Uploaded, project.Status);
        Assert.Equal("character.png", project.OriginalImage.FileName);
        Assert.Contains($"users/{userId:N}/projects/{project.Id:N}/original/", project.OriginalImage.OriginalObjectKey);
        var info = new MagickImageInfo(storage.Objects[project.OriginalImage.OriginalObjectKey]);
        Assert.Equal(1280u, info.Width);
        Assert.Equal(720u, info.Height);
        Assert.Equal("image/png", result.OriginalContentType);
        Assert.Equal("image/png", (await service.GetAsync(userId, project.Id, CancellationToken.None)).OriginalContentType);
        Assert.Equal("image/png", (await service.GetAllAsync(userId, CancellationToken.None)).Single().OriginalContentType);
    }

    [Fact]
    public async Task UpdateLayer_DoesNotExposeAnotherUsersProject()
    {
        await using var dbContext = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var project = new LiveLayerProject
        {
            UserId = ownerId,
            Title = "Owner project",
            OriginalImage = new Image
            {
                UserId = ownerId,
                OriginalUrl = "s3://test/original.png",
                OriginalObjectKey = "original.png",
                OriginalWidth = 10,
                OriginalHeight = 10,
                FileName = "original.png",
                ContentType = "image/png"
            }
        };
        var layer = new ImageLayer
        {
            Project = project,
            LayerOrder = 0,
            LayerType = LayerType.Body,
            ImageUrl = "s3://test/layer.png",
            ObjectKey = "layer.png",
            Scale = 1
        };
        dbContext.ImageLayers.Add(layer);
        await dbContext.SaveChangesAsync();
        var service = new ProjectService(dbContext, new FakeStorageService());

        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateLayerAsync(
            Guid.NewGuid(),
            project.Id,
            layer.Id,
            new UpdateLayerRequest(1, null, null, null, null, null, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task ReorderLayers_StoresBottomToTopOrderAndRetainsOriginalAutomaticOrder()
    {
        await using var db = CreateDbContext();
        var owner = Guid.NewGuid();
        var project = WorkflowData.Project(owner);
        var back = new ImageLayer { Project = project, ObjectKey = "users/u/projects/p/layers/000_a.png", LayerType = LayerType.Background, LayerOrder = 0 };
        var front = new ImageLayer { Project = project, ObjectKey = "users/u/projects/p/layers/001_b.png", LayerType = LayerType.Body, LayerOrder = 1 };
        var eyes = new ImageLayer { Project = project, ObjectKey = "users/u/projects/p/layers/002_c.png", LayerType = LayerType.Face, LayerOrder = 2 };
        db.Add(project);
        db.ImageLayers.AddRange(back, front, eyes);
        await db.SaveChangesAsync();
        var service = new ProjectService(db, new FakeStorageService());

        var saved = await service.ReorderLayersAsync(owner, project.Id,
            new ReorderProjectLayersRequest(new[] { back.Id, eyes.Id, front.Id }), CancellationToken.None);
        Assert.Equal(new[] { back.Id, eyes.Id, front.Id }, saved.Select(x => x.Id));
        Assert.Equal(new[] { 0, 1, 2 }, saved.Select(x => x.LayerOrder));
        Assert.Equal(new[] { 0, 2, 1 }, saved.Select(x => x.AutomaticOrder ?? -1));
        var read = await service.GetLayersAsync(owner, project.Id, CancellationToken.None);
        Assert.Equal(new[] { back.Id, eyes.Id, front.Id }, read.Select(x => x.Id));
    }

    [Fact]
    public async Task ReorderLayers_RejectsMissingDuplicateAndForeignLayerIds()
    {
        await using var db = CreateDbContext();
        var owner = Guid.NewGuid();
        var project = WorkflowData.Project(owner);
        var first = new ImageLayer { Project = project, ObjectKey = "first.png", LayerOrder = 0 };
        var second = new ImageLayer { Project = project, ObjectKey = "second.png", LayerOrder = 1 };
        db.Add(project);
        db.ImageLayers.AddRange(first, second);
        await db.SaveChangesAsync();
        var service = new ProjectService(db, new FakeStorageService());
        await Assert.ThrowsAsync<ValidationException>(() => service.ReorderLayersAsync(owner,
            project.Id, new ReorderProjectLayersRequest(new[] { first.Id }), CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => service.ReorderLayersAsync(owner,
            project.Id, new ReorderProjectLayersRequest(new[] { first.Id, first.Id }), CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => service.ReorderLayersAsync(owner,
            project.Id, new ReorderProjectLayersRequest(new[] { first.Id, Guid.NewGuid() }), CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => service.ReorderLayersAsync(Guid.NewGuid(),
            project.Id, new ReorderProjectLayersRequest(new[] { second.Id, first.Id }), CancellationToken.None));
        Assert.Equal(0, first.LayerOrder);
        Assert.Equal(1, second.LayerOrder);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class FakeStorageService : IStorageService
    {
        public Dictionary<string, byte[]> Objects { get; } = [];

        public async Task<StoredObject> UploadAsync(
            Stream content,
            string objectKey,
            string contentType,
            CancellationToken cancellationToken)
        {
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            Objects[objectKey] = memory.ToArray();
            return new StoredObject(objectKey, $"s3://test/{objectKey}");
        }

        public Task DownloadAsync(string objectKey, Stream destination, CancellationToken cancellationToken) =>
            destination.WriteAsync(Objects[objectKey], cancellationToken).AsTask();

        public Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
        {
            Objects.Remove(objectKey);
            return Task.CompletedTask;
        }

    public string CreateUploadUrl(
        string objectKey,
        string contentType,
        TimeSpan lifetime)
    {
        throw new NotSupportedException(
            "Upload URL is not required in this test.");
    }
        public string CreateDownloadUrl(string objectKey, TimeSpan lifetime) =>
            $"https://example.test/{objectKey}";
    }
}
