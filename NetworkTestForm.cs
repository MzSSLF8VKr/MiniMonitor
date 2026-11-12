using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// 网络测试窗口：TCPing 测试 / Traceroute / Telnet 测试 / Nmap 扫描 / PingInfoView /
/// WirelessNetworkWatcher / BluetoothView / 蓝屏查看器 / tcp_udp_Watch / 快捷键注册列表 十个选项卡。
/// 默认不打开，从右键菜单手动进入；窗口支持最大化/还原。
/// </summary>
public sealed class NetworkTestForm : Form
{
    private readonly AppConfig _config;
    private readonly Panel _header;
    private readonly TabControl _tabs;
    private readonly TracerouteTab _tracerouteTab;
    private readonly WirelessNetworkWatcherTab _wirelessTab;
    private readonly BluetoothViewTab _bluetoothTab;
    private readonly BlueScreenViewTab _blueScreenTab;
    private readonly LiveTcpUdpWatchTab _liveTcpUdpTab;
    private readonly HotKeysListTab _hotKeysTab;
    private readonly Font _headerTitleFont;
    private readonly Font _headerSubFont;
    private bool _closeHover;
    private bool _maxHover;

    public NetworkTestForm(AppConfig config)
    {
        _config = config;
        Text = "网络测试 - MiniMonitor";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        var workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1200, 800);
        ClientSize = new Size(
            Math.Min(1200, Math.Max(1000, workArea.Width - 40)),
            Math.Min(820, Math.Max(700, workArea.Height - 40)));
        MinimumSize = new Size(1000, 700);
        MaximizedBounds = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1200, 800);
        WindowState = FormWindowState.Maximized;
        BackColor = ShutdownTheme.Background;
        DoubleBuffered = true;
        KeyPreview = true;
        ShowInTaskbar = true;
        MaximizeBox = true;

        _headerTitleFont = new Font("Microsoft YaHei UI", 19f, FontStyle.Bold);
        _headerSubFont = new Font("Microsoft YaHei UI", 9.5f);

        _header = new Panel
        {
            Dock = DockStyle.Top,
            Size = new Size(1200, 104),
            BackColor = Color.FromArgb(15, 118, 110)
        };
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
            if (_closeHover || _maxHover)
            {
                _closeHover = false;
                _maxHover = false;
                _header.Cursor = Cursors.Default;
                _header.Invalidate();
            }
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
            p => CloseRect.Contains(p) || MaximizeRect.Contains(p)
                || WindowState == FormWindowState.Maximized);

        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            Padding = new Point(18, 8)
        };

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 10, 16, 14),
            BackColor = ShutdownTheme.Background
        };
        content.Controls.Add(_tabs);
        Controls.Add(content);
        Controls.Add(_header);

        AddTab("TCPing 测试", new TcpingTestTab());
        _tracerouteTab = new TracerouteTab();
        AddTab("Traceroute", _tracerouteTab);
        AddTab("Telnet 测试", new TelnetTestTab());
        AddTab("Nmap 扫描", new NmapTestTab());
        AddTab("PingInfoView", new PingInfoViewTab(_config));
        _wirelessTab = new WirelessNetworkWatcherTab(_config);
        AddTab("WirelessNetworkWatcher", _wirelessTab);
        _bluetoothTab = new BluetoothViewTab(_config, _wirelessTab);
        AddTab("BluetoothView", _bluetoothTab);
        _blueScreenTab = new BlueScreenViewTab(_config);
        AddTab("蓝屏查看器", _blueScreenTab);
        _liveTcpUdpTab = new LiveTcpUdpWatchTab(_config);
        AddTab("tcp_udp_Watch", _liveTcpUdpTab);
        _hotKeysTab = new HotKeysListTab(_config);
        AddTab("快捷键注册列表", _hotKeysTab);
        _tabs.SelectedIndexChanged += (_, _) => UpdateTabActivation();
        FormClosing += (_, _) =>
        {
            _wirelessTab.SetActive(false);
            _bluetoothTab.SetActive(false);
            _blueScreenTab.SetActive(false);
            _liveTcpUdpTab.SetActive(false);
            _hotKeysTab.SetActive(false);
            _tracerouteTab.SetActive(false);
        };
        UpdateTabActivation();
    }

    private void UpdateTabActivation()
    {
        bool wirelessActive = _wirelessTab != null
            && _tabs.SelectedTab != null
            && ReferenceEquals(_wirelessTab.Parent, _tabs.SelectedTab);
        _wirelessTab?.SetActive(wirelessActive);
        bool bluetoothActive = _bluetoothTab != null
            && _tabs.SelectedTab != null
            && ReferenceEquals(_bluetoothTab.Parent, _tabs.SelectedTab);
        _bluetoothTab?.SetActive(bluetoothActive);
        bool blueScreenActive = _blueScreenTab != null
            && _tabs.SelectedTab != null
            && ReferenceEquals(_blueScreenTab.Parent, _tabs.SelectedTab);
        _blueScreenTab?.SetActive(blueScreenActive);
        bool liveTcpUdpActive = _liveTcpUdpTab != null
            && _tabs.SelectedTab != null
            && ReferenceEquals(_liveTcpUdpTab.Parent, _tabs.SelectedTab);
        _liveTcpUdpTab?.SetActive(liveTcpUdpActive);
        bool hotKeysActive = _hotKeysTab != null
            && _tabs.SelectedTab != null
            && ReferenceEquals(_hotKeysTab.Parent, _tabs.SelectedTab);
        _hotKeysTab?.SetActive(hotKeysActive);
        bool tracerouteActive = _tracerouteTab != null
            && _tabs.SelectedTab != null
            && ReferenceEquals(_tracerouteTab.Parent, _tabs.SelectedTab);
        _tracerouteTab?.SetActive(tracerouteActive);
    }

    private void AddTab(string title, Control content)
    {
        var page = new TabPage(title)
        {
            BackColor = Color.White,
            Padding = new Padding(0)
        };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        _tabs.TabPages.Add(page);
    }

    private Rectangle CloseRect
    {
        get
        {
            float s = _header.Height / 104f;
            return new Rectangle(
                (int)(_header.Width - 52f * s),
                (int)(14f * s),
                (int)(34f * s),
                (int)(34f * s));
        }
    }

    private Rectangle MaximizeRect
    {
        get
        {
            float s = _header.Height / 104f;
            return new Rectangle(
                (int)(_header.Width - 96f * s),
                (int)(14f * s),
                (int)(34f * s),
                (int)(34f * s));
        }
    }

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
    }

    private void PaintHeader(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        float s = _header.Height / 104f;

        using (var brush = new LinearGradientBrush(_header.ClientRectangle,
            Color.FromArgb(15, 118, 110), Color.FromArgb(2, 132, 199),
            LinearGradientMode.Horizontal))
        {
            g.FillRectangle(brush, _header.ClientRectangle);
        }
        using (var glow = new SolidBrush(Color.FromArgb(20, 255, 255, 255)))
        {
            g.FillEllipse(glow, _header.Width - 300, -130, 380, 380);
            g.FillEllipse(glow, _header.Width - 120, 20, 160, 160);
        }

        TextRenderer.DrawText(g, "网络测试", _headerTitleFont,
            new Rectangle((int)(28f * s), (int)(12f * s),
                (int)(_header.Width - 220f * s), (int)(44f * s)),
            Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "TCPing · Traceroute · Telnet · Nmap · PingInfoView · WirelessNetworkWatcher · BluetoothView · 蓝屏查看器 · tcp_udp_Watch · 快捷键注册列表（全部内置）", _headerSubFont,
            new Rectangle((int)(30f * s), (int)(54f * s),
                (int)(_header.Width - 240f * s), (int)(26f * s)),
            Color.FromArgb(204, 251, 241), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        var close = CloseRect;
        if (_closeHover)
        {
            using var hover = new SolidBrush(Color.FromArgb(42, 255, 255, 255));
            g.FillEllipse(hover, close);
        }
        using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 2f * s))
        {
            int inset = (int)(12f * s);
            g.DrawLine(pen, close.Left + inset, close.Top + inset, close.Right - inset, close.Bottom - inset);
            g.DrawLine(pen, close.Right - inset, close.Top + inset, close.Left + inset, close.Bottom - inset);
        }

        var maximize = MaximizeRect;
        if (_maxHover)
        {
            using var hover = new SolidBrush(Color.FromArgb(42, 255, 255, 255));
            g.FillEllipse(hover, maximize);
        }
        using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 1.8f * s))
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

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_header == null || _header.IsDisposed)
        {
            return;
        }
        _header.Size = new Size(ClientSize.Width, 104);
        float radius = WindowState == FormWindowState.Maximized
            ? 0f
            : 18f * (Height / 820f);
        using var path = ShutdownTheme.RoundRect(new RectangleF(0, 0, Width, Height), radius);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
        _header.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _headerTitleFont.Dispose();
            _headerSubFont.Dispose();
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
