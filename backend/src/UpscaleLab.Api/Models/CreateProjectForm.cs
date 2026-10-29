using System.ComponentModel.DataAnnotations;

namespace UpscaleLab.Api.Models;

public sealed class CreateProjectForm
{
    [Required]
    public IFormFile File { get; init; } = null!;

    [Required, MaxLength(160)]
    public string Title { get; init; } = string.Empty;

    [Range(0, 32768)]
    public int OriginalWidth { get; init; }

    [Range(0, 32768)]
    public int OriginalHeight { get; init; }
    public bool IsDraft { get; init; }
}
