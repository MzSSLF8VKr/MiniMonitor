using System.Drawing.Drawing2D;
using MiniMonitor.Rss;

namespace MiniMonitor.Music;

/// <summary>
/// 虚拟化曲目列表：只绘制可见行，支持数万首歌曲、排序、多选、双击播放、
/// 点击心形收藏与右键菜单。
/// </summary>
public sealed class MusicTrackList : Control
{
    private const int RowHeightBase = 56;
    private const int HeaderHeightBase = 34;
    private const int IndexWidthBase = 42;
    private const int CoverSizeBase = 42;
    private const int DurationWidthBase = 62;
    private const int LikeWidthBase = 38;
    private const int ScrollWidth = 12;

    private readonly VScrollBar _scrollBar;
    private readonly HashSet<long> _selectedIds = new();
    private readonly Dictionary<string, Bitmap?> _covers = new(StringComparer.OrdinalIgnoreCase);
    private List<MusicTrack> _tracks = new();
    private RssTheme _theme = RssTheme.Light;
    private int _scrollOffset;
    private int _hoverIndex = -1;
    private int _anchorIndex = -1;
    private int _playingId = -1;
    private int _sortColumn;
    private bool _sortDescending;
    private string _emptyText = "音乐库是空的，点击“扫描音乐”开始";
    private string _emptyHint = "默认扫描 Windows 用户音乐目录";

