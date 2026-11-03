using System.Reflection;

namespace UpscaleLab.Companion;

internal static class AppBrand
{
    internal static readonly Color Background = Color.FromArgb(250, 251, 255);
    internal static readonly Color Primary = Color.FromArgb(83, 109, 254);
    internal static readonly Color Ink = Color.FromArgb(23, 32, 51);
    internal static readonly Color Muted = Color.FromArgb(119, 129, 150);

    internal static Icon Icon() {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("UpscaleLab.Companion.AppIcon")!;
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
    internal static Image Image() {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("UpscaleLab.Companion.BrandImage")!;
        using var image = System.Drawing.Image.FromStream(stream);
        return new Bitmap(image);
    }
    internal static void Style(Button button, bool primary = false) {
        button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(225, 230, 240);
        button.BackColor = primary ? Primary : Color.White;
        button.ForeColor = primary ? Color.White : Ink;
        button.Padding = new Padding(12, 8, 12, 8); button.Cursor = Cursors.Hand;
        button.MinimumSize = new Size(120, 44);
    }
}
