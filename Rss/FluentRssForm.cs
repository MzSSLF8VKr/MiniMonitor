using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MiniMonitor.Rss;

/// <summary>
/// FluentRSS 阅读器主窗口：三栏布局（订阅源 / 文章列表 / 内置文章视图），
/// 默认最大化打开，支持四种列表视图、分组、搜索、规则、OPML 与后台同步。
/// </summary>
public sealed class FluentRssForm : Form
{
    private readonly AppConfig _config;
    private readonly RssStore _store;
    private readonly RssSyncService _sync;
    private readonly HttpClient _http;
    private readonly RssImageCache _images;
    private RssTheme _theme;

    // 标题栏
    private readonly Panel _titleBar;
    private readonly Label _titleText;
    private readonly Label _titleSub;
    private readonly FlowLayoutPanel _titleButtons;
    private readonly RssGlyphButton _btnAdd;
    private readonly RssGlyphButton _btnRefresh;
    private readonly RssGlyphButton _btnMarkAll;
    private readonly RssGlyphButton _btnView;
    private readonly RssGlyphButton _btnSettings;
    private readonly RssGlyphButton _btnNotify;
    private readonly RssGlyphButton _btnMinimize;
    private readonly RssGlyphButton _btnMaximize;
    private readonly RssGlyphButton _btnClose;

    // 内容
    private readonly TableLayoutPanel _root;
    private readonly Panel _content;
    private readonly Panel _sidebarHost;
    private readonly RssSourceList _sources;
    private readonly SplitContainer _split;
    private readonly Panel _listToolbar;
    private readonly Panel _searchHost;
    private readonly TextBox _searchBox;
    private readonly RssGlyphButton _btnRegex;
    private readonly RssChip _chipAll;
    private readonly RssChip _chipUnread;
    private readonly RssChip _chipStarred;
    private readonly Label _listCount;
    private readonly RssArticleList _list;
    private readonly Panel _readerToolbar;
    private readonly Label _readerSource;
    private readonly Label _readerMeta;
    private readonly FlowLayoutPanel _readerButtons;
    private readonly RssGlyphButton _btnBack;
    private readonly RssGlyphButton _btnStar;
    private readonly RssGlyphButton _btnRead;
    private readonly RssGlyphButton _btnFull;
    private readonly RssGlyphButton _btnWebpage;
    private readonly RssGlyphButton _btnExternal;
    private readonly RssGlyphButton _btnFontMinus;
    private readonly RssGlyphButton _btnFontPlus;
    private readonly Panel _readerBody;
    private readonly Panel _emptyReader;
    private readonly TableLayoutPanel _emptyStack;
    private readonly Panel _fallbackHost;
    private readonly RichTextBox _fallbackText;
    private readonly Label _fallbackWarning;
    private WebView2? _web;
    private readonly Panel _statusBar;
    private readonly Label _statusLeft;
    private readonly Label _statusRight;
    private readonly Panel _toast;
    private readonly Label _toastLabel;
    private readonly System.Windows.Forms.Timer _toastTimer;
    private readonly System.Windows.Forms.Timer _searchTimer;
    private readonly System.Windows.Forms.Timer _reloadTimer;
    private readonly ToolTip _tips = new();
    private ContextMenuStrip _viewMenu;

    private RssScope _scope = RssScope.All();
    private RssFilterMode _filter = RssFilterMode.All;
    private List<RssArticle> _articles = new();
    private List<RssArticle> _queryPage = new();
    private RssArticle? _current;
    private bool _hasMore;
    private bool _loadingMore;
    private bool _webReady;
    private bool _webMode;
    private bool _allowWebNavigation;
    private long _renderedAid = -1;
    private bool _sidebarVisible = true;
    private bool _allowClose;
    private bool _initializing = true;
    private bool _syncing;
    private RssSettingsForm? _settingsForm;
    private Action<RssSyncResult>? _syncCompletedHandler;
    private Action? _dataChangedHandler;
    private Action? _syncStateHandler;

    private const int PageSize = 120;
    private const int SidebarWidth = 264;
    private const int WmNcHitTest = 0x84;

    public FluentRssForm(AppConfig config, RssStore store, RssSyncService sync)
    {
        _config = config;
        _store = store;
        _sync = sync;
        _theme = RssTheme.Create(_config.Rss.Theme);
        _http = Http.CreateClient();
        _images = new RssImageCache();

        Text = "FluentRSS 阅读器";
        Icon = LoadAppIcon();
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1120, 680);
        ClientSize = new Size(
            Math.Max(1120, _config.Rss.WindowWidth),
            Math.Max(680, _config.Rss.WindowHeight));
        BackColor = _theme.Background;
        ForeColor = _theme.Text;
        Font = _theme.Font(9.2f);
        KeyPreview = true;
        DoubleBuffered = true;
        ShowInTaskbar = true;
        MaximizeBox = true;

