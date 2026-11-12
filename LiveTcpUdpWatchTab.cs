using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// tcp_udp_Watch 风格选项卡：实时枚举本机所有 TCP / UDP 端点，
/// 显示协议、本地/远程地址与端口、连接状态、进程 ID、进程名与完整路径，
/// 支持自动刷新、筛选、排序、列显示与 CSV / TXT / HTML / XML 导出。
/// </summary>
public sealed class LiveTcpUdpWatchTab : UserControl
{
    private const int ColProtocol = 0;
    private const int ColLocalAddress = 1;
    private const int ColLocalPort = 2;
    private const int ColRemoteAddress = 3;
    private const int ColRemotePort = 4;
    private const int ColState = 5;
    private const int ColPid = 6;
    private const int ColProcess = 7;
    private const int ColPath = 8;
    private const int ColFirstSeen = 9;
    private const int ColLastSeen = 10;

    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int TcpTableOwnerPidAll = 5;
    private const int UdpTableOwnerPid = 1;
    private const int ErrorInsufficientBuffer = 122;

    private sealed class EndpointEntry
    {
        public string Protocol = "";
        public string Family = "";
        public string LocalAddress = "";
        public int LocalPort;
        public string RemoteAddress = "";
        public int RemotePort;
        public string State = "";
        public int Pid;
        public string ProcessName = "";
        public string ProcessPath = "";
        public DateTime FirstSeen;
        public DateTime LastSeen;
        public string Key = "";
        public bool IsNew;
    }

    private sealed class ProcessInfo
    {
        public string Name = "";
        public string Path = "";
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
    private readonly PillButton _clearButton;
    private readonly CheckBox _autoRefresh;
    private readonly CheckBox _establishedOnly;
    private readonly CheckBox _listeningOnly;
    private readonly CheckBox _hideLoopback;
    private readonly CheckBox _ipv4Only;
    private readonly CheckBox _ipv6Only;
    private readonly NumericUpDown _interval;
    private readonly ComboBox _protocolCombo;
    private readonly TextBox _filterBox;
    private readonly System.Windows.Forms.Timer _autoTimer;
    private readonly Font _titleFont;
    private readonly Font _statusFont;
    private readonly Font _smallFont;
    private readonly Font _smallBoldFont;
    private readonly object _dataLock = new();
    private readonly List<EndpointEntry> _entries = new();
    private readonly Dictionary<string, EndpointEntry> _history =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, ProcessInfo> _processCache = new();
    private readonly int[] _columnWidths = new int[11];

    private DateTime _processCacheTime = DateTime.MinValue;
    private CancellationTokenSource? _refreshCts;
    private bool _refreshing;
    private bool _isActive;
    private bool _disposed;
    private DateTime _lastRefresh;
    private double _lastRefreshMs;
    private int _sortColumn = ColProtocol;
    private bool _sortAscending = true;
    private bool _markOddEven;
    private bool _autoSizeColumns = true;
    private bool _columnsAutoSized;

