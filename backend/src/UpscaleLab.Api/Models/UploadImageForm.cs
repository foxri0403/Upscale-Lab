using System.ComponentModel.DataAnnotations;

namespace UpscaleLab.Api.Models;

public sealed class UploadImageForm
{
    [Required]
    public IFormFile File { get; set; } = null!;

    [Range(1, 32768)]
    public int OriginalWidth { get; set; }

    [Range(1, 32768)]
    public int OriginalHeight { get; set; }
}
