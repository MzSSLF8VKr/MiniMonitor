using System.Drawing.Drawing2D;

namespace MiniMonitor.Rss;

/// <summary>
/// 文章列表：列表 / 卡片 / 杂志 / 紧凑四种视图，自绘 Fluent 卡片，
/// 支持未读圆点、星标点击、封面缩略图、滚轮、键盘导航与加载更多。
/// </summary>
public sealed class RssArticleList : Control
{
    private readonly List<RssArticle> _articles = new();
    private readonly List<Rectangle> _itemRects = new();
    private RssTheme _theme = RssTheme.Light;
    private RssImageCache? _images;
    private RssViewMode _view = RssViewMode.List;
    private bool _showSnippet = true;
    private bool _showCover = true;
    private bool _fadeRead = true;
    private int _scroll;
    private int _selectedIndex = -1;
    private int _hoverIndex = -1;
    private int _hoverStarIndex = -1;
    private bool _hasMore;

    public event Action<RssArticle?>? SelectionChanged;
    public event Action<RssArticle>? ArticleActivated;
    public event Action<RssArticle>? StarToggled;
    public event Action<RssArticle>? ReadToggled;
    public event Action<RssArticle>? HideRequested;
    public event Action<RssArticle>? OpenExternalRequested;
    public event Action<RssArticle, Point>? ContextMenuRequested;
    public event Action? LoadMoreRequested;

    public RssArticle? SelectedArticle =>
        _selectedIndex >= 0 && _selectedIndex < _articles.Count ? _articles[_selectedIndex] : null;

    public int ArticleCount => _articles.Count;