    public LiveTcpUdpWatchTab(AppConfig config)
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
            Text = "tcp_udp_Watch（实时 TCP / UDP 连接监视）",
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
        _refreshButton = NewButton("立即刷新", 86, PillButtonStyle.Primary);
        _refreshButton.Click += (_, _) => _ = RefreshEndpointsAsync();
        _copyButton = NewButton("复制", 72);
        _copyButton.Click += (_, _) => CopySelected();
        _exportButton = NewButton("导出", 72);
        _exportButton.Click += (_, _) => ShowExportMenu();
        _clearButton = NewButton("清空", 72);
        _clearButton.Click += (_, _) => ClearEntries();
        buttonBar.Controls.AddRange(new Control[]
        {
            _refreshButton, _copyButton, _exportButton, _clearButton
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
        _autoRefresh = NewCheck("自动刷新", 80);
        _autoRefresh.Checked = true;
        _interval = new NumericUpDown
        {
            Width = 60,
            Height = 24,
            Minimum = 1,
            Maximum = 60,
            Value = 10,
            Font = _smallFont,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 1, 4, 0)
        };
        _protocolCombo = new ComboBox
        {
            Width = 90,
            Height = 26,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = _smallFont,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            Margin = new Padding(0, 1, 8, 0)
        };
        _protocolCombo.Items.AddRange(new object[] { "全部协议", "仅 TCP", "仅 UDP" });
        _protocolCombo.SelectedIndex = 1;
        _establishedOnly = NewCheck("仅已建立", 86);
        _establishedOnly.Checked = false;
        _listeningOnly = NewCheck("仅监听", 76);
        _listeningOnly.Checked = true;
        _hideLoopback = NewCheck("隐藏回环", 84);
        _hideLoopback.Checked = false;
        _ipv4Only = NewCheck("仅显示 IPv4", 100);
        _ipv4Only.Checked = true;
        _ipv6Only = NewCheck("仅显示 IPv6", 100);
        _ipv6Only.Checked = false;
        optionsBar.Controls.AddRange(new Control[]
        {
            _autoRefresh, _interval, NewOptionLabel("秒", 22), _protocolCombo,
            _establishedOnly, _listeningOnly, _ipv4Only, _ipv6Only, _hideLoopback
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
            PlaceholderText = "地址 / 端口 / 进程名 / 路径…",
            Margin = new Padding(0, 1, 10, 0)
        };
        var hint = new Label
        {
            Text = "仅在进入本页时刷新；离开页面立即停止",
            AutoSize = true,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Font = _smallFont,
            Margin = new Padding(4, 3, 0, 0)
        };
        filterBar.Controls.AddRange(new Control[] { _filterBox, hint });
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
        _list.Columns.Add("协议", 82);
        _list.Columns.Add("本地地址", 130);
        _list.Columns.Add("本地端口", 70);
        _list.Columns.Add("远程地址", 130);
        _list.Columns.Add("远程端口", 70);
        _list.Columns.Add("状态", 100);
        _list.Columns.Add("PID", 60);
        _list.Columns.Add("进程名", 130);
        _list.Columns.Add("进程完整路径", 300);
        _list.Columns.Add("首次发现", 140);
        _list.Columns.Add("最后刷新", 140);
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
            Text = "尚未刷新"
        };

        Controls.Add(_list);
        Controls.Add(_bottomStatus);
        Controls.Add(_top);
        Controls.SetChildIndex(_list, 0);
        Controls.SetChildIndex(_bottomStatus, 1);
        Controls.SetChildIndex(_top, 2);

        _autoTimer = new System.Windows.Forms.Timer { Interval = 10000 };
        _autoTimer.Tick += (_, _) =>
        {
            if (_isActive && _autoRefresh.Checked && !_refreshing)
            {
                _ = RefreshEndpointsAsync();
            }
        };

        WireOptionEvents();
        Disposed += (_, _) =>
        {
            _disposed = true;
            try { _refreshCts?.Cancel(); } catch { }
            _autoTimer.Stop();
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
        menu.Items.Add("复制选中", null, (_, _) => CopySelected());
        menu.Items.Add("复制全部", null, (_, _) => CopyAll());
        menu.Items.Add("复制进程路径", null, (_, _) => CopyProcessPath());
        menu.Items.Add("打开进程所在目录", null, (_, _) => OpenProcessFolder());
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
        var autoSize = new ToolStripMenuItem("自动调整列宽")
        {
            CheckOnClick = true,
            Checked = _autoSizeColumns
        };
        autoSize.CheckedChanged += (_, _) =>
        {
            _autoSizeColumns = autoSize.Checked;
            if (_autoSizeColumns)
            {
                AutoSizeColumns();
                _columnsAutoSized = true;
            }
        };
        menu.Items.Add(autoSize);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("清空列表", null, (_, _) => ClearEntries());
        menu.Opening += (_, _) =>
        {
            bool has = _list.SelectedItems.Count > 0;
            foreach (ToolStripItem item in menu.Items)
            {
                if (item is ToolStripMenuItem entry
                    && (entry.Text?.StartsWith("复制选中") == true
                        || entry.Text?.StartsWith("复制进程路径") == true
                        || entry.Text?.StartsWith("打开进程") == true))
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

    private void WireOptionEvents()
    {
        _autoRefresh.CheckedChanged += (_, _) =>
        {
            if (_autoRefresh.Checked)
            {
                _autoTimer.Interval = (int)_interval.Value * 1000;
                if (_isActive && !_refreshing)
                {
                    _autoTimer.Start();
                    _ = RefreshEndpointsAsync();
                }
            }
            else
            {
                _autoTimer.Stop();
            }
        };
        _interval.ValueChanged += (_, _) =>
        {
            if (_autoRefresh.Checked)
            {
                _autoTimer.Interval = (int)_interval.Value * 1000;
            }
        };
        _protocolCombo.SelectedIndexChanged += (_, _) => RefreshList();
        _establishedOnly.CheckedChanged += (_, _) => RefreshList();
        _listeningOnly.CheckedChanged += (_, _) => RefreshList();
        _hideLoopback.CheckedChanged += (_, _) => RefreshList();
        _ipv4Only.CheckedChanged += (_, _) =>
        {
            if (_ipv4Only.Checked)
            {
                _ipv6Only.Checked = false;
            }
            RefreshList();
        };
        _ipv6Only.CheckedChanged += (_, _) =>
        {
            if (_ipv6Only.Checked)
            {
                _ipv4Only.Checked = false;
            }
            RefreshList();
        };
        _filterBox.TextChanged += (_, _) => RefreshList();
    }

    // ================= 刷新 =================

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
            if (_autoRefresh.Checked)
            {
                _autoTimer.Interval = (int)_interval.Value * 1000;
                _autoTimer.Start();
            }
            _ = RefreshEndpointsAsync();
        }
        else
        {
            try { _refreshCts?.Cancel(); } catch { }
            _autoTimer.Stop();
            if (!IsDisposed)
            {
                _statusLabel.Text = "● 未启用（切换到该选项卡后自动刷新）";
                _statusLabel.ForeColor = ShutdownTheme.Muted;
            }
        }
    }

    private async Task RefreshEndpointsAsync()
    {
        if (_refreshing || _disposed || !_isActive)
        {
            return;
        }
        _refreshing = true;
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();
        var token = _refreshCts.Token;
        _statusLabel.Text = "● 正在刷新…";
        _statusLabel.ForeColor = ShutdownTheme.Accent;
        _refreshButton.Enabled = false;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var snapshot = await Task.Run(() => EnumerateEndpoints(token), token)
                .ConfigureAwait(true);
            if (token.IsCancellationRequested || _disposed || IsDisposed)
            {
                return;
            }
            DateTime now = DateTime.Now;
            int newCount = 0;
            lock (_dataLock)
            {
                var currentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in snapshot)
                {
                    if (_history.TryGetValue(entry.Key, out var previous))
                    {
                        entry.FirstSeen = previous.FirstSeen;
                        entry.IsNew = false;
                    }
                    else
                    {
                        entry.FirstSeen = now;
                        entry.IsNew = true;
                        newCount++;
                    }
                    entry.LastSeen = now;
                    _history[entry.Key] = entry;
                    currentKeys.Add(entry.Key);
                }
                _entries.Clear();
                _entries.AddRange(snapshot);
                // 5 分钟内消失的连接保留为“已断开”，方便观察连接生命周期。
                foreach (var key in _history.Keys.ToList())
                {
                    if (currentKeys.Contains(key))
                    {
                        continue;
                    }
                    var previous = _history[key];
                    if (now - previous.LastSeen > TimeSpan.FromMinutes(5))
                    {
                        _history.Remove(key);
                        continue;
                    }
                    _entries.Add(new EndpointEntry
                    {
                        Protocol = previous.Protocol,
                        Family = previous.Family,
                        LocalAddress = previous.LocalAddress,
                        LocalPort = previous.LocalPort,
                        RemoteAddress = previous.RemoteAddress,
                        RemotePort = previous.RemotePort,
                        State = "已断开",
                        Pid = previous.Pid,
                        ProcessName = previous.ProcessName,
                        ProcessPath = previous.ProcessPath,
                        FirstSeen = previous.FirstSeen,
                        LastSeen = now,
                        IsNew = false,
                        Key = key
                    });
                }
            }
            _lastRefresh = now;
            _lastRefreshMs = stopwatch.Elapsed.TotalMilliseconds;
            RefreshList();
            _statusLabel.Text = "● 刷新完成 · " + snapshot.Count + " 条连接"
                + (newCount > 0 ? " · 新增 " + newCount : "");
            _statusLabel.ForeColor = newCount > 0
                ? ShutdownTheme.Accent : ShutdownTheme.Success;
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && !IsDisposed)
            {
                _statusLabel.Text = _isActive ? "● 已停止" : "● 未启用";
                _statusLabel.ForeColor = ShutdownTheme.Muted;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("tcp_udp_Watch 刷新失败：" + ex);
            if (!_disposed && !IsDisposed)
            {
                _statusLabel.Text = "● 刷新失败：" + ex.Message;
                _statusLabel.ForeColor = ShutdownTheme.Danger;
            }
        }
        finally
        {
            _refreshing = false;
            if (!_disposed && !IsDisposed)
            {
                _refreshButton.Enabled = true;
                UpdateBottomStatus();
            }
        }
    }

