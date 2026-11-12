using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MiniMonitor.XFtp;

/// <summary>XFtp 风格圆角按钮（自绘，固定深色）。</summary>
internal sealed class XFtpButton : Control
{
    private bool _hover;
    private bool _pressed;

    public bool Primary { get; set; }

    public XFtpButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = XFtpTheme.Text;
        Cursor = Cursors.Hand;
        Font = XFtpTheme.Ui9;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _pressed = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = XFtpTheme.RoundedRect(rect, 7);

        Color from;
        Color to;
        Color border;
        Color text;
        if (Primary)
        {
            from = _pressed ? XFtpTheme.AccentDark
                : _hover ? XFtpTheme.Lighten(XFtpTheme.AccentHover, 18) : XFtpTheme.AccentHover;
            to = _pressed ? XFtpTheme.AccentDark : XFtpTheme.Accent;
            border = _hover ? XFtpTheme.AccentHover : XFtpTheme.Accent;
            text = Color.White;
        }
        else
        {
            from = _pressed ? XFtpTheme.Pressed
                : _hover ? XFtpTheme.Hover : XFtpTheme.PanelAlt;
            to = _pressed ? XFtpTheme.Pressed : XFtpTheme.Panel;
            border = _hover ? XFtpTheme.Accent : XFtpTheme.Border;
            text = _hover ? Color.White : XFtpTheme.Text;
        }
        if (!Enabled)
        {
            from = XFtpTheme.PanelAlt;
            to = XFtpTheme.Panel;
            border = XFtpTheme.BorderLight;
            text = XFtpTheme.Muted;
        }
        using (var brush = new LinearGradientBrush(rect, from, to, LinearGradientMode.Vertical))
        {
            g.FillPath(brush, path);
        }
        using (var pen = new Pen(border))
        {
            g.DrawPath(pen, path);
        }
        TextRenderer.DrawText(g, Text, Font, rect, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}

/// <summary>XFtp 品牌渐变标题区。</summary>
internal sealed class XFtpHeaderPanel : Panel
{
    public string Title { get; set; } = "XFTP 复刻版";
    public string Subtitle { get; set; } = "FTP / FTPS / SFTP 文件传输";

    public XFtpHeaderPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = XFtpTheme.HeaderFrom;
        Height = 62;
        Dock = DockStyle.Top;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        if (Width <= 0 || Height <= 0)
        {
            return;
        }
        using (var brush = new LinearGradientBrush(ClientRectangle,
            XFtpTheme.HeaderFrom, XFtpTheme.HeaderTo, 18f))
        {
            g.FillRectangle(brush, ClientRectangle);
        }
        using (var glow = new SolidBrush(Color.FromArgb(26, XFtpTheme.Accent)))
        {
            g.FillEllipse(glow, Width - 300, -140, 440, 440);
        }
        using (var pen = new Pen(Color.FromArgb(38, Color.White), 1f))
        {
            for (int x = -30; x < Width; x += 54)
            {
                g.DrawLine(pen, x, Height, x + Height, 0);
            }
        }

        var logo = new Rectangle(20, 14, 34, 34);
        using (var logoPath = XFtpTheme.RoundedRect(logo, 9))
        using (var logoBrush = new LinearGradientBrush(logo,
            XFtpTheme.AccentHover, XFtpTheme.AccentDark, 45f))
        {
            g.FillPath(logoBrush, logoPath);
        }
        TextRenderer.DrawText(g, "\uE8B7", XFtpTheme.Mdl2Logo,
            new Rectangle(logo.Left, logo.Top, logo.Width, logo.Height),
            Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPadding);

