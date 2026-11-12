using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MiniMonitor.XShell;

/// <summary>
/// XShell 复刻版主窗口：XShell 8 风格的菜单、功能区、快速会话栏、会话管理器、
/// 多标签终端、SFTP 与状态栏。所有数据仅保存在本机，不包含任何遥测代码。
/// </summary>
public sealed class XShellForm : Form
{
    private readonly XShellStore _store;
    private readonly XShellSettings _settings;
    private readonly MenuStrip _menu;
    private readonly Panel _ribbon;
    private readonly Panel _quickBar;
    private readonly SplitContainer _split;
    private readonly TreeView _tree;
    private readonly TextBox _searchBox;
    private readonly XShellTabControl _tabs;
    private readonly StatusStrip _status;
    private readonly ToolStripStatusLabel _stateLabel;
    private readonly ToolStripStatusLabel _hostLabel;
    private readonly ToolStripStatusLabel _sizeLabel;
    private readonly ToolStripStatusLabel _encodingLabel;
    private readonly ToolStripStatusLabel _sftpLabel;
    private readonly ToolStripStatusLabel _clockLabel;
    private readonly System.Windows.Forms.Timer _clock;
    private readonly System.Windows.Forms.Timer _themeWatch;
    private readonly ImageList _treeImages;
    private readonly Dictionary<string, ToolStripMenuItem> _themeItems = new();
    private readonly Dictionary<TabPage, XShellTerminalPage> _pages = new();
    private readonly Dictionary<TabPage, XShellSftpForm> _sftpForms = new();
    private MiniMonitor.XFtp.XFtpForm? _xftpForm;
    private bool _xftpOpening;
    private readonly Dictionary<TabPage, bool> _everConnected = new();
    private readonly Dictionary<TabPage, bool> _reconnecting = new();
    private readonly HashSet<TabPage> _manualDisconnect = new();
    private readonly ToolStripMenuItem _managerItem;
    private readonly ToolStripMenuItem _toolbarItem;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _launchItem;
    private XShellRibbonButton _managerRibbonButton = null!;
    private readonly Panel _hideStrip;
    /// <summary>快速会话按钮：名称 → 对应预置会话的主机地址。</summary>
    private static readonly (string Label, string Host)[] QuickLaunchSessions =
    {
        ("master", "192.168.33.90"),
        ("node1", "192.168.33.91"),
        ("node2", "192.168.33.92"),
        ("node3", "192.168.33.93"),
        ("96Ubuntu", "192.168.33.96"),
        ("97Debain", "192.168.33.97"),
        ("98Almalinux", "192.168.33.98"),
        ("99Rocky", "192.168.33.99"),
        ("66.90", "192.168.66.90"),
        ("66.91", "192.168.66.91"),
        ("66.92", "192.168.66.92"),
        ("66.93", "192.168.66.93")
    };

    private TabPage? _welcomeTab;
    private bool _fullScreen;
    private bool _closing;
    private Rectangle _restoreBounds;
    private FormBorderStyle _restoreBorder = FormBorderStyle.Sizable;

    public XShellForm()
    {
        _store = XShellStore.Load();
        _settings = _store.Settings;
        XShellTheme.Apply(_settings.UiTheme);

        Text = "XShell 复刻版";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(940, 620);
        ClientSize = new Size(
            Math.Max(960, _settings.WindowWidth),
            Math.Max(620, _settings.WindowHeight));
        BackColor = XShellTheme.Background;
        ForeColor = XShellTheme.Text;
        Font = XShellTheme.Ui(9f);
        AutoScaleMode = AutoScaleMode.Font;
        DoubleBuffered = true;
        KeyPreview = true;
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            // 图标读取失败不影响窗口。
        }
        HandleCreated += (_, _) => XShellWindowTheme.Apply(this);

        _managerItem = new ToolStripMenuItem("会话管理器(&M)") { CheckOnClick = true, Checked = _settings.ShowSessionManager };
        _toolbarItem = new ToolStripMenuItem("工具栏(&T)") { CheckOnClick = true, Checked = true };
        _statusItem = new ToolStripMenuItem("状态栏(&S)")
        {
            CheckOnClick = true,
            Checked = _settings.ShowStatusBar
        };
        _launchItem = new ToolStripMenuItem("快速会话栏(&Q)")
        {
            CheckOnClick = true,
            Checked = _settings.ShowQuickLaunch
        };

