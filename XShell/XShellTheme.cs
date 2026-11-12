using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MiniMonitor.XShell;

/// <summary>XShell 复刻版的工作区配色：深色（默认）、浅色，以及跟随 Windows 系统。</summary>
internal sealed class XShellUiPalette
{
    public string Name { get; init; } = "";
    public bool IsDark { get; init; }

    public Color Background { get; init; }
    public Color Panel { get; init; }
    public Color PanelAlt { get; init; }
    public Color Ribbon { get; init; }
    public Color RibbonGroup { get; init; }
    public Color Border { get; init; }
    public Color BorderLight { get; init; }
    public Color Text { get; init; }
    public Color Muted { get; init; }
    public Color Accent { get; init; }
    public Color AccentHover { get; init; }
    public Color AccentDark { get; init; }
    public Color AccentLight { get; init; }
    public Color Green { get; init; }
    public Color GreenLight { get; init; }
    public Color Red { get; init; }
    public Color Orange { get; init; }
    public Color TabStrip { get; init; }
    public Color TabInactive { get; init; }
    public Color TabActive { get; init; }
    public Color Terminal { get; init; }
    public Color Status { get; init; }
    public Color StatusFrom { get; init; }
    public Color StatusTo { get; init; }
    public Color HeaderFrom { get; init; }
    public Color HeaderTo { get; init; }
    public Color Card { get; init; }
    public Color CardHover { get; init; }
    public Color InputBack { get; init; }
    public Color InputBorder { get; init; }
    public Color SelectionBack { get; init; }
    public Color SelectionFore { get; init; }
    public Color MenuBack { get; init; }
    public Color MenuText { get; init; }
    public Color Hover { get; init; }
    public Color Pressed { get; init; }
    public Color Shadow { get; init; }
    public Color HeroText { get; init; }
    public Color HeroMuted { get; init; }
    public Color Caption { get; init; }
    public Color CaptionText { get; init; }
}

/// <summary>XShell 8 风格的主题、圆角控件与菜单渲染器。</summary>
internal static class XShellTheme
{
    public static readonly XShellUiPalette LightPalette = new()
    {
        Name = "浅色",
        IsDark = false,
        Background = Color.FromArgb(238, 243, 249),
        Panel = Color.White,
        PanelAlt = Color.FromArgb(246, 249, 253),
        Ribbon = Color.FromArgb(249, 251, 254),
        RibbonGroup = Color.FromArgb(242, 247, 252),
        Border = Color.FromArgb(198, 210, 224),
        BorderLight = Color.FromArgb(222, 231, 240),
        Text = Color.FromArgb(27, 41, 58),
        Muted = Color.FromArgb(104, 122, 142),
        Accent = Color.FromArgb(43, 124, 211),
        AccentHover = Color.FromArgb(61, 144, 232),
        AccentDark = Color.FromArgb(12, 67, 124),
        AccentLight = Color.FromArgb(219, 237, 253),
        Green = Color.FromArgb(26, 142, 92),
        GreenLight = Color.FromArgb(220, 244, 233),
        Red = Color.FromArgb(192, 57, 43),
        Orange = Color.FromArgb(214, 126, 30),
        TabStrip = Color.FromArgb(216, 226, 238),
        TabInactive = Color.FromArgb(232, 240, 248),
        TabActive = Color.White,
        Terminal = Color.FromArgb(12, 12, 12),
        Status = Color.FromArgb(23, 62, 99),
        StatusFrom = Color.FromArgb(27, 75, 120),
        StatusTo = Color.FromArgb(15, 45, 76),
        HeaderFrom = Color.FromArgb(9, 39, 71),
        HeaderTo = Color.FromArgb(24, 92, 152),
        Card = Color.White,
        CardHover = Color.FromArgb(244, 250, 255),
        InputBack = Color.White,
        InputBorder = Color.FromArgb(198, 210, 224),
        SelectionBack = Color.FromArgb(207, 228, 248),
        SelectionFore = Color.FromArgb(21, 48, 77),
        MenuBack = Color.White,
        MenuText = Color.FromArgb(27, 41, 58),
        Hover = Color.FromArgb(232, 243, 253),
        Pressed = Color.FromArgb(205, 228, 249),
        Shadow = Color.FromArgb(150, 168, 188),
        HeroText = Color.White,
        HeroMuted = Color.FromArgb(201, 223, 245),
        Caption = Color.White,
        CaptionText = Color.FromArgb(27, 41, 58)
    };

    public static readonly XShellUiPalette DarkPalette = new()
    {
        Name = "深色",
        IsDark = true,
        Background = Color.FromArgb(13, 19, 27),
        Panel = Color.FromArgb(22, 31, 43),
        PanelAlt = Color.FromArgb(26, 37, 50),
        Ribbon = Color.FromArgb(18, 26, 36),
        RibbonGroup = Color.FromArgb(24, 34, 47),
        Border = Color.FromArgb(42, 57, 76),
        BorderLight = Color.FromArgb(34, 47, 64),
        Text = Color.FromArgb(230, 238, 249),
        Muted = Color.FromArgb(143, 163, 186),
        Accent = Color.FromArgb(74, 159, 245),
        AccentHover = Color.FromArgb(99, 176, 255),
        AccentDark = Color.FromArgb(11, 53, 100),
        AccentLight = Color.FromArgb(29, 51, 80),
        Green = Color.FromArgb(63, 196, 142),
        GreenLight = Color.FromArgb(18, 51, 42),
        Red = Color.FromArgb(240, 106, 106),
        Orange = Color.FromArgb(240, 164, 76),
        TabStrip = Color.FromArgb(10, 15, 22),
        TabInactive = Color.FromArgb(22, 31, 43),
        TabActive = Color.FromArgb(29, 41, 55),
        Terminal = Color.FromArgb(12, 12, 12),
        Status = Color.FromArgb(9, 27, 45),
        StatusFrom = Color.FromArgb(13, 42, 69),
        StatusTo = Color.FromArgb(8, 25, 42),
        HeaderFrom = Color.FromArgb(7, 26, 46),
        HeaderTo = Color.FromArgb(15, 59, 102),
        Card = Color.FromArgb(24, 34, 47),
        CardHover = Color.FromArgb(31, 44, 60),
        InputBack = Color.FromArgb(15, 23, 34),
        InputBorder = Color.FromArgb(42, 57, 76),
        SelectionBack = Color.FromArgb(35, 71, 109),
        SelectionFore = Color.White,
        MenuBack = Color.FromArgb(20, 29, 40),
        MenuText = Color.FromArgb(230, 238, 249),
        Hover = Color.FromArgb(31, 49, 71),
        Pressed = Color.FromArgb(22, 41, 62),
        Shadow = Color.FromArgb(0, 0, 0),
        HeroText = Color.White,
        HeroMuted = Color.FromArgb(169, 199, 232),
        Caption = Color.FromArgb(11, 21, 32),
        CaptionText = Color.FromArgb(230, 238, 249)
    };

