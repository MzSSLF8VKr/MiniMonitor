using System.Text.RegularExpressions;

namespace MiniMonitor.Rss;

/// <summary>FluentRSS 对话框统一外观。</summary>
public abstract class RssDialogBase : Form
{
    protected readonly RssTheme Theme;

    protected RssDialogBase(RssTheme theme, string title, Size size)
    {
        Theme = theme;
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = theme.Background;
        ForeColor = theme.Text;
        Font = theme.Font(9.2f);
        ClientSize = size;
        KeyPreview = true;
    }

    protected Label MakeLabel(string text, bool muted = false)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = 20,
            ForeColor = muted ? Theme.TextSecondary : Theme.Text,
            Font = Theme.Font(muted ? 8.6f : 9f),
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    protected TextBox MakeTextBox(string text = "")
    {
        return new TextBox
        {
            Text = text,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.InputBackground,
            ForeColor = Theme.Text,
            Font = Theme.Font(9.5f)
        };
    }

    protected ComboBox MakeCombo()
    {
        return new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.InputBackground,
            ForeColor = Theme.Text,
            Font = Theme.Font(9.2f)
        };
    }

    protected CheckBox MakeCheck(string text, bool value)
    {
        return new CheckBox
        {
            Text = text,
            Checked = value,
            AutoSize = true,
            ForeColor = Theme.Text,
            Font = Theme.Font(9.2f)
        };
    }

    protected Button MakeButton(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            Height = 34,
            Width = 94,
            BackColor = primary ? Theme.Accent : Theme.SurfaceAlt,
            ForeColor = primary ? (Theme.IsDark ? Color.FromArgb(16, 28, 38) : Color.White) : Theme.Text,
            Font = Theme.Font(9.2f, primary ? FontStyle.Bold : FontStyle.Regular),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = primary ? Theme.Accent : Theme.Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Theme.AccentHover : Theme.Hover;
        return button;
    }

    protected void ApplyDarkTitleBar()
    {
        if (!Theme.IsDark)
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
            // 非 Windows 11 忽略
        }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

/// <summary>添加订阅源：输入网址自动发现 RSS / Atom / JSON Feed。</summary>
public sealed class AddFeedDialog : RssDialogBase
{
    private readonly RssStore _store;
    private readonly IReadOnlyList<RssGroup> _groups;
    private readonly HttpClient _http;
    private readonly TextBox _urlBox;
    private readonly TextBox _nameBox;
    private readonly ComboBox _groupCombo;
    private readonly ListBox _discoveredList;
    private readonly Label _status;
    private readonly Button _discoverButton;
    private readonly Button _addButton;
    private List<DiscoveredFeed> _discovered = new();

    public int AddedSid { get; private set; }
    public string AddedTitle { get; private set; } = "";
    public string AddedUrl { get; private set; } = "";

    public AddFeedDialog(RssTheme theme, RssStore store, IReadOnlyList<RssGroup> groups, HttpClient http)
        : base(theme, "添加订阅源", new Size(620, 470))
    {
        _store = store;
        _groups = groups;
        _http = http;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 18, 24, 18),
            ColumnCount = 1,
            RowCount = 10,
            BackColor = theme.Background
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        var header = MakeLabel("添加订阅源");
        header.Font = theme.Font(15f, FontStyle.Bold);
        header.Height = 34;
        root.Controls.Add(header, 0, 0);

        root.Controls.Add(MakeLabel("输入网站首页或 RSS / Atom / JSON Feed 地址", muted: true), 0, 1);

