using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MiniMonitor.Video;

/// <summary>
/// 仅监听 127.0.0.1 的轻量媒体服务器：为 WebView2 提供同源 HTML 页面与支持 Range 的本地文件读取。
/// 避免 file:// / 虚拟主机在部分系统上的媒体加载和拖动定位问题。
/// </summary>
internal sealed class MediaPreviewServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly string _html;
    private readonly string _token = Guid.NewGuid().ToString("N");
    private string _mediaPath = "";
    private bool _disposed;

    public MediaPreviewServer(string html)
    {
        _html = html;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }

    public string PageUrl => "http://127.0.0.1:" + Port + "/preview.html";

    public string MediaUrl => "http://127.0.0.1:" + Port + "/media?token=" + _token;

    public void SetMedia(string path)
    {
        _mediaPath = Path.GetFullPath(path);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch
            {
                break;
            }
            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            using (client)
            {
                client.NoDelay = true;
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
                string? requestLine = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(requestLine))
                {
                    return;
                }
                var parts = requestLine.Split(' ');
                if (parts.Length < 2)
                {
                    return;
                }
                string target = parts[1];
                string rangeHeader = "";
                while (await reader.ReadLineAsync() is { } line)
                {
                    if (line.Length == 0)
                    {
                        break;
                    }
                    if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
                    {
                        rangeHeader = line[6..].Trim();
                    }
                }
                if (target.StartsWith("/preview.html", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteTextAsync(stream, "text/html; charset=utf-8", _html);
                    return;
                }
                if (!target.StartsWith("/media", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteStatusAsync(stream, 404, "Not Found", "");
                    return;
                }
                if (!target.Contains("token=" + _token, StringComparison.Ordinal))
                {
                    await WriteStatusAsync(stream, 403, "Forbidden", "");
                    return;
                }
                string path = _mediaPath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    await WriteStatusAsync(stream, 404, "Not Found", "");
                    return;
                }
                await WriteFileAsync(stream, path, rangeHeader);
            }
        }
        catch (Exception ex)
        {
            VideoLog.Warn("媒体预览服务请求失败：" + ex.Message);
            try
            {
                client.Close();
            }
            catch
            {
                // ignore
            }
        }
    }

    private static async Task WriteTextAsync(NetworkStream stream, string contentType, string text)
    {
        byte[] body = Encoding.UTF8.GetBytes(text);
        await WriteHeadersAsync(stream, 200, "OK", contentType, body.Length, null, null);
        await stream.WriteAsync(body);
    }

    private static async Task WriteStatusAsync(NetworkStream stream, int code, string reason,
        string body)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        await WriteHeadersAsync(stream, code, reason, "text/plain; charset=utf-8", bytes.Length,
            null, null);
        if (bytes.Length > 0)
        {
            await stream.WriteAsync(bytes);
        }
    }

    private static async Task WriteFileAsync(NetworkStream stream, string path, string rangeHeader)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long length = file.Length;
        long start = 0;
        long end = length - 1;
        bool partial = false;
        if (!string.IsNullOrWhiteSpace(rangeHeader)
            && rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            string spec = rangeHeader[6..].Trim();
            int dash = spec.IndexOf('-');
            if (dash >= 0)
            {
                string startText = spec[..dash].Trim();
                string endText = spec[(dash + 1)..].Trim();
                if (long.TryParse(startText, out long parsedStart))
                {
                    start = Math.Max(0, parsedStart);
                }
                if (long.TryParse(endText, out long parsedEnd) && parsedEnd >= start)
                {
                    end = Math.Min(length - 1, parsedEnd);
                }
                partial = true;
            }
        }
        if (length <= 0 || start > end || start >= length)
        {
            await WriteHeadersAsync(stream, 416, "Range Not Satisfiable",
                "text/plain; charset=utf-8", 0, null, "bytes */" + length);
            return;
        }
        long count = end - start + 1;
        string contentType = ContentType(path);
        await WriteHeadersAsync(stream, partial ? 206 : 200, partial ? "Partial Content" : "OK",
            contentType, count, partial ? ("bytes " + start + "-" + end + "/" + length) : null, null);
        file.Position = start;
        var buffer = new byte[128 * 1024];
        long remaining = count;
        while (remaining > 0)
        {
            int read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)));
            if (read <= 0)
            {
                break;
            }
            await stream.WriteAsync(buffer.AsMemory(0, read));
            remaining -= read;
        }
    }

    private static async Task WriteHeadersAsync(NetworkStream stream, int code, string reason,
        string contentType, long contentLength, string? contentRange, string? extra)
    {
        var builder = new StringBuilder();
        builder.Append("HTTP/1.1 ").Append(code).Append(' ').Append(reason).Append("\r\n");
        builder.Append("Content-Type: ").Append(contentType).Append("\r\n");
        builder.Append("Content-Length: ").Append(contentLength).Append("\r\n");
        builder.Append("Accept-Ranges: bytes\r\n");
        builder.Append("Cache-Control: no-store\r\n");
        builder.Append("Connection: close\r\n");
        if (!string.IsNullOrWhiteSpace(contentRange))
        {
            builder.Append("Content-Range: ").Append(contentRange).Append("\r\n");
        }
        if (!string.IsNullOrWhiteSpace(extra))
        {
            builder.Append("Content-Range: ").Append(extra).Append("\r\n");
        }
        builder.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(builder.ToString()));
    }

    private static string ContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp4" or ".m4v" => "video/mp4",
            ".m4a" or ".aac" => "audio/mp4",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".flac" => "audio/flac",
            ".ogg" or ".oga" or ".opus" => "audio/ogg",
            ".webm" => "video/webm",
            ".mkv" => "video/x-matroska",
            ".mov" => "video/quicktime",
            ".avi" => "video/x-msvideo",
            ".wmv" => "video/x-ms-wmv",
            ".flv" => "video/x-flv",
            ".ts" or ".m2ts" => "video/mp2t",
            ".ogv" => "video/ogg",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        try
        {
            _cts.Cancel();
        }
        catch
        {
            // ignore
        }
        try
        {
            _listener.Stop();
        }
        catch
        {
            // ignore
        }
        _cts.Dispose();
    }
}
