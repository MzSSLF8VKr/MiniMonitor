using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// 定时关机倒计时窗口：置顶显示剩余时间与进度环，提供“取消定时关机”按钮。
/// 倒计时结束（或点击取消）前不会调用系统关机；到点后执行强制关机 shutdown /s /f /t 0。
/// </summary>
public sealed class ShutdownCountdownForm : Form
{
    private readonly DateTime _deadline;
    private readonly double _totalSeconds;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly PillButton _cancelButton;
    private readonly Font _titleFont;
    private readonly Font _subFont;
    private readonly Font _countFont;
    private readonly Font _countFontSmall;
    private readonly Font _targetFont;
    private readonly Font _hintFont;
    private bool _cancelled;
    private bool _shutdownStarted;

    public ShutdownCountdownForm(DateTime deadline)
    {
        _deadline = deadline;
        _totalSeconds = Math.Max(1d, (deadline - DateTime.Now).TotalSeconds);

        Text = "定时关机倒计时 - MiniMonitor";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(430, 350);
        BackColor = ShutdownTheme.DarkBg;
        DoubleBuffered = true;
        KeyPreview = true;
        ShowInTaskbar = true;

        _titleFont = new Font("Microsoft YaHei UI", 15.5f, FontStyle.Bold);
        _subFont = new Font("Microsoft YaHei UI", 8.5f);
        _countFont = new Font("Microsoft YaHei UI", 21f, FontStyle.Bold);
        _countFontSmall = new Font("Microsoft YaHei UI", 14.5f, FontStyle.Bold);
        _targetFont = new Font("Microsoft YaHei UI", 8.5f);
        _hintFont = new Font("Microsoft YaHei UI", 7.5f);

        _cancelButton = new PillButton
        {
            Text = "取消定时关机",
            ButtonStyle = PillButtonStyle.Danger,
            Location = new Point((ClientSize.Width - 190) / 2, 276),
            Size = new Size(190, 40),
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
            BackColor = ShutdownTheme.DarkBg
        };
        _cancelButton.Click += (_, _) => CancelAndClose();
        Controls.Add(_cancelButton);

        _timer = new System.Windows.Forms.Timer { Interval = 250 };
        _timer.Tick += (_, _) =>
        {
            if (_shutdownStarted)
            {
                return;
            }
            if (DateTime.Now >= _deadline)
            {
                ExecuteShutdown();
            }
            else
            {
                Invalidate();
            }
        };

        NativeWindowDrag.Attach(this, this);
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };
    }

    /// <summary>供外部（重新安排关机 / 程序退出）无提示取消并关闭。</summary>
    public void CancelAndClose()
    {
        _cancelled = true;
        _timer.Stop();
        Close();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        float scale = Height / 350f;

        using (var bg = new SolidBrush(ShutdownTheme.DarkBg))
        {
            g.FillRectangle(bg, ClientRectangle);
        }

        // 背景光晕 / 装饰圆
        float cx = Width / 2f;
        float cy = 146f * scale;
        using (var glowOuter = new SolidBrush(Color.FromArgb(14, 37, 99, 235)))
        {
            g.FillEllipse(glowOuter, cx - 190f * scale, cy - 170f * scale, 380f * scale, 380f * scale);
        }
        using (var glowInner = new SolidBrush(Color.FromArgb(22, 59, 130, 246)))
        {
            g.FillEllipse(glowInner, cx - 122f * scale, cy - 122f * scale, 244f * scale, 244f * scale);
        }
        using (var cornerGlow = new SolidBrush(Color.FromArgb(12, 96, 165, 250)))
        {
            g.FillEllipse(cornerGlow, Width - 150f * scale, -90f * scale, 250f * scale, 250f * scale);
        }

        ShutdownTheme.DrawRoundedBorder(g, new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f),
            18f * scale, Color.FromArgb(45, 55, 72));

        // 标题 / 副标题 / 强制关机标识
        TextRenderer.DrawText(g, "定时关机倒计时", _titleFont,
            new Rectangle((int)(26f * scale), (int)(16f * scale), (int)(Width - 150f * scale), (int)(36f * scale)),
            Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "到点后将执行强制关机", _subFont,
            new Rectangle((int)(27f * scale), (int)(47f * scale), (int)(Width - 150f * scale), (int)(24f * scale)),
            ShutdownTheme.DarkMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        var badge = new RectangleF(Width - 126f * scale, 20f * scale, 100f * scale, 28f * scale);
        ShutdownTheme.FillRounded(g, badge, 14f * scale, Color.FromArgb(38, 220, 38, 38));
        ShutdownTheme.DrawRoundedBorder(g, badge, 14f * scale, Color.FromArgb(95, 248, 113, 113));
        TextRenderer.DrawText(g, "强制关机", _hintFont, Rectangle.Round(badge),
            Color.FromArgb(254, 202, 202),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        // 倒计时进度环
        var remaining = _deadline - DateTime.Now;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }
        float progress = _totalSeconds > 0
            ? (float)Math.Clamp(remaining.TotalSeconds / _totalSeconds, 0d, 1d)
            : 0f;
        float radius = 61f * scale;
        var ringRect = new RectangleF(cx - radius, cy - radius, radius * 2f, radius * 2f);
        using (var trackPen = new Pen(ShutdownTheme.DarkTrack, 9f * scale))
        {
            g.DrawEllipse(trackPen, ringRect);
        }
        if (progress > 0f)
        {
            float sweep = Math.Min(progress * 360f, 359.9f);
            using (var glowPen = new Pen(Color.FromArgb(55, 59, 130, 246), 17f * scale))
            {
                glowPen.StartCap = LineCap.Round;
                glowPen.EndCap = LineCap.Round;
                g.DrawArc(glowPen, ringRect, -90f, sweep);
            }
            using (var progressPen = new Pen(ShutdownTheme.DarkAccent, 9f * scale))
            {
                progressPen.StartCap = LineCap.Round;
                progressPen.EndCap = LineCap.Round;
                g.DrawArc(progressPen, ringRect, -90f, sweep);
            }
        }

        // 环中心的电源符号
        using (var powerPen = new Pen(ShutdownTheme.DarkAccentLight, 2.6f * scale))
        {
            powerPen.StartCap = LineCap.Round;
            powerPen.EndCap = LineCap.Round;
            float powerRadius = 13f * scale;
            var powerRect = new RectangleF(cx - powerRadius, cy - powerRadius, powerRadius * 2f, powerRadius * 2f);
            g.DrawArc(powerPen, powerRect, -58f, 296f);
            g.DrawLine(powerPen, cx, cy - 24f * scale, cx, cy - 4f * scale);
        }

        // 大号倒计时数字
        bool hasDays = remaining.TotalDays >= 1;
        TextRenderer.DrawText(g, FormatRemaining(remaining),
            hasDays ? _countFontSmall : _countFont,
            new Rectangle(0, (int)(214f * scale), Width, (int)(40f * scale)),
            Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        // 目标时间
        string targetText = "预计 " + _deadline.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            + " 执行强制关机";
        TextRenderer.DrawText(g, targetText, _targetFont,
            new Rectangle(0, (int)(253f * scale), Width, (int)(22f * scale)),
            ShutdownTheme.DarkMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        // 底部提示
        TextRenderer.DrawText(g, _shutdownStarted ? "正在执行强制关机…" : "倒计时结束前均可取消",
            _hintFont,
            new Rectangle(0, (int)(322f * scale), Width, (int)(20f * scale)),
            ShutdownTheme.DarkMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    private static string FormatRemaining(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }
        int days = (int)span.TotalDays;
        if (days >= 1)
        {
            return days + " 天 "
                + span.Hours.ToString("00", CultureInfo.InvariantCulture) + ":"
                + span.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":"
                + span.Seconds.ToString("00", CultureInfo.InvariantCulture);
        }
        return span.Hours.ToString("00", CultureInfo.InvariantCulture) + ":"
            + span.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":"
            + span.Seconds.ToString("00", CultureInfo.InvariantCulture);
    }

    private void ExecuteShutdown()
    {
        if (_shutdownStarted)
        {
            return;
        }
        _shutdownStarted = true;
        _timer.Stop();
        _cancelButton.Enabled = false;
        _cancelButton.Text = "正在强制关机…";
        Invalidate();
        Update();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "shutdown.exe"),
                Arguments = "/s /f /t 0",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var process = Process.Start(psi);
            if (process == null)
            {
                throw new InvalidOperationException("无法启动 shutdown.exe");
            }
            // 若命令立即返回非 0，说明系统拒绝执行关机（例如权限/策略限制）
            if (process.WaitForExit(2000) && process.ExitCode != 0)
            {
                throw new InvalidOperationException("shutdown.exe 返回错误代码 " + process.ExitCode);
            }
        }
        catch (Exception ex)
        {
            _shutdownStarted = false;
            _cancelButton.Enabled = true;
            _cancelButton.Text = "取消定时关机";
            MessageBox.Show(this, "强制关机失败：" + ex.Message, "关机失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            CancelAndClose();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_cancelled && !_shutdownStarted && e.CloseReason == CloseReason.UserClosing)
        {
            var result = MessageBox.Show(this,
                "正在倒计时强制关机，确定取消定时关机吗？",
                "取消定时关机",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _cancelled = true;
        }
        _timer.Stop();
        base.OnFormClosing(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        float scale = Height / 350f;
        using var path = ShutdownTheme.RoundRect(new RectangleF(0, 0, Width, Height), 18f * scale);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer?.Stop();
            _timer?.Dispose();
            _titleFont.Dispose();
            _subFont.Dispose();
            _countFont.Dispose();
            _countFontSmall.Dispose();
            _targetFont.Dispose();
            _hintFont.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return cp;
        }
    }
}