        var urlRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 6)
        };
        urlRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        urlRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        _urlBox = MakeTextBox();
        _urlBox.Dock = DockStyle.Fill;
        _urlBox.PlaceholderText = "https://example.com 或 https://example.com/feed.xml";
        _urlBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _ = DiscoverAsync();
            }
        };
        _discoverButton = MakeButton("自动发现");
        _discoverButton.Dock = DockStyle.Fill;
        _discoverButton.Margin = new Padding(8, 0, 0, 0);
        _discoverButton.Click += (_, _) => _ = DiscoverAsync();
        urlRow.Controls.Add(_urlBox, 0, 0);
        urlRow.Controls.Add(_discoverButton, 1, 0);
        root.Controls.Add(urlRow, 0, 2);

        root.Controls.Add(MakeLabel("订阅源名称", muted: true), 0, 3);
        _nameBox = MakeTextBox();
        _nameBox.Dock = DockStyle.Fill;
        _nameBox.PlaceholderText = "留空则自动使用订阅源标题";
        root.Controls.Add(_nameBox, 0, 4);

        root.Controls.Add(MakeLabel("分组", muted: true), 0, 5);
        _groupCombo = MakeCombo();
        _groupCombo.Dock = DockStyle.Fill;
        _groupCombo.Items.Add("（未分组）");
        foreach (var group in groups)
        {
            _groupCombo.Items.Add(group.Name);
        }
        _groupCombo.SelectedIndex = 0;
        root.Controls.Add(_groupCombo, 0, 6);

        _discoveredList = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = theme.InputBackground,
            ForeColor = theme.Text,
            Font = theme.Font(9f),
            Visible = false
        };
        _discoveredList.SelectedIndexChanged += (_, _) =>
        {
            int index = _discoveredList.SelectedIndex;
            if (index >= 0 && index < _discovered.Count)
            {
                _urlBox.Text = _discovered[index].Url;
                if (!string.IsNullOrWhiteSpace(_discovered[index].Title))
                {
                    _nameBox.Text = _discovered[index].Title;
                }
            }
        };
        root.Controls.Add(_discoveredList, 0, 7);

        _status = MakeLabel("", muted: true);
        _status.Height = 24;
        root.Controls.Add(_status, 0, 8);

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = theme.Background,
            Margin = new Padding(0, 6, 0, 0)
        };
        _addButton = MakeButton("添加", primary: true);
        var cancel = MakeButton("取消");
        _addButton.Click += (_, _) => AddFeed();
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        buttonRow.Controls.Add(_addButton);
        buttonRow.Controls.Add(cancel);
        root.Controls.Add(buttonRow, 0, 9);

        Controls.Add(root);
        AcceptButton = _addButton;
        CancelButton = cancel;
        Shown += (_, _) =>
        {
            ApplyDarkTitleBar();
            _urlBox.Focus();
        };
    }

    private async Task DiscoverAsync()
    {
        string input = _urlBox.Text.Trim();
        if (!RssParser.TryNormalizeUrl(input, out _))
        {
            _status.ForeColor = Theme.IsDark ? Color.FromArgb(255, 160, 160) : Color.FromArgb(190, 40, 40);
            _status.Text = "请输入有效的 http / https 网址。";
            return;
        }
        _discoverButton.Enabled = false;
        _addButton.Enabled = false;
        _status.ForeColor = Theme.TextSecondary;
        _status.Text = "正在发现订阅源…";
        try
        {
            var results = await RssParser.DiscoverAsync(_http, input);
            _discovered = results;
            if (results.Count == 0)
            {
                _status.Text = "未发现订阅源，可直接添加为 RSS 地址。";
                return;
            }
            if (results.Count == 1)
            {
                _urlBox.Text = results[0].Url;
                if (!string.IsNullOrWhiteSpace(results[0].Title))
                {
                    _nameBox.Text = results[0].Title;
                }
                _status.ForeColor = Theme.IsDark ? Color.FromArgb(130, 220, 150) : Color.FromArgb(20, 130, 60);
                _status.Text = "已发现：" + (string.IsNullOrWhiteSpace(results[0].Title) ? results[0].Url : results[0].Title);
                _discoveredList.Visible = false;
            }
            else
            {
                _discoveredList.Items.Clear();
                foreach (var result in results)
                {
                    _discoveredList.Items.Add(string.IsNullOrWhiteSpace(result.Title)
                        ? result.Url
                        : result.Title + "  —  " + result.Url);
                }
                _discoveredList.Visible = true;
                _discoveredList.SelectedIndex = 0;
                _status.Text = $"发现 {results.Count} 个订阅源，请选择要添加的一项。";
            }
        }
        catch (Exception ex)
        {
            _status.ForeColor = Theme.IsDark ? Color.FromArgb(255, 160, 160) : Color.FromArgb(190, 40, 40);
            _status.Text = "自动发现失败：" + ex.Message;
        }
        finally
        {
            _discoverButton.Enabled = true;
            _addButton.Enabled = true;
        }
    }

    private void AddFeed()
    {
        string input = _urlBox.Text.Trim();
        if (!RssParser.TryNormalizeUrl(input, out string url))
        {
            _status.ForeColor = Theme.IsDark ? Color.FromArgb(255, 160, 160) : Color.FromArgb(190, 40, 40);
            _status.Text = "请输入有效的 http / https 网址。";
            _urlBox.Focus();
            return;
        }
        int? gid = null;
        int groupIndex = _groupCombo.SelectedIndex - 1;
        if (groupIndex >= 0 && groupIndex < _groups.Count)
        {
            gid = _groups[groupIndex].Gid;
        }
        string title = _nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            try
            {
                title = new Uri(url).Host;
            }
            catch
            {
                title = url;
            }
        }
        var feed = new RssFeed { Title = title, Url = url, Gid = gid };
        int sid = _store.AddFeed(feed, out bool created);
        if (!created)
        {
            _status.ForeColor = Theme.IsDark ? Color.FromArgb(255, 205, 120) : Color.FromArgb(160, 100, 0);
            _status.Text = "该订阅源已存在，无需重复添加。";
            return;
        }
        AddedSid = sid;
        AddedTitle = title;
        AddedUrl = url;
        DialogResult = DialogResult.OK;
    }
}

