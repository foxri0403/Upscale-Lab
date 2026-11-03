using System.Runtime.InteropServices;

namespace UpscaleLab.Companion;

internal sealed class CompanionInstaller(string installDirectory, string startMenuDirectory, string desktopDirectory,
    Action<string, string> createShortcut, Action? recordConsent = null)
{
    internal static string ShortcutName => CompanionConfiguration.AppName + ".lnk";
    internal string Executable => Path.Combine(installDirectory, "UpscaleLab.Connect.exe");
    internal static CompanionInstaller ForCurrentUser() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", CompanionConfiguration.AppName),
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), CreateShortcut);

    internal bool IsInstalled(string source) => string.Equals(Path.GetFullPath(source), Path.GetFullPath(Executable), StringComparison.OrdinalIgnoreCase);

    internal string Install(string source, bool desktopShortcut, bool startMenuShortcut, bool privacyAccepted)
    {
        if (!privacyAccepted) throw new InvalidOperationException("기기·화면 정보 수집·이용 동의가 필요합니다.");
        Directory.CreateDirectory(installDirectory);
        if (!IsInstalled(source)) File.Copy(source, Executable, overwrite: true);
        if (startMenuShortcut) {
            Directory.CreateDirectory(startMenuDirectory);
            createShortcut(Path.Combine(startMenuDirectory, ShortcutName), Executable);
        }
        if (desktopShortcut) {
            Directory.CreateDirectory(desktopDirectory);
            createShortcut(Path.Combine(desktopDirectory, ShortcutName), Executable);
        }
        (recordConsent ?? PrivacyConsent.Current.Accept)();
        return Executable;
    }

    private static void CreateShortcut(string path, string target)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows 바로 가기를 만들 수 없습니다.");
        dynamic shell = Activator.CreateInstance(type)!;
        try {
            dynamic shortcut = shell.CreateShortcut(path);
            try {
                shortcut.TargetPath = target; shortcut.Arguments = "--run";
                shortcut.WorkingDirectory = Path.GetDirectoryName(target);
                shortcut.IconLocation = target + ",0";
                shortcut.Description = "Upscale Lab Connect"; shortcut.Save();
            } finally { Marshal.FinalReleaseComObject(shortcut); }
        } finally { Marshal.FinalReleaseComObject(shell); }
    }
}
