using System.Drawing.Drawing2D;

namespace MiniMonitor.Rss;

/// <summary>
/// 左侧订阅源导航：全部 / 未读 / 星标 / 今天 + 分组与订阅源，显示未读数，
/// 支持展开折叠、右键菜单、滚轮与键盘导航。全部自绘，保持 Fluent 观感。
/// </summary>
public sealed class RssSourceList : Control
{
    private enum RowKind
    {
        Smart,
        Section,
        Group,
        Feed
    }

    private sealed class Row
    {
        public RowKind Kind;
        public RssScope Scope = RssScope.All();
        public RssGroup? Group;
        public RssFeed? Feed;
        public string Title = "";
        public int Count;
        public int Indent;
        public string Glyph = "";
        public Rectangle Bounds;
        public bool IsSeparator;
    }

    private readonly List<Row> _rows = new();
    private readonly List<Row> _visible = new();
    private RssTheme _theme = RssTheme.Light;
    private RssImageCache? _images;
    private int _scroll;
    private int _hoverIndex = -1;
    private RssScope _selected = RssScope.All();
    private readonly ToolTip _tip = new();
    private readonly Dictionary<int, Bitmap?> _feedIconCache = new();

    public event Action<RssScope>? ScopeChanged;
    public event Action<RssFeed, Point>? FeedMenuRequested;
    public event Action<RssGroup, Point>? GroupMenuRequested;
    public event Action<Point>? EmptyMenuRequested;
    public event Action<RssFeed>? RefreshFeedRequested;
    public event Action<RssGroup>? GroupToggleRequested;

    public RssScope SelectedScope => _selected;