/// <summary>编辑订阅源：名称、地址、分组、打开方式、通知与保留上限。</summary>
public sealed class EditFeedDialog : RssDialogBase
{
    private readonly RssStore _store;
    private readonly RssFeed _feed;
    private readonly TextBox _nameBox;
    private readonly TextBox _urlBox;
    private readonly ComboBox _groupCombo;
    private readonly ComboBox _openCombo;
    private readonly CheckBox _notifyCheck;
    private readonly NumericUpDown _limitBox;
    private readonly IReadOnlyList<RssGroup> _groups;
    private readonly Label _status;

    public bool Saved { get; private set; }

    public EditFeedDialog(RssTheme theme, RssStore store, RssFeed feed, IReadOnlyList<RssGroup> groups)
        : base(theme, "编辑订阅源", new Size(600, 580))
    {
        _store = store;
        _feed = feed;
        _groups = groups;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 18, 24, 18),
            ColumnCount = 1,
            RowCount = 16,
            BackColor = theme.Background
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        for (int i = 0; i < 6; i++)
        {
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        }
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = MakeLabel("编辑订阅源");
        header.Font = theme.Font(15f, FontStyle.Bold);
        root.Controls.Add(header, 0, 0);

        var rows = new List<(string Label, Control Control)>
        {
            ("名称", _nameBox = MakeTextBox(feed.Title)),
            ("订阅地址", _urlBox = MakeTextBox(feed.Url)),
            ("分组", _groupCombo = MakeCombo()),
            ("打开方式", _openCombo = MakeCombo()),
            ("", _notifyCheck = MakeCheck("后台抓取到新文章时发送通知", feed.Notify)),
            ("每个订阅源最多保留文章数（0 = 不限制）", _limitBox = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 100000,
                Value = Math.Clamp(feed.FetchLimit, 0, 100000),
                Width = 140,
                BackColor = theme.InputBackground,
                ForeColor = theme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = theme.Font(9.2f)
            })
        };
        _groupCombo.Items.Add("（未分组）");
        foreach (var group in groups)
        {
            _groupCombo.Items.Add(group.Name);
        }
        _groupCombo.SelectedIndex = 0;
        if (feed.Gid.HasValue)
        {
            int index = groups.ToList().FindIndex(g => g.Gid == feed.Gid.Value);
            if (index >= 0)
            {
                _groupCombo.SelectedIndex = index + 1;
            }
        }
        _openCombo.Items.Add("内置文章视图");
        _openCombo.Items.Add("加载原文网页");
        _openCombo.SelectedIndex = feed.OpenMode == RssOpenMode.Webpage ? 1 : 0;

        int row = 1;
        foreach (var (labelText, control) in rows)
        {
            if (!string.IsNullOrEmpty(labelText))
            {
                root.Controls.Add(MakeLabel(labelText, muted: true), 0, row++);
            }
            control.Dock = DockStyle.Fill;
            root.Controls.Add(control, 0, row++);
        }

        _status = MakeLabel("", muted: true);
        _status.Height = 30;
        root.Controls.Add(_status, 0, row++);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = theme.Background
        };
        var save = MakeButton("保存", primary: true);
        var cancel = MakeButton("取消");
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, row);

        Controls.Add(root);
        AcceptButton = save;
        CancelButton = cancel;
        Shown += (_, _) => ApplyDarkTitleBar();
    }

    private void Save()
    {
        if (!RssParser.TryNormalizeUrl(_urlBox.Text.Trim(), out string url))
        {
            _status.ForeColor = Theme.IsDark ? Color.FromArgb(255, 160, 160) : Color.FromArgb(190, 40, 40);
            _status.Text = "请输入有效的 http / https 订阅地址。";
            return;
        }
        string name = _nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _status.Text = "名称不能为空。";
            return;
        }
        int? gid = null;
        int groupIndex = _groupCombo.SelectedIndex - 1;
        if (groupIndex >= 0 && groupIndex < _groups.Count)
        {
            gid = _groups[groupIndex].Gid;
        }
        try
        {
            if (!string.Equals(url, _feed.Url, StringComparison.OrdinalIgnoreCase))
            {
                _store.UpdateFeedUrl(_feed.Sid, url);
            }
            _store.UpdateFeedSettings(_feed.Sid, name,
                gid, _openCombo.SelectedIndex == 1 ? RssOpenMode.Webpage : RssOpenMode.Content,
                _notifyCheck.Checked, (int)_limitBox.Value);
            Saved = true;
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            _status.ForeColor = Theme.IsDark ? Color.FromArgb(255, 160, 160) : Color.FromArgb(190, 40, 40);
            _status.Text = "保存失败：" + ex.Message;
        }
    }
}

