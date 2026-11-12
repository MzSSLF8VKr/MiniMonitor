using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text.Json;
using MiniMonitor.Rss;

namespace MiniMonitor.Music;

/// <summary>
/// 离线音乐播放器主窗口。默认最大化显示，三栏布局：资料库 / 曲目列表 / 正在播放。
/// 联网能力默认关闭；迷你播放器、桌面歌词插件默认开启。
/// </summary>
public sealed partial class MusicPlayerForm : Form
{
    private readonly AppConfig _config;
    private readonly MusicSettings _settings;
    private readonly MusicLibrary _library;
    private readonly MusicScanner _scanner;
    private readonly MusicPlayer _player;
    private DlnaMediaServer? _dlna;

    private RssTheme _theme;
    private Panel _titleBar = null!;
    private Panel _contentHeader = null!;
    private Panel _contentBody = null!;
    private Panel _centerPanel = null!;
    private Panel _nowPlayingPanel = null!;
    private Panel _playerBar = null!;
    private Panel _scanPanel = null!;
    private MusicSidebar _sidebar = null!;
    private MusicTrackList _trackList = null!;
    private MusicCardPanel _cardPanel = null!;
    private Control? _currentPage;
    private Label _viewTitle = null!;
    private Label _viewSubtitle = null!;
    private FlowLayoutPanel _headerActions = null!;
    private MusicIconButton _backButton = null!;
    private TextBox _search = null!;
    private Panel _searchHost = null!;
    private System.Windows.Forms.Timer _searchTimer = null!;
    private System.Windows.Forms.Timer _toastTimer = null!;
    private Label _toastLabel = null!;
    private Label _scanLabel = null!;
    private ProgressBar _scanBar = null!;
    private ContextMenuStrip _trackMenu = null!;

    private MusicView _view = MusicView.Songs;
    private string? _viewArg;
    private MusicQuery _currentQuery = new();
    private string _searchText = "";
    private string _sortColumn = "title";
    private bool _sortDescending;
    private bool _drilled;
    private string _drillTitle = "";
    private MusicQuery? _drillQuery;
    private string _drillSubtitle = "";
    private bool _allowClose;
    private CancellationTokenSource? _scanCts;
    private bool _restoredWindow;
    private bool _initialScanDone;
    private bool _cleanupRunning;
    private MusicStatsPage? _statsPage;
    private readonly ToolTip _toolTip = new();
    private MusicIconButton _titleMiniButton = null!;
    private MusicIconButton _titleLyricsButton = null!;
    private bool _desktopLyricsInitialized;


    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

