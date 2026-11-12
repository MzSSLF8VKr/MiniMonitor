using System.Diagnostics;
using System.Drawing.Drawing2D;
using MiniMonitor.Rss;

namespace MiniMonitor.Music;

/// <summary>听歌统计页面（纯本地数据）。</summary>
public sealed class MusicStatsPage : UserControl
{
    public MusicStatsPage(MusicLibrary library, RssTheme theme)
    {
        Dock = DockStyle.Fill;
        BackColor = theme.Surface;
        var stats = library.GetStatistics();
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(20, 14, 20, 18),
            BackColor = theme.Surface
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var cards = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.Transparent
        };
        cards.Controls.Add(BuildCard(theme, "曲目", stats.TrackCount.ToString(), "首本地音乐"));
        cards.Controls.Add(BuildCard(theme, "专辑", stats.AlbumCount.ToString(), "张专辑"));
        cards.Controls.Add(BuildCard(theme, "艺术家", stats.ArtistCount.ToString(), "位艺术家"));
        cards.Controls.Add(BuildCard(theme, "音乐总时长", FormatHours(stats.TotalSeconds), "小时"));
        cards.Controls.Add(BuildCard(theme, "播放次数", stats.TotalPlays.ToString(), "次"));
        cards.Controls.Add(BuildCard(theme, "累计收听", FormatHours(stats.PlayedSeconds), "小时"));
        root.Controls.Add(cards, 0, 0);

        var lists = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        for (int i = 0; i < 3; i++)
        {
            lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        }
        lists.Controls.Add(BuildList(theme, "最常播放的曲目", stats.TopTracks, "还没有播放记录"), 0, 0);
        lists.Controls.Add(BuildList(theme, "最常播放的艺术家", stats.TopArtists, "还没有播放记录"), 1, 0);
        lists.Controls.Add(BuildList(theme, "最常播放的专辑", stats.TopAlbums, "还没有播放记录"), 2, 0);
        root.Controls.Add(lists, 0, 1);
        Controls.Add(root);
    }

    private static string FormatHours(double seconds)
    {
        if (seconds < 3600)
        {
            return Math.Round(seconds / 60).ToString("0");
        }
        return (seconds / 3600).ToString("0.0");
    }

    private static Panel BuildCard(RssTheme theme, string title, string value, string subtitle)
    {
        var panel = new Panel
        {
            Width = 166,
            Height = 96,
            BackColor = theme.SurfaceAlt,
            Margin = new Padding(0, 0, 12, 0),
            Padding = new Padding(14, 10, 14, 10)
        };
        panel.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = ShutdownTheme.RoundRect(
                new RectangleF(0.5f, 0.5f, panel.Width - 1, panel.Height - 1), 9f);
            using var pen = new Pen(theme.Border);
            e.Graphics.DrawPath(pen, path);
        };
        var subtitleLabel = new Label
        {
            Text = subtitle,
            Dock = DockStyle.Fill,
            Font = theme.Font(8.2f),
            ForeColor = theme.TextSecondary,
            BackColor = Color.Transparent
        };
        var valueLabel = new Label
        {
            Text = value,
            Dock = DockStyle.Top,
            Height = 38,
            Font = theme.Font(20f, FontStyle.Bold),
            ForeColor = theme.Accent,
            BackColor = Color.Transparent
        };
        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 20,
            Font = theme.Font(8.4f),
            ForeColor = theme.TextTertiary,
            BackColor = Color.Transparent
        };
        panel.Controls.Add(subtitleLabel);
        panel.Controls.Add(valueLabel);
        panel.Controls.Add(titleLabel);
        return panel;
    }

    private static Panel BuildList(RssTheme theme, string title, List<MusicPlayStat> stats,
        string emptyText)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = theme.SurfaceAlt,
            Margin = new Padding(0, 0, 12, 0),
            Padding = new Padding(16, 12, 16, 12)
        };
        panel.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = ShutdownTheme.RoundRect(
                new RectangleF(0.5f, 0.5f, panel.Width - 1, panel.Height - 1), 9f);
            using var pen = new Pen(theme.Border);
            e.Graphics.DrawPath(pen, path);
        };
        var header = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 30,
            Font = theme.Font(9.4f, FontStyle.Bold),
            ForeColor = theme.Text,
            BackColor = Color.Transparent
        };
        var rows = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Color.Transparent,
            AutoScroll = true
        };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        if (stats.Count == 0)
        {
            rows.RowCount = 1;
            rows.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            rows.Controls.Add(new Label
            {
                Text = emptyText,
                Dock = DockStyle.Fill,
                Font = theme.Font(8.6f),
                ForeColor = theme.TextTertiary,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            }, 0, 0);
            rows.SetColumnSpan(rows.Controls[0], 2);
        }
        else
        {
            for (int i = 0; i < stats.Count; i++)
            {
                var item = stats[i];
                rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
                var name = new Label
                {
                    Text = $"{i + 1}.  {item.Name}",
                    Dock = DockStyle.Fill,
                    Font = theme.Font(8.6f),
                    ForeColor = theme.TextSecondary,
                    TextAlign = ContentAlignment.MiddleLeft,
                    AutoEllipsis = true,
                    BackColor = Color.Transparent
                };
                var count = new Label
                {
                    Text = item.Plays + " 次",
                    Dock = DockStyle.Fill,
                    Font = theme.Font(8.2f),
                    ForeColor = theme.TextTertiary,
                    TextAlign = ContentAlignment.MiddleRight,
                    BackColor = Color.Transparent
                };
                rows.Controls.Add(name, 0, i);
                rows.Controls.Add(count, 1, i);
            }
        }
        panel.Controls.Add(rows);
        panel.Controls.Add(header);
        return panel;
    }
}

