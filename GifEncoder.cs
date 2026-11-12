using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MiniMonitor;

/// <summary>轻量 GIF89a 编码器：固定 216 色立方 + 40 级灰阶调色板，支持无限循环。</summary>
public static class GifEncoder
{
    private const int CubeLevels = 6;
    private const int CubeColors = CubeLevels * CubeLevels * CubeLevels;
    private const int PaletteSize = 256;

    public static void Save(string path, IReadOnlyList<Bitmap> frames, int delayMilliseconds)
    {
        if (frames.Count == 0)
        {
            throw new InvalidOperationException("没有可编码的帧。");
        }
        int width = frames[0].Width;
        int height = frames[0].Height;
        int delay = Math.Max(2, delayMilliseconds / 10);
        var palette = BuildPalette();
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        WriteHeader(stream, width, height, palette);
        WriteLoopExtension(stream);
        foreach (var frame in frames)
        {
            byte[] indices = Quantize(frame);
            WriteFrame(stream, width, height, indices, delay, palette);
        }
        stream.WriteByte(0x3B);
    }

    private static Color[] BuildPalette()
    {
        var palette = new Color[PaletteSize];
        for (int i = 0; i < CubeColors; i++)
        {
            int r = (i / 36) * 51;
            int g = ((i / 6) % 6) * 51;
            int b = (i % 6) * 51;
            palette[i] = Color.FromArgb(r, g, b);
        }
        for (int i = CubeColors; i < PaletteSize; i++)
        {
            int v = (i - CubeColors) * 255 / (PaletteSize - CubeColors - 1);
            palette[i] = Color.FromArgb(v, v, v);
        }
        return palette;
    }

    private static void WriteHeader(Stream stream, int width, int height, Color[] palette)
    {
        stream.WriteByte(0x47);
        stream.WriteByte(0x49);
        stream.WriteByte(0x46);
        stream.WriteByte(0x38);
        stream.WriteByte(0x39);
        stream.WriteByte(0x61);
        WriteUInt16(stream, width);
        WriteUInt16(stream, height);
        stream.WriteByte(0xF7); // global table, 256 colors
        stream.WriteByte(0);
        stream.WriteByte(0);
        foreach (var color in palette)
        {
            stream.WriteByte((byte)color.R);
            stream.WriteByte((byte)color.G);
            stream.WriteByte((byte)color.B);
        }
    }

    private static void WriteLoopExtension(Stream stream)
    {
        stream.WriteByte(0x21);
        stream.WriteByte(0xFF);
        stream.WriteByte(0x0B);
        WriteAscii(stream, "NETSCAPE2.0");
        stream.WriteByte(0x03);
        stream.WriteByte(0x01);
        WriteUInt16(stream, 0);
        stream.WriteByte(0);
    }

