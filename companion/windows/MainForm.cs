using System.Reflection;
using Microsoft.Win32;

namespace UpscaleLab.Companion;

internal sealed class MainForm : Form
{
    private readonly CompanionController controller = new();
    private readonly TextBox server = new() { Dock = DockStyle.Fill, PlaceholderText = "서버 주소 (https://...)" };
    private readonly TextBox identifier = new() { Dock = DockStyle.Fill, PlaceholderText = "이메일 또는 아이디" };
    private readonly TextBox password = new() { Dock = DockStyle.Fill, PlaceholderText = "비밀번호", UseSystemPasswordChar = true };
    private readonly CheckBox remember = new() { Text = "자동 로그인", AutoSize = true };
    private readonly CheckBox startup = new() { Text = "Windows 시작 시 실행", AutoSize = true };
    private readonly Button login = new() { Text = "로그인 및 기기 연결", AutoSize = true };
    private readonly Button sync = new() { Text = "동기화 켜기", AutoSize = true };
    private readonly Button reconnect = new() { Text = "기기 다시 연결", AutoSize = true };
    private readonly Button logout = new() { Text = "로그아웃", AutoSize = true };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(520, 0), Padding = new Padding(0, 12, 0, 12) };
    private readonly Label account = new() { AutoSize = true, MaximumSize = new Size(520, 0) };
    private readonly ListBox screens = new() { Dock = DockStyle.Fill, Height = 130 };
    private readonly FlowLayoutPanel credentials = new() { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    private readonly FlowLayoutPanel actions = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 10, 0, 0) };
    private readonly NotifyIcon tray = new() { Icon = SystemIcons.Application, Text = "Upscale Lab Connect", Visible = true };
    private bool busy;
    private bool quitting;
    public MainForm()
    {
        Text = "Upscale Lab Connect"; Width = 620; Height = 630; MinimumSize = new Size(520, 500);
        Font = new Font("맑은 고딕", 10); BackColor = Color.FromArgb(248, 249, 255); StartPosition = FormStartPosition.CenterScreen;
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(25), ColumnCount = 1, RowCount = 7 };
        content.RowStyles.Add(new(SizeType.AutoSize)); content.RowStyles.Add(new(SizeType.AutoSize));
        content.RowStyles.Add(new(SizeType.AutoSize)); content.RowStyles.Add(new(SizeType.Percent, 100));
        content.RowStyles.Add(new(SizeType.AutoSize)); content.RowStyles.Add(new(SizeType.AutoSize));
        content.Controls.Add(new Label { Text = "내 모든 화면을 웹과 연결하세요", AutoSize = true, Font = new Font(Font.FontFamily, 17, FontStyle.Bold), Margin = new Padding(0, 0, 0, 15) }, 0, 0);
        foreach (var control in new Control[] { server, identifier, password }) { control.Width = 520; control.Height = 38; control.Margin = new Padding(0, 4, 0, 4); credentials.Controls.Add(control); }
        credentials.Controls.Add(remember); credentials.Controls.Add(login);
        content.Controls.Add(credentials, 0, 1); content.Controls.Add(account, 0, 2); content.Controls.Add(screens, 0, 3);
        content.Controls.Add(status, 0, 4);
        actions.Controls.AddRange([sync, reconnect, logout, startup]); content.Controls.Add(actions, 0, 5);
        content.Controls.Add(new Label { Text = "연결하면 화면 개수·해상도·주사율·기기 모델을 웹에 표시합니다.\n창을 닫으면 트레이에서 계속 동기화합니다.", AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(520, 0), Margin = new Padding(0, 18, 0, 0) }, 0, 6);
        Controls.Add(content);
        server.Text = controller.Server;
        controller.RefreshInventory();
        if (server.Text.Length == 0) server.Text = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "DefaultApiBaseUrl")?.Value ?? "";
        login.Click += async (_, _) => await Operation(async () => { try { await controller.SignIn(server.Text, identifier.Text, password.Text, remember.Checked); } finally { password.Clear(); } });
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
        FormClosed += (_, _) => { tray.Dispose(); controller.Dispose(); };
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
        account.Text = controller.LoggedIn ? $"{controller.Account}\n{controller.DeviceName} · 화면 {controller.Monitors.Length}개" : "";
        screens.BeginUpdate(); screens.Items.Clear();
        foreach (var monitor in controller.Monitors) screens.Items.Add($"{monitor.Name}  {monitor.Width} × {monitor.Height}  {(monitor.RefreshRateHz?.ToString("0.##") ?? "—")} Hz{(monitor.IsPrimary ? " · 주 화면" : "")}");
        screens.EndUpdate();
    }
    private void Startup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("UpscaleLabConnect", $"\"{Environment.ProcessPath}\""); else key.DeleteValue("UpscaleLabConnect", false);
    }
}