    public static XShellUiPalette Current { get; private set; } = DarkPalette;
    public static string RequestedMode { get; private set; } = "Dark";
    public static bool IsDark => Current.IsDark;

    public static Color Background => Current.Background;
    public static Color Panel => Current.Panel;
    public static Color PanelAlt => Current.PanelAlt;
    public static Color Ribbon => Current.Ribbon;
    public static Color RibbonGroup => Current.RibbonGroup;
    public static Color Border => Current.Border;
    public static Color BorderLight => Current.BorderLight;
    public static Color Text => Current.Text;
    public static Color Muted => Current.Muted;
    public static Color Accent => Current.Accent;
    public static Color AccentHover => Current.AccentHover;
    public static Color AccentDark => Current.AccentDark;
    public static Color AccentLight => Current.AccentLight;
    public static Color Green => Current.Green;
    public static Color GreenLight => Current.GreenLight;
    public static Color Red => Current.Red;
    public static Color Orange => Current.Orange;
    public static Color TabStrip => Current.TabStrip;
    public static Color TabInactive => Current.TabInactive;
    public static Color TabActive => Current.TabActive;
    public static Color Terminal => Current.Terminal;
    public static Color Status => Current.Status;
    public static Color HeaderFrom => Current.HeaderFrom;
    public static Color HeaderTo => Current.HeaderTo;
    public static Color Card => Current.Card;
    public static Color CardHover => Current.CardHover;
    public static Color InputBack => Current.InputBack;
    public static Color InputBorder => Current.InputBorder;
    public static Color SelectionBack => Current.SelectionBack;
    public static Color Shadow => Current.Shadow;
    public static Color Hover => Current.Hover;
    public static Color Pressed => Current.Pressed;
    public static Color HeroText => Current.HeroText;
    public static Color HeroMuted => Current.HeroMuted;

    public static string NormalizeMode(string? mode) => mode switch
    {
        "Light" or "浅色" => "Light",
        "System" or "跟随系统" => "System",
        _ => "Dark"
    };

    public static bool IsSystemDark()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? value = key?.GetValue("AppsUseLightTheme");
            return value is int light && light == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>按请求的模式切换主题，返回切换前的配色（用于映射已有控件）。</summary>
    public static XShellUiPalette Apply(string? requestedMode)
    {
        var old = Current;
        RequestedMode = NormalizeMode(requestedMode);
        bool dark = RequestedMode switch
        {
            "Light" => false,
            "System" => IsSystemDark(),
            _ => true
        };
        Current = dark ? DarkPalette : LightPalette;
        return old;
    }

    public static Color ToneColor(XShellTone tone) => tone switch
    {
        XShellTone.Green => Green,
        XShellTone.Red => Red,
        XShellTone.Orange => Orange,
        XShellTone.Muted => Muted,
        XShellTone.Text => Text,
        XShellTone.White => Color.White,
        _ => Accent
    };

