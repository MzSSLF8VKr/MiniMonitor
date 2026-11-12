using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace MiniMonitor.Navicat;

/// <summary>ER 图：可视化表结构与外键关系，支持拖动、缩放和导出 PNG。</summary>
internal sealed class NvErDiagramForm : Form
{
    private sealed class ErTable
    {
        public TableInfo Table { get; init; } = new();
        public List<ColumnInfo> Columns { get; init; } = new();
        public List<ForeignKeyInfo> ForeignKeys { get; init; } = new();
        public Rectangle Bounds { get; set; }
    }

    private readonly DbSession _session;
    private readonly ErCanvas _canvas;
    private readonly Label _status;

    public NvErDiagramForm(DbSession session)
    {
        _session = session;
        Text = "ER 图 - " + session.Profile.Name + " / " + session.CurrentDatabase;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1100, 720);
        MinimumSize = new Size(820, 560);
        BackColor = NvTheme.Background;
        Font = NvTheme.Ui(9f);
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;

        var header = SyncUi.Header("ER 图", session.Profile.Name + " · " + session.Kind.ShortName()
            + " · " + session.CurrentDatabase, NvIcon.Er);

        var toolbar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = NvTheme.Panel };
        var refresh = NvTheme.FlatButton("刷新", null, null, 76);
        refresh.Location = new Point(10, 6);
        refresh.Click += async (_, _) => await LoadDiagramAsync();
        var fit = NvTheme.FlatButton("适应窗口", null, null, 92);
        fit.Location = new Point(92, 6);
        fit.Click += (_, _) => _canvas.FitToWindow();
        var zoomIn = NvTheme.FlatButton("放大", null, null, 76);
        zoomIn.Location = new Point(190, 6);
        zoomIn.Click += (_, _) => _canvas.ZoomBy(1.2f);
        var zoomOut = NvTheme.FlatButton("缩小", null, null, 76);
        zoomOut.Location = new Point(272, 6);
        zoomOut.Click += (_, _) => _canvas.ZoomBy(1 / 1.2f);
        var layout = NvTheme.FlatButton("自动布局", null, null, 92);
        layout.Location = new Point(354, 6);
        layout.Click += (_, _) => _canvas.AutoLayout();
        var export = NvTheme.FlatButton("导出 PNG", NvTheme.Accent, Color.White, 104);
        export.Location = new Point(452, 6);
        export.Click += (_, _) => ExportPng();
        toolbar.Controls.AddRange(new Control[] { refresh, fit, zoomIn, zoomOut, layout, export });

        _canvas = new ErCanvas { Dock = DockStyle.Fill };
        _canvas.StatusChanged += (_, text) => _status.Text = text;

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = NvTheme.HeaderBack };
        _status = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = NvTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
            Text = "正在加载表结构…"
        };
        bottom.Controls.Add(_status);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 0) };
        body.Controls.Add(_canvas);
        Controls.Add(body);
        Controls.Add(bottom);
        Controls.Add(toolbar);
        Controls.Add(header);
        Load += async (_, _) => await LoadDiagramAsync();
    }

    private async Task LoadDiagramAsync()
    {
        _status.Text = "正在读取表、列与外键…";
        try
        {
            var tables = await Task.Run(() =>
            {
                var result = new List<ErTable>();
                var baseTables = _session.GetTables(_session.CurrentDatabase);
                foreach (var table in baseTables)
                {
                    var columns = _session.GetColumns(table);
                    var fks = _session.GetForeignKeys(table);
                    result.Add(new ErTable
                    {
                        Table = table,
                        Columns = columns,
                        ForeignKeys = fks
                    });
                }
                return result;
            });
            _canvas.SetTables(tables);
            _canvas.FitToWindow();
            _status.Text = "共 " + tables.Count + " 张表 · 拖动表可调整位置 · 滚轮缩放 · 右键拖动平移";
        }
        catch (Exception ex)
        {
            _status.Text = "加载失败：" + ex.Message;
            NvLog.Error(ex, "工具异常：ER 图");
            NvPrompt.Error(this, "ER 图", ex.Message);
        }
    }

    private void ExportPng()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "导出 ER 图",
            Filter = "PNG 图片 (*.png)|*.png|所有文件 (*.*)|*.*",
            FileName = "er_diagram_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        try
        {
            _canvas.ExportPng(dialog.FileName);
            NvPrompt.Info(this, "ER 图", "已导出到：\n" + dialog.FileName);
        }
        catch (Exception ex)
        {
            NvLog.Error(ex, "工具异常：导出 ER 图");
            NvPrompt.Error(this, "导出 ER 图", ex.Message);
        }
    }

    private sealed class ErCanvas : Control
    {
        private const int NodeWidth = 250;
        private const int HeaderHeight = 30;
        private const int RowHeight = 21;
        private readonly List<ErTable> _tables = new();
        private float _zoom = 1f;
        private PointF _pan;
        private ErTable? _drag;
        private PointF _dragStart;
        private PointF _dragOrigin;
        private bool _panning;
        private Point _panStart;

        public event EventHandler<string>? StatusChanged;

        public ErCanvas()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(247, 250, 253);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            MouseWheel += (_, e) =>
            {
                float old = _zoom;
                float factor = e.Delta > 0 ? 1.12f : 1 / 1.12f;
                float newZoom = Math.Clamp(old * factor, 0.25f, 3f);
                var world = ToWorld(e.Location);
                _zoom = newZoom;
                var after = ToScreen(world);
                _pan = new PointF(_pan.X + (e.X - after.X), _pan.Y + (e.Y - after.Y));
                Invalidate();
            };
        }

        public void SetTables(List<ErTable> tables)
        {
            _tables.Clear();
            _tables.AddRange(tables);
            AutoLayout();
        }

        public void AutoLayout()
        {
            int columns = Math.Max(1, Math.Min(6, (int)Math.Sqrt(Math.Max(1, _tables.Count)) + 1));
            int x = 40, y = 40;
            int maxHeight = 0;
            for (int i = 0; i < _tables.Count; i++)
            {
                var table = _tables[i];
                int rows = Math.Min(table.Columns.Count, 14);
                int height = HeaderHeight + rows * RowHeight + (table.Columns.Count > 14 ? RowHeight : 0) + 10;
                table.Bounds = new Rectangle(x, y, NodeWidth, height);
                maxHeight = Math.Max(maxHeight, height);
                if ((i + 1) % columns == 0)
                {
                    x = 40;
                    y += maxHeight + 40;
                    maxHeight = 0;
                }
                else
                {
                    x += NodeWidth + 60;
                }
            }
            Invalidate();
        }

        private Rectangle ContentBounds()
        {
            if (_tables.Count == 0)
            {
                return new Rectangle(0, 0, 800, 600);
            }
            int right = _tables.Max(t => t.Bounds.Right) + 40;
            int bottom = _tables.Max(t => t.Bounds.Bottom) + 40;
            return new Rectangle(0, 0, Math.Max(800, right), Math.Max(600, bottom));
        }

        public void FitToWindow()
        {
            if (_tables.Count == 0)
            {
                _zoom = 1;
                _pan = PointF.Empty;
                Invalidate();
                return;
            }
            var content = ContentBounds();
            float zoomX = (Width - 40f) / content.Width;
            float zoomY = (Height - 40f) / content.Height;
            _zoom = Math.Clamp(Math.Min(zoomX, zoomY), 0.2f, 1.5f);
            _pan = new PointF(20 - content.Left * _zoom, 20 - content.Top * _zoom);
            Invalidate();
        }

        public void ZoomBy(float factor)
        {
            _zoom = Math.Clamp(_zoom * factor, 0.2f, 3f);
            Invalidate();
        }

        private PointF ToWorld(Point point)
        {
            return new PointF((point.X - _pan.X) / _zoom, (point.Y - _pan.Y) / _zoom);
        }

        private PointF ToScreen(PointF world)
        {
            return new PointF(world.X * _zoom + _pan.X, world.Y * _zoom + _pan.Y);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            DrawGrid(g);
            g.TranslateTransform(_pan.X, _pan.Y);
            g.ScaleTransform(_zoom, _zoom);

            foreach (var table in _tables)
            {
                DrawRelations(g, table);
            }
            foreach (var table in _tables)
            {
                DrawTable(g, table);
            }
        }

        private void DrawGrid(Graphics g)
        {
            using var brush = new SolidBrush(Color.FromArgb(20, 150, 180, 210));
            for (int x = (int)_pan.X % 40; x < Width; x += 40)
            {
                for (int y = (int)_pan.Y % 40; y < Height; y += 40)
                {
                    g.FillRectangle(brush, x, y, 2, 2);
                }
            }
        }

        private void DrawRelations(Graphics g, ErTable table)
        {
            foreach (var fk in table.ForeignKeys)
            {
                var target = FindTable(fk.RefTable);
                if (target == null || ReferenceEquals(target, table))
                {
                    continue;
                }
                var from = new PointF(table.Bounds.Right, table.Bounds.Top + 18);
                var to = new PointF(target.Bounds.Left, target.Bounds.Top + 18);
                if (target.Bounds.Left > table.Bounds.Right)
                {
                    // 正常向右
                }
                else
                {
                    from = new PointF(table.Bounds.Left, table.Bounds.Top + 18);
                    to = new PointF(target.Bounds.Right, target.Bounds.Top + 18);
                }
                float midX = (from.X + to.X) / 2;
                using var path = new GraphicsPath();
                path.AddBezier(from, new PointF(midX, from.Y), new PointF(midX, to.Y), to);
                using var pen = new Pen(Color.FromArgb(120, 110, 150, 190), 1.3f);
                g.DrawPath(pen, path);
                using var arrow = new SolidBrush(Color.FromArgb(150, 90, 130, 175));
                var tip = to;
                var dir = from.X <= to.X ? 1 : -1;
                g.FillPolygon(arrow, new[]
                {
                    new PointF(tip.X, tip.Y),
                    new PointF(tip.X - dir * 8, tip.Y - 4),
                    new PointF(tip.X - dir * 8, tip.Y + 4)
                });
            }
        }

        private ErTable? FindTable(string reference)
        {
            string name = reference;
            int dot = name.LastIndexOf('.');
            if (dot >= 0)
            {
                name = name[(dot + 1)..];
            }
            name = name.Trim('"', '[', ']', '`');
            return _tables.FirstOrDefault(t => t.Table.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private void DrawTable(Graphics g, ErTable table)
        {
            var rect = table.Bounds;
            using (var shadow = new SolidBrush(Color.FromArgb(22, 0, 0, 0)))
            {
                g.FillRectangle(shadow, rect.X + 4, rect.Y + 4, rect.Width, rect.Height);
            }
            using (var border = new Pen(Color.FromArgb(150, 175, 200), 1.2f))
            using (var body = new SolidBrush(Color.White))
            {
                g.FillRectangle(body, rect);
                g.DrawRectangle(border, rect);
            }
            using (var headerBrush = new LinearGradientBrush(
                new Rectangle(rect.X, rect.Y, rect.Width, HeaderHeight),
                Color.FromArgb(43, 108, 176), Color.FromArgb(58, 131, 200), LinearGradientMode.Horizontal))
            {
                g.FillRectangle(headerBrush, rect.X, rect.Y, rect.Width, HeaderHeight);
            }
            using var titleFont = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            using var titleBrush = new SolidBrush(Color.White);
            using var titleFormat = new StringFormat
            {
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };
            g.DrawString(table.Table.Name, titleFont, titleBrush,
                new RectangleF(rect.X + 8, rect.Y + 2, rect.Width - 16, HeaderHeight - 2), titleFormat);

            int y = rect.Y + HeaderHeight;
            var columns = table.Columns.Take(14).ToList();
            using var nameFont = new Font("Microsoft YaHei UI", 8.2f);
            using var typeFont = new Font("Consolas", 7.6f);
            using var nameBrush = new SolidBrush(NvTheme.Text);
            using var pkBrush = new SolidBrush(Color.FromArgb(40, 60, 85));
            using var typeBrush = new SolidBrush(NvTheme.Muted);
            using var leftFormat = new StringFormat
            {
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };
            using var rightFormat = new StringFormat
            {
                LineAlignment = StringAlignment.Center,
                Alignment = StringAlignment.Far,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };
            foreach (var column in columns)
            {
                if (column.PrimaryKey)
                {
                    g.DrawImage(NvIcons.Get(NvIcon.Key, 12), rect.X + 6, y + 4, 12, 12);
                }
                else
                {
                    g.DrawImage(NvIcons.Get(NvIcon.Column, 11), rect.X + 7, y + 5, 11, 11);
                }
                g.DrawString(column.Name, nameFont, column.PrimaryKey ? pkBrush : nameBrush,
                    new RectangleF(rect.X + 22, y, rect.Width - 118, RowHeight), leftFormat);
                g.DrawString(column.DisplayType, typeFont, typeBrush,
                    new RectangleF(rect.X + rect.Width - 96, y, 90, RowHeight), rightFormat);
                using var line = new Pen(Color.FromArgb(238, 242, 247));
                g.DrawLine(line, rect.X + 1, y + RowHeight - 1, rect.Right - 1, y + RowHeight - 1);
                y += RowHeight;
            }
            if (table.Columns.Count > columns.Count)
            {
                g.DrawString("… 还有 " + (table.Columns.Count - columns.Count) + " 列", nameFont,
                    typeBrush, new RectangleF(rect.X + 22, y, rect.Width - 30, RowHeight), leftFormat);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var world = ToWorld(e.Location);
            if (e.Button == MouseButtons.Left)
            {
                _drag = _tables.FirstOrDefault(t => t.Bounds.Contains((int)world.X, (int)world.Y));
                if (_drag != null)
                {
                    _dragStart = world;
                    _dragOrigin = _drag.Bounds.Location;
                    Cursor = Cursors.SizeAll;
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                _panning = true;
                _panStart = e.Location;
                Cursor = Cursors.Hand;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag != null && e.Button == MouseButtons.Left)
            {
                var world = ToWorld(e.Location);
                int x = (int)(_dragOrigin.X + world.X - _dragStart.X);
                int y = (int)(_dragOrigin.Y + world.Y - _dragStart.Y);
                _drag.Bounds = new Rectangle(x, y, _drag.Bounds.Width, _drag.Bounds.Height);
                Invalidate();
            }
            else if (_panning && e.Button == MouseButtons.Right)
            {
                _pan = new PointF(_pan.X + e.X - _panStart.X, _pan.Y + e.Y - _panStart.Y);
                _panStart = e.Location;
                Invalidate();
            }
            else if (!_panning && _drag == null)
            {
                var world = ToWorld(e.Location);
                Cursor = _tables.Any(t => t.Bounds.Contains((int)world.X, (int)world.Y))
                    ? Cursors.SizeAll
                    : Cursors.Default;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_drag != null)
            {
                StatusChanged?.Invoke(this, "已移动表 " + _drag.Table.Name);
                _drag = null;
            }
            _panning = false;
            Cursor = Cursors.Default;
        }

        public void ExportPng(string path)
        {
            var content = ContentBounds();
            using var bitmap = new Bitmap(content.Width, content.Height);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.FromArgb(247, 250, 253));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                foreach (var table in _tables)
                {
                    DrawRelations(g, table);
                }
                foreach (var table in _tables)
                {
                    DrawTable(g, table);
                }
            }
            bitmap.Save(path, ImageFormat.Png);
        }
    }
}
