using ImageMagick;
using UpscaleLab.Application.Common;
using UpscaleLab.Application.Processing;
using UpscaleLab.Domain.Enums;

namespace UpscaleLab.Infrastructure.Processing;

// Adapts the cloud layer provider to the common project processing contract.
public sealed class ExistingLayerProjectProcessor(IImageLayerProcessor layers) : IProjectImageProcessor
{
    public bool Supports(ProcessingOperation operation) => operation == ProcessingOperation.Layers && layers.IsConfigured;
    public async Task<ProjectProcessingResult> ProcessAsync(ProjectProcessingRequest request, CancellationToken cancellationToken)
    {
        if (!Supports(request.Operation)) throw new ConfigurationException("클라우드 배경 분리 공급자가 아직 연결되지 않았습니다.");
        var result = await layers.ProcessAsync(new(request.InputPath, request.OutputDirectory, request.JobId,
            request.ReportProgress, request.SubjectMaskPath, request.RefineSelection), cancellationToken);
        if (result.Count == 0) throw new InvalidDataException("분리된 레이어가 없습니다.");
        var info = new MagickImageInfo(result[0].FilePath);
        return new(result, null, checked((int)info.Width), checked((int)info.Height));
    }
}
