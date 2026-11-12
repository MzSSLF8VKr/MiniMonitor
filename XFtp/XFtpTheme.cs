using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MiniMonitor.XShell;

namespace MiniMonitor.XFtp;

/// <summary>
/// XFtp 复刻版独立深色主题。与 XShell 复刻版共享静态绘图工具，
/// 但使用自己的固定深色配色，避免两个窗口互相影响主题。
/// </summary>
internal static class XFtpTheme
{
    public static readonly Color Background = Color.FromArgb(11, 17, 25);
    public static readonly Color Panel = Color.FromArgb(18, 27, 38);
    public static readonly Color PanelAlt = Color.FromArgb(23, 34, 47);
    public static readonly Color Ribbon = Color.FromArgb(14, 22, 31);
    public static readonly Color Border = Color.FromArgb(38, 53, 69);
    public static readonly Color BorderLight = Color.FromArgb(30, 44, 59);
    public static readonly Color Text = Color.FromArgb(230, 238, 249);
    public static readonly Color Muted = Color.FromArgb(143, 163, 186);
    public static readonly Color Accent = Color.FromArgb(74, 159, 245);
    public static readonly Color AccentHover = Color.FromArgb(99, 176, 255);
    public static readonly Color AccentDark = Color.FromArgb(11, 53, 100);
    public static readonly Color Green = Color.FromArgb(63, 196, 142);
    public static readonly Color Red = Color.FromArgb(240, 106, 106);
    public static readonly Color Orange = Color.FromArgb(240, 164, 76);
    public static readonly Color HeaderFrom = Color.FromArgb(7, 26, 46);
    public static readonly Color HeaderTo = Color.FromArgb(15, 59, 102);
    public static readonly Color InputBack = Color.FromArgb(15, 23, 34);
    public static readonly Color SelectionBack = Color.FromArgb(35, 71, 109);
    public static readonly Color MenuBack = Color.FromArgb(20, 29, 40);
    public static readonly Color Hover = Color.FromArgb(31, 49, 71);
    public static readonly Color Pressed = Color.FromArgb(22, 41, 62);
    public static readonly Color StatusFrom = Color.FromArgb(13, 42, 69);
    public static readonly Color StatusTo = Color.FromArgb(8, 25, 42);

    public static readonly Font Ui9 = new("Microsoft YaHei UI", 9f);
    public static readonly Font Ui88 = new("Microsoft YaHei UI", 8.8f);
    public static readonly Font Ui88Bold = new("Microsoft YaHei UI", 8.8f, FontStyle.Bold);
    public static readonly Font Ui85 = new("Microsoft YaHei UI", 8.5f);
    public static readonly Font UiBold = new("Microsoft YaHei UI", 9f, FontStyle.Bold);
    public static readonly Font Mono9 = new("Consolas", 9f);
    public static readonly Font HeaderTitle = new("Microsoft YaHei UI", 15f, FontStyle.Bold);
    public static readonly Font UiTitle = new("Microsoft YaHei UI", 13f, FontStyle.Bold);
    public static readonly Font Mdl2Logo = new("Segoe MDL2 Assets", 15f,
        FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font Mdl2_12 = new("Segoe MDL2 Assets", 12f,
        FontStyle.Regular, GraphicsUnit.Pixel);

    public static Font Ui(float size, FontStyle style = FontStyle.Regular) =>
        new("Microsoft YaHei UI", size, style);

    public static Font Mono(float size, FontStyle style = FontStyle.Regular) =>
        new("Cascadia Mono", size, style);

    public static GraphicsPath RoundedRect(Rectangle rect, int radius) =>
        XShellTheme.RoundedRect(rect, radius);

    public static Color Lighten(Color color, int amount) => XShellTheme.Lighten(color, amount);

    public static Color Blend(Color from, Color to, float amount) =>
        XShellTheme.Blend(from, to, amount);

    public static Image Icon(string glyph, Color color, int size = 16) =>
        XShellIcons.Glyph(glyph, color, size);

    public static readonly Image EmptyServerIcon = Icon("\uE8B7", Accent, 42);

    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeLegacy = 19;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute,
        ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName,
        string? subIdList);

    /// <summary>递归启用深色滚动条、下拉箭头等原生控件主题（固定深色）。</summary>
    public static void ApplyNative(Control root)
    {
        if (root is TreeView or ListView or DataGridView or TextBox or ComboBox
            or NumericUpDown or TabControl or ListBox or Panel { AutoScroll: true })
        {
            if (root.IsHandleCreated)
            {
                try { SetWindowTheme(root.Handle, "DarkMode_Explorer", null); } catch { }
            }
        }
        if (root is NumericUpDown)
        {
            foreach (Control child in root.Controls)
            {
                if (child.IsHandleCreated)
                {
                    try { SetWindowTheme(child.Handle, "DarkMode_Explorer", null); } catch { }
                }
            }
        }
        foreach (Control child in root.Controls)
        {
            ApplyNative(child);
        }
    }

