using UpscaleLab.Application.Common;

namespace UpscaleLab.Application.Upscaling;

public static class UpscaleResolution
{
    public static bool IsSupported(int edge) => edge is 1920 or 2560 or 3840;

    public static (int Width, int Height) Dimensions(int width, int height, int edge)
    {
        if (!IsSupported(edge) || width < 1 || height < 1)
            throw new ValidationException("FHD, QHD, 4K 중 목표 해상도를 선택해 주세요.");
        var longest = Math.Max(width, height);
        return (Math.Max(1, (int)(((long)width * edge + longest / 2) / longest)),
            Math.Max(1, (int)(((long)height * edge + longest / 2) / longest)));
    }

    public static int NativeScale(int width, int height, int edge) =>
        edge <= (long)Math.Max(width, height) * 2 ? 2 : 4;
}