    public MusicTrackList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.Selectable | ControlStyles.StandardDoubleClick, true);
        BackColor = Color.White;
        TabStop = true;
        _scrollBar = new VScrollBar { Dock = DockStyle.Right, Visible = false, Width = ScrollWidth };
        _scrollBar.Scroll += (_, e) =>
        {
            _scrollOffset = _scrollBar.Value;
            Invalidate();
        };
        Controls.Add(_scrollBar);
    }

    public event EventHandler<MusicTrack>? TrackActivated;
    public event EventHandler<MusicTrack>? LikeToggled;
    public event EventHandler? SelectionChanged;
    public event EventHandler<MusicSortRequest>? SortRequested;

    public RssTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            BackColor = value.Surface;
            Invalidate();
        }
    }

    public string EmptyText
    {
        get => _emptyText;
        set { _emptyText = value; Invalidate(); }
    }

    public string EmptyHint
    {
        get => _emptyHint;
        set { _emptyHint = value; Invalidate(); }
    }

    public IReadOnlyList<MusicTrack> Tracks => _tracks;

    public int SelectedIndex
    {
        get
        {
            var track = SelectedTrack;
            return track == null ? -1 : _tracks.IndexOf(track);
        }
    }

    public MusicTrack? SelectedTrack => _tracks.FirstOrDefault(t => _selectedIds.Contains(t.Id));

    public IReadOnlyList<MusicTrack> SelectedTracks =>
        _tracks.Where(t => _selectedIds.Contains(t.Id)).ToList();

    public int PlayingTrackId
    {
        get => _playingId;
        set { _playingId = value; Invalidate(); }
    }

    public void SetTracks(IEnumerable<MusicTrack> tracks, bool keepSelection = false)
    {
        var list = tracks.ToList();
        _tracks = list;
        if (!keepSelection)
        {
            _selectedIds.Clear();
        }
        else
        {
            var valid = list.Select(t => t.Id).ToHashSet();
            _selectedIds.RemoveWhere(id => !valid.Contains(id));
        }
        _hoverIndex = -1;
        _anchorIndex = -1;
        UpdateScrollBar();
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSelection()
    {
        _selectedIds.Clear();
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectTrack(MusicTrack? track, bool scrollIntoView = false)
    {
        _selectedIds.Clear();
        if (track != null)
        {
            _selectedIds.Add(track.Id);
            if (scrollIntoView)
            {
                int index = _tracks.IndexOf(track);
                if (index >= 0)
                {
                    ScrollToIndex(index);
                }
            }
        }
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectAll()
    {
        foreach (var track in _tracks)
        {
            _selectedIds.Add(track.Id);
        }
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshTrack(MusicTrack track)
    {
        int index = _tracks.FindIndex(t => t.Id == track.Id);
        if (index >= 0)
        {
            _tracks[index] = track;
            Invalidate(RowRectangle(index));
        }
    }

    public void RemoveTrack(long id)
    {
        int index = _tracks.FindIndex(t => t.Id == id);
        _tracks.RemoveAll(t => t.Id == id);
        _selectedIds.Remove(id);
        UpdateScrollBar();
        Invalidate();
        if (index >= 0)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int RowHeight => Dpi(56);
    private int HeaderHeight => Dpi(34);

    private int Dpi(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

    private void UpdateScrollBar()
    {
        int visibleHeight = Math.Max(0, Height - HeaderHeight);
        int totalHeight = _tracks.Count * RowHeight;
        bool needed = totalHeight > visibleHeight;
        _scrollBar.Visible = needed;
        int visibleRows = Math.Max(1, visibleHeight / RowHeight);
        _scrollBar.LargeChange = visibleRows;
        _scrollBar.SmallChange = 1;
        _scrollBar.Maximum = Math.Max(0, _tracks.Count - 1);
        if (!needed)
        {
            _scrollOffset = 0;
            _scrollBar.Value = 0;
        }
        else
        {
            int maxOffset = Math.Max(0, totalHeight - visibleHeight);
            _scrollOffset = Math.Min(_scrollOffset, maxOffset);
            _scrollBar.Value = Math.Min(_scrollBar.Value, _scrollBar.Maximum);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScrollBar();
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_scrollBar.Visible)
        {
            int lines = -e.Delta / 120 * 3;
            _scrollBar.Value = Math.Clamp(_scrollBar.Value + lines, _scrollBar.Minimum, _scrollBar.Maximum);
            _scrollOffset = _scrollBar.Value * RowHeight;
            Invalidate();
        }
        base.OnMouseWheel(e);
    }

    private int IndexAt(Point location)
    {
        if (location.Y < HeaderHeight)
        {
            return -1;
        }
        int index = (location.Y - HeaderHeight + _scrollOffset) / RowHeight;
        return index >= 0 && index < _tracks.Count ? index : -1;
    }

    private Rectangle RowRectangle(int index)
    {
        return new Rectangle(0, HeaderHeight + index * RowHeight - _scrollOffset, Width, RowHeight);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int index = IndexAt(e.Location);
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

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.Button == MouseButtons.Right)
        {
            int rightIndex = IndexAt(e.Location);
            if (rightIndex >= 0 && !_selectedIds.Contains(_tracks[rightIndex].Id))
            {
                _selectedIds.Clear();
                _selectedIds.Add(_tracks[rightIndex].Id);
                _anchorIndex = rightIndex;
                Invalidate();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            base.OnMouseDown(e);
            return;
        }
        if (e.Button != MouseButtons.Left)
        {
            base.OnMouseDown(e);
            return;
        }
        int headerClick = HeaderColumnAt(e.X);
        if (e.Y < HeaderHeight && headerClick >= 0)
        {
            if (_sortColumn == headerClick)
            {
                _sortDescending = !_sortDescending;
            }
            else
            {
                _sortColumn = headerClick;
                _sortDescending = false;
            }
            SortRequested?.Invoke(this, new MusicSortRequest(ColumnKey(headerClick), _sortDescending));
            Invalidate();
            base.OnMouseDown(e);
            return;
        }
        int index = IndexAt(e.Location);
        if (index < 0)
        {
            base.OnMouseDown(e);
            return;
        }
        var track = _tracks[index];
        if (IsLikeHit(e.Location))
        {
            LikeToggled?.Invoke(this, track);
            base.OnMouseDown(e);
            return;
        }
        bool control = ModifierKeys.HasFlag(Keys.Control);
        bool shift = ModifierKeys.HasFlag(Keys.Shift);
        if (shift && _anchorIndex >= 0)
        {
            _selectedIds.Clear();
            int from = Math.Min(_anchorIndex, index);
            int to = Math.Max(_anchorIndex, index);
            for (int i = from; i <= to; i++)
            {
                _selectedIds.Add(_tracks[i].Id);
            }
        }
        else if (control)
        {
            if (!_selectedIds.Add(track.Id))
            {
                _selectedIds.Remove(track.Id);
            }
            _anchorIndex = index;
        }
        else
        {
            _selectedIds.Clear();
            _selectedIds.Add(track.Id);
            _anchorIndex = index;
        }
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        base.OnMouseDown(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        int index = IndexAt(e.Location);
        if (index >= 0 && e.Button == MouseButtons.Left && !IsLikeHit(e.Location))
        {
            TrackActivated?.Invoke(this, _tracks[index]);
        }
        base.OnMouseDoubleClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_tracks.Count > 0)
        {
            int index = SelectedIndex;
            if (index < 0) index = 0;
            if (e.KeyCode == Keys.Down)
            {
                index = Math.Min(_tracks.Count - 1, index + 1);
                SelectIndex(index, e.Shift);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up)
            {
                index = Math.Max(0, index - 1);
                SelectIndex(index, e.Shift);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Home)
            {
                SelectIndex(0, e.Shift);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.End)
            {
                SelectIndex(_tracks.Count - 1, e.Shift);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                var track = _tracks[index];
                TrackActivated?.Invoke(this, track);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Space)
            {
                var track = _tracks[index];
                TrackActivated?.Invoke(this, track);
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.A)
            {
                SelectAll();
                e.Handled = true;
            }
        }
        base.OnKeyDown(e);
    }

    private void SelectIndex(int index, bool append)
    {
        if (!append)
        {
            _selectedIds.Clear();
        }
        _selectedIds.Add(_tracks[index].Id);
        _anchorIndex = index;
        ScrollToIndex(index);
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ScrollToIndex(int index)
    {
        if (!_scrollBar.Visible)
        {
            return;
        }
        int visibleRows = Math.Max(1, (Height - HeaderHeight) / RowHeight);
        if (index < _scrollBar.Value)
        {
            _scrollBar.Value = index;
        }
        else if (index >= _scrollBar.Value + visibleRows)
        {
            _scrollBar.Value = Math.Min(_scrollBar.Maximum, index - visibleRows + 1);
        }
        _scrollOffset = _scrollBar.Value * RowHeight;
    }

    private bool IsLikeHit(Point location)
    {
        return location.X >= Width - ScrollWidth - Dpi(LikeWidthBase) - 12
            && location.X <= Width - ScrollWidth - 4;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using (var background = new SolidBrush(_theme.Surface))
        {
            g.FillRectangle(background, ClientRectangle);
        }
        DrawHeader(g);
        if (_tracks.Count == 0)
        {
            DrawEmpty(g);
            return;
        }
        int first = Math.Max(0, _scrollOffset / RowHeight);
        int visibleRows = (Height - HeaderHeight) / RowHeight + 2;
        for (int i = first; i < Math.Min(_tracks.Count, first + visibleRows); i++)
        {
            DrawRow(g, i);
        }
    }

    private void DrawHeader(Graphics g)
    {
        var header = new Rectangle(0, 0, Width, HeaderHeight);
        using (var brush = new SolidBrush(_theme.SurfaceAlt))
        {
            g.FillRectangle(brush, header);
        }
        using (var pen = new Pen(_theme.Border))
        {
            g.DrawLine(pen, 0, HeaderHeight - 1, Width, HeaderHeight - 1);
        }
        var font = _theme.Font(8.6f);
        int indexWidth = Dpi(IndexWidthBase);
        int cover = Dpi(CoverSizeBase);
        int durationWidth = Dpi(DurationWidthBase);
        int likeWidth = Dpi(LikeWidthBase);
        int contentWidth = Width - ScrollWidth;
        int titleX = indexWidth + cover + Dpi(12);
        int titleWidth = Math.Max(120, (int)((contentWidth - titleX - durationWidth - likeWidth) * 0.55));
        int albumX = titleX + titleWidth;
        int albumWidth = Math.Max(80, contentWidth - albumX - durationWidth - likeWidth);
        DrawHeaderText(g, font, "#", 0, indexWidth, 1);
        DrawHeaderText(g, font, "标题", titleX, titleWidth, 0);
        DrawHeaderText(g, font, "专辑", albumX, albumWidth, 1);
        DrawHeaderText(g, font, "时长", contentWidth - durationWidth, durationWidth, 2);
        using var iconFont = _theme.IconFont(9f);
        TextRenderer.DrawText(g, "\uE00B", iconFont,
            new Rectangle(contentWidth - likeWidth, 0, likeWidth, HeaderHeight),
            _theme.TextTertiary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void DrawHeaderText(Graphics g, Font font, string text, int x, int width, int align)
    {
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
        if (align == 0) flags |= TextFormatFlags.Left;
        else if (align == 1) flags |= TextFormatFlags.HorizontalCenter;
        else flags |= TextFormatFlags.Right;
        TextRenderer.DrawText(g, text, font, new Rectangle(x, 0, width, HeaderHeight),
            _theme.TextSecondary, flags);
    }

    private int HeaderColumnAt(int x)
    {
        int indexWidth = Dpi(IndexWidthBase);
        int contentWidth = Width - ScrollWidth;
        int durationWidth = Dpi(DurationWidthBase);
        int likeWidth = Dpi(LikeWidthBase);
        int cover = Dpi(CoverSizeBase);
        int titleX = indexWidth + cover + Dpi(12);
        int titleWidth = (int)((contentWidth - titleX - durationWidth - likeWidth) * 0.55);
        int albumX = titleX + titleWidth;
        if (x < indexWidth) return 0;
        if (x >= titleX && x < albumX) return 1;
        if (x >= albumX && x < contentWidth - durationWidth) return 2;
        if (x >= contentWidth - durationWidth && x < contentWidth - likeWidth) return 3;
        return -1;
    }

    private static string ColumnKey(int column) => column switch
    {
        0 => "track",
        1 => "title",
        2 => "album",
        3 => "duration",
        _ => "title"
    };

    private void DrawEmpty(Graphics g)
    {
        using var iconFont = _theme.IconFont(34f);
        var iconRect = new Rectangle(0, Height / 2 - Dpi(80), Width, Dpi(60));
        TextRenderer.DrawText(g, "\uE8D6", iconFont, iconRect, _theme.TextTertiary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, _emptyText, _theme.Font(11f, FontStyle.Bold),
            new Rectangle(Dpi(20), Height / 2 - Dpi(8), Width - Dpi(40), Dpi(28)),
            _theme.TextSecondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, _emptyHint, _theme.Font(9f),
            new Rectangle(Dpi(20), Height / 2 + Dpi(24), Width - Dpi(40), Dpi(24)),
            _theme.TextTertiary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private void DrawRow(Graphics g, int index)
    {
        var track = _tracks[index];
        var row = RowRectangle(index);
        if (row.Bottom < HeaderHeight || row.Top > Height)
        {
            return;
        }
        bool selected = _selectedIds.Contains(track.Id);
        bool hover = index == _hoverIndex;
        bool playing = track.Id == _playingId;
        if (selected)
        {
            using var brush = new SolidBrush(_theme.Selection);
            g.FillRectangle(brush, row);
        }
        else if (hover)
        {
            using var brush = new SolidBrush(_theme.Hover);
            g.FillRectangle(brush, row);
        }
        if (playing)
        {
            using var accent = new SolidBrush(_theme.Accent);
            g.FillRectangle(accent, new Rectangle(0, row.Top + Dpi(10), Dpi(3), row.Height - Dpi(20)));
        }
        int indexWidth = Dpi(IndexWidthBase);
        int cover = Dpi(CoverSizeBase);
        int durationWidth = Dpi(DurationWidthBase);
        int likeWidth = Dpi(LikeWidthBase);
        int contentWidth = Width - ScrollWidth;
        int titleX = indexWidth + cover + Dpi(12);
        int titleWidth = Math.Max(120, (int)((contentWidth - titleX - durationWidth - likeWidth) * 0.55));
        int albumX = titleX + titleWidth;
        int albumWidth = Math.Max(80, contentWidth - albumX - durationWidth - likeWidth);

        var indexRect = new Rectangle(0, row.Top, indexWidth, row.Height);
        if (playing)
        {
            using var iconFont = _theme.IconFont(9f);
            TextRenderer.DrawText(g, "\uE768", iconFont, indexRect, _theme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        else
        {
            TextRenderer.DrawText(g, (index + 1).ToString(), _theme.Font(8.4f), indexRect,
                selected ? _theme.Text : _theme.TextTertiary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        var coverRect = new Rectangle(indexWidth, row.Top + (row.Height - cover) / 2, cover, cover);
        DrawCover(g, track, coverRect);

        var titleRect = new Rectangle(titleX, row.Top + Dpi(8), titleWidth, Dpi(22));
        TextRenderer.DrawText(g, track.DisplayTitle, _theme.Font(9.6f, FontStyle.Bold), titleRect,
            selected || hover ? _theme.Text : _theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        var subtitleRect = new Rectangle(titleX, row.Top + Dpi(29), titleWidth, Dpi(19));
        string subtitle = track.DisplayArtist;
        if (track.IsPerfect)
        {
            subtitle = "PerfectMusic · " + subtitle;
        }
        TextRenderer.DrawText(g, subtitle, _theme.Font(8.4f), subtitleRect, _theme.TextSecondary,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        var albumRect = new Rectangle(albumX, row.Top + Dpi(8), albumWidth, Dpi(20));
        TextRenderer.DrawText(g, track.DisplayAlbum, _theme.Font(8.8f), albumRect, _theme.TextSecondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        var detailRect = new Rectangle(albumX, row.Top + Dpi(29), albumWidth, Dpi(18));
        string detail = track.IsHiRes ? "Hi-Res · " + track.QualityText : track.QualityText;
        TextRenderer.DrawText(g, detail, _theme.Font(7.8f), detailRect, _theme.TextTertiary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        var durationRect = new Rectangle(contentWidth - durationWidth - Dpi(6), row.Top, durationWidth, row.Height);
        TextRenderer.DrawText(g, track.DurationText, _theme.Font(8.8f), durationRect,
            _theme.TextSecondary,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var likeRect = new Rectangle(contentWidth - likeWidth, row.Top, likeWidth, row.Height);
        using var heartFont = _theme.IconFont(11f);
        Color heartColor = track.Liked ? _theme.Star
            : hover || selected ? _theme.TextTertiary : Color.Transparent;
        if (heartColor.A > 0)
        {
            TextRenderer.DrawText(g, track.Liked ? "\uE735" : "\uE734", heartFont, likeRect, heartColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        using (var pen = new Pen(Color.FromArgb(30, _theme.Border)))
        {
            g.DrawLine(pen, titleX, row.Bottom - 1, contentWidth - Dpi(8), row.Bottom - 1);
        }
    }

    private void DrawCover(Graphics g, MusicTrack track, Rectangle rect)
    {
        if (!_covers.TryGetValue(track.CoverKey ?? "", out var bitmap))
        {
            bitmap = MusicCoverCache.Load(track.CoverKey);
            if (_covers.Count > 400)
            {
                foreach (var cached in _covers.Values)
                {
                    cached?.Dispose();
                }
                _covers.Clear();
            }
            _covers[track.CoverKey ?? ""] = bitmap;
        }
        using var path = ShutdownTheme.RoundRect(
            new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), Dpi(6));
        if (bitmap != null)
        {
            var state = g.Save();
            g.SetClip(path);
            g.DrawImage(bitmap, rect);
            g.Restore(state);
        }
        else
        {
            using var brush = new SolidBrush(_theme.SurfaceAlt);
            g.FillPath(brush, path);
            using var iconFont = _theme.IconFont(14f);
            TextRenderer.DrawText(g, "\uE8D6", iconFont, rect, _theme.TextTertiary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var bitmap in _covers.Values)
            {
                bitmap?.Dispose();
            }
            _covers.Clear();
        }
        base.Dispose(disposing);
    }
}

public sealed record MusicSortRequest(string Column, bool Descending);
