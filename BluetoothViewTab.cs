using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// BluetoothView 风格选项卡：复刻 NirSoft BluetoothView 的蓝牙设备监视功能。
/// 仅在切换到该选项卡时扫描，离开立即停止；默认每 60 秒自动扫描一次。
/// 新设备蓝色、二次发现绿色、离线灰色；OUI 厂商数据复用 WirelessNetworkWatcher。
/// </summary>
public sealed class BluetoothViewTab : UserControl
{
    private const int ColName = 0;
    private const int ColAddress = 1;
    private const int ColVendor = 2;
    private const int ColMajor = 3;
    private const int ColMinor = 4;
    private const int ColFirst = 5;
    private const int ColLast = 6;
    private const int ColState = 7;
    private const string AllRadios = "全部蓝牙适配器";

    private sealed class DeviceEntry
    {
        public string Address = "";
        public string Name = "";
        public string Vendor = "";
        public string Radio = "";
        public int ClassOfDevice;
        public string MajorType = "";
        public string MinorType = "";
        public DateTime FirstSeen;
        public DateTime LastSeen;
        public bool Online;
        public bool IsNew;
        public bool Connected;
        public bool Remembered;
        public bool Authenticated;
        public int DetectCount;
    }

    private sealed class RadioOption
    {
        public string Name = "";
        public string Address = "";

        public override string ToString() =>
            Name + (Address.Length == 0 ? "" : "  [" + Address + "]");
    }

    private sealed class PersistentState
    {
        public int Version { get; set; }
        public bool AutoScan { get; set; } = true;
        public int IntervalSeconds { get; set; } = 60;
        public bool OnlineOnly { get; set; }
        public bool BeepOnNew { get; set; } = true;
        public string SelectedRadio { get; set; } = "";
        public List<PersistentDevice> Devices { get; set; } = new();
    }

    private sealed class PersistentDevice
    {
        public string Address { get; set; } = "";
        public string Name { get; set; } = "";
        public int ClassOfDevice { get; set; }
        public string Radio { get; set; } = "";
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public int DetectCount { get; set; }
    }

    private readonly AppConfig _config;
    private readonly WirelessNetworkWatcherTab _ouiSource;
    private readonly Panel _top;
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly Label _bottomStatus;
    private readonly Label _ouiStatusLabel;
    private readonly ListView _list;
    private readonly PillButton _startButton;
    private readonly PillButton _stopButton;
    private readonly PillButton _refreshButton;
    private readonly PillButton _exportButton;
    private readonly PillButton _clearButton;
    private readonly PillButton _radioRefreshButton;
    private readonly CheckBox _autoScan;
    private readonly NumericUpDown _interval;
    private readonly CheckBox _onlineOnly;
    private readonly CheckBox _beepOnNew;
    private readonly ComboBox _radioCombo;
    private readonly TextBox _filterBox;
    private readonly ProgressBar _progress;
    private readonly ToolTip _ouiStatusTip;
    private readonly System.Windows.Forms.Timer _autoTimer;
    private readonly Font _titleFont;
    private readonly Font _statusFont;
    private readonly Font _smallFont;
    private readonly Font _smallBoldFont;
    private readonly List<DeviceEntry> _devices = new();
    private readonly object _deviceLock = new();
    private readonly int[] _columnWidths = new int[8];
    private readonly EventHandler _ouiChangedHandler;

    private CancellationTokenSource? _scanCts;
    private bool _scanning;
    private bool _isActive;
    private bool _pendingActivationScan;
    private bool _disposed;
    private bool _loadingState;
    private DateTime _lastScan;
    private double _lastScanSeconds;
    private int _sortColumn = ColAddress;
    private bool _sortAscending = true;
    private bool _markOddEven;
    private string _statePath = "";
    private string _savedRadioAddress = "";

