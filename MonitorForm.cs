using System.Diagnostics;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using MiniMonitor.Rss;
using MiniMonitor.Music;
using MiniMonitor.Web.Drawio;
using MiniMonitor.Web.WebTodo;
using MiniMonitor.Web.YamlMind;

namespace MiniMonitor;

/// <summary>
/// 置顶悬浮窗：下载速度 / 上传速度（MB/s）、CPU 温度 / GPU 温度（摄氏度），
/// 可选显示 CPU 占用率、内存占用、运行时长、开机时间等（右键菜单可开关）。
/// 左键拖动，右键菜单，双击托盘切换显示。
/// </summary>
public sealed class MonitorForm : Form
{
    private const string AppName = "MiniMonitor";

    private static readonly (string Label, double Value)[] OpacityLevels =
    {
        ("60%", 0.60), ("75%", 0.75), ("90%", 0.90), ("100%（不透明）", 1.00)
    };

    private static readonly (string Name, string Fore, string Back)[] Themes =
    {
        ("深邃灰（默认）", "#E6E6E6", "#141414"),
        ("终端绿", "#00E676", "#000000"),
        ("科技蓝", "#9CDCFE", "#0A1A2F"),
        ("琥珀橙", "#FFB347", "#1A1208"),
        ("极简白", "#1A1A1A", "#F5F5F5")
    };

    private readonly AppConfig _config;
    private readonly HardwareReader _hardware;
    private NetworkReader _network;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _nicMenu;
    private readonly ToolStripMenuItem _opacityMenu;
    private readonly ToolStripMenuItem _themeMenu;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _lockItem;
    private readonly ToolStripMenuItem _fullscreenHideItem;
    private readonly ToolStripMenuItem _edgeHideItem;
    private readonly ToolStripMenuItem _trayEdgeItem;
    private readonly ToolStripMenuItem _cpuLoadItem;
    private readonly ToolStripMenuItem _memoryItem;
    private readonly ToolStripMenuItem _gpuMemItem;
    private readonly ToolStripMenuItem _runTimeItem;
    private readonly ToolStripMenuItem _startTimeItem;

    private readonly Icon? _appIcon;
    private System.Diagnostics.PerformanceCounter? _cpuCounter;

    // 全屏自动隐藏
    private readonly System.Windows.Forms.Timer _fullscreenTimer;
    private bool _autoHidden;

    // 贴边自动隐藏
    private const int EdgeAnimSteps = 8;
    private enum EdgeSide { None = 0, Left = 1, Top = 2, Right = 3 }
    private readonly System.Windows.Forms.Timer _edgePollTimer;
    private readonly System.Windows.Forms.Timer _edgeAnimTimer;
    private EdgeSide _edgeSide;
    private bool _edgeHidden;
    private bool _edgeAnimating;
    private bool _edgeAnimatingHide;
    private Point _edgeShownLocation;
    private DateTime? _edgeHideAt;
    private DateTime _edgeShowAllowedAt;
    private DateTime _suppressDoubleClickUntil;
    private Point _edgeAnimStart;
    private Point _edgeAnimTarget;
    private int _edgeAnimStep;
    private bool _suppressEdgeToggle;

    // 开机时间 / 运行时长
    private DateTime _bootTime;
    private TimeSpan _runTime;

    // 定时关机倒计时窗口
    private ShutdownCountdownForm? _shutdownCountdown;

    // NTP 自动同步服务
    private readonly NtpHistoryStore _ntpHistory;
    private readonly NtpSyncManager _ntpManager;
    private NtpSyncForm? _ntpForm;

    // 全局鼠标手势
    private readonly GlobalMouseGestureService _mouseGestureService;
    private GlobalMouseGestureForm? _gestureForm;
    private bool _gestureStartTried;

    // 磁盘分析管理
    private DiskAnalysisForm? _diskAnalysisForm;

    // 全盘数据恢复
    private DataRecoveryForm? _dataRecoveryForm;

    // 截图插件
    private readonly ScreenshotPluginService _screenshotService;
    private ScreenshotManagerForm? _screenshotManagerForm;

    // 图片处理
    private ImageProcessingForm? _imageProcessingForm;

    // 通用格式转换
    private FormatConverterForm? _formatConverterForm;

    // 网络测试
    private NetworkTestForm? _networkTestForm;

    // Navicat 复刻版（默认只在点击时打开）
    private MiniMonitor.Navicat.NavicatForm? _navicatForm;

    // 无损视频截取（默认只在点击时打开）
    private MiniMonitor.Video.LosslessCutForm? _losslessCutForm;

    // XShell 复刻版（默认只在点击时打开）
    private MiniMonitor.XShell.XShellForm? _xshellForm;

    // XFTP 客户端（默认只在点击时打开）
    private MiniMonitor.XFtp.XFtpForm? _xftpForm;

    // QuickLook 复刻
    private readonly QuickLookService _quickLookService;
    private QuickLookSettingsForm? _quickLookSettingsForm;

    // FluentRSS 阅读器（默认只在点击时打开；后台按设定间隔同步）
    private readonly RssStore _rssStore;
    private readonly RssSyncService _rssSync;
    private FluentRssForm? _rssForm;
    private MusicPlayerForm? _musicForm;
    private bool _lastBalloonWasRss;

    // 内置 Web 服务（默认关闭）
    private readonly DrawioService _drawioService;
    private readonly YamlMindService _yamlMindService;
    private readonly WebTodoService _webTodoService;
    private readonly ToolStripMenuItem _drawioServiceItem;
    private readonly ToolStripMenuItem _yamlMindServiceItem;
    private readonly ToolStripMenuItem _webTodoServiceItem;
    private readonly ToolStripMenuItem _trayDrawioServiceItem;
    private readonly ToolStripMenuItem _trayYamlMindServiceItem;
    private readonly ToolStripMenuItem _trayWebTodoServiceItem;
    private bool _suppressServiceToggle;
    private bool _suppressTrayServiceToggle;

    private double _downMBps;
    private double _upMBps;
    private float? _cpuTemp;
    private float? _gpuTemp;
    private double _cpuLoadPercent;
    private double _memUsedGB;
    private double _memTotalGB;
    private double _gpuMemUsedGB;
    private double _gpuMemTotalGB;

    private Color _foreColor;
    private Color _backColor;
    private Font? _labelFont;
    private Font? _valueFont;
    private int _pad;
    private int _rowHeight;

    private bool _dragging;
    private bool _dragMoved;
    private Point _dragOffset;
    private Point _dragOrigin;

