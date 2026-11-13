using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace UpscaleLab.Companion;

internal enum ForegroundWindowMode { None, Active, Maximized, FullScreen }

internal sealed record PlaybackRules(bool PauseWhenActive = false, bool PauseWhenMaximized = true,
    bool PauseWhenFullScreen = true, bool PauseWhenDisplaySleeps = true)
{
    private static string SettingsFile => Path.Combine(SecureStore.Root, "profiles", CompanionConfiguration.ServerId, "playback.json");

    public bool ShouldPause(Rectangle monitor, ForegroundWindow foreground, bool displaySleeping)
    {
        if (displaySleeping) return PauseWhenDisplaySleeps;
        if (monitor != foreground.Monitor) return false;
        return foreground.Mode switch
        {
            ForegroundWindowMode.Active => PauseWhenActive,
            ForegroundWindowMode.Maximized => PauseWhenMaximized,
            ForegroundWindowMode.FullScreen => PauseWhenFullScreen,
            _ => false
        };
    }

    public static PlaybackRules Load()
    {
        try { return JsonSerializer.Deserialize<PlaybackRules>(File.ReadAllText(SettingsFile)) ?? new(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        var temp = SettingsFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this));
        File.Move(temp, SettingsFile, true);
    }
}

internal readonly record struct ForegroundWindow(Rectangle Monitor, ForegroundWindowMode Mode)
{
    private const int CaptionStyle = 0x00c00000;
    private const int ExtendedFrameBounds = 9;

    public static ForegroundWindow Read()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || IsIconic(window) || window == GetShellWindow()) return default;
        GetWindowThreadProcessId(window, out var processId);
        if (processId == Environment.ProcessId) return default;

        var className = new StringBuilder(64);
        GetClassName(window, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return default;
        if (DwmGetWindowAttribute(window, ExtendedFrameBounds, out var rect, Marshal.SizeOf<WindowRect>()) != 0 &&
            !GetWindowRect(window, out rect)) return default;

        var monitor = Screen.FromHandle(window).Bounds;
        var hasCaption = (GetWindowLong(window, -16) & CaptionStyle) == CaptionStyle;
        return new(monitor, Classify(rect.Bounds, monitor, IsZoomed(window), hasCaption));
    }

    internal static ForegroundWindowMode Classify(Rectangle window, Rectangle monitor, bool maximized, bool hasCaption)
    {
        if (window.Contains(monitor) && (!maximized || !hasCaption)) return ForegroundWindowMode.FullScreen;
        return maximized ? ForegroundWindowMode.Maximized : ForegroundWindowMode.Active;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rectangle Bounds => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int length);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out WindowRect bounds);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out WindowRect bounds, int size);
}

internal sealed class DisplayPowerNotifications
{
    private const int PowerBroadcast = 0x0218;
    private const int PowerSettingChange = 0x8013;
    private static readonly Guid SessionDisplayStatus = new("2b84c20e-ad23-4ddf-93db-05ffbd7efca5");
    private IntPtr registration;
    private bool displayOff, suspended;

    public bool Sleeping => displayOff || suspended;

    public void Register(IntPtr window)
    {
        var setting = SessionDisplayStatus;
        registration = RegisterPowerSettingNotification(window, ref setting, 0);
    }

    public void Unregister()
    {
        if (registration != IntPtr.Zero) UnregisterPowerSettingNotification(registration);
        registration = IntPtr.Zero;
    }

    public bool HandleMessage(Message message)
    {
        if (message.Msg != PowerBroadcast) return false;
        var wasSleeping = Sleeping;
        switch (message.WParam.ToInt32())
        {
            case 4: // PBT_APMSUSPEND
                suspended = true;
                break;
            case 7: // PBT_APMRESUMESUSPEND
            case 18: // PBT_APMRESUMEAUTOMATIC
                suspended = false;
                break;
            case PowerSettingChange when message.LParam != IntPtr.Zero &&
                Marshal.PtrToStructure<Guid>(message.LParam) == SessionDisplayStatus && Marshal.ReadInt32(message.LParam, 16) == 4:
                displayOff = Marshal.ReadInt32(message.LParam, 20) == 0;
                break;
        }
        return wasSleeping != Sleeping;
    }

    [DllImport("user32.dll")] private static extern IntPtr RegisterPowerSettingNotification(IntPtr window, ref Guid setting, uint flags);
    [DllImport("user32.dll")] private static extern bool UnregisterPowerSettingNotification(IntPtr registration);
}
