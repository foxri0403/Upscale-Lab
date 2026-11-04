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
        using var mutex = new Mutex(true, @"Local\" + InstanceActivation.Name, out var first);
        using var activation = new InstanceActivation(InstanceActivation.Name);
        if (!first) {
            if (!activation.Activate(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult())
                MessageBox.Show("실행 중인 앱의 응답을 기다리고 있습니다. 잠시 후 바로가기를 다시 눌러 주세요.");
            return;
        }
        using var dispatcher = new Control(); _ = dispatcher.Handle;
        Form? foreground = null;
        activation.Listen(() => { if (!dispatcher.IsDisposed) dispatcher.BeginInvoke((Action)(() => {
            if (foreground is MainForm main) main.ShowMain();
            else if (foreground is { IsDisposed: false }) { foreground.Show(); foreground.Activate(); }
        })); });
        if (!PrivacyConsent.Current.Accepted) {
            using var consent = new PrivacyForm();
            foreground = consent;
            if (consent.ShowDialog() != DialogResult.OK) return;
        }
        ResourceLimits.Memory = 256UL * 1024 * 1024;
        ResourceLimits.Disk = 256UL * 1024 * 1024; ResourceLimits.Thread = 2;
        using var mainForm = new MainForm(); foreground = mainForm;
        Application.Run(mainForm);
    }
}
