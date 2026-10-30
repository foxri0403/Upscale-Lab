using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Images;
using UpscaleLab.Application.Processing;
using UpscaleLab.Application.Projects;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Images;
using UpscaleLab.Infrastructure.Processing;
using UpscaleLab.Infrastructure.Projects;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class ProjectDraftTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly SaveImageEditRequest Edit = new(70, "original", "original", 1, true);
    private static MemoryStream Photo()
    {
        var stream = new MemoryStream(); using var image = new MagickImage(MagickColors.Blue, 10, 10);
        image.Write(stream, MagickFormat.Png); stream.Position = 0; return stream;
    }

    [Fact]
    public async Task UploadIsTemporaryUntilExplicitSaveAndStaleDiscardCannotDeleteSavedImage()
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore(); var user = Guid.NewGuid();
        var service = new ProjectService(db, store); using var photo = Photo();
        var draft = await service.CreateAsync(user, new(photo, "draft.png", "image/png", "Draft", 10, 10, IsDraft: true), Ct);
        Assert.True(draft.IsDraft); Assert.Null(draft.Edit);
        Assert.Empty(await service.GetAllAsync(user, Ct));
        Assert.Empty(await new ImageService(db, store).GetAllAsync(user, Ct));
        Assert.True((await service.GetAsync(user, draft.Id, Ct)).IsDraft);
        await service.SaveEditAsync(user, draft.Id, Edit, Ct);
        Assert.False(Assert.Single(await service.GetAllAsync(user, Ct)).IsDraft);
        Assert.Single(await new ImageService(db, store).GetAllAsync(user, Ct));
        await service.DiscardDraftAsync(user, draft.Id, Ct);
        Assert.Equal(draft.Id, Assert.Single(await service.GetAllAsync(user, Ct)).Id);
        Assert.Single(store.Files);
    }

    [Fact]
    public async Task DraftDiscardChecksOwnerAndPreservesLegacyRegisteredImages()
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore(); var user = Guid.NewGuid();
        var service = new ProjectService(db, store); using var first = Photo(); using var second = Photo();
        var saved = await service.CreateAsync(user, new(first, "old.png", "image/png", "Existing", 10, 10), Ct);
        var draft = await service.CreateAsync(user, new(second, "new.png", "image/png", "Draft", 10, 10, true), Ct);
        await Assert.ThrowsAsync<NotFoundException>(() => service.DiscardDraftAsync(Guid.NewGuid(), draft.Id, Ct));
        Assert.Equal(saved.Id, Assert.Single(await service.GetAllAsync(user, Ct)).Id);
        await service.DiscardDraftAsync(user, draft.Id, Ct);
        await service.DiscardDraftAsync(user, draft.Id, Ct);
        Assert.Equal(saved.Id, Assert.Single(await service.GetAllAsync(user, Ct)).Id);
        Assert.Single(await db.Images.ToListAsync()); Assert.Single(store.Files);
    }

    [Fact]
    public async Task LeavingBeforeQueuedJobStartsDiscardsWithoutRunningGpu()
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore(); store.Files["source.png"] = [1, 2, 3];
        var project = WorkflowData.Project(Guid.NewGuid()); project.SavedEditJson = ProjectDraftState.Pending;
        var job = new ProcessingJob { Project = project, Status = ProcessingJobStatus.Queued };
        db.Add(job); await db.SaveChangesAsync();
        await new ProjectService(db, store).DiscardDraftAsync(project.UserId, project.Id, Ct);
        var processor = new Processor(() => throw new Exception("Discarded job must not run"));
        await new ProjectProcessingService(db, WorkflowData.Factory, store, processor, new Queue(), new FakeTokenService(), NullLogger<ProjectProcessingService>.Instance).ExecuteAsync(job.Id, Ct);
        Assert.Equal(0, processor.Calls); Assert.Empty(await db.LiveLayerProjects.ToListAsync()); Assert.Empty(store.Files);
    }

    [Fact]
    public async Task LeavingDuringProcessingKeepsInputUntilCompletionThenRemovesTemporaryResults()
    {
        await using var db = WorkflowData.Db(); var store = new WorkflowStore(); using var photo = Photo(); store.Files["source.png"] = photo.ToArray();
        var project = WorkflowData.Project(Guid.NewGuid()); project.SavedEditJson = ProjectDraftState.Pending;
        var job = new ProcessingJob { Project = project, Operation = ProcessingOperation.Upscale, UpscaleFactor = 2 };
        db.Add(job); await db.SaveChangesAsync(); var service = new ProjectService(db, store);
        var processor = new Processor(async () => {
            await service.DiscardDraftAsync(project.UserId, project.Id, Ct);
            Assert.True(store.Files.ContainsKey("source.png"));
            Assert.Empty(await service.GetAllAsync(project.UserId, Ct));
            await Assert.ThrowsAsync<NotFoundException>(() => service.SaveEditAsync(project.UserId, project.Id, Edit, Ct));
        });
        await new ProjectProcessingService(db, WorkflowData.Factory, store, processor, new Queue(), new FakeTokenService(), NullLogger<ProjectProcessingService>.Instance).ExecuteAsync(job.Id, Ct);
        Assert.Equal(1, processor.Calls); Assert.Empty(await db.LiveLayerProjects.ToListAsync());
        Assert.Empty(await db.Images.ToListAsync()); Assert.Empty(store.Files);
    }

    private sealed class Processor(Func<Task> action) : IProjectImageProcessor
    {
        public int Calls;
        public bool Supports(ProcessingOperation operation) => true;
        public async Task<ProjectProcessingResult> ProcessAsync(ProjectProcessingRequest request, CancellationToken ct)
        {
            Calls++; await action(); var path = Path.Combine(request.OutputDirectory, "result.png");
            using var image = new MagickImage(MagickColors.Blue, 20, 20); image.Write(path, MagickFormat.Png);
            return new([], path, 20, 20);
        }
    }
    private sealed class Queue : IProcessingJobQueue
    {
        public ValueTask EnqueueAsync(Guid id, CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask<Guid> DequeueAsync(CancellationToken ct) => ValueTask.FromResult(Guid.Empty);
    }
}