    public static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(from.A + (to.A - from.A) * amount),
            (int)Math.Round(from.R + (to.R - from.R) * amount),
            (int)Math.Round(from.G + (to.G - from.G) * amount),
            (int)Math.Round(from.B + (to.B - from.B) * amount));
    }

    public static Color Lighten(Color color, int amount) => Color.FromArgb(
        color.A,
        Math.Min(255, color.R + amount),
        Math.Min(255, color.G + amount),
        Math.Min(255, color.B + amount));

    public static Color Darken(Color color, int amount) => Color.FromArgb(
        color.A,
        Math.Max(0, color.R - amount),
        Math.Max(0, color.G - amount),
        Math.Max(0, color.B - amount));

    public static Color WithAlpha(Color color, int alpha) =>
        Color.FromArgb(Math.Clamp(alpha, 0, 255), color);

    public static Font Ui(float size = 9f, FontStyle style = FontStyle.Regular) =>
        new("Microsoft YaHei UI", size, style);

    public static Font Mono(float size = 10f, FontStyle style = FontStyle.Regular) =>
        new("Consolas", size, style);

    public static void EnableDoubleBuffer(Control control)
    {
        typeof(Control).GetProperty("DoubleBuffered",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(control, true, null);
    }

    public static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Max(2, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
        if (d <= 2)
        {
            path.AddRectangle(rect);
            return path;
        }
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static ToolStripRenderer StyleMenu(ToolStrip menu)
    {
        ToolStripRenderer renderer = menu is StatusStrip
            ? new XShellStatusRenderer()
            : new XShellMenuRenderer();
        menu.Renderer = renderer;
        menu.BackColor = menu is StatusStrip ? Status : menu is MenuStrip ? Current.MenuBack : PanelAlt;
        menu.ForeColor = menu is StatusStrip ? Color.White : Text;
        if (menu is not StatusStrip)
        {
            foreach (ToolStripItem item in menu.Items)
            {
                item.ForeColor = Text;
            }
        }
        menu.Invalidate();
        return renderer;
    }

    public static void StyleTextBox(TextBox box)
    {
        box.BackColor = InputBack;
        box.ForeColor = Text;
        if (box.IsHandleCreated)
        {
            XShellWindowTheme.ApplyControlTheme(box);
        }
    }

    public static void StyleCombo(ComboBox combo)
    {
        combo.BackColor = InputBack;
        combo.ForeColor = Text;
        if (combo.IsHandleCreated)
        {
            XShellWindowTheme.ApplyControlTheme(combo);
        }
    }

    /// <summary>为已创建句柄的原生控件启用深色滚动条 / 展开箭头。</summary>
    public static void ApplyNativeTheme(Control root)
    {
        if (root is TreeView or ListView or DataGridView or TextBox or ComboBox
            or NumericUpDown or TabControl or ListBox or Panel { AutoScroll: true })
        {
            XShellWindowTheme.ApplyControlTheme(root);
        }
        if (root is NumericUpDown)
        {
            foreach (Control child in root.Controls)
            {
                XShellWindowTheme.ApplyControlTheme(child);
            }
        }
        foreach (Control child in root.Controls)
        {
            ApplyNativeTheme(child);
        }
    }

    /// <summary>把对话框中没有显式设置颜色的文本控件切换到当前主题。</summary>
    public static void StyleTextControls(Control root)
    {
        foreach (Control control in root.Controls)
        {
            switch (control)
            {
                case Label or System.Windows.Forms.CheckBox or RadioButton or GroupBox:
                    if (control.ForeColor.ToArgb() == SystemColors.ControlText.ToArgb()
                        || control.ForeColor.ToArgb() == Color.Black.ToArgb())
                    {
                        control.ForeColor = Text;
                    }
                    break;
                case TextBox box:
                    StyleTextBox(box);
                    break;
                case ComboBox combo:
                    StyleCombo(combo);
                    break;
                case NumericUpDown number:
                    number.BackColor = InputBack;
                    number.ForeColor = Text;
                    break;
            }
            StyleTextControls(control);
        }
    }

    public static void StyleGrid(DataGridView grid)
    {
        EnableDoubleBuffer(grid);
        grid.BackgroundColor = Panel;
        grid.BorderStyle = BorderStyle.None;
        grid.GridColor = BorderLight;
        grid.RowHeadersVisible = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.MultiSelect = true;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.ColumnHeadersHeight = 30;
        grid.RowTemplate.Height = 28;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = PanelAlt,
            ForeColor = Muted,
            Font = Ui(8.8f, FontStyle.Bold),
            SelectionBackColor = PanelAlt,
            SelectionForeColor = Muted
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Panel,
            ForeColor = Text,
            Font = Ui(8.8f),
            SelectionBackColor = SelectionBack,
            SelectionForeColor = Current.SelectionFore
        };
        if (grid.IsHandleCreated)
        {
            XShellWindowTheme.ApplyControlTheme(grid);
        }
    }

    /// <summary>主题切换后刷新已有控件：按旧配色映射到新配色，并重绘自绘控件。</summary>
    public static void Refresh(Control root, XShellUiPalette oldPalette)
    {
        ApplyToControl(root, oldPalette, Current);
        foreach (Control child in root.Controls)
        {
            Refresh(child, oldPalette);
        }
        if (root.ContextMenuStrip != null)
        {
            StyleMenu(root.ContextMenuStrip);
        }
        root.Invalidate(true);
    }

    private static void ApplyToControl(Control control, XShellUiPalette oldPalette,
        XShellUiPalette newPalette)
    {
        control.BackColor = MapColor(control.BackColor, oldPalette, newPalette);
        control.ForeColor = MapColor(control.ForeColor, oldPalette, newPalette);

        if (control is XShellButton or XShellIconButton or XShellRibbonButton
            or XShellTabControl or XShellActionCard or XShellRecentRow or XShellLogo
            or XShellUploadTaskRow)
        {
            if (control is XShellTabControl tabControl)
            {
                XShellWindowTheme.ApplyControlTheme(tabControl);
            }
            control.Invalidate();
            return;
        }

        switch (control)
        {
            case MenuStrip or ToolStrip when control is not StatusStrip:
                StyleMenu((ToolStrip)control);
                break;
            case StatusStrip status:
                StyleMenu(status);
                foreach (ToolStripItem item in status.Items)
                {
                    if (item.Name != "state")
                    {
                        item.ForeColor = Color.White;
                    }
                }
                break;
            case NumericUpDown number:
                number.BackColor = InputBack;
                number.ForeColor = Text;
                break;
            case TreeView tree:
                tree.BackColor = Panel;
                tree.ForeColor = Text;
                tree.LineColor = Border;
                XShellWindowTheme.ApplyControlTheme(tree);
                break;
            case ListView list:
                list.BackColor = Panel;
                list.ForeColor = Text;
                XShellWindowTheme.ApplyControlTheme(list);
                break;
            case TabPage page:
                page.BackColor = Panel;
                break;
            case DataGridView grid:
                StyleGrid(grid);
                XShellWindowTheme.ApplyControlTheme(grid);
                break;
            case TextBox box:
                StyleTextBox(box);
                XShellWindowTheme.ApplyControlTheme(box);
                break;
            case ComboBox combo:
                StyleCombo(combo);
                XShellWindowTheme.ApplyControlTheme(combo);
                break;
        }
    }

    private static Color MapColor(Color color, XShellUiPalette oldPalette,
        XShellUiPalette newPalette)
    {
        if (color.IsEmpty || color.A == 0 || color == Color.Transparent)
        {
            return color;
        }
        if (color.ToArgb() == oldPalette.Background.ToArgb()) return newPalette.Background;
        if (color.ToArgb() == oldPalette.Panel.ToArgb()) return newPalette.Panel;
        if (color.ToArgb() == oldPalette.PanelAlt.ToArgb()) return newPalette.PanelAlt;
        if (color.ToArgb() == oldPalette.Ribbon.ToArgb()) return newPalette.Ribbon;
        if (color.ToArgb() == oldPalette.RibbonGroup.ToArgb()) return newPalette.RibbonGroup;
        if (color.ToArgb() == oldPalette.Border.ToArgb()) return newPalette.Border;
        if (color.ToArgb() == oldPalette.BorderLight.ToArgb()) return newPalette.BorderLight;
        if (color.ToArgb() == oldPalette.Text.ToArgb()) return newPalette.Text;
        if (color.ToArgb() == oldPalette.Muted.ToArgb()) return newPalette.Muted;
        if (color.ToArgb() == oldPalette.Accent.ToArgb()) return newPalette.Accent;
        if (color.ToArgb() == oldPalette.AccentHover.ToArgb()) return newPalette.AccentHover;
        if (color.ToArgb() == oldPalette.AccentLight.ToArgb()) return newPalette.AccentLight;
        if (color.ToArgb() == oldPalette.AccentDark.ToArgb()) return newPalette.AccentDark;
        if (color.ToArgb() == oldPalette.Green.ToArgb()) return newPalette.Green;
        if (color.ToArgb() == oldPalette.GreenLight.ToArgb()) return newPalette.GreenLight;
        if (color.ToArgb() == oldPalette.Red.ToArgb()) return newPalette.Red;
        if (color.ToArgb() == oldPalette.Orange.ToArgb()) return newPalette.Orange;
        if (color.ToArgb() == oldPalette.Card.ToArgb()) return newPalette.Card;
        if (color.ToArgb() == oldPalette.CardHover.ToArgb()) return newPalette.CardHover;
        if (color.ToArgb() == oldPalette.InputBack.ToArgb()) return newPalette.InputBack;
        if (color.ToArgb() == oldPalette.InputBorder.ToArgb()) return newPalette.InputBorder;
        if (color.ToArgb() == oldPalette.MenuBack.ToArgb()) return newPalette.MenuBack;
        if (color.ToArgb() == oldPalette.Status.ToArgb()) return newPalette.Status;
        if (color.ToArgb() == oldPalette.StatusFrom.ToArgb()) return newPalette.StatusFrom;
        if (color.ToArgb() == oldPalette.StatusTo.ToArgb()) return newPalette.StatusTo;
        if (color.ToArgb() == oldPalette.HeaderFrom.ToArgb()) return newPalette.HeaderFrom;
        if (color.ToArgb() == oldPalette.HeaderTo.ToArgb()) return newPalette.HeaderTo;
        if (color.ToArgb() == oldPalette.Hover.ToArgb()) return newPalette.Hover;
        if (color.ToArgb() == oldPalette.Pressed.ToArgb()) return newPalette.Pressed;
        if (color.ToArgb() == oldPalette.SelectionBack.ToArgb()) return newPalette.SelectionBack;
        if (color.ToArgb() == oldPalette.TabActive.ToArgb()) return newPalette.TabActive;
        if (color.ToArgb() == oldPalette.TabInactive.ToArgb()) return newPalette.TabInactive;
        if (color.ToArgb() == oldPalette.TabStrip.ToArgb()) return newPalette.TabStrip;
        return color;
    }

    public static XShellButton FlatButton(string text, int width = 78, int height = 27)
    {
        return new XShellButton
        {
            Text = text,
            Width = width,
            Height = height,
            Font = Ui(8.8f)
        };
    }

    public static XShellButton PrimaryButton(string text, int width = 84, int height = 27)
    {
        var button = new XShellButton
        {
            Text = text,
            Width = width,
            Height = height,
            Font = Ui(8.8f),
            AccentStyle = true
        };
        return button;
    }

    public static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Muted,
        Font = Ui(8.5f),
        BackColor = Color.Transparent
    };

    public static TextBox Input(int width = 180)
    {
        return new TextBox
        {
            Width = width,
            Font = Ui(9f),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = InputBack,
            ForeColor = Text
        };
    }

    public static ComboBox Combo(int width = 160)
    {
        return new XShellComboBox
        {
            Width = width,
            Font = Ui(9f),
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = InputBack,
            ForeColor = Text
        };
    }

    public static CheckBox CheckBox(string text)
    {
        return new XShellCheckBox
        {
            Text = text,
            AutoSize = true,
            ForeColor = Text,
            Font = Ui(9f)
        };
    }
}

