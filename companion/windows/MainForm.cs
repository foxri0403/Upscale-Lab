using Microsoft.Win32;

namespace UpscaleLab.Companion;

internal sealed class MainForm : Form
{
    private readonly CompanionController controller = new();
    private readonly TextBox identifier = new() { Dock = DockStyle.Fill, PlaceholderText = "아이디 또는 이메일" };
    private readonly TextBox password = new() { Dock = DockStyle.Fill, PlaceholderText = "비밀번호", UseSystemPasswordChar = true };
    private readonly CheckBox remember = new() { Text = "자동 로그인", AutoSize = true };
    private readonly CheckBox startup = new() { Text = "Windows 시작 시 실행", AutoSize = true };
    private readonly Button login = new() { Text = "로그인하고 기기 연결", Dock = DockStyle.Fill, AutoSize = true };
    private readonly Button sync = new() { Text = "동기화 켜기", AutoSize = true };
    private readonly Button reconnect = new() { Text = "기기 다시 연결", AutoSize = true };
    private readonly Button logout = new() { Text = "로그아웃", AutoSize = true };
    private readonly Label status = new() { AutoSize = true, ForeColor = AppBrand.Muted, Margin = new Padding(0, 16, 0, 16) };
    private readonly Label account = new() { AutoSize = true, Font = new Font("맑은 고딕", 11, FontStyle.Bold), Margin = new Padding(0, 12, 0, 16) };
    private readonly FlowLayoutPanel screens = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0, 12, 0, 12) };
    private readonly TableLayoutPanel credentials = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(18), BackColor = Color.White };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0, 12, 0, 0) };
    private readonly NotifyIcon tray = new() { Icon = AppBrand.Icon(), Text = "Upscale Lab Connect", Visible = true };
    private bool busy;
    private bool quitting;
    private string inventoryKey = "";

    public MainForm()
    {
        Text = CompanionConfiguration.AppName; Icon = AppBrand.Icon();
        AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(660, 760); MinimumSize = new Size(480, 540);
        Font = new Font("맑은 고딕", 10); ForeColor = AppBrand.Ink;
        BackColor = AppBrand.Background; StartPosition = FormStartPosition.CenterScreen;
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(24), ColumnCount = 1 };
        content.ColumnStyles.Add(new(SizeType.Percent, 100));
        var header = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, Height = 88, Margin = new Padding(0, 0, 0, 16) };
        header.ColumnStyles.Add(new(SizeType.Absolute, 76)); header.ColumnStyles.Add(new(SizeType.Percent, 100));
        header.Controls.Add(new PictureBox { Image = AppBrand.Image(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(60, 60) }, 0, 0);
        header.Controls.Add(new Label { Text = "Upscale Lab Connect\n내 화면을 웹과 연결하세요", Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 15, FontStyle.Bold), AutoEllipsis = false }, 1, 0);
        content.Controls.Add(header);
        var explanation = new Label { Text = "웹과 같은 계정으로 로그인하면 기기가 자동으로 연결됩니다.", AutoSize = true, ForeColor = AppBrand.Muted, Margin = new Padding(0, 0, 0, 20) };
        content.Controls.Add(explanation);
        foreach (var item in new[] { ("아이디 또는 이메일", identifier), ("비밀번호", password) }) {
            credentials.Controls.Add(new Label { Text = item.Item1, AutoSize = true, Margin = new Padding(0, 6, 0, 8) });
            item.Item2.Margin = new Padding(0, 0, 0, 16); credentials.Controls.Add(item.Item2);
        }
        credentials.Controls.Add(remember); login.Margin = new Padding(0, 16, 0, 0); credentials.Controls.Add(login);
        AppBrand.Style(login, true); AppBrand.Style(sync, true); AppBrand.Style(reconnect); AppBrand.Style(logout);
        content.Controls.Add(credentials); content.Controls.Add(account);
        content.Controls.Add(new Label { Text = "이 기기의 화면", AutoSize = true, Font = new Font(Font.FontFamily, 12, FontStyle.Bold), Margin = new Padding(0, 20, 0, 0) });
        content.Controls.Add(screens); content.Controls.Add(status);
        foreach (var button in new[] { sync, reconnect, logout }) { button.Margin = new Padding(0, 0, 10, 10); actions.Controls.Add(button); }
        content.Controls.Add(actions); content.Controls.Add(startup);
        var footer = new Label { Text = "배경화면은 웹의 기기 관리에서 선택해 주세요.\n창을 닫으면 트레이에서 계속 동기화합니다.\n\n연결된 화면 개수·해상도·주사율·OS와 적용 상태만 보고합니다.", AutoSize = true,
            ForeColor = AppBrand.Muted, Margin = new Padding(0, 22, 0, 0) };
        content.Controls.Add(footer); scroll.Controls.Add(content); Controls.Add(scroll);
        void FitContent() {
            var width = Math.Max(240, content.ClientSize.Width - content.Padding.Horizontal);
            status.MaximumSize = account.MaximumSize = explanation.MaximumSize = footer.MaximumSize = new Size(width, 0);
            var cardWidth = width >= 540 ? (width - 16) / 2 : width - 8;
            foreach (Control card in screens.Controls) { card.Width = cardWidth; if (card is Label label) label.MaximumSize = new Size(cardWidth, 0); }
        }
        content.SizeChanged += (_, _) => FitContent(); screens.ControlAdded += (_, _) => FitContent();
        controller.RefreshInventory();
        login.Click += async (_, _) => await Operation(async () => { try { await controller.SignIn(identifier.Text, password.Text, remember.Checked); } finally { password.Clear(); } });
        AcceptButton = login;
        sync.Click += async (_, _) => await Operation(async () => { if (controller.Enabled) await controller.Stop(); else await controller.Enable(); });
        reconnect.Click += async (_, _) => await Operation(controller.Reconnect);
        logout.Click += async (_, _) => await Operation(async () => { await controller.Logout(); Startup(false); startup.Checked = false; });
        startup.CheckedChanged += (_, _) => { if (!busy) Startup(startup.Checked); };
        controller.Changed += RefreshState;
        tray.DoubleClick += (_, _) => ShowMain();
        var menu = new ContextMenuStrip(); menu.Items.Add("열기", null, (_, _) => ShowMain());
        menu.Items.Add("종료", null, async (_, _) => { quitting = true; var enabled = controller.Enabled; await controller.Stop();
            var saved = SecureStore.Read(); if (saved != null && saved.Remember) SecureStore.Write(saved with { Enabled = enabled }); Close(); });
        tray.ContextMenuStrip = menu;
        FormClosing += (_, e) => { if (!quitting && controller.LoggedIn) { e.Cancel = true; Hide(); } };
        FormClosed += (_, _) => { var trayIcon = tray.Icon; tray.Dispose(); trayIcon?.Dispose(); controller.Dispose(); };
        Shown += async (_, _) => await Operation(controller.Restore);
        RefreshState();
    }
    private void ShowMain() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private async Task Operation(Func<Task> operation)
    {
        if (busy) return; busy = true; RefreshState();
        try { await operation(); }
        catch (Exception error) { status.Text = error.Message; }
        finally { busy = false; RefreshState(false); }
    }
    private void RefreshState() => RefreshState(true);
    private void RefreshState(bool replaceStatus)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke((Action)(() => RefreshState(replaceStatus))); return; }
        credentials.Visible = !controller.LoggedIn; actions.Visible = controller.LoggedIn;
        login.Enabled = !busy; sync.Enabled = reconnect.Enabled = logout.Enabled = !busy;
        sync.Text = controller.Enabled ? "동기화 끄기" : "동기화 켜기";
        startup.Enabled = controller.LoggedIn || remember.Checked;
        if (replaceStatus) status.Text = controller.Status;
        account.Text = controller.LoggedIn ? $"● 연결됨 · {controller.Account}\n{controller.DeviceName} · 화면 {controller.Monitors.Length}개" : "";
        account.Visible = controller.LoggedIn;
        var nextInventory = string.Join("|", controller.Monitors.Select(m => $"{m.Name}:{m.Width}:{m.Height}:{m.RefreshRateHz}:{m.IsPrimary}"));
        if (nextInventory == inventoryKey) return;
        inventoryKey = nextInventory;
        foreach (var control in screens.Controls.Cast<Control>().ToArray()) control.Dispose();
        foreach (var monitor in controller.Monitors) screens.Controls.Add(new Label {
            AutoSize = true, MinimumSize = new Size(220, 100), Padding = new Padding(16), Margin = new Padding(0, 0, 8, 8), BackColor = Color.White,
            Text = $"{monitor.Name}{(monitor.IsPrimary ? " · 주 화면" : "")}\n\n{monitor.Width} × {monitor.Height} · {(monitor.RefreshRateHz?.ToString("0.##") ?? "—")} Hz"
        });
    }
    private void Startup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        var name = "UpscaleLabConnect-" + CompanionConfiguration.ServerId;
        if (enabled) key.SetValue(name, $"\"{Environment.ProcessPath}\" --run"); else key.DeleteValue(name, false);
    }
}