    public RssArticleList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = RssTheme.Light.Surface;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        TabStop = true;
    }

    public void SetTheme(RssTheme theme, RssImageCache? images)
    {
        _theme = theme;
        _images = images;
        BackColor = theme.Surface;
        ForeColor = theme.Text;
        Invalidate();
    }

    public void SetOptions(RssViewMode view, bool showSnippet, bool showCover, bool fadeRead)
    {
        _view = view;
        _showSnippet = showSnippet;
        _showCover = showCover;
        _fadeRead = fadeRead;
        RebuildLayout();
        Invalidate();
    }

    public void SetArticles(IEnumerable<RssArticle> articles, bool hasMore, long? keepSelectedAid = null)
    {
        var list = articles.ToList();
        _articles.Clear();
        _articles.AddRange(list);
        _hasMore = hasMore;
        _selectedIndex = -1;
        if (keepSelectedAid.HasValue)
        {
            int index = _articles.FindIndex(a => a.Aid == keepSelectedAid.Value);
            if (index >= 0)
            {
                _selectedIndex = index;
            }
        }
        if (_selectedIndex < 0)
        {
            _scroll = 0;
        }
        RebuildLayout();
        Invalidate();
        SelectionChanged?.Invoke(SelectedArticle);
    }

    public void UpdateArticle(RssArticle updated)
    {
        int index = _articles.FindIndex(a => a.Aid == updated.Aid);
        if (index >= 0)
        {
            _articles[index] = updated;
            Invalidate();
        }
    }

    public void RemoveArticle(long aid)
    {
        int index = _articles.FindIndex(a => a.Aid == aid);
        if (index < 0)
        {
            return;
        }
        _articles.RemoveAt(index);
        if (_selectedIndex > index)
        {
            _selectedIndex--;
        }
        else if (_selectedIndex == index)
        {
            _selectedIndex = Math.Min(index, _articles.Count - 1);
        }
        RebuildLayout();
        Invalidate();
        SelectionChanged?.Invoke(SelectedArticle);
    }

    public void Select(long aid, bool scrollIntoView = true)
    {
        int index = _articles.FindIndex(a => a.Aid == aid);
        if (index < 0)
        {
            return;
        }
        _selectedIndex = index;
        if (scrollIntoView)
        {
            EnsureVisible(index);
        }
        Invalidate();
        SelectionChanged?.Invoke(SelectedArticle);
    }

    public void SelectFirstIfNone()
    {
        if (_selectedIndex < 0 && _articles.Count > 0)
        {
            _selectedIndex = 0;
            Invalidate();
            SelectionChanged?.Invoke(SelectedArticle);
        }
    }

    public void Clear()
    {
        _articles.Clear();
        _selectedIndex = -1;
        _scroll = 0;
        _hasMore = false;
        RebuildLayout();
        Invalidate();
        SelectionChanged?.Invoke(null);
    }

    private int BodyPadding => 10;
    private int Gap => 10;

    private int ItemHeight
    {
        get
        {
            if (_view == RssViewMode.Compact)
            {
                return 56;
            }
            if (_view == RssViewMode.Cards)
            {
                return _showCover ? 208 : 132;
            }
            if (_view == RssViewMode.Magazine)
            {
                return _showCover ? 246 : 152;
            }
            if (_showSnippet)
            {
                return 112;
            }
            return _showCover ? 92 : 66;
        }
    }

    private void RebuildLayout()
    {
        _itemRects.Clear();
        int top = 8 - _scroll;
        if (_view == RssViewMode.Cards)
        {
            int columns = ClientSize.Width >= 400 ? 2 : 1;
            int available = ClientSize.Width - BodyPadding * 2 - Gap * (columns - 1);
            int width = Math.Max(120, available / columns);
            int height = ItemHeight;
            for (int i = 0; i < _articles.Count; i++)
            {
                int row = i / columns;
                int column = i % columns;
                _itemRects.Add(new Rectangle(
                    BodyPadding + column * (width + Gap),
                    top + row * (height + Gap),
                    width,
                    height));
            }
        }
        else
        {
            int height = ItemHeight;
            for (int i = 0; i < _articles.Count; i++)
            {
                _itemRects.Add(new Rectangle(BodyPadding, top + i * height,
                    Math.Max(0, ClientSize.Width - BodyPadding * 2), height));
            }
        }
    }

    private int ContentHeight
    {
        get
        {
            if (_itemRects.Count == 0)
            {
                return ClientSize.Height;
            }
            return _itemRects[^1].Bottom + _scroll + 12 + (_hasMore ? 54 : 0);
        }
    }

    private int MaxScroll => Math.Max(0, ContentHeight - ClientSize.Height);

    private Rectangle LoadMoreRect
    {
        get
        {
            int bottom = _itemRects.Count == 0 ? 100 : _itemRects[^1].Bottom;
            return new Rectangle(BodyPadding + 40, bottom + 8,
                Math.Max(80, ClientSize.Width - BodyPadding * 2 - 80), 38);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RebuildLayout();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(_theme.Surface);
        if (_articles.Count == 0)
        {
            DrawEmpty(g);
            return;
        }
        for (int i = 0; i < _articles.Count && i < _itemRects.Count; i++)
        {
            var rect = _itemRects[i];
            if (rect.Bottom < -4 || rect.Top > Height + 4)
            {
                continue;
            }
            DrawItem(g, _articles[i], rect, i);
        }
        if (_hasMore)
        {
            var loadRect = LoadMoreRect;
            if (loadRect.Top < Height)
            {
                using var path = ShutdownTheme.RoundRect(loadRect, 8f);
                using var brush = new SolidBrush(_theme.Hover);
                g.FillPath(brush, path);
                TextRenderer.DrawText(g, "加载更多…", _theme.Font(9f),
                    loadRect, _theme.TextSecondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
        if (MaxScroll > 0)
        {
            float ratio = (float)ClientSize.Height / (ClientSize.Height + MaxScroll);
            int barHeight = Math.Max(30, (int)(ClientSize.Height * ratio));
            int barY = (int)((ClientSize.Height - barHeight) * ((float)_scroll / MaxScroll));
            using var brush = new SolidBrush(Color.FromArgb(60, _theme.TextTertiary));
            using var path = ShutdownTheme.RoundRect(new RectangleF(Width - 5, barY, 3, barHeight), 1.5f);
            g.FillPath(brush, path);
        }
    }

    private void DrawEmpty(Graphics g)
    {
        var center = new Rectangle(0, Height / 2 - 80, Width, 40);
        TextRenderer.DrawText(g, "▤", _theme.Font(30f), center, _theme.TextTertiary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "无文章", _theme.Font(11f, FontStyle.Bold),
            new Rectangle(0, Height / 2 - 36, Width, 28), _theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "添加订阅源，或调整筛选条件后重试。", _theme.Font(9f),
            new Rectangle(0, Height / 2 - 4, Width, 24), _theme.TextTertiary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawItem(Graphics g, RssArticle article, Rectangle rect, int index)
    {
        bool selected = index == _selectedIndex;
        bool hover = index == _hoverIndex;
        var itemRect = new Rectangle(rect.X + 1, rect.Y + 2, rect.Width - 2, rect.Height - 4);
        if (selected)
        {
            using var path = ShutdownTheme.RoundRect(itemRect, 10f);
            using var brush = new SolidBrush(_theme.Selection);
            g.FillPath(brush, path);
            using var pen = new Pen(Color.FromArgb(120, _theme.Accent), 1f);
            g.DrawPath(pen, path);
        }
        else if (hover)
        {
            using var path = ShutdownTheme.RoundRect(itemRect, 10f);
            using var brush = new SolidBrush(_theme.Hover);
            g.FillPath(brush, path);
        }

        switch (_view)
        {
            case RssViewMode.Cards:
                DrawCard(g, article, itemRect, selected);
                break;
            case RssViewMode.Magazine:
                DrawMagazine(g, article, itemRect, selected);
                break;
            case RssViewMode.Compact:
                DrawCompact(g, article, itemRect, selected);
                break;
            default:
                DrawList(g, article, itemRect, selected);
                break;
        }
    }

    private void DrawList(Graphics g, RssArticle article, Rectangle rect, bool selected)
    {
        bool read = article.IsRead;
        Color titleColor = read && _fadeRead ? _theme.TextSecondary : _theme.Text;
        int coverSize = _showCover && !string.IsNullOrWhiteSpace(article.Thumb) ? 80 : 0;
        int textLeft = rect.Left + 22;
        int textWidth = rect.Width - 42 - (coverSize > 0 ? coverSize + 12 : 0);
        if (!read)
        {
            using var dot = new SolidBrush(_theme.UnreadDot);
            g.FillEllipse(dot, rect.Left + 12, rect.Top + 19, 7, 7);
        }
        var titleRect = new Rectangle(textLeft, rect.Top + 12, Math.Max(20, textWidth), 46);
        TextRenderer.DrawText(g, article.DisplayTitle,
            _theme.Font(10.2f, read ? FontStyle.Regular : FontStyle.Bold), titleRect, titleColor,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        var meta = FormatMeta(article);
        TextRenderer.DrawText(g, meta, _theme.Font(8.5f),
            new Rectangle(textLeft, rect.Top + 58, Math.Max(20, textWidth), 20),
            _theme.TextTertiary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (_showSnippet && !string.IsNullOrWhiteSpace(article.Snippet))
        {
            var snippetRect = new Rectangle(textLeft, rect.Top + 78, Math.Max(20, textWidth), 30);
            TextRenderer.DrawText(g, article.Snippet, _theme.Font(8.9f), snippetRect,
                _theme.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        if (coverSize > 0)
        {
            var cover = new Rectangle(rect.Right - coverSize - 10, rect.Top + (rect.Height - coverSize) / 2,
                coverSize, coverSize);
            DrawCover(g, article, cover, 8f);
        }
        DrawStar(g, article, rect, selected);
    }

    private void DrawCompact(Graphics g, RssArticle article, Rectangle rect, bool selected)
    {
        bool read = article.IsRead;
        Color titleColor = read && _fadeRead ? _theme.TextSecondary : _theme.Text;
        if (!read)
        {
            using var dot = new SolidBrush(_theme.UnreadDot);
            g.FillEllipse(dot, rect.Left + 12, rect.Top + rect.Height / 2 - 3, 6, 6);
        }
        string meta = FormatMeta(article);
        int right = 16 + 26 + 22;
        var metaSize = TextRenderer.MeasureText(g, meta, _theme.Font(8.4f));
        int metaWidth = Math.Min(metaSize.Width + 6, Math.Max(100, rect.Width / 2));
        var titleRect = new Rectangle(rect.Left + 22, rect.Top,
            Math.Max(40, rect.Width - 22 - metaWidth - right - 12), rect.Height);
        TextRenderer.DrawText(g, article.DisplayTitle,
            _theme.Font(9.6f, read ? FontStyle.Regular : FontStyle.Bold), titleRect, titleColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix);
        var metaRect = new Rectangle(rect.Right - right - metaWidth, rect.Top, metaWidth, rect.Height);
        TextRenderer.DrawText(g, meta, _theme.Font(8.4f), metaRect, _theme.TextTertiary,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix);
        DrawStar(g, article, rect, selected);
    }

    private void DrawCard(Graphics g, RssArticle article, Rectangle rect, bool selected)
    {
        bool read = article.IsRead;
        int padding = 10;
        int coverHeight = _showCover ? 92 : 0;
        if (_showCover)
        {
            var cover = new Rectangle(rect.Left + padding, rect.Top + padding,
                rect.Width - padding * 2, coverHeight);
            DrawCover(g, article, cover, 8f);
        }
        int top = rect.Top + padding + coverHeight + (_showCover ? 8 : 2);
        if (!read)
        {
            using var dot = new SolidBrush(_theme.UnreadDot);
            g.FillEllipse(dot, rect.Left + padding, top + 4, 6, 6);
        }
        var titleRect = new Rectangle(rect.Left + padding + (read ? 0 : 11), top,
            rect.Width - padding * 2 - (read ? 0 : 11) - 22, 42);
        TextRenderer.DrawText(g, article.DisplayTitle,
            _theme.Font(9.7f, read && _fadeRead ? FontStyle.Regular : FontStyle.Bold), titleRect,
            read && _fadeRead ? _theme.TextSecondary : _theme.Text,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (_showSnippet && !string.IsNullOrWhiteSpace(article.Snippet))
        {
            TextRenderer.DrawText(g, article.Snippet, _theme.Font(8.6f),
                new Rectangle(rect.Left + padding, top + 44, rect.Width - padding * 2, 30),
                _theme.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        TextRenderer.DrawText(g, FormatMeta(article), _theme.Font(8.2f),
            new Rectangle(rect.Left + padding, rect.Bottom - 24, rect.Width - padding * 2, 18),
            _theme.TextTertiary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix);
        DrawStar(g, article, rect, selected, overlayOnCover: _showCover);
    }

    private void DrawMagazine(Graphics g, RssArticle article, Rectangle rect, bool selected)
    {
        bool read = article.IsRead;
        int padding = 10;
        int coverHeight = _showCover ? 116 : 0;
        if (_showCover)
        {
            var cover = new Rectangle(rect.Left + padding, rect.Top + padding,
                rect.Width - padding * 2, coverHeight);
            DrawCover(g, article, cover, 9f);
            using var gradient = new LinearGradientBrush(
                new Rectangle(cover.Left, cover.Bottom - 56, cover.Width, 56),
                Color.Transparent, Color.FromArgb(150, 0, 0, 0), LinearGradientMode.Vertical);
            using var path = ShutdownTheme.RoundRect(cover, 9f);
            var state = g.Save();
            g.SetClip(path);
            g.FillRectangle(gradient, cover.Left, cover.Bottom - 56, cover.Width, 56);
            g.Restore(state);
        }
        int top = rect.Top + padding + coverHeight + (_showCover ? 8 : 2);
        if (!read)
        {
            using var dot = new SolidBrush(_theme.UnreadDot);
            g.FillEllipse(dot, rect.Left + padding, top + 5, 6, 6);
        }
        TextRenderer.DrawText(g, article.DisplayTitle,
            _theme.Font(11.2f, read && _fadeRead ? FontStyle.Regular : FontStyle.Bold),
            new Rectangle(rect.Left + padding + (read ? 0 : 11), top,
                rect.Width - padding * 2 - (read ? 0 : 11) - 24, 50),
            read && _fadeRead ? _theme.TextSecondary : _theme.Text,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (_showSnippet && !string.IsNullOrWhiteSpace(article.Snippet))
        {
            TextRenderer.DrawText(g, article.Snippet, _theme.Font(8.9f),
                new Rectangle(rect.Left + padding, top + 52, rect.Width - padding * 2, 34),
                _theme.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        TextRenderer.DrawText(g, FormatMeta(article), _theme.Font(8.3f),
            new Rectangle(rect.Left + padding, rect.Bottom - 24, rect.Width - padding * 2, 18),
            _theme.TextTertiary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix);
        DrawStar(g, article, rect, selected, overlayOnCover: _showCover);
    }

    private void DrawCover(Graphics g, RssArticle article, Rectangle rect, float radius)
    {
        Bitmap? bitmap = _images?.Peek(article.Thumb);
        if (bitmap == null && !string.IsNullOrWhiteSpace(article.Thumb) && _images != null)
        {
            _images.Get(article.Thumb, () =>
            {
                if (!IsDisposed)
                {
                    Invalidate();
                }
            });
        }
        using var path = ShutdownTheme.RoundRect(rect, radius);
        if (bitmap != null)
        {
            var state = g.Save();
            g.SetClip(path);
            float scale = Math.Max(rect.Width / (float)bitmap.Width, rect.Height / (float)bitmap.Height);
            float width = bitmap.Width * scale;
            float height = bitmap.Height * scale;
            g.DrawImage(bitmap, rect.X + (rect.Width - width) / 2, rect.Y + (rect.Height - height) / 2,
                width, height);
            g.Restore(state);
            return;
        }
        int hash = Math.Abs((article.SourceTitle + article.Aid).GetHashCode());
        Color[] colors =
        {
            Color.FromArgb(0, 120, 212), Color.FromArgb(16, 137, 62), Color.FromArgb(202, 80, 16),
            Color.FromArgb(136, 23, 152), Color.FromArgb(0, 153, 188), Color.FromArgb(196, 62, 94)
        };
        Color color = colors[hash % colors.Length];
        using var gradient = new LinearGradientBrush(rect,
            Color.FromArgb(220, color), Color.FromArgb(140, color), LinearGradientMode.ForwardDiagonal);
        g.FillPath(gradient, path);
        string letter = article.SourceTitle.Length > 0
            ? article.SourceTitle[..1].ToUpperInvariant()
            : "R";
        TextRenderer.DrawText(g, letter, _theme.Font(Math.Min(28f, rect.Height * 0.38f), FontStyle.Bold),
            rect, Color.FromArgb(235, 255, 255, 255),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void DrawStar(Graphics g, RssArticle article, Rectangle rect, bool selected,
        bool overlayOnCover = false)
    {
        var starRect = StarRect(rect, overlayOnCover);
        bool hover = _hoverStarIndex == _selectedIndexOf(article);
        if (hover)
        {
            using var brush = new SolidBrush(Color.FromArgb(_theme.IsDark ? 90 : 40, _theme.Text));
            g.FillEllipse(brush, starRect);
        }
        else if (overlayOnCover)
        {
            using var brush = new SolidBrush(Color.FromArgb(60, 0, 0, 0));
            g.FillEllipse(brush, starRect);
        }
        Color color = article.Starred ? _theme.Star : (overlayOnCover ? Color.White : _theme.TextTertiary);
        TextRenderer.DrawText(g, article.Starred ? "★" : "☆", _theme.Font(12f), starRect, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private int _selectedIndexOf(RssArticle article)
    {
        return _articles.FindIndex(a => a.Aid == article.Aid);
    }

    private Rectangle StarRect(Rectangle rect, bool overlay = false)
    {
        if (overlay)
        {
            return new Rectangle(rect.Right - 40, rect.Top + 12, 28, 28);
        }
        return new Rectangle(rect.Right - 40, rect.Top + (rect.Height - 28) / 2, 28, 28);
    }

    private static string FormatMeta(RssArticle article)
    {
        string time = FormatRelativeTime(article.PublishedTime);
        return string.IsNullOrWhiteSpace(article.SourceTitle)
            ? time
            : article.SourceTitle + " · " + time;
    }

    private static string FormatRelativeTime(DateTimeOffset time)
    {
        var now = DateTimeOffset.Now;
        var span = now - time;
        if (span.TotalSeconds < 60)
        {
            return "刚刚";
        }
        if (span.TotalMinutes < 60)
        {
            return $"{(int)span.TotalMinutes} 分钟前";
        }
        if (span.TotalHours < 24)
        {
            return $"{(int)span.TotalHours} 小时前";
        }
        if (span.TotalDays < 7)
        {
            return $"{(int)span.TotalDays} 天前";
        }
        return time.ToString("yyyy-MM-dd");
    }

    private int IndexAt(Point point)
    {
        for (int i = 0; i < _itemRects.Count; i++)
        {
            if (_itemRects[i].Contains(point))
            {
                return i;
            }
        }
        return -1;
    }

    private bool StarAt(Point point, int index, out RssArticle? article)
    {
        article = null;
        if (index < 0 || index >= _itemRects.Count || index >= _articles.Count)
        {
            return false;
        }
        bool overlay = _view is RssViewMode.Cards or RssViewMode.Magazine;
        var starRect = StarRect(_itemRects[index], overlay);
        if (starRect.Contains(point))
        {
            article = _articles[index];
            return true;
        }
        return false;
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Focus();
            int index = IndexAt(e.Location);
            if (StarAt(e.Location, index, out var starArticle) && starArticle != null)
            {
                StarToggled?.Invoke(starArticle);
                return;
            }
            if (index >= 0)
            {
                if (index != _selectedIndex)
                {
                    _selectedIndex = index;
                    Invalidate();
                    SelectionChanged?.Invoke(SelectedArticle);
                }
                ArticleActivated?.Invoke(_articles[index]);
            }
            else if (_hasMore && LoadMoreRect.Contains(e.Location))
            {
                LoadMoreRequested?.Invoke();
            }
        }
        base.OnMouseClick(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            int index = IndexAt(e.Location);
            if (index >= 0)
            {
                _selectedIndex = index;
                Invalidate();
                SelectionChanged?.Invoke(SelectedArticle);
                ContextMenuRequested?.Invoke(_articles[index], PointToScreen(e.Location));
            }
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int index = IndexAt(e.Location);
        int starIndex = StarAt(e.Location, index, out _) ? index : -1;
        if (index != _hoverIndex || starIndex != _hoverStarIndex)
        {
            _hoverIndex = index;
            _hoverStarIndex = starIndex;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverIndex = -1;
        _hoverStarIndex = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollBy(-e.Delta / 120 * 96);
        base.OnMouseWheel(e);
    }

    private void ScrollBy(int delta)
    {
        int next = Math.Clamp(_scroll + delta, 0, MaxScroll);
        if (next == _scroll)
        {
            return;
        }
        _scroll = next;
        RebuildLayout();
        Invalidate();
        if (_hasMore && _scroll + ClientSize.Height >= ContentHeight - ItemHeight)
        {
            LoadMoreRequested?.Invoke();
        }
    }

    private void EnsureVisible(int index)
    {
        if (index < 0 || index >= _itemRects.Count)
        {
            return;
        }
        var rect = _itemRects[index];
        int delta = 0;
        if (rect.Top < 0)
        {
            // 内容偏上：减小滚动偏移，把目标行拉回可视区
            delta = rect.Top - 8;
        }
        else if (rect.Bottom > ClientSize.Height)
        {
            // 内容偏下：增大滚动偏移
            delta = rect.Bottom - ClientSize.Height + 8;
        }
        if (delta != 0)
        {
            _scroll = Math.Clamp(_scroll + delta, 0, MaxScroll);
            RebuildLayout();
        }
    }

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData is Keys.Up or Keys.Down or Keys.Left or Keys.Right
            or Keys.Enter or Keys.Space || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_articles.Count == 0)
        {
            base.OnKeyDown(e);
            return;
        }
        int columns = _view == RssViewMode.Cards && ClientSize.Width >= 400 ? 2 : 1;
        switch (e.KeyCode)
        {
            case Keys.Down:
                MoveSelection(Math.Min(_articles.Count - 1, _selectedIndex + columns));
                e.Handled = true;
                break;
            case Keys.Up:
                MoveSelection(Math.Max(0, _selectedIndex - columns));
                e.Handled = true;
                break;
            case Keys.Right when columns == 2:
                MoveSelection(Math.Min(_articles.Count - 1, _selectedIndex + 1));
                e.Handled = true;
                break;
            case Keys.Left when columns == 2:
                MoveSelection(Math.Max(0, _selectedIndex - 1));
                e.Handled = true;
                break;
            case Keys.Enter:
            case Keys.Space:
                if (SelectedArticle != null)
                {
                    ArticleActivated?.Invoke(SelectedArticle);
                }
                e.Handled = true;
                break;
            case Keys.M:
                if (SelectedArticle != null)
                {
                    ReadToggled?.Invoke(SelectedArticle);
                }
                e.Handled = true;
                break;
            case Keys.S:
                if (SelectedArticle != null)
                {
                    StarToggled?.Invoke(SelectedArticle);
                }
                e.Handled = true;
                break;
            case Keys.H:
                if (SelectedArticle != null)
                {
                    HideRequested?.Invoke(SelectedArticle);
                }
                e.Handled = true;
                break;
            case Keys.B:
                if (SelectedArticle != null)
                {
                    OpenExternalRequested?.Invoke(SelectedArticle);
                }
                e.Handled = true;
                break;
            case Keys.PageDown:
                ScrollBy(ClientSize.Height - 60);
                e.Handled = true;
                break;
            case Keys.PageUp:
                ScrollBy(-(ClientSize.Height - 60));
                e.Handled = true;
                break;
        }
        base.OnKeyDown(e);
    }

    private void MoveSelection(int index)
    {
        if (_selectedIndex < 0 && _articles.Count > 0)
        {
            index = 0;
        }
        if (index < 0 || index >= _articles.Count || index == _selectedIndex)
        {
            return;
        }
        _selectedIndex = index;
        EnsureVisible(index);
        Invalidate();
        SelectionChanged?.Invoke(SelectedArticle);
    }
}