        _titleBar = new Panel { Dock = DockStyle.Fill, BackColor = _theme.Surface, Height = 52 };
        _titleBar.Paint += PaintTitleBar;
        _titleBar.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ToggleMaximize();
            }
        };
        NativeWindowDrag.Attach(this, _titleBar);

        _titleText = new Label
        {
            Text = "FluentRSS 阅读器",
            AutoSize = false,
            BackColor = Color.Transparent,
            ForeColor = _theme.Text,
            Font = _theme.Font(11.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _titleSub = new Label
        {
            Text = "Fluent Reader v1.3.2 复刻 · 本地 RSS",
            AutoSize = false,
            BackColor = Color.Transparent,
            ForeColor = _theme.TextTertiary,
            Font = _theme.Font(8.2f),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _titleText.MouseDoubleClick += (_, _) => ToggleMaximize();
        _titleSub.MouseDoubleClick += (_, _) => ToggleMaximize();
        NativeWindowDrag.Attach(this, _titleText);
        NativeWindowDrag.Attach(this, _titleSub);

        _btnAdd = GlyphButton("\uE710", "添加订阅源");
        _btnRefresh = GlyphButton("\uE72C", "立即刷新（F5）");
        _btnMarkAll = GlyphButton("\uE73E", "全部标为已读（F6）");
        _btnView = GlyphButton("\uE8A9", "切换视图（F8）");
        _btnNotify = GlyphButton("\uE7E7", "后台通知开关（F7）");
        _btnSettings = GlyphButton("\uE713", "选项（F9）");
        _btnMinimize = GlyphButton("\uE921", "最小化");
        _btnMaximize = GlyphButton("\uE922", "最大化");
        _btnClose = GlyphButton("\uE8BB", "关闭到托盘");

        _btnAdd.Click += (_, _) => AddFeed();
        _btnRefresh.Click += (_, _) => RefreshFeeds();
        _btnMarkAll.Click += (_, _) => MarkAllRead();
        _btnView.Click += (_, _) => ShowViewMenu(_btnView);
        _btnNotify.Click += (_, _) => ToggleNotifications();
        _btnSettings.Click += (_, _) => OpenSettings();
        _btnMinimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        _btnMaximize.Click += (_, _) => ToggleMaximize();
        _btnClose.Click += (_, _) => HideToTray();

        _titleButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 8, 8, 0)
        };
        foreach (var button in new[]
                 {
                     _btnClose, _btnMaximize, _btnMinimize, _btnSettings, _btnNotify,
                     _btnView, _btnMarkAll, _btnRefresh, _btnAdd
                 })
        {
            _titleButtons.Controls.Add(button);
        }
        _titleBar.Controls.Add(_titleText);
        _titleBar.Controls.Add(_titleSub);
        _titleBar.Controls.Add(_titleButtons);

        _root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = _theme.Background,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _root.Controls.Add(_titleBar, 0, 0);

        _content = new Panel { Dock = DockStyle.Fill, BackColor = _theme.Background };
        var contentLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = _theme.Background,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SidebarWidth));
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _sidebarHost = new Panel { Dock = DockStyle.Fill, BackColor = _theme.Sidebar };
        _sources = new RssSourceList { Dock = DockStyle.Fill };
        _sources.SetTheme(_theme, _images);
        _sidebarHost.Controls.Add(_sources);
        contentLayout.Controls.Add(_sidebarHost, 0, 0);

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 1,
            BackColor = _theme.Border
        };
        _split.Panel1.BackColor = _theme.Surface;
        _split.Panel2.BackColor = _theme.Surface;
        _split.SplitterMoved += (_, _) => LayoutToolbars();
        contentLayout.Controls.Add(_split, 1, 0);
        _content.Controls.Add(contentLayout);
        _root.Controls.Add(_content, 0, 1);

        // ---------- 文章列表 ----------
        var listColumn = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = _theme.Surface,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        listColumn.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        listColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        listColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _listToolbar = new Panel { Dock = DockStyle.Fill, BackColor = _theme.Surface };
        _listToolbar.Paint += (_, e) =>
        {
            using var pen = new Pen(_theme.Border);
            e.Graphics.DrawLine(pen, 0, _listToolbar.Height - 1,
                _listToolbar.Width, _listToolbar.Height - 1);
        };
        _searchHost = new Panel
        {
            BackColor = _theme.Surface,
            Height = 34
        };
        _searchHost.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(0.5f, 0.5f, _searchHost.Width - 1, _searchHost.Height - 1);
            using var path = ShutdownTheme.RoundRect(rect, 7f);
            using (var brush = new SolidBrush(_theme.InputBackground))
            {
                e.Graphics.FillPath(brush, path);
            }
            using var pen = new Pen(_searchBox is { Focused: true } ? _theme.Accent : _theme.Border);
            e.Graphics.DrawPath(pen, path);
        };
        _searchBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = _theme.InputBackground,
            ForeColor = _theme.Text,
            Font = _theme.Font(9.2f),
            Width = 200
        };
        _searchBox.TextChanged += (_, _) =>
        {
            _searchTimer?.Stop();
            _searchTimer?.Start();
        };
        _searchBox.GotFocus += (_, _) => _searchHost.Invalidate();
        _searchBox.LostFocus += (_, _) => _searchHost.Invalidate();
        _btnRegex = GlyphButton("\uE8D3", "使用正则表达式搜索");
        _btnRegex.Size = new Size(30, 26);
        _btnRegex.GlyphSize = 9.5f;
        _btnRegex.Toggled = _config.Rss.RegexSearch;
        _btnRegex.Click += (_, _) =>
        {
            _config.Rss.RegexSearch = !_config.Rss.RegexSearch;
            _config.Save();
            _btnRegex.Toggled = _config.Rss.RegexSearch;
            LoadArticles(preserveSelection: false);
        };
        _searchHost.Controls.Add(_searchBox);
        _searchHost.Controls.Add(_btnRegex);
        _listToolbar.Controls.Add(_searchHost);

        _chipAll = Chip("全部", RssFilterMode.All);
        _chipUnread = Chip("未读", RssFilterMode.Unread);
        _chipStarred = Chip("星标", RssFilterMode.Starred);
        _listCount = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleRight,
            Font = _theme.Font(8.4f),
            ForeColor = _theme.TextTertiary,
            BackColor = Color.Transparent,
            Text = ""
        };
        _listToolbar.Controls.Add(_chipAll);
        _listToolbar.Controls.Add(_chipUnread);
        _listToolbar.Controls.Add(_chipStarred);
        _listToolbar.Controls.Add(_listCount);

        _list = new RssArticleList { Dock = DockStyle.Fill };
        _list.SetTheme(_theme, _images);
        _list.SetOptions(ViewMode(), _config.Rss.ShowSnippet, _config.Rss.ShowCover, _config.Rss.FadeRead);
        listColumn.Controls.Add(_listToolbar, 0, 0);
        listColumn.Controls.Add(_list, 0, 1);
        _split.Panel1.Controls.Add(listColumn);

        // ---------- 阅读区 ----------
        var readerColumn = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = _theme.Surface,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        readerColumn.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        readerColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        readerColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _readerToolbar = new Panel { Dock = DockStyle.Fill, BackColor = _theme.Surface };
        _readerToolbar.Paint += (_, e) =>
        {
            using var pen = new Pen(_theme.Border);
            e.Graphics.DrawLine(pen, 0, _readerToolbar.Height - 1,
                _readerToolbar.Width, _readerToolbar.Height - 1);
        };
        _readerSource = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.BottomLeft,
            Font = _theme.Font(9.6f, FontStyle.Bold),
            ForeColor = _theme.Accent,
            BackColor = Color.Transparent,
            Text = ""
        };
        _readerMeta = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.TopLeft,
            Font = _theme.Font(8.2f),
            ForeColor = _theme.TextTertiary,
            BackColor = Color.Transparent,
            Text = ""
        };
        _readerButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 10, 8, 0)
        };
        _btnBack = GlyphButton("\uE72B", "返回内置文章视图");
        _btnStar = GlyphButton("\uE734", "标为星标（S）");
        _btnRead = GlyphButton("\uE73E", "标为未读（M）");
        _btnFull = GlyphButton("\uE8A5", "抓取全文（W）");
        _btnWebpage = GlyphButton("\uE774", "加载原文网页（L）");
        _btnExternal = GlyphButton("\uE8A7", "在浏览器中打开（B）");
        _btnFontMinus = GlyphButton("\uE8E7", "减小正文字号");
        _btnFontPlus = GlyphButton("\uE8E8", "增大正文字号");
        _btnBack.Visible = false;
        _btnBack.Click += (_, _) => LeaveWebMode();
        _btnStar.Click += (_, _) => ToggleStar(_current);
        _btnRead.Click += (_, _) => ToggleRead(_current);
        _btnFull.Click += (_, _) => _ = LoadFullContentAsync();
        _btnWebpage.Click += (_, _) => ToggleWebpage();
        _btnExternal.Click += (_, _) => OpenExternal(_current);
        _btnFontMinus.Click += (_, _) => ChangeFontSize(-1);
        _btnFontPlus.Click += (_, _) => ChangeFontSize(1);
        foreach (var button in new[]
                 {
                     _btnExternal, _btnWebpage, _btnFull, _btnRead, _btnStar,
                     _btnFontPlus, _btnFontMinus, _btnBack
                 })
        {
            _readerButtons.Controls.Add(button);
        }
        _readerToolbar.Controls.Add(_readerSource);
        _readerToolbar.Controls.Add(_readerMeta);
        _readerToolbar.Controls.Add(_readerButtons);

        _readerBody = new Panel { Dock = DockStyle.Fill, BackColor = _theme.Surface };
        _emptyReader = new Panel { Dock = DockStyle.Fill, BackColor = _theme.Surface };
        var emptyGlyph = new Label
        {
            Text = "▤",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = _theme.Font(32f),
            ForeColor = _theme.TextTertiary
        };
        var emptyText = new Label
        {
            Text = "选择一篇文章开始阅读",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = _theme.Font(11f, FontStyle.Bold),
            ForeColor = _theme.TextSecondary
        };
        var emptyHint = new Label
        {
            Text = "订阅源会在后台按设定的间隔自动同步；打开阅读器时也会立即同步一次。",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = _theme.Font(8.6f),
            ForeColor = _theme.TextTertiary
        };
        _emptyStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = _theme.Surface
        };
        _emptyStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _emptyStack.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _emptyStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        _emptyStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _emptyStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _emptyStack.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        _emptyStack.Controls.Add(emptyGlyph, 0, 1);
        _emptyStack.Controls.Add(emptyText, 0, 2);
        _emptyStack.Controls.Add(emptyHint, 0, 3);
        _emptyReader.Controls.Add(_emptyStack);

        _fallbackHost = new Panel { Dock = DockStyle.Fill, BackColor = _theme.Surface, Visible = false };
        _fallbackWarning = new Label
        {
            Dock = DockStyle.Top,
            Height = 44,
            BackColor = _theme.IsDark ? Color.FromArgb(70, 46, 30) : Color.FromArgb(255, 244, 229),
            ForeColor = _theme.IsDark ? Color.FromArgb(255, 200, 150) : Color.FromArgb(146, 96, 0),
            Font = _theme.Font(8.6f),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 10, 0),
            Text = "未检测到 Microsoft Edge WebView2 Runtime，正文以纯文本显示。"
                + "安装 WebView2 后可使用完整的排版、图片与网页模式。"
        };
        _fallbackText = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            BackColor = _theme.Surface,
            ForeColor = _theme.Text,
            Font = _theme.Font(10.5f),
            WordWrap = true,
            DetectUrls = true
        };
        _fallbackHost.Controls.Add(_fallbackText);
        _fallbackHost.Controls.Add(_fallbackWarning);
        _readerBody.Controls.Add(_emptyReader);
        _readerBody.Controls.Add(_fallbackHost);
        readerColumn.Controls.Add(_readerToolbar, 0, 0);
        readerColumn.Controls.Add(_readerBody, 0, 1);
        _split.Panel2.Controls.Add(readerColumn);

        // ---------- 状态栏 ----------
        _statusBar = new Panel { Dock = DockStyle.Fill, BackColor = _theme.Surface };
        _statusBar.Paint += (_, e) =>
        {
            using var pen = new Pen(_theme.Border);
            e.Graphics.DrawLine(pen, 0, 0, _statusBar.Width, 0);
        };
        _statusLeft = new Label
        {
            Dock = DockStyle.Left,
            AutoSize = false,
            Width = 460,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = _theme.Font(8.3f),
            ForeColor = _theme.TextSecondary,
            Padding = new Padding(14, 0, 0, 0),
            Text = ""
        };
        _statusRight = new Label
        {
            Dock = DockStyle.Right,
            AutoSize = false,
            Width = 520,
            TextAlign = ContentAlignment.MiddleRight,
            Font = _theme.Font(8.3f),
            ForeColor = _theme.TextTertiary,
            Padding = new Padding(0, 0, 14, 0),
            Text = ""
        };
        _statusBar.Controls.Add(_statusLeft);
        _statusBar.Controls.Add(_statusRight);
        _root.Controls.Add(_statusBar, 0, 2);

        Controls.Add(_root);

        // ---------- Toast ----------
        _toast = new Panel { Visible = false, BackColor = Color.Transparent, Size = new Size(420, 44) };
        _toast.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = ShutdownTheme.RoundRect(
                new RectangleF(0, 0, _toast.Width, _toast.Height), 9f);
            using var brush = new SolidBrush(_theme.IsDark
                ? Color.FromArgb(238, 58, 58, 58)
                : Color.FromArgb(242, 32, 32, 32));
            e.Graphics.FillPath(brush, path);
        };
        _toastLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = _theme.Font(9f),
            ForeColor = Color.White,
            BackColor = Color.Transparent
        };
        _toast.Controls.Add(_toastLabel);
        Controls.Add(_toast);
        _toast.BringToFront();

        _viewMenu = BuildViewMenu();
        _toastTimer = new System.Windows.Forms.Timer { Interval = 2800 };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            _toast.Visible = false;
        };
        _searchTimer = new System.Windows.Forms.Timer { Interval = 360 };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            LoadArticles(preserveSelection: false);
        };
        _reloadTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _reloadTimer.Tick += (_, _) =>
        {
            _reloadTimer.Stop();
            ReloadAll(preserveSelection: true);
        };

        BindEvents();
        _tips.SetToolTip(_searchBox, "搜索标题与正文（F2）");
        _tips.SetToolTip(_btnRegex, "使用正则表达式搜索");
        _tips.SetToolTip(_btnAdd, "添加订阅源");
        _tips.SetToolTip(_btnRefresh, "立即刷新（F5）");
        _tips.SetToolTip(_btnMarkAll, "全部标为已读（F6）");
        _tips.SetToolTip(_btnView, "切换视图（F8）");
        _tips.SetToolTip(_btnNotify, "后台通知开关（F7）");
        _tips.SetToolTip(_btnSettings, "选项（F9）");
        _tips.SetToolTip(_btnMinimize, "最小化");
        _tips.SetToolTip(_btnMaximize, "最大化 / 还原");
        _tips.SetToolTip(_btnClose, "关闭到托盘（后台继续同步）");
        _tips.SetToolTip(_btnBack, "返回内置文章视图");
        _tips.SetToolTip(_btnStar, "标为星标 / 取消星标（S）");
        _tips.SetToolTip(_btnRead, "标为已读 / 未读（M）");
        _tips.SetToolTip(_btnFull, "抓取全文（W）");
        _tips.SetToolTip(_btnWebpage, "加载原文网页 / 返回文章（L）");
        _tips.SetToolTip(_btnExternal, "在浏览器中打开（B）");
        _tips.SetToolTip(_btnFontMinus, "减小正文字号");
        _tips.SetToolTip(_btnFontPlus, "增大正文字号");

        _initializing = false;
        Load += (_, _) =>
        {
            ApplyCueBanner();
            UpdateNotifyButton();
            ReloadAll(preserveSelection: false);
            InitializeWebView();
        };
        Shown += (_, _) => SetRoundedCorners();
        Resize += (_, _) =>
        {
            LayoutTitleBar();
            LayoutToolbars();
            PositionToast();
            _btnMaximize.Glyph = WindowState == FormWindowState.Maximized ? "\uE923" : "\uE922";
            SetRoundedCorners();
        };
    }

    private static Icon? LoadAppIcon()
    {
        try
        {
            using var stream = typeof(FluentRssForm).Assembly
                .GetManifestResourceStream("MiniMonitor.app.ico");
            return stream != null ? new Icon(stream) : null;
        }
        catch
        {
            return null;
        }
    }

    private RssGlyphButton GlyphButton(string glyph, string tooltip)
    {
        var button = new RssGlyphButton { Glyph = glyph, Theme = _theme, Size = new Size(38, 34) };
        _tips.SetToolTip(button, tooltip);
        return button;
    }

    private RssChip Chip(string text, RssFilterMode mode)
    {
        var chip = new RssChip { Text = text, Theme = _theme, Size = new Size(56, 28), Tag = mode };
        chip.Click += (_, _) => SetFilter(mode);
        return chip;
    }

    private RssViewMode ViewMode()
    {
        return _config.Rss.ViewMode switch
        {
            "cards" => RssViewMode.Cards,
            "magazine" => RssViewMode.Magazine,
            "compact" => RssViewMode.Compact,
            _ => RssViewMode.List
        };
    }

    private void BindEvents()
    {
        _sources.ScopeChanged += scope =>
        {
            _scope = scope;
            LoadArticles(preserveSelection: false);
            _list.Focus();
        };
        _sources.FeedMenuRequested += ShowFeedMenu;
        _sources.GroupMenuRequested += ShowGroupMenu;
        _sources.EmptyMenuRequested += ShowEmptyMenu;
        _sources.RefreshFeedRequested += feed => _ = _sync.SyncFeedAsync(feed.Sid);
        _sources.GroupToggleRequested += group => _store.SetGroupExpanded(group.Gid, group.Expanded);

        _list.SelectionChanged += article => ShowArticle(article);
        _list.ArticleActivated += article =>
        {
            if (article != null)
            {
                ShowArticle(article);
            }
        };
        _list.StarToggled += article => ToggleStar(article);
        _list.ReadToggled += article => ToggleRead(article);
        _list.HideRequested += article => HideArticle(article);
        _list.OpenExternalRequested += article => OpenExternal(article);
        _list.ContextMenuRequested += ShowArticleMenu;
        _list.LoadMoreRequested += LoadMoreArticles;

        _syncCompletedHandler = result => SafeInvoke(() => OnSyncCompleted(result));
        _dataChangedHandler = () => SafeInvoke(() =>
        {
            _reloadTimer.Stop();
            _reloadTimer.Start();
        });
        _syncStateHandler = () => SafeInvoke(UpdateSyncState);
        _sync.SyncCompleted += _syncCompletedHandler;
        _sync.DataChanged += _dataChangedHandler;
        _sync.StateChanged += _syncStateHandler;
        _syncing = _sync.IsSyncing;
    }

    private void SafeInvoke(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }
        try
        {
            BeginInvoke(action);
        }
        catch
        {
            // 窗口正在销毁
        }
    }

    // ================= 公开接口 =================

    /// <summary>由 MiniMonitor 右键菜单调用：打开并最大化阅读器，同时立即同步一次。</summary>
    public void ShowReader()
    {
        if (!Visible)
        {
            if (!_config.Rss.WindowShown)
            {
                _config.Rss.WindowShown = true;
                _config.Save();
            }
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
            WindowState = FormWindowState.Maximized;
            Show();
            ReloadAll(preserveSelection: true);
        }
        else
        {
            if (WindowState == FormWindowState.Minimized)
            {
                MaximizedBounds = Screen.FromControl(this).WorkingArea;
                WindowState = FormWindowState.Maximized;
            }
            Activate();
            BringToFront();
        }
        BeginInvoke(new Action(() => _ = SyncOnOpenAsync()));
    }

    public void CloseForApp()
    {
        _allowClose = true;
        Close();
    }

    private async Task SyncOnOpenAsync()
    {
        if (_syncing)
        {
            return;
        }
        var result = await _sync.SyncAsync(manual: true);
        if (result != null && Visible)
        {
            ShowToast(result.NewArticles > 0
                ? $"已同步 {result.NewArticles} 篇新文章"
                : "已是最新内容");
        }
    }

    private void HideToTray()
    {
        SaveWindowState();
        Hide();
    }

    // ================= 数据加载 =================

    private void ReloadAll(bool preserveSelection)
    {
        if (IsDisposed)
        {
            return;
        }
        var groups = _store.GetGroups();
        var feeds = _store.GetFeeds();
        var counts = _store.GetSmartCounts();
        _sources.SetData(groups, feeds, counts);
        _sources.SetSelected(_scope);
        LoadArticles(preserveSelection);
        UpdateStatus();
    }

    private RssQuery BuildQuery(int offset)
    {
        return new RssQuery
        {
            Scope = _scope,
            UnreadOnly = _filter == RssFilterMode.Unread,
            StarredOnly = _filter == RssFilterMode.Starred,
            IncludeHidden = false,
            SearchText = string.IsNullOrWhiteSpace(_searchBox.Text) ? null : _searchBox.Text.Trim(),
            UseRegex = _config.Rss.RegexSearch,
            CaseSensitive = _config.Rss.CaseSensitiveSearch,
            SearchContent = _config.Rss.SearchContent,
            Offset = offset,
            Limit = PageSize
        };
    }

    private void LoadArticles(bool preserveSelection)
    {
        if (_initializing)
        {
            return;
        }
        long? keep = preserveSelection ? _current?.Aid : null;
        try
        {
            _queryPage = _store.QueryArticles(BuildQuery(0));
        }
        catch
        {
            _queryPage = new List<RssArticle>();
        }
        _articles = _queryPage.ToList();
        _hasMore = _queryPage.Count >= PageSize;
        _list.SetArticles(_articles, _hasMore, keep);
        if (keep == null && _articles.Count > 0)
        {
            _list.SelectFirstIfNone();
        }
        UpdateStatus();
    }

    private void LoadMoreArticles()
    {
        if (_loadingMore || !_hasMore)
        {
            return;
        }
        _loadingMore = true;
        try
        {
            long? keep = _current?.Aid;
            var next = _store.QueryArticles(BuildQuery(_articles.Count));
            if (next.Count == 0)
            {
                _hasMore = false;
            }
            else
            {
                _articles.AddRange(next);
                _hasMore = next.Count >= PageSize;
            }
            _list.SetArticles(_articles, _hasMore, keep);
        }
        finally
        {
            _loadingMore = false;
        }
    }

    private void SetFilter(RssFilterMode mode)
    {
        _filter = mode;
        _chipAll.Checked = mode == RssFilterMode.All;
        _chipUnread.Checked = mode == RssFilterMode.Unread;
        _chipStarred.Checked = mode == RssFilterMode.Starred;
        LoadArticles(preserveSelection: false);
    }

    private void UpdateStatus()
    {
        int unread = _store.GetUnreadCount(_scope);
        int total = _articles.Count;
        _statusLeft.Text = $"{_scope.Title} · 共 {total} 篇" + (_hasMore ? "+" : "")
            + $" · 未读 {unread}";
        _listCount.Text = total == 0 ? "无文章" : $"已加载 {total} 篇";
        _statusRight.Text = BuildSyncStatusText();
    }

    private string BuildSyncStatusText()
    {
        if (_syncing)
        {
            return "正在同步… · 后台每 " + _config.Rss.SyncIntervalMinutes + " 分钟自动同步";
        }
        var last = _sync.LastSync;
        string lastText = last.HasValue ? last.Value.ToString("HH:mm") : "从未";
        return "上次同步 " + lastText + " · 后台每 " + _config.Rss.SyncIntervalMinutes + " 分钟自动同步";
    }

    private void UpdateSyncState()
    {
        _syncing = _sync.IsSyncing;
        _statusRight.Text = BuildSyncStatusText();
        _btnRefresh.Enabled = !_syncing;
        if (_syncing)
        {
            _btnRefresh.Glyph = "\uE895";
        }
        else
        {
            _btnRefresh.Glyph = "\uE72C";
        }
    }

    // ================= 文章操作 =================

    private void ShowArticle(RssArticle? article)
    {
        if (article == null)
        {
            _current = null;
            RenderEmpty();
            UpdateReaderToolbar();
            return;
        }
        var fresh = _store.GetArticle(article.Aid);
        if (fresh == null)
        {
            return;
        }
        _current = fresh;
        if (_config.Rss.MarkReadOnOpen && !fresh.IsRead)
        {
            MarkRead(fresh, true);
        }
        UpdateReaderToolbar();
        if (_renderedAid == fresh.Aid && !_webMode && _webReady)
        {
            return;
        }
        RenderCurrent();
    }

    private void MarkRead(RssArticle article, bool read)
    {
        if (article.IsRead == read)
        {
            return;
        }
        article.IsRead = read;
        _store.MarkRead(article.Aid, read);
        _list.UpdateArticle(article);
        RefreshSidebarCounts();
        UpdateReaderToolbar();
        UpdateStatus();
    }

    private void ToggleRead(RssArticle? article)
    {
        if (article == null)
        {
            return;
        }
        MarkRead(article, !article.IsRead);
        ShowToast(article.IsRead ? "已标为已读" : "已标为未读");
    }

    private void ToggleStar(RssArticle? article)
    {
        if (article == null)
        {
            return;
        }
        article.Starred = !article.Starred;
        _store.MarkStar(article.Aid, article.Starred);
        _list.UpdateArticle(article);
        UpdateReaderToolbar();
        UpdateStatus();
        ShowToast(article.Starred ? "已加星标" : "已取消星标");
    }

    private void HideArticle(RssArticle article)
    {
        _store.SetHidden(article.Aid, !article.Hidden);
        _list.RemoveArticle(article.Aid);
        _articles.RemoveAll(a => a.Aid == article.Aid);
        RefreshSidebarCounts();
        UpdateStatus();
        ShowToast(article.Hidden ? "已取消隐藏" : "已隐藏文章（可在搜索中显示）");
    }

    private void RefreshSidebarCounts()
    {
        var groups = _store.GetGroups();
        var feeds = _store.GetFeeds();
        var counts = _store.GetSmartCounts();
        _sources.SetData(groups, feeds, counts);
        _sources.SetSelected(_scope);
    }

    private void MarkAllRead()
    {
        if (MessageBox.Show(this, $"确认将“{_scope.Title}”中的所有文章标为已读？",
                "全部标为已读", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }
        _store.MarkAllRead(_scope);
        if (_config.Rss.ServiceType is "fever" or "greader")
        {
            _ = _sync.PushMarkAllReadAsync(_scope);
        }
        ReloadAll(preserveSelection: true);
        ShowToast("已全部标为已读");
    }

    private void ChangeFontSize(int delta)
    {
        int next = Math.Clamp(_config.Rss.FontSize + delta, 11, 32);
        if (next == _config.Rss.FontSize)
        {
            return;
        }
        _config.Rss.FontSize = next;
        _config.Save();
        if (!_webMode)
        {
            RenderCurrent();
        }
        ShowToast($"正文字号 {next}px");
    }

    // ================= 阅读器渲染 =================

    private void RenderEmpty()
    {
        _emptyReader.Visible = true;
        _fallbackHost.Visible = false;
        if (_web != null)
        {
            _web.Visible = false;
        }
        _renderedAid = -1;
    }

    private void RenderCurrent()
    {
        if (_current == null)
        {
            RenderEmpty();
            return;
        }
        _webMode = false;
        _allowWebNavigation = false;
        _btnBack.Visible = false;
        _btnWebpage.Toggled = false;
        if (!_webReady || _web == null)
        {
            ShowFallback(_current);
            return;
        }
        _emptyReader.Visible = false;
        _fallbackHost.Visible = false;
        _web.Visible = true;
        try
        {
            string html = RssHtml.BuildArticle(_current, _theme, _config.Rss.FontSize,
                _config.Rss.MarkReadOnScroll);
            _web.NavigateToString(html);
            _renderedAid = _current.Aid;
        }
        catch (Exception ex)
        {
            ShowToast("正文渲染失败：" + ex.Message);
        }
    }

    private void ShowFallback(RssArticle article)
    {
        _emptyReader.Visible = false;
        if (_web != null)
        {
            _web.Visible = false;
        }
        _fallbackHost.Visible = true;
        string text = article.DisplayTitle + Environment.NewLine
            + article.SourceTitle + " · " + article.PublishedTime.ToString("yyyy-MM-dd HH:mm")
            + Environment.NewLine + Environment.NewLine
            + RssParser.HtmlToText(article.FullContent ?? article.Content ?? article.Snippet ?? "");
        _fallbackText.Text = text;
        _renderedAid = article.Aid;
    }

    private async void InitializeWebView()
    {
        try
        {
            string userDataFolder = AppPaths.Sub("WebView2RSS");
            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            _web = new WebView2
            {
                Dock = DockStyle.Fill,
                Visible = false,
                DefaultBackgroundColor = _theme.Surface
            };
            _readerBody.Controls.Add(_web);
            _web.BringToFront();
            _web.CoreWebView2InitializationCompleted += (_, e) =>
            {
                if (!e.IsSuccess)
                {
                    _webReady = false;
                    _fallbackWarning.Text = "WebView2 初始化失败：" + (e.InitializationException?.Message ?? "未知原因");
                    if (_current != null)
                    {
                        ShowFallback(_current);
                    }
                    return;
                }
                _webReady = true;
                var core = _web.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = true;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = true;
                core.Settings.IsZoomControlEnabled = true;
                core.Settings.AreHostObjectsAllowed = false;
                core.Settings.IsPasswordAutosaveEnabled = false;
                core.Settings.IsGeneralAutofillEnabled = false;
                core.NewWindowRequested += (_, args) =>
                {
                    args.Handled = true;
                    OpenExternalUrl(args.Uri);
                };
                core.NavigationStarting += (_, args) =>
                {
                    if (string.IsNullOrWhiteSpace(args.Uri)
                        || args.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
                        || args.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                    if (_allowWebNavigation)
                    {
                        if (!args.Uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                            && !args.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            args.Cancel = true;
                            return;
                        }
                        return;
                    }
                    args.Cancel = true;
                    OpenExternalUrl(args.Uri);
                };
                core.WebMessageReceived += (_, args) =>
                {
                    if (args.WebMessageAsJson.Contains("rss-bottom", StringComparison.Ordinal)
                        && _current != null && _config.Rss.MarkReadOnScroll && !_current.IsRead)
                    {
                        SafeInvoke(() => MarkRead(_current, true));
                    }
                    if (args.WebMessageAsJson.Contains("rss-escape", StringComparison.Ordinal)
                        && _webMode)
                    {
                        SafeInvoke(LeaveWebMode);
                    }
                };
                if (_current != null && !_webMode)
                {
                    RenderCurrent();
                }
            };
            await _web.EnsureCoreWebView2Async(environment);
        }
        catch (Exception ex)
        {
            _webReady = false;
            _fallbackWarning.Text = "未检测到 Microsoft Edge WebView2 Runtime，正文以纯文本显示。"
                + "安装 WebView2 后可获得完整排版与网页模式。";
            _fallbackText.Text = "";
            if (_current != null)
            {
                ShowFallback(_current);
            }
            Debug.WriteLine("WebView2 初始化失败：" + ex);
        }
    }

    private void ToggleWebpage()
    {
        if (_current == null)
        {
            return;
        }
        if (_webMode)
        {
            LeaveWebMode();
            return;
        }
        if (string.IsNullOrWhiteSpace(_current.Link))
        {
            ShowToast("这篇文章没有原文链接");
            return;
        }
        if (!_webReady || _web == null)
        {
            ShowToast("WebView2 不可用，已改用系统浏览器");
            OpenExternalUrl(_current.Link);
            return;
        }
        _webMode = true;
        _allowWebNavigation = true;
        _btnBack.Visible = true;
        _btnWebpage.Toggled = true;
        _emptyReader.Visible = false;
        _fallbackHost.Visible = false;
        _web.Visible = true;
        try
        {
            _web.CoreWebView2.Navigate(_current.Link);
        }
        catch (Exception ex)
        {
            ShowToast("加载网页失败：" + ex.Message);
        }
    }

    private void LeaveWebMode()
    {
        if (!_webMode)
        {
            return;
        }
        _webMode = false;
        _allowWebNavigation = false;
        _btnBack.Visible = false;
        _btnWebpage.Toggled = false;
        RenderCurrent();
    }

    private async Task LoadFullContentAsync()
    {
        if (_current == null)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(_current.Link))
        {
            ShowToast("这篇文章没有原文链接，无法抓取全文");
            return;
        }
        if (!string.IsNullOrWhiteSpace(_current.FullContent))
        {
            RenderCurrent();
            ShowToast("已显示之前抓取的全文");
            return;
        }
        ShowToast("正在抓取全文…");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _current.Link);
            request.Headers.TryAddWithoutValidation("User-Agent", Http.UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,*/*;q=0.8");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead,
                CancellationToken.None);
            response.EnsureSuccessStatusCode();
            string html = await Http.ReadStringLimitedAsync(response, 6 * 1024 * 1024, CancellationToken.None);
            var uri = new Uri(_current.Link);
            string? readable = RssParser.ExtractReadableArticle(html, uri);
            if (string.IsNullOrWhiteSpace(readable))
            {
                ShowToast("未能从网页中提取正文，已保留原始摘要");
                return;
            }
            _current.FullContent = readable;
            _store.SaveFullContent(_current.Aid, readable);
            _webMode = false;
            RenderCurrent();
            ShowToast("已抓取全文");
        }
        catch (Exception ex)
        {
            ShowToast("抓取全文失败：" + ex.Message);
        }
    }

    private void UpdateReaderToolbar()
    {
        bool has = _current != null;
        _btnStar.Enabled = has;
        _btnRead.Enabled = has;
        _btnFull.Enabled = has && !string.IsNullOrWhiteSpace(_current?.Link);
        _btnWebpage.Enabled = has && !string.IsNullOrWhiteSpace(_current?.Link);
        _btnExternal.Enabled = has && !string.IsNullOrWhiteSpace(_current?.Link);
        _btnBack.Visible = _webMode;
        if (!has)
        {
            _readerSource.Text = "FluentRSS 阅读器";
            _readerMeta.Text = "本地 RSS · 自动同步";
            _btnStar.Glyph = "\uE734";
            _btnRead.Glyph = "\uE73E";
            return;
        }
        _readerSource.Text = _current!.SourceTitle;
        _readerMeta.Text = _current.PublishedTime.ToString("yyyy-MM-dd HH:mm")
            + (string.IsNullOrWhiteSpace(_current.Author) ? "" : " · " + _current.Author)
            + (_current.IsRead ? " · 已读" : " · 未读");
        _btnStar.Glyph = _current.Starred ? "\uE735" : "\uE734";
        _btnRead.Glyph = _current.IsRead ? "\uE7E7" : "\uE73E";
    }

    private void OpenExternal(RssArticle? article)
    {
        OpenExternalUrl(article?.Link);
    }

    private void OpenExternalUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            ShowToast("没有可打开的链接");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowToast("打开浏览器失败：" + ex.Message);
        }
    }

    // ================= 同步 / 通知 =================

    private void OnSyncCompleted(RssSyncResult result)
    {
        _syncing = false;
        UpdateSyncState();
        _reloadTimer.Stop();
        _reloadTimer.Start();
        if (!Visible)
        {
            return;
        }
        if (result.Errors.Count > 0)
        {
            ShowToast($"同步完成，{result.FailedFeeds} 个订阅源失败，新增 {result.NewArticles} 篇");
        }
        else if (!result.TriggeredManually)
        {
            ShowToast(result.NewArticles > 0
                ? $"后台同步：{result.NewArticles} 篇新文章"
                : "后台同步完成，暂无新文章");
        }
    }

    private void ToggleNotifications()
    {
        _config.Rss.Notifications = !_config.Rss.Notifications;
        _config.Save();
        UpdateNotifyButton();
        ShowToast(_config.Rss.Notifications ? "后台通知已开启" : "后台通知已关闭");
    }

    private void UpdateNotifyButton()
    {
        _btnNotify.Toggled = _config.Rss.Notifications;
        _tips.SetToolTip(_btnNotify, _config.Rss.Notifications ? "后台通知：已开启" : "后台通知：已关闭");
    }

    private void RefreshFeeds()
    {
        if (_syncing)
        {
            ShowToast("正在同步，请稍候…");
            return;
        }
        _ = _sync.SyncAsync(manual: true);
        _syncing = true;
        UpdateSyncState();
        ShowToast("正在刷新全部订阅源…");
    }

    // ================= 视图 / 选项 =================

    private ContextMenuStrip BuildViewMenu()
    {
        var menu = ThemeMenu();
        (string Text, string Mode)[] items =
        {
            ("列表视图", "list"),
            ("卡片视图", "cards"),
            ("杂志视图", "magazine"),
            ("紧凑视图", "compact")
        };
        foreach (var item in items)
        {
            var menuItem = new ToolStripMenuItem(item.Text)
            {
                Checked = _config.Rss.ViewMode == item.Mode,
                CheckOnClick = false,
                ForeColor = _theme.Text
            };
            menuItem.Click += (_, _) => SetView(item.Mode);
            menu.Items.Add(menuItem);
        }
        menu.Items.Add(new ToolStripSeparator());
        var snippet = new ToolStripMenuItem("显示摘要") { Checked = _config.Rss.ShowSnippet, ForeColor = _theme.Text };
        snippet.Click += (_, _) =>
        {
            _config.Rss.ShowSnippet = !_config.Rss.ShowSnippet;
            _config.Save();
            ApplyListOptions();
            snippet.Checked = _config.Rss.ShowSnippet;
        };
        var cover = new ToolStripMenuItem("显示封面") { Checked = _config.Rss.ShowCover, ForeColor = _theme.Text };
        cover.Click += (_, _) =>
        {
            _config.Rss.ShowCover = !_config.Rss.ShowCover;
            _config.Save();
            ApplyListOptions();
            cover.Checked = _config.Rss.ShowCover;
        };
        var fade = new ToolStripMenuItem("淡化已读文章") { Checked = _config.Rss.FadeRead, ForeColor = _theme.Text };
        fade.Click += (_, _) =>
        {
            _config.Rss.FadeRead = !_config.Rss.FadeRead;
            _config.Save();
            ApplyListOptions();
            fade.Checked = _config.Rss.FadeRead;
        };
        menu.Items.Add(snippet);
        menu.Items.Add(cover);
        menu.Items.Add(fade);
        return menu;
    }

    private ContextMenuStrip ThemeMenu()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = _theme.Surface,
            ForeColor = _theme.Text,
            Font = _theme.Font(9f),
            RenderMode = ToolStripRenderMode.System
        };
        return menu;
    }

    private void SetView(string mode)
    {
        _config.Rss.ViewMode = mode;
        _config.Save();
        ApplyListOptions();
        foreach (ToolStripItem item in _viewMenu.Items)
        {
            if (item is ToolStripMenuItem menuItem)
            {
                menuItem.Checked = mode switch
                {
                    "cards" => menuItem.Text == "卡片视图",
                    "magazine" => menuItem.Text == "杂志视图",
                    "compact" => menuItem.Text == "紧凑视图",
                    "list" => menuItem.Text == "列表视图",
                    _ => menuItem.Checked
                };
            }
        }
    }

    private void ApplyListOptions()
    {
        _viewMenu.Dispose();
        _viewMenu = BuildViewMenu();
        _list.SetOptions(ViewMode(), _config.Rss.ShowSnippet, _config.Rss.ShowCover, _config.Rss.FadeRead);
    }

    private void ShowViewMenu(Control anchor)
    {
        _viewMenu.Dispose();
        _viewMenu = BuildViewMenu();
        _viewMenu.Show(anchor, new Point(0, anchor.Height + 2));
    }

    private void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false, Visible: true })
        {
            _settingsForm.Activate();
            return;
        }
        using var form = new RssSettingsForm(_config, _store, _sync, _theme, ApplySettingsChanged);
        _settingsForm = form;
        form.ShowDialog(this);
        _settingsForm = null;
        ApplySettingsChanged();
    }

    private void ApplySettingsChanged()
    {
        _theme = RssTheme.Create(_config.Rss.Theme);
        ApplyTheme();
        ReloadAll(preserveSelection: true);
        _btnRegex.Toggled = _config.Rss.RegexSearch;
        ApplyListOptions();
        UpdateNotifyButton();
        if (_current != null)
        {
            _current = _store.GetArticle(_current.Aid) ?? _current;
        }
        RenderCurrent();
    }

    private void ApplyTheme()
    {
        BackColor = _theme.Background;
        ForeColor = _theme.Text;
        _root.BackColor = _theme.Background;
        _titleBar.BackColor = _theme.Surface;
        _content.BackColor = _theme.Background;
        _sidebarHost.BackColor = _theme.Sidebar;
        _split.BackColor = _theme.Border;
        _split.Panel1.BackColor = _theme.Surface;
        _split.Panel2.BackColor = _theme.Surface;
        _listToolbar.BackColor = _theme.Surface;
        _searchHost.BackColor = _theme.Surface;
        _searchBox.BackColor = _theme.InputBackground;
        _searchBox.ForeColor = _theme.Text;
        _readerToolbar.BackColor = _theme.Surface;
        _readerBody.BackColor = _theme.Surface;
        _emptyReader.BackColor = _theme.Surface;
        _emptyStack.BackColor = _theme.Surface;
        _fallbackHost.BackColor = _theme.Surface;
        _fallbackText.BackColor = _theme.Surface;
        _fallbackText.ForeColor = _theme.Text;
        _statusBar.BackColor = _theme.Surface;
        _titleText.ForeColor = _theme.Text;
        _titleSub.ForeColor = _theme.TextTertiary;
        _readerSource.ForeColor = _theme.Accent;
        _readerMeta.ForeColor = _theme.TextTertiary;
        _listCount.ForeColor = _theme.TextTertiary;
        _statusLeft.ForeColor = _theme.TextSecondary;
        _statusRight.ForeColor = _theme.TextTertiary;
        _chipAll.Theme = _theme;
        _chipUnread.Theme = _theme;
        _chipStarred.Theme = _theme;
        foreach (var button in new[]
                 {
                     _btnAdd, _btnRefresh, _btnMarkAll, _btnView, _btnNotify, _btnSettings,
                     _btnMinimize, _btnMaximize, _btnClose, _btnBack, _btnStar, _btnRead,
                     _btnFull, _btnWebpage, _btnExternal, _btnFontMinus, _btnFontPlus, _btnRegex
                 })
        {
            button.Theme = _theme;
        }
        _sources.SetTheme(_theme, _images);
        _list.SetTheme(_theme, _images);
        if (_web != null)
        {
            _web.DefaultBackgroundColor = _theme.Surface;
        }
        _fallbackWarning.BackColor = _theme.IsDark
            ? Color.FromArgb(70, 46, 30) : Color.FromArgb(255, 244, 229);
        _fallbackWarning.ForeColor = _theme.IsDark
            ? Color.FromArgb(255, 200, 150) : Color.FromArgb(146, 96, 0);
        _viewMenu.Dispose();
        _viewMenu = BuildViewMenu();
        Invalidate(true);
    }

    // ================= 菜单 =================

    private void ShowFeedMenu(RssFeed feed, Point screen)
    {
        var menu = ThemeMenu();
        var refresh = new ToolStripMenuItem("刷新此订阅源");
        refresh.Click += (_, _) => _ = _sync.SyncFeedAsync(feed.Sid);
        var edit = new ToolStripMenuItem("编辑订阅源…");
        edit.Click += (_, _) => EditFeed(feed);
        var move = new ToolStripMenuItem("移动到分组");
        var none = new ToolStripMenuItem("未分组") { Checked = feed.Gid == null };
        none.Click += (_, _) => MoveFeed(feed, null);
        move.DropDownItems.Add(none);
        foreach (var group in _store.GetGroups())
        {
            var item = new ToolStripMenuItem(group.Name) { Checked = feed.Gid == group.Gid };
            item.Click += (_, _) => MoveFeed(feed, group.Gid);
            move.DropDownItems.Add(item);
        }
        var hide = new ToolStripMenuItem(feed.Hidden ? "在全部文章中显示" : "从全部文章中隐藏");
        hide.Click += (_, _) =>
        {
            _store.SetFeedHidden(feed.Sid, !feed.Hidden);
            ReloadAll(preserveSelection: true);
        };
        var openSite = new ToolStripMenuItem("打开网站主页");
        openSite.Click += (_, _) => OpenExternalUrl(feed.Link ?? feed.Url);
        var delete = new ToolStripMenuItem("删除订阅源…") { ForeColor = _theme.Danger };
        delete.Click += (_, _) => DeleteFeed(feed);
        menu.Items.AddRange(new ToolStripItem[]
        {
            refresh, edit, move, new ToolStripSeparator(), hide, openSite,
            new ToolStripSeparator(), delete
        });
        menu.Show(screen);
    }

    private void ShowGroupMenu(RssGroup group, Point screen)
    {
        var menu = ThemeMenu();
        var add = new ToolStripMenuItem("在此分组添加订阅源…");
        add.Click += (_, _) => AddFeed(group.Gid);
        var rename = new ToolStripMenuItem("重命名分组…");
        rename.Click += (_, _) =>
        {
            using var dialog = new GroupNameDialog(_theme, "重命名分组", group.Name);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _store.RenameGroup(group.Gid, dialog.GroupName);
                ReloadAll(preserveSelection: true);
            }
        };
        var delete = new ToolStripMenuItem("删除分组…") { ForeColor = _theme.Danger };
        delete.Click += (_, _) =>
        {
            if (MessageBox.Show(this,
                    $"删除分组“{group.Name}”？其中的订阅源会移动到“未分组”。",
                    "删除分组", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _store.DeleteGroup(group.Gid);
                ReloadAll(preserveSelection: true);
            }
        };
        menu.Items.AddRange(new ToolStripItem[] { add, rename, new ToolStripSeparator(), delete });
        menu.Show(screen);
    }

    private void ShowEmptyMenu(Point screen)
    {
        var menu = ThemeMenu();
        var add = new ToolStripMenuItem("添加订阅源…");
        add.Click += (_, _) => AddFeed(null);
        var group = new ToolStripMenuItem("新建分组…");
        group.Click += (_, _) => AddGroup();
        var import = new ToolStripMenuItem("导入 OPML…");
        import.Click += (_, _) => ImportOpml();
        var export = new ToolStripMenuItem("导出 OPML…");
        export.Click += (_, _) => ExportOpml();
        var refresh = new ToolStripMenuItem("刷新全部（F5）");
        refresh.Click += (_, _) => RefreshFeeds();
        var settings = new ToolStripMenuItem("选项…（F9）");
        settings.Click += (_, _) => OpenSettings();
        menu.Items.AddRange(new ToolStripItem[]
        {
            add, group, new ToolStripSeparator(), import, export,
            new ToolStripSeparator(), refresh, settings
        });
        menu.Show(screen);
    }

    private void ShowArticleMenu(RssArticle article, Point screen)
    {
        var menu = ThemeMenu();
        var read = new ToolStripMenuItem(article.IsRead ? "标为未读" : "标为已读");
        read.Click += (_, _) => ToggleRead(article);
        var star = new ToolStripMenuItem(article.Starred ? "取消星标" : "标为星标");
        star.Click += (_, _) => ToggleStar(article);
        var hide = new ToolStripMenuItem("隐藏文章");
        hide.Click += (_, _) => HideArticle(article);
        var external = new ToolStripMenuItem("在浏览器中打开");
        external.Click += (_, _) => OpenExternal(article);
        var copyTitle = new ToolStripMenuItem("复制标题");
        copyTitle.Click += (_, _) => CopyToClipboard(article.DisplayTitle);
        var copyLink = new ToolStripMenuItem("复制链接");
        copyLink.Click += (_, _) => CopyToClipboard(article.Link ?? "");
        int index = _articles.FindIndex(a => a.Aid == article.Aid);
        var above = new ToolStripMenuItem("将以上标为已读");
        above.Enabled = index > 0;
        above.Click += (_, _) =>
        {
            var aids = _articles.Take(Math.Max(0, index)).Select(a => a.Aid).ToList();
            _store.MarkRangeRead(aids, true);
            ReloadAll(preserveSelection: true);
        };
        var below = new ToolStripMenuItem("将以下标为已读");
        below.Enabled = index >= 0 && index < _articles.Count - 1;
        below.Click += (_, _) =>
        {
            var aids = _articles.Skip(index + 1).Select(a => a.Aid).ToList();
            _store.MarkRangeRead(aids, true);
            ReloadAll(preserveSelection: true);
        };
        menu.Items.AddRange(new ToolStripItem[]
        {
            read, star, hide, new ToolStripSeparator(), external,
            new ToolStripSeparator(), copyTitle, copyLink,
            new ToolStripSeparator(), above, below
        });
        menu.Show(screen);
    }

    private static void CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // 剪贴板被占用
        }
    }

    private void AddFeed(int? gid = null)
    {
        using var dialog = new AddFeedDialog(_theme, _store, _store.GetGroups(), _http);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            if (gid.HasValue)
            {
                _store.MoveFeed(dialog.AddedSid, gid);
            }
            _scope = RssScope.Feed(dialog.AddedSid, dialog.AddedTitle);
            _sources.SelectScope(_scope);
            ReloadAll(preserveSelection: false);
            _ = _sync.SyncFeedAsync(dialog.AddedSid);
            ShowToast("已添加订阅源，正在抓取…");
        }
    }

    private void EditFeed(RssFeed feed)
    {
        using var dialog = new EditFeedDialog(_theme, _store, feed, _store.GetGroups());
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            ReloadAll(preserveSelection: true);
        }
    }

    private void MoveFeed(RssFeed feed, int? gid)
    {
        _store.MoveFeed(feed.Sid, gid);
        ReloadAll(preserveSelection: true);
    }

    private void DeleteFeed(RssFeed feed)
    {
        if (MessageBox.Show(this,
                $"确定删除订阅源“{feed.Title}”及其全部文章吗？此操作无法撤销。",
                "删除订阅源", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }
        _store.DeleteFeed(feed.Sid);
        if (_scope.Kind == RssScopeKind.Feed && _scope.Id == feed.Sid)
        {
            _scope = RssScope.All();
            _sources.SelectScope(_scope);
        }
        ReloadAll(preserveSelection: false);
    }

    private void AddGroup()
    {
        using var dialog = new GroupNameDialog(_theme, "新建分组", "");
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _store.AddGroup(dialog.GroupName);
            ReloadAll(preserveSelection: true);
        }
    }

    private void ImportOpml()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "导入 OPML 订阅源",
            Filter = "OPML 文件|*.opml;*.xml|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            var entries = RssParser.ImportOpml(File.ReadAllText(dialog.FileName));
            var groups = _store.GetGroups();
            int added = 0;
            foreach (var entry in entries)
            {
                int? gid = null;
                if (!string.IsNullOrWhiteSpace(entry.GroupName))
                {
                    var group = groups.FirstOrDefault(g =>
                        string.Equals(g.Name, entry.GroupName, StringComparison.OrdinalIgnoreCase));
                    if (group == null)
                    {
                        int newGid = _store.AddGroup(entry.GroupName!);
                        group = new RssGroup { Gid = newGid, Name = entry.GroupName! };
                        groups.Add(group);
                    }
                    gid = group.Gid;
                }
                entry.Feed.Gid = gid;
                _store.AddFeed(entry.Feed, out bool created);
                if (created)
                {
                    added++;
                }
            }
            ReloadAll(preserveSelection: true);
            _ = _sync.SyncAsync(manual: true);
            ShowToast($"已导入 {added} 个订阅源");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导入 OPML 失败：" + ex.Message, "导入 OPML",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ExportOpml()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "导出 OPML 订阅源",
            Filter = "OPML 文件|*.opml",
            FileName = "FluentRSS订阅源_" + DateTime.Now.ToString("yyyyMMdd") + ".opml"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            File.WriteAllText(dialog.FileName,
                RssParser.BuildOpml(_store.GetGroups(), _store.GetFeeds()));
            ShowToast("OPML 已导出");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出 OPML 失败：" + ex.Message, "导出 OPML",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ================= 界面布局 =================

    private void LayoutTitleBar()
    {
        if (_titleBar == null)
        {
            return;
        }
        int buttonsWidth = _titleButtons.PreferredSize.Width;
        _titleText.SetBounds(52, 6, Math.Max(80, _titleBar.Width - buttonsWidth - 72), 24);
        _titleSub.SetBounds(53, 29, Math.Max(80, _titleBar.Width - buttonsWidth - 72), 18);
    }

    /// <summary>
    /// 在窗口已经有真实尺寸后再设置分栏最小宽度。SplitContainer 在构造时若先设置
    /// Panel2MinSize，会立即用默认宽度校验 SplitterDistance，从而抛出异常。
    /// </summary>
    private void ConfigureSplitter()
    {
        int width = _split.Width;
        if (width < 120)
        {
            return;
        }
        try
        {
            int target = Math.Clamp(_config.Rss.ListWidth,
                _split.Panel1MinSize, Math.Max(_split.Panel1MinSize, width - _split.Panel2MinSize - 1));
            _split.SplitterDistance = target;
            int panel1Min = Math.Min(300, target);
            int panel2Min = Math.Min(420, Math.Max(160, width - target - 1));
            if (panel1Min + panel2Min + _split.SplitterWidth > width)
            {
                panel1Min = Math.Max(120, width / 4);
                panel2Min = Math.Max(120, width - panel1Min - _split.SplitterWidth - 1);
            }
            _split.Panel1MinSize = panel1Min;
            _split.Panel2MinSize = panel2Min;
            int maxDistance = Math.Max(_split.Panel1MinSize,
                width - _split.Panel2MinSize - _split.SplitterWidth);
            _split.SplitterDistance = Math.Clamp(_config.Rss.ListWidth,
                _split.Panel1MinSize, maxDistance);
        }
        catch
        {
            // 极窄窗口下保持默认分栏，不影响阅读器打开
        }
    }

    private void LayoutToolbars()
    {
        if (_listToolbar == null)
        {
            return;
        }
        int listWidth = _split.Panel1.Width;
        if (listWidth <= 0)
        {
            listWidth = _split.Width - 420;
        }
        int searchWidth = Math.Max(160, listWidth - 24);
        _searchHost.SetBounds(12, 10, searchWidth, 34);
        _searchBox.SetBounds(10, 8, Math.Max(60, searchWidth - 54), 20);
        _btnRegex.SetBounds(searchWidth - 38, 4, 32, 26);

        int x = 12;
        foreach (var chip in new[] { _chipAll, _chipUnread, _chipStarred })
        {
            chip.SetBounds(x, 54, 58, 28);
            x += 64;
        }
        _listCount.SetBounds(x + 6, 54, Math.Max(40, listWidth - x - 64), 28);

        int readerWidth = _readerToolbar.Width;
        int buttonsWidth = _readerButtons.PreferredSize.Width;
        _readerSource.SetBounds(16, 6, Math.Max(60, readerWidth - buttonsWidth - 28), 24);
        _readerMeta.SetBounds(17, 29, Math.Max(60, readerWidth - buttonsWidth - 28), 18);
    }

    private void PositionToast()
    {
        if (_toast == null)
        {
            return;
        }
        _toast.SetBounds((ClientSize.Width - _toast.Width) / 2,
            ClientSize.Height - 88, _toast.Width, _toast.Height);
        _toast.BringToFront();
    }

    private void ShowToast(string text)
    {
        if (IsDisposed)
        {
            return;
        }
        _toastLabel.Text = text;
        _toast.Visible = true;
        _toast.BringToFront();
        PositionToast();
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void ApplyCueBanner()
    {
        try
        {
            SendMessage(_searchBox.Handle, 0x1501, (IntPtr)1, "搜索文章…（F2）");
        }
        catch
        {
            // 忽略
        }
    }

    private void PaintTitleBar(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(_theme.Border))
        {
            g.DrawLine(pen, 0, _titleBar.Height - 1, _titleBar.Width, _titleBar.Height - 1);
        }
        var logoRect = new Rectangle(18, 14, 24, 24);
        using (var path = ShutdownTheme.RoundRect(logoRect, 6f))
        using (var brush = new SolidBrush(_theme.Accent))
        {
            g.FillPath(brush, path);
        }
        using (var pen = new Pen(Color.White, 1.8f))
        {
            float cx = logoRect.Left + 7;
            float cy = logoRect.Top + 16;
            g.DrawArc(pen, cx - 5, cy - 8, 12, 12, -45, 90);
            g.DrawArc(pen, cx - 2, cy - 4, 8, 8, -45, 90);
            g.FillEllipse(Brushes.White, cx, cy - 1, 2.6f, 2.6f);
        }
    }

    private void ToggleMaximize()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            WindowState = FormWindowState.Normal;
        }
        else
        {
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
            WindowState = FormWindowState.Maximized;
        }
        _btnMaximize.Glyph = WindowState == FormWindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void SetRoundedCorners()
    {
        try
        {
            int preference = WindowState == FormWindowState.Maximized ? 1 : 2;
            DwmSetWindowAttribute(Handle, 33, ref preference, sizeof(int));
        }
        catch
        {
            // 旧系统忽略
        }
        if (WindowState == FormWindowState.Maximized)
        {
            Region = null;
            return;
        }
        try
        {
            using var path = ShutdownTheme.RoundRect(
                new RectangleF(0, 0, Width, Height), Math.Min(12f, Width / 20f));
            var old = Region;
            Region = new Region(path);
            old?.Dispose();
        }
        catch
        {
            Region = null;
        }
    }

    private void SaveWindowState()
    {
        if (WindowState == FormWindowState.Normal)
        {
            _config.Rss.WindowWidth = Width;
            _config.Rss.WindowHeight = Height;
        }
        if (_split != null && _split.Width > 0)
        {
            _config.Rss.ListWidth = Math.Clamp(_split.SplitterDistance, 300, 900);
        }
        _config.Save();
    }

    // ================= 键盘 =================

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F1:
                ToggleSidebar();
                return true;
            case Keys.F2:
            case Keys.Control | Keys.F:
                _searchBox.Focus();
                _searchBox.SelectAll();
                return true;
            case Keys.F5:
            case Keys.Control | Keys.R:
                RefreshFeeds();
                return true;
            case Keys.F6:
                MarkAllRead();
                return true;
            case Keys.F7:
                ToggleNotifications();
                return true;
            case Keys.F8:
                ShowViewMenu(_btnView);
                return true;
            case Keys.F9:
                OpenSettings();
                return true;
            case Keys.W when _list.Focused:
                _ = LoadFullContentAsync();
                return true;
            case Keys.L when _list.Focused:
                ToggleWebpage();
                return true;
            case Keys.Escape:
                if (_webMode)
                {
                    LeaveWebMode();
                    return true;
                }
                if (_searchBox.Focused)
                {
                    _searchBox.Clear();
                    _list.Focus();
                    return true;
                }
                break;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ToggleSidebar()
    {
        _sidebarVisible = !_sidebarVisible;
        var layout = _content.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
        if (layout != null)
        {
            layout.ColumnStyles[0].Width = _sidebarVisible ? SidebarWidth : 0;
            _sidebarHost.Visible = _sidebarVisible;
            layout.PerformLayout();
            LayoutToolbars();
        }
    }

    // ================= 窗口行为 =================

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        SaveWindowState();
        base.OnFormClosing(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        MaximizedBounds = Screen.FromControl(this).WorkingArea;
        if (WindowState != FormWindowState.Maximized)
        {
            WindowState = FormWindowState.Maximized;
        }
        ConfigureSplitter();
        LayoutTitleBar();
        LayoutToolbars();
        PositionToast();
        LayoutToolbars();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcHitTest && WindowState == FormWindowState.Normal)
        {
            base.WndProc(ref m);
            if ((int)m.Result == 1)
            {
                var screenPoint = new Point(unchecked((short)(long)m.LParam),
                    unchecked((short)((long)m.LParam >> 16)));
                var point = PointToClient(screenPoint);
                const int grip = 8;
                bool left = point.X <= grip;
                bool right = point.X >= ClientSize.Width - grip;
                bool top = point.Y <= grip;
                bool bottom = point.Y >= ClientSize.Height - grip;
                if (left && top) m.Result = (IntPtr)13;
                else if (right && top) m.Result = (IntPtr)14;
                else if (left && bottom) m.Result = (IntPtr)16;
                else if (right && bottom) m.Result = (IntPtr)17;
                else if (left) m.Result = (IntPtr)10;
                else if (right) m.Result = (IntPtr)11;
                else if (top) m.Result = (IntPtr)12;
                else if (bottom) m.Result = (IntPtr)15;
            }
            return;
        }
        base.WndProc(ref m);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_syncCompletedHandler != null)
            {
                _sync.SyncCompleted -= _syncCompletedHandler;
            }
            if (_dataChangedHandler != null)
            {
                _sync.DataChanged -= _dataChangedHandler;
            }
            if (_syncStateHandler != null)
            {
                _sync.StateChanged -= _syncStateHandler;
            }
            _toastTimer.Dispose();
            _searchTimer.Dispose();
            _reloadTimer.Dispose();
            _viewMenu.Dispose();
            _tips.Dispose();
            _images.Dispose();
            _http.Dispose();
            _web?.Dispose();
        }
        base.Dispose(disposing);
    }
}
