using System.Windows.Forms;

namespace MiniMonitor.XShell;

/// <summary>
/// XShell 复刻版全局设置。为保证高 DPI 与深色主题下文字始终清晰，
/// 此窗口只使用标准 WinForms 控件 + 实色背景 + 固定行距，不使用自绘透明控件。
/// </summary>
public sealed class XShellSettingsForm : Form
{
    private readonly XShellSettings _settings;
    private readonly ComboBox _uiTheme;
    private readonly ComboBox _palette;
    private readonly ComboBox _fontFamily;
    private readonly NumericUpDown _fontSize;
    private readonly NumericUpDown _scrollback;
    private readonly CheckBox _confirmClose;
    private readonly CheckBox _welcome;
    private readonly CheckBox _showQuickLaunch;
    private readonly CheckBox _showSessionManager;
    private readonly CheckBox _showStatusBar;
    private readonly CheckBox _autoReconnect;
    private readonly NumericUpDown _reconnectSeconds;
    private readonly CheckBox _sftpDoubleClick;

    public XShellSettingsForm(XShellSettings settings)
    {
        _settings = settings;
        Text = "XShell 复刻版 - 设置";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(620, 700);
        BackColor = XShellTheme.Panel;
        ForeColor = XShellTheme.Text;
        Font = XShellTheme.Ui(9f);
        DoubleBuffered = true;
        HandleCreated += (_, _) => XShellWindowTheme.Apply(this);

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 64,
            BackColor = XShellTheme.HeaderFrom
        };
        var title = new Label
        {
            Text = "设置",
            Location = new Point(24, 10),
            Size = new Size(300, 30),
            Font = XShellTheme.Ui(15f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = XShellTheme.HeaderFrom
        };
        var subtitle = new Label
        {
            Text = "界面主题、终端字体与启动行为",
            Location = new Point(26, 38),
            Size = new Size(420, 20),
            Font = XShellTheme.Ui(8.5f),
            ForeColor = Color.FromArgb(190, 216, 242),
            BackColor = XShellTheme.HeaderFrom
        };
        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            BackColor = XShellTheme.PanelAlt
        };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(XShellTheme.Border);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        var ok = new Button
        {
            Text = "确定",
            Location = new Point(420, 13),
            Size = new Size(88, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = XShellTheme.Accent,
            ForeColor = Color.White,
            UseVisualStyleBackColor = false
        };
        ok.FlatAppearance.BorderSize = 0;
        ok.Click += (_, _) => SaveAndClose();
        var cancel = new Button
        {
            Text = "取消",
            Location = new Point(516, 13),
            Size = new Size(88, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = XShellTheme.Panel,
            ForeColor = XShellTheme.Text,
            UseVisualStyleBackColor = false
        };
        cancel.FlatAppearance.BorderColor = XShellTheme.Border;
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        footer.Controls.Add(ok);
        footer.Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = XShellTheme.Panel,
            AutoScroll = true
        };

        _uiTheme = Combo(380);
        _uiTheme.Items.AddRange(new object[] { "深色（默认）", "浅色", "跟随系统" });
        _uiTheme.SelectedIndex = XShellTheme.NormalizeMode(settings.UiTheme) switch
        {
            "Light" => 1,
            "System" => 2,
            _ => 0
        };

        _palette = Combo(380);
        foreach (var palette in XShellPalettes.All)
        {
            _palette.Items.Add(palette.Name);
        }
        _palette.SelectedItem = settings.TerminalTheme;
        if (_palette.SelectedIndex < 0)
        {
            _palette.SelectedIndex = 0;
        }

        _fontFamily = Combo(380);
        _fontFamily.DropDownStyle = ComboBoxStyle.DropDown;
        // 仅列出本机已安装的候选字体（LTSC 等系统可能没有 Cascadia Mono / Fira Code 等），
        // 用户当前设置值始终保留在列表中，避免显示与实际不符。
        var fontCandidates = new[] { "Consolas", "Cascadia Mono", "Cascadia Code",
            "Microsoft YaHei Mono", "Fira Code", "JetBrains Mono" };
        var installedFonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var families = new System.Drawing.Text.InstalledFontCollection();
            foreach (var family in families.Families)
            {
                installedFonts.Add(family.Name);
            }
        }
        catch
        {
            // 枚举失败时不过滤。
        }
        var fontItems = fontCandidates.Where(f => installedFonts.Count == 0 || installedFonts.Contains(f))
            .ToList();
        string currentFont = settings.FontFamily;
        if (currentFont.Length > 0 && !fontItems.Contains(currentFont, StringComparer.OrdinalIgnoreCase))
        {
            fontItems.Insert(0, currentFont);
        }
        _fontFamily.Items.AddRange(fontItems.Cast<object>().ToArray());
        _fontFamily.Text = settings.FontFamily;

