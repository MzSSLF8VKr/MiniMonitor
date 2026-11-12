using System.Text.Json;
using System.Text.Json.Serialization;
using MiniMonitor.Rss;

namespace MiniMonitor;

/// <summary>
/// 用户配置，持久化到 %APPDATA%\MiniMonitor\config.json
/// </summary>
public sealed class AppConfig
{
    /// <summary>当前配置版本；低于该值的老配置启动时自动迁移到新默认偏好</summary>
    public const int CurrentVersion = 10;

    /// <summary>配置版本</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>悬浮窗 X 坐标（屏幕像素）</summary>
    public int? X { get; set; }

    /// <summary>悬浮窗 Y 坐标（屏幕像素）</summary>
    public int? Y { get; set; }

    /// <summary>窗口不透明度 0.4 ~ 1.0（默认 90%）</summary>
    public double Opacity { get; set; } = 0.90;

    /// <summary>文字颜色（HTML 色值，默认终端绿）</summary>
    public string ForeColor { get; set; } = "#00E676";

    /// <summary>背景颜色（HTML 色值，默认终端绿）</summary>
    public string BackColor { get; set; } = "#000000";

    /// <summary>统计的网卡 Id；"auto" 表示自动累加全部物理网卡</summary>
    public string NicId { get; set; } = "auto";

    /// <summary>是否开机自启（默认开启）</summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>是否锁定窗口位置</summary>
    public bool Locked { get; set; }

    /// <summary>是否显示 CPU 占用率（默认开启）</summary>
    public bool ShowCpuLoad { get; set; } = true;

    /// <summary>是否显示内存占用（默认开启）</summary>
    public bool ShowMemory { get; set; } = true;

    /// <summary>是否显示 GPU 专属显存 GPUMEM（默认开启）</summary>
    public bool ShowGpuMem { get; set; } = true;

    /// <summary>是否显示运行时长 RUN（本次开机以来运行的时间，默认开启）</summary>
    public bool ShowRunTime { get; set; } = true;

    /// <summary>是否显示开机时间 BOOT（本次开机时间，默认开启）</summary>
    public bool ShowStartTime { get; set; } = true;

    /// <summary>全屏自动隐藏：任意程序进入全屏（网页/视频/游戏等）时自动隐藏悬浮窗，退出全屏自动恢复（默认开启）</summary>
    public bool AutoHideFullscreen { get; set; } = true;

    /// <summary>贴边自动隐藏：拖动悬浮窗到屏幕上 / 左 / 右边缘时自动吸附隐藏，鼠标悬停显示（默认关闭）</summary>
    public bool EdgeAutoHide { get; set; }

    /// <summary>贴边方向：0 无，1 左，2 上，3 右</summary>
    public int EdgeAutoHideSide { get; set; }

    /// <summary>NTP 自动同步：每 5 分钟校准本机时间（默认开启）</summary>
    public bool NtpAutoSync { get; set; } = true;

    /// <summary>全局鼠标手势：右键向下→向右关闭当前窗口（默认开启，可手动关闭）</summary>
    public bool MouseGestureEnabled { get; set; } = true;

    /// <summary>资源管理器右键“通用格式转换”菜单（默认关闭）</summary>
    public bool ExplorerContextMenuEnabled { get; set; }

    // ===== QuickLook 复刻 =====

    /// <summary>QuickLook 复刻默认开启：资源管理器中选中文件后按空格预览</summary>
    public bool QuickLookEnabled { get; set; } = true;

    /// <summary>预览窗口置顶（默认开启）</summary>
    public bool QuickLookAlwaysOnTop { get; set; } = true;

    /// <summary>预览窗口淡入动画（默认开启）</summary>
    public bool QuickLookAnimation { get; set; } = true;

    /// <summary>预览窗口宽度</summary>
    public int QuickLookWidth { get; set; } = 960;

