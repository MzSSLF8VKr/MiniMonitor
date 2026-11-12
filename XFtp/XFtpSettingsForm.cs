using System.Windows.Forms;

namespace MiniMonitor.XFtp;

/// <summary>
/// XFtp 复刻版设置。为保证高 DPI 与深色主题下文字始终清晰、不出现重绘残影，
/// 此窗口与 XShell 复刻版设置一致：只使用标准 WinForms 控件 + 实色背景 + 固定行距，
/// 不使用自绘透明控件与渐变面板。
/// </summary>
internal sealed class XFtpSettingsForm : Form
{
    private readonly CheckBox _enableQuickConnect;
    private readonly CheckBox _showToolbar;
    private readonly CheckBox _showQueue;
    private readonly CheckBox _showStatus;
    private readonly CheckBox _showInTaskbar;

    public bool EnableQuickConnect => _enableQuickConnect.Checked;
    public bool ShowToolbar => _showToolbar.Checked;
    public bool ShowTransferQueue => _showQueue.Checked;
    public bool ShowStatusBar => _showStatus.Checked;
    public bool ShowTaskbar => _showInTaskbar.Checked;

    public XFtpSettingsForm(XFtpSettings settings)
    {
        Text = "XFTP 复刻版 - 设置";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(560, 430);
        BackColor = XFtpTheme.Panel;
        ForeColor = XFtpTheme.Text;
        Font = XFtpTheme.Ui9;
        DoubleBuffered = true;
        HandleCreated += (_, _) => XFtpTheme.ApplyWindow(this);

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 64,
            BackColor = XFtpTheme.HeaderFrom
        };
        var title = new Label
        {
            Text = "设置",
            Location = new Point(26, 10),
            Size = new Size(300, 30),
            Font = XFtpTheme.Ui(15f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = XFtpTheme.HeaderFrom
        };
        var subtitle = new Label
        {
            Text = "快速连接功能与界面显示",
            Location = new Point(28, 38),
            Size = new Size(420, 20),
            Font = XFtpTheme.Ui85,
            ForeColor = XFtpTheme.Muted,
            BackColor = XFtpTheme.HeaderFrom
        };
        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            BackColor = XFtpTheme.PanelAlt
        };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(XFtpTheme.Border);
            e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
        };
        var ok = new Button
        {
            Text = "确定",
            Location = new Point(352, 12),
            Size = new Size(90, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = XFtpTheme.Accent,
            ForeColor = Color.White,
            UseVisualStyleBackColor = false
        };
        ok.FlatAppearance.BorderSize = 0;
        ok.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        var cancel = new Button
        {
            Text = "取消",
            Location = new Point(452, 12),
            Size = new Size(90, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = XFtpTheme.Panel,
            ForeColor = XFtpTheme.Text,
            UseVisualStyleBackColor = false
        };
        cancel.FlatAppearance.BorderColor = XFtpTheme.Border;
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
            BackColor = XFtpTheme.Panel,
            AutoScroll = true
        };

        _enableQuickConnect = Check("启用快速连接功能", settings.EnableQuickConnect);
        _showToolbar = Check("显示工具栏", settings.ShowToolbar);
        _showQueue = Check("显示传输队列", settings.ShowTransferQueue);
        _showStatus = Check("显示状态栏", settings.ShowStatusBar);
        _showInTaskbar = Check("在 Windows 任务栏显示窗口", settings.ShowInTaskbar);

        int y = 18;
        AddRow(body, ref y, "", _enableQuickConnect);
        AddRow(body, ref y, "", _showToolbar);
        AddRow(body, ref y, "", _showQueue);
        AddRow(body, ref y, "", _showStatus);
        AddRow(body, ref y, "", _showInTaskbar);
        var hint = new Label
        {
            Text = "快速连接功能默认关闭：关闭时工具栏不显示“快速连接”按钮，\r\n也不能使用快速连接栏；仍可通过站点管理器连接服务器。",
            Location = new Point(28, y + 12),
            Size = new Size(500, 44),
            ForeColor = XFtpTheme.Muted,
            BackColor = XFtpTheme.Panel,
            Font = XFtpTheme.Ui85
        };
        body.Controls.Add(hint);

        Controls.Add(body);
        Controls.Add(footer);
        Controls.Add(header);
        body.BringToFront();
        Controls.SetChildIndex(body, 0);
        Controls.SetChildIndex(footer, 1);
        Controls.SetChildIndex(header, 2);
    }

    private static CheckBox Check(string text, bool value)
    {
        return new CheckBox
        {
            Text = text,
            Checked = value,
            AutoSize = false,
            Width = 440,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft,
            FlatStyle = FlatStyle.Flat,
            BackColor = XFtpTheme.Panel,
            ForeColor = XFtpTheme.Text,
            Font = XFtpTheme.Ui9
        };
    }

    private static void AddRow(Panel body, ref int y, string label, Control control)
    {
        if (label.Length > 0)
        {
            var caption = new Label
            {
                Text = label,
                Location = new Point(28, y + 8),
                Size = new Size(150, 28),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = XFtpTheme.Muted,
                BackColor = XFtpTheme.Panel,
                Font = XFtpTheme.Ui9
            };
            body.Controls.Add(caption);
        }
        control.Location = new Point(28, y + 6);
        body.Controls.Add(control);
        y += 42;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        XFtpTheme.ApplyNative(this);
    }
}