    public RssSourceList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = RssTheme.Light.Sidebar;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        _tip.InitialDelay = 600;
        _tip.ReshowDelay = 200;
    }

    public void SetTheme(RssTheme theme, RssImageCache? images)
    {
        _theme = theme;
        _images = images;
        BackColor = theme.Sidebar;
        ForeColor = theme.Text;
        Invalidate();
    }

    public void SetData(IReadOnlyList<RssGroup> groups, IReadOnlyList<RssFeed> feeds,
        (int All, int Today, int Starred) counts)
    {
        _lastGroups = groups.ToList();
        _lastFeeds = feeds.ToList();
        _lastCounts = counts;
        _rows.Clear();
        _rows.Add(new Row { Kind = RowKind.Smart, Scope = RssScope.All(), Title = "全部文章", Count = counts.All, Glyph = "≡" });
        _rows.Add(new Row { Kind = RowKind.Smart, Scope = RssScope.Unread(), Title = "未读文章", Count = counts.All, Glyph = "●" });
        _rows.Add(new Row { Kind = RowKind.Smart, Scope = RssScope.Starred(), Title = "星标文章", Count = counts.Starred, Glyph = "★" });
        _rows.Add(new Row { Kind = RowKind.Smart, Scope = RssScope.Today(), Title = "今天", Count = counts.Today, Glyph = "◷" });
        _rows.Add(new Row { Kind = RowKind.Section, Title = "订阅源", IsSeparator = true });

        var ungrouped = feeds.Where(f => f.Gid == null).OrderBy(f => f.Position).ThenBy(f => f.Sid).ToList();
        foreach (var feed in ungrouped)
        {
            _rows.Add(new Row { Kind = RowKind.Feed, Feed = feed, Title = feed.Title, Count = feed.Unread });
        }
        foreach (var group in groups.OrderBy(g => g.Position).ThenBy(g => g.Gid))
        {
            var groupFeeds = feeds.Where(f => f.Gid == group.Gid)
                .OrderBy(f => f.Position).ThenBy(f => f.Sid).ToList();
            _rows.Add(new Row
            {
                Kind = RowKind.Group,
                Group = group,
                Title = group.Name,
                Count = groupFeeds.Sum(f => f.Unread)
            });
            if (group.Expanded)
            {
                foreach (var feed in groupFeeds)
                {
                    _rows.Add(new Row
                    {
                        Kind = RowKind.Feed,
                        Feed = feed,
                        Title = feed.Title,
                        Count = feed.Unread,
                        Indent = 16
                    });
                }
            }
        }
        if (_rows.All(r => r.Kind != RowKind.Feed))
        {
            _rows.Add(new Row
            {
                Kind = RowKind.Section,
                Title = "还没有订阅源，点击 + 添加",
                IsSeparator = false
            });
        }
        SyncSelectionWithRows();
        RebuildLayout();
        Invalidate();
    }

    private void SyncSelectionWithRows()
    {
        bool exists = _rows.Any(r => r.Scope.Kind == _selected.Kind && r.Scope.Id == _selected.Id);
        if (!exists && _selected.Kind is RssScopeKind.Feed or RssScopeKind.Group)
        {
            _selected = RssScope.All();
        }
    }

    private void RebuildLayout()
    {
        _scroll = Math.Min(_scroll, MaxScroll);
        _visible.Clear();
        int y = 8 - _scroll;
        foreach (var row in _rows)
        {
            int height = row.Kind switch
            {
                RowKind.Smart => 40,
                RowKind.Section => row.IsSeparator ? 38 : 44,
                RowKind.Group => 36,
                _ => 36
            };
            row.Bounds = new Rectangle(0, y, ClientSize.Width, height);
            _visible.Add(row);
            y += height;
        }
        int total = y + _scroll + 8;
        if (total < ClientSize.Height)
        {
            _scroll = 0;
            RebuildLayoutInternal();
        }
    }

    private void RebuildLayoutInternal()
    {
        int y = 8;
        foreach (var row in _rows)
        {
            int height = row.Kind switch
            {
                RowKind.Smart => 40,
                RowKind.Section => row.IsSeparator ? 38 : 44,
                RowKind.Group => 36,
                _ => 36
            };
            row.Bounds = new Rectangle(0, y, ClientSize.Width, height);
            y += height;
        }
    }

    private int MaxScroll
    {
        get
        {
            int total = _rows.Sum(r => r.Kind switch
            {
                RowKind.Smart => 40,
                RowKind.Section => r.IsSeparator ? 38 : 44,
                RowKind.Group => 36,
                _ => 36
            }) + 16;
            return Math.Max(0, total - ClientSize.Height);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RebuildLayout();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(_theme.Sidebar);
        using (var borderPen = new Pen(_theme.Border))
        {
            g.DrawLine(borderPen, Width - 1, 0, Width - 1, Height);
        }
        int index = 0;
        foreach (var row in _rows)
        {
            int rowIndex = index++;
            if (row.Bounds.Bottom < 0 || row.Bounds.Top > Height)
            {
                continue;
            }
            DrawRow(g, row, rowIndex);
        }
        if (MaxScroll > 0)
        {
            float ratio = (float)ClientSize.Height / (ClientSize.Height + MaxScroll);
            int barHeight = Math.Max(32, (int)(ClientSize.Height * ratio));
            int barY = (int)((ClientSize.Height - barHeight) * ((float)_scroll / MaxScroll));
            using var brush = new SolidBrush(Color.FromArgb(70, _theme.TextTertiary));
            using var path = ShutdownTheme.RoundRect(new RectangleF(Width - 5, barY, 3, barHeight), 1.5f);
            g.FillPath(brush, path);
        }
    }

    private void DrawRow(Graphics g, Row row, int index)
    {
        var bounds = row.Bounds;
        if (row.Kind == RowKind.Section)
        {
            if (row.IsSeparator)
            {
                int lineY = bounds.Top + 14;
                using var pen = new Pen(_theme.Border);
                g.DrawLine(pen, 16, lineY, Width - 16, lineY);
                TextRenderer.DrawText(g, row.Title, _theme.Font(8.5f, FontStyle.Bold),
                    new Rectangle(18, bounds.Top + 20, Width - 36, 18),
                    _theme.TextTertiary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
            else
            {
                TextRenderer.DrawText(g, row.Title, _theme.Font(8.8f),
                    new Rectangle(18, bounds.Top, Width - 36, bounds.Height),
                    _theme.TextTertiary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
            return;
        }

        bool selected = row.Kind == RowKind.Smart
            ? _selected.Kind == row.Scope.Kind
            : row.Kind == RowKind.Feed && row.Feed != null
                ? _selected.Kind == RssScopeKind.Feed && _selected.Id == row.Feed.Sid
                : row.Kind == RowKind.Group && row.Group != null
                    && _selected.Kind == RssScopeKind.Group && _selected.Id == row.Group.Gid;
        bool hover = index == _hoverIndex;
        var itemRect = new Rectangle(8, bounds.Top + 2, Width - 16, bounds.Height - 4);
        if (selected)
        {
            using var path = ShutdownTheme.RoundRect(itemRect, 7f);
            using var brush = new SolidBrush(_theme.AccentSoft);
            g.FillPath(brush, path);
            using var accent = new SolidBrush(_theme.Accent);
            using var accentPath = ShutdownTheme.RoundRect(
                new RectangleF(itemRect.Left, itemRect.Top + 8, 3, itemRect.Height - 16), 1.5f);
            g.FillPath(accent, accentPath);
        }
        else if (hover)
        {
            using var path = ShutdownTheme.RoundRect(itemRect, 7f);
            using var brush = new SolidBrush(_theme.Hover);
            g.FillPath(brush, path);
        }

        int x = 18 + (row.Kind == RowKind.Feed ? row.Indent : 0);
        if (row.Kind == RowKind.Group)
        {
            string chevron = row.Group?.Expanded == true ? "\uE70D" : "\uE76C";
            using var chevronFont = _theme.IconFont(7.5f);
            TextRenderer.DrawText(g, chevron, chevronFont,
                new Rectangle(x, bounds.Top, 14, bounds.Height),
                _theme.TextSecondary, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x += 18;
            DrawFolder(g, new Rectangle(x, bounds.Top + (bounds.Height - 16) / 2, 16, 16),
                selected ? _theme.Accent : _theme.TextSecondary);
            x += 24;
        }
        else if (row.Kind == RowKind.Feed)
        {
            DrawFeedIcon(g, row, new Rectangle(x, bounds.Top + (bounds.Height - 18) / 2, 18, 18));
            x += 26;
        }
        else
        {
            Color glyphColor = selected ? _theme.Accent : _theme.TextSecondary;
            if (row.Glyph == "★")
            {
                glyphColor = selected ? _theme.Accent : _theme.Star;
            }
            TextRenderer.DrawText(g, row.Glyph, _theme.Font(12f),
                new Rectangle(x, bounds.Top, 20, bounds.Height), glyphColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            x += 26;
        }

        int countWidth = row.Count > 0 ? 40 : 0;
        var textRect = new Rectangle(x, bounds.Top, Math.Max(0, Width - x - 24 - countWidth), bounds.Height);
        Color textColor = selected ? _theme.Accent
            : row.Feed?.Hidden == true ? _theme.TextTertiary
            : _theme.Text;
        Font font = row.Kind == RowKind.Group
            ? _theme.Font(9.2f, FontStyle.Bold)
            : row.Count > 0 && row.Kind == RowKind.Feed
                ? _theme.Font(9.5f, selected ? FontStyle.Bold : FontStyle.Regular)
                : _theme.Font(9.5f);
        TextRenderer.DrawText(g, row.Title, font, textRect, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix);

        if (row.Count > 0)
        {
            string countText = row.Count > 999 ? "999+" : row.Count.ToString();
            var countRect = new Rectangle(Width - 46, bounds.Top + (bounds.Height - 20) / 2, 34, 20);
            using (var path = ShutdownTheme.RoundRect(countRect, 9f))
            {
                using var brush = new SolidBrush(selected
                    ? _theme.Accent
                    : Color.FromArgb(_theme.IsDark ? 46 : 22, _theme.Text));
                if (!selected)
                {
                    g.FillPath(brush, path);
                }
                else
                {
                    g.FillPath(brush, path);
                }
            }
            TextRenderer.DrawText(g, countText, _theme.Font(8f, FontStyle.Bold), countRect,
                selected ? (_theme.IsDark ? Color.FromArgb(20, 30, 38) : Color.White) : _theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private void DrawFolder(Graphics g, Rectangle rect, Color color)
    {
        using var pen = new Pen(color, 1.4f);
        using var path = new GraphicsPath();
        float r = 2f;
        path.AddArc(rect.X, rect.Y + 3, r * 2, r * 2, 180, 90);
        path.AddLine(rect.X + r, rect.Y + 3, rect.X + 6, rect.Y + 3);
        path.AddLine(rect.X + 6, rect.Y + 3, rect.X + 8, rect.Y + 5);
        path.AddLine(rect.X + 8, rect.Y + 5, rect.Right - r, rect.Y + 5);
        path.AddArc(rect.Right - r * 2, rect.Y + 3, r * 2, r * 2, 270, 90);
        path.AddLine(rect.Right, rect.Y + 5, rect.Right, rect.Bottom - r);
        path.AddArc(rect.Right - r * 2, rect.Bottom - r * 2, r * 2, r * 2, 0, 90);
        path.AddLine(rect.Right - r, rect.Bottom, rect.X + r, rect.Bottom);
        path.AddArc(rect.X, rect.Bottom - r * 2, r * 2, r * 2, 90, 90);
        path.CloseFigure();
        g.DrawPath(pen, path);
    }

    private void DrawFeedIcon(Graphics g, Row row, Rectangle rect)
    {
        if (row.Feed == null)
        {
            return;
        }
        if (!_feedIconCache.TryGetValue(row.Feed.Sid, out Bitmap? bitmap))
        {
            bitmap = row.Feed.Icon is { Length: > 0 } ? TryDecode(row.Feed.Icon) : null;
            _feedIconCache[row.Feed.Sid] = bitmap;
        }
        else if (bitmap == null && row.Feed.Icon is { Length: > 0 })
        {
            bitmap = TryDecode(row.Feed.Icon);
            _feedIconCache[row.Feed.Sid] = bitmap;
        }
        if (bitmap == null && row.Feed.IconUrl != null && _images != null)
        {
            bitmap = _images.Peek(row.Feed.IconUrl);
            if (bitmap == null)
            {
                _images.Get(row.Feed.IconUrl, () =>
                {
                    if (!IsDisposed)
                    {
                        Invalidate();
                    }
                });
            }
        }
        if (bitmap != null)
        {
            g.DrawImage(bitmap, rect);
            return;
        }
        string title = row.Feed?.Title ?? "?";
        string letter = title.Length > 0 ? title[..1].ToUpperInvariant() : "?";
        int hash = Math.Abs(title.GetHashCode());
        Color[] palette =
        {
            Color.FromArgb(0, 120, 212), Color.FromArgb(16, 137, 62), Color.FromArgb(202, 80, 16),
            Color.FromArgb(136, 23, 152), Color.FromArgb(0, 153, 188), Color.FromArgb(196, 62, 94)
        };
        Color color = palette[hash % palette.Length];
        using var brush = new SolidBrush(color);
        using var path = ShutdownTheme.RoundRect(rect, 4f);
        g.FillPath(brush, path);
        TextRenderer.DrawText(g, letter, _theme.Font(8.5f, FontStyle.Bold),
            new Rectangle(rect.X, rect.Y, rect.Width, rect.Height - 1), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void ResetIconCache()
    {
        foreach (var bitmap in _feedIconCache.Values)
        {
            bitmap?.Dispose();
        }
        _feedIconCache.Clear();
    }

    private static Bitmap? TryDecode(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return null;
        }
        try
        {
            using var stream = new MemoryStream(bytes);
            using var image = Image.FromStream(stream);
            return new Bitmap(image);
        }
        catch
        {
            return null;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int index = RowIndexAt(e.Location);
        if (index != _hoverIndex)
        {
            _hoverIndex = index;
            Invalidate();
        }
        if (_hoverIndex >= 0)
        {
            var row = _rows[_hoverIndex];
            if (row.Kind is RowKind.Feed or RowKind.Group)
            {
                _tip.SetToolTip(this, row.Title);
            }
            else
            {
                _tip.SetToolTip(this, "");
            }
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverIndex = -1;
        _tip.SetToolTip(this, "");
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            int index = RowIndexAt(e.Location);
            if (index >= 0)
            {
                var row = _rows[index];
                switch (row.Kind)
                {
                    case RowKind.Smart:
                        SelectScope(row.Scope);
                        break;
                    case RowKind.Group when row.Group != null:
                        if (e.X < 44)
                        {
                            row.Group.Expanded = !row.Group.Expanded;
                            GroupToggleRequested?.Invoke(row.Group);
                            SetData(_lastGroups, _lastFeeds, _lastCounts);
                        }
                        SelectScope(RssScope.Group(row.Group.Gid, row.Group.Name));
                        break;
                    case RowKind.Feed when row.Feed != null:
                        SelectScope(RssScope.Feed(row.Feed.Sid, row.Feed.Title));
                        break;
                }
            }
        }
        base.OnMouseClick(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            int index = RowIndexAt(e.Location);
            if (index >= 0 && _rows[index].Kind == RowKind.Feed && _rows[index].Feed != null)
            {
                RefreshFeedRequested?.Invoke(_rows[index].Feed!);
            }
        }
        base.OnMouseDoubleClick(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollBy(-e.Delta / 120 * 72);
        base.OnMouseWheel(e);
    }

    private void ScrollBy(int delta)
    {
        int next = Math.Clamp(_scroll + delta, 0, MaxScroll);
        if (next != _scroll)
        {
            _scroll = next;
            RebuildLayout();
            Invalidate();
        }
    }

    private List<RssGroup> _lastGroups = new();
    private List<RssFeed> _lastFeeds = new();
    private (int All, int Today, int Starred) _lastCounts;

    private int RowIndexAt(Point point)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            if (row.Kind == RowKind.Section)
            {
                continue;
            }
            if (row.Bounds.Contains(point))
            {
                return i;
            }
        }
        return -1;
    }

    public void SelectScope(RssScope scope, bool raiseEvent = true)
    {
        _selected = scope;
        if (raiseEvent)
        {
            ScopeChanged?.Invoke(scope);
        }
        Invalidate();
    }

    public void SetSelected(RssScope scope)
    {
        _selected = scope;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            int index = RowIndexAt(e.Location);
            if (index >= 0)
            {
                var row = _rows[index];
                if (row.Kind == RowKind.Feed && row.Feed != null)
                {
                    SelectScope(RssScope.Feed(row.Feed.Sid, row.Feed.Title));
                    FeedMenuRequested?.Invoke(row.Feed, PointToScreen(e.Location));
                    return;
                }
                if (row.Kind == RowKind.Group && row.Group != null)
                {
                    SelectScope(RssScope.Group(row.Group.Gid, row.Group.Name));
                    GroupMenuRequested?.Invoke(row.Group, PointToScreen(e.Location));
                    return;
                }
            }
            EmptyMenuRequested?.Invoke(PointToScreen(e.Location));
        }
        base.OnMouseUp(e);
    }

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData is Keys.Up or Keys.Down or Keys.Enter || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var selectable = _rows.Where(r => r.Kind != RowKind.Section).ToList();
        int current = selectable.FindIndex(r => r.Scope.Kind == _selected.Kind && r.Scope.Id == _selected.Id);
        if (e.KeyCode == Keys.Down && current < selectable.Count - 1)
        {
            SelectScope(selectable[current + 1].Scope);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Up && current > 0)
        {
            SelectScope(selectable[current - 1].Scope);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip.Dispose();
            ResetIconCache();
        }
        base.Dispose(disposing);
    }
}
