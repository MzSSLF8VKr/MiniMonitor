using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// 照片预览控件：棋盘格背景、等比适应显示、鼠标拖动框选裁剪区域。
/// 只负责显示，不负责释放 Photo。
/// </summary>
internal sealed class PhotoPreviewControl : Control
{
    private Bitmap? _photo;
    private Bitmap? _displayCache;
    private Bitmap? _checkerTile;
    private bool _dragging;
    private Point _startImage;
    private Rectangle _selection = Rectangle.Empty;
    private RectangleF _imageRect = RectangleF.Empty;
    private float _zoom = 1f;
    private PointF _pan = PointF.Empty;
    private bool _panning;
    private Point _panStart;
    private PointF _panOrigin;

    public PhotoPreviewControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(241, 245, 249);
        Cursor = Cursors.Default;
    }

    public Bitmap? Photo
    {
        get => _photo;
        set
        {
            _photo = value;
            _displayCache?.Dispose();
            _displayCache = null;
            _zoom = 1f;
            _pan = PointF.Empty;
            _panning = false;
            if (_selection.Width > 0 && (value == null
                || _selection.Right > value.Width || _selection.Bottom > value.Height))
            {
                _selection = Rectangle.Empty;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            Invalidate();
        }
    }

    public bool SelectionEnabled { get; set; } = true;

    /// <summary>启用滚轮缩放与右键拖动平移（仅预览场景开启，裁剪选区需要保持 1:1 显示）。</summary>
    public bool ZoomEnabled { get; set; }

    public float ZoomLevel => _zoom;

    public Rectangle? Selection => _selection.Width > 2 && _selection.Height > 2 ? _selection : null;

    public event EventHandler? SelectionChanged;

    public void ClearSelection()
    {
        if (_selection.IsEmpty)
        {
            return;
        }
        _selection = Rectangle.Empty;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private RectangleF CalcImageRect()
    {
        if (_photo == null || _photo.Width <= 0 || _photo.Height <= 0)
        {
            return RectangleF.Empty;
        }
        var fit = CalcFitRect();
        float scale = fit.Width / _photo.Width * _zoom;
        float w = _photo.Width * scale;
        float h = _photo.Height * scale;
        return new RectangleF(
            (Width - w) / 2f + _pan.X,
            (Height - h) / 2f + _pan.Y,
            w, h);
    }

    /// <summary>1:1 缩放时的等比适应矩形（不含缩放与平移）。</summary>
    private RectangleF CalcFitRect()
    {
        if (_photo == null || _photo.Width <= 0 || _photo.Height <= 0)
        {
            return RectangleF.Empty;
        }
        float availableWidth = Math.Max(20, Width - 28);
        float availableHeight = Math.Max(20, Height - 28);
        float scale = Math.Min(availableWidth / _photo.Width, availableHeight / _photo.Height);
        float w = _photo.Width * scale;
        float h = _photo.Height * scale;
        return new RectangleF((Width - w) / 2f, (Height - h) / 2f, w, h);
    }

    private void ClampPan()
    {
        if (_photo == null || _zoom <= 1f)
        {
            _pan = PointF.Empty;
            return;
        }
        var fit = CalcFitRect();
        float w = fit.Width * _zoom;
        float h = fit.Height * _zoom;
        float maxX = Math.Max(0f, (w - Math.Max(20, Width - 28)) / 2f);
        float maxY = Math.Max(0f, (h - Math.Max(20, Height - 28)) / 2f);
        _pan.X = Math.Clamp(_pan.X, -maxX, maxX);
        _pan.Y = Math.Clamp(_pan.Y, -maxY, maxY);
    }

    private void ZoomAt(Point location, float factor)
    {
        if (_photo == null)
        {
            return;
        }
        float target = Math.Clamp(_zoom * factor, 1f, 10f);
        if (Math.Abs(target - _zoom) < 0.001f)
        {
            return;
        }
        var before = CalcImageRect();
        float fx = before.Width > 0
            ? Math.Clamp((location.X - before.X) / before.Width, 0f, 1f) : 0.5f;
        float fy = before.Height > 0
            ? Math.Clamp((location.Y - before.Y) / before.Height, 0f, 1f) : 0.5f;
        _zoom = target;
        _pan = PointF.Empty;
        var after = CalcImageRect();
        _pan.X = location.X - (after.X + fx * after.Width);
        _pan.Y = location.Y - (after.Y + fy * after.Height);
        ClampPan();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(BackColor))
        {
            g.FillRectangle(bg, ClientRectangle);
        }

        if (_photo == null)
        {
            using var dash = new Pen(Color.FromArgb(203, 213, 225), 1.5f) { DashStyle = DashStyle.Dash };
            var box = new RectangleF(24, 24, Width - 48, Height - 48);
            using var path = ShutdownTheme.RoundRect(box, 14);
            g.DrawPath(dash, path);
            using var titleFont = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
            using var hintFont = new Font("Microsoft YaHei UI", 9f);
            TextRenderer.DrawText(g, "尚未打开照片", titleFont,
                new Rectangle(0, Height / 2 - 30, Width, 30),
                Color.FromArgb(100, 116, 139),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "点击“打开照片”，或把图片直接拖到这里", hintFont,
                new Rectangle(0, Height / 2 + 2, Width, 26),
                Color.FromArgb(148, 163, 184),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        _imageRect = CalcImageRect();
        if (_imageRect.Width <= 0 || _imageRect.Height <= 0)
        {
            return;
        }

        var display = EnsureDisplayCache();
        using (var clipPath = new GraphicsPath())
        {
            clipPath.AddRectangle(_imageRect);
            g.SetClip(clipPath);
            DrawCheckerboard(g, _imageRect);
            if (display != null)
            {
                int targetWidth = Math.Max(1, (int)Math.Round(_imageRect.Width));
                int targetHeight = Math.Max(1, (int)Math.Round(_imageRect.Height));
                if (display.Width == targetWidth && display.Height == targetHeight)
                {
                    // 尺寸完全一致时 1:1 绘制，避免二次重采样。
                    g.SmoothingMode = SmoothingMode.None;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(display, (int)Math.Round(_imageRect.X),
                        (int)Math.Round(_imageRect.Y), display.Width, display.Height);
                }
                else
                {
                    // 缩放 / 平移后按目标矩形绘制：放大时用双线性保证拖动流畅，
                    // 适应窗口时用双三次保证静态画质。
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = _zoom > 1.01f
                        ? InterpolationMode.HighQualityBilinear
                        : InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(display, _imageRect);
                }
            }
            g.ResetClip();
        }

        using (var border = new Pen(Color.FromArgb(203, 213, 225), 1f))
        {
            g.DrawRectangle(border, _imageRect.X, _imageRect.Y, _imageRect.Width, _imageRect.Height);
        }

        if (_selection.Width > 1 && _selection.Height > 1)
        {
            var selRect = ImageToScreen(_selection);
            using (var dim = new SolidBrush(Color.FromArgb(120, 15, 23, 42)))
            {
                float l = _imageRect.Left;
                float t = _imageRect.Top;
                float r = _imageRect.Right;
                float b = _imageRect.Bottom;
                g.FillRectangle(dim, l, t, r - l, Math.Max(0, selRect.Top - t));
                g.FillRectangle(dim, l, selRect.Bottom, r - l, Math.Max(0, b - selRect.Bottom));
                g.FillRectangle(dim, l, selRect.Top, Math.Max(0, selRect.Left - l), selRect.Height);
                g.FillRectangle(dim, selRect.Right, selRect.Top, Math.Max(0, r - selRect.Right), selRect.Height);
            }
            using (var pen = new Pen(ShutdownTheme.Accent, 2f))
            {
                g.DrawRectangle(pen, selRect.X, selRect.Y, selRect.Width, selRect.Height);
            }
            string label = _selection.Width + " × " + _selection.Height;
            using var font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);
            var size = TextRenderer.MeasureText(g, label, font);
            var tagRect = new Rectangle(
                (int)Math.Max(_imageRect.Left, selRect.Left),
                (int)Math.Max(_imageRect.Top, selRect.Top - size.Height - 6),
                size.Width + 12, size.Height + 4);
            using var tagBg = new SolidBrush(Color.FromArgb(220, 37, 99, 235));
            g.FillRectangle(tagBg, tagRect);
            TextRenderer.DrawText(g, label, font, tagRect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private Bitmap? EnsureDisplayCache()
    {
        if (_photo == null)
        {
            return null;
        }
        var fit = CalcFitRect();
        if (fit.Width <= 0 || fit.Height <= 0)
        {
            return null;
        }
        // 缩放预览时按 2 倍适应尺寸构建一次缓存：滚轮缩放只拉伸缓存，不会反复重建。
        float cacheScale = ZoomEnabled ? 2f : 1f;
        int width = Math.Min(_photo.Width,
            Math.Max(1, (int)Math.Round(fit.Width * cacheScale)));
        int height = Math.Min(_photo.Height,
            Math.Max(1, (int)Math.Round(fit.Height * cacheScale)));
        if (_displayCache != null
            && _displayCache.Width == width && _displayCache.Height == height)
        {
            return _displayCache;
        }
        _displayCache?.Dispose();
        var cache = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(cache))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            if (width == _photo.Width && height == _photo.Height)
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
            }
            else
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
            }
            g.DrawImage(_photo, new Rectangle(0, 0, width, height));
        }
        _displayCache = cache;
        return cache;
    }

    private void DrawCheckerboard(Graphics g, RectangleF rect)
    {
        if (_checkerTile == null)
        {
            const int cell = 12;
            var tile = new Bitmap(cell * 2, cell * 2);
            using (var tg = Graphics.FromImage(tile))
            {
                tg.Clear(Color.White);
                using var dark = new SolidBrush(Color.FromArgb(229, 235, 243));
                tg.FillRectangle(dark, cell, 0, cell, cell);
                tg.FillRectangle(dark, 0, cell, cell, cell);
            }
            _checkerTile = tile;
        }
        using var brush = new TextureBrush(_checkerTile);
        g.FillRectangle(brush, rect);
    }

    private RectangleF ImageToScreen(Rectangle rect)
    {
        if (_photo == null || _photo.Width <= 0)
        {
            return RectangleF.Empty;
        }
        float scaleX = _imageRect.Width / _photo.Width;
        float scaleY = _imageRect.Height / _photo.Height;
        return new RectangleF(
            _imageRect.X + rect.X * scaleX,
            _imageRect.Y + rect.Y * scaleY,
            rect.Width * scaleX,
            rect.Height * scaleY);
    }

    private Point? ToImagePoint(Point point)
    {
        if (_photo == null || _imageRect.Width <= 0 || _imageRect.Height <= 0)
        {
            return null;
        }
        float scaleX = _photo.Width / _imageRect.Width;
        float scaleY = _photo.Height / _imageRect.Height;
        int x = (int)Math.Round((point.X - _imageRect.X) * scaleX);
        int y = (int)Math.Round((point.Y - _imageRect.Y) * scaleY);
        x = Math.Clamp(x, 0, _photo.Width);
        y = Math.Clamp(y, 0, _photo.Height);
        return new Point(x, y);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right && ZoomEnabled && _photo != null)
        {
            _panning = true;
            _panStart = e.Location;
            _panOrigin = _pan;
            Capture = true;
            Cursor = Cursors.Hand;
            return;
        }
        if (e.Button != MouseButtons.Left || _photo == null || !SelectionEnabled)
        {
            return;
        }
        if (!_imageRect.Contains(e.Location))
        {
            return;
        }
        var imagePoint = ToImagePoint(e.Location);
        if (imagePoint == null)
        {
            return;
        }
        Focus();
        _dragging = true;
        _startImage = imagePoint.Value;
        _selection = new Rectangle(_startImage, Size.Empty);
        Capture = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_panning)
        {
            _pan.X = _panOrigin.X + (e.Location.X - _panStart.X);
            _pan.Y = _panOrigin.Y + (e.Location.Y - _panStart.Y);
            ClampPan();
            Invalidate();
            return;
        }
        if (ZoomEnabled)
        {
            Cursor = _photo != null && _zoom > 1.01f ? Cursors.Hand : Cursors.Default;
        }
        else
        {
            Cursor = _photo != null && SelectionEnabled ? Cursors.Cross : Cursors.Default;
        }
        if (!_dragging)
        {
            return;
        }
        var imagePoint = ToImagePoint(e.Location);
        if (imagePoint == null)
        {
            return;
        }
        int left = Math.Min(_startImage.X, imagePoint.Value.X);
        int top = Math.Min(_startImage.Y, imagePoint.Value.Y);
        int width = Math.Abs(imagePoint.Value.X - _startImage.X);
        int height = Math.Abs(imagePoint.Value.Y - _startImage.Y);
        _selection = new Rectangle(left, top, width, height);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_panning)
        {
            _panning = false;
            Capture = false;
            Cursor = _photo != null && _zoom > 1.01f ? Cursors.Hand : Cursors.Default;
            return;
        }
        if (!_dragging)
        {
            return;
        }
        _dragging = false;
        Capture = false;
        if (_selection.Width < 4 || _selection.Height < 4)
        {
            _selection = Rectangle.Empty;
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_dragging && !_panning)
        {
            Cursor = Cursors.Default;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!ZoomEnabled || _photo == null)
        {
            return;
        }
        if (e is HandledMouseEventArgs handled)
        {
            handled.Handled = true;
        }
        ApplyWheelZoom(e.Location, e.Delta);
    }

    /// <summary>供预览窗口的消息过滤器转发滚轮缩放（窗口不激活时滚轮不会自动送到控件）。</summary>
    public void ApplyWheelZoom(Point location, int delta)
    {
        if (!ZoomEnabled || _photo == null || delta == 0)
        {
            return;
        }
        ZoomAt(location, delta > 0 ? 1.18f : 1f / 1.18f);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _displayCache?.Dispose();
            _displayCache = null;
            _checkerTile?.Dispose();
            _checkerTile = null;
        }
        base.Dispose(disposing);
    }
}

