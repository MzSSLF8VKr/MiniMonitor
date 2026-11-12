using System.Text;
using System.Windows.Forms;

namespace MiniMonitor.Navicat;

/// <summary>可复制的详细错误窗口，用于展示连接失败等需要排错的完整信息。</summary>
internal sealed class NvErrorDialog : Form
{
    private readonly string _details;

    public NvErrorDialog(string title, string summary, string details, string? footer = null)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(880, 580);
        MinimumSize = new Size(640, 420);
        BackColor = NvTheme.Background;
        Font = NvTheme.Ui(9f);
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        _details = details ?? "";

        var header = new Panel { Dock = DockStyle.Fill, BackColor = NvTheme.HeaderBack };
        var icon = new PictureBox
        {
            Image = NvIcons.Get(NvIcon.Warning, 32),
            SizeMode = PictureBoxSizeMode.AutoSize,
            Location = new Point(16, 16)
        };
        header.Controls.Add(icon);
        var titleLabel = new Label
        {
            Text = title,
            Location = new Point(60, 12),
            Size = new Size(10, 26),
            Font = NvTheme.Ui(11f, FontStyle.Bold),
            ForeColor = NvTheme.Text
        };
        header.Controls.Add(titleLabel);
        var summaryLabel = new Label
        {
            Text = summary,
            Location = new Point(60, 38),
            Size = new Size(10, 22),
            ForeColor = NvTheme.Muted
        };
        header.Controls.Add(summaryLabel);
        header.Resize += (_, _) =>
        {
            icon.Location = new Point(Math.Max(8, header.Width - 56), 16);
            int width = Math.Max(120, header.Width - 136);
            titleLabel.Width = width;
            summaryLabel.Width = width;
        };

        var box = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = NvTheme.Mono(9f),
            BackColor = Color.FromArgb(253, 253, 253),
            ForeColor = NvTheme.Text,
            Text = _details
        };
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 8) };
        body.Controls.Add(box);

        var bottom = new Panel { Dock = DockStyle.Fill, BackColor = NvTheme.Background };
        bottom.Paint += (_, e) =>
        {
            using var pen = new Pen(NvTheme.BorderLight);
            e.Graphics.DrawLine(pen, 0, 0, bottom.Width, 0);
        };
        var copy = NvTheme.FlatButton("复制全部", NvTheme.Accent, Color.White, 104);
        copy.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        copy.Click += (_, _) =>
        {
            Clipboard.SetText(_details);
            copy.Text = "已复制";
            var timer = new System.Windows.Forms.Timer { Interval = 1200 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                timer.Dispose();
                copy.Text = "复制全部";
            };
            timer.Start();
        };
        var openLog = NvTheme.FlatButton("打开日志目录", null, null, 116);
        openLog.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        openLog.Click += (_, _) => NvLog.OpenFolder();
        var close = NvTheme.FlatButton("关闭", null, null, 88);
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.DialogResult = DialogResult.OK;
        bottom.Controls.AddRange(new Control[] { copy, openLog, close });
        bottom.Resize += (_, _) =>
        {
            int right = bottom.ClientSize.Width - 14;
            close.Location = new Point(right - close.Width, 12);
            copy.Location = new Point(close.Left - copy.Width - 8, 12);
            openLog.Location = new Point(copy.Left - openLog.Width - 8, 12);
        };

        var footerLabel = new Label
        {
            Text = footer ?? "",
            Dock = DockStyle.Bottom,
            Height = footer == null ? 0 : 28,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 0, 0),
            ForeColor = NvTheme.Muted
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = NvTheme.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, footer == null ? 0 : 28));
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(body, 0, 1);
        root.Controls.Add(bottom, 0, 2);
        root.Controls.Add(footerLabel, 0, 3);
        Controls.Add(root);
        Shown += (_, _) => box.Focus();
    }

    public static void Show(IWin32Window? owner, string title, string summary, string details,
        string? footer = null)
    {
        owner = NvPrompt.SafeOwner(owner);
        using var dialog = new NvErrorDialog(title, summary, details, footer);
        if (owner == null)
        {
            dialog.ShowDialog();
        }
        else
        {
            dialog.ShowDialog(owner);
        }
    }
}