    // ================= 枚举 =================

    private List<EndpointEntry> EnumerateEndpoints(CancellationToken token)
    {
        var list = new List<EndpointEntry>();
        var pids = new HashSet<int>();
        if (DateTime.Now - _processCacheTime > TimeSpan.FromSeconds(30))
        {
            _processCache.Clear();
            _processCacheTime = DateTime.Now;
        }
        foreach (int af in new[] { AfInet, AfInet6 })
        {
            if (token.IsCancellationRequested)
            {
                break;
            }
            var tcp = ReadTable(tcp: true, af);
            if (tcp != null)
            {
                ParseTcpTable(tcp, af, list, pids);
            }
            var udp = ReadTable(tcp: false, af);
            if (udp != null)
            {
                ParseUdpTable(udp, af, list, pids);
            }
        }
        foreach (int pid in pids)
        {
            if (_processCache.ContainsKey(pid))
            {
                continue;
            }
            _processCache[pid] = ResolveProcess(pid);
        }
        foreach (var entry in list)
        {
            if (_processCache.TryGetValue(entry.Pid, out var info))
            {
                entry.ProcessName = info.Name;
                entry.ProcessPath = info.Path;
            }
            entry.Key = string.Join("|", entry.Protocol, entry.Family,
                entry.LocalAddress, entry.LocalPort.ToString(),
                entry.RemoteAddress, entry.RemotePort.ToString(),
                entry.Pid.ToString());
        }
        return list;
    }