    /// <summary>预览窗口高度</summary>
    public int QuickLookHeight { get; set; } = 720;

    /// <summary>预览窗口 X 坐标</summary>
    public int? QuickLookX { get; set; }

    /// <summary>预览窗口 Y 坐标</summary>
    public int? QuickLookY { get; set; }

    public bool QuickLookPluginImage { get; set; } = true;
    public bool QuickLookPluginText { get; set; } = true;
    public bool QuickLookPluginAudio { get; set; } = true;
    public bool QuickLookPluginVideo { get; set; } = true;
    public bool QuickLookPluginWeb { get; set; } = true;
    public bool QuickLookPluginOffice { get; set; } = true;
    public bool QuickLookPluginArchive { get; set; } = true;
    public bool QuickLookPluginFont { get; set; } = true;
    public bool QuickLookPluginFolder { get; set; } = true;

    // ===== 截图插件 =====

    /// <summary>截图功能默认开启</summary>
    public bool ScreenshotEnabled { get; set; } = true;

    /// <summary>屏幕录制默认开启</summary>
    public bool ScreenRecordingEnabled { get; set; } = true;

    /// <summary>截图延迟秒数</summary>
    public int ScreenshotDelaySeconds { get; set; }

    /// <summary>截图后自动复制到剪贴板</summary>
    public bool ScreenshotAutoCopy { get; set; } = true;

    /// <summary>截图后自动打开图像编辑器</summary>
    public bool ScreenshotAutoEditor { get; set; } = true;

    /// <summary>默认截图形状（Rectangle/RoundedRectangle/Ellipse/Freehand/Polygon）</summary>
    public string ScreenshotShape { get; set; } = "Rectangle";

    /// <summary>录制格式（MP4/GIF）</summary>
    public string RecordingFormat { get; set; } = "MP4";

    /// <summary>录制帧率</summary>
    public int RecordingFps { get; set; } = 30;

    /// <summary>录制质量（Low/Medium/High）</summary>
    public string RecordingQuality { get; set; } = "High";

    /// <summary>录制音频来源（None/System/Microphone）</summary>
    public string RecordingAudio { get; set; } = "None";

    /// <summary>录制光标高亮</summary>
    public bool RecordingCursorHighlight { get; set; } = true;

    /// <summary>录制鼠标点击效果</summary>
    public bool RecordingClickEffects { get; set; } = true;

    /// <summary>录制区域模式（FullScreen/Region）</summary>
    public string RecordingMode { get; set; } = "FullScreen";

    /// <summary>FFmpeg 可执行文件路径；为空则自动查找</summary>
    public string FfmpegPath { get; set; } = "";

    /// <summary>水印文字</summary>
    public string WatermarkText { get; set; } = "MiniMonitor";

    /// <summary>水印位置（BottomRight/BottomLeft/TopRight/TopLeft/Center）</summary>
    public string WatermarkPosition { get; set; } = "BottomRight";

    /// <summary>水印透明度 0~1</summary>
    public double WatermarkOpacity { get; set; } = 0.65;

    /// <summary>水印字体名称（默认微软雅黑）</summary>
    public string WatermarkFontFamily { get; set; } = WatermarkStyle.DefaultFontFamily;

    /// <summary>水印字号（pt）；0 表示按图片宽度自动计算（沿用原有字号）</summary>
    public int WatermarkFontSize { get; set; }

    /// <summary>水印是否加粗</summary>
    public bool WatermarkBold { get; set; } = true;

    /// <summary>水印颜色（#RRGGBB，默认紫色）</summary>
    public string WatermarkColor { get; set; } = WatermarkStyle.DefaultColorHex;

    /// <summary>PingInfoView 主机列表与运行参数</summary>
    public PingInfoSettings PingInfo { get; set; } = new();

    /// <summary>draw.io 内置 Web 服务（默认关闭，在右键菜单勾选后启动）</summary>
    public bool DrawioServiceEnabled { get; set; }

