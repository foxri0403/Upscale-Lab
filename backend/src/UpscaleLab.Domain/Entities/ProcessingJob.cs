using UpscaleLab.Domain.Enums;

namespace UpscaleLab.Domain.Entities;

public sealed class ProcessingJob : BaseEntity
{
    public Guid ProjectId { get; set; }
    public ProcessingOperation Operation { get; set; } = ProcessingOperation.Layers;
    public int UpscaleFactor { get; set; } = 2;
    public int? TargetLongEdge { get; set; }
    public string? ResultObjectKey { get; set; }
    public string? SubjectMaskObjectKey { get; set; }
    public int ResultWidth { get; set; }
    public int ResultHeight { get; set; }
    public ProcessingJobStatus Status { get; set; } = ProcessingJobStatus.Queued;
    public int Progress { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public LiveLayerProject Project { get; set; } = null!;
}
