namespace UpscaleLab.Companion;

internal sealed class PrivacyForm : Form
{
    internal PrivacyForm() {
        Text = CompanionConfiguration.AppName; Icon = AppBrand.Icon(); Font = new("맑은 고딕", 10);
        AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new(600, 620); MinimumSize = new(480, 520);
        BackColor = AppBrand.Background; ForeColor = AppBrand.Ink; StartPosition = FormStartPosition.CenterScreen;
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(28), ColumnCount = 1, RowCount = 5 };
        content.ColumnStyles.Add(new(SizeType.Percent, 100));
        content.RowStyles.Add(new(SizeType.Absolute, 64)); content.RowStyles.Add(new(SizeType.Absolute, 50));
        content.RowStyles.Add(new(SizeType.Percent, 100)); content.RowStyles.Add(new(SizeType.Absolute, 64)); content.RowStyles.Add(new(SizeType.Absolute, 52));
        content.Controls.Add(new PictureBox { Image = AppBrand.Image(), Size = new(56, 56), SizeMode = PictureBoxSizeMode.Zoom });
        content.Controls.Add(new Label { Text = PrivacyConsent.Title, Dock = DockStyle.Fill, Font = new(Font.FontFamily, 15, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft });
        content.Controls.Add(new TextBox { Text = PrivacyConsent.Notice, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, TabStop = true });
        var agree = new CheckBox { Text = "[필수] 기기·화면 정보 수집·이용에 동의합니다.", Dock = DockStyle.Fill };
        var next = new BrandButton { Text = "동의하고 계속", Dock = DockStyle.Fill, Primary = true, Enabled = false };
        content.Controls.Add(agree); content.Controls.Add(next); Controls.Add(content);
        agree.CheckedChanged += (_, _) => next.Enabled = agree.Checked;
        next.Click += (_, _) => {
            if (!agree.Checked) return;
            try { PrivacyConsent.Current.Accept(); DialogResult = DialogResult.OK; Close(); }
            catch (Exception error) { MessageBox.Show(this, "동의를 저장하지 못했습니다.\n" + error.Message); }
        };
    }
}