    /// <summary>YamlMind 内置 Web 服务（默认关闭，在右键菜单勾选后启动）</summary>
    public bool YamlMindServiceEnabled { get; set; }

    /// <summary>WebTodo 内置 Web 服务（默认关闭，在右键菜单勾选后启动）</summary>
    public bool WebTodoServiceEnabled { get; set; }

    /// <summary>FluentRSS 阅读器设置（本地 RSS + 可选的 Fever / Google Reader 服务同步）</summary>
    public RssSettings Rss { get; set; } = new();

    /// <summary>离线音乐播放器设置（网络功能默认关闭，插件默认开启）</summary>
    public MusicSettings Music { get; set; } = new();

    public static string ConfigDir { get; } = AppPaths.Root;

    public static string ConfigPath { get; } = Path.Combine(ConfigDir, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                if (cfg != null)
                {
                    cfg.PingInfo ??= new PingInfoSettings();
                    cfg.Rss ??= new RssSettings();
                    cfg.Music ??= new MusicSettings();
                    if (cfg.Version < CurrentVersion)
                    {
                        // v1 → v3：应用新的默认偏好（开机自启、显示 CPU/内存、终端绿、90% 透明度）
                        if (cfg.Version < 3)
                        {
                            cfg.AutoStart = true;
                            cfg.ShowCpuLoad = true;
                            cfg.ShowMemory = true;
                            cfg.ShowGpuMem = true;
                            cfg.ForeColor = "#00E676";
                            cfg.BackColor = "#000000";
                            cfg.Opacity = 0.90;
                        }

                        // v3 → v4：开机时间改为默认显示
                        if (cfg.Version < 4)
                        {
                            cfg.ShowStartTime = true;
                        }

                        // v4 → v5：全局鼠标手势改为默认开启
                        if (cfg.Version < 5)
                        {
                            cfg.MouseGestureEnabled = true;
                        }

                        // v5 → v6：水印默认颜色由 #800080 改为 #FF0080
                        if (cfg.Version < 6
                            && string.Equals(cfg.WatermarkColor, "#800080", StringComparison.OrdinalIgnoreCase))
                        {
                            cfg.WatermarkColor = WatermarkStyle.DefaultColorHex;
                        }

                        // v6 → v7：PingInfoView 默认间隔改为 1 秒，空主机列表填入默认 DNS 列表
                        if (cfg.Version < 7)
                        {
                            if (cfg.PingInfo.IntervalSeconds == 2)
                            {
                                cfg.PingInfo.IntervalSeconds = 1;
                            }
                            if (string.IsNullOrWhiteSpace(cfg.PingInfo.HostsText))
                            {
                                cfg.PingInfo.HostsText = PingInfoSettings.DefaultHostsText;
                            }
                        }

                        // v8 → v9：新增离线音乐播放器；所有网络能力与插件保持默认关闭
                        if (cfg.Version < 9)
                        {
                            cfg.Music ??= new MusicSettings();
                            cfg.Music.NetworkEnabled = false;
                            cfg.Music.SsdpEnabled = false;
                            cfg.Music.PluginsEnabled = false;
                            cfg.Music.MiniPlayerPlugin = false;
                            cfg.Music.DesktopLyricsPlugin = false;
                            cfg.Music.VisualizerPlugin = false;
                        }

                        // v9 → v10：插件改为默认开启；记住窗口大小与最大化状态
                        if (cfg.Version < 10)
                        {
                            cfg.Music ??= new MusicSettings();
                            cfg.Music.PluginsEnabled = true;
                            cfg.Music.MiniPlayerPlugin = true;
                            cfg.Music.DesktopLyricsPlugin = true;
                            cfg.Music.VisualizerPlugin = true;
                            cfg.Music.DesktopLyricsVisible = true;
                            cfg.Music.WindowMaximized = true;
                        }

                        cfg.Version = CurrentVersion;
                    }

                    // 值域校验，防止配置损坏
                    if (cfg.Opacity is < 0.4 or > 1.0) cfg.Opacity = 0.90;
                    if (string.IsNullOrWhiteSpace(cfg.ForeColor)) cfg.ForeColor = "#00E676";
                    if (string.IsNullOrWhiteSpace(cfg.BackColor)) cfg.BackColor = "#000000";
                    if (string.IsNullOrWhiteSpace(cfg.NicId)) cfg.NicId = "auto";
                    if (cfg.QuickLookWidth is < 420 or > 4000) cfg.QuickLookWidth = 960;
                    if (cfg.QuickLookHeight is < 320 or > 4000) cfg.QuickLookHeight = 720;
                    if (string.IsNullOrWhiteSpace(cfg.WatermarkFontFamily))
                    {
                        cfg.WatermarkFontFamily = WatermarkStyle.DefaultFontFamily;
                    }
                    if (cfg.WatermarkFontSize is < 0 or > 200) cfg.WatermarkFontSize = 0;
                    if (string.IsNullOrWhiteSpace(cfg.WatermarkColor))
                    {
                        cfg.WatermarkColor = WatermarkStyle.DefaultColorHex;
                    }
                    var ping = cfg.PingInfo;
                    if (ping.IntervalSeconds is < 1 or > 3600) ping.IntervalSeconds = 1;
                    if (ping.TimeoutMs is < 100 or > 120000) ping.TimeoutMs = 2000;
                    if (ping.PayloadSize is < 0 or > 65500) ping.PayloadSize = 32;
                    if (ping.MaxConcurrent is < 1 or > 5000) ping.MaxConcurrent = 128;
                    if (ping.Ttl is < 1 or > 255) ping.Ttl = 128;
                    if (ping.HistoryLimit is < 100 or > 200000) ping.HistoryLimit = 5000;
                    if (ping.ConsecutiveFailuresForBeep is < 1 or > 1000) ping.ConsecutiveFailuresForBeep = 3;
                    if (ping.LowerPaneMode is not ("All" or "Failed" or "Changes" or "None"))
                    {
                        ping.LowerPaneMode = "All";
                    }
                    cfg.Rss.Normalize();
                    cfg.Music.Normalize();
                    return cfg;
                }
            }
        }
        catch
        {
            // 配置损坏时静默回退到默认值
        }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            Version = CurrentVersion;
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // 配置写入失败不应影响监控运行
        }
    }
}

