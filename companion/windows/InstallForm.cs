using System.Diagnostics;

namespace UpscaleLab.Companion;

internal sealed class InstallForm : Form
{
    internal InstallForm()
    {
        Text = CompanionConfiguration.AppName + (CompanionConfiguration.LocalTest ? " 설치 · 테스트 앱" : " 설치"); Icon = AppBrand.Icon();
        AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(610, 760); MinimumSize = new Size(500, 640);
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("맑은 고딕", 10);
        BackColor = AppBrand.Background; ForeColor = AppBrand.Ink;
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(28), ColumnCount = 1, RowCount = 9 };
        content.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (var height in new[] { 70, 58, 50, 40, 40, 42 }) content.RowStyles.Add(new(SizeType.Absolute, height));
        content.RowStyles.Add(new(SizeType.Percent, 100)); content.RowStyles.Add(new(SizeType.Absolute, 64)); content.RowStyles.Add(new(SizeType.Absolute, 68));
        content.Controls.Add(new PictureBox { Image = AppBrand.Image(), Size = new(60, 60), SizeMode = PictureBoxSizeMode.Zoom });
        content.Controls.Add(new Label { Text = "Upscale Lab Connect 설치", Dock = DockStyle.Fill, Font = new(Font.FontFamily, 18, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft });
        content.Controls.Add(new Label { Text = "현재 사용자 계정에 설치합니다.\n설치 옵션과 정보 수집 안내를 확인해 주세요.", Dock = DockStyle.Fill, ForeColor = AppBrand.Muted });
        var desktop = new CheckBox { Text = "바탕화면에 바로 가기 만들기", Checked = true, Dock = DockStyle.Fill };
        var startMenu = new CheckBox { Text = "시작 메뉴에 등록", Checked = true, Dock = DockStyle.Fill };
        content.Controls.Add(desktop); content.Controls.Add(startMenu);
        content.Controls.Add(new Label { Text = PrivacyConsent.Title, Dock = DockStyle.Fill, Font = new(Font.FontFamily, 11, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft });
        content.Controls.Add(new TextBox { Text = PrivacyConsent.Notice, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.White });
        var agree = new CheckBox { Text = "[필수] 기기·화면 정보 수집·이용에 동의합니다.", Dock = DockStyle.Fill };
        content.Controls.Add(agree);
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        bottom.ColumnStyles.Add(new(SizeType.Percent, 100)); bottom.ColumnStyles.Add(new(SizeType.Absolute, 150));
        var status = new Label { Dock = DockStyle.Fill, ForeColor = AppBrand.Muted, TextAlign = ContentAlignment.MiddleLeft };
        var install = new BrandButton { Text = "동의하고 설치", Dock = DockStyle.Fill, Primary = true, Enabled = false };
        bottom.Controls.Add(status); bottom.Controls.Add(install); content.Controls.Add(bottom); Controls.Add(content);
        agree.CheckedChanged += (_, _) => install.Enabled = agree.Checked;
        install.Click += (_, _) => {
            if (!agree.Checked) return;
            install.Enabled = false;
            try {
                var executable = CompanionInstaller.ForCurrentUser().Install(Environment.ProcessPath!, desktop.Checked, startMenu.Checked, agree.Checked);
                Process.Start(new ProcessStartInfo(executable, "--run") { UseShellExecute = true }); Close();
            } catch (Exception error) { status.Text = "설치 실패: " + error.Message; install.Enabled = agree.Checked; }
        };
    }
}