        TextRenderer.DrawText(g, Title, XFtpTheme.HeaderTitle,
            new Rectangle(66, 9, Math.Max(60, Width - 300), 26), Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, Subtitle, XFtpTheme.Ui85,
            new Rectangle(67, 34, Math.Max(60, Width - 300), 18),
            Color.FromArgb(176, 206, 238),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        int chipRight = Width - 18;
        var chips = new[] { "DARK", "PASSIVE", "UTF-8" };
        for (int i = chips.Length - 1; i >= 0; i--)
        {
            var font = XFtpTheme.Ui(7.6f, FontStyle.Bold);
            int width = TextRenderer.MeasureText(g, chips[i], font).Width + 20;
            var chip = new Rectangle(chipRight - width, Height / 2 - 12, width, 24);
            using var chipPath = XFtpTheme.RoundedRect(chip, 12);
            using (var fill = new SolidBrush(Color.FromArgb(34, Color.White)))
            {
                g.FillPath(fill, chipPath);
            }
            using (var pen = new Pen(Color.FromArgb(80, Color.White)))
            {
                g.DrawPath(pen, chipPath);
            }
            TextRenderer.DrawText(g, chips[i], font, chip, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPadding);
            chipRight = chip.Left - 7;
        }
        using var accent = new Pen(XFtpTheme.Accent, 2f);
        g.DrawLine(accent, 0, Height - 1, Width, Height - 1);
    }
}

/// <summary>深色提示气泡。</summary>
internal sealed class XFtpToolTip : ToolTip
{
    private static readonly Font TipFont = XFtpTheme.Ui(8.5f);

    public XFtpToolTip()
    {
        OwnerDraw = true;
        Draw += (_, e) =>
        {
            using (var background = new SolidBrush(XFtpTheme.Panel))
            {
                e.Graphics.FillRectangle(background, e.Bounds);
            }
            using (var pen = new Pen(XFtpTheme.Border))
            {
                e.Graphics.DrawRectangle(pen, 1, 1,
                    Math.Max(1, e.Bounds.Width - 3),
                    Math.Max(1, e.Bounds.Height - 3));
            }
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, TipFont,
                Rectangle.Inflate(e.Bounds, -7, -4), XFtpTheme.Text,
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

/// <summary>深色下拉框，下拉项由 XFtp 主题绘制。</summary>
internal sealed class XFtpComboBox : ComboBox
{
    public XFtpComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        BackColor = XFtpTheme.InputBack;
        ForeColor = XFtpTheme.Text;
        ItemHeight = 24;
        Font = XFtpTheme.Ui9;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            XFtpTheme.ApplyNative(this);
        }
        catch
        {
            // 忽略主题失败。
        }
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count)
        {
            return;
        }
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var background = new SolidBrush(
            selected ? XFtpTheme.SelectionBack : XFtpTheme.InputBack))
        {
            e.Graphics.FillRectangle(background, e.Bounds);
        }
        string text = GetItemText(Items[e.Index]) ?? "";
        TextRenderer.DrawText(e.Graphics, text, Font, e.Bounds,
            selected ? Color.White : XFtpTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }
}

/// <summary>深色输入对话框，用于新建文件夹 / 重命名。</summary>
internal static class XFtpPrompt
{
    public static string? Show(IWin32Window? owner, string title, string label,
        string defaultValue = "")
    {
        using var form = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            BackColor = XFtpTheme.Background,
            ForeColor = XFtpTheme.Text,
            Font = XFtpTheme.Ui9,
            ClientSize = new Size(420, 196)
        };
        form.HandleCreated += (_, _) => XFtpTheme.ApplyWindow(form);

        var header = new XFtpHeaderPanel
        {
            Dock = DockStyle.Top,
            Height = 56,
            Title = title,
            Subtitle = "XFTP 复刻版"
        };
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            BackColor = XFtpTheme.PanelAlt,
            Padding = new Padding(0, 10, 16, 10)
        };
        var ok = XFtpTheme.PrimaryButton("确定", 80, 28);
        ok.Dock = DockStyle.Right;
        ok.Margin = new Padding(6, 0, 0, 0);
        var cancel = XFtpTheme.FlatButton("取消", 80, 28);
        cancel.Dock = DockStyle.Right;
        footer.Controls.Add(ok);
        footer.Controls.Add(cancel);

