using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using MiniMonitor.Rss;

namespace MiniMonitor.Music;

/// <summary>迷你播放器（插件，默认关闭）。</summary>
public sealed class MusicMiniPlayerForm : Form
{
    private readonly MusicPlayer _player;
    private readonly RssTheme _theme;
    private readonly MusicCoverBox _cover;
    private readonly Label _title;
    private readonly Label _artist;
    private readonly MusicSeekBar _seek;
    private readonly MusicIconButton _play;
    private readonly Label _lyricLabel;
    private LrcDocument? _lyrics;
    private long _lyricsTrackId = -1;
    private bool _dragging;
    private Point _dragOffset;
    private readonly ToolTip _toolTip = new();

    public event EventHandler? ShowMainRequested;

    public MusicMiniPlayerForm(MusicPlayer player, MusicSettings settings, RssTheme theme)
    {
        _player = player;
        _theme = theme;
        Text = "离线音乐播放器 · 迷你播放器";
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(430, 176);
        BackColor = theme.Surface;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw, true);
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point(screen.Right - Width - 24, screen.Bottom - Height - 24);

        _cover = new MusicCoverBox { Location = new Point(14, 14), Size = new Size(96, 96), Theme = theme };
        Controls.Add(_cover);
        _title = new Label
        {
            AutoSize = false,
            Location = new Point(124, 16),
            Size = new Size(240, 24),
            Font = theme.Font(10f, FontStyle.Bold),
            ForeColor = theme.Text,
            BackColor = Color.Transparent,
            AutoEllipsis = true,
            Text = "未在播放"
        };
        _artist = new Label
        {
            AutoSize = false,
            Location = new Point(124, 42),
            Size = new Size(240, 20),
            Font = theme.Font(8.4f),
            ForeColor = theme.TextTertiary,
            BackColor = Color.Transparent,
            AutoEllipsis = true
        };
        Controls.Add(_title);
        Controls.Add(_artist);

        var back = new MusicIconButton(theme, "\uE740", 9f) { Location = new Point(ClientSize.Width - 38, 6), Size = new Size(30, 28) };
        _toolTip.SetToolTip(back, "返回主窗口");
        back.Click += (_, _) => ShowMainRequested?.Invoke(this, EventArgs.Empty);
        Controls.Add(back);

        var prev = new MusicIconButton(theme, "\uE892", 10f) { Location = new Point(124, 76), Size = new Size(32, 30) };
        prev.Click += (_, _) => _player.Previous();
        _play = new MusicIconButton(theme, "\uE768", 13f) { Location = new Point(162, 74), Size = new Size(38, 34), Active = true };
        _play.Click += (_, _) => _player.TogglePlayPause();
        var next = new MusicIconButton(theme, "\uE893", 10f) { Location = new Point(206, 76), Size = new Size(32, 30) };
        next.Click += (_, _) => _player.Next();
        Controls.Add(prev);
        Controls.Add(_play);
        Controls.Add(next);

        _seek = new MusicSeekBar { Location = new Point(124, 118), Size = new Size(280, 24), Theme = theme };
        _seek.SeekRequested += (_, fraction) => _player.SeekFraction(fraction);
        Controls.Add(_seek);
        _lyricLabel = new Label
        {
            AutoSize = false,
            Location = new Point(14, 142),
            Size = new Size(400, 28),
            Font = theme.Font(8.8f),
            ForeColor = theme.Accent,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true,
            Text = "离线音乐播放器"
        };
        Controls.Add(_lyricLabel);

        foreach (Control control in new Control[] { this, _cover, _title, _artist, _lyricLabel })
        {
            control.MouseDown += DragDown;
            control.MouseMove += DragMove;
            control.MouseUp += (_, _) => _dragging = false;
        }
        Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(_theme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };

