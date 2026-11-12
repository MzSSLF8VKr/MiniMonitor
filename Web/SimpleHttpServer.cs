using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Encodings.Web;

namespace MiniMonitor.Web;

/// <summary>极简 HTTP/1.1 服务器，供内置的 YamlMind / WebTodo 服务使用（仅依赖 .NET，不拉起任何外部进程）。</summary>
public sealed class SimpleHttpServer : IDisposable
{
    public delegate Task<HttpResponse> RequestHandler(HttpRequest request);

    private readonly IPAddress _address;
    private readonly int _port;
    private readonly string _serverName;
    private readonly RequestHandler _handler;
    private readonly long _maxBodyLength;
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<TcpClient, byte> _clients = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    /// <summary>请求体上限（默认 8 MB，与上层服务的请求体校验一致；大请求体服务可自行调高）。</summary>
    private const long DefaultMaxBodyLength = 8L * 1024 * 1024;

    public SimpleHttpServer(IPAddress address, int port, string serverName, RequestHandler handler,
        long? maxBodyLength = null)
    {
        _address = address;
        _port = port;
        _serverName = serverName;
        _handler = handler;
        _maxBodyLength = Math.Max(64L * 1024, maxBodyLength ?? DefaultMaxBodyLength);
    }

    public int Port => _port;
    public bool IsRunning { get; private set; }

