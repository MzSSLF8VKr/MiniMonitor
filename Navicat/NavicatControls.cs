using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MiniMonitor.Navicat;

/// <summary>Navicat 风格文档标签页（带图标、关闭按钮、滚动）。</summary>
internal sealed class NvTabHost : UserControl
{
    internal sealed class DocTab
    {
        public string Key { get; init; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "";
        public Image? Icon { get; set; }
        public Control Content { get; init; } = null!;
        public bool Closable { get; init; } = true;
        public object? Tag { get; set; }
        public bool Dirty { get; set; }

        public string DisplayTitle => Dirty ? Title + " *" : Title;
    }

    private readonly Panel _contentPanel;
    private readonly List<DocTab> _tabs = new();
    private DocTab? _active;
    private int _hoverIndex = -1;
    private int _scroll;
    private int _stripHeight = 31;
    private List<Rectangle> _tabRects = new();
    private readonly Font _tabFont = NvTheme.Ui(9f);

    public event EventHandler<DocTab>? TabClosed;
    public event EventHandler<DocTab>? ActiveTabChanged;

    public NvTabHost()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = NvTheme.TabStrip;
        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = NvTheme.Panel,
            Padding = new Padding(0),
            Location = new Point(0, _stripHeight)
        };
        Controls.Add(_contentPanel);
        _contentPanel.BringToFront();
        Padding = new Padding(0, _stripHeight, 0, 0);
        DoubleBuffered = true;
    }

    public IReadOnlyList<DocTab> Tabs => _tabs;
    public DocTab? ActiveTab => _active;
    public bool IsEmpty => _tabs.Count == 0;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tabFont.Dispose();
        }
        base.Dispose(disposing);
    }

    public DocTab Add(string title, Control content, Image? icon = null, bool closable = true,
        string? key = null)
    {
        var tab = new DocTab
        {
            Key = key ?? Guid.NewGuid().ToString("N"),
            Title = title,
            Icon = icon,
            Content = content,
            Closable = closable
        };
        content.Dock = DockStyle.Fill;
        content.Visible = false;
        _contentPanel.Controls.Add(content);
        _tabs.Add(tab);
        Activate(tab);
        return tab;
    }

    public DocTab? Find(string key) => _tabs.FirstOrDefault(t => t.Key == key);

    public void Activate(DocTab tab)
    {
        if (!_tabs.Contains(tab))
        {
            return;
        }
        if (_active == tab)
        {
            return;
        }
        if (_active != null)
        {
            _active.Content.Visible = false;
        }
        _active = tab;
        tab.Content.Visible = true;
        tab.Content.BringToFront();
        EnsureVisible(tab);
        Invalidate();
        ActiveTabChanged?.Invoke(this, tab);
    }

    public void Activate(string key)
    {
        var tab = Find(key);
        if (tab != null)
        {
            Activate(tab);
        }
    }

    public void Close(DocTab tab, bool force = false)
    {
        if (!force && !tab.Closable)
        {
            return;
        }
        int index = _tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }
        if (!force && tab.Content is NvClosable closable && !closable.TryClose())
        {
            return;
        }
        _tabs.RemoveAt(index);
        _contentPanel.Controls.Remove(tab.Content);
        if (ReferenceEquals(_active, tab))
        {
            _active = null;
            if (_tabs.Count > 0)
            {
                Activate(_tabs[Math.Min(index, _tabs.Count - 1)]);
            }
            else
            {
                Invalidate();
            }
        }
        tab.Content.Dispose();
        TabClosed?.Invoke(this, tab);
        Invalidate();
    }

    public void CloseOthers(DocTab keep)
    {
        foreach (var tab in _tabs.Where(t => t != keep && t.Closable).ToList())
        {
            Close(tab);
        }
    }

    public void CloseAll(bool includeNonClosable = false)
    {
        foreach (var tab in _tabs.ToList())
        {
            if (tab.Closable || includeNonClosable)
            {
                Close(tab, true);
            }
        }
    }

    public void SetDirty(DocTab tab, bool dirty)
    {
        if (tab.Dirty == dirty)
        {
            return;
        }
        tab.Dirty = dirty;
        Invalidate();
    }

    private int MeasureTab(DocTab tab)
    {
        int textWidth;
        using (var g = CreateGraphics())
        {
            textWidth = (int)Math.Ceiling(g.MeasureString(tab.DisplayTitle, _tabFont).Width);
        }
        int width = 12 + (tab.Icon != null ? 20 : 0) + textWidth + (tab.Closable ? 22 : 10);
        return Math.Clamp(width, 90, 230);
    }

    private void EnsureVisible(DocTab tab)
    {
        var rects = BuildRects();
        int index = _tabs.IndexOf(tab);
        if (index < 0 || index >= rects.Count)
        {
            return;
        }
        var rect = rects[index];
        if (rect.Left - _scroll < 0)
        {
            _scroll = Math.Max(0, rect.Left - 2);
        }
        else if (rect.Right - _scroll > Width)
        {
            _scroll += rect.Right - _scroll - Width + 2;
        }
    }

    private List<Rectangle> BuildRects()
    {
        var rects = new List<Rectangle>();
        int x = 4 - _scroll;
        foreach (var tab in _tabs)
        {
            int width = MeasureTab(tab);
            rects.Add(new Rectangle(x, 3, width, _stripHeight - 4));
            x += width + 1;
        }
        return rects;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.Default;
        using (var back = new LinearGradientBrush(ClientRectangle, NvTheme.HeaderBack, NvTheme.TabStrip,
            LinearGradientMode.Vertical))
        {
            g.FillRectangle(back, new Rectangle(0, 0, Width, _stripHeight));
        }
        using (var line = new Pen(NvTheme.Border))
        {
            g.DrawLine(line, 0, _stripHeight - 1, Width, _stripHeight - 1);
        }

        _tabRects = BuildRects();
        for (int i = 0; i < _tabs.Count; i++)
        {
            var tab = _tabs[i];
            var rect = _tabRects[i];
            if (rect.Right < 0 || rect.Left > Width)
            {
                continue;
            }
            bool active = ReferenceEquals(tab, _active);
            bool hover = i == _hoverIndex;
            Color fill = active ? NvTheme.TabActive
                : hover ? Color.FromArgb(239, 245, 251)
                : Color.FromArgb(214, 225, 236);
            using (var path = NvTheme.RoundedRect(new Rectangle(rect.X, rect.Y, rect.Width, rect.Height + 4), 4))
            using (var brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
            }
            if (active)
            {
                using var accent = new SolidBrush(NvTheme.Accent);
                g.FillRectangle(accent, rect.X + 4, rect.Y, rect.Width - 8, 2);
            }
            int textX = rect.X + 10;
            if (tab.Icon != null)
            {
                g.DrawImage(tab.Icon, new Rectangle(textX, rect.Y + (rect.Height - 16) / 2, 16, 16));
                textX += 20;
            }
            int closeWidth = tab.Closable ? 18 : 0;
            var textRect = new Rectangle(textX, rect.Y, rect.Width - (textX - rect.X) - closeWidth - 4,
                rect.Height);
            TextRenderer.DrawText(g, tab.DisplayTitle, _tabFont,
                textRect, active ? NvTheme.Text : Color.FromArgb(62, 78, 94),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix);
            if (tab.Closable)
            {
                var closeRect = new Rectangle(rect.Right - 20, rect.Y + (rect.Height - 14) / 2, 14, 14);
                bool closeHover = _hoverIndex == i && closeRect.Contains(PointToClient(MousePosition));
                if (closeHover)
                {
                    using var brush = new SolidBrush(Color.FromArgb(60, 190, 60, 60));
                    g.FillEllipse(brush, closeRect);
                }
                using var pen = new Pen(closeHover ? NvTheme.Red : Color.FromArgb(108, 122, 137), 1.3f);
                g.DrawLine(pen, closeRect.Left + 4, closeRect.Top + 4, closeRect.Right - 4, closeRect.Bottom - 4);
                g.DrawLine(pen, closeRect.Right - 4, closeRect.Top + 4, closeRect.Left + 4, closeRect.Bottom - 4);
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hover = -1;
        if (e.Y <= _stripHeight)
        {
            for (int i = 0; i < _tabRects.Count; i++)
            {
                if (_tabRects[i].Contains(e.Location))
                {
                    hover = i;
                    break;
                }
            }
        }
        if (hover != _hoverIndex)
        {
            _hoverIndex = hover;
            Cursor = hover >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate(new Rectangle(0, 0, Width, _stripHeight + 1));
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            Cursor = Cursors.Default;
            Invalidate(new Rectangle(0, 0, Width, _stripHeight + 1));
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Y > _stripHeight)
        {
            return;
        }
        for (int i = 0; i < _tabRects.Count; i++)
        {
            if (!_tabRects[i].Contains(e.Location))
            {
                continue;
            }
            var tab = _tabs[i];
            var closeRect = new Rectangle(_tabRects[i].Right - 20,
                _tabRects[i].Y + (_tabRects[i].Height - 14) / 2, 14, 14);
            if (e.Button == MouseButtons.Left && tab.Closable && closeRect.Contains(e.Location))
            {
                Close(tab);
            }
            else if (e.Button == MouseButtons.Left)
            {
                Activate(tab);
            }
            else if (e.Button == MouseButtons.Middle && tab.Closable)
            {
                Close(tab);
            }
            return;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Y <= _stripHeight && _tabs.Count > 0)
        {
            int total = BuildRects().Sum(r => r.Width + 1) + 4;
            int maxScroll = Math.Max(0, total - Width);
            _scroll = Math.Clamp(_scroll - e.Delta, 0, maxScroll);
            Invalidate(new Rectangle(0, 0, Width, _stripHeight + 1));
        }
    }
}

internal interface NvClosable
{
    bool TryClose();
}

/// <summary>SQL 编辑器的行号栏。</summary>
internal sealed class NvLineNumberPanel : Control
{
    private readonly RichTextBox _box;
    private readonly Font _numberFont;

    public NvLineNumberPanel(RichTextBox box)
    {
        _box = box;
        _numberFont = new Font("Consolas", Math.Max(7f, box.Font.Size - 1f));
        Width = 48;
        Dock = DockStyle.Left;
        BackColor = NvTheme.EditorGutter;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _numberFont.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var line = new Pen(NvTheme.BorderLight);
        e.Graphics.DrawLine(line, Width - 1, 0, Width - 1, Height);
        if (_box == null || _box.IsDisposed)
        {
            return;
        }
        int firstChar = _box.GetCharIndexFromPosition(new Point(1, 1));
        int firstLine = _box.GetLineFromCharIndex(firstChar);
        int lastChar = _box.GetCharIndexFromPosition(new Point(1, Math.Max(1, _box.Height - 2)));
        int lastLine = _box.GetLineFromCharIndex(lastChar);
        int lineHeight = _box.Font.Height;
        int y = 2 - (_box.GetPositionFromCharIndex(firstChar).Y % Math.Max(1, lineHeight));
        for (int i = firstLine; i <= lastLine + 1; i++)
        {
            TextRenderer.DrawText(e.Graphics, (i + 1).ToString(),
                _numberFont, new Rectangle(0, y, Width - 6, lineHeight),
                Color.FromArgb(140, 152, 166),
                TextFormatFlags.Right | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            y += lineHeight;
        }
    }
}

/// <summary>带行号、语法高亮、Ctrl+Space 补全的 SQL 文本编辑器。</summary>
internal sealed class NvSqlEditor : UserControl
{
    private readonly RichTextBox _box;
    private readonly NvLineNumberPanel _lines;
    private readonly System.Windows.Forms.Timer _highlightTimer;
    private readonly ListBox _suggestions;
    private readonly List<string> _words = new();
    private readonly List<string> _dynamicWords = new();
    private bool _suppressHighlight;

    public event EventHandler? TextChangedByUser;

    public NvSqlEditor()
    {
        BackColor = NvTheme.EditorBack;
        _box = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            Font = NvTheme.Mono(NvSettingsStore.Current.EditorFontSize),
            ForeColor = Color.FromArgb(30, 41, 54),
            BackColor = NvTheme.EditorBack,
            WordWrap = NvSettingsStore.Current.WordWrap,
            ScrollBars = RichTextBoxScrollBars.Both,
            AcceptsTab = true,
            DetectUrls = false,
            HideSelection = false,
            LanguageOption = RichTextBoxLanguageOptions.UIFonts,
            ShortcutsEnabled = true
        };
        _lines = new NvLineNumberPanel(_box);
        Controls.Add(_box);
        Controls.Add(_lines);

        _suggestions = new ListBox
        {
            Visible = false,
            Width = 260,
            Height = 160,
            Font = NvTheme.Mono(9.5f),
            BorderStyle = BorderStyle.FixedSingle,
            IntegralHeight = false
        };
        Controls.Add(_suggestions);
        _suggestions.BringToFront();
        _suggestions.DoubleClick += (_, _) => AcceptSuggestion();
        _suggestions.KeyDown += SuggestionKeyDown;

        _highlightTimer = new System.Windows.Forms.Timer { Interval = 260 };
        _highlightTimer.Tick += (_, _) =>
        {
            _highlightTimer.Stop();
            if (!_suppressHighlight)
            {
                NvSqlHighlighter.Highlight(_box);
            }
            _lines.Invalidate();
        };

        _box.TextChanged += (_, _) =>
        {
            _highlightTimer.Stop();
            _highlightTimer.Start();
            _lines.Invalidate();
            TextChangedByUser?.Invoke(this, EventArgs.Empty);
        };
        _box.VScroll += (_, _) => _lines.Invalidate();
        _box.Resize += (_, _) => _lines.Invalidate();
        _box.KeyDown += BoxKeyDown;
        _box.KeyUp += (_, _) => _lines.Invalidate();
        _box.MouseClick += (_, _) => _lines.Invalidate();
        _box.MouseWheel += (_, _) => _lines.Invalidate();
        _box.GotFocus += (_, _) =>
        {
            if (_suggestions.Visible && !_suggestions.Focused)
            {
                HideSuggestions();
            }
        };

        _words.AddRange(SqlKeywords);
        _words.AddRange(new[]
        {
            "information_schema", "mysql", "postgres", "public", "dbo", "sqlite_master",
            "CURRENT_TIMESTAMP", "CURRENT_DATE", "CURRENT_USER", "GROUP_CONCAT", "STRING_AGG",
            "ROW_NUMBER", "RANK", "DENSE_RANK", "OVER", "PARTITION", "IFNULL", "ISNULL", "NVL",
            "LIMIT", "OFFSET", "FETCH FIRST", "RETURNING", "ON CONFLICT", "AUTO_INCREMENT"
        });
    }

    public RichTextBox InnerBox => _box;
    public new string Text
    {
        get => _box.Text;
        set
        {
            _suppressHighlight = true;
            _box.Text = value;
            _suppressHighlight = false;
            NvSqlHighlighter.Highlight(_box);
            _lines.Invalidate();
        }
    }
    public string SelectedText => _box.SelectedText;
    public int SelectionStart => _box.SelectionStart;
    public int SelectionLength => _box.SelectionLength;
    public new Font Font
    {
        get => _box.Font;
        set
        {
            _box.Font = value;
            _lines.Invalidate();
        }
    }
    public bool WordWrap
    {
        get => _box.WordWrap;
        set
        {
            _box.WordWrap = value;
            _box.ScrollBars = value ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.Both;
        }
    }

    public void SelectAll() => _box.SelectAll();
    public void Select(int start, int length) => _box.Select(start, length);
    public void AppendText(string text) => _box.AppendText(text);
    public void FocusEditor() => _box.Focus();
    public void Clear() => _box.Clear();
    public void SetDynamicWords(IEnumerable<string> words)
    {
        _dynamicWords.Clear();
        _dynamicWords.AddRange(words.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private void BoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.Space)
        {
            ShowSuggestions(GetCurrentWordPrefix(), true);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        if (_suggestions.Visible)
        {
            if (e.KeyCode == Keys.Down)
            {
                _suggestions.SelectedIndex = Math.Min(_suggestions.Items.Count - 1, _suggestions.SelectedIndex + 1);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Up)
            {
                _suggestions.SelectedIndex = Math.Max(0, _suggestions.SelectedIndex - 1);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Tab)
            {
                AcceptSuggestion();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                HideSuggestions();
                e.Handled = true;
            }
        }
    }

    private void SuggestionKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Tab)
        {
            AcceptSuggestion();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            HideSuggestions();
            _box.Focus();
            e.Handled = true;
        }
    }

    private string GetCurrentWordPrefix()
    {
        int pos = _box.SelectionStart;
        int start = pos;
        string text = _box.Text;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
        {
            start--;
        }
        return text[start..pos];
    }

    private void ShowSuggestions(string prefix, bool force)
    {
        string lower = prefix.ToLowerInvariant();
        var items = _words.Concat(_dynamicWords)
            .Where(w => string.IsNullOrEmpty(lower) || w.StartsWith(lower, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(w => w, StringComparer.OrdinalIgnoreCase)
            .Take(60)
            .ToArray();
        if (items.Length == 0)
        {
            HideSuggestions();
            return;
        }
        _suggestions.BeginUpdate();
        _suggestions.Items.Clear();
        _suggestions.Items.AddRange(items);
        _suggestions.EndUpdate();
        if (_suggestions.Items.Count > 0)
        {
            _suggestions.SelectedIndex = 0;
        }
        var caret = _box.GetPositionFromCharIndex(_box.SelectionStart);
        int x = _box.Left + 2 + Math.Min(caret.X + 10, Math.Max(0, _box.Width - _suggestions.Width));
        int y = _box.Top + caret.Y + _box.Font.Height + 4;
        if (y + _suggestions.Height > Height)
        {
            y = Math.Max(0, Height - _suggestions.Height - 2);
        }
        _suggestions.Location = new Point(x, y);
        _suggestions.Visible = true;
        _suggestions.BringToFront();
        _suggestions.Focus();
    }

    private void HideSuggestions()
    {
        _suggestions.Visible = false;
        _box.Focus();
    }

    private void AcceptSuggestion()
    {
        if (_suggestions.SelectedItem is not string word)
        {
            HideSuggestions();
            return;
        }
        int pos = _box.SelectionStart;
        string text = _box.Text;
        int start = pos;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
        {
            start--;
        }
        _box.Select(start, pos - start);
        _box.SelectedText = word;
        _box.SelectionStart = start + word.Length;
        HideSuggestions();
    }

    private static readonly string[] SqlKeywords =
    {
        "SELECT", "FROM", "WHERE", "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE", "CREATE",
        "ALTER", "DROP", "TABLE", "VIEW", "INDEX", "PRIMARY", "KEY", "FOREIGN", "REFERENCES", "JOIN",
        "LEFT", "RIGHT", "INNER", "OUTER", "FULL", "CROSS", "ON", "GROUP", "BY", "ORDER", "HAVING",
        "LIMIT", "OFFSET", "UNION", "ALL", "DISTINCT", "AS", "AND", "OR", "NOT", "NULL", "IS",
        "IN", "EXISTS", "BETWEEN", "LIKE", "CASE", "WHEN", "THEN", "ELSE", "END", "COUNT", "SUM",
        "AVG", "MIN", "MAX", "CAST", "CONVERT", "WITH", "BEGIN", "COMMIT", "ROLLBACK", "TRANSACTION",
        "GRANT", "REVOKE", "TRUNCATE", "EXPLAIN", "SHOW", "USE", "DESCRIBE", "IF", "ELSEIF", "WHILE",
        "DECLARE", "RETURN", "RETURNS", "FUNCTION", "PROCEDURE", "TRIGGER", "DATABASE", "SCHEMA",
        "USER", "PASSWORD", "DEFAULT", "UNIQUE", "CHECK", "CASCADE", "RESTRICT", "ASC", "DESC",
        "VARCHAR", "INT", "BIGINT", "DECIMAL", "DATETIME", "TIMESTAMP", "TEXT", "BLOB", "BOOLEAN"
    };
}

internal static class NvPrompt
{
    public static string? Show(IWin32Window? owner, string title, string label, string defaultValue = "",
        bool multiline = false)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(multiline ? 620 : 460, multiline ? 360 : 160),
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            BackColor = NvTheme.Background,
            Font = NvTheme.Ui(9f)
        };
        var caption = new Label
        {
            Text = label,
            Location = new Point(18, 16),
            AutoSize = true,
            ForeColor = NvTheme.Text
        };
        var input = multiline
            ? new TextBox
            {
                Location = new Point(18, 44),
                Size = new Size(form.ClientSize.Width - 36, 240),
                Multiline = true,
                ScrollBars = ScrollBars.Both,
                Font = NvTheme.Mono(9.5f),
                Text = defaultValue
            }
            : new TextBox
            {
                Location = new Point(18, 46),
                Size = new Size(form.ClientSize.Width - 36, 26),
                Font = NvTheme.Ui(9.5f),
                Text = defaultValue
            };
        var ok = NvTheme.FlatButton("确定", NvTheme.Accent, Color.White, 88);
        ok.Location = new Point(form.ClientSize.Width - 200, form.ClientSize.Height - 44);
        ok.DialogResult = DialogResult.OK;
        var cancel = NvTheme.FlatButton("取消", null, null, 88);
        cancel.Location = new Point(form.ClientSize.Width - 104, form.ClientSize.Height - 44);
        cancel.DialogResult = DialogResult.Cancel;
        form.Controls.AddRange(new Control[] { caption, input, ok, cancel });
        form.AcceptButton = multiline ? null : ok;
        form.CancelButton = cancel;
        var result = form.ShowDialog(owner);
        return result == DialogResult.OK ? input.Text : null;
    }

    public static bool Confirm(IWin32Window? owner, string title, string message, bool warning = false)
    {
        return MessageBox.Show(SafeOwner(owner), message, title, MessageBoxButtons.YesNo,
            warning ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    public static void Info(IWin32Window? owner, string title, string message)
    {
        MessageBox.Show(SafeOwner(owner), message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public static IWin32Window? SafeOwner(IWin32Window? owner)
    {
        if (owner is Control control && (control.IsDisposed || control.Disposing || !control.IsHandleCreated))
        {
            return null;
        }
        return owner;
    }

    public static void Error(IWin32Window? owner, string title, string message)
    {
        NvLog.Error("[" + title + "] " + message);
        MessageBox.Show(SafeOwner(owner), message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

/// <summary>SQL / DDL 预览对话框。</summary>
internal sealed class NvSqlPreviewDialog : Form
{
    public NvSqlPreviewDialog(string title, string sql, string? subtitle = null, bool executeMode = false)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(860, 620);
        MinimumSize = new Size(600, 400);
        BackColor = NvTheme.Background;
        Font = NvTheme.Ui(9f);
        ShowInTaskbar = false;

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = subtitle == null ? 8 : 34,
            BackColor = NvTheme.Background
        };
        if (!string.IsNullOrEmpty(subtitle))
        {
            header.Controls.Add(new Label
            {
                Text = subtitle,
                Dock = DockStyle.Fill,
                ForeColor = NvTheme.Muted,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0)
            });
        }
        var editor = new NvSqlEditor
        {
            Dock = DockStyle.Fill,
            Text = sql
        };
        editor.InnerBox.ReadOnly = true;
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 48, BackColor = NvTheme.Background };
        var copy = NvTheme.FlatButton("复制", null, null, 88);
        copy.Location = new Point(12, 10);
        copy.Click += (_, _) =>
        {
            if (sql.Length > 0)
            {
                Clipboard.SetText(sql);
            }
        };
        var close = NvTheme.FlatButton(executeMode ? "执行" : "关闭",
            NvTheme.Accent, Color.White, 88);
        close.Location = new Point(ClientSize.Width - 100, 10);
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.DialogResult = DialogResult.OK;
        bottom.Controls.AddRange(new Control[] { copy, close });
        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 8) };
        content.Controls.Add(editor);
        Controls.Add(content);
        Controls.Add(bottom);
        Controls.Add(header);
        AcceptButton = close;
        CancelButton = close;
    }
}
