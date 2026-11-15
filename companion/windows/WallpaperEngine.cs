using System.Runtime.InteropServices;
using ImageMagick;

namespace UpscaleLab.Companion;

internal sealed class WallpaperEngine : IDisposable
{
    private readonly Dictionary<string, LiveDesktop> live = new();
    private PlaybackRules playbackRules = new();
    private bool displaySleeping;
    public bool SupportsLive => DesktopHost.Find(true) != IntPtr.Zero;
    public bool IsLive(string nativeId) => live.TryGetValue(nativeId, out var value) && !value.IsDisposed && value.Attached;
    public void UpdatePlayback(PlaybackRules rules, bool sleeping)
    {
        playbackRules = rules;
        displaySleeping = sleeping;
        if (live.Count == 0) return;
        var foreground = ForegroundWindow.Read();
        foreach (var desktop in live.Values)
        {
            if (desktop.IsDisposed) continue;
            desktop.SetPlaybackPaused(rules.ShouldPause(desktop.MonitorBounds, foreground, sleeping));
            if (!desktop.Attached)
            {
                try { desktop.Attach(allowHostCreation: false); }
                catch (System.ComponentModel.Win32Exception) { /* Explorer may still be updating the desktop. */ }
            }
        }
    }

    public async Task Apply(MonitorInfo monitor, WallpaperManifest manifest, Func<Task> confirm, CancellationToken ct)
    {
        var path = Path.Combine(Path.GetDirectoryName(manifest.Files[0].Source)!, $"desktop-{monitor.Width}x{monitor.Height}.png");
        var layers = await Task.Run(() =>
        {
            if (manifest.Mode == "Live2D") return WallpaperLayers.Load(manifest, monitor, path, ct);
            using var image = RasterImage.Read(manifest.Files[0]);
            if (image.Width > 32768 || image.Height > 32768) throw new InvalidDataException("이미지 크기가 너무 큽니다.");
            image.AutoOrient(); image.Resize(new MagickGeometry(8192, 8192) { Greater = true }); image.Write(path, MagickFormat.Png);
            return null;
        }, ct);
        await confirm();
        ct.ThrowIfCancellationRequested();
        LiveDesktop? next = null;
        try
        {
            if (layers is not null)
            {
                var paused = playbackRules.ShouldPause(monitor.Bounds, ForegroundWindow.Read(), displaySleeping);
                next = new LiveDesktop(monitor, manifest, layers, paused);
                if (!next.Attach()) throw new InvalidOperationException("Windows 바탕화면에 동적 배경화면을 연결하지 못했습니다.");
            }
            var desktop = DisplayInventory.Desktop();
            try
            {
                desktop.GetPosition(out var position);
                desktop.GetWallpaper(monitor.MonitorPath, out var previous);
                var changed = false;
                if (position != 4) desktop.SetPosition(4); // DWPOS_FILL
                try
                {
                    if (layers is null || !string.Equals(previous, path, StringComparison.OrdinalIgnoreCase))
                    {
                        desktop.SetWallpaper(monitor.MonitorPath, path);
                        changed = true;
                    }
                    desktop.GetWallpaper(monitor.MonitorPath, out var actual);
                    if (!string.Equals(actual, path, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Windows 배경화면 적용을 확인하지 못했습니다.");
                    if (next != null && !next.Attach())
                        throw new InvalidOperationException("동적 배경화면 연결이 해제되었습니다.");
                }
                catch
                {
                    if (changed)
                        try { desktop.SetWallpaper(monitor.MonitorPath, previous); } catch (COMException) { }
                    throw;
                }
            }
            finally { Marshal.FinalReleaseComObject(desktop); }
            if (live.Remove(monitor.NativeId, out var old)) old.Dispose();
            if (next != null) { live[monitor.NativeId] = next; next = null; }
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
            if (matches && live.TryGetValue(monitor.NativeId, out var active) && !active.IsDisposed) active.Attach();
            if (!matches && live.Remove(monitor.NativeId, out var old)) old.Dispose();
            return matches; }
        catch (COMException) { return false; }
        finally { Marshal.FinalReleaseComObject(desktop); }
    }
    public void Dispose() { foreach (var form in live.Values) form.Dispose(); live.Clear(); }

    private sealed class LiveDesktop : Form
    {
        private readonly MonitorInfo monitor;
        private readonly ParallaxView view;
        private readonly System.Windows.Forms.Integration.ElementHost host;
        public bool Attached => IsHandleCreated && DesktopHost.IsAttached(Handle);
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var value = base.CreateParams; value.ExStyle |= 0x080800a0; return value; } }
        public LiveDesktop(MonitorInfo monitor, WallpaperManifest manifest, WallpaperLayer[] layers, bool playbackPaused)
        {
            this.monitor = monitor;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; DoubleBuffered = true;
            view = new ParallaxView(monitor, manifest, layers, playbackPaused: playbackPaused);
            host = new System.Windows.Forms.Integration.ElementHost { Dock = DockStyle.Fill, Child = view };
            Controls.Add(host);
        }
        public Rectangle MonitorBounds => monitor.Bounds;
        public void SetPlaybackPaused(bool paused) => view.SetPlaybackPaused(paused);
        public bool Attach(bool allowHostCreation = true)
        {
            if (Attached && Visible) return true;
            var desktop = DesktopHost.Find(false);
            if (desktop == IntPtr.Zero && allowHostCreation) desktop = DesktopHost.Find(true);
            if (desktop == IntPtr.Zero) return false;
            DesktopHost.Parent(Handle, desktop, monitor.Bounds);
            if (!Visible) Show();
            if (!Attached) DesktopHost.Parent(Handle, desktop, monitor.Bounds);
            return Attached;
        }
        protected override void Dispose(bool disposing) { if (disposing) { view?.Dispose(); host?.Dispose(); } base.Dispose(disposing); }
    }
}

internal static class DesktopHost
{
    private const uint NextSibling = 2;
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
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
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
    public static bool IsAttached(IntPtr window)
    {
        var parent = GetParent(window);
        if (parent == IntPtr.Zero || !IsWindow(parent) || parent != Find(false)) return false;
        var wallpaper = FindWindowEx(parent, IntPtr.Zero, "WorkerW", null);
        if (wallpaper == IntPtr.Zero || wallpaper == window || !IsWindowVisible(wallpaper)) return true;
        // Explorer can move the system wallpaper above our window after SetWallpaper.
        for (var next = GetWindow(window, NextSibling); next != IntPtr.Zero; next = GetWindow(next, NextSibling))
            if (next == wallpaper) return true;
        return false;
    }
    public static void Parent(IntPtr child, IntPtr host, Rectangle bounds)
    {
        if (GetParent(child) != host)
        {
            SetStyle(child, -16, (GetStyle(child, -16) & ~unchecked((int)0x80000000)) | 0x40000000);
            SetStyle(child, -20, GetStyle(child, -20) | 0x00080000);
            SetParent(child, host);
            if (GetParent(child) != host || !SetLayeredWindowAttributes(child, 0, 255, 2))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "동적 배경화면 창을 연결하지 못했습니다.");
        }
        var points = new[] { bounds.Location }; MapWindowPoints(IntPtr.Zero, host, points, 1);
        var icons = FindWindowEx(host, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (!SetWindowPos(child, icons, points[0].X, points[0].Y, bounds.Width, bounds.Height, 0x0030))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var wallpaper = FindWindowEx(host, IntPtr.Zero, "WorkerW", null);
        if (icons != IntPtr.Zero && wallpaper != IntPtr.Zero && !SetWindowPos(wallpaper, child, 0, 0, 0, 0, 0x0013))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
}
