using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// WirelessNetworkWatcher 风格选项卡：扫描局域网内所有活动设备，
/// 显示 IP、MAC、厂商、主机名、设备名、适配器、首次 / 最后发现时间，
/// 支持自动扫描、在线 / 离线状态、新设备提示、重命名、排序、列显示与导出。
/// </summary>
public sealed class WirelessNetworkWatcherTab : UserControl
{
    private const string OuiUrl = "https://standards-oui.ieee.org/oui/oui.txt";
    private const string EmbeddedOuiSha256 =
        "5b244af71265e31f60aaeb7da8e9c259b7b30e8330dc579dc58ed7977218cb42";
    private const string AllAdapters = "全部网卡";
    private const int ColIp = 0;
    private const int ColMac = 1;
    private const int ColVendor = 2;
    private const int ColHost = 3;
    private const int ColDevice = 4;
    private const int ColAdapter = 5;
    private const int ColFirst = 6;
    private const int ColLast = 7;
    private const int ColState = 8;

    private sealed class DeviceEntry
    {
        public string Ip = "";
        public string Mac = "";
        public string Vendor = "";
        public string HostName = "";
        public string DeviceName = "";
        public string Adapter = "";
        public DateTime FirstSeen;
        public DateTime LastSeen;
        public bool Online;
        public bool IsNew;
    }

    private sealed class ScanTarget
    {
        public IPAddress Ip = IPAddress.None;
        public string Adapter = "";
    }

    private sealed class PersistentState
    {
        public int Version { get; set; }
        public Dictionary<string, string> Names { get; set; } = new();
        public bool AutoScan { get; set; } = true;
        public int IntervalSeconds { get; set; } = 60;
        public bool ResolveHosts { get; set; } = true;
        public bool OnlineOnly { get; set; }
        public bool BeepOnNew { get; set; } = true;
        public string OuiPath { get; set; } = "";
        public string SelectedAdapter { get; set; } = "";
        public DateTime LastOuiCheck { get; set; }
        public string LastOuiHash { get; set; } = "";
        public bool LastOuiSucceeded { get; set; }
    }

    private static class Native
    {
        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        public static extern int SendARP(int destIp, int srcIp, byte[] macAddr,
            ref int phyAddrLen);
    }

    private static readonly Dictionary<string, string> BuiltInOui = new(StringComparer.OrdinalIgnoreCase)
    {
        ["005056"] = "VMware",
        ["000C29"] = "VMware",
        ["000569"] = "VMware",
        ["080027"] = "Oracle VirtualBox",
        ["525400"] = "QEMU / KVM",
        ["001C42"] = "Parallels",
        ["00155D"] = "Microsoft Hyper-V",
        ["00163E"] = "Xen",
        ["B827EB"] = "Raspberry Pi",
        ["DCA632"] = "Raspberry Pi",
        ["E45F01"] = "Raspberry Pi",
        ["001B21"] = "Intel",
        ["001E67"] = "Intel",
        ["3C970E"] = "Intel",
        ["8C1645"] = "Intel",
        ["A4BB6D"] = "Intel",
        ["00E04C"] = "Realtek",
        ["000393"] = "Apple",
        ["000A27"] = "Apple",
        ["001B63"] = "Apple",
        ["3C15C2"] = "Apple",
        ["A483E7"] = "Apple",
        ["F01898"] = "Apple",
        ["0012FB"] = "Samsung",
        ["001E7D"] = "Samsung",
        ["5C0A5B"] = "Samsung",
        ["8C7712"] = "Samsung",
        ["00E0FC"] = "Huawei",
        ["286ED4"] = "Huawei",
        ["4846FB"] = "Huawei",
        ["70723C"] = "Huawei",
        ["EC233D"] = "Huawei",
        ["640980"] = "Xiaomi",
        ["7811DC"] = "Xiaomi",
        ["F8A45F"] = "Xiaomi",
        ["001D0F"] = "TP-Link",
        ["14CC20"] = "TP-Link",
        ["50C7BF"] = "TP-Link",
        ["A42BB0"] = "TP-Link",
        ["00000C"] = "Cisco",
        ["001AA1"] = "Cisco",
        ["002A6A"] = "Cisco",
        ["001422"] = "Dell",
        ["00219B"] = "Dell",
        ["B82A72"] = "Dell",
        ["001F29"] = "HP",
        ["3CD92B"] = "HP",
        ["9CB654"] = "HP",
        ["001BFC"] = "ASUS",
        ["00248C"] = "ASUS",
        ["2C56DC"] = "ASUS",
        ["AC220B"] = "ASUS",
        ["240AC4"] = "Espressif (ESP32/ESP8266)",
        ["30AEA4"] = "Espressif (ESP32/ESP8266)",
        ["84F3EB"] = "Espressif (ESP32/ESP8266)",
        ["001018"] = "Broadcom",
        ["000C42"] = "MikroTik",
        ["2CC81B"] = "MikroTik",
        ["488F5A"] = "MikroTik",
        ["00055D"] = "D-Link",
        ["001B11"] = "D-Link",
        ["14D64D"] = "D-Link",
        ["00095B"] = "Netgear",
        ["00146C"] = "Netgear",
        ["204E7F"] = "Netgear",
        ["000D4B"] = "Roku",
        ["B0A737"] = "Roku",
        ["000E58"] = "Sonos",
        ["5CAAFD"] = "Sonos",
        ["00156D"] = "Ubiquiti",
        ["0418D6"] = "Ubiquiti",
        ["24A43C"] = "Ubiquiti",
        ["3C5AB4"] = "Google",
        ["F4F5E8"] = "Google",
        ["44650D"] = "Amazon",
        ["74C246"] = "Amazon",
        ["0009BF"] = "Nintendo",
        ["98B6E9"] = "Nintendo",
        ["0013A9"] = "Sony",
        ["FC0FE6"] = "Sony",
        ["001C62"] = "LG",
        ["58A2B5"] = "LG",
        ["C83A35"] = "Tenda",
        ["0495E6"] = "Tenda",
        ["000FE2"] = "H3C",
        ["70F96D"] = "H3C",
        ["0015EB"] = "ZTE",
        ["34E0CF"] = "ZTE"
    };

