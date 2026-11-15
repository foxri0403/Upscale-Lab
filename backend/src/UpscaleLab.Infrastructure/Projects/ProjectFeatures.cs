using UpscaleLab.Application.Images;
using UpscaleLab.Domain.Entities;
using UpscaleLab.Domain.Enums;

namespace UpscaleLab.Infrastructure.Projects;

internal static class ProjectFeatures
{
    public static bool HasCharacterLayers(LiveLayerProject project) =>
        project.Layers.Count >= 2 &&
        (!project.Layers.Any(x => x.LayerType == LayerType.Background) ||
         project.ProcessingJobs
             .Where(x => x.Status == ProcessingJobStatus.Completed &&
                         x.Operation is ProcessingOperation.Layers or ProcessingOperation.CharacterLayers or ProcessingOperation.UpscaleAndLayers)
             .OrderByDescending(x => x.CompletedAt ?? x.CreatedAt)
             .FirstOrDefault()?.Operation == ProcessingOperation.CharacterLayers);

    public static IReadOnlyList<string> For(LiveLayerProject project)
    {
        var features = new List<string>();
        var character = HasCharacterLayers(project);
        if (project.ProcessingJobs.Any(x => x.Status == ProcessingJobStatus.Completed && x.ResultObjectKey != null))
            features.Add("업스케일");
        if (project.Layers.Count >= 2 && !character)
            features.Add("배경 분리");
        else if (character || ImageEditState.FromJson(project.SavedEditJson)?.LiveEnabled == true)
            features.Add("Live 2D");
        return features;
    }
}