        var body = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = XFtpTheme.Panel,
            Padding = new Padding(18, 14, 18, 10)
        };
        var caption = new Label
        {
            Text = label,
            Dock = DockStyle.Top,
            Height = 24,
            ForeColor = XFtpTheme.Muted,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var input = XFtpTheme.Input(360);
        input.Dock = DockStyle.Top;
        input.Text = defaultValue;
        body.Controls.Add(input);
        body.Controls.Add(caption);

        form.Controls.Add(body);
        form.Controls.Add(footer);
        form.Controls.Add(header);
        body.BringToFront();
        form.Controls.SetChildIndex(body, 0);
        form.Controls.SetChildIndex(footer, 1);
        form.Controls.SetChildIndex(header, 2);

        string? result = null;
        ok.Click += (_, _) =>
        {
            result = input.Text.Trim();
            form.DialogResult = DialogResult.OK;
            form.Close();
        };
        cancel.Click += (_, _) => form.Close();
        form.AcceptButton = null;
        input.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                result = input.Text.Trim();
                form.DialogResult = DialogResult.OK;
                form.Close();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                form.Close();
            }
        };
        form.Shown += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };
        return (owner != null ? form.ShowDialog(owner) : form.ShowDialog()) == DialogResult.OK
            ? result
            : null;
    }
}

/// <summary>XFtp 深色复选框。</summary>
internal sealed class XFtpCheckBox : CheckBox
{
    private bool _hover;

    public XFtpCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = XFtpTheme.Text;
        Font = XFtpTheme.Ui9;
        Cursor = Cursors.Hand;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size size = base.GetPreferredSize(proposedSize);
        return new Size(size.Width + 10, Math.Max(22, size.Height));
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
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
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        int boxSize = 16;
        int top = Math.Max(0, (Height - boxSize) / 2);
        var box = new Rectangle(1, top, boxSize, boxSize);
        using (var path = XFtpTheme.RoundedRect(box, 4))
        {
            using (var background = new SolidBrush(Checked
                ? XFtpTheme.Accent : XFtpTheme.InputBack))
            {
                g.FillPath(background, path);
            }
            using var pen = new Pen(Checked || _hover
                ? XFtpTheme.Accent : XFtpTheme.Border, 1.2f);
            g.DrawPath(pen, path);
        }
        if (Checked)
        {
            using var check = new Pen(Color.White, 1.8f)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round
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
            Enabled ? ForeColor : XFtpTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues)
        {
            ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(textRect, 1, 1),
                ForeColor, BackColor);
        }
    }
}

/// <summary>XFtp 深色消息框，替代系统白色 MessageBox。</summary>
internal static class XFtpMessageBox
{
    public static DialogResult Show(string text,
        string caption = "XFTP 复刻版",
        MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None)
    {
        return Show(null, text, caption, buttons, icon);
    }

    public static DialogResult Show(IWin32Window? owner, string text, string caption,
        MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None)
    {
        using var form = new XFtpMessageForm(text, caption, buttons, icon);
        return owner != null ? form.ShowDialog(owner) : form.ShowDialog();
    }

