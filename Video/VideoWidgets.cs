using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MiniMonitor.Video;

/// <summary>深色播放进度条，支持点击与拖动定位。</summary>
internal sealed class VideoSeekBar : Control
{
    private double _duration;
    private double _position;
    private bool _dragging;

    public event EventHandler<double>? SeekRequested;

    public VideoSeekBar()
    {
        DoubleBuffered = true;
        Height = 22;
        BackColor = VideoTheme.Panel;
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    public double Duration
    {
        get => _duration;
        set
        {
            _duration = Math.Max(0, value);
            Invalidate();
        }
    }

    public double Position
    {
        get => _position;
        set
        {
            _position = Math.Max(0, Math.Min(_duration, value));
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int pad = 8;
        int y = Height / 2;
        var backRect = new Rectangle(pad, y - 3, Math.Max(1, Width - pad * 2), 6);
        using (var back = new SolidBrush(VideoTheme.PanelLight))
        {
            VideoTheme.DrawRoundedRect(g, back, backRect, 3);
        }
        double fraction = _duration > 0 ? Math.Clamp(_position / _duration, 0, 1) : 0;
        int fillWidth = (int)Math.Round(backRect.Width * fraction);
        if (fillWidth > 0)
        {
            using var fill = new SolidBrush(VideoTheme.Accent);
            VideoTheme.DrawRoundedRect(g, fill,
                new Rectangle(backRect.X, backRect.Y, fillWidth, backRect.Height), 3);
        }
        int knobX = backRect.X + fillWidth;
        using (var knob = new SolidBrush(VideoTheme.Text))
        {
            g.FillEllipse(knob, knobX - 6, y - 6, 12, 12);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }
        _dragging = true;
        Capture = true;
        SeekTo(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            SeekTo(e.X);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
    }

    private void SeekTo(int x)
    {
        int pad = 8;
        int width = Math.Max(1, Width - pad * 2);
        double fraction = Math.Clamp((x - pad) / (double)width, 0, 1);
        double value = fraction * _duration;
        Position = value;
        SeekRequested?.Invoke(this, value);
    }
}

/// <summary>导出 / 合并等长任务的进度窗口（非模态，关闭前由调用方控制）。</summary>
internal sealed class VideoProgressDialog : Form
{
    private readonly Label _title;
    private readonly Label _status;
    private readonly ProgressBar _progress;
    private readonly Button _cancel;
    private readonly CancellationTokenSource _cts = new();
    private readonly TextBox _details;
    private bool _finished;

    public VideoProgressDialog(string title, string? subtitle = null)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(560, 220);
        BackColor = VideoTheme.Panel;
        ForeColor = VideoTheme.Text;
        Font = VideoTheme.Ui9;

        _title = new Label
        {
            Text = title,
            Location = new Point(18, 14),
            Size = new Size(520, 26),
            Font = VideoTheme.UiTitle,
            ForeColor = VideoTheme.Text
        };
        _status = new Label
        {
            Text = subtitle ?? "准备中…",
            Location = new Point(18, 48),
            Size = new Size(520, 42),
            ForeColor = VideoTheme.Muted
        };
        _progress = new ProgressBar
        {
            Location = new Point(18, 98),
            Size = new Size(524, 20),
            Minimum = 0,
            Maximum = 1000
        };
        _details = new TextBox
        {
            Location = new Point(18, 126),
            Size = new Size(524, 52),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = VideoTheme.PanelAlt,
            ForeColor = VideoTheme.Muted,
            BorderStyle = BorderStyle.FixedSingle,
            Font = VideoTheme.UiSmall
        };
        _cancel = VideoTheme.Button("取消", 88);
        _cancel.Location = new Point(454, 182);
        _cancel.Click += (_, _) =>
        {
            if (_finished)
            {
                Close();
                return;
            }
            _cancel.Enabled = false;
            _status.Text = "正在取消…";
            _cts.Cancel();
        };
        Controls.AddRange(new Control[] { _title, _status, _progress, _details, _cancel });
        FormClosing += (_, e) =>
        {
            if (!_finished)
            {
                e.Cancel = true;
                _cts.Cancel();
                _cancel.Enabled = false;
                _status.Text = "正在取消…";
            }
        };
    }

    public CancellationToken Token => _cts.Token;

    public void UpdateProgress(double fraction, string status)
    {
        if (IsDisposed)
        {
            return;
        }
        _progress.Value = Math.Clamp((int)Math.Round(fraction * 1000), 0, 1000);
        _status.Text = status;
    }

    public void SetDetails(string text)
    {
        if (!IsDisposed)
        {
            _details.Text = text;
        }
    }

    public void Finish(bool success, string message)
    {
        _finished = true;
        if (IsDisposed)
        {
            return;
        }
        _status.Text = message;
        _progress.Value = success ? 1000 : _progress.Value;
        _cancel.Text = "关闭";
        _cancel.Enabled = true;
    }

    /// <summary>主窗口关闭时使用：取消任务并允许进度窗口立即关闭。</summary>
    public void CancelAndClose()
    {
        _finished = true;
        try
        {
            _cts.Cancel();
        }
        catch
        {
            // ignore
        }
        Close();
    }
}