    public BluetoothViewTab(AppConfig config, WirelessNetworkWatcherTab ouiSource)
    {
        _config = config;
        _ouiSource = ouiSource;
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
            Text = "BluetoothView（蓝牙设备扫描）",
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
            WrapContents = true,
            BackColor = Color.White,
            Padding = new Padding(0, 5, 0, 0),
            Margin = new Padding(0)
        };
        _autoScan = NewCheck("自动扫描", 72);
        _autoScan.Checked = true;
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
        _onlineOnly = NewCheck("仅显示在线", 88);
        _beepOnNew = NewCheck("新设备提示音", 100);
        _beepOnNew.Checked = true;
        _ouiStatusLabel = new Label
        {
            Text = "OUI：复用 WirelessNetworkWatcher…",
            AutoSize = true,
            MinimumSize = new Size(0, 24),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Font = _smallFont,
            Cursor = Cursors.Hand,
            Margin = new Padding(8, 2, 0, 0)
        };
        _ouiStatusLabel.Click += (_, _) => ShowOuiInfo();
        _ouiStatusTip = new ToolTip();
        optionsBar.Controls.AddRange(new Control[]
        {
            _autoScan, _interval, NewOptionLabel("秒", 22), _onlineOnly,
            _beepOnNew, _ouiStatusLabel
        });
        topLayout.Controls.Add(optionsBar, 0, 2);

