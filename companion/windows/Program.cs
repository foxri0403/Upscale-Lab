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
        var background = args.Contains("--background");
#if !DEBUG
        install |= !args.Contains("--run") && !CompanionInstaller.ForCurrentUser().IsInstalled(Environment.ProcessPath!);
#endif
        if (install)
        {
            using var installerLock = new Mutex(false, @"Local\" + InstanceActivation.Name + "-installer");
            try
            {
                try { if (!installerLock.WaitOne(TimeSpan.FromSeconds(10))) return; }
                catch (AbandonedMutexException) { }
                try
                {
                    var installer = CompanionInstaller.ForCurrentUser();
                    if (installer.OpenOrUpgrade(Environment.ProcessPath!)) return;
                    Application.Run(new InstallForm());
                }
                finally { installerLock.ReleaseMutex(); }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                MessageBox.Show("업데이트를 적용하지 못했습니다. " + error.Message, CompanionConfiguration.AppName);
            }
            return;
        }
        using var mutex = new Mutex(true, @"Local\" + InstanceActivation.Name, out var first);
        using var activation = new InstanceActivation(InstanceActivation.Name);
        if (!first) {
            if (background) return;
            if (!activation.Activate(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult())
                MessageBox.Show("실행 중인 앱의 응답을 기다리고 있습니다. 잠시 후 바로가기를 다시 눌러 주세요.");
            return;
        }
        using var dispatcher = new Control(); _ = dispatcher.Handle;
        Form? foreground = null;
        activation.Listen(() => { if (!dispatcher.IsDisposed) dispatcher.BeginInvoke((Action)(() => {
            if (foreground is MainForm main) main.ShowMain();
            else if (foreground is { IsDisposed: false }) { foreground.Show(); foreground.Activate(); }
        })); }, () => { if (!dispatcher.IsDisposed) dispatcher.BeginInvoke((Action)(async () => {
            if (foreground is MainForm main) await main.ExitAsync();
            else foreground?.Close();
        })); });
        if (!PrivacyConsent.Current.Accepted) {
            if (background) return;
            using var consent = new PrivacyForm();
            foreground = consent;
            if (consent.ShowDialog() != DialogResult.OK) return;
        }
        ResourceLimits.Memory = 256UL * 1024 * 1024;
        ResourceLimits.Disk = 256UL * 1024 * 1024; ResourceLimits.Thread = 2;
        using var mainForm = new MainForm(); foreground = mainForm;
        using var context = new ApplicationContext();
        mainForm.FormClosed += (_, _) => context.ExitThread();
        async void Start(object? sender, EventArgs e)
        {
            Application.Idle -= Start;
            await mainForm.InitializeAsync(background);
        }
        Application.Idle += Start;
        Application.Run(context);
    }
}