    public void Start()
    {
        lock (_sync)
        {
            if (IsRunning)
            {
                return;
            }
            var listener = new TcpListener(_address, _port);
            try
            {
                listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                listener.Start(128);
            }
            catch (SocketException ex)
            {
                listener.Stop();
                throw new IOException($"端口 {_port} 无法监听：{ex.Message}", ex);
            }
            _listener = listener;
            _cts = new CancellationTokenSource();
            IsRunning = true;
            _acceptTask = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (!IsRunning)
            {
                return;
            }
            IsRunning = false;
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            foreach (TcpClient client in _clients.Keys)
            {
                try { client.Close(); } catch { }
            }
            _clients.Clear();
            _listener = null;
            var task = _acceptTask;
            _acceptTask = null;
            try { task?.Wait(2000); } catch { }
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }
                continue;
            }
            _clients[client] = 0;
            _ = Task.Run(() => HandleClientAsync(client, token));
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        try
        {
            client.NoDelay = true;
            using NetworkStream stream = client.GetStream();
            var reader = new HttpLineReader(stream);
            int served = 0;
            while (!token.IsCancellationRequested && served++ < 2000)
            {
                HttpRequest? request = await ReadRequestAsync(reader, client, token, _maxBodyLength);
                if (request == null)
                {
                    break;
                }
                HttpResponse response;
                try
                {
                    response = await _handler(request);
                }
                catch (Exception ex)
                {
                    response = HttpResponse.Json(500,
                        new JsonObject { ["ok"] = false, ["error"] = "服务器内部错误：" + ex.Message },
                        noStore: true);
                }
                bool headOnly = string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase);
                await WriteResponseAsync(stream, request, response, headOnly, token);
                string connection = request.Headers.TryGetValue("connection", out string? value)
                    ? value.ToLowerInvariant()
                    : "";
                if (request.HttpVersion == "HTTP/1.0"
                    ? !connection.Contains("keep-alive")
                    : connection.Contains("close"))
                {
                    break;
                }
            }
        }
        catch (Exception)
        {
            // 客户端断开或超时，直接关闭
        }
        finally
        {
            _clients.TryRemove(client, out _);
            try { client.Close(); } catch { }
        }
    }

    private static async Task<HttpRequest?> ReadRequestAsync(HttpLineReader reader, TcpClient client,
        CancellationToken token, long maxBodyLength)
    {
        string? requestLine = await reader.ReadLineAsync(token);
        if (string.IsNullOrEmpty(requestLine))
        {
            return null;
        }
        var parts = requestLine.Split(' ', 3);
        if (parts.Length < 3)
        {
            return null;
        }
        string method = parts[0];
        string target = parts[1];
        string version = parts[2];
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int totalHeaderBytes = 0;
        while (true)
        {
            string? line = await reader.ReadLineAsync(token);
            if (line == null || line.Length == 0)
            {
                break;
            }
            totalHeaderBytes += line.Length;
            if (totalHeaderBytes > 256 * 1024)
            {
                return null;
            }
            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }
            string key = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            headers[key] = headers.TryGetValue(key, out string? old) ? old + ", " + value : value;
        }

        string rawPath = target;
        string rawQuery = "";
        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(target);
                rawPath = uri.AbsolutePath;
                rawQuery = uri.Query.TrimStart('?');
            }
            catch
            {
                // ignore malformed absolute URI
            }
        }
        else
        {
            int q = target.IndexOf('?');
            if (q >= 0)
            {
                rawPath = target[..q];
                rawQuery = target[(q + 1)..];
            }
        }
        string path;
        try
        {
            path = Uri.UnescapeDataString(rawPath);
        }
        catch
        {
            path = rawPath;
        }

        byte[] body = Array.Empty<byte>();
        string transfer = headers.TryGetValue("transfer-encoding", out string? te) ? te.ToLowerInvariant() : "";
        if (transfer.Contains("chunked"))
        {
            body = await ReadChunkedBodyAsync(reader, token, maxBodyLength);
        }
        else
        {
            long length = 0;
            if (headers.TryGetValue("content-length", out string? len))
            {
                long.TryParse(len.Split(',')[0].Trim(), out length);
            }
            if (length > maxBodyLength)
            {
                return null;
            }
            if (length > 0)
            {
                body = await reader.ReadBytesAsync((int)length, token);
            }
        }

        var remote = client.Client.RemoteEndPoint as IPEndPoint;
        return new HttpRequest
        {
            Method = method,
            Path = path,
            RawQuery = rawQuery,
            Query = HttpRequest.ParseQuery(rawQuery),
            Headers = headers,
            Body = body,
            HttpVersion = version,
            RemoteIp = remote?.Address.ToString() ?? "",
            RemotePort = remote?.Port ?? 0
        };
    }

    private static async Task<byte[]> ReadChunkedBodyAsync(HttpLineReader reader, CancellationToken token,
        long maxBodyLength)
    {
        using var ms = new MemoryStream();
        while (true)
        {
            string? sizeLine = await reader.ReadLineAsync(token);
            if (string.IsNullOrEmpty(sizeLine))
            {
                break;
            }
            int semi = sizeLine.IndexOf(';');
            if (semi >= 0)
            {
                sizeLine = sizeLine[..semi];
            }
            if (!int.TryParse(sizeLine.Trim(), System.Globalization.NumberStyles.HexNumber, null, out int size))
            {
                break;
            }
            if (size == 0)
            {
                // 读取 trailer 直到空行
                while (true)
                {
                    string? trailer = await reader.ReadLineAsync(token);
                    if (string.IsNullOrEmpty(trailer))
                    {
                        break;
                    }
                }
                break;
            }
            if (ms.Length + size > maxBodyLength)
            {
                throw new IOException("请求体过大");
            }
            byte[] data = await reader.ReadBytesAsync(size, token);
            ms.Write(data, 0, data.Length);
            await reader.ReadLineAsync(token); // 结尾 CRLF
        }
        return ms.ToArray();
    }

    private static async Task WriteResponseAsync(NetworkStream stream, HttpRequest request, HttpResponse response,
        bool headOnly, CancellationToken token)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(response.Status).Append(' ').Append(ReasonPhrase(response.Status)).Append("\r\n");
        sb.Append("Date: ").Append(DateTime.UtcNow.ToString("R")).Append("\r\n");
        sb.Append("Server: MiniMonitor\r\n");
        if (response.Status != 304)
        {
            string contentType = response.ContentType ?? "application/octet-stream";
            sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
            sb.Append("Content-Length: ").Append(response.Body.Length).Append("\r\n");
        }
        foreach (var (key, value) in response.Headers)
        {
            sb.Append(key).Append(": ").Append(value).Append("\r\n");
        }
        sb.Append("Connection: keep-alive\r\n\r\n");
        byte[] head = Encoding.UTF8.GetBytes(sb.ToString());
        await stream.WriteAsync(head, token);
        if (!headOnly && response.Status != 304 && response.Body.Length > 0)
        {
            await stream.WriteAsync(response.Body, token);
        }
        await stream.FlushAsync(token);
    }

    private static string ReasonPhrase(int status) => status switch
    {
        200 => "OK",
        201 => "Created",
        204 => "No Content",
        304 => "Not Modified",
        400 => "Bad Request",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        409 => "Conflict",
        413 => "Payload Too Large",
        423 => "Locked",
        500 => "Internal Server Error",
        502 => "Bad Gateway",
        503 => "Service Unavailable",
        _ => "OK"
    };

    public void Dispose() => Stop();
}

