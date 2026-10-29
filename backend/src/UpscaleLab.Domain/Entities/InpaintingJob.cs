using UpscaleLab.Domain.Enums;

namespace UpscaleLab.Domain.Entities;

public sealed class InpaintingJob : BaseEntity
{
    public Guid UserId { get; set; }
    public string? RunPodJobId { get; set; }

    public ProcessingJobStatus Status { get; set; }
        = ProcessingJobStatus.Queued;

    public string? InputObjectKey { get; set; }
    public string? MaskObjectKey { get; set; }
    public string? ResultObjectKey { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public string? ErrorMessage { get; set; }

    public User User { get; set; } = null!;
}
