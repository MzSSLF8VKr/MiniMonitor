using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// CountryTraceRoute 风格的 Traceroute 选项卡：ICMP TTL 逐跳探测，
/// 显示 IP、可选反向解析主机名、国家 / 地区、三次延迟与平均延迟，
/// 支持最大跳数、超时、复制、导出与自动重试。
/// </summary>
public sealed class TracerouteTab : UserControl
{
    private sealed class ProbeResult
    {
        public IPStatus Status;
        public IPAddress? Address;
        public long? Milliseconds;
    }

    private sealed class CellNote
    {
        public string Text = "";
        public Color Color;
    }

    private sealed class TracerouteState
    {
        public int Version { get; set; }
        public string Target { get; set; } = "";
        public int MaxHops { get; set; } = 255;
        public int TimeoutMs { get; set; } = 3000;
        public bool ResolveNames { get; set; }
        public bool AutoRetry { get; set; }
        public int RetrySeconds { get; set; } = 30;
        public DateTime CountryLastCheck { get; set; }
        public string CountryLastHash { get; set; } = "";
        public bool CountryLastSucceeded { get; set; }
    }

    private const string EmbeddedCountrySha256 =
        "e5c8459d638a272ece7f4d7cfa49dce18d91b4f403f6940ba9249bf0db3781fd";
    private const string CountryUrlFormat =
        "https://download.db-ip.com/free/dbip-country-lite-{0:yyyy}-{0:MM}.csv.gz";
    private const int ColHop = 0;
    private const int ColIp = 1;
    private const int ColCountry = 2;
    private const int ColDelay1 = 3;
    private const int ColDelay2 = 4;
    private const int ColDelay3 = 5;
    private const int ColAverage = 6;
    private const int ColHost = 7;
    /// <summary>运营商共享地址（CGNAT）备注使用浅橙色，便于与普通私网备注区分。</summary>
    private static readonly Color CgnatNoteColor = Color.FromArgb(249, 115, 22);

    private readonly TextBox _target;
    private readonly PillButton _startButton;
    private readonly PillButton _stopButton;
    private readonly PillButton _copyButton;
    private readonly PillButton _exportButton;
    private readonly PillButton _clearButton;
    private readonly NumericUpDown _maxHops;
    private readonly NumericUpDown _timeout;
    private readonly NumericUpDown _retrySeconds;
    private readonly CheckBox _resolveNames;
    private readonly CheckBox _autoRetry;
    private readonly ListView _list;
    private readonly Label _status;
    private readonly Label _countryStatus;
    private readonly PillButton _countryUpdateButton;
    private readonly ToolTip _countryTip;
    private readonly Panel _top;
    private readonly Font _titleFont;
    private readonly Font _statusFont;
    private readonly Font _smallFont;
    private readonly System.Windows.Forms.Timer _retryTimer;
    private readonly string _statePath;
    private readonly string _countryCachePath;
    private readonly Dictionary<string, string> _countryNames =
        new(StringComparer.OrdinalIgnoreCase);

    private uint[] _countryStarts = Array.Empty<uint>();
    private byte[] _countryCodes = Array.Empty<byte>();
    private bool _countryReady;
    private bool _countryLoading;
    private bool _countryChecking;
    private bool _isActive;
    private bool _pendingCountryCheck;
    private DateTime _countryLastCheckDate = DateTime.MinValue;
    private string _countryLastHash = "";
    private bool _countryLastSucceeded;
    private string _countryHash = "";
    private string _countrySource = "";
    private int _countryCount;
    private CancellationTokenSource? _cts;
    private int _generation;
    private bool _running;
    private bool _hasStarted;
    private bool _exitMarked;
    private bool _loadingState;
    private bool _disposed;

