using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// 关于窗口：大号版本号 + 作者信息 + Excel 风格分类功能表。
/// 功能按大类分组，可滚动、可搜索、可一键复制为 TSV，适合直接粘贴进 Excel。
/// </summary>
public sealed class AboutForm : Form
{
    private sealed record Feature(string Category, string Name, string Description, string Entry);

    private static readonly string[] CategoryOrder =
    {
        "悬浮窗监控", "硬件与系统", "安全与维护", "效率工具", "网络工具",
        "XShell 复刻版", "XFTP 客户端", "内置 Web 服务", "FluentRSS 阅读器", "离线音乐播放器", "Navicat 复刻版", "其他"
    };

    private static readonly Color[] CategoryColors =
    {
        Color.FromArgb(219, 233, 248), // 蓝
        Color.FromArgb(226, 239, 218), // 绿
        Color.FromArgb(252, 228, 214), // 橙
        Color.FromArgb(233, 225, 242), // 紫
        Color.FromArgb(221, 238, 240), // 青
        Color.FromArgb(214, 232, 248), // 天蓝
        Color.FromArgb(220, 240, 236), // 青绿
        Color.FromArgb(255, 242, 204), // 黄
        Color.FromArgb(232, 238, 247), // 灰蓝
        Color.FromArgb(226, 246, 238), // 薄荷
        Color.FromArgb(255, 235, 214), // 暖橙
        Color.FromArgb(240, 240, 240)  // 灰
    };

    private static readonly Color HeaderFrom = Color.FromArgb(10, 18, 32);
    private static readonly Color HeaderTo = Color.FromArgb(28, 54, 86);
    private static readonly Color Accent = Color.FromArgb(0, 103, 192);
    private static readonly Color TextColor = Color.FromArgb(28, 36, 48);
    private static readonly Color Muted = Color.FromArgb(100, 116, 139);

    private readonly List<Feature> _features = BuildFeatures();
    private readonly Panel _header;
    private readonly TextBox _search;
    private readonly DataGridView _grid;
    private readonly Label _countLabel;
    private readonly Label _statusLabel;
    private readonly Font _titleFont;
    private readonly Font _versionFont;
    private readonly Font _headerSubFont;
    private readonly Font _headerSmallFont;
    private readonly Font _authorFont;
    private readonly Font _bandFont;
    private readonly Font _nameFont;
    private readonly Icon? _appIcon;
    private bool _closeHover;
    private bool _maxHover;

    public AboutForm()
    {
        Text = "关于 MiniMonitor";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1180, 820);
        MinimumSize = new Size(1000, 640);
        BackColor = Color.FromArgb(244, 247, 252);
        Font = new Font("Microsoft YaHei UI", 9.2f);
        KeyPreview = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        MaximizeBox = true;

        _titleFont = new Font("Microsoft YaHei UI", 25f, FontStyle.Bold);
        _versionFont = new Font("Segoe UI Variable Display", 38f, FontStyle.Bold);
        _headerSubFont = new Font("Microsoft YaHei UI", 10.2f);
        _headerSmallFont = new Font("Microsoft YaHei UI", 9.2f);
        _authorFont = new Font("Microsoft YaHei UI", 11.2f);
        _bandFont = new Font("Microsoft YaHei UI", 9.4f, FontStyle.Bold);
        _nameFont = new Font("Microsoft YaHei UI", 9.1f, FontStyle.Bold);
        _appIcon = TryLoadIcon();

        _header = new Panel { Dock = DockStyle.Top, Height = 212, BackColor = HeaderFrom };
        _header.Paint += PaintHeader;
        _header.MouseMove += (_, e) =>
        {
            bool close = CloseRect.Contains(e.Location);
            bool max = MaximizeRect.Contains(e.Location);
            if (close != _closeHover || max != _maxHover)
            {
                _closeHover = close;
                _maxHover = max;
                _header.Cursor = close || max ? Cursors.Hand : Cursors.Default;
                _header.Invalidate();
            }
        };
        _header.MouseLeave += (_, _) =>
        {
            _closeHover = false;
            _maxHover = false;
            _header.Cursor = Cursors.Default;
            _header.Invalidate();
        };
        _header.MouseClick += (_, e) =>
        {
            if (CloseRect.Contains(e.Location))
            {
                Close();
            }
            else if (MaximizeRect.Contains(e.Location))
            {
                ToggleMaximize();
            }
        };
        _header.MouseDoubleClick += (_, e) =>
        {
            if (!CloseRect.Contains(e.Location) && !MaximizeRect.Contains(e.Location))
            {
                ToggleMaximize();
            }
        };
        NativeWindowDrag.Attach(this, _header,
            p => CloseRect.Contains(p) || MaximizeRect.Contains(p));

