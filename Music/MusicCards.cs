using System.Drawing.Drawing2D;
using MiniMonitor.Rss;

namespace MiniMonitor.Music;

/// <summary>专辑 / 艺术家 / 流派 / 文件夹卡片。</summary>
public sealed class MusicCard : Control
{
    private readonly Font _titleFont;
    private readonly Font _subtitleFont;
    private readonly Font _iconFont;
    private bool _hover;
    private Bitmap? _cover;
    private bool _coverLoaded;

    public MusicCard(RssTheme theme, string title, string subtitle, string coverKey,
        string? leadingGlyph = null)
    {
        Theme = theme;
        CardTitle = title;
        CardSubtitle = subtitle;
        CoverKey = coverKey;
        LeadingGlyph = leadingGlyph;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(Scale(168), Scale(216));
        Margin = new Padding(Scale(6));
        _titleFont = theme.Font(9.6f, FontStyle.Bold);
        _subtitleFont = theme.Font(8.4f);
        _iconFont = theme.IconFont(10f);
    }

    public RssTheme Theme { get; }

    public string CardTitle { get; }

    public string CardSubtitle { get; }

    public string CoverKey { get; }

    public string? LeadingGlyph { get; }

    public object? Tag2 { get; set; }

    public event EventHandler? Activated;

    private int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

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

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Activated?.Invoke(this, EventArgs.Empty);
        }
        base.OnMouseDoubleClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int radius = Scale(10);
        var bounds = new RectangleF(0, 0, Width - 1, Height - 1);
        if (_hover)
        {
            using var hover = new SolidBrush(Theme.Hover);
            using var hoverPath = ShutdownTheme.RoundRect(bounds, radius);
            g.FillPath(hover, hoverPath);
        }
        int padding = Scale(10);
        int coverSize = Width - padding * 2;
        var coverRect = new Rectangle(padding, padding, coverSize, coverSize);
        using (var path = ShutdownTheme.RoundRect(
                   new RectangleF(coverRect.X, coverRect.Y, coverRect.Width, coverRect.Height), Scale(8)))
        {
            if (!_coverLoaded)
            {
                _coverLoaded = true;
                _cover = MusicCoverCache.Load(CoverKey);
            }
            if (_cover != null)
            {
                var state = g.Save();
                g.SetClip(path);
                g.DrawImage(_cover, coverRect);
                g.Restore(state);
            }
            else
            {
                using var brush = new LinearGradientBrush(coverRect,
                    Theme.IsDark ? Color.FromArgb(52, 60, 72) : Color.FromArgb(229, 238, 248),
                    Theme.IsDark ? Color.FromArgb(38, 44, 54) : Color.FromArgb(205, 221, 238), 45f);
                g.FillPath(brush, path);
                string glyph = LeadingGlyph ?? "\uE8D6";
                TextRenderer.DrawText(g, glyph, Theme.IconFont(30f), coverRect,
                    Color.FromArgb(Theme.IsDark ? 110 : 150, Theme.Accent),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
        if (_hover)
        {
            int button = Scale(36);
            var playRect = new Rectangle(coverRect.Right - button - Scale(8),
                coverRect.Bottom - button - Scale(8), button, button);
            using var accentBrush = new SolidBrush(Color.FromArgb(230, Theme.Accent));
            g.FillEllipse(accentBrush, playRect);
            TextRenderer.DrawText(g, "\uE768", Theme.IconFont(11f), playRect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        int textTop = coverRect.Bottom + Scale(9);
        var titleRect = new Rectangle(padding, textTop, Width - padding * 2, Scale(22));
        TextRenderer.DrawText(g, CardTitle, _titleFont, titleRect, Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        var subtitleRect = new Rectangle(padding, textTop + Scale(22), Width - padding * 2, Scale(20));
        TextRenderer.DrawText(g, CardSubtitle, _subtitleFont, subtitleRect, Theme.TextSecondary,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
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

/// <summary>双缓冲卡片容器，减少大量封面滚动时的闪烁。</summary>
public sealed class MusicCardPanel : FlowLayoutPanel
{
    public MusicCardPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint, true);
        DoubleBuffered = true;
        AutoScroll = true;
        FlowDirection = FlowDirection.LeftToRight;
        WrapContents = true;
        BackColor = Color.White;
        Padding = new Padding(14, 10, 14, 14);
    }

    public Color SurfaceColor
    {
        set => BackColor = value;
    }
}
