using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace MiniMonitor.Rss;

/// <summary>FluentRSS 阅读器选项窗口：应用偏好、阅读体验、订阅源、分组、规则、服务、数据与关于。</summary>
public sealed class RssSettingsForm : Form
{
    private readonly AppConfig _config;
    private readonly RssStore _store;
    private readonly RssSyncService _sync;
    private readonly RssTheme _theme;
    private readonly Action? _onChanged;
    private readonly ListBox _nav;
    private readonly Panel _host;
    private readonly List<Panel> _pages = new();
    private DataGridView? _feedsGrid;
    private DataGridView? _rulesGrid;
    private ListBox? _groupsList;
    private Label? _serviceStatus;
    private ComboBox? _serviceTypeCombo;
    private TextBox? _endpointBox;
    private TextBox? _usernameBox;
    private TextBox? _passwordBox;
    private TextBox? _appIdBox;
    private TextBox? _appKeyBox;
    private NumericUpDown? _serviceLimitBox;
    private CheckBox? _serviceGroupsCheck;
    private Label? _syncStatusLabel;
    private Label? _syncErrorLabel;
    private Label? _ruleTestResult;
    private TextBox? _ruleTestTitle;
    private TextBox? _ruleTestContent;
    private Label? _dataInfoLabel;
    private bool _initializing = true;

