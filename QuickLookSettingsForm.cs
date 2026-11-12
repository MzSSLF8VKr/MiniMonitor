using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// QuickLook 复刻管理 / 设置界面。功能本身默认开启、在后台监听空格键；
/// 本窗口只用于启停、窗口行为与插件开关设置。
/// </summary>
public sealed class QuickLookSettingsForm : Form
{
    private readonly AppConfig _config;
    private readonly QuickLookService _service;
    private readonly Panel _header;
    private readonly TabControl _tabs;
    private readonly ToggleSwitch _enableToggle;
    private readonly Label _statusLabel;
    private readonly CheckBox _topMostCheck;
    private readonly NumericUpDown _widthBox;
    private readonly NumericUpDown _heightBox;
    private readonly Label _footerStatus;
    private readonly Font _headerTitleFont;
    private readonly Font _headerSubFont;
    private bool _closeHover;
    private bool _maxHover;
    private bool _updating;

    public QuickLookSettingsForm(AppConfig config, QuickLookService service)
    {
        _config = config;
        _service = service;

        Text = "QuickLook复刻 - MiniMonitor";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        var workArea = Screen.PrimaryScreen?.WorkingArea
            ?? new Rectangle(0, 0, 1000, 720);
        ClientSize = new Size(
            Math.Min(1000, Math.Max(860, workArea.Width - 40)),
            Math.Min(720, Math.Max(640, workArea.Height - 40)));
        MinimumSize = new Size(860, 640);
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
            Size = new Size(1000, 104),
            BackColor = Color.FromArgb(30, 41, 59)
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
            _closeHover = false;
            _maxHover = false;
            _header.Cursor = Cursors.Default;
            _header.Invalidate();
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

        // ---------- 常规 ----------
        var generalPage = new TabPage("常规设置") { BackColor = Color.White, Padding = new Padding(14) };
        var generalLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.White
        };
        generalLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 176));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        generalLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        generalPage.Controls.Add(generalLayout);
        _tabs.TabPages.Add(generalPage);

        var enableCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
            CornerRadius = 14
        };
        _enableToggle = new ToggleSwitch
        {
            Location = new Point(20, 20),
            Size = new Size(56, 30),
            OnColor = Color.FromArgb(13, 148, 136),
            Checked = _config.QuickLookEnabled
        };
        var enableTitle = MakeLabel("启用 QuickLook复刻", 92, 12, 300, 26,
            new Font("Microsoft YaHei UI", 11f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var enableHint = MakeLabel("默认开启 · 资源管理器中选中文件后按空格预览",
            94, 40, 500, 22,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        _statusLabel = MakeLabel("", 600, 22, 260, 28,
            new Font("Microsoft YaHei UI", 9f, FontStyle.Bold), ShutdownTheme.Success, Color.White);
        _statusLabel.TextAlign = ContentAlignment.MiddleRight;
        _statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        enableCard.Controls.AddRange(new Control[]
        {
            _enableToggle, enableTitle, enableHint, _statusLabel
        });
        enableCard.Resize += (_, _) =>
        {
            _statusLabel.Width = Math.Max(120, enableCard.ClientSize.Width - _statusLabel.Left - 20);
        };
        generalLayout.Controls.Add(enableCard, 0, 0);

        var behaviorCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
            CornerRadius = 14
        };
        var behaviorTitle = MakeLabel("预览窗口", 20, 12, 240, 24,
            new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _topMostCheck = new CheckBox
        {
            Text = "预览窗口置顶显示",
            Location = new Point(20, 46),
            Size = new Size(220, 24),
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Checked = _config.QuickLookAlwaysOnTop
        };
        var widthTitle = MakeLabel("默认宽度", 20, 84, 64, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _widthBox = new NumericUpDown
        {
            Location = new Point(86, 82),
            Size = new Size(76, 26),
            Minimum = 420,
            Maximum = 4000,
            Value = Math.Clamp(_config.QuickLookWidth, 420, 4000),
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        var heightTitle = MakeLabel("默认高度", 190, 84, 64, 26,
            new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _heightBox = new NumericUpDown
        {
            Location = new Point(256, 82),
            Size = new Size(76, 26),
            Minimum = 320,
            Maximum = 4000,
            Value = Math.Clamp(_config.QuickLookHeight, 320, 4000),
            Font = new Font("Microsoft YaHei UI", 8.5f)
        };
        var behaviorHint = MakeLabel(
            "预览窗口可拖动标题栏移动，也可拖动边缘调整大小；关闭时自动记住位置和尺寸。",
            20, 116, 700, 22,
            new Font("Microsoft YaHei UI", 7.8f), ShutdownTheme.Muted, Color.White);
        behaviorCard.Controls.AddRange(new Control[]
        {
            behaviorTitle, _topMostCheck,
            widthTitle, _widthBox, heightTitle, _heightBox, behaviorHint
        });
        generalLayout.Controls.Add(behaviorCard, 0, 1);

        var shortcutCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
            CornerRadius = 14
        };
        var shortcutTitle = MakeLabel("快捷键", 20, 12, 240, 24,
            new Font("Microsoft YaHei UI", 10f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var shortcutInfo = MakeLabel(
            "空格键：在资源管理器中选中文件后打开预览；预览打开时再次按空格关闭。\n"
            + "Esc：关闭预览；← / →（或 ↑ / ↓）：切换同文件夹中的上一个 / 下一个文件。",
            20, 44, 820, 60,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Text, Color.White);
        shortcutCard.Controls.AddRange(new Control[] { shortcutTitle, shortcutInfo });
        generalLayout.Controls.Add(shortcutCard, 0, 2);

        // ---------- 插件 ----------
        var pluginPage = new TabPage("预览插件") { BackColor = Color.White, Padding = new Padding(14) };
        var pluginCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            CornerRadius = 14,
            Padding = new Padding(12)
        };
        var pluginTitle = MakeLabel("启用的预览类型", 12, 10, 400, 26,
            new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var pluginHint = MakeLabel(
            "关闭某个插件后，对应文件会显示“插件已关闭”提示。",
            12, 38, 600, 22,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        var pluginFlow = new FlowLayoutPanel
        {
            Location = new Point(12, 70),
            Size = new Size(900, 360),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            BackColor = Color.White,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = true,
            AutoScroll = true
        };
        AddPluginCheck(pluginFlow, "图片（JPG / PNG / GIF / BMP / TIFF / ICO / WebP / HEIC / AVIF / APNG / SVG / PSD / PSB / XCF / CDR / RAW / AI / EPS）",
            _config.QuickLookPluginImage, value => _config.QuickLookPluginImage = value);
        AddPluginCheck(pluginFlow, "文本 / 代码 / Markdown",
            _config.QuickLookPluginText, value => _config.QuickLookPluginText = value);
        AddPluginCheck(pluginFlow, "音频（MP3 / WAV / FLAC / M4A / OGG 等）",
            _config.QuickLookPluginAudio, value => _config.QuickLookPluginAudio = value);
        AddPluginCheck(pluginFlow, "视频（MP4 / MKV / MOV / WebM / AVI / TS / RMVB / FLV 等）",
            _config.QuickLookPluginVideo, value => _config.QuickLookPluginVideo = value);
        AddPluginCheck(pluginFlow, "PDF / HTML 网页预览",
            _config.QuickLookPluginWeb, value => _config.QuickLookPluginWeb = value);
        AddPluginCheck(pluginFlow, "Office 文档（docx / xlsx / pptx 内容提取）",
            _config.QuickLookPluginOffice, value => _config.QuickLookPluginOffice = value);
        AddPluginCheck(pluginFlow, "压缩包（ZIP / 7Z / RAR / TAR / TGZ 目录列表）",
            _config.QuickLookPluginArchive, value => _config.QuickLookPluginArchive = value);
        AddPluginCheck(pluginFlow, "字体（TTF / OTF / TTC 预览）",
            _config.QuickLookPluginFont, value => _config.QuickLookPluginFont = value);
        AddPluginCheck(pluginFlow, "文件夹内容列表",
            _config.QuickLookPluginFolder, value => _config.QuickLookPluginFolder = value);
        pluginCard.Controls.AddRange(new Control[]
        {
            pluginTitle, pluginHint, pluginFlow
        });
        pluginCard.Resize += (_, _) =>
        {
            pluginFlow.Width = Math.Max(200, pluginCard.ClientSize.Width - 24);
            pluginFlow.Height = Math.Max(120, pluginCard.ClientSize.Height - 82);
        };
        pluginPage.Controls.Add(pluginCard);
        _tabs.TabPages.Add(pluginPage);

        // ---------- 说明 ----------
        var aboutPage = new TabPage("使用说明") { BackColor = Color.White, Padding = new Padding(14) };
        var aboutCard = new CardPanel { Dock = DockStyle.Fill, CornerRadius = 14 };
        var aboutTitle = MakeLabel("QuickLook复刻 · 使用说明", 20, 14, 400, 26,
            new Font("Microsoft YaHei UI", 11f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var aboutText = MakeLabel(
            "· 功能默认开启，点击“QuickLook复刻”菜单只会打开本管理界面。\n"
            + "· 在 Windows 资源管理器中选中文件或文件夹，按空格键即可快速预览。\n"
            + "· 预览窗口保持置顶，可按空格 / Esc 关闭；← / → 切换同文件夹文件。\n\n"
            + "支持预览：图片（JPG/PNG/GIF/BMP/TIFF/ICO/WebP/HEIC/AVIF/APNG/SVG/PSD/PSB/XCF/CDR/RAW/AI/EPS）、\n"
            + "文本/代码/Markdown、音频、视频、PDF、HTML、Office 内容提取、\n"
            + "ZIP / 7Z / RAR / TAR / TGZ 压缩包目录、字体文件与文件夹列表。\n\n"
            + "说明：视频 / PDF / 网页预览依赖 Microsoft Edge WebView2 Runtime\n"
            + "（Windows 10/11 通常已随 Edge 安装）。Office 预览采用内容提取，\n"
            + "不渲染原始版式；3D 模型等特殊插件暂未包含。\n\n"
            + "若空格预览未触发：先确认本页顶部状态为“运行中”，再在资源管理器\n"
            + "中单击选中文件，点击左下角“检测选中项”查看诊断信息。\n"
            + "管理员权限运行时若无法读取资源管理器选中项，会自动短暂复制后恢复剪贴板。\n"
            + "为保持空格预览响应速度，本功能不再写入日志文件。",
            20, 54, 820, 380,
            new Font("Microsoft YaHei UI", 8.8f), ShutdownTheme.Text, Color.White);
        aboutCard.Controls.AddRange(new Control[] { aboutTitle, aboutText });
        aboutPage.Controls.Add(aboutCard);
        _tabs.TabPages.Add(aboutPage);

        var tabsHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 8, 16, 6),
            BackColor = ShutdownTheme.Background
        };
        tabsHost.Controls.Add(_tabs);

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 64,
            BackColor = ShutdownTheme.Background,
            Padding = new Padding(16, 4, 16, 12)
        };
        var footerCard = new CardPanel { Dock = DockStyle.Fill, CornerRadius = 14 };
        _footerStatus = MakeLabel("", 14, 8, 500, 40,
            new Font("Microsoft YaHei UI", 8.2f), ShutdownTheme.Muted, Color.White);
        _footerStatus.TextAlign = ContentAlignment.MiddleLeft;
        var testButton = new PillButton
        {
            Text = "测试预览…",
            Location = new Point(0, 10),
            Size = new Size(120, 32),
            ButtonStyle = PillButtonStyle.Primary
        };
        testButton.Click += (_, _) => TestPreview();
        var diagnosticButton = new PillButton
        {
            Text = "检测选中项",
            Location = new Point(0, 10),
            Size = new Size(120, 32),
            ButtonStyle = PillButtonStyle.Light
        };
        diagnosticButton.Click += (_, _) => DiagnoseSelection();
        footerCard.Controls.AddRange(new Control[] { _footerStatus, testButton, diagnosticButton });
        footerCard.Resize += (_, _) =>
        {
            testButton.Location = new Point(Math.Max(0, footerCard.ClientSize.Width - 134), 10);
            diagnosticButton.Location = new Point(Math.Max(0, testButton.Left - 130), 10);
            _footerStatus.Width = Math.Max(120, diagnosticButton.Left - 28);
        };
        footer.Controls.Add(footerCard);
        Controls.Add(tabsHost);
        Controls.Add(footer);
        Controls.Add(_header);

        _enableToggle.CheckedChanged += (_, _) => OnEnableChanged();
        _topMostCheck.CheckedChanged += (_, _) =>
        {
            if (_updating) return;
            _config.QuickLookAlwaysOnTop = _topMostCheck.Checked;
            _config.Save();
            _service.ApplySettings();
        };
        _widthBox.ValueChanged += (_, _) => SaveWindowSize();
        _heightBox.ValueChanged += (_, _) => SaveWindowSize();
        UpdateState();
    }

    private void AddPluginCheck(Control parent, string text, bool initial,
        Action<bool> setter)
    {
        var check = new CheckBox
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(420, 26),
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Checked = initial,
            Margin = new Padding(0, 4, 16, 4)
        };
        check.CheckedChanged += (_, _) =>
        {
            setter(check.Checked);
            _config.Save();
        };
        parent.Controls.Add(check);
    }

    private void OnEnableChanged()
    {
        if (_updating)
        {
            return;
        }
        _config.QuickLookEnabled = _enableToggle.Checked;
        _config.Save();
        _service.ApplySettings();
        UpdateState();
    }

    private void UpdateState()
    {
        _updating = true;
        _enableToggle.Checked = _config.QuickLookEnabled;
        _topMostCheck.Checked = _config.QuickLookAlwaysOnTop;
        _updating = false;

        if (!_config.QuickLookEnabled)
        {
            _statusLabel.Text = "已停用";
            _statusLabel.ForeColor = ShutdownTheme.Muted;
            _footerStatus.Text = "功能已停用：不会监听空格键。重新开启后立即生效。";
        }
        else if (_service.IsRunning)
        {
            _statusLabel.Text = "运行中";
            _statusLabel.ForeColor = ShutdownTheme.Success;
            _footerStatus.Text = "运行中：在资源管理器中选中文件后按空格键预览；再按空格或 Esc 关闭。";
        }
        else
        {
            _statusLabel.Text = "未运行";
            _statusLabel.ForeColor = ShutdownTheme.Danger;
            _footerStatus.Text = "全局键盘监听未启动："
                + (_service.LastError ?? "可能被安全软件拦截。");
        }
    }

    private void SaveWindowSize()
    {
        if (_updating)
        {
            return;
        }
        _config.QuickLookWidth = (int)_widthBox.Value;
        _config.QuickLookHeight = (int)_heightBox.Value;
        _config.Save();
        _footerStatus.Text = "预览窗口默认尺寸已保存（新打开的预览窗口生效）。";
    }

    private void TestPreview()
    {
        if (!_config.QuickLookEnabled)
        {
            MessageBox.Show(FindForm(), "请先启用 QuickLook复刻。", "QuickLook复刻",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new OpenFileDialog
        {
            Title = "选择要预览的文件",
            Filter = "所有文件|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            _service.ShowPreviewForPath(dialog.FileName);
        }
    }

    private void DiagnoseSelection()
    {
        string? path = _service.GetExplorerSelectionForDiagnostics(out string detail);
        MessageBox.Show(FindForm(),
            detail + (path == null
                ? "\r\n\r\n提示：如果这里读取不到选中项，空格预览也无法触发。\r\n"
                    + "请确认使用 Windows 资源管理器窗口、单击选中文件、且焦点不在地址栏/搜索框。"
                : "\r\n\r\n空格预览目标读取正常。若按空格仍无反应，请检查设置页顶部的“运行中”状态。"),
            "QuickLook复刻 · 选中项检测",
            MessageBoxButtons.OK, path == null ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

    private Rectangle CloseRect
    {
        get
        {
            float s = _header.Height / 104f;
            return new Rectangle((int)(_header.Width - 52f * s), (int)(14f * s),
                (int)(34f * s), (int)(34f * s));
        }
    }

    private Rectangle MaximizeRect
    {
        get
        {
            float s = _header.Height / 104f;
            return new Rectangle((int)(_header.Width - 96f * s), (int)(14f * s),
                (int)(34f * s), (int)(34f * s));
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
            Color.FromArgb(30, 41, 59), Color.FromArgb(51, 65, 85),
            LinearGradientMode.Horizontal))
        {
            g.FillRectangle(brush, _header.ClientRectangle);
        }
        using (var glow = new SolidBrush(Color.FromArgb(20, 255, 255, 255)))
        {
            g.FillEllipse(glow, _header.Width - 300, -130, 380, 380);
        }
        TextRenderer.DrawText(g, "QuickLook复刻", _headerTitleFont,
            new Rectangle((int)(28f * s), (int)(12f * s),
                (int)(_header.Width - 220f * s), (int)(44f * s)),
            Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "空格键快速预览 · 图片 / 音频 / 视频 / PDF / 文档", _headerSubFont,
            new Rectangle((int)(30f * s), (int)(54f * s),
                (int)(_header.Width - 240f * s), (int)(26f * s)),
            Color.FromArgb(226, 232, 240), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        var close = CloseRect;
        if (_closeHover)
        {
            using var hover = new SolidBrush(Color.FromArgb(42, 255, 255, 255));
            g.FillEllipse(hover, close);
        }
        using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 2f * s))
        {
            int inset = (int)(12f * s);
            g.DrawLine(pen, close.Left + inset, close.Top + inset,
                close.Right - inset, close.Bottom - inset);
            g.DrawLine(pen, close.Right - inset, close.Top + inset,
                close.Left + inset, close.Bottom - inset);
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
            : 18f * (Height / 720f);
        using var path = ShutdownTheme.RoundRect(new RectangleF(0, 0, Width, Height), radius);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
        _header.Invalidate();
    }

    private static Label MakeLabel(string text, int x, int y, int width, int height,
        Font font, Color color, Color backColor)
    {
        return new Label
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            Font = font,
            ForeColor = color,
            BackColor = backColor,
            AutoSize = false
        };
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