        _player.StateChanged += OnPlayerChanged;
        _player.TrackChanged += OnPlayerChanged;
        _player.PositionChanged += OnPlayerPosition;
        FormClosed += (_, _) =>
        {
            _player.StateChanged -= OnPlayerChanged;
            _player.TrackChanged -= OnPlayerChanged;
            _player.PositionChanged -= OnPlayerPosition;
            _toolTip.Dispose();
        };
        RefreshFromPlayer();
    }

    private void DragDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _dragOffset = e.Location;
    }

    private void DragMove(object? sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        Location = new Point(
            Location.X + e.X - _dragOffset.X,
            Location.Y + e.Y - _dragOffset.Y);
    }

    private void OnPlayerChanged(object? sender, EventArgs e) => RefreshFromPlayer();

    private void OnPlayerPosition(object? sender, EventArgs e)
    {
        var duration = _player.Duration.TotalSeconds;
        _seek.Value = duration > 0 ? _player.Position.TotalSeconds / duration : 0;
        UpdateLyricLine();
    }

    public void RefreshFromPlayer()
    {
        var track = _player.CurrentTrack;
        _title.Text = track?.DisplayTitle ?? "未在播放";
        _artist.Text = track == null
            ? "离线音乐播放器"
            : $"{track.DisplayArtist} · {track.DisplayAlbum}";
        _cover.CoverKey = track?.CoverKey ?? "";
        _play.Glyph = _player.IsPlaying ? "\uE769" : "\uE768";
        long trackId = track?.Id ?? -1;
        if (trackId != _lyricsTrackId)
        {
            _lyricsTrackId = trackId;
            _lyrics = track == null ? null : LrcDocument.LoadForTrack(track);
        }
        OnPlayerPosition(this, EventArgs.Empty);
    }

    private void UpdateLyricLine()
    {
        if (_lyricLabel == null)
        {
            return;
        }
        var track = _player.CurrentTrack;
        if (track == null)
        {
            _lyricLabel.Text = "离线音乐播放器";
            return;
        }
        if (_lyrics == null)
        {
            _lyricLabel.Text = "暂无歌词 · " + track.DisplayTitle;
            return;
        }
        if (!_lyrics.HasTimestamps)
        {
            string first = _lyrics.RawText
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.Length > 0 && !line.StartsWith("[", StringComparison.Ordinal))
                ?? "";
            _lyricLabel.Text = string.IsNullOrWhiteSpace(first)
                ? "暂无同步歌词"
                : first;
            return;
        }
        int index = _lyrics.IndexAt(_player.Position);
        if (index < 0) index = 0;
        var line = _lyrics.Lines[Math.Clamp(index, 0, _lyrics.Lines.Count - 1)];
        _lyricLabel.Text = line.Translation.Length > 0
            ? line.Text + "  ·  " + line.Translation
            : line.Text;
    }
}

/// <summary>桌面歌词悬浮窗（插件，默认开启）：顶部播放控制 / 返回主窗口，逐字卡拉 OK 炫彩高亮。</summary>
public sealed class DesktopLyricsForm : Form
{
    private readonly MusicPlayer _player;
    private readonly MusicSettings _settings;
    private readonly RssTheme _theme;
    private readonly ToolTip _toolTip = new();
    private LrcDocument? _lyrics;
    private long _lyricsTrackId = -1;
    private int _index = -1;
    private string _line = "离线音乐播放器";
    private string _translation = "";
    private TimeSpan _lineStart;
    private TimeSpan _lineEnd;
    private double _lineProgress = 1;
    private Font _font;
    private Font _translationFont;
    private Font _captionFont;
    private MusicIconButton _playButton;
    private bool _dragging;
    private Point _dragOffset;
    private readonly System.Windows.Forms.Timer _hoverTimer;
    private bool _expanded = true;
    private DateTime? _hoverStarted;
    private DateTime? _leaveStarted;
    private Rectangle _expandedBounds;

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    public event EventHandler? ShowMainRequested;

    public DesktopLyricsForm(MusicPlayer player, MusicSettings settings, RssTheme theme)
    {
        _player = player;
        _settings = settings;
        _theme = theme;
        Text = "离线音乐播放器 · 桌面歌词";
        FormBorderStyle = FormBorderStyle.None;
        TopMost = settings.DesktopLyricsTopmost;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(960, 140);
        AllowTransparency = true;
        BackColor = Color.Black;
        TransparencyKey = Color.Black;
        Opacity = 0.92;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw, true);
        _font = new Font("Microsoft YaHei UI", settings.DesktopLyricsFontSize,
            FontStyle.Bold, GraphicsUnit.Pixel);
        _translationFont = new Font("Microsoft YaHei UI",
            Math.Max(14, settings.DesktopLyricsFontSize * 0.6f), FontStyle.Regular, GraphicsUnit.Pixel);
        _captionFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Location = new Point((screen.Width - Width) / 2, screen.Bottom - Height - 70);
        _expandedBounds = Bounds;

