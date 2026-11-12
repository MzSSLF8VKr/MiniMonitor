using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>全屏区域选择覆盖层：矩形、圆角矩形、椭圆、自由手绘、多边形。</summary>
public sealed class RegionSelectorForm : Form
{
    private readonly Bitmap _background;
    private readonly Rectangle _virtualBounds;
    private readonly SelectionShape _shape;
    private Point _start;
    private Point _current;
    private bool _dragging;
    private readonly List<Point> _freehand = new();
    private readonly List<Point> _polygon = new();
    private Bitmap? _result;

    public Bitmap? Result => _result;
    public Rectangle SelectedBounds { get; private set; }

    public RegionSelectorForm(Bitmap background, Rectangle virtualBounds, SelectionShape shape)
    {
        _background = background;
        _virtualBounds = virtualBounds;
        _shape = shape;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = virtualBounds;
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = Color.Black;
        Cursor = Cursors.Cross;
        AutoScaleMode = AutoScaleMode.None;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawImage(_background, new Rectangle(0, 0, _background.Width, _background.Height),
            new Rectangle(0, 0, _background.Width, _background.Height), GraphicsUnit.Pixel);

        using (var dim = new SolidBrush(Color.FromArgb(118, 0, 0, 0)))
        {
            g.FillRectangle(dim, ClientRectangle);
        }

        var selection = GetSelectionPath();
        if (selection != null)
        {
            g.SetClip(selection);
            g.DrawImage(_background, new Rectangle(0, 0, _background.Width, _background.Height),
                new Rectangle(0, 0, _background.Width, _background.Height), GraphicsUnit.Pixel);
            g.ResetClip();
            using var border = new Pen(Color.FromArgb(79, 70, 229), 2f);
            g.DrawPath(border, selection);
        }

        if (_shape == SelectionShape.Polygon)
        {
            foreach (var point in _polygon)
            {
                using var brush = new SolidBrush(Color.FromArgb(79, 70, 229));
                g.FillEllipse(brush, point.X - 4, point.Y - 4, 8, 8);
            }
        }

        DrawSizeLabel(g);
        DrawInstructions(g);
    }

    private GraphicsPath? GetSelectionPath()
    {
        switch (_shape)
        {
            case SelectionShape.Rectangle:
            case SelectionShape.RoundedRectangle:
            case SelectionShape.Ellipse:
                if (!_dragging && _current == _start)
                {
                    return null;
                }
                var rect = Normalize(_start, _current);
                if (rect.Width < 2 || rect.Height < 2)
                {
                    return null;
                }
                var path = new GraphicsPath();
                if (_shape == SelectionShape.Rectangle)
                {
                    path.AddRectangle(rect);
                }
                else if (_shape == SelectionShape.Ellipse)
                {
                    path.AddEllipse(rect);
                }
                else
                {
                    float radius = Math.Min(24f, Math.Min(rect.Width, rect.Height) / 2f);
                    path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
                    path.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
                    path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
                    path.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
                    path.CloseFigure();
                }
                return path;

            case SelectionShape.Freehand:
                if (_freehand.Count < 2)
                {
                    return null;
                }
                var freePath = new GraphicsPath();
                freePath.AddLines(_freehand.ToArray());
                return freePath;

            case SelectionShape.Polygon:
                if (_polygon.Count < 2)
                {
                    return null;
                }
                var polyPath = new GraphicsPath();
                polyPath.AddLines(_polygon.ToArray());
                if (_polygon.Count >= 3)
                {
                    polyPath.CloseFigure();
                }
                return polyPath;

            default:
                return null;
        }
    }