/// <summary>
/// FluentRSS 阅读器设置。同步间隔默认 60 分钟；关闭阅读器后仍按该间隔在后台同步。
/// </summary>
public sealed class RssSettings
{
    /// <summary>自动抓取间隔（分钟）；0 表示从不自动抓取，默认 60 分钟。</summary>
    public int SyncIntervalMinutes { get; set; } = 60;

    /// <summary>上次后台同步完成时间（Unix 秒）</summary>
    public long LastSyncUnix { get; set; }

    /// <summary>主题：system / light / dark</summary>
    public string Theme { get; set; } = "system";

    /// <summary>列表视图：list / cards / magazine / compact</summary>
    public string ViewMode { get; set; } = "list";

    /// <summary>筛选：all / unread / starred</summary>
    public string Filter { get; set; } = "all";

    /// <summary>文章正文字号（px）</summary>
    public int FontSize { get; set; } = 16;

    public bool ShowSnippet { get; set; } = true;
    public bool ShowCover { get; set; } = true;
    public bool FadeRead { get; set; } = true;

    /// <summary>打开文章时自动标为已读</summary>
    public bool MarkReadOnOpen { get; set; } = true;

    /// <summary>滚动到正文底部时自动标为已读</summary>
    public bool MarkReadOnScroll { get; set; }

    /// <summary>后台同步到新文章时发送托盘通知</summary>
    public bool Notifications { get; set; } = true;

