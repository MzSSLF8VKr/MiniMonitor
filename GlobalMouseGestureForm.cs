using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// 全局鼠标手势管理窗口。默认不打开；从右键菜单进入。
/// 仅一个手势：按住右键向下、再向右，松开后关闭当前窗口（浏览器关闭当前标签）。
/// </summary>
public sealed class GlobalMouseGestureForm : Form
{
    private readonly GlobalMouseGestureService _service;
    private readonly AppConfig _config;
    private readonly GestureLogStore _log;
    private readonly Panel _header;
    private readonly CardPanel _enableCard;
    private readonly ToggleSwitch _enableToggle;
    private readonly Label _enableStatus;
    private readonly Label _enableHint;
    private readonly Label _lastTriggerLabel;
    private readonly Label _errorLabel;
    private readonly Label _logTitle;
    private readonly TextBox _logBox;
    private readonly PillButton _copyLogButton;
    private readonly System.Windows.Forms.Timer _statusTimer;
    private readonly System.Windows.Forms.Timer _logTimer;
    private readonly System.Windows.Forms.Timer _copyResetTimer;
    private readonly Font _headerTitleFont;
    private readonly Font _headerSubFont;
    private bool _closeHover;
    private bool _suppressToggle;

    public GlobalMouseGestureForm(GlobalMouseGestureService service, AppConfig config)
    {
        _service = service;
        _config = config;
        _log = service.Log;

        Text = "全局鼠标手势 - MiniMonitor";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(900, 720);
        BackColor = ShutdownTheme.Background;
        DoubleBuffered = true;
        KeyPreview = true;
        ShowInTaskbar = true;

        _headerTitleFont = new Font("Microsoft YaHei UI", 19f, FontStyle.Bold);
        _headerSubFont = new Font("Microsoft YaHei UI", 9.5f);

        _header = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(900, 104),
            BackColor = Color.FromArgb(67, 56, 202)
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
                Close();
            }
        };
        NativeWindowDrag.Attach(this, _header, p => CloseRect.Contains(p));
        Controls.Add(_header);

        // ---------- 启用卡片 ----------
        _enableCard = new CardPanel
        {
            Location = new Point(24, 116),
            Size = new Size(852, 74)
        };
        _enableToggle = new ToggleSwitch
        {
            Location = new Point(20, 22),
            Size = new Size(56, 30),
            OnColor = Color.FromArgb(79, 70, 229),
            Checked = _service.IsRunning
        };
        var enableTitle = MakeLabel("启用全局鼠标手势", 90, 12, 430, 26,
            new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _enableHint = MakeLabel("", 92, 40, 640, 22,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Muted, Color.White);
        _enableStatus = new Label
        {
            Text = "",
            Location = new Point(_enableCard.Width - 112, 22),
            Size = new Size(92, 28),
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoSize = false
        };
        _enableCard.Controls.AddRange(new Control[] { _enableToggle, enableTitle, _enableHint, _enableStatus });
        Controls.Add(_enableCard);

        // ---------- 手势说明卡片 ----------
        var gestureCard = new CardPanel
        {
            Location = new Point(24, 202),
            Size = new Size(852, 280)
        };
        var preview = new GesturePreview
        {
            Location = new Point(18, 18),
            Size = new Size(270, 244)
        };
        var gestureTitle = MakeLabel("按住右键 → 向下 → 向右 → 松开", 306, 12, 528, 28,
            new Font("Microsoft YaHei UI", 11f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var gestureSubtitle = MakeLabel("仅此一个手势，全局生效", 306, 40, 528, 20,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Muted, Color.White);

        var actionTitle = MakeLabel("触发动作", 306, 66, 528, 20,
            new Font("Microsoft YaHei UI", 9f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var browserAction = MakeLabel("· 浏览器：关闭当前标签页（Ctrl+W）", 306, 90, 528, 20,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Text, Color.White);
        var appAction = MakeLabel("· 资源管理器 / 其他软件：关闭当前窗口", 306, 114, 528, 20,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Text, Color.White);
        var desktopAction = MakeLabel("· 桌面 / 任务栏：没有可关闭窗口，不执行操作", 306, 138, 528, 20,
            new Font("Microsoft YaHei UI", 8.5f), ShutdownTheme.Muted, Color.White);

        var noteTitle = MakeLabel("说明", 306, 162, 528, 18,
            new Font("Microsoft YaHei UI", 9f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        var note1 = MakeLabel("右键按下时不会弹出菜单；未形成手势会自动补发正常右键单击。",
            306, 184, 528, 18,
            new Font("Microsoft YaHei UI", 7.8f), ShutdownTheme.Muted, Color.White);
        var note2 = MakeLabel("VMware 窗口内自动临时失效，不拦截右键；手势作用于鼠标所在窗口。",
            306, 204, 528, 18,
            new Font("Microsoft YaHei UI", 7.8f), ShutdownTheme.Muted, Color.White);
        _lastTriggerLabel = MakeLabel("最近触发：尚未触发", 306, 226, 528, 18,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        _errorLabel = MakeLabel("", 306, 248, 528, 18,
            new Font("Microsoft YaHei UI", 7.5f), ShutdownTheme.Danger, Color.White);

        gestureCard.Controls.AddRange(new Control[]
        {
            preview, gestureTitle, gestureSubtitle, actionTitle, browserAction,
            appAction, desktopAction, noteTitle, note1, note2,
            _lastTriggerLabel, _errorLabel
        });
        Controls.Add(gestureCard);

        // ---------- 详细日志 ----------
        var logCard = new CardPanel
        {
            Location = new Point(24, 494),
            Size = new Size(852, 180)
        };
        _logTitle = MakeLabel("详细日志（已关闭）", 18, 8, 680, 24,
            new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold), ShutdownTheme.Text, Color.White);
        _copyLogButton = new PillButton
        {
            Text = "复制日志",
            Location = new Point(714, 8),
            Size = new Size(120, 30),
            ButtonStyle = PillButtonStyle.Light,
            Enabled = false
        };
        _logBox = new TextBox
        {
            Location = new Point(18, 44),
            Size = new Size(816, 120),
            Multiline = true,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 8.5f),
            BackColor = Color.White,
            ForeColor = ShutdownTheme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            HideSelection = false
        };
        logCard.Controls.AddRange(new Control[] { _logTitle, _copyLogButton, _logBox });
        Controls.Add(logCard);

        var footer = MakeLabel("提示：关闭本窗口不会停止已启用的手势服务；需要停止时重新打开本窗口关闭开关。",
            24, 686, 852, 20,
            new Font("Microsoft YaHei UI", 8f), ShutdownTheme.Muted, Color.White);
        Controls.Add(footer);

        _enableToggle.CheckedChanged += (_, _) => ApplyToggle();
        _copyLogButton.Click += (_, _) => CopyLog();
        _enableCard.BorderColor = _service.IsRunning
            ? Color.FromArgb(79, 70, 229)
            : ShutdownTheme.CardBorder;

        _statusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();

        _logTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _logTimer.Tick += (_, _) => RefreshLog();
        // 日志能力已移除：不再启动刷新计时器，避免任何额外开销。

        _copyResetTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _copyResetTimer.Tick += (_, _) =>
        {
            _copyResetTimer.Stop();
            _copyLogButton.Text = "复制日志";
        };

        UpdateUi();
        RefreshLog();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };
    }

    private void ApplyToggle()
    {
        if (_suppressToggle)
        {
            return;
        }

        bool enable = _enableToggle.Checked;
        UseWaitCursor = true;
        try
        {
            if (enable)
            {
                if (!_service.Start())
                {
                    string message = _service.LastError ?? "无法启用全局鼠标手势。";
                    _suppressToggle = true;
                    _enableToggle.Checked = false;
                    _suppressToggle = false;
                    _config.MouseGestureEnabled = false;
                    _config.Save();
                    UpdateUi();
                    MessageBox.Show(this, message, "无法启用全局鼠标手势",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                _config.MouseGestureEnabled = true;
                _config.Save();
            }
            else
            {
                _service.Stop();
                _config.MouseGestureEnabled = false;
                _config.Save();
            }
        }
        finally
        {
            UseWaitCursor = false;
        }
        UpdateUi();
    }

    private void UpdateUi()
    {
        bool running = _service.IsRunning;
        if (_enableToggle.Checked != running)
        {
            _suppressToggle = true;
            _enableToggle.Checked = running;
            _suppressToggle = false;
        }
        _enableStatus.Text = running ? "运行中" : "未启用";
        _enableStatus.ForeColor = running
            ? Color.FromArgb(79, 70, 229)
            : ShutdownTheme.Muted;
        _enableHint.Text = running
            ? "已开启；在目标窗口上按住右键向下，再向右滑动，松开后关闭该窗口（VMware 窗口内自动失效）"
            : "已关闭；点击开关可重新开启全局手势";
        _enableCard.BorderColor = running
            ? Color.FromArgb(79, 70, 229)
            : ShutdownTheme.CardBorder;
        _enableCard.Invalidate();
        _errorLabel.Text = running ? "" : (_service.LastError ?? "");
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        DateTime? last = _service.LastTriggerTime;
        if (last is DateTime time)
        {
            _lastTriggerLabel.Text = "最近触发："
                + time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                + " · " + _service.LastAction;
        }
        else
        {
            _lastTriggerLabel.Text = "最近触发：尚未触发";
        }
    }

    private void RefreshLog()
    {
        _logTitle.Text = "详细日志（已关闭：为降低鼠标延迟不再记录）";
        _logBox.Text = "为保证鼠标手势的响应速度，日志记录能力已移除，"
            + "不再写入 gesture.log。" + Environment.NewLine
            + "手势识别与窗口关闭功能不受影响。";
        _logBox.SelectionStart = 0;
    }

    private void CopyLog()
    {
        try
        {
            string text = _log.SnapshotText();
            if (string.IsNullOrEmpty(text))
            {
                MessageBox.Show(this, "当前没有日志可复制。", "全局鼠标手势日志",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Clipboard.SetText(text);
            _copyLogButton.Text = "已复制";
            _copyResetTimer.Stop();
            _copyResetTimer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "复制日志失败：" + ex.Message, "全局鼠标手势日志",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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

    private void PaintHeader(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        float s = _header.Height / 104f;

        using (var brush = new LinearGradientBrush(_header.ClientRectangle,
            Color.FromArgb(67, 56, 202), Color.FromArgb(37, 99, 235),
            LinearGradientMode.Horizontal))
        {
            g.FillRectangle(brush, _header.ClientRectangle);
        }
        using (var glow = new SolidBrush(Color.FromArgb(22, 255, 255, 255)))
        {
            g.FillEllipse(glow, _header.Width - 230, -100, 310, 310);
            g.FillEllipse(glow, _header.Width - 96, 34, 130, 130);
        }

        TextRenderer.DrawText(g, "全局鼠标手势", _headerTitleFont,
            new Rectangle((int)(28f * s), (int)(12f * s), (int)(_header.Width - 140f * s), (int)(44f * s)),
            Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "右键向下 → 向右 · 关闭当前窗口 · 诊断日志", _headerSubFont,
            new Rectangle((int)(30f * s), (int)(54f * s), (int)(_header.Width - 160f * s), (int)(26f * s)),
            Color.FromArgb(224, 231, 255), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

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
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _statusTimer.Stop();
        _logTimer.Stop();
        _copyResetTimer.Stop();
        base.OnFormClosing(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        float scale = Height / 720f;
        using var path = ShutdownTheme.RoundRect(new RectangleF(0, 0, Width, Height), 18f * scale);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _statusTimer.Stop();
            _statusTimer.Dispose();
            _logTimer.Stop();
            _logTimer.Dispose();
            _copyResetTimer.Stop();
            _copyResetTimer.Dispose();
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

/// <summary>手势示意图：鼠标右键、向下再向右的 L 形路径和关闭标记。</summary>
internal sealed class GesturePreview : Control
{
    private readonly Font _smallFont = new Font("Microsoft YaHei UI", 8f);
    private readonly Font _boldFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);

    public GesturePreview()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var panel = new RectangleF(0, 0, Width, Height);
        ShutdownTheme.FillRounded(g, panel, 14f, Color.FromArgb(248, 250, 252));
        ShutdownTheme.DrawRoundedBorder(g, panel, 14f, ShutdownTheme.CardBorder);

        Color accent = Color.FromArgb(79, 70, 229);
        var mouse = new RectangleF(68, 42, 82, 126);
        using (var bodyPath = ShutdownTheme.RoundRect(mouse, 38f))
        {
            using var bodyBrush = new SolidBrush(Color.White);
            g.FillPath(bodyBrush, bodyPath);
            using var bodyPen = new Pen(ShutdownTheme.CardBorder, 1.3f);
            g.DrawPath(bodyPen, bodyPath);
        }

        float centerX = mouse.Left + mouse.Width / 2f;
        using (var divider = new Pen(Color.FromArgb(206, 213, 224), 1.1f))
        {
            g.DrawLine(divider, centerX, mouse.Top, centerX, mouse.Top + 42);
            g.DrawLine(divider, mouse.Left + 4, mouse.Top + 42, mouse.Right - 4, mouse.Top + 42);
        }
        var rightButton = new RectangleF(centerX + 4, mouse.Top + 5, mouse.Width / 2f - 9, 33);
        ShutdownTheme.FillRounded(g, rightButton, 12f, Color.FromArgb(238, 242, 255));
        ShutdownTheme.DrawRoundedBorder(g, rightButton, 12f, Color.FromArgb(199, 210, 254));
        TextRenderer.DrawText(g, "右键", _smallFont,
            new Rectangle((int)rightButton.X, (int)rightButton.Y, (int)rightButton.Width, (int)rightButton.Height),
            accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        float startX = centerX + 20;
        float startY = mouse.Top + 52;
        float bottomY = mouse.Bottom + 10;
        float endX = Width - 44;
        PointF[] pathPoints =
        {
            new(startX, startY),
            new(startX, bottomY),
            new(endX, bottomY)
        };
        using (var path = new GraphicsPath())
        {
            path.AddLines(pathPoints);
            using var pen = new Pen(accent, 4.2f);
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            g.DrawPath(pen, path);
        }

        // 箭头
        using (var arrowPen = new Pen(accent, 4f))
        {
            g.DrawLine(arrowPen, endX - 12, bottomY - 9, endX, bottomY);
            g.DrawLine(arrowPen, endX - 12, bottomY + 9, endX, bottomY);
        }
        // 关闭标记
        var closeBadge = new RectangleF(endX + 4, bottomY - 14, 28, 28);
        ShutdownTheme.FillRounded(g, closeBadge, 14f, Color.FromArgb(254, 242, 242));
        ShutdownTheme.DrawRoundedBorder(g, closeBadge, 14f, Color.FromArgb(252, 165, 165));
        using (var closePen = new Pen(ShutdownTheme.Danger, 2.2f))
        {
            g.DrawLine(closePen, closeBadge.Left + 8, closeBadge.Top + 8,
                closeBadge.Right - 8, closeBadge.Bottom - 8);
            g.DrawLine(closePen, closeBadge.Right - 8, closeBadge.Top + 8,
                closeBadge.Left + 8, closeBadge.Bottom - 8);
        }

        TextRenderer.DrawText(g, "向下", _boldFont,
            new Rectangle((int)startX + 8, (int)(startY + 38), 60, 20), accent,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "向右", _boldFont,
            new Rectangle((int)startX + 22, (int)bottomY + 8, 60, 20), accent,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "按住右键滑动", _smallFont,
            new Rectangle(48, 220, 140, 20), ShutdownTheme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _smallFont.Dispose();
            _boldFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
