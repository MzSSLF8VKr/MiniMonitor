using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MiniMonitor.Navicat;

internal enum NvIcon
{
    Database, Table, View, Function, Query, User, Backup, Schedule, Report, Transfer, Sync,
    Structure, Er, Search, Connect, NewConnection, Server, Folder, Column, Key, Index, Trigger,
    Sqlite, MySQL, PostgreSQL, SqlServer, Oracle, Logo, Export, Import, Refresh, Save, Run,
    Stop, Format, Add, Delete, Edit, Filter, Close, FolderOpen, Check, Warning, Info, Link,
    Chart, File, Open, Print, Lock, Unlock, Copy, Rename, Truncate, Drop, SqlFile
}

/// <summary>用 GDI+ 矢量绘制 Navicat 风格的小图标，避免额外资源依赖。</summary>
internal static class NvIcons
{
    private static readonly Dictionary<string, Bitmap> Cache = new();

    private static readonly Color Blue = Color.FromArgb(43, 108, 176);
    private static readonly Color Green = Color.FromArgb(46, 139, 87);
    private static readonly Color Orange = Color.FromArgb(231, 126, 35);
    private static readonly Color Purple = Color.FromArgb(122, 86, 172);
    private static readonly Color Red = Color.FromArgb(190, 60, 60);
    private static readonly Color Gray = Color.FromArgb(112, 124, 138);
    private static readonly Color Dark = Color.FromArgb(58, 72, 88);