/// <summary>插件页面：迷你播放器与桌面歌词默认开启，不联网。</summary>
public sealed class MusicPluginsPage : UserControl
{
    private readonly MusicSettings _settings;
    private readonly AppConfig _config;
    private readonly RssTheme _theme;
    private readonly Action _openMini;
    private readonly Action _openDesktop;
    private readonly Action _settingsChanged;

    public MusicPluginsPage(MusicSettings settings, AppConfig config, Action openMini,
        Action openDesktop, Action settingsChanged)
    {
        _settings = settings;
        _config = config;
        _theme = RssTheme.Create(settings.Theme);
        _openMini = openMini;
        _openDesktop = openDesktop;
        _settingsChanged = settingsChanged;
        Dock = DockStyle.Fill;
        BackColor = _theme.Surface;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(22, 18, 22, 18),
            BackColor = _theme.Surface
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var banner = new Panel { Dock = DockStyle.Fill, BackColor = _theme.AccentSoft, Padding = new Padding(16, 10, 16, 10) };
        banner.Controls.Add(new Label
        {
            Text = "迷你播放器与桌面歌词默认开启，可随时关闭。插件不会联网；所有插件均为 Windows 原生实现，不包含商店、账号或第三方脚本。",
            Dock = DockStyle.Fill,
            Font = _theme.Font(8.8f),
            ForeColor = _theme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        });
        root.Controls.Add(banner, 0, 0);

        var master = new CheckBox
        {
            Text = "启用插件功能（总开关）",
            Checked = settings.PluginsEnabled,
            Dock = DockStyle.Fill,
            Font = _theme.Font(9.4f, FontStyle.Bold),
            ForeColor = _theme.Text,
            BackColor = Color.Transparent
        };
        master.CheckedChanged += (_, _) =>
        {
            settings.PluginsEnabled = master.Checked;
            if (!master.Checked)
            {
                settings.MiniPlayerPlugin = false;
                settings.DesktopLyricsPlugin = false;
            }
            config.Save();
            _settingsChanged();
            RebuildState();
        };
        root.Controls.Add(master, 0, 1);
        root.Controls.Add(BuildPluginCard("迷你播放器", "始终置顶的小窗，显示封面、歌名、进度与播放控制。",
            "\uE8A4", () => settings.MiniPlayerPlugin, value => settings.MiniPlayerPlugin = value,
            _openMini), 0, 2);
        root.Controls.Add(BuildPluginCard("桌面歌词", "桌面悬浮歌词，自动读取同名 .lrc 文件，可拖动、置顶显示。",
            "\uE8F2", () => settings.DesktopLyricsPlugin, value => settings.DesktopLyricsPlugin = value,
            _openDesktop), 0, 3);

        var note = new Label
        {
            Dock = DockStyle.Fill,
            Font = _theme.Font(8.4f),
            ForeColor = _theme.TextTertiary,
            TextAlign = ContentAlignment.TopLeft,
            BackColor = Color.Transparent,
            Text = "提示：插件总开关关闭时，已打开的插件窗口会自动关闭。"
        };
        root.Controls.Add(note, 0, 4);
        Controls.Add(root);
    }