    public MonitorForm()
    {
        _config = AppConfig.Load();
        // 启动时同步“通用格式转换”资源管理器右键菜单（默认关闭）。
        ExplorerContextMenu.SetEnabled(_config.ExplorerContextMenuEnabled,
            Application.ExecutablePath, out _);
        _ntpHistory = new NtpHistoryStore();
        _ntpManager = new NtpSyncManager(_config.NtpAutoSync, _ntpHistory);
        _mouseGestureService = new GlobalMouseGestureService();
        _screenshotService = new ScreenshotPluginService(_config);
        _quickLookService = new QuickLookService(_config);
        _rssStore = new RssStore();
        _rssSync = new RssSyncService(_rssStore, _config);
        _rssSync.SyncCompleted += OnRssSyncCompleted;
        _rssSync.DataChanged += OnRssDataChanged;
        _drawioService = new DrawioService();
        _yamlMindService = new YamlMindService();
        _webTodoService = new WebTodoService();

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        MaximizeBox = false;
        MinimizeBox = false;
        Text = AppName;

        _foreColor = ColorTranslator.FromHtml(_config.ForeColor);
        _backColor = ColorTranslator.FromHtml(_config.BackColor);
        BackColor = _backColor;
        ForeColor = _foreColor;
        Opacity = _config.Opacity;

        SetStyle(ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.StandardDoubleClick
            | ControlStyles.ResizeRedraw, true);

        _hardware = new HardwareReader();
        _network = new NetworkReader(_config.NicId);
        _bootTime = ReadBootTime();
        _runTime = DateTime.Now - _bootTime;
        _appIcon = LoadAppIcon();
        Icon = _appIcon;

        // ---------- 右键菜单 ----------
        _nicMenu = new ToolStripMenuItem("网卡选择(&N)");
        _opacityMenu = new ToolStripMenuItem("透明度(&O)");
        _themeMenu = new ToolStripMenuItem("配色主题(&T)");
        _autoStartItem = new ToolStripMenuItem("开机自启(&A)") { CheckOnClick = true };
        _lockItem = new ToolStripMenuItem("锁定窗口位置(&L)") { CheckOnClick = true };
        _fullscreenHideItem = new ToolStripMenuItem("全屏自动隐藏(&F)")
        {
            CheckOnClick = true,
            Checked = _config.AutoHideFullscreen
        };
        _edgeHideItem = new ToolStripMenuItem("贴边自动隐藏(&E)")
        {
            CheckOnClick = true,
            Checked = _config.EdgeAutoHide
        };
        _cpuLoadItem = new ToolStripMenuItem("显示 CPU 占用率(&P)") { CheckOnClick = true };
        _memoryItem = new ToolStripMenuItem("显示内存占用(&M)") { CheckOnClick = true };
        _gpuMemItem = new ToolStripMenuItem("显示 GPU 内存(&G)") { CheckOnClick = true };
        _runTimeItem = new ToolStripMenuItem("显示运行时长(&R)") { CheckOnClick = true };
        _startTimeItem = new ToolStripMenuItem("显示开机时间(&S)") { CheckOnClick = true };
        _drawioServiceItem = new ToolStripMenuItem("drawio服务50000") { CheckOnClick = true };
        _yamlMindServiceItem = new ToolStripMenuItem("YamlMind服务50003") { CheckOnClick = true };
        _webTodoServiceItem = new ToolStripMenuItem("Webtodo服务50004") { CheckOnClick = true };
        var hideItem = new ToolStripMenuItem("隐藏到托盘(&H)");
        var xshellItem = new ToolStripMenuItem("XShell复刻版(&T)");
        var rssItem = new ToolStripMenuItem("FluentRSS阅读器(&D)");
        var musicItem = new ToolStripMenuItem("离线音乐播放器(&M)");
        var hwInfoItem = new ToolStripMenuItem("硬件信息查看(&V)");
        var shutdownItem = new ToolStripMenuItem("自定义关机(&U)");
        var wipeItem = new ToolStripMenuItem("磁盘安全覆写(&W)");
        var ntpItem = new ToolStripMenuItem("NTP 同步管理(&Y)");
        var gestureItem = new ToolStripMenuItem("全局鼠标手势(&Z)");
        var diskAnalysisItem = new ToolStripMenuItem("磁盘分析管理(&K)");
        var dataRecoveryItem = new ToolStripMenuItem("全盘数据恢复(&R)");
        var screenshotItem = new ToolStripMenuItem("截图插件(&C)");
        var imageToolsItem = new ToolStripMenuItem("图片处理(&B)");
        var formatConverterItem = new ToolStripMenuItem("通用格式转换(&J)");
        var networkTestItem = new ToolStripMenuItem("网络测试(&E)");
        var navicatItem = new ToolStripMenuItem("Navicat复刻版(&V)");
        var quickLookItem = new ToolStripMenuItem("QuickLook复刻(&Q)");
        var losslessCutItem = new ToolStripMenuItem("无损视频截取(&L)");
        var aboutItem = new ToolStripMenuItem("关于(&I)");
        var xftpItem = new ToolStripMenuItem("XFtp复刻版(&F)");
        var exitItem = new ToolStripMenuItem("退出(&X)");

        _nicMenu.DropDownOpening += (_, _) => BuildNicMenu();
        _opacityMenu.DropDownOpening += (_, _) => BuildOpacityMenu();
        _themeMenu.DropDownOpening += (_, _) => BuildThemeMenu();
        // 默认开机自启：确保计划任务存在（幂等，exe 路径变化时自动更新），用户手动关闭后不会再强制开启
        if (_config.AutoStart)
        {
            bool byTask = AutoStartManager.EnsureEnabled();
            if (!byTask)
            {
                try
                {
                    MessageBox.Show(
                        "任务计划程序不可用，开机自启已回退到注册表 Run 方式。\r\n"
                        + "本程序需要管理员权限，Win10 / Win11 登录时 UAC 可能静默拦截 Run 方式，"
                        + "开机自启可能无效；可在右键菜单重新开关一次重试。",
                        AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch
                {
                    // 提示失败不影响启动。
                }
            }
        }
        _autoStartItem.Checked = AutoStartManager.IsEnabled();
        _autoStartItem.CheckedChanged += (_, _) =>
        {
            _config.AutoStart = _autoStartItem.Checked;
            _config.Save();
            AutoStartManager.SetEnabled(_autoStartItem.Checked);
        };
        _lockItem.Checked = _config.Locked;
        _lockItem.CheckedChanged += (_, _) =>
        {
            _config.Locked = _lockItem.Checked;
            _config.Save();
        };
        _fullscreenHideItem.CheckedChanged += (_, _) =>
        {
            _config.AutoHideFullscreen = _fullscreenHideItem.Checked;
            _config.Save();
            if (_config.AutoHideFullscreen)
            {
                EvaluateFullscreen();
            }
            else if (_autoHidden)
            {
                // 关闭该功能时，若正因全屏被自动隐藏，立即恢复显示
                _autoHidden = false;
                Show();
            }
        };
        _edgeHideItem.CheckedChanged += (_, _) => SetEdgeAutoHide(_edgeHideItem.Checked);
        _cpuLoadItem.Checked = _config.ShowCpuLoad;
        _cpuLoadItem.CheckedChanged += (_, _) =>
        {
            _config.ShowCpuLoad = _cpuLoadItem.Checked;
            _config.Save();
            LayoutForDpi();
            Invalidate();
        };
        _memoryItem.Checked = _config.ShowMemory;
        _memoryItem.CheckedChanged += (_, _) =>
        {
            _config.ShowMemory = _memoryItem.Checked;
            _config.Save();
            LayoutForDpi();
            Invalidate();
        };
        _gpuMemItem.Checked = _config.ShowGpuMem;
        _gpuMemItem.CheckedChanged += (_, _) =>
        {
            _config.ShowGpuMem = _gpuMemItem.Checked;
            _config.Save();
            LayoutForDpi();
            Invalidate();
        };
        _runTimeItem.Checked = _config.ShowRunTime;
        _runTimeItem.CheckedChanged += (_, _) =>
        {
            _config.ShowRunTime = _runTimeItem.Checked;
            _config.Save();
            LayoutForDpi();
            Invalidate();
        };
        _startTimeItem.Checked = _config.ShowStartTime;
        _startTimeItem.CheckedChanged += (_, _) =>
        {
            _config.ShowStartTime = _startTimeItem.Checked;
            _config.Save();
            LayoutForDpi();
            Invalidate();
        };
        _drawioServiceItem.Checked = _config.DrawioServiceEnabled;
        _drawioServiceItem.CheckedChanged += (_, _) =>
        {
            ApplyDrawioService();
            SyncTrayServiceItems();
        };
        _yamlMindServiceItem.Checked = _config.YamlMindServiceEnabled;
        _yamlMindServiceItem.CheckedChanged += (_, _) =>
        {
            ApplyYamlMindService();
            SyncTrayServiceItems();
        };
        _webTodoServiceItem.Checked = _config.WebTodoServiceEnabled;
        _webTodoServiceItem.CheckedChanged += (_, _) =>
        {
            ApplyWebTodoService();
            SyncTrayServiceItems();
        };
        hideItem.Click += (_, _) => Hide();
        xshellItem.Click += (_, _) => ShowXShell();
        rssItem.Click += (_, _) => ShowFluentRss();
        musicItem.Click += (_, _) => ShowMusicPlayer();
        hwInfoItem.Click += (_, _) => ShowHardwareInfo();
        shutdownItem.Click += (_, _) => ShowShutdownDialog();
        wipeItem.Click += (_, _) => ShowDiskWipe();
        ntpItem.Click += (_, _) => ShowNtpManager();
        gestureItem.Click += (_, _) => ShowGestureManager();
        diskAnalysisItem.Click += (_, _) => ShowDiskAnalysis();
        dataRecoveryItem.Click += (_, _) => ShowDataRecovery();
        screenshotItem.Click += (_, _) => ShowScreenshotPlugin();
        imageToolsItem.Click += (_, _) => ShowImageProcessing();
        formatConverterItem.Click += (_, _) => ShowFormatConverter();
        networkTestItem.Click += (_, _) => ShowNetworkTest();
        navicatItem.Click += (_, _) => ShowNavicat();
        quickLookItem.Click += (_, _) => ShowQuickLookSettings();
        losslessCutItem.Click += (_, _) => ShowLosslessCut();
        aboutItem.Click += (_, _) => ShowAbout();
        xftpItem.Click += (_, _) => ShowXFtp();
        exitItem.Click += (_, _) => Quit();

        _menu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.System };
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _nicMenu,
            _opacityMenu,
            _themeMenu,
            new ToolStripSeparator(),
            _autoStartItem,
            _lockItem,
            _fullscreenHideItem,
            new ToolStripSeparator(),
            _cpuLoadItem,
            _memoryItem,
            _gpuMemItem,
            _runTimeItem,
            _startTimeItem,
            new ToolStripSeparator(),
            _drawioServiceItem,
            _yamlMindServiceItem,
            _webTodoServiceItem,
            new ToolStripSeparator(),
            xshellItem,
            xftpItem,
            rssItem,
            musicItem,
            hwInfoItem,
            shutdownItem,
            wipeItem,
            ntpItem,
            gestureItem,
            diskAnalysisItem,
            dataRecoveryItem,
            screenshotItem,
            imageToolsItem,
            formatConverterItem,
            networkTestItem,
            navicatItem,
            quickLookItem,
            losslessCutItem,
            aboutItem,
            new ToolStripSeparator(),
            hideItem,
            _edgeHideItem,
            exitItem
        });
        ContextMenuStrip = _menu;

