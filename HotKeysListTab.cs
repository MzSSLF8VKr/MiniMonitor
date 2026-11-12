using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// 快捷键注册列表选项卡：复刻 NirSoft HotKeysList，并尽力显示注册该快捷键的软件完整路径。
/// 数据来源：Windows 快捷方式（.lnk）热键 + RegisterHotKey 全局热键探测 + MiniMonitor 自身热键；
/// 由于 Windows 没有公开“按热键查询注册进程”的 API，无法识别时会明确显示“未知”。
/// </summary>
public sealed class HotKeysListTab : UserControl
{
    private const int ColHotKey = 0;
    private const int ColOwner = 1;
    private const int ColProcess = 2;
    private const int ColSource = 3;
    private const int ColNote = 4;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const int ErrorHotKeyAlreadyRegistered = 1409;
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkShift = 0x10;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    // Windows 10 / 11 / Server 2016-2025 常用内置快捷键。
    // 这些条目默认隐藏，取消“仅显示非系统默认快捷键”后可查看完整列表。
    private static readonly (string HotKey, string Action)[] WindowsDefaultHotKeys =
    {
        ("Win", "打开或关闭开始菜单"),
        ("Win+A", "操作中心 / 快速设置"),
        ("Win+B", "将焦点移到通知区域"),
        ("Win+C", "Copilot（Win11 23H2+）/ Charms 栏"),
        ("Win+D", "显示或隐藏桌面"),
        ("Win+E", "打开文件资源管理器"),
        ("Win+F", "打开反馈中心"),
        ("Win+G", "打开 Xbox Game Bar"),
        ("Win+H", "语音输入"),
        ("Win+I", "打开 Windows 设置"),
        ("Win+J", "聚焦提示"),
        ("Win+K", "连接 / 投屏"),
        ("Win+L", "锁定计算机"),
        ("Win+M", "最小化所有窗口"),
        ("Win+N", "通知中心 / 日历（Win11）"),
        ("Win+O", "锁定设备方向"),
        ("Win+P", "选择演示模式 / 投影"),
        ("Win+Q", "搜索（Win10）/ 快速搜索"),
        ("Win+R", "打开运行对话框"),
        ("Win+S", "打开搜索"),
        ("Win+Shift+S", "截图工具区域截图"),
        ("Win+T", "循环切换任务栏应用"),
        ("Win+U", "打开轻松使用 / 辅助功能"),
        ("Win+V", "剪贴板历史"),
        ("Win+W", "小组件（Win11）/ Windows Ink"),
        ("Win+X", "打开快速链接菜单"),
        ("Win+Y", "混合现实门户"),
        ("Win+Z", "贴靠布局（Win11）"),
        ("Win+Tab", "打开任务视图"),
        ("Win+Shift+Tab", "反向任务视图"),
        ("Win+Ctrl+D", "新建虚拟桌面"),
        ("Win+Ctrl+F4", "关闭当前虚拟桌面"),
        ("Win+Ctrl+Left", "切换到左侧虚拟桌面"),
        ("Win+Ctrl+Right", "切换到右侧虚拟桌面"),
        ("Win+Ctrl+Shift+B", "重置显卡驱动程序"),
        ("Win+Shift+Left", "将窗口移到左侧显示器"),
        ("Win+Shift+Right", "将窗口移到右侧显示器"),
        ("Win+Shift+Up", "垂直最大化窗口"),
        ("Win+Ctrl+Enter", "启动讲述人"),
        ("Win+Ctrl+F", "在域中搜索计算机"),
        ("Win+Ctrl+Q", "打开快速助手"),
        ("Win+Alt+D", "显示或隐藏桌面"),
        ("Win+Alt+G", "录制最后 30 秒"),
        ("Win+Alt+R", "开始或停止游戏录制"),
        ("Win+Alt+B", "开启或关闭 HDR"),
        ("Win+Alt+PrintScreen", "截取游戏画面"),
        ("Win+PrintScreen", "截取全屏并保存到图片文件夹"),
        ("Win+Shift+PrintScreen", "截取屏幕区域"),
        ("Win+1", "打开或切换任务栏第 1 个程序"),
        ("Win+2", "打开或切换任务栏第 2 个程序"),
        ("Win+3", "打开或切换任务栏第 3 个程序"),
        ("Win+4", "打开或切换任务栏第 4 个程序"),
        ("Win+5", "打开或切换任务栏第 5 个程序"),
        ("Win+6", "打开或切换任务栏第 6 个程序"),
        ("Win+7", "打开或切换任务栏第 7 个程序"),
        ("Win+8", "打开或切换任务栏第 8 个程序"),
        ("Win+9", "打开或切换任务栏第 9 个程序"),
        ("Win+Shift+1", "打开任务栏第 1 个程序的新实例"),
        ("Win+Shift+2", "打开任务栏第 2 个程序的新实例"),
        ("Win+Shift+3", "打开任务栏第 3 个程序的新实例"),
        ("Win+Shift+4", "打开任务栏第 4 个程序的新实例"),
        ("Win+Shift+5", "打开任务栏第 5 个程序的新实例"),
        ("Win+Shift+6", "打开任务栏第 6 个程序的新实例"),
        ("Win+Shift+7", "打开任务栏第 7 个程序的新实例"),
        ("Win+Shift+8", "打开任务栏第 8 个程序的新实例"),
        ("Win+Shift+9", "打开任务栏第 9 个程序的新实例"),
        ("Win+NumPlus", "放大镜放大"),
        ("Win+NumMinus", "放大镜缩小"),
        ("Win+Esc", "退出放大镜"),
        ("Win+Pause", "打开系统属性"),
        ("Win+Space", "切换输入法 / 键盘布局"),
        ("Alt+Tab", "切换窗口"),
        ("Alt+Shift+Tab", "反向切换窗口"),
        ("Alt+F4", "关闭当前窗口"),
        ("Alt+Space", "打开窗口控制菜单"),
        ("Alt+Enter", "查看所选项目属性"),
        ("Alt+Esc", "按打开顺序切换窗口"),
        ("Alt+Left", "返回上一页"),
        ("Alt+Right", "前进下一页"),
        ("Alt+Up", "返回上一级文件夹"),
        ("Alt+PrintScreen", "截取活动窗口"),
        ("Ctrl+Alt+Tab", "保持任务切换器打开"),
        ("Ctrl+Alt+Delete", "打开安全选项屏幕"),
        ("Ctrl+Shift+Esc", "打开任务管理器"),
        ("Ctrl+Esc", "打开开始菜单"),
        ("Ctrl+Shift+Enter", "以管理员身份运行"),
        ("Shift+F10", "打开上下文菜单"),
        ("Shift+Delete", "永久删除，不进入回收站"),
        ("Shift+Insert", "粘贴"),
        ("F1", "帮助"),
        ("F2", "重命名"),
        ("F3", "搜索文件或文件夹"),
        ("F4", "打开地址栏列表"),
        ("F5", "刷新"),
        ("F6", "循环切换屏幕元素"),
        ("F10", "激活菜单栏"),
        ("F11", "全屏 / 退出全屏"),
        ("Ctrl+Shift+N", "新建文件夹"),
        ("Ctrl+Alt+End", "远程桌面安全选项"),
        ("Alt+PageUp", "远程桌面中切换程序"),
        ("Alt+PageDown", "远程桌面中反向切换程序"),
        ("Ctrl+Alt+NumPlus", "远程桌面复制整个客户端窗口"),
        ("Ctrl+Alt+NumMinus", "远程桌面复制活动窗口"),
        ("Ctrl+Alt+Break", "远程桌面全屏 / 窗口切换")
    };