        var menu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.System };
        menu.Items.Add("返回主窗口", null, (_, _) => ShowMainRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("关闭桌面歌词", null, (_, _) => Close());
        ContextMenuStrip = menu;

        int controlX = ClientSize.Width / 2 - 50;
        var controlTheme = RssTheme.Dark;
        var previous = new MusicIconButton(controlTheme, "\uE892", 9f)
        {
            Location = new Point(controlX, 3),
            Size = new Size(32, 26)
        };
        _toolTip.SetToolTip(previous, "上一首");
        previous.Click += (_, _) => _player.Previous();
        _playButton = new MusicIconButton(controlTheme, "\uE768", 11f)
        {
            Location = new Point(controlX + 34, 3),
            Size = new Size(32, 26)
        };
        _toolTip.SetToolTip(_playButton, "播放 / 暂停");
        _playButton.Click += (_, _) => _player.TogglePlayPause();
        var next = new MusicIconButton(controlTheme, "\uE893", 9f)
        {
            Location = new Point(controlX + 68, 3),
            Size = new Size(32, 26)
        };
        _toolTip.SetToolTip(next, "下一首");
        next.Click += (_, _) => _player.Next();
        var back = new MusicIconButton(controlTheme, "\uE740", 9f)
        {
            Location = new Point(ClientSize.Width - 76, 3),
            Size = new Size(32, 26)
        };
        _toolTip.SetToolTip(back, "返回主窗口");
        back.Click += (_, _) => ShowMainRequested?.Invoke(this, EventArgs.Empty);
        var close = new MusicIconButton(controlTheme, "\uE8BB", 9.5f)
        {
            Location = new Point(ClientSize.Width - 40, 3),
            Size = new Size(32, 26)
        };
        _toolTip.SetToolTip(close, "关闭桌面歌词");
        close.Click += (_, _) => Close();
        Controls.Add(previous);
        Controls.Add(_playButton);
        Controls.Add(next);
        Controls.Add(back);
        Controls.Add(close);
        foreach (var button in new[] { previous, _playButton, next, back, close })
        {
            button.BackColor = Color.FromArgb(20, 22, 28);
        }

        MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && _expanded)
            {
                _dragging = true;
                _dragOffset = e.Location;
            }
        };
        MouseMove += (_, e) =>
        {
            Cursor = _expanded ? Cursors.SizeAll : Cursors.Default;
            if (!_dragging || !_expanded) return;
            Location = new Point(Location.X + e.X - _dragOffset.X,
                Location.Y + e.Y - _dragOffset.Y);
        };
        MouseUp += (_, _) =>
        {
            if (_dragging)
            {
                _dragging = false;
                _expandedBounds = Bounds;
            }
        };
        DoubleClick += (_, _) =>
        {
            if (!_expanded)
            {
                ExpandBar();
            }
        };
        Paint += PaintLyrics;
        _hoverTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _hoverTimer.Tick += OnHoverTick;

        _player.TrackChanged += OnTrackChanged;
        _player.PositionChanged += OnPositionChanged;
        _player.StateChanged += OnStateChanged;
        FormClosed += (_, _) =>
        {
            _hoverTimer.Stop();
            _hoverTimer.Dispose();
            _player.TrackChanged -= OnTrackChanged;
            _player.PositionChanged -= OnPositionChanged;
            _player.StateChanged -= OnStateChanged;
            _font.Dispose();
            _translationFont.Dispose();
            _captionFont.Dispose();
            _toolTip.Dispose();
        };
        RefreshFromPlayer();
        EnterCompactMode();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE：不抢输入焦点
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _hoverTimer.Start();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (_hoverTimer == null)
        {
            return;
        }
        if (Visible)
        {
            _hoverTimer.Start();
            if (!_expanded)
            {
                UpdateCompactBounds();
            }
        }
        else
        {
            _hoverTimer.Stop();
        }
    }

    private void OnHoverTick(object? sender, EventArgs e)
    {
        if (!Visible || _dragging)
        {
            return;
        }
        var now = DateTime.UtcNow;
        Point cursor = Cursor.Position;
        if (!_expanded)
        {
            if (Bounds.Contains(cursor))
            {
                _hoverStarted ??= now;
                if ((now - _hoverStarted.Value).TotalMilliseconds >= 1000)
                {
                    ExpandBar();
                }
            }
            else
            {
                _hoverStarted = null;
            }
        }
        else
        {
            if (Bounds.Contains(cursor))
            {
                _leaveStarted = null;
            }
            else
            {
                _leaveStarted ??= now;
                if ((now - _leaveStarted.Value).TotalMilliseconds >= 500)
                {
                    EnterCompactMode();
                }
            }
        }
    }

    private void SetChromeVisible(bool visible)
    {
        foreach (Control control in Controls)
        {
            control.Visible = visible;
        }
    }

    private void ExpandBar()
    {
        _expanded = true;
        _hoverStarted = null;
        _leaveStarted = null;
        SetChromeVisible(true);
        Bounds = _expandedBounds;
        Invalidate();
    }

    private void EnterCompactMode()
    {
        _expanded = false;
        _hoverStarted = null;
        _leaveStarted = null;
        SetChromeVisible(false);
        UpdateCompactBounds();
        Invalidate();
        if (IsHandleCreated)
        {
            SetFocus(IntPtr.Zero);
        }
    }

    private void UpdateCompactBounds()
    {
        if (_expanded || !IsHandleCreated && Bounds.Width <= 0)
        {
            return;
        }
        string text = string.IsNullOrWhiteSpace(_line) ? "♪" : _line;
        Size textSize = TextRenderer.MeasureText(text, _font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        var screen = Screen.FromRectangle(_expandedBounds).WorkingArea;
        if (screen.Width <= 0 || screen.Height <= 0)
        {
            screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        }
        int maxWidth = Math.Max(180, screen.Width - 60);
        int width = Math.Clamp(textSize.Width + 36, 120, maxWidth);
        int height = Math.Max(52, textSize.Height + 24);
        int centerX = _expandedBounds.Left + _expandedBounds.Width / 2;
        int centerY = _expandedBounds.Top + 67;
        int x = Math.Clamp(centerX - width / 2, screen.Left, Math.Max(screen.Left, screen.Right - width));
        int y = Math.Clamp(centerY - height / 2, screen.Top, Math.Max(screen.Top, screen.Bottom - height));
        Bounds = new Rectangle(x, y, width, height);
    }

    private void OnTrackChanged(object? sender, EventArgs e) => RefreshFromPlayer();

    private void OnStateChanged(object? sender, EventArgs e)
    {
        UpdatePlayGlyph();
        Invalidate();
    }

    private void OnPositionChanged(object? sender, EventArgs e)
    {
        UpdatePlayGlyph();
        if (_lyrics is { HasTimestamps: true })
        {
            UpdateSyncedLine(_player.Position);
        }
        Invalidate();
    }

    private void UpdatePlayGlyph()
    {
        if (_playButton == null) return;
        _playButton.Glyph = _player.IsPlaying ? "\uE769" : "\uE768";
        _playButton.Active = _player.IsPlaying;
    }

    public void RefreshFromPlayer()
    {
        var track = _player.CurrentTrack;
        if (track == null)
        {
            _lyrics = null;
            _lyricsTrackId = -1;
            _line = "离线音乐播放器";
            _translation = "";
            _lineProgress = 1;
            UpdatePlayGlyph();
            if (!_expanded)
            {
                UpdateCompactBounds();
            }
            Invalidate();
            return;
        }
        if (track.Id != _lyricsTrackId)
        {
            _lyricsTrackId = track.Id;
            _lyrics = LrcDocument.LoadForTrack(track);
            _index = -1;
        }
        if (_lyrics == null)
        {
            _line = track.DisplayTitle;
            _translation = "暂无歌词（将同名 .lrc 文件放在歌曲旁）";
            _lineProgress = 1;
        }
        else if (!_lyrics.HasTimestamps)
        {
            string first = _lyrics.RawText
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.Length > 0 && !line.StartsWith("[", StringComparison.Ordinal))
                ?? "";
            _line = string.IsNullOrWhiteSpace(first) ? track.DisplayTitle : first;
            _translation = "未同步歌词";
            _lineProgress = 1;
        }
        else
        {
            UpdateSyncedLine(_player.Position);
        }
        UpdatePlayGlyph();
        if (!_expanded)
        {
            UpdateCompactBounds();
        }
        Invalidate();
    }

    private void UpdateSyncedLine(TimeSpan position)
    {
        if (_lyrics == null || _lyrics.Lines.Count == 0)
        {
            return;
        }
        int index = _lyrics.IndexAt(position);
        if (index != _index)
        {
            _index = index;
            if (index >= 0 && index < _lyrics.Lines.Count)
            {
                var line = _lyrics.Lines[index];
                _line = line.Text;
                _translation = line.Translation;
                _lineStart = line.Time;
                _lineEnd = index + 1 < _lyrics.Lines.Count
                    ? _lyrics.Lines[index + 1].Time
                    : line.Time + TimeSpan.FromSeconds(6);
            }
            else
            {
                _line = _player.CurrentTrack?.DisplayTitle ?? "离线音乐播放器";
                _translation = "";
                _lineStart = TimeSpan.Zero;
                _lineEnd = TimeSpan.FromSeconds(1);
            }
            if (!_expanded)
            {
                UpdateCompactBounds();
            }
        }
        double total = Math.Max(0.001, (_lineEnd - _lineStart).TotalSeconds);
        _lineProgress = Math.Clamp((position - _lineStart).TotalSeconds / total, 0, 1);
    }

    private void PaintLyrics(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle area;
        if (_expanded)
        {
            using (var brush = new SolidBrush(Color.FromArgb(20, 22, 28)))
            {
                g.FillRectangle(brush, ClientRectangle);
            }
            TextRenderer.DrawText(g, "桌面歌词", _captionFont,
                new Rectangle(12, 4, 120, 24), Color.FromArgb(150, 160, 220, 255),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            area = new Rectangle(20, 32, Width - 40, Height - 70);
        }
        else
        {
            area = new Rectangle(8, 6, Width - 16, Height - 12);
        }

        string text = string.IsNullOrWhiteSpace(_line) ? "♪" : _line;
        double progress = _lyrics is { HasTimestamps: true } ? _lineProgress : 1;
        DrawKaraokeText(g, text, area, progress, _settings.DesktopLyricsFontSize, _font);

        if (_expanded)
        {
            if (!string.IsNullOrWhiteSpace(_translation))
            {
                var transRect = new Rectangle(20, Height - 36, Width - 40, 28);
                TextRenderer.DrawText(g, _translation, _translationFont, transRect,
                    Color.FromArgb(225, 150, 225, 255),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
            using var pen = new Pen(Color.FromArgb(36, 255, 255, 255));
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    private void DrawKaraokeText(Graphics g, string text, Rectangle area, double progress,
        float basePixelSize, Font baseFont)
    {
        const TextFormatFlags measureFlags = TextFormatFlags.NoPadding
            | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        Size total = TextRenderer.MeasureText(text, baseFont, Size.Empty, measureFlags);
        Font font = baseFont;
        bool ownFont = false;
        if (total.Width > area.Width && total.Width > 0)
        {
            float scale = Math.Max(0.35f, area.Width / (float)total.Width);
            font = new Font("Microsoft YaHei UI", Math.Max(10f, basePixelSize * scale),
                FontStyle.Bold, GraphicsUnit.Pixel);
            ownFont = true;
            total = TextRenderer.MeasureText(text, font, Size.Empty, measureFlags);
        }
        int startX = area.X + Math.Max(0, (area.Width - total.Width) / 2);
        int startY = area.Y + Math.Max(0, (area.Height - total.Height) / 2);
        var shadowFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(g, text, font,
            new Rectangle(area.X + 2, area.Y + 3, area.Width, area.Height),
            Color.FromArgb(190, 8, 10, 16), shadowFlags);

        var elements = new List<string>();
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            elements.Add(enumerator.GetTextElement());
        }
        int count = Math.Max(1, elements.Count);
        int x = startX;
        double time = _player.Position.TotalSeconds;
        for (int i = 0; i < elements.Count; i++)
        {
            string element = elements[i];
            bool lit = progress >= (i + 0.65) / count;
            Color color = lit
                ? Rainbow(i, count, time)
                : Color.FromArgb(150, 245, 245, 245);
            TextRenderer.DrawText(g, element, font, new Point(x, startY), color,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding
                | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            x += TextRenderer.MeasureText(element, font, Size.Empty, measureFlags).Width;
        }
        if (ownFont)
        {
            font.Dispose();
        }
    }

    private static Color Rainbow(int index, int count, double timeSeconds)
    {
        double hue = (index * 360.0 / Math.Max(1, count) + timeSeconds * 55.0) % 360.0;
        return HsvToColor(hue, 0.82, 1.0);
    }

    private static Color HsvToColor(double hue, double saturation, double value)
    {
        double c = value * saturation;
        double x = c * (1 - Math.Abs((hue / 60.0) % 2 - 1));
        double m = value - c;
        double r = 0, g = 0, b = 0;
        if (hue < 60) { r = c; g = x; }
        else if (hue < 120) { r = x; g = c; }
        else if (hue < 180) { g = c; b = x; }
        else if (hue < 240) { g = x; b = c; }
        else if (hue < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return Color.FromArgb(
            (int)Math.Round((r + m) * 255),
            (int)Math.Round((g + m) * 255),
            (int)Math.Round((b + m) * 255));
    }
}
