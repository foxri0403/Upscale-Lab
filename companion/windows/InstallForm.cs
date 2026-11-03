using System.Diagnostics;

namespace UpscaleLab.Companion;

internal sealed class InstallForm : Form
{
    internal InstallForm()
    {
        Text = CompanionConfiguration.AppName + " 설치"; Icon = AppBrand.Icon();
        AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(520, 460); MinimumSize = new Size(420, 400);
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("맑은 고딕", 10);
        BackColor = AppBrand.Background; ForeColor = AppBrand.Ink;
        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(28) };
        var brand = new PictureBox { Image = AppBrand.Image(), Size = new Size(60, 60), SizeMode = PictureBoxSizeMode.Zoom };
        var title = new Label { Text = "Upscale Lab Connect 설치", AutoSize = true, Font = new Font(Font.FontFamily, 17, FontStyle.Bold), Margin = new Padding(0, 16, 0, 12) };
        var note = new Label { Text = "현재 사용자 계정에 설치합니다.\n웹과 같은 계정으로 로그인하면 기기가 연결됩니다.", AutoSize = true, ForeColor = AppBrand.Muted, Margin = new Padding(0, 0, 0, 20) };
        var desktop = new CheckBox { Text = "바탕화면에 바로 가기 만들기", Checked = true, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        var startMenu = new CheckBox { Text = "시작 메뉴에 등록", Checked = true, AutoSize = true };
        var status = new Label { AutoSize = true, ForeColor = AppBrand.Muted, Margin = new Padding(0, 16, 0, 12) };
        var install = new Button { Text = "설치", AutoSize = true }; AppBrand.Style(install, true);
        content.Controls.AddRange([brand, title, note, desktop, startMenu, status, install]); Controls.Add(content);
        content.SizeChanged += (_, _) => {
            var width = Math.Max(240, content.ClientSize.Width - content.Padding.Horizontal - 20);
            title.MaximumSize = note.MaximumSize = status.MaximumSize = new Size(width, 0);
        };
        install.Click += (_, _) => {
            install.Enabled = false;
            try {
                var executable = CompanionInstaller.ForCurrentUser().Install(Environment.ProcessPath!, desktop.Checked, startMenu.Checked);
                Process.Start(new ProcessStartInfo(executable, "--run") { UseShellExecute = true }); Close();
            } catch (Exception error) {
                status.Text = "설치하지 못했습니다. 실행 중인 연결 앱을 종료한 뒤 다시 시도해 주세요.\n" + error.Message;
                install.Enabled = true;
            }
        };
    }
}
