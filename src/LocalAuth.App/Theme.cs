using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LocalAuth.App;

internal static class Theme
{
    internal static bool IsDark { get; private set; }
    internal static string Mode { get; private set; } = "System";
    internal static Color Background => IsDark ? Color.FromArgb(20, 25, 32) : Color.FromArgb(246, 248, 250);
    internal static Color Surface => IsDark ? Color.FromArgb(31, 39, 49) : Color.White;
    internal static Color Ink => IsDark ? Color.FromArgb(232, 238, 243) : Color.FromArgb(26, 39, 48);
    internal static Color Muted => IsDark ? Color.FromArgb(159, 173, 186) : Color.FromArgb(109, 124, 136);
    internal static Color Accent => IsDark ? Color.FromArgb(112, 220, 181) : Color.FromArgb(14, 118, 99);
    internal static Color Primary => IsDark ? Color.FromArgb(24, 122, 94) : Color.FromArgb(14, 118, 99);
    internal static Color AccentLight => IsDark ? Color.FromArgb(36, 62, 54) : Color.FromArgb(228, 244, 237);
    internal static Color Navigation => IsDark ? Color.FromArgb(25, 31, 39) : Color.FromArgb(239, 243, 245);
    internal static Color Border => IsDark ? Color.FromArgb(56, 68, 81) : Color.FromArgb(222, 230, 234);
    internal static Color Warning => IsDark ? Color.FromArgb(239, 191, 111) : Color.FromArgb(181, 113, 37);

    internal static bool SetMode(string mode, bool? systemDark = null)
    {
        Mode = mode;
        var dark = mode == "Dark" || (mode == "System" && (systemDark ?? ReadSystemDark()));
        var changed = dark != IsDark;
        IsDark = dark;
        return changed;
    }

    private static bool ReadSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return false; }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);

    internal static void ApplyTitleBar(Form form)
    {
        if (!form.IsHandleCreated) return;
        try
        {
            var value = IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(form.Handle, 20, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(form.Handle, 19, ref value, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    internal static void ApplyTo(Control root)
    {
        root.BackColor = root is TextBoxBase or ComboBox or NumericUpDown ? Surface : Background;
        if (root is RoundedButton button)
        {
            var primary = Equals(button.Tag, "Primary");
            button.Fill = primary ? Primary : Surface;
            button.BorderColor = primary ? Primary : Border;
            button.ForeColor = primary ? Color.White : Ink;
        }
        else root.ForeColor = (root.Tag as string) switch { "Muted" => Muted, "Accent" => Accent, "Warning" => Warning, _ => Ink };
        if (root is Form form) ApplyTitleBar(form);
        foreach (Control child in root.Controls) ApplyTo(child);
        root.Invalidate();
    }

    internal static Font Font(float size = 10, FontStyle style = FontStyle.Regular) => new("Microsoft YaHei UI", size, style);

    internal static Label Label(string text, int x, int y, int width, int height = 28, float size = 10, Color? color = null, FontStyle style = FontStyle.Regular) => new()
    {
        Text = UiText.T(text), Location = new Point(x, y), Size = new Size(width, height),
        Font = Font(size, style), ForeColor = color ?? Ink, BackColor = Color.Transparent,
        UseMnemonic = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
        Tag = color == Muted ? "Muted" : color == Accent ? "Accent" : color == Warning ? "Warning" : "Ink"
    };

    internal static RoundedButton Button(string text, int x, int y, int width = 120, bool primary = false) => new()
    {
        Text = UiText.T(text), Location = new Point(x, y), Size = new Size(width, 42),
        Fill = primary ? Primary : Surface, ForeColor = primary ? Color.White : Ink,
        BorderColor = primary ? Primary : Border, Tag = primary ? "Primary" : null
    };

    internal static ContextMenuStrip Menu() => new()
    {
        Font = Font(), BackColor = Surface, ForeColor = Ink,
        Renderer = new ToolStripProfessionalRenderer(new MenuColors())
    };

    internal static void RefreshMenu(ContextMenuStrip menu)
    {
        menu.BackColor = Surface;
        menu.ForeColor = Ink;
        foreach (var item in menu.Items.OfType<ToolStripMenuItem>())
        {
            item.DropDown.BackColor = Surface;
            item.DropDown.ForeColor = Ink;
        }
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Border;
        public override Color MenuItemSelected => AccentLight;
        public override Color MenuItemSelectedGradientBegin => AccentLight;
        public override Color MenuItemSelectedGradientEnd => AccentLight;
        public override Color MenuItemPressedGradientBegin => AccentLight;
        public override Color MenuItemPressedGradientMiddle => AccentLight;
        public override Color MenuItemPressedGradientEnd => AccentLight;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Surface;
    }

    internal static TextBox TextBox(int x, int y, int width, bool password = false) => new()
    {
        Location = new Point(x, y), Size = new Size(width, 34), Font = Font(11),
        BorderStyle = BorderStyle.FixedSingle, UseSystemPasswordChar = password, BackColor = Surface, ForeColor = Ink,
        MaxLength = password ? 1024 : 4096
    };

    internal static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static void ConfigureDialog(Form form, string title, Size size)
    {
        form.Text = UiText.T(title);
        form.ClientSize = size;
        form.Font = Font();
        form.BackColor = Background;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.CenterParent;
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.Shown += (_, _) => { Theme.ApplyTo(form); UiText.Apply(form); };
    }
}

internal sealed class RoundedButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color Fill { get; set; } = Color.White;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color BorderColor { get; set; } = Theme.Border;
    private bool _hover;

    internal RoundedButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = Theme.Font(10, FontStyle.Bold);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Rounded(new RectangleF(1, 1, Width - 3, Height - 3), 9 * DeviceDpi / 96f);
        var fill = Enabled ? (_hover ? ControlPaint.Light(Fill, .08f) : Fill) : Theme.Border;
        using var brush = new SolidBrush(fill);
        e.Graphics.FillPath(brush, path);
        using var pen = new Pen(BorderColor);
        e.Graphics.DrawPath(pen, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Theme.Muted,
            TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5));
    }
}

internal sealed class BufferedPanel : Panel
{
    internal BufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; }
}

internal sealed class BrandMark : Control
{
    internal BrandMark()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        AccessibleName = "AuthLocal";
        AccessibleRole = AccessibleRole.Graphic;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = Math.Min(Width, Height) / 64f;
        g.ScaleTransform(scale, scale);
        using var shield = new GraphicsPath();
        shield.AddLines([new PointF(32, 4), new PointF(56, 14), new PointF(53, 37), new PointF(45, 49), new PointF(32, 60), new PointF(19, 49), new PointF(11, 37), new PointF(8, 14)]);
        shield.CloseFigure();
        using var fill = new SolidBrush(Color.FromArgb(163, 232, 202));
        using var ink = new SolidBrush(Theme.Primary);
        using var pen = new Pen(Theme.Primary, 3.5f);
        g.FillPath(fill, shield);
        g.DrawArc(pen, 24, 17, 16, 19, 180, 180);
        using var body = Theme.Rounded(new RectangleF(20, 28, 24, 19), 4);
        g.FillPath(ink, body);
        using var light = new SolidBrush(Color.FromArgb(228, 244, 237));
        g.FillEllipse(light, 30, 33, 4, 4);
        g.FillRectangle(light, 31, 36, 2, 5);
    }
}
