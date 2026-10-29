using UpscaleLab.Application.Common;
namespace UpscaleLab.Application.Images;

public sealed record ImageCropRegion(double X, double Y, double Width, double Height)
{
    public void Validate()
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y) || !double.IsFinite(Width) || !double.IsFinite(Height) ||
            X < 0 || Y < 0 || Width <= 0 || Height <= 0 || X + Width > 1 + 1e-8 || Y + Height > 1 + 1e-8)
            throw new ValidationException("자르기 영역은 원본 이미지 안에 있어야 합니다.");
    }
}
public static class ImageCropGeometry
{
    public static (int X, int Y, int Width, int Height) Resolve(int width, int height, string crop, ImageCropRegion? region)
    {
        if (width < 1 || height < 1) throw new ValidationException("원본 이미지 크기를 확인해 주세요.");
        if (crop == "original") return (0, 0, width, height);
        if (crop is not ("wide" or "portrait" or "square" or "custom")) throw new ValidationException("이미지 비율이 올바르지 않습니다.");
        if (region == null)
        {
            if (crop == "custom") throw new ValidationException("사용자 비율의 선택 영역을 지정해 주세요.");
            var ratio = crop switch { "wide" => 16.0 / 9, "portrait" => 9.0 / 16, _ => 1 };
            var w = Math.Min(1, height * ratio / width); var h = Math.Min(1, width / ratio / height);
            region = new((1 - w) / 2, (1 - h) / 2, w, h);
        }
        region.Validate();
        int Round(double value) => (int)Math.Floor(value + .5);
        var x = Math.Clamp(Round(region.X * width), 0, width - 1); var y = Math.Clamp(Round(region.Y * height), 0, height - 1);
        return (x, y, Math.Clamp(Round(region.Width * width), 1, width - x), Math.Clamp(Round(region.Height * height), 1, height - y));
    }
}