        // ---------- 托盘 ----------
        _tray = new NotifyIcon
        {
            Text = AppName,
            Icon = _appIcon ?? CreateTrayIcon(),
            Visible = true
        };
        _tray.DoubleClick += (_, _) =>
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        };
        var trayMenu = new ContextMenuStrip();
        _trayDrawioServiceItem = new ToolStripMenuItem("drawio服务50000")
        {
            CheckOnClick = true,
            Checked = _config.DrawioServiceEnabled
        };
        _trayYamlMindServiceItem = new ToolStripMenuItem("YamlMind服务50003")
        {
            CheckOnClick = true,
            Checked = _config.YamlMindServiceEnabled
        };
        _trayWebTodoServiceItem = new ToolStripMenuItem("Webtodo服务50004")
        {
            CheckOnClick = true,
            Checked = _config.WebTodoServiceEnabled
        };
        _trayDrawioServiceItem.CheckedChanged += (_, _) =>
        {
            if (_suppressTrayServiceToggle)
            {
                return;
            }
            _drawioServiceItem.Checked = _trayDrawioServiceItem.Checked;
        };
        _trayYamlMindServiceItem.CheckedChanged += (_, _) =>
        {
            if (_suppressTrayServiceToggle)
            {
                return;
            }
            _yamlMindServiceItem.Checked = _trayYamlMindServiceItem.Checked;
        };
        _trayWebTodoServiceItem.CheckedChanged += (_, _) =>
        {
            if (_suppressTrayServiceToggle)
            {
                return;
            }
            _webTodoServiceItem.Checked = _trayWebTodoServiceItem.Checked;
        };
        trayMenu.Items.Add(_trayDrawioServiceItem);
        trayMenu.Items.Add(_trayYamlMindServiceItem);
        trayMenu.Items.Add(_trayWebTodoServiceItem);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("XShell 复刻版", null, (_, _) => ShowXShell());
        trayMenu.Items.Add("XFtp 复刻版", null, (_, _) => ShowXFtp());
        trayMenu.Items.Add("网络测试", null, (_, _) => ShowNetworkTest());
        trayMenu.Items.Add("FluentRSS 阅读器", null, (_, _) => ShowFluentRss());
        trayMenu.Items.Add("离线音乐播放器", null, (_, _) => ShowMusicPlayer());
        trayMenu.Items.Add("Navicat 复刻版", null, (_, _) => ShowNavicat());
        trayMenu.Items.Add("全盘数据恢复", null, (_, _) => ShowDataRecovery());
        trayMenu.Items.Add(new ToolStripSeparator());
        _trayEdgeItem = new ToolStripMenuItem("贴边自动隐藏")
        {
            CheckOnClick = true,
            Checked = _config.EdgeAutoHide
        };
        _trayEdgeItem.CheckedChanged += (_, _) => SetEdgeAutoHide(_trayEdgeItem.Checked);
        trayMenu.Items.Add(_trayEdgeItem);
        trayMenu.Items.Add("显示主窗口", null, (_, _) =>
        {
            Show();
            Activate();
        });
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("退出", null, (_, _) => Quit());
        _tray.ContextMenuStrip = trayMenu;
        _tray.BalloonTipClicked += (_, _) =>
        {
            if (_lastBalloonWasRss)
            {
                _lastBalloonWasRss = false;
                ShowFluentRss();
            }
        };

        // ---------- 定时刷新 ----------
        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += OnTick;

        // ---------- 全屏自动隐藏（300ms 轻量轮询前台窗口） ----------
        _fullscreenTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _fullscreenTimer.Tick += (_, _) => EvaluateFullscreen();

        // ---------- 贴边自动隐藏 ----------
        _edgePollTimer = new System.Windows.Forms.Timer { Interval = 120 };
        _edgePollTimer.Tick += (_, _) => EdgePollTick();
        _edgeAnimTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _edgeAnimTimer.Tick += (_, _) => EdgeAnimTick();

        LayoutForDpi();
        Load += (_, _) =>
        {
            PlaceWindow();
            StartEmbeddedServicesAtLaunch();
            _rssSync.Start();
            UpdateRssTrayText();
            OnTick(this, EventArgs.Empty);
            _timer.Start();
            _fullscreenTimer.Start();
            if (_config.EdgeAutoHide)
            {
                RestoreEdgeOnStartup();
                _edgePollTimer.Start();
            }
        };
    }

    // ================= 内置 Web 服务（drawio / YamlMind / WebTodo） =================

    private void StartEmbeddedServicesAtLaunch()
    {
        if (_config.DrawioServiceEnabled)
        {
            try
            {
                _drawioService.Start();
            }
            catch (Exception ex)
            {
                _config.DrawioServiceEnabled = false;
                _config.Save();
                _suppressServiceToggle = true;
                _drawioServiceItem.Checked = false;
                _suppressServiceToggle = false;
                SyncTrayServiceItems();
                ShowServiceBalloon("drawio 服务启动失败", ex.Message, ToolTipIcon.Error);
            }
        }
        if (_config.YamlMindServiceEnabled)
        {
            try
            {
                _yamlMindService.Start();
            }
            catch (Exception ex)
            {
                _config.YamlMindServiceEnabled = false;
                _config.Save();
                _suppressServiceToggle = true;
                _yamlMindServiceItem.Checked = false;
                _suppressServiceToggle = false;
                SyncTrayServiceItems();
                ShowServiceBalloon("YamlMind 服务启动失败", ex.Message, ToolTipIcon.Error);
            }
        }
        if (_config.WebTodoServiceEnabled)
        {
            try
            {
                _webTodoService.Start();
            }
            catch (Exception ex)
            {
                _config.WebTodoServiceEnabled = false;
                _config.Save();
                _suppressServiceToggle = true;
                _webTodoServiceItem.Checked = false;
                _suppressServiceToggle = false;
                SyncTrayServiceItems();
                ShowServiceBalloon("Webtodo 服务启动失败", ex.Message, ToolTipIcon.Error);
            }
        }
    }

    private void ApplyDrawioService()
    {
        if (_suppressServiceToggle)
        {
            return;
        }
        if (_drawioServiceItem.Checked)
        {
            try
            {
                _drawioService.Start();
                _config.DrawioServiceEnabled = true;
                _config.Save();
                ShowServiceBalloon("drawio 服务已启动",
                    $"打开 http://localhost:{_drawioService.Port}/ 即可使用", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                _config.DrawioServiceEnabled = false;
                _config.Save();
                _suppressServiceToggle = true;
                _drawioServiceItem.Checked = false;
                _suppressServiceToggle = false;
                ShowServiceBalloon("drawio 服务启动失败", ex.Message, ToolTipIcon.Error);
            }
        }
        else
        {
            _drawioService.Stop();
            _config.DrawioServiceEnabled = false;
            _config.Save();
        }
    }

    private void ApplyYamlMindService()
    {
        if (_suppressServiceToggle)
        {
            return;
        }
        if (_yamlMindServiceItem.Checked)
        {
            try
            {
                _yamlMindService.Start();
                _config.YamlMindServiceEnabled = true;
                _config.Save();
                ShowServiceBalloon("YamlMind 服务已启动",
                    $"打开 http://localhost:{_yamlMindService.Port}/ 即可使用", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                _config.YamlMindServiceEnabled = false;
                _config.Save();
                _suppressServiceToggle = true;
                _yamlMindServiceItem.Checked = false;
                _suppressServiceToggle = false;
                ShowServiceBalloon("YamlMind 服务启动失败", ex.Message, ToolTipIcon.Error);
            }
        }
        else
        {
            _yamlMindService.Stop();
            _config.YamlMindServiceEnabled = false;
            _config.Save();
        }
    }

    private void ApplyWebTodoService()
    {
        if (_suppressServiceToggle)
        {
            return;
        }
        if (_webTodoServiceItem.Checked)
        {
            try
            {
                _webTodoService.Start();
                _config.WebTodoServiceEnabled = true;
                _config.Save();
                ShowServiceBalloon("Webtodo 服务已启动",
                    $"打开 http://localhost:{_webTodoService.Port}/ 即可使用", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                _config.WebTodoServiceEnabled = false;
                _config.Save();
                _suppressServiceToggle = true;
                _webTodoServiceItem.Checked = false;
                _suppressServiceToggle = false;
                ShowServiceBalloon("Webtodo 服务启动失败", ex.Message, ToolTipIcon.Error);
            }
        }
        else
        {
            _webTodoService.Stop();
            _config.WebTodoServiceEnabled = false;
            _config.Save();
        }
    }

    private void ShowServiceBalloon(string title, string message, ToolTipIcon icon)
    {
        try
        {
            _tray.ShowBalloonTip(3000, title, message, icon);
        }
        catch
        {
            // 托盘气泡失败不影响服务本身
        }
    }

    /// <summary>把主菜单的服务开关状态同步到托盘菜单。</summary>
    private void SyncTrayServiceItems()
    {
        _suppressTrayServiceToggle = true;
        try
        {
            _trayDrawioServiceItem.Checked = _drawioServiceItem.Checked;
            _trayYamlMindServiceItem.Checked = _yamlMindServiceItem.Checked;
            _trayWebTodoServiceItem.Checked = _webTodoServiceItem.Checked;
        }
        finally
        {
            _suppressTrayServiceToggle = false;
        }
    }

    // ================= 布局与绘制 =================

    private void LayoutForDpi()
    {
        float scale = DeviceDpi / 96f;
        _pad = (int)(7 * scale);
        _rowHeight = (int)(20 * scale);
        int rowCount = RowCount();
        // 内存/显存/运行时长/开机时间行内容较长（如 88.2/256G 51%、GPUMEM 2.0G、2天 05:12），开启时加宽窗口
        bool wide = _config.ShowMemory || _config.ShowGpuMem
            || _config.ShowRunTime || _config.ShowStartTime;
        int width = (int)((wide ? 190 : 152) * scale);
        int height = _pad * 2 + _rowHeight * rowCount;
        ClientSize = new Size(width, height);

        _labelFont?.Dispose();
        _valueFont?.Dispose();
        _labelFont = new Font("Microsoft YaHei UI", 12f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        _valueFont = new Font("Consolas", 13f * scale, FontStyle.Bold, GraphicsUnit.Pixel);

        ApplyRoundRect((int)(10 * scale));
    }

    private int RowCount()
    {
        int count = 4; // 下载 / 上传 / CPU 温度 / GPU 温度
        if (_config.ShowCpuLoad)
        {
            count++;
        }
        if (_config.ShowMemory)
        {
            count++;
        }
        if (_config.ShowGpuMem)
        {
            count++;
        }
        if (_config.ShowRunTime)
        {
            count++;
        }
        if (_config.ShowStartTime)
        {
            count++;
        }
        return count;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // 句柄创建后才能拿到真实的显示器 DPI，按真实 DPI 重新布局一次
        LayoutForDpi();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        LayoutForDpi();
        if (_edgeSide == EdgeSide.None)
        {
            return;
        }
        _edgeAnimTimer.Stop();
        _edgeAnimating = false;
        var area = Screen.FromPoint(
            new Point(_edgeShownLocation.X + Width / 2,
                _edgeShownLocation.Y + Height / 2)).WorkingArea;
        _edgeShownLocation = ClampShownLocation(_edgeSide, area, _edgeShownLocation);
        Location = _edgeHidden
            ? HiddenLocationFor(_edgeSide, _edgeShownLocation)
            : _edgeShownLocation;
    }

    private void ApplyRoundRect(int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        int w = Width;
        int h = Height;
        path.AddArc(0, 0, d, d, 180, 90);
        path.AddArc(w - d, 0, d, d, 270, 90);
        path.AddArc(w - d, h - d, d, d, 0, 90);
        path.AddArc(0, h - d, d, d, 90, 90);
        path.CloseFigure();

        var old = Region;
        Region = new Region(path);
        old?.Dispose();
        path.Dispose();
    }

    private void PlaceWindow()
    {
        if (_config.X is int x && _config.Y is int y)
        {
            Location = new Point(x, y);
            EnsureOnScreen();
        }
        else
        {
            var area = Screen.PrimaryScreen?.WorkingArea
                ?? new Rectangle(0, 0, 1280, 720);
            Location = new Point(area.Right - Width - 20, area.Bottom - Height - 20);
        }
    }

    /// <summary>分辨率变化或换显示器后，保证窗口没有跑到屏幕外。</summary>
    private void EnsureOnScreen()
    {
        var area = Screen.FromPoint(Location).WorkingArea;
        int x = Math.Clamp(Location.X, area.Left, area.Right - 60);
        int y = Math.Clamp(Location.Y, area.Top, area.Bottom - 30);
        Location = new Point(x, y);
    }

    // ================= 贴边自动隐藏 =================

    private int EdgePeek => Math.Max(6, (int)Math.Round(6 * DeviceDpi / 96.0));

    private void SetEdgeAutoHide(bool enabled)
    {
        if (_suppressEdgeToggle)
        {
            return;
        }
        _suppressEdgeToggle = true;
        try
        {
            if (_edgeHideItem.Checked != enabled)
            {
                _edgeHideItem.Checked = enabled;
            }
            if (_trayEdgeItem.Checked != enabled)
            {
                _trayEdgeItem.Checked = enabled;
            }
        }
        finally
        {
            _suppressEdgeToggle = false;
        }

        if (_config.EdgeAutoHide == enabled)
        {
            if (enabled)
            {
                _edgePollTimer.Start();
            }
            return;
        }
        _config.EdgeAutoHide = enabled;
        _config.Save();
        if (enabled)
        {
            _edgePollTimer.Start();
            try
            {
                _tray.ShowBalloonTip(3000, "贴边自动隐藏已开启",
                    "把悬浮窗拖到屏幕上 / 左 / 右边缘即可自动吸附隐藏，鼠标悬停边缘时显示。",
                    ToolTipIcon.Info);
            }
            catch
            {
                // 气泡提示失败不影响功能
            }
        }
        else
        {
            _edgePollTimer.Stop();
            _edgeAnimTimer.Stop();
            _edgeAnimating = false;
            _edgeHideAt = null;
            RestoreFromEdge();
        }
    }

    private void RestoreEdgeOnStartup()
    {
        var side = (EdgeSide)_config.EdgeAutoHideSide;
        if (side == EdgeSide.None)
        {
            return;
        }
        var area = Screen.FromPoint(
            new Point(Location.X + Width / 2, Location.Y + Height / 2)).WorkingArea;
        if (!IsOuterEdge(side, area))
        {
            _config.EdgeAutoHideSide = 0;
            _config.Save();
            return;
        }
        _edgeSide = side;
        _edgeShownLocation = ClampShownLocation(side, area, Location);
        Location = _edgeShownLocation;
        HideToEdge(animate: false);
    }

    private static bool IsOuterEdge(EdgeSide side, Rectangle area)
    {
        var virtualScreen = SystemInformation.VirtualScreen;
        return side switch
        {
            EdgeSide.Left => area.Left <= virtualScreen.Left,
            EdgeSide.Top => area.Top <= virtualScreen.Top,
            EdgeSide.Right => area.Right >= virtualScreen.Right,
            _ => false
        };
    }

    private Point ClampShownLocation(EdgeSide side, Rectangle area, Point current)
    {
        int maxX = Math.Max(area.Left, area.Right - Width);
        int maxY = Math.Max(area.Top, area.Bottom - Height);
        int x = Math.Clamp(current.X, area.Left, maxX);
        int y = Math.Clamp(current.Y, area.Top, maxY);
        return side switch
        {
            EdgeSide.Left => new Point(area.Left, y),
            EdgeSide.Top => new Point(x, area.Top),
            EdgeSide.Right => new Point(maxX, y),
            _ => new Point(x, y)
        };
    }

    private Point HiddenLocationFor(EdgeSide side, Point shown)
    {
        int peek = EdgePeek;
        return side switch
        {
            EdgeSide.Left => new Point(shown.X - Width + peek, shown.Y),
            EdgeSide.Top => new Point(shown.X, shown.Y - Height + peek),
            EdgeSide.Right => new Point(shown.X + Width - peek, shown.Y),
            _ => shown
        };
    }

    private void HideToEdge(bool animate)
    {
        if (_edgeSide == EdgeSide.None)
        {
            return;
        }
        Point target = HiddenLocationFor(_edgeSide, _edgeShownLocation);
        if (!animate)
        {
            _edgeAnimTimer.Stop();
            _edgeAnimating = false;
            Location = target;
            _edgeHidden = true;
            _edgeShowAllowedAt = DateTime.UtcNow.AddMilliseconds(600);
            Invalidate();
            return;
        }
        if (_edgeHidden)
        {
            return;
        }
        StartEdgeAnimation(target, hide: true);
    }

    private void ShowFromEdge()
    {
        if (_edgeSide == EdgeSide.None || DateTime.UtcNow < _edgeShowAllowedAt)
        {
            return;
        }
        if (!_edgeHidden && !(_edgeAnimating && _edgeAnimatingHide))
        {
            _edgeHideAt = null;
            return;
        }
        if (_edgeAnimating && !_edgeAnimatingHide)
        {
            return;
        }
        var area = Screen.FromPoint(
            new Point(_edgeShownLocation.X + Width / 2,
                _edgeShownLocation.Y + Height / 2)).WorkingArea;
        _edgeShownLocation = ClampShownLocation(_edgeSide, area, _edgeShownLocation);
        StartEdgeAnimation(_edgeShownLocation, hide: false);
        _edgeHideAt = null;
        Invalidate();
    }

    private void StartEdgeAnimation(Point target, bool hide)
    {
        _edgeAnimTimer.Stop();
        _edgeAnimStart = Location;
        _edgeAnimTarget = target;
        _edgeAnimStep = 0;
        _edgeAnimating = true;
        _edgeAnimatingHide = hide;
        Invalidate();
        if (_edgeAnimStart == _edgeAnimTarget)
        {
            _edgeAnimating = false;
            _edgeHidden = hide;
            if (hide)
            {
                _edgeShowAllowedAt = DateTime.UtcNow.AddMilliseconds(600);
            }
            Invalidate();
            return;
        }
        _edgeAnimTimer.Start();
    }

    private void EdgeAnimTick()
    {
        _edgeAnimStep++;
        double t = Math.Min(1.0, _edgeAnimStep / (double)EdgeAnimSteps);
        double eased = 1 - Math.Pow(1 - t, 3);
        Location = new Point(
            _edgeAnimStart.X + (int)Math.Round((_edgeAnimTarget.X - _edgeAnimStart.X) * eased),
            _edgeAnimStart.Y + (int)Math.Round((_edgeAnimTarget.Y - _edgeAnimStart.Y) * eased));
        if (t >= 1)
        {
            _edgeAnimTimer.Stop();
            _edgeAnimating = false;
            _edgeHidden = _edgeAnimatingHide;
            if (_edgeHidden)
            {
                _edgeShowAllowedAt = DateTime.UtcNow.AddMilliseconds(600);
            }
            else
            {
                _edgeHideAt = null;
            }
            Invalidate();
        }
    }

    private void ScheduleEdgeHide()
    {
        if (!_config.EdgeAutoHide || _edgeSide == EdgeSide.None || _dragging)
        {
            return;
        }
        _edgeHideAt ??= DateTime.UtcNow.AddMilliseconds(350);
    }

    private void EdgePollTick()
    {
        if (!_config.EdgeAutoHide || !Visible || _autoHidden
            || _edgeAnimating)
        {
            return;
        }
        if (_edgeSide == EdgeSide.None && !TryAdoptCurrentEdge())
        {
            return;
        }
        bool menuOpen = _menu.Visible || (_tray.ContextMenuStrip?.Visible ?? false);
        if (_edgeHidden)
        {
            var hot = Bounds;
            int margin = EdgePeek + 6;
            hot.Inflate(margin, margin);
            if (hot.Contains(Cursor.Position))
            {
                ShowFromEdge();
            }
            return;
        }
        if (_dragging || menuOpen)
        {
            _edgeHideAt = null;
            return;
        }
        var keep = Bounds;
        int keepMargin = Math.Max(12, EdgePeek * 2);
        keep.Inflate(keepMargin, keepMargin);
        if (keep.Contains(Cursor.Position))
        {
            _edgeHideAt = null;
            return;
        }
        _edgeHideAt ??= DateTime.UtcNow.AddMilliseconds(350);
        if (DateTime.UtcNow >= _edgeHideAt.Value)
        {
            _edgeHideAt = null;
            HideToEdge(animate: true);
        }
    }

    /// <summary>
    /// 窗口在没有发生拖动的情况下已经贴在屏幕外边缘时（例如启动恢复位置、
    /// 其它程序调整窗口位置后），主动识别贴边方向，保证贴边隐藏仍能触发。
    /// </summary>
    private bool TryAdoptCurrentEdge()
    {
        if (WindowState != FormWindowState.Normal)
        {
            return false;
        }
        var area = Screen.FromPoint(
            new Point(Location.X + Width / 2, Location.Y + Height / 2)).WorkingArea;
        int threshold = Math.Max(6, (int)Math.Round(10 * DeviceDpi / 96.0));
        int left = Math.Abs(Location.X - area.Left);
        int top = Math.Abs(Location.Y - area.Top);
        int right = Math.Abs(area.Right - (Location.X + Width));
        int nearest = Math.Min(left, Math.Min(top, right));
        if (nearest > threshold)
        {
            return false;
        }
        EdgeSide side = left == nearest ? EdgeSide.Left
            : top == nearest ? EdgeSide.Top
            : EdgeSide.Right;
        if (!IsOuterEdge(side, area))
        {
            return false;
        }
        _edgeSide = side;
        _edgeShownLocation = ClampShownLocation(side, area, Location);
        Location = _edgeShownLocation;
        _edgeHidden = false;
        _edgeHideAt = null;
        _config.X = _edgeShownLocation.X;
        _config.Y = _edgeShownLocation.Y;
        _config.EdgeAutoHideSide = (int)side;
        _config.Save();
        return true;
    }

    private void RestoreFromEdge()
    {
        if (_edgeSide == EdgeSide.None)
        {
            _edgeHidden = false;
            return;
        }
        Point restore = _edgeShownLocation;
        _edgeAnimTimer.Stop();
        _edgeAnimating = false;
        _edgeHidden = false;
        _edgeSide = EdgeSide.None;
        _edgeHideAt = null;
        Location = restore;
        _config.X = restore.X;
        _config.Y = restore.Y;
        _config.EdgeAutoHideSide = 0;
        _config.Save();
        Invalidate();
    }

    private void FinishEdgeDrag()
    {
        if (_config.EdgeAutoHide)
        {
            var area = Screen.FromPoint(
                new Point(Location.X + Width / 2, Location.Y + Height / 2)).WorkingArea;
            int threshold = Math.Max(14, (int)Math.Round(20 * DeviceDpi / 96.0));
            int leftDistance = Math.Abs(Location.X - area.Left);
            int topDistance = Math.Abs(Location.Y - area.Top);
            int rightDistance = Math.Abs(area.Right - (Location.X + Width));
            int nearest = Math.Min(leftDistance, Math.Min(topDistance, rightDistance));
            EdgeSide side = EdgeSide.None;
            if (nearest <= threshold)
            {
                side = leftDistance == nearest ? EdgeSide.Left
                    : topDistance == nearest ? EdgeSide.Top
                    : EdgeSide.Right;
                if (!IsOuterEdge(side, area))
                {
                    side = EdgeSide.None;
                }
            }
            if (side != EdgeSide.None)
            {
                _edgeSide = side;
                _edgeShownLocation = ClampShownLocation(side, area, Location);
                Location = _edgeShownLocation;
                _edgeHidden = false;
                _config.X = _edgeShownLocation.X;
                _config.Y = _edgeShownLocation.Y;
                _config.EdgeAutoHideSide = (int)side;
                _config.Save();
                HideToEdge(animate: true);
                return;
            }
            _edgeSide = EdgeSide.None;
            _config.EdgeAutoHideSide = 0;
        }
        _config.X = Location.X;
        _config.Y = Location.Y;
        _config.Save();
    }

    private static Icon CreateTrayIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var bg = new SolidBrush(Color.FromArgb(20, 20, 20));
            g.FillRectangle(bg, 0, 0, 32, 32);
            using var pen = new Pen(Color.LimeGreen, 2);
            g.DrawRectangle(pen, 1, 1, 30, 30);
            using var font = new Font("Consolas", 18f, FontStyle.Bold, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, "M", font, new Rectangle(0, 2, 32, 30), Color.LimeGreen,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private void OnTick(object? sender, EventArgs e)
    {
        (_downMBps, _upMBps) = _network.Sample();
        _cpuTemp = _hardware.CpuTemperature;
        _gpuTemp = _hardware.GpuTemperature;

        if (_config.ShowCpuLoad)
        {
            _cpuLoadPercent = ReadCpuLoad();
        }
        if (_config.ShowMemory)
        {
            (_memUsedGB, _memTotalGB) = ReadMemory();
        }
        if (_config.ShowGpuMem)
        {
            (_gpuMemTotalGB, _gpuMemUsedGB) = HardwareReader.GpuMemoryInfo();
        }
        if (_config.ShowRunTime || _config.ShowStartTime)
        {
            _runTime = DateTime.Now - _bootTime;
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_labelFont == null || _valueFont == null)
        {
            return;
        }

        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var bg = new SolidBrush(_backColor))
        {
            g.FillRectangle(bg, ClientRectangle);
        }
        using (var border = new Pen(Color.FromArgb(70, 70, 70)))
        {
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }

        var rows = new List<(string Label, string Value, Color LabelColor, Color ValueColor)>
        {
            ("↓", FormatSpeed(_downMBps), Color.FromArgb(0x4E, 0xC9, 0xB0), _foreColor),
            ("↑", FormatSpeed(_upMBps),   Color.FromArgb(0xFF, 0xB3, 0x47), _foreColor),
            ("CPU", FormatTemp(_cpuTemp), Color.FromArgb(0x9C, 0xDC, 0xFE), TempColor(_cpuTemp)),
            ("GPU", FormatTemp(_gpuTemp), Color.FromArgb(0x9C, 0xDC, 0xFE), TempColor(_gpuTemp))
        };
        if (_config.ShowCpuLoad)
        {
            rows.Add(("CPU%", FormatCpuLoad(_cpuLoadPercent),
                Color.FromArgb(0x7E, 0xE7, 0x87), _foreColor));
        }
        if (_config.ShowMemory)
        {
            rows.Add(("MEM", FormatMemory(_memUsedGB, _memTotalGB),
                Color.FromArgb(0xD2, 0xA8, 0xFF), _foreColor));
        }
        if (_config.ShowGpuMem)
        {
            rows.Add(("GPUMEM", FormatGpuMem(_gpuMemUsedGB, _gpuMemTotalGB),
                Color.FromArgb(0x4F, 0xC3, 0xF7), _foreColor));
        }
        if (_config.ShowRunTime)
        {
            rows.Add(("RUN", FormatRunTime(_runTime),
                Color.FromArgb(0xFF, 0xE0, 0x82), _foreColor));
        }
        if (_config.ShowStartTime)
        {
            rows.Add(("BOOT", FormatBootTime(_bootTime),
                Color.FromArgb(0xCE, 0x93, 0xD8), _foreColor));
        }

        const TextFormatFlags labelFlags =
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
        const TextFormatFlags valueFlags =
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
            | TextFormatFlags.NoPadding;

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            int y = _pad + i * _rowHeight;

            var labelRect = new Rectangle(_pad, y, (int)(60 * (DeviceDpi / 96f)), _rowHeight);
            TextRenderer.DrawText(g, row.Label, _labelFont, labelRect, row.LabelColor, labelFlags);

            var valueSize = TextRenderer.MeasureText(g, row.Value, _valueFont, Size.Empty, valueFlags);
            var valueRect = new Rectangle(
                Width - _pad - valueSize.Width, y, valueSize.Width, _rowHeight);
            TextRenderer.DrawText(g, row.Value, _valueFont, valueRect, row.ValueColor, valueFlags);
        }

        if (_edgeHidden && _edgeSide != EdgeSide.None)
        {
            DrawEdgeIndicator(g);
        }
    }

    /// <summary>贴边隐藏时，在屏幕外沿绘制 6px 粉红色提示条；完全展开后自动消失。</summary>
    private void DrawEdgeIndicator(Graphics g)
    {
        int peek = EdgePeek;
        var area = Screen.FromPoint(
            new Point(Location.X + Width / 2, Location.Y + Height / 2)).WorkingArea;
        using var fill = new SolidBrush(Color.FromArgb(0xFF, 0x4F, 0x8B));
        using var edge = new Pen(Color.FromArgb(0xD8, 0x1B, 0x60));
        switch (_edgeSide)
        {
            case EdgeSide.Left:
            {
                int edgeX = area.Left - Location.X;
                g.FillRectangle(fill, edgeX, 0, peek, Height);
                g.DrawLine(edge, edgeX, 0, edgeX, Height);
                break;
            }
            case EdgeSide.Right:
            {
                int edgeX = area.Right - Location.X;
                g.FillRectangle(fill, edgeX - peek, 0, peek, Height);
                g.DrawLine(edge, edgeX - 1, 0, edgeX - 1, Height);
                break;
            }
            case EdgeSide.Top:
            {
                int edgeY = area.Top - Location.Y;
                g.FillRectangle(fill, 0, edgeY, Width, peek);
                g.DrawLine(edge, 0, edgeY, Width, edgeY);
                break;
            }
        }
    }

    // ================= 显示格式 =================

    /// <summary>速度统一以 M（MB/s，兆字节/秒）显示，保留 1 位小数。</summary>
    private static string FormatSpeed(double mbps)
    {
        if (mbps < 0)
        {
            mbps = 0;
        }
        return mbps.ToString("0.0", CultureInfo.InvariantCulture) + " M";
    }

    /// <summary>温度取整显示，单位“度”；读不到时显示 --。</summary>
    private static string FormatTemp(float? temp)
    {
        return temp.HasValue
            ? Math.Round(temp.Value).ToString("0", CultureInfo.InvariantCulture) + " 度"
            : "-- 度";
    }

    // ================= 图标 =================

    /// <summary>从嵌入资源加载 app.ico（CPU + 温度计图标），失败返回 null。</summary>
    private static Icon? LoadAppIcon()
    {
        try
        {
            using var stream = typeof(MonitorForm).Assembly
                .GetManifestResourceStream("MiniMonitor.app.ico");
            return stream != null ? new Icon(stream) : null;
        }
        catch
        {
            return null;
        }
    }

    // ================= CPU 占用 / 内存（可选显示） =================

    /// <summary>CPU 总占用百分比（PerformanceCounter，Win10+ 内置）。</summary>
    private double ReadCpuLoad()
    {
        try
        {
            _cpuCounter ??= new System.Diagnostics.PerformanceCounter(
                "Processor", "% Processor Time", "_Total");
            return Math.Clamp(_cpuCounter.NextValue(), 0, 100);
        }
        catch
        {
            return 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);

    /// <summary>物理内存 (已用 GB, 总量 GB)。</summary>
    private static (double UsedGB, double TotalGB) ReadMemory()
    {
        try
        {
            var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(status) && status.ullTotalPhys > 0)
            {
                double total = status.ullTotalPhys / 1073741824.0;
                double used = (status.ullTotalPhys - status.ullAvailPhys) / 1073741824.0;
                return (used, total);
            }
        }
        catch
        {
            // 读取失败时保持 0，显示 --/--G
        }
        return (0, 0);
    }

    /// <summary>CPU 占用显示为整数百分比。</summary>
    private static string FormatCpuLoad(double percent)
    {
        return Math.Round(percent).ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>内存显示为“已用/总量G 百分比”。</summary>
    private static string FormatMemory(double usedGB, double totalGB)
    {
        if (totalGB <= 0)
        {
            return "--/--G --%";
        }
        double percent = usedGB / totalGB * 100.0;
        return usedGB.ToString("0.0", CultureInfo.InvariantCulture) + "/"
            + totalGB.ToString("0", CultureInfo.InvariantCulture) + "G "
            + Math.Round(percent).ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>GPU 专属显存显示为“已用/总量G 百分比”，与系统内存 MEM 行格式一致。</summary>
    private static string FormatGpuMem(double usedGB, double totalGB)
    {
        if (totalGB <= 0)
        {
            return "--/--G --%";
        }
        double percent = usedGB / totalGB * 100.0;
        return usedGB.ToString("0.0", CultureInfo.InvariantCulture) + "/"
            + totalGB.ToString("0.0", CultureInfo.InvariantCulture) + "G "
            + Math.Round(percent).ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    // ================= 开机时间 / 运行时长 =================

    /// <summary>
    /// 读取本次开机时间：优先 WMI Win32_OperatingSystem.LastBootUpTime（含休眠时间，
    /// 挂起恢复后也准确）；WMI 不可用时回退为“当前时间 - TickCount64”。
    /// </summary>
    private static DateTime ReadBootTime()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT LastBootUpTime FROM Win32_OperatingSystem");
            foreach (var obj in searcher.Get())
            {
                if (obj["LastBootUpTime"] is DateTime t)
                {
                    return t;
                }
            }
        }
        catch
        {
            // WMI 不可用时回退
        }
        return DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
    }

    /// <summary>运行时长显示：≥1 天为“N天 HH:MM”，不足 1 天为“HH:MM:SS”。</summary>
    private static string FormatRunTime(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
        {
            t = TimeSpan.Zero;
        }
        if (t.TotalDays >= 1)
        {
            return (int)t.TotalDays + "天 " + t.Hours.ToString("00", CultureInfo.InvariantCulture)
                + ":" + t.Minutes.ToString("00", CultureInfo.InvariantCulture);
        }
        return t.Hours.ToString("00", CultureInfo.InvariantCulture) + ":"
            + t.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":"
            + t.Seconds.ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>开机时间显示为“月-日 时:分”，如 10-31 08:12。</summary>
    private static string FormatBootTime(DateTime dt)
    {
        return dt.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    private Color TempColor(float? temp)
    {
        if (!temp.HasValue)
        {
            return Color.Gray;
        }
        if (temp.Value >= 85)
        {
            return Color.FromArgb(0xFF, 0x6B, 0x6B); // 高温红
        }
        if (temp.Value >= 75)
        {
            return Color.FromArgb(0xFF, 0xB3, 0x47); // 偏高橙
        }
        return _foreColor;
    }

    // ================= 拖动 =================

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_edgeHidden)
        {
            ShowFromEdge();
            _suppressDoubleClickUntil = DateTime.UtcNow.AddMilliseconds(600);
            return;
        }
        if (!_config.Locked && e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _dragMoved = false;
            _dragOffset = e.Location;
            _dragOrigin = Location;
            _edgeHideAt = null;
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (!_config.EdgeAutoHide || _edgeSide == EdgeSide.None)
        {
            return;
        }
        _edgeHideAt = null;
        if (_edgeHidden || (_edgeAnimating && _edgeAnimatingHide))
        {
            ShowFromEdge();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        ScheduleEdgeHide();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging && e.Button == MouseButtons.Left)
        {
            var screen = PointToScreen(e.Location);
            var target = new Point(screen.X - _dragOffset.X, screen.Y - _dragOffset.Y);
            if (!_dragMoved
                && (Math.Abs(target.X - _dragOrigin.X) > 2
                    || Math.Abs(target.Y - _dragOrigin.Y) > 2))
            {
                _dragMoved = true;
                if (_edgeSide != EdgeSide.None)
                {
                    // 从贴边位置真正拖出时先恢复为普通窗口，松开时再按新位置判断是否贴边。
                    _edgeAnimTimer.Stop();
                    _edgeAnimating = false;
                    _edgeHidden = false;
                    _edgeSide = EdgeSide.None;
                    _config.EdgeAutoHideSide = 0;
                }
            }
            Location = target;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragging)
        {
            _dragging = false;
            if (_dragMoved)
            {
                FinishEdgeDrag();
            }
            else if (_edgeSide == EdgeSide.None)
            {
                _config.X = Location.X;
                _config.Y = Location.Y;
                _config.Save();
            }
        }
    }

    /// <summary>左键双击悬浮窗：以管理员身份打开命令提示符，并定位到用户的 Downloads 目录。</summary>
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button != MouseButtons.Left
            || DateTime.UtcNow < _suppressDoubleClickUntil)
        {
            return;
        }
        _dragging = false;
        OpenAdminCommandPrompt();
    }

    private void OpenAdminCommandPrompt()
    {
        string downloads = ResolveDownloadsFolder();
        try
        {
            // /k 让命令提示符在执行 cd 后保持打开；UseShellExecute + runas 触发 UAC 提权。
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/k cd /d \"" + downloads + "\"",
                WorkingDirectory = downloads,
                UseShellExecute = true,
                Verb = "runas"
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // 用户在 UAC 提示中取消，不做处理。
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "无法以管理员方式打开命令提示符：\n" + ex.Message,
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>读取系统下载目录（兼容 OneDrive 等重定向），读不到时回退到用户目录。</summary>
    private static string ResolveDownloadsFolder()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
            object? value = key?.GetValue("{374DE290-123F-4565-9164-39C4925E467B}");
            if (value is string text && text.Length > 0)
            {
                text = Environment.ExpandEnvironmentVariables(text);
                if (Directory.Exists(text))
                {
                    return text;
                }
            }
        }
        catch
        {
            // 注册表不可读时回退
        }
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string fallback = Path.Combine(profile, "Downloads");
        if (Directory.Exists(fallback))
        {
            return fallback;
        }
        return Directory.Exists(profile) ? profile : Environment.CurrentDirectory;
    }

    // ================= 右键菜单 =================

    private void BuildNicMenu()
    {
        _nicMenu.DropDownItems.Clear();

        var auto = new ToolStripMenuItem("自动（累加全部物理网卡）")
        {
            Checked = _config.NicId == "auto"
        };
        auto.Click += (_, _) => SelectNic("auto");
        _nicMenu.DropDownItems.Add(auto);
        _nicMenu.DropDownItems.Add(new ToolStripSeparator());

        foreach (var nic in NetworkReader.GetInterfaces())
        {
            var item = new ToolStripMenuItem(nic.Name + "  —  " + nic.Description)
            {
                Checked = _config.NicId == nic.Id
            };
            string id = nic.Id;
            item.Click += (_, _) => SelectNic(id);
            _nicMenu.DropDownItems.Add(item);
        }
    }

    private void SelectNic(string id)
    {
        _config.NicId = id;
        _config.Save();
        _network = new NetworkReader(id);
        OnTick(this, EventArgs.Empty);
    }

    private void BuildOpacityMenu()
    {
        _opacityMenu.DropDownItems.Clear();
        foreach (var (label, value) in OpacityLevels)
        {
            var item = new ToolStripMenuItem(label)
            {
                Checked = Math.Abs(_config.Opacity - value) < 0.01
            };
            item.Click += (_, _) =>
            {
                _config.Opacity = value;
                _config.Save();
                Opacity = value;
            };
            _opacityMenu.DropDownItems.Add(item);
        }
    }

    private void BuildThemeMenu()
    {
        _themeMenu.DropDownItems.Clear();
        foreach (var (name, fore, back) in Themes)
        {
            var item = new ToolStripMenuItem(name)
            {
                Checked = _config.ForeColor == fore && _config.BackColor == back
            };
            item.Click += (_, _) =>
            {
                _config.ForeColor = fore;
                _config.BackColor = back;
                _config.Save();
                _foreColor = ColorTranslator.FromHtml(fore);
                _backColor = ColorTranslator.FromHtml(back);
                BackColor = _backColor;
                ForeColor = _foreColor;
                Invalidate();
            };
            _themeMenu.DropDownItems.Add(item);
        }
    }

    // ================= 全屏自动隐藏 =================

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int cbSize;
        public WinRect rcMonitor;
        public WinRect rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out WinRect lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private const uint MonitorDefaultToNearest = 0x00000002;
    private const int FullscreenTolerancePx = 2;

    /// <summary>
    /// 恢复显示时不给窗口抢焦点，避免打断正在使用的全屏/视频操作。
    /// </summary>
    protected override bool ShowWithoutActivation => true;

    /// <summary>
    /// 每 300ms 检查一次：任意程序进入全屏（网页全屏 / 视频全屏 / 游戏 / 播放器等，
    /// 即前台窗口铺满整块显示器）时自动隐藏悬浮窗，退出全屏后自动恢复显示。
    /// 用户手动“隐藏到托盘”不受影响，不会被自动重新显示。
    /// </summary>
    private void EvaluateFullscreen()
    {
        if (!_config.AutoHideFullscreen)
        {
            return;
        }

        if (IsFullscreenActive())
        {
            if (Visible && !_autoHidden)
            {
                _autoHidden = true; // 记住是“全屏自动隐藏”，退出全屏后自动恢复
                Hide();
            }
        }
        else if (_autoHidden)
        {
            _autoHidden = false;
            Show(); // ShowWithoutActivation=true，不会抢焦点
        }
    }

    /// <summary>前台窗口是否覆盖其所在显示器的完整屏幕区域（物理像素，多显示器各自判定）。</summary>
    private static bool IsFullscreenActive()
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero)
        {
            return false;
        }

        // 悬浮窗自身（含右键菜单 / 托盘菜单）获得焦点时不隐藏
        GetWindowThreadProcessId(fg, out uint pid);
        if (pid == Environment.ProcessId)
        {
            return false;
        }

        if (!GetWindowRect(fg, out WinRect w) || w.Right - w.Left <= 0 || w.Bottom - w.Top <= 0)
        {
            return false;
        }

        IntPtr monitor = MonitorFromWindow(fg, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var mi = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref mi))
        {
            return false;
        }

        // 窗口必须铺满整块显示器（含任务栏区域）才算全屏；普通最大化窗口只覆盖工作区，不算
        var m = mi.rcMonitor;
        return w.Left <= m.Left + FullscreenTolerancePx
            && w.Top <= m.Top + FullscreenTolerancePx
            && w.Right >= m.Right - FullscreenTolerancePx
            && w.Bottom >= m.Bottom - FullscreenTolerancePx;
    }

    // ================= XShell 复刻版 =================

    /// <summary>打开 XShell 复刻版。仅在用户点击菜单时创建，关闭窗口后释放。</summary>
    private void ShowXShell()
    {
        try
        {
            if (_xshellForm == null || _xshellForm.IsDisposed)
            {
                _xshellForm = new MiniMonitor.XShell.XShellForm();
                _xshellForm.FormClosed += (_, _) => _xshellForm = null;
            }
            if (!_xshellForm.Visible)
            {
                _xshellForm.Show();
            }
            if (_xshellForm.WindowState == FormWindowState.Minimized)
            {
                _xshellForm.WindowState = FormWindowState.Normal;
            }
            _xshellForm.Activate();
        }
        catch (Exception ex)
        {
            _xshellForm?.Dispose();
            _xshellForm = null;
            MessageBox.Show(
                "无法打开 XShell 复刻版：\r\n\r\n" + ex.Message
                + "\r\n\r\n详细异常已写入 %LOCALAPPDATA%\\MiniMonitor\\error.log。",
                "XShell 复刻版", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>打开独立 XFTP 客户端。仅在用户点击菜单时创建，关闭窗口后释放。</summary>
    private void ShowXFtp()
    {
        try
        {
            if (_xftpForm == null || _xftpForm.IsDisposed)
            {
                _xftpForm = new MiniMonitor.XFtp.XFtpForm();
                _xftpForm.FormClosed += (_, _) => _xftpForm = null;
            }
            if (!_xftpForm.Visible)
            {
                _xftpForm.Show();
            }
            if (_xftpForm.WindowState == FormWindowState.Minimized)
            {
                _xftpForm.WindowState = FormWindowState.Normal;
            }
            _xftpForm.Activate();
        }
        catch (Exception ex)
        {
            _xftpForm?.Dispose();
            _xftpForm = null;
            MessageBox.Show(
                "无法打开 XFTP 客户端：\r\n\r\n" + ex.Message,
                "XFTP 客户端", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ================= FluentRSS 阅读器 =================

    /// <summary>
    /// 打开 FluentRSS 阅读器。窗口默认最大化，并在打开时立即同步一次；
    /// 关闭窗口只是隐藏到托盘，后台仍按设定的间隔（默认 1 小时）同步。
    /// </summary>
    private void ShowFluentRss()
    {
        try
        {
            if (_rssForm == null || _rssForm.IsDisposed)
            {
                _rssForm = new FluentRssForm(_config, _rssStore, _rssSync);
            }
            _rssForm.ShowReader();
        }
        catch (Exception ex)
        {
            _rssForm?.Dispose();
            _rssForm = null;
            MessageBox.Show(
                "无法打开 FluentRSS 阅读器：\r\n\r\n" + ex.Message
                + "\r\n\r\n详细异常已写入 %LOCALAPPDATA%\\MiniMonitor\\error.log。",
                "FluentRSS 阅读器", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// 打开离线音乐播放器。窗口恢复上次大小；关闭只是隐藏，音乐继续播放。
    /// 联网能力默认关闭，需在音乐设置页手动开启。
    /// </summary>
    private void ShowMusicPlayer()
    {
        try
        {
            if (_musicForm == null || _musicForm.IsDisposed)
            {
                _musicForm = new MusicPlayerForm(_config);
            }
            _musicForm.ShowReader();
        }
        catch (Exception ex)
        {
            _musicForm?.Dispose();
            _musicForm = null;
            MessageBox.Show(
                "无法打开离线音乐播放器：\r\n\r\n" + ex.Message,
                "离线音乐播放器", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnRssSyncCompleted(RssSyncResult result)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }
        try
        {
            BeginInvoke(new Action(() =>
            {
                UpdateRssTrayText();
                if (result.NewArticles > 0
                    && _config.Rss.Notifications
                    && !result.TriggeredManually
                    && (_rssForm == null || !_rssForm.Visible))
                {
                    string title = result.NewTitles.Count > 0
                        ? result.NewTitles[0]
                        : result.NewArticles + " 篇新文章";
                    _lastBalloonWasRss = true;
                    _tray.ShowBalloonTip(5000, "FluentRSS 阅读器",
                        $"后台同步到 {result.NewArticles} 篇新文章\n{title}\n点击打开阅读器",
                        ToolTipIcon.Info);
                }
            }));
        }
        catch
        {
            // 窗口正在退出
        }
    }

    private void OnRssDataChanged()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }
        try
        {
            BeginInvoke(new Action(UpdateRssTrayText));
        }
        catch
        {
            // 窗口正在退出
        }
    }

    private void UpdateRssTrayText()
    {
        try
        {
            var counts = _rssStore.GetSmartCounts();
            _tray.Text = counts.All > 0
                ? $"MiniMonitor · FluentRSS 未读 {counts.All}"
                : "MiniMonitor · FluentRSS";
        }
        catch
        {
            _tray.Text = AppName;
        }
    }

    // ================= 关于 / 退出 =================

    /// <summary>打开硬件信息查看窗口（Excel 风格分组表格，默认不占任何界面，仅在菜单点击时弹出）。</summary>
    private void ShowHardwareInfo()
    {
        using var form = new HardwareInfoForm();
        form.ShowDialog(this);
    }

    /// <summary>打开自定义关机设置窗口；确认后启动置顶倒计时。</summary>
    private void ShowShutdownDialog()
    {
        if (_shutdownCountdown is { IsDisposed: false } active)
        {
            active.Show();
            active.Activate();
            MessageBox.Show(active,
                "定时关机正在进行，请先在倒计时窗口点击“取消定时关机”后再重新设置。",
                "提示",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        using var form = new ShutdownForm();
        if (form.ShowDialog() == DialogResult.OK && form.Deadline is DateTime deadline)
        {
            StartShutdownCountdown(deadline);
        }
    }

    private void StartShutdownCountdown(DateTime deadline)
    {
        if (_shutdownCountdown is { IsDisposed: false } existing)
        {
            existing.CancelAndClose();
        }
        var form = new ShutdownCountdownForm(deadline);
        _shutdownCountdown = form;
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_shutdownCountdown, form))
            {
                _shutdownCountdown = null;
            }
        };
        form.Show();
    }

    /// <summary>打开磁盘安全覆写窗口；定时关机进行中时禁止进入，避免覆写中途被关机打断。</summary>
    private void ShowDiskWipe()
    {
        if (_shutdownCountdown is { IsDisposed: false } active)
        {
            active.Show();
            active.Activate();
            MessageBox.Show(active,
                "定时关机正在进行，请先取消定时关机后再使用磁盘安全覆写。\n"
                + "覆写过程中被强制关机会导致磁盘只被部分覆写。",
                "提示",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        using var form = new DiskWipeForm();
        form.ShowDialog();
    }

    /// <summary>打开 NTP 同步管理窗口；后台自动同步服务独立运行，关闭此窗口不会停止同步。</summary>
    private void ShowNtpManager()
    {
        if (_ntpForm is { IsDisposed: false } existing)
        {
            existing.Show();
            existing.Activate();
            return;
        }
        var form = new NtpSyncForm(_ntpManager, _ntpHistory, _config);
        _ntpForm = form;
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_ntpForm, form))
            {
                _ntpForm = null;
            }
        };
        form.Show();
    }

    /// <summary>打开全局鼠标手势管理窗口；关闭窗口不会停止已启用的手势服务。</summary>
    private void ShowGestureManager()
    {
        if (_gestureForm is { IsDisposed: false } existing)
        {
            existing.Show();
            existing.Activate();
            return;
        }
        var form = new GlobalMouseGestureForm(_mouseGestureService, _config);
        _gestureForm = form;
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_gestureForm, form))
            {
                _gestureForm = null;
            }
        };
        form.Show();
    }

    /// <summary>打开磁盘分析管理窗口；默认不打开，从右键菜单进入。</summary>
    private void ShowDiskAnalysis()
    {
        if (_diskAnalysisForm is { IsDisposed: false } existing)
        {
            existing.Show();
            existing.Activate();
            return;
        }
        var form = new DiskAnalysisForm();
        _diskAnalysisForm = form;
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_diskAnalysisForm, form))
            {
                _diskAnalysisForm = null;
            }
        };
        form.Show();
    }

    /// <summary>打开全盘数据恢复窗口；默认不打开，从右键菜单或托盘菜单进入。</summary>
    private void ShowDataRecovery()
    {
        if (_dataRecoveryForm is { IsDisposed: false } existing)
        {
            existing.Show();
            if (existing.WindowState == FormWindowState.Minimized)
            {
                existing.WindowState = FormWindowState.Normal;
            }
            existing.Activate();
            return;
        }
        var form = new DataRecoveryForm();
        _dataRecoveryForm = form;
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_dataRecoveryForm, form))
            {
                _dataRecoveryForm = null;
            }
        };
        form.Show();
    }

    /// <summary>打开截图插件管理窗口；默认不打开，快捷键全局生效。</summary>
    private void ShowScreenshotPlugin()
    {
        if (_screenshotManagerForm is { IsDisposed: false } existing)
        {
            existing.Show();
            existing.Activate();
            return;
        }
        var form = new ScreenshotManagerForm(_screenshotService, _config);
        _screenshotManagerForm = form;
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_screenshotManagerForm, form))
            {
                _screenshotManagerForm = null;
            }
        };
        form.Show();
    }

    /// <summary>打开图片处理管理窗口；默认不打开，从右键菜单进入。</summary>
    private void ShowImageProcessing()
    {
        if (_imageProcessingForm is { IsDisposed: false } existing)
        {
            existing.Show();
            existing.Activate();
            return;
        }
        var form = new ImageProcessingForm();
        _imageProcessingForm = form;
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_imageProcessingForm, form))
            {
                _imageProcessingForm = null;
            }
        };
        form.Show();
    }

    /// <summary>打开通用格式转换管理窗口；默认不打开，从右键菜单进入。</summary>
    private void ShowFormatConverter()
    {
        if (_formatConverterForm is { IsDisposed: false } existing)
        {
            existing.Show();
            existing.Activate();
            return;
        }
        var form = new FormatConverterForm(_config);
        _formatConverterForm = form;
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_formatConverterForm, form))
            {
                _formatConverterForm = null;
            }
        };
        form.Show();
    }

    /// <summary>打开网络测试窗口；默认不打开，从右键菜单进入。</summary>
    private void ShowNetworkTest()
    {
        if (_networkTestForm is { IsDisposed: false } existing)
        {
            existing.Show();
            existing.WindowState = FormWindowState.Maximized;
            existing.Activate();
            return;
        }
        var form = new NetworkTestForm(_config);
        _networkTestForm = form;
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_networkTestForm, form))
            {
                _networkTestForm = null;
            }
        };
        form.Show();
    }

    /// <summary>打开 Navicat 复刻版数据库工具；默认最大化显示，关闭窗口不影响悬浮窗。</summary>
    private void ShowNavicat()
    {
        try
        {
            if (_navicatForm == null || _navicatForm.IsDisposed)
            {
                _navicatForm = new MiniMonitor.Navicat.NavicatForm();
                _navicatForm.FormClosed += (_, _) =>
                {
                    _navicatForm = null;
                };
            }
            if (!_navicatForm.Visible)
            {
                _navicatForm.Show();
            }
            if (_navicatForm.WindowState == FormWindowState.Minimized)
            {
                _navicatForm.WindowState = FormWindowState.Normal;
            }
            _navicatForm.WindowState = FormWindowState.Maximized;
            _navicatForm.Activate();
        }
        catch (Exception ex)
        {
            _navicatForm?.Dispose();
            _navicatForm = null;
            MessageBox.Show(this,
                "无法打开 Navicat 复刻版：\r\n\r\n" + ex.Message,
                "Navicat复刻版", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>打开 QuickLook 复刻的设置界面；预览服务本身在后台运行。</summary>
    private void ShowQuickLookSettings()
    {
        if (_quickLookSettingsForm is { IsDisposed: false } existing)
        {
            existing.Show();
            existing.Activate();
            return;
        }
        var form = new QuickLookSettingsForm(_config, _quickLookService);
        _quickLookSettingsForm = form;
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_quickLookSettingsForm, form))
            {
                _quickLookSettingsForm = null;
            }
        };
        form.Show();
    }

    /// <summary>打开无损视频截取复刻版；默认不打开，只在点击菜单时创建。</summary>
    private void ShowLosslessCut()
    {
        if (_losslessCutForm is { IsDisposed: false } existing)
        {
            existing.Show();
            existing.WindowState = FormWindowState.Maximized;
            existing.Activate();
            return;
        }
        var form = new MiniMonitor.Video.LosslessCutForm(_config);
        _losslessCutForm = form;
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_losslessCutForm, form))
            {
                _losslessCutForm = null;
            }
        };
        form.Show();
    }

    /// <summary>打开重新设计的“关于”窗口：大号版本号、作者信息与 Excel 风格分类功能表。</summary>
    private void ShowAbout()
    {
        using var form = new AboutForm();
        form.ShowDialog(this);
    }

    private void Quit()
    {
        if (_shutdownCountdown is { IsDisposed: false } active)
        {
            var result = MessageBox.Show(active,
                "定时关机正在进行，退出 MiniMonitor 将取消本次定时关机。确定退出吗？",
                "退出确认",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                return;
            }
            active.CancelAndClose();
        }
        _config.X = Location.X;
        _config.Y = Location.Y;
        _config.Save();
        if (_navicatForm is { IsDisposed: false } navicat)
        {
            navicat.Close();
            if (!navicat.IsDisposed)
            {
                // 用户在 Navicat 复刻版中取消了关闭（存在未保存修改）
                return;
            }
        }
        _timer.Stop();
        _fullscreenTimer.Stop();
        if (_rssForm is { IsDisposed: false })
        {
            _rssForm.CloseForApp();
        }
        if (_musicForm is { IsDisposed: false })
        {
            _musicForm.CloseForApp();
        }
        _rssSync.SyncCompleted -= OnRssSyncCompleted;
        _rssSync.DataChanged -= OnRssDataChanged;
        _rssSync.Dispose();
        _mouseGestureService.Dispose();
        _screenshotService.Dispose();
        _quickLookService.Dispose();
        _drawioService.Dispose();
        _yamlMindService.Dispose();
        _webTodoService.Dispose();
        _ntpManager.Dispose();
        _tray.Visible = false;
        Application.Exit();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // WH_MOUSE_LL 必须安装在有消息循环的 UI 线程上；窗口显示后再启动，避免后台线程收不到鼠标事件。
        if (_gestureStartTried)
        {
            return;
        }
        _gestureStartTried = true;
        if (_config.MouseGestureEnabled && !_mouseGestureService.IsRunning)
        {
            if (!_mouseGestureService.Start())
            {
                _config.MouseGestureEnabled = false;
                _config.Save();
            }
        }
        _screenshotService.Start();
        _quickLookService.Start();
        BeginInvoke(new Action(() => _quickLookService.Prewarm()));
        if (_config.QuickLookEnabled && !_quickLookService.IsRunning)
        {
            _tray.ShowBalloonTip(5000, "QuickLook复刻",
                "全局空格键监听未启动：" + (_quickLookService.LastError ?? "未知原因")
                + "\n可在“QuickLook复刻”设置界面查看状态并重新启用。",
                ToolTipIcon.Warning);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_edgeSide != EdgeSide.None)
        {
            // 贴边隐藏时不能把屏幕外的坐标写入配置，否则下次启动会消失。
            _config.X = _edgeShownLocation.X;
            _config.Y = _edgeShownLocation.Y;
        }
        else
        {
            _config.X = Location.X;
            _config.Y = Location.Y;
        }
        _config.Save();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _fullscreenTimer.Dispose();
            _edgePollTimer.Dispose();
            _edgeAnimTimer.Dispose();
            _quickLookService.Dispose();
            _rssSync.SyncCompleted -= OnRssSyncCompleted;
            _rssSync.DataChanged -= OnRssDataChanged;
            _rssSync.Dispose();
            if (_rssForm is { IsDisposed: false })
            {
                _rssForm.Dispose();
            }
            if (_musicForm is { IsDisposed: false })
            {
                _musicForm.CloseForApp();
                _musicForm.Dispose();
            }
            if (_navicatForm is { IsDisposed: false })
            {
                _navicatForm.Dispose();
            }
            _tray.Dispose();
            _menu.Dispose();
            _hardware.Dispose();
            _cpuCounter?.Dispose();
            _appIcon?.Dispose();
            _labelFont?.Dispose();
            _valueFont?.Dispose();
        }
        base.Dispose(disposing);
    }
}
