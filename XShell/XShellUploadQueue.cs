using System.Collections.Concurrent;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MiniMonitor.XShell;

/// <summary>SFTP 窗口底部的上传任务队列：显示每个文件的上传进度、成功与失败状态。</summary>
internal sealed class XShellUploadQueue : UserControl
{
    private readonly FlowLayoutPanel _rows;
    private readonly Label _empty;
    private readonly Label _summary;
    private readonly Dictionary<string, XShellUploadTaskRow> _tasks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _lastPercent =
        new(StringComparer.OrdinalIgnoreCase);

    public XShellUploadQueue()
    {
        BackColor = XShellTheme.Panel;
        Font = XShellTheme.Ui(8.8f);
        Height = 176;

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = XShellTheme.PanelAlt
        };
        header.Paint += (_, e) =>
        {
            using var background = new SolidBrush(XShellTheme.PanelAlt);
            e.Graphics.FillRectangle(background, header.ClientRectangle);
            using var accent = new SolidBrush(XShellTheme.Accent);
            e.Graphics.FillRectangle(accent, 10, 9, 3, 16);
            TextRenderer.DrawText(e.Graphics, "上传任务",
                XShellTheme.Ui(9f, FontStyle.Bold),
                new Rectangle(20, 0, 120, header.Height), XShellTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPadding);
            using var pen = new Pen(XShellTheme.BorderLight);
            e.Graphics.DrawLine(pen, 0, header.Height - 1,
                header.Width, header.Height - 1);
        };
        _summary = new Label
        {
            Dock = DockStyle.Right,
            Width = 150,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = XShellTheme.Muted,
            BackColor = Color.Transparent,
            Font = XShellTheme.Ui(8.2f),
            Padding = new Padding(0, 0, 8, 0)
        };
        var clear = XShellTheme.FlatButton("清空已完成", 92, 24);
        clear.Dock = DockStyle.Right;
        clear.Margin = new Padding(4, 5, 4, 5);
        clear.Click += (_, _) => ClearFinished();
        header.Controls.Add(_summary);
        header.Controls.Add(clear);

