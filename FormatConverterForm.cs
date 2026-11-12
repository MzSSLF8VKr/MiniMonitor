using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// 通用格式转换管理界面：图片 / 音频 / 视频三个选项卡，默认不打开。
/// 窗口支持最大化/还原；可开关资源管理器右键“通用格式转换”菜单。
/// </summary>
public sealed class FormatConverterForm : Form
{
    private readonly AppConfig _config;
    private string? _pendingFile;
    private readonly Panel _header;
    private readonly TabControl _tabs;
    private readonly FormatConverterPanel _imagePanel;
    private readonly FormatConverterPanel _audioPanel;
    private readonly FormatConverterPanel _videoPanel;
    private readonly Panel _bottomHost;
    private readonly CardPanel _bottomCard;
    private readonly CheckBox _contextCheck;
    private readonly Label _contextHint;
    private readonly Label _ffmpegLabel;
    private readonly PillButton _browseFfmpeg;
    private readonly Font _headerTitleFont;
    private readonly Font _headerSubFont;
    private bool _closeHover;
    private bool _maxHover;
    private bool _updating;

    public FormatConverterForm(AppConfig config, string? initialFile = null)
    {
        _config = config;
        _pendingFile = initialFile;

        Text = "通用格式转换 - MiniMonitor";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        var workArea = Screen.PrimaryScreen?.WorkingArea
            ?? new Rectangle(0, 0, 1200, 800);
        ClientSize = new Size(
            Math.Min(1200, Math.Max(1000, workArea.Width - 40)),
            Math.Min(820, Math.Max(700, workArea.Height - 40)));
        MinimumSize = new Size(1000, 700);
        BackColor = ShutdownTheme.Background;
        DoubleBuffered = true;
        KeyPreview = true;
        ShowInTaskbar = true;
        MaximizeBox = true;

        _headerTitleFont = new Font("Microsoft YaHei UI", 19f, FontStyle.Bold);
        _headerSubFont = new Font("Microsoft YaHei UI", 9.5f);

        _header = new Panel
        {
            Dock = DockStyle.Top,
            Size = new Size(1200, 104),
            BackColor = Color.FromArgb(14, 116, 144)
        };
        _header.Paint += PaintHeader;
        _header.MouseMove += (_, e) =>
        {
            bool close = CloseRect.Contains(e.Location);
            bool max = MaximizeRect.Contains(e.Location);
            if (close != _closeHover || max != _maxHover)
            {
                _closeHover = close;
                _maxHover = max;
                _header.Cursor = close || max ? Cursors.Hand : Cursors.Default;
                _header.Invalidate();
            }
        };
        _header.MouseLeave += (_, _) =>
        {
            if (_closeHover || _maxHover)
            {
                _closeHover = false;
                _maxHover = false;
                _header.Cursor = Cursors.Default;
                _header.Invalidate();
            }
        };
        _header.MouseClick += (_, e) =>
        {
            if (CloseRect.Contains(e.Location))
            {
                Close();
            }
            else if (MaximizeRect.Contains(e.Location))
            {
                ToggleMaximize();
            }
        };
        _header.MouseDoubleClick += (_, e) =>
        {
            if (!CloseRect.Contains(e.Location) && !MaximizeRect.Contains(e.Location))
            {
                ToggleMaximize();
            }
        };
        NativeWindowDrag.Attach(this, _header,
            p => CloseRect.Contains(p) || MaximizeRect.Contains(p)
                || WindowState == FormWindowState.Maximized);

        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            Padding = new Point(18, 8)
        };
        _imagePanel = new FormatConverterPanel(_config, MediaCategory.Image);
        _audioPanel = new FormatConverterPanel(_config, MediaCategory.Audio);
        _videoPanel = new FormatConverterPanel(_config, MediaCategory.Video);
        AddTab("图片格式转换", _imagePanel);
        AddTab("音频格式转换", _audioPanel);
        AddTab("视频格式转换", _videoPanel);

