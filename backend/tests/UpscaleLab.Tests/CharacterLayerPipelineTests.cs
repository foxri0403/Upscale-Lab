using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UpscaleLab.Application.Processing;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Processing;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class CharacterLayerPipelineTests
{
    [Fact]
    public async Task RetainedBackground_UsesTwoProvidersWithinOneLive2DJobAndKeepsRevisionPricing()
    {
        await using var db = WorkflowData.Db();
        var project = WorkflowData.Project(Guid.NewGuid());
        db.Add(project);
        await db.SaveChangesAsync();
        var storage = new WorkflowStore();
        using (var input = new MagickImage(MagickColors.Black, 12, 20))
            storage.Files["source.png"] = input.ToByteArray(MagickFormat.Png);
        var provider = new BackgroundAndCharacterProcessor();
        var tokens = new FakeTokenService();
        var service = new ProjectProcessingService(db, storage, provider, new ProcessingJobQueue(),
            NullLogger<ProjectProcessingService>.Instance, tokens);

        var first = await service.StartAsync(project.UserId, project.Id,
            new(ProcessingOperation.CharacterLayers, KeepBackground: true), CancellationToken.None);
        await service.ExecuteAsync(first.JobId, CancellationToken.None);
        var completed = await service.GetStatusAsync(project.UserId, project.Id, CancellationToken.None);
        Assert.Equal(ProcessingJobStatus.Completed, completed.Status);
        Assert.Equal(ProcessingOperation.CharacterLayers, completed.Operation);
        Assert.True(completed.KeepBackground);
        Assert.Equal((12, 20), (completed.ResultWidth, completed.ResultHeight));
        Assert.Equal(new[] { (first.JobId, ProcessingOperation.Layers), (first.JobId, ProcessingOperation.CharacterLayers) }, provider.Calls);
        Assert.Equal(new[] { 7 }, tokens.ReservedCosts);
        var layers = await db.ImageLayers.OrderBy(x => x.LayerOrder).ToListAsync();
        Assert.Equal(new[] { LayerType.Background, LayerType.Face, LayerType.Hair }, layers.Select(layer => layer.LayerType));
        using var background = new MagickImage(storage.Files[layers[0].ObjectKey]);
        Assert.Equal(MagickColors.Red, background.GetPixels().GetPixel(0, 0).ToColor());
        foreach (var part in layers.Skip(1))
        {
            using var image = new MagickImage(storage.Files[part.ObjectKey]);
            Assert.Equal((12u, 20u), (image.Width, image.Height));
        }

        var revision = await service.StartAsync(project.UserId, project.Id, new(ProcessingOperation.CharacterLayers), CancellationToken.None);
        Assert.True(revision.KeepBackground);
        provider.FailCharacter = true;
        await service.ExecuteAsync(revision.JobId, CancellationToken.None);
        var failed = await service.GetStatusAsync(project.UserId, project.Id, CancellationToken.None);
        Assert.Equal(ProcessingJobStatus.Failed, failed.Status);
        Assert.Equal(new[] { 7, 3 }, tokens.ReservedCosts);
        Assert.Equal(1, tokens.RefundedCount);
        Assert.Equal(layers.Select(layer => layer.Id), await db.ImageLayers.OrderBy(x => x.LayerOrder).Select(layer => layer.Id).ToListAsync());
    }

    [Fact]
    public async Task CharacterLayers_SavesOrderedLayersWithoutImageResult()
    {
        await using var db = WorkflowData.Db();
        var project = WorkflowData.Project(Guid.NewGuid());
        db.Add(project);
        await db.SaveChangesAsync();
        var storage = new WorkflowStore();
        using (var input = new MagickImage(MagickColors.Cyan, 10, 10))
            storage.Files["source.png"] = input.ToByteArray(MagickFormat.Png);

        var service = new ProjectProcessingService(db, storage, new CharacterProcessor(),
            new ProcessingJobQueue(), NullLogger<ProjectProcessingService>.Instance, new FakeTokenService());
        var queued = await service.StartAsync(project.UserId, project.Id,
            new ProcessProjectRequest(ProcessingOperation.CharacterLayers), CancellationToken.None);
        await service.ExecuteAsync(queued.JobId, CancellationToken.None);
        var result = await service.GetStatusAsync(project.UserId, project.Id, CancellationToken.None);

        Assert.Equal(ProcessingJobStatus.Completed, result.Status);
        Assert.Equal(ProcessingOperation.CharacterLayers, result.Operation);
        Assert.False(result.HasImageResult);
        var layers = await db.ImageLayers.OrderBy(x => x.LayerOrder).ToListAsync();
        Assert.Equal(2, layers.Count);
        Assert.Equal(new[] { LayerType.BackPart, LayerType.Face }, layers.Select(x => x.LayerType).ToArray());
        Assert.Equal(new[] { 0, 1 }, layers.Select(x => x.LayerOrder).ToArray());
    }

    private sealed class CharacterProcessor : IProjectImageProcessor
    {
        public bool Supports(ProcessingOperation operation) => operation == ProcessingOperation.CharacterLayers;

        public Task<ProjectProcessingResult> ProcessAsync(ProjectProcessingRequest request, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(request.OutputDirectory);
            var result = new List<ProcessedLayer>();
            foreach (var (index, type, color) in new[]
            {
                (0, LayerType.BackPart, MagickColors.Blue),
                (1, LayerType.Face, MagickColors.Yellow)
            })
            {
                var path = Path.Combine(request.OutputDirectory, $"{index:000}.png");
                using var image = new MagickImage(color, 10, 10);
                image.Write(path, MagickFormat.Png);
                result.Add(new ProcessedLayer(path, type, index, index * 0.5,
                    0, 0, 0, 1, 2, 2));
            }
            return Task.FromResult(new ProjectProcessingResult(result, null, 10, 10));
        }
    }

    private sealed class BackgroundAndCharacterProcessor : IProjectImageProcessor
    {
        public List<(Guid JobId, ProcessingOperation Operation)> Calls { get; } = [];
        public bool FailCharacter { get; set; }
        public bool Supports(ProcessingOperation operation) => operation is ProcessingOperation.Layers or ProcessingOperation.CharacterLayers;

        public async Task<ProjectProcessingResult> ProcessAsync(ProjectProcessingRequest request, CancellationToken ct)
        {
            Calls.Add((request.JobId, request.Operation));
            Directory.CreateDirectory(request.OutputDirectory);
            if (request.ReportProgress is not null) await request.ReportProgress(45, ct);
            if (request.Operation == ProcessingOperation.Layers)
                return new([layer(LayerType.Background, 0, MagickColors.Red, 12, 20),
                    layer(LayerType.Body, 1, MagickColors.Cyan, 12, 20)], null, 12, 20);

            if (FailCharacter) throw new InvalidOperationException("Second stage fails");
            using var image = new MagickImage(request.InputPath);
            Assert.Equal((1280u, 1280u), (image.Width, image.Height));
            using var pixels = image.GetPixels();
            Assert.Equal(MagickColors.White, pixels.GetPixel(0, 640).ToColor());
            Assert.Equal(MagickColors.Cyan, pixels.GetPixel(640, 640).ToColor());
            return new([layer(LayerType.Background, 0, MagickColors.Magenta, 32, 32),
                layer(LayerType.Face, 1, MagickColors.Yellow, 32, 32),
                layer(LayerType.Hair, 2, MagickColors.Blue, 32, 32)], null, 32, 32);

            ProcessedLayer layer(LayerType type, int order, MagickColor color, uint width, uint height)
            {
                var path = Path.Combine(request.OutputDirectory, $"{order:000}.png");
                using var part = new MagickImage(color, width, height);
                part.Write(path, MagickFormat.Png);
                return new(path, type, order, 0, 0, 0, 0, 1, 0, 0);
            }
        }
    }
}
