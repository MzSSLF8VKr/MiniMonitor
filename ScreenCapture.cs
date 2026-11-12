using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MiniMonitor;

public static class ScreenCapture
{
    private const int DwmwaExtendedFrameBounds = 9;
    private const int CursorShowing = 0x00000001;
    private const int DiNormal = 0x0003;
    private const uint MouseEventWheel = 0x0800;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointStruct
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int Size;
        public int Flags;
        public IntPtr Cursor;
        public PointStruct ScreenPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Mouse;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute,
        out Rect rect, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorInfo(ref CursorInfo cursorInfo);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out PointStruct point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon,
        int width, int height, int step, IntPtr flicker, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    public static Rectangle VirtualScreenBounds => SystemInformation.VirtualScreen;

    public static Bitmap CaptureVirtualScreen()
    {
        return CaptureRegion(SystemInformation.VirtualScreen);
    }

    public static Bitmap CaptureRegion(Rectangle bounds)
    {
        int width = Math.Max(1, bounds.Width);
        int height = Math.Max(1, bounds.Height);
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0,
            new Size(width, height), CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    public static Rectangle GetActiveWindowBounds()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return SystemInformation.VirtualScreen;
        }
        Rect rect;
        int result = DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds,
            out rect, Marshal.SizeOf<Rect>());
        if (result != 0 || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
        {
            if (!GetWindowRect(hwnd, out rect))
            {
                return SystemInformation.VirtualScreen;
            }
        }
        var bounds = new Rectangle(rect.Left, rect.Top,
            Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top));
        var clipped = Rectangle.Intersect(bounds, SystemInformation.VirtualScreen);
        return clipped.Width > 0 && clipped.Height > 0 ? clipped : SystemInformation.VirtualScreen;
    }

    public static Bitmap CaptureActiveWindow()
    {
        return CaptureRegion(GetActiveWindowBounds());
    }

    public static void DrawCursor(Graphics graphics, Rectangle captureBounds)
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref info) || (info.Flags & CursorShowing) == 0 || info.Cursor == IntPtr.Zero)
        {
            return;
        }
        int x = info.ScreenPosition.X - captureBounds.Left;
        int y = info.ScreenPosition.Y - captureBounds.Top;
        IntPtr hdc = graphics.GetHdc();
        try
        {
            DrawIconEx(hdc, x, y, info.Cursor, 0, 0, 0, IntPtr.Zero, DiNormal);
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }
    }

    public static void DrawCursorEffects(Graphics graphics, Rectangle captureBounds,
        List<ClickRipple> ripples, bool highlight)
    {
        if (!GetCursorPos(out PointStruct point))
        {
            return;
        }
        int x = point.X - captureBounds.Left;
        int y = point.Y - captureBounds.Top;
        if (highlight)
        {
            using var pen = new Pen(Color.FromArgb(230, 255, 196, 0), 2.4f);
            using var glow = new SolidBrush(Color.FromArgb(55, 255, 214, 64));
            graphics.FillEllipse(glow, x - 18, y - 18, 36, 36);
            graphics.DrawEllipse(pen, x - 18, y - 18, 36, 36);
        }

        DateTime now = DateTime.Now;
        foreach (var ripple in ripples.ToArray())
        {
            double elapsed = (now - ripple.Start).TotalMilliseconds;
            if (elapsed > 500)
            {
                ripples.Remove(ripple);
                continue;
            }
            float progress = (float)(elapsed / 500);
            int radius = (int)(8 + 34 * progress);
            int alpha = (int)(180 * (1 - progress));
            int rx = ripple.Position.X - captureBounds.Left;
            int ry = ripple.Position.Y - captureBounds.Top;
            using var pen = new Pen(Color.FromArgb(alpha, 255, 82, 82), 2.5f);
            graphics.DrawEllipse(pen, rx - radius, ry - radius, radius * 2, radius * 2);
        }
    }

    public static Bitmap CaptureScrollingWindow(IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var bounds = GetActiveWindowBounds();
        Bitmap first = CaptureRegion(bounds);
        int width = first.Width;
        int height = first.Height;
        var frames = new List<Bitmap> { first };
        PointStruct originalCursor = default;
        GetCursorPos(out originalCursor);
        int scrollCount = 0;
        try
        {
            SetCursorPos(bounds.Left + width / 2, bounds.Top + height / 2);
            Bitmap previous = first;
            for (int i = 0; i < 12; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report("滚动截图：第 " + (i + 1) + " 屏");
                SendWheel(-120);
                Thread.Sleep(320);
                Bitmap current = CaptureRegion(bounds);
                int delta = FindScrollDelta(previous, current);
                if (delta <= 8)
                {
                    current.Dispose();
                    break;
                }
                frames.Add(current);
                previous = current;
                scrollCount++;
                if (frames.Sum(f => f.Height) > 20000)
                {
                    break;
                }
            }
        }
        finally
        {
            if (scrollCount > 0)
            {
                for (int i = 0; i < scrollCount; i++)
                {
                    SendWheel(120);
                    Thread.Sleep(30);
                }
            }
            SetCursorPos(originalCursor.X, originalCursor.Y);
        }

        if (frames.Count == 1)
        {
            return frames[0];
        }

        var deltas = new List<int> { 0 };
        for (int i = 1; i < frames.Count; i++)
        {
            int delta = FindScrollDelta(frames[i - 1], frames[i]);
            deltas.Add(Math.Max(0, delta));
        }
        int totalHeight = frames[0].Height + deltas.Skip(1).Sum();
        totalHeight = Math.Min(totalHeight, 20000);
        var result = new Bitmap(width, Math.Max(1, totalHeight), PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.DrawImage(frames[0], new Rectangle(0, 0, width, frames[0].Height),
            new Rectangle(0, 0, frames[0].Width, frames[0].Height), GraphicsUnit.Pixel);
        int y = frames[0].Height;
        for (int i = 1; i < frames.Count; i++)
        {
            int appendHeight = Math.Min(deltas[i], Math.Max(0, totalHeight - y));
            if (appendHeight <= 0)
            {
                break;
            }
            var source = new Rectangle(0, frames[i].Height - appendHeight, width, appendHeight);
            graphics.DrawImage(frames[i], new Rectangle(0, y, width, appendHeight),
                source, GraphicsUnit.Pixel);
            y += appendHeight;
        }
        foreach (var frame in frames)
        {
            frame.Dispose();
        }
        return result;
    }

    private static void SendWheel(int delta)
    {
        var input = new Input
        {
            Type = 0,
            Mouse = new MouseInput
            {
                MouseData = (uint)delta,
                Flags = MouseEventWheel
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
    }

    private static int FindScrollDelta(Bitmap previous, Bitmap current)
    {
        int width = Math.Min(previous.Width, current.Width);
        int height = Math.Min(previous.Height, current.Height);
        int templateY = Math.Max(0, height - 90);
        int templateHeight = Math.Min(26, height - templateY);
        int maxSearchY = Math.Max(1, height / 2);
        int bestY = -1;
        long bestScore = long.MaxValue;

        var prevData = previous.LockBits(new Rectangle(0, 0, previous.Width, previous.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var currData = current.LockBits(new Rectangle(0, 0, current.Width, current.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int pStride = prevData.Stride;
            int cStride = currData.Stride;
            IntPtr pScan = prevData.Scan0;
            IntPtr cScan = currData.Scan0;
            for (int y = 0; y < maxSearchY; y += 2)
            {
                long score = 0;
                int samples = 0;
                for (int ty = 0; ty < templateHeight; ty += 2)
                {
                    for (int x = 0; x < width; x += 4)
                    {
                        IntPtr pOffset = IntPtr.Add(pScan, (templateY + ty) * pStride + x * 4);
                        IntPtr cOffset = IntPtr.Add(cScan, (y + ty) * cStride + x * 4);
                        byte b1 = Marshal.ReadByte(pOffset);
                        byte g1 = Marshal.ReadByte(pOffset, 1);
                        byte r1 = Marshal.ReadByte(pOffset, 2);
                        byte b2 = Marshal.ReadByte(cOffset);
                        byte g2 = Marshal.ReadByte(cOffset, 1);
                        byte r2 = Marshal.ReadByte(cOffset, 2);
                        score += Math.Abs(b1 - b2) + Math.Abs(g1 - g2) + Math.Abs(r1 - r2);
                        samples++;
                    }
                }
                long average = samples > 0 ? score / samples : long.MaxValue;
                if (average < bestScore)
                {
                    bestScore = average;
                    bestY = y;
                }
            }
        }
        finally
        {
            previous.UnlockBits(prevData);
            current.UnlockBits(currData);
        }

        if (bestY < 0 || bestScore > 60)
        {
            return 0;
        }
        int delta = templateY - bestY;
        return delta is > 8 and < 800 ? delta : 0;
    }
}

public sealed class ClickRipple
{
    public Point Position { get; }
    public DateTime Start { get; }
    public MouseButtons Button { get; }

    public ClickRipple(Point position, MouseButtons button)
    {
        Position = position;
        Button = button;
        Start = DateTime.Now;
    }
}
