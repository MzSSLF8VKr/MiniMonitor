using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MiniMonitor.XShell;

/// <summary>XShell 启动页：渐变品牌区、悬停动效操作卡片和最近会话。</summary>
public sealed class XShellWelcomePage : UserControl
{
    private readonly FlowLayoutPanel _recentList;
    private readonly Label _countLabel;
    private readonly Label _themeLabel;

    public event Action? NewSessionRequested;
    public event Action? SessionManagerRequested;
    public event Action? SettingsRequested;
    public event Action<XSessionProfile>? SessionRequested;

    public XShellWelcomePage(XShellStore store)
    {
        BackColor = XShellTheme.Background;
        DoubleBuffered = true;

        var hero = new Panel
        {
            Dock = DockStyle.Top,
            Height = 188,
            BackColor = XShellTheme.HeaderFrom
        };
        hero.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new LinearGradientBrush(hero.ClientRectangle,
                XShellTheme.HeaderFrom, XShellTheme.HeaderTo, 20f))
            {
                g.FillRectangle(brush, hero.ClientRectangle);
            }
            using (var glow = new SolidBrush(Color.FromArgb(26, XShellTheme.Accent)))
            {
                g.FillEllipse(glow, hero.Width - 320, -130, 460, 460);
            }
            using (var glow2 = new SolidBrush(Color.FromArgb(16, Color.White)))
            {
                g.FillEllipse(glow2, hero.Width - 150, 40, 220, 220);
            }
            using (var pen = new Pen(Color.FromArgb(42, Color.White), 1f))
            {
                for (int x = -40; x < hero.Width; x += 58)
                {
                    g.DrawLine(pen, x, hero.Height, x + hero.Height, 0);
                }
            }
            if (hero.Width > 860)
            {
                DrawChips(g, hero.Width - 330, 46,
                    new[] { "SSH", "SFTP", "SERIAL", "XTERM" });
            }
            using var accent = new Pen(XShellTheme.Accent, 2f);
            g.DrawLine(accent, 0, hero.Height - 1, hero.Width, hero.Height - 1);
        };

        var logo = new XShellLogo
        {
            Left = 38,
            Top = 36,
            Width = 70,
            Height = 70
        };
        var title = new Label
        {
            Text = "XShell 复刻版",
            Left = 126,
            Top = 36,
            AutoSize = true,
            ForeColor = XShellTheme.HeroText,
            Font = new Font("Microsoft YaHei UI", 23f, FontStyle.Bold),
            BackColor = Color.Transparent
        };
        var subtitle = new Label
        {
            Text = "SSH  ·  Telnet  ·  Serial  ·  Local Shell  ·  SFTP 文件传输",
            Left = 130,
            Top = 86,
            AutoSize = true,
            ForeColor = XShellTheme.HeroMuted,
            Font = new Font("Microsoft YaHei UI", 9.2f),
            BackColor = Color.Transparent
        };
        _themeLabel = new Label
        {
            Text = ThemeLine(),
            Left = 130,
            Top = 112,
            AutoSize = true,
            ForeColor = Color.FromArgb(168, 201, 233),
            Font = new Font("Microsoft YaHei UI", 8.5f),
            BackColor = Color.Transparent
        };
        hero.Controls.AddRange(new Control[] { logo, title, subtitle, _themeLabel });
        Controls.Add(hero);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 122,
            ColumnCount = 3,
            Padding = new Padding(28, 14, 28, 10),
            BackColor = XShellTheme.Background
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        actions.Controls.Add(Card("新建会话", "创建 SSH / Telnet / Serial 会话",
            "\uE710", XShellTone.Accent, () => NewSessionRequested?.Invoke()), 0, 0);
        actions.Controls.Add(Card("会话管理器", "打开左侧的会话管理器管理会话",
            "\uE8FD", XShellTone.Green, () => SessionManagerRequested?.Invoke()), 1, 0);
        actions.Controls.Add(Card("设置", "界面主题、终端字体、日志与启动行为",
            "\uE713", XShellTone.Muted, () => SettingsRequested?.Invoke()), 2, 0);
        Controls.Add(actions);

        var recentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(34, 2, 34, 18),
            BackColor = XShellTheme.Background
        };
        var recentHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 42,
            BackColor = XShellTheme.Background
        };
        recentHeader.Paint += (_, e) =>
        {
            using var accent = new SolidBrush(XShellTheme.Accent);
            e.Graphics.FillRectangle(accent, 0, 12, 3, 18);
            TextRenderer.DrawText(e.Graphics, "最近会话",
                XShellTheme.Ui(11f, FontStyle.Bold), new Rectangle(12, 6, 260, 30),
                XShellTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        };
        _countLabel = new Label
        {
            Dock = DockStyle.Right,
            Width = 180,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = XShellTheme.Muted,
            Font = XShellTheme.Ui(8.5f),
            BackColor = Color.Transparent
        };
        recentHeader.Controls.Add(_countLabel);

        _recentList = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = XShellTheme.Background,
            Padding = new Padding(0, 2, 0, 0)
        };
        _recentList.Resize += (_, _) => ResizeRows();

        recentPanel.Controls.Add(_recentList);
        recentPanel.Controls.Add(recentHeader);
        Controls.Add(recentPanel);

        // 先让最近会话占满剩余区域，再自上而下停靠操作卡片与 Hero 区。
        Controls.SetChildIndex(recentPanel, 0);
        Controls.SetChildIndex(actions, 1);
        Controls.SetChildIndex(hero, 2);

        LoadRecent(store);
    }

    private static string ThemeLine() =>
        "界面参考 XShell 8   ·   会话能力参考 Tabby 1.0.238   ·   本地运行 / 无遥测   ·   "
        + (XShellTheme.RequestedMode == "System"
            ? "跟随系统 · " + XShellTheme.Current.Name
            : XShellTheme.Current.Name);

    /// <summary>主题切换后刷新品牌区中的主题名称。</summary>
    public void RefreshTheme()
    {
        _themeLabel.Text = ThemeLine();
        Invalidate(true);
    }

    private Control Card(string title, string subtitle, string glyph, XShellTone tone,
        Action click)
    {
        var card = new XShellActionCard(title, subtitle, glyph, tone)
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(5, 2, 5, 2)
        };
        card.Click += (_, _) => click();
        return card;
    }

    private void LoadRecent(XShellStore store)
    {
        _recentList.SuspendLayout();
        _recentList.Controls.Clear();
        var sessions = store.Sessions
            .OrderByDescending(s => s.LastUsedAt)
            .ThenBy(s => s.Name, StringComparer.CurrentCulture)
            .Take(40)
            .ToList();
        _countLabel.Text = sessions.Count == 0 ? "暂无记录"
            : "最近 " + sessions.Count + " 个连接";
        if (sessions.Count == 0)
        {
            _recentList.Controls.Add(new Label
            {
                Text = "还没有最近会话，点击上方卡片开始连接。",
                AutoSize = true,
                ForeColor = XShellTheme.Muted,
                Font = XShellTheme.Ui(9f),
                Margin = new Padding(8, 12, 0, 0),
                BackColor = Color.Transparent
            });
        }
        else
        {
            foreach (var profile in sessions)
            {
                var row = new XShellRecentRow(profile);
                row.DoubleClick += (_, _) => SessionRequested?.Invoke(profile);
                _recentList.Controls.Add(row);
            }
        }
        _recentList.ResumeLayout();
        ResizeRows();
    }

    private void ResizeRows()
    {
        int width = Math.Max(240, _recentList.ClientSize.Width - 26);
        foreach (Control control in _recentList.Controls)
        {
            if (control is XShellRecentRow)
            {
                control.Width = width;
            }
        }
    }

    private static void DrawChips(Graphics g, int x, int y, string[] labels)
    {
        var font = XShellTheme.Ui(7.8f, FontStyle.Bold);
        foreach (string label in labels)
        {
            int width = TextRenderer.MeasureText(g, label, font).Width + 22;
            var rect = new Rectangle(x, y, width, 24);
            using (var path = XShellTheme.RoundedRect(rect, 12))
            using (var brush = new SolidBrush(Color.FromArgb(34, Color.White)))
            {
                g.FillPath(brush, path);
                using var pen = new Pen(Color.FromArgb(80, Color.White));
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, label, font, rect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPadding);
            x += width + 8;
        }
    }
}
