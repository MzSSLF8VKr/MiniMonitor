using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>字体文件预览：加载 TTF / OTF / TTC 并绘制字体名称与样张。</summary>
internal sealed class QuickLookFontViewer : Control
{
    private PrivateFontCollection? _collection;
    private string _familyName = "";
    private string _fileInfo = "";
    private string? _error;

    public QuickLookFontViewer()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(15, 23, 42);
    }

    public void LoadFont(string path)
    {
        _collection?.Dispose();
        _collection = new PrivateFontCollection();
        _error = null;
        try
        {
            _collection.AddFontFile(path);
            if (_collection.Families.Length == 0)
            {
                _error = "字体文件中未找到可用字体。";
                return;
            }
            _familyName = string.Join("、", _collection.Families.Select(f => f.Name));
            _fileInfo = "字体家族：" + _collection.Families.Length
                + "  ·  " + QuickLookFormats.FormatSize(new FileInfo(path).Length);
        }
        catch (Exception ex)
        {
            _error = "无法加载字体：" + ex.Message;
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using (var bg = new SolidBrush(BackColor))
        {
            g.FillRectangle(bg, ClientRectangle);
        }

        if (_error != null)
        {
            using var font = new Font("Microsoft YaHei UI", 12f);
            TextRenderer.DrawText(g, _error, font, ClientRectangle, Color.FromArgb(252, 165, 165),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        if (_collection == null || _collection.Families.Length == 0)
        {
            return;
        }

        using var titleFont = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold);
        TextRenderer.DrawText(g, _familyName, titleFont,
            new Rectangle(30, 24, Width - 60, 36), Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        using var infoFont = new Font("Microsoft YaHei UI", 8.5f);
        TextRenderer.DrawText(g, _fileInfo, infoFont, new Rectangle(30, 64, Width - 60, 24),
            Color.FromArgb(148, 163, 184), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        var family = _collection.Families[0];
        string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ  abcdefghijklmnopqrstuvwxyz";
        string numbers = "0123456789  !@#$%^&*()_+-=[]{};:'\",.<>/?";
        string chinese = "字体预览：永和九年，岁在癸丑，暮春之初，会于会稽山阴之兰亭。";
        float y = 104f;
        foreach (float size in new[] { 34f, 24f, 18f, 13f })
        {
            using var font = new Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel);
            string text = size >= 24 ? alphabet : numbers;
            TextRenderer.DrawText(g, text, font,
                new Rectangle(30, (int)y, Width - 60, (int)(size * 1.7f)), Color.FromArgb(226, 232, 240),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            y += size * 1.75f;
        }
        using (var font = new Font(family, 20f, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            TextRenderer.DrawText(g, chinese, font,
                new Rectangle(30, (int)y, Width - 60, 48), Color.FromArgb(226, 232, 240),
                TextFormatFlags.Left | TextFormatFlags.WordBreak);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _collection?.Dispose();
            _collection = null;
        }
        base.Dispose(disposing);
    }
}
