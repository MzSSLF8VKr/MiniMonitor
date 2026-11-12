using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MiniMonitor.Navicat;

/// <summary>Navicat 风格启动页：快捷连接、最近连接、常用功能入口。</summary>
internal sealed class NavicatWelcomePage : UserControl
{
    private readonly Panel _scrollPanel;
    private readonly FlowLayoutPanel _recentPanel;
    private readonly Label _recentEmpty;
    private readonly List<ConnectionProfile> _recent = new();

    public event EventHandler? NewConnectionRequested;
    public event EventHandler<DbKind>? QuickConnectRequested;
    public event EventHandler? OpenSqliteRequested;
    public event EventHandler? ImportRequested;
    public event EventHandler<ConnectionProfile>? RecentOpenRequested;
    public event EventHandler<string>? ToolRequested;

    public NavicatWelcomePage()
    {
        BackColor = NvTheme.Background;
        DoubleBuffered = true;

        _scrollPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = NvTheme.Background
        };
        Controls.Add(_scrollPanel);

        var hero = new HeroPanel
        {
            Location = new Point(0, 0),
            Height = 238,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        hero.NewConnection += (_, _) => NewConnectionRequested?.Invoke(this, EventArgs.Empty);
        hero.OpenSqlite += (_, _) => OpenSqliteRequested?.Invoke(this, EventArgs.Empty);
        hero.ImportConnections += (_, _) => ImportRequested?.Invoke(this, EventArgs.Empty);
        _scrollPanel.Controls.Add(hero);

        var quickTitle = new Label
        {
            Text = "快速连接",
            Font = NvTheme.Ui(11f, FontStyle.Bold),
            ForeColor = NvTheme.Text,
            AutoSize = true,
            Location = new Point(28, 262)
        };
        _scrollPanel.Controls.Add(quickTitle);

        var quickFlow = new FlowLayoutPanel
        {
            Location = new Point(22, 292),
            Height = 96,
            Width = 1080,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.Transparent,
            WrapContents = false,
            AutoScroll = true
        };
        quickFlow.Controls.Add(MakeQuickCard("MySQL", "MySQL / MariaDB", NvIcon.MySQL,
            Color.FromArgb(0, 117, 143), () => QuickConnectRequested?.Invoke(this, DbKind.MySQL)));
        quickFlow.Controls.Add(MakeQuickCard("PostgreSQL", "开源对象关系数据库", NvIcon.PostgreSQL,
            Color.FromArgb(51, 103, 145), () => QuickConnectRequested?.Invoke(this, DbKind.PostgreSQL)));
        quickFlow.Controls.Add(MakeQuickCard("SQLite", "单文件本地数据库", NvIcon.Sqlite,
            Color.FromArgb(60, 130, 190), () => OpenSqliteRequested?.Invoke(this, EventArgs.Empty)));
        quickFlow.Controls.Add(MakeQuickCard("SQL Server", "Microsoft SQL Server", NvIcon.SqlServer,
            Color.FromArgb(190, 60, 60), () => QuickConnectRequested?.Invoke(this, DbKind.SqlServer)));
        quickFlow.Controls.Add(MakeQuickCard("Oracle", "Oracle Database", NvIcon.Oracle,
            Color.FromArgb(200, 50, 50), () => QuickConnectRequested?.Invoke(this, DbKind.Oracle)));
        _scrollPanel.Controls.Add(quickFlow);

        var recentTitle = new Label
        {
            Text = "最近使用的连接",
            Font = NvTheme.Ui(11f, FontStyle.Bold),
            ForeColor = NvTheme.Text,
            AutoSize = true,
            Location = new Point(28, 404)
        };
        _scrollPanel.Controls.Add(recentTitle);

        var recentCard = new Panel
        {
            Location = new Point(28, 434),
            Width = 1060,
            Height = 176,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = NvTheme.Panel,
            Padding = new Padding(14)
        };
        recentCard.Paint += (_, e) =>
        {
            using var pen = new Pen(NvTheme.BorderLight);
            e.Graphics.DrawRectangle(pen, 0, 0, recentCard.Width - 1, recentCard.Height - 1);
        };
        _recentPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = NvTheme.Panel,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        _recentEmpty = new Label
        {
            Text = "暂无最近连接。点击上方“新建连接”开始。",
            AutoSize = true,
            ForeColor = NvTheme.Muted,
            Padding = new Padding(4, 8, 0, 0)
        };
        _recentPanel.Controls.Add(_recentEmpty);
        recentCard.Controls.Add(_recentPanel);
        _scrollPanel.Controls.Add(recentCard);

        var toolsTitle = new Label
        {
            Text = "常用工具",
            Font = NvTheme.Ui(11f, FontStyle.Bold),
            ForeColor = NvTheme.Text,
            AutoSize = true,
            Location = new Point(28, 626)
        };
        _scrollPanel.Controls.Add(toolsTitle);

        var toolsFlow = new FlowLayoutPanel
        {
            Location = new Point(22, 656),
            Height = 100,
            Width = 1080,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.Transparent,
            WrapContents = false,
            AutoScroll = true
        };
        toolsFlow.Controls.Add(MakeToolCard("SQL 查询", "多标签 SQL 编辑器", NvIcon.Query, "query"));
        toolsFlow.Controls.Add(MakeToolCard("数据传输", "跨库复制表和数据", NvIcon.Transfer, "transfer"));
        toolsFlow.Controls.Add(MakeToolCard("结构同步", "比较并同步表结构", NvIcon.Structure, "structure"));
        toolsFlow.Controls.Add(MakeToolCard("数据同步", "按主键同步表数据", NvIcon.Sync, "datasync"));
        toolsFlow.Controls.Add(MakeToolCard("ER 图", "可视化数据库关系", NvIcon.Er, "er"));
        toolsFlow.Controls.Add(MakeToolCard("计划任务", "定时查询与备份", NvIcon.Schedule, "schedule"));
        _scrollPanel.Controls.Add(toolsFlow);

        var footer = new Label
        {
            Text = "MiniMonitor · Navicat 复刻版  |  兼容 MySQL · MariaDB · PostgreSQL · SQL Server · Oracle · SQLite",
            Font = NvTheme.Ui(8.6f),
            ForeColor = NvTheme.Muted,
            AutoSize = true,
            Location = new Point(28, 776)
        };
        _scrollPanel.Controls.Add(footer);

        _scrollPanel.Resize += (_, _) => Relayout();
        Relayout();
    }

