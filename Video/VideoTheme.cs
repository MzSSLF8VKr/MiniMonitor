using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MiniMonitor.Video;

/// <summary>无损视频截取等视频工具共用的深色编辑器主题。</summary>
internal static class VideoTheme
{
    public static readonly Color Window = Color.FromArgb(27, 28, 30);
    public static readonly Color Panel = Color.FromArgb(35, 36, 39);
    public static readonly Color PanelAlt = Color.FromArgb(45, 46, 50);
    public static readonly Color PanelLight = Color.FromArgb(54, 55, 60);
    public static readonly Color Border = Color.FromArgb(66, 68, 74);
    public static readonly Color Text = Color.FromArgb(232, 234, 237);
    public static readonly Color Muted = Color.FromArgb(156, 160, 168);
    public static readonly Color Accent = Color.FromArgb(58, 142, 230);
    public static readonly Color AccentHover = Color.FromArgb(82, 162, 245);
    public static readonly Color AccentSoft = Color.FromArgb(38, 64, 96);
    public static readonly Color Playhead = Color.FromArgb(255, 92, 92);
    public static readonly Color Selection = Color.FromArgb(255, 196, 72);
    public static readonly Color VideoClip = Color.FromArgb(53, 104, 158);
    public static readonly Color VideoClipAlt = Color.FromArgb(44, 86, 132);
    public static readonly Color AudioClip = Color.FromArgb(45, 128, 92);
    public static readonly Color AudioClipAlt = Color.FromArgb(37, 106, 77);
    public static readonly Color ImageClip = Color.FromArgb(150, 112, 60);
    public static readonly Color Danger = Color.FromArgb(230, 92, 92);
    public static readonly Color Success = Color.FromArgb(72, 190, 120);
    public static readonly Color TrackBack = Color.FromArgb(31, 32, 35);
    public static readonly Color RulerBack = Color.FromArgb(39, 40, 44);

    public static readonly Font Ui9 = new("Microsoft YaHei UI", 9f);
    public static readonly Font UiBold9 = new("Microsoft YaHei UI", 9f, FontStyle.Bold);
    public static readonly Font UiSmall = new("Microsoft YaHei UI", 8f);
    public static readonly Font UiTitle = new("Microsoft YaHei UI", 12f, FontStyle.Bold);
    public static readonly Font UiTime = new("Consolas", 11f, FontStyle.Bold);
    public static readonly Font UiTimeSmall = new("Consolas", 8.5f);

    public static Button Button(string text, int width = 88, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : PanelAlt,
            ForeColor = primary ? Color.White : Text,
            Font = Ui9,
            Cursor = Cursors.Hand,
            TabStop = false,
            Margin = new Padding(0, 0, 6, 0)
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? AccentHover : PanelLight;
        button.FlatAppearance.MouseDownBackColor = primary ? Accent : Border;
        return button;
    }

