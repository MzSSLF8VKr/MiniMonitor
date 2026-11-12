using System.Drawing.Drawing2D;
using MiniMonitor.Rss;

namespace MiniMonitor.Music;

public sealed record MusicNavItem(
    string Key,
    string Label,
    string Glyph,
    string Section,
    int Count = -1,
    bool Action = false,
    bool Highlight = false);

/// <summary>自绘侧边导航：资料库 / 智能列表 / 播放列表 / 其他。</summary>
public sealed class MusicSidebar : Control
{
    private readonly List<MusicNavItem> _items = new();
    private readonly List<(string Section, int Y, int Height)> _sections = new();
    private readonly List<(MusicNavItem Item, Rectangle Bounds)> _hitAreas = new();
    private RssTheme _theme = RssTheme.Light;
    private string _selectedKey = "songs";
    private int _hoverIndex = -1;
    private int _scrollOffset;
    private VScrollBar? _scrollBar;

    public MusicSidebar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
    }

    public event EventHandler<MusicNavItem>? ItemActivated;

    public RssTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            BackColor = value.Sidebar;
            Invalidate();
        }
    }

    public string SelectedKey
    {
        get => _selectedKey;
        set
        {
            _selectedKey = value ?? "";
            Invalidate();
        }
    }

    public void SetItems(IEnumerable<MusicNavItem> items)
    {
        _items.Clear();
        _items.AddRange(items);
        _scrollOffset = 0;
        UpdateScrollBar();
        Invalidate();
    }

    private int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

    private int ItemHeight => Scale(36);
    private int SectionHeight => Scale(34);

    private void UpdateScrollBar()
    {
        int contentHeight = MeasureContentHeight();
        bool needed = contentHeight > Height;
        if (needed && _scrollBar == null)
        {
            _scrollBar = new VScrollBar { Dock = DockStyle.Right, Width = Scale(10) };
            _scrollBar.Scroll += (_, _) =>
            {
                _scrollOffset = _scrollBar.Value;
                Invalidate();
            };
            Controls.Add(_scrollBar);
        }
        if (_scrollBar != null)
        {
            _scrollBar.Visible = needed;
            if (needed)
            {
                _scrollBar.LargeChange = Math.Max(1, Height / ItemHeight);
                _scrollBar.Maximum = Math.Max(0, contentHeight / ItemHeight) + _scrollBar.LargeChange;
            }
            else
            {
                _scrollOffset = 0;
            }
        }
    }

    private int MeasureContentHeight()
    {
        int height = Scale(10);
        string? currentSection = null;
        foreach (var item in _items)
        {
            if (item.Section != currentSection)
            {
                currentSection = item.Section;
                height += SectionHeight;
            }
            height += ItemHeight;
        }
        return height + Scale(14);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScrollBar();
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_scrollBar is { Visible: true })
        {
            int max = Math.Max(_scrollBar.Minimum,
                _scrollBar.Maximum - _scrollBar.LargeChange + 1);
            _scrollBar.Value = Math.Clamp(_scrollBar.Value - e.Delta / 120 * 2,
                _scrollBar.Minimum, max);
            _scrollOffset = _scrollBar.Value;
            Invalidate();
        }
        base.OnMouseWheel(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int index = HitTest(e.Location);
        if (index != _hoverIndex)
        {
            _hoverIndex = index;
            Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverIndex = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        int index = HitTest(e.Location);
        if (e.Button == MouseButtons.Left && index >= 0)
        {
            var item = _items[index];
            if (!item.Action)
            {
                _selectedKey = item.Key;
            }
            ItemActivated?.Invoke(this, item);
            Invalidate();
        }
        base.OnMouseClick(e);
    }

    private int HitTest(Point location)
    {
        foreach (var (item, bounds) in _hitAreas)
        {
            if (bounds.Contains(location))
            {
                return _items.IndexOf(item);
            }
        }
        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var background = new SolidBrush(_theme.Sidebar))
        {
            g.FillRectangle(background, ClientRectangle);
        }
        _hitAreas.Clear();
        _sections.Clear();
        int width = Width - (Scale(6));
        var sectionFont = _theme.Font(8.2f, FontStyle.Bold);
        var itemFont = _theme.Font(9.4f);
        int y = Scale(8) - _scrollOffset;
        string? section = null;
        foreach (var item in _items)
        {
            if (!string.Equals(section, item.Section, StringComparison.Ordinal))
            {
                section = item.Section;
                _sections.Add((section, y, SectionHeight));
                TextRenderer.DrawText(g, section.ToUpperInvariant(), sectionFont,
                    new Rectangle(Scale(18), y, width, SectionHeight), _theme.TextTertiary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                y += SectionHeight;
            }
            var row = new Rectangle(Scale(8), y + Scale(1), width - Scale(8), ItemHeight - Scale(2));
            bool selected = item.Key == _selectedKey && !item.Action;
            bool hover = _items.IndexOf(item) == _hoverIndex;
            if (selected)
            {
                using var brush = new SolidBrush(_theme.AccentSoft);
                using var path = ShutdownTheme.RoundRect(row, Scale(7));
                g.FillPath(brush, path);
                using var accent = new SolidBrush(_theme.Accent);
                g.FillRectangle(accent, new Rectangle(row.X, row.Y + Scale(9), Scale(3), row.Height - Scale(18)));
            }
            else if (hover)
            {
                using var brush = new SolidBrush(_theme.Hover);
                using var path = ShutdownTheme.RoundRect(row, Scale(7));
                g.FillPath(brush, path);
            }
            Color foreground = selected ? _theme.Accent
                : item.Highlight ? _theme.Star : _theme.TextSecondary;
            using (var iconFont = _theme.IconFont(10.5f))
            {
                TextRenderer.DrawText(g, item.Glyph, iconFont,
                    new Rectangle(row.X + Scale(10), row.Y, Scale(26), row.Height), foreground,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            TextRenderer.DrawText(g, item.Label, itemFont,
                new Rectangle(row.X + Scale(38), row.Y, row.Width - Scale(76), row.Height),
                selected ? _theme.Text : _theme.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            if (item.Count >= 0)
            {
                TextRenderer.DrawText(g, item.Count.ToString(), _theme.Font(8.2f),
                    new Rectangle(row.Right - Scale(48), row.Y, Scale(40), row.Height),
                    selected ? _theme.Accent : _theme.TextTertiary,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            _hitAreas.Add((item, row));
            y += ItemHeight;
        }
    }
}
