using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// PingInfoView 风格选项卡：多主机 ICMP / TCP 持续 Ping。
/// 采用虚拟列表、异步并发调度、有界日志和缓存 DNS，适合长时间监视大量 IP。
/// </summary>
public sealed class PingInfoViewTab : UserControl
{
    private const int MaxLowerItems = 1000;
    private const int SortColumnNone = -2;

    private const int ColHost = 0;
    private const int ColReply = 1;
    private const int ColGroup = 2;
    private const int ColDescription = 3;
    private const int ColSucceed = 4;
    private const int ColFailed = 5;
    private const int ColPercent = 6;
    private const int ColSent = 7;
    private const int ColConsecutive = 8;
    private const int ColMaxConsecutive = 9;
    private const int ColLastStatus = 10;
    private const int ColLastRtt = 11;
    private const int ColAverage = 12;
    private const int ColMin = 13;
    private const int ColMax = 14;
    private const int ColTtl = 15;
    private const int ColLastSucceed = 16;
    private const int ColLastFailed = 17;
    private const int ColMac = 18;
    private const int ColOrder = 19;
    private const int ColDisabled = 20;

    private static readonly Color LightAltRow = Color.FromArgb(248, 250, 252);
    private static readonly Color DarkAltRow = Color.FromArgb(15, 23, 42);
    private static readonly Color DarkBackground = Color.FromArgb(9, 14, 26);
    private static readonly Color DarkForeground = Color.FromArgb(209, 213, 219);
    private static readonly Color SuccessLight = Color.FromArgb(22, 163, 74);
    private static readonly Color SuccessDark = Color.FromArgb(74, 222, 128);
    private static readonly Color FailLight = Color.FromArgb(220, 38, 38);
    private static readonly Color FailDark = Color.FromArgb(248, 113, 113);
    private static readonly Color DisabledForeground = Color.FromArgb(148, 163, 184);

    private readonly AppConfig _config;
    private readonly Panel _top;
    private readonly TableLayoutPanel _topLayout;
    private readonly TableLayoutPanel _titleRow;
    private readonly Label _titleLabel;
    private readonly Label _statusLabel;
    private readonly FlowLayoutPanel _buttonBar;
    private readonly FlowLayoutPanel _settingsBar;
    private readonly PillButton _hostsButton;
    private readonly PillButton _startButton;
    private readonly PillButton _stopButton;
    private readonly PillButton _resetButton;
    private readonly PillButton _exportButton;
    private readonly PillButton _optionsButton;
    private readonly NumericUpDown _intervalInput;
    private readonly NumericUpDown _timeoutInput;
    private readonly NumericUpDown _concurrentInput;
    private readonly ComboBox _logModeCombo;
    private readonly SplitContainer _split;
    private readonly Panel _lowerPanel;
    private readonly Label _logTitleLabel;
    private readonly ListView _hostList;
    private readonly ListView _logList;
    private readonly ContextMenuStrip _hostMenu;
    private ToolStripMenuItem _copySelectedItem = null!;
    private ToolStripMenuItem _copyAllItem = null!;
    private ToolStripMenuItem _toggleEnabledItem = null!;
    private ToolStripMenuItem _resetSelectedItem = null!;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Font _titleFont;
    private readonly Font _statusFont;
    private readonly Font _smallFont;
    private readonly Font _listFont;
    private readonly List<PingHostState> _hosts = new();
    private readonly List<int> _displayOrder = new();
    private readonly PingLogBuffer _log = new();
    private readonly object _macScheduleLock = new();

    private CancellationTokenSource? _sessionCts;
    private bool _running;
    private bool _disposed;
    private bool _updatingUi;
    private bool _updatingSelection;
    private bool _splitInitialized;
    private int _schedulerCursor;
    private int _inFlight;
    private int _macSchedulePending;
    private long _totalSuccess;
    private long _totalFailed;
    private long _lastUiRefresh;
    private int _sortColumn = SortColumnNone;
    private bool _sortAscending = true;
    private PingHostState? _selectedHost;
    private long _lowerLastSeq;
    private string _lowerHostKey = "";
    private string _hostsError = "";