    private sealed class HotKeyEntry
    {
        public string HotKey = "";
        public string OwnerPath = "";
        public string OwnerName = "";
        public string Source = "";
        public string Note = "";
        public bool IsSystemDefault;
    }

    private readonly AppConfig _config;
    private readonly Panel _top;
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly Label _bottomStatus;
    private readonly ListView _list;
    private readonly PillButton _refreshButton;
    private readonly PillButton _exportButton;
    private readonly PillButton _copyButton;
    private readonly CheckBox _identifyByPress;
    private readonly CheckBox _hideSystemDefaults;
    private readonly CheckBox _hideUnknown;
    private readonly TextBox _filterBox;
    private readonly Font _titleFont;
    private readonly Font _statusFont;
    private readonly Font _smallFont;
    private readonly Font _smallBoldFont;
    private readonly List<HotKeyEntry> _entries = new();
    private readonly Dictionary<string, HotKeyEntry> _entryMap =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly int[] _columnWidths = new int[5];

    private NativeWindow? _probeWindow;
    private IntPtr _keyboardHook;
    private LowLevelKeyboardProc? _hookProc;
    private System.Windows.Forms.Timer? _identifyTimer;
    private string _pendingHotKey = "";
    private int _pendingForegroundPid;
    private bool _scanning;
    private bool _isActive;
    private bool _disposed;
    private DateTime _lastScan;
    private int _sortColumn = ColHotKey;
    private bool _sortAscending = true;
    private bool _markOddEven;

    public HotKeysListTab(AppConfig config)
    {
        _config = config;
        BackColor = Color.White;
        Padding = new Padding(16, 12, 16, 14);

        _titleFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
        _statusFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        _smallFont = new Font("Microsoft YaHei UI", 8.5f);
        _smallBoldFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);