    private sealed class XFtpMessageForm : Form
    {
        public XFtpMessageForm(string text, string caption,
            MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            Text = caption;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = XFtpTheme.Background;
            ForeColor = XFtpTheme.Text;
            Font = XFtpTheme.Ui9;
            DoubleBuffered = true;
            HandleCreated += (_, _) => XFtpTheme.ApplyWindow(this);

            var messageFont = XFtpTheme.Ui(9.2f);
            Size measured = TextRenderer.MeasureText(text, messageFont,
                new Size(468, 10000), TextFormatFlags.WordBreak);
            int contentHeight = Math.Max(66, Math.Min(measured.Height + 24, 460));
            ClientSize = new Size(560, 60 + contentHeight + 62);

            var header = new XFtpHeaderPanel
            {
                Dock = DockStyle.Top,
                Height = 60,
                Title = caption,
                Subtitle = "XFTP 复刻版"
            };
            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 62,
                BackColor = XFtpTheme.PanelAlt,
                Padding = new Padding(0, 15, 18, 15)
            };
            footer.Paint += (_, e) =>
            {
                using var pen = new Pen(XFtpTheme.Border);
                e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
            };
            var content = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = XFtpTheme.Panel,
                Padding = new Padding(22, 16, 22, 12)
            };
            int textLeft = 22;
            if (icon != MessageBoxIcon.None)
            {
                string glyph = icon switch
                {
                    MessageBoxIcon.Warning => "\uE7BA",
                    MessageBoxIcon.Error => "\uE783",
                    MessageBoxIcon.Question => "\uE9CE",
                    _ => "\uE946"
                };
                Color color = icon switch
                {
                    MessageBoxIcon.Warning => XFtpTheme.Orange,
                    MessageBoxIcon.Error => XFtpTheme.Red,
                    _ => XFtpTheme.Accent
                };
                var picture = new PictureBox
                {
                    Image = XFtpTheme.Icon(glyph, color, 34),
                    Left = 22,
                    Top = 18,
                    Width = 36,
                    Height = 36,
                    SizeMode = PictureBoxSizeMode.CenterImage,
                    BackColor = Color.Transparent
                };
                content.Controls.Add(picture);
                textLeft = 72;
            }
            var label = new Label
            {
                Text = text,
                Left = textLeft,
                Top = 16,
                Width = 560 - textLeft - 40,
                Height = contentHeight - 10,
                ForeColor = XFtpTheme.Text,
                BackColor = Color.Transparent,
                Font = messageFont
            };
            content.Controls.Add(label);

            var buttonFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.RightToLeft,
                BackColor = XFtpTheme.PanelAlt
            };
            switch (buttons)
            {
                case MessageBoxButtons.OKCancel:
                    buttonFlow.Controls.Add(Button("取消", DialogResult.Cancel, false));
                    buttonFlow.Controls.Add(Button("确定", DialogResult.OK, true));
                    break;
                case MessageBoxButtons.YesNo:
                    buttonFlow.Controls.Add(Button("否", DialogResult.No, false));
                    buttonFlow.Controls.Add(Button("是", DialogResult.Yes, true));
                    break;
                case MessageBoxButtons.YesNoCancel:
                    buttonFlow.Controls.Add(Button("取消", DialogResult.Cancel, false));
                    buttonFlow.Controls.Add(Button("否", DialogResult.No, false));
                    buttonFlow.Controls.Add(Button("是", DialogResult.Yes, true));
                    break;
                case MessageBoxButtons.RetryCancel:
                    buttonFlow.Controls.Add(Button("取消", DialogResult.Cancel, false));
                    buttonFlow.Controls.Add(Button("重试", DialogResult.Retry, true));
                    break;
                default:
                    buttonFlow.Controls.Add(Button("确定", DialogResult.OK, true));
                    break;
            }
            footer.Controls.Add(buttonFlow);

            Controls.Add(content);
            Controls.Add(footer);
            Controls.Add(header);
            content.BringToFront();
            Controls.SetChildIndex(content, 0);
            Controls.SetChildIndex(footer, 1);
            Controls.SetChildIndex(header, 2);
        }

        private XFtpButton Button(string text, DialogResult result, bool primary)
        {
            var button = primary
                ? XFtpTheme.PrimaryButton(text, 88, 30)
                : XFtpTheme.FlatButton(text, 88, 30);
            button.Margin = new Padding(6, 0, 0, 0);
            button.Click += (_, _) =>
            {
                DialogResult = result;
                Close();
            };
            return button;
        }
    }
}