    /// <summary>每个订阅源最多保留的文章数；0 表示不限制</summary>
    public int MaxArticlesPerFeed { get; set; } = 800;

    /// <summary>默认打开方式：content（内置文章视图）/ webpage（加载网页）</summary>
    public string DefaultOpenMode { get; set; } = "content";

    /// <summary>默认搜索是否使用正则表达式</summary>
    public bool RegexSearch { get; set; }

    /// <summary>搜索是否区分大小写</summary>
    public bool CaseSensitiveSearch { get; set; }

    /// <summary>搜索是否包含正文</summary>
    public bool SearchContent { get; set; } = true;

    /// <summary>文章列表宽度（像素）</summary>
    public int ListWidth { get; set; } = 420;

    /// <summary>启动后自动执行一次后台同步</summary>
    public bool SyncOnStartup { get; set; } = true;

    /// <summary>阅读器窗口是否已经首次最大化展示过</summary>
    public bool WindowShown { get; set; }

    /// <summary>阅读器窗口宽度（还原状态）</summary>
    public int WindowWidth { get; set; } = 1440;

    /// <summary>阅读器窗口高度（还原状态）</summary>
    public int WindowHeight { get; set; } = 900;

    /// <summary>最近一次同步错误的订阅源摘要（用于设置页展示）</summary>
    public string LastSyncErrors { get; set; } = "";

    /// <summary>同步服务：local / fever / greader</summary>
    public string ServiceType { get; set; } = "local";

    public string ServiceEndpoint { get; set; } = "";
    public string ServiceUsername { get; set; } = "";

    /// <summary>DPAPI 加密后的密码（Base64）</summary>
    public string ServicePassword { get; set; } = "";

    /// <summary>DPAPI 加密后的 Fever API Key（Base64）</summary>
    public string ServiceApiKey { get; set; } = "";

    /// <summary>Inoreader 自定义 AppId / AppKey（可选）</summary>
    public string ServiceAppId { get; set; } = "";
    public string ServiceAppKey { get; set; } = "";

    /// <summary>服务端每次拉取的文章数量</summary>
    public int ServiceFetchLimit { get; set; } = 300;

    /// <summary>首次连接服务时导入服务端分组</summary>
    public bool ServiceImportGroups { get; set; } = true;

    /// <summary>GReader 增量游标（毫秒）</summary>
    public long ServiceLastFetched { get; set; }

    /// <summary>自动处理规则</summary>
    public List<RssRule> Rules { get; set; } = new();

    public void Normalize()
    {
        if (SyncIntervalMinutes is < 0 or > 10080) SyncIntervalMinutes = 60;
        if (Theme is not ("system" or "light" or "dark")) Theme = "system";
        if (ViewMode is not ("list" or "cards" or "magazine" or "compact")) ViewMode = "list";
        if (Filter is not ("all" or "unread" or "starred")) Filter = "all";
        if (FontSize is < 11 or > 32) FontSize = 16;
        if (MaxArticlesPerFeed is < 0 or > 100000) MaxArticlesPerFeed = 800;
        if (DefaultOpenMode is not ("content" or "webpage")) DefaultOpenMode = "content";
        if (ListWidth is < 300 or > 900) ListWidth = 420;
        if (WindowWidth is < 900 or > 10000) WindowWidth = 1440;
        if (WindowHeight is < 600 or > 10000) WindowHeight = 900;
        if (ServiceType is not ("local" or "fever" or "greader")) ServiceType = "local";
        if (ServiceFetchLimit is < 50 or > 5000) ServiceFetchLimit = 300;
        Rules ??= new List<RssRule>();
        foreach (var rule in Rules)
        {
            if (rule.Field is not ("title" or "content" or "both")) rule.Field = "title";
            if (string.IsNullOrWhiteSpace(rule.Target)) rule.Target = "all";
        }
    }
}

