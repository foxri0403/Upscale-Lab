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
            new ProcessingJobQueue(), NullLogger<ProjectProcessingService>.Instance);
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
}
