using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;

namespace UpscaleLab.Companion;

internal sealed record WallpaperLayer(WallpaperFile File, BitmapSource Image);

internal static class WallpaperLayers
{
    public static WallpaperLayer[] Load(WallpaperManifest manifest, MonitorInfo monitor, string? neutralPath, CancellationToken ct = default)
    {
        var reduction = Math.Min(1, 4096.0 / Math.Max(monitor.Width, monitor.Height));
        using var neutral = neutralPath is null ? null : new Bitmap(
            Math.Max(1, (int)(monitor.Width * reduction)), Math.Max(1, (int)(monitor.Height * reduction)));
        using var graphics = neutral is null ? null : Graphics.FromImage(neutral);
        if (graphics is not null)
        {
            graphics.Clear(System.Drawing.Color.Black);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }
        var maxPixels = Math.Min(16_000_000, 48_000_000 / manifest.Files.Length);
        var layers = new List<WallpaperLayer>(manifest.Files.Length);
        foreach (var file in manifest.Files.OrderBy(file => file.Order))
        {
            ct.ThrowIfCancellationRequested();
            using var image = RasterImage.Read(file);
            if (image.Width != manifest.Width || image.Height != manifest.Height)
                throw new InvalidDataException("동적 배경화면 레이어 크기가 일치하지 않습니다.");
            var factor = Math.Min(1, Math.Sqrt(maxPixels / ((double)image.Width * image.Height)));
            if (factor < 1)
                image.Resize((uint)Math.Max(1, Math.Round(image.Width * factor)), (uint)Math.Max(1, Math.Round(image.Height * factor)));
            var width = (int)image.Width;
            var height = (int)image.Height;
            using var pixels = image.GetPixelsUnsafe();
            var bgra = pixels.ToByteArray(PixelMapping.BGRA) ?? throw new InvalidDataException("레이어를 읽지 못했습니다.");
            var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
            source.Freeze();
            layers.Add(new(file, source));
            if (graphics is not null && neutral is not null)
                DrawNeutralLayer(graphics, neutral.Size, manifest, file, bgra, width, height);
        }
        if (neutralPath is not null && neutral is not null)
        {
            ct.ThrowIfCancellationRequested();
            var temp = neutralPath + ".part";
            try
            {
                neutral.Save(temp, ImageFormat.Png);
                File.Move(temp, neutralPath, true);
            }
            finally { File.Delete(temp); }
        }
        return layers.ToArray();
    }

    private static void DrawNeutralLayer(Graphics graphics, Size size, WallpaperManifest manifest,
        WallpaperFile file, byte[] pixels, int width, int height)
    {
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            using var bitmap = new Bitmap(width, height, width * 4, System.Drawing.Imaging.PixelFormat.Format32bppArgb, pinned.AddrOfPinnedObject());
            var cover = Math.Max((double)size.Width / manifest.Width, (double)size.Height / manifest.Height);
            var state = graphics.Save();
            graphics.TranslateTransform((float)(size.Width / 2.0 + file.X * cover), (float)(size.Height / 2.0 + file.Y * cover));
            graphics.RotateTransform((float)file.Rotation);
            graphics.ScaleTransform((float)(cover * file.Scale), (float)(cover * file.Scale));
            graphics.DrawImage(bitmap, new RectangleF(-manifest.Width / 2f, -manifest.Height / 2f, manifest.Width, manifest.Height));
            graphics.Restore(state);
        }
        finally { pinned.Free(); }
    }
}