    private void Relayout()
    {
        int width = Math.Max(800, _scrollPanel.ClientSize.Width - 40);
        foreach (Control control in _scrollPanel.Controls)
        {
            if (control is HeroPanel hero)
            {
                hero.Width = _scrollPanel.ClientSize.Width;
            }
            else if (control is FlowLayoutPanel flow)
            {
                flow.Width = width;
            }
            else if (control is Panel panel && panel.Height == 176)
            {
                panel.Width = width - 20;
            }
        }
    }

    public void SetRecent(IEnumerable<ConnectionProfile> profiles)
    {
        _recent.Clear();
        _recent.AddRange(profiles.OrderByDescending(p => p.LastUsed).Take(8));
        _recentPanel.SuspendLayout();
        _recentPanel.Controls.Clear();
        if (_recent.Count == 0)
        {
            _recentPanel.Controls.Add(_recentEmpty);
        }
        else
        {
            foreach (var profile in _recent)
            {
                var item = new RecentItem(profile);
                item.OpenRequested += (_, _) => RecentOpenRequested?.Invoke(this, profile);
                _recentPanel.Controls.Add(item);
            }
        }
        _recentPanel.ResumeLayout();
    }

    private Control MakeQuickCard(string title, string subtitle, NvIcon icon, Color color, Action action)
    {
        var card = new NvActionCard
        {
            Title = title,
            Subtitle = subtitle,
            Icon = NvIcons.Get(icon, 34),
            Accent = color,
            Width = 198,
            Height = 84
        };
        card.Click += (_, _) => action();
        return card;
    }

    private Control MakeToolCard(string title, string subtitle, NvIcon icon, string key)
    {
        var card = new NvActionCard
        {
            Title = title,
            Subtitle = subtitle,
            Icon = NvIcons.Get(icon, 30),
            Accent = NvTheme.Accent,
            Width = 172,
            Height = 84
        };
        card.Click += (_, _) => ToolRequested?.Invoke(this, key);
        return card;
    }

    private sealed class HeroPanel : Panel
    {
        public event EventHandler? NewConnection;
        public event EventHandler? OpenSqlite;
        public event EventHandler? ImportConnections;

