using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>OCR 识别结果窗口：文本可编辑、可复制。</summary>
public sealed class OcrResultForm : Form
{
    private readonly TextBox _textBox;
    private readonly Button _copyButton;
    private readonly System.Windows.Forms.Timer _copyResetTimer;
    private readonly Font _layoutFont;

    public OcrResultForm(string text, string language, TimeSpan elapsed)
    {
        Text = "OCR 识别结果 - MiniMonitor";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(900, 680);
        MinimumSize = new Size(620, 420);
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;
        BackColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9f);

        var title = new Label
        {
            Text = "OCR 识别结果（按原图排版）",
            Location = new Point(18, 14),
            Size = new Size(500, 28),
            Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White,
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        var info = new Label
        {
            Text = "Windows 本地 OCR · 语言：" + (string.IsNullOrWhiteSpace(language) ? "默认" : language)
                + " · 用时 " + elapsed.TotalMilliseconds.ToString("0") + " ms"
                + " · 已按原图排版，等宽字体显示；文本可编辑后复制",
            Location = new Point(20, 48),
            Size = new Size(860, 22),
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoEllipsis = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _layoutFont = CreateLayoutFont();
        _textBox = new TextBox
        {
            Location = new Point(18, 80),
            Size = new Size(864, 508),
            Multiline = true,
            AcceptsReturn = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            MaxLength = 0,
            Font = _layoutFont,
            BackColor = Color.FromArgb(248, 250, 252),
            ForeColor = ShutdownTheme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Text = text ?? "",
            HideSelection = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        _copyButton = new Button
        {
            Text = "复制全部",
            Location = new Point(630, 612),
            Size = new Size(120, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(13, 148, 136),
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _copyButton.FlatAppearance.BorderColor = Color.FromArgb(13, 148, 136);
        _copyButton.Click += (_, _) => CopyAll();

        var closeButton = new Button
        {
            Text = "关闭",
            Location = new Point(762, 612),
            Size = new Size(120, 34),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            DialogResult = DialogResult.Cancel,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        closeButton.FlatAppearance.BorderColor = ShutdownTheme.CardBorder;
        CancelButton = closeButton;

        _copyResetTimer = new System.Windows.Forms.Timer { Interval = 1600 };
        _copyResetTimer.Tick += (_, _) =>
        {
            _copyResetTimer.Stop();
            _copyButton.Text = "复制全部";
        };

        Controls.AddRange(new Control[] { title, info, _textBox, _copyButton, closeButton });
        Shown += (_, _) =>
        {
            _textBox.Focus();
            _textBox.SelectAll();
        };
    }

    private static Font CreateLayoutFont()
    {
        try
        {
            var names = FontFamily.Families
                .Select(family => family.Name)
                .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            foreach (string candidate in new[] { "NSimSun", "SimSun", "新宋体", "Consolas" })
            {
                if (names.Contains(candidate))
                {
                    return new Font(candidate, 11f);
                }
            }
        }
        catch
        {
            // 字体枚举失败时使用通用等宽字体
        }
        return new Font(FontFamily.GenericMonospace, 11f);
    }

    private void CopyAll()
    {
        if (string.IsNullOrEmpty(_textBox.Text))
        {
            return;
        }
        try
        {
            Clipboard.SetText(_textBox.Text);
            _copyButton.Text = "已复制";
            _copyResetTimer.Stop();
            _copyResetTimer.Start();
        }
        catch
        {
            _copyButton.Text = "复制失败";
            _copyResetTimer.Stop();
            _copyResetTimer.Start();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _copyResetTimer.Stop();
            _copyResetTimer.Dispose();
            _layoutFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
