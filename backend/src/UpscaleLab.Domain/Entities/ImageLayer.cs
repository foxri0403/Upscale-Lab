using UpscaleLab.Domain.Enums;

namespace UpscaleLab.Domain.Entities;

public sealed class ImageLayer : BaseEntity
{
    public Guid ProjectId { get; set; }
    public LayerType LayerType { get; set; }
    public int LayerOrder { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public double Depth { get; set; }
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public double Rotation { get; set; }
    public double Scale { get; set; } = 1;
    public double MovementX { get; set; }
    public double MovementY { get; set; }

    public LiveLayerProject Project { get; set; } = null!;
}
