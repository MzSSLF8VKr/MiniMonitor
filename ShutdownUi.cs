using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>自定义关机界面统一配色与绘制工具（纯 GDI+ 绘制，不依赖图片资源）。</summary>
internal static class ShutdownTheme
{
    public static readonly Color Background = Color.FromArgb(244, 247, 252);
    public static readonly Color Card = Color.White;
    public static readonly Color CardBorder = Color.FromArgb(226, 232, 240);
    public static readonly Color Text = Color.FromArgb(30, 41, 59);
    public static readonly Color Muted = Color.FromArgb(100, 116, 139);
    public static readonly Color Accent = Color.FromArgb(37, 99, 235);
    public static readonly Color AccentHover = Color.FromArgb(59, 130, 246);
    public static readonly Color AccentDark = Color.FromArgb(29, 78, 216);
    public static readonly Color AccentSoft = Color.FromArgb(239, 246, 255);
    public static readonly Color Success = Color.FromArgb(22, 163, 74);
    public static readonly Color Danger = Color.FromArgb(220, 38, 38);
    public static readonly Color DangerHover = Color.FromArgb(239, 68, 68);
    public static readonly Color DangerDark = Color.FromArgb(185, 28, 28);
    public static readonly Color WarningBg = Color.FromArgb(255, 247, 237);
    public static readonly Color WarningBorder = Color.FromArgb(253, 230, 200);
    public static readonly Color WarningText = Color.FromArgb(146, 96, 0);
    public static readonly Color DarkBg = Color.FromArgb(11, 18, 32);
    public static readonly Color DarkBorder = Color.FromArgb(30, 41, 59);
    public static readonly Color DarkMuted = Color.FromArgb(148, 163, 184);
    public static readonly Color DarkTrack = Color.FromArgb(31, 41, 55);
    public static readonly Color DarkAccent = Color.FromArgb(59, 130, 246);
    public static readonly Color DarkAccentLight = Color.FromArgb(96, 165, 250);

    /// <summary>生成圆角矩形路径（自动限制半径不超过短边的一半）。</summary>
    public static GraphicsPath RoundRect(RectangleF rect, float radius)
    {
        float r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2f);
        var path = new GraphicsPath();
        if (r <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }
        path.AddArc(rect.X, rect.Y, r * 2, r * 2, 180, 90);
        path.AddArc(rect.Right - r * 2, rect.Y, r * 2, r * 2, 270, 90);
        path.AddArc(rect.Right - r * 2, rect.Bottom - r * 2, r * 2, r * 2, 0, 90);
        path.AddArc(rect.X, rect.Bottom - r * 2, r * 2, r * 2, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics g, RectangleF rect, float radius, Color color)
    {
        using var path = RoundRect(rect, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void DrawRoundedBorder(Graphics g, RectangleF rect, float radius, Color color, float width = 1f)
    {
        using var path = RoundRect(rect, radius);
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }
}

/// <summary>白色圆角卡片：浅色阴影 + 1px 描边。</summary>
internal sealed class CardPanel : Panel
{
    public int CornerRadius { get; set; } = 16;
    public Color FillColor { get; set; } = ShutdownTheme.Card;
    public Color BorderColor { get; set; } = ShutdownTheme.CardBorder;
    public bool ShowShadow { get; set; } = true;

    public CardPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = ShutdownTheme.Background;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var cardRect = new RectangleF(1f, 1f, Width - 2f, Height - 2f);
        if (ShowShadow)
        {
            var shadowRect = new RectangleF(2f, 3f, Width - 4f, Height - 4f);
            using var shadowPath = ShutdownTheme.RoundRect(shadowRect, CornerRadius);
            using var shadow = new SolidBrush(Color.FromArgb(16, 15, 23, 42));
            e.Graphics.FillPath(shadow, shadowPath);
        }
        ShutdownTheme.FillRounded(e.Graphics, cardRect, CornerRadius, FillColor);
        ShutdownTheme.DrawRoundedBorder(e.Graphics, cardRect, CornerRadius, BorderColor);
    }
}

internal enum PillButtonStyle
{
    Light,
    Primary,
    Danger
}

/// <summary>扁平圆角按钮，带悬停/按下/禁用状态。</summary>
internal sealed class PillButton : Control
{
    private bool _hover;
    private bool _pressed;