internal enum XShellTone
{
    Accent,
    Green,
    Red,
    Orange,
    Muted,
    Text,
    White
}

/// <summary>圆角、带渐变与悬停光效的按钮，适配浅色 / 深色主题。</summary>
internal sealed class XShellButton : Button
{
    private bool _hover;
    private bool _down;

    public bool AccentStyle { get; set; }
    public bool GhostStyle { get; set; }

    public XShellButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        ForeColor = XShellTheme.Text;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        _down = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);
        if (mevent.Button == MouseButtons.Left)
        {
            _down = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        _down = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        int radius = Math.Max(4, (int)Math.Round(6 * DeviceDpi / 96.0));
        using var path = XShellTheme.RoundedRect(rect, radius);

        Color fill;
        Color border;
        Color text;
        if (!Enabled)
        {
            fill = XShellTheme.PanelAlt;
            border = XShellTheme.BorderLight;
            text = XShellTheme.Muted;
        }
        else if (AccentStyle)
        {
            fill = _down ? XShellTheme.AccentDark
                : _hover ? XShellTheme.AccentHover : XShellTheme.Accent;
            border = XShellTheme.AccentDark;
            text = Color.White;
        }
        else if (GhostStyle)
        {
            fill = _hover ? XShellTheme.Hover : XShellTheme.Panel;
            border = _hover ? XShellTheme.Accent : XShellTheme.BorderLight;
            text = XShellTheme.Text;
        }
        else
        {
            fill = _down ? XShellTheme.Pressed
                : _hover ? XShellTheme.Hover : XShellTheme.Panel;
            border = _hover ? XShellTheme.Accent : XShellTheme.Border;
            text = XShellTheme.Text;
        }

        using (var brush = new LinearGradientBrush(rect,
            XShellTheme.Lighten(fill, AccentStyle ? 18 : 4),
            fill, LinearGradientMode.Vertical))
        {
            g.FillPath(brush, path);
        }
        using (var pen = new Pen(border))
        {
            g.DrawPath(pen, path);
        }
        if (_hover && Enabled)
        {
            using var glow = new Pen(Color.FromArgb(80, XShellTheme.Accent));
            g.DrawPath(glow, path);
        }

        var textRect = new Rectangle(2, 0, Math.Max(1, Width - 4), Height);
        TextRenderer.DrawText(g, Text, Font, textRect, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        if (Focused && ShowFocusCues)
        {
            var focus = Rectangle.Inflate(rect, -3, -3);
            ControlPaint.DrawFocusRectangle(g, focus, text, fill);
        }
    }
}