/// <summary>
/// 离线音乐播放器设置。联网能力（网络总开关、SSDP/DLNA）默认关闭，
/// 必须由用户在音乐设置页显式开启；迷你播放器与桌面歌词插件默认开启。
/// </summary>
public sealed class MusicSettings
{
    /// <summary>Windows 用户音乐目录（默认音乐库根目录）</summary>
    public static string DefaultLibraryPath
    {
        get
        {
            string path = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (string.IsNullOrWhiteSpace(path))
            {
                path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music");
            }
            return path;
        }
    }

    /// <summary>音乐库目录；为空时使用 Windows 用户音乐目录</summary>
    public string LibraryPath { get; set; } = "";

    /// <summary>递归扫描子目录（默认开启）</summary>
    public bool IncludeSubfolders { get; set; } = true;

    /// <summary>PerfectMusic 文件夹自动加入“喜欢音乐”（默认开启）</summary>
    public bool PerfectMusicAsLiked { get; set; } = true;

    // ===== 网络与外部能力：默认全部关闭 =====

    /// <summary>联网总开关（默认关闭）。关闭时 SSDP/DLNA 不可用</summary>
    public bool NetworkEnabled { get; set; }

    /// <summary>SSDP / DLNA 媒体共享通告（默认关闭，需同时开启联网总开关）</summary>
    public bool SsdpEnabled { get; set; }

    /// <summary>DLNA 媒体服务 HTTP 端口</summary>
    public int SsdpHttpPort { get; set; } = 51234;

    /// <summary>插件总开关（默认开启）</summary>
    public bool PluginsEnabled { get; set; } = true;

    public bool MiniPlayerPlugin { get; set; } = true;
    public bool DesktopLyricsPlugin { get; set; } = true;
    public bool VisualizerPlugin { get; set; } = true;

    // ===== 播放 =====

    public int Volume { get; set; } = 80;
    public bool Mute { get; set; }

    /// <summary>重复模式：off / all / one</summary>
    public string RepeatMode { get; set; } = "all";

    public bool Shuffle { get; set; }

    /// <summary>切歌淡入淡出（默认开启）</summary>
    public bool FadeInOut { get; set; } = true;

    /// <summary>读取 ReplayGain 标签并自动调整音量（默认开启）</summary>
    public bool ReplayGainEnabled { get; set; } = true;

    public bool EqEnabled { get; set; }

    /// <summary>6 段均衡器增益（dB）：60 / 230 / 910 / 3600 / 14000 Hz</summary>
    public float[] EqGains { get; set; } = new float[6];

    public float PreampDb { get; set; }
    public float Balance { get; set; }

    /// <summary>立体声宽度 0~2（1 为原始）</summary>
    public float StereoWidth { get; set; } = 1f;

    /// <summary>播放速度 0.5~2.0（变速会改变音高）</summary>
    public double PlaybackSpeed { get; set; } = 1.0;

    /// <summary>WASAPI 独占输出（默认关闭）</summary>
    public bool ExclusiveOutput { get; set; }

    /// <summary>睡眠定时分钟数；0 表示关闭</summary>
    public int SleepMinutes { get; set; }

    // ===== 界面 =====

    /// <summary>主题：system / light / dark</summary>
    public string Theme { get; set; } = "system";

    /// <summary>显示右侧正在播放 / 歌词面板</summary>
    public bool ShowNowPlaying { get; set; } = true;

    /// <summary>打开音乐窗口时自动扫描（默认开启，仅扫描本地目录）</summary>
    public bool ScanOnOpen { get; set; } = true;

    /// <summary>程序启动时后台扫描一次本地音乐目录</summary>
    public bool ScanOnStartup { get; set; }

    /// <summary>上次浏览的视图</summary>
    public string LastView { get; set; } = "songs";

    /// <summary>桌面歌词置顶</summary>
    public bool DesktopLyricsTopmost { get; set; } = true;

    /// <summary>桌面歌词窗口当前是否显示（默认开启）</summary>
    public bool DesktopLyricsVisible { get; set; } = true;