        _fontSize = Number(8, 32, settings.FontSize);
        _scrollback = Number(500, 200000, settings.Scrollback, 100);
        _confirmClose = Check("关闭已连接的标签页时确认", settings.ConfirmCloseSession);
        _welcome = Check("启动时显示欢迎页", settings.ShowWelcomeOnStart);
        _showQuickLaunch = Check("显示快速会话栏", settings.ShowQuickLaunch);
        _showSessionManager = Check("启动时显示会话管理器（默认隐藏）", settings.ShowSessionManager);
        _showStatusBar = Check("显示底部状态栏", settings.ShowStatusBar);
        _autoReconnect = Check("连接意外断开后自动重连", settings.AutoReconnect);
        _reconnectSeconds = Number(1, 120, settings.AutoReconnectSeconds);
        _sftpDoubleClick = Check("双击远程文件时下载", settings.SftpDoubleClickDownloads);

        int y = 10;
        AddRow(body, ref y, "界面主题", _uiTheme);
        AddRow(body, ref y, "终端配色", _palette);
        AddRow(body, ref y, "字体", _fontFamily);
        AddRow(body, ref y, "字号", _fontSize);
        AddRow(body, ref y, "回滚行数", _scrollback);
        AddRow(body, ref y, "", _confirmClose);
        AddRow(body, ref y, "", _welcome);
        AddRow(body, ref y, "", _showQuickLaunch);
        AddRow(body, ref y, "", _showSessionManager);
        AddRow(body, ref y, "", _showStatusBar);
        AddRow(body, ref y, "", _autoReconnect);
        AddRow(body, ref y, "重连间隔（秒）", _reconnectSeconds);
        AddRow(body, ref y, "", _sftpDoubleClick);

        Controls.Add(body);
        Controls.Add(footer);
        Controls.Add(header);
        body.BringToFront();
        Controls.SetChildIndex(body, 0);
        Controls.SetChildIndex(footer, 1);
        Controls.SetChildIndex(header, 2);
    }

    private ComboBox Combo(int width)
    {
        return new ComboBox
        {
            Width = width,
            Height = 28,
            Font = XShellTheme.Ui(9f),
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = XShellTheme.InputBack,
            ForeColor = XShellTheme.Text
        };
    }

    private static CheckBox Check(string text, bool value)
    {
        return new CheckBox
        {
            Text = text,
            Checked = value,
            AutoSize = false,
            Width = 420,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft,
            FlatStyle = FlatStyle.Flat,
            BackColor = XShellTheme.Panel,
            ForeColor = XShellTheme.Text,
            Font = XShellTheme.Ui(9f)
        };
    }

    private static NumericUpDown Number(int min, int max, int value, int increment = 1)
    {
        return new NumericUpDown
        {
            Width = 140,
            Height = 28,
            Font = XShellTheme.Ui(9f),
            BackColor = XShellTheme.InputBack,
            ForeColor = XShellTheme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Minimum = min,
            Maximum = max,
            Increment = increment,
            Value = Math.Clamp(value, min, max)
        };
    }

    private static void AddRow(Panel body, ref int y, string label, Control control)
    {
        if (label.Length > 0)
        {
            var caption = new Label
            {
                Text = label,
                Location = new Point(26, y + 8),
                Size = new Size(150, 28),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = XShellTheme.Muted,
                BackColor = XShellTheme.Panel,
                Font = XShellTheme.Ui(9f)
            };
            body.Controls.Add(caption);
        }
        control.Location = new Point(184, y + 6);
        body.Controls.Add(control);
        y += 42;
    }

    private void SaveAndClose()
    {
        _settings.UiTheme = _uiTheme.SelectedIndex switch
        {
            1 => "Light",
            2 => "System",
            _ => "Dark"
        };
        _settings.TerminalTheme = _palette.SelectedItem?.ToString() ?? "XShell 经典";
        _settings.FontFamily = string.IsNullOrWhiteSpace(_fontFamily.Text)
            ? "Consolas" : _fontFamily.Text.Trim();
        _settings.FontSize = (int)_fontSize.Value;
        _settings.Scrollback = (int)_scrollback.Value;
        _settings.ConfirmCloseSession = _confirmClose.Checked;
        _settings.ShowWelcomeOnStart = _welcome.Checked;
        _settings.ShowQuickLaunch = _showQuickLaunch.Checked;
        _settings.ShowSessionManager = _showSessionManager.Checked;
        _settings.ShowStatusBar = _showStatusBar.Checked;
        _settings.AutoReconnect = _autoReconnect.Checked;
        _settings.AutoReconnectSeconds = (int)_reconnectSeconds.Value;
        _settings.SftpDoubleClickDownloads = _sftpDoubleClick.Checked;
        DialogResult = DialogResult.OK;
        Close();
    }
}
