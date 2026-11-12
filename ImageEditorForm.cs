using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MiniMonitor;

public enum EditorTool
{
    Crop,
    Arrow,
    Rectangle,
    Ellipse,
    Text,
    Highlight,
    Blur,
    Mosaic,
    Erase,
    None
}

/// <summary>截图后的图像编辑器：标注、裁剪、水印、模糊/马赛克、擦除、边框、圆角。</summary>
public sealed class ImageEditorForm : Form
{
    private readonly AppConfig _config;
    private readonly Panel _header;
    private readonly EditorCanvas _canvas;
    private readonly FlowLayoutPanel _toolbar;
    private readonly Label _statusLabel;
    private readonly List<(EditorTool Tool, PillButton Button)> _toolButtons = new();
    private readonly List<Bitmap> _undo = new();
    private readonly List<Bitmap> _redo = new();
    private readonly CanvasWheelFilter _wheelFilter;
    private Bitmap _image;
    private EditorTool _tool = EditorTool.Rectangle;
    private Color _drawColor = Color.FromArgb(239, 68, 68);
    private float _lineWidth = 4f;
    private float _fontSize = 22f;
    private bool _modified;
    private bool _closeHover;

    public ImageEditorForm(Bitmap image, AppConfig config)
    {
        _image = image;
        _config = config;

        Text = "图像编辑 - MiniMonitor";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1000, 720);
        BackColor = ShutdownTheme.Background;
        DoubleBuffered = true;
        KeyPreview = true;
        ShowInTaskbar = true;

        _header = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(1000, 60),
            BackColor = Color.FromArgb(15, 118, 110)
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
            _closeHover = false;
            _header.Cursor = Cursors.Default;
            _header.Invalidate();
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

        _toolbar = new FlowLayoutPanel
        {
            Location = new Point(12, 66),
            Size = new Size(976, 88),
            BackColor = Color.White,
            Padding = new Padding(6),
            WrapContents = true,
            AutoScroll = false
        };
        _toolbar.Paint += (_, e) =>
        {
            using var pen = new Pen(ShutdownTheme.CardBorder);
            e.Graphics.DrawRectangle(pen, 0, 0, _toolbar.Width - 1, _toolbar.Height - 1);
        };
        Controls.Add(_toolbar);

        AddToolButton("裁剪", EditorTool.Crop);
        AddToolButton("箭头", EditorTool.Arrow);
        AddToolButton("矩形", EditorTool.Rectangle);
        AddToolButton("椭圆", EditorTool.Ellipse);
        AddToolButton("文字", EditorTool.Text);
        AddToolButton("高亮", EditorTool.Highlight);
        AddToolButton("模糊", EditorTool.Blur);
        AddToolButton("马赛克", EditorTool.Mosaic);
        AddToolButton("智能擦除", EditorTool.Erase);
        AddActionButton("水印", AddWatermark);
        AddActionButton("边框", AddBorder);
        AddActionButton("圆角", RoundCorners);
        AddActionButton("撤销", Undo);
        AddActionButton("重做", Redo);

