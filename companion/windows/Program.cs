using ImageMagick;

namespace UpscaleLab.Companion;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        // Published single-file EXEs install per user. --run also lets a local
        // tester launch the executable directly without installing anything.
        var install = args.Contains("--install");
#if !DEBUG
        install |= !args.Contains("--run") && !CompanionInstaller.ForCurrentUser().IsInstalled(Environment.ProcessPath!);
#endif
        if (install) { Application.Run(new InstallForm()); return; }
        using var mutex = new Mutex(true, @"Local\UpscaleLabConnect-" + CompanionConfiguration.ServerId, out var first);
        if (!first) { MessageBox.Show("Upscale Lab Connect가 이미 실행 중입니다. 트레이에서 열어 주세요."); return; }
        if (!PrivacyConsent.Current.Accepted) {
            using var consent = new PrivacyForm();
            if (consent.ShowDialog() != DialogResult.OK) return;
        }
        ResourceLimits.Memory = 256UL * 1024 * 1024;
        ResourceLimits.Disk = 256UL * 1024 * 1024; ResourceLimits.Thread = 2;
        Application.Run(new MainForm());
    }
}
