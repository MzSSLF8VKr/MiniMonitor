using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MiniMonitor.Navicat;

/// <summary>Navicat Premium 风格配色、字体与原生控件美化辅助。</summary>
internal static class NvTheme
{
    public static readonly Color Background = Color.FromArgb(243, 246, 250);
    public static readonly Color Panel = Color.FromArgb(255, 255, 255);
    public static readonly Color PanelAlt = Color.FromArgb(248, 250, 252);
    public static readonly Color HeaderBack = Color.FromArgb(233, 240, 247);
    public static readonly Color HeaderBack2 = Color.FromArgb(222, 232, 242);
    public static readonly Color Border = Color.FromArgb(204, 214, 224);
    public static readonly Color BorderLight = Color.FromArgb(226, 232, 238);
    public static readonly Color Text = Color.FromArgb(31, 42, 54);
    public static readonly Color Muted = Color.FromArgb(108, 122, 137);
    public static readonly Color Accent = Color.FromArgb(30, 111, 168);
    public static readonly Color AccentLight = Color.FromArgb(214, 232, 246);
    public static readonly Color AccentDark = Color.FromArgb(22, 84, 128);
    public static readonly Color Orange = Color.FromArgb(240, 140, 40);
    public static readonly Color OrangeLight = Color.FromArgb(253, 239, 224);
    public static readonly Color Green = Color.FromArgb(46, 139, 87);
    public static readonly Color Red = Color.FromArgb(196, 62, 62);
    public static readonly Color GridHeader = Color.FromArgb(238, 244, 250);
    public static readonly Color GridAlt = Color.FromArgb(249, 251, 253);
    public static readonly Color GridSelect = Color.FromArgb(207, 228, 247);
    public static readonly Color EditorBack = Color.FromArgb(255, 255, 255);
    public static readonly Color EditorGutter = Color.FromArgb(243, 246, 249);
    public static readonly Color TabStrip = Color.FromArgb(226, 234, 242);
    public static readonly Color TabActive = Color.FromArgb(255, 255, 255);

    public static Font Ui(float size = 9f, FontStyle style = FontStyle.Regular) =>
        new("Microsoft YaHei UI", size, style);

    public static Font Mono(float size = 10f, FontStyle style = FontStyle.Regular) =>
        new("Consolas", size, style);

