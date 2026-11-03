using System.Drawing.Drawing2D;

namespace UpscaleLab.Companion;

internal sealed class BrandPanel : Panel
{
    internal BrandPanel() { DoubleBuffered = true; BackColor = Color.White; Padding = new(20); Margin = new(0, 0, 0, 16); }
    internal Color Fill { get; set; } = Color.White;
    protected override void OnPaintBackground(PaintEventArgs e) {
        e.Graphics.Clear(AppBrand.Surface(Parent));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Shape(new Rectangle(0, 0, Width - 1, Height - 1), 16);
        using var fill = new SolidBrush(Fill); using var border = new Pen(Color.FromArgb(222, 226, 241));
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
    }
    internal static GraphicsPath Shape(Rectangle area, int radius) {
        var path = new GraphicsPath(); var size = radius * 2;
        path.AddArc(area.Left, area.Top, size, size, 180, 90);
        path.AddArc(area.Right - size, area.Top, size, size, 270, 90);
        path.AddArc(area.Right - size, area.Bottom - size, size, size, 0, 90);
        path.AddArc(area.Left, area.Bottom - size, size, size, 90, 90); path.CloseFigure(); return path;
    }
}

internal sealed class BrandButton : Button
{
    internal bool Primary { get; set; }
    internal BrandButton() { FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand; MinimumSize = new(120, 48); Padding = new(12, 8, 12, 8); }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(AppBrand.Surface(Parent)); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = BrandPanel.Shape(new Rectangle(0, 0, Width - 1, Height - 1), 11);
        using var fill = new SolidBrush(Enabled ? (Primary ? AppBrand.Primary : Color.White) : Color.FromArgb(232, 234, 243));
        using var border = new Pen(Primary ? AppBrand.Primary : Color.FromArgb(181, 190, 218));
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? (Primary ? Color.White : AppBrand.Ink) : AppBrand.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5));
    }
}

internal sealed class BrandToggle : CheckBox
{
    internal BrandToggle() { SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); Cursor = Cursors.Hand; }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.Clear(AppBrand.Surface(Parent)); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var area = new Rectangle(Width - 48, (Height - 26) / 2, 46, 26);
        using var track = BrandPanel.Shape(area, 13);
        using var fill = new SolidBrush(Checked ? AppBrand.Primary : Color.FromArgb(183, 190, 211));
        using var thumb = new SolidBrush(Color.White);
        e.Graphics.FillPath(fill, track); e.Graphics.FillEllipse(thumb, area.X + (Checked ? 23 : 3), area.Y + 3, 20, 20);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, 0, Width - 56, Height), Enabled ? AppBrand.Ink : AppBrand.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -2, -2));
    }
}

internal sealed class MonitorGlyph : Control
{
    internal MonitorGlyph() { Size = new(58, 54); SetStyle(ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; }
    protected override void OnPaint(PaintEventArgs e) {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(AppBrand.Primary, 2.5f);
        using var path = BrandPanel.Shape(new(4, 6, 46, 30), 4);
        e.Graphics.DrawPath(pen, path); e.Graphics.DrawLine(pen, 27, 37, 27, 44); e.Graphics.DrawLine(pen, 17, 45, 37, 45);
    }
}