/// <summary>仅绘制图标的小按钮，图标颜色随主题变化。</summary>
internal sealed class XShellIconButton : Control
{
    private bool _hover;
    private bool _down;

    public string Glyph { get; set; } = "";
    public XShellTone Tone { get; set; } = XShellTone.Accent;

    public XShellIconButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = false;
        Size = new Size(28, 26);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        _down = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _down = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _down = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var rect = new Rectangle(1, 1, Math.Max(1, Width - 2), Math.Max(1, Height - 2));
        using var path = XShellTheme.RoundedRect(rect, 5);
        if (_hover || _down)
        {
            using var brush = new SolidBrush(_down
                ? XShellTheme.Pressed : XShellTheme.Hover);
            g.FillPath(brush, path);
            using var pen = new Pen(Color.FromArgb(70, XShellTheme.Accent));
            g.DrawPath(pen, path);
        }
        using var font = new Font("Segoe MDL2 Assets",
            Math.Max(10f, Height * 0.52f), FontStyle.Regular, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, Glyph, font, ClientRectangle,
            XShellTheme.ToneColor(Tone),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }
}

/// <summary>功能区大按钮：图标在上、文字在下，悬停时出现柔和渐变。</summary>
internal sealed class XShellRibbonButton : Control
{
    private bool _hover;
    private bool _down;

    public string Caption { get; set; } = "";
    public string Glyph { get; set; } = "";
    public XShellTone Tone { get; set; } = XShellTone.Accent;

    public XShellRibbonButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = false;
        Size = new Size(64, 56);
        Font = XShellTheme.Ui(8.2f);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        _down = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _down = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _down = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var rect = new Rectangle(1, 0, Math.Max(1, Width - 2), Math.Max(1, Height - 2));
        using var path = XShellTheme.RoundedRect(rect, 8);
        if (_hover || _down)
        {
            using var brush = new LinearGradientBrush(rect,
                Color.FromArgb(_down ? 90 : 58, XShellTheme.Accent),
                Color.FromArgb(_down ? 40 : 12, XShellTheme.Accent),
                LinearGradientMode.Vertical);
            g.FillPath(brush, path);
            using var pen = new Pen(Color.FromArgb(_down ? 120 : 80, XShellTheme.Accent));
            g.DrawPath(pen, path);
        }

        using (var iconFont = new Font("Segoe MDL2 Assets", 23f, FontStyle.Regular,
            GraphicsUnit.Pixel))
        {
            var iconRect = new Rectangle(0, 5, Width, 28);
            TextRenderer.DrawText(g, Glyph, iconFont, iconRect,
                XShellTheme.ToneColor(Tone),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
        var textRect = new Rectangle(1, Height - 22, Width - 2, 18);
        TextRenderer.DrawText(g, Caption, Font, textRect, XShellTheme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }
}

/// <summary>功能区分组卡片。</summary>
internal sealed class XShellRibbonGroup : Panel
{
    private readonly string _caption;

    public XShellRibbonGroup(string caption, params Control[] buttons)
    {
        _caption = caption;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 76;
        Width = buttons.Length * 60 + 12;
        Margin = new Padding(0, 0, 6, 0);
        int x = 5;
        foreach (var button in buttons)
        {
            button.Left = x;
            button.Top = 2;
            Controls.Add(button);
            x += button.Width + 2;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 4));
        using var path = XShellTheme.RoundedRect(rect, 9);
        using (var brush = new SolidBrush(Color.FromArgb(
            XShellTheme.IsDark ? 90 : 150, XShellTheme.RibbonGroup)))
        {
            g.FillPath(brush, path);
        }
        using (var pen = new Pen(Color.FromArgb(
            XShellTheme.IsDark ? 65 : 120, XShellTheme.BorderLight)))
        {
            g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, _caption, XShellTheme.Ui(7.8f),
            new Rectangle(0, Height - 18, Width - 3, 14), XShellTheme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPadding);
    }
}

/// <summary>主窗口功能区的渐变背景与柔和分隔线。</summary>
internal sealed class XShellRibbonPanel : Panel
{
    public XShellRibbonPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = XShellTheme.Ribbon;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var rect = ClientRectangle;
        using var brush = new LinearGradientBrush(rect,
            XShellTheme.Ribbon, XShellTheme.Panel, LinearGradientMode.Vertical);
        e.Graphics.FillRectangle(brush, rect);
        using var accent = new Pen(Color.FromArgb(45, XShellTheme.Accent));
        e.Graphics.DrawLine(accent, 0, 0, Width, 0);
        using var border = new Pen(XShellTheme.Border);
        e.Graphics.DrawLine(border, 0, Height - 1, Width, Height - 1);
        using var shadow = new Pen(Color.FromArgb(28, XShellTheme.Shadow));
        e.Graphics.DrawLine(shadow, 0, Height, Width, Height);
    }
}