    public static void EnableDoubleBuffer(Control control)
    {
        typeof(Control).GetProperty("DoubleBuffered",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(control, true, null);
    }

    public static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Max(2, radius * 2);
        if (d > rect.Width)
        {
            d = rect.Width;
        }
        if (d > rect.Height)
        {
            d = rect.Height;
        }
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void StyleGrid(DataGridView grid, bool readOnly = false)
    {
        EnableDoubleBuffer(grid);
        grid.BackgroundColor = Panel;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = BorderLight;
        grid.RowHeadersVisible = false;
        grid.EnableHeadersVisualStyles = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.MultiSelect = true;
        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        grid.EditMode = readOnly ? DataGridViewEditMode.EditProgrammatically : DataGridViewEditMode.EditOnKeystrokeOrF2;
        grid.ReadOnly = readOnly;
        grid.ColumnHeadersHeight = 28;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.RowTemplate.Height = 24;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = GridHeader,
            ForeColor = Color.FromArgb(51, 71, 91),
            Font = Ui(9f, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 4, 0),
            SelectionBackColor = GridHeader,
            SelectionForeColor = Color.FromArgb(51, 71, 91),
            WrapMode = DataGridViewTriState.False
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Panel,
            ForeColor = Text,
            Font = Ui(9f),
            SelectionBackColor = GridSelect,
            SelectionForeColor = Text,
            Padding = new Padding(3, 0, 3, 0),
            WrapMode = DataGridViewTriState.False
        };
        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = GridAlt,
            ForeColor = Text,
            SelectionBackColor = GridSelect,
            SelectionForeColor = Text
        };
        grid.ShowCellToolTips = true;
        grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;
    }

    public static void StyleTree(TreeView tree)
    {
        EnableDoubleBuffer(tree);
        tree.BackColor = Panel;
        tree.BorderStyle = BorderStyle.None;
        tree.Font = Ui(9.2f);
        tree.FullRowSelect = true;
        tree.HideSelection = false;
        tree.ShowLines = false;
        tree.ShowPlusMinus = true;
        tree.ShowRootLines = false;
        tree.ItemHeight = 24;
        tree.Indent = 18;
        tree.DrawMode = TreeViewDrawMode.OwnerDrawAll;
    }

    public static Button FlatButton(string text, Color? back = null, Color? fore = null, int width = 92)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = false,
            Width = width,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = back ?? Color.FromArgb(248, 250, 252),
            ForeColor = fore ?? Text,
            Font = Ui(9f),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            ImageAlign = ContentAlignment.MiddleLeft,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(4, 0, 4, 0)
        };
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.MouseOverBackColor = back.HasValue
            ? ControlPaint.Light(back.Value, 0.08f)
            : Color.FromArgb(232, 240, 248);
        button.FlatAppearance.MouseDownBackColor = back.HasValue
            ? ControlPaint.Dark(back.Value, 0.04f)
            : Color.FromArgb(214, 230, 244);
        return button;
    }

    public static Label SectionLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Ui(9f, FontStyle.Bold),
        ForeColor = Color.FromArgb(52, 73, 94),
        Padding = new Padding(0, 3, 0, 3)
    };

    public static TextBox SearchBox(string placeholder)
    {
        var box = new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            Font = Ui(9f),
            ForeColor = Text,
            BackColor = Color.White
        };
        box.PlaceholderText = placeholder;
        return box;
    }

    public static void DrawCard(Graphics g, Rectangle rect, Color? fill = null, Color? border = null,
        int radius = 8)
    {
        using var path = RoundedRect(rect, radius);
        using var brush = new SolidBrush(fill ?? Panel);
        g.FillPath(brush, path);
        using var pen = new Pen(border ?? BorderLight);
        g.DrawPath(pen, path);
    }

    public static void DrawText(Graphics g, string text, Font font, Color color, Rectangle rect,
        TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter)
    {
        TextRenderer.DrawText(g, text, font, rect, color, flags | TextFormatFlags.NoPrefix);
    }

    public static string Ellipsis(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
        {
            return text;
        }
        return text[..Math.Max(0, max - 1)] + "…";
    }

    public static void ApplyToolStrip(ToolStrip strip)
    {
        strip.RenderMode = ToolStripRenderMode.Professional;
        strip.Renderer = new NvToolStripRenderer();
        strip.BackColor = HeaderBack;
        strip.ForeColor = Text;
        strip.Font = Ui(9f);
        strip.GripStyle = ToolStripGripStyle.Hidden;
        strip.Padding = new Padding(4, 2, 4, 2);
    }

    public static void ApplyMenu(MenuStrip menu)
    {
        menu.Renderer = new NvMenuRenderer();
        menu.BackColor = Color.FromArgb(248, 250, 252);
        menu.ForeColor = Text;
        menu.Font = Ui(9f);
        menu.Padding = new Padding(4, 2, 0, 2);
    }

    public static void DrawTriangle(Graphics g, Rectangle rect, Color color, bool down)
    {
        using var brush = new SolidBrush(color);
        var points = down
            ? new[]
            {
                new Point(rect.Left, rect.Top + 3),
                new Point(rect.Right, rect.Top + 3),
                new Point(rect.Left + rect.Width / 2, rect.Bottom - 2)
            }
            : new[]
            {
                new Point(rect.Left, rect.Bottom - 3),
                new Point(rect.Right, rect.Bottom - 3),
                new Point(rect.Left + rect.Width / 2, rect.Top + 2)
            };
        g.FillPolygon(brush, points);
    }

    private sealed class NvToolStripRenderer : ToolStripProfessionalRenderer
    {
        public NvToolStripRenderer() : base(new NvColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new LinearGradientBrush(
                e.ToolStrip.ClientRectangle, HeaderBack, HeaderBack2, LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(brush, e.ToolStrip.ClientRectangle);
            using var pen = new Pen(Border);
            e.Graphics.DrawLine(pen, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item is not ToolStripButton button)
            {
                base.OnRenderButtonBackground(e);
                return;
            }
            var rect = new Rectangle(0, 0, e.Item.Width - 1, e.Item.Height - 1);
            if (button.Pressed || button.Checked)
            {
                using var path = RoundedRect(rect, 4);
                using var brush = new SolidBrush(Color.FromArgb(206, 225, 242));
                e.Graphics.FillPath(brush, path);
                using var pen = new Pen(Color.FromArgb(150, 185, 214));
                e.Graphics.DrawPath(pen, path);
            }
            else if (button.Selected)
            {
                using var path = RoundedRect(rect, 4);
                using var brush = new SolidBrush(Color.FromArgb(222, 236, 248));
                e.Graphics.FillPath(brush, path);
                using var pen = new Pen(Color.FromArgb(177, 206, 232));
                e.Graphics.DrawPath(pen, path);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Text;
            base.OnRenderItemText(e);
        }
    }

    private sealed class NvMenuRenderer : ToolStripProfessionalRenderer
    {
        public NvMenuRenderer() : base(new NvColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected && !e.Item.Pressed)
            {
                return;
            }
            var rect = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
            using var brush = new SolidBrush(Color.FromArgb(214, 232, 246));
            e.Graphics.FillRectangle(brush, rect);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Text;
            base.OnRenderItemText(e);
        }
    }

    private sealed class NvColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(252, 253, 254);
        public override Color ImageMarginGradientBegin => Color.FromArgb(244, 247, 250);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(244, 247, 250);
        public override Color ImageMarginGradientEnd => Color.FromArgb(244, 247, 250);
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Color.FromArgb(157, 193, 227);
        public override Color MenuItemSelected => Color.FromArgb(214, 232, 246);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(214, 232, 246);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(214, 232, 246);
        public override Color SeparatorDark => Color.FromArgb(217, 225, 233);
        public override Color SeparatorLight => Color.White;
        public override Color ToolStripBorder => Border;
        public override Color StatusStripGradientBegin => HeaderBack;
        public override Color StatusStripGradientEnd => HeaderBack;
    }
}

