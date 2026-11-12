using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MiniMonitor.XFtp;

/// <summary>XFtp 底部传输队列：显示每个任务的方向、进度、速度与状态。</summary>
internal sealed class XFtpTransferPanel : UserControl
{
    private readonly FlowLayoutPanel _rows;
    private readonly Label _empty;
    private readonly Label _summary;
    private readonly Dictionary<XFtpTransferTask, XFtpTransferRow> _rowsByTask = new();

    public event Action? CancelAllRequested;

    public XFtpTransferPanel()
    {
        BackColor = XFtpTheme.Panel;
        Font = XFtpTheme.Ui88;

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = XFtpTheme.PanelAlt
        };
        header.Paint += (_, e) =>
        {
            using var background = new SolidBrush(XFtpTheme.PanelAlt);
            e.Graphics.FillRectangle(background, header.ClientRectangle);
            using var accent = new SolidBrush(XFtpTheme.Accent);
            e.Graphics.FillRectangle(accent, 10, 10, 3, 16);
            TextRenderer.DrawText(e.Graphics, "传输队列", XFtpTheme.UiBold,
                new Rectangle(20, 0, 100, header.Height), XFtpTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPadding);
            using var pen = new Pen(XFtpTheme.BorderLight);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        var clear = XFtpTheme.FlatButton("清空已完成", 92, 24);
        clear.Dock = DockStyle.Right;
        clear.Margin = new Padding(4, 6, 8, 6);
        clear.Click += (_, _) => ClearFinished();
        var cancel = XFtpTheme.FlatButton("取消全部", 80, 24);
        cancel.Dock = DockStyle.Right;
        cancel.Margin = new Padding(4, 6, 4, 6);
        cancel.Click += (_, _) => CancelAllRequested?.Invoke();
        _summary = new Label
        {
            Dock = DockStyle.Right,
            Width = 170,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = XFtpTheme.Muted,
            BackColor = Color.Transparent,
            Font = XFtpTheme.Ui85,
            Padding = new Padding(0, 0, 6, 0)
        };
        header.Controls.Add(clear);
        header.Controls.Add(cancel);
        header.Controls.Add(_summary);

        _empty = new Label
        {
            Text = "暂无传输任务。上传 / 下载文件后会在这里显示进度。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = XFtpTheme.Muted,
            BackColor = XFtpTheme.Panel
        };
        _rows = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = XFtpTheme.Panel,
            Padding = new Padding(6, 4, 6, 4),
            Visible = false
        };
        _rows.Resize += (_, _) => ResizeRows();
        var body = new Panel { Dock = DockStyle.Fill, BackColor = XFtpTheme.Panel };
        body.Controls.Add(_empty);
        body.Controls.Add(_rows);
        Controls.Add(body);
        Controls.Add(header);
        Controls.SetChildIndex(body, 0);
        Controls.SetChildIndex(header, 1);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        XFtpTheme.ApplyNative(this);
    }

    public void AddTask(XFtpTransferTask task)
    {
        RunOnUi(() =>
        {
            if (_rowsByTask.ContainsKey(task))
            {
                return;
            }
            var row = new XFtpTransferRow(task);
            _rowsByTask[task] = row;
            _rows.Controls.Add(row);
            UpdateState();
        });
    }

    public void UpdateTask(XFtpTransferTask task)
    {
        RunOnUi(() =>
        {
            if (_rowsByTask.TryGetValue(task, out var row))
            {
                row.RefreshState();
                UpdateState();
            }
        });
    }

    public void ClearFinished()
    {
        RunOnUi(() =>
        {
            foreach (var pair in _rowsByTask.ToList())
            {
                if (!pair.Key.IsFinished)
                {
                    continue;
                }
                _rows.Controls.Remove(pair.Value);
                pair.Value.Dispose();
                _rowsByTask.Remove(pair.Key);
            }
            UpdateState();
        });
    }

    private void UpdateState()
    {
        int running = _rowsByTask.Keys.Count(task =>
            task.State is XFtpTaskState.Waiting or XFtpTaskState.Running);
        int success = _rowsByTask.Keys.Count(task => task.State == XFtpTaskState.Success);
        int failed = _rowsByTask.Keys.Count(task => task.State == XFtpTaskState.Failed);
        _summary.Text = "进行中 " + running + " · 成功 " + success + " · 失败 " + failed;
        bool has = _rowsByTask.Count > 0;
        _rows.Visible = has;
        _empty.Visible = !has;
        ResizeRows();
    }

    private void ResizeRows()
    {
        int width = Math.Max(260, _rows.ClientSize.Width - 22);
        foreach (Control control in _rows.Controls)
        {
            control.Width = width;
        }
    }

    private void RunOnUi(Action action)
    {
        if (IsDisposed)
        {
            return;
        }
        if (InvokeRequired)
        {
            try { BeginInvoke(action); } catch { }
            return;
        }
        action();
    }
}

/// <summary>单个传输任务行。</summary>
internal sealed class XFtpTransferRow : Control
{
    private readonly XFtpTransferTask _task;
    private readonly ToolTip _toolTip = new XFtpToolTip();

