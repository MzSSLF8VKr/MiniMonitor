using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace MiniMonitor.Web.Drawio;

/// <summary>
/// draw.io 静态资源包（drawio.pak）。整个站点在服务启动时一次性读入内存，
/// 文本资源在建包时已用 Brotli 预压缩，响应时直接输出压缩字节，避免磁盘 IO 与实时压缩开销。
/// </summary>
public sealed class DrawioAssetPack
{
    private const string ResourceName = "drawio.pak";
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("DMPAK1\n");

    public sealed class Entry
    {
        public int Offset { get; init; }
        public int Length { get; init; }
        public int RawLength { get; init; }
        public string Encoding { get; init; } = "none";
        public string ContentType { get; init; } = "application/octet-stream";
        public string ETag { get; init; } = "";
    }

    private readonly byte[] _data;
    private readonly int _dataStart;
    private readonly Dictionary<string, Entry> _files;

    public string Version { get; }
    public int Count => _files.Count;

    private DrawioAssetPack(byte[] data, int dataStart, Dictionary<string, Entry> files, string version)
    {
        _data = data;
        _dataStart = dataStart;
        _files = files;
        Version = version;
    }

    public static DrawioAssetPack Load()
    {
        Assembly assembly = typeof(DrawioAssetPack).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("程序内缺少 drawio.pak 资源，无法启动 drawio 服务");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        byte[] data = ms.ToArray();
        if (data.Length < 11 || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidOperationException("drawio.pak 资源格式无效");
        }
        int indexLength = BitConverter.ToInt32(data, Magic.Length);
        int dataStart = Magic.Length + 4 + indexLength;
        if (indexLength <= 0 || dataStart > data.Length)
        {
            throw new InvalidOperationException("drawio.pak 索引损坏");
        }
        string json = Encoding.UTF8.GetString(data, Magic.Length + 4, indexLength);
        var files = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        string version = "";
        using (JsonDocument doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.TryGetProperty("v", out JsonElement versionElement))
            {
                version = versionElement.GetString() ?? "";
            }
            if (!doc.RootElement.TryGetProperty("files", out JsonElement fileElement))
            {
                throw new InvalidOperationException("drawio.pak 索引缺少文件列表");
            }
            foreach (JsonProperty property in fileElement.EnumerateObject())
            {
                JsonElement value = property.Value;
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < 6)
                {
                    continue;
                }
                files[property.Name] = new Entry
                {
                    Offset = value[0].GetInt32(),
                    Length = value[1].GetInt32(),
                    RawLength = value[2].GetInt32(),
                    Encoding = value[3].GetString() ?? "none",
                    ContentType = value[4].GetString() ?? "application/octet-stream",
                    ETag = value[5].GetString() ?? ""
                };
            }
        }
        if (files.Count == 0)
        {
            throw new InvalidOperationException("drawio.pak 中没有任何资源");
        }
        return new DrawioAssetPack(data, dataStart, files, version);
    }

    public bool TryGet(string path, out Entry entry)
    {
        if (_files.TryGetValue(path, out Entry? found))
        {
            entry = found;
            return true;
        }
        entry = null!;
        return false;
    }

    /// <summary>不复制地返回资源在内存包中的存储区间。</summary>
    public ReadOnlyMemory<byte> GetStored(Entry entry)
    {
        long start = (long)_dataStart + entry.Offset;
        if (start < 0 || start + entry.Length > _data.Length)
        {
            return ReadOnlyMemory<byte>.Empty;
        }
        return new ReadOnlyMemory<byte>(_data, (int)start, entry.Length);
    }

    public byte[] Decompress(Entry entry)
    {
        ReadOnlySpan<byte> stored = GetStored(entry).Span;
        if (entry.Encoding != "br")
        {
            return stored.ToArray();
        }
        using var input = new MemoryStream(stored.ToArray(), writable: false);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(Math.Max(entry.RawLength, 1024));
        brotli.CopyTo(output);
        return output.ToArray();
    }
}