    /// <summary>桌面歌词字号</summary>
    public int DesktopLyricsFontSize { get; set; } = 34;

    /// <summary>窗口是否已展示过（首次打开最大化）</summary>
    public bool WindowShown { get; set; }

    /// <summary>下次打开时是否保持最大化（关闭时为记住的手动尺寸）</summary>
    public bool WindowMaximized { get; set; } = true;

    /// <summary>手动调整后的窗口位置</summary>
    public int? WindowX { get; set; }

    public int? WindowY { get; set; }

    public int WindowWidth { get; set; } = 1440;
    public int WindowHeight { get; set; } = 900;

    [JsonIgnore]
    public string EffectiveLibraryPath =>
        string.IsNullOrWhiteSpace(LibraryPath) ? DefaultLibraryPath : LibraryPath.Trim();

    [JsonIgnore]
    public string PerfectMusicPath => Path.Combine(EffectiveLibraryPath, "PerfectMusic");

    [JsonIgnore]
    public bool PerfectMusicExists => Directory.Exists(PerfectMusicPath);

    /// <summary>联网能力是否真正可用</summary>
    [JsonIgnore]
    public bool NetworkAvailable => NetworkEnabled;

    [JsonIgnore]
    public bool SsdpAvailable => NetworkEnabled && SsdpEnabled;

    [JsonIgnore]
    public bool MiniPlayerAvailable => PluginsEnabled && MiniPlayerPlugin;

    [JsonIgnore]
    public bool DesktopLyricsAvailable => PluginsEnabled && DesktopLyricsPlugin;

    [JsonIgnore]
    public bool VisualizerAvailable => PluginsEnabled && VisualizerPlugin;

    public void Normalize()
    {
        if (LibraryPath == null) LibraryPath = "";
        if (SsdpHttpPort is < 1024 or > 65535) SsdpHttpPort = 51234;
        if (Volume is < 0 or > 100) Volume = 80;
        if (RepeatMode is not ("off" or "all" or "one")) RepeatMode = "all";
        if (EqGains == null || EqGains.Length != 6)
        {
            var gains = new float[6];
            if (EqGains != null)
            {
                Array.Copy(EqGains, gains, Math.Min(6, EqGains.Length));
            }
            EqGains = gains;
        }
        for (int i = 0; i < EqGains.Length; i++)
        {
            EqGains[i] = Math.Clamp(EqGains[i], -12f, 12f);
        }
        PreampDb = Math.Clamp(PreampDb, -12f, 12f);
        Balance = Math.Clamp(Balance, -1f, 1f);
        StereoWidth = Math.Clamp(StereoWidth, 0f, 2f);
        PlaybackSpeed = Math.Clamp(PlaybackSpeed, 0.5, 2.0);
        SleepMinutes = Math.Clamp(SleepMinutes, 0, 24 * 60);
        if (Theme is not ("system" or "light" or "dark")) Theme = "system";
        if (DesktopLyricsFontSize is < 16 or > 96) DesktopLyricsFontSize = 34;
        if (WindowWidth is < 900 or > 10000) WindowWidth = 1440;
        if (WindowHeight is < 600 or > 10000) WindowHeight = 900;
        if (string.IsNullOrWhiteSpace(LastView)) LastView = "songs";
        // 编辑器写坏配置时保证网络能力严格关闭
        if (!NetworkEnabled)
        {
            SsdpEnabled = false;
        }
        if (!PluginsEnabled)
        {
            MiniPlayerPlugin = false;
            DesktopLyricsPlugin = false;
            VisualizerPlugin = false;
        }
    }
}

/// <summary>PingInfoView 持久化参数。</summary>
public sealed class PingInfoSettings
{
    public const string DefaultHostsText = """
Group: 国外DNS
1.1.1.1  Cloudflare DNS
8.8.8.8  Google DNS
Group: 国内DNS
223.5.5.5        阿里DNS
119.29.29.29     腾讯DNS
114.114.114.114  电信DNS
180.76.76.76     百度DNS
""";