/// <summary>快速会话栏（快速会话按钮）的圆角卡片背景。</summary>
internal sealed class XShellQuickPanel : Panel
{
    public XShellQuickPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = XShellTheme.Background;
        Height = 46;
        Padding = new Padding(12, 7, 12, 7);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(XShellTheme.Background);
        var rect = new Rectangle(8, 5, Math.Max(1, Width - 17), Math.Max(1, Height - 11));
        using var path = XShellTheme.RoundedRect(rect, 9);
        using (var brush = new LinearGradientBrush(rect,
            XShellTheme.Panel, XShellTheme.PanelAlt, LinearGradientMode.Horizontal))
        {
            e.Graphics.FillPath(brush, path);
        }
        using (var pen = new Pen(Color.FromArgb(130, XShellTheme.Border)))
        {
            e.Graphics.DrawPath(pen, path);
        }
        using var accent = new Pen(Color.FromArgb(80, XShellTheme.Accent));
        e.Graphics.DrawLine(accent, 0, Height - 1, Width, Height - 1);
    }
}

/// <summary>
/// 会话管理器向左自动隐藏后，停靠在窗口左边缘的竖排展开标签。
/// </summary>
internal sealed class XShellHideStrip : Panel
{
    public XShellHideStrip()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = XShellTheme.PanelAlt;
        Cursor = Cursors.Hand;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(XShellTheme.PanelAlt);
        using var border = new Pen(XShellTheme.Border);
        e.Graphics.DrawLine(border, Width - 1, 0, Width - 1, Height);
        e.Graphics.DrawLine(border, 0, 0, Width, 0);
        using var accent = new SolidBrush(XShellTheme.Accent);
        e.Graphics.FillRectangle(accent, 0, Height / 2 - 22, 2, 44);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // 竖排“会话管理器”标签：文字旋转 90°，自下而上阅读。
        using var font = XShellTheme.Ui(9f, FontStyle.Bold);
        g.TranslateTransform(Width / 2f - 2, Height / 2f);
        g.RotateTransform(-90);
        string text = "会话管理器 ›";
        var size = TextRenderer.MeasureText(text, font);
        TextRenderer.DrawText(g, text, font,
            new Rectangle(-size.Width / 2, -size.Height / 2, size.Width, size.Height),
            XShellTheme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        g.ResetTransform();
    }
}