        var radioBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.White,
            Padding = new Padding(0, 2, 0, 0),
            Margin = new Padding(0)
        };
        var radioLabel = new Label
        {
            Text = "扫描蓝牙适配器：",
            AutoSize = false,
            Width = 112,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Font = _smallFont,
            Margin = new Padding(0, 1, 4, 0)
        };
        _radioCombo = new ComboBox
        {
            Width = 260,
            Height = 26,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = _smallFont,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            Margin = new Padding(0, 1, 8, 0)
        };
        _radioCombo.Items.Add(AllRadios);
        _radioCombo.SelectedIndex = 0;
        _radioRefreshButton = NewButton("刷新适配器", 92);
        _radioRefreshButton.Height = 26;
        _radioRefreshButton.Click += (_, _) => RefreshRadios();
        var filterLabel = NewOptionLabel("筛选：", 42);
        _filterBox = new TextBox
        {
            Width = 170,
            Height = 26,
            Font = _smallFont,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "设备名 / 地址 / 厂商…",
            Margin = new Padding(0, 1, 8, 0)
        };
        var scanHint = new Label
        {
            Text = "仅在本页扫描，离开即停止",
            AutoSize = true,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Font = _smallFont,
            Margin = new Padding(6, 3, 0, 0)
        };
        radioBar.Controls.AddRange(new Control[]
        {
            radioLabel, _radioCombo, _radioRefreshButton, filterLabel, _filterBox, scanHint
        });
        topLayout.Controls.Add(radioBar, 0, 3);
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
        _list.Columns.Add("设备名", 170);
        _list.Columns.Add("蓝牙地址", 150);
        _list.Columns.Add("制造商", 180);
        _list.Columns.Add("主设备类型", 120);
        _list.Columns.Add("次设备类型", 130);
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
        _list.DoubleClick += (_, _) => ShowDetails();
        _list.ContextMenuStrip = BuildMenu();

        _progress = new ProgressBar
        {
            Dock = DockStyle.Bottom,
            Height = 14,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 35,
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

        _statePath = Path.Combine(AppPaths.Sub("BluetoothView"), "devices.json");
        LoadState();
        WireOptionEvents();

        _ouiChangedHandler = (_, _) => OnOuiDatabaseChanged();
        _ouiSource.OuiDatabaseChanged += _ouiChangedHandler;
        UpdateOuiStatus();
        RefreshList();

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
        menu.Items.Add("设备详情…", null, (_, _) => ShowDetails());
        menu.Items.Add("复制选中", null, (_, _) => CopySelected());
        menu.Items.Add("复制全部", null, (_, _) => CopyAll());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("导出 CSV…", null, (_, _) => Export("csv"));
        menu.Items.Add("导出 TXT…", null, (_, _) => Export("txt"));
        menu.Items.Add("导出 HTML…", null, (_, _) => Export("html"));
        menu.Items.Add("导出 XML…", null, (_, _) => Export("xml"));
        var exportSelected = new ToolStripMenuItem("导出选中");
        exportSelected.DropDownItems.Add("CSV 文件…", null, (_, _) => Export("csv", true));
        exportSelected.DropDownItems.Add("TXT 文件…", null, (_, _) => Export("txt", true));
        exportSelected.DropDownItems.Add("HTML 文件…", null, (_, _) => Export("html", true));
        exportSelected.DropDownItems.Add("XML 文件…", null, (_, _) => Export("xml", true));
        menu.Items.Add(exportSelected);
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
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("清空列表", null, (_, _) => ClearDevices());
        menu.Opening += (_, _) =>
        {
            bool has = _list.SelectedItems.Count > 0;
            foreach (ToolStripItem item in menu.Items)
            {
                if (item is ToolStripMenuItem entry
                    && (entry.Text?.StartsWith("设备详情") == true
                        || entry.Text?.StartsWith("复制选中") == true
                        || entry.Text?.StartsWith("导出选中") == true))
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

    private void RefreshRadios()
    {
        string current = (_radioCombo.SelectedItem as RadioOption)?.Address
            ?? _savedRadioAddress;
        bool oldLoading = _loadingState;
        _loadingState = true;
        try
        {
            _radioCombo.BeginUpdate();
            try
            {
                _radioCombo.Items.Clear();
                _radioCombo.Items.Add(AllRadios);
                List<BluetoothRadioRecord> radios;
                try
                {
                    radios = BluetoothReader.GetRadios();
                }
                catch
                {
                    radios = new List<BluetoothRadioRecord>();
                }
                foreach (var radio in radios)
                {
                    _radioCombo.Items.Add(new RadioOption
                    {
                        Name = radio.Name,
                        Address = radio.Address
                    });
                }
            }
            finally
            {
                _radioCombo.EndUpdate();
            }
            int index = 0;
            if (current.Length > 0)
            {
                for (int i = 1; i < _radioCombo.Items.Count; i++)
                {
                    if (_radioCombo.Items[i] is RadioOption option
                        && string.Equals(option.Address, current,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }
            }
            _radioCombo.SelectedIndex = index;
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
        _radioCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_loadingState)
            {
                return;
            }
            _savedRadioAddress = (_radioCombo.SelectedItem as RadioOption)?.Address ?? "";
            SaveState();
            if (_isActive && _autoScan.Checked && !_scanning)
            {
                _ = ScanAsync();
            }
        };
        _filterBox.TextChanged += (_, _) => RefreshList();
    }

    private void UpdateOuiStatus()
    {
        if (_disposed || IsDisposed)
        {
            return;
        }
        int count = _ouiSource.OuiEntryCount;
        _ouiStatusLabel.Text = "OUI：复用 WirelessNetworkWatcher（" + count + " 条厂商记录）";
        _ouiStatusLabel.ForeColor = count > 0 ? ShutdownTheme.Muted : ShutdownTheme.Danger;
        _ouiStatusTip.SetToolTip(_ouiStatusLabel,
            "蓝牙地址与 MAC 地址使用同一套 IEEE OUI 厂商库；"
            + "在 WirelessNetworkWatcher 选项卡更新或手动加载 OUI 后，这里会自动刷新。");
    }

    private void ShowOuiInfo()
    {
        MessageBox.Show(FindForm(),
            "OUI 厂商数据库复用说明\n\n"
            + "当前来源：" + _ouiSource.OuiSourceText + "\n"
            + "厂商条目：" + _ouiSource.OuiEntryCount + " 条\n\n"
            + "BluetoothView 与 WirelessNetworkWatcher 共享同一份 IEEE OUI 数据。\n"
            + "如需在线更新或手动加载 OUI，请切换到 WirelessNetworkWatcher 选项卡，"
            + "使用“OUI 数据库…”或“手动获取 OUI”按钮。",
            "BluetoothView - OUI 数据库", MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OnOuiDatabaseChanged()
    {
        if (_disposed || IsDisposed)
        {
            return;
        }
        lock (_deviceLock)
        {
            foreach (var device in _devices)
            {
                device.Vendor = _ouiSource.LookupVendor(device.Address);
            }
        }
        UpdateOuiStatus();
        RefreshList();
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
            RefreshRadios();
            UpdateOuiStatus();
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
        if (!BluetoothReader.IsSupported)
        {
            _statusLabel.Text = "● 仅支持 Windows 系统";
            _statusLabel.ForeColor = ShutdownTheme.Danger;
            return;
        }
        _scanning = true;
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;
        _startButton.Enabled = false;
        _stopButton.Enabled = true;
        _refreshButton.Enabled = false;
        _radioRefreshButton.Enabled = false;
        _statusLabel.Text = "● 正在扫描蓝牙设备…";
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

            string? radioAddress = (_radioCombo.SelectedItem as RadioOption)?.Address;
            _progress.Visible = true;
            var result = await Task.Run(
                () => BluetoothReader.Scan(radioAddress, 4, token), token)
                .ConfigureAwait(true);

            if (token.IsCancellationRequested || _disposed || IsDisposed)
            {
                return;
            }

            foreach (var record in result.Devices)
            {
                if (RecordDevice(record))
                {
                    newCount++;
                }
            }
            int seenCount = result.Devices.Count(record => record.SeenInThisScan);

            _lastScan = DateTime.Now;
            _lastScanSeconds = stopwatch.Elapsed.TotalSeconds;
            RefreshList();
            if (newCount > 0 && _beepOnNew.Checked)
            {
                try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
            }
            if (result.RadioCount == 0)
            {
                _statusLabel.Text = "● 未检测到蓝牙适配器（请确认蓝牙已开启）";
                _statusLabel.ForeColor = ShutdownTheme.Danger;
            }
            else if (seenCount == 0)
            {
                _statusLabel.Text = "● 扫描完成 · 未发现蓝牙设备";
                _statusLabel.ForeColor = ShutdownTheme.Success;
            }
            else
            {
                _statusLabel.Text = "● 扫描完成 · 发现 " + seenCount + " 台设备"
                    + (newCount > 0 ? " · 新增 " + newCount + " 台" : "");
                _statusLabel.ForeColor = newCount > 0
                    ? ShutdownTheme.Accent : ShutdownTheme.Success;
            }
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
            Debug.WriteLine("BluetoothView 扫描失败：" + ex);
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
                _progress.Visible = false;
                _startButton.Enabled = true;
                _stopButton.Enabled = false;
                _refreshButton.Enabled = true;
                _radioRefreshButton.Enabled = true;
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

    private bool RecordDevice(BluetoothDeviceRecord record)
    {
        lock (_deviceLock)
        {
            var device = _devices.FirstOrDefault(entry =>
                string.Equals(entry.Address, record.Address,
                    StringComparison.OrdinalIgnoreCase));
            // 已配对但不在范围内的设备不加入列表，避免出现从未发现过的灰色条目。
            if (device == null && !record.SeenInThisScan)
            {
                return false;
            }
            bool isNew = device == null;
            if (device == null)
            {
                device = new DeviceEntry
                {
                    Address = record.Address,
                    FirstSeen = DateTime.Now
                };
                _devices.Add(device);
            }
            if (!string.IsNullOrWhiteSpace(record.Name))
            {
                device.Name = record.Name;
            }
            device.ClassOfDevice = record.ClassOfDevice;
            var (major, minor) = BluetoothReader.DecodeClassOfDevice(record.ClassOfDevice);
            device.MajorType = major;
            device.MinorType = minor;
            device.Radio = record.RadioName;
            device.Connected = record.Connected;
            device.Remembered = record.Remembered;
            device.Authenticated = record.Authenticated;
            if (record.SeenInThisScan)
            {
                device.Online = true;
                device.LastSeen = DateTime.Now;
                device.DetectCount++;
                if (isNew)
                {
                    device.IsNew = true;
                }
            }
            else
            {
                device.Online = false;
            }
            device.Vendor = _ouiSource.LookupVendor(device.Address);
            return isNew && record.SeenInThisScan;
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
            .Select(entry => entry!.Address)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<DeviceEntry> devices;
        lock (_deviceLock)
        {
            devices = _devices.ToList();
        }
        if (_onlineOnly.Checked)
        {
            devices = devices.Where(device => device.Online).ToList();
        }
        string filter = _filterBox.Text.Trim();
        if (filter.Length > 0)
        {
            devices = devices.Where(device =>
                device.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || device.Address.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || device.Vendor.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || device.MajorType.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || device.MinorType.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || device.Radio.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        devices.Sort(CompareDevices);
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            int rowIndex = 0;
            foreach (var device in devices)
            {
                var item = new ListViewItem(string.IsNullOrWhiteSpace(device.Name)
                    ? "未知设备" : device.Name) { Tag = device };
                item.SubItems.Add(device.Address);
                item.SubItems.Add(device.Vendor);
                item.SubItems.Add(device.MajorType);
                item.SubItems.Add(device.MinorType);
                item.SubItems.Add(device.FirstSeen == DateTime.MinValue ? ""
                    : device.FirstSeen.ToString("yyyy-MM-dd HH:mm:ss"));
                item.SubItems.Add(device.LastSeen == DateTime.MinValue ? ""
                    : device.LastSeen.ToString("yyyy-MM-dd HH:mm:ss"));
                item.SubItems.Add(StateText(device));
                if (_markOddEven && rowIndex % 2 == 1)
                {
                    item.BackColor = Color.FromArgb(248, 250, 252);
                }
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
                    // 二次发现且在线：墨绿色。
                    item.ForeColor = Color.FromArgb(6, 95, 70);
                }
                item.ToolTipText = (string.IsNullOrWhiteSpace(device.Name)
                        ? "未知设备" : device.Name)
                    + "\n" + device.Address
                    + "\n厂商：" + (string.IsNullOrWhiteSpace(device.Vendor)
                        ? "未知" : device.Vendor)
                    + "\n类型：" + device.MajorType + " / " + device.MinorType
                    + "\n适配器：" + device.Radio
                    + "\n识别次数：" + device.DetectCount;
                if (selected.Contains(device.Address))
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

    private static string StateText(DeviceEntry device) =>
        device.IsNew ? "新增" : device.Connected ? "已连接" : device.Online ? "在线" : "离线";

    private int CompareDevices(DeviceEntry left, DeviceEntry right)
    {
        int result = _sortColumn switch
        {
            ColName => string.Compare(left.Name, right.Name,
                StringComparison.CurrentCultureIgnoreCase),
            ColAddress => CompareAddress(left.Address, right.Address),
            ColVendor => string.Compare(left.Vendor, right.Vendor,
                StringComparison.CurrentCulture),
            ColMajor => string.Compare(left.MajorType, right.MajorType,
                StringComparison.CurrentCulture),
            ColMinor => string.Compare(left.MinorType, right.MinorType,
                StringComparison.CurrentCulture),
            ColFirst => left.FirstSeen.CompareTo(right.FirstSeen),
            ColLast => left.LastSeen.CompareTo(right.LastSeen),
            ColState => StateRank(left).CompareTo(StateRank(right)),
            _ => CompareAddress(left.Address, right.Address)
        };
        if (result == 0)
        {
            result = CompareAddress(left.Address, right.Address);
        }
        return _sortAscending ? result : -result;
    }

    private static int StateRank(DeviceEntry device) =>
        device.IsNew ? 0 : device.Connected ? 1 : device.Online ? 2 : 3;

    private static int CompareAddress(string left, string right) =>
        string.Compare(left.Replace(":", ""), right.Replace(":", ""),
            StringComparison.OrdinalIgnoreCase);

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
            + "  ·  共 " + _devices.Count + " 台设备"
            + (_list.Items.Count != _devices.Count
                ? "（当前显示 " + _list.Items.Count + " 台）" : "");
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

    private void ShowDetails()
    {
        if (_list.SelectedItems.Count == 0)
        {
            return;
        }
        if (_list.SelectedItems[0].Tag is not DeviceEntry device)
        {
            return;
        }
        MessageBox.Show(FindForm(),
            "设备名：" + (string.IsNullOrWhiteSpace(device.Name)
                ? "（未广播名称）" : device.Name) + "\n"
            + "蓝牙地址：" + device.Address + "\n"
            + "制造商：" + (string.IsNullOrWhiteSpace(device.Vendor)
                ? "未知" : device.Vendor) + "\n"
            + "主设备类型：" + device.MajorType + "\n"
            + "次设备类型：" + device.MinorType + "\n"
            + "Class of Device：0x" + device.ClassOfDevice.ToString("X6") + "\n\n"
            + "首次发现：" + FormatTime(device.FirstSeen) + "\n"
            + "最后发现：" + FormatTime(device.LastSeen) + "\n"
            + "识别次数：" + device.DetectCount + "\n"
            + "连接状态：" + (device.Connected ? "已连接" : "未连接") + "\n"
            + "配对状态：" + (device.Remembered ? "已配对 / 已记忆" : "未配对") + "\n"
            + "认证状态：" + (device.Authenticated ? "已认证" : "未认证") + "\n"
            + "扫描适配器：" + (string.IsNullOrWhiteSpace(device.Radio)
                ? AllRadios : device.Radio),
            "BluetoothView - 设备详情", MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static string FormatTime(DateTime value) =>
        value == DateTime.MinValue ? "—" : value.ToString("yyyy-MM-dd HH:mm:ss");

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

    private void Export(string format, bool selectedOnly = false)
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
        List<DeviceEntry> devices;
        if (selectedOnly)
        {
            devices = _list.SelectedItems.Cast<ListViewItem>()
                .Select(item => item.Tag as DeviceEntry)
                .Where(entry => entry != null)
                .Select(entry => entry!)
                .ToList();
            if (devices.Count == 0)
            {
                return;
            }
        }
        else
        {
            lock (_deviceLock)
            {
                devices = _devices.ToList();
            }
        }
        using var dialog = new SaveFileDialog
        {
            Title = selectedOnly
                ? "导出 BluetoothView 选中设备" : "导出 BluetoothView 设备列表",
            Filter = filter,
            FileName = "BluetoothView_"
                + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "." + extension
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }
        try
        {
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
                + "\n\n是否打开所在目录？", "BluetoothView",
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
                "BluetoothView", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string[] ExportHeaders() => new[]
    {
        "设备名", "蓝牙地址", "制造商", "主设备类型", "次设备类型",
        "首次发现", "最后发现", "状态"
    };

    private static string[] ExportRow(DeviceEntry device) => new[]
    {
        device.Name,
        device.Address,
        device.Vendor,
        device.MajorType,
        device.MinorType,
        device.FirstSeen == DateTime.MinValue ? ""
            : device.FirstSeen.ToString("yyyy-MM-dd HH:mm:ss"),
        device.LastSeen == DateTime.MinValue ? ""
            : device.LastSeen.ToString("yyyy-MM-dd HH:mm:ss"),
        StateText(device)
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
        builder.AppendLine("<title>BluetoothView</title><style>");
        builder.AppendLine("body{font-family:'Microsoft YaHei UI',Arial,sans-serif;background:#f8fafc;color:#1e293b;padding:20px}");
        builder.AppendLine("h1{font-size:20px}table{border-collapse:collapse;width:100%;background:#fff}");
        builder.AppendLine("th,td{border:1px solid #cbd5e1;padding:6px 9px;font-size:13px;text-align:left}");
        builder.AppendLine("th{background:#e2e8f0}tr:nth-child(even) td{background:#f8fafc}");
        builder.AppendLine("</style></head><body><h1>BluetoothView</h1>");
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
        builder.AppendLine("<bluetoothView exported=\""
            + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") + "\">");
        foreach (var device in devices)
        {
            builder.AppendLine("  <device>");
            AppendXml(builder, "name", device.Name);
            AppendXml(builder, "bluetoothAddress", device.Address);
            AppendXml(builder, "vendor", device.Vendor);
            AppendXml(builder, "majorDeviceType", device.MajorType);
            AppendXml(builder, "minorDeviceType", device.MinorType);
            AppendXml(builder, "firstDetected", device.FirstSeen == DateTime.MinValue
                ? "" : device.FirstSeen.ToString("yyyy-MM-ddTHH:mm:ss"));
            AppendXml(builder, "lastDetected", device.LastSeen == DateTime.MinValue
                ? "" : device.LastSeen.ToString("yyyy-MM-ddTHH:mm:ss"));
            AppendXml(builder, "state", device.IsNew ? "new"
                : device.Connected ? "connected" : device.Online ? "online" : "offline");
            builder.AppendLine("  </device>");
        }
        builder.AppendLine("</bluetoothView>");
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

    // ================= 持久化 =================

    private void LoadState()
    {
        _loadingState = true;
        try
        {
            if (File.Exists(_statePath))
            {
                var state = JsonSerializer.Deserialize<PersistentState>(
                    File.ReadAllText(_statePath));
                if (state != null)
                {
                    if (state.Version < 1)
                    {
                        state.AutoScan = true;
                        state.IntervalSeconds = 60;
                        state.Version = 1;
                    }
                    _autoScan.Checked = state.AutoScan;
                    _interval.Value = Math.Clamp(state.IntervalSeconds, 10, 3600);
                    _onlineOnly.Checked = state.OnlineOnly;
                    _beepOnNew.Checked = state.BeepOnNew;
                    _savedRadioAddress = state.SelectedRadio ?? "";
                    state.Devices ??= new List<PersistentDevice>();
                    foreach (var saved in state.Devices)
                    {
                        if (string.IsNullOrWhiteSpace(saved.Address))
                        {
                            continue;
                        }
                        var (major, minor) = BluetoothReader.DecodeClassOfDevice(
                            saved.ClassOfDevice);
                        _devices.Add(new DeviceEntry
                        {
                            Address = saved.Address,
                            Name = saved.Name ?? "",
                            Vendor = _ouiSource.LookupVendor(saved.Address),
                            Radio = saved.Radio ?? "",
                            ClassOfDevice = saved.ClassOfDevice,
                            MajorType = major,
                            MinorType = minor,
                            FirstSeen = saved.FirstSeen,
                            LastSeen = saved.LastSeen,
                            DetectCount = saved.DetectCount,
                            Online = false,
                            IsNew = false
                        });
                    }
                }
            }
            else
            {
                _autoScan.Checked = true;
                _interval.Value = 60;
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
            List<PersistentDevice> devices;
            lock (_deviceLock)
            {
                devices = _devices.Select(device => new PersistentDevice
                {
                    Address = device.Address,
                    Name = device.Name,
                    ClassOfDevice = device.ClassOfDevice,
                    Radio = device.Radio,
                    FirstSeen = device.FirstSeen,
                    LastSeen = device.LastSeen,
                    DetectCount = device.DetectCount
                }).ToList();
            }
            var state = new PersistentState
            {
                Version = 1,
                AutoScan = _autoScan.Checked,
                IntervalSeconds = (int)_interval.Value,
                OnlineOnly = _onlineOnly.Checked,
                BeepOnNew = _beepOnNew.Checked,
                SelectedRadio = (_radioCombo.SelectedItem as RadioOption)?.Address
                    ?? _savedRadioAddress,
                Devices = devices
            };
            File.WriteAllText(_statePath,
                JsonSerializer.Serialize(state, new JsonSerializerOptions
                {
                    WriteIndented = true
                }),
                new UTF8Encoding(false));
        }
        catch
        {
            // 保存失败不影响扫描。
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            try { _scanCts?.Cancel(); } catch { }
            _scanCts?.Dispose();
            _autoTimer.Dispose();
            _titleFont.Dispose();
            _statusFont.Dispose();
            _smallFont.Dispose();
            _smallBoldFont.Dispose();
            _ouiStatusTip.Dispose();
            try { _ouiSource.OuiDatabaseChanged -= _ouiChangedHandler; } catch { }
            SaveState();
        }
        base.Dispose(disposing);
    }
}