        // ---------- 工具栏 ----------
        var toolbar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 66,
            BackColor = Color.White,
            Padding = new Padding(26, 14, 26, 12)
        };
        toolbar.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(226, 232, 240));
            e.Graphics.DrawLine(pen, 0, toolbar.Height - 1, toolbar.Width, toolbar.Height - 1);
        };
        _search = new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(248, 250, 252),
            ForeColor = TextColor,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            Width = 320,
            PlaceholderText = "搜索功能名称或说明（例如：RSS、截图、温度）"
        };
        _search.TextChanged += (_, _) => RebuildGrid();
        _search.SetBounds(28, 17, 340, 30);

        _countLabel = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 8.8f),
            ForeColor = Muted,
            Text = ""
        };
        _countLabel.SetBounds(388, 17, 380, 30);

        var copyButton = new PillButton
        {
            Text = "复制全部（TSV）",
            ButtonStyle = PillButtonStyle.Light,
            Size = new Size(150, 32),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        copyButton.Click += (_, _) => CopyAll();
        _statusLabel = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Microsoft YaHei UI", 8.6f),
            ForeColor = Muted,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        toolbar.Controls.Add(_search);
        toolbar.Controls.Add(_countLabel);
        toolbar.Controls.Add(copyButton);
        toolbar.Controls.Add(_statusLabel);
        toolbar.Resize += (_, _) =>
        {
            copyButton.Location = new Point(toolbar.Width - 26 - copyButton.Width, 17);
            _statusLabel.SetBounds(toolbar.Width - 26 - copyButton.Width - 310, 17, 300, 30);
            _countLabel.Width = Math.Max(100, toolbar.Width - 388 - copyButton.Width - 330);
        };

        // ---------- Excel 风格功能表 ----------
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ColumnHeadersHeight = 38,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            RowHeadersVisible = false,
            ReadOnly = true,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            GridColor = Color.FromArgb(219, 226, 234),
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            EnableHeadersVisualStyles = false,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            RowTemplate = { Height = 50 },
            Font = new Font("Microsoft YaHei UI", 9.1f),
            Margin = new Padding(0)
        };
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(30, 58, 95);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9.2f, FontStyle.Bold);
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(30, 58, 95);
        _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 4, 0);
        _grid.DefaultCellStyle.BackColor = Color.White;
        _grid.DefaultCellStyle.ForeColor = TextColor;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(232, 240, 250);
        _grid.DefaultCellStyle.SelectionForeColor = TextColor;
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.DefaultCellStyle.Padding = new Padding(8, 4, 6, 4);
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 251, 253);

        var colName = new DataGridViewTextBoxColumn
        {
            HeaderText = "功能",
            Name = "name",
            Width = 250,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        var colDesc = new DataGridViewTextBoxColumn
        {
            HeaderText = "说明",
            Name = "description",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 380,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        var colEntry = new DataGridViewTextBoxColumn
        {
            HeaderText = "入口 / 默认状态",
            Name = "entry",
            Width = 205,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        _grid.Columns.AddRange(colName, colDesc, colEntry);
        _grid.CellDoubleClick += (_, e) => CopyRow(e.RowIndex);

        // ---------- 底部 ----------
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            BackColor = Color.White
        };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(226, 232, 240));
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        var footerText = new Label
        {
            Dock = DockStyle.Left,
            AutoSize = false,
            Width = 700,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(28, 0, 0, 0),
            Font = new Font("Microsoft YaHei UI", 8.4f),
            ForeColor = Muted,
            Text = "本机运行 · 不收集任何数据 · 单文件绿色版 · 部分功能需要管理员权限 / WebView2 Runtime / ffmpeg"
        };
        var footerAuthor = new Label
        {
            Dock = DockStyle.Right,
            AutoSize = false,
            Width = 330,
            TextAlign = ContentAlignment.MiddleRight,
            Padding = new Padding(0, 0, 28, 0),
            Font = new Font("Microsoft YaHei UI", 8.4f),
            ForeColor = Muted,
            Text = "2026年制作"
        };
        footer.Controls.Add(footerText);
        footer.Controls.Add(footerAuthor);

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(_header);
        Controls.Add(footer);

        Load += (_, _) => RebuildGrid();
        Shown += (_, _) => ApplyRoundedCorners();
        Resize += (_, _) =>
        {
            ApplyRoundedCorners();
            _header.Invalidate();
        };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };
    }

    private static List<Feature> BuildFeatures()
    {
        return new List<Feature>
        {
            // 悬浮窗监控
            new("悬浮窗监控", "实时网速", "下载 / 上传速度，单位 MB/s，自动累加物理网卡，也可指定单张网卡。", "默认显示"),
            new("悬浮窗监控", "CPU / GPU 温度", "LibreHardwareMonitor → WMI 性能计数器 → ACPI 热区三路回退，读不到显示 -- 度。", "默认显示"),
            new("悬浮窗监控", "CPU 占用率", "PerformanceCounter 读取总占用百分比，每秒刷新。", "默认显示"),
            new("悬浮窗监控", "内存占用", "已用 / 总量 GB 与百分比，GlobalMemoryStatusEx 原生读取。", "默认显示"),
            new("悬浮窗监控", "GPU 专属显存", "多卡总和，注册表 / DXGI / WDDM 性能计数器多路径读取。", "默认显示"),
            new("悬浮窗监控", "运行时长 / 开机时间", "RUN 与 BOOT，WMI 读取开机时间，休眠唤醒后仍准确。", "默认显示"),
            new("悬浮窗监控", "外观与位置", "5 套配色、4 档透明度、锁定位置、左键拖动、双击托盘显隐。", "右键菜单"),
            new("悬浮窗监控", "全屏自动隐藏", "任意程序全屏（视频 / 游戏 / 网页）时自动隐藏，退出全屏恢复。", "默认开启"),
            new("悬浮窗监控", "贴边自动隐藏", "拖动悬浮窗到屏幕上 / 左 / 右边缘时自动吸附隐藏，保留 6px 粉红色窄边提示；鼠标悬停显示，完全展开后窄边消失；右键菜单和托盘菜单均可开关。", "默认关闭"),
            new("悬浮窗监控", "开机自启", "写入任务计划程序（最高权限登录触发，无 UAC 弹窗），兼容 Win10 / Win11 / Windows Server 2016-2025，可在右键菜单随时开关。", "默认开启"),

            // 硬件与系统
            new("硬件与系统", "硬件信息查看", "精简 / 详细双视图：一页概览或全量分类表格，支持搜索与复制 TSV。", "右键菜单"),
            new("硬件与系统", "TPM 与刷新率", "TPM 支持 / 规范 / 启用状态与诊断；显示器最高刷新率。", "详细视图"),
            new("硬件与系统", "系统概览", "系统型号、序列号、OS、主板、内存、显卡、磁盘、网卡、声卡、电池等。", "精简视图"),
            new("硬件与系统", "NTP 时间同步", "默认每 5 分钟向 edu.ntp.org.cn / 阿里 / Cloudflare 校时，保留最近 100 条记录。", "默认开启"),

            // 安全与维护
            new("安全与维护", "自定义关机", "15 分钟 ~ 7 天预设、指定日期时间、自定义时长，倒计时可随时取消。", "默认关闭"),
            new("安全与维护", "磁盘安全覆写", "排除系统盘；随机 / 全零 / DoD 3 遍 / Gutmann 35 遍，AES-256 预覆盖。", "默认关闭"),
            new("安全与维护", "磁盘分析管理", "占用矩形图、最大文件夹与最大文件排行，双击在资源管理器中打开。", "右键菜单"),
            new("安全与维护", "磁盘清理辅助", "分析结果支持停止扫描、窗口最大化，自适应铺满布局。", "磁盘分析窗口"),
            new("安全与维护", "全盘数据恢复", "五个选项卡：Kickass Undelete 复刻（NTFS $MFT/$Bitmap、FAT 目录已删除文件扫描与恢复）、FreeRecover（按文件签名恢复）、TestDisk（MBR/GPT 分区表分析、备份、恢复与丢失分区搜索）、PhotoRec（文件签名深度雕刻恢复）、WinfrGUI调用系统工具（检测并调用 Microsoft Store 的 winfr.exe，支持常规/广泛/NTFS/段/签名模式与 /n /y /a /u /k /g /e /o /p /s /b 开关、命令预览与实时日志）。恢复文件可保存到另一块磁盘，写分区表前需二次确认。", "右键菜单 / 托盘菜单"),

            // 效率工具
            new("效率工具", "截图插件", "Ctrl+Shift+C 矩形截图；全屏 / 窗口 / 手绘 / 多边形 / 滚动截图，标注、水印、OCR。", "默认开启"),
            new("效率工具", "屏幕录制", "MP4 / GIF，帧率、质量、系统或麦克风音频、光标高亮与点击涟漪。", "Shift+PrintScreen"),
            new("效率工具", "OCR 文字识别", "Windows 本地 OCR，按原图排版重建结果，可编辑复制，不上传图片。", "截图编辑器"),
            new("效率工具", "图片处理", "单张 / 证件照 / 批量 / 打印 / EXIF 五个选项卡，导出默认剔除 EXIF 与 GPS。", "右键菜单"),
            new("效率工具", "证件照处理", "一寸 / 二寸等规格，物理尺寸 / 像素 / DPI 联动，自动抠图换红蓝白底。", "图片处理窗口"),
            new("效率工具", "通用格式转换", "图片 / 音频 / 视频批量互转，支持 ICO 输出，可选资源管理器右键菜单。", "右键菜单"),
            new("效率工具", "QuickLook 复刻", "空格预览图片、文本 / 代码、音频、视频、PDF、Office、压缩包、字体、文件夹。", "默认开启"),
            new("效率工具", "无损视频截取", "关键帧吸附、无损流复制切割、片段合并、文件拼接、提取音频、截图、场景切换与黑帧检测；左侧常驻显示入点 / 出点时间，导出窗口顶部显示当前视频截取入点 / 出点，并列出每个片段的入点、出点与时长；对标 LosslessCut 3.70.2。", "右键菜单"),
            new("效率工具", "全局鼠标手势", "按住右键向下再向右，关闭窗口或浏览器标签页；VMware 窗口内自动失效。", "默认开启"),

            // 网络工具
            new("网络工具", "网络测试", "每次打开默认最大化；TCPing 测试 / Traceroute / Telnet 测试 / Nmap 扫描 / PingInfoView / WirelessNetworkWatcher / BluetoothView / 蓝屏查看器 / tcp_udp_Watch / 快捷键注册列表 十个选项卡，全部内置。", "右键菜单"),
            new("网络工具", "Traceroute", "复刻 NirSoft CountryTraceRoute：ICMP TTL 逐跳跟踪，默认 255 跳 / 3000ms，默认不反向解析；显示 IP、可选主机名、国家/地区、三次延迟与平均；支持 Enter 启动、停止、复制、CSV / HTML / TXT 导出；自动重试默认关闭、默认 30 秒可调，每次重试先取消上一轮。国家库默认使用内嵌 DB-IP Lite 2026-11（CC BY 4.0），切换选项卡后每天自动检查一次最新库，也可手动更新，始终使用最新缓存；底部显示来源、下载、比对、更新与失败等提示。", "网络测试窗口"),
            new("网络工具", "PingInfoView", "多主机 ICMP / TCP 持续 Ping，统计列、MAC、日志、排序与 CSV / HTML 导出。", "网络测试窗口"),
            new("网络工具", "WirelessNetworkWatcher", "复刻 NirSoft 局域网设备扫描：仅在切换到该选项卡时扫描，离开或关闭窗口立即停止；默认每 60 秒自动扫描，默认优先“以太网 / Ethernet0 / Ethernet / 本地连接”网卡（均不存在则全部网卡），显示扫描进度；在线墨绿、离线灰色、新设备蓝色；显示 IP / MAC / OUI 厂商 / 主机名 / 设备名 / 适配器 / 首次与最后发现时间，支持网卡切换、仅显示在线、新设备提示音、重命名、排序、列显示、CSV / TXT / HTML / XML 导出；内嵌 2026-11-08 IEEE OUI（SHA256 5b244af7…）每天只下载 / 比对一次并自动使用最新 OUI，OUI 按钮右侧实时显示“今日已更新 OUI”、检测、比对、失败与本地缓存等操作提示，“OUI 数据库”右侧依次提供“手动获取 OUI”按钮和操作提示，可随时立即下载比对。", "网络测试窗口"),
            new("网络工具", "BluetoothView", "复刻 NirSoft BluetoothView：使用 Windows 内置蓝牙栈询问周边经典蓝牙设备与已配对 / 已记忆设备，仅在切换到该选项卡时扫描，离开立即停止；默认每 60 秒自动扫描；新设备蓝色、二次发现绿色、离线灰色；显示设备名 / 蓝牙地址 / OUI 制造商 / 主设备类型 / 次设备类型 / 首次与最后发现时间 / 连接状态，支持适配器选择、仅显示在线、筛选、新设备提示音、排序、列显示、设备详情、CSV / TXT / HTML / XML 导出；OUI 厂商库复用 WirelessNetworkWatcher。", "网络测试窗口"),
            new("网络工具", "蓝屏查看器", "复刻 NirSoft BlueScreenView：自动检测 Windows 10 / 11 / Server 2016-2025，扫描 C:\\Windows\\Minidump 与 MEMORY.DMP，解析蓝屏代码、4 个参数与可能相关驱动；内置 Microsoft Learn 官方蓝屏代码库（273 条，含索引页未列出的现代代码），自动用简体中文给出发生缘由与解决方法，分隔线下显示 Microsoft 官方英文原文；支持选择转储目录、打开官方页面、在线获取并缓存原文、CSV / TXT / HTML / XML 导出。", "网络测试窗口"),
            new("网络工具", "tcp_udp_Watch", "复刻 NirSoft LiveTcpUdpWatch：实时枚举本机 TCP / UDP（IPv4 / IPv6）连接，显示本地 / 远程地址与端口、TCP 状态、PID、进程名与完整执行路径、首次发现与最后刷新时间；默认每 1 秒自动刷新，仅在进入本页时刷新；支持协议筛选、仅已建立、隐藏回环、关键词筛选、排序、列显示、复制、打开进程目录与 CSV / TXT / HTML / XML 导出。", "网络测试窗口"),
            new("网络工具", "快捷键注册列表", "复刻 NirSoft HotKeysList：扫描 Windows 快捷方式热键、RegisterHotKey 全局热键与 MiniMonitor 自身热键，第一列显示 HotKey，第二列显示注册软件的完整执行路径；Windows 未公开“按热键查询注册进程”的 API，无法确认时明确显示未知，并提供“按键识别注册程序（实验）”按前台程序变化推断；支持筛选、排序、列显示、复制与导出。", "网络测试窗口"),
            new("网络工具", "Nmap 扫描", "内置引擎，支持 -sS/-sT、-sU、-p、-T、--min-rate、-Pn、CIDR 与地址范围。", "网络测试窗口"),
            new("网络工具", "Telnet / TCPing", "无需安装系统组件，可直接输入目标与端口，输出可选中复制。", "网络测试窗口"),

            // XShell 复刻版
            new("XShell 复刻版", "多协议终端", "SSH / Telnet / 串口 / 本地 PowerShell 会话，xterm.js 终端仿真，支持 UTF-8 与 GB18030。", "右键菜单"),
            new("XShell 复刻版", "会话管理器", "分组支持新建 / 重命名 / 删除（可包含空分组），会话支持搜索、新建 / 属性 / 复制 / 删除、JSON 导入导出，密码使用 Windows DPAPI 加密；顶部“预置快速启动”分组内置 14 个 SSH 预置会话（192.168.33.90~99、192.168.66.90~93，root / 1，远程 /root，本地 Downloads），默认展开；默认不预置本地 PowerShell 示例会话与“本地”分组。", "XShell 窗口"),
            new("XShell 复刻版", "右键复制粘贴", "终端中选中文字后右键 → 复制，右键 → 粘贴；SSH / Telnet / 串口 / 本地 Shell 一致，Ctrl+C 始终发送中断。", "终端右键"),
            new("XShell 复刻版", "SFTP 文件传输", "独立 Xftp 风格双栏窗口：左侧本地文件、右侧远程文件，支持拖动本地文件到远程上传、拖动远程文件 / 目录到本地下载；底部上传任务队列显示实时进度条，成功为绿色 Success，失败为红色 uploadfail，无需 Xftp。", "工具栏 SFTP"),
            new("XShell 复刻版", "会话日志", "文件 → 日志或终端右键 → 日志，手动启动 / 停止 / 打开日志文件夹；文件名格式为 终端名称_日期_时间_随机数.logs.txt，UTF-8（带 BOM）编码，自动剔除 ANSI/OSC/ESC 控制符，并用行内光标模型处理退格、回车覆盖与整行擦除，进度条只记录最终可见状态，日志不会出现 BS、进度堆叠或乱码；开启后标签显示红色 REC 常亮标识（不做定时闪烁，界面不会反复重绘）。", "文件 → 日志"),
            new("XShell 复刻版", "SSH 高级能力", "密码 / 私钥 / 键盘交互认证、主机指纹确认、跳板机、本地 / 远程 / 动态端口隧道。", "会话属性"),
            new("XShell 复刻版", "界面与外观", "参考 XShell 8 的菜单、功能区、会话树、标签页与状态栏；深色（默认）/ 浅色 / 跟随系统三种主题，所有 UI 细节（菜单、右键菜单、输入框、下拉列表、复选框、列表 / 表格、滚动条、提示气泡、SFTP 窗口、上传队列、标签页外框）均适配深色；默认终端字体 Consolas、默认终端字号 16（旧版自动迁移）；设置窗口固定行高、可滚动并修复悬停文字残影；会话管理器默认隐藏，功能区“会话管理器”按钮 / 文件菜单 / 启动页 / 左边缘竖排标签点击显示；快速会话栏（master / node1-3 等一键连接按钮）默认显示，可按需开启。", "视图 → 界面主题"),
            new("XShell 复刻版", "无遥测", "参考 Tabby 1.0.238 能力并剔除官方信息收集代码，不联网上报任何使用数据。", "本地运行"),

            // XFTP 客户端
            new("XFTP 客户端", "独立 FTP 工具", "参考 Xftp 8 的独立双栏客户端，支持 FTP / 显式 FTPS / SFTP（密码 / 私钥 / 跳板机），本地与远程目录同时浏览；默认深色主题。", "右键菜单 → XFtp复刻版"),
            new("XFTP 客户端", "拖拽与队列", "本地文件拖到远程即上传，远程文件 / 目录拖到本地即下载；底部传输队列显示方向、速度、进度条与 Success / 失败状态，可取消全部或清空已完成。", "XFTP 窗口"),
            new("XFTP 客户端", "与 XShell 联动", "在 XShell 中点击 XFtp 时自动带入当前 SSH 会话的主机、账号、密码 / 私钥与跳板机信息并直接连接；远程目录使用终端当前 shell 目录（OSC 7 / SSH 只读探测 / 标题回退），本地目录固定为用户目录下的 Downloads。", "XShell → XFtp"),
            new("XFTP 客户端", "站点管理器", "保存站点名称、协议、主机、端口、账号与默认目录；密码使用 Windows DPAPI 加密，快速连接栏默认隐藏。", "文件 → 站点管理器"),
            new("XFTP 客户端", "Xftp 风格界面", "渐变品牌标题栏、图标工具栏、本地 / 远程双栏、路径栏、自定义深色表头、状态栏与提示气泡；快速连接功能默认关闭（可在工具 → 设置启用）；工具栏 / 快速连接栏 / 传输队列 / 状态栏均可在查看菜单切换。", "XFTP 窗口"),

            // 内置 Web 服务
            new("内置 Web 服务", "draw.io 服务", "完整离线 draw.io 32.6.10，浏览器打开 http://localhost:50000/。", "默认关闭"),
            new("内置 Web 服务", "YamlMind 服务", "YAML 思维导图，数据保存在 文档\\yamlmind，不依赖 Python。", "默认关闭"),
            new("内置 Web 服务", "WebTodo 服务", "Todo Push 待办管理（已剔除提醒推送 / 邮件配置 / 操作日志），数据保存在 文档\\MiniMonitor\\WebTodo。", "默认关闭"),

            // FluentRSS 阅读器
            new("FluentRSS 阅读器", "三栏阅读", "订阅源 / 文章列表 / 内置文章视图；打开默认最大化，关闭后隐藏到托盘。", "右键菜单"),
            new("FluentRSS 阅读器", "订阅管理", "输入网址自动发现 RSS / Atom / JSON Feed；分组、移动、隐藏、OPML 导入导出。", "阅读器"),
            new("FluentRSS 阅读器", "四种视图", "列表 / 卡片 / 杂志 / 紧凑，可显示封面与摘要、淡化已读文章。", "阅读器 F8"),
            new("FluentRSS 阅读器", "阅读体验", "WebView2 排版、抓取全文、加载原文网页、正文字号、浅色 / 深色主题。", "阅读器"),
            new("FluentRSS 阅读器", "筛选与搜索", "未读 / 星标筛选；正则搜索、区分大小写、正文搜索。", "F2 / 列表筛选"),
            new("FluentRSS 阅读器", "自动规则", "正则匹配标题或正文，自动标为已读、加星标、隐藏或发送通知。", "选项 → 自动规则"),
            new("FluentRSS 阅读器", "后台定时同步", "默认每 60 分钟同步一次，可调 5 分钟 ~ 24 小时或从不；关闭窗口仍后台同步。", "默认 60 分钟"),
            new("FluentRSS 阅读器", "打开即同步", "每次手动打开阅读器都会立即同步一次，保证最新阅读体验。", "自动"),
            new("FluentRSS 阅读器", "服务同步", "Fever / Google Reader API，兼容 FreshRSS、TTRSS、Miniflux、The Old Reader、BazQux、Inoreader。", "选项 → 同步服务"),
            new("FluentRSS 阅读器", "快捷键", "F1~F9、↑↓ 选择、Enter / Space 打开、M 已读、S 星标、H 隐藏、B 浏览器。", "阅读器"),
            new("FluentRSS 阅读器", "桌面通知", "后台同步到新文章时托盘气泡通知，点击直接打开阅读器。", "默认开启"),
            new("FluentRSS 阅读器", "数据备份", "完整备份 / 还原（.frbackup），按时间清理旧文章，清理全文缓存。", "选项 → 数据管理"),
            new("FluentRSS 阅读器", "复刻信息", "Fluent Reader v1.3.2 的 Windows 原生复刻。", "v1.3.2 复刻"),

            // 离线音乐播放器
            new("离线音乐播放器", "本地音乐库", "默认扫描 Windows 用户音乐目录，递归子目录、增量扫描、缺失文件标记。", "右键菜单"),
            new("离线音乐播放器", "PerfectMusic", "音乐目录下的 PerfectMusic 文件夹自动加入“喜欢音乐”，并单独提供入口。", "自动"),
            new("离线音乐播放器", "多视图浏览", "全部歌曲 / 专辑 / 艺术家 / 流派 / 文件夹 / 喜欢 / 播放列表，卡片与虚拟列表。", "音乐窗口"),
            new("离线音乐播放器", "Windows 原生播放", "NAudio + Windows 媒体解码，支持 MP3 / FLAC / WAV / AIFF / M4A / AAC / WMA 等。", "默认启用"),
            new("离线音乐播放器", "播放控制", "随机、列表循环、单曲循环、上一首/下一首、拖动进度、音量与静音。", "底栏"),
            new("离线音乐播放器", "音效", "6 段均衡器、前置增益、左右平衡、立体声宽度、变速与 ReplayGain。", "设置 → 播放"),
            new("离线音乐播放器", "淡入淡出", "切歌与暂停/继续时平滑淡入淡出，避免爆音。", "默认开启"),
            new("离线音乐播放器", "睡眠定时 / A-B 循环", "定时停止播放；A-B 片段循环通过播放器接口提供。", "设置 / 播放器"),
            new("离线音乐播放器", "歌词", "同名 .lrc 或标签内嵌歌词，逐行同步高亮，支持点击歌词跳转。", "右侧歌词面板"),
            new("离线音乐播放器", "播放列表", "新建 / 重命名 / 删除、右键添加曲目、M3U / M3U8 导入与 M3U 导出。", "侧栏 → 播放列表"),
            new("离线音乐播放器", "标签编辑", "标题、艺术家、专辑艺术家、专辑、流派、年份、音轨、碟号、评分、备注写入本地文件。", "右键 → 编辑标签"),
            new("离线音乐播放器", "听歌统计", "曲目/专辑/艺术家数量、总时长、播放次数、累计收听与 Top 排行，全部本地统计。", "侧栏 → 听歌统计"),
            new("离线音乐播放器", "迷你播放器", "默认开启；小窗置顶显示封面、歌名、进度、控制与当前歌词。", "插件（默认开启）"),
            new("离线音乐播放器", "桌面歌词", "默认开启；顶部含上一首/播放/下一首/返回主窗口按钮，歌词逐字炫彩卡拉 OK 高亮。", "插件（默认开启）"),
            new("离线音乐播放器", "重复项清理", "按文件名检测同名重复音乐；喜欢 / PerfectMusic 曲目保留一份到 PerfectMusic，并同步移动 .lrc，其余删除。", "侧栏 → 重复项"),
            new("离线音乐播放器", "SSDP / DLNA", "局域网媒体共享通告默认关闭；开启后仅在局域网提供只读媒体服务。", "默认关闭"),
            new("离线音乐播放器", "隐私与账号", "无账号、无登录、无 Discord / Last.fm / Deezer 等外部服务，无遥测。", "始终离线"),
            new("离线音乐播放器", "复刻信息", "WaveFlow v1.9.1 的 Windows 原生复刻。", "v1.9.1 复刻"),

            // Navicat 复刻版
            new("Navicat 复刻版", "多数据库连接", "MySQL / MariaDB、PostgreSQL、SQL Server、Oracle、SQLite，驱动全部内嵌。", "右键菜单"),
            new("Navicat 复刻版", "SQL 编辑器", "语法高亮、行号、补全、F5 / Ctrl+Enter、事务、保存脚本、多结果集。", "查询标签页"),
            new("Navicat 复刻版", "表数据编辑", "分页、排序、WHERE 筛选、增删改、批量粘贴、自动提交或手动事务。", "双击表"),
            new("Navicat 复刻版", "表设计器", "列 / 类型 / 主键 / 自增 / 默认值 / 注释 / 索引 / 外键，预览并执行 ALTER。", "右键 → 设计表"),
            new("Navicat 复刻版", "导入导出", "CSV / TXT / XLSX / JSON / XML / HTML / SQL，自定义列映射与批量导入。", "表工具栏"),
            new("Navicat 复刻版", "备份还原", "完整 SQL 备份（结构 + 数据 + 视图），支持计划任务定时备份。", "工具 → 备份"),
            new("Navicat 复刻版", "传输与同步", "跨连接表传输、按主键数据同步、结构差异脚本与一键执行。", "工具菜单"),
            new("Navicat 复刻版", "数据生成", "按列生成随机整数 / 小数 / 字符串 / 姓名 / 邮箱 / 日期 / UUID 等测试数据。", "工具菜单"),
            new("Navicat 复刻版", "管理工具", "用户与权限、服务器会话 / 变量监控、ER 图、查询构建器、报表打印、数据库文档。", "工具菜单"),
            new("Navicat 复刻版", "安全连接", "SSH 隧道、SSL、连接超时、字符集，密码使用 Windows DPAPI 加密。", "连接属性"),
            new("Navicat 复刻版", "排错日志", "连接 / SQL / 导入导出 / 备份同步错误写入按天日志，含完整异常堆栈，可在帮助菜单查看。", "帮助 → 查看日志"),
            new("Navicat 复刻版", "复刻信息", "界面与交互复刻 Navicat Premium，数据库能力对标 DBeaver 26.2.4。", "DBeaver 26.2.4 对标"),

            // 其他
            new("其他", "单实例运行", "重复启动会直接退出，避免多个悬浮窗同时采样。", "始终启用"),
            new("其他", "高分屏适配", "Per-Monitor V2 DPI，多显示器与缩放比例下界面清晰。", "始终启用"),
            new("其他", "绿色免安装", "发布为单个自包含 exe，目标电脑无需安装 .NET 运行时。", "单文件"),
            new("其他", "配置与数据", "配置保存在 %APPDATA%\\MiniMonitor\\config.json；RSS 为 fluentrss.db，音乐库为 music.db。", "%APPDATA%\\MiniMonitor")
        };
    }

    private void RebuildGrid()
    {
        string query = _search.Text.Trim();
        _grid.Rows.Clear();
        int total = 0;
        int bandIndex = 0;
        foreach (string category in CategoryOrder)
        {
            var items = _features.Where(f => f.Category == category
                && (query.Length == 0
                    || f.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || f.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || f.Category.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
            if (items.Count == 0)
            {
                continue;
            }
            Color band = CategoryColors[bandIndex % CategoryColors.Length];
            bandIndex++;
            int bandRowIndex = _grid.Rows.Add("▍ " + category, "", "");
            var bandRow = _grid.Rows[bandRowIndex];
            bandRow.Height = 32;
            bandRow.DefaultCellStyle.BackColor = band;
            bandRow.DefaultCellStyle.SelectionBackColor = band;
            bandRow.DefaultCellStyle.ForeColor = Color.FromArgb(45, 62, 80);
            bandRow.DefaultCellStyle.SelectionForeColor = Color.FromArgb(45, 62, 80);
            bandRow.DefaultCellStyle.Font = _bandFont;
            for (int column = 0; column < bandRow.Cells.Count; column++)
            {
                bandRow.Cells[column].Style.BackColor = band;
                bandRow.Cells[column].Style.SelectionBackColor = band;
            }
            foreach (var feature in items)
            {
                int index = _grid.Rows.Add(feature.Name, feature.Description, feature.Entry);
                var row = _grid.Rows[index];
                row.Height = 50;
                row.Cells[0].Style.Font = _nameFont;
                row.Cells[0].Style.ForeColor = Color.FromArgb(28, 36, 48);
                row.Cells[2].Style.ForeColor = Color.FromArgb(80, 100, 120);
                row.Tag = feature;
                total++;
            }
        }
        if (total == 0)
        {
            int index = _grid.Rows.Add("没有匹配的功能", "换个关键词试试，例如“截图”“RSS”“温度”。", "");
            _grid.Rows[index].Height = 60;
        }
        _countLabel.Text = query.Length == 0
            ? $"共 {total} 项功能 · {CategoryOrder.Length} 个大类"
            : $"匹配 {total} 项功能";
        _statusLabel.Text = "";
    }

    private void CopyAll()
    {
        var builder = new StringBuilder();
        builder.AppendLine("大类\t功能\t说明\t入口 / 默认状态");
        foreach (string category in CategoryOrder)
        {
            foreach (var feature in _features.Where(f => f.Category == category))
            {
                builder.AppendLine($"{feature.Category}\t{feature.Name}\t{feature.Description}\t{feature.Entry}");
            }
        }
        try
        {
            Clipboard.SetText(builder.ToString());
            _statusLabel.Text = "已复制全部功能（TSV，可直接粘贴到 Excel）";
        }
        catch
        {
            _statusLabel.Text = "剪贴板被占用，请稍后重试";
        }
    }

    private void CopyRow(int rowIndex)
    {
        if (rowIndex < 0 || _grid.Rows[rowIndex].Tag is not Feature feature)
        {
            return;
        }
        try
        {
            Clipboard.SetText($"{feature.Category}\t{feature.Name}\t{feature.Description}\t{feature.Entry}");
            _statusLabel.Text = "已复制该行";
        }
        catch
        {
            _statusLabel.Text = "剪贴板被占用";
        }
    }

    private void PaintHeader(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using (var brush = new LinearGradientBrush(_header.ClientRectangle,
            HeaderFrom, HeaderTo, LinearGradientMode.Horizontal))
        {
            g.FillRectangle(brush, _header.ClientRectangle);
        }
        using (var glow = new SolidBrush(Color.FromArgb(22, 120, 190, 255)))
        {
            g.FillEllipse(glow, _header.Width - 420, -180, 620, 420);
        }
        using (var glow2 = new SolidBrush(Color.FromArgb(16, 255, 255, 255)))
        {
            g.FillEllipse(glow2, _header.Width - 240, 70, 360, 360);
        }

        // 应用图标
        var iconRect = new Rectangle(34, 34, 58, 58);
        if (_appIcon != null)
        {
            g.DrawIcon(_appIcon, iconRect);
        }
        else
        {
            using var path = ShutdownTheme.RoundRect(iconRect, 13f);
            using var brush = new SolidBrush(Accent);
            g.FillPath(brush, path);
            using var letterFont = new Font("Segoe UI", 24f, FontStyle.Bold);
            TextRenderer.DrawText(g, "M", letterFont, iconRect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        TextRenderer.DrawText(g, "MiniMonitor", _titleFont,
            new Rectangle(112, 24, 620, 44), Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "Windows 桌面监控与效率工具箱", _headerSubFont,
            new Rectangle(114, 70, 620, 24), Color.FromArgb(190, 210, 230),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        string version = "v" + ShortVersion();
        TextRenderer.DrawText(g, version, _versionFont,
            new Rectangle(110, 96, 420, 62), Color.FromArgb(124, 199, 255),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        // 右侧系统信息
        TextRenderer.DrawText(g, "Fluent Reader v1.3.2 复刻 · FluentRSS 阅读器",
            _headerSmallFont,
            new Rectangle(_header.Width - 500, 44, 430, 24), Color.FromArgb(180, 205, 230),
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, ".NET 8 · WinForms · WebView2 · SQLite", _headerSmallFont,
            new Rectangle(_header.Width - 500, 70, 430, 24), Color.FromArgb(150, 180, 210),
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

        // 关闭 / 最大化
        var close = CloseRect;
        if (_closeHover)
        {
            using var hover = new SolidBrush(Color.FromArgb(45, 255, 255, 255));
            g.FillEllipse(hover, close);
        }
        using (var pen = new Pen(Color.FromArgb(225, 255, 255, 255), 2f))
        {
            int inset = 12;
            g.DrawLine(pen, close.Left + inset, close.Top + inset,
                close.Right - inset, close.Bottom - inset);
            g.DrawLine(pen, close.Right - inset, close.Top + inset,
                close.Left + inset, close.Bottom - inset);
        }
        var maximize = MaximizeRect;
        if (_maxHover)
        {
            using var hover = new SolidBrush(Color.FromArgb(45, 255, 255, 255));
            g.FillEllipse(hover, maximize);
        }
        using (var pen = new Pen(Color.FromArgb(225, 255, 255, 255), 1.8f))
        {
            int cx = maximize.Left + maximize.Width / 2;
            int cy = maximize.Top + maximize.Height / 2;
            if (WindowState == FormWindowState.Maximized)
            {
                g.DrawRectangle(pen, cx - 8, cy - 6, 11, 9);
                g.DrawRectangle(pen, cx - 3, cy - 1, 11, 9);
            }
            else
            {
                g.DrawRectangle(pen, cx - 7, cy - 6, 15, 12);
            }
        }
    }

    private static Icon? TryLoadIcon()
    {
        try
        {
            using var stream = typeof(AboutForm).Assembly
                .GetManifestResourceStream("MiniMonitor.app.ico");
            return stream != null ? new Icon(stream) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ShortVersion()
    {
        string version = Application.ProductVersion;
        var parts = version.Split('.');
        if (parts.Length >= 4 && parts[3] == "0")
        {
            return string.Join(".", parts.Take(3));
        }
        return version;
    }

    private Rectangle CloseRect => new(_header.Width - 52, 16, 34, 34);
    private Rectangle MaximizeRect => new(_header.Width - 96, 16, 34, 34);

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
        _header.Invalidate();
        ApplyRoundedCorners();
    }

    private void ApplyRoundedCorners()
    {
        float radius = WindowState == FormWindowState.Maximized ? 0f : 14f;
        try
        {
            using var path = ShutdownTheme.RoundRect(new RectangleF(0, 0, Width, Height), radius);
            var old = Region;
            Region = radius <= 0 ? null : new Region(path);
            old?.Dispose();
        }
        catch
        {
            Region = null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
            _versionFont.Dispose();
            _headerSubFont.Dispose();
            _headerSmallFont.Dispose();
            _authorFont.Dispose();
            _bandFont.Dispose();
            _nameFont.Dispose();
            _appIcon?.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000;
            return cp;
        }
    }
}
