using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MiniMonitor.Video;

/// <summary>
/// 无损截取时间线：关键帧刻度、音频波形、片段块、入出点修剪与缩放滚动。
/// </summary>
internal sealed class LosslessCutTimeline : Control
{
    private const int Pad = 10;
    private const int RulerHeight = 28;
    private const int WaveTop = RulerHeight;
    private const int WaveHeight = 64;
    private const int SegmentTop = WaveTop + WaveHeight + 4;
    private const int SegmentHeight = 46;
    private const int EdgeGrip = 7;

    private enum DragMode
    {
        None,
        Playhead,
        TrimStart,
        TrimEnd
    }

    private LosslessFile? _file;
    private double _pixelsPerSecond = 80;
    private double _scrollSeconds;
    private double _playhead;
    private LosslessSegment? _selected;
    private bool _snapToKeyframes = true;
    private DragMode _dragMode;
    private LosslessSegment? _dragSegment;
    private double _dragMouseTime;
    private double _dragOriginalStart;
    private double _dragOriginalEnd;
    private readonly StringFormat _left = new()
    {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.NoWrap
    };
    private readonly StringFormat _center = new()
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.NoWrap
    };

    public event EventHandler<double>? SeekRequested;
    public event EventHandler? SelectionChanged;
    public event EventHandler? SegmentChanged;
    public event EventHandler? BeforeEdit;
    public event EventHandler? ScrollChanged;
    public event EventHandler<(LosslessSegment Segment, Point Screen)>? SegmentContextRequested;

    public LosslessCutTimeline()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = VideoTheme.Window;
        TabStop = true;
    }

    public LosslessFile? File
    {
        get => _file;
        set
        {
            _file = value;
            _selected = null;
            _scrollSeconds = 0;
            Invalidate();
        }
    }

    public double PixelsPerSecond
    {
        get => _pixelsPerSecond;
        set
        {
            _pixelsPerSecond = Math.Clamp(value, 4, 1600);
            Invalidate();
        }
    }

    public double ScrollSeconds
    {
        get => _scrollSeconds;
        set
        {
            _scrollSeconds = Math.Max(0, value);
            Invalidate();
        }
    }

    public double Playhead
    {
        get => _playhead;
        set
        {
            _playhead = Math.Max(0, value);
            Invalidate();
        }
    }

    public LosslessSegment? SelectedSegment
    {
        get => _selected;
        set
        {
            if (!ReferenceEquals(_selected, value))
            {
                _selected = value;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
        }
    }

    public bool SnapToKeyframes
    {
        get => _snapToKeyframes;
        set => _snapToKeyframes = value;
    }

    public double VisibleSeconds =>
        Math.Max(0.1, (Width - Pad * 2) / Math.Max(1, _pixelsPerSecond));

    public double TotalSeconds => Math.Max(10, (_file?.Duration ?? 0) + 2);

    public double TimeToX(double time) => Pad + (time - _scrollSeconds) * _pixelsPerSecond;

    public double XToTime(double x) =>
        Math.Max(0, (x - Pad) / Math.Max(1, _pixelsPerSecond) + _scrollSeconds);

    public void SetPlayhead(double seconds, bool notify)
    {
        int oldX = (int)Math.Round(TimeToX(_playhead));
        _playhead = Math.Max(0, seconds);
        int newX = (int)Math.Round(TimeToX(_playhead));
        if (oldX == newX)
        {
            Invalidate(new Rectangle(oldX - 10, 0, 21, Height));
        }
        else
        {
            Invalidate(new Rectangle(Math.Min(oldX, newX) - 10, 0,
                Math.Abs(newX - oldX) + 21, Height));
        }
        if (notify)
        {
            SeekRequested?.Invoke(this, _playhead);
        }
    }

    public void EnsureVisible(double seconds)
    {
        double visible = VisibleSeconds;
        double target = _scrollSeconds;
        if (seconds < _scrollSeconds)
        {
            target = Math.Max(0, seconds - visible * 0.1);
        }
        else if (seconds > _scrollSeconds + visible)
        {
            target = Math.Max(0, seconds - visible * 0.85);
        }
        if (Math.Abs(target - _scrollSeconds) > 0.001)
        {
            _scrollSeconds = target;
            Invalidate();
            ScrollChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ZoomAt(double time, double factor)
    {
        double old = _pixelsPerSecond;
        double updated = Math.Clamp(old * factor, 4, 1600);
        if (Math.Abs(updated - old) < 0.001)
        {
            return;
        }
        int x = (int)TimeToX(time);
        _pixelsPerSecond = updated;
        _scrollSeconds = Math.Max(0, time - (x - Pad) / updated);
        Invalidate();
        ScrollChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ScrollBy(double delta)
    {
        _scrollSeconds = Math.Max(0, _scrollSeconds + delta);
        Invalidate();
        ScrollChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshTimeline()
    {
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var background = new SolidBrush(VideoTheme.Window))
        {
            g.FillRectangle(background, ClientRectangle);
        }
        if (_file == null)
        {
            using var brush = new SolidBrush(VideoTheme.Muted);
            g.DrawString("打开视频 / 音频文件后显示关键帧与片段", VideoTheme.UiTitle,
                brush, new RectangleF(0, 0, Width, Height), _center);
            return;
        }
        DrawRuler(g);
        DrawWaveform(g);
        DrawSegments(g);
        DrawPlayhead(g);
    }

    private void DrawRuler(Graphics g)
    {
        if (_file == null)
        {
            return;
        }
        using (var back = new SolidBrush(VideoTheme.RulerBack))
        {
            g.FillRectangle(back, 0, 0, Width, RulerHeight);
        }
        using (var border = new Pen(VideoTheme.Border))
        {
            g.DrawLine(border, 0, RulerHeight - 1, Width, RulerHeight - 1);
        }
        double step = ChooseStep();
        double start = Math.Floor(_scrollSeconds / step) * step;
        double end = _scrollSeconds + VisibleSeconds + step;
        using var pen = new Pen(Color.FromArgb(120, 124, 132));
        using var text = new SolidBrush(VideoTheme.Muted);
        for (double time = start; time <= end; time += step)
        {
            int x = (int)Math.Round(TimeToX(time));
            if (x < 0 || x > Width)
            {
                continue;
            }
            g.DrawLine(pen, x, RulerHeight - 10, x, RulerHeight - 1);
            g.DrawString(VideoTheme.ShortTime(time), VideoTheme.UiTimeSmall, text,
                new PointF(x + 3, 2));
        }
        // 关键帧刻度
        if (_file.Keyframes.Count > 0)
        {
            int stepFrames = Math.Max(1, _file.Keyframes.Count / 4000);
            using var keyPen = new Pen(Color.FromArgb(230, 168, 72));
            for (int i = 0; i < _file.Keyframes.Count; i += stepFrames)
            {
                double time = _file.Keyframes[i];
                int x = (int)Math.Round(TimeToX(time));
                if (x < 0 || x > Width)
                {
                    continue;
                }
                g.DrawLine(keyPen, x, RulerHeight - 6, x, RulerHeight - 1);
            }
        }
    }

    private double ChooseStep()
    {
        double[] steps = { 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800, 3600 };
        foreach (double step in steps)
        {
            if (step * _pixelsPerSecond >= 72)
            {
                return step;
            }
        }
        return 3600;
    }

    private void DrawWaveform(Graphics g)
    {
        if (_file == null)
        {
            return;
        }
        var band = new Rectangle(0, WaveTop, Width, WaveHeight);
        using (var back = new SolidBrush(Color.FromArgb(28, 29, 32)))
        {
            g.FillRectangle(back, band);
        }
        using (var centerPen = new Pen(Color.FromArgb(60, 63, 69)))
        {
            g.DrawLine(centerPen, 0, WaveTop + WaveHeight / 2, Width, WaveTop + WaveHeight / 2);
        }
        if (_file.Waveform == null || _file.Waveform.Length == 0)
        {
            using var hint = new SolidBrush(VideoTheme.Muted);
            g.DrawString(_file.Info.HasAudio ? "正在生成音频波形…" : "无音频流",
                VideoTheme.UiSmall, hint, new RectangleF(8, WaveTop, Width - 16, WaveHeight), _left);
            return;
        }
        var peaks = _file.Waveform;
        using var wavePen = new Pen(Color.FromArgb(145, 86, 170, 230));
        int center = WaveTop + WaveHeight / 2;
        int half = WaveHeight / 2 - 4;
        int left = (int)Math.Max(0, TimeToX(0));
        int right = (int)Math.Min(Width, TimeToX(_file.Duration));
        for (int x = left; x <= right; x++)
        {
            double time = XToTime(x);
            int index = (int)(time / Math.Max(0.001, _file.Duration) * peaks.Length);
            if (index < 0 || index >= peaks.Length)
            {
                continue;
            }
            int height = Math.Max(1, (int)(peaks[index] * half));
            g.DrawLine(wavePen, x, center - height, x, center + height);
        }
    }

    private void DrawSegments(Graphics g)
    {
        if (_file == null)
        {
            return;
        }
        var band = new Rectangle(0, SegmentTop, Width, SegmentHeight);
        using (var back = new SolidBrush(Color.FromArgb(32, 33, 36)))
        {
            g.FillRectangle(back, band);
        }
        using var hint = new SolidBrush(VideoTheme.Muted);
        if (_file.Segments.Count == 0)
        {
            g.DrawString("用 I / O 设置入出点，或点击“在播放头分割”创建片段",
                VideoTheme.UiSmall, hint,
                new RectangleF(8, SegmentTop, Width - 16, SegmentHeight), _left);
            return;
        }
        for (int i = 0; i < _file.Segments.Count; i++)
        {
            var segment = _file.Segments[i];
            int left = (int)Math.Round(TimeToX(segment.Start));
            int right = (int)Math.Round(TimeToX(segment.End));
            if (right < 0 || left > Width)
            {
                continue;
            }
            int drawLeft = Math.Max(0, left);
            int drawRight = Math.Min(Width, right);
            int width = Math.Max(2, drawRight - drawLeft);
            var rect = new Rectangle(drawLeft + 1, SegmentTop + 5, width - 2, SegmentHeight - 10);
            if (!rect.IntersectsWith(Rectangle.Round(g.VisibleClipBounds)))
            {
                continue;
            }
            Color color = i % 2 == 0 ? Color.FromArgb(56, 112, 170)
                : Color.FromArgb(52, 96, 145);
            if (!segment.Enabled)
            {
                color = Color.FromArgb(70, 72, 78);
            }
            using (var fill = new SolidBrush(color))
            {
                VideoTheme.DrawRoundedRect(g, fill, rect, 5);
            }
            using (var pen = new Pen(ReferenceEquals(segment, _selected)
                ? VideoTheme.Selection : Color.FromArgb(45, 255, 255, 255),
                ReferenceEquals(segment, _selected) ? 2 : 1))
            {
                g.DrawRectangle(pen, rect);
            }
            if (rect.Width > 28)
            {
                using var text = new SolidBrush(Color.White);
                string label = "#" + (i + 1) + "  " + VideoTheme.Timecode(segment.Start, false)
                    + " → " + VideoTheme.Timecode(segment.End, false)
                    + "  (" + VideoTheme.Timecode(segment.Duration, false) + ")"
                    + (string.IsNullOrWhiteSpace(segment.Name) ? "" : "  " + segment.Name)
                    + (segment.Enabled ? "" : "  [不导出]");
                g.DrawString(label, VideoTheme.UiBold9, text,
                    new RectangleF(rect.X + 8, rect.Y, rect.Width - 16, rect.Height), _left);
            }
            // 修剪把手
            if (rect.Width > 8)
            {
                using var handle = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
                g.FillRectangle(handle, rect.Left + 2, rect.Top + rect.Height / 2 - 8, 2, 16);
                g.FillRectangle(handle, rect.Right - 4, rect.Top + rect.Height / 2 - 8, 2, 16);
            }
        }
    }

    private void DrawPlayhead(Graphics g)
    {
        int x = (int)Math.Round(TimeToX(_playhead));
        if (x < 0 || x > Width)
        {
            return;
        }
        using var pen = new Pen(VideoTheme.Playhead, 2);
        g.DrawLine(pen, x, 0, x, Height);
        using var brush = new SolidBrush(VideoTheme.Playhead);
        g.FillPolygon(brush, new[]
        {
            new Point(x - 6, 0),
            new Point(x + 6, 0),
            new Point(x, 10)
        });
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_file == null)
        {
            return;
        }
        Focus();
        if (e.Button == MouseButtons.Right)
        {
            var hit = HitSegment(e.Location, out _, out _);
            if (hit != null)
            {
                SelectedSegment = hit;
                SegmentContextRequested?.Invoke(this, (hit, PointToScreen(e.Location)));
            }
            return;
        }
        if (e.Button != MouseButtons.Left)
        {
            return;
        }
        if (e.Y < SegmentTop)
        {
            _dragMode = DragMode.Playhead;
            Capture = true;
            SetPlayhead(XToTime(e.X), true);
            return;
        }
        var segment = HitSegment(e.Location, out bool leftEdge, out bool rightEdge);
        if (segment == null)
        {
            SelectedSegment = null;
            SetPlayhead(XToTime(e.X), true);
            return;
        }
        SelectedSegment = segment;
        _dragSegment = segment;
        _dragMouseTime = XToTime(e.X);
        _dragOriginalStart = segment.Start;
        _dragOriginalEnd = segment.End;
        _dragMode = leftEdge ? DragMode.TrimStart : rightEdge ? DragMode.TrimEnd : DragMode.Playhead;
        if (_dragMode == DragMode.Playhead)
        {
            SetPlayhead(XToTime(e.X), true);
            return;
        }
        BeforeEdit?.Invoke(this, EventArgs.Empty);
        Capture = true;
    }

    private LosslessSegment? HitSegment(Point location, out bool leftEdge, out bool rightEdge)
    {
        return HitSegment(location, out leftEdge, out rightEdge, out _);
    }

    private LosslessSegment? HitSegment(Point location, out bool leftEdge, out bool rightEdge,
        out int index)
    {
        leftEdge = false;
        rightEdge = false;
        index = -1;
        if (_file == null || location.Y < SegmentTop || location.Y > SegmentTop + SegmentHeight)
        {
            return null;
        }
        for (int i = 0; i < _file.Segments.Count; i++)
        {
            var segment = _file.Segments[i];
            int left = (int)Math.Round(TimeToX(segment.Start));
            int right = (int)Math.Round(TimeToX(segment.End));
            if (location.X < left - EdgeGrip || location.X > right + EdgeGrip)
            {
                continue;
            }
            index = i;
            leftEdge = Math.Abs(location.X - left) <= EdgeGrip;
            rightEdge = Math.Abs(location.X - right) <= EdgeGrip;
            return segment;
        }
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_file == null)
        {
            return;
        }
        if (_dragMode == DragMode.None)
        {
            var hit = HitSegment(e.Location, out bool left, out bool right);
            Cursor = hit == null ? Cursors.Default
                : left || right ? Cursors.SizeWE : Cursors.Hand;
            return;
        }
        double time = XToTime(e.X);
        switch (_dragMode)
        {
            case DragMode.Playhead:
                SetPlayhead(time, true);
                break;
            case DragMode.TrimStart when _dragSegment != null:
            {
                double value = Math.Max(0, Math.Min(time, _dragOriginalEnd - 0.02));
                if (_snapToKeyframes)
                {
                    value = _file.NearestKeyframe(value);
                }
                _dragSegment.Start = Math.Max(0, Math.Min(value, _dragSegment.End - 0.02));
                Invalidate();
                break;
            }
            case DragMode.TrimEnd when _dragSegment != null:
            {
                double value = Math.Min(_file.Duration, Math.Max(time, _dragOriginalStart + 0.02));
                if (_snapToKeyframes)
                {
                    value = _file.NearestKeyframe(value);
                }
                _dragSegment.End = Math.Min(_file.Duration,
                    Math.Max(value, _dragSegment.Start + 0.02));
                Invalidate();
                break;
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragMode == DragMode.None)
        {
            return;
        }
        bool changed = _dragMode != DragMode.Playhead;
        _dragMode = DragMode.None;
        _dragSegment = null;
        Capture = false;
        if (changed)
        {
            SegmentChanged?.Invoke(this, EventArgs.Empty);
        }
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((ModifierKeys & Keys.Control) == Keys.Control)
        {
            ZoomAt(XToTime(e.X), e.Delta > 0 ? 1.2 : 1 / 1.2);
            return;
        }
        if ((ModifierKeys & Keys.Shift) == Keys.Shift)
        {
            ScrollBy(VisibleSeconds * (e.Delta > 0 ? -0.12 : 0.12));
            return;
        }
        double direction = e.Delta > 0 ? -1 : 1;
        SetPlayhead(Math.Clamp(_playhead + direction, 0, _file?.Duration ?? 0), true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _left.Dispose();
            _center.Dispose();
        }
        base.Dispose(disposing);
    }
}
