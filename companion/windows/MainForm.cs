using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace UpscaleLab.Companion;

internal sealed class MainForm : Form
{
    private readonly CompanionController controller;
    private readonly TextBox identifier = new() { PlaceholderText = "아이디 또는 이메일", BorderStyle = BorderStyle.None, Dock = DockStyle.Fill };
    private readonly TextBox password = new() { PlaceholderText = "비밀번호", UseSystemPasswordChar = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill };
    private readonly CheckBox saveIdentifier = new() { Text = "아이디 저장", AutoSize = true, Margin = new(0, 8, 22, 8) };
    private readonly CheckBox remember = new() { Text = "자동 로그인", AutoSize = true, Margin = new(0, 8, 0, 8) };
    private readonly CheckBox startup = new() { Text = "Windows 시작 시 실행", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly BrandButton login = new() { Text = "로그인", Primary = true, CornerRadius = 0, Dock = DockStyle.Top };
    private readonly BrandButton logout = new() { Text = "로그아웃", AutoSize = true, Margin = new(16, 0, 0, 0) };
    private readonly Label status = new() { AutoSize = true, ForeColor = AppBrand.Muted, Margin = new(0, 12, 0, 0) };
    private readonly Label connection = new() { AutoSize = true, Anchor = AnchorStyles.Right, Font = new("맑은 고딕", 12, FontStyle.Bold) };
    private readonly Label username = new() { AutoSize = true, Font = new("맑은 고딕", 14, FontStyle.Bold), Margin = Padding.Empty };
    private readonly Label email = new() { AutoSize = true, ForeColor = AppBrand.Muted, Font = new("맑은 고딕", 9), Margin = new(0, 4, 0, 0) };
    private readonly Label plan = new() { AutoSize = true, ForeColor = AppBrand.Primary, Margin = new(0, 4, 0, 0) };
    private readonly CircularAvatar avatar = new() { Size = new(64, 64), Margin = new(0, 0, 14, 0) };
    private readonly TableLayoutPanel account = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Top | AnchorStyles.Right, ColumnCount = 2, Margin = Padding.Empty };
    private readonly Label screens = new() { AutoSize = true, Font = new("맑은 고딕", 11), Margin = new(0, 16, 0, 10) };
    private readonly TableLayoutPanel credentials = Stack();
    private readonly TableLayoutPanel connected = Stack();
    private readonly NotifyIcon tray = new() { Icon = AppBrand.Icon(), Text = "Upscale Lab Connect", Visible = true };
    private bool busy, quitting, loadingAvatar;
    private string? avatarUrl;
    private DateTime lastProfileRefresh;

