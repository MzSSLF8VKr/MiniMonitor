using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MiniMonitor.XShell;

/// <summary>XShell 风格的多标签容器：圆角活动标签、蓝色顶线、渐变标签条与关闭按钮。</summary>
internal sealed class XShellTabControl : TabControl
{
    private int _hoverClose = -1;
    private int _hoverTab = -1;
    private readonly HashSet<TabPage> _recording = new();

    public event Action<TabPage>? TabCloseRequested;
    public event Action<TabPage, Point>? TabRightClicked;

    public XShellTabControl()
    {
        // TabControl 必须保留原生绘制，否则系统不会发送 DrawItem 通知；
        // Opaque 关闭系统背景擦除，再由 WM_PAINT 后的自绘覆盖整个标签条，
        // 这样切换会话标签时不会先闪一下系统白色底。
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.Opaque, true);
        DrawMode = TabDrawMode.OwnerDrawFixed;
        Appearance = TabAppearance.FlatButtons;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(200, 32);
        Padding = new Point(10, 5);
        Font = XShellTheme.Ui(9f);
        BackColor = XShellTheme.Panel;
        ForeColor = XShellTheme.Text;
    }

    /// <summary>设置某个标签是否正在记录日志，显示红色 REC 标识。</summary>
    public void SetRecording(TabPage page, bool recording)
    {
        if (page == null)
        {
            return;
        }
        bool changed = recording ? _recording.Add(page) : _recording.Remove(page);
        if (changed)
        {
            Invalidate();
        }
    }

    public bool IsRecording(TabPage page) => _recording.Contains(page);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        XShellWindowTheme.ApplyControlTheme(this);
    }

    public TabPage AddPage(Control content, string title, int imageIndex = 0)
    {
        var page = new BufferedTabPage(title)
        {
            BackColor = XShellTheme.Panel,
            Padding = new Padding(0),
            ImageIndex = imageIndex,
            ToolTipText = title,
            BorderStyle = BorderStyle.None,
            UseVisualStyleBackColor = false
        };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        TabPages.Add(page);
        SelectedTab = page;
        return page;
    }

    public void ClosePage(TabPage page)
    {
        if (page == null || !TabPages.Contains(page))
        {
            return;
        }
        int index = TabPages.IndexOf(page);
        _recording.Remove(page);
        TabPages.Remove(page);
        page.Dispose();
        if (TabPages.Count > 0)
        {
            SelectedIndex = Math.Min(index, TabPages.Count - 1);
        }
        // 关闭最后一个标签后强制重绘，确保顶部标签条与页面区立刻切换为深色底。
        Invalidate();
    }

    /// <summary>双缓冲标签页，减少切换标签时的页面区闪烁。</summary>
    private sealed class BufferedTabPage : TabPage
    {
        public BufferedTabPage(string text) : base(text)
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.AllPaintingInWmPaint, true);
        }
    }

    private Rectangle GetCloseRect(Rectangle tab)
    {
        return new Rectangle(tab.Right - 24, tab.Top + (tab.Height - 16) / 2, 16, 16);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= TabPages.Count)
        {
            return;
        }
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        DrawTab(e.Graphics, e.Index);
    }

    /// <summary>标签条的实际高度：兼容多行标签，避免第二行区域露出系统底色。</summary>
    private int StripHeight()
    {
        int height = ItemSize.Height + Padding.Y + 8;
        if (TabPages.Count > 0)
        {
            height = Math.Max(height, GetTabRect(TabPages.Count - 1).Bottom + 8);
        }
        return Math.Min(ClientSize.Height, height);
    }

    /// <summary>绘制整个标签条背景，覆盖原生控件残留的浅色底。</summary>
    private void DrawTabStrip(Graphics g)
    {
        int height = StripHeight();
        if (height <= 0 || ClientSize.Width <= 0)
        {
            return;
        }
        var rect = new Rectangle(0, 0, ClientSize.Width, height);
        using (var brush = new LinearGradientBrush(rect,
            XShellTheme.TabStrip, XShellTheme.Background, LinearGradientMode.Vertical))
        {
            g.FillRectangle(brush, rect);
        }
        using var line = new Pen(Color.FromArgb(120, XShellTheme.Accent));
        g.DrawLine(line, 0, height - 1, ClientSize.Width, height - 1);
    }

    private void DrawTab(Graphics g, int index)
    {
        var tab = GetTabRect(index);
        bool selected = SelectedIndex == index;
        bool hover = _hoverTab == index;
        bool recording = _recording.Contains(TabPages[index]);
        Color recordColor = Color.FromArgb(232, 48, 58);

        var body = new Rectangle(tab.Left + 2, tab.Top + 3, tab.Width - 5, tab.Height + 4);
        using var path = XShellTheme.RoundedRect(body, 9);
        Color from = selected ? XShellTheme.TabActive
            : hover ? XShellTheme.Lighten(XShellTheme.TabInactive, XShellTheme.IsDark ? 8 : 4)
            : XShellTheme.TabInactive;
        Color to = selected ? XShellTheme.Panel : from;
        using (var brush = new LinearGradientBrush(body, from, to, LinearGradientMode.Vertical))
        {
            g.FillPath(brush, path);
        }
        using (var pen = new Pen(selected ? XShellTheme.BorderLight : XShellTheme.Border, 1f))
        {
            g.DrawPath(pen, path);
        }
        if (selected)
        {
            using var accent = new SolidBrush(recording ? recordColor : XShellTheme.Accent);
            var bar = new Rectangle(body.Left + 10, body.Top + 1, Math.Max(10, body.Width - 20), 3);
            using var barPath = XShellTheme.RoundedRect(bar, 2);
            g.FillPath(accent, barPath);
            using var highlight = new Pen(Color.FromArgb(50, Color.White));
            g.DrawLine(highlight, body.Left + 10, body.Top + 6, body.Right - 10, body.Top + 6);
        }

        int textRight = recording ? 98 : 44;
        var textRect = new Rectangle(tab.Left + 14, tab.Top,
            Math.Max(20, tab.Width - textRight), tab.Height);
        TextRenderer.DrawText(g, TabPages[index].Text, Font, textRect,
            selected ? XShellTheme.Text : XShellTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

        var close = GetCloseRect(tab);
        if (recording)
        {
            var pill = new Rectangle(close.Left - 49, close.Top, 43, 16);
            using (var pillPath = XShellTheme.RoundedRect(pill, 8))
            using (var pillBrush = new SolidBrush(Color.FromArgb(245, recordColor)))
            {
                g.FillPath(pillBrush, pillPath);
            }
            using (var dot = new SolidBrush(Color.White))
            {
                g.FillEllipse(dot, pill.Left + 7, pill.Top + 5, 6, 6);
            }
            TextRenderer.DrawText(g, "REC", XShellTheme.Ui(7.2f, FontStyle.Bold),
                new Rectangle(pill.Left + 17, pill.Top, pill.Width - 17, pill.Height),
                Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPadding);
        }
        if (selected || _hoverClose == index || _hoverTab == index)
        {
            if (_hoverClose == index)
            {
                using var closeBack = new SolidBrush(Color.FromArgb(
                    XShellTheme.IsDark ? 70 : 40, XShellTheme.Red));
                g.FillEllipse(closeBack, close);
            }
            using var pen = new Pen(_hoverClose == index ? XShellTheme.Red
                : selected ? XShellTheme.Text : XShellTheme.Muted, 1.4f);
            var x = close.Left + 5;
            var y = close.Top + 5;
            g.DrawLine(pen, x, y, x + 6, y + 6);
            g.DrawLine(pen, x + 6, y, x, y + 6);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hoverTab = -1;
        int hit = -1;
        for (int i = 0; i < TabPages.Count; i++)
        {
            var tab = GetTabRect(i);
            if (!tab.Contains(e.Location))
            {
                continue;
            }
            hoverTab = i;
            if (GetCloseRect(tab).Contains(e.Location))
            {
                hit = i;
            }
            break;
        }
        if (hit != _hoverClose || hoverTab != _hoverTab)
        {
            _hoverClose = hit;
            _hoverTab = hoverTab;
            Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverClose != -1 || _hoverTab != -1)
        {
            _hoverClose = -1;
            _hoverTab = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        for (int i = 0; i < TabPages.Count; i++)
        {
            var tab = GetTabRect(i);
            if (!tab.Contains(e.Location))
            {
                continue;
            }
            if (e.Button == MouseButtons.Middle)
            {
                TabCloseRequested?.Invoke(TabPages[i]);
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                TabRightClicked?.Invoke(TabPages[i], e.Location);
                return;
            }
            if (e.Button == MouseButtons.Left && GetCloseRect(tab).Contains(e.Location))
            {
                TabCloseRequested?.Invoke(TabPages[i]);
                return;
            }
            SelectedIndex = i;
            return;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        int height = Math.Min(ClientSize.Height, ItemSize.Height + Padding.Y + 8);
        var rect = new Rectangle(0, 0, ClientSize.Width, height);
        using (var brush = new LinearGradientBrush(rect,
            XShellTheme.TabStrip, XShellTheme.Background, LinearGradientMode.Vertical))
        {
            g.FillRectangle(brush, rect);
        }
        using var line = new Pen(Color.FromArgb(120, XShellTheme.Accent));
        g.DrawLine(line, 0, height - 1, ClientSize.Width, height - 1);
    }

    protected override void WndProc(ref Message m)
    {
        // 完全接管背景：禁止系统擦除，避免切换标签时出现白色闪屏。
        if (m.Msg == 0x0014)
        {
            m.Result = (IntPtr)1;
            return;
        }
        base.WndProc(ref m);
        // Windows 原生 TabControl 会在页面区外沿绘制一圈浅色边框；
        // 深色主题下先把页面区四周的缝隙全部填成主题背景色，再画一层主题边框，
        // 避免终端四周出现刺眼的白色细线。
        if (m.Msg == 0x000F && IsHandleCreated)
        {
            try
            {
                using var g = Graphics.FromHwnd(Handle);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // 原生 TabControl 会先用系统浅色绘制整个标签条；
                // 这里在原生绘制之后，无论有没有标签页，都先用主题色重铺整个标签条，
                // 再逐个重绘标签，彻底消除标签之间与右侧的白色残留。
                DrawTabStrip(g);

                if (TabPages.Count == 0)
                {
                    if (ClientSize.Width > 0 && ClientSize.Height > 0)
                    {
                        int strip = StripHeight();
                        using var background = new SolidBrush(XShellTheme.Background);
                        if (ClientSize.Height > strip)
                        {
                            g.FillRectangle(background, 0, strip, ClientSize.Width,
                                ClientSize.Height - strip);
                        }
                    }
                    return;
                }

                for (int i = 0; i < TabPages.Count; i++)
                {
                    DrawTab(g, i);
                }

                // Windows 原生 TabControl 会在页面区外沿绘制一圈浅色边框；
                // 这里把页面区四周的缝隙全部填成主题背景色，再画一层主题边框。
                var display = DisplayRectangle;
                using (var gap = new SolidBrush(XShellTheme.Background))
                {
                    if (display.Left > 0)
                    {
                        g.FillRectangle(gap, 0, display.Top, display.Left,
                            Math.Max(0, ClientSize.Height - display.Top));
                    }
                    if (display.Right < ClientSize.Width)
                    {
                        g.FillRectangle(gap, display.Right, display.Top,
                            ClientSize.Width - display.Right,
                            Math.Max(0, ClientSize.Height - display.Top));
                    }
                    if (display.Bottom < ClientSize.Height)
                    {
                        g.FillRectangle(gap, 0, display.Bottom, ClientSize.Width,
                            ClientSize.Height - display.Bottom);
                    }
                    int stripHeight = StripHeight();
                    if (display.Top > stripHeight)
                    {
                        g.FillRectangle(gap, 0, stripHeight, ClientSize.Width,
                            display.Top - stripHeight);
                    }
                }
                using (var border = new Pen(XShellTheme.Border, 1f))
                {
                    g.DrawRectangle(border, display.X - 1, display.Y - 1,
                        Math.Max(1, display.Width + 1), Math.Max(1, display.Height + 1));
                }

                if (_recording.Count > 0)
                {
                    var font = XShellTheme.Ui(8f, FontStyle.Bold);
                    string text = "● REC";
                    Size size = TextRenderer.MeasureText(g, text, font);
                    var pill = new Rectangle(
                        Math.Max(4, ClientSize.Width - size.Width - 34),
                        6, size.Width + 18, 20);
                    using var path = XShellTheme.RoundedRect(pill, 10);
                    using var brush = new SolidBrush(Color.FromArgb(240, 232, 48, 58));
                    g.FillPath(brush, path);
                    TextRenderer.DrawText(g, text, font,
                        new Rectangle(pill.Left + 9, pill.Top, pill.Width - 9, pill.Height),
                        Color.White,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.NoPadding);
                }
            }
            catch
            {
                // 绘制失败不影响标签切换。
            }
        }
    }
}
