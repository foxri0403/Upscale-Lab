namespace UpscaleLab.Companion;

internal sealed class ReinstallForm : Form
{
    internal ReinstallForm(CompanionInstaller installer, string source, Version installed)
    {
        var version = CompanionInstaller.ReadVersion(source);
        var canInstall = version >= installed;
        Text = CompanionConfiguration.AppName + " 재설치";
        Icon = AppBrand.Icon();
        Font = new("맑은 고딕", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = AppBrand.Background;
        ForeColor = AppBrand.Ink;
        ClientSize = new(540, 245);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new(24) };
        var title = new Label
        {
            AutoSize = true, Font = new(Font.FontFamily, 17, FontStyle.Bold),
            Text = canInstall ? "연결 앱을 재설치할까요?" : "더 최신 버전이 설치되어 있습니다."
        };
        var versions = new Label
        {
            AutoSize = true, Text = $"설치된 버전 {installed.ToString(3)}  ·  다운로드한 버전 {version.ToString(3)}",
            Margin = new(3, 8, 3, 0)
        };
        var status = new Label
        {
            AutoSize = true, ForeColor = AppBrand.Muted, Margin = new(3, 12, 3, 0),
            Text = canInstall ? "저장된 로그인과 기기 연결 정보는 유지됩니다." : "설치된 앱을 열 수 있습니다."
        };
        var progressBar = new ProgressBar { Dock = DockStyle.Top, Height = 10, Visible = false, Style = ProgressBarStyle.Marquee };
        var actions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom };
        var confirm = new BrandButton { Text = canInstall ? "재설치" : "앱 열기", Primary = true };
        var cancel = new BrandButton { Text = "취소" };
        cancel.Click += (_, _) => Close();
        actions.Controls.Add(confirm);
        actions.Controls.Add(cancel);
        content.Controls.Add(title);
        content.Controls.Add(versions);
        content.Controls.Add(status);
        content.Controls.Add(progressBar);
        content.Controls.Add(actions);
        Controls.Add(content);
        AcceptButton = confirm;
        CancelButton = cancel;

        var installing = false;
        FormClosing += (_, e) => { if (installing && e.CloseReason == CloseReason.UserClosing) e.Cancel = true; };
        confirm.Click += async (_, _) =>
        {
            try
            {
                if (!canInstall)
                {
                    installer.OpenInstalled();
                    Close();
                    return;
                }

                installing = true;
                confirm.Enabled = cancel.Enabled = false;
                progressBar.Visible = true;
                var progress = new Progress<string>(message => status.Text = message);
                await Task.Run(() => installer.Reinstall(source, progress));
                canInstall = false;
                status.Text = "재설치가 완료되었습니다.";
                confirm.Text = "앱 열기";
                cancel.Text = "닫기";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                status.Text = "재설치 오류: " + error.Message;
            }
            finally
            {
                installing = false;
                if (!IsDisposed)
                {
                    confirm.Enabled = cancel.Enabled = true;
                    progressBar.Visible = false;
                }
            }
        };
    }
}
