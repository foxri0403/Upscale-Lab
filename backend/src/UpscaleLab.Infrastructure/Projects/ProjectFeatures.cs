using UpscaleLab.Application.Images;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;

namespace UpscaleLab.Infrastructure.Projects;

internal static class ProjectFeatures
{
    public static IReadOnlyList<string> For(LiveLayerProject project)
    {
        var features = new List<string>();
        if (project.ProcessingJobs.Any(x => x.Status == ProcessingJobStatus.Completed && x.ResultObjectKey != null))
            features.Add("업스케일");
        if (project.Layers.Count >= 2)
            features.Add("배경 분리");
        if (ImageEditState.FromJson(project.SavedEditJson)?.LiveEnabled == true)
            features.Add("Live 2D");
        return features;
    }
}