    public PingInfoViewTab(AppConfig config)
    {
        _config = config;
        BackColor = Color.White;
        Padding = new Padding(16, 12, 16, 14);

        _titleFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
        _statusFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        _smallFont = new Font("Microsoft YaHei UI", 8.5f);
        _listFont = new Font("Microsoft YaHei UI", 8.5f);

        _top = new Panel { Dock = DockStyle.Top, Height = 112, BackColor = Color.White };
        _top.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(226, 232, 240));
            e.Graphics.DrawLine(pen, 0, _top.Height - 1, _top.Width, _top.Height - 1);
        };

        _topLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        _topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
        _topLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));

        _titleRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _titleRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _titleLabel = new Label
        {
            Text = "PingInfoView（多主机 ICMP / TCP 持续 Ping）",
            Font = _titleFont,
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 14, 0),
            Anchor = AnchorStyles.Left
        };
        _titleRow.Controls.Add(_titleLabel, 0, 0);

        _statusLabel = new Label
        {
            Text = "● 已停止",
            Font = _statusFont,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0)
        };
        _titleRow.Controls.Add(_statusLabel, 1, 0);
        _topLayout.Controls.Add(_titleRow, 0, 0);

        _buttonBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = false,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _hostsButton = MakeToolbarButton("主机列表", 88);
        _startButton = MakeToolbarButton("开始", 72, PillButtonStyle.Primary);
        _stopButton = MakeToolbarButton("停止", 72, PillButtonStyle.Danger);
        _stopButton.Enabled = false;
        _resetButton = MakeToolbarButton("重置", 72);
        _exportButton = MakeToolbarButton("导出", 72);
        _optionsButton = MakeToolbarButton("选项", 72);
        _buttonBar.Controls.AddRange(new Control[]
        {
            _hostsButton, _startButton, _stopButton, _resetButton, _exportButton, _optionsButton
        });
        _topLayout.Controls.Add(_buttonBar, 0, 1);

        _settingsBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = false,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        var settings = config.PingInfo;
        AddSettingsLabel("间隔");
        _intervalInput = AddSettingsNumeric(1, 3600, settings.IntervalSeconds, 60);
        AddSettingsLabel("秒");
        AddSettingsLabel("超时");
        _timeoutInput = AddSettingsNumeric(100, 120000, settings.TimeoutMs, 76);
        AddSettingsLabel("ms");
        AddSettingsLabel("并发");
        _concurrentInput = AddSettingsNumeric(1, 5000, settings.MaxConcurrent, 68);
        AddSettingsLabel("日志");
        _logModeCombo = new ComboBox
        {
            Width = 116,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = _smallFont,
            Margin = new Padding(2, 6, 8, 3)
        };
        _logModeCombo.Items.AddRange(new object[]
        {
            "全部", "仅失败", "仅状态变化", "关闭"
        });
        _settingsBar.Controls.Add(_logModeCombo);
        _topLayout.Controls.Add(_settingsBar, 0, 2);
        _top.Controls.Add(_topLayout);

        _hostList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            VirtualMode = true,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
            GridLines = false,
            ShowItemToolTips = true,
            BorderStyle = BorderStyle.None,
            Font = _listFont,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text
        };
        AddHostColumns();

        _logTitleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "Ping 历史：请选择上方主机",
            Font = _smallFont,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 0, 0),
            AutoEllipsis = true
        };
        _logList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
            BorderStyle = BorderStyle.None,
            Font = _listFont,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text
        };
        AddLogColumns();
        _lowerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(0)
        };
        _lowerPanel.Controls.Add(_logList);
        _lowerPanel.Controls.Add(_logTitleLabel);

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = 6,
            Panel2MinSize = 100,
            FixedPanel = FixedPanel.Panel2,
            BackColor = ShutdownTheme.Background
        };
        _split.Panel1.Controls.Add(_hostList);
        _split.Panel2.Controls.Add(_lowerPanel);
        _split.SizeChanged += (_, _) => EnsureSplitterInitialized();

        _hostMenu = BuildHostContextMenu();
        _hostList.ContextMenuStrip = _hostMenu;
        _hostList.RetrieveVirtualItem += HostListRetrieveVirtualItem;
        _hostList.ColumnClick += HostListColumnClick;
        _hostList.HandleCreated += (_, _) =>
        {
            _hostList.VirtualListSize = _displayOrder.Count;
            if (_hostList.SelectedIndices.Count == 0 && _displayOrder.Count > 0)
            {
                SelectFirstHost();
            }
        };
        _hostList.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingSelection)
            {
                return;
            }
            _selectedHost = GetSelectedHost();
            RebuildLowerPane();
        };
        _hostList.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                e.SuppressKeyPress = true;
                CopySelectedHosts();
            }
        };

        var logMenu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.System };
        logMenu.Items.Add("复制选中记录", null, (_, _) => CopyLogItems(selected: true));
        logMenu.Items.Add("复制全部记录", null, (_, _) => CopyLogItems(selected: false));
        logMenu.Items.Add(new ToolStripSeparator());
        logMenu.Items.Add("清空日志", null, (_, _) =>
        {
            _log.Clear();
            _logList.Items.Clear();
            _logTitleLabel.Text = "Ping 历史：日志已清空";
        });
        _logList.ContextMenuStrip = logMenu;
        _logList.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                e.SuppressKeyPress = true;
                CopyLogItems(selected: true);
            }
        };

        Controls.Add(_split);
        Controls.Add(_top);

        _timer = new System.Windows.Forms.Timer { Interval = 250 };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        _updatingUi = true;
        _intervalInput.Value = ClampNumeric(_intervalInput, settings.IntervalSeconds);
        _timeoutInput.Value = ClampNumeric(_timeoutInput, settings.TimeoutMs);
        _concurrentInput.Value = ClampNumeric(_concurrentInput, settings.MaxConcurrent);
        _logModeCombo.SelectedIndex = LogModeIndex(settings.LowerPaneMode);
        _log.Limit = settings.HistoryLimit;
        _updatingUi = false;

        AttachEvents();
        ApplyTheme();
        UpdateLowerPaneVisibility();
        LoadSavedHosts();
        UpdateStatus();
    }

    private PillButton MakeToolbarButton(
        string text, int width, PillButtonStyle style = PillButtonStyle.Light)
    {
        return new PillButton
        {
            Text = text,
            Width = width,
            Height = 30,
            ButtonStyle = style,
            Margin = new Padding(0, 5, 6, 4)
        };
    }

    private void AddSettingsLabel(string text)
    {
        var label = new Label
        {
            Text = text,
            Width = text.Length <= 2 ? 32 : 38,
            Height = 26,
            Font = _smallFont,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 6, 2, 3)
        };
        _settingsBar.Controls.Add(label);
    }

    private NumericUpDown AddSettingsNumeric(
        decimal min, decimal max, decimal value, int width)
    {
        var input = new NumericUpDown
        {
            Width = width,
            Height = 24,
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Font = _smallFont,
            Margin = new Padding(2, 6, 2, 3)
        };
        _settingsBar.Controls.Add(input);
        return input;
    }

    private static decimal ClampNumeric(NumericUpDown input, decimal value)
    {
        return Math.Clamp(value, input.Minimum, input.Maximum);
    }

    private void AddHostColumns()
    {
        _hostList.Columns.Add("主机", 155);
        _hostList.Columns.Add("回复 IP", 135);
        _hostList.Columns.Add("分组", 95);
        _hostList.Columns.Add("描述", 135);
        _hostList.Columns.Add("成功", 60);
        _hostList.Columns.Add("失败", 58);
        _hostList.Columns.Add("% 失败", 62);
        _hostList.Columns.Add("发送", 55);
        _hostList.Columns.Add("连续失败", 70);
        _hostList.Columns.Add("最大连续失败", 88);
        _hostList.Columns.Add("最后状态", 105);
        _hostList.Columns.Add("最后 RTT(ms)", 80);
        _hostList.Columns.Add("平均(ms)", 70);
        _hostList.Columns.Add("最小(ms)", 66);
        _hostList.Columns.Add("最大(ms)", 66);
        _hostList.Columns.Add("TTL", 45);
        _hostList.Columns.Add("最后成功时间", 120);
        _hostList.Columns.Add("最后失败时间", 120);
        _hostList.Columns.Add("MAC 地址", 115);
        _hostList.Columns.Add("顺序", 45);
        _hostList.Columns.Add("禁用", 45);
    }

    private void AddLogColumns()
    {
        _logList.Columns.Add("时间", 75);
        _logList.Columns.Add("主机", 160);
        _logList.Columns.Add("状态", 90);
        _logList.Columns.Add("回复 IP", 115);
        _logList.Columns.Add("RTT(ms)", 70);
        _logList.Columns.Add("TTL", 45);
        _logList.Columns.Add("错误", 300);
    }

    private ContextMenuStrip BuildHostContextMenu()
    {
        _copySelectedItem = new ToolStripMenuItem("复制选中主机");
        _copySelectedItem.ShortcutKeyDisplayString = "Ctrl+C";
        _copySelectedItem.Click += (_, _) => CopySelectedHosts();
        _copyAllItem = new ToolStripMenuItem("复制全部主机");
        _copyAllItem.Click += (_, _) => CopyAllHosts();
        _toggleEnabledItem = new ToolStripMenuItem("启用/禁用选中");
        _toggleEnabledItem.Click += (_, _) => ToggleSelectedEnabled();
        _resetSelectedItem = new ToolStripMenuItem("重置选中统计");
        _resetSelectedItem.Click += (_, _) => ResetSelectedStatistics();
        var hostsItem = new ToolStripMenuItem("编辑主机列表…");
        hostsItem.Click += (_, _) => OpenHostsDialog();
        var exportItem = new ToolStripMenuItem("导出…");
        exportItem.Click += (_, _) => ExportResults();

        var menu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.System };
        menu.Items.AddRange(new ToolStripItem[]
        {
            _copySelectedItem, _copyAllItem,
            new ToolStripSeparator(),
            _toggleEnabledItem, _resetSelectedItem,
            new ToolStripSeparator(),
            hostsItem, exportItem
        });
        menu.Opening += (_, _) =>
        {
            int count = _hostList.SelectedIndices.Count;
            _copySelectedItem.Enabled = count > 0;
            _copyAllItem.Enabled = _hosts.Count > 0;
            _toggleEnabledItem.Enabled = count > 0;
            _resetSelectedItem.Enabled = count > 0;
            if (count > 0)
            {
                bool allEnabled = GetSelectedHosts().All(host => host.Enabled);
                _toggleEnabledItem.Text = allEnabled ? "禁用选中主机" : "启用选中主机";
            }
            else
            {
                _toggleEnabledItem.Text = "启用/禁用选中";
            }
        };
        return menu;
    }

    private void AttachEvents()
    {
        _hostsButton.Click += (_, _) => OpenHostsDialog();
        _startButton.Click += (_, _) => StartPinging();
        _stopButton.Click += (_, _) => StopPinging();
        _resetButton.Click += (_, _) => ResetAllStatistics();
        _exportButton.Click += (_, _) => ExportResults();
        _optionsButton.Click += (_, _) => OpenOptionsDialog();

        _intervalInput.ValueChanged += (_, _) =>
        {
            if (_updatingUi) return;
            _config.PingInfo.IntervalSeconds = (int)_intervalInput.Value;
            _config.Save();
            if (_running)
            {
                long now = Environment.TickCount64;
                long interval = _config.PingInfo.IntervalSeconds * 1000L;
                foreach (var host in _hosts)
                {
                    host.NextDue = now + interval;
                }
            }
        };
        _timeoutInput.ValueChanged += (_, _) =>
        {
            if (_updatingUi) return;
            _config.PingInfo.TimeoutMs = (int)_timeoutInput.Value;
            _config.Save();
        };
        _concurrentInput.ValueChanged += (_, _) =>
        {
            if (_updatingUi) return;
            _config.PingInfo.MaxConcurrent = (int)_concurrentInput.Value;
            _config.Save();
        };
        _logModeCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingUi) return;
            _config.PingInfo.LowerPaneMode = LogModeValue(_logModeCombo.SelectedIndex);
            _config.Save();
            UpdateLowerPaneVisibility();
            RebuildLowerPane();
        };
    }

    private void LoadSavedHosts()
    {
        try
        {
            var definitions = PingHostParser.Parse(
                _config.PingInfo.HostsText,
                _config.PingInfo.CidrSkipFirst,
                _config.PingInfo.CidrSkipLast);
            ApplyHostDefinitions(definitions, persistText: null);
            _hostsError = "";
        }
        catch (Exception ex)
        {
            _hosts.Clear();
            _displayOrder.Clear();
            _hostList.VirtualListSize = 0;
            _hostsError = "主机列表解析失败：" + ex.Message;
        }
    }

    private void ApplyHostDefinitions(List<PingHostDefinition> definitions, string? persistText)
    {
        bool wasRunning = _running;
        if (wasRunning)
        {
            StopPinging();
        }

        var oldByKey = new Dictionary<string, PingHostState>(StringComparer.OrdinalIgnoreCase);
        foreach (var state in _hosts)
        {
            string key = state.Definition.Key;
            if (!oldByKey.ContainsKey(key))
            {
                oldByKey[key] = state;
            }
        }

        var newHosts = new List<PingHostState>(definitions.Count);
        foreach (var definition in definitions)
        {
            if (oldByKey.TryGetValue(definition.Key, out PingHostState? existing))
            {
                oldByKey.Remove(definition.Key);
                existing.SetDefinition(definition);
                existing.NextDue = 0;
                newHosts.Add(existing);
            }
            else
            {
                newHosts.Add(new PingHostState(definition));
            }
        }
        foreach (var leftover in oldByKey.Values)
        {
            DisposePingClient(leftover);
        }

        _hosts.Clear();
        _hosts.AddRange(newHosts);
        if (persistText != null)
        {
            _config.PingInfo.HostsText = persistText;
            _config.Save();
        }

        RecalculateTotals();
        RebuildDisplayOrder();
        if (_hostList.IsHandleCreated)
        {
            _hostList.VirtualListSize = _displayOrder.Count;
        }
        _hostList.Invalidate();

        if (_hosts.Count > 0)
        {
            SelectFirstHost();
        }
        else
        {
            _selectedHost = null;
            _logTitleLabel.Text = "Ping 历史：请选择上方主机";
            _logList.Items.Clear();
        }
        UpdateStatus();

        if (wasRunning)
        {
            StartPinging();
        }
    }

    private void RecalculateTotals()
    {
        long success = 0;
        long failed = 0;
        foreach (var host in _hosts)
        {
            lock (host.Sync)
            {
                success += host.SuccessCount;
                failed += host.FailureCount;
            }
        }
        Interlocked.Exchange(ref _totalSuccess, success);
        Interlocked.Exchange(ref _totalFailed, failed);
    }

    private void OpenHostsDialog()
    {
        bool wasRunning = _running;
        if (wasRunning)
        {
            StopPinging();
        }

        string text = _config.PingInfo.HostsText;
        while (true)
        {
            using var dialog = new PingHostListDialog(text);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                if (wasRunning && _hosts.Count > 0)
                {
                    StartPinging();
                }
                return;
            }
            text = dialog.HostsText;
            try
            {
                var definitions = PingHostParser.Parse(
                    text,
                    _config.PingInfo.CidrSkipFirst,
                    _config.PingInfo.CidrSkipLast);
                _hostsError = "";
                ApplyHostDefinitions(definitions, text);
                break;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "PingInfoView 主机列表",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        if (wasRunning && _hosts.Count > 0)
        {
            StartPinging();
        }
    }

    private void OpenOptionsDialog()
    {
        var clone = _config.PingInfo.Clone();
        using var dialog = new PingInfoOptionsDialog(clone);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        bool oldAllowIpv6 = _config.PingInfo.AllowIpv6;
        _config.PingInfo = dialog.Apply(clone);
        _log.Limit = _config.PingInfo.HistoryLimit;
        if (oldAllowIpv6 != _config.PingInfo.AllowIpv6)
        {
            foreach (var host in _hosts)
            {
                lock (host.Sync)
                {
                    host.CachedAddress = null;
                    host.Address = "-";
                }
            }
        }
        _config.Save();
        ApplyTheme();
        UpdateLowerPaneVisibility();
        RebuildLowerPane();
        _hostList.Invalidate();
    }

    private void StartPinging()
    {
        if (_running)
        {
            return;
        }
        if (_hosts.Count == 0)
        {
            OpenHostsDialog();
            return;
        }

        _running = true;
        if (_sessionCts != null)
        {
            try
            {
                _sessionCts.Cancel();
            }
            catch
            {
                // 忽略旧会话取消异常
            }
        }
        _sessionCts = new CancellationTokenSource();
        long now = Environment.TickCount64;
        foreach (var host in _hosts)
        {
            host.NextDue = now;
        }
        _schedulerCursor = 0;
        _startButton.Enabled = false;
        _stopButton.Enabled = true;
        UpdateStatus();
        _hostList.Invalidate();
    }

    private void StopPinging()
    {
        if (!_running)
        {
            return;
        }
        _running = false;
        try
        {
            _sessionCts?.Cancel();
        }
        catch
        {
            // 忽略取消异常
        }
        _startButton.Enabled = true;
        _stopButton.Enabled = false;
        UpdateStatus();
        _hostList.Invalidate();
    }

    private void ResetAllStatistics()
    {
        if (_hosts.Count == 0)
        {
            return;
        }
        if (MessageBox.Show(this, "确定重置所有主机的 Ping 统计和日志吗？", "PingInfoView",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }
        foreach (var host in _hosts)
        {
            host.ResetStatistics();
        }
        _log.Clear();
        _logList.Items.Clear();
        Interlocked.Exchange(ref _totalSuccess, 0);
        Interlocked.Exchange(ref _totalFailed, 0);
        RebuildLowerPane();
        _hostList.Invalidate();
        UpdateStatus();
    }

    private void ResetSelectedStatistics()
    {
        foreach (var host in GetSelectedHosts())
        {
            host.ResetStatistics();
        }
        RecalculateTotals();
        RebuildLowerPane();
        _hostList.Invalidate();
        UpdateStatus();
    }

    private void ToggleSelectedEnabled()
    {
        var selected = GetSelectedHosts().ToArray();
        if (selected.Length == 0)
        {
            return;
        }
        bool allEnabled = selected.All(host => host.Enabled);
        foreach (var host in selected)
        {
            host.Enabled = !allEnabled;
        }
        _hostList.Invalidate();
        UpdateStatus();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }
        if (_running)
        {
            SchedulePings();
        }

        long now = Environment.TickCount64;
        if (now - _lastUiRefresh < 1000)
        {
            return;
        }
        _lastUiRefresh = now;
        UpdateStatus();
        if (_hostList.IsHandleCreated)
        {
            _hostList.Invalidate();
        }
        if (!_split.Panel2Collapsed)
        {
            AppendLowerPaneNewEntries();
        }
    }

    private void SchedulePings()
    {
        if (_sessionCts == null || _hosts.Count == 0)
        {
            return;
        }
        int maxConcurrent = Math.Max(1, _config.PingInfo.MaxConcurrent);
        if (Volatile.Read(ref _inFlight) >= maxConcurrent)
        {
            return;
        }

        long now = Environment.TickCount64;
        long interval = Math.Max(1, _config.PingInfo.IntervalSeconds) * 1000L;
        CancellationToken token = _sessionCts.Token;
        int count = _hosts.Count;
        int scanned = 0;

        while (_running
               && scanned < count
               && Volatile.Read(ref _inFlight) < maxConcurrent)
        {
            int index = _schedulerCursor;
            _schedulerCursor = (_schedulerCursor + 1) % count;
            scanned++;

            var host = _hosts[index];
            if (!host.Enabled || host.InFlight)
            {
                continue;
            }
            if (host.NextDue > now)
            {
                continue;
            }

            host.NextDue = now + interval;
            host.InFlight = true;
            Interlocked.Increment(ref _inFlight);
            _ = Task.Run(async () =>
            {
                try
                {
                    await PingHostAsync(host, token).ConfigureAwait(false);
                }
                catch
                {
                    // 后台 Ping 的任何异常都不能影响主线程；内部已记录失败状态。
                }
            });
        }
    }

    private async Task PingHostAsync(PingHostState host, CancellationToken sessionToken)
    {
        var settings = _config.PingInfo;
        PingHostDefinition definition;
        lock (host.Sync)
        {
            definition = host.Definition;
        }

        IPAddress? address = null;
        try
        {
            PingAttempt attempt;
            try
            {
                address = await ResolveAddressAsync(host, definition, settings, sessionToken)
                    .ConfigureAwait(false);
                Stopwatch stopwatch = Stopwatch.StartNew();
                attempt = definition.IsTcp
                    ? await TcpPingAsync(address, definition.Port!.Value, settings, sessionToken)
                        .ConfigureAwait(false)
                    : await IcmpPingAsync(host, address, settings, sessionToken, stopwatch)
                        .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (sessionToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                attempt = new PingAttempt(false, "异常", ex.Message, address, null, null);
            }

            if (sessionToken.IsCancellationRequested || _disposed)
            {
                return;
            }

            bool? previousSuccess;
            lock (host.Sync)
            {
                previousSuccess = host.PreviousSuccess;
            }

            IPAddress? reply = attempt.Reply ?? address;
            bool success = attempt.Success && reply != null;
            string status = success ? attempt.Status : (attempt.Success ? "无回复地址" : attempt.Status);
            string error = success ? "" : attempt.Error;

            DateTime completedAt = DateTime.Now;
            if (success)
            {
                host.UpdateSuccess(reply!, attempt.Ms ?? 0, attempt.Ttl, completedAt);
                Interlocked.Increment(ref _totalSuccess);
            }
            else
            {
                host.UpdateFailure(status, error, completedAt);
                Interlocked.Increment(ref _totalFailed);
                if (settings.BeepOnFailure)
                {
                    int streak;
                    lock (host.Sync)
                    {
                        streak = host.ConsecutiveFailures;
                    }
                    if (streak == settings.ConsecutiveFailuresForBeep)
                    {
                        try
                        {
                            System.Media.SystemSounds.Exclamation.Play();
                        }
                        catch
                        {
                            // 音频设备不可用时忽略
                        }
                    }
                }
            }

            bool changed = previousSuccess == null || previousSuccess.Value != success;
            bool shouldLog = settings.LowerPaneMode switch
            {
                "All" => true,
                "Failed" => !success,
                "Changes" => changed,
                _ => false
            };
            if (shouldLog)
            {
                _log.Add(
                    definition.Key,
                    definition.DisplayHost,
                    success,
                    reply?.ToString() ?? "-",
                    status,
                    attempt.Ms,
                    attempt.Ttl,
                    error);
            }

            if (address is { AddressFamily: AddressFamily.InterNetwork })
            {
                ScheduleMacResolve(host, address);
            }
        }
        finally
        {
            host.InFlight = false;
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private async Task<IPAddress> ResolveAddressAsync(
        PingHostState host,
        PingHostDefinition definition,
        PingInfoSettings settings,
        CancellationToken token)
    {
        if (IPAddress.TryParse(definition.Host, out IPAddress? literal))
        {
            if (literal.AddressFamily == AddressFamily.InterNetworkV6 && !settings.AllowIpv6)
            {
                throw new InvalidOperationException("IPv6 未启用（可在“选项”中开启）。");
            }
            return literal;
        }

        if (!settings.ResolveEveryPing)
        {
            lock (host.Sync)
            {
                if (host.CachedAddress != null)
                {
                    return host.CachedAddress;
                }
            }
        }

        IPAddress[] addresses;
        using (var dnsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, dnsTimeout.Token))
        {
            addresses = await Dns.GetHostAddressesAsync(definition.Host, linked.Token)
                .ConfigureAwait(false);
        }

        IPAddress? chosen;
        if (settings.AllowIpv6)
        {
            chosen = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6)
                ?? addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        }
        else
        {
            chosen = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        }
        if (chosen == null)
        {
            throw new InvalidOperationException(
                settings.AllowIpv6 ? "未解析到可用的 IP 地址。" : "未解析到 IPv4 地址（可开启 IPv6）。");
        }
        host.SetAddress(chosen);
        return chosen;
    }

    private async Task<PingAttempt> IcmpPingAsync(
        PingHostState host,
        IPAddress address,
        PingInfoSettings settings,
        CancellationToken token,
        Stopwatch stopwatch)
    {
        Ping ping;
        if (host.PingClient == null || host.PingClientUses >= 5000)
        {
            host.PingClient?.Dispose();
            host.PingClient = new Ping();
            host.PingClientUses = 0;
        }
        ping = host.PingClient;
        host.PingClientUses++;

        try
        {
            byte[] payload = new byte[Math.Clamp(settings.PayloadSize, 1, 65500)];
            var options = new PingOptions(Math.Clamp(settings.Ttl, 1, 255), settings.DontFragment);
            stopwatch.Restart();
            PingReply reply = await ping.SendPingAsync(
                address,
                TimeSpan.FromMilliseconds(Math.Clamp(settings.TimeoutMs, 100, 120000)),
                payload, options, token).ConfigureAwait(false);
            stopwatch.Stop();

            if (reply.Status == IPStatus.Success)
            {
                double rtt = settings.HighResolution
                    ? stopwatch.Elapsed.TotalMilliseconds
                    : reply.RoundtripTime;
                return new PingAttempt(true, "成功", "", reply.Address,
                    rtt, reply.Options?.Ttl);
            }

            string status = reply.Status switch
            {
                IPStatus.TimedOut => "超时",
                IPStatus.DestinationHostUnreachable => "主机不可达",
                IPStatus.DestinationNetworkUnreachable => "网络不可达",
                IPStatus.TtlExpired => "TTL 超时",
                IPStatus.PacketTooBig => "数据包过大",
                _ => reply.Status.ToString()
            };
            return new PingAttempt(false, status, reply.Status.ToString(),
                address, null, null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new PingAttempt(false, "异常", ex.Message, address, null, null);
        }
    }

    private async Task<PingAttempt> TcpPingAsync(
        IPAddress address,
        int port,
        PingInfoSettings settings,
        CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        socket.NoDelay = true;
        using var timeout = new CancellationTokenSource(
            Math.Clamp(settings.TimeoutMs, 100, 120000));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), linked.Token)
                .ConfigureAwait(false);
            stopwatch.Stop();
            return new PingAttempt(true, "成功", "", address,
                stopwatch.Elapsed.TotalMilliseconds, null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new PingAttempt(false, "TCP 超时", "连接超时", address, null, null);
        }
        catch (SocketException ex)
        {
            string status = ex.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => "TCP 拒绝",
                SocketError.HostUnreachable => "主机不可达",
                SocketError.NetworkUnreachable => "网络不可达",
                _ => "TCP 失败"
            };
            return new PingAttempt(false, status, ex.Message, address, null, null);
        }
        catch (Exception ex)
        {
            return new PingAttempt(false, "TCP 异常", ex.Message, address, null, null);
        }
    }

    private void ScheduleMacResolve(PingHostState host, IPAddress address)
    {
        DateTime now = DateTime.UtcNow;
        lock (_macScheduleLock)
        {
            if (_macSchedulePending >= 4)
            {
                // 最多同时保留 4 个 ARP 查询任务，避免大量主机时任务队列膨胀。
                return;
            }
            lock (host.Sync)
            {
                if (host.MacInFlight || now - host.LastMacAttemptUtc < TimeSpan.FromMinutes(10))
                {
                    return;
                }
                host.MacInFlight = true;
                host.LastMacAttemptUtc = now;
                _macSchedulePending++;
            }
        }

        _ = Task.Run(() =>
        {
            try
            {
                host.SetMac(ResolveMacAddress(address));
            }
            catch
            {
                host.SetMac("-");
            }
            finally
            {
                host.MacInFlight = false;
                lock (_macScheduleLock)
                {
                    _macSchedulePending--;
                }
            }
        });
    }

    private static string ResolveMacAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return "-";
        }
        IntPtr buffer = IntPtr.Zero;
        try
        {
            byte[] bytes = address.GetAddressBytes();
            uint destination = BitConverter.ToUInt32(bytes, 0);
            buffer = Marshal.AllocHGlobal(6);
            uint length = 6;
            int result = SendARP(destination, 0, buffer, ref length);
            if (result != 0 || length < 6)
            {
                return "-";
            }
            byte[] mac = new byte[6];
            Marshal.Copy(buffer, mac, 0, 6);
            return string.Join("-", mac.Select(value => value.ToString("X2")));
        }
        catch
        {
            return "-";
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(uint destIp, uint srcIp, IntPtr macAddr, ref uint phyAddrLen);

    private sealed record PingAttempt(
        bool Success,
        string Status,
        string Error,
        IPAddress? Reply,
        double? Ms,
        int? Ttl);

    private void RebuildDisplayOrder()
    {
        var ordered = Enumerable.Range(0, _hosts.Count).ToList();
        if (_sortColumn != SortColumnNone && _hosts.Count > 0)
        {
            var snapshots = _hosts.Select(host => host.Snapshot()).ToArray();
            ordered.Sort((a, b) =>
            {
                int compare = CompareSnapshots(snapshots[a], snapshots[b], _sortColumn);
                if (compare == 0)
                {
                    compare = snapshots[a].Order.CompareTo(snapshots[b].Order);
                }
                return _sortAscending ? compare : -compare;
            });
        }
        _displayOrder.Clear();
        _displayOrder.AddRange(ordered);
        if (_hostList.IsHandleCreated && !_hostList.IsDisposed)
        {
            _hostList.VirtualListSize = _displayOrder.Count;
        }
    }

    private static int CompareSnapshots(PingHostSnapshot a, PingHostSnapshot b, int column)
    {
        return column switch
        {
            ColHost => string.Compare(a.DisplayHost, b.DisplayHost, StringComparison.CurrentCultureIgnoreCase),
            ColReply => string.Compare(a.ReplyAddress, b.ReplyAddress, StringComparison.CurrentCultureIgnoreCase),
            ColGroup => string.Compare(a.Group, b.Group, StringComparison.CurrentCultureIgnoreCase),
            ColDescription => string.Compare(a.Description, b.Description, StringComparison.CurrentCultureIgnoreCase),
            ColSucceed => a.Succeed.CompareTo(b.Succeed),
            ColFailed => a.Failed.CompareTo(b.Failed),
            ColPercent => a.PercentFailed.CompareTo(b.PercentFailed),
            ColSent => a.Total.CompareTo(b.Total),
            ColConsecutive => a.ConsecutiveFailed.CompareTo(b.ConsecutiveFailed),
            ColMaxConsecutive => a.MaxConsecutiveFailed.CompareTo(b.MaxConsecutiveFailed),
            ColLastStatus => string.Compare(a.LastStatus, b.LastStatus, StringComparison.CurrentCultureIgnoreCase),
            ColLastRtt => CompareNullable(a.LastRttMs, b.LastRttMs),
            ColAverage => CompareNullable(a.AverageMs, b.AverageMs),
            ColMin => CompareNullable(a.MinMs, b.MinMs),
            ColMax => CompareNullable(a.MaxMs, b.MaxMs),
            ColTtl => CompareNullable(a.Ttl, b.Ttl),
            ColLastSucceed => CompareNullable(a.LastSucceedOn, b.LastSucceedOn),
            ColLastFailed => CompareNullable(a.LastFailedOn, b.LastFailedOn),
            ColMac => string.Compare(a.MacAddress, b.MacAddress, StringComparison.CurrentCultureIgnoreCase),
            ColOrder => a.Order.CompareTo(b.Order),
            ColDisabled => a.Disabled.CompareTo(b.Disabled),
            _ => a.Order.CompareTo(b.Order)
        };
    }

    private static int CompareNullable<T>(T? a, T? b) where T : struct, IComparable<T>
    {
        if (a == null && b == null) return 0;
        if (a == null) return 1;
        if (b == null) return -1;
        return a.Value.CompareTo(b.Value);
    }

    private static int CompareNullable(DateTime? a, DateTime? b)
    {
        if (a == null && b == null) return 0;
        if (a == null) return 1;
        if (b == null) return -1;
        return a.Value.CompareTo(b.Value);
    }

    private void HostListColumnClick(object? sender, ColumnClickEventArgs e)
    {
        if (e.Column == _sortColumn)
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortColumn = e.Column;
            _sortAscending = true;
        }
        var selected = GetSelectedHost();
        RebuildDisplayOrder();
        RestoreSelection(selected);
        _hostList.Invalidate();
    }

    private void RestoreSelection(PingHostState? selected)
    {
        if (selected == null)
        {
            return;
        }
        int hostIndex = _hosts.IndexOf(selected);
        if (hostIndex < 0)
        {
            return;
        }
        int displayIndex = _displayOrder.IndexOf(hostIndex);
        if (displayIndex < 0)
        {
            return;
        }
        _updatingSelection = true;
        try
        {
            _hostList.SelectedIndices.Clear();
            _hostList.SelectedIndices.Add(displayIndex);
            _hostList.EnsureVisible(displayIndex);
        }
        finally
        {
            _updatingSelection = false;
        }
    }

    private PingHostState? GetSelectedHost()
    {
        if (_hostList.SelectedIndices.Count == 0)
        {
            return null;
        }
        int displayIndex = _hostList.SelectedIndices[0];
        if (displayIndex < 0 || displayIndex >= _displayOrder.Count)
        {
            return null;
        }
        int hostIndex = _displayOrder[displayIndex];
        if (hostIndex < 0 || hostIndex >= _hosts.Count)
        {
            return null;
        }
        return _hosts[hostIndex];
    }

    private IEnumerable<PingHostState> GetSelectedHosts()
    {
        foreach (int displayIndex in _hostList.SelectedIndices)
        {
            if (displayIndex < 0 || displayIndex >= _displayOrder.Count)
            {
                continue;
            }
            int hostIndex = _displayOrder[displayIndex];
            if (hostIndex >= 0 && hostIndex < _hosts.Count)
            {
                yield return _hosts[hostIndex];
            }
        }
    }

    private void SelectFirstHost()
    {
        if (_displayOrder.Count == 0)
        {
            _selectedHost = null;
            return;
        }
        if (!_hostList.IsHandleCreated)
        {
            int hostIndex = _displayOrder[0];
            _selectedHost = hostIndex >= 0 && hostIndex < _hosts.Count ? _hosts[hostIndex] : null;
            return;
        }
        _updatingSelection = true;
        try
        {
            _hostList.SelectedIndices.Clear();
            _hostList.SelectedIndices.Add(0);
        }
        finally
        {
            _updatingSelection = false;
        }
        _selectedHost = GetSelectedHost();
        RebuildLowerPane();
    }

    private void HostListRetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
    {
        if (e.ItemIndex < 0 || e.ItemIndex >= _displayOrder.Count)
        {
            e.Item = new ListViewItem();
            return;
        }
        int hostIndex = _displayOrder[e.ItemIndex];
        if (hostIndex < 0 || hostIndex >= _hosts.Count)
        {
            e.Item = new ListViewItem();
            return;
        }
        PingHostSnapshot snapshot = _hosts[hostIndex].Snapshot();
        var item = new ListViewItem(snapshot.DisplayHost)
        {
            ToolTipText = snapshot.LastError
        };
        item.SubItems.Add(snapshot.ReplyAddress);
        item.SubItems.Add(snapshot.Group);
        item.SubItems.Add(snapshot.Description);
        item.SubItems.Add(snapshot.Succeed.ToString());
        item.SubItems.Add(snapshot.Failed.ToString());
        item.SubItems.Add(snapshot.PercentFailed.ToString("0.00") + "%");
        item.SubItems.Add(snapshot.Total.ToString());
        item.SubItems.Add(snapshot.ConsecutiveFailed.ToString());
        item.SubItems.Add(snapshot.MaxConsecutiveFailed.ToString());
        item.SubItems.Add(snapshot.LastStatus);
        item.SubItems.Add(FormatMs(snapshot.LastRttMs));
        item.SubItems.Add(FormatMs(snapshot.AverageMs));
        item.SubItems.Add(FormatMs(snapshot.MinMs));
        item.SubItems.Add(FormatMs(snapshot.MaxMs));
        item.SubItems.Add(snapshot.Ttl?.ToString() ?? "-");
        item.SubItems.Add(FormatDate(snapshot.LastSucceedOn));
        item.SubItems.Add(FormatDate(snapshot.LastFailedOn));
        item.SubItems.Add(snapshot.MacAddress);
        item.SubItems.Add(snapshot.Order.ToString());
        item.SubItems.Add(snapshot.Disabled ? "是" : "");

        Color normal = _config.PingInfo.BlackBackground ? DarkForeground : ShutdownTheme.Text;
        if (snapshot.Disabled)
        {
            item.ForeColor = DisabledForeground;
        }
        else if (snapshot.LastStatus.StartsWith("成功", StringComparison.Ordinal))
        {
            item.ForeColor = _config.PingInfo.BlackBackground ? SuccessDark : SuccessLight;
        }
        else if (snapshot.LastStatus.StartsWith("失败", StringComparison.Ordinal))
        {
            item.ForeColor = _config.PingInfo.BlackBackground ? FailDark : FailLight;
        }
        else
        {
            item.ForeColor = normal;
        }

        if (_config.PingInfo.MarkOddEvenRows && e.ItemIndex % 2 == 1)
        {
            item.BackColor = _config.PingInfo.BlackBackground ? DarkAltRow : LightAltRow;
        }
        e.Item = item;
    }

    private string FormatMs(double? value)
    {
        if (value == null)
        {
            return "-";
        }
        return _config.PingInfo.HighResolution
            ? value.Value.ToString("0.000")
            : value.Value.ToString("0");
    }

    private static string FormatDate(DateTime? value)
    {
        return value?.ToString("MM-dd HH:mm:ss") ?? "-";
    }

    private void RebuildLowerPane()
    {
        _updatingSelection = true;
        try
        {
            _logList.BeginUpdate();
            _logList.Items.Clear();
            if (_selectedHost == null)
            {
                _lowerHostKey = "";
                _lowerLastSeq = 0;
                _logTitleLabel.Text = "Ping 历史：请选择上方主机";
            }
            else
            {
                string key = _selectedHost.Definition.Key;
                _lowerHostKey = key;
                List<PingLogEntry> entries = _log.GetRecent(key, MaxLowerItems);
                // 最新记录排在最上方
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    AddLogItem(entries[i], newestFirst: false);
                }
                _lowerLastSeq = entries.Count > 0 ? entries[^1].Sequence : 0;
                _logTitleLabel.Text = "Ping 历史：" + _selectedHost.Definition.DisplayHost
                    + " · " + entries.Count + " 条 · " + LogModeText(_config.PingInfo.LowerPaneMode)
                    + " · 最新在上";
            }
            _logList.EndUpdate();
        }
        finally
        {
            _updatingSelection = false;
        }
    }

    private void AppendLowerPaneNewEntries()
    {
        if (_selectedHost == null)
        {
            return;
        }
        string key = _selectedHost.Definition.Key;
        if (!string.Equals(key, _lowerHostKey, StringComparison.Ordinal))
        {
            RebuildLowerPane();
            return;
        }
        List<PingLogEntry> entries = _log.GetSince(key, _lowerLastSeq, MaxLowerItems);
        if (entries.Count == 0)
        {
            return;
        }

        _logList.BeginUpdate();
        try
        {
            foreach (var entry in entries)
            {
                AddLogItem(entry, newestFirst: true);
            }
            _lowerLastSeq = entries[^1].Sequence;
            int overflow = _logList.Items.Count - MaxLowerItems;
            for (int i = 0; i < overflow; i++)
            {
                // 新记录插入顶部，超出上限时从底部移除最旧的记录
                _logList.Items.RemoveAt(_logList.Items.Count - 1);
            }
            _logTitleLabel.Text = "Ping 历史：" + _selectedHost.Definition.DisplayHost
                + " · " + _logList.Items.Count + " 条 · " + LogModeText(_config.PingInfo.LowerPaneMode)
                + " · 最新在上";
        }
        finally
        {
            _logList.EndUpdate();
        }
    }

    private void AddLogItem(PingLogEntry entry, bool newestFirst)
    {
        var item = new ListViewItem(entry.Time.ToString("HH:mm:ss"));
        item.SubItems.Add(entry.HostText);
        item.SubItems.Add(entry.Status);
        item.SubItems.Add(entry.Address);
        item.SubItems.Add(FormatMs(entry.Ms));
        item.SubItems.Add(entry.Ttl?.ToString() ?? "-");
        item.SubItems.Add(entry.Error);
        item.ForeColor = entry.Success
            ? (_config.PingInfo.BlackBackground ? SuccessDark : SuccessLight)
            : (_config.PingInfo.BlackBackground ? FailDark : FailLight);
        if (_config.PingInfo.MarkOddEvenRows && _logList.Items.Count % 2 == 1)
        {
            item.BackColor = _config.PingInfo.BlackBackground ? DarkAltRow : LightAltRow;
        }
        if (newestFirst)
        {
            _logList.Items.Insert(0, item);
        }
        else
        {
            _logList.Items.Add(item);
        }
    }

    private void UpdateLowerPaneVisibility()
    {
        bool hidden = string.Equals(_config.PingInfo.LowerPaneMode, "None", StringComparison.Ordinal);
        _split.Panel2Collapsed = hidden;
        if (!hidden)
        {
            EnsureSplitterInitialized();
        }
    }

    private void EnsureSplitterInitialized()
    {
        if (_splitInitialized || _split.Height < 260 || _split.Panel2Collapsed)
        {
            return;
        }
        _split.SplitterDistance = Math.Max(160, _split.Height - 190);
        _splitInitialized = true;
    }

    private void ApplyTheme()
    {
        bool dark = _config.PingInfo.BlackBackground;
        Color background = dark ? DarkBackground : Color.White;
        Color foreground = dark ? DarkForeground : ShutdownTheme.Text;
        _hostList.BackColor = background;
        _hostList.ForeColor = foreground;
        _logList.BackColor = background;
        _logList.ForeColor = foreground;
        _lowerPanel.BackColor = background;
        _logTitleLabel.BackColor = background;
        _logTitleLabel.ForeColor = dark ? Color.FromArgb(148, 163, 184) : ShutdownTheme.Muted;
        RebuildLowerPane();
    }

    private void UpdateStatus()
    {
        int enabled = _hosts.Count(host => host.Enabled);
        long success = Interlocked.Read(ref _totalSuccess);
        long failed = Interlocked.Read(ref _totalFailed);
        int inFlight = Volatile.Read(ref _inFlight);
        if (!string.IsNullOrEmpty(_hostsError) && _hosts.Count == 0)
        {
            _statusLabel.Text = "● " + _hostsError;
            _statusLabel.ForeColor = ShutdownTheme.Danger;
            return;
        }

        if (_running)
        {
            _statusLabel.Text = $"● 运行中 · 主机 {_hosts.Count}（启用 {enabled}）· 成功 {success} / 失败 {failed}"
                + $" · 并发 {inFlight}/{_config.PingInfo.MaxConcurrent}";
            _statusLabel.ForeColor = ShutdownTheme.Accent;
        }
        else
        {
            _statusLabel.Text = _hosts.Count == 0
                ? "● 已停止 · 请点击“主机列表”添加目标"
                : $"● 已停止 · 主机 {_hosts.Count}（启用 {enabled}）· 成功 {success} / 失败 {failed}";
            _statusLabel.ForeColor = ShutdownTheme.Muted;
        }
    }

    private static int LogModeIndex(string mode)
    {
        return mode switch
        {
            "Failed" => 1,
            "Changes" => 2,
            "None" => 3,
            _ => 0
        };
    }

    private static string LogModeValue(int index)
    {
        return index switch
        {
            1 => "Failed",
            2 => "Changes",
            3 => "None",
            _ => "All"
        };
    }

    private static string LogModeText(string mode)
    {
        return mode switch
        {
            "Failed" => "仅失败",
            "Changes" => "仅状态变化",
            "None" => "关闭",
            _ => "全部"
        };
    }

    private void CopySelectedHosts()
    {
        var snapshots = GetSelectedSnapshots();
        if (snapshots.Count == 0)
        {
            return;
        }
        CopyText(BuildTsv(snapshots));
    }

    private void CopyAllHosts()
    {
        CopyText(BuildTsv(GetAllSnapshots()));
    }

    private List<PingHostSnapshot> GetSelectedSnapshots()
    {
        var result = new List<PingHostSnapshot>();
        foreach (var host in GetSelectedHosts())
        {
            result.Add(host.Snapshot());
        }
        return result;
    }

    private List<PingHostSnapshot> GetAllSnapshots()
    {
        return _displayOrder
            .Where(index => index >= 0 && index < _hosts.Count)
            .Select(index => _hosts[index].Snapshot())
            .ToList();
    }

    private static void CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // 剪贴板被占用时忽略
        }
    }

    private static string BuildTsv(IEnumerable<PingHostSnapshot> snapshots)
    {
        var builder = new StringBuilder();
        builder.AppendLine("主机\tIP 地址\t回复 IP\t分组\t描述\t成功\t失败\t% 失败\t发送\t连续失败\t最大连续失败\t最后状态\t最后 RTT(ms)\t平均(ms)\t最小(ms)\t最大(ms)\tTTL\t最后成功时间\t最后失败时间\tMAC 地址\t顺序\t禁用");
        foreach (var snapshot in snapshots)
        {
            builder.Append(Sanitize(snapshot.DisplayHost)).Append('\t')
                .Append(Sanitize(snapshot.Address)).Append('\t')
                .Append(Sanitize(snapshot.ReplyAddress)).Append('\t')
                .Append(Sanitize(snapshot.Group)).Append('\t')
                .Append(Sanitize(snapshot.Description)).Append('\t')
                .Append(snapshot.Succeed).Append('\t')
                .Append(snapshot.Failed).Append('\t')
                .Append(snapshot.PercentFailed.ToString("0.00")).Append('\t')
                .Append(snapshot.Total).Append('\t')
                .Append(snapshot.ConsecutiveFailed).Append('\t')
                .Append(snapshot.MaxConsecutiveFailed).Append('\t')
                .Append(Sanitize(snapshot.LastStatus)).Append('\t')
                .Append(FormatMsStatic(snapshot.LastRttMs, false)).Append('\t')
                .Append(FormatMsStatic(snapshot.AverageMs, false)).Append('\t')
                .Append(FormatMsStatic(snapshot.MinMs, false)).Append('\t')
                .Append(FormatMsStatic(snapshot.MaxMs, false)).Append('\t')
                .Append(snapshot.Ttl?.ToString() ?? "-").Append('\t')
                .Append(snapshot.LastSucceedOn?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-").Append('\t')
                .Append(snapshot.LastFailedOn?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-").Append('\t')
                .Append(Sanitize(snapshot.MacAddress)).Append('\t')
                .Append(snapshot.Order).Append('\t')
                .Append(snapshot.Disabled ? "是" : "")
                .AppendLine();
        }
        return builder.ToString();
    }

    private static string Sanitize(string value)
    {
        return value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }

    private static string FormatMsStatic(double? value, bool highResolution)
    {
        if (value == null) return "-";
        return highResolution ? value.Value.ToString("0.000") : value.Value.ToString("0");
    }

    private void CopyLogItems(bool selected)
    {
        var builder = new StringBuilder();
        builder.AppendLine("时间\t主机\t状态\t回复 IP\tRTT(ms)\tTTL\t错误");
        IEnumerable<ListViewItem> items = selected
            ? _logList.SelectedItems.Cast<ListViewItem>()
            : _logList.Items.Cast<ListViewItem>();
        foreach (var item in items)
        {
            for (int i = 0; i < item.SubItems.Count; i++)
            {
                if (i > 0) builder.Append('\t');
                builder.Append(Sanitize(item.SubItems[i].Text));
            }
            builder.AppendLine();
        }
        CopyText(builder.ToString());
    }

    private void ExportResults()
    {
        if (_hosts.Count == 0)
        {
            return;
        }

        string folder = AppPaths.Sub("PingInfoView");
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch
        {
            folder = Environment.CurrentDirectory;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "导出 PingInfoView 结果",
            InitialDirectory = folder,
            FileName = "PingInfoView_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
            Filter = "CSV 文件|*.csv|制表符文本|*.txt|HTML 文件|*.html|XML 文件|*.xml",
            FilterIndex = 1,
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var snapshots = GetAllSnapshots();
            string content = dialog.FilterIndex switch
            {
                2 => BuildTsv(snapshots),
                3 => BuildHtml(snapshots),
                4 => BuildXml(snapshots),
                _ => BuildCsv(snapshots)
            };
            File.WriteAllText(dialog.FileName, content, new UTF8Encoding(true));
            MessageBox.Show(this, "已导出：\n" + dialog.FileName, "PingInfoView",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出失败：" + ex.Message, "PingInfoView",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string BuildCsv(IEnumerable<PingHostSnapshot> snapshots)
    {
        var builder = new StringBuilder();
        builder.AppendLine("主机,IP 地址,回复 IP,分组,描述,成功,失败,% 失败,发送,连续失败,最大连续失败,最后状态,最后 RTT(ms),平均(ms),最小(ms),最大(ms),TTL,最后成功时间,最后失败时间,MAC 地址,顺序,禁用");
        foreach (var snapshot in snapshots)
        {
            string[] cells =
            {
                snapshot.DisplayHost, snapshot.Address, snapshot.ReplyAddress,
                snapshot.Group, snapshot.Description,
                snapshot.Succeed.ToString(), snapshot.Failed.ToString(),
                snapshot.PercentFailed.ToString("0.00"), snapshot.Total.ToString(),
                snapshot.ConsecutiveFailed.ToString(), snapshot.MaxConsecutiveFailed.ToString(),
                snapshot.LastStatus, FormatMsStatic(snapshot.LastRttMs, false),
                FormatMsStatic(snapshot.AverageMs, false), FormatMsStatic(snapshot.MinMs, false),
                FormatMsStatic(snapshot.MaxMs, false), snapshot.Ttl?.ToString() ?? "-",
                snapshot.LastSucceedOn?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
                snapshot.LastFailedOn?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
                snapshot.MacAddress, snapshot.Order.ToString(), snapshot.Disabled ? "是" : ""
            };
            builder.AppendLine(string.Join(",", cells.Select(CsvCell)));
        }
        return builder.ToString();
    }

    private static string CsvCell(string value)
    {
        value ??= "";
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    private static string BuildHtml(IEnumerable<PingHostSnapshot> snapshots)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
        builder.AppendLine("<title>PingInfoView</title>");
        builder.AppendLine("<style>body{font-family:Consolas,Microsoft YaHei UI,sans-serif;background:#0b1220;color:#d1d5db;padding:18px}"
            + "table{border-collapse:collapse;width:100%;font-size:12px}th,td{border:1px solid #334155;padding:4px 6px;white-space:nowrap}"
            + "th{background:#1e293b;color:#93c5fd;text-align:left}tr:nth-child(even){background:#111827}</style></head><body>");
        builder.AppendLine("<h2>PingInfoView · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "</h2>");
        builder.AppendLine("<table><tr><th>主机</th><th>IP 地址</th><th>回复 IP</th><th>分组</th><th>描述</th>"
            + "<th>成功</th><th>失败</th><th>% 失败</th><th>发送</th><th>连续失败</th><th>最大连续失败</th>"
            + "<th>最后状态</th><th>最后 RTT(ms)</th><th>平均(ms)</th><th>最小(ms)</th><th>最大(ms)</th>"
            + "<th>TTL</th><th>最后成功时间</th><th>最后失败时间</th><th>MAC 地址</th><th>顺序</th><th>禁用</th></tr>");
        foreach (var snapshot in snapshots)
        {
            builder.Append("<tr>");
            string[] cells =
            {
                snapshot.DisplayHost, snapshot.Address, snapshot.ReplyAddress,
                snapshot.Group, snapshot.Description,
                snapshot.Succeed.ToString(), snapshot.Failed.ToString(),
                snapshot.PercentFailed.ToString("0.00"), snapshot.Total.ToString(),
                snapshot.ConsecutiveFailed.ToString(), snapshot.MaxConsecutiveFailed.ToString(),
                snapshot.LastStatus, FormatMsStatic(snapshot.LastRttMs, false),
                FormatMsStatic(snapshot.AverageMs, false), FormatMsStatic(snapshot.MinMs, false),
                FormatMsStatic(snapshot.MaxMs, false), snapshot.Ttl?.ToString() ?? "-",
                snapshot.LastSucceedOn?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
                snapshot.LastFailedOn?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
                snapshot.MacAddress, snapshot.Order.ToString(), snapshot.Disabled ? "是" : ""
            };
            foreach (string cell in cells)
            {
                builder.Append("<td>").Append(HtmlEscape(cell)).Append("</td>");
            }
            builder.AppendLine("</tr>");
        }
        builder.AppendLine("</table></body></html>");
        return builder.ToString();
    }

    private static string BuildXml(IEnumerable<PingHostSnapshot> snapshots)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        builder.AppendLine("<PingInfoView generated=\"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\">");
        foreach (var snapshot in snapshots)
        {
            builder.Append("  <Host name=\"").Append(XmlEscape(snapshot.DisplayHost))
                .Append("\" address=\"").Append(XmlEscape(snapshot.Address))
                .Append("\" reply=\"").Append(XmlEscape(snapshot.ReplyAddress))
                .Append("\" group=\"").Append(XmlEscape(snapshot.Group))
                .Append("\" description=\"").Append(XmlEscape(snapshot.Description))
                .Append("\" succeed=\"").Append(snapshot.Succeed)
                .Append("\" failed=\"").Append(snapshot.Failed)
                .Append("\" percentFailed=\"").Append(snapshot.PercentFailed.ToString("0.00"))
                .Append("\" sent=\"").Append(snapshot.Total)
                .Append("\" consecutiveFailed=\"").Append(snapshot.ConsecutiveFailed)
                .Append("\" maxConsecutiveFailed=\"").Append(snapshot.MaxConsecutiveFailed)
                .Append("\" lastStatus=\"").Append(XmlEscape(snapshot.LastStatus))
                .Append("\" lastRtt=\"").Append(FormatMsStatic(snapshot.LastRttMs, false))
                .Append("\" average=\"").Append(FormatMsStatic(snapshot.AverageMs, false))
                .Append("\" min=\"").Append(FormatMsStatic(snapshot.MinMs, false))
                .Append("\" max=\"").Append(FormatMsStatic(snapshot.MaxMs, false))
                .Append("\" ttl=\"").Append(snapshot.Ttl?.ToString() ?? "")
                .Append("\" lastSucceed=\"").Append(snapshot.LastSucceedOn?.ToString("yyyy-MM-dd HH:mm:ss") ?? "")
                .Append("\" lastFailed=\"").Append(snapshot.LastFailedOn?.ToString("yyyy-MM-dd HH:mm:ss") ?? "")
                .Append("\" mac=\"").Append(XmlEscape(snapshot.MacAddress))
                .Append("\" order=\"").Append(snapshot.Order)
                .Append("\" disabled=\"").Append(snapshot.Disabled ? "true" : "false")
                .AppendLine("\" />");
        }
        builder.AppendLine("</PingInfoView>");
        return builder.ToString();
    }

    private static string HtmlEscape(string value)
    {
        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    private static string XmlEscape(string value)
    {
        return HtmlEscape(value);
    }

    private static void DisposePingClient(PingHostState state)
    {
        Ping? ping = state.PingClient;
        state.PingClient = null;
        try
        {
            ping?.Dispose();
        }
        catch
        {
            // 忽略释放异常
        }
    }

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
            try
            {
                _sessionCts?.Cancel();
                _sessionCts?.Dispose();
            }
            catch
            {
                // 忽略取消异常
            }
            _sessionCts = null;
            foreach (var host in _hosts)
            {
                DisposePingClient(host);
            }
            _hosts.Clear();
            _displayOrder.Clear();
            _log.Clear();
            _hostMenu.Dispose();
            _titleFont.Dispose();
            _statusFont.Dispose();
            _smallFont.Dispose();
            _listFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
