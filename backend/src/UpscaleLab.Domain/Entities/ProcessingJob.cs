using UpscaleLab.Domain.Enums;

namespace UpscaleLab.Domain.Entities;

public sealed class ProcessingJob : BaseEntity
{
    public Guid ProjectId { get; set; }
    public ProcessingJobStatus Status { get; set; } = ProcessingJobStatus.Queued;
    public int Progress { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public LiveLayerProject Project { get; set; } = null!;
}
