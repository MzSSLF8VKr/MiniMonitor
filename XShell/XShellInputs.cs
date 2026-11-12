using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MiniMonitor.XShell;

/// <summary>深色 / 浅色主题下的提示气泡。</summary>
internal sealed class XShellToolTip : ToolTip
{
    private static readonly Font TipFont = XShellTheme.Ui(8.5f);

    public XShellToolTip()
    {
        OwnerDraw = true;
        Draw += (_, e) =>
        {
            using (var background = new SolidBrush(XShellTheme.Panel))
            {
                e.Graphics.FillRectangle(background, e.Bounds);
            }
            using (var pen = new Pen(XShellTheme.Border))
            {
                e.Graphics.DrawRectangle(pen, 1, 1,
                    Math.Max(1, e.Bounds.Width - 3),
                    Math.Max(1, e.Bounds.Height - 3));
            }
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, TipFont,
                Rectangle.Inflate(e.Bounds, -7, -4), XShellTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        };
        Popup += (_, e) =>
        {
            string text = e.AssociatedControl != null
                ? GetToolTip(e.AssociatedControl) ?? "" : "";
            Size size = TextRenderer.MeasureText(text, TipFont);
            e.ToolTipSize = new Size(size.Width + 18, size.Height + 12);
        };
    }
}

/// <summary>深色 / 浅色主题下的下拉框：下拉列表项由主题绘制。</summary>
internal sealed class XShellComboBox : ComboBox
{
    public XShellComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        BackColor = XShellTheme.InputBack;
        ForeColor = XShellTheme.Text;
        ItemHeight = 24;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        XShellWindowTheme.ApplyControlTheme(this);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count)
        {
            return;
        }
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var background = new SolidBrush(
            selected ? XShellTheme.SelectionBack : XShellTheme.InputBack))
        {
            e.Graphics.FillRectangle(background, e.Bounds);
        }
        string text = GetItemText(Items[e.Index]) ?? "";
        TextRenderer.DrawText(e.Graphics, text, Font, e.Bounds,
            selected ? XShellTheme.Current.SelectionFore : XShellTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}

/// <summary>自绘复选框，确保深色主题下不会出现刺眼的白色方框。</summary>
internal sealed class XShellCheckBox : CheckBox
{
    private bool _hover;

    public XShellCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        // 使用实色背景，避免透明控件在悬停重绘时把相邻文字一起“抹花”。
        BackColor = XShellTheme.Panel;
        ForeColor = XShellTheme.Text;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size size = base.GetPreferredSize(proposedSize);
        return new Size(size.Width + 10, Math.Max(20, size.Height));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        int boxSize = 16;
        int top = Math.Max(0, (Height - boxSize) / 2);
        var box = new Rectangle(1, top, boxSize, boxSize);
        using (var path = XShellTheme.RoundedRect(box, 4))
        {
            using (var background = new SolidBrush(Checked
                ? XShellTheme.Accent : XShellTheme.InputBack))
            {
                g.FillPath(background, path);
            }
            using var pen = new Pen(Checked ? XShellTheme.Accent
                : _hover ? XShellTheme.Accent : XShellTheme.Border, 1.2f);
            g.DrawPath(pen, path);
        }
        if (Checked)
        {
            using var check = new Pen(Color.White, 1.8f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawLines(check, new[]
            {
                new Point(box.Left + 4, box.Top + 8),
                new Point(box.Left + 7, box.Top + 11),
                new Point(box.Left + 12, box.Top + 5)
            });
        }
        var textRect = new Rectangle(box.Right + 7, 0,
            Math.Max(1, Width - box.Right - 8), Height);
        TextRenderer.DrawText(g, Text, Font, textRect,
            Enabled ? ForeColor : XShellTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues)
        {
            ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(textRect, 1, 1),
                ForeColor, BackColor);
        }
    }
}
