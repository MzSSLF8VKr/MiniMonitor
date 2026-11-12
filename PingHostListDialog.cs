using System.IO;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>PingInfoView 主机列表编辑窗口。</summary>
public sealed class PingHostListDialog : Form
{
    private readonly TextBox _textBox;

    public PingHostListDialog(string initialText)
    {
        Text = "主机列表 - PingInfoView";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(840, 640);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9f);
        KeyPreview = true;

        var title = new Label
        {
            Text = "PingInfoView 主机列表",
            Location = new Point(18, 14),
            Size = new Size(500, 26),
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White
        };

        var help = new Label
        {
            Text =
                "每行一个主机，支持：IP / 域名、TCP Ping（主机:端口，例如 192.168.1.10:80、[2001:db8::1]:443）、\n" +
                "描述（192.168.1.10 主服务器）、分组（Group: 名称）、注释（# 开头）、\n" +
                "IPv4 范围（192.168.1.10-20）和 CIDR（192.168.0.0/24）；IPv6 需在“选项”中启用。\n" +
                "CIDR 是否跳过首尾地址可在“选项”中设置。",
            Location = new Point(18, 44),
            Size = new Size(800, 74),
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White
        };

        _textBox = new TextBox
        {
            Location = new Point(18, 124),
            Size = new Size(804, 430),
            Multiline = true,
            AcceptsReturn = true,
            AcceptsTab = false,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 10f),
            BackColor = Color.FromArgb(248, 250, 252),
            ForeColor = ShutdownTheme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Text = initialText ?? "",
            HideSelection = false
        };

        var importButton = new Button
        {
            Text = "从文件导入…",
            Location = new Point(18, 572),
            Size = new Size(120, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text
        };
        importButton.FlatAppearance.BorderColor = ShutdownTheme.CardBorder;
        importButton.Click += (_, _) => ImportFromFile();

        var clearButton = new Button
        {
            Text = "清空",
            Location = new Point(148, 572),
            Size = new Size(90, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text
        };
        clearButton.FlatAppearance.BorderColor = ShutdownTheme.CardBorder;
        clearButton.Click += (_, _) => _textBox.Clear();

        var okButton = new Button
        {
            Text = "确定",
            Location = new Point(636, 572),
            Size = new Size(90, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(13, 148, 136),
            ForeColor = Color.White,
            DialogResult = DialogResult.OK
        };
        okButton.FlatAppearance.BorderColor = Color.FromArgb(13, 148, 136);
        var cancelButton = new Button
        {
            Text = "取消",
            Location = new Point(734, 572),
            Size = new Size(90, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            DialogResult = DialogResult.Cancel
        };
        cancelButton.FlatAppearance.BorderColor = ShutdownTheme.CardBorder;

        Controls.AddRange(new Control[]
        {
            title, help, _textBox, importButton, clearButton, okButton, cancelButton
        });
        CancelButton = cancelButton;
        Shown += (_, _) =>
        {
            _textBox.Focus();
            _textBox.SelectionStart = _textBox.TextLength;
        };
    }

    public string HostsText => _textBox.Text;

    private void ImportFromFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "导入主机列表",
            Filter = "文本文件|*.txt;*.csv;*.list|所有文件|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            _textBox.Text = File.ReadAllText(dialog.FileName);
            _textBox.SelectionStart = 0;
            _textBox.ScrollToCaret();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "读取文件失败：" + ex.Message, "PingInfoView",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