/// <summary>带柔和阴影与渐变头部的对话框标题栏。</summary>
internal sealed class XShellHeaderPanel : Panel
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";

    public XShellHeaderPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 58;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var rect = ClientRectangle;
        using (var brush = new LinearGradientBrush(rect,
            XShellTheme.HeaderFrom, XShellTheme.HeaderTo, 18f))
        {
            e.Graphics.FillRectangle(brush, rect);
        }
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var glow = new SolidBrush(Color.FromArgb(24, XShellTheme.Accent)))
        {
            e.Graphics.FillEllipse(glow, Width - 150, -70, 210, 210);
        }
        using (var line = new Pen(Color.FromArgb(70, 255, 255, 255)))
        {
            for (int x = -40; x < Width; x += 56)
            {
                e.Graphics.DrawLine(line, x, Height, x + Height, 0);
            }
        }
        using (var accent = new Pen(XShellTheme.Accent, 2f))
        {
            e.Graphics.DrawLine(accent, 0, Height - 1, Width, Height - 1);
        }
        TextRenderer.DrawText(e.Graphics, Title, XShellTheme.Ui(13f, FontStyle.Bold),
            new Rectangle(18, 9, Width - 200, 24), Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (!string.IsNullOrEmpty(Subtitle))
        {
            TextRenderer.DrawText(e.Graphics, Subtitle, XShellTheme.Ui(8.6f),
                new Rectangle(20, 32, Width - 220, 18), XShellTheme.HeroMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>启动页三张操作卡片，带悬停动画、阴影与渐变。</summary>
internal sealed class XShellActionCard : Control
{
    private readonly string _title;
    private readonly string _subtitle;
    private readonly string _glyph;
    private readonly XShellTone _tone;
    private readonly System.Windows.Forms.Timer _animation;
    private float _hover;
    private bool _targetHover;

    public XShellActionCard(string title, string subtitle, string glyph, XShellTone tone)
    {
        _title = title;
        _subtitle = subtitle;
        _glyph = glyph;
        _tone = tone;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = XShellTheme.Background;
        Cursor = Cursors.Hand;
        Height = 96;
        _animation = new System.Windows.Forms.Timer { Interval = 15 };
        _animation.Tick += (_, _) =>
        {
            float target = _targetHover ? 1f : 0f;
            _hover += (target - _hover) * 0.22f;
            if (Math.Abs(target - _hover) < 0.02f)
            {
                _hover = target;
                _animation.Stop();
            }
            Invalidate();
        };
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _targetHover = true;
        _animation.Start();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _targetHover = false;
        _animation.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var card = new Rectangle(5, 5, Math.Max(1, Width - 12), Math.Max(1, Height - 12));
        using var path = XShellTheme.RoundedRect(card, 10);
        if (_hover > 0.01f)
        {
            for (int i = 3; i >= 1; i--)
            {
                var shadowRect = new Rectangle(card.X - i, card.Y + i,
                    card.Width + i * 2, card.Height + i);
                using var shadowPath = XShellTheme.RoundedRect(shadowRect, 12);
                using var shadow = new SolidBrush(Color.FromArgb(
                    (int)(10 * _hover), XShellTheme.Shadow));
                g.FillPath(shadow, shadowPath);
            }
        }
        var fill = XShellTheme.Blend(XShellTheme.Card, XShellTheme.CardHover, _hover);
        using (var brush = new LinearGradientBrush(card, fill,
            XShellTheme.Blend(fill, XShellTheme.AccentLight, _hover * 0.35f),
            LinearGradientMode.Vertical))
        {
            g.FillPath(brush, path);
        }
        using (var pen = new Pen(XShellTheme.Blend(XShellTheme.Border,
            XShellTheme.Accent, _hover * 0.75f), 1f + _hover * 0.6f))
        {
            g.DrawPath(pen, path);
        }

        var badge = new Rectangle(card.X + 16, card.Y + 20, 48, 48);
        using (var badgePath = XShellTheme.RoundedRect(badge, 11))
        using (var badgeBrush = new LinearGradientBrush(badge,
            XShellTheme.WithAlpha(XShellTheme.ToneColor(_tone), 46),
            XShellTheme.WithAlpha(XShellTheme.ToneColor(_tone), 16),
            LinearGradientMode.Vertical))
        {
            g.FillPath(badgeBrush, badgePath);
            using var badgePen = new Pen(XShellTheme.WithAlpha(
                XShellTheme.ToneColor(_tone), 90 + (int)(80 * _hover)));
            g.DrawPath(badgePen, badgePath);
        }
        using (var iconFont = new Font("Segoe MDL2 Assets", 23f, FontStyle.Regular,
            GraphicsUnit.Pixel))
        {
            TextRenderer.DrawText(g, _glyph, iconFont, badge,
                XShellTheme.ToneColor(_tone),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPadding);
        }
        TextRenderer.DrawText(g, _title, XShellTheme.Ui(10.5f, FontStyle.Bold),
            new Rectangle(card.X + 78, card.Y + 19, card.Width - 92, 24),
            XShellTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, _subtitle, XShellTheme.Ui(8.3f),
            new Rectangle(card.X + 78, card.Y + 45, card.Width - 92, 34),
            XShellTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.Top
            | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animation.Stop();
            _animation.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>最近会话行：圆角悬停、协议标签、图标与时间。</summary>
internal sealed class XShellRecentRow : Control
{
    private readonly XSessionProfile _profile;
    private bool _hover;

    public XSessionProfile Profile => _profile;

    public XShellRecentRow(XSessionProfile profile)
    {
        _profile = profile;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = XShellTheme.Panel;
        Cursor = Cursors.Hand;
        Height = 46;
        Margin = new Padding(0, 0, 0, 4);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var rect = new Rectangle(0, 0, Math.Max(1, Width - 2), Math.Max(1, Height - 2));
        using var path = XShellTheme.RoundedRect(rect, 8);
        using (var brush = new SolidBrush(_hover ? XShellTheme.CardHover : XShellTheme.Card))
        {
            g.FillPath(brush, path);
        }
        using (var pen = new Pen(_hover ? XShellTheme.Accent : XShellTheme.BorderLight))
        {
            g.DrawPath(pen, path);
        }

        var badge = new Rectangle(12, 9, 28, 28);
        using (var badgePath = XShellTheme.RoundedRect(badge, 7))
        using (var badgeBrush = new SolidBrush(XShellTheme.AccentLight))
        {
            g.FillPath(badgeBrush, badgePath);
        }
        using (var iconFont = new Font("Segoe MDL2 Assets", 15f, FontStyle.Regular,
            GraphicsUnit.Pixel))
        {
            TextRenderer.DrawText(g, _profile.Protocol switch
            {
                XProtocol.Telnet => "\uE756",
                XProtocol.Serial => "\uE9D9",
                XProtocol.Local => "\uE756",
                _ => "\uE8B7"
            }, iconFont, badge, XShellTheme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPadding);
        }
        TextRenderer.DrawText(g, _profile.Name, XShellTheme.Ui(9.2f, FontStyle.Bold),
            new Rectangle(50, 6, Math.Max(60, Width - 260), 20), XShellTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, _profile.ProtocolText + "  ·  " + _profile.DisplayAddress,
            XShellTheme.Ui(8.2f),
            new Rectangle(50, 25, Math.Max(60, Width - 260), 16), XShellTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        string time = _profile.LastUsedAt == default
            ? "从未连接" : _profile.LastUsedAt.ToString("MM-dd HH:mm");
        TextRenderer.DrawText(g, time, XShellTheme.Ui(8.2f),
            new Rectangle(Width - 150, 0, 138, Height), XShellTheme.Muted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        if (_hover)
        {
            using var arrowFont = new Font("Segoe MDL2 Assets", 12f, FontStyle.Regular,
                GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, "\uE76C", arrowFont,
                new Rectangle(Width - 34, 0, 20, Height), XShellTheme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>启动页品牌徽标。</summary>
internal sealed class XShellLogo : Control
{
    public XShellLogo()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(66, 66);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(4, 4, Width - 10, Height - 10);
        using (var glow = new SolidBrush(Color.FromArgb(55, XShellTheme.Accent)))
        {
            g.FillEllipse(glow, 0, 8, Width, Height);
        }
        using var path = XShellTheme.RoundedRect(rect, 15);
        using (var brush = new LinearGradientBrush(rect,
            XShellTheme.Lighten(XShellTheme.Accent, 25), XShellTheme.AccentDark, 55f))
        {
            g.FillPath(brush, path);
        }
        using (var pen = new Pen(Color.FromArgb(120, 255, 255, 255)))
        {
            g.DrawPath(pen, path);
        }
        using var font = new Font("Segoe UI", Width * 0.48f, FontStyle.Bold);
        TextRenderer.DrawText(g, "X", font, rect, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPadding);
    }
}

/// <summary>专业菜单渲染器，颜色实时读取当前主题。</summary>
internal sealed class XShellMenuRenderer : ToolStripProfessionalRenderer
{
    public XShellMenuRenderer() : base(new XShellMenuColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (!e.Item.Enabled)
        {
            e.TextColor = XShellTheme.Muted;
        }
        else if (e.ToolStrip is StatusStrip)
        {
            e.TextColor = e.Item.Selected ? Color.White : XShellTheme.Current.HeroMuted;
        }
        else
        {
            e.TextColor = XShellTheme.Current.MenuText;
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
        using var brush = new SolidBrush(XShellTheme.Current.MenuBack);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = e.ImageRectangle;
        using var pen = new Pen(XShellTheme.Current.Accent, 2f)
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
        e.ArrowColor = XShellTheme.Current.MenuText;
        base.OnRenderArrow(e);
    }
}

internal sealed class XShellStatusRenderer : ToolStripProfessionalRenderer
{
    public XShellStatusRenderer() : base(new XShellMenuColorTable())
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
            XShellTheme.Current.StatusFrom, XShellTheme.Current.StatusTo,
            LinearGradientMode.Horizontal);
        e.Graphics.FillRectangle(brush, rect);
        using var line = new Pen(Color.FromArgb(80, XShellTheme.Current.Accent));
        e.Graphics.DrawLine(line, 0, 0, rect.Width, 0);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (e.Item.Name == "state")
        {
            e.TextColor = e.Item.ForeColor;
        }
        else
        {
            e.TextColor = e.Item.Enabled
                ? (e.Item.Selected ? Color.White : XShellTheme.Current.HeroMuted)
                : Color.FromArgb(150, XShellTheme.Current.HeroMuted);
        }
        base.OnRenderItemText(e);
    }
}

internal sealed class XShellMenuColorTable : ProfessionalColorTable
{
    public override Color MenuStripGradientBegin => XShellTheme.Current.MenuBack;
    public override Color MenuStripGradientEnd => XShellTheme.Current.MenuBack;
    public override Color ToolStripDropDownBackground => XShellTheme.Current.MenuBack;
    public override Color MenuItemSelected => XShellTheme.Current.AccentLight;
    public override Color MenuItemSelectedGradientBegin => XShellTheme.Current.AccentLight;
    public override Color MenuItemSelectedGradientEnd => XShellTheme.Current.AccentLight;
    public override Color MenuItemBorder => XShellTheme.Current.Accent;
    public override Color MenuBorder => XShellTheme.Current.Border;
    public override Color ImageMarginGradientBegin => XShellTheme.Current.MenuBack;
    public override Color ImageMarginGradientMiddle => XShellTheme.Current.MenuBack;
    public override Color ImageMarginGradientEnd => XShellTheme.Current.MenuBack;
    public override Color SeparatorDark => XShellTheme.Current.Border;
    public override Color SeparatorLight => XShellTheme.Current.BorderLight;
    public override Color CheckBackground => XShellTheme.Current.AccentLight;
    public override Color CheckSelectedBackground => XShellTheme.Current.Accent;
    public override Color ButtonSelectedHighlightBorder => XShellTheme.Current.Accent;
    public override Color ButtonSelectedGradientBegin => XShellTheme.Current.AccentLight;
    public override Color ButtonSelectedGradientEnd => XShellTheme.Current.AccentLight;
    public override Color ButtonPressedGradientBegin => XShellTheme.Current.Pressed;
    public override Color ButtonPressedGradientEnd => XShellTheme.Current.Pressed;
    public override Color ToolStripGradientBegin => XShellTheme.Current.PanelAlt;
    public override Color ToolStripGradientMiddle => XShellTheme.Current.PanelAlt;
    public override Color ToolStripGradientEnd => XShellTheme.Current.PanelAlt;
}

/// <summary>让窗口标题栏跟随 XShell 主题（Windows 11 支持自定义标题栏颜色）。</summary>
internal static class XShellWindowTheme
{
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

    public static void ApplyControlTheme(Control control)
    {
        if (!control.IsHandleCreated)
        {
            return;
        }
        try
        {
            SetWindowTheme(control.Handle,
                XShellTheme.IsDark ? "DarkMode_Explorer" : "Explorer", null);
        }
        catch
        {
            // 旧版 Windows 不支持 DarkMode_Explorer 时保持系统默认。
        }
    }

    public static void Apply(Form form)
    {
        if (!form.IsHandleCreated)
        {
            return;
        }
        try
        {
            int dark = XShellTheme.IsDark ? 1 : 0;
            int result = DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkMode,
                ref dark, sizeof(int));
            if (result != 0)
            {
                DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkModeLegacy,
                    ref dark, sizeof(int));
            }
            int caption = ColorTranslator.ToWin32(XShellTheme.Current.Caption);
            DwmSetWindowAttribute(form.Handle, DwmCaptionColor, ref caption, sizeof(int));
            int text = ColorTranslator.ToWin32(XShellTheme.Current.CaptionText);
            DwmSetWindowAttribute(form.Handle, DwmTextColor, ref text, sizeof(int));
        }
        catch
        {
            // Windows 10 或系统不支持时保持默认标题栏。
        }
    }
}

internal static class XShellIcons
{
    public static Image Glyph(string glyph, Color color, int size = 20)
    {
        var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using var font = new Font("Segoe MDL2 Assets", size * 0.66f,
                FontStyle.Regular, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, glyph, font, new Rectangle(0, 0, size, size), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
        return bmp;
    }

    public static Image Server(Color color, int size = 20) => Glyph("\uE8B7", color, size);
    public static Image Folder(Color color, int size = 20) => Glyph("\uE8B7", color, size);
    public static Image Terminal(Color color, int size = 20) => Glyph("\uE756", color, size);
    public static Image Plus(Color color, int size = 20) => Glyph("\uE710", color, size);
    public static Image Play(Color color, int size = 20) => Glyph("\uE768", color, size);
    public static Image Stop(Color color, int size = 20) => Glyph("\uE71A", color, size);
    public static Image Gear(Color color, int size = 20) => Glyph("\uE713", color, size);
    public static Image Link(Color color, int size = 20) => Glyph("\uE71B", color, size);
    public static Image Upload(Color color, int size = 20) => Glyph("\uE898", color, size);
    public static Image Download(Color color, int size = 20) => Glyph("\uE896", color, size);
    public static Image Refresh(Color color, int size = 20) => Glyph("\uE72C", color, size);
    public static Image Delete(Color color, int size = 20) => Glyph("\uE74D", color, size);
    public static Image Edit(Color color, int size = 20) => Glyph("\uE70F", color, size);
    public static Image Search(Color color, int size = 20) => Glyph("\uE721", color, size);
    public static Image Up(Color color, int size = 20) => Glyph("\uE74A", color, size);
}