        var colorButton = new Button
        {
            Text = "颜色",
            Size = new Size(58, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = _drawColor,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold),
            Margin = new Padding(4, 3, 2, 3)
        };
        colorButton.FlatAppearance.BorderColor = ShutdownTheme.CardBorder;
        colorButton.Click += (_, _) =>
        {
            using var dialog = new ColorDialog { Color = _drawColor, FullOpen = true };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _drawColor = dialog.Color;
                colorButton.BackColor = _drawColor;
                _canvas!.DrawColor = _drawColor;
            }
        };
        _toolbar.Controls.Add(colorButton);

        _toolbar.Controls.Add(MakeSmallLabel("线宽"));
        var widthInput = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 24,
            Value = 4,
            Width = 48,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Margin = new Padding(2, 3, 4, 3)
        };
        widthInput.ValueChanged += (_, _) =>
        {
            _lineWidth = (float)widthInput.Value;
            _canvas!.LineWidth = _lineWidth;
        };
        _toolbar.Controls.Add(widthInput);

        _toolbar.Controls.Add(MakeSmallLabel("字号"));
        var fontInput = new NumericUpDown
        {
            Minimum = 8,
            Maximum = 96,
            Value = 22,
            Width = 48,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Margin = new Padding(2, 3, 4, 3)
        };
        fontInput.ValueChanged += (_, _) => _fontSize = (float)fontInput.Value;
        _toolbar.Controls.Add(fontInput);

        _canvas = new EditorCanvas
        {
            Location = new Point(12, 160),
            Size = new Size(976, 492),
            Image = _image,
            DrawColor = _drawColor,
            LineWidth = _lineWidth
        };
        _canvas.EditRequested += Canvas_EditRequested;
        Controls.Add(_canvas);
        _wheelFilter = new CanvasWheelFilter(this);
        Application.AddMessageFilter(_wheelFilter);

        var actionPanel = new Panel
        {
            Location = new Point(12, 660),
            Size = new Size(976, 44),
            BackColor = Color.White
        };
        actionPanel.Paint += (_, e) =>
        {
            using var pen = new Pen(ShutdownTheme.CardBorder);
            e.Graphics.DrawRectangle(pen, 0, 0, actionPanel.Width - 1, actionPanel.Height - 1);
        };
        _statusLabel = MakeSmallLabel("拖动选择区域；工具在选区内生效");
        _statusLabel.Location = new Point(14, 12);
        _statusLabel.Size = new Size(430, 22);
        _statusLabel.AutoEllipsis = true;

        var ocrButton = MakeActionButton("OCR识别", 456);
        ocrButton.ButtonStyle = PillButtonStyle.Primary;
        ocrButton.Click += async (_, _) => await RunOcrAsync(ocrButton);
        var copyButton = MakeActionButton("复制", 540);
        copyButton.Click += (_, _) => CopyImage();
        var saveButton = MakeActionButton("保存", 624);
        saveButton.Click += (_, _) => SaveImage();
        var closeButton = MakeActionButton("关闭", 708);
        closeButton.Click += (_, _) => Close();
        var undoButton = MakeActionButton("撤销", 792);
        undoButton.Click += (_, _) => Undo();
        var redoButton = MakeActionButton("重做", 876);
        redoButton.Click += (_, _) => Redo();
        actionPanel.Controls.AddRange(new Control[]
        {
            _statusLabel, ocrButton, copyButton, saveButton, closeButton, undoButton, redoButton
        });
        Controls.Add(actionPanel);

        SelectTool(EditorTool.Rectangle);
        UpdateStatus();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
            else if (e.Control && e.KeyCode == Keys.Z)
            {
                Undo();
            }
            else if (e.Control && e.KeyCode == Keys.Y)
            {
                Redo();
            }
            else if (e.Control && e.KeyCode == Keys.S)
            {
                SaveImage();
            }
            else if (e.Control && e.KeyCode == Keys.C)
            {
                CopyImage();
            }
        };
    }

    private void AddToolButton(string text, EditorTool tool)
    {
        var button = new PillButton
        {
            Text = text,
            Size = new Size(66, 28),
            ButtonStyle = PillButtonStyle.Light,
            Margin = new Padding(3)
        };
        button.Click += (_, _) => SelectTool(tool);
        _toolButtons.Add((tool, button));
        _toolbar.Controls.Add(button);
    }

    private void AddActionButton(string text, Action action)
    {
        var button = new PillButton
        {
            Text = text,
            Size = new Size(66, 28),
            ButtonStyle = PillButtonStyle.Light,
            Margin = new Padding(3)
        };
        button.Click += (_, _) => action();
        _toolbar.Controls.Add(button);
    }

    private static Label MakeSmallLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 8f),
            ForeColor = ShutdownTheme.Muted,
            Margin = new Padding(6, 10, 2, 3)
        };
    }

    private static PillButton MakeActionButton(string text, int x)
    {
        var button = new PillButton
        {
            Text = text,
            Location = new Point(x, 7),
            Size = new Size(74, 30),
            ButtonStyle = PillButtonStyle.Light
        };
        return button;
    }

    private void SelectTool(EditorTool tool)
    {
        _tool = tool;
        _canvas.Tool = tool;
        foreach (var pair in _toolButtons)
        {
            pair.Button.ButtonStyle = pair.Tool == tool
                ? PillButtonStyle.Primary
                : PillButtonStyle.Light;
            pair.Button.Invalidate();
        }
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        string text = _tool switch
        {
            EditorTool.Crop => "拖动矩形裁剪图像",
            EditorTool.Arrow => "拖动画箭头",
            EditorTool.Rectangle => "拖动画矩形标注",
            EditorTool.Ellipse => "拖动画椭圆标注",
            EditorTool.Text => "单击放置文字，随后输入内容",
            EditorTool.Highlight => "拖动添加黄色高亮",
            EditorTool.Blur => "拖动区域应用模糊",
            EditorTool.Mosaic => "拖动区域应用马赛克",
            EditorTool.Erase => "拖动区域智能擦除（用周边平均色填充）",
            _ => "选择工具"
        };
        _statusLabel.Text = text + " · " + _image.Width + " × " + _image.Height
            + " · 滚轮缩放/右键平移"
            + (_modified ? " · 已修改" : "");
    }

    private void Canvas_EditRequested(EditorTool tool, Rectangle rect, Point start, Point end)
    {
        if (tool == EditorTool.Text)
        {
            AddText(start);
            return;
        }
        if (rect.Width < 3 || rect.Height < 3)
        {
            return;
        }
        try
        {
            switch (tool)
            {
                case EditorTool.Crop:
                    Crop(rect);
                    break;
                case EditorTool.Arrow:
                    DrawArrow(rect, start, end);
                    break;
                case EditorTool.Rectangle:
                    DrawShape(rect, false);
                    break;
                case EditorTool.Ellipse:
                    DrawShape(rect, true);
                    break;
                case EditorTool.Highlight:
                    Highlight(rect);
                    break;
                case EditorTool.Blur:
                    Blur(rect);
                    break;
                case EditorTool.Mosaic:
                    Mosaic(rect);
                    break;
                case EditorTool.Erase:
                    SmartErase(rect);
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "编辑失败：" + ex.Message, "图像编辑",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PushUndo()
    {
        _undo.Add(_image.Clone() as Bitmap ?? new Bitmap(_image));
        if (_undo.Count > 10)
        {
            var oldest = _undo[0];
            _undo.RemoveAt(0);
            oldest.Dispose();
        }
        foreach (var bmp in _redo)
        {
            bmp.Dispose();
        }
        _redo.Clear();
        _modified = true;
    }

    private Bitmap CreateWorkingCopy()
    {
        return _image.Clone() as Bitmap ?? new Bitmap(_image);
    }

    private void ReplaceImage(Bitmap bitmap, bool pushUndo = true)
    {
        if (pushUndo)
        {
            PushUndo();
        }
        var old = _image;
        _image = bitmap;
        _canvas.Image = _image;
        old.Dispose();
        _canvas.Invalidate();
        UpdateStatus();
    }

    private void DrawArrow(Rectangle rect, Point start, Point end)
    {
        var working = CreateWorkingCopy();
        using var g = Graphics.FromImage(working);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(_drawColor, _lineWidth);
        g.DrawLine(pen, start, end);
        double angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        float length = 16f * Math.Max(1f, _lineWidth / 4f);
        var p1 = new PointF(
            end.X - (float)(length * Math.Cos(angle - Math.PI / 6)),
            end.Y - (float)(length * Math.Sin(angle - Math.PI / 6)));
        var p2 = new PointF(
            end.X - (float)(length * Math.Cos(angle + Math.PI / 6)),
            end.Y - (float)(length * Math.Sin(angle + Math.PI / 6)));
        g.DrawLine(pen, end, p1);
        g.DrawLine(pen, end, p2);
        ReplaceImage(working);
    }

    private void DrawShape(Rectangle rect, bool ellipse)
    {
        var working = CreateWorkingCopy();
        using var g = Graphics.FromImage(working);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(_drawColor, _lineWidth);
        if (ellipse)
        {
            g.DrawEllipse(pen, rect);
        }
        else
        {
            g.DrawRectangle(pen, rect);
        }
        ReplaceImage(working);
    }

    private void Highlight(Rectangle rect)
    {
        var working = CreateWorkingCopy();
        using var g = Graphics.FromImage(working);
        using var brush = new SolidBrush(Color.FromArgb(110, 255, 214, 64));
        g.FillRectangle(brush, rect);
        ReplaceImage(working);
    }

    private void AddText(Point point)
    {
        using var dialog = new TextEntryForm();
        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.Value))
        {
            return;
        }
        var working = CreateWorkingCopy();
        using var g = Graphics.FromImage(working);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Microsoft YaHei UI", _fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var shadow = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
        using var brush = new SolidBrush(_drawColor);
        var textPoint = new PointF(point.X, point.Y);
        g.DrawString(dialog.Value, font, shadow, textPoint.X + 2, textPoint.Y + 2);
        g.DrawString(dialog.Value, font, brush, textPoint);
        ReplaceImage(working);
    }

    private void Crop(Rectangle rect)
    {
        rect.Intersect(new Rectangle(0, 0, _image.Width, _image.Height));
        if (rect.Width < 4 || rect.Height < 4)
        {
            return;
        }
        var cropped = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(cropped);
        g.DrawImage(_image, new Rectangle(0, 0, rect.Width, rect.Height),
            rect, GraphicsUnit.Pixel);
        ReplaceImage(cropped);
        _canvas.ResetView();
    }

    private void Blur(Rectangle rect)
    {
        rect.Intersect(new Rectangle(0, 0, _image.Width, _image.Height));
        if (rect.Width < 2 || rect.Height < 2)
        {
            return;
        }
        int radius = 5;
        var working = CreateWorkingCopy();
        var data = working.LockBits(new Rectangle(0, 0, working.Width, working.Height),
            ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            int width = working.Width;
            var bytes = new byte[Math.Abs(stride) * working.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var source = bytes.ToArray();
            for (int y = rect.Top; y < rect.Bottom; y++)
            {
                for (int x = rect.Left; x < rect.Right; x++)
                {
                    int r = 0, g = 0, b = 0, count = 0;
                    for (int oy = -radius; oy <= radius; oy++)
                    {
                        for (int ox = -radius; ox <= radius; ox++)
                        {
                            int sx = Math.Clamp(x + ox, rect.Left, rect.Right - 1);
                            int sy = Math.Clamp(y + oy, rect.Top, rect.Bottom - 1);
                            int offset = sy * stride + sx * 4;
                            b += source[offset];
                            g += source[offset + 1];
                            r += source[offset + 2];
                            count++;
                        }
                    }
                    int dest = y * stride + x * 4;
                    bytes[dest] = (byte)(b / count);
                    bytes[dest + 1] = (byte)(g / count);
                    bytes[dest + 2] = (byte)(r / count);
                }
            }
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            working.UnlockBits(data);
        }
        ReplaceImage(working);
    }

    private void Mosaic(Rectangle rect)
    {
        rect.Intersect(new Rectangle(0, 0, _image.Width, _image.Height));
        if (rect.Width < 4 || rect.Height < 4)
        {
            return;
        }
        int cell = 10;
        var working = CreateWorkingCopy();
        using var g = Graphics.FromImage(working);
        for (int y = rect.Top; y < rect.Bottom; y += cell)
        {
            for (int x = rect.Left; x < rect.Right; x += cell)
            {
                int w = Math.Min(cell, rect.Right - x);
                int h = Math.Min(cell, rect.Bottom - y);
                var sample = _image.GetPixel(
                    Math.Min(_image.Width - 1, x + w / 2),
                    Math.Min(_image.Height - 1, y + h / 2));
                using var brush = new SolidBrush(sample);
                g.FillRectangle(brush, x, y, w, h);
            }
        }
        ReplaceImage(working);
    }

    private void SmartErase(Rectangle rect)
    {
        rect.Intersect(new Rectangle(0, 0, _image.Width, _image.Height));
        if (rect.Width < 2 || rect.Height < 2)
        {
            return;
        }
        int r = 0, g = 0, b = 0, count = 0;
        for (int x = Math.Max(0, rect.Left - 1); x <= Math.Min(_image.Width - 1, rect.Right); x++)
        {
            for (int y = Math.Max(0, rect.Top - 1); y <= Math.Min(_image.Height - 1, rect.Bottom); y++)
            {
                if (rect.Contains(x, y))
                {
                    continue;
                }
                var c = _image.GetPixel(x, y);
                r += c.R; g += c.G; b += c.B; count++;
            }
        }
        Color fill = count > 0
            ? Color.FromArgb(r / count, g / count, b / count)
            : Color.White;
        var working = CreateWorkingCopy();
        using var graphics = Graphics.FromImage(working);
        using var brush = new SolidBrush(fill);
        graphics.FillRectangle(brush, rect);
        ReplaceImage(working);
    }

    private void AddWatermark()
    {
        string text = string.IsNullOrWhiteSpace(_config.WatermarkText)
            ? "MiniMonitor"
            : _config.WatermarkText;
        var working = CreateWorkingCopy();
        using var g = Graphics.FromImage(working);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        string family = string.IsNullOrWhiteSpace(_config.WatermarkFontFamily)
            ? WatermarkStyle.DefaultFontFamily
            : _config.WatermarkFontFamily;
        float fontSize = _config.WatermarkFontSize > 0
            ? _config.WatermarkFontSize
            : Math.Max(16f, working.Width / 40f); // 0 = 沿用原有“按图片宽度”的字号
        FontStyle fontStyle = _config.WatermarkBold ? FontStyle.Bold : FontStyle.Regular;
        using var font = CreateWatermarkFont(family, fontSize, fontStyle);
        var size = TextRenderer.MeasureText(text, font);
        int margin = 24;
        int x = _config.WatermarkPosition switch
        {
            "BottomLeft" => margin,
            "TopRight" => working.Width - size.Width - margin,
            "TopLeft" => margin,
            "Center" => (working.Width - size.Width) / 2,
            _ => working.Width - size.Width - margin
        };
        int y = _config.WatermarkPosition switch
        {
            "TopRight" or "TopLeft" => margin,
            "Center" => (working.Height - size.Height) / 2,
            _ => working.Height - size.Height - margin
        };
        int alpha = (int)(Math.Clamp(_config.WatermarkOpacity, 0, 1) * 255);
        Color color = WatermarkStyle.ParseColor(_config.WatermarkColor);
        using var shadow = new SolidBrush(Color.FromArgb(Math.Min(180, alpha), 0, 0, 0));
        using var brush = new SolidBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        g.DrawString(text, font, shadow, x + 2, y + 2);
        g.DrawString(text, font, brush, x, y);
        ReplaceImage(working);
    }

    private static Font CreateWatermarkFont(string family, float size, FontStyle style)
    {
        try
        {
            return new Font(family, size, style, GraphicsUnit.Point);
        }
        catch
        {
            try
            {
                return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
            }
            catch
            {
                return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point);
            }
        }
    }

    private void AddBorder()
    {
        var working = CreateWorkingCopy();
        using var g = Graphics.FromImage(working);
        using var pen = new Pen(_drawColor, Math.Max(2, _lineWidth));
        int inset = (int)(pen.Width / 2f);
        g.DrawRectangle(pen, inset, inset,
            Math.Max(1, working.Width - inset * 2 - 1),
            Math.Max(1, working.Height - inset * 2 - 1));
        ReplaceImage(working);
    }

    private void RoundCorners()
    {
        int radius = Math.Min(32, Math.Min(_image.Width, _image.Height) / 8);
        var working = new Bitmap(_image.Width, _image.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(working);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = ShutdownTheme.RoundRect(
            new RectangleF(0, 0, _image.Width, _image.Height), radius);
        g.SetClip(path);
        g.DrawImage(_image, 0, 0);
        ReplaceImage(working);
    }

    private void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }
        _redo.Add(_image.Clone() as Bitmap ?? new Bitmap(_image));
        var old = _image;
        _image = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _canvas.Image = _image;
        old.Dispose();
        _canvas.ResetView();
        _modified = true;
        UpdateStatus();
    }

    private void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }
        _undo.Add(_image.Clone() as Bitmap ?? new Bitmap(_image));
        var old = _image;
        _image = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _canvas.Image = _image;
        old.Dispose();
        _canvas.ResetView();
        _modified = true;
        UpdateStatus();
    }

    private async Task RunOcrAsync(PillButton button)
    {
        if (!button.Enabled || _image == null)
        {
            return;
        }

        Bitmap? snapshot = null;
        button.Enabled = false;
        _statusLabel.Text = "OCR 识别中…";
        try
        {
            snapshot = new Bitmap(_image);
            OcrRecognitionResult result = await WindowsOcrService.RecognizeAsync(snapshot);
            if (IsDisposed)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(result.Text))
            {
                MessageBox.Show(this, "未识别到文字。\n可以尝试放大截图后重新 OCR。", "OCR 识别",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var form = new OcrResultForm(result.Text, result.Language, result.Elapsed);
            form.ShowDialog(this);
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                MessageBox.Show(this, "OCR 识别失败：" + ex.Message, "OCR 识别",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            snapshot?.Dispose();
            if (!IsDisposed)
            {
                button.Enabled = true;
                UpdateStatus();
            }
        }
    }

    private void CopyImage()
    {
        try
        {
            Clipboard.SetImage(_image);
            _statusLabel.Text = "已复制到剪贴板 · " + _image.Width + " × " + _image.Height;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "复制失败：" + ex.Message, "图像编辑",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveImage()
    {
        string defaultDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "MiniMonitor Screenshots");
        Directory.CreateDirectory(defaultDir);
        using var dialog = new SaveFileDialog
        {
            Title = "保存截图",
            InitialDirectory = defaultDir,
            FileName = "截图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png",
            Filter = "PNG 图片|*.png|JPEG 图片|*.jpg",
            DefaultExt = "png"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            if (dialog.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                using var flattened = new Bitmap(_image.Width, _image.Height, PixelFormat.Format24bppRgb);
                using var g = Graphics.FromImage(flattened);
                g.Clear(Color.White);
                g.DrawImage(_image, 0, 0);
                flattened.Save(dialog.FileName, ImageFormat.Jpeg);
            }
            else
            {
                _image.Save(dialog.FileName, ImageFormat.Png);
            }
            _statusLabel.Text = "已保存：" + dialog.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存失败：" + ex.Message, "图像编辑",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private Rectangle CloseRect
    {
        get
        {
            float s = _header.Height / 60f;
            return new Rectangle((int)(_header.Width - 46f * s), (int)(12f * s),
                (int)(30f * s), (int)(30f * s));
        }
    }

    private void PaintHeader(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new LinearGradientBrush(_header.ClientRectangle,
            Color.FromArgb(15, 118, 110), Color.FromArgb(13, 148, 136),
            LinearGradientMode.Horizontal);
        g.FillRectangle(brush, _header.ClientRectangle);
        using var titleFont = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold);
        TextRenderer.DrawText(g, "图像编辑", titleFont,
            new Rectangle(24, 0, 400, _header.Height), Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        using var subFont = new Font("Microsoft YaHei UI", 8.5f);
        TextRenderer.DrawText(g, "标注 · 裁剪 · 水印 · 模糊 · 马赛克 · 边框",
            subFont, new Rectangle(130, 0, 500, _header.Height),
            Color.FromArgb(220, 255, 255), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        var close = CloseRect;
        if (_closeHover)
        {
            using var hover = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
            g.FillEllipse(hover, close);
        }
        using var pen = new Pen(Color.FromArgb(230, 255, 255, 255), 1.8f);
        g.DrawLine(pen, close.Left + 10, close.Top + 10, close.Right - 10, close.Bottom - 10);
        g.DrawLine(pen, close.Right - 10, close.Top + 10, close.Left + 10, close.Bottom - 10);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        float scale = Height / 720f;
        using var path = ShutdownTheme.RoundRect(new RectangleF(0, 0, Width, Height), 18f * scale);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
        if (_canvas != null)
        {
            _canvas.Size = new Size(Math.Max(100, ClientSize.Width - 24),
                Math.Max(100, ClientSize.Height - 228));
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Application.RemoveMessageFilter(_wheelFilter);
            _canvas.Image = null;
            _image.Dispose();
            foreach (var bmp in _undo)
            {
                bmp.Dispose();
            }
            foreach (var bmp in _redo)
            {
                bmp.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    /// <summary>把鼠标所在位置（画布区域）的滚轮消息转发给画布，实现悬停缩放。</summary>
    private sealed class CanvasWheelFilter : IMessageFilter
    {
        private const int WmMouseWheel = 0x020A;
        private readonly ImageEditorForm _owner;

        public CanvasWheelFilter(ImageEditorForm owner)
        {
            _owner = owner;
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WmMouseWheel || _owner.IsDisposed || !_owner.Visible)
            {
                return false;
            }
            var canvas = _owner._canvas;
            if (canvas?.Image == null || !canvas.IsHandleCreated)
            {
                return false;
            }
            Point screenPoint = Cursor.Position;
            if (!canvas.RectangleToScreen(canvas.ClientRectangle).Contains(screenPoint))
            {
                return false;
            }
            int delta = unchecked((short)((long)m.WParam >> 16));
            canvas.ZoomAt(canvas.PointToClient(screenPoint), delta);
            return true;
        }
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

/// <summary>图像编辑画布：显示图像，收集拖拽并绘制预览。</summary>
internal sealed class EditorCanvas : Control
{
    private const float MinScale = 0.05f;
    private const float MaxScale = 16f;

    private Bitmap? _image;
    private Point _start;
    private Point _current;
    private bool _dragging;
    private bool _panning;
    private Point _panStart;
    private PointF _panOrigin;
    private float _scale = 1f;
    private PointF _offset;
    public EditorTool Tool { get; set; }
    public Color DrawColor { get; set; } = Color.Red;
    public float LineWidth { get; set; } = 4f;
    public event Action<EditorTool, Rectangle, Point, Point>? EditRequested;

    public Bitmap? Image
    {
        get => _image;
        set
        {
            _image = value;
            ResetView();
            Invalidate();
        }
    }

    public EditorCanvas()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(241, 245, 249);
        Cursor = Cursors.Cross;
        TabStop = true;
    }

    public void ResetView()
    {
        if (_image == null || Width <= 4 || Height <= 4)
        {
            _scale = 1f;
            _offset = PointF.Empty;
            return;
        }
        _scale = Math.Min((Width - 24f) / _image.Width, (Height - 24f) / _image.Height);
        _scale = Math.Max(0.02f, Math.Min(1f, _scale));
        _offset = new PointF(
            (Width - _image.Width * _scale) / 2f,
            (Height - _image.Height * _scale) / 2f);
    }

    private PointF ToImage(Point point)
    {
        return new PointF(
            (point.X - _offset.X) / _scale,
            (point.Y - _offset.Y) / _scale);
    }

    /// <summary>以指定画布位置为中心缩放；delta 为滚轮增量（120 为一格）。</summary>
    public void ZoomAt(Point location, int delta)
    {
        if (_image == null || delta == 0)
        {
            return;
        }
        float factor = (float)Math.Pow(1.15, delta / 120.0);
        float newScale = Math.Clamp(_scale * factor, MinScale, MaxScale);
        if (Math.Abs(newScale - _scale) < 0.0001f)
        {
            return;
        }
        // 保持鼠标指向的图像内容在缩放前后不动
        float imageX = (location.X - _offset.X) / _scale;
        float imageY = (location.Y - _offset.Y) / _scale;
        _scale = newScale;
        _offset = new PointF(
            location.X - imageX * _scale,
            location.Y - imageY * _scale);
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ZoomAt(e.Location, e.Delta);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        if (_image == null)
        {
            return;
        }
        DrawCheckerboard(g);
        var destination = new RectangleF(
            _offset.X, _offset.Y,
            _image.Width * _scale, _image.Height * _scale);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(_image, destination);
        if (_dragging)
        {
            DrawPreview(g);
        }
    }

    private void DrawCheckerboard(Graphics g)
    {
        if (_image == null)
        {
            return;
        }
        int cell = 12;
        var imageRect = new RectangleF(_offset.X, _offset.Y,
            _image.Width * _scale, _image.Height * _scale);
        for (int y = 0; y < imageRect.Height; y += cell)
        {
            for (int x = 0; x < imageRect.Width; x += cell)
            {
                using var brush = new SolidBrush(
                    ((x / cell + y / cell) % 2 == 0)
                        ? Color.FromArgb(235, 239, 245)
                        : Color.White);
                g.FillRectangle(brush, imageRect.X + x, imageRect.Y + y,
                    Math.Min(cell, imageRect.Width - x),
                    Math.Min(cell, imageRect.Height - y));
            }
        }
    }

    private void DrawPreview(Graphics g)
    {
        var a = ToImage(_start);
        var b = ToImage(_current);
        var rect = Rectangle.FromLTRB(
            (int)Math.Min(a.X, b.X), (int)Math.Min(a.Y, b.Y),
            (int)Math.Max(a.X, b.X), (int)Math.Max(a.Y, b.Y));
        var screenRect = new RectangleF(
            _offset.X + rect.X * _scale, _offset.Y + rect.Y * _scale,
            rect.Width * _scale, rect.Height * _scale);
        switch (Tool)
        {
            case EditorTool.Arrow:
                using (var pen = new Pen(DrawColor, Math.Max(1f, LineWidth * _scale)))
                {
                    g.DrawLine(pen, _start, _current);
                }
                break;
            case EditorTool.Rectangle:
            case EditorTool.Crop:
            case EditorTool.Blur:
            case EditorTool.Mosaic:
            case EditorTool.Erase:
                using (var pen = new Pen(Tool == EditorTool.Crop
                    ? Color.FromArgb(79, 70, 229) : DrawColor,
                    Math.Max(1f, 1.5f * _scale)))
                {
                    pen.DashStyle = Tool == EditorTool.Crop
                        ? DashStyle.Dash : DashStyle.Solid;
                    g.DrawRectangle(pen, screenRect.X, screenRect.Y,
                        screenRect.Width, screenRect.Height);
                }
                break;
            case EditorTool.Ellipse:
                using (var pen = new Pen(DrawColor, Math.Max(1f, 1.5f * _scale)))
                {
                    g.DrawEllipse(pen, screenRect.X, screenRect.Y,
                        screenRect.Width, screenRect.Height);
                }
                break;
            case EditorTool.Highlight:
                using (var brush = new SolidBrush(Color.FromArgb(100, 255, 214, 64)))
                {
                    g.FillRectangle(brush, screenRect);
                }
                break;
            case EditorTool.Text:
                using (var pen = new Pen(Color.FromArgb(79, 70, 229), 1f))
                {
                    g.DrawLine(pen, _start.X - 6, _start.Y, _start.X + 6, _start.Y);
                    g.DrawLine(pen, _start.X, _start.Y - 6, _start.X, _start.Y + 6);
                }
                break;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_image == null)
        {
            return;
        }
        if (e.Button == MouseButtons.Right)
        {
            if (_dragging)
            {
                return;
            }
            // 右键拖动平移图像
            _panning = true;
            _panStart = e.Location;
            _panOrigin = _offset;
            Capture = true;
            Cursor = Cursors.Hand;
            Invalidate();
            return;
        }
        if (e.Button != MouseButtons.Left || _panning)
        {
            return;
        }
        PointF point = ToImage(e.Location);
        if (point.X < 0 || point.Y < 0 || point.X >= _image.Width || point.Y >= _image.Height)
        {
            return;
        }
        _start = e.Location;
        _current = e.Location;
        _dragging = true;
        Capture = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_panning)
        {
            _offset = new PointF(
                _panOrigin.X + e.X - _panStart.X,
                _panOrigin.Y + e.Y - _panStart.Y);
            Invalidate();
            return;
        }
        if (_dragging)
        {
            _current = e.Location;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_panning && e.Button == MouseButtons.Right)
        {
            _panning = false;
            Capture = false;
            Cursor = Cursors.Cross;
            return;
        }
        if (!_dragging || _image == null)
        {
            return;
        }
        _current = e.Location;
        _dragging = false;
        Capture = false;
        var a = ToImage(_start);
        var b = ToImage(_current);
        var rect = Rectangle.FromLTRB(
            (int)Math.Min(a.X, b.X), (int)Math.Min(a.Y, b.Y),
            (int)Math.Max(a.X, b.X), (int)Math.Max(a.Y, b.Y));
        var start = new Point((int)a.X, (int)a.Y);
        var end = new Point((int)b.X, (int)b.Y);
        EditRequested?.Invoke(Tool, rect, start, end);
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ResetView();
    }
}

/// <summary>文字输入对话框。</summary>
internal sealed class TextEntryForm : Form
{
    private readonly TextBox _textBox;
    public string Value => _textBox.Text;

    public TextEntryForm()
    {
        Text = "添加文字";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 150);
        BackColor = Color.White;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        var label = new Label
        {
            Text = "输入要添加的文字：",
            Location = new Point(18, 16),
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9f)
        };
        _textBox = new TextBox
        {
            Location = new Point(18, 44),
            Size = new Size(384, 28),
            Font = new Font("Microsoft YaHei UI", 9.5f)
        };
        var ok = new Button
        {
            Text = "确定",
            DialogResult = DialogResult.OK,
            Location = new Point(230, 92),
            Size = new Size(80, 30),
            BackColor = Color.FromArgb(79, 70, 229),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        var cancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Location = new Point(322, 92),
            Size = new Size(80, 30)
        };
        ok.FlatAppearance.BorderSize = 0;
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.AddRange(new Control[] { label, _textBox, ok, cancel });
    }
}
