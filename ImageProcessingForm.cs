using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// 图片处理管理界面：默认不打开，从右键菜单进入。
/// 包含单张处理、批量处理、照片打印、EXIF 查看与剔除四个选项卡，窗口支持最大化/还原。
/// </summary>
public sealed class ImageProcessingForm : Form
{
    private readonly Panel _header;
    private readonly TabControl _tabs;
    private readonly Font _headerTitleFont;
    private readonly Font _headerSubFont;
    private bool _closeHover;
    private bool _maxHover;

    public ImageProcessingForm()
    {
        Text = "图片处理 - MiniMonitor";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        var workArea = Screen.PrimaryScreen?.WorkingArea
            ?? new Rectangle(0, 0, 1200, 800);
        ClientSize = new Size(
            Math.Min(1200, Math.Max(1000, workArea.Width - 40)),
            Math.Min(800, Math.Max(680, workArea.Height - 40)));
        MinimumSize = new Size(1000, 680);
        BackColor = ShutdownTheme.Background;
        DoubleBuffered = true;
        KeyPreview = true;
        ShowInTaskbar = true;
        MaximizeBox = true;

        _headerTitleFont = new Font("Microsoft YaHei UI", 19f, FontStyle.Bold);
        _headerSubFont = new Font("Microsoft YaHei UI", 9.5f);

        _header = new Panel
        {
            Dock = DockStyle.Top,
            Location = new Point(0, 0),
            Size = new Size(1200, 104),
            BackColor = Color.FromArgb(49, 46, 129)
        };
        _header.Paint += PaintHeader;
        _header.MouseMove += (_, e) =>
        {
            bool close = CloseRect.Contains(e.Location);
            bool max = MaximizeRect.Contains(e.Location);
            if (close != _closeHover || max != _maxHover)
            {
                _closeHover = close;
                _maxHover = max;
                _header.Cursor = close || max ? Cursors.Hand : Cursors.Default;
                _header.Invalidate();
            }
        };
        _header.MouseLeave += (_, _) =>
        {
            if (_closeHover || _maxHover)
            {
                _closeHover = false;
                _maxHover = false;
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
            else if (MaximizeRect.Contains(e.Location))
            {
                ToggleMaximize();
            }
        };
        _header.MouseDoubleClick += (_, e) =>
        {
            if (!CloseRect.Contains(e.Location) && !MaximizeRect.Contains(e.Location))
            {
                ToggleMaximize();
            }
        };
        NativeWindowDrag.Attach(this, _header,
            p => CloseRect.Contains(p) || MaximizeRect.Contains(p)
                || WindowState == FormWindowState.Maximized);

        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            Padding = new Point(18, 8)
        };

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 10, 16, 14),
            BackColor = ShutdownTheme.Background
        };
        content.Controls.Add(_tabs);
        // Dock 布局按 z-order 反向处理：先加内容（Fill），后加标题栏（Top），
        // 标题栏才能压在内容上方并占住顶部 104px。
        Controls.Add(content);
        Controls.Add(_header);

        AddTab("单张照片处理", new ImageSingleTab());
        AddTab("证件照处理", new ImageIdPhotoTab());
        AddTab("批量处理", new ImageBatchTab());
        AddTab("打印处理", new ImagePrintTab());
        AddTab("EXIF 信息查看与剔除", new ImageExifTab());
    }

    private void AddTab(string title, Control content)
    {
        var page = new TabPage(title)
        {
            BackColor = Color.White,
            Padding = new Padding(0)
        };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        _tabs.TabPages.Add(page);
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

    private Rectangle MaximizeRect
    {
        get
        {
            float s = _header.Height / 104f;
            return new Rectangle(
                (int)(_header.Width - 96f * s),
                (int)(14f * s),
                (int)(34f * s),
                (int)(34f * s));
        }
    }

    private void ToggleMaximize()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            WindowState = FormWindowState.Normal;
        }
        else
        {
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
            WindowState = FormWindowState.Maximized;
        }
        _header.Invalidate();
    }

    private void PaintHeader(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        float s = _header.Height / 104f;

        using (var brush = new LinearGradientBrush(_header.ClientRectangle,
            Color.FromArgb(49, 46, 129), Color.FromArgb(37, 99, 235),
            LinearGradientMode.Horizontal))
        {
            g.FillRectangle(brush, _header.ClientRectangle);
        }
        using (var glow = new SolidBrush(Color.FromArgb(20, 255, 255, 255)))
        {
            g.FillEllipse(glow, _header.Width - 300, -130, 380, 380);
            g.FillEllipse(glow, _header.Width - 120, 20, 160, 160);
        }

        TextRenderer.DrawText(g, "图片处理", _headerTitleFont,
            new Rectangle((int)(28f * s), (int)(12f * s),
                (int)(_header.Width - 220f * s), (int)(44f * s)),
            Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "单张处理 · 批量转换 · 照片打印 · EXIF 查看与剔除", _headerSubFont,
            new Rectangle((int)(30f * s), (int)(54f * s),
                (int)(_header.Width - 240f * s), (int)(26f * s)),
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

        var maximize = MaximizeRect;
        if (_maxHover)
        {
            using var hover = new SolidBrush(Color.FromArgb(42, 255, 255, 255));
            g.FillEllipse(hover, maximize);
        }
        using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), 1.8f * s))
        {
            int cx = maximize.Left + maximize.Width / 2;
            int cy = maximize.Top + maximize.Height / 2;
            if (WindowState == FormWindowState.Maximized)
            {
                g.DrawRectangle(pen, cx - 8, cy - 6, 11, 9);
                g.DrawRectangle(pen, cx - 3, cy - 1, 11, 9);
            }
            else
            {
                g.DrawRectangle(pen, cx - 7, cy - 6, 15, 12);
            }
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_header == null || _header.IsDisposed)
        {
            return;
        }
        _header.Size = new Size(ClientSize.Width, 104);
        float radius = WindowState == FormWindowState.Maximized
            ? 0f
            : 18f * (Height / 800f);
        using var path = ShutdownTheme.RoundRect(new RectangleF(0, 0, Width, Height), radius);
        var old = Region;
        Region = new Region(path);
        old?.Dispose();
        _header.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
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