/// <summary>SQL 语法高亮器（RichTextBox 使用）。</summary>
internal static class NvSqlHighlighter
{
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE", "CREATE",
        "TABLE", "DROP", "ALTER", "ADD", "COLUMN", "INDEX", "VIEW", "JOIN", "INNER", "LEFT", "RIGHT",
        "FULL", "OUTER", "CROSS", "ON", "GROUP", "BY", "ORDER", "HAVING", "LIMIT", "OFFSET", "AS",
        "AND", "OR", "NOT", "NULL", "IS", "IN", "EXISTS", "BETWEEN", "LIKE", "CASE", "WHEN", "THEN",
        "ELSE", "END", "DISTINCT", "UNION", "ALL", "TOP", "PRIMARY", "KEY", "FOREIGN", "REFERENCES",
        "CONSTRAINT", "DEFAULT", "UNIQUE", "CHECK", "BEGIN", "COMMIT", "ROLLBACK", "TRANSACTION",
        "DECLARE", "PROCEDURE", "FUNCTION", "RETURNS", "RETURN", "IF", "WHILE", "LOOP", "EXEC",
        "TRUNCATE", "GRANT", "REVOKE", "CAST", "CONVERT", "WITH", "OVER", "PARTITION", "ASC", "DESC",
        "USE", "SHOW", "DESCRIBE", "EXPLAIN", "ANALYZE", "OPTIMIZE", "RENAME", "TRIGGER", "DATABASE",
        "SCHEMA", "USER", "IDENTIFIED", "PASSWORD", "ENGINE", "CHARSET", "COLLATE", "AUTO_INCREMENT",
        "SERIAL", "BIGSERIAL", "BOOLEAN", "INTEGER", "INT", "BIGINT", "SMALLINT", "VARCHAR", "CHAR",
        "TEXT", "DATE", "DATETIME", "TIMESTAMP", "DECIMAL", "NUMERIC", "FLOAT", "DOUBLE", "BLOB",
        "JSON", "UUID", "CASCADE", "RESTRICT", "NO", "ACTION", "PRAGMA", "VACUUM", "ATTACH", "DETACH",
        "REPLACE", "IGNORE", "DUPLICATE", "TEMPORARY", "TEMP", "IFNULL", "COALESCE", "COUNT", "SUM",
        "AVG", "MIN", "MAX", "ROW_NUMBER", "RANK", "DENSE_RANK", "LAG", "LEAD", "NOW", "CURRENT_DATE"
    };

    private static readonly Color KeywordColor = Color.FromArgb(0, 84, 166);
    private static readonly Color StringColor = Color.FromArgb(163, 21, 21);
    private static readonly Color CommentColor = Color.FromArgb(0, 128, 96);
    private static readonly Color NumberColor = Color.FromArgb(128, 80, 0);
    private static readonly Color FunctionColor = Color.FromArgb(112, 60, 160);
    private static readonly Color NormalColor = Color.FromArgb(30, 41, 54);

    /// <summary>对编辑器全文着色；保留光标、选择和滚动位置。</summary>
    public static void Highlight(RichTextBox box)
    {
        if (box.IsDisposed || box.TextLength == 0 || box.TextLength > 400_000)
        {
            return;
        }

        int start = box.SelectionStart;
        int length = box.SelectionLength;
        var firstVisible = new NativeMethods.POINT();
        NativeMethods.SendMessage(box.Handle, NativeMethods.EM_GETSCROLLPOS, IntPtr.Zero, ref firstVisible);

        NativeMethods.SendMessage(box.Handle, NativeMethods.WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        try
        {
            box.SelectAll();
            box.SelectionColor = NormalColor;
            box.Select(0, 0);

            string text = box.Text;
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '\'' || c == '"' || c == '`')
                {
                    int end = FindStringEnd(text, i, c);
                    Colorize(box, i, end - i, StringColor);
                    i = end;
                }
                else if (c == '-' && i + 1 < text.Length && text[i + 1] == '-')
                {
                    int end = text.IndexOf('\n', i);
                    if (end < 0)
                    {
                        end = text.Length;
                    }
                    Colorize(box, i, end - i, CommentColor);
                    i = end;
                }
                else if (c == '#' && IsLineStart(text, i))
                {
                    int end = text.IndexOf('\n', i);
                    if (end < 0)
                    {
                        end = text.Length;
                    }
                    Colorize(box, i, end - i, CommentColor);
                    i = end;
                }
                else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    end = end < 0 ? text.Length : end + 2;
                    Colorize(box, i, end - i, CommentColor);
                    i = end;
                }
                else if (char.IsLetter(c) || c == '_' || c == '@')
                {
                    int startWord = i;
                    i++;
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '$'))
                    {
                        i++;
                    }
                    string word = text[startWord..i];
                    if (Keywords.Contains(word))
                    {
                        Colorize(box, startWord, i - startWord, KeywordColor);
                    }
                    else
                    {
                        int j = i;
                        while (j < text.Length && char.IsWhiteSpace(text[j]))
                        {
                            j++;
                        }
                        if (j < text.Length && text[j] == '(')
                        {
                            Colorize(box, startWord, i - startWord, FunctionColor);
                        }
                    }
                }
                else if (char.IsDigit(c))
                {
                    int startNum = i;
                    i++;
                    while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.' || text[i] == 'x'
                        || "ABCDEFabcdef".Contains(text[i])))
                    {
                        i++;
                    }
                    Colorize(box, startNum, i - startNum, NumberColor);
                }
                else
                {
                    i++;
                }
            }
        }
        finally
        {
            NativeMethods.SendMessage(box.Handle, NativeMethods.WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
            box.Select(start, length);
            NativeMethods.SendMessage(box.Handle, NativeMethods.EM_SETSCROLLPOS, IntPtr.Zero, ref firstVisible);
            box.Invalidate();
        }
    }

    private static bool IsLineStart(string text, int index)
    {
        int i = index - 1;
        while (i >= 0 && text[i] != '\n')
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                return false;
            }
            i--;
        }
        return true;
    }

    private static int FindStringEnd(string text, int start, char quote)
    {
        int i = start + 1;
        while (i < text.Length)
        {
            if (text[i] == quote)
            {
                if (i + 1 < text.Length && text[i + 1] == quote)
                {
                    i += 2;
                    continue;
                }
                return i + 1;
            }
            i++;
        }
        return text.Length;
    }

    private static void Colorize(RichTextBox box, int start, int length, Color color)
    {
        if (length <= 0)
        {
            return;
        }
        box.Select(start, length);
        box.SelectionColor = color;
    }

    private static class NativeMethods
    {
        public const int WM_SETREDRAW = 0x000B;
        public const int EM_GETSCROLLPOS = 0x0400 + 221;
        public const int EM_SETSCROLLPOS = 0x0400 + 222;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref POINT lParam);
    }
}