    public PillButtonStyle ButtonStyle { get; set; } = PillButtonStyle.Light;
    public int CornerRadius { get; set; } = 9;

    public PillButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        ForeColor = ShutdownTheme.Text;
        BackColor = Color.White;
        TabStop = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        Color fill;
        Color border;
        Color text;
        if (!Enabled)
        {
            fill = Color.FromArgb(241, 245, 249);
            border = Color.FromArgb(226, 232, 240);
            text = Color.FromArgb(148, 163, 184);
        }
        else if (ButtonStyle == PillButtonStyle.Primary)
        {
            fill = _pressed ? ShutdownTheme.AccentDark : _hover ? ShutdownTheme.AccentHover : ShutdownTheme.Accent;
            border = fill;
            text = Color.White;
        }
        else if (ButtonStyle == PillButtonStyle.Danger)
        {
            fill = _pressed ? ShutdownTheme.DangerDark : _hover ? ShutdownTheme.DangerHover : ShutdownTheme.Danger;
            border = fill;
            text = Color.White;
        }
        else
        {
            fill = _hover ? ShutdownTheme.AccentSoft : Color.FromArgb(248, 250, 252);
            border = _hover ? ShutdownTheme.Accent : ShutdownTheme.CardBorder;
            text = _hover ? ShutdownTheme.Accent : ShutdownTheme.Text;
        }

        ShutdownTheme.FillRounded(e.Graphics, rect, CornerRadius, fill);
        ShutdownTheme.DrawRoundedBorder(e.Graphics, rect, CornerRadius, border);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        Invalidate();
        base.OnTextChanged(e);
    }

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}

/// <summary>扁平滑块开关（开启为蓝色，关闭为灰色）。</summary>
internal sealed class ToggleSwitch : Control
{
    private bool _checked;

    public Color OnColor { get; set; } = ShutdownTheme.Accent;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CheckedChanged;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(56, 30);
        Cursor = Cursors.Hand;
        BackColor = Color.White;
        TabStop = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float radius = Height / 2f;
        var track = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        Color trackColor = !Enabled
            ? Color.FromArgb(226, 232, 240)
            : _checked ? OnColor : Color.FromArgb(203, 213, 225);
        ShutdownTheme.FillRounded(e.Graphics, track, radius, trackColor);

        float knobSize = Height - 6f;
        float knobX = _checked ? Width - knobSize - 3f : 3f;
        var knob = new RectangleF(knobX, 3f, knobSize, knobSize);
        using var brush = new SolidBrush(Color.White);
        e.Graphics.FillEllipse(brush, knob);
        using var pen = new Pen(Color.FromArgb(28, 15, 23, 42));
        e.Graphics.DrawEllipse(pen, knob);
    }

    protected override void OnClick(EventArgs e)
    {
        if (Enabled)
        {
            Checked = !Checked;
            Focus();
        }
        base.OnClick(e);
    }

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled && e.KeyCode is Keys.Space or Keys.Enter)
        {
            Checked = !Checked;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }
}

/// <summary>无边框窗体的拖动辅助。</summary>
internal static class NativeWindowDrag
{
    private const int WmNcLButtonDown = 0xA1;
    private const int HtCaption = 0x2;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public static void Attach(Form form, Control surface, Func<Point, bool>? ignoreArea = null)
    {
        surface.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }
            if (ignoreArea?.Invoke(e.Location) == true)
            {
                return;
            }
            ReleaseCapture();
            SendMessage(form.Handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
        };
    }
}