    public static Label Label(string text, bool muted = false, float size = 9f)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = muted ? Muted : Text,
            BackColor = Color.Transparent,
            Font = size == 9f ? Ui9 : new Font("Microsoft YaHei UI", size)
        };
    }

    public static void StyleCombo(ComboBox combo, int width = 100)
    {
        combo.Width = width;
        combo.FlatStyle = FlatStyle.Flat;
        combo.BackColor = PanelAlt;
        combo.ForeColor = Text;
        combo.Font = Ui9;
    }

    public static void StyleNumeric(NumericUpDown numeric, int width = 74, int decimals = 2)
    {
        numeric.Width = width;
        numeric.DecimalPlaces = decimals;
        numeric.BackColor = PanelAlt;
        numeric.ForeColor = Text;
        numeric.Font = Ui9;
        numeric.BorderStyle = BorderStyle.FixedSingle;
    }

    public static void StyleTextBox(TextBox box)
    {
        box.BackColor = PanelAlt;
        box.ForeColor = Text;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = Ui9;
    }

    public static void StyleListView(ListView list)
    {
        list.BackColor = Panel;
        list.ForeColor = Text;
        list.BorderStyle = BorderStyle.None;
        list.Font = Ui9;
        list.FullRowSelect = true;
        list.HideSelection = false;
        list.OwnerDraw = false;
    }

    /// <summary>
    /// 用自绘深色表头包裹 ListView，避免系统表头在深色界面中出现白色横条。
    /// </summary>
    public static Panel WrapWithDarkHeader(ListView list)
    {
        list.HeaderStyle = ColumnHeaderStyle.None;
        list.Dock = DockStyle.Fill;
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 26,
            BackColor = PanelAlt
        };
        header.Paint += (_, e) => DrawListHeader(e.Graphics, header, list);
        list.Resize += (_, _) => header.Invalidate();
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Panel
        };
        host.Controls.Add(list);
        host.Controls.Add(header);
        host.Resize += (_, _) => header.Invalidate();
        return host;
    }

    private static void DrawListHeader(Graphics g, Panel header, ListView list)
    {
        if (header.Width <= 0 || header.Height <= 0)
        {
            return;
        }
        using (var background = new SolidBrush(PanelAlt))
        {
            g.FillRectangle(background, header.ClientRectangle);
        }
        using (var pen = new Pen(Border))
        {
            g.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        }
        int x = 0;
        for (int i = 0; i < list.Columns.Count; i++)
        {
            int width = list.Columns[i].Width;
            var align = list.Columns[i].TextAlign == HorizontalAlignment.Right
                ? TextFormatFlags.Right : TextFormatFlags.Left;
            TextRenderer.DrawText(g, list.Columns[i].Text, UiSmall,
                new Rectangle(x + 6, 0, Math.Max(10, width - 12), header.Height),
                Muted,
                align | TextFormatFlags.VerticalCenter
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            x += width;
        }
    }

    public static ToolStripRenderer CreateRenderer()
    {
        return new ToolStripProfessionalRenderer(new VideoColorTable()) { RoundedEdges = false };
    }

    public static void StyleTabControl(TabControl tabs)
    {
        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabs.ItemSize = new Size(112, 28);
        tabs.SizeMode = TabSizeMode.Fixed;
        tabs.Padding = new Point(8, 3);
        tabs.DrawItem += (_, e) =>
        {
            var page = tabs.TabPages[e.Index];
            bool selected = tabs.SelectedIndex == e.Index;
            using var back = new SolidBrush(selected ? PanelLight : Panel);
            e.Graphics.FillRectangle(back, e.Bounds);
            using var border = new Pen(Border);
            e.Graphics.DrawRectangle(border, e.Bounds);
            using var text = new SolidBrush(selected ? Text : Muted);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            e.Graphics.DrawString(page.Text, selected ? UiBold9 : Ui9, text,
                e.Bounds, format);
        };
    }

    public static void StyleMenuItems(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            item.ForeColor = Text;
            if (item is ToolStripMenuItem menu && menu.DropDownItems.Count > 0)
            {
                StyleMenuItems(menu.DropDownItems);
            }
        }
    }

    public static void StyleContextMenu(ContextMenuStrip menu)
    {
        menu.BackColor = Panel;
        menu.ForeColor = Text;
        menu.Font = Ui9;
        menu.Renderer = CreateRenderer();
        StyleMenuItems(menu.Items);
    }

    public static void StyleMenuStrip(MenuStrip menu)
    {
        menu.BackColor = Panel;
        menu.ForeColor = Text;
        menu.Font = Ui9;
        menu.Renderer = CreateRenderer();
        menu.Padding = new Padding(4, 2, 0, 2);
    }

    public static void StyleToolStrip(ToolStrip strip)
    {
        strip.BackColor = Panel;
        strip.ForeColor = Text;
        strip.Font = Ui9;
        strip.Renderer = CreateRenderer();
        strip.GripStyle = ToolStripGripStyle.Hidden;
        strip.Padding = new Padding(6, 2, 6, 2);
    }

    public static ToolStripButton ToolButton(string text, EventHandler? click = null)
    {
        var button = new ToolStripButton(text)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ForeColor = Text,
            Font = Ui9,
            AutoSize = true,
            Margin = new Padding(1, 0, 1, 0),
            Padding = new Padding(6, 2, 6, 2)
        };
        if (click != null)
        {
            button.Click += click;
        }
        return button;
    }

    public static void DrawRoundedRect(Graphics graphics, Brush brush, RectangleF rect, float radius)
    {
        using var path = new GraphicsPath();
        float r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2f);
        if (r <= 0)
        {
            graphics.FillRectangle(brush, rect);
            return;
        }
        path.AddArc(rect.X, rect.Y, r * 2, r * 2, 180, 90);
        path.AddArc(rect.Right - r * 2, rect.Y, r * 2, r * 2, 270, 90);
        path.AddArc(rect.Right - r * 2, rect.Bottom - r * 2, r * 2, r * 2, 0, 90);
        path.AddArc(rect.X, rect.Bottom - r * 2, r * 2, r * 2, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }

    public static string Timecode(double seconds, bool milliseconds = true)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
        {
            seconds = 0;
        }
        var time = TimeSpan.FromSeconds(seconds);
        if (time.TotalHours >= 1)
        {
            return milliseconds
                ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}"
                : $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
        }
        return milliseconds
            ? $"{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }

    public static string ShortTime(double seconds)
    {
        return Timecode(seconds, milliseconds: false);
    }
}

internal sealed class VideoColorTable : ProfessionalColorTable
{
    public override Color MenuStripGradientBegin => VideoTheme.Panel;
    public override Color MenuStripGradientEnd => VideoTheme.Panel;
    public override Color MenuItemSelected => VideoTheme.PanelLight;
    public override Color MenuItemBorder => VideoTheme.Border;
    public override Color MenuItemSelectedGradientBegin => VideoTheme.PanelLight;
    public override Color MenuItemSelectedGradientEnd => VideoTheme.PanelLight;
    public override Color MenuItemPressedGradientBegin => VideoTheme.PanelAlt;
    public override Color MenuItemPressedGradientEnd => VideoTheme.PanelAlt;
    public override Color ToolStripDropDownBackground => VideoTheme.Panel;
    public override Color ImageMarginGradientBegin => VideoTheme.Panel;
    public override Color ImageMarginGradientMiddle => VideoTheme.Panel;
    public override Color ImageMarginGradientEnd => VideoTheme.Panel;
    public override Color SeparatorDark => VideoTheme.Border;
    public override Color SeparatorLight => VideoTheme.Border;
    public override Color ToolStripBorder => VideoTheme.Border;
    public override Color ButtonSelectedHighlight => VideoTheme.PanelLight;
    public override Color ButtonSelectedGradientBegin => VideoTheme.PanelLight;
    public override Color ButtonSelectedGradientEnd => VideoTheme.PanelLight;
    public override Color ButtonPressedHighlight => VideoTheme.PanelAlt;
    public override Color ButtonPressedGradientBegin => VideoTheme.PanelAlt;
    public override Color ButtonPressedGradientEnd => VideoTheme.PanelAlt;
    public override Color CheckBackground => VideoTheme.AccentSoft;
    public override Color CheckSelectedBackground => VideoTheme.Accent;
    public override Color CheckPressedBackground => VideoTheme.Accent;
    public override Color GripDark => VideoTheme.Border;
    public override Color GripLight => VideoTheme.PanelAlt;
}
