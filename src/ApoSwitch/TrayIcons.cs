using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ApoSwitch;

internal static class TrayIcons
{
    public static int PixelSize
    {
        get
        {
            nint taskbar = FindWindow("Shell_TrayWnd", null);
            uint dpi = taskbar != 0 ? GetDpiForWindow(taskbar) : GetDpiForSystem();
            return Math.Max(16, GetSystemMetricsForDpi(49, dpi == 0 ? 96u : dpi)); // SM_CXSMICON
        }
    }

    public static Icon Create(bool warning, int presetNumber = 0, int pixelSize = 0, bool? taskbarDark = null,
        bool multiplePresets = false, bool unavailable = false)
    {
        int size = pixelSize > 0 ? pixelSize : PixelSize;
        using var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);
        bool dark = taskbarDark ?? WindowsSettings.TaskbarDark;
        using var foreground = new SolidBrush(warning ? Color.FromArgb(255, 193, 7)
            : dark ? Color.FromArgb(247, 248, 251) : Color.FromArgb(32, 36, 44));
        if (unavailable && !warning)
        {
            DrawUnavailable(graphics, size, foreground);
            return ToIcon(bitmap);
        }
        using var family = new FontFamily(warning ? "Segoe Fluent Icons" : "Segoe UI");
        using var format = StringFormat.GenericTypographic;
        string suffix = multiplePresets ? "+" : presetNumber > 0
            ? presetNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) : "?";
        var metrics = warning ? default : MeasurePreset(family, format);
        // WarningSolid (U+F736) is supplied by Windows 11's Segoe Fluent Icons font.
        using var path = warning ? CreateTextPath("\uF736", family, format, FontStyle.Regular) : CreatePresetPath(suffix, family, format, metrics);
        // Numbers and state symbols share layout bounds so EQ stays fixed when the state changes.
        RectangleF bounds = warning ? path.GetBounds()
            : new RectangleF(0, metrics.Top, metrics.PrefixWidth + 1 + suffix.Length * metrics.SlotWidth, metrics.Bottom - metrics.Top);
        var target = warning
            ? new RectangleF(size / 32f, size / 32f, size * .9375f, size * .9375f)
            : new RectangleF(size / 32f, size * .09375f, size * .9375f, size * .8125f);
        float scale = Math.Min(target.Width / bounds.Width, target.Height / bounds.Height);
        using var transform = new Matrix(scale, 0, 0, scale,
            target.X + (target.Width - bounds.Width * scale) / 2 - bounds.X * scale,
            target.Y + (target.Height - bounds.Height * scale) / 2 - bounds.Y * scale);
        path.Transform(transform);
        if (warning)
        {
            // Back the glyph's cutouts with dark ink so the exclamation stays legible on a light taskbar.
            using var backing = new SolidBrush(Color.FromArgb(30, 30, 30));
            using var contours = new GraphicsPathIterator(path);
            using var contour = new GraphicsPath();
            while (contours.NextSubpath(contour, out _) > 0)
            {
                graphics.FillPath(backing, contour);
                contour.Reset();
            }
        }
        graphics.FillPath(foreground, path);
        return ToIcon(bitmap);
    }

    private static Icon ToIcon(Bitmap bitmap)
    {
        nint icon = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(icon).Clone(); }
        finally { DestroyIcon(icon); }
    }

    private static void DrawUnavailable(Graphics graphics, int size, Brush foreground)
    {
        // Scheme D, on a 16-unit canvas: dominant Blocked glyph with a lower-left EQ badge.
        graphics.ScaleTransform(size / 16f, size / 16f);
        using var symbols = new FontFamily("Segoe Fluent Icons");
        using var letters = new FontFamily("Segoe UI");
        using var format = StringFormat.GenericTypographic;
        using var blocked = CreateTextPath("\uE733", symbols, format, FontStyle.Regular);
        DrawFittedPath(graphics, blocked, new RectangleF(2.1f, .5f, 13.4f, 13.4f), foreground);

        // Clear to transparency so the badge has breathing room on any taskbar background.
        graphics.CompositingMode = CompositingMode.SourceCopy;
        using (var transparent = new SolidBrush(Color.Transparent))
            graphics.FillRectangle(transparent, 0, 9.8f, 9.4f, 6.2f);
        graphics.CompositingMode = CompositingMode.SourceOver;
        using var badge = CreateTextPath("EQ", letters, format);
        DrawFittedPath(graphics, badge, new RectangleF(.3f, 10.5f, 8.4f, 5), foreground);
    }

    private static void DrawFittedPath(Graphics graphics, GraphicsPath path, RectangleF target, Brush foreground)
    {
        RectangleF bounds = path.GetBounds();
        float scale = Math.Min(target.Width / bounds.Width, target.Height / bounds.Height);
        using var placement = new Matrix(scale, 0, 0, scale,
            target.X + (target.Width - bounds.Width * scale) / 2 - bounds.X * scale,
            target.Y + (target.Height - bounds.Height * scale) / 2 - bounds.Y * scale);
        path.Transform(placement);
        graphics.FillPath(foreground, path);
    }

    private static GraphicsPath CreateTextPath(string text, FontFamily family, StringFormat format,
        FontStyle style = FontStyle.Bold)
    {
        var path = new GraphicsPath();
        path.AddString(text, family, (int)style, 32, PointF.Empty, format);
        return path;
    }

    private static (float PrefixWidth, float SlotWidth, float Top, float Bottom) MeasurePreset(FontFamily family, StringFormat format)
    {
        using var prefix = CreateTextPath("EQ", family, format);
        RectangleF bounds = prefix.GetBounds();
        float slotWidth = 0, top = bounds.Top, bottom = bounds.Bottom;
        foreach (char character in "0123456789+?−")
        {
            using var glyph = CreateTextPath(character.ToString(), family, format);
            RectangleF glyphBounds = glyph.GetBounds();
            slotWidth = Math.Max(slotWidth, glyphBounds.Width);
            top = Math.Min(top, glyphBounds.Top);
            bottom = Math.Max(bottom, glyphBounds.Bottom);
        }
        return (bounds.Width, slotWidth, top, bottom);
    }

    private static GraphicsPath CreatePresetPath(string suffix, FontFamily family, StringFormat format,
        (float PrefixWidth, float SlotWidth, float Top, float Bottom) metrics)
    {
        var path = CreateTextPath("EQ", family, format);
        using (var shift = new Matrix(1, 0, 0, 1, -path.GetBounds().Left, 0)) path.Transform(shift);
        for (int i = 0; i < suffix.Length; i++)
        {
            using var glyph = CreateTextPath(suffix[i].ToString(), family, format);
            RectangleF bounds = glyph.GetBounds();
            float x = metrics.PrefixWidth + 1 + i * metrics.SlotWidth + (metrics.SlotWidth - bounds.Width) / 2 - bounds.Left;
            // Move only horizontally: all glyphs retain the font's shared baseline.
            using var shift = new Matrix(1, 0, 0, 1, x, 0);
            glyph.Transform(shift);
            path.AddPath(glyph, false);
        }
        return path;
    }
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string className, string? windowName);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
