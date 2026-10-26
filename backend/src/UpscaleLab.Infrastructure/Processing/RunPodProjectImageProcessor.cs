using UpscaleLab.Application.Processing;
using UpscaleLab.Domain.Enums;
using UpscaleLab.Infrastructure.Upscaling;

namespace UpscaleLab.Infrastructure.Processing;

public sealed class RunPodProjectImageProcessor(
    ExistingLayerProjectProcessor layerProcessor,
    RunPodUpscaleService runPodUpscaleService) : IProjectImageProcessor
{
    public bool Supports(ProcessingOperation operation)
    {
        return operation is ProcessingOperation.Layers
            or ProcessingOperation.Upscale;
    }

    public async Task<ProjectProcessingResult> ProcessAsync(
        ProjectProcessingRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Operation == ProcessingOperation.Layers)
        {
            return await layerProcessor.ProcessAsync(
                request,
                cancellationToken);
        }

        if (request.Operation == ProcessingOperation.Upscale)
        {
            Directory.CreateDirectory(request.OutputDirectory);

            string outputPath = Path.Combine(
                request.OutputDirectory,
                $"upscaled_{request.JobId:N}.png");

            var result = await runPodUpscaleService.UpscaleAsync(
                request.InputPath,
                request.UpscaleFactor,
                outputPath,
                cancellationToken);

            return new ProjectProcessingResult(
                Array.Empty<ProcessedLayer>(),
                result.FilePath,
                result.Width,
                result.Height);
        }

        throw new NotSupportedException(
            $"Unsupported processing operation: {request.Operation}");
    }
}