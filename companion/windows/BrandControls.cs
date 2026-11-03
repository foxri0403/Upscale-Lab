using System.Drawing.Drawing2D;

namespace UpscaleLab.Companion;

internal sealed class BrandPanel : Panel
{
    internal BrandPanel() { DoubleBuffered = true; BackColor = Color.White; Padding = new(20); Margin = new(0, 0, 0, 16); }
    internal Color Fill { get; set; } = Color.White;
    internal int CornerRadius { get; set; } = 16;
    protected override void OnPaintBackground(PaintEventArgs e) {
        e.Graphics.Clear(AppBrand.Surface(Parent));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Shape(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius);
        using var fill = new SolidBrush(Fill); using var border = new Pen(Color.FromArgb(222, 226, 241));
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
    }
    internal static GraphicsPath Shape(Rectangle area, int radius) {
        var path = new GraphicsPath(); var size = radius * 2;
        if (radius <= 0) { path.AddRectangle(area); return path; }
        path.AddArc(area.Left, area.Top, size, size, 180, 90);
        path.AddArc(area.Right - size, area.Top, size, size, 270, 90);
        path.AddArc(area.Right - size, area.Bottom - size, size, size, 0, 90);
        path.AddArc(area.Left, area.Bottom - size, size, size, 90, 90); path.CloseFigure(); return path;
    }
}

internal sealed class BrandButton : Button
{
    internal bool Primary { get; set; }
    internal int CornerRadius { get; set; } = 11;
    internal BrandButton() { FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand; MinimumSize = new(120, 48); Padding = new(12, 8, 12, 8); }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(AppBrand.Surface(Parent)); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = BrandPanel.Shape(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius);
        using var fill = new SolidBrush(Enabled ? (Primary ? AppBrand.Primary : Color.White) : Color.FromArgb(232, 234, 243));
        using var border = new Pen(Primary ? AppBrand.Primary : Color.FromArgb(181, 190, 218));
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? (Primary ? Color.White : AppBrand.Ink) : AppBrand.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5));
    }
}

internal sealed class CircularAvatar : PictureBox
{
    internal CircularAvatar() { SizeMode = PictureBoxSizeMode.Zoom; BackColor = Color.FromArgb(231, 234, 248); }
    protected override void OnSizeChanged(EventArgs e) {
        base.OnSizeChanged(e); using var path = new GraphicsPath(); path.AddEllipse(ClientRectangle);
        var previous = Region; Region = new Region(path); previous?.Dispose();
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); if (Image != null) return;
        using var fill = new SolidBrush(AppBrand.Muted); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillEllipse(fill, Width * .35f, Height * .2f, Width * .3f, Height * .3f);
        e.Graphics.FillEllipse(fill, Width * .2f, Height * .56f, Width * .6f, Height * .5f);
    }
}
