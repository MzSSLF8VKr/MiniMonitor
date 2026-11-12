using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>覆写前的二次确认窗口：必须手动输入 WIPE &lt;磁盘号&gt; 才能开始。</summary>
public sealed class WipeConfirmForm : Form
{
    private readonly WipeDiskInfo _disk;
    private readonly WipeAlgorithm _algorithm;
    private readonly bool _encryptPrePass;
    private readonly string _phrase;
    private readonly Panel _header;
    private readonly TextBox _input;
    private readonly PillButton _startButton;
    private readonly Label _inputHint;
    private readonly Font _headerTitleFont;
    private readonly Font _headerSubFont;
    private bool _closeHover;

    public WipeConfirmForm(WipeDiskInfo disk, WipeAlgorithm algorithm, bool encryptPrePass)
    {
        _disk = disk;
        _algorithm = algorithm;
        _encryptPrePass = encryptPrePass;
        _phrase = "WIPE " + disk.Number.ToString();

        Text = "确认覆写磁盘 - MiniMonitor";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 460);
        BackColor = ShutdownTheme.Background;
        DoubleBuffered = true;
        KeyPreview = true;
        ShowInTaskbar = true;

        _headerTitleFont = new Font("Microsoft YaHei UI", 18f, FontStyle.Bold);
        _headerSubFont = new Font("Microsoft YaHei UI", 9f);

