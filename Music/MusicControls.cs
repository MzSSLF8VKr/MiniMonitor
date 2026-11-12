using System.Drawing.Drawing2D;
using MiniMonitor.Rss;

namespace MiniMonitor.Music;

/// <summary>正在播放封面（圆角、阴影、占位图标）。</summary>
public sealed class MusicCoverBox : Control
{
    private Bitmap? _cover;
    private string _coverKey = "";
    private RssTheme _theme = RssTheme.Light;

    public MusicCoverBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    public RssTheme Theme
    {
        get => _theme;
        set { _theme = value; Invalidate(); }
    }

    public string CoverKey
    {
        get => _coverKey;
        set
        {
            if (_coverKey == value) return;
            _coverKey = value ?? "";
            _cover?.Dispose();
            _cover = MusicCoverCache.Load(_coverKey);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int side = Math.Min(Width, Height);
        var rect = new Rectangle((Width - side) / 2, (Height - side) / 2, side, side);
        using var path = ShutdownTheme.RoundRect(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), 12f);
        if (_cover != null)
        {
            var state = g.Save();
            g.SetClip(path);
            g.DrawImage(_cover, rect);
            g.Restore(state);
        }
        else
        {
            using var brush = new LinearGradientBrush(rect,
                _theme.IsDark ? Color.FromArgb(54, 64, 78) : Color.FromArgb(226, 238, 250),
                _theme.IsDark ? Color.FromArgb(34, 40, 50) : Color.FromArgb(198, 216, 236), 45f);
            g.FillPath(brush, path);
            using var iconFont = _theme.IconFont(side / 4f);
            TextRenderer.DrawText(g, "\uE8D6", iconFont, rect,
                Color.FromArgb(_theme.IsDark ? 120 : 150, _theme.Accent),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        using var border = new Pen(Color.FromArgb(_theme.IsDark ? 40 : 26, _theme.Text));
        g.DrawPath(border, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cover?.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>可拖动的播放进度条。</summary>
public sealed class MusicSeekBar : Control
{
    private RssTheme _theme = RssTheme.Light;
    private double _value;
    private bool _dragging;
    private bool _hover;

    public MusicSeekBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 22;
        Cursor = Cursors.Hand;
    }

    public event EventHandler<double>? SeekRequested;

    public RssTheme Theme
    {
        get => _theme;
        set { _theme = value; Invalidate(); }
    }

    /// <summary>0~1</summary>
    public double Value
    {
        get => _value;
        set
        {
            if (_dragging) return;
            _value = Math.Clamp(value, 0, 1);
            Invalidate();
        }
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
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            UpdateFromMouse(e.X);
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            UpdateFromMouse(e.X);
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragging && e.Button == MouseButtons.Left)
        {
            _dragging = false;
            UpdateFromMouse(e.X);
            SeekRequested?.Invoke(this, _value);
        }
        base.OnMouseUp(e);
    }

    private void UpdateFromMouse(int x)
    {
        int padding = 8;
        int width = Math.Max(1, Width - padding * 2);
        _value = Math.Clamp((x - padding) / (double)width, 0, 1);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int padding = 8;
        int y = Height / 2;
        int width = Width - padding * 2;
        using (var back = new Pen(_theme.Border, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(back, padding, y, padding + width, y);
        }
        int progressWidth = (int)(width * _value);
        if (progressWidth > 0)
        {
            using var accent = new Pen(_theme.Accent, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(accent, padding, y, padding + progressWidth, y);
        }
        int radius = _hover || _dragging ? 7 : 5;
        var thumb = new Rectangle(padding + progressWidth - radius, y - radius, radius * 2, radius * 2);
        using var thumbBrush = new SolidBrush(_theme.Accent);
        g.FillEllipse(thumbBrush, thumb);
    }
}

/// <summary>水平数值滑块（音量 / 均衡器）。</summary>
public sealed class MusicSlider : Control
{
    private RssTheme _theme = RssTheme.Light;
    private double _minimum;
    private double _maximum = 100;
    private double _value;
    private bool _dragging;
    private bool _hover;

    public MusicSlider()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 22;
        Cursor = Cursors.Hand;
    }

    public event EventHandler? ValueChanged;

    public RssTheme Theme
    {
        get => _theme;
        set { _theme = value; Invalidate(); }
    }

    public double Minimum
    {
        get => _minimum;
        set { _minimum = value; Invalidate(); }
    }

    public double Maximum
    {
        get => _maximum;
        set { _maximum = Math.Max(_minimum + 0.0001, value); Invalidate(); }
    }

    public double Value
    {
        get => _value;
        set
        {
            double clamped = Math.Clamp(value, _minimum, _maximum);
            if (Math.Abs(clamped - _value) < 0.0001) return;
            _value = clamped;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool ShowValue { get; set; } = true;

    public string ValueFormat { get; set; } = "0";

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            UpdateFromMouse(e.X);
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging)
        {
            UpdateFromMouse(e.X);
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        base.OnMouseUp(e);
    }

    private void UpdateFromMouse(int x)
    {
        int padding = 8;
        int width = Math.Max(1, Width - padding * 2);
        double ratio = Math.Clamp((x - padding) / (double)width, 0, 1);
        _value = _minimum + (_maximum - _minimum) * ratio;
        Invalidate();
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int padding = 8;
        int y = Height / 2;
        int width = Width - padding * 2;
        double ratio = (_value - _minimum) / Math.Max(0.0001, _maximum - _minimum);
        using (var back = new Pen(_theme.Border, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(back, padding, y, padding + width, y);
        }
        int progress = (int)(width * ratio);
        if (progress > 0)
        {
            using var accent = new Pen(_theme.Accent, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(accent, padding, y, padding + progress, y);
        }
        int radius = _hover || _dragging ? 7 : 5;
        using var brush = new SolidBrush(_theme.Accent);
        g.FillEllipse(brush, padding + progress - radius, y - radius, radius * 2, radius * 2);
        if (ShowValue)
        {
            using var font = _theme.Font(8.2f);
            TextRenderer.DrawText(g, _value.ToString(ValueFormat), font,
                new Rectangle(Width - 46, 0, 42, Height), _theme.TextTertiary,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}

/// <summary>同步歌词显示，当前行高亮，点击行可跳转。</summary>
public sealed class MusicLyricsView : Control
{
    private RssTheme _theme = RssTheme.Light;
    private LrcDocument? _document;
    private int _currentIndex = -1;
    private string _placeholder = "暂无歌词";
    private string _hint = "将同名 .lrc 文件放在歌曲旁即可自动加载";
    private readonly List<(Rectangle Bounds, TimeSpan Time)> _lineHits = new();

    public MusicLyricsView()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    public event EventHandler<TimeSpan>? LineClicked;

    public RssTheme Theme
    {
        get => _theme;
        set { _theme = value; Invalidate(); }
    }

    public LrcDocument? Document
    {
        get => _document;
        set
        {
            _document = value;
            _currentIndex = -1;
            Invalidate();
        }
    }

    public string Placeholder
    {
        get => _placeholder;
        set { _placeholder = value; Invalidate(); }
    }

    public int CurrentIndex
    {
        get => _currentIndex;
        set
        {
            if (_currentIndex == value) return;
            _currentIndex = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        _lineHits.Clear();
        if (_document == null || _document.Lines.Count == 0)
        {
            using var iconFont = _theme.IconFont(22f);
            TextRenderer.DrawText(g, "\uE8A5", iconFont,
                new Rectangle(0, Height / 2 - 46, Width, 40), _theme.TextTertiary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, _placeholder, _theme.Font(9.6f),
                new Rectangle(10, Height / 2, Width - 20, 24), _theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, _hint, _theme.Font(8.2f),
                new Rectangle(10, Height / 2 + 24, Width - 20, 22), _theme.TextTertiary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        if (!_document.HasTimestamps)
        {
            string text = string.Join("\n", _document.RawText
                .Replace("\r\n", "\n").Split('\n')
                .Where(l => l.Trim().Length > 0)
                .Take(60));
            TextRenderer.DrawText(g, text, _theme.Font(9.6f),
                new Rectangle(14, 12, Width - 28, Height - 24), _theme.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
            return;
        }

        int index = _currentIndex;
        if (index < 0) index = 0;
        int lineHeight = 42;
        int center = Height / 2;
        int first = Math.Max(0, index - 2);
        int last = Math.Min(_document.Lines.Count - 1, index + 2);
        for (int i = first; i <= last; i++)
        {
            var line = _document.Lines[i];
            int offset = (i - index) * lineHeight;
            var rect = new Rectangle(10, center + offset - lineHeight / 2, Width - 20, lineHeight);
            bool current = i == index;
            Color color = current ? _theme.Accent : _theme.TextSecondary;
            var font = current ? _theme.Font(11f, FontStyle.Bold) : _theme.Font(9.4f);
            string text = line.Text;
            if (line.Translation.Length > 0)
            {
                if (current)
                {
                    TextRenderer.DrawText(g, text, font,
                        new Rectangle(rect.X, rect.Y - 5, rect.Width, rect.Height / 2 + 4),
                        color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, line.Translation, _theme.Font(8.4f),
                        new Rectangle(rect.X, rect.Y + rect.Height / 2 - 2, rect.Width, rect.Height / 2),
                        _theme.TextSecondary,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                }
                else
                {
                    text += " / " + line.Translation;
                    TextRenderer.DrawText(g, text, font, rect, color,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                }
            }
            else
            {
                TextRenderer.DrawText(g, text, font, rect, color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
            _lineHits.Add((rect, line.Time));
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        foreach (var (bounds, time) in _lineHits)
        {
            if (bounds.Contains(e.Location))
            {
                LineClicked?.Invoke(this, time);
                break;
            }
        }
        base.OnMouseClick(e);
    }
}

/// <summary>底栏圆形图标按钮。</summary>
public sealed class MusicIconButton : Control
{
    private readonly Font _iconFont;
    private RssTheme _theme;
    private bool _hover;
    private bool _pressed;
    private bool _active;

    public MusicIconButton(RssTheme theme, string glyph, float glyphSize = 11f)
    {
        _theme = theme;
        _iconFont = theme.IconFont(glyphSize);
        Glyph = glyph;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(34, 34);
        Cursor = Cursors.Hand;
    }

    public RssTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            Invalidate();
        }
    }

    private string _glyph = "";
    public string Glyph
    {
        get => _glyph;
        set
        {
            _glyph = value ?? "";
            Invalidate();
        }
    }

    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            Invalidate();
        }
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

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (_hover || _pressed)
        {
            using var brush = new SolidBrush(_theme.Hover);
            g.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
        }
        Color color = !Enabled ? _theme.TextTertiary
            : _active ? _theme.Accent : _theme.TextSecondary;
        TextRenderer.DrawText(g, Glyph, _iconFont, ClientRectangle, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