/// <summary>把缩放后的照片绘制到打印页面，供打印预览与实体打印共用。</summary>
internal static class PhotoPrintRenderer
{
    public static void DrawPhoto(Graphics g, Bitmap photo, RectangleF cell, PrintFitMode fit,
        bool showFileName)
    {
        float captionHeight = showFileName ? Math.Min(26f, cell.Height * 0.14f) : 0f;
        var imageCell = RectangleF.FromLTRB(cell.Left, cell.Top, cell.Right,
            Math.Max(cell.Top + 1, cell.Bottom - captionHeight));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        if (fit == PrintFitMode.Stretch)
        {
            g.DrawImage(photo, imageCell);
        }
        else
        {
            float imageAspect = photo.Width / (float)photo.Height;
            float cellAspect = imageCell.Width / imageCell.Height;
            if (fit == PrintFitMode.Fill)
            {
                RectangleF src;
                if (imageAspect > cellAspect)
                {
                    float cropWidth = photo.Height * cellAspect;
                    src = new RectangleF((photo.Width - cropWidth) / 2f, 0, cropWidth, photo.Height);
                }
                else
                {
                    float cropHeight = photo.Width / cellAspect;
                    src = new RectangleF(0, (photo.Height - cropHeight) / 2f, photo.Width, cropHeight);
                }
                g.DrawImage(photo, imageCell, src, GraphicsUnit.Pixel);
            }
            else
            {
                RectangleF dest;
                if (imageAspect > cellAspect)
                {
                    float height = imageCell.Width / imageAspect;
                    dest = new RectangleF(imageCell.Left, imageCell.Top + (imageCell.Height - height) / 2f,
                        imageCell.Width, height);
                }
                else
                {
                    float width = imageCell.Height * imageAspect;
                    dest = new RectangleF(imageCell.Left + (imageCell.Width - width) / 2f, imageCell.Top,
                        width, imageCell.Height);
                }
                g.DrawImage(photo, dest);
            }
        }

        using var border = new Pen(Color.FromArgb(198, 208, 219), 0.6f);
        g.DrawRectangle(border, imageCell.X, imageCell.Y, imageCell.Width, imageCell.Height);

        if (showFileName)
        {
            using var font = new Font("Microsoft YaHei UI", 8f);
            var captionRect = new RectangleF(imageCell.Left, imageCell.Bottom + 2,
                imageCell.Width, Math.Max(10f, captionHeight - 2));
            TextRenderer.DrawText(g, Path.GetFileNameWithoutExtension(photo.Tag?.ToString() ?? ""),
                font, Rectangle.Round(captionRect), Color.FromArgb(71, 85, 105),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
        }
    }
}

internal enum PrintFitMode
{
    Fit,
    Fill,
    Stretch
}