public sealed class HttpRequest
{
    public string Method { get; init; } = "GET";
    public string Path { get; init; } = "/";
    public string RawQuery { get; init; } = "";
    public Dictionary<string, List<string>> Query { get; init; } = new();
    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public byte[] Body { get; init; } = Array.Empty<byte>();
    public string HttpVersion { get; init; } = "HTTP/1.1";
    public string RemoteIp { get; init; } = "";
    public int RemotePort { get; init; }

    public string? Q(string key, string? fallback = null)
    {
        if (Query.TryGetValue(key, out var values) && values.Count > 0 && values[0] != "")
        {
            return values[0];
        }
        return fallback;
    }

    public int QInt(string key, int fallback, int? min = null, int? max = null)
    {
        string? raw = Q(key);
        int value;
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = fallback;
        }
        else
        {
            try { value = (int)double.Parse(raw, System.Globalization.CultureInfo.InvariantCulture); }
            catch { value = fallback; }
        }
        if (min.HasValue) value = Math.Max(min.Value, value);
        if (max.HasValue) value = Math.Min(max.Value, value);
        return value;
    }

    public bool QBool(string key, bool fallback = false)
    {
        string? raw = Q(key);
        if (raw == null)
        {
            return fallback;
        }
        return raw.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on" or "y";
    }

    public string Header(string key) => Headers.TryGetValue(key, out string? v) ? v : "";

    public JsonObject JsonBody()
    {
        if (Body.Length == 0)
        {
            return new JsonObject();
        }
        try
        {
            JsonNode? node = JsonNode.Parse(Encoding.UTF8.GetString(Body));
            return node as JsonObject ?? new JsonObject();
        }
        catch (Exception ex)
        {
            throw new HttpApiException("请求体不是合法 JSON：" + ex.Message, 400, "VALIDATION_ERROR");
        }
    }

    public string JsonText()
    {
        return Body.Length == 0 ? "" : Encoding.UTF8.GetString(Body);
    }

    public static Dictionary<string, List<string>> ParseQuery(string raw)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(raw))
        {
            return result;
        }
        foreach (string pair in raw.Split('&'))
        {
            if (pair.Length == 0)
            {
                continue;
            }
            int eq = pair.IndexOf('=');
            string key = eq >= 0 ? pair[..eq] : pair;
            string value = eq >= 0 ? pair[(eq + 1)..] : "";
            try
            {
                key = WebUtility.UrlDecode(key);
                value = WebUtility.UrlDecode(value);
            }
            catch
            {
                // keep raw
            }
            if (!result.TryGetValue(key, out var list))
            {
                list = new List<string>();
                result[key] = list;
            }
            list.Add(value);
        }
        return result;
    }
}

