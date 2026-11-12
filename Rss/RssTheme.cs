using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace MiniMonitor.Rss;

/// <summary>Fluent Design 风格配色。浅色 / 深色 / 跟随系统，蓝色强调色与 Fluent Reader 一致。</summary>
public sealed class RssTheme
{
    public bool IsDark { get; init; }
    public Color Background { get; init; }
    public Color Surface { get; init; }
    public Color SurfaceAlt { get; init; }
    public Color Sidebar { get; init; }
    public Color Border { get; init; }
    public Color Text { get; init; }
    public Color TextSecondary { get; init; }
    public Color TextTertiary { get; init; }
    public Color Accent { get; init; }
    public Color AccentHover { get; init; }
    public Color AccentSoft { get; init; }
    public Color Selection { get; init; }
    public Color Hover { get; init; }
    public Color Star { get; init; }
    public Color Danger { get; init; }
    public Color UnreadDot { get; init; }
    public Color InputBackground { get; init; }
    public Color Shadow { get; init; }

    public static RssTheme Light { get; } = new()
    {
        IsDark = false,
        Background = Color.FromArgb(243, 243, 243),
        Surface = Color.FromArgb(255, 255, 255),
        SurfaceAlt = Color.FromArgb(250, 250, 250),
        Sidebar = Color.FromArgb(248, 248, 248),
        Border = Color.FromArgb(226, 226, 226),
        Text = Color.FromArgb(27, 27, 27),
        TextSecondary = Color.FromArgb(96, 96, 96),
        TextTertiary = Color.FromArgb(138, 138, 138),
        Accent = Color.FromArgb(0, 103, 192),
        AccentHover = Color.FromArgb(0, 90, 168),
        AccentSoft = Color.FromArgb(229, 241, 251),
        Selection = Color.FromArgb(232, 240, 250),
        Hover = Color.FromArgb(245, 245, 245),
        Star = Color.FromArgb(214, 158, 0),
        Danger = Color.FromArgb(196, 43, 28),
        UnreadDot = Color.FromArgb(0, 103, 192),
        InputBackground = Color.FromArgb(255, 255, 255),
        Shadow = Color.FromArgb(18, 0, 0, 0)
    };

    public static RssTheme Dark { get; } = new()
    {
        IsDark = true,
        Background = Color.FromArgb(32, 32, 32),
        Surface = Color.FromArgb(39, 39, 39),
        SurfaceAlt = Color.FromArgb(43, 43, 43),
        Sidebar = Color.FromArgb(28, 28, 28),
        Border = Color.FromArgb(58, 58, 58),
        Text = Color.FromArgb(243, 243, 243),
        TextSecondary = Color.FromArgb(200, 200, 200),
        TextTertiary = Color.FromArgb(154, 154, 154),
        Accent = Color.FromArgb(96, 205, 255),
        AccentHover = Color.FromArgb(130, 220, 255),
        AccentSoft = Color.FromArgb(30, 58, 79),
        Selection = Color.FromArgb(38, 54, 68),
        Hover = Color.FromArgb(47, 47, 47),
        Star = Color.FromArgb(255, 210, 74),
        Danger = Color.FromArgb(255, 120, 110),
        UnreadDot = Color.FromArgb(96, 205, 255),
        InputBackground = Color.FromArgb(43, 43, 43),
        Shadow = Color.FromArgb(40, 0, 0, 0)
    };

    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? value = key?.GetValue("AppsUseLightTheme");
            return value is int number && number == 0;
        }
        catch
        {
            return false;
        }
    }

    public static RssTheme Create(string mode)
    {
        return mode switch
        {
            "light" => Light,
            "dark" => Dark,
            _ => SystemUsesDarkTheme() ? Dark : Light
        };
    }

    private readonly Dictionary<(float Size, FontStyle Style), Font> _fonts = new();
    private readonly Dictionary<float, Font> _iconFonts = new();

    public Font Font(float size, FontStyle style = FontStyle.Regular)
    {
        var key = (size, style);
        if (!_fonts.TryGetValue(key, out var font))
        {
            font = new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
            _fonts[key] = font;
        }
        return font;
    }

    public Font IconFont(float size)
    {
        if (!_iconFonts.TryGetValue(size, out var font))
        {
            font = new Font("Segoe MDL2 Assets", size, FontStyle.Regular, GraphicsUnit.Point);
            _iconFonts[size] = font;
        }
        return font;
    }
}

/// <summary>Fluent 风格的图标按钮（Segoe MDL2 Assets 字形），带悬停与按下反馈。</summary>
public sealed class RssGlyphButton : Control
{
    private bool _hover;
    private bool _pressed;
    private bool _toggled;
    private RssTheme _theme = RssTheme.Light;