        _header = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(ClientSize.Width, 90),
            BackColor = Color.FromArgb(127, 29, 29)
        };
        _header.Paint += PaintHeader;
        _header.MouseMove += (_, e) =>
        {
            bool hover = CloseRect.Contains(e.Location);
            if (hover != _closeHover)
            {
                _closeHover = hover;
                _header.Cursor = hover ? Cursors.Hand : Cursors.Default;
                _header.Invalidate();
            }
        };
        _header.MouseLeave += (_, _) =>
        {
            if (_closeHover)
            {
                _closeHover = false;
                _header.Cursor = Cursors.Default;
                _header.Invalidate();
            }
        };
        _header.MouseClick += (_, e) =>
        {
            if (CloseRect.Contains(e.Location))
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };
        NativeWindowDrag.Attach(this, _header, p => CloseRect.Contains(p));
        Controls.Add(_header);

        var card = new CardPanel
        {
            Location = new Point(24, 104),
            Size = new Size(512, 300)
        };

        var cardTitle = MakeLabel("目标磁盘", 18, 12, 476, 24,
            new Font("Microsoft YaHei UI", 11f, FontStyle.Bold), ShutdownTheme.Text);
        var model = MakeLabel(_disk.Model, 18, 38, 476, 26,
            new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold), ShutdownTheme.Text);
        string info = "PhysicalDrive " + _disk.Number + " · "
            + FormatBytes(_disk.Size) + " · " + _disk.BusName
            + (_disk.Removable ? " · 可移动磁盘" : "")
            + (_disk.Serial.Length > 0 ? " · SN " + _disk.Serial : "");
        var infoLabel = MakeLabel(info, 18, 66, 476, 20,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Muted);
        var algorithmLabel = MakeLabel(
            "算法：" + DiskWipeEngine.AlgorithmName(_algorithm)
            + (_encryptPrePass
                ? "（AES-256 加密预覆盖 + " + DiskWipeEngine.AlgorithmPasses(_algorithm) + " 遍覆写）"
                : "（仅 " + DiskWipeEngine.AlgorithmPasses(_algorithm) + " 遍覆写，无 AES 预覆盖）"),
            18, 92, 476, 20,
            new Font("Microsoft YaHei UI", 9f, FontStyle.Bold), ShutdownTheme.Text);
        var warning = MakeLabel(
            "警告：开始后将永久覆写该磁盘的全部扇区，分区表、文件系统和所有数据均无法恢复。\n"
            + "请再次核对磁盘编号、容量和型号，确保没有选错磁盘。",
            18, 118, 476, 46,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Danger);
        warning.TextAlign = ContentAlignment.TopLeft;

        var inputLabel = MakeLabel(
            "请输入 " + _phrase + " 以确认（不区分大小写）：",
            18, 172, 476, 20,
            new Font("Microsoft YaHei UI", 9f, FontStyle.Bold), ShutdownTheme.Text);
        _input = new TextBox
        {
            Location = new Point(18, 196),
            Size = new Size(476, 32),
            Font = new Font("Microsoft YaHei UI", 11f),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text
        };
        _input.TextChanged += (_, _) => UpdateStartState();
        _input.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                Submit();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };
        _inputHint = MakeLabel("输入内容必须与上方完全一致，才会启用“开始覆写”按钮。",
            18, 232, 476, 18,
            new Font("Microsoft YaHei UI", 7.5f), ShutdownTheme.Muted);

        var cancelButton = new PillButton
        {
            Text = "取消",
            ButtonStyle = PillButtonStyle.Light,
            Location = new Point(18, 250),
            Size = new Size(140, 38),
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
            BackColor = Color.White
        };
        cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        _startButton = new PillButton
        {
            Text = "开始覆写",
            ButtonStyle = PillButtonStyle.Danger,
            Location = new Point(338, 250),
            Size = new Size(156, 38),
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
            BackColor = Color.White,
            Enabled = false
        };
        _startButton.Click += (_, _) => Submit();

        card.Controls.AddRange(new Control[]
        {
            cardTitle, model, infoLabel, algorithmLabel, warning,
            inputLabel, _input, _inputHint, cancelButton, _startButton
        });
        Controls.Add(card);

        var footer = MakeLabel(
            "覆写一旦开始，即使立即取消也可能已经写入部分扇区，数据将无法恢复。",
            24, 418, 512, 24,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted);
        Controls.Add(footer);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };

        Shown += (_, _) => _input.Focus();
    }

    private static Label MakeLabel(string text, int x, int y, int width, int height,
        Font font, Color color)
    {
        return new Label
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            Font = font,
            ForeColor = color,
            BackColor = Color.White,
            AutoSize = false
        };
    }

    private string Phrase => _phrase;

    private void UpdateStartState()
    {
        bool ok = string.Equals(_input.Text.Trim(), Phrase, StringComparison.OrdinalIgnoreCase);
        if (_startButton.Enabled != ok)
        {
            _startButton.Enabled = ok;
            _inputHint.ForeColor = ok ? ShutdownTheme.Success : ShutdownTheme.Muted;
        }
    }

    private void Submit()
    {
        UpdateStartState();
        if (!_startButton.Enabled)
        {
            System.Media.SystemSounds.Beep.Play();
            _input.Focus();
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private Rectangle CloseRect
    {
        get
        {
            float s = _header.Height / 90f;
            return new Rectangle(
                (int)(_header.Width - 48f * s),
                (int)(12f * s),
                (int)(32f * s),
                (int)(32f * s));
        }
    }

    private void PaintHeader(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        float s = _header.Height / 90f;

        using (var brush = new LinearGradientBrush(_header.ClientRectangle,
            Color.FromArgb(127, 29, 29), Color.FromArgb(220, 38, 38),
            LinearGradientMode.Horizontal))
        {
            g.FillRectangle(brush, _header.ClientRectangle);
        }
        using (var glow = new SolidBrush(Color.FromArgb(20, 255, 255, 255)))
        {
            g.FillEllipse(glow, _header.Width - 190, -80, 260, 260);
            g.FillEllipse(glow, _header.Width - 70, 30, 110, 110);
        }

        TextRenderer.DrawText(g, "确认覆写磁盘", _headerTitleFont,
            new Rectangle((int)(24f * s), (int)(10f * s), (int)(_header.Width - 130f * s), (int)(40f * s)),
            Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "此操作将永久销毁目标磁盘上的全部数据", _headerSubFont,
            new Rectangle((int)(26f * s), (int)(50f * s), (int)(_header.Width - 140f * s), (int)(24f * s)),
            Color.FromArgb(254, 226, 226), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        var close = CloseRect;
        if (_closeHover)
        {
            using var hover = new SolidBrush(Color.FromArgb(42, 255, 255, 255));
            g.FillEllipse(hover, close);
        }
        using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 2f * s))
        {
            int inset = (int)(11f * s);
            g.DrawLine(pen, close.Left + inset, close.Top + inset, close.Right - inset, close.Bottom - inset);
            g.DrawLine(pen, close.Right - inset, close.Top + inset, close.Left + inset, close.Bottom - inset);
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1L << 40)
        {
            return (bytes / (double)(1L << 40)).ToString("0.00") + " TB";
        }
        if (bytes >= 1L << 30)
        {
            return (bytes / (double)(1L << 30)).ToString("0.0") + " GB";
        }
        if (bytes >= 1L << 20)
        {
            return (bytes / (double)(1L << 20)).ToString("0.0") + " MB";
        }
        return (bytes / (double)(1L << 10)).ToString("0.0") + " KB";
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        float scale = Height / 460f;
        using var path = ShutdownTheme.RoundRect(new RectangleF(0, 0, Width, Height), 16f * scale);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _headerTitleFont.Dispose();
            _headerSubFont.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000;
            return cp;
        }
    }
}
