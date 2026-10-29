using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using ImageMagick;

namespace UpscaleLab.Companion;

internal sealed class WallpaperEngine : IDisposable
{
    private readonly Dictionary<string, LiveDesktop> live = new();
    public bool SupportsLive => DesktopHost.Find(true) != IntPtr.Zero;
    public bool IsLive(string nativeId) => live.TryGetValue(nativeId, out var value) && !value.IsDisposed && value.Attached;
    public void Apply(MonitorInfo monitor, WallpaperManifest manifest)
    {
        LiveDesktop? next = null;
        if (manifest.Mode == "Live2D")
        {
            next = new LiveDesktop(monitor, manifest);
            if (!next.Attach()) { next.Dispose(); throw new InvalidOperationException("Windows 바탕화면에 Live 2D를 연결하지 못했습니다."); }
        }
        var path = Path.Combine(Path.GetDirectoryName(manifest.Files[0].Source)!, $"desktop-{monitor.Width}x{monitor.Height}.png");
        try
        {
            if (manifest.Mode == "Static")
            {
                using var image = RasterImage.Read(manifest.Files[0]);
                if (image.Width > 32768 || image.Height > 32768) throw new InvalidDataException("이미지 크기가 너무 큽니다.");
                image.AutoOrient(); image.Resize(new MagickGeometry(8192, 8192) { Greater = true }); image.Write(path, MagickFormat.Png);
            }
            else next!.SaveNeutral(path);
            var desktop = DisplayInventory.Desktop();
            try
            {
                desktop.SetPosition(4); // DWPOS_FILL, applied per physical monitor.
                desktop.SetWallpaper(monitor.MonitorPath, path);
                desktop.GetWallpaper(monitor.MonitorPath, out var actual);
                if (!string.Equals(Path.GetFullPath(actual), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Windows 배경화면 적용을 확인하지 못했습니다.");
            }
            finally { Marshal.FinalReleaseComObject(desktop); }
            if (live.Remove(monitor.NativeId, out var old)) old.Dispose();
            if (next != null)
            {
                if (!next.Attach()) throw new InvalidOperationException("Live 2D 바탕화면 연결이 해제되었습니다.");
                live[monitor.NativeId] = next; next = null;
            }
            CurrentPath = path;
        }
        finally { next?.Dispose(); }
    }
    public string CurrentPath { get; private set; } = "";
    public void Clear(MonitorInfo monitor)
    {
        var desktop = DisplayInventory.Desktop();
        try
        {
            WallpaperRemoval.Clear(monitor.MonitorPath, desktop.SetWallpaper,
                path => { desktop.GetWallpaper(path, out var value); return value; },
                () => { if (live.Remove(monitor.NativeId, out var old)) old.Dispose(); });
            CurrentPath = "";
        }
        finally { Marshal.FinalReleaseComObject(desktop); }
    }
    public bool IsClear(MonitorInfo monitor)
    {
        var desktop = DisplayInventory.Desktop();
        try { desktop.GetWallpaper(monitor.MonitorPath, out var path); return string.IsNullOrEmpty(path) && !IsLive(monitor.NativeId); }
        catch (COMException) { return false; }
        finally { Marshal.FinalReleaseComObject(desktop); }
    }
    public bool Matches(MonitorInfo monitor, AppliedWallpaper applied)
    {
        var desktop = DisplayInventory.Desktop();
        try { desktop.GetWallpaper(monitor.MonitorPath, out var path);
            var matches = string.Equals(path, applied.Path, StringComparison.OrdinalIgnoreCase);
            if (matches && live.TryGetValue(monitor.NativeId, out var active)) active.Attach();
            if (!matches && live.Remove(monitor.NativeId, out var old)) old.Dispose();
            return matches; }
        catch (COMException) { return false; }
        finally { Marshal.FinalReleaseComObject(desktop); }
    }
    public void Dispose() { foreach (var form in live.Values) form.Dispose(); live.Clear(); }

    private sealed class LiveDesktop : Form
    {
        private readonly MonitorInfo monitor;
        private readonly WallpaperManifest manifest;
        private readonly List<(WallpaperFile Meta, Bitmap Image)> layers = new();
        private readonly ParallaxView view;
        private readonly System.Windows.Forms.Integration.ElementHost host;
        public bool Attached => DesktopHost.IsAttached(Handle);
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var value = base.CreateParams; value.ExStyle |= 0x080800a0; return value; } }
        public LiveDesktop(MonitorInfo monitor, WallpaperManifest manifest)
        {
            this.monitor = monitor; this.manifest = manifest;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; DoubleBuffered = true;
            var maxPixels = Math.Min(16_000_000, 48_000_000 / manifest.Files.Length);
            try
            {
                foreach (var file in manifest.Files.OrderBy(f => f.Order))
                {
                    using var image = RasterImage.Read(file);
                    if (image.Width != manifest.Width || image.Height != manifest.Height)
                        throw new InvalidDataException("Live 2D 레이어 크기가 일치하지 않습니다.");
                    var factor = Math.Min(1, Math.Sqrt((double)maxPixels / (image.Width * (double)image.Height)));
                    image.Resize((uint)Math.Max(1, Math.Round(image.Width * factor)), (uint)Math.Max(1, Math.Round(image.Height * factor)));
                    using var bytes = new MemoryStream(image.ToByteArray(MagickFormat.Png));
                    using var decoded = Image.FromStream(bytes);
                    layers.Add((file, new Bitmap(decoded)));
                }
            }
            catch { foreach (var layer in layers) layer.Image.Dispose(); throw; }
            view = new ParallaxView(monitor, manifest);
            host = new System.Windows.Forms.Integration.ElementHost { Dock = DockStyle.Fill, Child = view };
            Controls.Add(host);
        }
        public bool Attach()
        {
            var host = DesktopHost.Find(true); if (host == IntPtr.Zero) return false;
            if (!IsHandleCreated) CreateControl();
            DesktopHost.Parent(Handle, host, monitor.Bounds); Show();
            DesktopHost.Parent(Handle, host, monitor.Bounds); return Attached;
        }
        private void Render(Graphics graphics, int width, int height, double inputX, double inputY)
        {
            graphics.Clear(Color.Black); graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var cover = Math.Max((double)width / manifest.Width, (double)height / manifest.Height);
            foreach (var (meta, bitmap) in layers)
            {
                var state = graphics.Save(); var moving = meta.Role != "Background";
                graphics.TranslateTransform((float)(width / 2.0 + (meta.X - (moving ? inputX * meta.MovementX : 0)) * cover),
                    (float)(height / 2.0 + (meta.Y + (moving ? inputY * meta.MovementY : 0)) * cover));
                graphics.RotateTransform((float)meta.Rotation); graphics.ScaleTransform((float)(cover * meta.Scale), (float)(cover * meta.Scale));
                graphics.DrawImage(bitmap, new RectangleF(-manifest.Width / 2f, -manifest.Height / 2f, manifest.Width, manifest.Height));
                graphics.Restore(state);
            }
        }
        public void SaveNeutral(string path)
        {
            var factor = Math.Min(1, 4096.0 / Math.Max(monitor.Width, monitor.Height));
            using var bitmap = new Bitmap(Math.Max(1, (int)(monitor.Width * factor)), Math.Max(1, (int)(monitor.Height * factor)));
            using var graphics = Graphics.FromImage(bitmap); Render(graphics, bitmap.Width, bitmap.Height, 0, 0); bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        protected override void Dispose(bool disposing) { if (disposing) { view?.Dispose(); host?.Dispose(); foreach (var layer in layers) layer.Image.Dispose(); layers.Clear(); } base.Dispose(disposing); }
    }
}

internal static class DesktopHost
{
    private delegate bool EnumProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr child);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetStyle(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetStyle(IntPtr window, int index, int value);
    [DllImport("user32.dll")] private static extern int MapWindowPoints(IntPtr from, IntPtr to, [In, Out] Point[] points, uint count);
    [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool redraw);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(IntPtr window, uint color, byte alpha, uint flags);
    public static IntPtr Find(bool create)
    {
        var program = FindWindow("Progman", null); if (program == IntPtr.Zero) return IntPtr.Zero;
        if (create) SendMessageTimeout(program, 0x052c, IntPtr.Zero, IntPtr.Zero, 2, 1000, out _);
        // Raised desktops put the system wallpaper in a child WorkerW. Nesting WPF inside
        // that WorkerW leaves it behind the static image. Use a layered sibling below icons.
        if (FindWindowEx(program, IntPtr.Zero, "WorkerW", null) != IntPtr.Zero &&
            FindWindowEx(program, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return program;
        var worker = IntPtr.Zero;
        EnumWindows((top, _) => {
            if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero) return true;
            worker = FindWindowEx(top, IntPtr.Zero, "WorkerW", null);
            if (worker == IntPtr.Zero) worker = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            return worker == IntPtr.Zero;
        }, IntPtr.Zero);
        return worker;
    }
    public static bool IsAttached(IntPtr window) { var parent = GetParent(window); return parent != IntPtr.Zero && IsWindow(parent) && parent == Find(false); }
    public static void Parent(IntPtr child, IntPtr host, Rectangle bounds)
    {
        SetStyle(child, -16, (GetStyle(child, -16) & ~unchecked((int)0x80000000)) | 0x40000000);
        SetStyle(child, -20, GetStyle(child, -20) | 0x00080000);
        SetParent(child, host);
        if (GetParent(child) != host || !SetLayeredWindowAttributes(child, 0, 255, 2))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "동적 배경화면 창을 연결하지 못했습니다.");
        var points = new[] { bounds.Location }; MapWindowPoints(IntPtr.Zero, host, points, 1);
        var icons = FindWindowEx(host, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (!SetWindowPos(child, icons, points[0].X, points[0].Y, bounds.Width, bounds.Height, 0x0030))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var wallpaper = FindWindowEx(host, IntPtr.Zero, "WorkerW", null);
        if (icons != IntPtr.Zero && wallpaper != IntPtr.Zero) SetWindowPos(wallpaper, child, 0, 0, 0, 0, 0x0013);
    }
}