/// <summary>新建 / 重命名分组。</summary>
public sealed class GroupNameDialog : RssDialogBase
{
    private readonly TextBox _nameBox;

    public string GroupName => _nameBox.Text.Trim();

    public GroupNameDialog(RssTheme theme, string title, string initial)
        : base(theme, title, new Size(420, 190))
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22, 18, 22, 16),
            ColumnCount = 1,
            RowCount = 5,
            BackColor = theme.Background
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var header = MakeLabel(title);
        header.Font = theme.Font(13f, FontStyle.Bold);
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(MakeLabel("分组名称", muted: true), 0, 1);
        _nameBox = MakeTextBox(initial);
        _nameBox.Dock = DockStyle.Fill;
        root.Controls.Add(_nameBox, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = theme.Background
        };
        var ok = MakeButton("确定", primary: true);
        var cancel = MakeButton("取消");
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_nameBox.Text))
            {
                MessageBox.Show(this, "分组名称不能为空。", title);
                return;
            }
            DialogResult = DialogResult.OK;
        };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 4);

        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;
        Shown += (_, _) =>
        {
            ApplyDarkTitleBar();
            _nameBox.SelectAll();
            _nameBox.Focus();
        };
    }
}

/// <summary>规则编辑器：正则匹配标题/正文并执行隐藏、已读、星标、通知。</summary>
public sealed class RuleEditDialog : RssDialogBase
{
    private readonly ComboBox _targetCombo;
    private readonly TextBox _patternBox;
    private readonly ComboBox _fieldCombo;
    private readonly ComboBox _matchCombo;
    private readonly CheckBox _caseCheck;
    private readonly CheckBox _readCheck;
    private readonly CheckBox _starCheck;
    private readonly CheckBox _hideCheck;
    private readonly CheckBox _notifyCheck;
    private readonly Label _status;
    private readonly List<(string Text, string Key)> _targets = new();

    public RssRule Rule { get; private set; }