    public event EventHandler? SettingsChanged;

    private CheckBox _miniCheck = null!;
    private CheckBox _desktopCheck = null!;
    private Button _miniOpen = null!;
    private Button _desktopOpen = null!;

    private Control BuildPluginCard(string title, string description, string glyph,
        Func<bool> getter, Action<bool> setter, Action open)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _theme.SurfaceAlt,
            Padding = new Padding(18, 12, 18, 12),
            Margin = new Padding(0, 0, 0, 10)
        };
        card.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = ShutdownTheme.RoundRect(
                new RectangleF(0.5f, 0.5f, card.Width - 1, card.Height - 1), 9f);
            using var pen = new Pen(_theme.Border);
            e.Graphics.DrawPath(pen, path);
        };
        var titleLabel = new Label
        {
            Text = "  " + title,
            AutoSize = false,
            Location = new Point(18, 12),
            Size = new Size(240, 26),
            Font = _theme.Font(10.2f, FontStyle.Bold),
            ForeColor = _theme.Text,
            BackColor = Color.Transparent
        };
        var desc = new Label
        {
            Text = description,
            AutoSize = false,
            Location = new Point(18, 40),
            Size = new Size(520, 24),
            Font = _theme.Font(8.2f),
            ForeColor = _theme.TextTertiary,
            BackColor = Color.Transparent
        };
        var check = new CheckBox
        {
            Text = "启用",
            Checked = getter(),
            Size = new Size(76, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Font = _theme.Font(8.8f),
            ForeColor = _theme.TextSecondary,
            BackColor = Color.Transparent,
            Enabled = _settings.PluginsEnabled
        };
        var openButton = new Button
        {
            Text = "打开",
            Size = new Size(110, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = _theme.Surface,
            ForeColor = _theme.TextSecondary,
            Font = _theme.Font(8.6f),
            Enabled = _settings.PluginsEnabled && getter()
        };
        check.CheckedChanged += (_, _) =>
        {
            setter(check.Checked);
            openButton.Enabled = _settings.PluginsEnabled && check.Checked;
            _config.Save();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            _settingsChanged();
        };
        openButton.FlatAppearance.BorderColor = _theme.Border;
        openButton.Click += (_, _) => open();
        card.Controls.Add(titleLabel);
        card.Controls.Add(desc);
        card.Controls.Add(check);
        card.Controls.Add(openButton);
        card.Resize += (_, _) =>
        {
            check.Location = new Point(Math.Max(18, card.Width - 248), 20);
            openButton.Location = new Point(Math.Max(18, card.Width - 158), 18);
        };
        if (title == "迷你播放器")
        {
            _miniCheck = check;
            _miniOpen = openButton;
        }
        if (title == "桌面歌词")
        {
            _desktopCheck = check;
            _desktopOpen = openButton;
        }
        return card;
    }

    private void RebuildState()
    {
        if (_miniCheck != null)
        {
            _miniCheck.Enabled = _settings.PluginsEnabled;
            _miniCheck.Checked = _settings.MiniPlayerPlugin;
            _miniOpen.Enabled = _settings.PluginsEnabled && _settings.MiniPlayerPlugin;
        }
        if (_desktopCheck != null)
        {
            _desktopCheck.Enabled = _settings.PluginsEnabled;
            _desktopCheck.Checked = _settings.DesktopLyricsPlugin;
            _desktopOpen.Enabled = _settings.PluginsEnabled && _settings.DesktopLyricsPlugin;
        }
    }
}
