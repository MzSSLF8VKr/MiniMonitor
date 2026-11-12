using System.Diagnostics;
using MiniMonitor.Rss;

namespace MiniMonitor.Music;

/// <summary>重复项清理页面：顶部扫描 / 清理按钮，按同名文件分组显示保留与删除项。</summary>
public sealed class MusicDuplicatesPage : UserControl
{
    private readonly RssTheme _theme;
    private readonly FlowLayoutPanel _list;
    private readonly Panel _scroll;

    public MusicDuplicatesPage(IReadOnlyList<DuplicateFileGroup> groups, RssTheme theme,
        string perfectMusicPath)
    {
        Groups = groups;
        _theme = theme;
        Dock = DockStyle.Fill;
        BackColor = theme.Surface;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(22, 12, 22, 16),
            BackColor = theme.Surface
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var rule = new Label
        {
            Dock = DockStyle.Fill,
            Font = theme.Font(8.4f),
            ForeColor = theme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent,
            Text = "判定规则：音乐文件名（不含扩展名）完全相同即为重复。喜欢 / PerfectMusic 曲目保留一份到 PerfectMusic，并同步移动同名 .lrc。"
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 6, 0, 0)
        };
        var scan = MakeButton("扫描", theme.Accent, Color.White);
        scan.Click += (_, _) => ScanRequested?.Invoke(this, EventArgs.Empty);
        var cleanup = MakeButton("清理重复项目", Color.FromArgb(196, 43, 28), Color.White);
        cleanup.Enabled = groups.Count > 0;
        cleanup.Click += (_, _) => CleanupRequested?.Invoke(this, EventArgs.Empty);
        actions.Controls.Add(scan);
        actions.Controls.Add(cleanup);
        toolbar.Controls.Add(rule, 0, 0);
        toolbar.Controls.Add(actions, 1, 0);
        root.Controls.Add(toolbar, 0, 0);

        int protectedGroups = groups.Count(g => g.HasProtectedTrack);
        var summary = new Label
        {
            Dock = DockStyle.Fill,
            Font = theme.Font(9f, FontStyle.Bold),
            ForeColor = theme.Text,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent,
            Text = groups.Count == 0
                ? "没有发现同名重复音乐"
                : $"共 {groups.Count} 组同名文件 · 多余 {groups.Sum(g => g.ExtraCount)} 个 · "
                  + $"{protectedGroups} 组会保留到 PerfectMusic\n"
                  + "PerfectMusic：" + perfectMusicPath
        };
        root.Controls.Add(summary, 0, 1);

        _scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = theme.Surface
        };
        _list = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            BackColor = theme.Surface,
            Padding = new Padding(0, 4, 0, 8)
        };
        _scroll.Controls.Add(_list);
        _scroll.Resize += (_, _) => ResizeCards();
        root.Controls.Add(_scroll, 0, 2);
        Controls.Add(root);

        if (groups.Count == 0)
        {
            _list.Controls.Add(new Label
            {
                Text = "没有同名重复音乐。\n如果音乐库尚未扫描，请先到“全部歌曲”点击“扫描音乐”。",
                AutoSize = false,
                Width = 760,
                Height = 90,
                Font = theme.Font(9.4f),
                ForeColor = theme.TextSecondary,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = theme.Surface
            });
        }
        else
        {
            foreach (var group in groups)
            {
                _list.Controls.Add(BuildGroupCard(group));
            }
        }
        ResizeCards();
    }

    public IReadOnlyList<DuplicateFileGroup> Groups { get; }

    public event EventHandler? ScanRequested;

    public event EventHandler? CleanupRequested;

    private void ResizeCards()
    {
        int width = Math.Max(600, _scroll.ClientSize.Width - 28);
        foreach (Control card in _list.Controls)
        {
            card.Width = width;
        }
    }

    private Control BuildGroupCard(DuplicateFileGroup group)
    {
        var card = new Panel
        {
            BackColor = _theme.SurfaceAlt,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(14, 10, 14, 12),
            AutoSize = true,
            Width = 800
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        var header = new Label
        {
            Text = group.DisplaySummary,
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 28,
            Font = _theme.Font(9.6f, FontStyle.Bold),
            ForeColor = group.HasProtectedTrack ? _theme.Accent : _theme.Text,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(header, 0, 0);
        int rowIndex = 1;
        foreach (var track in group.Tracks)
        {
            bool keep = track.Id == group.Keeper.Id;
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 4,
                AutoSize = true,
                BackColor = keep ? Color.FromArgb(28, _theme.Accent) : Color.Transparent,
                Margin = new Padding(0, 4, 0, 0)
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            row.Controls.Add(new Label
            {
                Text = keep ? "保留" : "删除",
                Dock = DockStyle.Fill,
                Font = _theme.Font(8.6f, FontStyle.Bold),
                ForeColor = keep ? _theme.Accent : Color.FromArgb(196, 43, 28),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            }, 0, 0);
            var info = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            info.Controls.Add(new Label
            {
                Text = $"{track.DisplayTitle} · {track.DisplayArtist} · {track.DisplayAlbum}",
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 22,
                Font = _theme.Font(8.8f),
                ForeColor = _theme.Text,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            });
            info.Controls.Add(new Label
            {
                Text = track.Path,
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 20,
                Font = _theme.Font(7.8f),
                ForeColor = _theme.TextTertiary,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            });
            row.Controls.Add(info, 1, 0);
            row.Controls.Add(new Label
            {
                Text = FormatSize(track.FileSize),
                Dock = DockStyle.Fill,
                Font = _theme.Font(8.2f),
                ForeColor = _theme.TextSecondary,
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent
            }, 2, 0);
            var flags = new List<string>();
            if (track.IsPerfect) flags.Add("PerfectMusic");
            if (track.Liked) flags.Add("喜欢");
            if (track.IsHiRes) flags.Add("Hi-Res");
            if (flags.Count == 0) flags.Add(track.QualityText);
            row.Controls.Add(new Label
            {
                Text = string.Join(" · ", flags),
                Dock = DockStyle.Fill,
                Font = _theme.Font(8f),
                ForeColor = _theme.TextTertiary,
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            }, 3, 0);
            layout.Controls.Add(row, 0, rowIndex++);
        }
        card.Controls.Add(layout);
        return card;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "";
        double mb = bytes / 1024.0 / 1024.0;
        return mb >= 1 ? $"{mb:0.0} MB" : $"{bytes / 1024.0:0} KB";
    }

    private Button MakeButton(string text, Color backColor, Color foreColor)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = false,
            Height = 32,
            Width = TextRenderer.MeasureText(text, _theme.Font(8.8f)).Width + 36,
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor,
            ForeColor = foreColor,
            Font = _theme.Font(8.8f),
            Margin = new Padding(10, 0, 0, 0),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }
}