    public RuleEditDialog(RssTheme theme, RssRule rule, IReadOnlyList<RssGroup> groups,
        IReadOnlyList<RssFeed> feeds)
        : base(theme, "编辑规则", new Size(620, 560))
    {
        Rule = rule.Clone();
        _targets.Add(("全部订阅源", "all"));
        foreach (var group in groups)
        {
            _targets.Add(("分组：" + group.Name, "group:" + group.Gid));
        }
        foreach (var feed in feeds)
        {
            _targets.Add(("订阅源：" + feed.Title, "feed:" + feed.Sid));
        }

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 18, 24, 16),
            ColumnCount = 1,
            RowCount = 14,
            BackColor = theme.Background
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));  // 标题
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));  // 作用范围
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));  // 正则
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));  // 匹配范围
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // 动作标题
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));  // 动作
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // 状态
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));  // 按钮
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = MakeLabel("自动处理规则");
        header.Font = theme.Font(15f, FontStyle.Bold);
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(MakeLabel("作用范围", muted: true), 0, 1);
        _targetCombo = MakeCombo();
        foreach (var target in _targets)
        {
            _targetCombo.Items.Add(target.Text);
        }
        int targetIndex = _targets.FindIndex(t => t.Key == rule.Target);
        _targetCombo.SelectedIndex = targetIndex >= 0 ? targetIndex : 0;
        _targetCombo.Dock = DockStyle.Fill;
        root.Controls.Add(_targetCombo, 0, 2);

        root.Controls.Add(MakeLabel("正则表达式", muted: true), 0, 3);
        _patternBox = MakeTextBox(rule.Pattern);
        _patternBox.Dock = DockStyle.Fill;
        _patternBox.PlaceholderText = "例如：广告|推广|sponsor";
        root.Controls.Add(_patternBox, 0, 4);

        var middle = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            BackColor = theme.Background
        };
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        middle.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        middle.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        middle.Controls.Add(MakeLabel("匹配范围", muted: true), 0, 0);
        middle.Controls.Add(MakeLabel("执行条件", muted: true), 1, 0);
        middle.Controls.Add(MakeLabel("", muted: true), 2, 0);
        _fieldCombo = MakeCombo();
        _fieldCombo.Items.AddRange(new object[] { "标题", "正文", "标题 + 正文" });
        _fieldCombo.SelectedIndex = rule.Field switch { "content" => 1, "both" => 2, _ => 0 };
        _fieldCombo.Dock = DockStyle.Fill;
        _matchCombo = MakeCombo();
        _matchCombo.Items.AddRange(new object[] { "匹配时执行", "不匹配时执行" });
        _matchCombo.SelectedIndex = rule.Match ? 0 : 1;
        _matchCombo.Dock = DockStyle.Fill;
        _caseCheck = MakeCheck("区分大小写", rule.CaseSensitive);
        _caseCheck.Dock = DockStyle.Fill;
        middle.Controls.Add(_fieldCombo, 0, 1);
        middle.Controls.Add(_matchCombo, 1, 1);
        middle.Controls.Add(_caseCheck, 2, 1);
        root.Controls.Add(middle, 0, 5);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = theme.Background,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        _readCheck = MakeCheck("标为已读", rule.MarkRead);
        _starCheck = MakeCheck("加星标", rule.Star);
        _hideCheck = MakeCheck("隐藏文章", rule.Hide);
        _notifyCheck = MakeCheck("发送通知", rule.Notify);
        foreach (var check in new[] { _readCheck, _starCheck, _hideCheck, _notifyCheck })
        {
            check.Margin = new Padding(0, 8, 18, 0);
            actions.Controls.Add(check);
        }
        root.Controls.Add(MakeLabel("命中后执行的动作", muted: true), 0, 6);
        root.Controls.Add(actions, 0, 7);

        _status = MakeLabel("", muted: true);
        _status.Height = 36;
        root.Controls.Add(_status, 0, 8);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = theme.Background
        };
        var ok = MakeButton("保存规则", primary: true);
        var cancel = MakeButton("取消");
        ok.Click += (_, _) => Save();
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 9);

        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;
        Shown += (_, _) =>
        {
            ApplyDarkTitleBar();
            _patternBox.Focus();
        };
    }

    private void Save()
    {
        string pattern = _patternBox.Text.Trim();
        if (string.IsNullOrEmpty(pattern))
        {
            _status.ForeColor = Theme.IsDark ? Color.FromArgb(255, 160, 160) : Color.FromArgb(190, 40, 40);
            _status.Text = "正则表达式不能为空。";
            return;
        }
        try
        {
            _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            _status.ForeColor = Theme.IsDark ? Color.FromArgb(255, 160, 160) : Color.FromArgb(190, 40, 40);
            _status.Text = "正则表达式无效：" + ex.Message;
            return;
        }
        Rule = new RssRule
        {
            Target = _targets[Math.Max(0, _targetCombo.SelectedIndex)].Key,
            Pattern = pattern,
            Field = _fieldCombo.SelectedIndex switch { 1 => "content", 2 => "both", _ => "title" },
            Match = _matchCombo.SelectedIndex == 0,
            CaseSensitive = _caseCheck.Checked,
            MarkRead = _readCheck.Checked,
            Star = _starCheck.Checked,
            Hide = _hideCheck.Checked,
            Notify = _notifyCheck.Checked
        };
        DialogResult = DialogResult.OK;
    }
}