        _empty = new Label
        {
            Text = "暂无上传任务。上传文件后会在这里显示进度。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = XShellTheme.Muted,
            BackColor = XShellTheme.Panel
        };
        _rows = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = XShellTheme.Panel,
            Padding = new Padding(6, 4, 6, 4),
            Visible = false
        };
        _rows.Resize += (_, _) => ResizeRows();

        var body = new Panel { Dock = DockStyle.Fill, BackColor = XShellTheme.Panel };
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
        XShellTheme.ApplyNativeTheme(this);
    }

    public void StartTask(string filePath, long total)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }
        RunOnUi(() =>
        {
            _lastPercent.TryRemove(filePath, out _);
            if (!_tasks.TryGetValue(filePath, out var row))
            {
                row = new XShellUploadTaskRow(filePath);
                _tasks[filePath] = row;
                _rows.Controls.Add(row);
            }
            else
            {
                row.Reset();
            }
            row.SetTotal(total);
            UpdateState();
        });
    }

    public void UpdateProgress(string filePath, long uploaded, long total)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }
        int percent = total > 0
            ? (int)Math.Clamp(uploaded * 100L / total, 0, 100)
            : 0;
        if (_lastPercent.TryGetValue(filePath, out int last) && last == percent)
        {
            return;
        }
        _lastPercent[filePath] = percent;
        RunOnUi(() =>
        {
            if (_tasks.TryGetValue(filePath, out var row))
            {
                row.SetProgress(uploaded, total);
            }
        });
    }

    public void Complete(string filePath)
    {
        _lastPercent.TryRemove(filePath, out _);
        RunOnUi(() =>
        {
            if (_tasks.TryGetValue(filePath, out var row))
            {
                row.SetSuccess();
                UpdateState();
            }
        });
    }

    public void Fail(string filePath, string error)
    {
        _lastPercent.TryRemove(filePath, out _);
        RunOnUi(() =>
        {
            if (_tasks.TryGetValue(filePath, out var row))
            {
                row.SetFailed(error);
                UpdateState();
            }
        });
    }

    public void ClearFinished()
    {
        RunOnUi(() =>
        {
            foreach (var pair in _tasks.ToList())
            {
                if (!pair.Value.IsFinished)
                {
                    continue;
                }
                _rows.Controls.Remove(pair.Value);
                pair.Value.Dispose();
                _tasks.Remove(pair.Key);
            }
            UpdateState();
        });
    }

    private void UpdateState()
    {
        int running = _tasks.Values.Count(row => !row.IsFinished);
        int done = _tasks.Values.Count(row => row.IsSuccess);
        int failed = _tasks.Values.Count(row => row.IsFailed);
        _summary.Text = running > 0
            ? $"上传中 {running} · 成功 {done} · 失败 {failed}"
            : $"成功 {done} · 失败 {failed}";
        bool hasRows = _tasks.Count > 0;
        _rows.Visible = hasRows;
        _empty.Visible = !hasRows;
        ResizeRows();
    }

    private void ResizeRows()
    {
        int width = Math.Max(240, _rows.ClientSize.Width - 22);
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

/// <summary>单个上传任务行：文件名、大小、进度条与 Success / uploadfail 状态。</summary>
internal sealed class XShellUploadTaskRow : Control
{
    private readonly string _fileName;
    private readonly ToolTip _toolTip = new XShellToolTip();
    private long _total;
    private long _uploaded;
    private UploadState _state = UploadState.Uploading;
    private int _lastPercent = -1;
    private string _error = "";

    private enum UploadState
    {
        Uploading,
        Success,
        Failed
    }

    public XShellUploadTaskRow(string filePath)
    {
        _fileName = Path.GetFileName(filePath);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = XShellTheme.Panel;
        Height = 56;
        Margin = new Padding(0, 0, 0, 4);
    }

    public bool IsFinished => _state != UploadState.Uploading;
    public bool IsSuccess => _state == UploadState.Success;
    public bool IsFailed => _state == UploadState.Failed;

    public void Reset()
    {
        _state = UploadState.Uploading;
        _uploaded = 0;
        _lastPercent = -1;
        _error = "";
        _toolTip.SetToolTip(this, "");
        Invalidate();
    }

    public void SetTotal(long total)
    {
        _total = Math.Max(0, total);
        Invalidate();
    }

    public void SetProgress(long uploaded, long total)
    {
        if (_state != UploadState.Uploading)
        {
            return;
        }
        _uploaded = Math.Max(0, uploaded);
        if (total > 0)
        {
            _total = total;
        }
        int percent = _total <= 0 ? 0 : (int)Math.Clamp(_uploaded * 100L / _total, 0, 100);
        if (percent != _lastPercent)
        {
            _lastPercent = percent;
            Invalidate();
        }
    }

    public void SetSuccess()
    {
        _state = UploadState.Success;
        if (_total > 0)
        {
            _uploaded = _total;
        }
        Invalidate();
    }

    public void SetFailed(string error)
    {
        _state = UploadState.Failed;
        _error = error ?? "";
        _toolTip.SetToolTip(this, _error);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var rect = new Rectangle(0, 0, Math.Max(1, Width - 2), Math.Max(1, Height - 2));
        using var cardPath = XShellTheme.RoundedRect(rect, 7);
        using (var brush = new SolidBrush(_state == UploadState.Failed
            ? XShellTheme.Blend(XShellTheme.PanelAlt, XShellTheme.Red, 0.06f)
            : XShellTheme.PanelAlt))
        {
            g.FillPath(brush, cardPath);
        }
        Color border = _state switch
        {
            UploadState.Success => XShellTheme.Green,
            UploadState.Failed => XShellTheme.Red,
            _ => XShellTheme.BorderLight
        };
        using (var pen = new Pen(border))
        {
            g.DrawPath(pen, cardPath);
        }

        TextRenderer.DrawText(g, _fileName, XShellTheme.Ui(8.8f, FontStyle.Bold),
            new Rectangle(12, 7, Math.Max(80, Width - 250), 18),
            XShellTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        string sizeText = _total > 0
            ? FormatSize(_uploaded) + " / " + FormatSize(_total)
            : FormatSize(_uploaded);
        TextRenderer.DrawText(g, sizeText, XShellTheme.Ui(8f),
            new Rectangle(12, 25, Math.Max(80, Width - 250), 16),
            XShellTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPadding);

        string status;
        Color statusColor;
        if (_state == UploadState.Success)
        {
            status = "Success";
            statusColor = XShellTheme.Green;
        }
        else if (_state == UploadState.Failed)
        {
            status = "uploadfail";
            statusColor = XShellTheme.Red;
        }
        else
        {
            int percent = _total <= 0 ? 0
                : (int)Math.Clamp(_uploaded * 100L / _total, 0, 100);
            status = "上传中 " + percent + "%";
            statusColor = XShellTheme.Accent;
        }
        TextRenderer.DrawText(g, status, XShellTheme.Ui(8.8f, FontStyle.Bold),
            new Rectangle(Width - 190, 7, 176, 18), statusColor,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPadding);

        int trackLeft = 12;
        int trackTop = Height - 18;
        int trackWidth = Math.Max(40, Width - 24);
        var track = new Rectangle(trackLeft, trackTop, trackWidth, 7);
        using (var trackPath = XShellTheme.RoundedRect(track, 3))
        using (var trackBrush = new SolidBrush(XShellTheme.BorderLight))
        {
            g.FillPath(trackBrush, trackPath);
        }
        int percentValue = _total <= 0 ? (_state == UploadState.Success ? 100 : 0)
            : (int)Math.Clamp(_uploaded * 100L / _total, 0, 100);
        if (percentValue > 0)
        {
            int fillWidth = Math.Max(6, trackWidth * percentValue / 100);
            var fill = new Rectangle(trackLeft, trackTop, fillWidth, 7);
            Color fillColor = _state switch
            {
                UploadState.Success => XShellTheme.Green,
                UploadState.Failed => XShellTheme.Red,
                _ => XShellTheme.Accent
            };
            using var fillPath = XShellTheme.RoundedRect(fill, 3);
            using var fillBrush = new LinearGradientBrush(fill,
                XShellTheme.Lighten(fillColor, 22), fillColor,
                LinearGradientMode.Horizontal);
            g.FillPath(fillBrush, fillPath);
        }
    }

    private static string FormatSize(long size)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = Math.Max(0, size);
        int index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        return index == 0 ? value.ToString("0") + " " + units[index]
            : value.ToString("0.##") + " " + units[index];
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