        _menu = BuildMenu();
        MainMenuStrip = _menu;
        _ribbon = BuildRibbon();
        _quickBar = BuildQuickLaunchBar();
        _hideStrip = BuildHideStrip();

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 5,
            BackColor = XShellTheme.Border,
            BorderStyle = BorderStyle.None,
            Panel1MinSize = 180,
            FixedPanel = FixedPanel.Panel1
        };
        _split.Panel2.BackColor = XShellTheme.Panel;
        _searchBox = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            Font = XShellTheme.Ui(9f),
            PlaceholderText = "搜索会话名称、地址或分组..."
        };
        _tree = new TreeView
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = XShellTheme.Panel,
            ForeColor = XShellTheme.Text,
            Font = XShellTheme.Ui(9f),
            FullRowSelect = true,
            HideSelection = false,
            ShowLines = false,
            ShowPlusMinus = true,
            ShowRootLines = true,
            ItemHeight = 27,
            LineColor = XShellTheme.Border,
            HotTracking = false
        };
        _treeImages = new ImageList
        {
            ImageSize = new Size(16, 16),
            ColorDepth = ColorDepth.Depth32Bit
        };
        _treeImages.Images.Add(XShellIcons.Server(XShellTheme.Accent, 16));
        _treeImages.Images.Add(XShellIcons.Folder(XShellTheme.Muted, 16));
        _tree.ImageList = _treeImages;
        _tree.AfterSelect += (_, _) => UpdateSelectionHint();
        _tree.NodeMouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.Node != null)
            {
                _tree.SelectedNode = e.Node;
            }
        };
        _tree.NodeMouseDoubleClick += (_, e) => ConnectNode(e.Node);
        var treeTip = new XShellToolTip();
        _tree.ShowNodeToolTips = false;
        _tree.NodeMouseHover += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Node?.ToolTipText))
            {
                Point position = _tree.PointToClient(Cursor.Position);
                treeTip.Show(e.Node.ToolTipText, _tree,
                    position.X + 14, position.Y + 18, 4000);
            }
        };
        _tree.MouseLeave += (_, _) => treeTip.Hide(_tree);
        _tree.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                ConnectNode(_tree.SelectedNode);
            }
        };
        _tree.ContextMenuStrip = BuildTreeMenu();
        _searchBox.TextChanged += (_, _) => RefreshTree();

        BuildSessionPanel();

        _tabs = new XShellTabControl { Dock = DockStyle.Fill };
        _tabs.TabCloseRequested += CloseTab;
        _tabs.TabRightClicked += ShowTabMenu;
        _tabs.SelectedIndexChanged += (_, _) => UpdateStatus();

        _split.Panel2.Controls.Add(_tabs);

        _status = new StatusStrip
        {
            Dock = DockStyle.Bottom,
            BackColor = XShellTheme.Status,
            ForeColor = Color.White,
            SizingGrip = false,
            Font = XShellTheme.Ui(8.5f),
            Padding = new Padding(8, 2, 8, 2),
            RenderMode = ToolStripRenderMode.Professional
        };
        XShellTheme.StyleMenu(_status);
        _stateLabel = new ToolStripStatusLabel("● 就绪")
        {
            Name = "state",
            ForeColor = XShellTheme.HeroMuted,
            AutoSize = false,
            Width = 150,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _hostLabel = new ToolStripStatusLabel("")
        {
            AutoSize = false,
            Width = 300,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _sizeLabel = new ToolStripStatusLabel("")
        {
            AutoSize = false,
            Width = 110,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _encodingLabel = new ToolStripStatusLabel("")
        {
            AutoSize = false,
            Width = 100,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _sftpLabel = new ToolStripStatusLabel("")
        {
            AutoSize = false,
            Width = 110,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _clockLabel = new ToolStripStatusLabel("")
        {
            AutoSize = false,
            Width = 110,
            TextAlign = ContentAlignment.MiddleRight
        };
        _status.Items.AddRange(new ToolStripItem[]
        {
            _stateLabel, Separator(), _hostLabel, Separator(), _sizeLabel,
            Separator(), _encodingLabel, Separator(), _sftpLabel,
            new ToolStripStatusLabel { Spring = true }, _clockLabel
        });

        _clock = new System.Windows.Forms.Timer { Interval = 1000 };
        _clock.Tick += (_, _) => _clockLabel.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        _themeWatch = new System.Windows.Forms.Timer { Interval = 4000 };
        _themeWatch.Tick += (_, _) => CheckSystemTheme();

        Controls.Add(_split);
        Controls.Add(_hideStrip);
        Controls.Add(_status);
        Controls.Add(_quickBar);
        Controls.Add(_ribbon);
        Controls.Add(_menu);

        _managerItem.CheckedChanged += (_, _) => ApplySessionManagerVisibility();
        _toolbarItem.CheckedChanged += (_, _) => _ribbon.Visible = _toolbarItem.Checked;
        _statusItem.CheckedChanged += (_, _) =>
        {
            _status.Visible = _statusItem.Checked;
            _settings.ShowStatusBar = _statusItem.Checked;
            _store.Save();
        };
        _launchItem.CheckedChanged += (_, _) =>
        {
            _quickBar.Visible = _launchItem.Checked;
            _settings.ShowQuickLaunch = _launchItem.Checked;
            _store.Save();
        };
        _quickBar.Visible = _settings.ShowQuickLaunch;
        _status.Visible = _settings.ShowStatusBar;

        RestoreWindowState();
        RefreshTree();
        if (_settings.ShowWelcomeOnStart)
        {
            ShowWelcome();
        }
        UpdateStatus();
        _clockLabel.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        _clock.Start();
        _themeWatch.Start();
        ApplySessionManagerVisibility();
    }

    private static ToolStripStatusLabel Separator() => new("│")
    {
        ForeColor = Color.FromArgb(90, XShellTheme.Accent),
        AutoSize = false,
        Width = 2
    };

    private ToolStripMenuItem Entry(string text, EventHandler? click = null)
    {
        var item = new ToolStripMenuItem(text);
        if (click != null)
        {
            item.Click += click;
        }
        return item;
    }

    // ================= 菜单 =================

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip
        {
            Dock = DockStyle.Top,
            BackColor = XShellTheme.Current.MenuBack,
            ForeColor = XShellTheme.Text,
            Font = XShellTheme.Ui(9f),
            Padding = new Padding(6, 2, 0, 2),
            RenderMode = ToolStripRenderMode.Professional
        };
        XShellTheme.StyleMenu(menu);

        var file = new ToolStripMenuItem("文件(&F)");
        file.DropDownItems.Add(Entry("新建会话(&N)...", (_, _) => NewSession()));
        file.DropDownItems.Add(Entry("打开会话管理器(&O)", (_, _) =>
        {
            _managerItem.Checked = true;
            _tree.Focus();
        }));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Entry("导入会话(&I)...", (_, _) => ImportSessions()));
        file.DropDownItems.Add(Entry("导出会话(&E)...", (_, _) => ExportSessions()));
        file.DropDownItems.Add(new ToolStripSeparator());
        var logMenu = new ToolStripMenuItem("日志(&L)");
        var startLog = Entry("启动日志记录(&S)", (_, _) => StartCurrentLog());
        var stopLog = Entry("停止日志记录(&T)", (_, _) => StopCurrentLog());
        var openLog = Entry("打开日志文件夹(&O)", (_, _) => OpenCurrentLogFolder());
        logMenu.DropDownItems.Add(startLog);
        logMenu.DropDownItems.Add(stopLog);
        logMenu.DropDownItems.Add(new ToolStripSeparator());
        logMenu.DropDownItems.Add(openLog);
        logMenu.DropDownOpening += (_, _) =>
        {
            var terminal = CurrentPage()?.Terminal;
            startLog.Enabled = terminal != null && !terminal.IsLogging;
            stopLog.Enabled = terminal != null && terminal.IsLogging;
            openLog.Enabled = terminal != null;
        };
        file.DropDownItems.Add(logMenu);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Entry("设置(&P)...", (_, _) => ShowSettings()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Entry("退出(&X)", (_, _) => Close()));

        var edit = new ToolStripMenuItem("编辑(&E)");
        edit.DropDownOpening += (_, _) => UpdateEditMenu(edit);
        edit.DropDownItems.Add(Entry("复制(&C)", (_, _) => WithTerminal(t => t.RequestCopy())));
        edit.DropDownItems.Add(Entry("粘贴(&V)", (_, _) => WithTerminal(t => t.Paste())));
        edit.DropDownItems.Add(Entry("全选(&A)", (_, _) => WithTerminal(t => t.SelectAll())));
        edit.DropDownItems.Add(Entry("查找(&F)...", (_, _) => WithTerminal(t => t.Search())));
        edit.DropDownItems.Add(Entry("清屏(&L)", (_, _) => WithTerminal(t => t.ClearScreen())));
        edit.DropDownItems.Add(new ToolStripSeparator());
        var zoomIn = Entry("放大字体(&G)", (_, _) => ZoomFont(1));
        zoomIn.ShortcutKeyDisplayString = "Ctrl+Shift++";
        var zoomOut = Entry("缩小字体(&S)", (_, _) => ZoomFont(-1));
        zoomOut.ShortcutKeyDisplayString = "Ctrl+-";
        var zoomReset = Entry("重置字号(&R)", (_, _) => ResetTerminalFont());
        zoomReset.ShortcutKeyDisplayString = "Ctrl+0";
        edit.DropDownItems.Add(zoomIn);
        edit.DropDownItems.Add(zoomOut);
        edit.DropDownItems.Add(zoomReset);

        var view = new ToolStripMenuItem("查看(&V)");
        view.DropDownItems.Add(_managerItem);
        view.DropDownItems.Add(_toolbarItem);
        view.DropDownItems.Add(_launchItem);
        view.DropDownItems.Add(_statusItem);
        view.DropDownItems.Add(BuildThemeMenu());
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(Entry("起始页(&H)", (_, _) => ShowWelcome()));
        view.DropDownItems.Add(Entry("下一个标签(&N)", (_, _) => NextTab(1)));
        view.DropDownItems.Add(Entry("上一个标签(&P)", (_, _) => NextTab(-1)));
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(Entry("全屏(&F)  F11", (_, _) => ToggleFullScreen()));

        var tools = new ToolStripMenuItem("工具(&T)");
        tools.DropDownItems.Add(Entry("SFTP 文件传输(&F)", (_, _) => OpenSftp()));
        tools.DropDownItems.Add(Entry("撰写栏(&C)", (_, _) => WithPage(p => p.ToggleCompose())));
        tools.DropDownItems.Add(Entry("隧道状态(&T)...", (_, _) => ShowTunnels()));
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add(Entry("会话属性(&P)...", (_, _) => EditSelectedSession()));
        tools.DropDownItems.Add(Entry("设置(&S)...", (_, _) => ShowSettings()));
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add(Entry("XFtp 复刻版(&X)...", (_, _) => OpenXFtp()));

        var window = new ToolStripMenuItem("窗口(&W)");
        window.DropDownItems.Add(Entry("关闭标签页(&C)", (_, _) => CloseCurrentTab()));
        window.DropDownItems.Add(Entry("关闭其他标签页(&O)", (_, _) => CloseOtherTabs()));
        window.DropDownItems.Add(Entry("关闭所有标签页(&A)", (_, _) => CloseAllTabs()));

        var help = new ToolStripMenuItem("帮助(&H)");
        help.DropDownItems.Add(Entry("快捷键(&K)...", (_, _) => ShowShortcuts()));
        help.DropDownItems.Add(Entry("关于 XShell 复刻版(&A)...", (_, _) => ShowAbout()));
        help.DropDownItems.Add(Entry("XFTP 客户端(&F)...", (_, _) => OpenXFtp()));

        menu.Items.AddRange(new ToolStripItem[] { file, edit, view, tools, window, help });
        return menu;
    }

    private void UpdateEditMenu(ToolStripMenuItem edit)
    {
        bool hasTerminal = CurrentPage != null;
        foreach (ToolStripItem item in edit.DropDownItems)
        {
            if (item is ToolStripMenuItem entry)
            {
                entry.Enabled = hasTerminal || entry.Text!.StartsWith("放大字体")
                    || entry.Text!.StartsWith("缩小字体") || entry.Text!.StartsWith("重置字号");
            }
        }
    }

    private ToolStripMenuItem BuildThemeMenu()
    {
        var menu = new ToolStripMenuItem("界面主题(&T)");
        var modes = new (string Mode, string Text)[]
        {
            ("Dark", "深色"),
            ("Light", "浅色"),
            ("System", "跟随系统")
        };
        foreach (var (mode, text) in modes)
        {
            var item = new ToolStripMenuItem(text)
            {
                Checked = XShellTheme.NormalizeMode(_settings.UiTheme) == mode
            };
            item.Click += (_, _) => SetUiTheme(mode);
            _themeItems[mode] = item;
            menu.DropDownItems.Add(item);
        }
        menu.DropDownOpening += (_, _) => UpdateThemeChecks();
        return menu;
    }

    private void UpdateThemeChecks()
    {
        string current = XShellTheme.NormalizeMode(_settings.UiTheme);
        foreach (var pair in _themeItems)
        {
            pair.Value.Checked = string.Equals(pair.Key, current, StringComparison.Ordinal);
        }
    }

    private void SetUiTheme(string mode)
    {
        _settings.UiTheme = XShellTheme.NormalizeMode(mode);
        var old = XShellTheme.Apply(_settings.UiTheme);
        XShellTheme.Refresh(this, old);
        XShellWindowTheme.Apply(this);
        UpdateTreeImages();
        UpdateThemeChecks();
        RefreshWelcomeTheme();
        RefreshSftpThemes(old);
        RefreshTerminalUiThemes();
        _store.Save();
    }

    private void CycleTheme()
    {
        string next = XShellTheme.RequestedMode switch
        {
            "Dark" => "Light",
            "Light" => "System",
            _ => "Dark"
        };
        SetUiTheme(next);
    }

    private void ApplyUiThemeFromSettings()
    {
        var old = XShellTheme.Apply(_settings.UiTheme);
        XShellTheme.Refresh(this, old);
        XShellWindowTheme.Apply(this);
        UpdateTreeImages();
        UpdateThemeChecks();
        RefreshWelcomeTheme();
        RefreshSftpThemes(old);
        RefreshTerminalUiThemes();
    }

    private void RefreshWelcomeTheme()
    {
        if (_welcomeTab?.Controls.Count > 0
            && _welcomeTab.Controls[0] is XShellWelcomePage welcome)
        {
            welcome.RefreshTheme();
        }
    }

    private void UpdateTreeImages()
    {
        if (_treeImages.Images.Count >= 2)
        {
            _treeImages.Images[0] = XShellIcons.Server(XShellTheme.Accent, 16);
            _treeImages.Images[1] = XShellIcons.Folder(XShellTheme.Muted, 16);
            _tree.Invalidate();
        }
    }

    private void CheckSystemTheme()
    {
        if (XShellTheme.RequestedMode != "System")
        {
            return;
        }
        var old = XShellTheme.Apply("System");
        if (!ReferenceEquals(old, XShellTheme.Current))
        {
            XShellTheme.Refresh(this, old);
            XShellWindowTheme.Apply(this);
            UpdateTreeImages();
            RefreshWelcomeTheme();
            RefreshSftpThemes(old);
            RefreshTerminalUiThemes();
        }
    }

    // ================= 功能区 =================

    private Panel BuildRibbon()
    {
        var ribbon = new XShellRibbonPanel
        {
            Dock = DockStyle.Top,
            Height = 86,
            BackColor = XShellTheme.Ribbon
        };

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = XShellTheme.Ribbon,
            Padding = new Padding(8, 3, 8, 2)
        };
        _managerRibbonButton = RibbonButton("会话管理器", "\uE8FD", XShellTone.Accent,
            () => _managerItem.Checked = !_managerItem.Checked);
        flow.Controls.Add(new XShellRibbonGroup("会话",
            RibbonButton("连接", "\uE768", XShellTone.Green, () => ConnectSelectedSession()),
            RibbonButton("新建", "\uE710", XShellTone.Accent, () => NewSession()),
            _managerRibbonButton,
            RibbonButton("属性", "\uE713", XShellTone.Muted, () => EditSelectedSession()),
            RibbonButton("复制会话", "\uE8C8", XShellTone.Muted, () => DuplicateSelectedSession())));
        flow.Controls.Add(new XShellRibbonGroup("连接",
            RibbonButton("断开", "\uE71A", XShellTone.Red, () => DisconnectCurrent()),
            RibbonButton("重连", "\uE72C", XShellTone.Orange, () => ReconnectCurrent()),
            RibbonButton("SFTP", "\uE898", XShellTone.Accent, () => OpenSftp()),
            RibbonButton("撰写栏", "\uE70F", XShellTone.Muted, () => WithPage(p => p.ToggleCompose())),
            RibbonButton("查找", "\uE721", XShellTone.Muted, () => WithTerminal(t => t.Search()))));
        flow.Controls.Add(new XShellRibbonGroup("工具",
            RibbonButton("隧道", "\uE71B", XShellTone.Accent, () => ShowTunnels()),
            RibbonButton("主题", "\uE793", XShellTone.Orange, () => CycleTheme()),
            RibbonButton("设置", "\uE713", XShellTone.Muted, () => ShowSettings()),
            RibbonButton("XFtp", "\uE8B7", XShellTone.Green, () => OpenXFtp()),
            RibbonButton("关于", "\uE946", XShellTone.Muted, () => ShowAbout())));
        ribbon.Controls.Add(flow);
        return ribbon;
    }

    private XShellRibbonButton RibbonButton(string text, string glyph, XShellTone tone,
        Action click)
    {
        var button = new XShellRibbonButton
        {
            Caption = text,
            Glyph = glyph,
            Tone = tone,
            Width = 58,
            Height = 56
        };
        button.Click += (_, _) => click();
        var tip = new XShellToolTip();
        tip.SetToolTip(button, text);
        return button;
    }

    // ================= 快速会话栏 =================

    private Panel BuildQuickLaunchBar()
    {
        var bar = new XShellQuickPanel
        {
            Dock = DockStyle.Top,
            // 56px：按钮行 + 水平滚动条的空间；窄窗口 / 高 DPI 下按钮溢出时可横向滚动而不是被裁剪。
            Height = 56,
            BackColor = XShellTheme.Background,
            Padding = new Padding(14, 8, 14, 8)
        };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            BackColor = XShellTheme.Panel
        };

        var quickTitle = new Label
        {
            Text = "快速会话",
            AutoSize = true,
            ForeColor = XShellTheme.Accent,
            Font = XShellTheme.Ui(9f, FontStyle.Bold),
            BackColor = Color.Transparent,
            Margin = new Padding(2, 7, 12, 0)
        };
        flow.Controls.Add(quickTitle);
        var tip = new XShellToolTip();
        foreach (var (label, host) in QuickLaunchSessions)
        {
            var button = new Button
            {
                Text = label,
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                BackColor = XShellTheme.InputBack,
                ForeColor = XShellTheme.Text,
                Font = XShellTheme.Ui(9f, FontStyle.Bold),
                Margin = new Padding(0, 3, 8, 0),
                Padding = new Padding(10, 4, 10, 4),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = XShellTheme.Accent;
            button.FlatAppearance.MouseOverBackColor = XShellTheme.PanelAlt;
            tip.SetToolTip(button, "连接会话：" + host);
            button.Click += (_, _) => LaunchQuickSession(host);
            flow.Controls.Add(button);
        }
        bar.Controls.Add(flow);
        return bar;
    }

    /// <summary>
    /// 快速会话按钮：等价于在会话管理器里双击对应主机（如 master → 192.168.33.90-master）的会话。
    /// 会话存在时直接使用其配置；被删除时按预置默认值临时重建，保证按钮始终可用。
    /// </summary>
    private void LaunchQuickSession(string host)
    {
        var profile = _store.Sessions.FirstOrDefault(s =>
            string.Equals(s.Host, host, StringComparison.OrdinalIgnoreCase));
        if (profile == null)
        {
            string name = QuickLaunchSessions
                .Where(p => string.Equals(p.Host, host, StringComparison.OrdinalIgnoreCase))
                .Select(p => host + "-" + p.Label)
                .FirstOrDefault() ?? host;
            profile = new XSessionProfile
            {
                Name = name,
                Group = "预置快速启动",
                Protocol = XProtocol.Ssh,
                Host = host,
                Port = 22,
                UserName = "root",
                AuthMethod = XAuthMethod.Password,
                Password = "1",
                SftpEnabled = true,
                RemoteStartFolder = "/root"
            }.Normalize();
        }
        _ = ConnectProfileAsync(profile);
    }

    // ================= 会话管理器 =================

    private void BuildSessionPanel()
    {
        var left = _split.Panel1;
        left.BackColor = XShellTheme.Panel;

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 42,
            BackColor = XShellTheme.PanelAlt,
            Padding = new Padding(12, 0, 6, 0)
        };
        header.Paint += (_, e) =>
        {
            using var brush = new LinearGradientBrush(header.ClientRectangle,
                XShellTheme.PanelAlt, XShellTheme.Panel, LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(brush, header.ClientRectangle);
            using var accent = new SolidBrush(XShellTheme.Accent);
            e.Graphics.FillRectangle(accent, 0, 10, 3, header.Height - 20);
            using var pen = new Pen(XShellTheme.BorderLight);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };
        var title = new Label
        {
            Text = "会话管理器",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = XShellTheme.Ui(9.4f, FontStyle.Bold),
            ForeColor = XShellTheme.Text,
            BackColor = Color.Transparent,
            Padding = new Padding(4, 0, 0, 0)
        };
        var refresh = IconButton("\uE72C", "刷新会话列表", XShellTone.Accent, () => RefreshTree());
        var add = IconButton("\uE710", "新建会话", XShellTone.Accent, () => NewSession());
        var addGroup = IconButton("\uE8F4", "新建分组", XShellTone.Orange, () => NewGroup());
        var hide = IconButton("\uE76B", "向左自动隐藏（点击左边缘的会话管理器标签可再展开）",
            XShellTone.Muted, () => _managerItem.Checked = false);
        refresh.Dock = DockStyle.Right;
        add.Dock = DockStyle.Right;
        addGroup.Dock = DockStyle.Right;
        hide.Dock = DockStyle.Right;
        header.Controls.Add(title);
        header.Controls.Add(refresh);
        header.Controls.Add(add);
        header.Controls.Add(addGroup);
        header.Controls.Add(hide);

        var searchHost = new Panel
        {
            Dock = DockStyle.Top,
            Height = 40,
            BackColor = XShellTheme.Panel,
            Padding = new Padding(10, 7, 10, 7)
        };
        searchHost.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(9, 6, Math.Max(1, searchHost.Width - 19),
                Math.Max(1, searchHost.Height - 13));
            using var path = XShellTheme.RoundedRect(rect, 7);
            using var brush = new SolidBrush(XShellTheme.InputBack);
            e.Graphics.FillPath(brush, path);
            using var pen = new Pen(XShellTheme.InputBorder);
            e.Graphics.DrawPath(pen, path);
        };
        _searchBox.BorderStyle = BorderStyle.None;
        _searchBox.BackColor = XShellTheme.InputBack;
        _searchBox.ForeColor = XShellTheme.Text;
        searchHost.Controls.Add(_searchBox);

        left.Controls.Add(_tree);
        left.Controls.Add(searchHost);
        left.Controls.Add(header);
    }

    private XShellIconButton IconButton(string glyph, string tooltip, XShellTone tone,
        Action click)
    {
        var button = new XShellIconButton
        {
            Glyph = glyph,
            Tone = tone,
            Width = 30,
            Height = 28,
            Margin = new Padding(2, 0, 0, 0)
        };
        var tip = new XShellToolTip();
        tip.SetToolTip(button, tooltip);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>
    /// 会话管理器自动隐藏后停靠在窗口左边缘的竖排标签，点击可重新展开会话管理器。
    /// </summary>
    private Panel BuildHideStrip()
    {
        var strip = new XShellHideStrip
        {
            Dock = DockStyle.Left,
            Width = 26,
            Visible = false
        };
        var tip = new XShellToolTip();
        tip.SetToolTip(strip, "展开会话管理器");
        strip.Click += (_, _) => _managerItem.Checked = true;
        return strip;
    }

    /// <summary>
    /// 应用会话管理器显隐状态：显示时占用左侧面板；隐藏时整体向左收起，
    /// 左边缘保留一个竖排标签，点击即可重新展开。
    /// </summary>
    private void ApplySessionManagerVisibility()
    {
        bool show = _managerItem.Checked;
        _split.Panel1Collapsed = !show;
        if (show && !_split.Panel1Collapsed)
        {
            // 从隐藏状态展开时按设计值设置分隔位置（默认隐藏启动时 OnShown 不会设置）。
            try
            {
                _split.SplitterDistance = Math.Max(200,
                    (int)Math.Round(258 * DeviceDpi / 96.0));
            }
            catch
            {
                // 窗口过窄时保持当前分隔位置。
            }
        }
        _hideStrip.Visible = !show && !_fullScreen;
        if (_settings.ShowSessionManager != show)
        {
            _settings.ShowSessionManager = show;
            _store.Save();
        }
    }

    private ContextMenuStrip BuildTreeMenu()
    {
        var menu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.Professional };
        XShellTheme.StyleMenu(menu);
        var connect = Entry("连接(&O)");
        connect.Font = new Font(menu.Font, FontStyle.Bold);
        connect.Click += (_, _) => ConnectNode(_tree.SelectedNode);
        var newSession = Entry("新建会话(&N)...");
        newSession.Click += (_, _) =>
        {
            string? group = _tree.SelectedNode?.Tag as string;
            NewSession(group);
        };
        var newGroup = Entry("新建分组(&G)...");
        newGroup.Click += (_, _) => NewGroup();
        var property = Entry("会话属性(&P)...");
        property.Click += (_, _) => EditSelectedSession();
        var duplicate = Entry("复制会话(&C)");
        duplicate.Click += (_, _) => DuplicateSelectedSession();
        var delete = Entry("删除会话(&D)");
        delete.Click += (_, _) => DeleteSelectedSession();
        var renameGroup = Entry("重命名分组(&M)...");
        renameGroup.Click += (_, _) => RenameSelectedGroup();
        var deleteGroup = Entry("删除分组(&T)...");
        deleteGroup.Click += (_, _) => DeleteSelectedGroup();
        var import = Entry("导入会话(&I)...");
        import.Click += (_, _) => ImportSessions();
        var export = Entry("导出会话(&E)...");
        export.Click += (_, _) => ExportSessions();
        var refresh = Entry("刷新(&R)");
        refresh.Click += (_, _) => RefreshTree();
        menu.Items.AddRange(new ToolStripItem[]
        {
            connect, new ToolStripSeparator(), newSession, newGroup,
            new ToolStripSeparator(), property, duplicate, delete,
            new ToolStripSeparator(), renameGroup, deleteGroup,
            new ToolStripSeparator(), import, export, new ToolStripSeparator(), refresh
        });
        menu.Opening += (_, _) =>
        {
            bool hasProfile = SelectedProfile() != null;
            bool isGroup = _tree.SelectedNode?.Tag is string groupName
                && !string.Equals(groupName, "默认", StringComparison.OrdinalIgnoreCase);
            connect.Enabled = hasProfile;
            property.Enabled = hasProfile;
            duplicate.Enabled = hasProfile;
            delete.Enabled = hasProfile;
            renameGroup.Enabled = isGroup;
            deleteGroup.Enabled = isGroup;
        };
        return menu;
    }

    private XSessionProfile? SelectedProfile()
    {
        return _tree.SelectedNode?.Tag as XSessionProfile;
    }

    private void RefreshTree()
    {
        string filter = _searchBox.Text.Trim();
        _tree.BeginUpdate();
        try
        {
            var selectedId = SelectedProfile()?.Id;
            var selectedGroup = _tree.SelectedNode?.Tag as string;
            _tree.Nodes.Clear();
            foreach (string groupName in _store.AllGroups())
            {
                var sessions = _store.Sessions
                    .Where(s => string.Equals(
                        string.IsNullOrWhiteSpace(s.Group) ? "默认" : s.Group,
                        groupName, StringComparison.OrdinalIgnoreCase))
                    .Where(s => MatchesFilter(s, filter))
                    .OrderBy(s => s.Name, StringComparer.CurrentCulture)
                    .ToList();
                if (filter.Length > 0 && sessions.Count == 0)
                {
                    continue;
                }
                var groupNode = new TreeNode(groupName)
                {
                    Tag = groupName,
                    ImageIndex = 1,
                    SelectedImageIndex = 1,
                    ForeColor = XShellTheme.Muted,
                    NodeFont = XShellTheme.Ui(8.8f, FontStyle.Bold)
                };
                foreach (var session in sessions)
                {
                    groupNode.Nodes.Add(new TreeNode(session.Name)
                    {
                        Tag = session,
                        ImageIndex = 0,
                        SelectedImageIndex = 0,
                        ToolTipText = session.ProtocolText + "  " + session.DisplayAddress
                    });
                }
                _tree.Nodes.Add(groupNode);
                groupNode.Expand();
            }
            if (selectedId != null)
            {
                SelectSessionById(selectedId);
            }
            else if (!string.IsNullOrWhiteSpace(selectedGroup))
            {
                SelectGroupByName(selectedGroup);
            }
        }
        finally
        {
            _tree.EndUpdate();
        }
    }

    private static bool MatchesFilter(XSessionProfile profile, string filter)
    {
        if (filter.Length == 0)
        {
            return true;
        }
        return profile.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || profile.Group.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || profile.DisplayAddress.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void SelectSessionById(string id)
    {
        foreach (TreeNode group in _tree.Nodes)
        {
            foreach (TreeNode node in group.Nodes)
            {
                if (node.Tag is XSessionProfile profile
                    && string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    _tree.SelectedNode = node;
                    node.EnsureVisible();
                    return;
                }
            }
        }
    }

    private void SelectSession(XSessionProfile profile)
    {
        RefreshTree();
        SelectSessionById(profile.Id);
    }

    private void SelectGroupByName(string name)
    {
        foreach (TreeNode node in _tree.Nodes)
        {
            if (string.Equals(node.Text, name, StringComparison.OrdinalIgnoreCase))
            {
                _tree.SelectedNode = node;
                node.EnsureVisible();
                return;
            }
        }
    }

    private void UpdateSelectionHint()
    {
        if (SelectedProfile() is { } profile)
        {
            _hostLabel.Text = profile.ProtocolText + "  " + profile.DisplayAddress;
        }
        else if (CurrentPage() is { } page)
        {
            _hostLabel.Text = page.Profile.DisplayAddress;
        }
        else
        {
            _hostLabel.Text = "";
        }
    }

    private void ConnectNode(TreeNode? node)
    {
        if (node?.Tag is XSessionProfile profile)
        {
            _ = ConnectProfileAsync(profile);
        }
    }

    private void ConnectSelectedSession()
    {
        if (SelectedProfile() is { } profile)
        {
            _ = ConnectProfileAsync(profile);
        }
        else
        {
            XShellMessageBox.Show(this, "请先在会话管理器中选择一个会话。",
                "XShell 复刻版", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // ================= 会话操作 =================

    private string NextSessionName()
    {
        int index = 1;
        while (_store.Sessions.Any(s => s.Name == "新建会话 " + index))
        {
            index++;
        }
        return "新建会话 " + index;
    }

    private void NewSession(string? group = null)
    {
        var profile = new XSessionProfile
        {
            Name = NextSessionName(),
            Group = string.IsNullOrWhiteSpace(group) ? "默认" : group,
            Protocol = XProtocol.Ssh,
            Port = 22
        };
        using var dialog = new XShellConnectionDialog(profile, _store);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            RefreshTree();
            SelectSession(profile);
        }
    }

    private void NewGroup()
    {
        string? name = XShellPrompt.Show(this, "新建分组", "分组名称", "");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }
        _store.AddGroup(name);
        RefreshTree();
        SelectGroupByName(name);
    }

    private void RenameSelectedGroup()
    {
        if (_tree.SelectedNode?.Tag is not string oldName)
        {
            XShellMessageBox.Show(this, "请先在会话管理器中选择一个分组。", "XShell 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (string.Equals(oldName, "默认", StringComparison.OrdinalIgnoreCase))
        {
            XShellMessageBox.Show(this, "默认分组不能重命名。", "XShell 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string? name = XShellPrompt.Show(this, "重命名分组", "新的分组名称", oldName);
        if (string.IsNullOrWhiteSpace(name)
            || string.Equals(name, oldName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (_store.RenameGroup(oldName, name))
        {
            RefreshTree();
            SelectGroupByName(name);
        }
    }

    private void DeleteSelectedGroup()
    {
        if (_tree.SelectedNode?.Tag is not string groupName)
        {
            XShellMessageBox.Show(this, "请先在会话管理器中选择一个分组。", "XShell 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (string.Equals(groupName, "默认", StringComparison.OrdinalIgnoreCase))
        {
            XShellMessageBox.Show(this, "默认分组不能删除。", "XShell 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        int count = _store.Sessions.Count(s =>
            string.Equals(s.Group, groupName, StringComparison.OrdinalIgnoreCase));
        if (XShellMessageBox.Show(this,
                "确定要删除分组“" + groupName + "”吗？"
                + (count > 0 ? "\r\n\r\n其中的 " + count + " 个会话将移动到“默认”分组。" : ""),
                "XShell 复刻版", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
            != DialogResult.Yes)
        {
            return;
        }
        _store.DeleteGroup(groupName);
        RefreshTree();
    }

    private void EditSelectedSession()
    {
        if (SelectedProfile() is not { } profile)
        {
            XShellMessageBox.Show(this, "请先在会话管理器中选择一个会话。", "XShell 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new XShellConnectionDialog(profile, _store);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SelectSession(profile);
            UpdateOpenTabsTitle(profile);
        }
    }

    private void UpdateOpenTabsTitle(XSessionProfile profile)
    {
        foreach (var pair in _pages)
        {
            if (ReferenceEquals(pair.Value.Profile, profile)
                || string.Equals(pair.Value.Profile.Id, profile.Id, StringComparison.OrdinalIgnoreCase))
            {
                pair.Key.Text = profile.Name;
                pair.Key.ToolTipText = profile.Name;
            }
        }
        _tabs.Invalidate();
        UpdateStatus();
    }

    private void DuplicateSelectedSession()
    {
        if (SelectedProfile() is not { } profile)
        {
            XShellMessageBox.Show(this, "请先在会话管理器中选择一个会话。", "XShell 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var copy = profile.Clone();
        _store.AddOrUpdate(copy);
        SelectSession(copy);
    }

    private void DeleteSelectedSession()
    {
        if (SelectedProfile() is not { } profile)
        {
            XShellMessageBox.Show(this, "请先在会话管理器中选择一个会话。", "XShell 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (XShellMessageBox.Show(this, "确定要删除会话“" + profile.Name + "”吗？",
                "XShell 复刻版", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
            != DialogResult.Yes)
        {
            return;
        }
        _store.Remove(profile);
        RefreshTree();
    }

    private void ImportSessions()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "导入 XShell 会话",
            Filter = "XShell 会话 (*.json)|*.json|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            int count = _store.ImportSessions(dialog.FileName);
            RefreshTree();
            XShellMessageBox.Show(this, "已导入 " + count + " 个会话。", "XShell 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            XShellMessageBox.Show(this, "导入失败：\r\n" + ex.Message, "XShell 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ExportSessions()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "导出 XShell 会话",
            Filter = "XShell 会话 (*.json)|*.json",
            FileName = "xshell-sessions-" + DateTime.Now.ToString("yyyyMMdd") + ".json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            string temp = _store.ExportSessions();
            File.Copy(temp, dialog.FileName, overwrite: true);
            try { File.Delete(temp); } catch { }
            XShellMessageBox.Show(this, "会话已导出到：\r\n" + dialog.FileName, "XShell 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            XShellMessageBox.Show(this, "导出失败：\r\n" + ex.Message, "XShell 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ================= 终端标签 =================

    private XShellTerminalPage? CurrentPage()
    {
        return _tabs.SelectedTab is { } tab && _pages.TryGetValue(tab, out var page) ? page : null;
    }

    private async Task ConnectProfileAsync(XSessionProfile profile)
    {
        try
        {
            var content = new XShellTerminalPage(profile, _store, _settings);
            var tab = _tabs.AddPage(content, profile.Name);
            tab.ToolTipText = profile.ProtocolText + "  " + profile.DisplayAddress;
            _pages[tab] = content;
            _everConnected[tab] = false;
            _reconnecting[tab] = false;
            _manualDisconnect.Remove(tab);
            content.StateChanged += page => OnPageStateChanged(tab, page);
            content.TitleChanged += (_, _) => UpdateStatus();
            content.FullScreenRequested += _ => ToggleFullScreen();
            _tabs.SelectedTab = tab;
            _store.Touch(profile);
            UpdateStatus();
            await content.ConnectAsync();
        }
        catch (Exception ex)
        {
            XShellMessageBox.Show(this, "打开会话失败：\r\n" + ex.Message, "XShell 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnPageStateChanged(TabPage tab, XShellTerminalPage page)
    {
        if (!_pages.ContainsKey(tab))
        {
            return;
        }
        _tabs.SetRecording(tab, page.Terminal.IsLogging);
        bool connected = page.Terminal.IsConnected;
        if (connected)
        {
            _everConnected[tab] = true;
            _manualDisconnect.Remove(tab);
        }
        else if (_settings.AutoReconnect
            && _everConnected[tab]
            && !_manualDisconnect.Contains(tab)
            && !_closing
            && page.Terminal.StatusText == "已断开")
        {
            ScheduleReconnect(tab, page);
        }
        if (_tabs.SelectedTab == tab)
        {
            UpdateStatus();
        }
    }

    private async void ScheduleReconnect(TabPage tab, XShellTerminalPage page)
    {
        if (_reconnecting.TryGetValue(tab, out bool running) && running)
        {
            return;
        }
        _reconnecting[tab] = true;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(
                Math.Clamp(_settings.AutoReconnectSeconds, 1, 120)));
            if (_closing || !_pages.ContainsKey(tab) || page.Terminal.IsConnected
                || _manualDisconnect.Contains(tab))
            {
                return;
            }
            await page.ReconnectAsync();
        }
        catch
        {
            // 自动重连失败时等待下一次断开事件。
        }
        finally
        {
            if (_reconnecting.ContainsKey(tab))
            {
                _reconnecting[tab] = false;
            }
        }
    }

    private void CloseCurrentTab()
    {
        if (_tabs.SelectedTab is { } tab)
        {
            CloseTab(tab);
        }
    }

    private void CloseOtherTabs()
    {
        if (_tabs.SelectedTab is not { } current)
        {
            return;
        }
        foreach (var tab in _tabs.TabPages.Cast<TabPage>().ToList())
        {
            if (tab != current)
            {
                CloseTab(tab, confirm: false);
            }
        }
    }

    private void CloseAllTabs()
    {
        foreach (var tab in _tabs.TabPages.Cast<TabPage>().ToList())
        {
            CloseTab(tab, confirm: false);
        }
    }

    private void CloseTab(TabPage tab) => CloseTab(tab, confirm: true);

    private void CloseTab(TabPage tab, bool confirm)
    {
        if (tab == _welcomeTab)
        {
            _welcomeTab = null;
            _tabs.ClosePage(tab);
            return;
        }
        if (!_pages.TryGetValue(tab, out var page))
        {
            _tabs.ClosePage(tab);
            return;
        }
        if (confirm && page.Terminal.IsConnected && _settings.ConfirmCloseSession
            && XShellMessageBox.Show(this,
                "会话“" + page.Profile.Name + "”仍在连接中，确定要关闭该标签页吗？",
                "XShell 复刻版", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
            != DialogResult.Yes)
        {
            return;
        }
        _pages.Remove(tab);
        _everConnected.Remove(tab);
        _reconnecting.Remove(tab);
        _manualDisconnect.Remove(tab);
        CloseSftpForTab(tab);
        try { page.Disconnect(); } catch { }
        _tabs.ClosePage(tab);
        UpdateStatus();
    }

    private void ShowTabMenu(TabPage tab, Point location)
    {
        if (!_pages.TryGetValue(tab, out var page))
        {
            return;
        }
        _tabs.SelectedTab = tab;
        var menu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.Professional };
        XShellTheme.StyleMenu(menu);
        var reconnect = Entry("重新连接(&R)");
        reconnect.Click += (_, _) => _ = page.ReconnectAsync();
        var disconnect = Entry("断开连接(&D)");
        disconnect.Click += (_, _) =>
        {
            _manualDisconnect.Add(tab);
            page.Disconnect();
        };
        var copyTab = Entry("复制标签页(&C)");
        copyTab.Click += (_, _) => _ = ConnectProfileAsync(page.Profile);
        var sftp = Entry("SFTP 文件传输(&F)");
        sftp.Click += (_, _) => OpenSftp();
        var compose = Entry("撰写栏(&M)");
        compose.Click += (_, _) => page.ToggleCompose();
        var find = Entry("查找(&F)");
        find.Click += (_, _) => page.Terminal.Search();
        var close = Entry("关闭(&X)");
        close.Click += (_, _) => CloseTab(tab);
        var closeOthers = Entry("关闭其他标签页(&O)");
        closeOthers.Click += (_, _) => CloseOtherTabs();
        var closeAll = Entry("关闭所有标签页(&A)");
        closeAll.Click += (_, _) => CloseAllTabs();
        menu.Items.AddRange(new ToolStripItem[]
        {
            reconnect, disconnect, new ToolStripSeparator(), copyTab, sftp, compose, find,
            new ToolStripSeparator(), close, closeOthers, closeAll
        });
        menu.Opening += (_, _) =>
        {
            reconnect.Enabled = page.Profile.Protocol != XProtocol.Serial;
            disconnect.Enabled = page.Terminal.IsConnected;
            sftp.Enabled = page.Profile.Protocol == XProtocol.Ssh && page.Profile.SftpEnabled;
        };
        menu.Show(_tabs, location);
    }

    private void DisconnectCurrent()
    {
        if (CurrentPage() is not { } page)
        {
            return;
        }
        if (_tabs.SelectedTab is { } tab)
        {
            _manualDisconnect.Add(tab);
        }
        page.Disconnect();
    }

    private void ReconnectCurrent()
    {
        if (CurrentPage() is not { } page)
        {
            return;
        }
        if (_tabs.SelectedTab is { } tab)
        {
            _manualDisconnect.Remove(tab);
        }
        _ = page.ReconnectAsync();
    }

    private void WithTerminal(Action<XShellTerminalControl> action)
    {
        if (CurrentPage() is { } page)
        {
            action(page.Terminal);
        }
    }

    private void StartCurrentLog()
    {
        if (CurrentPage() is not { } page)
        {
            XShellMessageBox.Show(this, "请先打开一个终端会话。", "XShell 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            page.Terminal.StartLogging();
        }
        catch (Exception ex)
        {
            XShellMessageBox.Show(this, "启动日志失败：\r\n\r\n" + ex.Message, "XShell 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void StopCurrentLog()
    {
        CurrentPage()?.Terminal.StopLogging();
    }

    private void OpenCurrentLogFolder()
    {
        try
        {
            CurrentPage()?.Terminal.OpenLogFolder();
        }
        catch (Exception ex)
        {
            XShellMessageBox.Show(this, "打开日志文件夹失败：\r\n\r\n" + ex.Message, "XShell 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void WithPage(Action<XShellTerminalPage> action)
    {
        if (CurrentPage() is { } page)
        {
            action(page);
        }
        else
        {
            XShellMessageBox.Show(this, "请先打开一个终端会话。", "XShell 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    /// <summary>弹出独立的 SFTP 文件传输窗口（Xftp 风格），同一标签复用同一个窗口。</summary>
    private void OpenSftp()
    {
        if (CurrentPage() is not { } page || _tabs.SelectedTab is not { } tab)
        {
            XShellMessageBox.Show(this, "请先打开一个 SSH 会话。", "XShell 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (page.Profile.Protocol != XProtocol.Ssh)
        {
            XShellMessageBox.Show(this, "SFTP 仅支持 SSH 会话。", "XShell 复刻版",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!page.Profile.SftpEnabled)
        {
            XShellMessageBox.Show(this,
                "当前会话未启用 SFTP，请在会话属性中勾选“启用 SFTP 文件传输面板”。",
                "XShell 复刻版", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_sftpForms.TryGetValue(tab, out var existing) && !existing.IsDisposed)
        {
            existing.Show();
            if (existing.WindowState == FormWindowState.Minimized)
            {
                existing.WindowState = FormWindowState.Normal;
            }
            existing.Activate();
            return;
        }
        try
        {
            var form = new XShellSftpForm(page.Profile, _settings,
                page.Terminal.CreateSftpClientAsync);
            form.FormClosed += (_, _) => _sftpForms.Remove(tab);
            _sftpForms[tab] = form;
            form.Show();
            form.Activate();
        }
        catch (Exception ex)
        {
            XShellMessageBox.Show(this, "打开 SFTP 窗口失败：\r\n\r\n" + ex.Message,
                "XShell 复刻版", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void CloseSftpForTab(TabPage tab)
    {
        if (_sftpForms.TryGetValue(tab, out var form))
        {
            _sftpForms.Remove(tab);
            try { form.Close(); } catch { }
        }
    }

    private void RefreshSftpThemes(XShellUiPalette oldPalette)
    {
        foreach (var form in _sftpForms.Values.ToList())
        {
            if (!form.IsDisposed)
            {
                try { form.ApplyTheme(oldPalette); } catch { }
            }
        }
    }

    /// <summary>同步所有终端的右键菜单 / 查找栏为当前界面主题配色。</summary>
    private void RefreshTerminalUiThemes()
    {
        foreach (var page in _pages.Values.ToList())
        {
            if (!page.IsDisposed)
            {
                try { page.Terminal.ApplyUiTheme(); } catch { }
            }
        }
    }

    /// <summary>
    /// 打开独立的 XFtp 复刻版客户端。若当前标签是已连接的 SSH 会话，
    /// 自动带入主机、端口、账号、认证信息、跳板机与终端当前 shell 目录。
    /// </summary>
    private async void OpenXFtp()
    {
        if (_xftpOpening)
        {
            return;
        }
        _xftpOpening = true;
        MiniMonitor.XFtp.XFtpSite? autoSite = null;
        string downloads = LocalDownloadsFolder();
        var page = CurrentPage();
        if (page != null && page.Profile.Protocol == XProtocol.Ssh
            && !string.IsNullOrWhiteSpace(page.Profile.Host))
        {
            var profile = page.Profile;
            autoSite = new MiniMonitor.XFtp.XFtpSite
            {
                Name = profile.Name,
                Protocol = MiniMonitor.XFtp.XFtpProtocol.Sftp,
                Host = profile.Host,
                Port = profile.Port <= 0 ? 22 : profile.Port,
                User = profile.UserName,
                Password = profile.Password,
                PrivateKeyPath = profile.AuthMethod == XAuthMethod.PrivateKey
                    ? profile.PrivateKeyPath : "",
                PrivateKeyPassphrase = profile.PrivateKeyPassphrase,
                RemotePath = !string.IsNullOrWhiteSpace(page.Terminal.EffectiveDirectory)
                    ? page.Terminal.EffectiveDirectory
                    : profile.RemoteStartFolder,
                LocalPath = downloads,
                JumpHost = profile.UseJumpHost ? profile.JumpHost : "",
                JumpPort = profile.JumpPort <= 0 ? 22 : profile.JumpPort,
                JumpUser = profile.JumpUserName,
                JumpPassword = profile.JumpPassword,
                JumpPrivateKeyPath = profile.JumpPrivateKeyPath
            };

            // 优先使用 OSC 7 上报的目录；没有时通过 SSH 只读探测当前 shell 目录，
            // 最后回退到终端标题中的路径。
            string shellDir = page.Terminal.CurrentDirectory;
            if (string.IsNullOrWhiteSpace(shellDir))
            {
                try
                {
                    Cursor.Current = Cursors.WaitCursor;
                    string? probed = await page.Terminal.ProbeShellDirectoryAsync();
                    if (!string.IsNullOrWhiteSpace(probed))
                    {
                        shellDir = probed;
                    }
                }
                finally
                {
                    Cursor.Current = Cursors.Default;
                }
            }
            if (string.IsNullOrWhiteSpace(shellDir))
            {
                shellDir = page.Terminal.EffectiveDirectory;
            }
            autoSite.RemotePath = !string.IsNullOrWhiteSpace(shellDir)
                ? shellDir : profile.RemoteStartFolder;
        }
        try
        {
            if (_xftpForm == null || _xftpForm.IsDisposed)
            {
                _xftpForm = new MiniMonitor.XFtp.XFtpForm(autoSite, downloads);
                _xftpForm.FormClosed += (_, _) => _xftpForm = null;
                _xftpForm.Show();
            }
            else
            {
                if (autoSite != null)
                {
                    _xftpForm.AutoConnect(autoSite, downloads);
                }
                else
                {
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
            }
        }
        catch (Exception ex)
        {
            _xftpForm?.Dispose();
            _xftpForm = null;
            XShellMessageBox.Show(this, "无法打开 XFtp 复刻版：\r\n\r\n" + ex.Message,
                "XShell 复刻版", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _xftpOpening = false;
        }
    }

    /// <summary>本地用户目录下的 Downloads（兼容 Download，最后回退到文档目录）。</summary>
    private static string LocalDownloadsFolder()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string downloads = Path.Combine(profile, "Downloads");
        string download = Path.Combine(profile, "Download");
        return Directory.Exists(downloads) ? downloads
            : Directory.Exists(download) ? download
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private void ZoomFont(int delta)
    {
        if (CurrentPage() is not { } page)
        {
            return;
        }
        page.Profile.FontSize = Math.Clamp(page.Profile.FontSize + delta, 8, 32);
        page.Terminal.SetFontSize(page.Profile.FontSize);
    }

    private void ResetTerminalFont()
    {
        if (CurrentPage() is not { } page)
        {
            return;
        }
        page.Profile.FontSize = _settings.FontSize;
        page.Terminal.SetFontSize(page.Profile.FontSize);
    }

    private void NextTab(int direction)
    {
        if (_tabs.TabPages.Count < 2)
        {
            return;
        }
        int index = _tabs.SelectedIndex + direction;
        if (index < 0)
        {
            index = _tabs.TabPages.Count - 1;
        }
        else if (index >= _tabs.TabPages.Count)
        {
            index = 0;
        }
        _tabs.SelectedIndex = index;
    }

    private void ShowWelcome()
    {
        if (_welcomeTab != null && _tabs.TabPages.Contains(_welcomeTab))
        {
            _tabs.SelectedTab = _welcomeTab;
            return;
        }
        var page = new XShellWelcomePage(_store);
        page.NewSessionRequested += () => NewSession();
        page.SessionManagerRequested += () => _managerItem.Checked = true;
        page.SettingsRequested += ShowSettings;
        page.SessionRequested += profile => _ = ConnectProfileAsync(profile);
        _welcomeTab = _tabs.AddPage(page, "起始页");
    }

    // ================= 工具窗口 =================

    private void ShowSettings()
    {
        using var dialog = new XShellSettingsForm(_settings);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        _store.Save();
        ApplyUiThemeFromSettings();
        _quickBar.Visible = _settings.ShowQuickLaunch;
        _status.Visible = _settings.ShowStatusBar;
        _launchItem.Checked = _settings.ShowQuickLaunch;
        _statusItem.Checked = _settings.ShowStatusBar;
        foreach (var page in _pages.Values)
        {
            try { page.ApplySettings(); } catch { }
        }
        UpdateStatus();
    }

    private void ShowTunnels()
    {
        var page = CurrentPage();
        using var form = new Form
        {
            Text = "隧道 - " + (page?.Profile.Name ?? "未选择会话"),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable,
            MinimumSize = new Size(620, 320),
            ClientSize = new Size(700, 390),
            BackColor = XShellTheme.Background,
            ForeColor = XShellTheme.Text,
            Font = XShellTheme.Ui(9f),
            ShowInTaskbar = false,
            MaximizeBox = true,
            MinimizeBox = false
        };
        form.HandleCreated += (_, _) => XShellWindowTheme.Apply(form);
        var header = new XShellHeaderPanel
        {
            Dock = DockStyle.Top,
            Height = 60,
            Title = "SSH 端口隧道",
            Subtitle = "本地转发 / 远程转发 / 动态 SOCKS"
        };
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = XShellTheme.Panel,
            BorderStyle = BorderStyle.None
        };
        XShellTheme.StyleGrid(grid);
        grid.Columns.Add("name", "名称");
        grid.Columns.Add("type", "类型");
        grid.Columns.Add("bind", "监听地址");
        grid.Columns.Add("target", "目标地址");
        grid.Columns.Add("state", "状态");
        if (page != null && page.Profile.Protocol == XProtocol.Ssh)
        {
            foreach (var tunnel in page.Profile.Tunnels)
            {
                grid.Rows.Add(
                    tunnel.Name,
                    tunnel.Type switch
                    {
                        XTunnelType.Remote => "远程转发",
                        XTunnelType.Dynamic => "动态 SOCKS",
                        _ => "本地转发"
                    },
                    tunnel.BindHost + ":" + tunnel.BindPort,
                    tunnel.Type == XTunnelType.Dynamic
                        ? "-" : tunnel.TargetHost + ":" + tunnel.TargetPort,
                    tunnel.Enabled
                        ? (page.Terminal.IsConnected ? "已启动" : "等待连接")
                        : "已禁用");
            }
        }
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            BackColor = XShellTheme.PanelAlt,
            Padding = new Padding(12, 8, 12, 8)
        };
        footer.Controls.Add(new Label
        {
            Text = page == null
                ? "请先连接到 SSH 会话。"
                : "隧道规则在会话属性 → 隧道中维护，SSH 连接建立后自动启动。",
            Dock = DockStyle.Fill,
            ForeColor = XShellTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft
        });
        form.Controls.Add(grid);
        form.Controls.Add(footer);
        form.Controls.Add(header);
        form.Shown += (_, _) => XShellTheme.ApplyNativeTheme(form);
        form.ShowDialog(this);
    }

    private void ShowShortcuts()
    {
        XShellMessageBox.Show(this,
            "F11\t全屏 / 退出全屏\r\n"
            + "Ctrl+Tab\t下一个标签页\r\n"
            + "Ctrl+Shift+Tab\t上一个标签页\r\n"
            + "Ctrl+Shift++\t放大终端字号\r\n"
            + "Ctrl+-\t缩小终端字号\r\n"
            + "Ctrl+0\t重置终端字号\r\n"
            + "Ctrl+滚轮\t调整终端字号\r\n\r\n"
            + "复制：在终端中选中文字后点击鼠标右键 → 复制\r\n"
            + "粘贴：在终端中点击鼠标右键 → 粘贴\r\n"
            + "Ctrl+C 始终发送中断字符，不会复制文本。\r\n"
            + "SSH、Telnet、串口和本地 Shell 均遵循这一规则。",
            "XShell 复刻版 - 快捷键", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowAbout()
    {
        XShellMessageBox.Show(this,
            "XShell 复刻版\r\n\r\n"
            + "界面参考 XShell 8，会话能力参考 Tabby 1.0.238。\r\n"
            + "支持 SSH / Telnet / 串口 / 本地 Shell、SFTP 文件传输、跳板机、\r\n"
            + "本地 / 远程 / 动态端口转发、会话日志与多种终端配色。\r\n\r\n"
            + "终端由 xterm.js 渲染，SFTP 使用 SSH.NET，全部能力运行于本机。\r\n"
            + "已剔除任何信息收集与遥测代码；密码使用 Windows DPAPI 加密保存。\r\n\r\n"
            + "复制与粘贴仅通过终端右键菜单完成。",
            "关于 XShell 复刻版", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ================= 状态栏与窗口 =================

    private void UpdateStatus()
    {
        var page = CurrentPage();
        if (page == null)
        {
            _stateLabel.Text = "● 就绪";
            _stateLabel.ForeColor = XShellTheme.HeroMuted;
            if (SelectedProfile() is { } profile)
            {
                _hostLabel.Text = profile.ProtocolText + "  " + profile.DisplayAddress;
            }
            else
            {
                _hostLabel.Text = "";
            }
            _sizeLabel.Text = "";
            _encodingLabel.Text = "";
            _sftpLabel.Text = "";
            Text = "XShell 复刻版";
            return;
        }
        var terminal = page.Terminal;
        _stateLabel.Text = terminal.IsConnected ? "● 已连接" : "● " + terminal.StatusText;
        _stateLabel.ForeColor = terminal.IsConnected
            ? Color.FromArgb(118, 232, 177)
            : Color.FromArgb(255, 205, 140);
        _hostLabel.Text = page.Profile.DisplayAddress;
        _sizeLabel.Text = terminal.Columns + " × " + terminal.Rows;
        _encodingLabel.Text = page.Profile.EncodingName;
        _sftpLabel.Text = page.Profile.Protocol == XProtocol.Ssh && page.Profile.SftpEnabled
            ? "SFTP 可用" : "SFTP 不可用";
        Text = "XShell 复刻版 - " + page.Profile.Name;
    }

    private void ToggleFullScreen()
    {
        if (!_fullScreen)
        {
            _restoreBorder = FormBorderStyle;
            _restoreBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            Bounds = Screen.FromControl(this).Bounds;
            _menu.Visible = false;
            _ribbon.Visible = false;
            _quickBar.Visible = false;
            _status.Visible = false;
            _hideStrip.Visible = false;
            _split.Panel1Collapsed = true;
            _fullScreen = true;
        }
        else
        {
            _fullScreen = false;
            FormBorderStyle = _restoreBorder;
            Bounds = _restoreBounds;
            _menu.Visible = true;
            _ribbon.Visible = _toolbarItem.Checked;
            _quickBar.Visible = _settings.ShowQuickLaunch;
            _status.Visible = _statusItem.Checked;
            _split.Panel1Collapsed = !_managerItem.Checked;
            _hideStrip.Visible = !_managerItem.Checked;
            WindowState = FormWindowState.Normal;
        }
    }

    private void RestoreWindowState()
    {
        var bounds = new Rectangle(
            _settings.WindowX, _settings.WindowY,
            Math.Max(960, _settings.WindowWidth),
            Math.Max(620, _settings.WindowHeight));
        if (_settings.WindowX >= 0 && _settings.WindowY >= 0
            && Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds)))
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
        }
        if (_settings.WindowMaximized)
        {
            WindowState = FormWindowState.Maximized;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            if (!_split.Panel1Collapsed)
            {
                _split.SplitterDistance = Math.Max(200, (int)Math.Round(258 * DeviceDpi / 96.0));
            }
        }
        catch
        {
            // 窗口过窄时保持默认分隔位置。
        }
        if (CurrentPage() == null && _welcomeTab != null)
        {
            _tabs.SelectedTab = _welcomeTab;
        }
        XShellTheme.ApplyNativeTheme(this);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Shift | Keys.Oemplus)
            || keyData == (Keys.Control | Keys.Shift | Keys.Add)
            || keyData == (Keys.Control | Keys.Oemplus))
        {
            ZoomFont(1);
            return true;
        }
        if (keyData == (Keys.Control | Keys.OemMinus)
            || keyData == (Keys.Control | Keys.Subtract))
        {
            ZoomFont(-1);
            return true;
        }
        if (keyData == (Keys.Control | Keys.D0)
            || keyData == (Keys.Control | Keys.NumPad0))
        {
            ResetTerminalFont();
            return true;
        }
        if (keyData == Keys.F11)
        {
            ToggleFullScreen();
            return true;
        }
        if (keyData == (Keys.Control | Keys.Tab))
        {
            NextTab(1);
            return true;
        }
        if (keyData == (Keys.Control | Keys.Shift | Keys.Tab))
        {
            NextTab(-1);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_closing)
        {
            _closing = true;
            _clock.Stop();
            bool savedMaximized = _fullScreen ? false : WindowState == FormWindowState.Maximized;
            Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            if (_fullScreen)
            {
                bounds = _restoreBounds;
            }
            if (bounds.Width >= 600 && bounds.Height >= 400)
            {
                _settings.WindowX = bounds.X;
                _settings.WindowY = bounds.Y;
                _settings.WindowWidth = bounds.Width;
                _settings.WindowHeight = bounds.Height;
            }
            _settings.WindowMaximized = savedMaximized;
            _store.Save();
            foreach (var form in _sftpForms.Values.ToList())
            {
                try { form.Close(); } catch { }
            }
            _sftpForms.Clear();
            foreach (var page in _pages.Values.ToList())
            {
                try { page.Disconnect(); } catch { }
            }
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _clock.Stop();
            _clock.Dispose();
            _themeWatch.Stop();
            _themeWatch.Dispose();
        }
        base.Dispose(disposing);
    }
}