    public XFtpTransferRow(XFtpTransferTask task)
    {
        _task = task;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = XFtpTheme.Panel;
        Height = 58;
        Margin = new Padding(0, 0, 0, 4);
    }

    public void RefreshState()
    {
        if (!string.IsNullOrWhiteSpace(_task.Error))
        {
            _toolTip.SetToolTip(this, _task.Error);
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var rect = new Rectangle(0, 0, Math.Max(1, Width - 2), Math.Max(1, Height - 2));
        using var card = XFtpTheme.RoundedRect(rect, 7);
        Color stateColor = _task.State switch
        {
            XFtpTaskState.Success => XFtpTheme.Green,
            XFtpTaskState.Failed => XFtpTheme.Red,
            XFtpTaskState.Canceled => XFtpTheme.Muted,
            _ => _task.IsUpload ? XFtpTheme.Green : XFtpTheme.Accent
        };
        using (var fill = new SolidBrush(_task.State == XFtpTaskState.Failed
            ? XFtpTheme.Blend(XFtpTheme.PanelAlt, XFtpTheme.Red, 0.06f)
            : XFtpTheme.PanelAlt))
        {
            g.FillPath(fill, card);
        }
        using (var pen = new Pen(_task.State == XFtpTaskState.Running
            ? stateColor : XFtpTheme.BorderLight))
        {
            g.DrawPath(pen, card);
        }

        var iconRect = new Rectangle(10, 12, 24, 24);
        using (var iconBack = new SolidBrush(Color.FromArgb(40, stateColor)))
        using (var iconPath = XFtpTheme.RoundedRect(iconRect, 6))
        {
            g.FillPath(iconBack, iconPath);
        }
        TextRenderer.DrawText(g, _task.IsUpload ? "\uE898" : "\uE896",
            XFtpTheme.Mdl2_12, iconRect, stateColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPadding);

        string name = _task.Name;
        TextRenderer.DrawText(g, name, XFtpTheme.Ui88Bold,
            new Rectangle(44, 7, Math.Max(80, Width - 250), 18), XFtpTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        string direction = _task.IsUpload ? "上传" : "下载";
        string detail = direction;
        if (_task.Total > 0)
        {
            detail += "  " + XFtpEntry.FormatSize(_task.Done) + " / "
                + XFtpEntry.FormatSize(_task.Total);
        }
        else
        {
            detail += "  " + XFtpEntry.FormatSize(_task.Done);
        }
        if (_task.State == XFtpTaskState.Running && _task.Speed > 0)
        {
            detail += "  ·  " + FormatSpeed(_task.Speed);
        }
        TextRenderer.DrawText(g, detail, XFtpTheme.Ui85,
            new Rectangle(44, 25, Math.Max(80, Width - 250), 16), XFtpTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        string status;
        Color statusColor;
        switch (_task.State)
        {
            case XFtpTaskState.Success:
                status = "Success";
                statusColor = XFtpTheme.Green;
                break;
            case XFtpTaskState.Failed:
                status = "uploadfail";
                statusColor = XFtpTheme.Red;
                break;
            case XFtpTaskState.Canceled:
                status = "已取消";
                statusColor = XFtpTheme.Muted;
                break;
            case XFtpTaskState.Waiting:
                status = "等待中";
                statusColor = XFtpTheme.Muted;
                break;
            default:
                int percent = _task.Total > 0
                    ? (int)Math.Clamp(_task.Done * 100L / _task.Total, 0, 100) : 0;
                status = percent + "%";
                statusColor = XFtpTheme.Accent;
                break;
        }
        if (_task.State == XFtpTaskState.Failed && !_task.IsUpload)
        {
            status = "downloadfail";
        }
        TextRenderer.DrawText(g, status, XFtpTheme.Ui(8.8f, FontStyle.Bold),
            new Rectangle(Width - 160, 8, 146, 18), statusColor,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        int trackLeft = 44;
        int trackTop = Height - 17;
        int trackWidth = Math.Max(40, Width - trackLeft - 14);
        var track = new Rectangle(trackLeft, trackTop, trackWidth, 6);
        using (var trackPath = XFtpTheme.RoundedRect(track, 3))
        using (var trackBrush = new SolidBrush(XFtpTheme.BorderLight))
        {
            g.FillPath(trackBrush, trackPath);
        }
        int percentValue = _task.State == XFtpTaskState.Success ? 100
            : _task.Total > 0
                ? (int)Math.Clamp(_task.Done * 100L / _task.Total, 0, 100)
                : _task.State is XFtpTaskState.Failed or XFtpTaskState.Canceled ? 100 : 0;
        if (percentValue > 0)
        {
            int fillWidth = Math.Max(6, trackWidth * percentValue / 100);
            var fill = new Rectangle(trackLeft, trackTop, fillWidth, 6);
            using var fillPath = XFtpTheme.RoundedRect(fill, 3);
            using var fillBrush = new LinearGradientBrush(fill,
                XFtpTheme.Lighten(stateColor, 22), stateColor, LinearGradientMode.Horizontal);
            g.FillPath(fillBrush, fillPath);
        }
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0)
        {
            return "";
        }
        return XFtpEntry.FormatSize((long)bytesPerSecond) + "/s";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
        }
        base.Dispose(disposing);
    }
}
