using System.Diagnostics;
using Microsoft.Win32;

namespace UpscaleLab.Companion;

internal sealed class MainForm : Form
{
    private readonly CompanionController controller;
    private readonly TextBox identifier = new() { PlaceholderText = "아이디 또는 이메일", BorderStyle = BorderStyle.None, Dock = DockStyle.Fill };
    private readonly TextBox password = new() { PlaceholderText = "비밀번호", UseSystemPasswordChar = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill };
    private readonly CheckBox remember = new() { Text = "자동 로그인", AutoSize = true, Margin = new(0, 8, 0, 8) };
    private readonly CheckBox startup = new() { Text = "Windows 시작 시 실행", AutoSize = true, Margin = new(0, 4, 0, 18) };
    private readonly BrandButton login = new() { Text = "로그인하고 연결", Primary = true, Dock = DockStyle.Top };
    private readonly BrandToggle sync = new() { Text = "배경화면 동기화", Width = 220, Height = 50, AutoCheck = false };
    private readonly BrandButton reconnect = new() { Text = "↻  기기 다시 연결", Dock = DockStyle.Fill };
    private readonly BrandButton logout = new() { Text = "로그아웃", AutoSize = true };
    private readonly Label status = new() { AutoSize = true, ForeColor = AppBrand.Muted, Margin = new(0, 8, 0, 12) };
    private readonly Label badge = new() { Text = "● 연결됨", AutoSize = true, ForeColor = Color.FromArgb(37, 146, 79), BackColor = Color.FromArgb(222, 248, 229), Padding = new(16, 9, 16, 9), Anchor = AnchorStyles.Right };
    private readonly Label account = new() { AutoSize = true, Font = new("맑은 고딕", 11, FontStyle.Bold), Anchor = AnchorStyles.Left };
    private readonly Label device = new() { AutoSize = true, Font = new("맑은 고딕", 11, FontStyle.Bold), Anchor = AnchorStyles.Left };
    private readonly FlowLayoutPanel screens = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new(0, 0, 0, 6) };
    private readonly TableLayoutPanel credentials = Stack();
    private readonly TableLayoutPanel connected = Stack();
    private readonly NotifyIcon tray = new() { Icon = AppBrand.Icon(), Text = "Upscale Lab Connect", Visible = true };
    private bool busy, quitting;
    private string inventoryKey = "";

    private static TableLayoutPanel Stack() {
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
        table.ColumnStyles.Add(new(SizeType.Percent, 100)); return table;
    }
    private static Label Copy(string text, bool title = false) => new() { Text = text, AutoSize = true,
        Font = new("맑은 고딕", title ? 16 : 10, title ? FontStyle.Bold : FontStyle.Regular),
        ForeColor = title ? AppBrand.Ink : AppBrand.Muted, Margin = new(0, 0, 0, title ? 14 : 20) };
    private static BrandPanel Input(TextBox input) {
        var box = new BrandPanel { Dock = DockStyle.Top, Height = 50, Padding = new(14, 14, 14, 10) }; box.Controls.Add(input); return box;
    }
    internal MainForm(CompanionController? companion = null)
    {
        controller = companion ?? new();
        Text = CompanionConfiguration.AppName; Icon = AppBrand.Icon();
        Font = new("맑은 고딕", 10); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new(900, 840); MinimumSize = new(600, 600);
        ForeColor = AppBrand.Ink; BackColor = AppBrand.Background; StartPosition = FormStartPosition.CenterScreen;
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var content = Stack(); content.Padding = new(28);
        var header = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, Height = 96, Margin = new(0, 0, 0, 22) };
        header.ColumnStyles.Add(new(SizeType.Absolute, 86)); header.ColumnStyles.Add(new(SizeType.Percent, 100)); header.ColumnStyles.Add(new(SizeType.AutoSize));
        header.Controls.Add(new PictureBox { Image = AppBrand.Image(), SizeMode = PictureBoxSizeMode.Zoom, Size = new(72, 72) });
        var brand = Stack(); brand.Controls.Add(Copy("Upscale Lab Connect", true)); brand.Controls.Add(Copy("내 화면을 웹과 연결하세요")); header.Controls.Add(brand); header.Controls.Add(badge);
        content.Controls.Add(header);
        credentials.Controls.Add(Copy("이 기기를 연결하세요", true));
        credentials.Controls.Add(Copy("웹과 같은 계정으로 로그인해 주세요."));
        var form = Stack(); form.BackColor = Color.White;
        form.Controls.Add(Copy("아이디 또는 이메일")); form.Controls.Add(Input(identifier));
        form.Controls.Add(Copy("비밀번호")); form.Controls.Add(Input(password)); form.Controls.Add(remember);
        login.Margin = new(0, 14, 0, 0); form.Controls.Add(login);
        var loginCard = new BrandPanel { Dock = DockStyle.Top, AutoSize = true }; loginCard.Controls.Add(form);
        credentials.Controls.Add(loginCard);
        credentials.Controls.Add(Copy("배경화면은 웹의 기기 관리에서 선택해 주세요."));
        credentials.Dock = DockStyle.None; credentials.Anchor = AnchorStyles.Top;
        credentials.MinimumSize = new(500, 0); credentials.MaximumSize = new(500, 0);
        content.Controls.Add(credentials);
        var accountRow = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, Height = 52, BackColor = Color.Transparent };
        accountRow.ColumnStyles.Add(new(SizeType.Percent, 100)); accountRow.ColumnStyles.Add(new(SizeType.AutoSize));
        accountRow.Controls.Add(account); accountRow.Controls.Add(logout);
        var accountCard = new BrandPanel { Dock = DockStyle.Top, Height = 90, Padding = new(18) }; accountCard.Controls.Add(accountRow); connected.Controls.Add(accountCard);
        var deviceRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, BackColor = Color.Transparent };
        deviceRow.ColumnStyles.Add(new(SizeType.Absolute, 70)); deviceRow.ColumnStyles.Add(new(SizeType.Percent, 100)); deviceRow.ColumnStyles.Add(new(SizeType.Absolute, 220));
        deviceRow.Controls.Add(new MonitorGlyph()); deviceRow.Controls.Add(device); deviceRow.Controls.Add(sync);
        var deviceCard = new BrandPanel { Dock = DockStyle.Top, Height = 104, Fill = Color.FromArgb(238, 235, 255), BackColor = Color.FromArgb(238, 235, 255) };
        deviceCard.Controls.Add(deviceRow); connected.Controls.Add(deviceCard);
        connected.Controls.Add(Copy("연결된 화면", true)); connected.Controls.Add(screens);
        connected.Controls.Add(Copy("배경화면은 웹의 기기 관리에서 변경할 수 있어요."));
        connected.Controls.Add(startup);
        var actions = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, Height = 58, Margin = new(0, 0, 0, 16) };
        actions.ColumnStyles.Add(new(SizeType.Percent, 50)); actions.ColumnStyles.Add(new(SizeType.Percent, 50));
        var web = new BrandButton { Text = "↗  웹 기기 관리 열기", Primary = true, Dock = DockStyle.Fill, Margin = new(0, 0, 10, 0) };
        actions.Controls.Add(web); actions.Controls.Add(reconnect); connected.Controls.Add(actions);
        connected.Controls.Add(Copy("창을 닫아도 트레이에서 동기화해요."));
        var privacy = new LinkLabel { Text = "수집 정보 안내", AutoSize = true, LinkColor = AppBrand.Muted };
        privacy.LinkClicked += (_, _) => MessageBox.Show(this, PrivacyConsent.Details, PrivacyConsent.Title); connected.Controls.Add(privacy);
        content.Controls.Add(connected); content.Controls.Add(status); scroll.Controls.Add(content); Controls.Add(scroll);
        void FitContent() {
            var width = Math.Max(260, content.ClientSize.Width - content.Padding.Horizontal);
            foreach (var label in content.Controls.Cast<Control>().SelectMany(c => c.Controls.Cast<Control>()).OfType<Label>()) label.MaximumSize = new(width, 0);
            status.MaximumSize = new(width, 0);
            foreach (Control card in screens.Controls) card.Width = width >= 700 ? (width - 18) / 2 : width - 8;
            header.Height = width < 700 ? 120 : 96;
            deviceRow.ColumnStyles[2].Width = width < 700 ? 174 : 220;
            sync.Width = width < 700 ? 174 : 220;
        }
        content.SizeChanged += (_, _) => FitContent(); screens.ControlAdded += (_, _) => FitContent();
        login.Click += async (_, _) => await Operation(async () => { try { await controller.SignIn(identifier.Text, password.Text, remember.Checked); } finally { password.Clear(); } });
        sync.Click += async (_, _) => await Operation(async () => { if (controller.Enabled) await controller.Stop(); else await controller.Enable(); });
        reconnect.Click += async (_, _) => await Operation(controller.Reconnect);
        logout.Click += async (_, _) => await Operation(async () => { await controller.Logout(); Startup(false); startup.Checked = false; });
        web.Click += (_, _) => {
            try { Process.Start(new ProcessStartInfo(CompanionConfiguration.WebDevicesUrl) { UseShellExecute = true }); }
            catch (Exception error) { status.Text = error.Message; }
        };
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) startup.Checked = key?.GetValue("UpscaleLabConnect-" + CompanionConfiguration.ServerId) != null;
        startup.CheckedChanged += (_, _) => { if (!busy) Startup(startup.Checked); };
        controller.Changed += RefreshState;
        tray.DoubleClick += (_, _) => ShowMain();
        var menu = new ContextMenuStrip(); menu.Items.Add("열기", null, (_, _) => ShowMain());
        menu.Items.Add("종료", null, async (_, _) => { quitting = true; var enabled = controller.Enabled; await controller.Stop();
            var saved = SecureStore.Read(); if (saved != null && saved.Remember) SecureStore.Write(saved with { Enabled = enabled }); Close(); });
        tray.ContextMenuStrip = menu;
        FormClosing += (_, e) => { if (!quitting && controller.LoggedIn) { e.Cancel = true; Hide(); } };
        FormClosed += (_, _) => { var icon = tray.Icon; tray.Dispose(); icon?.Dispose(); controller.Dispose(); };
        Shown += async (_, _) => await Operation(controller.Restore);
        RefreshState();
    }
    private void ShowMain() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private async Task Operation(Func<Task> operation) {
        if (busy) return; busy = true; RefreshState();
        try { await operation(); } catch (Exception error) { status.Text = error.Message; }
        finally { busy = false; RefreshState(false); }
    }
    private void RefreshState() => RefreshState(true);
    private void RefreshState(bool replaceStatus) {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke((Action)(() => RefreshState(replaceStatus))); return; }
        credentials.Visible = !controller.LoggedIn; connected.Visible = badge.Visible = controller.LoggedIn;
        AcceptButton = controller.LoggedIn ? null : login;
        login.Enabled = identifier.Enabled = password.Enabled = remember.Enabled = !busy;
        sync.Enabled = reconnect.Enabled = logout.Enabled = startup.Enabled = !busy;
        sync.Checked = controller.Enabled;
        if (replaceStatus) status.Text = controller.Status;
        account.Text = "●  " + controller.Account;
        device.Text = "내 Windows PC\n연결된 화면 " + controller.Monitors.Length + "개";
        var next = string.Join("|", controller.Monitors.Select(m => $"{m.NativeId}:{m.Name}:{m.Width}:{m.Height}:{m.RefreshRateHz}:{m.IsPrimary}"));
        if (next == inventoryKey) return;
        inventoryKey = next;
        foreach (var control in screens.Controls.Cast<Control>().ToArray()) control.Dispose();
        foreach (var monitor in controller.Monitors) {
            var card = new BrandPanel { Size = new(390, 126), Margin = new(0, 0, 8, 10), Padding = new(18) };
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.Transparent };
            row.ColumnStyles.Add(new(SizeType.Absolute, 66)); row.ColumnStyles.Add(new(SizeType.Percent, 100));
            row.Controls.Add(new MonitorGlyph());
            row.Controls.Add(new Label { Text = $"{monitor.Name}{(monitor.IsPrimary ? " · 주 화면" : "")}\n\n{monitor.Width} × {monitor.Height} · {(monitor.RefreshRateHz?.ToString("0.##") ?? "—")} Hz",
                Dock = DockStyle.Fill, AutoEllipsis = true }); card.Controls.Add(row); screens.Controls.Add(card);
        }
    }
    private void Startup(bool enabled) {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        var name = "UpscaleLabConnect-" + CompanionConfiguration.ServerId;
        if (enabled) key.SetValue(name, $"\"{Environment.ProcessPath}\" --run"); else key.DeleteValue(name, false);
    }
}
