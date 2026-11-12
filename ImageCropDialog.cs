using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// 证件照裁剪对话框：拖动鼠标框选需要保留的区域，返回原图像素坐标。
/// 用于在抠图、构图之前去掉照片中不需要的元素。
/// </summary>
internal sealed class ImageCropDialog : Form
{
    private readonly CropCanvas _canvas;
    private readonly Label _sizeLabel;
    private readonly PillButton _confirmButton;

    private ImageCropDialog(Bitmap image, Rectangle? initial)
    {
        Text = "裁剪原图";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.White;
        DoubleBuffered = true;
        KeyPreview = true;
        ClientSize = new Size(940, 700);
        MinimumSize = new Size(640, 500);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.White,
            Padding = new Padding(16, 12, 16, 12)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        var title = new Label
        {
            Text = "裁剪原图",
            Location = new Point(2, 2),
            Size = new Size(320, 26),
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            ForeColor = ShutdownTheme.Text,
            BackColor = Color.White
        };
        var hint = new Label
        {
            Text = "在照片上按住鼠标拖动，框选需要保留的区域；裁剪后再做抠图与构图。",
            Location = new Point(3, 28),
            Size = new Size(700, 22),
            Font = new Font("Microsoft YaHei UI", 8.2f),
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White
        };
        header.Controls.AddRange(new Control[] { title, hint });
        root.Controls.Add(header, 0, 0);

        _canvas = new CropCanvas(image, initial)
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8)
        };
        root.Controls.Add(_canvas, 0, 1);

        var footer = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        _sizeLabel = new Label
        {
            Text = "选区 —",
            Location = new Point(2, 0),
            Size = new Size(400, 40),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = ShutdownTheme.Muted,
            BackColor = Color.White,
            AutoEllipsis = true
        };
        var selectAllButton = new PillButton
        {
            Text = "全选",
            Size = new Size(84, 34),
            ButtonStyle = PillButtonStyle.Light
        };
        selectAllButton.Click += (_, _) => _canvas.SelectAll();
        var cancelButton = new PillButton
        {
            Text = "取消",
            Size = new Size(84, 34),
            ButtonStyle = PillButtonStyle.Light
        };
        cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        _confirmButton = new PillButton
        {
            Text = "确定裁剪",
            Size = new Size(104, 34),
            ButtonStyle = PillButtonStyle.Primary
        };
        _confirmButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        footer.Controls.AddRange(new Control[]
        {
            _sizeLabel, selectAllButton, cancelButton, _confirmButton
        });
        footer.Resize += (_, _) =>
        {
            int right = footer.ClientSize.Width;
            _confirmButton.Location = new Point(Math.Max(0, right - 104), 3);
            cancelButton.Location = new Point(Math.Max(0, _confirmButton.Left - 94), 3);
            selectAllButton.Location = new Point(Math.Max(0, cancelButton.Left - 94), 3);
            _sizeLabel.Width = Math.Max(120, selectAllButton.Left - 20);
        };
        root.Controls.Add(footer, 0, 2);

        _canvas.SelectionChanged += (_, _) => UpdateSizeLabel();
        UpdateSizeLabel();
    }

    public static Rectangle? Show(IWin32Window? owner, Bitmap image, Rectangle? initial)
    {
        using var dialog = new ImageCropDialog(image, initial);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog._canvas.Selection : null;
    }

    private void UpdateSizeLabel()
    {
        var selection = _canvas.Selection;
        _sizeLabel.Text = selection.Width < 1 || selection.Height < 1
            ? "选区 —"
            : "选区：" + selection.Width + " × " + selection.Height + " px"
            + "（原图 " + _canvas.ImageWidth + " × " + _canvas.ImageHeight + "）";
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Enter)
        {
            DialogResult = DialogResult.OK;
            Close();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            e.Handled = true;
        }
    }

    /// <summary>带缩略缓存的选择画布：拖动时只重绘遮罩，不对原图重复重采样。</summary>
    private sealed class CropCanvas : Control
    {
        private readonly Bitmap _image;
        private Rectangle _selection;
        private Rectangle _previous;
        private RectangleF _imageRect = RectangleF.Empty;
        private Bitmap? _displayCache;
        private bool _dragging;
        private Point _dragStart;

        public CropCanvas(Bitmap image, Rectangle? initial)
        {
            _image = image;
            _selection = initial.HasValue
                ? Rectangle.Intersect(initial.Value, new Rectangle(0, 0, image.Width, image.Height))
                : new Rectangle(0, 0, image.Width, image.Height);
            if (_selection.Width < 1 || _selection.Height < 1)
            {
                _selection = new Rectangle(0, 0, image.Width, image.Height);
            }
            _previous = _selection;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(241, 245, 249);
            Cursor = Cursors.Cross;
            TabStop = true;
        }

        public int ImageWidth => _image.Width;
        public int ImageHeight => _image.Height;
        public Rectangle Selection => _selection;
        public event EventHandler? SelectionChanged;

        public void SelectAll()
        {
            _selection = new Rectangle(0, 0, _image.Width, _image.Height);
            _previous = _selection;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        private RectangleF CalcImageRect()
        {
            float availableWidth = Math.Max(40, Width - 32);
            float availableHeight = Math.Max(40, Height - 32);
            float scale = Math.Min(availableWidth / _image.Width, availableHeight / _image.Height);
            float w = _image.Width * scale;
            float h = _image.Height * scale;
            return new RectangleF((Width - w) / 2f, (Height - h) / 2f, w, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var background = new SolidBrush(BackColor))
            {
                g.FillRectangle(background, ClientRectangle);
            }
            _imageRect = CalcImageRect();
            if (_imageRect.Width < 1 || _imageRect.Height < 1)
            {
                return;
            }
            EnsureDisplayCache();
            if (_displayCache != null)
            {
                g.SmoothingMode = SmoothingMode.None;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(_displayCache, (int)Math.Round(_imageRect.X),
                    (int)Math.Round(_imageRect.Y), _displayCache.Width, _displayCache.Height);
                g.SmoothingMode = SmoothingMode.AntiAlias;
            }

            var border = new Pen(Color.FromArgb(203, 213, 225), 1f);
            g.DrawRectangle(border, _imageRect.X, _imageRect.Y, _imageRect.Width, _imageRect.Height);
            border.Dispose();

            if (_selection.Width < 2 || _selection.Height < 2)
            {
                return;
            }
            var selectionRect = ImageToScreen(_selection);
            using (var dim = new SolidBrush(Color.FromArgb(136, 15, 23, 42)))
            {
                float left = _imageRect.Left;
                float top = _imageRect.Top;
                float right = _imageRect.Right;
                float bottom = _imageRect.Bottom;
                g.FillRectangle(dim, left, top, right - left,
                    Math.Max(0, selectionRect.Top - top));
                g.FillRectangle(dim, left, selectionRect.Bottom, right - left,
                    Math.Max(0, bottom - selectionRect.Bottom));
                g.FillRectangle(dim, left, selectionRect.Top,
                    Math.Max(0, selectionRect.Left - left), selectionRect.Height);
                g.FillRectangle(dim, selectionRect.Right, selectionRect.Top,
                    Math.Max(0, right - selectionRect.Right), selectionRect.Height);
            }

            using (var thirds = new Pen(Color.FromArgb(90, 255, 255, 255), 1f)
            {
                DashStyle = DashStyle.Dash
            })
            {
                for (int i = 1; i <= 2; i++)
                {
                    float x = selectionRect.Left + selectionRect.Width * i / 3f;
                    float y = selectionRect.Top + selectionRect.Height * i / 3f;
                    g.DrawLine(thirds, x, selectionRect.Top, x, selectionRect.Bottom);
                    g.DrawLine(thirds, selectionRect.Left, y, selectionRect.Right, y);
                }
            }
            using (var pen = new Pen(ShutdownTheme.Accent, 1.6f))
            {
                g.DrawRectangle(pen, selectionRect.X, selectionRect.Y,
                    selectionRect.Width, selectionRect.Height);
            }
            DrawHandle(g, selectionRect.Left, selectionRect.Top);
            DrawHandle(g, selectionRect.Right, selectionRect.Top);
            DrawHandle(g, selectionRect.Left, selectionRect.Bottom);
            DrawHandle(g, selectionRect.Right, selectionRect.Bottom);
            DrawHandle(g, selectionRect.Left + selectionRect.Width / 2f, selectionRect.Top);
            DrawHandle(g, selectionRect.Left + selectionRect.Width / 2f, selectionRect.Bottom);
            DrawHandle(g, selectionRect.Left, selectionRect.Top + selectionRect.Height / 2f);
            DrawHandle(g, selectionRect.Right, selectionRect.Top + selectionRect.Height / 2f);
        }

        private static void DrawHandle(Graphics g, float x, float y)
        {
            var rect = new RectangleF(x - 3.5f, y - 3.5f, 7f, 7f);
            using var brush = new SolidBrush(Color.White);
            g.FillRectangle(brush, rect);
            using var pen = new Pen(ShutdownTheme.Accent, 1f);
            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
        }

        private void EnsureDisplayCache()
        {
            int width = Math.Max(1, (int)Math.Round(_imageRect.Width));
            int height = Math.Max(1, (int)Math.Round(_imageRect.Height));
            if (_displayCache != null
                && _displayCache.Width == width && _displayCache.Height == height)
            {
                return;
            }
            _displayCache?.Dispose();
            var cache = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(cache))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(_image, new Rectangle(0, 0, width, height));
            }
            _displayCache = cache;
        }

        private RectangleF ImageToScreen(Rectangle rect)
        {
            float scaleX = _imageRect.Width / _image.Width;
            float scaleY = _imageRect.Height / _image.Height;
            return new RectangleF(
                _imageRect.X + rect.X * scaleX,
                _imageRect.Y + rect.Y * scaleY,
                rect.Width * scaleX,
                rect.Height * scaleY);
        }

        private Point? ToImagePoint(Point point)
        {
            if (_imageRect.Width < 1 || _imageRect.Height < 1)
            {
                return null;
            }
            float scaleX = _image.Width / _imageRect.Width;
            float scaleY = _image.Height / _imageRect.Height;
            int x = (int)Math.Round((point.X - _imageRect.X) * scaleX);
            int y = (int)Math.Round((point.Y - _imageRect.Y) * scaleY);
            return new Point(Math.Clamp(x, 0, _image.Width), Math.Clamp(y, 0, _image.Height));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !_imageRect.Contains(e.Location))
            {
                return;
            }
            var point = ToImagePoint(e.Location);
            if (point == null)
            {
                return;
            }
            Focus();
            _previous = _selection;
            _dragStart = point.Value;
            _dragging = true;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging)
            {
                return;
            }
            var point = ToImagePoint(e.Location);
            if (point == null)
            {
                return;
            }
            int left = Math.Min(_dragStart.X, point.Value.X);
            int top = Math.Min(_dragStart.Y, point.Value.Y);
            int width = Math.Abs(point.Value.X - _dragStart.X);
            int height = Math.Abs(point.Value.Y - _dragStart.Y);
            _selection = new Rectangle(left, top, width, height);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging)
            {
                return;
            }
            _dragging = false;
            Capture = false;
            if (_selection.Width < 8 || _selection.Height < 8)
            {
                _selection = _previous;
            }
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _displayCache?.Dispose();
                _displayCache = null;
            }
            base.Dispose(disposing);
        }
    }
}
