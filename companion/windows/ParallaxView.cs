using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Media;

namespace UpscaleLab.Companion;

// WPF composes cached image planes on the GPU and drives updates from DWM rendering.
// No 33ms WinForms timer or per-frame bitmap decoding/scaling.
internal sealed class ParallaxView : Grid, IDisposable
{
    private readonly WallpaperManifest manifest;
    private readonly MonitorInfo monitor;
    private readonly Canvas canvas = new();
    // Role assignments do not change while this wallpaper manifest is loaded.
    // Resolve them once instead of checking the dictionary on every DWM frame.
    private readonly List<(WallpaperFile Meta, TranslateTransform Translation, RotateTransform Rotation, string Part)> planes = new();
    private readonly ScaleTransform cover = new();
    private readonly TranslateTransform offset = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private TimeSpan lastFrame;
    private double lastTime, x, y;
    private bool playbackPaused;
    private readonly Func<System.Drawing.Point> pointer;

    public ParallaxView(MonitorInfo monitor, WallpaperManifest manifest, WallpaperLayer[] layers, Func<System.Drawing.Point>? pointer = null,
        bool playbackPaused = false)
    {
        this.monitor = monitor; this.manifest = manifest;
        this.pointer = pointer ?? (() => System.Windows.Forms.Cursor.Position);
        this.playbackPaused = playbackPaused;
        ClipToBounds = true; Background = System.Windows.Media.Brushes.Black; Children.Add(canvas);
        canvas.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        canvas.VerticalAlignment = System.Windows.VerticalAlignment.Top;
        canvas.Width = manifest.Width; canvas.Height = manifest.Height;
        var scene = new TransformGroup(); scene.Children.Add(cover); scene.Children.Add(offset);
        canvas.RenderTransform = scene;
        foreach (var (file, image) in layers)
        {
            var plane = new System.Windows.Controls.Image { Source = image, Width = manifest.Width, Height = manifest.Height,
                Stretch = Stretch.Fill, CacheMode = new BitmapCache() };
            var transforms = new TransformGroup();
            transforms.Children.Add(new ScaleTransform(file.Scale, file.Scale, manifest.Width / 2.0, manifest.Height / 2.0));
            transforms.Children.Add(new RotateTransform(file.Rotation, manifest.Width / 2.0, manifest.Height / 2.0));
            var rotation = new RotateTransform(0, manifest.Width / 2.0, manifest.Height / 2.0);
            var part = manifest.Rig is { Enabled: true } enabledRig && file.Role != "Background"
                ? RigMotion.Part(file, enabledRig) : "fixed";
            if (manifest.Rig is { Enabled: true } rig)
            {
                rotation.CenterX = manifest.Width * rig.PivotX / 100.0;
                rotation.CenterY = manifest.Height * RigMotion.PivotY(part, rig) / 100.0;
            }
            transforms.Children.Add(rotation);
            var translation = new TranslateTransform(file.X, file.Y); transforms.Children.Add(translation);
            plane.RenderTransform = transforms; canvas.Children.Add(plane); planes.Add((file, translation, rotation, part));
        }
        SizeChanged += (_, _) => Fit();
        Loaded += (_, _) => UpdateRenderingSubscription();
        Unloaded += (_, _) => CompositionTarget.Rendering -= Render;
    }

    internal void SetPlaybackPaused(bool paused)
    {
        if (playbackPaused == paused) return;
        playbackPaused = paused;
        UpdateRenderingSubscription();
    }

    private void UpdateRenderingSubscription()
    {
        CompositionTarget.Rendering -= Render;
        if (playbackPaused || !IsLoaded || !manifest.UsesMouse) return;
        lastTime = clock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += Render;
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
        foreach (var (meta, translation, rotation, part) in planes)
        {
            var moving = meta.Role != "Background";
            var rig = manifest.Rig;
            var shift = rig is { Enabled: true } && moving
                ? RigMotion.BodyShift(part, rig, x, y, manifest.Width, manifest.Height) : (0.0, 0.0);
            var faceShift = rig is { Enabled: true } && moving
                ? RigMotion.FaceShift(part, rig, x, y, manifest.Width, manifest.Height) : (0.0, 0.0);
            translation.X = meta.X + (moving ? x * meta.MovementX : 0) + shift.Item1 + faceShift.Item1;
            translation.Y = meta.Y + (moving ? y * meta.MovementY : 0) + shift.Item2 + faceShift.Item2;
            rotation.Angle = rig is { Enabled: true } && moving
                ? RigMotion.AngleDegrees(part, rig, x) : 0;
        }
    }

    public void Dispose() { CompositionTarget.Rendering -= Render; Children.Clear(); planes.Clear(); }
}