    public RssSettingsForm(AppConfig config, RssStore store, RssSyncService sync, RssTheme theme,
        Action? onChanged)
    {
        _config = config;
        _store = store;
        _sync = sync;
        _theme = theme;
        _onChanged = onChanged;

        Text = "FluentRSS 阅读器 · 选项";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1080, 780);
        MinimumSize = new Size(900, 640);
        BackColor = theme.Background;
        ForeColor = theme.Text;
        Font = theme.Font(9.2f);
        MaximizeBox = true;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = theme.Surface };
        header.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(theme.Border);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
            using var titleFont = theme.Font(17f, FontStyle.Bold);
            TextRenderer.DrawText(e.Graphics, "选项", titleFont,
                new Rectangle(28, 14, header.Width - 56, 30), theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics,
                "Fluent Reader v1.3.2 复刻 · 订阅、阅读、规则与服务同步", theme.Font(8.8f),
                new Rectangle(30, 44, header.Width - 60, 22), theme.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        };

        _nav = new ListBox
        {
            Dock = DockStyle.Left,
            Width = 210,
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 46,
            BackColor = theme.Sidebar,
            ForeColor = theme.Text,
            Font = theme.Font(9.4f),
            IntegralHeight = false
        };
        _nav.Items.AddRange(new object[]
        {
            "应用偏好", "阅读体验", "订阅源", "分组与排序", "自动规则", "同步服务", "数据管理", "关于"
        });
        _nav.DrawItem += DrawNavItem;
        _nav.SelectedIndexChanged += (_, _) => ShowPage(_nav.SelectedIndex);

        _host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = theme.Background,
            Padding = new Padding(22, 16, 22, 22)
        };

        Controls.Add(_host);
        Controls.Add(_nav);
        Controls.Add(header);

        _pages.Add(BuildAppPage());
        _pages.Add(BuildReadingPage());
        _pages.Add(BuildSourcesPage());
        _pages.Add(BuildGroupsPage());
        _pages.Add(BuildRulesPage());
        _pages.Add(BuildServicePage());
        _pages.Add(BuildDataPage());
        _pages.Add(BuildAboutPage());
        foreach (var page in _pages)
        {
            page.Visible = false;
            _host.Controls.Add(page);
        }
        _nav.SelectedIndex = 0;
        _initializing = false;
        Shown += (_, _) => ApplyDarkTitleBar();
    }

    private void DrawNavItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0)
        {
            return;
        }
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        bool selected = e.Index == _nav.SelectedIndex;
        using (var background = new SolidBrush(_theme.Sidebar))
        {
            g.FillRectangle(background, e.Bounds);
        }
        var itemRect = new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 4,
            e.Bounds.Width - 20, e.Bounds.Height - 8);
        if (selected)
        {
            using var path = ShutdownTheme.RoundRect(itemRect, 7f);
            using var brush = new SolidBrush(_theme.AccentSoft);
            g.FillPath(brush, path);
            using var accent = new SolidBrush(_theme.Accent);
            using var accentPath = ShutdownTheme.RoundRect(
                new RectangleF(itemRect.Left, itemRect.Top + 8, 3, itemRect.Height - 16), 1.5f);
            g.FillPath(accent, accentPath);
        }
        else if ((e.State & DrawItemState.Selected) != 0)
        {
            using var path = ShutdownTheme.RoundRect(itemRect, 7f);
            using var brush = new SolidBrush(_theme.Hover);
            g.FillPath(brush, path);
        }
        string[] glyphs = { "⚙", "▤", "▥", "▦", "⚑", "☁", "⛁", "ⓘ" };
        TextRenderer.DrawText(g, glyphs[e.Index], _theme.Font(11f),
            new Rectangle(itemRect.Left + 12, itemRect.Top, 22, itemRect.Height),
            selected ? _theme.Accent : _theme.TextSecondary,
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, _nav.Items[e.Index].ToString(), _theme.Font(9.4f),
            new Rectangle(itemRect.Left + 42, itemRect.Top, itemRect.Width - 50, itemRect.Height),
            selected ? _theme.Accent : _theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    private void ShowPage(int index)
    {
        if (index < 0 || index >= _pages.Count)
        {
            return;
        }
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].Visible = i == index;
        }
        RefreshPage(index);
        _nav.Invalidate();
    }

    private void RefreshPage(int index)
    {
        switch (index)
        {
            case 2:
                RefreshFeedsGrid();
                break;
            case 3:
                RefreshGroupsList();
                break;
            case 4:
                RefreshRulesGrid();
                break;
            case 5:
                RefreshServiceStatus();
                break;
            case 6:
                RefreshDataInfo();
                break;
        }
        RefreshSyncLabels();
    }

    // ================= 页面骨架 =================

    private FlowLayoutPanel CreatePage(string title, string subtitle)
    {
        var page = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = _theme.Background,
            Padding = new Padding(0, 0, 12, 20),
            Visible = false
        };
        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = _theme.Background,
            Padding = new Padding(0)
        };
        var header = new Label
        {
            Text = title,
            AutoSize = false,
            Height = 38,
            Width = 800,
            Font = _theme.Font(15f, FontStyle.Bold),
            ForeColor = _theme.Text,
            Margin = new Padding(0, 0, 0, 2)
        };
        var desc = new Label
        {
            Text = subtitle,
            AutoSize = false,
            Height = 30,
            Width = 800,
            Font = _theme.Font(8.8f),
            ForeColor = _theme.TextSecondary,
            Margin = new Padding(0, 0, 0, 14)
        };
        stack.Controls.Add(header);
        stack.Controls.Add(desc);
        page.Controls.Add(stack);
        page.Tag = stack;
        page.Resize += (_, _) =>
        {
            int width = Math.Max(300, page.ClientSize.Width - page.Padding.Horizontal - 18);
            stack.Width = width;
            header.Width = width;
            desc.Width = width;
            foreach (Control control in stack.Controls)
            {
                control.Width = width - 6;
            }
        };
        return stack;
    }

    private TableLayoutPanel NewCard()
    {
        var card = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = _theme.Surface,
            Padding = new Padding(18, 8, 18, 8),
            Margin = new Padding(0, 0, 0, 16),
            Dock = DockStyle.None
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(_theme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        return card;
    }

    private Label SectionTitle(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Height = 30,
            Font = _theme.Font(10.4f, FontStyle.Bold),
            ForeColor = _theme.Text,
            Margin = new Padding(0, 6, 0, 6)
        };
    }

    private static void AddFullWidthRow(TableLayoutPanel card, Control control, int absoluteHeight = 0)
    {
        int row = card.RowCount;
        card.RowCount = row + 1;
        card.RowStyles.Add(absoluteHeight > 0
            ? new RowStyle(SizeType.Absolute, absoluteHeight)
            : new RowStyle(SizeType.AutoSize));
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 2, 0, 2);
        card.Controls.Add(control, 0, row);
        card.SetColumnSpan(control, 2);
    }

    private void AddCardRow(TableLayoutPanel card, string title, string? description, Control control)
    {
        int row = card.RowCount++;
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, description == null ? 46 : 58));
        var left = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = description == null ? 1 : 2,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        left.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            Font = _theme.Font(9.3f),
            ForeColor = _theme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        if (description != null)
        {
            left.Controls.Add(new Label
            {
                Text = description,
                Dock = DockStyle.Fill,
                Font = _theme.Font(8.2f),
                ForeColor = _theme.TextTertiary,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 1);
        }
        control.Anchor = AnchorStyles.Right;
        control.Margin = new Padding(8, 0, 0, 0);
        card.Controls.Add(left, 0, row);
        card.Controls.Add(control, 1, row);
    }

    private ComboBox MakeCombo(params string[] items)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Width = 230,
            BackColor = _theme.InputBackground,
            ForeColor = _theme.Text,
            Font = _theme.Font(9f)
        };
        combo.Items.AddRange(items);
        combo.SelectedIndex = 0;
        return combo;
    }

    private NumericUpDown MakeNumeric(int min, int max, int value)
    {
        return new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Width = 120,
            BackColor = _theme.InputBackground,
            ForeColor = _theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = _theme.Font(9f)
        };
    }

    private CheckBox MakeCheck(string text, bool value)
    {
        return new CheckBox
        {
            Text = text,
            Checked = value,
            AutoSize = true,
            ForeColor = _theme.Text,
            Font = _theme.Font(9f)
        };
    }

    private Button MakeButton(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            Height = 34,
            AutoSize = false,
            Width = 100,
            BackColor = primary ? _theme.Accent : _theme.SurfaceAlt,
            ForeColor = primary ? (_theme.IsDark ? Color.FromArgb(16, 28, 38) : Color.White) : _theme.Text,
            Font = _theme.Font(9f, primary ? FontStyle.Bold : FontStyle.Regular),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 6, 0)
        };
        button.FlatAppearance.BorderColor = primary ? _theme.Accent : _theme.Border;
        button.FlatAppearance.MouseOverBackColor = primary ? _theme.AccentHover : _theme.Hover;
        return button;
    }

    private FlowLayoutPanel ButtonRow(params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = _theme.Background,
            Margin = new Padding(0, 0, 0, 10)
        };
        row.Controls.AddRange(controls);
        return row;
    }

    // ================= 应用偏好 =================

    private Panel BuildAppPage()
    {
        var stack = CreatePage("应用偏好", "外观、自动抓取频率与启动行为。默认每 60 分钟在后台同步一次 RSS。");
        stack.Controls.Add(SectionTitle("外观"));

        var appearance = NewCard();
        var themeCombo = MakeCombo("跟随系统", "浅色模式", "深色模式");
        themeCombo.SelectedIndex = _config.Rss.Theme switch { "light" => 1, "dark" => 2, _ => 0 };
        themeCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_initializing) return;
            _config.Rss.Theme = themeCombo.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "system" };
            _config.Save();
            Note("主题已保存，关闭选项窗口后应用到阅读器。");
            _onChanged?.Invoke();
        };
        AddCardRow(appearance, "应用主题", "跟随系统 / 浅色 / 深色，阅读器与内置文章视图同步切换", themeCombo);

        var viewCombo = MakeCombo("列表视图", "卡片视图", "杂志视图", "紧凑视图");
        viewCombo.SelectedIndex = _config.Rss.ViewMode switch { "cards" => 1, "magazine" => 2, "compact" => 3, _ => 0 };
        viewCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_initializing) return;
            _config.Rss.ViewMode = viewCombo.SelectedIndex switch { 1 => "cards", 2 => "magazine", 3 => "compact", _ => "list" };
            _config.Save();
            _onChanged?.Invoke();
        };
        AddCardRow(appearance, "文章列表视图", "Fluent Reader 的四种视图：列表、卡片、杂志、紧凑", viewCombo);

        var fontBox = MakeNumeric(11, 32, _config.Rss.FontSize);
        fontBox.ValueChanged += (_, _) =>
        {
            if (_initializing) return;
            _config.Rss.FontSize = (int)fontBox.Value;
            _config.Save();
            _onChanged?.Invoke();
        };
        AddCardRow(appearance, "正文字号", "内置文章视图的正文大小（11–32 px）", fontBox);

        var snippetCheck = MakeCheck("显示摘要", _config.Rss.ShowSnippet);
        snippetCheck.CheckedChanged += (_, _) =>
        {
            if (_initializing) return;
            _config.Rss.ShowSnippet = snippetCheck.Checked;
            _config.Save();
            _onChanged?.Invoke();
        };
        AddCardRow(appearance, "摘要", "在文章列表中显示正文摘要", snippetCheck);

        var coverCheck = MakeCheck("显示封面", _config.Rss.ShowCover);
        coverCheck.CheckedChanged += (_, _) =>
        {
            if (_initializing) return;
            _config.Rss.ShowCover = coverCheck.Checked;
            _config.Save();
            _onChanged?.Invoke();
        };
        AddCardRow(appearance, "封面图", "在列表中显示文章封面缩略图", coverCheck);

        var fadeCheck = MakeCheck("淡化已读文章", _config.Rss.FadeRead);
        fadeCheck.CheckedChanged += (_, _) =>
        {
            if (_initializing) return;
            _config.Rss.FadeRead = fadeCheck.Checked;
            _config.Save();
            _onChanged?.Invoke();
        };
        AddCardRow(appearance, "已读样式", "已读文章标题变淡，减少视觉干扰", fadeCheck);
        stack.Controls.Add(appearance);

        stack.Controls.Add(SectionTitle("自动抓取"));
        var syncCard = NewCard();
        var intervalCombo = MakeCombo(
            "从不", "5 分钟", "10 分钟", "15 分钟", "30 分钟", "45 分钟",
            "1 小时（默认）", "2 小时", "4 小时", "8 小时", "12 小时", "24 小时");
        int[] intervals = { 0, 5, 10, 15, 30, 45, 60, 120, 240, 480, 720, 1440 };
        int intervalIndex = Array.IndexOf(intervals, _config.Rss.SyncIntervalMinutes);
        intervalCombo.SelectedIndex = intervalIndex >= 0 ? intervalIndex : 6;
        intervalCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_initializing) return;
            int index = Math.Clamp(intervalCombo.SelectedIndex, 0, intervals.Length - 1);
            _config.Rss.SyncIntervalMinutes = intervals[index];
            _config.Save();
            _sync.Reschedule();
            RefreshSyncLabels();
            Note("自动抓取频率已保存。关闭阅读器后仍会按该间隔在后台同步。");
        };
        AddCardRow(syncCard, "自动抓取频率", "默认 1 小时；关闭阅读器窗口后仍会后台执行", intervalCombo);

        var startupSyncCheck = MakeCheck("启动后自动同步一次", _config.Rss.SyncOnStartup);
        startupSyncCheck.CheckedChanged += (_, _) =>
        {
            if (_initializing) return;
            _config.Rss.SyncOnStartup = startupSyncCheck.Checked;
            _config.Save();
        };
        AddCardRow(syncCard, "启动同步", "MiniMonitor 启动后自动抓取全部订阅源", startupSyncCheck);

        var notifyCheck = MakeCheck("后台抓取时发送通知", _config.Rss.Notifications);
        notifyCheck.CheckedChanged += (_, _) =>
        {
            if (_initializing) return;
            _config.Rss.Notifications = notifyCheck.Checked;
            _config.Save();
        };
        AddCardRow(syncCard, "桌面通知", "后台同步到新文章时弹出托盘通知", notifyCheck);

        _syncStatusLabel = new Label
        {
            Text = "",
            AutoSize = true,
            Font = _theme.Font(8.6f),
            ForeColor = _theme.TextSecondary,
            Margin = new Padding(2, 2, 0, 0)
        };
        AddFullWidthRow(syncCard, _syncStatusLabel, 26);
        _syncErrorLabel = new Label
        {
            Text = "",
            AutoSize = true,
            MaximumSize = new Size(720, 120),
            Font = _theme.Font(8.4f),
            ForeColor = _theme.TextTertiary,
            Margin = new Padding(2, 0, 0, 4)
        };
        AddFullWidthRow(syncCard, _syncErrorLabel);
        stack.Controls.Add(syncCard);

        stack.Controls.Add(SectionTitle("界面语言"));
        var languageCard = NewCard();
        var language = new Label
        {
            Text = "简体中文（内置）",
            AutoSize = true,
            ForeColor = _theme.TextSecondary,
            Font = _theme.Font(9f),
            Margin = new Padding(0, 10, 0, 10)
        };
        AddCardRow(languageCard, "语言", "复刻版内置简体中文界面，保持 Fluent Reader 的阅读习惯", language);
        stack.Controls.Add(languageCard);

        RefreshSyncLabels();
        return (Panel)stack.Parent!;
    }

    // ================= 阅读体验 =================

    private Panel BuildReadingPage()
    {
        var stack = CreatePage("阅读体验", "打开行为、正文加载方式与本地文章保留策略。");
        var card = NewCard();

        var markOpen = MakeCheck("打开文章时标为已读", _config.Rss.MarkReadOnOpen);
        markOpen.CheckedChanged += (_, _) =>
        {
            if (_initializing) return;
            _config.Rss.MarkReadOnOpen = markOpen.Checked;
            _config.Save();
        };
        AddCardRow(card, "打开即已读", "单击列表中的文章后自动标为已读", markOpen);

        var markScroll = MakeCheck("滚动到正文底部时标为已读", _config.Rss.MarkReadOnScroll);
        markScroll.CheckedChanged += (_, _) =>
        {
            if (_initializing) return;
            _config.Rss.MarkReadOnScroll = markScroll.Checked;
            _config.Save();
            _onChanged?.Invoke();
        };
        AddCardRow(card, "滚动标记", "阅读到文章底部后自动标为已读", markScroll);

        var openCombo = MakeCombo("内置文章视图（推荐）", "加载原文网页");
        openCombo.SelectedIndex = _config.Rss.DefaultOpenMode == "webpage" ? 1 : 0;
        openCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_initializing) return;
            _config.Rss.DefaultOpenMode = openCombo.SelectedIndex == 1 ? "webpage" : "content";
            _config.Save();
        };
        AddCardRow(card, "默认打开方式", "内置文章视图排版更干净；加载网页保留原始样式", openCombo);

        var retention = MakeNumeric(0, 100000, _config.Rss.MaxArticlesPerFeed);
        retention.Increment = 100;
        retention.ValueChanged += (_, _) =>
        {
            if (_initializing) return;
            _config.Rss.MaxArticlesPerFeed = (int)retention.Value;
            _config.Save();
        };
        AddCardRow(card, "每个订阅源保留文章数", "0 表示不限制；超出后自动清理最旧的未星标文章", retention);

        var regexCheck = MakeCheck("搜索默认使用正则表达式", _config.Rss.RegexSearch);
        regexCheck.CheckedChanged += (_, _) =>
        {
            if (_initializing) return;
            _config.Rss.RegexSearch = regexCheck.Checked;
            _config.Save();
            _onChanged?.Invoke();
        };
        AddCardRow(card, "正则搜索", "与 Fluent Reader 一致，可用 .* 等正则筛选文章", regexCheck);

        var caseCheck = MakeCheck("搜索区分大小写", _config.Rss.CaseSensitiveSearch);
        caseCheck.CheckedChanged += (_, _) =>
        {
            if (_initializing) return;
            _config.Rss.CaseSensitiveSearch = caseCheck.Checked;
            _config.Save();
        };
        AddCardRow(card, "区分大小写", "正则与普通搜索均生效", caseCheck);

        var contentCheck = MakeCheck("在正文中搜索", _config.Rss.SearchContent);
        contentCheck.CheckedChanged += (_, _) =>
        {
            if (_initializing) return;
            _config.Rss.SearchContent = contentCheck.Checked;
            _config.Save();
        };
        AddCardRow(card, "全文搜索", "搜索结果包含正文内容，而不仅是标题", contentCheck);
        stack.Controls.Add(card);

        stack.Controls.Add(SectionTitle("快捷键"));
        var shortcutCard = NewCard();
        var shortcuts = new Label
        {
            Text = "F1 显示/隐藏订阅栏 · F2 搜索 · F5 刷新 · F6 全部已读 · F7 通知开关\n"
                + "F8 切换视图 · F9 选项 · ↑↓ 选择文章 · Enter/Space 打开 · M 已读 · S 星标 · H 隐藏 · B 浏览器",
            AutoSize = true,
            MaximumSize = new Size(760, 160),
            Font = _theme.Font(8.8f),
            ForeColor = _theme.TextSecondary,
            Margin = new Padding(0, 6, 0, 6)
        };
        shortcutCard.RowCount = 1;
        shortcutCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shortcutCard.Controls.Add(shortcuts, 0, 0);
        shortcutCard.SetColumnSpan(shortcuts, 2);
        stack.Controls.Add(shortcutCard);
        return (Panel)stack.Parent!;
    }

    // ================= 订阅源 =================

    private Panel BuildSourcesPage()
    {
        var page = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _theme.Background,
            Visible = false
        };
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = _theme.Background
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        var header = new Label
        {
            Text = "订阅源管理",
            Dock = DockStyle.Fill,
            Font = _theme.Font(15f, FontStyle.Bold),
            ForeColor = _theme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(header, 0, 0);

        var add = MakeButton("添加订阅源", primary: true);
        var edit = MakeButton("编辑");
        var delete = MakeButton("删除");
        var refresh = MakeButton("立即刷新");
        var import = MakeButton("导入 OPML");
        var export = MakeButton("导出 OPML");
        add.Click += (_, _) => AddFeed();
        edit.Click += (_, _) => EditSelectedFeed();
        delete.Click += (_, _) => DeleteSelectedFeed();
        refresh.Click += (_, _) =>
        {
            _ = _sync.SyncAsync(manual: true);
            Note("已开始刷新全部订阅源…");
        };
        import.Click += (_, _) => ImportOpml();
        export.Click += (_, _) => ExportOpml();
        var toolbar = ButtonRow(add, edit, delete, refresh, import, export);
        toolbar.Dock = DockStyle.Fill;
        root.Controls.Add(toolbar, 0, 1);

        _feedsGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = _theme.Surface,
            BorderStyle = BorderStyle.FixedSingle,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            Font = _theme.Font(9f),
            GridColor = _theme.Border,
            EnableHeadersVisualStyles = false
        };
        StyleGrid(_feedsGrid);
        _feedsGrid.Columns.Add("title", "名称");
        _feedsGrid.Columns.Add("url", "订阅地址");
        _feedsGrid.Columns.Add("group", "分组");
        _feedsGrid.Columns.Add("unread", "未读");
        _feedsGrid.Columns.Add("updated", "最后更新");
        _feedsGrid.Columns.Add("status", "状态");
        _feedsGrid.Columns["title"].FillWeight = 24;
        _feedsGrid.Columns["url"].FillWeight = 38;
        _feedsGrid.Columns["group"].FillWeight = 12;
        _feedsGrid.Columns["unread"].FillWeight = 8;
        _feedsGrid.Columns["updated"].FillWeight = 12;
        _feedsGrid.Columns["status"].FillWeight = 10;
        _feedsGrid.CellDoubleClick += (_, _) => EditSelectedFeed();
        root.Controls.Add(_feedsGrid, 0, 2);

        var hint = new Label
        {
            Text = "提示：浏览器导入书签时请使用 OPML；支持 RSS、Atom、RDF 与 JSON Feed。",
            Dock = DockStyle.Fill,
            Font = _theme.Font(8.3f),
            ForeColor = _theme.TextTertiary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(hint, 0, 3);
        page.Controls.Add(root);
        return page;
    }

    private void StyleGrid(DataGridView grid)
    {
        grid.ColumnHeadersDefaultCellStyle.BackColor = _theme.IsDark
            ? Color.FromArgb(50, 50, 50) : Color.FromArgb(240, 244, 248);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = _theme.Text;
        grid.ColumnHeadersDefaultCellStyle.Font = _theme.Font(8.8f, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = grid.ColumnHeadersDefaultCellStyle.BackColor;
        grid.DefaultCellStyle.BackColor = _theme.Surface;
        grid.DefaultCellStyle.ForeColor = _theme.Text;
        grid.DefaultCellStyle.SelectionBackColor = _theme.Selection;
        grid.DefaultCellStyle.SelectionForeColor = _theme.Text;
        grid.AlternatingRowsDefaultCellStyle.BackColor = _theme.IsDark
            ? Color.FromArgb(43, 43, 43) : Color.FromArgb(250, 251, 252);
        grid.RowTemplate.Height = 30;
        grid.ColumnHeadersHeight = 34;
    }

    private void RefreshFeedsGrid()
    {
        if (_feedsGrid == null)
        {
            return;
        }
        var groups = _store.GetGroups();
        var feeds = _store.GetFeeds();
        _feedsGrid.Rows.Clear();
        foreach (var feed in feeds)
        {
            string group = groups.FirstOrDefault(g => g.Gid == feed.Gid)?.Name ?? "未分组";
            string status = !string.IsNullOrWhiteSpace(feed.LastError)
                ? "错误：" + feed.LastError
                : feed.LastFetch > 0
                    ? "正常"
                    : "尚未抓取";
            string updated = feed.LastFetch > 0
                ? DateTimeOffset.FromUnixTimeSeconds(feed.LastFetch).ToLocalTime().ToString("MM-dd HH:mm")
                : "—";
            int index = _feedsGrid.Rows.Add(feed.Title, feed.Url, group, feed.Unread.ToString(),
                updated, status);
            _feedsGrid.Rows[index].Tag = feed.Sid;
            if (!string.IsNullOrWhiteSpace(feed.LastError))
            {
                _feedsGrid.Rows[index].DefaultCellStyle.ForeColor =
                    _theme.IsDark ? Color.FromArgb(255, 170, 170) : Color.FromArgb(170, 30, 30);
            }
        }
    }

    private RssFeed? SelectedFeed()
    {
        if (_feedsGrid == null || _feedsGrid.CurrentRow?.Tag is not int sid)
        {
            return null;
        }
        return _store.GetFeed(sid);
    }

    private void AddFeed()
    {
        using var http = Http.CreateClient();
        using var dialog = new AddFeedDialog(_theme, _store, _store.GetGroups(), http);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _onChanged?.Invoke();
            RefreshFeedsGrid();
            _ = _sync.SyncFeedAsync(dialog.AddedSid);
        }
    }

    private void EditSelectedFeed()
    {
        var feed = SelectedFeed();
        if (feed == null)
        {
            return;
        }
        using var dialog = new EditFeedDialog(_theme, _store, feed, _store.GetGroups());
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _onChanged?.Invoke();
            RefreshFeedsGrid();
            RefreshGroupsList();
        }
    }

    private void DeleteSelectedFeed()
    {
        var feed = SelectedFeed();
        if (feed == null)
        {
            return;
        }
        var result = MessageBox.Show(this,
            $"确定删除订阅源“{feed.Title}”吗？\n\n这会同时删除该订阅源保存的全部文章，且无法撤销。",
            "删除订阅源", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (result != DialogResult.Yes)
        {
            return;
        }
        _store.DeleteFeed(feed.Sid);
        _onChanged?.Invoke();
        RefreshFeedsGrid();
        RefreshGroupsList();
    }

    private void ImportOpml()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "导入 OPML 订阅源",
            Filter = "OPML 文件|*.opml;*.xml|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            string xml = File.ReadAllText(dialog.FileName, Encoding.UTF8);
            var entries = RssParser.ImportOpml(xml);
            if (entries.Count == 0)
            {
                MessageBox.Show(this, "文件中没有可导入的订阅源。", "导入 OPML",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var groups = _store.GetGroups();
            int added = 0;
            int skipped = 0;
            foreach (var entry in entries)
            {
                int? gid = null;
                if (!string.IsNullOrWhiteSpace(entry.GroupName))
                {
                    var group = groups.FirstOrDefault(g =>
                        string.Equals(g.Name, entry.GroupName, StringComparison.OrdinalIgnoreCase));
                    if (group == null)
                    {
                        int newGid = _store.AddGroup(entry.GroupName!);
                        group = new RssGroup { Gid = newGid, Name = entry.GroupName! };
                        groups.Add(group);
                    }
                    gid = group.Gid;
                }
                entry.Feed.Gid = gid;
                _store.AddFeed(entry.Feed, out bool created);
                if (created)
                {
                    added++;
                }
                else
                {
                    skipped++;
                }
            }
            _onChanged?.Invoke();
            RefreshFeedsGrid();
            MessageBox.Show(this,
                $"导入完成：新增 {added} 个订阅源，已存在 {skipped} 个。\n同步将在后台自动开始。",
                "导入 OPML", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _ = _sync.SyncAsync(manual: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导入 OPML 失败：" + ex.Message, "导入 OPML",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ExportOpml()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "导出 OPML 订阅源",
            Filter = "OPML 文件|*.opml",
            FileName = "FluentRSS订阅源_" + DateTime.Now.ToString("yyyyMMdd") + ".opml"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            string xml = RssParser.BuildOpml(_store.GetGroups(), _store.GetFeeds());
            File.WriteAllText(dialog.FileName, xml, new UTF8Encoding(false));
            MessageBox.Show(this, "OPML 已导出：\n" + dialog.FileName, "导出 OPML",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出 OPML 失败：" + ex.Message, "导出 OPML",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ================= 分组 =================

    private Panel BuildGroupsPage()
    {
        var page = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _theme.Background,
            Visible = false
        };
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = _theme.Background
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        root.Controls.Add(new Label
        {
            Text = "分组与排序",
            Dock = DockStyle.Fill,
            Font = _theme.Font(15f, FontStyle.Bold),
            ForeColor = _theme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var add = MakeButton("新建分组", primary: true);
        var rename = MakeButton("重命名");
        var delete = MakeButton("删除分组");
        var up = MakeButton("上移");
        var down = MakeButton("下移");
        add.Click += (_, _) => AddGroup();
        rename.Click += (_, _) => RenameGroup();
        delete.Click += (_, _) => DeleteGroup();
        up.Click += (_, _) => MoveGroup(-1);
        down.Click += (_, _) => MoveGroup(1);
        var toolbar = ButtonRow(add, rename, delete, up, down);
        toolbar.Dock = DockStyle.Fill;
        root.Controls.Add(toolbar, 0, 1);

        _groupsList = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = _theme.Surface,
            ForeColor = _theme.Text,
            Font = _theme.Font(9.6f),
            IntegralHeight = false
        };
        _groupsList.DoubleClick += (_, _) => RenameGroup();
        root.Controls.Add(_groupsList, 0, 2);

        root.Controls.Add(new Label
        {
            Text = "订阅源可以移动到任意分组；删除分组不会删除其中的订阅源，它们会回到“未分组”。",
            Dock = DockStyle.Fill,
            Font = _theme.Font(8.3f),
            ForeColor = _theme.TextTertiary,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 3);
        page.Controls.Add(root);
        return page;
    }

    private void RefreshGroupsList()
    {
        if (_groupsList == null)
        {
            return;
        }
        var groups = _store.GetGroups();
        var feeds = _store.GetFeeds();
        _groupsList.Items.Clear();
        foreach (var group in groups)
        {
            int count = feeds.Count(f => f.Gid == group.Gid);
            _groupsList.Items.Add(new GroupItem(group.Gid, group.Name, count));
        }
        if (_groupsList.Items.Count == 0)
        {
            _groupsList.Items.Add("（还没有分组）");
        }
    }

    private sealed record GroupItem(int Gid, string Name, int Count)
    {
        public override string ToString() => $"{Name}    ·    {Count} 个订阅源";
    }

    private int? SelectedGroupId()
    {
        return _groupsList?.SelectedItem is GroupItem item ? item.Gid : null;
    }

    private void AddGroup()
    {
        using var dialog = new GroupNameDialog(_theme, "新建分组", "");
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _store.AddGroup(dialog.GroupName);
            _onChanged?.Invoke();
            RefreshGroupsList();
            RefreshFeedsGrid();
        }
    }

    private void RenameGroup()
    {
        int? gid = SelectedGroupId();
        if (gid == null)
        {
            return;
        }
        var group = _store.GetGroups().FirstOrDefault(g => g.Gid == gid);
        if (group == null)
        {
            return;
        }
        using var dialog = new GroupNameDialog(_theme, "重命名分组", group.Name);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _store.RenameGroup(group.Gid, dialog.GroupName);
            _onChanged?.Invoke();
            RefreshGroupsList();
            RefreshFeedsGrid();
        }
    }

    private void DeleteGroup()
    {
        int? gid = SelectedGroupId();
        if (gid == null)
        {
            return;
        }
        var group = _store.GetGroups().FirstOrDefault(g => g.Gid == gid);
        if (group == null)
        {
            return;
        }
        var result = MessageBox.Show(this,
            $"确定删除分组“{group.Name}”吗？\n其中的订阅源会移动到“未分组”，不会删除订阅源。",
            "删除分组", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (result != DialogResult.Yes)
        {
            return;
        }
        _store.DeleteGroup(group.Gid);
        _onChanged?.Invoke();
        RefreshGroupsList();
        RefreshFeedsGrid();
    }

    private void MoveGroup(int delta)
    {
        int? gid = SelectedGroupId();
        if (gid == null)
        {
            return;
        }
        var groups = _store.GetGroups();
        int index = groups.FindIndex(g => g.Gid == gid);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= groups.Count)
        {
            return;
        }
        (groups[index], groups[target]) = (groups[target], groups[index]);
        _store.ReorderGroups(groups.Select(g => g.Gid).ToList());
        _onChanged?.Invoke();
        RefreshGroupsList();
        if (_groupsList != null)
        {
            _groupsList.SelectedIndex = target;
        }
    }

    // ================= 规则 =================

    private Panel BuildRulesPage()
    {
        var page = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _theme.Background,
            Visible = false
        };
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = _theme.Background
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        root.Controls.Add(new Label
        {
            Text = "自动规则",
            Dock = DockStyle.Fill,
            Font = _theme.Font(15f, FontStyle.Bold),
            ForeColor = _theme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var add = MakeButton("添加规则", primary: true);
        var edit = MakeButton("编辑");
        var delete = MakeButton("删除");
        add.Click += (_, _) => AddRule();
        edit.Click += (_, _) => EditRule();
        delete.Click += (_, _) => DeleteRule();
        var toolbar = ButtonRow(add, edit, delete);
        toolbar.Dock = DockStyle.Fill;
        root.Controls.Add(toolbar, 0, 1);

        _rulesGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = _theme.Surface,
            BorderStyle = BorderStyle.FixedSingle,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            Font = _theme.Font(9f),
            GridColor = _theme.Border,
            EnableHeadersVisualStyles = false
        };
        StyleGrid(_rulesGrid);
        _rulesGrid.Columns.Add("target", "作用范围");
        _rulesGrid.Columns.Add("pattern", "正则表达式");
        _rulesGrid.Columns.Add("field", "匹配范围");
        _rulesGrid.Columns.Add("condition", "条件");
        _rulesGrid.Columns.Add("actions", "动作");
        _rulesGrid.Columns["target"].FillWeight = 22;
        _rulesGrid.Columns["pattern"].FillWeight = 32;
        _rulesGrid.Columns["field"].FillWeight = 14;
        _rulesGrid.Columns["condition"].FillWeight = 12;
        _rulesGrid.Columns["actions"].FillWeight = 26;
        _rulesGrid.CellDoubleClick += (_, _) => EditRule();
        root.Controls.Add(_rulesGrid, 0, 2);

        var testPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = _theme.Surface,
            Padding = new Padding(14, 8, 14, 8)
        };
        var testLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            BackColor = _theme.Surface
        };
        testLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        testLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        testLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        testLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        testLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        testLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        testLayout.Controls.Add(new Label
        {
            Text = "规则测试",
            Dock = DockStyle.Fill,
            Font = _theme.Font(9f, FontStyle.Bold),
            ForeColor = _theme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        testLayout.SetColumnSpan(testLayout.GetControlFromPosition(0, 0)!, 3);
        _ruleTestTitle = new TextBox
        {
            Dock = DockStyle.Fill,
            BackColor = _theme.InputBackground,
            ForeColor = _theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "输入测试文章标题"
        };
        _ruleTestContent = new TextBox
        {
            Dock = DockStyle.Fill,
            BackColor = _theme.InputBackground,
            ForeColor = _theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "输入测试正文（可留空）"
        };
        var testButton = MakeButton("测试");
        testButton.Dock = DockStyle.Fill;
        testButton.Click += (_, _) => TestRules();
        testLayout.Controls.Add(_ruleTestTitle, 0, 1);
        testLayout.Controls.Add(_ruleTestContent, 1, 1);
        testLayout.Controls.Add(testButton, 2, 1);
        _ruleTestResult = new Label
        {
            Text = "规则按顺序执行，可叠加；测试结果仅用于检查匹配情况。",
            Dock = DockStyle.Fill,
            Font = _theme.Font(8.5f),
            ForeColor = _theme.TextSecondary,
            TextAlign = ContentAlignment.TopLeft
        };
        testLayout.Controls.Add(_ruleTestResult, 0, 2);
        testLayout.SetColumnSpan(_ruleTestResult, 3);
        testPanel.Controls.Add(testLayout);
        root.Controls.Add(testPanel, 0, 3);

        root.Controls.Add(new Label
        {
            Text = "示例：正则 “广告|推广|sponsor” + 动作“隐藏文章”，即可自动过滤推广内容。",
            Dock = DockStyle.Fill,
            Font = _theme.Font(8.3f),
            ForeColor = _theme.TextTertiary,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 4);
        page.Controls.Add(root);
        return page;
    }

    private void RefreshRulesGrid()
    {
        if (_rulesGrid == null)
        {
            return;
        }
        var groups = _store.GetGroups();
        var feeds = _store.GetFeeds();
        _rulesGrid.Rows.Clear();
        for (int i = 0; i < _config.Rss.Rules.Count; i++)
        {
            var rule = _config.Rss.Rules[i];
            string target = rule.Target switch
            {
                "all" => "全部订阅源",
                _ when rule.Target.StartsWith("group:") => "分组：" +
                    (groups.FirstOrDefault(g => g.Gid.ToString() == rule.Target[6..])?.Name ?? rule.Target),
                _ when rule.Target.StartsWith("feed:") => "订阅源：" +
                    (feeds.FirstOrDefault(f => f.Sid.ToString() == rule.Target[5..])?.Title ?? rule.Target),
                _ => rule.Target
            };
            int index = _rulesGrid.Rows.Add(target, rule.Pattern, rule.FieldText,
                rule.Match ? "匹配" : "不匹配", rule.ActionText);
            _rulesGrid.Rows[index].Tag = i;
        }
    }

    private void AddRule()
    {
        var rule = new RssRule();
        using var dialog = new RuleEditDialog(_theme, rule, _store.GetGroups(), _store.GetFeeds());
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _config.Rss.Rules.Add(dialog.Rule);
            _config.Save();
            RefreshRulesGrid();
            _onChanged?.Invoke();
        }
    }

    private void EditRule()
    {
        if (_rulesGrid?.CurrentRow?.Tag is not int index
            || index < 0 || index >= _config.Rss.Rules.Count)
        {
            return;
        }
        using var dialog = new RuleEditDialog(_theme, _config.Rss.Rules[index],
            _store.GetGroups(), _store.GetFeeds());
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _config.Rss.Rules[index] = dialog.Rule;
            _config.Save();
            RefreshRulesGrid();
            _onChanged?.Invoke();
        }
    }

    private void DeleteRule()
    {
        if (_rulesGrid?.CurrentRow?.Tag is not int index
            || index < 0 || index >= _config.Rss.Rules.Count)
        {
            return;
        }
        _config.Rss.Rules.RemoveAt(index);
        _config.Save();
        RefreshRulesGrid();
        _onChanged?.Invoke();
    }

    private void TestRules()
    {
        if (_ruleTestResult == null)
        {
            return;
        }
        var feed = _store.GetFeeds().FirstOrDefault() ?? new RssFeed { Sid = -1, Title = "测试" };
        var (hits, outcome) = RssRules.Test(_config.Rss.Rules, feed,
            _ruleTestTitle?.Text ?? "", _ruleTestContent?.Text ?? "");
        if (_config.Rss.Rules.Count == 0)
        {
            _ruleTestResult.Text = "尚未创建规则。";
            return;
        }
        if (hits.Count == 0)
        {
            _ruleTestResult.Text = "没有规则命中该测试文章。";
            return;
        }
        var actions = new List<string>();
        if (outcome.MarkRead) actions.Add("标为已读");
        if (outcome.Star) actions.Add("加星标");
        if (outcome.Hide) actions.Add("隐藏");
        if (outcome.Notify) actions.Add("通知");
        _ruleTestResult.Text = $"命中 {hits.Count} 条规则：" +
            string.Join("；", hits.Select(h => h.Pattern)) +
            "\n最终动作：" + (actions.Count == 0 ? "无" : string.Join("、", actions));
    }

    // ================= 同步服务 =================

    private Panel BuildServicePage()
    {
        var stack = CreatePage("同步服务",
            "默认使用本地 RSS，不依赖任何账号。也可以连接 Fever 或 Google Reader 兼容服务，在设备间同步。");

        var typeCard = NewCard();
        _serviceTypeCombo = MakeCombo("本地 RSS（默认）", "Fever API", "Google Reader API");
        _serviceTypeCombo.SelectedIndex = _config.Rss.ServiceType switch
        {
            "fever" => 1,
            "greader" => 2,
            _ => 0
        };
        _serviceTypeCombo.SelectedIndexChanged += (_, _) => UpdateServiceFields();
        AddCardRow(typeCard, "同步方式", "本地模式直接抓取 RSS；服务模式从服务器拉取订阅与文章", _serviceTypeCombo);
        stack.Controls.Add(typeCard);

        var serviceCard = NewCard();
        _endpointBox = new TextBox
        {
            Width = 260,
            BackColor = _theme.InputBackground,
            ForeColor = _theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "https://example.com/fever/ 或服务端点"
        };
        _endpointBox.Text = _config.Rss.ServiceEndpoint;
        AddCardRow(serviceCard, "端点", "Fever：站点 fever 端点；GReader：如 https://theoldreader.com", _endpointBox);

        _usernameBox = new TextBox
        {
            Width = 260,
            BackColor = _theme.InputBackground,
            ForeColor = _theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "登录用户名 / 邮箱"
        };
        _usernameBox.Text = _config.Rss.ServiceUsername;
        AddCardRow(serviceCard, "用户名", "Fever 与 Google Reader 服务均需要", _usernameBox);

        _passwordBox = new TextBox
        {
            Width = 260,
            BackColor = _theme.InputBackground,
            ForeColor = _theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            UseSystemPasswordChar = true,
            PlaceholderText = "留空表示不修改已保存的密码"
        };
        AddCardRow(serviceCard, "密码", "使用 Windows DPAPI 加密保存在本机配置中", _passwordBox);

        _appIdBox = new TextBox
        {
            Width = 260,
            BackColor = _theme.InputBackground,
            ForeColor = _theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "Inoreader 自定义 AppId（可选）"
        };
        _appIdBox.Text = _config.Rss.ServiceAppId;
        AddCardRow(serviceCard, "AppId（可选）", "仅 Inoreader 等需要自定义 API Key 的服务", _appIdBox);

        _appKeyBox = new TextBox
        {
            Width = 260,
            BackColor = _theme.InputBackground,
            ForeColor = _theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "Inoreader 自定义 AppKey（可选）"
        };
        _appKeyBox.Text = _config.Rss.ServiceAppKey;
        AddCardRow(serviceCard, "AppKey（可选）", "与 AppId 配套使用", _appKeyBox);

        _serviceLimitBox = MakeNumeric(50, 5000, _config.Rss.ServiceFetchLimit);
        _serviceLimitBox.Increment = 50;
        AddCardRow(serviceCard, "同步数量", "每次从服务端拉取的文章数量", _serviceLimitBox);

        _serviceGroupsCheck = MakeCheck("首次连接时导入服务端分组", _config.Rss.ServiceImportGroups);
        AddCardRow(serviceCard, "导入分组", "关闭后订阅源将放在“未分组”", _serviceGroupsCheck);
        stack.Controls.Add(serviceCard);

        var save = MakeButton("保存服务设置", primary: true);
        var test = MakeButton("测试连接");
        var syncNow = MakeButton("立即同步");
        save.Click += (_, _) => SaveService(syncAfterSave: false);
        test.Click += (_, _) => _ = TestServiceAsync();
        syncNow.Click += (_, _) =>
        {
            SaveService(syncAfterSave: false);
            _ = SyncServiceNowAsync();
        };
        stack.Controls.Add(ButtonRow(save, test, syncNow));

        _serviceStatus = new Label
        {
            Text = "",
            AutoSize = true,
            MaximumSize = new Size(760, 160),
            Font = _theme.Font(8.8f),
            ForeColor = _theme.TextSecondary,
            Margin = new Padding(0, 4, 0, 8)
        };
        stack.Controls.Add(_serviceStatus);

        var note = new Label
        {
            Text = "说明：本地 RSS 模式无需任何服务；服务模式会以服务器数据为准，"
                + "本地与服务端 URL 相同的订阅源将合并。Fever 适用于 FreshRSS / Tiny Tiny RSS / Miniflux；"
                + "Google Reader API 适用于 The Old Reader / BazQux / FreshRSS / Inoreader。",
            AutoSize = true,
            MaximumSize = new Size(760, 160),
            Font = _theme.Font(8.4f),
            ForeColor = _theme.TextTertiary,
            Margin = new Padding(0, 0, 0, 10)
        };
        stack.Controls.Add(note);

        UpdateServiceFields();
        RefreshServiceStatus();
        return (Panel)stack.Parent!;
    }

    private void UpdateServiceFields()
    {
        bool local = _serviceTypeCombo?.SelectedIndex == 0;
        if (_endpointBox != null) _endpointBox.Enabled = !local;
        if (_usernameBox != null) _usernameBox.Enabled = !local;
        if (_passwordBox != null) _passwordBox.Enabled = !local;
        bool greader = _serviceTypeCombo?.SelectedIndex == 2;
        if (_appIdBox != null) _appIdBox.Enabled = !local && greader;
        if (_appKeyBox != null) _appKeyBox.Enabled = !local && greader;
        if (_serviceLimitBox != null) _serviceLimitBox.Enabled = !local;
        if (_serviceGroupsCheck != null) _serviceGroupsCheck.Enabled = !local;
    }

    private bool SaveService(bool syncAfterSave)
    {
        _config.Rss.ServiceType = _serviceTypeCombo?.SelectedIndex switch
        {
            1 => "fever",
            2 => "greader",
            _ => "local"
        };
        _config.Rss.ServiceEndpoint = _endpointBox?.Text.Trim() ?? "";
        _config.Rss.ServiceUsername = _usernameBox?.Text.Trim() ?? "";
        if (_passwordBox != null && _passwordBox.Text.Length > 0)
        {
            _config.Rss.ServicePassword = RssSecrets.Protect(_passwordBox.Text);
            _passwordBox.Clear();
        }
        _config.Rss.ServiceAppId = _appIdBox?.Text.Trim() ?? "";
        _config.Rss.ServiceAppKey = _appKeyBox?.Text.Trim() ?? "";
        _config.Rss.ServiceFetchLimit = (int)(_serviceLimitBox?.Value ?? 300);
        _config.Rss.ServiceImportGroups = _serviceGroupsCheck?.Checked ?? true;
        if (_config.Rss.ServiceType != "local")
        {
            if (string.IsNullOrWhiteSpace(_config.Rss.ServiceEndpoint)
                || string.IsNullOrWhiteSpace(_config.Rss.ServiceUsername)
                || string.IsNullOrEmpty(RssSecrets.Unprotect(_config.Rss.ServicePassword)))
            {
                _serviceStatus!.ForeColor = _theme.IsDark
                    ? Color.FromArgb(255, 170, 170) : Color.FromArgb(180, 40, 40);
                _serviceStatus.Text = "端点、用户名和密码均不能为空（密码需要重新输入一次以加密保存）。";
                _config.Save();
                return false;
            }
        }
        _config.Save();
        _onChanged?.Invoke();
        RefreshServiceStatus();
        if (syncAfterSave)
        {
            _ = SyncServiceNowAsync();
        }
        return true;
    }

    private async Task TestServiceAsync()
    {
        if (!SaveService(syncAfterSave: false))
        {
            return;
        }
        if (_config.Rss.ServiceType == "local")
        {
            _serviceStatus!.ForeColor = _theme.TextSecondary;
            _serviceStatus.Text = "当前为本地 RSS 模式，无需测试连接。";
            return;
        }
        _serviceStatus!.ForeColor = _theme.TextSecondary;
        _serviceStatus.Text = "正在测试连接…";
        try
        {
            using var http = Http.CreateClient();
            using var client = RssServiceClient.Create(_config.Rss, http);
            await client.TestAsync(CancellationToken.None);
            _serviceStatus.ForeColor = _theme.IsDark
                ? Color.FromArgb(130, 220, 150) : Color.FromArgb(20, 130, 60);
            _serviceStatus.Text = "连接成功：" + client.DisplayName + "。";
        }
        catch (Exception ex)
        {
            _serviceStatus.ForeColor = _theme.IsDark
                ? Color.FromArgb(255, 170, 170) : Color.FromArgb(180, 40, 40);
            _serviceStatus.Text = "连接失败：" + ex.Message;
        }
    }

    private async Task SyncServiceNowAsync()
    {
        if (_serviceStatus == null)
        {
            return;
        }
        _serviceStatus.ForeColor = _theme.TextSecondary;
        _serviceStatus.Text = "正在同步，请稍候…";
        var result = await _sync.SyncAsync(manual: true);
        if (result == null)
        {
            _serviceStatus.Text = "同步服务正忙，请稍后再试。";
            return;
        }
        _serviceStatus.ForeColor = result.Errors.Count > 0
            ? (_theme.IsDark ? Color.FromArgb(255, 190, 120) : Color.FromArgb(160, 100, 0))
            : (_theme.IsDark ? Color.FromArgb(130, 220, 150) : Color.FromArgb(20, 130, 60));
        _serviceStatus.Text = $"同步完成：新增 {result.NewArticles} 篇文章"
            + (result.Errors.Count > 0 ? "；" + string.Join("；", result.Errors.Take(3)) : "。");
        RefreshServiceStatus();
        RefreshFeedsGrid();
        _onChanged?.Invoke();
    }

    private void RefreshServiceStatus()
    {
        if (_serviceStatus == null)
        {
            return;
        }
        string type = _config.Rss.ServiceType switch
        {
            "fever" => "Fever API",
            "greader" => "Google Reader API",
            _ => "本地 RSS"
        };
        var last = _sync.LastSync;
        string lastText = last.HasValue ? last.Value.ToString("yyyy-MM-dd HH:mm") : "从未同步";
        _serviceStatus.Text = $"当前模式：{type} · 上次同步：{lastText}"
            + (string.IsNullOrWhiteSpace(_config.Rss.LastSyncErrors)
                ? ""
                : "\n最近错误：" + _config.Rss.LastSyncErrors.Split('\n')[0]);
        _serviceStatus.ForeColor = string.IsNullOrWhiteSpace(_config.Rss.LastSyncErrors)
            ? _theme.TextSecondary
            : (_theme.IsDark ? Color.FromArgb(255, 190, 120) : Color.FromArgb(160, 100, 0));
    }

    private void RefreshSyncLabels()
    {
        if (_syncStatusLabel != null)
        {
            var last = _sync.LastSync;
            string interval = _config.Rss.SyncIntervalMinutes <= 0
                ? "未开启自动抓取"
                : $"每 {_config.Rss.SyncIntervalMinutes} 分钟自动抓取";
            _syncStatusLabel.Text = "上次同步：" +
                (last.HasValue ? last.Value.ToString("yyyy-MM-dd HH:mm:ss") : "从未同步")
                + "    ·    " + interval;
        }
        if (_syncErrorLabel != null)
        {
            _syncErrorLabel.Text = string.IsNullOrWhiteSpace(_config.Rss.LastSyncErrors)
                ? ""
                : "最近同步错误：\n" + _config.Rss.LastSyncErrors;
        }
    }

    // ================= 数据管理 =================

    private Panel BuildDataPage()
    {
        var stack = CreatePage("数据管理", "完整备份 / 还原、清理旧文章与维护本地数据库。");
        var infoCard = NewCard();
        _dataInfoLabel = new Label
        {
            Text = "",
            AutoSize = true,
            MaximumSize = new Size(760, 160),
            Font = _theme.Font(8.8f),
            ForeColor = _theme.TextSecondary,
            Margin = new Padding(0, 8, 0, 8)
        };
        AddFullWidthRow(infoCard, _dataInfoLabel);
        stack.Controls.Add(infoCard);

        var backup = MakeButton("备份全部数据…", primary: true);
        var restore = MakeButton("还原备份…");
        var clearCache = MakeButton("清理全文缓存");
        var openFolder = MakeButton("打开数据目录");
        backup.Click += (_, _) => BackupAll();
        restore.Click += (_, _) => RestoreAll();
        clearCache.Click += (_, _) =>
        {
            _store.ClearFullContentCache();
            RefreshDataInfo();
            MessageBox.Show(this, "已清理缓存的“抓取全文”内容，重新打开文章时可再次抓取。",
                "清理完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        openFolder.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppConfig.ConfigDir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "打开目录");
            }
        };
        stack.Controls.Add(ButtonRow(backup, restore, clearCache, openFolder));

        stack.Controls.Add(SectionTitle("清理文章"));
        var cleanupCard = NewCard();
        var daysCombo = MakeCombo("7 天前的文章", "14 天前的文章", "21 天前的文章", "28 天前的文章", "全部文章");
        var cleanupButton = MakeButton("删除");
        cleanupButton.Click += (_, _) =>
        {
            int[] days = { 7, 14, 21, 28 };
            int index = daysCombo.SelectedIndex;
            string message = index == 4
                ? "确定删除全部文章吗？订阅源会保留，但所有已读 / 星标状态都会丢失。"
                : $"确定删除 {days[index]} 天前的文章吗？星标文章会保留。";
            if (MessageBox.Show(this, message, "删除文章", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }
            int deleted = index == 4
                ? _store.DeleteAllArticles()
                : _store.DeleteArticlesOlderThan(days[index]);
            _onChanged?.Invoke();
            RefreshDataInfo();
            MessageBox.Show(this, $"已删除 {deleted} 篇文章。", "删除文章",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        AddCardRow(cleanupCard, "按时间清理", "星标文章不会被自动删除；删除全部会清空所有文章", daysCombo);
        int cleanupRow = cleanupCard.RowCount++;
        cleanupCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        cleanupButton.Anchor = AnchorStyles.Right;
        cleanupButton.Margin = new Padding(8, 0, 0, 0);
        cleanupCard.Controls.Add(cleanupButton, 1, cleanupRow);
        stack.Controls.Add(cleanupCard);

        return (Panel)stack.Parent!;
    }

    private void RefreshDataInfo()
    {
        if (_dataInfoLabel == null)
        {
            return;
        }
        var counts = _store.GetSmartCounts();
        long size = _store.GetDatabaseSize();
        _dataInfoLabel.Text = $"数据库：{_store.DatabasePath}\n"
            + $"大小：{FormatSize(size)}    ·    未读：{counts.All}    ·    星标：{counts.Starred}\n"
            + $"共 {_store.GetFeeds().Count} 个订阅源、{_store.GetGroups().Count} 个分组";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes + " B";
        }
        if (bytes < 1024 * 1024)
        {
            return (bytes / 1024.0).ToString("0.0") + " KB";
        }
        return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
    }

    private void BackupAll()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "备份 FluentRSS 数据",
            Filter = "FluentRSS 备份|*.frbackup",
            FileName = "FluentRSS备份_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".frbackup"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            RssBackup.Export(dialog.FileName, _store, _config.Rss);
            MessageBox.Show(this,
                "备份完成：\n" + dialog.FileName + "\n\n备份包含订阅源、分组、文章、已读状态与规则设置。",
                "备份完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "备份失败：" + ex.Message, "备份失败",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RestoreAll()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "还原 FluentRSS 备份",
            Filter = "FluentRSS 备份|*.frbackup|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        if (MessageBox.Show(this,
                "还原会覆盖当前全部订阅源、文章与规则设置，确定继续吗？",
                "还原备份", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }
        try
        {
            var restored = RssBackup.Import(dialog.FileName, _store);
            // 直接整体替换 RSS 设置（DPAPI 密文原样保留）
            _config.Rss = restored;
            _config.Save();
            RefreshFeedsGrid();
            RefreshGroupsList();
            RefreshRulesGrid();
            RefreshDataInfo();
            _onChanged?.Invoke();
            MessageBox.Show(this, "还原完成，订阅源与文章已恢复。", "还原备份",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "还原失败：" + ex.Message, "还原失败",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ================= 关于 =================

    private Panel BuildAboutPage()
    {
        var stack = CreatePage("关于 FluentRSS 阅读器", "Fluent Reader 的 Windows 原生复刻实现。");
        var card = NewCard();
        var title = new Label
        {
            Text = "FluentRSS 阅读器",
            AutoSize = false,
            Height = 46,
            Font = _theme.Font(24f, FontStyle.Bold),
            ForeColor = _theme.Text,
            Margin = new Padding(0, 6, 0, 0)
        };
        var version = new Label
        {
            Text = "v1.3.2 复刻版",
            AutoSize = false,
            Height = 34,
            Font = _theme.Font(14f, FontStyle.Bold),
            ForeColor = _theme.Accent,
            Margin = new Padding(0, 0, 0, 0)
        };
        var author = new Label
        {
            Text = "2026年制作",
            AutoSize = false,
            Height = 28,
            Font = _theme.Font(9.5f),
            ForeColor = _theme.TextSecondary,
            Margin = new Padding(0, 0, 0, 8)
        };
        var description = new Label
        {
            Text = "以 Windows 原生 WinForms + WebView2 复刻 Fluent Reader v1.3.2："
                + "三栏阅读、分组订阅、四种视图、正则搜索、自动规则、OPML 导入导出、"
                + "Fever / Google Reader 同步、后台定时抓取与桌面通知。\n\n"
                + "原项目：https://github.com/yang991178/fluent-reader （BSD-3-Clause）\n"
                + "本复刻版为独立实现，不包含 Electron / Node.js 运行时。",
            AutoSize = true,
            MaximumSize = new Size(760, 260),
            Font = _theme.Font(9f),
            ForeColor = _theme.TextSecondary,
            Margin = new Padding(0, 4, 0, 10)
        };
        foreach (var control in new Control[] { title, version, author, description })
        {
            AddFullWidthRow(card, control);
        }
        stack.Controls.Add(card);
        return (Panel)stack.Parent!;
    }

    // ================= 工具 =================

    private void Note(string text)
    {
        if (_syncStatusLabel != null)
        {
            _syncStatusLabel.Text = text;
        }
    }

    private void ApplyDarkTitleBar()
    {
        if (!_theme.IsDark)
        {
            return;
        }
        try
        {
            int enabled = 1;
            DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
        }
        catch
        {
            // 忽略
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _onChanged?.Invoke();
        base.OnFormClosed(e);
    }
}