        public HeroPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(24, 54, 82);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new LinearGradientBrush(
                new Rectangle(0, 0, Math.Max(1, Width), Height),
                Color.FromArgb(18, 45, 71), Color.FromArgb(35, 91, 124), LinearGradientMode.Horizontal))
            {
                g.FillRectangle(brush, ClientRectangle);
            }
            using (var glow = new SolidBrush(Color.FromArgb(30, 240, 150, 50)))
            {
                g.FillEllipse(glow, Width - 360, -90, 420, 420);
                g.FillEllipse(glow, Width - 160, 100, 260, 260);
            }
            using (var brush = new SolidBrush(Color.FromArgb(25, 255, 255, 255)))
            {
                for (int i = 0; i < 7; i++)
                {
                    g.FillEllipse(brush, Width - 250 + i * 26, 26 + (i % 3) * 30, 12, 12);
                }
            }
            g.DrawImage(NvIcons.Get(NvIcon.Logo, 64), new Rectangle(34, 38, 64, 64));
            using (var titleFont = new Font("Microsoft YaHei UI", 25f, FontStyle.Bold))
            using (var white = new SolidBrush(Color.White))
            {
                g.DrawString("Navicat 复刻版", titleFont, white, new PointF(112, 34));
            }
            using (var subFont = new Font("Microsoft YaHei UI", 10.5f))
            using (var sub = new SolidBrush(Color.FromArgb(218, 232, 243)))
            {
                g.DrawString("DBeaver 26.2.4 功能对标  ·  Navicat Premium 界面与交互复刻",
                    subFont, sub, new PointF(116, 88));
                g.DrawString("统一管理 MySQL / MariaDB / PostgreSQL / SQL Server / Oracle / SQLite",
                    subFont, sub, new PointF(116, 112));
            }
            using (var badge = new SolidBrush(Color.FromArgb(210, 240, 150, 50)))
            {
                g.FillRectangle(badge, 116, 140, 142, 22);
            }
            using (var badgeText = new SolidBrush(Color.FromArgb(28, 48, 66)))
            using (var small = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold))
            {
                g.DrawString("MiniMonitor v" + Application.ProductVersion, small, badgeText,
                    new PointF(122, 143));
            }

            DrawButton(g, new Rectangle(116, 178, 126, 34), "新建连接", true);
            DrawButton(g, new Rectangle(252, 178, 146, 34), "打开 SQLite 数据库", false);
            DrawButton(g, new Rectangle(408, 178, 126, 34), "导入连接", false);
        }

        private void DrawButton(Graphics g, Rectangle rect, string text, bool primary)
        {
            using var path = NvTheme.RoundedRect(rect, 4);
            using var brush = new SolidBrush(primary ? Color.FromArgb(240, 140, 40) : Color.FromArgb(42, 74, 100));
            g.FillPath(brush, path);
            using var pen = new Pen(primary ? Color.FromArgb(250, 170, 80) : Color.FromArgb(87, 123, 150));
            g.DrawPath(pen, path);
            TextRenderer.DrawText(g, text, NvTheme.Ui(9.2f, FontStyle.Bold), rect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = e.Y is >= 178 and <= 212 && e.X is >= 116 and <= 534 ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Y is < 178 or > 212)
            {
                return;
            }
            if (e.X is >= 116 and <= 242)
            {
                NewConnection?.Invoke(this, EventArgs.Empty);
            }
            else if (e.X is >= 252 and <= 398)
            {
                OpenSqlite?.Invoke(this, EventArgs.Empty);
            }
            else if (e.X is >= 408 and <= 534)
            {
                ImportConnections?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private sealed class NvActionCard : Control
    {
        private bool _hover;
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public Image? Icon { get; set; }
        public Color Accent { get; set; } = NvTheme.Accent;

        public NvActionCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = NvTheme.Panel;
            Cursor = Cursors.Hand;
            Margin = new Padding(6, 4, 6, 4);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = NvTheme.RoundedRect(rect, 6);
            using var brush = new SolidBrush(_hover ? Color.FromArgb(245, 250, 254) : NvTheme.Panel);
            g.FillPath(brush, path);
            using var pen = new Pen(_hover ? Color.FromArgb(157, 193, 227) : NvTheme.BorderLight, _hover ? 1.4f : 1f);
            g.DrawPath(pen, path);
            if (_hover)
            {
                using var accent = new SolidBrush(Accent);
                g.FillRectangle(accent, 0, 8, 3, Height - 16);
            }
            if (Icon != null)
            {
                g.DrawImage(Icon, new Rectangle(14, (Height - 34) / 2, 34, 34));
            }
            using var titleFont = NvTheme.Ui(9.8f, FontStyle.Bold);
            TextRenderer.DrawText(g, Title, titleFont, new Rectangle(58, 16, Width - 66, 22),
                NvTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
                | TextFormatFlags.EndEllipsis);
            using var subFont = NvTheme.Ui(8.2f);
            TextRenderer.DrawText(g, Subtitle, subFont, new Rectangle(58, 40, Width - 66, 26),
                NvTheme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
                | TextFormatFlags.EndEllipsis);
        }
    }

    private sealed class RecentItem : Control
    {
        private readonly ConnectionProfile _profile;
        private bool _hover;

        public event EventHandler? OpenRequested;

        public RecentItem(ConnectionProfile profile)
        {
            _profile = profile;
            Width = 900;
            Height = 32;
            Margin = new Padding(0, 0, 0, 2);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            OpenRequested?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (_hover)
            {
                using var brush = new SolidBrush(Color.FromArgb(230, 240, 250));
                g.FillRectangle(brush, ClientRectangle);
            }
            g.DrawImage(NvIcons.Get(_profile.Kind.Icon(), 16), new Rectangle(4, 8, 16, 16));
            using var font = NvTheme.Ui(9f, FontStyle.Bold);
            TextRenderer.DrawText(g, _profile.Name, font, new Rectangle(28, 0, 220, Height),
                NvTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            using var sub = NvTheme.Ui(8.6f);
            TextRenderer.DrawText(g, _profile.Kind.ShortName() + "  ·  " + _profile.ServerLabel, sub,
                new Rectangle(255, 0, 380, Height), NvTheme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "最近使用 " + _profile.LastUsed.ToString("MM-dd HH:mm"), sub,
                new Rectangle(Math.Max(0, Width - 220), 0, 210, Height), NvTheme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}
