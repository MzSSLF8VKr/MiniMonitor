using System.Reflection;

namespace MiniMonitor.Web;

/// <summary>读取随程序集嵌入的前端静态资源（YamlMind / WebTodo 的 HTML、CSS、JS、字体等）。</summary>
public sealed class WebAssetStore
{
    private readonly Dictionary<string, byte[]> _assets = new(StringComparer.OrdinalIgnoreCase);

    public WebAssetStore(string prefix)
    {
        Assembly assembly = typeof(WebAssetStore).Assembly;
        foreach (string name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string key = name[prefix.Length..].Replace('\\', '/').TrimStart('/');
            try
            {
                using Stream? stream = assembly.GetManifestResourceStream(name);
                if (stream == null)
                {
                    continue;
                }
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                _assets[key] = ms.ToArray();
            }
            catch
            {
                // 单个资源读取失败不影响其它文件
            }
        }
    }

    public int Count => _assets.Count;

    public byte[]? Get(string path)
    {
        string key = Normalize(path);
        return key.Length == 0 ? null : _assets.TryGetValue(key, out byte[]? data) ? data : null;
    }

    public static string Normalize(string path)
    {
        string name = (path ?? "").Replace('\\', '/').TrimStart('/');
        if (name.Length == 0 || name.Contains('\0') || name.Split('/').Contains(".."))
        {
            return "";
        }
        return name;
    }

    public static string MimeType(string path)
    {
        int dot = path.LastIndexOf('.');
        string ext = dot >= 0 ? path[dot..].ToLowerInvariant() : "";
        return ext switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            ".mjs" => "application/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".map" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".woff2" => "font/woff2",
            ".woff" => "font/woff",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".ico" => "image/x-icon",
            ".webp" => "image/webp",
            ".txt" => "text/plain; charset=utf-8",
            _ => "application/octet-stream"
        };
    }
}