    public MusicPlayerForm(AppConfig config)
    {
        _config = config;
        _settings = config.Music;
        _library = new MusicLibrary();
        _scanner = new MusicScanner(_library);
        _player = new MusicPlayer(_settings);
        _theme = RssTheme.Create(_settings.Theme);

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw, true);
        Text = "离线音乐播放器";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 700);
        BackColor = _theme.Background;
        KeyPreview = true;
        DoubleBuffered = true;
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            // 图标缺失不影响使用
        }

        BuildUi();
        BindPlayerEvents();
        BuildTrackContextMenu();
        ResizeEnd += (_, _) =>
        {
            if (WindowState == FormWindowState.Normal && Visible)
            {
                SaveWindowState();
            }
        };
        _searchTimer = new System.Windows.Forms.Timer { Interval = 260 };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            ApplySearch();
        };
        _toastTimer = new System.Windows.Forms.Timer { Interval = 4200 };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            _toastLabel.Visible = false;
        };
        RefreshSidebar();
        ShowView(ParseView(_settings.LastView));
        ApplyPlaybackSettings();
        ApplyNetworkServices();
    }

    // ================= 窗口 =================

    public void ShowReader()
    {
        if (_allowClose || IsDisposed)
        {
            return;
        }
        bool wasVisible = Visible;
        if (!wasVisible)
        {
            Show();
        }
        if (_miniPlayer is { Visible: true })
        {
            _miniPlayer.Hide();
        }
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }
        if (!wasVisible)
        {
            ApplySavedWindowState();
        }
        Activate();
        BringToFront();
        RefreshSidebar();
        if (_trackList.Tracks.Count == 0 && _cardPanel.Controls.Count == 0)
        {
            ShowView(_view, _viewArg);
        }
    }

    public void CloseForApp()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplySavedWindowState();
        try
        {
            SendMessage(_search.Handle, 0x1501, (IntPtr)1,
                "搜索歌曲、专辑、艺术家…（Ctrl+F）");
        }
        catch
        {
            // 占位提示失败不影响使用
        }
        if (!_restoredWindow)
        {
            _restoredWindow = true;
            RestoreQueue();
            RefreshSidebar();
        }
        if (!_initialScanDone)
        {
            _initialScanDone = true;
            if (_settings.ScanOnOpen && Directory.Exists(_settings.EffectiveLibraryPath))
            {
                long lastScan = 0;
                long.TryParse(_library.GetMeta("last_scan"), out lastScan);
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (_library.GetAllTracks().Count == 0 || now - lastScan > 300)
                {
                    StartScan(interactive: false);
                }
            }
        }
        if (!_desktopLyricsInitialized)
        {
            _desktopLyricsInitialized = true;
            if (_settings.DesktopLyricsAvailable && _settings.DesktopLyricsVisible)
            {
                SetDesktopLyricsVisible(true);
            }
        }
    }

    private void ApplySavedWindowState()
    {
        var primary = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        if (_settings.WindowMaximized || !_settings.WindowShown)
        {
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
            if (MaximizedBounds.Width <= 0)
            {
                MaximizedBounds = primary;
            }
            WindowState = FormWindowState.Maximized;
            if (!_settings.WindowShown)
            {
                _settings.WindowMaximized = true;
            }
        }
        else
        {
            WindowState = FormWindowState.Normal;
            int minWidth = Math.Min(MinimumSize.Width, primary.Width);
            int minHeight = Math.Min(MinimumSize.Height, primary.Height);
            int width = Math.Clamp(_settings.WindowWidth, minWidth, primary.Width);
            int height = Math.Clamp(_settings.WindowHeight, minHeight, primary.Height);
            int x = _settings.WindowX ?? (primary.Left + (primary.Width - width) / 2);
            int y = _settings.WindowY ?? (primary.Top + (primary.Height - height) / 2);
            var area = Screen.FromRectangle(new Rectangle(x, y, width, height)).WorkingArea;
            if (area.Width <= 0 || area.Height <= 0)
            {
                area = primary;
            }
            width = Math.Min(width, area.Width);
            height = Math.Min(height, area.Height);
            x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - width));
            y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - height));
            Bounds = new Rectangle(x, y, width, height);
        }
        _settings.WindowShown = true;
        _config.Save();
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84;
        if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
        {
            int screenX = unchecked((short)(long)m.LParam);
            int screenY = unchecked((short)((long)m.LParam >> 16));
            Point point = PointToClient(new Point(screenX, screenY));
            int grip = Dpi(6);
            bool left = point.X <= grip;
            bool right = point.X >= ClientSize.Width - grip;
            bool top = point.Y <= grip;
            bool bottom = point.Y >= ClientSize.Height - grip;
            int hit = 0;
            if (top && left) hit = 13;          // HTTOPLEFT
            else if (top && right) hit = 14;    // HTTOPRIGHT
            else if (bottom && left) hit = 16;  // HTBOTTOMLEFT
            else if (bottom && right) hit = 17; // HTBOTTOMRIGHT
            else if (left) hit = 10;            // HTLEFT
            else if (right) hit = 11;           // HTRIGHT
            else if (top) hit = 12;             // HTTOP
            else if (bottom) hit = 15;          // HTBOTTOM
            if (hit != 0)
            {
                m.Result = (IntPtr)hit;
                return;
            }
        }
        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            SaveWindowState();
            Hide();
            return;
        }
        SaveQueue();
        SaveWindowState();
        _searchTimer?.Stop();
        _toastTimer?.Stop();
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = null;
        _player.Dispose();
        _dlna?.Dispose();
        _dlna = null;
        _miniPlayer?.Close();
        _desktopLyrics?.Close();
        _toolTip.Dispose();
        base.OnFormClosing(e);
    }

    private void SaveWindowState()
    {
        try
        {
            _settings.LastView = ViewKey();
            if (!Visible || !IsHandleCreated)
            {
                _config.Save();
                return;
            }
            _settings.WindowMaximized = WindowState == FormWindowState.Maximized;
            if (WindowState == FormWindowState.Normal
                && Width >= MinimumSize.Width && Height >= MinimumSize.Height)
            {
                _settings.WindowWidth = Width;
                _settings.WindowHeight = Height;
                _settings.WindowX = Location.X;
                _settings.WindowY = Location.Y;
            }
            _config.Save();
        }
        catch
        {
            // 配置写入失败不影响退出
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!(_search.Focused))
        {
            if (e.KeyCode == Keys.Space)
            {
                TogglePlayPause();
                e.Handled = true;
                return;
            }
            if (e.Control && e.KeyCode == Keys.Left)
            {
                _player.Previous();
                e.Handled = true;
                return;
            }
            if (e.Control && e.KeyCode == Keys.Right)
            {
                _player.Next();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.F5)
            {
                StartScan();
                e.Handled = true;
                return;
            }
        }
        if (e.Control && e.KeyCode == Keys.F)
        {
            _search.Focus();
            _search.SelectAll();
            e.Handled = true;
            return;
        }
        if (e.KeyCode == Keys.Escape && _search.Text.Length > 0)
        {
            _search.Clear();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private void ToggleMaximize()
    {
        MaximizedBounds = Screen.FromControl(this).WorkingArea;
        if (WindowState == FormWindowState.Maximized)
        {
            WindowState = FormWindowState.Normal;
        }
        else
        {
            WindowState = FormWindowState.Maximized;
        }
        _settings.WindowMaximized = WindowState == FormWindowState.Maximized;
        _config.Save();
    }

    // ================= 界面构建 =================

    private void BuildUi()
    {
        SuspendLayout();
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = _theme.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi(46)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi(92)));
        _titleBar = BuildTitleBar();
        root.Controls.Add(_titleBar, 0, 0);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = _theme.Background
        };
        _contentLayout = content;
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Dpi(222)));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Dpi(326)));
        _sidebar = new MusicSidebar { Dock = DockStyle.Fill, Theme = _theme };
        _sidebar.ItemActivated += (_, item) => OnSidebarItem(item);
        content.Controls.Add(_sidebar, 0, 0);

        _centerPanel = new Panel { Dock = DockStyle.Fill, BackColor = _theme.Surface };
        var centerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            BackColor = _theme.Surface
        };
        centerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi(86)));
        centerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        centerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi(34)));
        _contentHeader = BuildContentHeader();
        centerLayout.Controls.Add(_contentHeader, 0, 0);
        _contentBody = new Panel { Dock = DockStyle.Fill, BackColor = _theme.Surface };
        centerLayout.Controls.Add(_contentBody, 0, 1);
        _scanPanel = BuildScanPanel();
        centerLayout.Controls.Add(_scanPanel, 0, 2);
        _centerPanel.Controls.Add(centerLayout);
        content.Controls.Add(_centerPanel, 1, 0);

        _nowPlayingPanel = BuildNowPlayingPanel();
        content.Controls.Add(_nowPlayingPanel, 2, 0);
        root.Controls.Add(content, 0, 1);

        _playerBar = BuildPlayerBar();
        root.Controls.Add(_playerBar, 0, 2);
        Controls.Add(root);

        _trackList = new MusicTrackList { Dock = DockStyle.Fill, Theme = _theme };
        _trackList.TrackActivated += (_, track) => PlayTrack(track);
        _trackList.LikeToggled += (_, track) => ToggleLike(track);
        _trackList.SortRequested += (_, request) =>
        {
            _sortColumn = request.Column;
            _sortDescending = request.Descending;
            ReloadCurrentView();
        };
        _trackList.SelectionChanged += (_, _) => UpdateLikeButton();

        _cardPanel = new MusicCardPanel { Dock = DockStyle.Fill, SurfaceColor = _theme.Surface };

        _toastLabel = new Label
        {
            AutoSize = false,
            Height = 34,
            BackColor = Color.FromArgb(230, 24, 35, 52),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 9f),
            Visible = false,
            Anchor = AnchorStyles.None
        };
        Controls.Add(_toastLabel);
        _toastLabel.BringToFront();

        ResumeLayout();
    }

    private Panel BuildTitleBar()
    {
        var bar = new Panel { Dock = DockStyle.Fill, BackColor = _theme.Surface };
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(_theme.Border);
            e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
        };
        bar.MouseDown += TitleBarMouseDown;
        bar.MouseMove += TitleBarMouseMove;
        bar.MouseUp += (_, _) => _draggingWindow = false;
        bar.MouseDoubleClick += (_, _) => ToggleMaximize();

        var logo = new PictureBox
        {
            Size = new Size(28, 28),
            Location = new Point(16, 9),
            BackColor = Color.Transparent
        };
        logo.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(1, 1, 26, 26);
            using var path = ShutdownTheme.RoundRect(rect, 7f);
            using var brush = new LinearGradientBrush(rect, _theme.Accent,
                Color.FromArgb(255, 128, 200, 255), 45f);
            e.Graphics.FillPath(brush, path);
            using var font = _theme.IconFont(13f);
            TextRenderer.DrawText(e.Graphics, "\uE8D6", font,
                new Rectangle(1, 1, 26, 26), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        };
        bar.Controls.Add(logo);
        var title = new Label
        {
            Text = "离线音乐播放器",
            AutoSize = false,
            Location = new Point(52, 6),
            Size = new Size(180, 24),
            Font = _theme.Font(11.5f, FontStyle.Bold),
            ForeColor = _theme.Text,
            BackColor = Color.Transparent
        };
        var subtitle = new Label
        {
            Text = "离线 · 本地音乐库",
            AutoSize = false,
            Location = new Point(53, 27),
            Size = new Size(160, 16),
            Font = _theme.Font(7.8f),
            ForeColor = _theme.TextTertiary,
            BackColor = Color.Transparent
        };
        bar.Controls.Add(title);
        bar.Controls.Add(subtitle);
        foreach (Control dragControl in new Control[] { logo, title, subtitle })
        {
            dragControl.MouseDown += TitleBarMouseDown;
            dragControl.MouseMove += TitleBarMouseMove;
            dragControl.MouseUp += (_, _) => _draggingWindow = false;
            dragControl.MouseDoubleClick += (_, _) => ToggleMaximize();
        }

        _searchHost = new Panel
        {
            Height = 30,
            Width = 420,
            BackColor = _theme.SurfaceAlt,
            Anchor = AnchorStyles.Top
        };
        _searchHost.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = ShutdownTheme.RoundRect(
                new RectangleF(0.5f, 0.5f, _searchHost.Width - 1, _searchHost.Height - 1), 7f);
            using var pen = new Pen(_theme.Border);
            e.Graphics.DrawPath(pen, path);
            using var font = _theme.IconFont(9.5f);
            TextRenderer.DrawText(e.Graphics, "\uE721", font,
                new Rectangle(8, 0, 22, _searchHost.Height), _theme.TextTertiary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        };
        _search = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = _theme.SurfaceAlt,
            ForeColor = _theme.Text,
            Font = _theme.Font(9.4f),
            Location = new Point(32, 6),
            Width = 370
        };
        _search.TextChanged += (_, _) =>
        {
            if (_searchTimer == null)
            {
                return;
            }
            _searchTimer.Stop();
            _searchTimer.Start();
        };
        _searchHost.Controls.Add(_search);
        bar.Controls.Add(_searchHost);
        bar.Resize += (_, _) =>
        {
            int width = Math.Min(460, Math.Max(260, bar.Width - 900));
            _searchHost.Width = width;
            _searchHost.Location = new Point((bar.Width - width) / 2, 8);
            _search.Width = width - 42;
        };

        var minimize = new MusicIconButton(_theme, "\uE921", 10f) { Size = new Size(42, 34) };
        _toolTip.SetToolTip(minimize, "最小化");
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        _titleMiniButton = new MusicIconButton(_theme, "\uE8A4", 10f) { Size = new Size(42, 34) };
        _toolTip.SetToolTip(_titleMiniButton, "切换到迷你播放器");
        _titleMiniButton.Click += (_, _) => ToggleMiniPlayer();
        _titleLyricsButton = new MusicIconButton(_theme, "\uE8A5", 10f) { Size = new Size(42, 34) };
        _toolTip.SetToolTip(_titleLyricsButton, "开启 / 关闭桌面歌词");
        _titleLyricsButton.Click += (_, _) => ToggleDesktopLyrics();
        var maximize = new MusicIconButton(_theme, "\uE922", 10f) { Size = new Size(42, 34) };
        _toolTip.SetToolTip(maximize, "最大化 / 还原");
        maximize.Click += (_, _) => ToggleMaximize();
        var close = new MusicIconButton(_theme, "\uE8BB", 11.5f) { Size = new Size(42, 34) };
        _toolTip.SetToolTip(close, "关闭到托盘");
        close.Click += (_, _) => Close();
        var windowButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 236,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 4, 14, 0)
        };
        windowButtons.Controls.Add(_titleMiniButton);
        windowButtons.Controls.Add(_titleLyricsButton);
        windowButtons.Controls.Add(minimize);
        windowButtons.Controls.Add(maximize);
        windowButtons.Controls.Add(close);
        bar.Controls.Add(windowButtons);
        return bar;
    }

    private bool _draggingWindow;
    private Point _dragStart;
    private Rectangle _restoreBounds;

    private void TitleBarMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }
        _draggingWindow = true;
        _dragStart = Cursor.Position;
        if (WindowState == FormWindowState.Maximized)
        {
            _restoreBounds = Bounds;
        }
    }

    private void TitleBarMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_draggingWindow || e.Button != MouseButtons.Left)
        {
            return;
        }
        Point cursor = Cursor.Position;
        if (WindowState == FormWindowState.Maximized)
        {
            double ratio = Math.Clamp((cursor.X - Location.X) / (double)Math.Max(1, Width), 0.1, 0.9);
            WindowState = FormWindowState.Normal;
            Width = _settings.WindowWidth;
            Height = _settings.WindowHeight;
            Location = new Point(
                Math.Max(0, cursor.X - (int)(Width * ratio)),
                Math.Max(0, cursor.Y - 20));
            _dragStart = cursor;
            return;
        }
        Location = new Point(
            Location.X + cursor.X - _dragStart.X,
            Location.Y + cursor.Y - _dragStart.Y);
        _dragStart = cursor;
    }

    private Panel BuildContentHeader()
    {
        var header = new Panel { Dock = DockStyle.Fill, BackColor = _theme.Surface };
        _backButton = new MusicIconButton(_theme, "\uE72B", 10f)
        {
            Location = new Point(14, 24),
            Size = new Size(34, 34),
            Visible = false
        };
        _backButton.Click += (_, _) =>
        {
            _drilled = false;
            ShowView(_view, _viewArg);
        };
        header.Controls.Add(_backButton);
        _viewTitle = new Label
        {
            AutoSize = false,
            Location = new Point(22, 15),
            Size = new Size(380, 32),
            Font = _theme.Font(15.5f, FontStyle.Bold),
            ForeColor = _theme.Text,
            BackColor = Color.Transparent,
            Text = "全部歌曲"
        };
        _viewSubtitle = new Label
        {
            AutoSize = false,
            Location = new Point(24, 48),
            Size = new Size(380, 22),
            Font = _theme.Font(8.8f),
            ForeColor = _theme.TextTertiary,
            BackColor = Color.Transparent
        };
        header.Controls.Add(_viewTitle);
        header.Controls.Add(_viewSubtitle);
        _headerActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 26, 18, 0)
        };
        header.Controls.Add(_headerActions);
        header.Resize += (_, _) =>
        {
            int actionsWidth = _headerActions.Width > 0 ? _headerActions.Width : 0;
            int available = Math.Max(160, header.Width - actionsWidth - 30);
            _viewTitle.Width = available;
            _viewSubtitle.Width = available;
        };
        return header;
    }

    private Panel BuildScanPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = _theme.Surface, Visible = false };
        _scanLabel = new Label
        {
            AutoSize = false,
            Location = new Point(20, 6),
            Size = new Size(420, 22),
            Font = _theme.Font(8.4f),
            ForeColor = _theme.TextSecondary,
            BackColor = Color.Transparent
        };
        _scanBar = new ProgressBar
        {
            Location = new Point(0, 0),
            Height = 4,
            Dock = DockStyle.Bottom,
            Style = ProgressBarStyle.Continuous
        };
        panel.Controls.Add(_scanLabel);
        panel.Controls.Add(_scanBar);
        return panel;
    }

    // ================= 侧栏与视图 =================

    private void RefreshSidebar()
    {
        var all = _library.GetAllTracks();
        int liked = all.Count(t => t.Liked);
        int perfect = all.Count(t => t.IsPerfect);
        int albums = _library.GetAlbums().Count;
        int artists = _library.GetArtists().Count;
        int genres = _library.GetGenres().Count;
        int folders = all.Select(t => t.Folder).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var items = new List<MusicNavItem>
        {
            new("songs", "全部歌曲", "\uE8FD", "资料库", all.Count),
            new("albums", "专辑", "\uE93C", "资料库", albums),
            new("artists", "艺术家", "\uE77B", "资料库", artists),
            new("genres", "流派", "\uE8EC", "资料库", genres),
            new("folders", "文件夹", "\uE8B7", "资料库", folders),
            new("liked", "喜欢音乐", "\uE735", "资料库", liked)
        };
        if (_settings.PerfectMusicExists || perfect > 0)
        {
            items.Add(new MusicNavItem("perfect", "PerfectMusic", "\uE734", "资料库", perfect,
                false, true));
        }
        items.Add(new MusicNavItem("recent", "最近添加", "\uE81C", "智能列表"));
        items.Add(new MusicNavItem("mostplayed", "最常播放", "\uE9D9", "智能列表"));
        items.Add(new MusicNavItem("neverplayed", "从未播放", "\uE81D", "智能列表"));
        items.Add(new MusicNavItem("duplicates", "重复项", "\uE8C8", "智能列表"));
        items.Add(new MusicNavItem("missing", "缺失文件", "\uE7BA", "智能列表"));
        items.Add(new MusicNavItem("newplaylist", "新建播放列表", "\uE710", "播放列表", -1, true));
        foreach (var playlist in _library.GetPlaylists())
        {
            items.Add(new MusicNavItem($"playlist:{playlist.Id}", playlist.Name, "\uE8FD",
                "播放列表", playlist.TrackCount));
        }
        items.Add(new MusicNavItem("stats", "听歌统计", "\uE9D2", "其他"));
        items.Add(new MusicNavItem("plugins", "插件", "\uE74C", "其他"));
        items.Add(new MusicNavItem("settings", "设置", "\uE713", "其他"));
        _sidebar.SelectedKey = ViewKey();
        _sidebar.SetItems(items);
    }

    private string ViewKey()
    {
        return _view switch
        {
            MusicView.Songs => "songs",
            MusicView.Albums => "albums",
            MusicView.Artists => "artists",
            MusicView.Genres => "genres",
            MusicView.Folders => "folders",
            MusicView.Liked => "liked",
            MusicView.Perfect => "perfect",
            MusicView.Recent => "recent",
            MusicView.MostPlayed => "mostplayed",
            MusicView.NeverPlayed => "neverplayed",
            MusicView.Duplicates => "duplicates",
            MusicView.Missing => "missing",
            MusicView.Playlist => $"playlist:{_viewArg}",
            MusicView.Stats => "stats",
            MusicView.Plugins => "plugins",
            MusicView.Settings => "settings",
            _ => "songs"
        };
    }

    private void OnSidebarItem(MusicNavItem item)
    {
        if (item.Key == "newplaylist")
        {
            CreatePlaylist();
            return;
        }
        if (item.Key.StartsWith("playlist:", StringComparison.Ordinal))
        {
            ShowView(MusicView.Playlist, item.Key["playlist:".Length..]);
            return;
        }
        var view = ParseView(item.Key);
        ShowView(view);
    }

    private static MusicView ParseView(string key) => key switch
    {
        "albums" => MusicView.Albums,
        "artists" => MusicView.Artists,
        "genres" => MusicView.Genres,
        "folders" => MusicView.Folders,
        "liked" => MusicView.Liked,
        "perfect" => MusicView.Perfect,
        "recent" => MusicView.Recent,
        "mostplayed" => MusicView.MostPlayed,
        "neverplayed" => MusicView.NeverPlayed,
        "duplicates" => MusicView.Duplicates,
        "missing" => MusicView.Missing,
        "stats" => MusicView.Stats,
        "plugins" => MusicView.Plugins,
        "settings" => MusicView.Settings,
        "playlist" => MusicView.Playlist,
        _ => MusicView.Songs
    };

    private void ShowView(MusicView view, string? arg = null)
    {
        _view = view;
        _viewArg = arg;
        _drilled = false;
        _drillQuery = null;
        _sidebar.SelectedKey = ViewKey();
        _backButton.Visible = false;
        _viewTitle.Left = 22;
        foreach (Control action in _headerActions.Controls.Cast<Control>().ToArray())
        {
            _headerActions.Controls.Remove(action);
            action.Dispose();
        }
        switch (view)
        {
            case MusicView.Songs:
                AddHeaderAction("扫描音乐", "\uE895", () => StartScan());
                AddHeaderAction("播放全部", "\uE768", () => PlayAllVisible());
                LoadTrackView("全部歌曲", "本地音乐库", new MusicQuery());
                break;
            case MusicView.Liked:
                AddHeaderAction("播放全部", "\uE768", () => PlayAllVisible());
                LoadTrackView("喜欢音乐", "PerfectMusic 文件夹自动加入", new MusicQuery { LikedOnly = true });
                break;
            case MusicView.Perfect:
                AddHeaderAction("播放全部", "\uE768", () => PlayAllVisible());
                LoadTrackView("PerfectMusic", "优质收藏 · 自动加入喜欢音乐",
                    new MusicQuery { PerfectOnly = true });
                break;
            case MusicView.Recent:
                AddHeaderAction("播放全部", "\uE768", () => PlayAllVisible());
                LoadTrackView("最近添加", "按加入时间排序", new MusicQuery
                {
                    Sort = "added",
                    Descending = true,
                    Limit = 100
                });
                break;
            case MusicView.MostPlayed:
                AddHeaderAction("播放全部", "\uE768", () => PlayAllVisible());
                LoadTrackView("最常播放", "按播放次数排序", new MusicQuery
                {
                    HasPlayed = true,
                    Sort = "played",
                    Descending = true,
                    Limit = 200
                });
                break;
            case MusicView.NeverPlayed:
                AddHeaderAction("播放全部", "\uE768", () => PlayAllVisible());
                LoadTrackView("从未播放", "还没有播放过的曲目", new MusicQuery
                {
                    NeverPlayed = true,
                    Sort = "added",
                    Descending = true
                });
                break;
            case MusicView.Missing:
                LoadTrackView("缺失文件", "文件已被移动或删除", new MusicQuery
                {
                    MissingOnly = true
                });
                break;
            case MusicView.Duplicates:
                LoadDuplicates();
                break;
            case MusicView.Playlist:
                LoadPlaylistView(arg);
                break;
            case MusicView.Albums:
                LoadAlbums();
                break;
            case MusicView.Artists:
                LoadArtists();
                break;
            case MusicView.Genres:
                LoadGenres();
                break;
            case MusicView.Folders:
                LoadFolders();
                break;
            case MusicView.Stats:
                ShowStats();
                break;
            case MusicView.Plugins:
                ShowPlugins();
                break;
            case MusicView.Settings:
                ShowSettings();
                break;
        }
        SaveWindowState();
    }

    private void AddHeaderAction(string text, string glyph, Action action, bool primary = false)
    {
        var button = new Button
        {
            Text = "  " + text,
            AutoSize = false,
            Size = new Size(TextRenderer.MeasureText(text, _theme.Font(8.8f)).Width + 34, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? _theme.Accent : _theme.SurfaceAlt,
            ForeColor = primary ? Color.White : _theme.TextSecondary,
            Font = _theme.Font(8.8f),
            Margin = new Padding(8, 0, 0, 0),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = primary ? _theme.Accent : _theme.Border;
        button.FlatAppearance.BorderSize = 1;
        button.Click += (_, _) => action();
        _headerActions.Controls.Add(button);
    }

    private void SetContent(Control control)
    {
        foreach (Control existing in _contentBody.Controls.Cast<Control>().ToArray())
        {
            if (existing != control)
            {
                _contentBody.Controls.Remove(existing);
                if (existing != _trackList && existing != _cardPanel)
                {
                    existing.Dispose();
                }
            }
        }
        _currentPage = control == _trackList || control == _cardPanel ? null : control;
        if (!_contentBody.Controls.Contains(control))
        {
            _contentBody.Controls.Add(control);
        }
        control.Dock = DockStyle.Fill;
        control.BringToFront();
    }

    private void LoadTrackView(string title, string subtitle, MusicQuery query)
    {
        _drillQuery = null;
        _drilled = false;
        _backButton.Visible = false;
        _viewTitle.Left = 22;
        _currentQuery = query;
        ApplyQueryDefaults();
        var tracks = _library.QueryTracks(query);
        SetContent(_trackList);
        _trackList.EmptyText = string.IsNullOrWhiteSpace(_searchText)
            ? "没有找到音乐"
            : "没有匹配的音乐";
        _trackList.EmptyHint = string.IsNullOrWhiteSpace(_searchText)
            ? "点击右上角“扫描音乐”，默认扫描 Windows 用户音乐目录"
            : "尝试更换关键词，或清空搜索框";
        _trackList.SetTracks(tracks);
        _trackList.PlayingTrackId = (int)(_player.CurrentTrack?.Id ?? -1);
        _viewTitle.Text = title;
        _viewSubtitle.Text = _searchText.Length > 0
            ? $"{subtitle} · 搜索“{_searchText}” · {tracks.Count} 首"
            : $"{subtitle} · {tracks.Count} 首";
        UpdateLikeButton();
    }

    private MusicQuery BuildCurrentQuery()
    {
        var query = new MusicQuery
        {
            Search = _searchText,
            Sort = _sortColumn,
            Descending = _sortDescending
        };
        switch (_view)
        {
            case MusicView.Liked:
                query.LikedOnly = true;
                break;
            case MusicView.Perfect:
                query.PerfectOnly = true;
                break;
            case MusicView.Recent:
                query.Sort = "added";
                query.Descending = true;
                query.Limit = 100;
                break;
            case MusicView.MostPlayed:
                query.HasPlayed = true;
                query.Sort = "played";
                query.Descending = true;
                query.Limit = 200;
                break;
            case MusicView.NeverPlayed:
                query.NeverPlayed = true;
                break;
            case MusicView.Missing:
                query.MissingOnly = true;
                break;
            case MusicView.Playlist:
                if (long.TryParse(_viewArg, out long playlistId))
                {
                    query.PlaylistId = playlistId;
                }
                break;
        }
        return query;
    }

    private void ApplyQueryDefaults()
    {
        if (_view is MusicView.Recent or MusicView.MostPlayed)
        {
            return;
        }
        _currentQuery.Sort = _sortColumn;
        _currentQuery.Descending = _sortDescending;
        _currentQuery.Search = _searchText;
        _currentQuery.Limit = 0;
    }

    private void ReloadCurrentView()
    {
        if (_drilled && _drillQuery != null)
        {
            LoadDrill(_drillTitle, _drillSubtitle, _drillQuery);
            return;
        }
        switch (_view)
        {
            case MusicView.Albums:
                LoadAlbums();
                break;
            case MusicView.Artists:
                LoadArtists();
                break;
            case MusicView.Genres:
                LoadGenres();
                break;
            case MusicView.Folders:
                LoadFolders();
                break;
            case MusicView.Duplicates:
                LoadDuplicates();
                break;
            case MusicView.Stats:
                ShowStats();
                break;
            case MusicView.Settings:
                ShowSettings();
                break;
            case MusicView.Plugins:
                ShowPlugins();
                break;
            default:
                ShowView(_view, _viewArg);
                break;
        }
    }

    private void LoadDrill(string title, string subtitle, MusicQuery query)
    {
        _drilled = true;
        _drillTitle = title;
        _drillSubtitle = subtitle;
        _drillQuery = query;
        _backButton.Visible = true;
        _viewTitle.Left = 58;
        var effective = new MusicQuery
        {
            Search = _searchText,
            Sort = _sortColumn,
            Descending = _sortDescending,
            Album = query.Album,
            Artist = query.Artist,
            Genre = query.Genre,
            Folder = query.Folder,
            LikedOnly = query.LikedOnly,
            PerfectOnly = query.PerfectOnly,
            PlaylistId = query.PlaylistId
        };
        _currentQuery = effective;
        var tracks = _library.QueryTracks(effective);
        SetContent(_trackList);
        _trackList.SetTracks(tracks);
        _trackList.PlayingTrackId = (int)(_player.CurrentTrack?.Id ?? -1);
        _viewTitle.Text = title;
        _viewSubtitle.Text = $"{subtitle} · {tracks.Count} 首";
        _trackList.EmptyText = "没有匹配的曲目";
    }

    private void LoadAlbums()
    {
        _backButton.Visible = false;
        _viewTitle.Left = 22;
        var albums = _library.GetAlbums(_searchText);
        PopulateCards(albums.Select(album => new MusicCard(_theme, album.Name,
            $"{album.DisplayArtist} · {album.TrackCount} 首", album.CoverKey, "\uE93C")
        {
            Tag2 = album
        }), card => ((MusicAlbum)card.Tag2!).Name);
        foreach (var card in _cardPanel.Controls.OfType<MusicCard>())
        {
            var album = (MusicAlbum)card.Tag2!;
            card.Activated += (_, _) => LoadDrill("专辑 · " + album.Name, album.DisplayArtist,
                new MusicQuery { Album = album.Name });
        }
        _viewTitle.Text = "专辑";
        _viewSubtitle.Text = _searchText.Length > 0
            ? $"搜索“{_searchText}” · {albums.Count} 张专辑"
            : $"{albums.Count} 张专辑";
    }

    private void LoadArtists()
    {
        _backButton.Visible = false;
        _viewTitle.Left = 22;
        var artists = _library.GetArtists(_searchText);
        PopulateCards(artists.Select(artist => new MusicCard(_theme, artist.Name,
            $"{artist.TrackCount} 首 · {artist.AlbumCount} 张专辑", artist.CoverKey, "\uE77B")
        {
            Tag2 = artist
        }), card => ((MusicArtist)card.Tag2!).Name);
        foreach (var card in _cardPanel.Controls.OfType<MusicCard>())
        {
            var artist = (MusicArtist)card.Tag2!;
            card.Activated += (_, _) => LoadDrill("艺术家 · " + artist.Name,
                $"{artist.TrackCount} 首 · {artist.AlbumCount} 张专辑",
                new MusicQuery { Artist = artist.Name });
        }
        _viewTitle.Text = "艺术家";
        _viewSubtitle.Text = _searchText.Length > 0
            ? $"搜索“{_searchText}” · {artists.Count} 位艺术家"
            : $"{artists.Count} 位艺术家";
    }

    private void LoadGenres()
    {
        _backButton.Visible = false;
        _viewTitle.Left = 22;
        var genres = _library.GetGenres(_searchText);
        PopulateCards(genres.Select(genre => new MusicCard(_theme, genre.Name,
            $"{genre.TrackCount} 首", "", "\uE8EC")
        {
            Tag2 = genre
        }), card => ((MusicGenre)card.Tag2!).Name);
        foreach (var card in _cardPanel.Controls.OfType<MusicCard>())
        {
            var genre = (MusicGenre)card.Tag2!;
            card.Activated += (_, _) => LoadDrill("流派 · " + genre.Name,
                $"{genre.TrackCount} 首", new MusicQuery { Genre = genre.Name });
        }
        _viewTitle.Text = "流派";
        _viewSubtitle.Text = $"{genres.Count} 个流派";
    }

    private void LoadFolders()
    {
        _backButton.Visible = false;
        _viewTitle.Left = 22;
        var folders = _library.GetFolders();
        if (_searchText.Length > 0)
        {
            folders = folders.Where(f => f.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                || f.Path.Contains(_searchText, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        PopulateCards(folders.Select(folder => new MusicCard(_theme,
            folder.IsPerfect ? "PerfectMusic" : folder.Name,
            $"{folder.TrackCount} 首", "", folder.IsPerfect ? "\uE734" : "\uE8B7")
        {
            Tag2 = folder
        }), card => ((MusicFolder)card.Tag2!).Name);
        foreach (var card in _cardPanel.Controls.OfType<MusicCard>())
        {
            var folder = (MusicFolder)card.Tag2!;
            card.Activated += (_, _) => LoadDrill("文件夹 · " + folder.Name,
                folder.Path, new MusicQuery { Folder = folder.Path });
        }
        _viewTitle.Text = "文件夹";
        _viewSubtitle.Text = $"{folders.Count} 个文件夹";
    }

    private void PopulateCards(IEnumerable<MusicCard> cards, Func<MusicCard, string> _)
    {
        SetContent(_cardPanel);
        _cardPanel.SuspendLayout();
        foreach (Control old in _cardPanel.Controls.Cast<Control>().ToArray())
        {
            _cardPanel.Controls.Remove(old);
            old.Dispose();
        }
        foreach (var card in cards)
        {
            _cardPanel.Controls.Add(card);
        }
        _cardPanel.ResumeLayout();
    }

    private void LoadDuplicates()
    {
        _backButton.Visible = false;
        _viewTitle.Left = 22;
        var groups = _library.GetDuplicateFileGroups(_settings.EffectiveLibraryPath);
        var page = new MusicDuplicatesPage(groups, _theme, _settings.PerfectMusicPath);
        page.ScanRequested += (_, _) => StartScan();
        page.CleanupRequested += async (_, _) => await CleanupDuplicatesAsync(page);
        SetContent(page);
        _viewTitle.Text = "重复项清理";
        _viewSubtitle.Text = groups.Count == 0
            ? "按文件名检测同名音乐；喜欢 / PerfectMusic 曲目会保留到 PerfectMusic"
            : $"{groups.Count} 组同名重复音乐 · 多余 {groups.Sum(g => g.ExtraCount)} 个文件";
    }

    private async Task CleanupDuplicatesAsync(MusicDuplicatesPage page)
    {
        if (_cleanupRunning)
        {
            Toast("正在清理重复项目，请稍候");
            return;
        }
        if (_scanCts != null)
        {
            Toast("正在扫描音乐目录，请等扫描完成后再清理");
            return;
        }
        var groups = page.Groups;
        if (groups.Count == 0)
        {
            Toast("没有需要清理的重复项");
            return;
        }
        int extras = groups.Sum(g => g.ExtraCount);
        int protectedGroups = groups.Count(g => g.HasProtectedTrack);
        var confirm = MessageBox.Show(this,
            $"将处理 {groups.Count} 组同名音乐：\n"
            + $"· 删除多余重复文件：{extras} 个\n"
            + $"· 喜欢 / PerfectMusic 曲目移动到 PerfectMusic：{protectedGroups} 组\n"
            + "· 同名 .lrc 歌词会随保留曲目一起移动，删除项对应的歌词一并删除\n\n"
            + "删除的音乐文件不可恢复，是否继续？",
            "清理重复项目", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
        {
            return;
        }
        var affectedIds = groups.SelectMany(g => g.Tracks).Select(t => t.Id).ToHashSet();
        if (_player.Queue.Any(t => affectedIds.Contains(t.Id)))
        {
            _player.SetQueue(Array.Empty<MusicTrack>(), 0, false);
        }
        _cleanupRunning = true;
        Toast("正在清理重复项目…");
        try
        {
            var result = await Task.Run(() =>
                DuplicateCleanupService.Cleanup(_library, _settings, groups));
            RefreshSidebar();
            ShowView(MusicView.Duplicates);
            string message = $"清理完成：处理 {result.Groups} 组，"
                + $"移动 {result.Moved} 个到 PerfectMusic，删除 {result.Deleted} 个重复文件";
            if (result.Failed > 0)
            {
                message += $"，失败 {result.Failed} 个";
            }
            if (result.Errors.Count > 0)
            {
                message += "\n\n" + string.Join("\n", result.Errors.Take(5));
            }
            MessageBox.Show(this, message, "清理重复项目", MessageBoxButtons.OK,
                result.Failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "清理重复项目失败：" + ex.Message, "清理重复项目",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _cleanupRunning = false;
        }
    }

    private void LoadPlaylistView(string? idText)
    {
        if (!long.TryParse(idText, out long id))
        {
            ShowView(MusicView.Songs);
            return;
        }
        var playlist = _library.GetPlaylists().FirstOrDefault(p => p.Id == id);
        if (playlist == null)
        {
            ShowView(MusicView.Songs);
            return;
        }
        AddHeaderAction("删除列表", "\uE74D", () => DeletePlaylist(playlist));
        AddHeaderAction("重命名", "\uE70F", () => RenamePlaylist(playlist));
        AddHeaderAction("导出 M3U", "\uE896", () => ExportM3U(playlist));
        AddHeaderAction("播放全部", "\uE768", () => PlayAllVisible());
        _currentQuery = new MusicQuery
        {
            PlaylistId = id,
            Search = _searchText,
            Sort = _sortColumn,
            Descending = _sortDescending
        };
        var tracks = _library.QueryTracks(_currentQuery);
        SetContent(_trackList);
        _trackList.EmptyText = "播放列表还是空的";
        _trackList.EmptyHint = "在曲目上右键 → 添加到播放列表";
        _trackList.SetTracks(tracks);
        _trackList.PlayingTrackId = (int)(_player.CurrentTrack?.Id ?? -1);
        _viewTitle.Text = playlist.Name;
        _viewSubtitle.Text = $"播放列表 · {tracks.Count} 首";
    }

    private void ShowStats()
    {
        _statsPage?.Dispose();
        var page = new MusicStatsPage(_library, _theme);
        _statsPage = page;
        SetContent(page);
        _viewTitle.Text = "听歌统计";
        _viewSubtitle.Text = "全部数据仅保存在本机";
        _backButton.Visible = false;
        _viewTitle.Left = 22;
    }

    private void ShowPlugins()
    {
        var page = new MusicPluginsPage(_settings, _config, ToggleMiniPlayer,
            () => SetDesktopLyricsVisible(true), () => ApplyNetworkServices());
        page.SettingsChanged += (_, _) =>
        {
            RefreshSidebar();
            UpdatePluginButtons();
            ApplyNetworkServices();
        };
        SetContent(page);
        _viewTitle.Text = "插件";
        _viewSubtitle.Text = "迷你播放器与桌面歌词默认开启，可随时关闭；插件不联网";
        _backButton.Visible = false;
        _viewTitle.Left = 22;
    }

    private void ShowSettings()
    {
        var page = new MusicSettingsPage(_settings, _library, _player, _config);
        page.SettingsChanged += (_, _) =>
        {
            _config.Save();
            ApplyPlaybackSettings();
            ApplyNetworkServices();
            RefreshSidebar();
            UpdatePluginButtons();
            var theme = RssTheme.Create(_settings.Theme);
            if (!ReferenceEquals(theme, _theme))
            {
                _theme = theme;
                ApplyThemeSurfaces();
            }
        };
        page.ThemeChanged += (_, _) =>
        {
            _theme = RssTheme.Create(_settings.Theme);
            ApplyThemeSurfaces();
        };
        page.NetworkChanged += (_, _) => ApplyNetworkServices();
        page.RescanRequested += (_, _) => StartScan();
        page.OpenLibraryRequested += (_, _) => OpenLibraryFolder();
        page.SetDlnaStatus(_dlna?.IsRunning == true ? _dlna.Status : "未启动");
        SetContent(page);
        _viewTitle.Text = "设置";
        _viewSubtitle.Text = "网络与 SSDP 默认关闭；迷你播放器与桌面歌词默认开启";
        _backButton.Visible = false;
        _viewTitle.Left = 22;
    }

    private void ApplyThemeSurfaces()
    {
        BackColor = _theme.Background;
        _titleBar.BackColor = _theme.Surface;
        _contentHeader.BackColor = _theme.Surface;
        _contentBody.BackColor = _theme.Surface;
        _centerPanel.BackColor = _theme.Surface;
        _playerBar.BackColor = _theme.Surface;
        _searchHost.BackColor = _theme.SurfaceAlt;
        _search.BackColor = _theme.SurfaceAlt;
        _search.ForeColor = _theme.Text;
        _sidebar.Theme = _theme;
        _trackList.Theme = _theme;
        _cardPanel.SurfaceColor = _theme.Surface;
        _viewTitle.ForeColor = _theme.Text;
        _viewSubtitle.ForeColor = _theme.TextTertiary;
        _npTitle.ForeColor = _theme.Text;
        _npArtist.ForeColor = _theme.TextSecondary;
        _npAlbum.ForeColor = _theme.TextTertiary;
        _barTitle.ForeColor = _theme.Text;
        _barArtist.ForeColor = _theme.TextTertiary;
        _timeCurrent.ForeColor = _theme.TextTertiary;
        _timeTotal.ForeColor = _theme.TextTertiary;
        _coverBox.Theme = _theme;
        _barCover.Theme = _theme;
        _lyricsView.Theme = _theme;
        _seekBar.Theme = _theme;
        _volumeSlider.Theme = _theme;
        ApplyThemeToChildren(this);
        Invalidate(true);
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(new Action(() =>
            {
                if (!IsDisposed)
                {
                    ReloadCurrentView();
                }
            }));
        }
    }

    private void ApplyThemeToChildren(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            switch (child)
            {
                case MusicIconButton button:
                    button.Theme = _theme;
                    break;
                case MusicSlider slider:
                    slider.Theme = _theme;
                    break;
                case MusicSeekBar seekBar:
                    seekBar.Theme = _theme;
                    break;
                case MusicLyricsView lyrics:
                    lyrics.Theme = _theme;
                    break;
                case MusicCoverBox cover:
                    cover.Theme = _theme;
                    break;
                case MusicTrackList list:
                    list.Theme = _theme;
                    break;
                case MusicSidebar sidebar:
                    sidebar.Theme = _theme;
                    break;
                case MusicCardPanel panel:
                    panel.SurfaceColor = _theme.Surface;
                    break;
            }
            if (child.HasChildren)
            {
                ApplyThemeToChildren(child);
            }
        }
    }

    // ================= 搜索 =================

    private void ApplySearch()
    {
        _searchText = _search.Text.Trim();
        if (_view is MusicView.Settings or MusicView.Plugins)
        {
            return;
        }
        ReloadCurrentView();
        if (_drilled)
        {
            _sidebar.SelectedKey = ViewKey();
        }
    }

    // ================= 扫描 =================

    private async void StartScan(bool interactive = true)
    {
        if (_scanCts != null)
        {
            return;
        }
        if (!Directory.Exists(_settings.EffectiveLibraryPath))
        {
            if (!interactive)
            {
                return;
            }
            var result = MessageBox.Show(this,
                "音乐目录不存在：\r\n" + _settings.EffectiveLibraryPath
                + "\r\n\r\n是否打开设置修改音乐库目录？",
                "离线音乐播放器", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result == DialogResult.Yes)
            {
                ShowView(MusicView.Settings);
            }
            return;
        }
        var cts = new CancellationTokenSource();
        _scanCts = cts;
        CancellationToken token = cts.Token;
        _scanPanel.Visible = true;
        _scanLabel.Text = "正在扫描音乐目录…";
        _scanBar.Style = ProgressBarStyle.Continuous;
        _scanBar.Value = 0;
        var progress = new Progress<MusicScanProgress>(p =>
        {
            if (IsDisposed || _scanBar.IsDisposed)
            {
                return;
            }
            _scanBar.Maximum = Math.Max(1, p.Total);
            _scanBar.Value = Math.Min(_scanBar.Maximum, p.Done);
            _scanLabel.Text = p.Total > 0
                ? $"{p.Stage} {p.Done}/{p.Total}  {Path.GetFileName(p.CurrentFile)}"
                : p.Stage;
        });
        MusicScanResult scan;
        try
        {
            scan = await Task.Run(() => _scanner.Scan(_settings, progress, token));
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                _scanPanel.Visible = false;
                Toast("扫描失败：" + ex.Message);
            }
            cts.Dispose();
            if (ReferenceEquals(_scanCts, cts))
            {
                _scanCts = null;
            }
            return;
        }
        if (IsDisposed)
        {
            cts.Dispose();
            if (ReferenceEquals(_scanCts, cts))
            {
                _scanCts = null;
            }
            return;
        }
        _scanPanel.Visible = false;
        cts.Dispose();
        if (ReferenceEquals(_scanCts, cts))
        {
            _scanCts = null;
        }
        _library.SetMeta("last_scan", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        RefreshSidebar();
        if (_view is MusicView.Songs or MusicView.Liked or MusicView.Perfect
            or MusicView.Recent or MusicView.MostPlayed or MusicView.NeverPlayed
            or MusicView.Duplicates or MusicView.Missing or MusicView.Playlist)
        {
            ShowView(_view, _viewArg);
        }
        else if (_view is MusicView.Albums or MusicView.Artists or MusicView.Genres or MusicView.Folders)
        {
            ReloadCurrentView();
        }
        string message = $"扫描完成：新增 {scan.Added}，更新 {scan.Updated}，跳过 {scan.Skipped}";
        if (scan.Failed > 0) message += $"，失败 {scan.Failed}";
        if (scan.Removed > 0) message += $"，缺失 {scan.Removed}";
        if (scan.PerfectMusicFound)
        {
            message += " · PerfectMusic 已自动加入喜欢音乐";
        }
        Toast(message);
    }

    private void OpenLibraryFolder()
    {
        try
        {
            Directory.CreateDirectory(_settings.EffectiveLibraryPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{_settings.EffectiveLibraryPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Toast("无法打开音乐目录：" + ex.Message);
        }
    }

    // ================= 播放列表 =================

    private void CreatePlaylist()
    {
        string? name = MusicTextPromptDialog.Show(this, "新建播放列表", "播放列表名称");
        if (name == null)
        {
            return;
        }
        long id = _library.CreatePlaylist(name);
        RefreshSidebar();
        ShowView(MusicView.Playlist, id.ToString());
    }

    private void RenamePlaylist(MusicPlaylist playlist)
    {
        string? name = MusicTextPromptDialog.Show(this, "重命名播放列表", "新的名称", playlist.Name);
        if (name == null)
        {
            return;
        }
        _library.RenamePlaylist(playlist.Id, name);
        RefreshSidebar();
        ShowView(MusicView.Playlist, playlist.Id.ToString());
    }

    private void DeletePlaylist(MusicPlaylist playlist)
    {
        var result = MessageBox.Show(this,
            $"确定删除播放列表“{playlist.Name}”吗？\n（不会删除本地音乐文件）",
            "离线音乐播放器", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (result != DialogResult.Yes)
        {
            return;
        }
        _library.DeletePlaylist(playlist.Id);
        RefreshSidebar();
        ShowView(MusicView.Playlist);
    }

    private void AddSelectedToPlaylist()
    {
        var tracks = _trackList.SelectedTracks;
        if (tracks.Count == 0)
        {
            Toast("请先选择曲目");
            return;
        }
        var playlists = _library.GetPlaylists();
        if (playlists.Count == 0)
        {
            if (MessageBox.Show(this, "还没有播放列表，是否新建？", "离线音乐播放器",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            CreatePlaylist();
            return;
        }
        string? name = MusicTextPromptDialog.Show(this, "添加到播放列表",
            "输入播放列表名称（不存在则新建）", playlists[0].Name);
        if (name == null)
        {
            return;
        }
        var playlist = playlists.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        long id = playlist?.Id ?? _library.CreatePlaylist(name);
        _library.AddToPlaylist(id, tracks.Select(t => t.Id));
        RefreshSidebar();
        Toast($"已添加 {tracks.Count} 首到“{name}”");
    }

    private void ExportM3U(MusicPlaylist? playlist = null)
    {
        var tracks = playlist != null && _view == MusicView.Playlist
            ? _library.QueryTracks(new MusicQuery { PlaylistId = playlist.Id })
            : _trackList.Tracks.ToList();
        if (tracks.Count == 0)
        {
            Toast("当前列表没有可导出的曲目");
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Filter = "M3U 播放列表 (*.m3u)|*.m3u|所有文件 (*.*)|*.*",
            FileName = (playlist?.Name ?? "离线音乐播放器") + ".m3u",
            Title = "导出 M3U 播放列表"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            var builder = new System.Text.StringBuilder();
            builder.AppendLine("#EXTM3U");
            foreach (var track in tracks)
            {
                builder.AppendLine($"#EXTINF:{(int)track.DurationSeconds},{track.DisplayArtist} - {track.DisplayTitle}");
                builder.AppendLine(track.Path);
            }
            File.WriteAllText(dialog.FileName, builder.ToString(), new System.Text.UTF8Encoding(true));
            Toast($"已导出 {tracks.Count} 首到 {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出失败：" + ex.Message, "离线音乐播放器",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ImportM3U()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "M3U / M3U8 播放列表 (*.m3u;*.m3u8)|*.m3u;*.m3u8|所有文件 (*.*)|*.*",
            Title = "导入 M3U 播放列表"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            var ids = new List<long>();
            var all = _library.GetAllTracks();
            var byPath = all.ToDictionary(t => t.Path, t => t.Id, StringComparer.OrdinalIgnoreCase);
            foreach (string raw in File.ReadAllLines(dialog.FileName))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }
                string full;
                try
                {
                    full = Path.GetFullPath(Path.IsPathRooted(line)
                        ? line
                        : Path.Combine(Path.GetDirectoryName(dialog.FileName) ?? "", line));
                }
                catch
                {
                    continue;
                }
                if (byPath.TryGetValue(full, out long id))
                {
                    ids.Add(id);
                }
            }
            if (ids.Count == 0)
            {
                Toast("没有找到音乐库中的曲目，请先扫描音乐目录");
                return;
            }
            long playlistId = _library.CreatePlaylist(Path.GetFileNameWithoutExtension(dialog.FileName));
            _library.AddToPlaylist(playlistId, ids);
            RefreshSidebar();
            ShowView(MusicView.Playlist, playlistId.ToString());
            Toast($"已导入 {ids.Count} 首");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导入失败：" + ex.Message, "离线音乐播放器",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ================= 右键菜单 =================

    private void BuildTrackContextMenu()
    {
        _trackMenu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.System };
        _trackMenu.Opening += (_, _) => BuildTrackMenuItems();
        _trackList.ContextMenuStrip = _trackMenu;
    }

    private void BuildTrackMenuItems()
    {
        _trackMenu.Items.Clear();
        var selected = _trackList.SelectedTracks;
        if (selected.Count == 0)
        {
            _trackMenu.Items.Add("没有选中曲目").Enabled = false;
            return;
        }
        var first = selected[0];
        _trackMenu.Items.Add("播放", null, (_, _) => PlayTrack(first));
        _trackMenu.Items.Add(selected[0].Liked ? "取消喜欢" : "加入喜欢音乐", null,
            (_, _) => ToggleLike(first));
        var addMenu = new ToolStripMenuItem("添加到播放列表");
        foreach (var playlist in _library.GetPlaylists())
        {
            addMenu.DropDownItems.Add(playlist.Name, null, (_, _) =>
            {
                _library.AddToPlaylist(playlist.Id, selected.Select(t => t.Id));
                RefreshSidebar();
                Toast($"已添加 {selected.Count} 首到“{playlist.Name}”");
            });
        }
        addMenu.DropDownItems.Add(new ToolStripSeparator());
        addMenu.DropDownItems.Add("新建播放列表并添加…", null, (_, _) => AddSelectedToPlaylist());
        _trackMenu.Items.Add(addMenu);
        _trackMenu.Items.Add("编辑标签…", null, (_, _) =>
        {
            using var dialog = new MusicTagDialog(first, _library);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _trackList.RefreshTrack(first);
                if (_player.CurrentTrack?.Id == first.Id)
                {
                    UpdateNowPlayingTrack();
                    LoadLyrics(first);
                }
                Toast("标签已保存");
            }
        });
        _trackMenu.Items.Add(new ToolStripSeparator());
        _trackMenu.Items.Add("在资源管理器中显示", null, (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{first.Path}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Toast("无法打开资源管理器：" + ex.Message);
            }
        });
        _trackMenu.Items.Add("复制文件路径", null, (_, _) =>
        {
            try { Clipboard.SetText(first.Path); Toast("路径已复制"); } catch { }
        });
        if (_view == MusicView.Playlist && long.TryParse(_viewArg, out long playlistId))
        {
            _trackMenu.Items.Add("从当前播放列表移除", null, (_, _) =>
            {
                foreach (var track in selected)
                {
                    _library.RemoveFromPlaylist(playlistId, track.Id);
                }
                RefreshSidebar();
                ShowView(MusicView.Playlist, _viewArg);
            });
        }
        if (_view != MusicView.Missing)
        {
            _trackMenu.Items.Add(new ToolStripSeparator());
            _trackMenu.Items.Add("从音乐库移除（不删除文件）", null, (_, _) =>
            {
                if (MessageBox.Show(this,
                        $"确定从音乐库移除 {selected.Count} 首曲目吗？\n本地文件不会被删除。",
                        "离线音乐播放器", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    return;
                }
                foreach (var track in selected)
                {
                    _library.RemoveTrack(track.Id);
                }
                RefreshSidebar();
                ReloadCurrentView();
                Toast("已从音乐库移除");
            });
        }
    }

    // ================= 工具 =================

    private void Toast(string message)
    {
        _toastLabel.Text = message;
        _toastLabel.Visible = true;
        _toastLabel.BringToFront();
        PositionToast();
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void PositionToast()
    {
        int width = Math.Min(720, ClientSize.Width - 80);
        _toastLabel.Width = width;
        _toastLabel.Left = (ClientSize.Width - width) / 2;
        _toastLabel.Top = ClientSize.Height - _playerBar.Height - 52;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_toastLabel != null && _toastLabel.Visible)
        {
            PositionToast();
        }
    }

    private void SaveQueue()
    {
        try
        {
            var ids = _player.Queue.Select(t => t.Id).ToArray();
            string json = JsonSerializer.Serialize(new QueueState { Ids = ids, Index = _player.QueueIndex });
            _library.SetMeta("queue", json);
        }
        catch
        {
            // 忽略
        }
    }

    private void RestoreQueue()
    {
        try
        {
            string? json = _library.GetMeta("queue");
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }
            var state = JsonSerializer.Deserialize<QueueState>(json);
            if (state?.Ids == null || state.Ids.Length == 0)
            {
                return;
            }
            var tracks = new List<MusicTrack>();
            foreach (long id in state.Ids)
            {
                var track = _library.GetTrack(id);
                if (track != null && !track.Missing)
                {
                    tracks.Add(track);
                }
            }
            if (tracks.Count > 0)
            {
                _player.SetQueue(tracks, Math.Clamp(state.Index, 0, tracks.Count - 1), false);
                UpdateNowPlayingTrack();
                UpdatePlayerBarState();
            }
        }
        catch
        {
            // 队列还原失败不影响使用
        }
    }

    private sealed class QueueState
    {
        public long[] Ids { get; set; } = Array.Empty<long>();
        public int Index { get; set; }
    }
}