public sealed class HttpResponse
{
    public int Status { get; set; } = 200;
    public string? ContentType { get; set; } = "application/json; charset=utf-8";
    public ReadOnlyMemory<byte> Body { get; set; } = ReadOnlyMemory<byte>.Empty;
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
    };

    public static HttpResponse Json(int status, JsonNode? payload, bool noStore = false)
    {
        var response = new HttpResponse
        {
            Status = status,
            ContentType = "application/json; charset=utf-8",
            Body = Encoding.UTF8.GetBytes(payload?.ToJsonString(JsonOptions) ?? "null")
        };
        if (noStore)
        {
            response.Headers["Cache-Control"] = "no-store, must-revalidate";
        }
        return response;
    }

    public static HttpResponse Text(int status, string text, string contentType = "text/plain; charset=utf-8")
    {
        return new HttpResponse
        {
            Status = status,
            ContentType = contentType,
            Body = Encoding.UTF8.GetBytes(text)
        };
    }

    public static HttpResponse Bytes(int status, byte[] data, string contentType, string? cacheControl = null,
        string? etag = null, string? contentDisposition = null)
    {
        var response = new HttpResponse { Status = status, ContentType = contentType, Body = data };
        if (cacheControl != null)
        {
            response.Headers["Cache-Control"] = cacheControl;
        }
        if (etag != null)
        {
            response.Headers["ETag"] = etag;
        }
        if (contentDisposition != null)
        {
            response.Headers["Content-Disposition"] = contentDisposition;
        }
        return response;
    }
}

/// <summary>服务层可抛出的带状态码异常。</summary>
public sealed class HttpApiException : Exception
{
    public int Status { get; }
    public string Code { get; }

    public HttpApiException(string message, int status = 400, string code = "VALIDATION_ERROR") : base(message)
    {
        Status = status;
        Code = code;
    }
}

/// <summary>按行读取网络流，内部维护缓冲，支持读取指定长度的请求体。</summary>
internal sealed class HttpLineReader
{
    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[8192];
    private int _start;
    private int _count;

    public HttpLineReader(Stream stream)
    {
        _stream = stream;
    }

    public async Task<string?> ReadLineAsync(CancellationToken token)
    {
        while (true)
        {
            for (int i = _start; i < _count; i++)
            {
                if (_buffer[i] == (byte)'\n')
                {
                    int end = i;
                    if (end > _start && _buffer[end - 1] == (byte)'\r')
                    {
                        end--;
                    }
                    string line = Encoding.UTF8.GetString(_buffer, _start, end - _start);
                    _start = i + 1;
                    if (_start >= _count)
                    {
                        _start = 0;
                        _count = 0;
                    }
                    return line;
                }
            }
            if (_start > 0)
            {
                Buffer.BlockCopy(_buffer, _start, _buffer, 0, _count - _start);
                _count -= _start;
                _start = 0;
            }
            if (_count >= _buffer.Length)
            {
                throw new IOException("请求行过长");
            }
            int read = await ReadWithTimeoutAsync(_buffer, _count, _buffer.Length - _count, token);
            if (read <= 0)
            {
                return null;
            }
            _count += read;
        }
    }

    public async Task<byte[]> ReadBytesAsync(int count, CancellationToken token)
    {
        byte[] result = new byte[count];
        int filled = 0;
        if (_count > _start)
        {
            int take = Math.Min(count, _count - _start);
            Buffer.BlockCopy(_buffer, _start, result, 0, take);
            _start += take;
            filled += take;
            if (_start >= _count)
            {
                _start = 0;
                _count = 0;
            }
        }
        while (filled < count)
        {
            int read = await ReadWithTimeoutAsync(result, filled, count - filled, token);
            if (read <= 0)
            {
                throw new IOException("连接提前关闭");
            }
            filled += read;
        }
        return result;
    }

    private async Task<int> ReadWithTimeoutAsync(byte[] buffer, int offset, int count, CancellationToken token)
    {
        try
        {
            return await _stream.ReadAsync(buffer.AsMemory(offset, count), token)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(30), token);
        }
        catch (TimeoutException)
        {
            throw new IOException("读取请求超时");
        }
    }
}