    /// <summary>固定深色 DWM 标题栏，不受 XShell 当前主题影响。</summary>
    public static void ApplyWindow(Form form)
    {
        if (!form.IsHandleCreated)
        {
            return;
        }
        try
        {
            int dark = 1;
            int result = DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkMode,
                ref dark, sizeof(int));
            if (result != 0)
            {
                DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkModeLegacy,
                    ref dark, sizeof(int));
            }
            int caption = ColorTranslator.ToWin32(Color.FromArgb(11, 21, 32));
            DwmSetWindowAttribute(form.Handle, DwmCaptionColor, ref caption, sizeof(int));
            int text = ColorTranslator.ToWin32(Text);
            DwmSetWindowAttribute(form.Handle, DwmTextColor, ref text, sizeof(int));
        }
        catch
        {
            // 旧版 Windows 不支持时保持默认标题栏。
        }
    }

    public static void StyleMenu(ToolStrip menu, bool statusStrip = false)
    {
        menu.Renderer = statusStrip ? new XFtpStatusRenderer() : new XFtpMenuRenderer();
        menu.BackColor = statusStrip ? StatusFrom : MenuBack;
        menu.ForeColor = statusStrip ? Color.White : Text;
        if (!statusStrip)
        {
            foreach (ToolStripItem item in menu.Items)
            {
                item.ForeColor = Text;
            }
        }
        menu.Invalidate();
    }

    public static void StyleList(ListView list)
    {
        list.BackColor = Panel;
        list.ForeColor = Text;
        list.BorderStyle = BorderStyle.None;
        list.Font = Ui88;
        list.FullRowSelect = true;
        list.MultiSelect = true;
        list.HideSelection = false;
        list.HeaderStyle = ColumnHeaderStyle.None;
        list.OwnerDraw = false;
        list.AllowColumnReorder = false;
        XShellTheme.EnableDoubleBuffer(list);
        if (list.IsHandleCreated)
        {
            XShellWindowTheme.ApplyControlTheme(list);
        }
    }

    public static void StyleToolbar(ToolStrip toolbar)
    {
        toolbar.Renderer = new XFtpMenuRenderer();
        toolbar.BackColor = PanelAlt;
        toolbar.ForeColor = Text;
        toolbar.GripStyle = ToolStripGripStyle.Hidden;
        toolbar.RenderMode = ToolStripRenderMode.Professional;
        toolbar.Padding = new Padding(4, 3, 4, 3);
        toolbar.Font = Ui85;
        toolbar.ImageScalingSize = new Size(16, 16);
        foreach (ToolStripItem item in toolbar.Items)
        {
            item.ForeColor = Text;
        }
    }

    public static TextBox Input(int width = 160)
    {
        return new TextBox
        {
            Width = width,
            Font = Ui9,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = InputBack,
            ForeColor = Text
        };
    }

    public static ComboBox Combo(int width = 140)
    {
        return new XFtpComboBox
        {
            Width = width,
            Font = Ui9,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = InputBack,
            ForeColor = Text
        };
    }

    public static XFtpButton PrimaryButton(string text, int width = 88, int height = 28) =>
        new()
        {
            Text = text,
            Width = width,
            Height = height,
            Primary = true,
            Font = UiBold
        };

    public static XFtpButton FlatButton(string text, int width = 88, int height = 28) =>
        new()
        {
            Text = text,
            Width = width,
            Height = height,
            Font = Ui9
        };
}