    private static byte[] Quantize(Bitmap frame)
    {
        int width = frame.Width;
        int height = frame.Height;
        var data = frame.LockBits(new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var result = new byte[width * height];
        var cache = new Dictionary<int, byte>(4096);
        try
        {
            int stride = data.Stride;
            var row = new byte[Math.Abs(stride)];
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * stride), row, 0, row.Length);
                for (int x = 0; x < width; x++)
                {
                    int offset = x * 4;
                    byte b = row[offset];
                    byte g = row[offset + 1];
                    byte r = row[offset + 2];
                    int key = (r << 16) | (g << 8) | b;
                    byte index;
                    if (!cache.TryGetValue(key, out index))
                    {
                        index = FindNearest(r, g, b);
                        if (cache.Count < 200000)
                        {
                            cache[key] = index;
                        }
                    }
                    result[y * width + x] = index;
                }
            }
        }
        finally
        {
            frame.UnlockBits(data);
        }
        return result;
    }

    private static byte FindNearest(byte r, byte g, byte b)
    {
        int qr = r * 5 / 255;
        int qg = g * 5 / 255;
        int qb = b * 5 / 255;
        int cubeIndex = qr * 36 + qg * 6 + qb;
        int cr = qr * 51;
        int cg = qg * 51;
        int cb = qb * 51;
        long cubeDistance = (long)(r - cr) * (r - cr)
            + (long)(g - cg) * (g - cg) + (long)(b - cb) * (b - cb);
        int grayValue = (r * 299 + g * 587 + b * 114) / 1000;
        int grayIndex = CubeColors + (int)Math.Round(grayValue * 39.0 / 255.0);
        int gv = (grayIndex - CubeColors) * 255 / 39;
        long grayDistance = (long)(r - gv) * (r - gv)
            + (long)(g - gv) * (g - gv) + (long)(b - gv) * (b - gv);
        return (byte)(grayDistance < cubeDistance ? grayIndex : cubeIndex);
    }

    private static void WriteFrame(Stream stream, int width, int height,
        byte[] indices, int delay, Color[] palette)
    {
        stream.WriteByte(0x21);
        stream.WriteByte(0xF9);
        stream.WriteByte(0x04);
        stream.WriteByte(0x04); // disposal 1, no transparency
        WriteUInt16(stream, delay);
        stream.WriteByte(0);
        stream.WriteByte(0);

        stream.WriteByte(0x2C);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, width);
        WriteUInt16(stream, height);
        stream.WriteByte(0);

        stream.WriteByte(8);
        byte[] compressed = LzwEncode(indices);
        int offset = 0;
        while (offset < compressed.Length)
        {
            int size = Math.Min(255, compressed.Length - offset);
            stream.WriteByte((byte)size);
            stream.Write(compressed, offset, size);
            offset += size;
        }
        stream.WriteByte(0);
    }

    private static byte[] LzwEncode(byte[] indices)
    {
        var output = new MemoryStream();
        var writer = new BitWriter(output);
        const int clearCode = 256;
        const int endCode = 257;
        int codeSize = 9;
        int nextCode = 258;
        var dictionary = new Dictionary<int, int>(4096);

        writer.Write(clearCode, codeSize);
        if (indices.Length == 0)
        {
            writer.Write(endCode, codeSize);
            writer.Flush();
            return output.ToArray();
        }

        int prefix = indices[0];
        for (int i = 1; i < indices.Length; i++)
        {
            int character = indices[i];
            int key = (prefix << 8) | character;
            int code;
            if (dictionary.TryGetValue(key, out code))
            {
                prefix = code;
                continue;
            }

            writer.Write(prefix, codeSize);
            if (nextCode < 4096)
            {
                dictionary[key] = nextCode++;
                if (nextCode > (1 << codeSize) && codeSize < 12)
                {
                    codeSize++;
                }
            }
            else
            {
                writer.Write(clearCode, codeSize);
                dictionary.Clear();
                nextCode = 258;
                codeSize = 9;
            }
            prefix = character;
        }
        writer.Write(prefix, codeSize);
        writer.Write(endCode, codeSize);
        writer.Flush();
        return output.ToArray();
    }

    private static void WriteUInt16(Stream stream, int value)
    {
        stream.WriteByte((byte)(value & 0xFF));
        stream.WriteByte((byte)((value >> 8) & 0xFF));
    }

    private static void WriteAscii(Stream stream, string text)
    {
        foreach (char c in text)
        {
            stream.WriteByte((byte)c);
        }
    }

    private sealed class BitWriter
    {
        private readonly Stream _stream;
        private int _bits;
        private int _bitCount;

        public BitWriter(Stream stream)
        {
            _stream = stream;
        }

        public void Write(int code, int size)
        {
            _bits |= code << _bitCount;
            _bitCount += size;
            while (_bitCount >= 8)
            {
                _stream.WriteByte((byte)(_bits & 0xFF));
                _bits >>= 8;
                _bitCount -= 8;
            }
        }

        public void Flush()
        {
            if (_bitCount > 0)
            {
                _stream.WriteByte((byte)(_bits & 0xFF));
                _bits = 0;
                _bitCount = 0;
            }
        }
    }
}