    public TracerouteTab()
    {
        BackColor = Color.White;
        Padding = new Padding(16, 12, 16, 14);

        _titleFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
        _statusFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        _smallFont = new Font("Microsoft YaHei UI", 8.5f);

        _top = new Panel { Dock = DockStyle.Top, Height = 112, BackColor = Color.White };
        _top.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(226, 232, 240));
            e.Graphics.DrawLine(pen, 0, _top.Height - 1, _top.Width, _top.Height - 1);
        };

        // 标题 / 状态 / 开始 / 停止
        var titleRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 38,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Color.White,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = new Label
        {
            Text = "Traceroute（ICMP 路由跟踪 · CountryTraceRoute 风格）",
            Font = _titleFont,
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0, 0, 8, 0)
        };
        titleRow.Controls.Add(title, 0, 0);
        _status = new Label
        {
            Text = "● 就绪",
            Font = _statusFont,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = false,
            Width = 250,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(8, 0, 8, 0)
        };
        titleRow.Controls.Add(_status, 1, 0);
        _startButton = new PillButton
        {
            Text = "开始",
            ButtonStyle = PillButtonStyle.Primary,
            Width = 76,
            Height = 30,
            Margin = new Padding(0, 4, 8, 4)
        };
        _startButton.Click += (_, _) => StartTrace();
        titleRow.Controls.Add(_startButton, 2, 0);
        _stopButton = new PillButton
        {
            Text = "停止",
            ButtonStyle = PillButtonStyle.Danger,
            Width = 76,
            Height = 30,
            Enabled = false,
            Margin = new Padding(0, 4, 0, 4)
        };
        _stopButton.Click += (_, _) => StopTrace();
        titleRow.Controls.Add(_stopButton, 3, 0);

        // 目标 / 复制 / 导出 / 清空
        var targetRow = new Panel
        {
            Dock = DockStyle.Top,
            Height = 38,
            BackColor = Color.White,
            Padding = new Padding(0, 3, 0, 3)
        };
        var targetLabel = new Label
        {
            Text = "目标：",
            Dock = DockStyle.Left,
            Width = 46,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            Font = _smallFont
        };
        targetRow.Controls.Add(targetLabel);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 214,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.White,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        _copyButton = SmallButton("复制", 64);
        _copyButton.Click += (_, _) => CopyResults();
        _exportButton = SmallButton("导出", 64);
        _exportButton.Click += (_, _) => ExportResults();
        _clearButton = SmallButton("清空", 64);
        _clearButton.Click += (_, _) =>
        {
            _list.Items.Clear();
            _exitMarked = false;
            _status.Text = "● 就绪";
            _status.ForeColor = ShutdownTheme.Muted;
        };
        actions.Controls.AddRange(new Control[] { _copyButton, _exportButton, _clearButton });
        targetRow.Controls.Add(actions);
        _target = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            PlaceholderText = "输入域名或 IP（IPv4），按 Enter 开始跟踪"
        };
        _target.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                StartTrace();
            }
        };
        targetRow.Controls.Add(_target);
        targetRow.Controls.SetChildIndex(_target, 0);

        // 选项
        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.White,
            Padding = new Padding(0, 2, 0, 0),
            Margin = new Padding(0)
        };
        _maxHops = Number(1, 255, 255, 52);
        _timeout = Number(500, 20000, 3000, 70);
        _retrySeconds = Number(5, 3600, 30, 52);
        _resolveNames = Check("反向解析域名", false, 116);
        _autoRetry = Check("自动重试", false, 88);
        _resolveNames.Margin = new Padding(8, 2, 8, 0);
        options.Controls.AddRange(new Control[]
        {
            OptionLabel("最大跳数"), _maxHops,
            OptionLabel("超时(ms)"), _timeout,
            OptionLabel("ms"), _resolveNames, _autoRetry,
            _retrySeconds, OptionLabel("秒")
        });
        options.Controls.SetChildIndex(_resolveNames, 5);
        _autoRetry.CheckedChanged += (_, _) =>
        {
            if (!_loadingState)
            {
                if (_autoRetry.Checked && _hasStarted)
                {
                    _retryTimer.Interval = (int)_retrySeconds.Value * 1000;
                    _retryTimer.Start();
                }
                else if (!_autoRetry.Checked)
                {
                    _retryTimer.Stop();
                }
                SaveState();
            }
        };
        _retrySeconds.ValueChanged += (_, _) =>
        {
            if (!_loadingState)
            {
                _retryTimer.Interval = (int)_retrySeconds.Value * 1000;
                SaveState();
            }
        };
        _maxHops.ValueChanged += (_, _) => { if (!_loadingState) SaveState(); };
        _timeout.ValueChanged += (_, _) => { if (!_loadingState) SaveState(); };
        _resolveNames.CheckedChanged += (_, _) => { if (!_loadingState) SaveState(); };

        _top.Controls.Add(options);
        _top.Controls.Add(targetRow);
        _top.Controls.Add(titleRow);

        var listFont = new Font("Microsoft YaHei UI", 8.8f);
        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            OwnerDraw = true,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
            GridLines = false,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            Font = listFont
        };
        // IP 列预留“完整 IPv6 地址 + 网络出口 / 到达目的地 / 私网备注括号”的最宽组合。
        int ipTextWidth = TextRenderer.MeasureText(
            "ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff", listFont).Width;
        int exitWidth = TextRenderer.MeasureText("（网络出口）", listFont).Width;
        int destinationWidth = TextRenderer.MeasureText("（到达目的地）", listFont).Width;
        int privateWidth = new[]
        {
            "（私网 10.0.0.0/8）",
            "（私网 172.16.0.0/12）",
            "（私网 192.168.0.0/16）",
            "（运营商共享地址 CGNAT）",
            "（链路本地 APIPA）",
            "（环回地址）",
            "（IPv6 唯一本地 ULA）",
            "（IPv6 链路本地）"
        }.Max(note => TextRenderer.MeasureText(note, listFont).Width);
        int ipWidth = Math.Max(380, ipTextWidth
            + Math.Max(exitWidth + destinationWidth, privateWidth + destinationWidth) + 34);
        _list.Columns.Add("跳数", 56);
        _list.Columns.Add("IP 地址", ipWidth);
        _list.Columns.Add("国家 / 地区", 150);
        _list.Columns.Add("延迟 1", 82);
        _list.Columns.Add("延迟 2", 82);
        _list.Columns.Add("延迟 3", 82);
        _list.Columns.Add("平均", 82);
        _list.Columns.Add("主机名", 180);
        _list.DrawColumnHeader += (_, e) => e.DrawDefault = true;
        _list.DrawSubItem += List_DrawSubItem;
        _list.Resize += (_, _) => ResizeColumns();
        typeof(ListView)
            .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(_list, true);

        var bottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 30,
            BackColor = Color.FromArgb(248, 250, 252)
        };
        var bottomLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.FromArgb(248, 250, 252),
            Padding = new Padding(6, 3, 0, 0),
            Margin = new Padding(0)
        };
        bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _countryUpdateButton = new PillButton
        {
            Text = "手动更新国家库",
            ButtonStyle = PillButtonStyle.Light,
            Width = 124,
            Height = 24,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 8, 0)
        };
        _countryUpdateButton.Click += (_, _) => _ = CheckRemoteCountryAsync(manual: true);
        _countryStatus = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = "国家库：正在加载…",
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.FromArgb(248, 250, 252),
            Font = _smallFont,
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.Hand,
            Margin = new Padding(0)
        };
        _countryTip = new ToolTip();
        _countryStatus.Click += (_, _) => ShowCountryInfo();
        bottomLayout.Controls.Add(_countryUpdateButton, 0, 0);
        bottomLayout.Controls.Add(_countryStatus, 1, 0);
        bottom.Controls.Add(bottomLayout);

        Controls.Add(_list);
        Controls.Add(bottom);
        Controls.Add(_top);
        Controls.SetChildIndex(_list, 0);
        Controls.SetChildIndex(bottom, 1);
        Controls.SetChildIndex(_top, 2);

        _retryTimer = new System.Windows.Forms.Timer { Interval = 30000 };
        _retryTimer.Tick += (_, _) =>
        {
            if (!_disposed && _autoRetry.Checked)
            {
                _status.Text = "● 自动重试：重新开始跟踪…";
                _status.ForeColor = ShutdownTheme.Accent;
                _ = RunTraceAsync();
            }
        };

        _statePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiniMonitor", "Traceroute", "settings.json");
        _countryCachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiniMonitor", "Traceroute", "country.csv.gz");
        LoadState();
        SetCountryStatus(_countrySource.Length == 0
            ? "国家库：正在加载内置 DB-IP 2026-11…"
            : "国家库：" + _countrySource, ShutdownTheme.Muted);
        Disposed += (_, _) =>
        {
            _disposed = true;
            _retryTimer.Stop();
            try { _cts?.Cancel(); } catch { }
            SaveState();
        };

        _ = LoadCountryDatabaseAsync();
    }

    private static Label OptionLabel(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Width = text == "ms" || text == "秒" ? 30 : 66,
        Height = 26,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = ShutdownTheme.Muted,
        BackColor = Color.White,
        Font = new Font("Microsoft YaHei UI", 8.5f),
        Margin = new Padding(0, 2, 10, 0)
    };

    private static CheckBox Check(string text, bool value, int width) => new()
    {
        Text = text,
        Checked = value,
        AutoSize = false,
        Width = width,
        Height = 26,
        Font = new Font("Microsoft YaHei UI", 8.5f),
        ForeColor = ShutdownTheme.Text,
        BackColor = Color.White,
        FlatStyle = FlatStyle.System,
        Margin = new Padding(0, 2, 8, 0)
    };

    private static NumericUpDown Number(int min, int max, int value, int width) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = value,
        Width = width,
        Height = 26,
        Font = new Font("Microsoft YaHei UI", 8.5f),
        BackColor = Color.White,
        ForeColor = ShutdownTheme.Text,
        Margin = new Padding(0, 2, 8, 0)
    };

    private static PillButton SmallButton(string text, int width) => new()
    {
        Text = text,
        ButtonStyle = PillButtonStyle.Light,
        Width = width,
        Height = 28,
        Margin = new Padding(0, 1, 6, 0)
    };

    private void ResizeColumns()
    {
        if (_list.Columns.Count < 8)
        {
            return;
        }
        int fixedWidth = _list.Columns[ColHop].Width + _list.Columns[ColIp].Width
            + _list.Columns[ColCountry].Width
            + _list.Columns[ColDelay1].Width + _list.Columns[ColDelay2].Width
            + _list.Columns[ColDelay3].Width + _list.Columns[ColAverage].Width;
        _list.Columns[ColHost].Width =
            Math.Max(180, _list.ClientSize.Width - fixedWidth - 4);
    }

    /// <summary>自绘单元格：IP 列在地址后绘制红色括号说明（网络出口 / 到达目的地）。</summary>
    private void List_DrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        // 自绘选中背景，避免选中时白色文字落在白色底上导致整行“消失”。
        if (e.Item.Selected)
        {
            using var highlight = new SolidBrush(SystemColors.Highlight);
            e.Graphics.FillRectangle(highlight, e.Bounds);
        }
        else
        {
            e.DrawBackground();
        }
        Font font = e.Item.Font ?? _list.Font;
        Color color = e.Item.Selected ? SystemColors.HighlightText : e.Item.ForeColor;
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        var bounds = new Rectangle(e.Bounds.X + 4, e.Bounds.Y,
            Math.Max(0, e.Bounds.Width - 6), e.Bounds.Height);
        string text = e.SubItem?.Text ?? e.Item.Text;
        TextRenderer.DrawText(e.Graphics, text, font, bounds, color, flags);
        if (e.ColumnIndex != ColIp
            || e.SubItem?.Tag is not List<CellNote> notes || notes.Count == 0)
        {
            return;
        }
        var measureFlags = TextFormatFlags.NoPadding
            | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        int x = bounds.X + TextRenderer.MeasureText(e.Graphics, text, font,
            new Size(10000, 1000), measureFlags).Width + 2;
        foreach (CellNote note in notes)
        {
            Size size = TextRenderer.MeasureText(e.Graphics, note.Text, font,
                new Size(10000, 1000), measureFlags);
            if (x + size.Width > bounds.Right)
            {
                break;
            }
            TextRenderer.DrawText(e.Graphics, note.Text, font,
                new Rectangle(x, bounds.Y, size.Width + 2, bounds.Height),
                e.Item.Selected ? color : note.Color, flags);
            x += size.Width;
        }
    }

    private void StartTrace()
    {
        _hasStarted = true;
        _ = RunTraceAsync();
        if (_autoRetry.Checked)
        {
            _retryTimer.Interval = (int)_retrySeconds.Value * 1000;
            _retryTimer.Start();
        }
    }

    private void StopTrace()
    {
        _autoRetry.Checked = false;
        _retryTimer.Stop();
        Interlocked.Increment(ref _generation);
        try { _cts?.Cancel(); } catch { }
        _running = false;
        UpdateButtons();
        _status.Text = "● 已停止";
        _status.ForeColor = ShutdownTheme.Muted;
    }

    private async Task RunTraceAsync()
    {
        string target = _target.Text.Trim();
        if (target.Length == 0)
        {
            _status.Text = "● 请输入域名或 IP 地址";
            _status.ForeColor = ShutdownTheme.Danger;
            return;
        }

        int generation = Interlocked.Increment(ref _generation);
        var previous = _cts;
        var cts = new CancellationTokenSource();
        _cts = cts;
        previous?.Cancel();
        CancellationToken token = cts.Token;

        _running = true;
        UpdateButtons();
        _list.Items.Clear();
        _exitMarked = false;
        _status.Text = "● 正在解析目标…";
        _status.ForeColor = ShutdownTheme.Accent;

        try
        {
            IPAddress? targetIp = await ResolveTargetAsync(target, token)
                .WaitAsync(TimeSpan.FromSeconds(8), token);
            if (generation != _generation || token.IsCancellationRequested)
            {
                return;
            }
            if (targetIp == null)
            {
                _status.Text = "● 无法解析目标（仅支持 IPv4）";
                _status.ForeColor = ShutdownTheme.Danger;
                return;
            }

            int maxHops = (int)_maxHops.Value;
            int timeout = (int)_timeout.Value;
            _status.Text = "● 正在跟踪 " + targetIp
                + (string.Equals(targetIp.ToString(), target,
                    StringComparison.OrdinalIgnoreCase) ? "" : "（" + target + "）")
                + " · 最多 " + maxHops + " 跳";
            _status.ForeColor = ShutdownTheme.Accent;

            var stopwatch = Stopwatch.StartNew();
            int reachedHop = 0;
            for (int ttl = 1; ttl <= maxHops; ttl++)
            {
                token.ThrowIfCancellationRequested();
                var probes = new Task<ProbeResult>[3];
                for (int i = 0; i < probes.Length; i++)
                {
                    probes[i] = ProbeAsync(targetIp, ttl, timeout, token);
                }
                ProbeResult[] results;
                try
                {
                    results = await Task.WhenAll(probes);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                if (generation != _generation || token.IsCancellationRequested)
                {
                    return;
                }

                ProbeResult? success = results.FirstOrDefault(
                    result => result.Status == IPStatus.Success);
                bool reached = success != null;
                IPAddress? hopIp = success?.Address ?? results
                    .Select(result => result.Address)
                    .FirstOrDefault(address => address != null
                        && !address.Equals(IPAddress.Any));
                long?[] times = results.Select(result => result.Milliseconds).ToArray();
                string host = "";
                if (hopIp != null && _resolveNames.Checked)
                {
                    host = await ResolveHostAsync(hopIp, token);
                }
                string country = hopIp == null ? "" : LookupCountry(hopIp);
                AddRow(ttl, hopIp, host, country, times, reached);
                _status.Text = "● 第 " + ttl + " 跳："
                    + (hopIp == null ? "请求超时" : hopIp.ToString())
                    + (country.Length > 0 ? " · " + country : "");
                _status.ForeColor = reached
                    ? ShutdownTheme.Success
                    : ShutdownTheme.Accent;

                if (reached)
                {
                    reachedHop = ttl;
                    break;
                }
            }

            stopwatch.Stop();
            _status.Text = reachedHop > 0
                ? "● 跟踪完成：经过 " + reachedHop + " 跳到达目标，用时 "
                  + stopwatch.Elapsed.TotalSeconds.ToString("0.0") + " 秒"
                : "● 跟踪完成：未在 " + maxHops + " 跳内到达目标，用时 "
                  + stopwatch.Elapsed.TotalSeconds.ToString("0.0") + " 秒";
            _status.ForeColor = reachedHop > 0
                ? ShutdownTheme.Success
                : ShutdownTheme.Muted;
        }
        catch (OperationCanceledException)
        {
            if (generation == _generation)
            {
                _status.Text = "● 已停止";
                _status.ForeColor = ShutdownTheme.Muted;
            }
        }
        catch (Exception ex)
        {
            if (generation == _generation)
            {
                _status.Text = "● 跟踪失败：" + ex.Message;
                _status.ForeColor = ShutdownTheme.Danger;
            }
        }
        finally
        {
            if (generation == _generation)
            {
                _running = false;
                UpdateButtons();
                if (ReferenceEquals(_cts, cts))
                {
                    _cts = null;
                }
                cts.Dispose();
            }
        }
    }

    private static async Task<IPAddress?> ResolveTargetAsync(string target,
        CancellationToken token)
    {
        if (IPAddress.TryParse(target, out IPAddress? parsed))
        {
            return parsed.AddressFamily == AddressFamily.InterNetwork ? parsed : null;
        }
        try
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(target, token);
            return addresses.FirstOrDefault(address =>
                address.AddressFamily == AddressFamily.InterNetwork);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> ResolveHostAsync(IPAddress address,
        CancellationToken token)
    {
        try
        {
            IPHostEntry entry = await Dns.GetHostEntryAsync(address)
                .WaitAsync(TimeSpan.FromSeconds(1), token);
            return entry.HostName;
        }
        catch
        {
            return "";
        }
    }

    private static async Task<ProbeResult> ProbeAsync(IPAddress target, int ttl,
        int timeout, CancellationToken token)
    {
        using var ping = new Ping();
        var options = new PingOptions(ttl, true);
        var buffer = new byte[32];
        try
        {
            PingReply reply = await ping.SendPingAsync(target,
                TimeSpan.FromMilliseconds(timeout), buffer, options, token);
            long? milliseconds = reply.Status switch
            {
                IPStatus.Success => reply.RoundtripTime,
                IPStatus.TtlExpired => reply.RoundtripTime,
                _ => null
            };
            return new ProbeResult
            {
                Status = reply.Status,
                Address = reply.Address,
                Milliseconds = milliseconds
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new ProbeResult { Status = IPStatus.Unknown };
        }
    }

    private void AddRow(int hop, IPAddress? address, string host, string country,
        long?[] times, bool reached)
    {
        long? average = times.Any(value => value.HasValue)
            ? (long)times.Where(value => value.HasValue).Average(value => value!.Value)
            : null;
        var notes = new List<CellNote>();
        if (address != null)
        {
            string? privateNote = PrivateNote(address);
            if (privateNote != null)
            {
                // 私网 / 特殊地址用蓝色括号备注具体网段；CGNAT 用浅橙色突出运营商共享地址。
                notes.Add(new CellNote
                {
                    Text = "（" + privateNote + "）",
                    Color = privateNote.Contains("CGNAT", StringComparison.Ordinal)
                        ? CgnatNoteColor
                        : ShutdownTheme.Accent
                });
            }
            else if (!_exitMarked)
            {
                // 从第 1 跳开始，第一个公网地址即本机网络出口。
                _exitMarked = true;
                notes.Add(new CellNote
                {
                    Text = "（网络出口）",
                    Color = ShutdownTheme.Danger
                });
            }
            if (reached)
            {
                notes.Add(new CellNote
                {
                    Text = "（到达目的地）",
                    Color = ShutdownTheme.Danger
                });
            }
        }
        var item = new ListViewItem(hop.ToString());
        item.SubItems.Add(address?.ToString() ?? "*");
        if (notes.Count > 0)
        {
            item.SubItems[ColIp].Tag = notes;
        }
        item.SubItems.Add(country);
        item.SubItems.Add(FormatTime(times.ElementAtOrDefault(0)));
        item.SubItems.Add(FormatTime(times.ElementAtOrDefault(1)));
        item.SubItems.Add(FormatTime(times.ElementAtOrDefault(2)));
        item.SubItems.Add(average.HasValue ? average.Value + " ms" : "*");
        item.SubItems.Add(host);
        if (reached)
        {
            item.ForeColor = Color.FromArgb(6, 95, 70);
            item.Font = new Font(_list.Font, FontStyle.Bold);
        }
        else if (address == null)
        {
            item.ForeColor = Color.FromArgb(148, 163, 184);
        }
        _list.Items.Add(item);
        item.EnsureVisible();
    }

    /// <summary>
    /// 返回私网 / 不可公网路由地址对应的备注；返回 null 表示公网地址。
    /// IPv4：10/8、172.16/12、192.168/16、100.64/10（CGNAT）、169.254/16（APIPA）、127/8（环回）；
    /// IPv6：fc00::/7（ULA）、fe80::/10（链路本地）。
    /// </summary>
    private static string? PrivateNote(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] bytes = address.GetAddressBytes();
            if (bytes[0] == 10)
            {
                return "私网 10.0.0.0/8";
            }
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                return "私网 172.16.0.0/12";
            }
            if (bytes[0] == 192 && bytes[1] == 168)
            {
                return "私网 192.168.0.0/16";
            }
            if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
            {
                return "运营商共享地址 CGNAT";
            }
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return "链路本地 APIPA";
            }
            if (bytes[0] == 127)
            {
                return "环回地址";
            }
            return null;
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            byte first = address.GetAddressBytes()[0];
            if ((first & 0xFE) == 0xFC)
            {
                return "IPv6 唯一本地 ULA";
            }
            if (address.IsIPv6LinkLocal)
            {
                return "IPv6 链路本地";
            }
            return null;
        }
        return null;
    }

    private static string FormatTime(long? milliseconds) =>
        milliseconds.HasValue ? milliseconds.Value + " ms" : "*";

    /// <summary>导出 / 复制时把 IP 列的红色 / 蓝色标注一并拼入文本。</summary>
    private static string FormatSubItem(ListViewItem.ListViewSubItem sub) =>
        sub.Tag is List<CellNote> notes && notes.Count > 0
            ? sub.Text + string.Concat(notes.Select(note => note.Text))
            : sub.Text;

    private void UpdateButtons()
    {
        _startButton.Enabled = !_running;
        _stopButton.Enabled = _running;
    }

    private void CopyResults()
    {
        if (_list.Items.Count == 0)
        {
            _status.Text = "● 没有可复制的结果";
            _status.ForeColor = ShutdownTheme.Muted;
            return;
        }
        var builder = new StringBuilder();
        builder.AppendLine("跳数\tIP 地址\t国家/地区\t延迟 1\t延迟 2\t延迟 3\t平均\t主机名");
        IEnumerable<ListViewItem> items = _list.SelectedItems.Count > 0
            ? _list.SelectedItems.Cast<ListViewItem>()
            : _list.Items.Cast<ListViewItem>();
        foreach (ListViewItem item in items)
        {
            builder.AppendLine(string.Join('\t', item.SubItems
                .Cast<ListViewItem.ListViewSubItem>().Select(FormatSubItem)));
        }
        try
        {
            Clipboard.SetText(builder.ToString());
            _status.Text = "● 已复制到剪贴板";
            _status.ForeColor = ShutdownTheme.Success;
        }
        catch (Exception ex)
        {
            _status.Text = "● 复制失败：" + ex.Message;
            _status.ForeColor = ShutdownTheme.Danger;
        }
    }

    private void ExportResults()
    {
        if (_list.Items.Count == 0)
        {
            _status.Text = "● 没有可导出的结果";
            _status.ForeColor = ShutdownTheme.Muted;
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV 文件 (*.csv)|*.csv|HTML 文件 (*.html)|*.html|文本文件 (*.txt)|*.txt",
            FileName = "traceroute_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }
        try
        {
            string extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            string content;
            if (extension == ".html")
            {
                var html = new StringBuilder();
                html.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
                html.AppendLine("<title>Traceroute</title><style>body{font-family:'Microsoft YaHei UI',sans-serif;}"
                    + "table{border-collapse:collapse;}th,td{border:1px solid #cbd5e1;padding:4px 8px;}"
                    + "th{background:#0f766e;color:white;}tr:nth-child(even){background:#f1f5f9;}</style>");
                html.AppendLine("</head><body><h2>Traceroute - " + _target.Text.Trim()
                    + "</h2><table><tr><th>跳数</th><th>IP 地址</th>"
                    + "<th>国家/地区</th><th>延迟 1</th><th>延迟 2</th><th>延迟 3</th>"
                    + "<th>平均</th><th>主机名</th></tr>");
                foreach (ListViewItem item in _list.Items)
                {
                    html.Append("<tr>");
                    foreach (ListViewItem.ListViewSubItem sub in item.SubItems)
                    {
                        html.Append("<td>" + WebUtility.HtmlEncode(FormatSubItem(sub)) + "</td>");
                    }
                    html.AppendLine("</tr>");
                }
                html.AppendLine("</table></body></html>");
                content = html.ToString();
            }
            else
            {
                char separator = extension == ".txt" ? '\t' : ',';
                var text = new StringBuilder();
                text.AppendLine(string.Join(separator, new[]
                {
                    "跳数", "IP 地址", "国家/地区",
                    "延迟 1", "延迟 2", "延迟 3", "平均", "主机名"
                }));
                foreach (ListViewItem item in _list.Items)
                {
                    var values = item.SubItems
                        .Cast<ListViewItem.ListViewSubItem>()
                        .Select(FormatSubItem);
                    text.AppendLine(string.Join(separator, values.Select(value =>
                        extension == ".csv" && (value.Contains(',') || value.Contains('"'))
                            ? "\"" + value.Replace("\"", "\"\"") + "\""
                            : value)));
                }
                content = text.ToString();
            }
            File.WriteAllText(dialog.FileName, content, new UTF8Encoding(true));
            _status.Text = "● 已导出：" + dialog.FileName;
            _status.ForeColor = ShutdownTheme.Success;
        }
        catch (Exception ex)
        {
            _status.Text = "● 导出失败：" + ex.Message;
            _status.ForeColor = ShutdownTheme.Danger;
        }
    }

    /// <summary>
    /// 加载国家 / 地区库：优先使用上次下载并缓存的库，其次使用内嵌
    /// DB-IP Lite 2026-11；加载完成后若选项卡已激活，再检查当天更新。
    /// </summary>
    private async Task LoadCountryDatabaseAsync()
    {
        if (_countryLoading || _countryReady)
        {
            return;
        }
        _countryLoading = true;
        try
        {
            var data = await Task.Run(() =>
            {
                // 本地缓存优先，保证重启后仍使用上次更新到的最新库。
                try
                {
                    if (File.Exists(_countryCachePath))
                    {
                        byte[] cached = File.ReadAllBytes(_countryCachePath);
                        var table = ParseCountryDatabase(cached);
                        if (table.Item1.Length > 100_000)
                        {
                            return (table.Item1, table.Item2,
                                ComputeSha256(cached), "本地缓存");
                        }
                    }
                }
                catch
                {
                    // 缓存损坏时回退到内置库。
                }

                using Stream? stream = typeof(TracerouteTab).Assembly
                    .GetManifestResourceStream("MiniMonitor.Resources.dbip-country.csv.gz");
                if (stream == null)
                {
                    return (Array.Empty<uint>(), Array.Empty<byte>(), "", "");
                }
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                byte[] bytes = memory.ToArray();
                var embedded = ParseCountryDatabase(bytes);
                return (embedded.Item1, embedded.Item2, EmbeddedCountrySha256,
                    "内置 DB-IP 2026-11");
            });
            if (_disposed || IsDisposed || data.Item1.Length == 0)
            {
                return;
            }
            _countryStarts = data.Item1;
            _countryCodes = data.Item2;
            _countryHash = data.Item3;
            _countrySource = data.Item4;
            _countryCount = data.Item1.Length;
            _countryReady = true;
            if (_disposed || IsDisposed || !IsHandleCreated)
            {
                return;
            }
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (_disposed || IsDisposed)
                    {
                        return;
                    }
                    RefreshCountryCells();
                    SetCountryStatus("国家库：" + _countrySource + "（" + _countryCount
                        + " 条，SHA256 " + ShortCountryHash() + "…）",
                        ShutdownTheme.Muted);
                    if (_countryLastCheckDate.Date >= DateTime.Today)
                    {
                        ShowTodayCountryStatus();
                    }
                    else if (_isActive)
                    {
                        EnsureDailyCountryUpdate();
                    }
                }));
            }
            catch
            {
            }
        }
        catch
        {
            SetCountryStatus("国家库：加载失败，将继续显示 IP 与延迟", ShutdownTheme.Danger);
        }
        finally
        {
            _countryLoading = false;
        }
    }

    /// <summary>解析 DB-IP Lite gzip CSV，仅保留 IPv4 段。</summary>
    private static (uint[] Starts, byte[] Codes) ParseCountryDatabase(byte[] gzipBytes)
    {
        var starts = new List<uint>(380000);
        var codes = new List<byte>(760000);
        using var memory = new MemoryStream(gzipBytes);
        using var gzip = new GZipStream(memory, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            int first = line.IndexOf(',');
            if (first <= 0)
            {
                continue;
            }
            int second = line.IndexOf(',', first + 1);
            if (second <= 0)
            {
                continue;
            }
            if (!TryParseIpv4(line.AsSpan(0, first), out uint start))
            {
                continue;
            }
            string code = line[(second + 1)..].Trim();
            if (code.Length < 2)
            {
                continue;
            }
            starts.Add(start);
            codes.Add((byte)char.ToUpperInvariant(code[0]));
            codes.Add((byte)char.ToUpperInvariant(code[1]));
        }
        return (starts.ToArray(), codes.ToArray());
    }

    /// <summary>选项卡激活时最多每天检查一次国家库更新。</summary>
    public void SetActive(bool active)
    {
        if (_disposed)
        {
            return;
        }
        _isActive = active;
        if (!active)
        {
            return;
        }
        if (!_countryReady)
        {
            _pendingCountryCheck = true;
            return;
        }
        EnsureDailyCountryUpdate();
    }

    private void EnsureDailyCountryUpdate()
    {
        if (!_isActive || !_countryReady || _countryChecking || _disposed)
        {
            return;
        }
        if (_countryLastCheckDate.Date >= DateTime.Today)
        {
            ShowTodayCountryStatus();
            return;
        }
        _pendingCountryCheck = false;
        _countryLastCheckDate = DateTime.Today;
        _countryLastSucceeded = false;
        _countryLastHash = _countryHash;
        SaveState();
        _ = CheckRemoteCountryAsync(manual: false);
    }

    /// <summary>下载最新 DB-IP Lite 国家库；每日自动一次，或手动立即更新。</summary>
    private async Task CheckRemoteCountryAsync(bool manual = false)
    {
        if (_countryChecking)
        {
            SetCountryStatus("国家库：正在更新，请稍候…", ShutdownTheme.Accent);
            return;
        }
        if (!_countryReady)
        {
            SetCountryStatus("国家库：正在加载，请稍候…", ShutdownTheme.Accent);
            return;
        }
        _countryChecking = true;
        if (manual)
        {
            _countryLastCheckDate = DateTime.Today;
            _countryLastSucceeded = false;
            _countryLastHash = _countryHash;
            SaveState();
        }
        try
        {
            SetCountryStatus(manual
                ? "国家库：手动更新，正在下载最新 DB-IP…"
                : "国家库：正在下载今日最新 DB-IP…", ShutdownTheme.Accent);
            byte[]? bytes = null;
            DateTime month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MiniMonitor/1.66.4");
            for (int attempt = 0; attempt < 3 && bytes == null; attempt++)
            {
                string url = string.Format(CountryUrlFormat, month);
                try
                {
                    byte[] downloaded = await http.GetByteArrayAsync(url);
                    if (downloaded.Length > 1_000_000)
                    {
                        bytes = downloaded;
                    }
                }
                catch
                {
                    // 当月文件尚未发布时回退到上个月。
                }
                month = month.AddMonths(-1);
            }
            if (bytes == null)
            {
                throw new IOException("无法下载 DB-IP 国家库");
            }
            string hash = ComputeSha256(bytes);
            if (string.Equals(hash, _countryHash, StringComparison.OrdinalIgnoreCase))
            {
                SaveCountryCheckResult(hash);
                SetCountryStatus("国家库：今日已下载比对，已是最新（SHA256 "
                    + hash[..12] + "…）", ShutdownTheme.Success);
                return;
            }

            SetCountryStatus("国家库：发现新版本，正在解析并应用…", ShutdownTheme.Accent);
            var table = await Task.Run(() => ParseCountryDatabase(bytes));
            if (table.Item1.Length <= 100_000)
            {
                throw new IOException("下载内容没有有效 IPv4 记录");
            }
            _countryStarts = table.Item1;
            _countryCodes = table.Item2;
            _countryHash = hash;
            _countrySource = "在线更新（" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "）";
            _countryCount = table.Item1.Length;
            _countryReady = true;
            try
            {
                string? folder = Path.GetDirectoryName(_countryCachePath);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                File.WriteAllBytes(_countryCachePath, bytes);
                File.WriteAllText(_countryCachePath + ".sha256", hash,
                    new UTF8Encoding(false));
            }
            catch
            {
                // 缓存写入失败不影响本次使用新数据。
            }
            RefreshCountryCells();
            SaveCountryCheckResult(hash);
            SetCountryStatus("国家库：已更新为最新版本（" + _countryCount + " 条，SHA256 "
                + hash[..12] + "…）", ShutdownTheme.Success);
        }
        catch (Exception ex)
        {
            _countryLastSucceeded = false;
            SaveState();
            SetCountryStatus("国家库：下载 / 更新失败，继续使用当前数据库（"
                + ex.Message + "）", ShutdownTheme.Danger);
        }
        finally
        {
            _countryChecking = false;
        }
    }

    private void SaveCountryCheckResult(string hash)
    {
        _countryLastCheckDate = DateTime.Today;
        _countryLastHash = hash;
        _countryLastSucceeded = true;
        SaveState();
    }

    private void ShowTodayCountryStatus()
    {
        bool latest = _countryLastSucceeded
            && string.Equals(_countryLastHash, _countryHash,
                StringComparison.OrdinalIgnoreCase);
        SetCountryStatus(latest
                ? "国家库：今日已下载比对，已是最新（SHA256 "
                  + ShortCountryHash() + "…）"
                : "国家库：今日已执行检查，继续使用当前数据库",
            latest ? ShutdownTheme.Success : ShutdownTheme.Muted);
    }

    private void SetCountryStatus(string text, Color? color = null)
    {
        void Apply()
        {
            if (_disposed || IsDisposed)
            {
                return;
            }
            _countryStatus.Text = text;
            _countryStatus.ForeColor = color ?? ShutdownTheme.Muted;
            _countryTip.SetToolTip(_countryStatus, text);
        }
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(Apply)); } catch { }
            return;
        }
        Apply();
    }

    private void ShowCountryInfo()
    {
        if (_disposed || IsDisposed)
        {
            return;
        }
        string cache = File.Exists(_countryCachePath) ? "存在" : "不存在";
        string text = "国家 / 地区数据库信息\n\n"
            + "当前来源：" + (_countrySource.Length == 0 ? "未知" : _countrySource) + "\n"
            + "记录数量：" + _countryCount + " 条 IPv4 段\n"
            + "SHA256：" + (_countryHash.Length == 0 ? "未知" : _countryHash) + "\n"
            + "本地缓存：" + cache + "\n"
            + "缓存路径：" + _countryCachePath + "\n\n"
            + "更新策略：默认使用内置 / 本地最新库；选项卡激活后每天最多\n"
            + "自动下载比对一次，也可点击“手动更新国家库”立即更新。";
        MessageBox.Show(FindForm(), text, "国家 / 地区数据库",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private string ShortCountryHash() =>
        _countryHash.Length >= 12 ? _countryHash[..12] : _countryHash;
    private void RefreshCountryCells()
    {
        foreach (ListViewItem item in _list.Items)
        {
            if (item.SubItems.Count < 3
                || !IPAddress.TryParse(item.SubItems[ColIp].Text, out IPAddress? address))
            {
                continue;
            }
            item.SubItems[ColCountry].Text = LookupCountry(address);
        }
    }

    private string LookupCountry(IPAddress address)
    {
        if (IsPrivate(address))
        {
            return "局域网";
        }
        if (!_countryReady || address.AddressFamily != AddressFamily.InterNetwork
            || _countryStarts.Length == 0)
        {
            return _countryLoading ? "加载中…" : "未知";
        }
        uint value = ToUInt32(address);
        int index = Array.BinarySearch(_countryStarts, value);
        if (index < 0)
        {
            index = ~index - 1;
        }
        if (index < 0)
        {
            return "未知";
        }
        int codeIndex = index * 2;
        if (codeIndex + 1 >= _countryCodes.Length)
        {
            return "未知";
        }
        char first = (char)_countryCodes[codeIndex];
        char second = (char)_countryCodes[codeIndex + 1];
        if (first == '\0' || (first == 'Z' && second == 'Z'))
        {
            return "未知";
        }
        string code = new string(new[] { first, second });
        if (_countryNames.TryGetValue(code, out string? cached))
        {
            return cached.Length == 0 ? code : code + " " + cached;
        }
        string name = "";
        try
        {
            var region = new RegionInfo(code);
            name = region.NativeName;
            if (string.IsNullOrWhiteSpace(name)
                || string.Equals(name, code, StringComparison.OrdinalIgnoreCase))
            {
                name = region.EnglishName;
            }
        }
        catch
        {
        }
        if (string.Equals(name, code, StringComparison.OrdinalIgnoreCase))
        {
            name = "";
        }
        _countryNames[code] = name;
        return name.Length == 0 ? code : code + " " + name;
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }
        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || bytes[0] == 127
            || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 169 && bytes[1] == 254);
    }

    private static uint ToUInt32(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16)
            | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static bool TryParseIpv4(ReadOnlySpan<char> text, out uint value)
    {
        value = 0;
        uint result = 0;
        int part = 0;
        int digits = 0;
        int dots = 0;
        foreach (char c in text)
        {
            if (c == '.')
            {
                if (digits == 0)
                {
                    return false;
                }
                result = (result << 8) | (uint)part;
                part = 0;
                digits = 0;
                dots++;
            }
            else if (c is >= '0' and <= '9')
            {
                part = part * 10 + (c - '0');
                digits++;
                if (part > 255)
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }
        if (dots != 3 || digits == 0)
        {
            return false;
        }
        value = (result << 8) | (uint)part;
        return true;
    }

    private static string ComputeSha256(byte[] bytes)
    {
        byte[] hash = SHA256.HashData(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (byte value in hash)
        {
            builder.Append(value.ToString("x2"));
        }
        return builder.ToString();
    }

    private void LoadState()
    {
        _loadingState = true;
        try
        {
            if (File.Exists(_statePath))
            {
                var state = JsonSerializer.Deserialize<TracerouteState>(
                    File.ReadAllText(_statePath));
                if (state != null)
                {
                    _target.Text = state.Target ?? "";
                    int maxHops = state.MaxHops;
                    if (state.Version < 3 && (maxHops == 36 || maxHops == 100))
                    {
                        // 旧版本默认 36 / 100 跳，升级后直接使用最大 255 跳。
                        maxHops = 255;
                    }
                    _maxHops.Value = Math.Clamp(maxHops, 1, 255);
                    _timeout.Value = Math.Clamp(state.TimeoutMs, 500, 20000);
                    _resolveNames.Checked = state.ResolveNames;
                    _autoRetry.Checked = state.AutoRetry;
                    _retrySeconds.Value = Math.Clamp(state.RetrySeconds, 5, 3600);
                    _countryLastCheckDate = state.CountryLastCheck;
                    _countryLastHash = state.CountryLastHash ?? "";
                    _countryLastSucceeded = state.CountryLastSucceeded;
                }
            }
        }
        catch
        {
            // 读取失败时使用默认值。
        }
        finally
        {
            _loadingState = false;
        }
        _retryTimer.Interval = (int)_retrySeconds.Value * 1000;
    }

    private void SaveState()
    {
        try
        {
            string? folder = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }
            var state = new TracerouteState
            {
                Version = 3,
                Target = _target.Text.Trim(),
                MaxHops = (int)_maxHops.Value,
                TimeoutMs = (int)_timeout.Value,
                ResolveNames = _resolveNames.Checked,
                AutoRetry = _autoRetry.Checked,
                RetrySeconds = (int)_retrySeconds.Value,
                CountryLastCheck = _countryLastCheckDate,
                CountryLastHash = _countryLastHash,
                CountryLastSucceeded = _countryLastSucceeded
            };
            File.WriteAllText(_statePath,
                JsonSerializer.Serialize(state, new JsonSerializerOptions
                {
                    WriteIndented = true
                }), new UTF8Encoding(false));
        }
        catch
        {
            // 保存失败不影响跟踪。
        }
    }
}