    private readonly AppConfig _config;
    private readonly Panel _top;
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly Label _bottomStatus;
    private readonly ListView _list;
    private readonly PillButton _startButton;
    private readonly PillButton _stopButton;
    private readonly PillButton _refreshButton;
    private readonly PillButton _exportButton;
    private readonly PillButton _clearButton;
    private readonly CheckBox _autoScan;
    private readonly NumericUpDown _interval;
    private readonly CheckBox _resolveHosts;
    private readonly CheckBox _onlineOnly;
    private readonly CheckBox _beepOnNew;
    private readonly ComboBox _adapterCombo;
    private readonly PillButton _adapterRefreshButton;
    private readonly ProgressBar _progress;
    private readonly Label _ouiStatusLabel;
    private readonly PillButton _manualOuiButton;
    private readonly ToolTip _ouiStatusTip;
    private readonly System.Windows.Forms.Timer _autoTimer;
    private readonly System.Windows.Forms.Timer _progressTimer;
    private readonly Font _titleFont;
    private readonly Font _statusFont;
    private readonly Font _smallFont;
    private readonly Font _smallBoldFont;
    private readonly List<DeviceEntry> _devices = new();
    private readonly Dictionary<string, string> _oui = new(BuiltInOui, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _customNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _deviceLock = new();
    private readonly int[] _columnWidths = new int[9];

    private CancellationTokenSource? _scanCts;
    private bool _scanning;
    private bool _isActive;
    private bool _pendingActivationScan;
    private DateTime _lastOuiCheckDate = DateTime.MinValue;
    private string _lastOuiHash = "";
    private bool _lastOuiSucceeded;
    private bool _disposed;
    private DateTime _lastScan;
    private double _lastScanSeconds;
    private int _sortColumn = ColIp;
    private bool _sortAscending = true;
    private string _statePath = "";
    private bool _loadingState;
    private int _scanTotal;
    private int _scanCompleted;
    private string _ouiHash = "";
    private string _ouiSource = "";
    private string _ouiCachePath = "";
    private bool _ouiChecking;

    public WirelessNetworkWatcherTab(AppConfig config)
    {
        _config = config;
        BackColor = Color.White;
        Padding = new Padding(16, 12, 16, 14);

        _titleFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
        _statusFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        _smallFont = new Font("Microsoft YaHei UI", 8.5f);
        _smallBoldFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);

        _top = new Panel { Dock = DockStyle.Top, Height = 180, BackColor = Color.White };
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
        topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68f));
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
            Text = "WirelessNetworkWatcher（局域网设备扫描）",
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
        _startButton = NewButton("开始扫描", 96, PillButtonStyle.Primary);
        _startButton.Click += (_, _) => _ = ScanAsync();
        _stopButton = NewButton("停止", 72);
        _stopButton.Enabled = false;
        _stopButton.Click += (_, _) => StopScan();
        _refreshButton = NewButton("立即刷新", 86);
        _refreshButton.Click += (_, _) => _ = ScanAsync();
        _exportButton = NewButton("导出", 72);
        _exportButton.Click += (_, _) => ShowExportMenu();
        _clearButton = NewButton("清空", 72);
        _clearButton.Click += (_, _) => ClearDevices();
        buttonBar.Controls.AddRange(new Control[]
        {
            _startButton, _stopButton, _refreshButton, _exportButton, _clearButton
        });
        topLayout.Controls.Add(buttonBar, 0, 1);

        var optionsBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            // 允许换行：窗口较窄时 OUI 提示自动独占下一行，保证完整显示不截断。
            WrapContents = true,
            BackColor = Color.White,
            Padding = new Padding(0, 5, 0, 0),
            Margin = new Padding(0)
        };
        _autoScan = NewCheck("自动扫描", 72);
        _interval = new NumericUpDown
        {
            Width = 66,
            Height = 24,
            Minimum = 10,
            Maximum = 3600,
            Value = 60,
            Font = _smallFont,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 1, 4, 0)
        };
        _resolveHosts = NewCheck("解析主机名", 88);
        _resolveHosts.Checked = true;
        _onlineOnly = NewCheck("仅显示在线", 88);
        _beepOnNew = NewCheck("新设备提示音", 100);
        _beepOnNew.Checked = true;
        var ouiButton = NewButton("OUI 数据库…", 100);
        ouiButton.Height = 26;
        ouiButton.Click += (_, _) => LoadOuiDialog();
        _ouiStatusLabel = new Label
        {
            Text = "OUI：准备加载本地数据库…",
            AutoSize = true,
            MinimumSize = new Size(0, 24),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Font = _smallFont,
            Cursor = Cursors.Hand,
            Margin = new Padding(4, 2, 0, 0)
        };
        _ouiStatusLabel.Click += (_, _) => ShowOuiInfo();
        _manualOuiButton = NewButton("手动获取 OUI", 104);
        _manualOuiButton.Height = 26;
        _manualOuiButton.Margin = new Padding(6, 1, 0, 0);
        _manualOuiButton.Click += (_, _) => _ = CheckRemoteOuiAsync(manual: true);
        _ouiStatusTip = new ToolTip();
        optionsBar.Controls.AddRange(new Control[]
        {
            _autoScan, _interval, NewOptionLabel("秒"), _resolveHosts, _onlineOnly,
            _beepOnNew, ouiButton, _manualOuiButton, _ouiStatusLabel
        });
        topLayout.Controls.Add(optionsBar, 0, 2);

        var adapterBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.White,
            Padding = new Padding(0, 2, 0, 0),
            Margin = new Padding(0)
        };
        var adapterLabel = new Label
        {
            Text = "扫描网卡：",
            AutoSize = false,
            Width = 70,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Font = _smallFont,
            Margin = new Padding(0, 1, 4, 0)
        };
        _adapterCombo = new ComboBox
        {
            Width = 250,
            Height = 26,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = _smallFont,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            Margin = new Padding(0, 1, 8, 0)
        };
        _adapterRefreshButton = NewButton("刷新网卡", 82);
        _adapterRefreshButton.Height = 26;
        _adapterRefreshButton.Click += (_, _) => RefreshAdapters();
        adapterBar.Controls.AddRange(new Control[]
        {
            adapterLabel, _adapterCombo, _adapterRefreshButton
        });
        topLayout.Controls.Add(adapterBar, 0, 3);
        _top.Controls.Add(topLayout);

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
            GridLines = true,
            Font = _smallFont,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            BorderStyle = BorderStyle.None
        };
        _list.Columns.Add("IP 地址", 120);
        _list.Columns.Add("MAC 地址", 150);
        _list.Columns.Add("制造商", 180);
        _list.Columns.Add("主机名", 150);
        _list.Columns.Add("设备名", 150);
        _list.Columns.Add("网络适配器", 160);
        _list.Columns.Add("首次发现", 140);
        _list.Columns.Add("最后发现", 140);
        _list.Columns.Add("状态", 70);
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
        _list.DoubleClick += (_, _) => RenameSelected();
        _list.ContextMenuStrip = BuildMenu();

        _progress = new ProgressBar
        {
            Dock = DockStyle.Bottom,
            Height = 14,
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous,
            Visible = false,
            BackColor = Color.White
        };

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
        Controls.Add(_progress);
        Controls.Add(_bottomStatus);
        Controls.Add(_top);
        Controls.SetChildIndex(_list, 0);
        Controls.SetChildIndex(_progress, 1);
        Controls.SetChildIndex(_bottomStatus, 2);
        Controls.SetChildIndex(_top, 3);

        _autoTimer = new System.Windows.Forms.Timer { Interval = 60000 };
        _autoTimer.Tick += (_, _) =>
        {
            if (_isActive && _autoScan.Checked && !_scanning)
            {
                _ = ScanAsync();
            }
        };
        _progressTimer = new System.Windows.Forms.Timer { Interval = 120 };
        _progressTimer.Tick += (_, _) =>
        {
            if (_disposed || IsDisposed)
            {
                return;
            }
            int total = _scanTotal;
            int done = Volatile.Read(ref _scanCompleted);
            int percent = total <= 0 ? 0 : Math.Min(100, done * 100 / total);
            _progress.Value = percent;
            if (_scanning)
            {
                _statusLabel.Text = "● 扫描中… " + done + "/" + total
                    + "（" + percent + "%）";
            }
        };

        _ouiCachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiniMonitor", "WirelessNetworkWatcher", "oui.txt");
        LoadEmbeddedOui();
        LoadOuiCache();
        RefreshAdapters();
        LoadState();
        WireOptionEvents();

        Disposed += (_, _) =>
        {
            _disposed = true;
            try { _scanCts?.Cancel(); } catch { }
            _autoTimer.Stop();
            SaveState();
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

    private CheckBox NewCheck(string text, int width)
    {
        return new CheckBox
        {
            Text = text,
            Width = width,
            Height = 26,
            Font = _smallFont,
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 1, 8, 0)
        };
    }

    private Label NewOptionLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Width = 22,
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
        menu.Items.Add("重命名设备…", null, (_, _) => RenameSelected());
        menu.Items.Add("复制选中", null, (_, _) => CopySelected());
        menu.Items.Add("复制全部", null, (_, _) => CopyAll());
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
        menu.Items.Add("OUI 数据库信息…", null, (_, _) => ShowOuiInfo());
        menu.Items.Add("加载 OUI 数据库…", null, (_, _) => LoadOuiDialog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("清空列表", null, (_, _) => ClearDevices());
        menu.Opening += (_, _) =>
        {
            bool has = _list.SelectedItems.Count > 0;
            foreach (ToolStripItem item in menu.Items)
            {
                if (item is ToolStripMenuItem entry
                    && (entry.Text.StartsWith("重命名设备")
                        || entry.Text.StartsWith("复制选中")))
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

    private int FindAdapterIndex(string name)
    {
        for (int i = 0; i < _adapterCombo.Items.Count; i++)
        {
            if (string.Equals(_adapterCombo.Items[i]?.ToString(), name,
                StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    private void RefreshAdapters()
    {
        string current = _adapterCombo.SelectedItem?.ToString() ?? "";
        bool oldLoading = _loadingState;
        _loadingState = true;
        try
        {
            _adapterCombo.BeginUpdate();
            _adapterCombo.Items.Clear();
            _adapterCombo.Items.Add(AllAdapters);
            var names = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up
                        || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }
                    bool hasIpv4 = false;
                    try
                    {
                        hasIpv4 = ni.GetIPProperties().UnicastAddresses.Any(address =>
                            address.Address.AddressFamily == AddressFamily.InterNetwork);
                    }
                    catch
                    {
                    }
                    if (hasIpv4 && !string.IsNullOrWhiteSpace(ni.Name)
                        && !names.Contains(ni.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        names.Add(ni.Name);
                    }
                }
            }
            catch
            {
            }
            names.Sort(StringComparer.CurrentCultureIgnoreCase);
            foreach (string name in names)
            {
                _adapterCombo.Items.Add(name);
            }
            _adapterCombo.EndUpdate();
            int index = current.Length > 0 ? FindAdapterIndex(current) : -1;
            if (index < 0)
            {
                // 默认优先“以太网 / Ethernet0 / 本地连接”，都没有则扫描全部网卡。
                index = FindAdapterIndex("以太网");
                if (index < 0)
                {
                    index = FindAdapterIndex("Ethernet0");
                }
                if (index < 0)
                {
                    index = FindAdapterIndex("Ethernet");
                }
                if (index < 0)
                {
                    index = FindAdapterIndex("本地连接");
                }
                if (index < 0)
                {
                    index = 0;
                }
            }
            _adapterCombo.SelectedIndex = index;
        }
        finally
        {
            _loadingState = oldLoading;
        }
    }

    private void WireOptionEvents()
    {
        _autoScan.CheckedChanged += (_, _) =>
        {
            if (_loadingState)
            {
                return;
            }
            if (_autoScan.Checked)
            {
                _autoTimer.Interval = (int)_interval.Value * 1000;
                if (_isActive)
                {
                    _autoTimer.Start();
                    if (!_scanning)
                    {
                        _ = ScanAsync();
                    }
                }
            }
            else
            {
                _autoTimer.Stop();
            }
            SaveState();
        };
        _interval.ValueChanged += (_, _) =>
        {
            if (_autoScan.Checked)
            {
                _autoTimer.Interval = (int)_interval.Value * 1000;
            }
            if (!_loadingState)
            {
                SaveState();
            }
        };
        _resolveHosts.CheckedChanged += (_, _) =>
        {
            if (!_loadingState)
            {
                SaveState();
                if (_resolveHosts.Checked && _devices.Count > 0)
                {
                    _ = ResolveMissingHostNamesAsync();
                }
            }
        };
        _onlineOnly.CheckedChanged += (_, _) =>
        {
            if (!_loadingState)
            {
                SaveState();
            }
            RefreshList();
        };
        _beepOnNew.CheckedChanged += (_, _) =>
        {
            if (!_loadingState)
            {
                SaveState();
            }
        };
        _adapterCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_loadingState)
            {
                return;
            }
            SaveState();
            if (_autoScan.Checked && !_scanning)
            {
                _ = ScanAsync();
            }
        };
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
            if (_lastOuiCheckDate.Date >= DateTime.Today)
            {
                ShowTodayOuiStatus();
            }
            else
            {
                // 一天只下载 / 比对一次；无论成功失败，今天不再重复下载。
                _lastOuiCheckDate = DateTime.Today;
                _lastOuiSucceeded = false;
                SaveState();
                _ = CheckRemoteOuiAsync();
            }
            if (_autoScan.Checked)
            {
                _autoTimer.Interval = (int)_interval.Value * 1000;
                _autoTimer.Start();
                if (!_scanning)
                {
                    _ = ScanAsync();
                }
                else
                {
                    _pendingActivationScan = true;
                }
            }
        }
        else
        {
            _pendingActivationScan = false;
            try { _scanCts?.Cancel(); } catch { }
            _autoTimer.Stop();
            _progressTimer.Stop();
            if (!IsDisposed)
            {
                _progress.Visible = false;
                _statusLabel.Text = "● 未启用（切换到该选项卡后自动扫描）";
                _statusLabel.ForeColor = ShutdownTheme.Muted;
            }
        }
    }

    private async Task ScanAsync()
    {
        if (_scanning || _disposed || !_isActive)
        {
            return;
        }
        _scanning = true;
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;
        _startButton.Enabled = false;
        _stopButton.Enabled = true;
        _refreshButton.Enabled = false;
        _statusLabel.Text = "● 扫描中…";
        _statusLabel.ForeColor = ShutdownTheme.Accent;
        var stopwatch = Stopwatch.StartNew();
        int newCount = 0;
        try
        {
            lock (_deviceLock)
            {
                foreach (var device in _devices)
                {
                    device.Online = false;
                    device.IsNew = false;
                }
            }

            var targets = BuildScanTargets();
            if (targets.Count == 0)
            {
                _statusLabel.Text = "● 未找到可扫描的活动网卡";
                return;
            }
            _scanTotal = targets.Count;
            _scanCompleted = 0;
            _progress.Value = 0;
            _progress.Visible = true;
            _progressTimer.Start();

            var found = new List<(IPAddress Ip, string Mac, string Adapter)>();
            using var gate = new SemaphoreSlim(48);
            var tasks = new List<Task>(targets.Count);
            foreach (var target in targets)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }
                tasks.Add(Task.Run(async () =>
                {
                    await gate.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        string? mac = SendArp(target.Ip);
                        if (mac != null)
                        {
                            lock (found)
                            {
                                found.Add((target.Ip, mac, target.Adapter));
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch
                    {
                        // 单个地址失败不影响整体扫描。
                    }
                    finally
                    {
                        Interlocked.Increment(ref _scanCompleted);
                        gate.Release();
                    }
                }, token));
            }
            await Task.WhenAll(tasks).ConfigureAwait(true);

            if (token.IsCancellationRequested || _disposed || IsDisposed)
            {
                return;
            }

            foreach (var (ip, mac, adapter) in found)
            {
                bool isNew = RecordDevice(ip.ToString(), mac, adapter);
                if (isNew)
                {
                    newCount++;
                }
            }

            if (_resolveHosts.Checked)
            {
                await ResolveHostNamesAsync(found, token).ConfigureAwait(true);
            }

            if (_disposed || IsDisposed)
            {
                return;
            }
            _lastScan = DateTime.Now;
            _lastScanSeconds = stopwatch.Elapsed.TotalSeconds;
            RefreshList();
            if (newCount > 0 && _beepOnNew.Checked)
            {
                try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
            }
            _statusLabel.Text = "● 扫描完成"
                + (newCount > 0 ? " · 新增 " + newCount + " 台设备" : "");
            _statusLabel.ForeColor = newCount > 0 ? ShutdownTheme.Accent : ShutdownTheme.Success;
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && !IsDisposed)
            {
                _statusLabel.Text = _isActive
                    ? "● 已停止"
                    : "● 未启用（切换到该选项卡后自动扫描）";
                _statusLabel.ForeColor = ShutdownTheme.Muted;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("WirelessNetworkWatcher 扫描失败：" + ex);
            if (!_disposed && !IsDisposed)
            {
                _statusLabel.Text = "● 扫描失败：" + ex.Message;
                _statusLabel.ForeColor = ShutdownTheme.Danger;
            }
        }
        finally
        {
            _scanning = false;
            _progressTimer.Stop();
            if (!_disposed && !IsDisposed)
            {
                _progress.Visible = false;
                _startButton.Enabled = true;
                _stopButton.Enabled = false;
                _refreshButton.Enabled = true;
                UpdateBottomStatus();
                if (_pendingActivationScan && _isActive && _autoScan.Checked && !_disposed)
                {
                    _pendingActivationScan = false;
                    _ = ScanAsync();
                }
            }
        }
    }

    private void StopScan()
    {
        try { _scanCts?.Cancel(); } catch { }
        _autoScan.Checked = false;
        _autoTimer.Stop();
    }

    private List<ScanTarget> BuildScanTargets()
    {
        var targets = new List<ScanTarget>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string selectedAdapter = _adapterCombo.SelectedItem?.ToString() ?? AllAdapters;
        bool allAdapters = string.Equals(selectedAdapter, AllAdapters,
            StringComparison.OrdinalIgnoreCase);
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up
                || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }
            if (!allAdapters
                && !string.Equals(ni.Name, selectedAdapter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            IPInterfaceProperties properties;
            try
            {
                properties = ni.GetIPProperties();
            }
            catch
            {
                continue;
            }
            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }
                byte[] ipBytes = unicast.Address.GetAddressBytes();
                if (ipBytes[0] == 169 && ipBytes[1] == 254)
                {
                    continue;
                }
                byte[] maskBytes = unicast.IPv4Mask?.GetAddressBytes()
                    ?? new byte[] { 255, 255, 255, 0 };
                uint ipValue = ToUInt(ipBytes);
                uint maskValue = ToUInt(maskBytes);
                if (maskValue == 0)
                {
                    maskValue = 0xFFFFFF00;
                }
                // 限制为 /24，避免 /16 等大子网一次扫描过多地址。
                if (CountBits(maskValue) < 24)
                {
                    maskValue = 0xFFFFFF00;
                }
                uint network = ipValue & maskValue;
                uint broadcast = network | ~maskValue;
                string adapter = string.IsNullOrWhiteSpace(ni.Name)
                    ? ni.Description : ni.Name;
                for (uint host = network + 1; host < broadcast; host++)
                {
                    if (targets.Count >= 1024)
                    {
                        break;
                    }
                    var address = FromUInt(host);
                    if (!seen.Add(address.ToString()))
                    {
                        continue;
                    }
                    targets.Add(new ScanTarget { Ip = address, Adapter = adapter });
                }
            }
        }
        return targets;
    }

    private static string? SendArp(IPAddress address)
    {
        try
        {
            byte[] mac = new byte[6];
            int length = mac.Length;
            int destination = BitConverter.ToInt32(address.GetAddressBytes(), 0);
            int result = Native.SendARP(destination, 0, mac, ref length);
            if (result != 0 || length < 6)
            {
                return null;
            }
            bool any = false;
            foreach (byte value in mac)
            {
                if (value != 0)
                {
                    any = true;
                    break;
                }
            }
            if (!any)
            {
                return null;
            }
            return string.Join(":", mac.Select(value => value.ToString("X2")));
        }
        catch
        {
            return null;
        }
    }

    private bool RecordDevice(string ip, string mac, string adapter)
    {
        lock (_deviceLock)
        {
            var device = _devices.FirstOrDefault(entry =>
                    !string.IsNullOrEmpty(mac)
                    && string.Equals(entry.Mac, mac, StringComparison.OrdinalIgnoreCase))
                ?? _devices.FirstOrDefault(entry => string.Equals(entry.Ip, ip,
                    StringComparison.OrdinalIgnoreCase));
            bool isNew = device == null;
            if (device == null)
            {
                device = new DeviceEntry
                {
                    Ip = ip,
                    Mac = mac,
                    Adapter = adapter,
                    FirstSeen = DateTime.Now
                };
                _devices.Add(device);
            }
            device.Ip = ip;
            device.Mac = string.IsNullOrEmpty(mac) ? device.Mac : mac;
            device.Adapter = adapter;
            device.Online = true;
            device.LastSeen = DateTime.Now;
            device.Vendor = LookupVendor(device.Mac);
            if (isNew)
            {
                device.IsNew = true;
            }
            if (_customNames.TryGetValue(device.Mac, out string? name)
                || _customNames.TryGetValue(device.Ip, out name))
            {
                device.DeviceName = name;
            }
            return isNew;
        }
    }

    private async Task ResolveHostNamesAsync(
        List<(IPAddress Ip, string Mac, string Adapter)> found,
        CancellationToken token)
    {
        using var gate = new SemaphoreSlim(16);
        var tasks = found.Select(async item =>
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                string? host = await ResolveHostNameAsync(item.Ip, token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(host))
                {
                    lock (_deviceLock)
                    {
                        var device = _devices.FirstOrDefault(entry =>
                            string.Equals(entry.Mac, item.Mac, StringComparison.OrdinalIgnoreCase));
                        if (device != null)
                        {
                            device.HostName = host;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
            finally
            {
                gate.Release();
            }
        }).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task ResolveMissingHostNamesAsync()
    {
        List<DeviceEntry> pending;
        lock (_deviceLock)
        {
            pending = _devices.Where(device => device.Online
                && string.IsNullOrWhiteSpace(device.HostName)).ToList();
        }
        if (pending.Count == 0)
        {
            RefreshList();
            return;
        }
        using var gate = new SemaphoreSlim(16);
        foreach (var device in pending)
        {
            _ = Task.Run(async () =>
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (!IPAddress.TryParse(device.Ip, out var ip))
                    {
                        return;
                    }
                    string? host = await ResolveHostNameAsync(ip, CancellationToken.None)
                        .ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(host))
                    {
                        device.HostName = host;
                    }
                }
                finally
                {
                    gate.Release();
                }
            });
        }
        await Task.Delay(400).ConfigureAwait(false);
        if (!_disposed)
        {
            BeginInvoke(new Action(RefreshList));
        }
    }

    private static async Task<string?> ResolveHostNameAsync(IPAddress ip,
        CancellationToken token)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(ip)
                .WaitAsync(TimeSpan.FromSeconds(1.5), token).ConfigureAwait(false);
            string host = entry.HostName?.Trim() ?? "";
            return host.Length == 0 || host == ip.ToString() ? null : host;
        }
        catch
        {
            return null;
        }
    }

    // ================= 列表 =================

    private void RefreshList()
    {
        if (_disposed || IsDisposed)
        {
            return;
        }
        var selected = _list.SelectedItems.Cast<ListViewItem>()
            .Select(item => item.Tag as DeviceEntry)
            .Where(entry => entry != null)
            .Select(entry => entry!)
            .ToHashSet();
        List<DeviceEntry> devices;
        lock (_deviceLock)
        {
            devices = _devices.ToList();
        }
        if (_onlineOnly.Checked)
        {
            devices = devices.Where(device => device.Online).ToList();
        }
        devices.Sort(CompareDevices);
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var device in devices)
            {
                var item = new ListViewItem(device.Ip) { Tag = device };
                item.SubItems.Add(device.Mac);
                item.SubItems.Add(device.Vendor);
                item.SubItems.Add(device.HostName);
                item.SubItems.Add(string.IsNullOrWhiteSpace(device.DeviceName)
                    ? "" : device.DeviceName);
                item.SubItems.Add(device.Adapter);
                item.SubItems.Add(device.FirstSeen == DateTime.MinValue ? ""
                    : device.FirstSeen.ToString("yyyy-MM-dd HH:mm:ss"));
                item.SubItems.Add(device.LastSeen == DateTime.MinValue ? ""
                    : device.LastSeen.ToString("yyyy-MM-dd HH:mm:ss"));
                item.SubItems.Add(device.IsNew ? "新增" : device.Online ? "在线" : "离线");
                if (device.IsNew)
                {
                    item.ForeColor = ShutdownTheme.Accent;
                    item.Font = _smallBoldFont;
                }
                else if (!device.Online)
                {
                    item.ForeColor = Color.FromArgb(148, 163, 184);
                }
                else
                {
                    // 在线设备使用墨绿色。
                    item.ForeColor = Color.FromArgb(6, 95, 70);
                }
                if (selected.Contains(device))
                {
                    item.Selected = true;
                }
                _list.Items.Add(item);
            }
        }
        finally
        {
            _list.EndUpdate();
        }
        UpdateBottomStatus();
    }

    private int CompareDevices(DeviceEntry left, DeviceEntry right)
    {
        int result;
        switch (_sortColumn)
        {
            case ColIp:
                result = CompareIp(left.Ip, right.Ip);
                break;
            case ColFirst:
                result = left.FirstSeen.CompareTo(right.FirstSeen);
                break;
            case ColLast:
                result = left.LastSeen.CompareTo(right.LastSeen);
                break;
            case ColState:
                result = string.CompareOrdinal(
                    left.IsNew ? "0" : left.Online ? "1" : "2",
                    right.IsNew ? "0" : right.Online ? "1" : "2");
                break;
            case ColMac:
                result = string.Compare(left.Mac, right.Mac, StringComparison.OrdinalIgnoreCase);
                break;
            case ColVendor:
                result = string.Compare(left.Vendor, right.Vendor, StringComparison.CurrentCulture);
                break;
            case ColHost:
                result = string.Compare(left.HostName, right.HostName, StringComparison.CurrentCulture);
                break;
            case ColDevice:
                result = string.Compare(left.DeviceName, right.DeviceName, StringComparison.CurrentCulture);
                break;
            case ColAdapter:
                result = string.Compare(left.Adapter, right.Adapter, StringComparison.CurrentCulture);
                break;
            default:
                result = CompareIp(left.Ip, right.Ip);
                break;
        }
        if (result == 0)
        {
            result = CompareIp(left.Ip, right.Ip);
        }
        return _sortAscending ? result : -result;
    }

    private static int CompareIp(string left, string right)
    {
        bool leftOk = IPAddress.TryParse(left, out var leftIp);
        bool rightOk = IPAddress.TryParse(right, out var rightIp);
        if (leftOk && rightOk)
        {
            byte[] a = leftIp!.GetAddressBytes();
            byte[] b = rightIp!.GetAddressBytes();
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                int diff = a[i].CompareTo(b[i]);
                if (diff != 0)
                {
                    return diff;
                }
            }
            return a.Length.CompareTo(b.Length);
        }
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateBottomStatus()
    {
        int online;
        int offline;
        int newCount;
        lock (_deviceLock)
        {
            online = _devices.Count(device => device.Online);
            offline = _devices.Count(device => !device.Online);
            newCount = _devices.Count(device => device.IsNew);
        }
        _bottomStatus.Text = "在线 " + online + " · 离线 " + offline
            + " · 新增 " + newCount
            + (_lastScan == DateTime.MinValue ? ""
                : "  ·  上次扫描 " + _lastScan.ToString("HH:mm:ss")
                  + "，用时 " + _lastScanSeconds.ToString("0.0") + " 秒")
            + "  ·  共 " + _devices.Count + " 台设备";
    }

    public void ClearDevices()
    {
        lock (_deviceLock)
        {
            _devices.Clear();
        }
        _list.Items.Clear();
        _lastScan = DateTime.MinValue;
        _lastScanSeconds = 0;
        _statusLabel.Text = "● 已清空";
        _statusLabel.ForeColor = ShutdownTheme.Muted;
        UpdateBottomStatus();
    }

    private void RenameSelected()
    {
        if (_list.SelectedItems.Count == 0)
        {
            return;
        }
        var device = _list.SelectedItems[0].Tag as DeviceEntry;
        if (device == null)
        {
            return;
        }
        string? name = Prompt.Show(FindForm(), "重命名设备",
            "为 " + device.Ip + "（" + device.Mac + "）设置显示名称：",
            string.IsNullOrWhiteSpace(device.DeviceName) ? device.HostName : device.DeviceName);
        if (name == null)
        {
            return;
        }
        device.DeviceName = name.Trim();
        if (device.DeviceName.Length == 0)
        {
            _customNames.Remove(device.Mac);
            _customNames.Remove(device.Ip);
        }
        else
        {
            string key = string.IsNullOrWhiteSpace(device.Mac) ? device.Ip : device.Mac;
            _customNames[key] = device.DeviceName;
        }
        SaveState();
        RefreshList();
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
            Title = "导出 WirelessNetworkWatcher 设备列表",
            Filter = filter,
            FileName = "WirelessNetworkWatcher_"
                + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "." + extension
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }
        try
        {
            List<DeviceEntry> devices;
            lock (_deviceLock)
            {
                devices = _devices.ToList();
            }
            devices.Sort(CompareDevices);
            string content = format switch
            {
                "csv" => BuildCsv(devices),
                "html" => BuildHtml(devices),
                "xml" => BuildXml(devices),
                _ => BuildText(devices)
            };
            File.WriteAllText(dialog.FileName, content, new UTF8Encoding(true));
            _bottomStatus.Text = "已导出：" + dialog.FileName;
            if (MessageBox.Show(FindForm(), "导出完成：\n" + dialog.FileName
                + "\n\n是否打开所在目录？", "WirelessNetworkWatcher",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Path.GetDirectoryName(dialog.FileName) ?? "",
                        UseShellExecute = true
                    });
                }
                catch
                {
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(FindForm(), "导出失败：\n" + ex.Message,
                "WirelessNetworkWatcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string[] ExportHeaders() => new[]
    {
        "IP 地址", "MAC 地址", "制造商", "主机名", "设备名",
        "网络适配器", "首次发现", "最后发现", "状态"
    };

    private static string[] ExportRow(DeviceEntry device) => new[]
    {
        device.Ip,
        device.Mac,
        device.Vendor,
        device.HostName,
        device.DeviceName,
        device.Adapter,
        device.FirstSeen == DateTime.MinValue ? ""
            : device.FirstSeen.ToString("yyyy-MM-dd HH:mm:ss"),
        device.LastSeen == DateTime.MinValue ? ""
            : device.LastSeen.ToString("yyyy-MM-dd HH:mm:ss"),
        device.IsNew ? "新增" : device.Online ? "在线" : "离线"
    };

    private static string BuildText(List<DeviceEntry> devices)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join("\t", ExportHeaders()));
        foreach (var device in devices)
        {
            builder.AppendLine(string.Join("\t", ExportRow(device)));
        }
        return builder.ToString();
    }

    private static string BuildCsv(List<DeviceEntry> devices)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", ExportHeaders().Select(QuoteCsv)));
        foreach (var device in devices)
        {
            builder.AppendLine(string.Join(",", ExportRow(device).Select(QuoteCsv)));
        }
        return builder.ToString();
    }

    private static string QuoteCsv(string value) =>
        "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

    private static string BuildHtml(List<DeviceEntry> devices)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
        builder.AppendLine("<title>WirelessNetworkWatcher</title><style>");
        builder.AppendLine("body{font-family:'Microsoft YaHei UI',Arial,sans-serif;background:#f8fafc;color:#1e293b;padding:20px}");
        builder.AppendLine("h1{font-size:20px}table{border-collapse:collapse;width:100%;background:#fff}");
        builder.AppendLine("th,td{border:1px solid #cbd5e1;padding:6px 9px;font-size:13px;text-align:left}");
        builder.AppendLine("th{background:#e2e8f0}tr:nth-child(even) td{background:#f8fafc}");
        builder.AppendLine("</style></head><body><h1>WirelessNetworkWatcher</h1>");
        builder.AppendLine("<p>导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            + " · 共 " + devices.Count + " 台设备</p><table><thead><tr>");
        foreach (string header in ExportHeaders())
        {
            builder.Append("<th>").Append(WebUtility.HtmlEncode(header)).AppendLine("</th>");
        }
        builder.AppendLine("</tr></thead><tbody>");
        foreach (var device in devices)
        {
            builder.AppendLine("<tr>");
            foreach (string value in ExportRow(device))
            {
                builder.Append("<td>").Append(WebUtility.HtmlEncode(value)).AppendLine("</td>");
            }
            builder.AppendLine("</tr>");
        }
        builder.AppendLine("</tbody></table></body></html>");
        return builder.ToString();
    }

    private static string BuildXml(List<DeviceEntry> devices)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        builder.AppendLine("<wirelessNetworkWatcher exported=\""
            + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") + "\">");
        foreach (var device in devices)
        {
            builder.AppendLine("  <device>");
            AppendXml(builder, "ipAddress", device.Ip);
            AppendXml(builder, "macAddress", device.Mac);
            AppendXml(builder, "vendor", device.Vendor);
            AppendXml(builder, "hostName", device.HostName);
            AppendXml(builder, "deviceName", device.DeviceName);
            AppendXml(builder, "adapter", device.Adapter);
            AppendXml(builder, "firstDetected", device.FirstSeen == DateTime.MinValue
                ? "" : device.FirstSeen.ToString("yyyy-MM-ddTHH:mm:ss"));
            AppendXml(builder, "lastDetected", device.LastSeen == DateTime.MinValue
                ? "" : device.LastSeen.ToString("yyyy-MM-ddTHH:mm:ss"));
            AppendXml(builder, "state", device.IsNew ? "new" : device.Online ? "online" : "offline");
            builder.AppendLine("  </device>");
        }
        builder.AppendLine("</wirelessNetworkWatcher>");
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
        var devices = _list.SelectedItems.Cast<ListViewItem>()
            .Select(item => item.Tag as DeviceEntry)
            .Where(entry => entry != null)
            .Select(entry => entry!)
            .ToList();
        if (devices.Count == 0)
        {
            return;
        }
        CopyToClipboard(devices);
    }

    private void CopyAll()
    {
        List<DeviceEntry> devices;
        lock (_deviceLock)
        {
            devices = _devices.ToList();
        }
        devices.Sort(CompareDevices);
        CopyToClipboard(devices);
    }

    private static void CopyToClipboard(List<DeviceEntry> devices)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join("\t", ExportHeaders()));
        foreach (var device in devices)
        {
            builder.AppendLine(string.Join("\t", ExportRow(device)));
        }
        try
        {
            Clipboard.SetText(builder.ToString());
        }
        catch
        {
            // 剪贴板被占用时忽略。
        }
    }

    // ================= OUI / 持久化 =================

    /// <summary>当前 OUI 厂商条目数（供 BluetoothView 复用同一份数据库）。</summary>
    public int OuiEntryCount => _oui.Count;

    /// <summary>当前 OUI 来源说明（供 BluetoothView 复用）。</summary>
    public string OuiSourceText => string.IsNullOrWhiteSpace(_ouiSource)
        ? "内置常见厂商表" : _ouiSource;

    /// <summary>OUI 数据库在线更新或手动加载后触发，供 BluetoothView 刷新厂商列。</summary>
    public event EventHandler? OuiDatabaseChanged;

    /// <summary>按 MAC / 蓝牙地址查询 OUI 厂商，供 BluetoothView 复用。</summary>
    public string LookupVendor(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac))
        {
            return "";
        }
        string prefix = mac.Replace(":", "").Replace("-", "");
        if (prefix.Length < 6)
        {
            return "";
        }
        prefix = prefix[..6];
        return _oui.TryGetValue(prefix, out string? vendor) ? vendor : "未知";
    }

    private void LoadOuiDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "加载 OUI 厂商数据库（oui.txt / oui.csv）",
            Filter = "OUI 数据库|*.txt;*.csv|所有文件|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }
        if (LoadOuiFile(dialog.FileName, out string error))
        {
            foreach (var device in _devices)
            {
                device.Vendor = LookupVendor(device.Mac);
            }
            RefreshList();
            _bottomStatus.Text = "已加载 OUI 数据库：" + dialog.FileName;
            SaveState();
        }
        else
        {
            MessageBox.Show(FindForm(), "加载 OUI 数据库失败：\n" + error,
                "WirelessNetworkWatcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private bool LoadOuiFile(string path, out string error)
    {
        error = "";
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            int loaded = ParseOuiLines(File.ReadLines(path));
            if (loaded == 0)
            {
                error = "文件中没有可识别的 OUI 记录。";
                return false;
            }
            _lastOuiPath = path;
            _ouiHash = ComputeSha256(bytes);
            _ouiSource = "手动加载：" + path;
            SetOuiStatus("OUI：已手动加载 " + loaded + " 条厂商记录（SHA256 "
                + _ouiHash[..12] + "…）", ShutdownTheme.Success);
            OuiDatabaseChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>从嵌入资源加载 2026-11-08 的 IEEE OUI 数据库。</summary>
    private void LoadEmbeddedOui()
    {
        try
        {
            using var stream = typeof(WirelessNetworkWatcherTab).Assembly
                .GetManifestResourceStream("MiniMonitor.Resources.oui.txt");
            if (stream == null)
            {
                return;
            }
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            int loaded = ParseOuiLines(reader);
            if (loaded > 0)
            {
                _ouiHash = EmbeddedOuiSha256;
                _ouiSource = "内置 IEEE OUI（2026-11-08）";
                SetOuiStatus("OUI：使用内置 IEEE（2026-11-08，"
                    + loaded + " 条，SHA256 " + EmbeddedOuiSha256[..12] + "…）");
            }
        }
        catch
        {
            // 嵌入资源读取失败时继续使用内置常见厂商表。
        }
    }

    /// <summary>加载上次在线更新并校验哈希通过的本地 OUI 缓存。</summary>
    private void LoadOuiCache()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_ouiCachePath) || !File.Exists(_ouiCachePath))
            {
                return;
            }
            byte[] bytes = File.ReadAllBytes(_ouiCachePath);
            if (bytes.Length < 500_000)
            {
                return;
            }
            string hash = ComputeSha256(bytes);
            string hashPath = _ouiCachePath + ".sha256";
            if (File.Exists(hashPath))
            {
                string expected = File.ReadAllText(hashPath).Trim();
                if (!string.Equals(expected, hash, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true);
            int loaded = ParseOuiLines(reader);
            if (loaded > 0)
            {
                _ouiHash = hash;
                _ouiSource = "本地缓存 OUI 数据库";
                SetOuiStatus("OUI：已加载本地缓存（" + loaded + " 条，SHA256 "
                    + hash[..12] + "…）");
            }
        }
        catch
        {
            // 缓存损坏时使用内置数据库。
        }
    }

    /// <summary>
    /// 每次打开选项卡时在后台重新下载 IEEE OUI，计算 SHA256；
    /// 与当前使用的哈希不同才替换，确保始终使用最新厂商信息。
    /// </summary>
    private async Task CheckRemoteOuiAsync(bool manual = false)
    {
        if (_ouiChecking)
        {
            SetOuiStatus("OUI：正在检测，请稍候…", ShutdownTheme.Accent);
            return;
        }
        _ouiChecking = true;
        _manualOuiButton.Enabled = false;
        if (manual)
        {
            // 手动获取不受“每天一次”限制；立即记录检查日期与结果。
            _lastOuiCheckDate = DateTime.Today;
            _lastOuiSucceeded = false;
            SaveState();
        }
        try
        {
        SetOuiStatus(manual
            ? "OUI：手动获取，正在下载并比对 SHA256…"
            : "OUI：正在下载今日最新数据库并比对 SHA256…", ShutdownTheme.Accent);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MiniMonitor/1.66.4");
            byte[] bytes = await http.GetByteArrayAsync(OuiUrl).ConfigureAwait(false);
            if (bytes.Length < 500_000)
            {
                SetOuiStatus("OUI：下载内容无效，继续使用本地数据库",
                    ShutdownTheme.Danger);
                return;
            }
            string hash = ComputeSha256(bytes);
            if (string.Equals(hash, _ouiHash, StringComparison.OrdinalIgnoreCase))
            {
                SaveOuiCheckResult(hash, updated: false, count: 0);
                SetOuiStatus("OUI：今日已下载比对，OUI 已是最新（SHA256 "
                    + hash[..12] + "…）", ShutdownTheme.Success);
                return;
            }
            SetOuiStatus("OUI：发现新版本（SHA256 " + hash[..12]
                + "…），正在应用…", ShutdownTheme.Accent);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true))
            {
                if (ParseOuiLines(reader, map) == 0)
                {
                    SetOuiStatus("OUI：下载内容无有效厂商记录，继续使用本地数据库",
                        ShutdownTheme.Danger);
                    return;
                }
            }
            try
            {
                string? folder = Path.GetDirectoryName(_ouiCachePath);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                File.WriteAllBytes(_ouiCachePath, bytes);
                File.WriteAllText(_ouiCachePath + ".sha256", hash, new UTF8Encoding(false));
            }
            catch
            {
                // 缓存写入失败不影响本次使用新数据。
            }
            if (_disposed || IsDisposed)
            {
                return;
            }
            BeginInvoke(new Action(() =>
            {
                if (_disposed || IsDisposed)
                {
                    return;
                }
                _oui.Clear();
                foreach (var pair in map)
                {
                    _oui[pair.Key] = pair.Value;
                }
                _ouiHash = hash;
                _ouiSource = "在线更新（" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "）";
                for (int i = 0; i < _devices.Count; i++)
                {
                    _devices[i].Vendor = LookupVendor(_devices[i].Mac);
                }
                RefreshList();
                OuiDatabaseChanged?.Invoke(this, EventArgs.Empty);
                SaveOuiCheckResult(hash, updated: true, count: map.Count);
                SetOuiStatus("OUI：今日已更新 OUI 为最新版本（" + map.Count
                    + " 条，SHA256 " + hash[..12] + "…）", ShutdownTheme.Success);
                _bottomStatus.Text = "OUI 数据库已更新：" + map.Count
                    + " 条 · SHA256 " + hash[..12] + "…";
            }));
        }
        catch
        {
            _lastOuiSucceeded = false;
            SetOuiStatus("OUI：今日下载 / 比对失败，继续使用本地数据库",
                ShutdownTheme.Danger);
            if (!IsDisposed && IsHandleCreated)
            {
                try { BeginInvoke(new Action(SaveState)); } catch { }
            }
        }
        }
        finally
        {
            _ouiChecking = false;
            if (!IsDisposed && IsHandleCreated)
            {
                try
                {
                    BeginInvoke(new Action(() => _manualOuiButton.Enabled = true));
                }
                catch
                {
                }
            }
        }
    }

    private void ShowTodayOuiStatus()
    {
        bool latest = _lastOuiSucceeded
            && string.Equals(_lastOuiHash, _ouiHash, StringComparison.OrdinalIgnoreCase);
        SetOuiStatus(latest
                ? "OUI：今日已下载并比对，今日已更新 OUI（SHA256 "
                  + ShortOuiHash() + "…）"
                : "OUI：今日已执行检查，继续使用本地数据库",
            latest ? ShutdownTheme.Success : ShutdownTheme.Muted);
    }

    private string ShortOuiHash() =>
        string.IsNullOrWhiteSpace(_ouiHash) ? "—"
            : _ouiHash[..Math.Min(12, _ouiHash.Length)];

    private void SaveOuiCheckResult(string hash, bool updated, int count)
    {
        _lastOuiCheckDate = DateTime.Today;
        _lastOuiHash = hash;
        _lastOuiSucceeded = true;
        if (IsDisposed)
        {
            return;
        }
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(SaveState)); } catch { }
            return;
        }
        SaveState();
    }

    private int ParseOuiLines(IEnumerable<string> lines,
        Dictionary<string, string>? target = null)
    {
        target ??= _oui;
        int loaded = 0;
        foreach (string line in lines)
        {
            loaded += ParseOuiLine(line, target);
        }
        return loaded;
    }

    private int ParseOuiLines(TextReader reader,
        Dictionary<string, string>? target = null)
    {
        target ??= _oui;
        int loaded = 0;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            loaded += ParseOuiLine(line, target);
        }
        return loaded;
    }

    private static int ParseOuiLine(string line, Dictionary<string, string> target)
    {
        string text = line.Trim();
        if (text.Length == 0 || text.StartsWith("#", StringComparison.Ordinal))
        {
            return 0;
        }
        string prefix = "";
        string vendor = "";
        int hexIndex = text.IndexOf("(hex)", StringComparison.OrdinalIgnoreCase);
        if (hexIndex > 0)
        {
            prefix = text[..hexIndex].Trim();
            vendor = text[(hexIndex + 5)..].Trim();
        }
        else
        {
            int comma = text.IndexOf(',');
            if (comma > 0)
            {
                prefix = text[..comma].Trim();
                vendor = text[(comma + 1)..].Trim().Trim('"');
            }
            else
            {
                int space = text.IndexOfAny(new[] { ' ', '\t' });
                if (space > 0)
                {
                    prefix = text[..space].Trim();
                    vendor = text[(space + 1)..].Trim();
                }
            }
        }
        prefix = prefix.Replace("-", "").Replace(":", "").Trim();
        if (prefix.Length < 6 || vendor.Length == 0)
        {
            return 0;
        }
        target[prefix[..6]] = vendor;
        return 1;
    }

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>在 OUI 按钮右侧显示当前 OUI 操作与结果。</summary>
    private void SetOuiStatus(string text, Color? color = null)
    {
        void Apply()
        {
            if (_disposed || IsDisposed)
            {
                return;
            }
            _ouiStatusLabel.Text = text;
            _ouiStatusLabel.ForeColor = color ?? ShutdownTheme.Muted;
            _ouiStatusTip.SetToolTip(_ouiStatusLabel, text);
        }
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(Apply)); } catch { }
            return;
        }
        Apply();
    }

    private void ShowOuiInfo()
    {
        string cacheState = File.Exists(_ouiCachePath) ? "存在" : "不存在";
        MessageBox.Show(FindForm(),
            "OUI 数据库信息\n\n"
            + "当前来源：" + (_ouiSource.Length == 0 ? "内置常见厂商表" : _ouiSource) + "\n"
            + "当前 SHA256：" + (_ouiHash.Length == 0 ? "—" : _ouiHash) + "\n"
            + "内置版本：" + EmbeddedOuiSha256 + "\n"
            + "厂商条目：" + _oui.Count + " 条\n"
            + "本地缓存：" + _ouiCachePath + "（" + cacheState + "）",
            "WirelessNetworkWatcher - OUI 数据库", MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private string _lastOuiPath = "";

    private void LoadState()
    {
        _statePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiniMonitor", "WirelessNetworkWatcher", "devices.json");
        _loadingState = true;
        bool migrated = false;
        try
        {
            if (File.Exists(_statePath))
            {
                var state = JsonSerializer.Deserialize<PersistentState>(
                    File.ReadAllText(_statePath));
                if (state != null)
                {
                    // 旧版本默认关闭自动扫描；升级后默认开启并改为 60 秒一次。
                    if (state.Version < 1)
                    {
                        state.AutoScan = true;
                        state.IntervalSeconds = 60;
                        state.Version = 1;
                        migrated = true;
                    }
                    foreach (var pair in state.Names)
                    {
                        _customNames[pair.Key] = pair.Value;
                    }
                    _autoScan.Checked = state.AutoScan;
                    _interval.Value = Math.Clamp(state.IntervalSeconds, 10, 3600);
                    _resolveHosts.Checked = state.ResolveHosts;
                    _onlineOnly.Checked = state.OnlineOnly;
                    _beepOnNew.Checked = state.BeepOnNew;
                    _lastOuiCheckDate = state.LastOuiCheck;
                    _lastOuiHash = state.LastOuiHash;
                    _lastOuiSucceeded = state.LastOuiSucceeded;
                    if (!string.IsNullOrWhiteSpace(state.SelectedAdapter))
                    {
                        int adapterIndex = FindAdapterIndex(state.SelectedAdapter);
                        if (adapterIndex >= 0)
                        {
                            _adapterCombo.SelectedIndex = adapterIndex;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(state.OuiPath)
                        && File.Exists(state.OuiPath))
                    {
                        LoadOuiFile(state.OuiPath, out _);
                    }
                }
            }
            else
            {
                _autoScan.Checked = true;
                _interval.Value = 60;
                _resolveHosts.Checked = true;
                _onlineOnly.Checked = false;
                _beepOnNew.Checked = true;
            }
        }
        catch
        {
            // 读取失败时使用默认设置。
        }
        finally
        {
            _loadingState = false;
        }
        _autoTimer.Interval = (int)_interval.Value * 1000;
        if (_lastOuiCheckDate.Date >= DateTime.Today)
        {
            ShowTodayOuiStatus();
        }
        if (migrated)
        {
            SaveState();
        }
    }

    private void SaveState()
    {
        if (_loadingState || _statePath.Length == 0)
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath) ?? "");
            var state = new PersistentState
            {
                Version = 1,
                Names = new Dictionary<string, string>(_customNames, StringComparer.OrdinalIgnoreCase),
                AutoScan = _autoScan.Checked,
                IntervalSeconds = (int)_interval.Value,
                ResolveHosts = _resolveHosts.Checked,
                OnlineOnly = _onlineOnly.Checked,
                BeepOnNew = _beepOnNew.Checked,
                OuiPath = _lastOuiPath,
                SelectedAdapter = _adapterCombo.SelectedItem?.ToString() ?? "",
                LastOuiCheck = _lastOuiCheckDate,
                LastOuiHash = _lastOuiHash,
                LastOuiSucceeded = _lastOuiSucceeded
            };
            File.WriteAllText(_statePath,
                JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
        }
        catch
        {
            // 保存失败不影响扫描。
        }
    }

    private static uint ToUInt(byte[] bytes) =>
        ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16)
        | ((uint)bytes[2] << 8) | bytes[3];

    private static IPAddress FromUInt(uint value) => new(new[]
    {
        (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value
    });

    private static int CountBits(uint value)
    {
        int count = 0;
        while (value != 0)
        {
            count += (int)(value & 1);
            value >>= 1;
        }
        return count;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            try { _scanCts?.Cancel(); } catch { }
            _scanCts?.Dispose();
            _autoTimer.Dispose();
            _progressTimer.Dispose();
            _titleFont.Dispose();
            _statusFont.Dispose();
            _smallFont.Dispose();
            _smallBoldFont.Dispose();
            _ouiStatusTip.Dispose();
            SaveState();
        }
        base.Dispose(disposing);
    }

    /// <summary>简单输入框，用于重命名设备。</summary>
    private static class Prompt
    {
        public static string? Show(IWin32Window? owner, string title, string label,
            string value)
        {
            using var form = new Form
            {
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                ClientSize = new Size(460, 160),
                BackColor = Color.White,
                ForeColor = ShutdownTheme.Text,
                Font = new Font("Microsoft YaHei UI", 9f)
            };
            var caption = new Label
            {
                Text = label,
                Location = new Point(18, 18),
                Size = new Size(424, 24),
                ForeColor = ShutdownTheme.Muted
            };
            var input = new TextBox
            {
                Text = value,
                Location = new Point(18, 48),
                Size = new Size(424, 28),
                BorderStyle = BorderStyle.FixedSingle
            };
            var ok = new Button
            {
                Text = "确定",
                DialogResult = DialogResult.OK,
                Location = new Point(270, 104),
                Size = new Size(84, 30)
            };
            var cancel = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(360, 104),
                Size = new Size(84, 30)
            };
            form.Controls.AddRange(new Control[] { caption, input, ok, cancel });
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            form.Shown += (_, _) =>
            {
                input.Focus();
                input.SelectAll();
            };
            return form.ShowDialog(owner) == DialogResult.OK ? input.Text.Trim() : null;
        }
    }
}
