using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UpscaleLab.Companion;

// WPF composes cached image planes on the GPU and drives updates from DWM rendering.
// No 33ms WinForms timer or per-frame bitmap decoding/scaling.
internal sealed class ParallaxView : Grid, IDisposable
{
    private readonly WallpaperManifest manifest;
    private readonly MonitorInfo monitor;
    private readonly Canvas canvas = new();
    private readonly List<(WallpaperFile Meta, TranslateTransform Translation)> planes = new();
    private readonly ScaleTransform cover = new();
    private readonly TranslateTransform offset = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private TimeSpan lastFrame;
    private double lastTime, x, y;
    private readonly Func<System.Drawing.Point> pointer;

    public ParallaxView(MonitorInfo monitor, WallpaperManifest manifest, Func<System.Drawing.Point>? pointer = null)
    {
        this.monitor = monitor; this.manifest = manifest;
        this.pointer = pointer ?? (() => System.Windows.Forms.Cursor.Position);
        ClipToBounds = true; Background = System.Windows.Media.Brushes.Black; Children.Add(canvas);
        canvas.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        canvas.VerticalAlignment = System.Windows.VerticalAlignment.Top;
        canvas.Width = manifest.Width; canvas.Height = manifest.Height;
        var scene = new TransformGroup(); scene.Children.Add(cover); scene.Children.Add(offset);
        canvas.RenderTransform = scene;
        foreach (var file in manifest.Files.OrderBy(file => file.Order))
        {
            using var image = RasterImage.Read(file);
            var maxPixels = Math.Min(16_000_000, 48_000_000 / manifest.Files.Length);
            var factor = Math.Min(1, Math.Sqrt(maxPixels / ((double)image.Width * image.Height)));
            image.Resize((uint)Math.Max(1, Math.Round(image.Width * factor)), (uint)Math.Max(1, Math.Round(image.Height * factor)));
            using var bytes = new MemoryStream(image.ToByteArray(ImageMagick.MagickFormat.Png));
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = bytes; bitmap.EndInit(); bitmap.Freeze();
            var plane = new System.Windows.Controls.Image { Source = bitmap, Width = manifest.Width, Height = manifest.Height,
                Stretch = Stretch.Fill, CacheMode = new BitmapCache() };
            var transforms = new TransformGroup();
            transforms.Children.Add(new ScaleTransform(file.Scale, file.Scale, manifest.Width / 2.0, manifest.Height / 2.0));
            transforms.Children.Add(new RotateTransform(file.Rotation, manifest.Width / 2.0, manifest.Height / 2.0));
            var translation = new TranslateTransform(file.X, file.Y); transforms.Children.Add(translation);
            plane.RenderTransform = transforms; canvas.Children.Add(plane); planes.Add((file, translation));
        }
        SizeChanged += (_, _) => Fit();
        Loaded += (_, _) => { lastTime = clock.Elapsed.TotalSeconds; CompositionTarget.Rendering -= Render;
            if (manifest.UsesMouse) CompositionTarget.Rendering += Render; };
        Unloaded += (_, _) => CompositionTarget.Rendering -= Render;
    }

    private void Fit()
    {
        var scale = Math.Max(ActualWidth / manifest.Width, ActualHeight / manifest.Height);
        cover.ScaleX = cover.ScaleY = scale;
        offset.X = (ActualWidth - manifest.Width * scale) / 2;
        offset.Y = (ActualHeight - manifest.Height * scale) / 2;
    }

    private void Render(object? sender, EventArgs args)
    {
        if (args is RenderingEventArgs frame)
        {
            if (lastFrame == frame.RenderingTime) return;
            lastFrame = frame.RenderingTime;
        }
        var now = clock.Elapsed.TotalSeconds;
        var smoothing = 1 - Math.Exp(-Math.Clamp(now - lastTime, 0, .1) / .065); lastTime = now;
        var pointer = this.pointer();
        var targetX = Math.Clamp(2.0 * (pointer.X - monitor.OffsetX) / monitor.Width - 1, -1, 1);
        var targetY = Math.Clamp(2.0 * (pointer.Y - monitor.OffsetY) / monitor.Height - 1, -1, 1);
        x += (targetX - x) * smoothing; y += (targetY - y) * smoothing;
        foreach (var (meta, translation) in planes)
        {
            var moving = meta.Role != "Background";
            translation.X = meta.X + (moving ? x * meta.MovementX : 0);
            translation.Y = meta.Y + (moving ? y * meta.MovementY : 0);
        }
    }

    public void Dispose() { CompositionTarget.Rendering -= Render; Children.Clear(); planes.Clear(); }
}