    public static Bitmap Get(NvIcon icon, int size = 16, Color? tint = null)
    {
        string key = icon + "|" + size + "|" + (tint?.ToArgb().ToString() ?? "-");
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float s = size / 16f;
            g.ScaleTransform(s, s);
            Draw(g, icon, tint);
        }
        Cache[key] = bmp;
        return bmp;
    }

    private static void Draw(Graphics g, NvIcon icon, Color? tint)
    {
        Color c = tint ?? Dark;
        switch (icon)
        {
            case NvIcon.Database:
                DrawDatabase(g, tint ?? Blue);
                break;
            case NvIcon.Table:
                DrawTable(g, tint ?? Blue);
                break;
            case NvIcon.View:
                DrawView(g, tint ?? Green);
                break;
            case NvIcon.Function:
                DrawLetter(g, "f", tint ?? Purple, 11);
                break;
            case NvIcon.Query:
                DrawDoc(g, "SQL", tint ?? Orange);
                break;
            case NvIcon.SqlFile:
                DrawDoc(g, "SQL", tint ?? Blue);
                break;
            case NvIcon.User:
                DrawUser(g, tint ?? Blue);
                break;
            case NvIcon.Backup:
                DrawBackup(g, tint ?? Orange);
                break;
            case NvIcon.Schedule:
                DrawClock(g, tint ?? Orange);
                break;
            case NvIcon.Report:
                DrawChart(g, tint ?? Green);
                break;
            case NvIcon.Transfer:
                DrawTransfer(g, tint ?? Blue);
                break;
            case NvIcon.Sync:
                DrawSync(g, tint ?? Green);
                break;
            case NvIcon.Structure:
                DrawStructure(g, tint ?? Purple);
                break;
            case NvIcon.Er:
                DrawEr(g, tint ?? Blue);
                break;
            case NvIcon.Search:
                DrawSearch(g, tint ?? Gray);
                break;
            case NvIcon.Connect:
                DrawPlug(g, tint ?? Green);
                break;
            case NvIcon.NewConnection:
                DrawPlug(g, tint ?? Blue, true);
                break;
            case NvIcon.Server:
                DrawServer(g, tint ?? Gray);
                break;
            case NvIcon.Folder:
                DrawFolder(g, tint ?? Color.FromArgb(220, 170, 70));
                break;
            case NvIcon.FolderOpen:
                DrawFolder(g, tint ?? Color.FromArgb(220, 170, 70), true);
                break;
            case NvIcon.Column:
                DrawColumn(g, tint ?? Gray);
                break;
            case NvIcon.Key:
                DrawKey(g, tint ?? Orange);
                break;
            case NvIcon.Index:
                DrawIndex(g, tint ?? Blue);
                break;
            case NvIcon.Trigger:
                DrawBolt(g, tint ?? Orange);
                break;
            case NvIcon.Sqlite:
                DrawDatabase(g, Color.FromArgb(60, 130, 190), "SQLite");
                break;
            case NvIcon.MySQL:
                DrawDatabase(g, Color.FromArgb(0, 117, 143), "My");
                break;
            case NvIcon.PostgreSQL:
                DrawDatabase(g, Color.FromArgb(51, 103, 145), "P");
                break;
            case NvIcon.SqlServer:
                DrawDatabase(g, Color.FromArgb(190, 60, 60), "S");
                break;
            case NvIcon.Oracle:
                DrawDatabase(g, Color.FromArgb(200, 50, 50), "O");
                break;
            case NvIcon.Logo:
                DrawLogo(g);
                break;
            case NvIcon.Export:
                DrawArrow(g, c, true);
                break;
            case NvIcon.Import:
                DrawArrow(g, c, false);
                break;
            case NvIcon.Refresh:
                DrawRefresh(g, tint ?? Blue);
                break;
            case NvIcon.Save:
                DrawSave(g, tint ?? Blue);
                break;
            case NvIcon.Run:
                using (var brush = new SolidBrush(tint ?? Green))
                {
                    g.FillPolygon(brush, new[] { new PointF(5, 3), new PointF(5, 13), new PointF(13, 8) });
                }
                break;
            case NvIcon.Stop:
                using (var brush = new SolidBrush(tint ?? Red))
                {
                    g.FillRectangle(brush, 4, 4, 8, 8);
                }
                break;
            case NvIcon.Format:
                DrawFormat(g, tint ?? Purple);
                break;
            case NvIcon.Add:
                DrawPlus(g, tint ?? Green);
                break;
            case NvIcon.Delete:
                DrawCross(g, tint ?? Red);
                break;
            case NvIcon.Edit:
                DrawPencil(g, tint ?? Blue);
                break;
            case NvIcon.Filter:
                DrawFilter(g, tint ?? Blue);
                break;
            case NvIcon.Close:
                DrawCross(g, tint ?? Gray);
                break;
            case NvIcon.Check:
                DrawCheck(g, tint ?? Green);
                break;
            case NvIcon.Warning:
                DrawWarning(g, tint ?? Orange);
                break;
            case NvIcon.Info:
                DrawInfo(g, tint ?? Blue);
                break;
            case NvIcon.Link:
                DrawLink(g, tint ?? Blue);
                break;
            case NvIcon.Chart:
                DrawChart(g, tint ?? Green);
                break;
            case NvIcon.File:
                DrawDoc(g, "", tint ?? Gray);
                break;
            case NvIcon.Open:
                DrawFolder(g, tint ?? Color.FromArgb(220, 170, 70), true);
                break;
            case NvIcon.Print:
                DrawPrint(g, tint ?? Gray);
                break;
            case NvIcon.Lock:
                DrawLock(g, tint ?? Gray, true);
                break;
            case NvIcon.Unlock:
                DrawLock(g, tint ?? Gray, false);
                break;
            case NvIcon.Copy:
                DrawCopy(g, tint ?? Blue);
                break;
            case NvIcon.Rename:
                DrawPencil(g, tint ?? Orange);
                break;
            case NvIcon.Truncate:
                DrawBroom(g, tint ?? Orange);
                break;
            case NvIcon.Drop:
                DrawCross(g, tint ?? Red);
                break;
            default:
                DrawDoc(g, "", c);
                break;
        }
    }

    private static void DrawDatabase(Graphics g, Color color, string label = "")
    {
        using var pen = new Pen(color, 1.5f) { LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(Color.FromArgb(55, color));
        var body = new RectangleF(2.5f, 3, 11, 10.5f);
        g.FillRectangle(brush, body);
        g.DrawRectangle(pen, body.X, body.Y, body.Width, body.Height);
        g.DrawEllipse(pen, body.X, 1.6f, body.Width, 3.4f);
        using var topBrush = new SolidBrush(Color.FromArgb(110, color));
        g.FillEllipse(topBrush, body.X, 1.6f, body.Width, 3.4f);
        g.DrawArc(pen, body.X, body.Y + 3.6f, body.Width, 3, 0, 180);
        g.DrawArc(pen, body.X, body.Y + 7, body.Width, 3, 0, 180);
        if (!string.IsNullOrEmpty(label))
        {
            using var font = new Font("Arial", label.Length > 2 ? 3.4f : 4.2f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString(label, font, textBrush, new RectangleF(body.X, body.Y + 2, body.Width, body.Height), format);
        }
    }

    private static void DrawTable(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.4f);
        using var head = new SolidBrush(Color.FromArgb(70, color));
        g.FillRectangle(head, 2, 2.5f, 12, 3.4f);
        g.DrawRectangle(pen, 2, 2.5f, 12, 11);
        g.DrawLine(pen, 2, 5.9f, 14, 5.9f);
        g.DrawLine(pen, 2, 9, 14, 9);
        g.DrawLine(pen, 2, 11.3f, 14, 11.3f);
        g.DrawLine(pen, 6.8f, 5.9f, 6.8f, 13.5f);
        g.DrawLine(pen, 10.6f, 5.9f, 10.6f, 13.5f);
    }

    private static void DrawView(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.5f);
        using var path = new GraphicsPath();
        path.AddBezier(1.5f, 8, 5, 3.2f, 11, 3.2f, 14.5f, 8);
        path.AddBezier(14.5f, 8, 11, 12.8f, 5, 12.8f, 1.5f, 8);
        g.DrawPath(pen, path);
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 6, 6, 4, 4);
    }

    private static void DrawUser(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.5f);
        using var brush = new SolidBrush(Color.FromArgb(50, color));
        g.FillEllipse(brush, 5.4f, 2.2f, 5.2f, 5.2f);
        g.DrawEllipse(pen, 5.4f, 2.2f, 5.2f, 5.2f);
        var path = new GraphicsPath();
        path.AddBezier(2.2f, 14.2f, 2.8f, 10.2f, 6, 8.6f, 8, 8.6f);
        path.AddBezier(8, 8.6f, 10, 8.6f, 13.2f, 10.2f, 13.8f, 14.2f);
        g.FillPath(brush, path);
        g.DrawPath(pen, path);
    }

    private static void DrawBackup(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.5f);
        using var brush = new SolidBrush(Color.FromArgb(45, color));
        var box = new RectangleF(2.5f, 6, 11, 7.5f);
        g.FillRectangle(brush, box);
        g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
        g.DrawLine(pen, 2.5f, 9, 13.5f, 9);
        using var arrow = new Pen(color, 1.5f) { EndCap = LineCap.ArrowAnchor };
        g.DrawLine(arrow, 8, 1.5f, 8, 5.5f);
    }

    private static void DrawClock(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.5f);
        using var brush = new SolidBrush(Color.FromArgb(35, color));
        g.FillEllipse(brush, 2, 2, 12, 12);
        g.DrawEllipse(pen, 2, 2, 12, 12);
        g.DrawLine(pen, 8, 4.3f, 8, 8);
        g.DrawLine(pen, 8, 8, 11, 9.5f);
    }

    private static void DrawChart(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.4f);
        using var brush = new SolidBrush(Color.FromArgb(80, color));
        g.DrawRectangle(pen, 2, 2.5f, 12, 11);
        g.FillRectangle(brush, 4, 8, 2, 3.5f);
        g.FillRectangle(brush, 7, 5.5f, 2, 6);
        g.FillRectangle(brush, 10, 7, 2, 4.5f);
    }

    private static void DrawTransfer(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.5f) { EndCap = LineCap.ArrowAnchor };
        g.DrawLine(pen, 2, 5, 12.5f, 5);
        g.DrawLine(pen, 13.5f, 11, 3, 11);
        using var pen2 = new Pen(color, 1.5f) { EndCap = LineCap.ArrowAnchor };
        g.DrawLine(pen2, 12.5f, 12.8f, 12.5f, 8);
        g.DrawLine(pen2, 3.5f, 3.2f, 3.5f, 8);
    }

    private static void DrawSync(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.5f) { EndCap = LineCap.ArrowAnchor };
        g.DrawArc(pen, 2.4f, 2.4f, 11.2f, 11.2f, 20, 140);
        g.DrawArc(pen, 2.4f, 2.4f, 11.2f, 11.2f, 200, 140);
    }

    private static void DrawStructure(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.3f);
        g.DrawRectangle(pen, 1.5f, 2, 5, 4);
        g.DrawRectangle(pen, 9.5f, 2, 5, 4);
        g.DrawRectangle(pen, 5.5f, 10, 5, 4);
        g.DrawLine(pen, 4, 6, 4, 8);
        g.DrawLine(pen, 12, 6, 12, 8);
        g.DrawLine(pen, 4, 8, 12, 8);
        g.DrawLine(pen, 8, 8, 8, 10);
    }

    private static void DrawEr(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.2f);
        using var brush = new SolidBrush(Color.FromArgb(45, color));
        g.FillRectangle(brush, 1.5f, 1.5f, 6, 4.5f);
        g.DrawRectangle(pen, 1.5f, 1.5f, 6, 4.5f);
        g.FillRectangle(brush, 8.5f, 9.5f, 6, 4.5f);
        g.DrawRectangle(pen, 8.5f, 9.5f, 6, 4.5f);
        g.DrawLine(pen, 4.5f, 6, 4.5f, 12);
        g.DrawLine(pen, 4.5f, 12, 8.5f, 12);
    }

    private static void DrawSearch(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.6f);
        g.DrawEllipse(pen, 2, 2, 8.5f, 8.5f);
        using var thick = new Pen(color, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(thick, 9.6f, 9.6f, 14, 14);
    }

    private static void DrawPlug(Graphics g, Color color, bool plus = false)
    {
        using var pen = new Pen(color, 1.5f) { LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(Color.FromArgb(55, color));
        g.FillRectangle(brush, 4.5f, 6, 7, 6);
        g.DrawRectangle(pen, 4.5f, 6, 7, 6);
        g.DrawLine(pen, 6, 6, 6, 3);
        g.DrawLine(pen, 10, 6, 10, 3);
        g.DrawLine(pen, 8, 12, 8, 14.5f);
        if (plus)
        {
            using var addPen = new Pen(Color.FromArgb(46, 139, 87), 2f);
            g.DrawLine(addPen, 11, 2, 15, 2);
            g.DrawLine(addPen, 13, 0, 13, 4);
        }
    }

    private static void DrawServer(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.4f);
        using var brush = new SolidBrush(Color.FromArgb(40, color));
        for (int i = 0; i < 3; i++)
        {
            float y = 2 + i * 4.2f;
            g.FillRectangle(brush, 2, y, 12, 3.4f);
            g.DrawRectangle(pen, 2, y, 12, 3.4f);
            using var dot = new SolidBrush(color);
            g.FillEllipse(dot, 10.6f, y + 1, 1.8f, 1.8f);
        }
    }

    private static void DrawFolder(Graphics g, Color color, bool open = false)
    {
        using var pen = new Pen(ControlPaint.Dark(color, 0.15f), 1.2f);
        using var brush = new SolidBrush(color);
        using var light = new SolidBrush(ControlPaint.Light(color, 0.25f));
        var path = new GraphicsPath();
        path.AddLine(1.5f, 4, 6, 4);
        path.AddLine(7.2f, 5.4f, 14.5f, 5.4f);
        path.AddLine(14.5f, 13, 1.5f, 13);
        path.CloseFigure();
        g.FillPath(brush, path);
        g.DrawPath(pen, path);
        g.FillRectangle(light, 1.5f, 3, 5, 2);
        if (open)
        {
            using var white = new SolidBrush(Color.FromArgb(120, Color.White));
            g.FillRectangle(white, 3, 6.5f, 10, 5.5f);
            using var openPen = new Pen(ControlPaint.Dark(color, 0.15f), 1f);
            g.DrawRectangle(openPen, 3, 6.5f, 10, 5.5f);
        }
    }

    private static void DrawColumn(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.3f);
        g.DrawRectangle(pen, 2, 2, 12, 12);
        g.DrawLine(pen, 2, 5.5f, 14, 5.5f);
        using var brush = new SolidBrush(Color.FromArgb(70, color));
        g.FillRectangle(brush, 3, 7, 10, 2);
        g.FillRectangle(brush, 3, 10.5f, 7, 2);
    }

    private static void DrawKey(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.5f);
        g.DrawEllipse(pen, 1.5f, 4.5f, 5.5f, 5.5f);
        g.DrawLine(pen, 6.6f, 7.2f, 14, 7.2f);
        g.DrawLine(pen, 11.5f, 7.2f, 11.5f, 10);
        g.DrawLine(pen, 14, 7.2f, 14, 9.5f);
    }

    private static void DrawIndex(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.3f);
        using var brush = new SolidBrush(Color.FromArgb(55, color));
        for (int i = 0; i < 3; i++)
        {
            g.FillRectangle(brush, 2, 2 + i * 4, 12, 2.6f);
            g.DrawRectangle(pen, 2, 2 + i * 4, 12, 2.6f);
        }
    }

    private static void DrawBolt(Graphics g, Color color)
    {
        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, new[]
        {
            new PointF(9, 1), new PointF(4, 9), new PointF(7.5f, 9), new PointF(6, 15),
            new PointF(12, 6.5f), new PointF(8.5f, 6.5f)
        });
    }

    private static void DrawDoc(Graphics g, string label, Color color)
    {
        using var pen = new Pen(color, 1.4f);
        using var brush = new SolidBrush(Color.FromArgb(30, color));
        var path = new GraphicsPath();
        path.AddLine(3, 1.5f, 10, 1.5f);
        path.AddLine(13, 4.5f, 13, 14.5f);
        path.AddLine(13, 14.5f, 3, 14.5f);
        path.CloseFigure();
        g.FillPath(brush, path);
        g.DrawPath(pen, path);
        g.DrawLine(pen, 10, 1.5f, 10, 4.5f);
        g.DrawLine(pen, 10, 4.5f, 13, 4.5f);
        if (label.Length > 0)
        {
            using var font = new Font("Arial", 2.9f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(color);
            var format = new StringFormat { Alignment = StringAlignment.Center };
            g.DrawString(label, font, textBrush, new RectangleF(3, 6.5f, 10, 5), format);
        }
        else
        {
            using var line = new Pen(Color.FromArgb(120, color), 1f);
            g.DrawLine(line, 4.8f, 7, 11, 7);
            g.DrawLine(line, 4.8f, 9.5f, 11, 9.5f);
            g.DrawLine(line, 4.8f, 12, 8.5f, 12);
        }
    }

    private static void DrawArrow(Graphics g, Color color, bool up)
    {
        using var pen = new Pen(color, 1.4f);
        using var brush = new SolidBrush(Color.FromArgb(55, color));
        g.FillRectangle(brush, 2, 6, 7, 5);
        g.DrawRectangle(pen, 2, 6, 7, 5);
        using var arrow = new Pen(color, 1.8f) { EndCap = LineCap.ArrowAnchor };
        if (up)
        {
            g.DrawLine(arrow, 11.5f, 8.5f, 11.5f, 2.5f);
        }
        else
        {
            g.DrawLine(arrow, 11.5f, 7.5f, 11.5f, 13.5f);
        }
    }

    private static void DrawRefresh(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.6f) { EndCap = LineCap.ArrowAnchor };
        g.DrawArc(pen, 2.5f, 2.5f, 11, 11, 30, 285);
    }

    private static void DrawSave(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.4f);
        using var brush = new SolidBrush(Color.FromArgb(50, color));
        var rect = new RectangleF(2, 2, 12, 12);
        g.FillRectangle(brush, rect);
        g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
        using var white = new SolidBrush(Color.White);
        g.FillRectangle(white, 4.5f, 2.5f, 7, 4);
        g.DrawRectangle(pen, 4.5f, 9, 7, 4.5f);
    }

    private static void DrawFormat(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.3f);
        g.DrawLine(pen, 2, 3.5f, 14, 3.5f);
        g.DrawLine(pen, 4, 6.8f, 14, 6.8f);
        g.DrawLine(pen, 2, 10.1f, 14, 10.1f);
        g.DrawLine(pen, 4, 13.4f, 14, 13.4f);
    }

    private static void DrawPlus(Graphics g, Color color)
    {
        using var pen = new Pen(color, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, 8, 2.5f, 8, 13.5f);
        g.DrawLine(pen, 2.5f, 8, 13.5f, 8);
    }

    private static void DrawCross(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(pen, 4, 4, 12, 12);
        g.DrawLine(pen, 12, 4, 4, 12);
    }

    private static void DrawPencil(Graphics g, Color color)
    {
        using var brush = new SolidBrush(Color.FromArgb(50, color));
        using var pen = new Pen(color, 1.2f);
        g.FillPolygon(brush, new[]
        {
            new PointF(2, 13), new PointF(3, 9.5f), new PointF(10, 2.5f), new PointF(13.5f, 6), new PointF(6.5f, 13)
        });
        g.DrawPolygon(pen, new[]
        {
            new PointF(2, 13), new PointF(3, 9.5f), new PointF(10, 2.5f), new PointF(13.5f, 6), new PointF(6.5f, 13)
        });
    }

    private static void DrawFilter(Graphics g, Color color)
    {
        using var brush = new SolidBrush(Color.FromArgb(70, color));
        using var pen = new Pen(color, 1.3f);
        g.FillPolygon(brush, new[]
        {
            new PointF(1.5f, 2.5f), new PointF(14.5f, 2.5f), new PointF(9.3f, 9),
            new PointF(9.3f, 14), new PointF(6.7f, 12), new PointF(6.7f, 9)
        });
        g.DrawPolygon(pen, new[]
        {
            new PointF(1.5f, 2.5f), new PointF(14.5f, 2.5f), new PointF(9.3f, 9),
            new PointF(9.3f, 14), new PointF(6.7f, 12), new PointF(6.7f, 9)
        });
    }

    private static void DrawCheck(Graphics g, Color color)
    {
        using var pen = new Pen(color, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(pen, new[] { new PointF(2.5f, 8.5f), new PointF(6.5f, 12.5f), new PointF(13.5f, 3.5f) });
    }

    private static void DrawWarning(Graphics g, Color color)
    {
        using var brush = new SolidBrush(Color.FromArgb(60, color));
        using var pen = new Pen(color, 1.4f);
        var points = new[] { new PointF(8, 2), new PointF(14.5f, 13.5f), new PointF(1.5f, 13.5f) };
        g.FillPolygon(brush, points);
        g.DrawPolygon(pen, points);
        using var line = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(line, 8, 6, 8, 9.5f);
        using var dot = new SolidBrush(color);
        g.FillEllipse(dot, 7.2f, 10.8f, 1.7f, 1.7f);
    }

    private static void DrawInfo(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.4f);
        using var brush = new SolidBrush(Color.FromArgb(35, color));
        g.FillEllipse(brush, 2, 2, 12, 12);
        g.DrawEllipse(pen, 2, 2, 12, 12);
        using var line = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(line, 8, 6.5f, 8, 10.5f);
        using var dot = new SolidBrush(color);
        g.FillEllipse(dot, 7.2f, 4.3f, 1.7f, 1.7f);
    }

    private static void DrawLink(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.5f);
        g.DrawArc(pen, 1.5f, 5.5f, 7, 6, 45, 260);
        g.DrawArc(pen, 7.5f, 5.5f, 7, 6, 225, 260);
    }

    private static void DrawPrint(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.3f);
        using var brush = new SolidBrush(Color.FromArgb(55, color));
        g.FillRectangle(brush, 3.5f, 2, 9, 4);
        g.DrawRectangle(pen, 3.5f, 2, 9, 4);
        g.FillRectangle(brush, 1.5f, 6, 13, 5.5f);
        g.DrawRectangle(pen, 1.5f, 6, 13, 5.5f);
        using var white = new SolidBrush(Color.White);
        g.FillRectangle(white, 4, 9.5f, 8, 4.5f);
        g.DrawRectangle(pen, 4, 9.5f, 8, 4.5f);
    }

    private static void DrawLock(Graphics g, Color color, bool locked)
    {
        using var pen = new Pen(color, 1.4f);
        using var brush = new SolidBrush(Color.FromArgb(55, color));
        g.FillRectangle(brush, 3, 7, 10, 7);
        g.DrawRectangle(pen, 3, 7, 10, 7);
        g.DrawArc(pen, 5, 2.5f, 6, 7, 180, locked ? 180 : 120);
        if (locked)
        {
            g.DrawLine(pen, 5, 6, 5, 7);
            g.DrawLine(pen, 11, 6, 11, 7);
        }
    }

    private static void DrawCopy(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.3f);
        g.DrawRectangle(pen, 5, 1.5f, 9, 10);
        using var brush = new SolidBrush(Color.White);
        g.FillRectangle(brush, 2, 4.5f, 9, 10);
        g.DrawRectangle(pen, 2, 4.5f, 9, 10);
    }

    private static void DrawBroom(Graphics g, Color color)
    {
        using var pen = new Pen(color, 1.4f);
        g.DrawLine(pen, 12, 2, 7, 7);
        using var brush = new SolidBrush(Color.FromArgb(60, color));
        g.FillPolygon(brush, new[]
        {
            new PointF(6, 6), new PointF(9.5f, 9.5f), new PointF(6.5f, 13.5f), new PointF(1.5f, 14), new PointF(3, 9)
        });
        g.DrawPolygon(pen, new[]
        {
            new PointF(6, 6), new PointF(9.5f, 9.5f), new PointF(6.5f, 13.5f), new PointF(1.5f, 14), new PointF(3, 9)
        });
    }

    private static void DrawLogo(Graphics g)
    {
        using var brush = new SolidBrush(Orange);
        using var pen = new Pen(Color.FromArgb(210, 105, 20), 1.2f);
        g.FillEllipse(brush, 1.5f, 1.5f, 13, 13);
        g.DrawEllipse(pen, 1.5f, 1.5f, 13, 13);
        using var font = new Font("Arial", 8.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var white = new SolidBrush(Color.White);
        var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("N", font, white, new RectangleF(0, 0, 16, 16), format);
    }

    private static void DrawLetter(Graphics g, string letter, Color color, float size)
    {
        using var font = new Font("Georgia", size, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(letter, font, brush, new RectangleF(0, 0, 16, 16), format);
    }
}