    /// <summary>主机列表原始文本（支持注释、描述、分组、CIDR、范围、host:port）</summary>
    public string HostsText { get; set; } = DefaultHostsText;

    /// <summary>每轮 Ping 间隔（秒）</summary>
    public int IntervalSeconds { get; set; } = 1;

    /// <summary>Ping 超时（毫秒）</summary>
    public int TimeoutMs { get; set; } = 2000;

    /// <summary>ICMP 数据包大小（字节）</summary>
    public int PayloadSize { get; set; } = 32;

    /// <summary>最大并发 Ping 数</summary>
    public int MaxConcurrent { get; set; } = 128;

    /// <summary>是否允许解析/使用 IPv6 地址</summary>
    public bool AllowIpv6 { get; set; }

    /// <summary>ICMP 生存时间 TTL</summary>
    public int Ttl { get; set; } = 128;

    /// <summary>ICMP 不分片</summary>
    public bool DontFragment { get; set; }

    /// <summary>每次 Ping 都重新解析主机名</summary>
    public bool ResolveEveryPing { get; set; }

    /// <summary>高精度计时（使用 Stopwatch，包含少量异步调度开销）</summary>
    public bool HighResolution { get; set; }

    /// <summary>下方日志模式：All / Failed / Changes / None</summary>
    public string LowerPaneMode { get; set; } = "All";

    /// <summary>累积 Ping 日志上限（限制内存占用）</summary>
    public int HistoryLimit { get; set; } = 5000;

    /// <summary>表格黑底白字</summary>
    public bool BlackBackground { get; set; }

    /// <summary>奇偶行交替底色</summary>
    public bool MarkOddEvenRows { get; set; } = true;

    /// <summary>连续失败达到阈值时蜂鸣</summary>
    public bool BeepOnFailure { get; set; }

    /// <summary>触发蜂鸣的连续失败次数</summary>
    public int ConsecutiveFailuresForBeep { get; set; } = 3;

    /// <summary>展开 IPv4 CIDR 时跳过首地址</summary>
    public bool CidrSkipFirst { get; set; }

    /// <summary>展开 IPv4 CIDR 时跳过末地址</summary>
    public bool CidrSkipLast { get; set; }

    public PingInfoSettings Clone()
    {
        return new PingInfoSettings
        {
            HostsText = HostsText,
            IntervalSeconds = IntervalSeconds,
            TimeoutMs = TimeoutMs,
            PayloadSize = PayloadSize,
            MaxConcurrent = MaxConcurrent,
            AllowIpv6 = AllowIpv6,
            Ttl = Ttl,
            DontFragment = DontFragment,
            ResolveEveryPing = ResolveEveryPing,
            HighResolution = HighResolution,
            LowerPaneMode = LowerPaneMode,
            HistoryLimit = HistoryLimit,
            BlackBackground = BlackBackground,
            MarkOddEvenRows = MarkOddEvenRows,
            BeepOnFailure = BeepOnFailure,
            ConsecutiveFailuresForBeep = ConsecutiveFailuresForBeep,
            CidrSkipFirst = CidrSkipFirst,
            CidrSkipLast = CidrSkipLast
        };
    }
}

/// <summary>水印外观默认值与颜色解析工具。</summary>
internal static class WatermarkStyle
{
    public const string DefaultFontFamily = "Microsoft YaHei";
    public const string DefaultColorHex = "#FF0080";
    public static readonly Color DefaultColor = Color.FromArgb(255, 0, 128);

    public static Color ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultColor;
        }
        try
        {
            Color color = ColorTranslator.FromHtml(value);
            return color.IsEmpty ? DefaultColor : color;
        }
        catch
        {
            return DefaultColor;
        }
    }

    public static string ToHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
