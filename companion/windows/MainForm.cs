using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace UpscaleLab.Companion;

internal sealed class MainForm : Form
{
    private static readonly Size DefaultClientSize = new(664, 588);
    private readonly CompanionController controller;
    private readonly TextBox identifier = new() { PlaceholderText = "아이디 또는 이메일", BorderStyle = BorderStyle.None, Dock = DockStyle.Fill };
    private readonly TextBox password = new() { PlaceholderText = "비밀번호", UseSystemPasswordChar = true, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill };
    private readonly CheckBox saveIdentifier = new() { Text = "아이디 저장", AutoSize = true, Margin = new(0, 8, 22, 8) };
    private readonly CheckBox remember = new() { Text = "자동 로그인", AutoSize = true, Margin = new(0, 8, 0, 8) };
    private readonly CheckBox startup = new() { Text = "PC 부팅 시 실행", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly CheckBox backgroundStartup = new() { Text = "백그라운드로 자동 실행", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly System.Windows.Forms.Timer restoreTimer = new() { Interval = 30_000 };
    private readonly System.Windows.Forms.Timer playbackTimer = new() { Interval = 250 };
    private readonly DisplayPowerNotifications displayPower = new();
    private PlaybackRules playbackRules = PlaybackRules.Load();
    private readonly BrandButton login = new() { Text = "로그인", Primary = true, CornerRadius = 0, Dock = DockStyle.Top };
    private readonly BrandButton logout = new() { Text = "로그아웃", AutoSize = true, Margin = new(16, 0, 0, 0) };
    private readonly Label status = new() { Dock = DockStyle.Top, AutoEllipsis = true, Height = 22,
        ForeColor = AppBrand.Muted, Margin = Padding.Empty };
    private readonly Label trayHint = new() { Text = "창을 닫아도 트레이에서 동기화합니다.", Dock = DockStyle.Top,
        Height = 22, ForeColor = AppBrand.Muted, Margin = Padding.Empty };
    private readonly Label connection = new() { AutoSize = true, Anchor = AnchorStyles.Right, Font = new("맑은 고딕", 12, FontStyle.Bold) };
    private readonly Label username = new() { AutoSize = true, Font = new("맑은 고딕", 14, FontStyle.Bold), Margin = Padding.Empty };
    private readonly Label email = new() { AutoSize = true, ForeColor = AppBrand.Muted, Font = new("맑은 고딕", 9), Margin = new(0, 4, 0, 0) };
    private readonly Label plan = new() { AutoSize = true, ForeColor = AppBrand.Primary, Margin = new(0, 4, 0, 0) };
    private readonly CircularAvatar avatar = new() { Size = new(64, 64), Margin = new(0, 0, 14, 0) };
    private readonly TableLayoutPanel account = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Top | AnchorStyles.Right, ColumnCount = 2, Margin = Padding.Empty };
    private readonly Label screens = new() { AutoEllipsis = true, Dock = DockStyle.Top, Height = 26,
        TextAlign = ContentAlignment.MiddleLeft, Font = new("맑은 고딕", 11), Margin = new(0, 4, 0, 6), UseMnemonic = false };
    private readonly ComboBox monitorPicker = new() { DropDownStyle = ComboBoxStyle.DropDownList, MaxDropDownItems = 8,
        AccessibleName = "연결된 화면 선택", DisplayMember = nameof(MonitorInfo.Name), Width = 148, Anchor = AnchorStyles.Right };
    private MonitorInfo[] shownMonitors = [];
    private readonly TableLayoutPanel credentials = Stack();
    private readonly TableLayoutPanel connected = Stack();
    private readonly NotifyIcon tray = new() { Icon = AppBrand.Icon(), Text = "Upscale Lab Connect", Visible = true };
    private bool busy, quitting, loadingAvatar;
    private StartupMode startupMode;
    private bool changingStartup;
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
        SuspendLayout();
        Font = new("맑은 고딕", 10); AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = DefaultClientSize;
        MinimumSize = Size;
        ForeColor = AppBrand.Ink; BackColor = AppBrand.Background; StartPosition = FormStartPosition.CenterScreen;
        var content = Stack(); content.Padding = new(24, 24, 24, 12);
        var header = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, Height = 82, Margin = new(0, 0, 0, 10) };
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
        deviceContent.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(220, 224, 235), Margin = new(0, 0, 0, 14) });
        var screensHeader = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        screensHeader.ColumnStyles.Add(new(SizeType.Percent, 100));
        screensHeader.ColumnStyles.Add(new(SizeType.AutoSize));
        var screensTitle = Copy("연결된 화면", 13, true);
        screensTitle.Margin = new(0, 0, 0, 6);
        screensHeader.Controls.Add(screensTitle);
        screensHeader.Controls.Add(monitorPicker);
        monitorPicker.SelectedIndexChanged += (_, _) => ShowSelectedMonitor();
        deviceContent.Controls.Add(screensHeader);
        deviceContent.Controls.Add(screens);
        var deviceCard = new BrandPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new(14), CornerRadius = 2, Margin = new(0, 0, 0, 6) }; deviceCard.Controls.Add(deviceContent); connected.Controls.Add(deviceCard);
        var footer = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new(0, 12, 0, 0) };
        footer.ColumnStyles.Add(new(SizeType.Percent, 48)); footer.ColumnStyles.Add(new(SizeType.Percent, 52));
        var startupOptions = Stack();
        startupOptions.Controls.Add(startup); startupOptions.Controls.Add(backgroundStartup);
        footer.Controls.Add(startupOptions);
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        var privacy = new LinkLabel { Text = "수집 정보 안내", AutoSize = true, LinkColor = AppBrand.Muted, Margin = new(0, 14, 0, 0) };
        privacy.LinkClicked += (_, _) => MessageBox.Show(this, PrivacyConsent.Details, PrivacyConsent.Title);
        actions.Controls.Add(privacy); actions.Controls.Add(logout); footer.Controls.Add(actions); connected.Controls.Add(footer);
        connected.Controls.Add(PlaybackSettings());
        playbackTimer.Tick += (_, _) => UpdatePlayback();
        UpdatePlayback();
        connected.Controls.Add(trayHint);
        content.Controls.Add(connected); content.Controls.Add(status); Controls.Add(content);
        content.SizeChanged += (_, _) => {
            var width = Math.Max(300, content.ClientSize.Width - content.Padding.Horizontal);
            username.MaximumSize = email.MaximumSize = plan.MaximumSize = new(Math.Max(180, (int)(width * .43) - 90), 0);
        };
        identifier.Text = LoginPreferences.Current.Read(); saveIdentifier.Checked = identifier.Text.Length > 0;
        void SaveIdentity() { try { LoginPreferences.Current.Save(saveIdentifier.Checked, identifier.Text); } catch (Exception e) { status.Text = "아이디 저장 실패: " + e.Message; } }
        saveIdentifier.CheckedChanged += (_, _) => SaveIdentity(); identifier.Leave += (_, _) => SaveIdentity();
        login.Click += async (_, _) => await Operation(async () => { SaveIdentity(); try { await controller.SignIn(identifier.Text, password.Text, remember.Checked); } finally { password.Clear(); } });
        logout.Click += async (_, _) => await Operation(async () => { await controller.Logout(); SaveStartup(StartupMode.Off); identifier.Text = LoginPreferences.Current.Read(); });
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
            UpdateStartupChecks(key?.GetValue(StartupName) is string command
                ? command.EndsWith(" --background", StringComparison.Ordinal) ? StartupMode.Background : StartupMode.Window
                : StartupMode.Off);
        startup.CheckedChanged += (_, _) => { if (!changingStartup) SaveStartup(startup.Checked ? StartupMode.Window : StartupMode.Off); };
        backgroundStartup.CheckedChanged += (_, _) => { if (!changingStartup) SaveStartup(backgroundStartup.Checked ? StartupMode.Background : StartupMode.Off); };
        restoreTimer.Tick += async (_, _) => await Operation(controller.Restore);
        controller.Changed += RefreshState; tray.DoubleClick += (_, _) => ShowMain();
        var menu = new ContextMenuStrip(); menu.Items.Add("열기", null, (_, _) => ShowMain());
        menu.Items.Add("종료", null, async (_, _) => await ExitAsync());
        tray.ContextMenuStrip = menu;
        FormClosing += (_, e) => { SaveIdentity(); if (e.CloseReason == CloseReason.UserClosing && !quitting && controller.LoggedIn) { e.Cancel = true; Hide(); } };
        Disposed += (_, _) => {
            controller.Changed -= RefreshState;
            restoreTimer.Dispose(); playbackTimer.Dispose();
            var icon = tray.Icon; tray.Dispose(); icon?.Dispose();
            avatar.Image?.Dispose(); controller.Dispose();
        };
        Shown += (_, _) => RoundWindow();
        SizeChanged += (_, _) => { if (IsHandleCreated) RoundWindow(); };
        Activated += async (_, _) => { if (controller.LoggedIn && !busy && DateTime.UtcNow - lastProfileRefresh > TimeSpan.FromMinutes(1)) {
            lastProfileRefresh = DateTime.UtcNow; await controller.RefreshProfile(); } };
        RefreshState();
        ResumeLayout(performLayout: true);
    }

    private GroupBox PlaybackSettings()
    {
        var group = new GroupBox { Text = "재생", Dock = DockStyle.Top, AutoSize = true,
            Margin = new(0, 12, 0, 14), Padding = new(14, 6, 14, 10) };
        var rows = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        rows.ColumnStyles.Add(new(SizeType.Percent, 50));
        rows.ColumnStyles.Add(new(SizeType.Percent, 50));
        AddRow("타 응용 프로그램 활성화", playbackRules.PauseWhenActive, paused => playbackRules with { PauseWhenActive = paused });
        AddRow("타 응용 프로그램 최대화", playbackRules.PauseWhenMaximized, paused => playbackRules with { PauseWhenMaximized = paused });
        AddRow("타 응용 프로그램 전체 화면", playbackRules.PauseWhenFullScreen, paused => playbackRules with { PauseWhenFullScreen = paused });
        AddRow("디스플레이 절전 상태", playbackRules.PauseWhenDisplaySleeps, paused => playbackRules with { PauseWhenDisplaySleeps = paused });
        group.Controls.Add(rows);
        return group;

        void AddRow(string title, bool paused, Func<bool, PlaybackRules> change)
        {
            var label = new Label { Text = title, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new(0, 3, 12, 3) };
            var choices = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = title,
                Dock = DockStyle.Fill, Margin = new(0, 3, 0, 3) };
            choices.Items.AddRange(["재생", "일시정지"]);
            choices.SelectedIndex = paused ? 1 : 0;
            choices.SelectionChangeCommitted += (_, _) =>
            {
                var next = change(choices.SelectedIndex == 1);
                try { next.Save(); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    choices.SelectedIndex = choices.SelectedIndex == 1 ? 0 : 1;
                    MessageBox.Show(this, "재생 설정을 저장하지 못했습니다.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                playbackRules = next;
                UpdatePlayback();
            };
            rows.Controls.Add(label);
            rows.Controls.Add(choices);
        }
    }

    private void UpdatePlayback() => controller.UpdatePlayback(playbackRules, displayPower.Sleeping);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        displayPower.Register(Handle);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        displayPower.Unregister();
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (displayPower.HandleMessage(m)) UpdatePlayback();
        base.WndProc(ref m);
    }
    internal void ShowMain() { if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Show(); Activate(); }
    internal async Task ExitAsync()
    {
        if (quitting) return;
        quitting = true;
        restoreTimer.Stop();
        try
        {
            var restore = controller.LoggedIn;
            await controller.Stop();
            if (SecureStore.Read() is { Remember: true } saved) SecureStore.Write(saved with { Enabled = restore });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // The last committed login state remains available for the next launch.
        }
        finally { Close(); }
    }
    internal async Task InitializeAsync(bool background)
    {
        _ = Handle;
        if (!background) ShowMain();
        await Operation(controller.Restore);
        if (!controller.Enabled && controller.CanRetryRestore) restoreTimer.Start();
    }
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
        logout.Enabled = startup.Enabled = backgroundStartup.Enabled = !busy;
        if (restoreTimer.Enabled && (controller.Enabled || !controller.CanRetryRestore)) restoreTimer.Stop();
        playbackTimer.Enabled = controller.LoggedIn;
        if (replaceStatus) status.Text = "";
        status.Visible = status.Text.Length > 0;
        trayHint.Visible = status.Text.Length == 0;
        username.Text = controller.Username; email.Text = controller.Account; plan.Text = controller.Plan + " 플랜";
        connection.Text = controller.Connection switch { ConnectionState.Connected => "● 연결됨", ConnectionState.Connecting => "● 연결중", _ => "● 연결안됨" };
        connection.ForeColor = controller.Connection switch { ConnectionState.Connected => Color.FromArgb(38, 153, 83), ConnectionState.Connecting => Color.FromArgb(230, 139, 32), _ => Color.FromArgb(208, 64, 64) };
        RefreshMonitors();
        _ = RefreshAvatar();
    }

    private void RefreshMonitors()
    {
        var monitors = controller.Monitors;
        if (!shownMonitors.SequenceEqual(monitors))
        {
            var selected = (monitorPicker.SelectedItem as MonitorInfo)?.NativeId;
            shownMonitors = monitors;
            monitorPicker.DataSource = monitors.Length > 1 ? monitors : null;
            var index = Array.FindIndex(monitors, monitor => monitor.NativeId == selected);
            if (index < 0) index = Array.FindIndex(monitors, monitor => monitor.IsPrimary);
            if (monitors.Length > 1) monitorPicker.SelectedIndex = Math.Max(0, index);
        }
        monitorPicker.Visible = monitors.Length > 1;
        ShowSelectedMonitor();
    }

    private void ShowSelectedMonitor()
    {
        var monitor = monitorPicker.SelectedItem as MonitorInfo ?? shownMonitors.FirstOrDefault();
        screens.Text = monitor is null ? "화면 정보를 확인하고 있습니다." : FormatMonitor(monitor);
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
    private enum StartupMode { Off, Window, Background }
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static string StartupName => "UpscaleLabConnect-" + CompanionConfiguration.ServerId;

    private void SaveStartup(StartupMode mode)
    {
        if (mode == StartupMode.Background && !controller.CanRetryRestore)
        {
            UpdateStartupChecks(startupMode);
            MessageBox.Show(this, "백그라운드에서 연결하려면 자동 로그인을 켜고 로그인해 주세요.", Text);
            return;
        }
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (mode == StartupMode.Off) key.DeleteValue(StartupName, false);
            else key.SetValue(StartupName, $"\"{Environment.ProcessPath}\" --run" +
                (mode == StartupMode.Background ? " --background" : ""));
            UpdateStartupChecks(mode);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            UpdateStartupChecks(startupMode);
            MessageBox.Show(this, "Windows 시작 설정을 저장하지 못했습니다.", Text);
        }
    }

    private void UpdateStartupChecks(StartupMode mode)
    {
        startupMode = mode;
        changingStartup = true;
        startup.Checked = mode == StartupMode.Window;
        backgroundStartup.Checked = mode == StartupMode.Background;
        changingStartup = false;
    }
}