/// <summary>深色菜单 / 工具栏渲染器。</summary>
internal sealed class XFtpMenuColorTable : ProfessionalColorTable
{
    public override Color MenuStripGradientBegin => XFtpTheme.MenuBack;
    public override Color MenuStripGradientEnd => XFtpTheme.MenuBack;
    public override Color ToolStripDropDownBackground => XFtpTheme.MenuBack;
    public override Color ImageMarginGradientBegin => XFtpTheme.MenuBack;
    public override Color ImageMarginGradientMiddle => XFtpTheme.MenuBack;
    public override Color ImageMarginGradientEnd => XFtpTheme.MenuBack;
    public override Color MenuItemSelected => XFtpTheme.Hover;
    public override Color MenuItemSelectedGradientBegin => XFtpTheme.Hover;
    public override Color MenuItemSelectedGradientEnd => XFtpTheme.Hover;
    public override Color MenuItemPressedGradientBegin => XFtpTheme.Pressed;
    public override Color MenuItemPressedGradientEnd => XFtpTheme.Pressed;
    public override Color MenuItemBorder => XFtpTheme.Accent;
    public override Color MenuBorder => XFtpTheme.Border;
    public override Color SeparatorDark => XFtpTheme.Border;
    public override Color SeparatorLight => XFtpTheme.BorderLight;
    public override Color CheckBackground => XFtpTheme.AccentDark;
    public override Color CheckSelectedBackground => XFtpTheme.AccentDark;
    public override Color CheckPressedBackground => XFtpTheme.AccentDark;
    public override Color ButtonSelectedHighlight => XFtpTheme.Hover;
    public override Color ButtonSelectedHighlightBorder => XFtpTheme.Accent;
    public override Color ButtonSelectedBorder => XFtpTheme.Accent;
    public override Color ButtonSelectedGradientBegin => XFtpTheme.Hover;
    public override Color ButtonSelectedGradientEnd => XFtpTheme.Hover;
    public override Color ButtonPressedHighlight => XFtpTheme.Pressed;
    public override Color ButtonPressedHighlightBorder => XFtpTheme.Accent;
    public override Color ButtonPressedBorder => XFtpTheme.Accent;
    public override Color ButtonPressedGradientBegin => XFtpTheme.Pressed;
    public override Color ButtonPressedGradientEnd => XFtpTheme.Pressed;
    public override Color ButtonCheckedHighlight => XFtpTheme.AccentDark;
    public override Color ButtonCheckedHighlightBorder => XFtpTheme.Accent;
    public override Color ButtonCheckedGradientBegin => XFtpTheme.AccentDark;
    public override Color ButtonCheckedGradientEnd => XFtpTheme.AccentDark;
    public override Color ToolStripBorder => XFtpTheme.Border;
    public override Color ToolStripGradientBegin => XFtpTheme.PanelAlt;
    public override Color ToolStripGradientMiddle => XFtpTheme.PanelAlt;
    public override Color ToolStripGradientEnd => XFtpTheme.PanelAlt;
    public override Color ToolStripContentPanelGradientBegin => XFtpTheme.PanelAlt;
    public override Color ToolStripContentPanelGradientEnd => XFtpTheme.Panel;
}

internal sealed class XFtpMenuRenderer : ToolStripProfessionalRenderer
{
    public XFtpMenuRenderer() : base(new XFtpMenuColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (!e.Item.Enabled)
        {
            e.TextColor = XFtpTheme.Muted;
        }
        else if (e.ToolStrip is StatusStrip)
        {
            e.TextColor = e.Item.Selected ? Color.White : XFtpTheme.Muted;
        }
        else
        {
            e.TextColor = e.Item.Selected ? Color.White : XFtpTheme.Text;
        }
        base.OnRenderItemText(e);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is StatusStrip)
        {
            base.OnRenderToolStripBackground(e);
            return;
        }
        var rect = e.AffectedBounds;
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }
        if (e.ToolStrip is MenuStrip)
        {
            using var brush = new LinearGradientBrush(rect,
                XFtpTheme.MenuBack, XFtpTheme.Ribbon, LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(brush, rect);
            return;
        }
        using (var brush = new LinearGradientBrush(rect,
            XFtpTheme.PanelAlt, XFtpTheme.Ribbon, LinearGradientMode.Vertical))
        {
            e.Graphics.FillRectangle(brush, rect);
        }
        using var line = new Pen(XFtpTheme.Border);
        e.Graphics.DrawLine(line, rect.Left, rect.Bottom - 1,
            rect.Right, rect.Bottom - 1);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is ToolStripDropDown)
        {
            using var pen = new Pen(XFtpTheme.Border);
            var rect = new Rectangle(0, 0,
                Math.Max(1, e.ToolStrip.Width - 1),
                Math.Max(1, e.ToolStrip.Height - 1));
            e.Graphics.DrawRectangle(pen, rect);
        }
        else
        {
            base.OnRenderToolStripBorder(e);
        }
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = e.ImageRectangle;
        using var pen = new Pen(XFtpTheme.Accent, 2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        g.DrawLines(pen, new[]
        {
            new Point(rect.Left + 3, rect.Top + rect.Height / 2),
            new Point(rect.Left + rect.Width / 2, rect.Bottom - 4),
            new Point(rect.Right - 2, rect.Top + 3)
        });
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = XFtpTheme.Text;
        base.OnRenderArrow(e);
    }
}

internal sealed class XFtpStatusRenderer : ToolStripProfessionalRenderer
{
    public XFtpStatusRenderer() : base(new XFtpMenuColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        var rect = e.ToolStrip.ClientRectangle;
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }
        using var brush = new LinearGradientBrush(rect,
            XFtpTheme.StatusFrom, XFtpTheme.StatusTo, LinearGradientMode.Horizontal);
        e.Graphics.FillRectangle(brush, rect);
        using var line = new Pen(Color.FromArgb(90, XFtpTheme.Accent));
        e.Graphics.DrawLine(line, 0, 0, rect.Width, 0);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled
            ? (e.Item.Selected ? Color.White : XFtpTheme.Muted)
            : Color.FromArgb(150, XFtpTheme.Muted);
        base.OnRenderItemText(e);
    }
}