    public string Glyph { get; set; } = "\uE72C";
    public float GlyphSize { get; set; } = 11f;
    public RssTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            Invalidate();
        }
    }
    public bool Toggled
    {
        get => _toggled;
        set
        {
            if (_toggled == value) return;
            _toggled = value;
            Invalidate();
        }
    }

    public RssGlyphButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        Size = new Size(36, 32);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(1f, 1f, Width - 2f, Height - 2f);
        Color fill;
        if (!Enabled)
        {
            fill = Color.Transparent;
        }
        else if (_pressed)
        {
            fill = Color.FromArgb(_theme.IsDark ? 60 : 36, _theme.Accent);
        }
        else if (_hover)
        {
            fill = Color.FromArgb(_theme.IsDark ? 38 : 20, _theme.Text);
        }
        else if (_toggled)
        {
            fill = _theme.AccentSoft;
        }
        else
        {
            fill = Color.Transparent;
        }
        if (fill.A > 0)
        {
            using var path = ShutdownTheme.RoundRect(rect, 6f);
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
        }
        Color color = !Enabled ? _theme.TextTertiary : _toggled ? _theme.Accent : _theme.Text;
        using var font = _theme.IconFont(GlyphSize);
        TextRenderer.DrawText(g, Glyph, font, ClientRectangle, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
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
}

/// <summary>筛选胶囊按钮（全部 / 未读 / 星标）。</summary>
public sealed class RssChip : Control
{
    private bool _hover;
    private bool _checked;
    private RssTheme _theme = RssTheme.Light;

    public RssTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            Invalidate();
        }
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
        }
    }

    public RssChip()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        Size = new Size(58, 28);
        Cursor = Cursors.Hand;
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.Transparent;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        Color fill = _checked
            ? _theme.AccentSoft
            : _hover ? Color.FromArgb(_theme.IsDark ? 34 : 16, _theme.Text) : Color.Transparent;
        Color border = _checked ? _theme.Accent : _theme.Border;
        Color text = _checked ? _theme.Accent : _theme.TextSecondary;
        if (fill.A > 0)
        {
            using var path = ShutdownTheme.RoundRect(rect, 14f);
            using var brush = new SolidBrush(fill);
            e.Graphics.FillPath(brush, path);
        }
        using (var path = ShutdownTheme.RoundRect(rect, 14f))
        using (var pen = new Pen(border))
        {
            e.Graphics.DrawPath(pen, path);
        }
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
        Invalidate();
        base.OnMouseLeave(e);
    }
}

/// <summary>订阅源 / 文章缩略图异步缓存。下载完成后回到 UI 线程重绘。</summary>
public sealed class RssImageCache : IDisposable
{
    private readonly HttpClient _http = Http.CreateClient();
    private readonly SynchronizationContext? _context;
    private readonly Dictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loading = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public RssImageCache()
    {
        _context = SynchronizationContext.Current;
    }

    public Bitmap? Get(string? url, Action? onLoaded = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }
        lock (_gate)
        {
            if (_cache.TryGetValue(url, out var cached))
            {
                return cached;
            }
            if (_loading.Contains(url))
            {
                return null;
            }
            _loading.Add(url);
        }
        _ = Task.Run(async () =>
        {
            Bitmap? bitmap = null;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", Http.UserAgent);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead);
                if (response.IsSuccessStatusCode)
                {
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                    if (bytes.Length is > 0 and < 5 * 1024 * 1024)
                    {
                        using var stream = new MemoryStream(bytes);
                        using var image = Image.FromStream(stream);
                        bitmap = new Bitmap(image);
                    }
                }
            }
            catch
            {
                bitmap = null;
            }
            lock (_gate)
            {
                if (_cache.Count > 300)
                {
                    // 只从缓存移除，不立即 Dispose：避免与 UI 线程正在绘制的位图产生竞态。
                    // Bitmap 的终结器会释放原生句柄。
                    foreach (var key in _cache.Keys.Take(80).ToList())
                    {
                        _cache.Remove(key);
                    }
                }
                _cache[url] = bitmap;
                _loading.Remove(url);
            }
            if (onLoaded != null)
            {
                if (_context != null)
                {
                    _context.Post(_ => onLoaded(), null);
                }
                else
                {
                    onLoaded();
                }
            }
        });
        return null;
    }

    public Bitmap? Peek(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }
        lock (_gate)
        {
            return _cache.TryGetValue(url, out var bitmap) ? bitmap : null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var bitmap in _cache.Values)
            {
                bitmap?.Dispose();
            }
            _cache.Clear();
        }
        _http.Dispose();
    }
}
