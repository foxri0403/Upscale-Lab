using System.Diagnostics;
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

    internal Version? InstalledVersion => File.Exists(Executable) ? ReadVersion(Executable) : null;
    internal void OpenInstalled() => Process.Start(new ProcessStartInfo(Executable, "--run") { UseShellExecute = true });

    internal void Reinstall(string source, IProgress<string> progress)
    {
        if (InstalledVersion is not { } installed || IsInstalled(source) || ReadVersion(source) < installed)
            throw new InvalidOperationException("설치 파일 버전을 확인해 주세요.");
        ReplaceExecutable(source, StopInstalledInstance, progress);
    }

    internal static Version ReadVersion(string path)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        return new(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
    }

    internal void ReplaceExecutable(string source, Action stopInstalledInstance, IProgress<string>? progress = null)
    {
        var candidate = Path.Combine(installDirectory, $".{Guid.NewGuid():N}.exe");
        try
        {
            progress?.Report("새 설치 파일을 준비하고 있습니다.");
            File.Copy(source, candidate);
            progress?.Report("실행 중인 앱을 종료하고 있습니다.");
            stopInstalledInstance();
            progress?.Report("설치 파일을 교체하고 있습니다.");
            File.Move(candidate, Executable, overwrite: true);
        }
        finally { File.Delete(candidate); }
    }

    private void StopInstalledInstance()
    {
        using var current = Process.GetCurrentProcess();
        using var activation = new InstanceActivation(InstanceActivation.Name);
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Executable)))
        {
            using (process)
            {
                if (process.Id == current.Id || process.HasExited || process.SessionId != current.SessionId) continue;
                string? path;
                try { path = process.MainModule?.FileName; }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { continue; }
                if (path is null || !IsInstalled(path)) continue;
                activation.RequestExit(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                if (!process.WaitForExit(5000))
                {
                    // Versions that predate the update signal still save account state atomically.
                    try { process.Kill(); }
                    catch (InvalidOperationException) when (process.HasExited) { }
                    if (!process.WaitForExit(5000)) throw new IOException("기존 앱이 종료되지 않았습니다.");
                }
            }
        }
    }

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
