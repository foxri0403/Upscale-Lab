using Microsoft.Extensions.Logging;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Processing;
using UpscaleLab.Application.Upscaling;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Processing;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class ProjectProcessingServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static void Original(WorkflowStore store, uint width = 10, uint height = 10)
    {
        using var image = new MagickImage(MagickColors.Blue, width, height);
        store.Files["source.png"] = image.ToByteArray(MagickFormat.Png);
    }

    [Fact]
    public async Task Upscale_PersistsVerifiedResultAndPreservesExistingLayers()
    {
        await using var db = WorkflowData.Db();
        var project = WorkflowData.Project(Guid.NewGuid());
        project.Layers.Add(new ImageLayer { ObjectKey = "existing.png", LayerType = LayerType.Background });
        db.Add(project); await db.SaveChangesAsync();
        var store = new WorkflowStore(); Original(store);
        var processor = new TestProcessor();
        var service = new ProjectProcessingService(db, store, processor, new ProcessingJobQueue(), new ConsoleErrorLogger<ProjectProcessingService>(), new FakeTokenService());
        var started = await service.StartAsync(project.UserId, project.Id, new(ProcessingOperation.Upscale, 2), Ct);
        await service.ExecuteAsync(started.JobId, Ct);
        var status = await service.GetStatusAsync(project.UserId, project.Id, Ct);
        Assert.True(
            status.Status == ProcessingJobStatus.Completed,
            $"작업 상태: {status.Status}, 오류: {status.ErrorMessage}");
        Assert.Equal(20, status.ResultWidth);
        Assert.True(status.HasImageResult);
        Assert.Equal("existing.png", (await db.ImageLayers.SingleAsync()).ObjectKey);
        Assert.NotEmpty((await service.GetResultUrlAsync(project.UserId, project.Id, started.JobId, Ct)).Url);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetResultUrlAsync(Guid.NewGuid(), project.Id, started.JobId, Ct));
    }

    [Fact]
    public async Task InvalidCloudOutput_FailsWithoutReplacingPreviousLayersOrLeavingResultFiles()
    {
        await using var db = WorkflowData.Db();
        var project = WorkflowData.Project(Guid.NewGuid());
        project.Layers.Add(new ImageLayer { ObjectKey = "existing.png", LayerType = LayerType.Background });
        db.Add(project); await db.SaveChangesAsync();
        var store = new WorkflowStore(); Original(store);
        var processor = new TestProcessor { WrongDimensions = true };
        var service = new ProjectProcessingService(db, store, processor, new ProcessingJobQueue(), new ConsoleErrorLogger<ProjectProcessingService>(), new FakeTokenService());
        var job = await service.StartAsync(project.UserId, project.Id, new(ProcessingOperation.Upscale, 2), Ct);
        await service.ExecuteAsync(job.JobId, Ct);
        var result = await db.ProcessingJobs.SingleAsync();
        Assert.Equal(ProcessingJobStatus.Failed, result.Status);
        Assert.Null(result.ResultObjectKey);
        Assert.Equal("existing.png", (await db.ImageLayers.SingleAsync()).ObjectKey);
        Assert.Single(store.Files);
    }

    [Fact]
    public async Task UnconnectedCloudProvider_RejectsBeforeCreatingAJob()
    {
        await using var db = WorkflowData.Db();
        var project = WorkflowData.Project(Guid.NewGuid()); db.Add(project); await db.SaveChangesAsync();
        var service = new ProjectProcessingService(db, new WorkflowStore(), new TestProcessor { Enabled = false },
            new ProcessingJobQueue(), new ConsoleErrorLogger<ProjectProcessingService>(), new FakeTokenService());
        await Assert.ThrowsAsync<ConfigurationException>(() => service.StartAsync(project.UserId, project.Id, new(ProcessingOperation.Upscale), Ct));
        Assert.Empty(await db.ProcessingJobs.ToListAsync());
    }

    [Fact]
    public async Task ResolutionTargetPersistsAnExactQhdResultAndDoesNotRepeatExistingUpgrades()
    {
        await using var db = WorkflowData.Db();
        var project = WorkflowData.Project(Guid.NewGuid());
        project.OriginalImage.OriginalWidth = 1920; project.OriginalImage.OriginalHeight = 1080;
        db.Add(project); await db.SaveChangesAsync();
        var store = new WorkflowStore(); Original(store, 1920, 1080);
        var service = new ProjectProcessingService(db, store, new TestProcessor(), new ProcessingJobQueue(), new ConsoleErrorLogger<ProjectProcessingService>(), new FakeTokenService());
        var request = new ProcessProjectRequest(ProcessingOperation.Upscale, TargetLongEdge: 2560);
        var job = await service.StartAsync(project.UserId, project.Id, request, Ct);
        Assert.Equal(2560, job.TargetLongEdge);
        await service.ExecuteAsync(job.JobId, Ct);
        var saved = await service.GetStatusAsync(project.UserId, project.Id, Ct);
        Assert.Equal(ProcessingJobStatus.Completed, saved.Status);
        Assert.Equal(2560, saved.ResultWidth); Assert.Equal(1440, saved.ResultHeight);
        Assert.Equal(2560, saved.TargetLongEdge);
        // 같은 해상도 재수정은 허용된다.
        var revision = await service.StartAsync(project.UserId, project.Id, request, Ct);
        Assert.NotEqual(job.JobId, revision.JobId);
        Assert.Equal(2, await db.ProcessingJobs.CountAsync());
        await Assert.ThrowsAsync<NotFoundException>(() => service.StartAsync(Guid.NewGuid(), project.Id,
            request with { TargetLongEdge = 3840 }, Ct));
    }

    [Theory]
    [InlineData(0, ProcessingOperation.Upscale)]
    [InlineData(4096, ProcessingOperation.Upscale)]
    [InlineData(1920, ProcessingOperation.Upscale)]
    [InlineData(2560, ProcessingOperation.Layers)]
    public async Task InvalidOrLowerResolutionIsRejectedBeforeAJobIsCreated(int edge, ProcessingOperation operation)
    {
        await using var db = WorkflowData.Db(); var project = WorkflowData.Project(Guid.NewGuid());
        project.OriginalImage.OriginalWidth = 1920; project.OriginalImage.OriginalHeight = 1080;
        db.Add(project); await db.SaveChangesAsync();
        var service = new ProjectProcessingService(db, new WorkflowStore(), new TestProcessor(), new ProcessingJobQueue(), new ConsoleErrorLogger<ProjectProcessingService>(), new FakeTokenService());
        await Assert.ThrowsAsync<ValidationException>(() => service.StartAsync(project.UserId, project.Id,
            new(operation, TargetLongEdge: edge), Ct));
        Assert.Empty(await db.ProcessingJobs.ToListAsync());
    }

    [Fact]
    public async Task ACloudResultThatMissesTheSelectedResolutionIsNotSaved()
    {
        await using var db = WorkflowData.Db(); var project = WorkflowData.Project(Guid.NewGuid());
        db.Add(project); await db.SaveChangesAsync();
        var store = new WorkflowStore(); Original(store);
        var service = new ProjectProcessingService(db, store, new TestProcessor { WrongDimensions = true }, new ProcessingJobQueue(), new ConsoleErrorLogger<ProjectProcessingService>(), new FakeTokenService());
        var job = await service.StartAsync(project.UserId, project.Id, new(ProcessingOperation.Upscale, TargetLongEdge: 1920), Ct);
        await service.ExecuteAsync(job.JobId, Ct);
        Assert.Equal(ProcessingJobStatus.Failed, (await service.GetStatusAsync(project.UserId, project.Id, Ct)).Status);
        Assert.Single(store.Files);
    }

    [Theory]
    [InlineData(1920, 2)]
    [InlineData(2560, 4)]
    [InlineData(3840, 6)]
    public async Task Upscale_ReservesExpectedTokenCost(
        int targetEdge,
        int expectedCost)
    {
        await using var db = WorkflowData.Db();
        var project = WorkflowData.Project(Guid.NewGuid());
        db.Add(project);
        await db.SaveChangesAsync();

        var store = new WorkflowStore();
        Original(store);

        var tokens = new FakeTokenService();

        var service = new ProjectProcessingService(
            db,
            store,
            new TestProcessor(),
            new ProcessingJobQueue(),
            NullLogger<ProjectProcessingService>.Instance,
            tokens);

        await service.StartAsync(
            project.UserId,
            project.Id,
            new ProcessProjectRequest(
                ProcessingOperation.Upscale,
                TargetLongEdge: targetEdge),
            Ct);

        Assert.Single(tokens.ReservedCosts);
        Assert.Equal(expectedCost, tokens.ReservedCosts[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Upscale_SettlesTokensBasedOnJobResult(bool fail)
    {
        await using var db = WorkflowData.Db();
        var project = WorkflowData.Project(Guid.NewGuid());
        db.Add(project);
        await db.SaveChangesAsync();

        var store = new WorkflowStore();
        Original(store);

        var tokens = new FakeTokenService();

        var service = new ProjectProcessingService(
            db,
            store,
            new TestProcessor { WrongDimensions = fail },
            new ProcessingJobQueue(),
            NullLogger<ProjectProcessingService>.Instance,
            tokens);

        var started = await service.StartAsync(
            project.UserId,
            project.Id,
            new ProcessProjectRequest(
                ProcessingOperation.Upscale,
                TargetLongEdge: 1920),
            Ct);

        await service.ExecuteAsync(started.JobId, Ct);

        Assert.Single(tokens.ReservedCosts);
        Assert.Equal(2, tokens.ReservedCosts[0]);

        var status = await service.GetStatusAsync(
            project.UserId, project.Id, Ct);

        if (fail)
        {
            Assert.Equal(ProcessingJobStatus.Failed, status.Status);
            Assert.Equal(0, tokens.CompletedCount);
            Assert.Equal(1, tokens.RefundedCount);
        }
        else
        {
            Assert.Equal(ProcessingJobStatus.Completed, status.Status);
            Assert.Equal(1, tokens.CompletedCount);
            Assert.Equal(0, tokens.RefundedCount);
        }
    }

    [Theory]
    [InlineData(ProcessingOperation.Layers, false, 3)]
    [InlineData(ProcessingOperation.Layers, true, 1)]
    [InlineData(ProcessingOperation.CharacterLayers, false, 7)]
    [InlineData(ProcessingOperation.CharacterLayers, true, 3)]
    public async Task LayerOperations_ReserveExpectedTokenCost(
        ProcessingOperation operation,
        bool isRevision,
        int expectedCost)
    {
        await using var db = WorkflowData.Db();
        var project = WorkflowData.Project(Guid.NewGuid());

        db.Add(project);
        await db.SaveChangesAsync();

        // 이미 성공한 작업이 있으면 재분리로 판단한다.
        if (isRevision)
        {
            db.ProcessingJobs.Add(new ProcessingJob
            {
                ProjectId = project.Id,
                Operation = operation,
                Status = ProcessingJobStatus.Completed
            });

            await db.SaveChangesAsync();
        }

        var tokens = new FakeTokenService();

        var service = new ProjectProcessingService(
            db,
            new WorkflowStore(),
            new TestProcessor(),
            new ProcessingJobQueue(),
            NullLogger<ProjectProcessingService>.Instance,
            tokens);

        await service.StartAsync(
            project.UserId,
            project.Id,
            new ProcessProjectRequest(operation),
            Ct);

        Assert.Single(tokens.ReservedCosts);
        Assert.Equal(expectedCost, tokens.ReservedCosts[0]);
    }

    [Theory]
    [InlineData(1920, 1, true)]
    [InlineData(1920, 1, false)]
    [InlineData(2560, 2, true)]
    [InlineData(2560, 2, false)]
    [InlineData(3840, 3, true)]
    [InlineData(3840, 3, false)]
    public async Task UpscaleRevision_ReservesReducedTokenCost(
        int targetEdge,
        int expectedCost,
        bool recordedTarget)
    {
        await using var db = WorkflowData.Db();
        var project = WorkflowData.Project(Guid.NewGuid());

        db.Add(project);
        await db.SaveChangesAsync();

        db.ProcessingJobs.Add(new ProcessingJob
        {
            ProjectId = project.Id,
            Operation = ProcessingOperation.Upscale,
            Status = ProcessingJobStatus.Completed,
            TargetLongEdge = recordedTarget ? targetEdge : null,
            ResultObjectKey = "previous-result.png",
            ResultWidth = targetEdge,
            ResultHeight = targetEdge / 2
        });

        await db.SaveChangesAsync();

        var tokens = new FakeTokenService();
        var store = new WorkflowStore();
        Original(store);

        var service = new ProjectProcessingService(
            db,
            store,
            new TestProcessor(),
            new ProcessingJobQueue(),
            NullLogger<ProjectProcessingService>.Instance,
            tokens);

        await service.StartAsync(
            project.UserId,
            project.Id,
            new ProcessProjectRequest(
                ProcessingOperation.Upscale,
                TargetLongEdge: targetEdge),
            Ct);

        Assert.Single(tokens.ReservedCosts);
        Assert.Equal(expectedCost, tokens.ReservedCosts[0]);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task FhdRevision_ReservesOneToken(bool recordedTarget, bool resolutionRequest)
    {
        await using var db = WorkflowData.Db();
        var project = WorkflowData.Project(Guid.NewGuid());
        db.Add(project);
        var prior = new ProcessingJob
        {
            ProjectId = project.Id, Operation = ProcessingOperation.Upscale,
            Status = ProcessingJobStatus.Completed, TargetLongEdge = recordedTarget ? 1920 : null,
            ResultObjectKey = "completed-fhd.png", ResultWidth = 1920, ResultHeight = 1080
        };
        db.ProcessingJobs.Add(prior);
        await db.SaveChangesAsync();
        var tokens = new FakeTokenService();
        var service = new ProjectProcessingService(db, new WorkflowStore(), new TestProcessor(),
            new ProcessingJobQueue(), NullLogger<ProjectProcessingService>.Instance, tokens);

        await service.StartAsync(
            project.UserId, project.Id, new(ProcessingOperation.Upscale, TargetLongEdge: resolutionRequest ? 1920 : null), Ct);

        Assert.Equal(1, Assert.Single(tokens.ReservedCosts));
        Assert.Equal(2, await db.ProcessingJobs.CountAsync());
        Assert.Equal(ProjectStatus.Processing, (await db.LiveLayerProjects.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(ProcessingOperation.Upscale, 2)]
    [InlineData(ProcessingOperation.Layers, 3)]
    [InlineData(ProcessingOperation.CharacterLayers, 7)]
    public async Task FailedAttempt_DoesNotCountAsASuccessfulFirstUse(ProcessingOperation operation, int expectedCost)
    {
        await using var db = WorkflowData.Db();
        var project = WorkflowData.Project(Guid.NewGuid());
        project.ProcessingJobs.Add(new ProcessingJob { Operation = operation,
            Status = ProcessingJobStatus.Failed, TargetLongEdge = operation == ProcessingOperation.Upscale ? 1920 : null });
        db.Add(project);
        await db.SaveChangesAsync();
        var tokens = new FakeTokenService();
        var service = new ProjectProcessingService(db, new WorkflowStore(), new TestProcessor(),
            new ProcessingJobQueue(), NullLogger<ProjectProcessingService>.Instance, tokens);

        await service.StartAsync(project.UserId, project.Id,
            new(operation, TargetLongEdge: operation == ProcessingOperation.Upscale ? 1920 : null), Ct);

        Assert.Equal(expectedCost, Assert.Single(tokens.ReservedCosts));
    }

    private sealed class TestProcessor : IProjectImageProcessor
    {
        public bool WrongDimensions { get; init; }
        public bool Enabled { get; init; } = true;
        public bool Supports(ProcessingOperation operation) => Enabled;
        public Task<ProjectProcessingResult> ProcessAsync(ProjectProcessingRequest request, CancellationToken cancellationToken)
        {
            var path = Path.Combine(request.OutputDirectory, "upscaled.png");
            var size = WrongDimensions ? 10 : 20;
            var dimensions = (Width: size, Height: size);
            if (request.TargetLongEdge is int edge && !WrongDimensions)
            {
                var info = new MagickImageInfo(request.InputPath);
                dimensions = UpscaleResolution.Dimensions((int)info.Width, (int)info.Height, edge);
            }
            using var image = new MagickImage(MagickColors.Red, (uint)dimensions.Width, (uint)dimensions.Height);
            image.Write(path, MagickFormat.Png);
            return Task.FromResult(new ProjectProcessingResult([], path, dimensions.Width, dimensions.Height));
        }
    }
}


internal sealed class ConsoleErrorLogger<T> : ILogger<T>
{
    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel < LogLevel.Error) return;

        Console.Error.WriteLine(formatter(state, exception));

        if (exception is not null)
            Console.Error.WriteLine(exception.ToString());
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