    private static byte[]? ReadTable(bool tcp, int family)
    {
        int size = 0;
        uint result = tcp
            ? GetExtendedTcpTable(IntPtr.Zero, ref size, false, family,
                TcpTableOwnerPidAll, 0)
            : GetExtendedUdpTable(IntPtr.Zero, ref size, false, family,
                UdpTableOwnerPid, 0);
        if (result != ErrorInsufficientBuffer && result != 0)
        {
            return null;
        }
        if (size <= 0)
        {
            return null;
        }
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            result = tcp
                ? GetExtendedTcpTable(buffer, ref size, false, family,
                    TcpTableOwnerPidAll, 0)
                : GetExtendedUdpTable(buffer, ref size, false, family,
                    UdpTableOwnerPid, 0);
            if (result != 0)
            {
                return null;
            }
            byte[] data = new byte[size];
            Marshal.Copy(buffer, data, 0, size);
            return data;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void ParseTcpTable(byte[] data, int family,
        List<EndpointEntry> list, HashSet<int> pids)
    {
        int count = BitConverter.ToInt32(data, 0);
        if (family == AfInet)
        {
            const int rowSize = 24;
            int offset = 4;
            for (int i = 0; i < count && offset + rowSize <= data.Length; i++, offset += rowSize)
            {
                uint state = BitConverter.ToUInt32(data, offset);
                uint local = BitConverter.ToUInt32(data, offset + 4);
                int localPort = ReadPort(BitConverter.ToUInt32(data, offset + 8));
                uint remote = BitConverter.ToUInt32(data, offset + 12);
                int remotePort = ReadPort(BitConverter.ToUInt32(data, offset + 16));
                int pid = BitConverter.ToInt32(data, offset + 20);
                list.Add(new EndpointEntry
                {
                    Protocol = "TCP",
                    Family = "IPv4",
                    LocalAddress = new IPAddress(BitConverter.GetBytes(local)).ToString(),
                    LocalPort = localPort,
                    RemoteAddress = new IPAddress(BitConverter.GetBytes(remote)).ToString(),
                    RemotePort = remotePort,
                    State = TcpStateName(state),
                    Pid = pid
                });
                pids.Add(pid);
            }
        }
        else
        {
            const int rowSize = 56;
            int offset = 4;
            for (int i = 0; i < count && offset + rowSize <= data.Length; i++, offset += rowSize)
            {
                byte[] local = new byte[16];
                Buffer.BlockCopy(data, offset, local, 0, 16);
                uint localScope = BitConverter.ToUInt32(data, offset + 16);
                int localPort = ReadPort(BitConverter.ToUInt32(data, offset + 20));
                byte[] remote = new byte[16];
                Buffer.BlockCopy(data, offset + 24, remote, 0, 16);
                uint remoteScope = BitConverter.ToUInt32(data, offset + 40);
                int remotePort = ReadPort(BitConverter.ToUInt32(data, offset + 44));
                uint state = BitConverter.ToUInt32(data, offset + 48);
                int pid = BitConverter.ToInt32(data, offset + 52);
                list.Add(new EndpointEntry
                {
                    Protocol = "TCP",
                    Family = "IPv6",
                    LocalAddress = FormatIpv6(local, localScope),
                    LocalPort = localPort,
                    RemoteAddress = FormatIpv6(remote, remoteScope),
                    RemotePort = remotePort,
                    State = TcpStateName(state),
                    Pid = pid
                });
                pids.Add(pid);
            }
        }
    }

    private static void ParseUdpTable(byte[] data, int family,
        List<EndpointEntry> list, HashSet<int> pids)
    {
        int count = BitConverter.ToInt32(data, 0);
        if (family == AfInet)
        {
            const int rowSize = 12;
            int offset = 4;
            for (int i = 0; i < count && offset + rowSize <= data.Length; i++, offset += rowSize)
            {
                uint local = BitConverter.ToUInt32(data, offset);
                int localPort = ReadPort(BitConverter.ToUInt32(data, offset + 4));
                int pid = BitConverter.ToInt32(data, offset + 8);
                list.Add(new EndpointEntry
                {
                    Protocol = "UDP",
                    Family = "IPv4",
                    LocalAddress = new IPAddress(BitConverter.GetBytes(local)).ToString(),
                    LocalPort = localPort,
                    RemoteAddress = "*",
                    RemotePort = 0,
                    State = "—",
                    Pid = pid
                });
                pids.Add(pid);
            }
        }
        else
        {
            const int rowSize = 28;
            int offset = 4;
            for (int i = 0; i < count && offset + rowSize <= data.Length; i++, offset += rowSize)
            {
                byte[] local = new byte[16];
                Buffer.BlockCopy(data, offset, local, 0, 16);
                uint scope = BitConverter.ToUInt32(data, offset + 16);
                int localPort = ReadPort(BitConverter.ToUInt32(data, offset + 20));
                int pid = BitConverter.ToInt32(data, offset + 24);
                list.Add(new EndpointEntry
                {
                    Protocol = "UDP",
                    Family = "IPv6",
                    LocalAddress = FormatIpv6(local, scope),
                    LocalPort = localPort,
                    RemoteAddress = "*",
                    RemotePort = 0,
                    State = "—",
                    Pid = pid
                });
                pids.Add(pid);
            }
        }
    }

    private static int ReadPort(uint raw)
    {
        int value = (int)(raw & 0xFFFF);
        return ((value & 0xFF) << 8) | ((value >> 8) & 0xFF);
    }

    private static string FormatIpv6(byte[] address, uint scope)
    {
        try
        {
            var ip = new IPAddress(address, scope);
            return ip.ToString();
        }
        catch
        {
            return new IPAddress(address).ToString();
        }
    }

    private static string TcpStateName(uint state) => state switch
    {
        1 => "CLOSED",
        2 => "LISTEN",
        3 => "SYN_SENT",
        4 => "SYN_RECEIVED",
        5 => "ESTABLISHED",
        6 => "FIN_WAIT1",
        7 => "FIN_WAIT2",
        8 => "CLOSE_WAIT",
        9 => "CLOSING",
        10 => "LAST_ACK",
        11 => "TIME_WAIT",
        12 => "DELETE_TCB",
        _ => "未知"
    };

    private static ProcessInfo ResolveProcess(int pid)
    {
        if (pid <= 0)
        {
            return new ProcessInfo { Name = "System Idle Process", Path = "" };
        }
        if (pid == 4)
        {
            return new ProcessInfo
            {
                Name = "System",
                Path = Path.Combine(Environment.SystemDirectory, "ntoskrnl.exe")
            };
        }
        string name = "";
        string path = "";
        try
        {
            using var process = Process.GetProcessById(pid);
            name = process.ProcessName;
        }
        catch
        {
            return new ProcessInfo { Name = "", Path = "" };
        }
        try
        {
            path = QueryProcessPath(pid);
        }
        catch
        {
        }
        return new ProcessInfo { Name = name, Path = path };
    }

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
            if (QueryFullProcessImageNameW(handle, 0, builder, ref size))
            {
                return builder.ToString(0, size);
            }
            return "";
        }
        finally
        {
            CloseHandle(handle);
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
            .Select(item => item.Tag as EndpointEntry)
            .Where(entry => entry != null)
            .Select(entry => entry!.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<EndpointEntry> entries;
        lock (_dataLock)
        {
            entries = _entries.ToList();
        }
        int protocol = _protocolCombo.SelectedIndex;
        if (protocol == 1)
        {
            entries = entries.Where(entry => entry.Protocol == "TCP").ToList();
        }
        else if (protocol == 2)
        {
            entries = entries.Where(entry => entry.Protocol == "UDP").ToList();
        }
        if (_establishedOnly.Checked || _listeningOnly.Checked)
        {
            entries = entries.Where(entry =>
                (_establishedOnly.Checked && entry.State == "ESTABLISHED")
                || (_listeningOnly.Checked && entry.State == "LISTEN")).ToList();
        }
        if (_ipv4Only.Checked)
        {
            entries = entries.Where(entry => entry.Family == "IPv4").ToList();
        }
        else if (_ipv6Only.Checked)
        {
            entries = entries.Where(entry => entry.Family == "IPv6").ToList();
        }
        if (_hideLoopback.Checked)
        {
            entries = entries.Where(entry =>
                !IsLoopback(entry.LocalAddress) && !IsLoopback(entry.RemoteAddress)).ToList();
        }
        string filter = _filterBox.Text.Trim();
        if (filter.Length > 0)
        {
            entries = entries.Where(entry =>
                entry.LocalAddress.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || entry.RemoteAddress.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || entry.LocalPort.ToString().Contains(filter, StringComparison.Ordinal)
                || entry.RemotePort.ToString().Contains(filter, StringComparison.Ordinal)
                || entry.State.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || entry.Pid.ToString().Contains(filter, StringComparison.Ordinal)
                || entry.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || entry.ProcessPath.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        entries.Sort(CompareEntries);
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            int rowIndex = 0;
            foreach (var entry in entries)
            {
                var item = new ListViewItem(entry.Protocol + " / " + entry.Family)
                {
                    Tag = entry
                };
                item.SubItems.Add(entry.LocalAddress);
                item.SubItems.Add(entry.LocalPort.ToString());
                item.SubItems.Add(entry.RemoteAddress);
                item.SubItems.Add(entry.RemotePort.ToString());
                item.SubItems.Add(entry.State);
                item.SubItems.Add(entry.Pid.ToString());
                item.SubItems.Add(entry.ProcessName);
                item.SubItems.Add(entry.ProcessPath);
                item.SubItems.Add(entry.FirstSeen == DateTime.MinValue ? ""
                    : entry.FirstSeen.ToString("yyyy-MM-dd HH:mm:ss"));
                item.SubItems.Add(entry.LastSeen == DateTime.MinValue ? ""
                    : entry.LastSeen.ToString("yyyy-MM-dd HH:mm:ss"));
                if (_markOddEven && rowIndex % 2 == 1)
                {
                    item.BackColor = Color.FromArgb(248, 250, 252);
                }
                if (entry.IsNew)
                {
                    item.ForeColor = ShutdownTheme.Accent;
                    item.Font = _smallBoldFont;
                }
                else if (entry.State == "ESTABLISHED")
                {
                    item.ForeColor = Color.FromArgb(6, 95, 70);
                }
                else if (entry.State == "LISTEN")
                {
                    item.ForeColor = Color.FromArgb(29, 78, 216);
                }
                else if (entry.State == "已断开"
                    || entry.State.StartsWith("TIME_WAIT", StringComparison.Ordinal)
                    || entry.State.StartsWith("CLOSE_WAIT", StringComparison.Ordinal)
                    || entry.State.StartsWith("FIN_WAIT", StringComparison.Ordinal))
                {
                    item.ForeColor = Color.FromArgb(148, 163, 184);
                }
                item.ToolTipText = entry.Protocol + " / " + entry.Family
                    + "\n本地：" + entry.LocalAddress + ":" + entry.LocalPort
                    + "\n远程：" + entry.RemoteAddress + ":" + entry.RemotePort
                    + "\n状态：" + entry.State
                    + "\nPID：" + entry.Pid
                    + "\n进程：" + (string.IsNullOrWhiteSpace(entry.ProcessPath)
                        ? entry.ProcessName : entry.ProcessPath);
                if (selected.Contains(entry.Key))
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
        if (_autoSizeColumns && !_columnsAutoSized && entries.Count > 0)
        {
            AutoSizeColumns();
            _columnsAutoSized = true;
        }
        UpdateBottomStatus();
    }

    private void AutoSizeColumns()
    {
        for (int i = 0; i < _list.Columns.Count; i++)
        {
            _list.AutoResizeColumn(i, ColumnHeaderAutoResizeStyle.HeaderSize);
            int headerWidth = _list.Columns[i].Width;
            _list.AutoResizeColumn(i, ColumnHeaderAutoResizeStyle.ColumnContent);
            _list.Columns[i].Width = Math.Max(headerWidth, _list.Columns[i].Width);
            _columnWidths[i] = _list.Columns[i].Width;
        }
    }

    private static bool IsLoopback(string address)
    {
        if (address == "*" || address.Length == 0)
        {
            return false;
        }
        if (!IPAddress.TryParse(address.Split('%')[0], out var ip))
        {
            return false;
        }
        return IPAddress.IsLoopback(ip);
    }

    private int CompareEntries(EndpointEntry left, EndpointEntry right)
    {
        int result = _sortColumn switch
        {
            ColProtocol => string.Compare(left.Protocol, right.Protocol,
                StringComparison.OrdinalIgnoreCase),
            ColLocalAddress => CompareIp(left.LocalAddress, right.LocalAddress),
            ColLocalPort => left.LocalPort.CompareTo(right.LocalPort),
            ColRemoteAddress => CompareIp(left.RemoteAddress, right.RemoteAddress),
            ColRemotePort => left.RemotePort.CompareTo(right.RemotePort),
            ColState => string.Compare(left.State, right.State,
                StringComparison.OrdinalIgnoreCase),
            ColPid => left.Pid.CompareTo(right.Pid),
            ColProcess => string.Compare(left.ProcessName, right.ProcessName,
                StringComparison.CurrentCultureIgnoreCase),
            ColPath => string.Compare(left.ProcessPath, right.ProcessPath,
                StringComparison.CurrentCultureIgnoreCase),
            ColFirstSeen => left.FirstSeen.CompareTo(right.FirstSeen),
            ColLastSeen => left.LastSeen.CompareTo(right.LastSeen),
            _ => 0
        };
        if (result == 0)
        {
            result = string.Compare(left.Key, right.Key,
                StringComparison.OrdinalIgnoreCase);
        }
        return _sortAscending ? result : -result;
    }

    private static int CompareIp(string left, string right)
    {
        bool leftOk = IPAddress.TryParse(left.Split('%')[0], out var leftIp);
        bool rightOk = IPAddress.TryParse(right.Split('%')[0], out var rightIp);
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
        int tcp;
        int udp;
        lock (_dataLock)
        {
            tcp = _entries.Count(entry => entry.Protocol == "TCP");
            udp = _entries.Count(entry => entry.Protocol == "UDP");
        }
        _bottomStatus.Text = "TCP " + tcp + " · UDP " + udp
            + " · 共 " + _entries.Count + " 条"
            + (_lastRefresh == DateTime.MinValue ? ""
                : "  ·  上次刷新 " + _lastRefresh.ToString("HH:mm:ss")
                  + "，用时 " + _lastRefreshMs.ToString("0") + " ms")
            + (_list.Items.Count != _entries.Count
                ? "（当前显示 " + _list.Items.Count + " 条）" : "");
    }

    public void ClearEntries()
    {
        lock (_dataLock)
        {
            _entries.Clear();
            _history.Clear();
        }
        _list.Items.Clear();
        _lastRefresh = DateTime.MinValue;
        _lastRefreshMs = 0;
        _statusLabel.Text = "● 已清空";
        _statusLabel.ForeColor = ShutdownTheme.Muted;
        UpdateBottomStatus();
    }

    private EndpointEntry? SelectedEntry()
    {
        if (_list.SelectedItems.Count == 0)
        {
            return null;
        }
        return _list.SelectedItems[0].Tag as EndpointEntry;
    }

    private void CopyProcessPath()
    {
        var entry = SelectedEntry();
        if (entry == null)
        {
            return;
        }
        string text = string.IsNullOrWhiteSpace(entry.ProcessPath)
            ? entry.ProcessName : entry.ProcessPath;
        if (text.Length == 0)
        {
            return;
        }
        try { Clipboard.SetText(text); } catch { }
    }

    private void OpenProcessFolder()
    {
        var entry = SelectedEntry();
        if (entry == null || string.IsNullOrWhiteSpace(entry.ProcessPath))
        {
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "/select,\"" + entry.ProcessPath + "\"",
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
            Title = "导出 tcp_udp_Watch 连接列表",
            Filter = filter,
            FileName = "tcp_udp_Watch_"
                + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "." + extension
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }
        try
        {
            List<EndpointEntry> entries;
            lock (_dataLock)
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
                "tcp_udp_Watch", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string[] ExportHeaders() => new[]
    {
        "协议", "IP 版本", "本地地址", "本地端口", "远程地址", "远程端口",
        "状态", "PID", "进程名", "进程完整路径", "首次发现", "最后刷新"
    };

    private static string[] ExportRow(EndpointEntry entry) => new[]
    {
        entry.Protocol,
        entry.Family,
        entry.LocalAddress,
        entry.LocalPort.ToString(),
        entry.RemoteAddress,
        entry.RemotePort.ToString(),
        entry.State,
        entry.Pid.ToString(),
        entry.ProcessName,
        entry.ProcessPath,
        entry.FirstSeen == DateTime.MinValue ? ""
            : entry.FirstSeen.ToString("yyyy-MM-dd HH:mm:ss"),
        entry.LastSeen == DateTime.MinValue ? ""
            : entry.LastSeen.ToString("yyyy-MM-dd HH:mm:ss")
    };

    private static string BuildText(List<EndpointEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join("\t", ExportHeaders()));
        foreach (var entry in entries)
        {
            builder.AppendLine(string.Join("\t", ExportRow(entry)));
        }
        return builder.ToString();
    }

    private static string BuildCsv(List<EndpointEntry> entries)
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

    private static string BuildHtml(List<EndpointEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
        builder.AppendLine("<title>tcp_udp_Watch</title><style>");
        builder.AppendLine("body{font-family:'Microsoft YaHei UI',Arial,sans-serif;background:#f8fafc;color:#1e293b;padding:20px}");
        builder.AppendLine("h1{font-size:20px}table{border-collapse:collapse;width:100%;background:#fff}");
        builder.AppendLine("th,td{border:1px solid #cbd5e1;padding:6px 9px;font-size:13px;text-align:left}");
        builder.AppendLine("th{background:#e2e8f0}tr:nth-child(even) td{background:#f8fafc}");
        builder.AppendLine("</style></head><body><h1>tcp_udp_Watch</h1>");
        builder.AppendLine("<p>导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            + " · 共 " + entries.Count + " 条</p><table><thead><tr>");
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

    private static string BuildXml(List<EndpointEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        builder.AppendLine("<tcpUdpWatch exported=\""
            + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") + "\">");
        foreach (var entry in entries)
        {
            builder.AppendLine("  <connection>");
            AppendXml(builder, "protocol", entry.Protocol);
            AppendXml(builder, "ipVersion", entry.Family);
            AppendXml(builder, "localAddress", entry.LocalAddress);
            AppendXml(builder, "localPort", entry.LocalPort.ToString());
            AppendXml(builder, "remoteAddress", entry.RemoteAddress);
            AppendXml(builder, "remotePort", entry.RemotePort.ToString());
            AppendXml(builder, "state", entry.State);
            AppendXml(builder, "processId", entry.Pid.ToString());
            AppendXml(builder, "processName", entry.ProcessName);
            AppendXml(builder, "processPath", entry.ProcessPath);
            AppendXml(builder, "firstSeen", entry.FirstSeen == DateTime.MinValue
                ? "" : entry.FirstSeen.ToString("yyyy-MM-ddTHH:mm:ss"));
            AppendXml(builder, "lastSeen", entry.LastSeen == DateTime.MinValue
                ? "" : entry.LastSeen.ToString("yyyy-MM-ddTHH:mm:ss"));
            builder.AppendLine("  </connection>");
        }
        builder.AppendLine("</tcpUdpWatch>");
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
            .Select(item => item.Tag as EndpointEntry)
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
        List<EndpointEntry> entries;
        lock (_dataLock)
        {
            entries = _entries.ToList();
        }
        entries.Sort(CompareEntries);
        CopyToClipboard(entries);
    }

    private static void CopyToClipboard(List<EndpointEntry> entries)
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

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable,
        ref int pdwSize, bool bOrder, int ulAf, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(IntPtr pUdpTable,
        ref int pdwSize, bool bOrder, int ulAf, int tableClass, uint reserved);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess,
        bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr hProcess,
        uint dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            try { _refreshCts?.Cancel(); } catch { }
            _refreshCts?.Dispose();
            _autoTimer.Dispose();
            _titleFont.Dispose();
            _statusFont.Dispose();
            _smallFont.Dispose();
            _smallBoldFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