    private static TableLayoutPanel Stack() {
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.Transparent };
        table.ColumnStyles.Add(new(SizeType.Percent, 100)); return table;
    }
    private static Label Copy(string text, int size = 10, bool bold = false) => new() { Text = text, AutoSize = true,
        Font = new("맑은 고딕", size, bold ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = bold ? AppBrand.Ink : AppBrand.Muted, Margin = new(0, 0, 0, 14) };
    private static BrandPanel Input(TextBox input) {
        var box = new BrandPanel { Dock = DockStyle.Top, Height = 50, Padding = new(14, 14, 14, 10), CornerRadius = 0 }; box.Controls.Add(input); return box;
    }
    internal MainForm(CompanionController? companion = null)
    {
        controller = companion ?? new(); Text = CompanionConfiguration.AppName; Icon = AppBrand.Icon(); ShowIcon = false;
        Font = new("맑은 고딕", 10); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new(1000, 660); MinimumSize = new(820, 540);
        ForeColor = AppBrand.Ink; BackColor = AppBrand.Background; StartPosition = FormStartPosition.CenterScreen;
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var content = Stack(); content.Padding = new(28);
        var header = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, Height = 100, Margin = new(0, 0, 0, 22) };
        header.ColumnStyles.Add(new(SizeType.Percent, 57)); header.ColumnStyles.Add(new(SizeType.Percent, 43));
        header.Controls.Add(new Label { Text = "Upscale Lab Connect", Font = new(Font.FontFamily, 22, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, Margin = Padding.Empty });
        account.ColumnStyles.Add(new(SizeType.Absolute, 82)); account.ColumnStyles.Add(new(SizeType.AutoSize));
        var identity = Stack(); identity.Controls.Add(username); identity.Controls.Add(email); identity.Controls.Add(plan);
        account.Controls.Add(avatar); account.Controls.Add(identity); header.Controls.Add(account); content.Controls.Add(header);
        credentials.Controls.Add(Copy("로그인", 18, true));
        var form = Stack(); form.BackColor = Color.White;
        form.Controls.Add(Copy("아이디 또는 이메일")); form.Controls.Add(Input(identifier));
        form.Controls.Add(Copy("비밀번호")); form.Controls.Add(Input(password));
        var choices = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        choices.Controls.Add(saveIdentifier); choices.Controls.Add(remember); form.Controls.Add(choices);
        login.Margin = new(0, 14, 0, 0); form.Controls.Add(login);
        var loginCard = new BrandPanel { Dock = DockStyle.Top, AutoSize = true, CornerRadius = 0 }; loginCard.Controls.Add(form);
        credentials.Controls.Add(loginCard); credentials.Dock = DockStyle.None; credentials.Anchor = AnchorStyles.Top;
        credentials.MinimumSize = new(500, 0); credentials.MaximumSize = new(500, 0); content.Controls.Add(credentials);
        var deviceContent = Stack();
        var deviceHeader = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new(0, 0, 0, 12) };
        deviceHeader.ColumnStyles.Add(new(SizeType.Percent, 65)); deviceHeader.ColumnStyles.Add(new(SizeType.Percent, 35));
        deviceHeader.Controls.Add(Copy("내 Windows PC", 17, true)); deviceHeader.Controls.Add(connection); deviceContent.Controls.Add(deviceHeader);
        deviceContent.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(220, 224, 235), Margin = new(0, 0, 0, 18) });
        deviceContent.Controls.Add(Copy("연결된 화면", 13, true)); deviceContent.Controls.Add(screens);
        var deviceCard = new BrandPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new(22), CornerRadius = 2 }; deviceCard.Controls.Add(deviceContent); connected.Controls.Add(deviceCard);
        var footer = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new(0, 12, 0, 0) };
        footer.ColumnStyles.Add(new(SizeType.Percent, 48)); footer.ColumnStyles.Add(new(SizeType.Percent, 52)); footer.Controls.Add(startup);
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        var privacy = new LinkLabel { Text = "수집 정보 안내", AutoSize = true, LinkColor = AppBrand.Muted, Margin = new(0, 14, 0, 0) };
        privacy.LinkClicked += (_, _) => MessageBox.Show(this, PrivacyConsent.Details, PrivacyConsent.Title);
        actions.Controls.Add(privacy); actions.Controls.Add(logout); footer.Controls.Add(actions); connected.Controls.Add(footer);
        connected.Controls.Add(Copy("창을 닫아도 트레이에서 동기화합니다."));
        content.Controls.Add(connected); content.Controls.Add(status); scroll.Controls.Add(content); Controls.Add(scroll);
        content.SizeChanged += (_, _) => {
            var width = Math.Max(300, content.ClientSize.Width - content.Padding.Horizontal);
            status.MaximumSize = new(width, 0); screens.MaximumSize = new(width - 48, 0);
            username.MaximumSize = email.MaximumSize = plan.MaximumSize = new(Math.Max(180, (int)(width * .43) - 90), 0);
        };
        identifier.Text = LoginPreferences.Current.Read(); saveIdentifier.Checked = identifier.Text.Length > 0;
        void SaveIdentity() { try { LoginPreferences.Current.Save(saveIdentifier.Checked, identifier.Text); } catch (Exception e) { status.Text = "아이디 저장 실패: " + e.Message; } }
        saveIdentifier.CheckedChanged += (_, _) => SaveIdentity(); identifier.Leave += (_, _) => SaveIdentity();
        login.Click += async (_, _) => await Operation(async () => { SaveIdentity(); try { await controller.SignIn(identifier.Text, password.Text, remember.Checked); } finally { password.Clear(); } });
        logout.Click += async (_, _) => await Operation(async () => { await controller.Logout(); Startup(false); startup.Checked = false; identifier.Text = LoginPreferences.Current.Read(); });
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) startup.Checked = key?.GetValue("UpscaleLabConnect-" + CompanionConfiguration.ServerId) != null;
        startup.CheckedChanged += (_, _) => { if (!busy) Startup(startup.Checked); };
        controller.Changed += RefreshState; tray.DoubleClick += (_, _) => ShowMain();
        var menu = new ContextMenuStrip(); menu.Items.Add("열기", null, (_, _) => ShowMain());
        menu.Items.Add("종료", null, async (_, _) => { quitting = true; var restore = controller.LoggedIn; await controller.Stop();
            var saved = SecureStore.Read(); if (saved != null && saved.Remember) SecureStore.Write(saved with { Enabled = restore }); Close(); });
        tray.ContextMenuStrip = menu;
        FormClosing += (_, e) => { SaveIdentity(); if (!quitting && controller.LoggedIn) { e.Cancel = true; Hide(); } };
        FormClosed += (_, _) => { var icon = tray.Icon; tray.Dispose(); icon?.Dispose(); avatar.Image?.Dispose(); controller.Dispose(); };
        Shown += async (_, _) => { RoundWindow(); await Operation(controller.Restore); };
        SizeChanged += (_, _) => { if (IsHandleCreated) RoundWindow(); };
        Activated += async (_, _) => { if (controller.LoggedIn && !busy && DateTime.UtcNow - lastProfileRefresh > TimeSpan.FromMinutes(1)) {
            lastProfileRefresh = DateTime.UtcNow; await controller.RefreshProfile(); } };
        RefreshState();
    }
    internal void ShowMain() { if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Show(); Activate(); }
    private async Task Operation(Func<Task> operation) {
        if (busy) return; busy = true; RefreshState();
        try { await operation(); } catch (Exception error) { controller.ConnectionFailed(); status.Text = error.Message; }
        finally { busy = false; RefreshState(false); }
    }
    private void RefreshState() => RefreshState(true);
    private void RefreshState(bool replaceStatus) {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke((Action)(() => RefreshState(replaceStatus))); return; }
        credentials.Visible = !controller.LoggedIn; connected.Visible = account.Visible = controller.LoggedIn;
        AcceptButton = controller.LoggedIn ? null : login;
        login.Enabled = identifier.Enabled = password.Enabled = remember.Enabled = saveIdentifier.Enabled = !busy;
        logout.Enabled = startup.Enabled = !busy;
        if (replaceStatus) status.Text = "";
        status.Visible = status.Text.Length > 0;
        username.Text = controller.Username; email.Text = controller.Account; plan.Text = controller.Plan + " 플랜";
        connection.Text = controller.Connection switch { ConnectionState.Connected => "● 연결됨", ConnectionState.Connecting => "● 연결중", _ => "● 연결안됨" };
        connection.ForeColor = controller.Connection switch { ConnectionState.Connected => Color.FromArgb(38, 153, 83), ConnectionState.Connecting => Color.FromArgb(230, 139, 32), _ => Color.FromArgb(208, 64, 64) };
        screens.Text = controller.Monitors.Length == 0 ? "화면 정보를 확인하고 있습니다." : string.Join("\n\n", controller.Monitors.Select(FormatMonitor));
        _ = RefreshAvatar();
    }
    internal static string FormatMonitor(MonitorInfo m) => $"{m.Name} | {m.Width} × {m.Height} | {(m.RefreshRateHz?.ToString("0.##") ?? "—")} Hz{(m.IsPrimary ? " - 주 화면" : "")}";
    private async Task RefreshAvatar() {
        var url = controller.Profile?.ProfileImageUrl;
        if (loadingAvatar || avatarUrl == url) return;
        loadingAvatar = true; avatarUrl = url;
        Image? image = null;
        try { if (url != null) image = await controller.LoadAvatar(url); }
        catch (Exception) { }
        finally { loadingAvatar = false; }
        if (IsDisposed || url != controller.Profile?.ProfileImageUrl) { image?.Dispose(); if (!IsDisposed) _ = RefreshAvatar(); return; }
        var old = avatar.Image; avatar.Image = image; old?.Dispose(); avatar.Invalidate();
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    private void RoundWindow() {
        if (WindowState == FormWindowState.Maximized) { var old = Region; Region = null; old?.Dispose(); return; }
        try { var round = 2; if (DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)) == 0) return; } catch (DllNotFoundException) { }
        if (Width > 40 && Height > 40) { using var outline = BrandPanel.Shape(new(0, 0, Width, Height), 14);
            var previous = Region; Region = new Region(outline); previous?.Dispose(); }
    }
    private void Startup(bool enabled) {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        var name = "UpscaleLabConnect-" + CompanionConfiguration.ServerId;
        if (enabled) key.SetValue(name, $"\"{Environment.ProcessPath}\" --run"); else key.DeleteValue(name, false);
    }
}