        _top = new Panel { Dock = DockStyle.Top, Height = 148, BackColor = Color.White };
        _top.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(226, 232, 240));
            e.Graphics.DrawLine(pen, 0, _top.Height - 1, _top.Width, _top.Height - 1);
        };

        var topLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
        topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));

        var titleRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.White
        };
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _titleLabel = new Label
        {
            Text = "快捷键注册列表（HotKeysList）",
            Font = _titleFont,
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 14, 0),
            Anchor = AnchorStyles.Left
        };
        _statusLabel = new Label
        {
            Text = "● 未启用",
            Font = _statusFont,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };
        titleRow.Controls.Add(_titleLabel, 0, 0);
        titleRow.Controls.Add(_statusLabel, 1, 0);
        topLayout.Controls.Add(titleRow, 0, 0);

        var buttonBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.White,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        _refreshButton = NewButton("刷新", 82, PillButtonStyle.Primary);
        _refreshButton.Click += (_, _) => _ = ScanAsync();
        _copyButton = NewButton("复制", 72);
        _copyButton.Click += (_, _) => CopySelected();
        _exportButton = NewButton("导出", 72);
        _exportButton.Click += (_, _) => ShowExportMenu();
        buttonBar.Controls.AddRange(new Control[]
        {
            _refreshButton, _copyButton, _exportButton
        });
        topLayout.Controls.Add(buttonBar, 0, 1);

        var optionsBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.White,
            Padding = new Padding(0, 5, 0, 0),
            Margin = new Padding(0)
        };
        _identifyByPress = new CheckBox
        {
            Text = "按键识别注册程序（实验）",
            Width = 190,
            Height = 26,
            Font = _smallFont,
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 1, 10, 0)
        };
        _hideSystemDefaults = new CheckBox
        {
            Text = "仅显示非系统默认快捷键",
            Width = 190,
            Height = 26,
            Font = _smallFont,
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Checked = true,
            Margin = new Padding(0, 1, 10, 0)
        };
        _hideUnknown = new CheckBox
        {
            Text = "不显示未知进程",
            Width = 140,
            Height = 26,
            Font = _smallFont,
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Checked = true,
            Margin = new Padding(0, 1, 10, 0)
        };
        var hint = new Label
        {
            Text = "默认仅显示已识别注册软件的外部快捷键；取消勾选可查看系统默认与未知进程条目。",
            AutoSize = true,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Font = _smallFont,
            Margin = new Padding(0, 3, 0, 0)
        };
        optionsBar.Controls.AddRange(new Control[]
        {
            _hideSystemDefaults, _hideUnknown, _identifyByPress, hint
        });
        topLayout.Controls.Add(optionsBar, 0, 2);

        var filterBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.White,
            Padding = new Padding(0, 2, 0, 0),
            Margin = new Padding(0)
        };
        filterBar.Controls.Add(NewOptionLabel("筛选：", 42));
        _filterBox = new TextBox
        {
            Width = 260,
            Height = 26,
            Font = _smallFont,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "热键 / 软件路径 / 进程名…",
            Margin = new Padding(0, 1, 10, 0)
        };
        var refreshHint = new Label
        {
            Text = "仅在进入本页时扫描",
            AutoSize = true,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Font = _smallFont,
            Margin = new Padding(4, 3, 0, 0)
        };
        filterBar.Controls.AddRange(new Control[] { _filterBox, refreshHint });
        topLayout.Controls.Add(filterBar, 0, 3);
        _top.Controls.Add(topLayout);

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
            GridLines = true,
            ShowItemToolTips = true,
            Font = _smallFont,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            BorderStyle = BorderStyle.None
        };
        _list.Columns.Add("HotKey", 180);
        _list.Columns.Add("注册软件（完整执行路径）", 420);
        _list.Columns.Add("进程名", 160);
        _list.Columns.Add("来源", 160);
        _list.Columns.Add("说明", 300);
        for (int i = 0; i < _list.Columns.Count && i < _columnWidths.Length; i++)
        {
            _columnWidths[i] = _list.Columns[i].Width;
        }
        _list.ColumnClick += (_, e) =>
        {
            if (e.Column < 0)
            {
                return;
            }
            if (_sortColumn == e.Column)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _sortColumn = e.Column;
                _sortAscending = true;
            }
            RefreshList();
        };
        _list.ContextMenuStrip = BuildMenu();

        _bottomStatus = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Font = _smallFont,
            Text = "尚未扫描"
        };

        Controls.Add(_list);
        Controls.Add(_bottomStatus);
        Controls.Add(_top);
        Controls.SetChildIndex(_list, 0);
        Controls.SetChildIndex(_bottomStatus, 1);
        Controls.SetChildIndex(_top, 2);

        _identifyByPress.CheckedChanged += (_, _) => UpdatePressIdentify();
        _hideSystemDefaults.CheckedChanged += (_, _) => RefreshList();
        _hideUnknown.CheckedChanged += (_, _) => RefreshList();
        _filterBox.TextChanged += (_, _) => RefreshList();
        _identifyTimer = new System.Windows.Forms.Timer { Interval = 800 };
        _identifyTimer.Tick += (_, _) =>
        {
            _identifyTimer!.Stop();
            IdentifyPendingHotKey();
        };

        Disposed += (_, _) =>
        {
            _disposed = true;
            StopPressIdentify();
            try { _probeWindow?.DestroyHandle(); } catch { }
            _probeWindow = null;
        };
    }

    // ================= UI =================

    private PillButton NewButton(string text, int width,
        PillButtonStyle style = PillButtonStyle.Light)
    {
        return new PillButton
        {
            Text = text,
            Width = width,
            Height = 30,
            ButtonStyle = style,
            Font = _smallFont,
            Margin = new Padding(0, 3, 6, 3)
        };
    }

    private Label NewOptionLabel(string text, int width = 22)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Width = width,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Font = _smallFont,
            Margin = new Padding(0, 2, 8, 0)
        };
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { Renderer = CreateRenderer() };
        menu.Items.Add("复制选中", null, (_, _) => CopySelected());
        menu.Items.Add("复制全部", null, (_, _) => CopyAll());
        menu.Items.Add("复制软件路径", null, (_, _) => CopyOwnerPath());
        menu.Items.Add("打开软件所在目录", null, (_, _) => OpenOwnerFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("导出 CSV…", null, (_, _) => Export("csv"));
        menu.Items.Add("导出 TXT…", null, (_, _) => Export("txt"));
        menu.Items.Add("导出 HTML…", null, (_, _) => Export("html"));
        menu.Items.Add("导出 XML…", null, (_, _) => Export("xml"));
        menu.Items.Add(new ToolStripSeparator());
        var columns = new ToolStripMenuItem("显示列");
        for (int i = 0; i < _list.Columns.Count; i++)
        {
            int index = i;
            var item = new ToolStripMenuItem(_list.Columns[i].Text)
            {
                CheckOnClick = true,
                Checked = true
            };
            item.CheckedChanged += (_, _) =>
            {
                int width = _columnWidths[index];
                _list.Columns[index].Width = item.Checked ? Math.Max(50, width) : 0;
            };
            columns.DropDownItems.Add(item);
        }
        menu.Items.Add(columns);
        var grid = new ToolStripMenuItem("显示网格线")
        {
            CheckOnClick = true,
            Checked = _list.GridLines
        };
        grid.CheckedChanged += (_, _) => _list.GridLines = grid.Checked;
        menu.Items.Add(grid);
        var markRows = new ToolStripMenuItem("奇偶行交替底色")
        {
            CheckOnClick = true,
            Checked = _markOddEven
        };
        markRows.CheckedChanged += (_, _) =>
        {
            _markOddEven = markRows.Checked;
            RefreshList();
        };
        menu.Items.Add(markRows);
        menu.Items.Add("自动调整列宽", null, (_, _) =>
        {
            for (int i = 0; i < _list.Columns.Count; i++)
            {
                _list.AutoResizeColumn(i, ColumnHeaderAutoResizeStyle.HeaderSize);
                int headerWidth = _list.Columns[i].Width;
                _list.AutoResizeColumn(i, ColumnHeaderAutoResizeStyle.ColumnContent);
                _list.Columns[i].Width = Math.Max(headerWidth, _list.Columns[i].Width);
                _columnWidths[i] = _list.Columns[i].Width;
            }
        });
        menu.Opening += (_, _) =>
        {
            bool has = _list.SelectedItems.Count > 0;
            foreach (ToolStripItem item in menu.Items)
            {
                if (item is ToolStripMenuItem entry
                    && (entry.Text?.StartsWith("复制选中") == true
                        || entry.Text?.StartsWith("复制软件路径") == true
                        || entry.Text?.StartsWith("打开软件") == true))
                {
                    entry.Enabled = has;
                }
            }
        };
        return menu;
    }

    private static ToolStripRenderer CreateRenderer() =>
        new ToolStripProfessionalRenderer(new MoniColorTable()) { RoundedEdges = false };

    private sealed class MoniColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.White;
        public override Color ImageMarginGradientBegin => Color.White;
        public override Color ImageMarginGradientMiddle => Color.White;
        public override Color ImageMarginGradientEnd => Color.White;
        public override Color MenuItemSelected => Color.FromArgb(224, 239, 254);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(224, 239, 254);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(224, 239, 254);
        public override Color MenuItemBorder => Color.FromArgb(191, 219, 254);
        public override Color MenuBorder => Color.FromArgb(203, 213, 225);
        public override Color SeparatorDark => Color.FromArgb(226, 232, 240);
        public override Color SeparatorLight => Color.White;
        public override Color CheckBackground => Color.FromArgb(219, 234, 254);
        public override Color CheckSelectedBackground => Color.FromArgb(219, 234, 254);
    }

    // ================= 扫描 =================

    /// <summary>由网络测试窗口在选择 / 离开该选项卡时调用。</summary>
    public void SetActive(bool active)
    {
        if (_isActive == active || _disposed)
        {
            return;
        }
        _isActive = active;
        if (active)
        {
            _statusLabel.Text = "● 已启用";
            _statusLabel.ForeColor = ShutdownTheme.Muted;
            _ = ScanAsync();
        }
        else
        {
            _statusLabel.Text = "● 未启用（切换到该选项卡后自动扫描）";
            _statusLabel.ForeColor = ShutdownTheme.Muted;
            StopPressIdentify();
            _identifyByPress.Checked = false;
        }
    }

    private async Task ScanAsync()
    {
        if (_scanning || _disposed || !_isActive)
        {
            return;
        }
        _scanning = true;
        _refreshButton.Enabled = false;
        _statusLabel.Text = "● 正在扫描热键与快捷方式…";
        _statusLabel.ForeColor = ShutdownTheme.Accent;
        try
        {
            var shortcuts = await Task.Run(ScanShortcutHotKeys).ConfigureAwait(true);
            if (_disposed || IsDisposed || !_isActive)
            {
                return;
            }
            var probes = ProbeGlobalHotKeys();
            MergeEntries(shortcuts, probes);
            _lastScan = DateTime.Now;
            RefreshList();
            int systemCount = _entries.Count(entry => entry.IsSystemDefault);
            int knownThirdParty = _entries.Count(entry =>
                !entry.IsSystemDefault && entry.OwnerPath.Length > 0);
            int unknown = _entries.Count(entry => entry.OwnerPath.Length == 0);
            _statusLabel.Text = "● 扫描完成 · 已识别外部热键 " + knownThirdParty
                + " 个 · 系统默认 " + systemCount + " 个 · 未知 " + unknown + " 个"
                + (_hideSystemDefaults.Checked ? "（系统默认已隐藏）" : "")
                + (_hideUnknown.Checked ? "（未知已隐藏）" : "");
            _statusLabel.ForeColor = ShutdownTheme.Success;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("HotKeysList 扫描失败：" + ex);
            if (!_disposed && !IsDisposed)
            {
                _statusLabel.Text = "● 扫描失败：" + ex.Message;
                _statusLabel.ForeColor = ShutdownTheme.Danger;
            }
        }
        finally
        {
            _scanning = false;
            if (!_disposed && !IsDisposed)
            {
                _refreshButton.Enabled = true;
                UpdateBottomStatus();
            }
        }
    }

    private void MergeEntries(List<HotKeyEntry> shortcuts, List<HotKeyEntry> probes)
    {
        _entries.Clear();
        _entryMap.Clear();
        foreach (var entry in shortcuts)
        {
            if (entry.HotKey.Length == 0)
            {
                continue;
            }
            _entryMap[entry.HotKey] = entry;
        }
        foreach (var probe in probes)
        {
            if (probe.HotKey.Length == 0)
            {
                continue;
            }
            if (_entryMap.TryGetValue(probe.HotKey, out var existing))
            {
                existing.Source = existing.Source + " / RegisterHotKey";
                if (existing.Note.Length == 0)
                {
                    existing.Note = "系统当前已注册该全局热键";
                }
                continue;
            }
            _entryMap[probe.HotKey] = probe;
        }
        AddWindowsDefaultHotKeys();
        foreach (var entry in _entryMap.Values)
        {
            _entries.Add(entry);
        }
        _entries.Sort((left, right) => string.Compare(left.HotKey, right.HotKey,
            StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>补充 Windows 10 / 11 / Server 2016-2025 常用系统默认快捷键。</summary>
    private void AddWindowsDefaultHotKeys()
    {
        string explorer = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "explorer.exe");
        foreach (var (rawHotKey, action) in WindowsDefaultHotKeys)
        {
            string hotKey = NormalizeHotKey(rawHotKey);
            if (hotKey.Length == 0)
            {
                continue;
            }
            if (_entryMap.TryGetValue(hotKey, out var existing))
            {
                // 只把“探测到但没有归属”的系统组合归入系统默认；
                // 快捷方式或 MiniMonitor 的明确归属保持不变。
                if (existing.Source.Contains("Windows 系统",
                        StringComparison.OrdinalIgnoreCase))
                {
                    existing.IsSystemDefault = true;
                }
                else if (existing.OwnerPath.Length == 0
                    && existing.Source.Contains("RegisterHotKey",
                        StringComparison.OrdinalIgnoreCase))
                {
                    existing.IsSystemDefault = true;
                    existing.OwnerPath = explorer;
                    existing.OwnerName = "explorer";
                    existing.Source = "Windows 系统默认";
                    existing.Note = action;
                }
                continue;
            }
            _entryMap[hotKey] = new HotKeyEntry
            {
                HotKey = hotKey,
                OwnerPath = explorer,
                OwnerName = "explorer",
                Source = "Windows 系统默认",
                Note = action,
                IsSystemDefault = true
            };
        }
    }

    // ================= 快捷方式热键 =================

    private static List<HotKeyEntry> ScanShortcutHotKeys()
    {
        var result = new List<HotKeyEntry>();
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                ScanShortcutHotKeysCore(result);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null && result.Count == 0)
        {
            Debug.WriteLine("扫描快捷方式热键失败：" + error);
        }
        return result;
    }

    private static void ScanShortcutHotKeysCore(List<HotKeyEntry> result)
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
        {
            return;
        }
        object? shell = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell == null)
            {
                return;
            }
            foreach (string root in ShortcutRoots())
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(root, "*.lnk",
                        new EnumerationOptions
                        {
                            RecurseSubdirectories = true,
                            IgnoreInaccessible = true,
                            MaxRecursionDepth = 6
                        });
                }
                catch
                {
                    continue;
                }
                foreach (string file in files)
                {
                    if (result.Count >= 2000)
                    {
                        return;
                    }
                    object? shortcut = null;
                    try
                    {
                        shortcut = shellType.InvokeMember("CreateShortcut",
                            BindingFlags.InvokeMethod, null, shell, new object[] { file });
                        if (shortcut == null)
                        {
                            continue;
                        }
                        string hotkey = Convert.ToString(
                            shortcut.GetType().InvokeMember("Hotkey",
                                BindingFlags.GetProperty, null, shortcut, null)) ?? "";
                        string normalized = NormalizeHotKey(hotkey);
                        if (normalized.Length == 0 || !seen.Add(normalized))
                        {
                            continue;
                        }
                        string target = Convert.ToString(
                            shortcut.GetType().InvokeMember("TargetPath",
                                BindingFlags.GetProperty, null, shortcut, null)) ?? "";
                        string arguments = Convert.ToString(
                            shortcut.GetType().InvokeMember("Arguments",
                                BindingFlags.GetProperty, null, shortcut, null)) ?? "";
                        string description = Convert.ToString(
                            shortcut.GetType().InvokeMember("Description",
                                BindingFlags.GetProperty, null, shortcut, null)) ?? "";
                        if (target.Length == 0)
                        {
                            target = file;
                        }
                        result.Add(new HotKeyEntry
                        {
                            HotKey = normalized,
                            OwnerPath = target,
                            OwnerName = Path.GetFileNameWithoutExtension(target),
                            Source = "Windows 快捷方式",
                            Note = (description.Length > 0 ? description + " · " : "")
                                + "快捷方式：" + file
                                + (arguments.Length > 0 ? " " + arguments : "")
                        });
                    }
                    catch
                    {
                        // 单个快捷方式损坏 / 无热键时跳过。
                    }
                    finally
                    {
                        if (shortcut != null && Marshal.IsComObject(shortcut))
                        {
                            try { Marshal.ReleaseComObject(shortcut); } catch { }
                        }
                    }
                }
            }
        }
        finally
        {
            if (shell != null && Marshal.IsComObject(shell))
            {
                try { Marshal.ReleaseComObject(shell); } catch { }
            }
        }
    }

    private static IEnumerable<string> ShortcutRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Path.Combine(appData,
            "Microsoft", "Internet Explorer", "Quick Launch");
    }

    private static string NormalizeHotKey(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }
        string trimmed = text.Trim();
        if (trimmed.Equals("Win", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Windows", StringComparison.OrdinalIgnoreCase))
        {
            return "Win";
        }
        bool ctrl = false;
        bool alt = false;
        bool shift = false;
        bool win = false;
        string key = "";
        foreach (string part in text.Split('+',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    ctrl = true;
                    break;
                case "alt":
                    alt = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                case "win":
                case "windows":
                case "lwin":
                case "rwin":
                    win = true;
                    break;
                default:
                    key = NormalizeKeyName(part);
                    break;
            }
        }
        if (key.Length == 0)
        {
            return "";
        }
        var parts = new List<string>(5);
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        if (win) parts.Add("Win");
        parts.Add(key);
        return string.Join("+", parts);
    }

    private static string NormalizeKeyName(string part)
    {
        string key = part.Trim().ToUpperInvariant();
        return key switch
        {
            "PRINTSCREEN" or "PRTSC" or "SNAPSHOT" => "PrintScreen",
            "PAGEUP" or "PGUP" or "PRIOR" => "PageUp",
            "PAGEDOWN" or "PGDN" or "NEXT" => "PageDown",
            "INSERT" or "INS" => "Insert",
            "DELETE" or "DEL" => "Delete",
            "HOME" => "Home",
            "END" => "End",
            "SPACE" => "Space",
            "TAB" => "Tab",
            "ENTER" or "RETURN" => "Enter",
            "ESC" or "ESCAPE" => "Esc",
            "BACK" or "BACKSPACE" => "Backspace",
            "PAUSE" => "Pause",
            "SCROLLLOCK" or "SCROLL" => "ScrollLock",
            "NUMLOCK" => "NumLock",
            "CAPSLOCK" or "CAPITAL" => "CapsLock",
            "LEFT" => "Left",
            "RIGHT" => "Right",
            "UP" => "Up",
            "DOWN" => "Down",
            "NUMPLUS" or "ADD" => "NumPlus",
            "NUMMINUS" or "SUBTRACT" => "NumMinus",
            "NUMMULTIPLY" or "MULTIPLY" => "NumMultiply",
            "NUMDIVIDE" or "DIVIDE" => "NumDivide",
            "NUMDECIMAL" or "DECIMAL" => "NumDecimal",
            "BREAK" => "Break",
            _ when key.StartsWith("NUM", StringComparison.Ordinal)
                && key.Length == 4 && char.IsDigit(key[3]) => "Num" + key[3],
            _ => part.Trim().Length == 1
                ? part.Trim().ToUpperInvariant()
                : CultureSafeUpper(part.Trim())
        };
    }

    private static string CultureSafeUpper(string value) =>
        value.Length <= 4 ? value.ToUpperInvariant() : value;

    // ================= RegisterHotKey 探测 =================

    private List<HotKeyEntry> ProbeGlobalHotKeys()
    {
        var result = new List<HotKeyEntry>();
        EnsureProbeWindow();
        IntPtr handle = _probeWindow?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return result;
        }
        string selfPath = Environment.ProcessPath
            ?? Application.ExecutablePath ?? "";
        string selfName = Path.GetFileNameWithoutExtension(selfPath);
        var knownSelf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ctrl+Shift+C"] = "截图",
            ["Ctrl+Shift+W"] = "滚动截图",
            ["Shift+PrintScreen"] = "屏幕录制",
            ["Shift+F9"] = "屏幕录制"
        };
        var knownSystem = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Win+E"] = "资源管理器",
            ["Win+R"] = "运行对话框",
            ["Win+D"] = "显示桌面",
            ["Win+L"] = "锁定计算机",
            ["Win+I"] = "Windows 设置",
            ["Win+S"] = "搜索",
            ["Win+A"] = "操作中心",
            ["Win+X"] = "快速链接菜单",
            ["Win+Tab"] = "任务视图",
            ["Win+Shift+S"] = "截图工具",
            ["Ctrl+Shift+Esc"] = "任务管理器",
            ["Alt+Tab"] = "切换窗口",
            ["Alt+F4"] = "关闭窗口",
            ["Ctrl+Alt+Delete"] = "安全选项"
        };
        var modifiers = new (uint Mask, string Name)[]
        {
            (ModControl, "Ctrl"),
            (ModAlt, "Alt"),
            (ModShift, "Shift"),
            (ModWin, "Win")
        };
        var modifierSets = new List<(uint Mask, string Text)>();
        for (int bits = 1; bits < 16; bits++)
        {
            uint mask = 0;
            var names = new List<string>();
            for (int i = 0; i < modifiers.Length; i++)
            {
                if ((bits & (1 << i)) == 0)
                {
                    continue;
                }
                mask |= modifiers[i].Mask;
                names.Add(modifiers[i].Name);
            }
            modifierSets.Add((mask, string.Join("+", names)));
        }
        int nextId = 1;
        foreach (var (mask, text) in modifierSets)
        {
            foreach (var (vk, keyName) in ProbeKeys())
            {
                string hotKey = text + "+" + keyName;
                bool registered = RegisterHotKey(handle, nextId++,
                    mask | ModNoRepeat, (uint)vk);
                if (registered)
                {
                    UnregisterHotKey(handle, nextId - 1);
                    continue;
                }
                int error = Marshal.GetLastWin32Error();
                if (error != ErrorHotKeyAlreadyRegistered)
                {
                    continue;
                }
                var entry = new HotKeyEntry
                {
                    HotKey = NormalizeHotKey(hotKey),
                    Source = "RegisterHotKey 全局注册",
                    OwnerPath = "",
                    OwnerName = "未知",
                    Note = "Windows 未公开注册进程，无法自动确认；可开启“按键识别注册程序”尝试推断"
                };
                if (knownSelf.TryGetValue(entry.HotKey, out string? action))
                {
                    entry.OwnerPath = selfPath;
                    entry.OwnerName = selfName;
                    entry.Source = "MiniMonitor";
                    entry.Note = "MiniMonitor 内置热键：" + action;
                }
                else if (knownSystem.TryGetValue(entry.HotKey, out string? systemAction))
                {
                    entry.OwnerPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                        "explorer.exe");
                    entry.OwnerName = "explorer";
                    entry.Source = "Windows 系统";
                    entry.Note = "系统功能：" + systemAction;
                    entry.IsSystemDefault = true;
                }
                result.Add(entry);
            }
        }
        return result;
    }

    private static IEnumerable<(int Vk, string Name)> ProbeKeys()
    {
        for (char c = 'A'; c <= 'Z'; c++)
        {
            yield return (c, c.ToString());
        }
        for (char c = '0'; c <= '9'; c++)
        {
            yield return (c, c.ToString());
        }
        for (int i = 1; i <= 24; i++)
        {
            yield return (0x6F + i, "F" + i);
        }
        yield return (0x2C, "PrintScreen");
        yield return (0x2D, "Insert");
        yield return (0x2E, "Delete");
        yield return (0x24, "Home");
        yield return (0x23, "End");
        yield return (0x21, "PageUp");
        yield return (0x22, "PageDown");
        yield return (0x25, "Left");
        yield return (0x26, "Up");
        yield return (0x27, "Right");
        yield return (0x28, "Down");
        yield return (0x20, "Space");
        yield return (0x09, "Tab");
        yield return (0x0D, "Enter");
        yield return (0x1B, "Esc");
        yield return (0x08, "Backspace");
        yield return (0x13, "Pause");
        yield return (0x91, "ScrollLock");
        yield return (0x90, "NumLock");
        yield return (0x14, "CapsLock");
        yield return (0x6B, "NumPlus");
        yield return (0x6D, "NumMinus");
        yield return (0x6A, "NumMultiply");
        yield return (0x6F, "NumDivide");
        yield return (0x6E, "NumDecimal");
        for (int i = 0; i <= 9; i++)
        {
            yield return (0x60 + i, "Num" + i);
        }
        yield return (0xBA, ";");
        yield return (0xBB, "=");
        yield return (0xBC, ",");
        yield return (0xBD, "-");
        yield return (0xBE, ".");
        yield return (0xBF, "/");
        yield return (0xDB, "[");
        yield return (0xDC, "\\");
        yield return (0xDD, "]");
        yield return (0xDE, "'");
    }

    private void EnsureProbeWindow()
    {
        if (_probeWindow != null && _probeWindow.Handle != IntPtr.Zero)
        {
            return;
        }
        _probeWindow = new NativeWindow();
        _probeWindow.CreateHandle(new CreateParams
        {
            Caption = "MiniMonitorHotkeyProbe",
            Parent = new IntPtr(-3),
            Style = 0
        });
    }

    // ================= 按键识别（实验） =================

    private void UpdatePressIdentify()
    {
        if (_identifyByPress.Checked)
        {
            StartPressIdentify();
        }
        else
        {
            StopPressIdentify();
        }
    }

    private void StartPressIdentify()
    {
        if (_keyboardHook != IntPtr.Zero || _disposed)
        {
            return;
        }
        _hookProc = KeyboardHookCallback;
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _hookProc, IntPtr.Zero, 0);
        if (_keyboardHook == IntPtr.Zero)
        {
            _statusLabel.Text = "● 按键识别启动失败";
            _statusLabel.ForeColor = ShutdownTheme.Danger;
        }
        else
        {
            _statusLabel.Text = "● 按键识别已开启：按下未知热键后尝试识别前台程序";
            _statusLabel.ForeColor = ShutdownTheme.Muted;
        }
    }

    private void StopPressIdentify()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
        _hookProc = null;
        try { _identifyTimer?.Stop(); } catch { }
        _pendingHotKey = "";
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WmKeyDown || wParam == (IntPtr)WmSysKeyDown))
        {
            var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            int vk = (int)info.vkCode;
            if (!IsModifierKey(vk))
            {
                string hotKey = CurrentHotKey(vk);
                if (hotKey.Length > 0)
                {
                    lock (_entries)
                    {
                        if (_entryMap.TryGetValue(hotKey, out var entry)
                            && entry.OwnerPath.Length == 0)
                        {
                            _pendingHotKey = hotKey;
                            _pendingForegroundPid = ForegroundProcessId();
                            try
                            {
                                _identifyTimer?.Stop();
                                _identifyTimer?.Start();
                            }
                            catch
                            {
                            }
                        }
                    }
                }
            }
        }
        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private static bool IsModifierKey(int vk) =>
        vk is VkControl or VkMenu or VkShift or VkLWin or VkRWin
        or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    private static string CurrentHotKey(int vk)
    {
        bool ctrl = (GetAsyncKeyState(VkControl) & 0x8000) != 0;
        bool alt = (GetAsyncKeyState(VkMenu) & 0x8000) != 0;
        bool shift = (GetAsyncKeyState(VkShift) & 0x8000) != 0;
        bool win = (GetAsyncKeyState(VkLWin) & 0x8000) != 0
            || (GetAsyncKeyState(VkRWin) & 0x8000) != 0;
        if (!ctrl && !alt && !shift && !win)
        {
            return "";
        }
        string key = KeyNameFromVk(vk);
        if (key.Length == 0)
        {
            return "";
        }
        var parts = new List<string>(5);
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        if (win) parts.Add("Win");
        parts.Add(key);
        return string.Join("+", parts);
    }

    private static string KeyNameFromVk(int vk)
    {
        if (vk >= 'A' && vk <= 'Z') return ((char)vk).ToString();
        if (vk >= '0' && vk <= '9') return ((char)vk).ToString();
        if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);
        return vk switch
        {
            0x2C => "PrintScreen",
            0x2D => "Insert",
            0x2E => "Delete",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x20 => "Space",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Esc",
            0x08 => "Backspace",
            0x13 => "Pause",
            0x91 => "ScrollLock",
            0x90 => "NumLock",
            0x14 => "CapsLock",
            _ => ""
        };
    }

    private void IdentifyPendingHotKey()
    {
        string hotKey = _pendingHotKey;
        _pendingHotKey = "";
        if (hotKey.Length == 0 || _disposed || IsDisposed)
        {
            return;
        }
        int pid = ForegroundProcessId();
        if (pid <= 0 || pid == _pendingForegroundPid)
        {
            return;
        }
        string path = QueryProcessPath(pid);
        string name = "";
        try
        {
            using var process = Process.GetProcessById(pid);
            name = process.ProcessName;
        }
        catch
        {
        }
        if (path.Length == 0)
        {
            return;
        }
        lock (_entries)
        {
            if (_entryMap.TryGetValue(hotKey, out var entry) && entry.OwnerPath.Length == 0)
            {
                entry.OwnerPath = path;
                entry.OwnerName = name.Length > 0 ? name : Path.GetFileNameWithoutExtension(path);
                entry.Source = entry.Source + " / 按键推断";
                entry.Note = "根据按下热键后前台程序变化推断，仅供参考";
            }
        }
        RefreshList();
        _statusLabel.Text = "● 已识别 " + hotKey + " → " + path;
        _statusLabel.ForeColor = ShutdownTheme.Success;
    }

    private static int ForegroundProcessId()
    {
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return 0;
        }
        GetWindowThreadProcessId(window, out uint pid);
        return (int)pid;
    }

    // ================= 列表 =================

    private void RefreshList()
    {
        if (_disposed || IsDisposed)
        {
            return;
        }
        var selected = _list.SelectedItems.Cast<ListViewItem>()
            .Select(item => item.Tag as HotKeyEntry)
            .Where(entry => entry != null)
            .Select(entry => entry!.HotKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<HotKeyEntry> entries;
        lock (_entries)
        {
            entries = _entries.ToList();
        }
        string filter = _filterBox.Text.Trim();
        if (filter.Length > 0)
        {
            entries = entries.Where(entry =>
                entry.HotKey.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || entry.OwnerPath.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || entry.OwnerName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || entry.Source.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || entry.Note.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (_hideSystemDefaults.Checked)
        {
            entries = entries.Where(entry => !entry.IsSystemDefault).ToList();
        }
        if (_hideUnknown.Checked)
        {
            entries = entries.Where(entry => entry.OwnerPath.Length > 0).ToList();
        }
        entries.Sort(CompareEntries);
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            int rowIndex = 0;
            foreach (var entry in entries)
            {
                var item = new ListViewItem(entry.HotKey) { Tag = entry };
                item.SubItems.Add(entry.OwnerPath.Length > 0
                    ? entry.OwnerPath : "（未知：Windows 未公开注册进程）");
                item.SubItems.Add(entry.OwnerName);
                item.SubItems.Add(entry.Source);
                item.SubItems.Add(entry.Note);
                if (_markOddEven && rowIndex % 2 == 1)
                {
                    item.BackColor = Color.FromArgb(248, 250, 252);
                }
                if (entry.OwnerPath.Length > 0)
                {
                    item.ForeColor = Color.FromArgb(6, 95, 70);
                }
                else
                {
                    item.ForeColor = Color.FromArgb(148, 163, 184);
                }
                item.ToolTipText = entry.HotKey
                    + "\n注册软件：" + (entry.OwnerPath.Length > 0
                        ? entry.OwnerPath : "未知")
                    + "\n来源：" + entry.Source
                    + "\n说明：" + entry.Note;
                if (selected.Contains(entry.HotKey))
                {
                    item.Selected = true;
                }
                _list.Items.Add(item);
                rowIndex++;
            }
        }
        finally
        {
            _list.EndUpdate();
        }
        UpdateBottomStatus();
    }

    private int CompareEntries(HotKeyEntry left, HotKeyEntry right)
    {
        int result = _sortColumn switch
        {
            ColHotKey => string.Compare(left.HotKey, right.HotKey,
                StringComparison.OrdinalIgnoreCase),
            ColOwner => string.Compare(left.OwnerPath, right.OwnerPath,
                StringComparison.CurrentCultureIgnoreCase),
            ColProcess => string.Compare(left.OwnerName, right.OwnerName,
                StringComparison.CurrentCultureIgnoreCase),
            ColSource => string.Compare(left.Source, right.Source,
                StringComparison.CurrentCultureIgnoreCase),
            ColNote => string.Compare(left.Note, right.Note,
                StringComparison.CurrentCultureIgnoreCase),
            _ => 0
        };
        if (result == 0)
        {
            result = string.Compare(left.HotKey, right.HotKey,
                StringComparison.OrdinalIgnoreCase);
        }
        return _sortAscending ? result : -result;
    }

    private void UpdateBottomStatus()
    {
        int known;
        int total;
        int system;
        int visible;
        lock (_entries)
        {
            known = _entries.Count(entry => entry.OwnerPath.Length > 0);
            total = _entries.Count;
            system = _entries.Count(entry => entry.IsSystemDefault);
        }
        visible = _list.Items.Count;
        int unknown = total - known;
        _bottomStatus.Text = "共 " + total + " 个热键 · 第三方 "
            + (total - system) + " 个 · 系统默认 " + system + " 个"
            + (_hideSystemDefaults.Checked ? "（已隐藏）" : "")
            + " · 当前显示 " + visible + " 个 · 已识别注册软件 " + known + " 个"
            + " · 未知 " + unknown + " 个"
            + (_hideUnknown.Checked ? "（已隐藏）" : "")
            + (_lastScan == DateTime.MinValue ? ""
                : "  ·  上次扫描 " + _lastScan.ToString("HH:mm:ss"));
    }

    private HotKeyEntry? SelectedEntry()
    {
        if (_list.SelectedItems.Count == 0)
        {
            return null;
        }
        return _list.SelectedItems[0].Tag as HotKeyEntry;
    }

    private void CopyOwnerPath()
    {
        var entry = SelectedEntry();
        if (entry == null || entry.OwnerPath.Length == 0)
        {
            return;
        }
        try { Clipboard.SetText(entry.OwnerPath); } catch { }
    }

    private void OpenOwnerFolder()
    {
        var entry = SelectedEntry();
        if (entry == null || entry.OwnerPath.Length == 0)
        {
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "/select,\"" + entry.OwnerPath + "\"",
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    // ================= 导出 / 复制 =================

    private void ShowExportMenu()
    {
        var menu = new ContextMenuStrip { Renderer = CreateRenderer() };
        menu.Items.Add("CSV 文件（.csv）", null, (_, _) => Export("csv"));
        menu.Items.Add("制表符文本（.txt）", null, (_, _) => Export("txt"));
        menu.Items.Add("HTML 文件（.html）", null, (_, _) => Export("html"));
        menu.Items.Add("XML 文件（.xml）", null, (_, _) => Export("xml"));
        menu.Show(_exportButton, new Point(0, _exportButton.Height));
    }

    private void Export(string format)
    {
        string extension = format switch
        {
            "csv" => "csv",
            "html" => "html",
            "xml" => "xml",
            _ => "txt"
        };
        string filter = format switch
        {
            "csv" => "CSV 文件|*.csv",
            "html" => "HTML 文件|*.html",
            "xml" => "XML 文件|*.xml",
            _ => "文本文件|*.txt"
        };
        using var dialog = new SaveFileDialog
        {
            Title = "导出快捷键注册列表",
            Filter = filter,
            FileName = "HotKeysList_"
                + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "." + extension
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }
        try
        {
            List<HotKeyEntry> entries;
            lock (_entries)
            {
                entries = _entries.ToList();
            }
            entries.Sort(CompareEntries);
            string content = format switch
            {
                "csv" => BuildCsv(entries),
                "html" => BuildHtml(entries),
                "xml" => BuildXml(entries),
                _ => BuildText(entries)
            };
            File.WriteAllText(dialog.FileName, content, new UTF8Encoding(true));
            _bottomStatus.Text = "已导出：" + dialog.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "导出失败：\n" + ex.Message,
                "快捷键注册列表", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string[] ExportHeaders() => new[]
    {
        "HotKey", "注册软件完整路径", "进程名", "来源", "说明"
    };

    private static string[] ExportRow(HotKeyEntry entry) => new[]
    {
        entry.HotKey,
        entry.OwnerPath,
        entry.OwnerName,
        entry.Source,
        entry.Note
    };

    private static string BuildText(List<HotKeyEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join("\t", ExportHeaders()));
        foreach (var entry in entries)
        {
            builder.AppendLine(string.Join("\t", ExportRow(entry)));
        }
        return builder.ToString();
    }

    private static string BuildCsv(List<HotKeyEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", ExportHeaders().Select(QuoteCsv)));
        foreach (var entry in entries)
        {
            builder.AppendLine(string.Join(",", ExportRow(entry).Select(QuoteCsv)));
        }
        return builder.ToString();
    }

    private static string QuoteCsv(string value) =>
        "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

    private static string BuildHtml(List<HotKeyEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
        builder.AppendLine("<title>HotKeysList</title><style>");
        builder.AppendLine("body{font-family:'Microsoft YaHei UI',Arial,sans-serif;background:#f8fafc;color:#1e293b;padding:20px}");
        builder.AppendLine("h1{font-size:20px}table{border-collapse:collapse;width:100%;background:#fff}");
        builder.AppendLine("th,td{border:1px solid #cbd5e1;padding:6px 9px;font-size:13px;text-align:left}");
        builder.AppendLine("th{background:#e2e8f0}tr:nth-child(even) td{background:#f8fafc}");
        builder.AppendLine("</style></head><body><h1>HotKeysList</h1>");
        builder.AppendLine("<p>导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            + " · 共 " + entries.Count + " 个热键</p><table><thead><tr>");
        foreach (string header in ExportHeaders())
        {
            builder.Append("<th>").Append(WebUtility.HtmlEncode(header)).AppendLine("</th>");
        }
        builder.AppendLine("</tr></thead><tbody>");
        foreach (var entry in entries)
        {
            builder.AppendLine("<tr>");
            foreach (string value in ExportRow(entry))
            {
                builder.Append("<td>").Append(WebUtility.HtmlEncode(value)).AppendLine("</td>");
            }
            builder.AppendLine("</tr>");
        }
        builder.AppendLine("</tbody></table></body></html>");
        return builder.ToString();
    }

    private static string BuildXml(List<HotKeyEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        builder.AppendLine("<hotKeysList exported=\""
            + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") + "\">");
        foreach (var entry in entries)
        {
            builder.AppendLine("  <hotkey>");
            AppendXml(builder, "hotKey", entry.HotKey);
            AppendXml(builder, "ownerPath", entry.OwnerPath);
            AppendXml(builder, "ownerName", entry.OwnerName);
            AppendXml(builder, "source", entry.Source);
            AppendXml(builder, "note", entry.Note);
            builder.AppendLine("  </hotkey>");
        }
        builder.AppendLine("</hotKeysList>");
        return builder.ToString();
    }

    private static void AppendXml(StringBuilder builder, string name, string value)
    {
        builder.Append("    <").Append(name).Append('>')
            .Append(WebUtility.HtmlEncode(value ?? ""))
            .Append("</").Append(name).AppendLine(">");
    }

    private void CopySelected()
    {
        var entries = _list.SelectedItems.Cast<ListViewItem>()
            .Select(item => item.Tag as HotKeyEntry)
            .Where(entry => entry != null)
            .Select(entry => entry!)
            .ToList();
        if (entries.Count == 0)
        {
            return;
        }
        CopyToClipboard(entries);
    }

    private void CopyAll()
    {
        List<HotKeyEntry> entries;
        lock (_entries)
        {
            entries = _entries.ToList();
        }
        entries.Sort(CompareEntries);
        CopyToClipboard(entries);
    }

    private static void CopyToClipboard(List<HotKeyEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join("\t", ExportHeaders()));
        foreach (var entry in entries)
        {
            builder.AppendLine(string.Join("\t", ExportRow(entry)));
        }
        try
        {
            Clipboard.SetText(builder.ToString());
        }
        catch
        {
        }
    }

    // ================= P/Invoke =================

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id,
        uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook,
        LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
        IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd,
        out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess,
        bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr hProcess,
        uint dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static string QueryProcessPath(int pid)
    {
        IntPtr handle = OpenProcess(0x1000, false, pid);
        if (handle == IntPtr.Zero)
        {
            return "";
        }
        try
        {
            var builder = new StringBuilder(1024);
            int size = builder.Capacity;
            return QueryFullProcessImageNameW(handle, 0, builder, ref size)
                ? builder.ToString(0, size) : "";
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            StopPressIdentify();
            _identifyTimer?.Dispose();
            _identifyTimer = null;
            try { _probeWindow?.DestroyHandle(); } catch { }
            _probeWindow = null;
            _titleFont.Dispose();
            _statusFont.Dispose();
            _smallFont.Dispose();
            _smallBoldFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