        var tabsHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 8, 16, 6),
            BackColor = ShutdownTheme.Background
        };
        tabsHost.Controls.Add(_tabs);

        _bottomHost = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 96,
            BackColor = ShutdownTheme.Background,
            Padding = new Padding(16, 4, 16, 12)
        };
        _bottomCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 14
        };
        _contextCheck = new CheckBox
        {
            Location = new Point(14, 8),
            Size = new Size(430, 24),
            Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold),
            Text = "启用资源管理器右键菜单：通用格式转换（默认关闭）"
        };
        _contextCheck.CheckedChanged += (_, _) => OnContextMenuToggled();
        _contextHint = new Label
        {
            Location = new Point(14, 34),
            Size = new Size(520, 40),
            Font = new Font("Microsoft YaHei UI", 7.8f),
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = false,
            AutoEllipsis = true
        };
        _ffmpegLabel = new Label
        {
            Location = new Point(550, 12),
            Size = new Size(420, 56),
            Font = new Font("Microsoft YaHei UI", 7.8f),
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _browseFfmpeg = new PillButton
        {
            Text = "设置 FFmpeg…",
            Location = new Point(0, 16),
            Size = new Size(130, 32),
            ButtonStyle = PillButtonStyle.Light
        };
        _browseFfmpeg.Click += (_, _) => BrowseFfmpeg();
        _bottomCard.Controls.AddRange(new Control[]
        {
            _contextCheck, _contextHint, _ffmpegLabel, _browseFfmpeg
        });
        _bottomCard.Resize += (_, _) => LayoutBottomCard();
        _bottomHost.Controls.Add(_bottomCard);
        Controls.Add(tabsHost);
        Controls.Add(_bottomHost);
        Controls.Add(_header);

        _updating = true;
        _contextCheck.Checked = _config.ExplorerContextMenuEnabled;
        _updating = false;
        UpdateContextHint();
        UpdateFfmpegStatus();
        LayoutBottomCard();

        // 确保注册表状态与配置一致（例如程序移动目录后更新命令行路径）。
        ExplorerContextMenu.SetEnabled(_config.ExplorerContextMenuEnabled,
            Application.ExecutablePath, out _);
    }

    private void AddTab(string title, Control content)
    {
        var page = new TabPage(title)
        {
            BackColor = Color.White,
            Padding = new Padding(0)
        };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        _tabs.TabPages.Add(page);
    }

    private void LayoutBottomCard()
    {
        int width = _bottomCard.ClientSize.Width;
        _browseFfmpeg.Location = new Point(Math.Max(0, width - 144), 16);
        _ffmpegLabel.Location = new Point(Math.Min(550, Math.Max(440, width / 2)), 12);
        _ffmpegLabel.Width = Math.Max(100, _browseFfmpeg.Left - 10 - _ffmpegLabel.Left);
        _contextHint.Width = Math.Max(120, _ffmpegLabel.Left - 20);
    }

    private void UpdateContextHint()
    {
        _contextHint.Text = _contextCheck.Checked
            ? "已启用：支持的图片 / 音频 / 视频文件右键可见“通用格式转换”。\r\n"
                + "Windows 11 在“显示更多选项”中；若未刷新可重启资源管理器。"
            : "默认关闭；开启后支持的媒体文件右键会出现“通用格式转换”。\r\n"
                + "Windows 11 在“显示更多选项”中；若未刷新可重启资源管理器。";
    }

    private void OnContextMenuToggled()
    {
        if (_updating)
        {
            return;
        }
        _config.ExplorerContextMenuEnabled = _contextCheck.Checked;
        _config.Save();
        bool ok = ExplorerContextMenu.SetEnabled(_contextCheck.Checked,
            Application.ExecutablePath, out string? error);
        if (!ok)
        {
            MessageBox.Show(FindForm(),
                "写入右键菜单注册表失败：\n" + (error ?? "未知错误")
                + "\n\n可尝试以管理员身份运行，或检查安全软件是否拦截注册表写入。",
                "通用格式转换", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _updating = true;
            _contextCheck.Checked = !_contextCheck.Checked;
            _updating = false;
            _config.ExplorerContextMenuEnabled = _contextCheck.Checked;
            _config.Save();
        }
        UpdateContextHint();
    }

    private void UpdateFfmpegStatus()
    {
        string? ffmpeg = FfmpegLocator.Find(_config.FfmpegPath);
        if (ffmpeg != null)
        {
            _ffmpegLabel.Text = "FFmpeg：已找到\n" + ffmpeg;
            _ffmpegLabel.ForeColor = ShutdownTheme.Success;
        }
        else
        {
            _ffmpegLabel.Text = "FFmpeg：未找到\n音频、视频及 WebP / AVIF / APNG 转换需要 ffmpeg.exe";
            _ffmpegLabel.ForeColor = ShutdownTheme.Danger;
        }
    }

    private void BrowseFfmpeg()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择 ffmpeg.exe",
            Filter = "FFmpeg|ffmpeg.exe|可执行文件|*.exe",
            CheckFileExists = true
        };
        if (FfmpegLocator.Find(_config.FfmpegPath) is string current)
        {
            dialog.InitialDirectory = Path.GetDirectoryName(current);
        }
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            _config.FfmpegPath = dialog.FileName;
            _config.Save();
            UpdateFfmpegStatus();
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_pendingFile == null)
        {
            return;
        }
        string file = _pendingFile;
        _pendingFile = null;
        var category = ConverterFormats.DetectCategory(file);
        if (category == null)
        {
            MessageBox.Show(this,
                "暂不支持该文件格式：\n" + file + "\n\n"
                + "支持图片、音频和视频的常见格式。",
                "通用格式转换", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        int index = category switch
        {
            MediaCategory.Audio => 1,
            MediaCategory.Video => 2,
            _ => 0
        };
        _tabs.SelectedIndex = index;
        var panel = index switch
        {
            1 => _audioPanel,
            2 => _videoPanel,
            _ => _imagePanel
        };
        panel.AddExternalFile(file);
    }

    private Rectangle CloseRect
    {
        get
        {
            float s = _header.Height / 104f;
            return new Rectangle(
                (int)(_header.Width - 52f * s),
                (int)(14f * s),
                (int)(34f * s),
                (int)(34f * s));
        }
    }

    private Rectangle MaximizeRect
    {
        get
        {
            float s = _header.Height / 104f;
            return new Rectangle(
                (int)(_header.Width - 96f * s),
                (int)(14f * s),
                (int)(34f * s),
                (int)(34f * s));
        }
    }

    private void ToggleMaximize()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            WindowState = FormWindowState.Normal;
        }
        else
        {
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
            WindowState = FormWindowState.Maximized;
        }
        _header.Invalidate();
    }

    private void PaintHeader(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        float s = _header.Height / 104f;

        using (var brush = new LinearGradientBrush(_header.ClientRectangle,
            Color.FromArgb(14, 116, 144), Color.FromArgb(8, 145, 178),
            LinearGradientMode.Horizontal))
        {
            g.FillRectangle(brush, _header.ClientRectangle);
        }
        using (var glow = new SolidBrush(Color.FromArgb(22, 255, 255, 255)))
        {
            g.FillEllipse(glow, _header.Width - 300, -130, 380, 380);
            g.FillEllipse(glow, _header.Width - 120, 20, 160, 160);
        }

        TextRenderer.DrawText(g, "通用格式转换", _headerTitleFont,
            new Rectangle((int)(28f * s), (int)(12f * s),
                (int)(_header.Width - 220f * s), (int)(44f * s)),
            Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "图片 · 音频 · 视频批量格式转换", _headerSubFont,
            new Rectangle((int)(30f * s), (int)(54f * s),
                (int)(_header.Width - 240f * s), (int)(26f * s)),
            Color.FromArgb(224, 251, 252), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        var close = CloseRect;
        if (_closeHover)
        {
            using var hover = new SolidBrush(Color.FromArgb(42, 255, 255, 255));
            g.FillEllipse(hover, close);
        }
        using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 2f * s))
        {
            int inset = (int)(12f * s);
            g.DrawLine(pen, close.Left + inset, close.Top + inset, close.Right - inset, close.Bottom - inset);
            g.DrawLine(pen, close.Right - inset, close.Top + inset, close.Left + inset, close.Bottom - inset);
        }

        var maximize = MaximizeRect;
        if (_maxHover)
        {
            using var hover = new SolidBrush(Color.FromArgb(42, 255, 255, 255));
            g.FillEllipse(hover, maximize);
        }
        using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 1.8f * s))
        {
            int cx = maximize.Left + maximize.Width / 2;
            int cy = maximize.Top + maximize.Height / 2;
            if (WindowState == FormWindowState.Maximized)
            {
                g.DrawRectangle(pen, cx - 8, cy - 6, 11, 9);
                g.DrawRectangle(pen, cx - 3, cy - 1, 11, 9);
            }
            else
            {
                g.DrawRectangle(pen, cx - 7, cy - 6, 15, 12);
            }
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_header == null || _header.IsDisposed)
        {
            return;
        }
        _header.Size = new Size(ClientSize.Width, 104);
        float radius = WindowState == FormWindowState.Maximized
            ? 0f
            : 18f * (Height / 820f);
        using var path = ShutdownTheme.RoundRect(new RectangleF(0, 0, Width, Height), radius);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
        _header.Invalidate();
        if (_bottomCard != null && !_bottomCard.IsDisposed)
        {
            LayoutBottomCard();
        }
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
