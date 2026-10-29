using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace UpscaleLab.Companion;

internal record MonitorInfo(string NativeId, string Name, int Width, int Height, double Scale, int Rotation,
    int OffsetX, int OffsetY, bool IsPrimary, bool IsConnected, double? RefreshRateHz)
{
    [System.Text.Json.Serialization.JsonIgnore] public string MonitorPath { get; init; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public Rectangle Bounds => new(OffsetX, OffsetY, Width, Height);
}
internal static class DisplayInventory
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct Rational { public uint Numerator, Denominator; }
    [StructLayout(LayoutKind.Sequential)] private struct PathSource { public Luid Adapter; public uint Id, Mode, Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct PathTarget
    { public Luid Adapter; public uint Id, Mode; public int Technology; public uint Rotation, Scaling; public Rational Rate; public uint Scanline; public int Available; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct DisplayPath { public PathSource Source; public PathTarget Target; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct Header { public uint Type, Size; public Luid Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct SourceName
    { public Header Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct TargetName
    { public Header Header; public uint Flags, Technology; public ushort Manufacturer, Product; public uint Connector;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Name;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Path; }
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] DisplayPath[] pathArray, ref uint modes, IntPtr modeArray, IntPtr topology);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int DeviceInfo(ref SourceName name);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] private static extern int DeviceInfo(ref TargetName name);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    internal static IDesktopWallpaper Desktop() => (IDesktopWallpaper)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"))!)!;
    private static List<(string Gdi, string Path, string Name, double? Rate, int Rotation)> Modes()
    {
        var result = new List<(string, string, string, double?, int)>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (GetDisplayConfigBufferSizes(2, out var count, out var modeCount) != 0) return result;
            var paths = new DisplayPath[count]; var modes = Marshal.AllocHGlobal(checked((int)modeCount * 64));
            try
            {
                var code = QueryDisplayConfig(2, ref count, paths, ref modeCount, modes, IntPtr.Zero);
                if (code == 122) continue;
                if (code != 0) return result;
                foreach (var path in paths.Take((int)count))
                {
                    var source = new SourceName { Header = new Header { Type = 1, Size = (uint)Marshal.SizeOf<SourceName>(), Adapter = path.Source.Adapter, Id = path.Source.Id } };
                    var target = new TargetName { Header = new Header { Type = 2, Size = (uint)Marshal.SizeOf<TargetName>(), Adapter = path.Target.Adapter, Id = path.Target.Id } };
                    if (DeviceInfo(ref source) != 0 || DeviceInfo(ref target) != 0) continue;
                    var rate = path.Target.Rate.Denominator == 0 ? (double?)null : (double)path.Target.Rate.Numerator / path.Target.Rate.Denominator;
                    result.Add((source.Name, target.Path, target.Name, rate is >= 1 and <= 1000 ? rate : null, (int)(path.Target.Rotation - 1) * 90));
                }
                return result;
            }
            finally { Marshal.FreeHGlobal(modes); }
        }
        return result;
    }
    public static MonitorInfo[] Read()
    {
        var modes = Modes(); var desktop = Desktop(); var result = new List<MonitorInfo>(); var primarySeen = false;
        try
        {
            desktop.GetMonitorDevicePathCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                desktop.GetMonitorDevicePathAt(i, out var path); desktop.GetMonitorRECT(path, out var rect);
                if (rect.Right <= rect.Left || rect.Bottom <= rect.Top) continue;
                var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                var screen = Screen.AllScreens.FirstOrDefault(s => s.Bounds == bounds);
                var mode = modes.FirstOrDefault(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase));
                if (mode.Gdi is null) mode = modes.FirstOrDefault(m => m.Gdi == screen?.DeviceName);
                var dpi = GetDpiForMonitor(MonitorFromPoint(new Point(rect.Left + 1, rect.Top + 1), 2), 0, out var dx, out _) == 0 ? dx / 96.0 : 1;
                var primary = screen?.Primary == true && !primarySeen; primarySeen |= primary;
                var id = "monitor-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..32];
                result.Add(new(id, string.IsNullOrWhiteSpace(mode.Name) ? $"모니터 {i + 1}" : mode.Name,
                    bounds.Width, bounds.Height, Math.Clamp(dpi, .25, 8), mode.Rotation, bounds.Left, bounds.Top, primary, true, mode.Rate) { MonitorPath = path });
            }
            if (result.Count > 16) throw new InvalidOperationException("최대 16개 화면을 연결할 수 있습니다.");
            return result.ToArray();
        }
        finally { Marshal.FinalReleaseComObject(desktop); }
    }
}

[ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDesktopWallpaper
{
    void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitor, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
    void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitor, [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);
    void GetMonitorDevicePathAt(uint index, [MarshalAs(UnmanagedType.LPWStr)] out string monitor);
    void GetMonitorDevicePathCount(out uint count);
    void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitor, out DisplayInventory.Rect rect);
    void SetBackgroundColor(uint color);
    void GetBackgroundColor(out uint color);
    void SetPosition(int position);
    void GetPosition(out int position);
}