    private void DrawSizeLabel(Graphics g)
    {
        Rectangle rect;
        if (_shape == SelectionShape.Freehand)
        {
            if (_freehand.Count < 2)
            {
                return;
            }
            rect = GetBounds(_freehand);
        }
        else if (_shape == SelectionShape.Polygon)
        {
            if (_polygon.Count < 2)
            {
                return;
            }
            rect = GetBounds(_polygon);
        }
        else
        {
            rect = Normalize(_start, _current);
        }
        if (rect.Width <= 2 || rect.Height <= 2)
        {
            return;
        }
        string text = rect.Width + " × " + rect.Height;
        int x = Math.Max(8, Math.Min(Width - 110, _current.X + 14));
        int y = Math.Max(8, Math.Min(Height - 32, _current.Y + 18));
        using var bg = new SolidBrush(Color.FromArgb(220, 15, 23, 42));
        using var font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);
        var size = TextRenderer.MeasureText(text, font);
        g.FillRectangle(bg, x - 6, y - 4, size.Width + 12, size.Height + 8);
        TextRenderer.DrawText(g, text, font, new Point(x, y), Color.White);
    }

    private void DrawInstructions(Graphics g)
    {
        string text = _shape switch
        {
            SelectionShape.Rectangle => "拖动选择区域 · 松开完成 · Esc 取消",
            SelectionShape.RoundedRectangle => "拖动选择圆角区域 · 松开完成 · Esc 取消",
            SelectionShape.Ellipse => "拖动选择椭圆区域 · 松开完成 · Esc 取消",
            SelectionShape.Freehand => "按住左键自由绘制 · 松开完成 · Esc 取消",
            SelectionShape.Polygon => "单击添加顶点 · 双击或 Enter 完成 · Backspace 回退 · Esc 取消",
            _ => "拖动选择区域 · Esc 取消"
        };
        using var font = new Font("Microsoft YaHei UI", 9f);
        var size = TextRenderer.MeasureText(text, font);
        int x = (Width - size.Width) / 2;
        using var bg = new SolidBrush(Color.FromArgb(225, 15, 23, 42));
        g.FillRectangle(bg, x - 14, 18, size.Width + 28, size.Height + 14);
        TextRenderer.DrawText(g, text, font, new Point(x, 25), Color.White);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }
        if (_shape == SelectionShape.Polygon)
        {
            _polygon.Add(e.Location);
            Invalidate();
            return;
        }
        if (_shape == SelectionShape.Freehand)
        {
            _freehand.Clear();
            _freehand.Add(e.Location);
            _dragging = true;
            Invalidate();
            return;
        }
        _start = e.Location;
        _current = e.Location;
        _dragging = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
        {
            return;
        }
        _current = e.Location;
        if (_shape == SelectionShape.Freehand)
        {
            _freehand.Add(e.Location);
        }
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragging)
        {
            _current = e.Location;
            _dragging = false;
            if (_shape == SelectionShape.Freehand)
            {
                if (_freehand.Count >= 3)
                {
                    AcceptSelection();
                }
                else
                {
                    _freehand.Clear();
                    Invalidate();
                }
                return;
            }
            var rect = Normalize(_start, _current);
            if (rect.Width >= 8 && rect.Height >= 8)
            {
                AcceptSelection();
            }
            else
            {
                _start = _current;
                Invalidate();
            }
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (_shape == SelectionShape.Polygon && _polygon.Count >= 3)
        {
            AcceptSelection();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
        else if (e.KeyCode == Keys.Enter && _shape == SelectionShape.Polygon && _polygon.Count >= 3)
        {
            AcceptSelection();
        }
        else if (e.KeyCode == Keys.Back && _shape == SelectionShape.Polygon && _polygon.Count > 0)
        {
            _polygon.RemoveAt(_polygon.Count - 1);
            Invalidate();
        }
    }

    private void AcceptSelection()
    {
        var path = GetSelectionPath();
        if (path == null)
        {
            return;
        }
        var bounds = Rectangle.Round(path.GetBounds());
        bounds.Intersect(new Rectangle(0, 0, _background.Width, _background.Height));
        if (bounds.Width < 1 || bounds.Height < 1)
        {
            path.Dispose();
            return;
        }

        Bitmap result;
        if (_shape == SelectionShape.Rectangle)
        {
            result = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(result);
            g.DrawImage(_background, new Rectangle(0, 0, bounds.Width, bounds.Height),
                bounds, GraphicsUnit.Pixel);
        }
        else
        {
            result = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(result);
            using var translated = new GraphicsPath();
            using var matrix = new Matrix();
            matrix.Translate(-bounds.X, -bounds.Y);
            translated.AddPath(path, true);
            translated.Transform(matrix);
            g.SetClip(translated);
            g.DrawImage(_background, new Rectangle(-bounds.X, -bounds.Y,
                _background.Width, _background.Height));
            g.ResetClip();
        }
        path.Dispose();
        _result = result;
        SelectedBounds = bounds;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static Rectangle Normalize(Point a, Point b)
    {
        return new Rectangle(
            Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
            Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    private static Rectangle GetBounds(List<Point> points)
    {
        int minX = points.Min(p => p.X);
        int minY = points.Min(p => p.Y);
        int maxX = points.Max(p => p.X);
        int maxY = points.Max(p => p.Y);
        return new Rectangle(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _background.Dispose();
        }
        base.Dispose(disposing);
    }
}
