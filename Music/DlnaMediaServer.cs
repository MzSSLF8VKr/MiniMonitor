using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace MiniMonitor.Music;

/// <summary>
/// 可选的 SSDP / DLNA 媒体共享（默认关闭）。只有用户在设置中同时打开
/// “联网总开关”和“SSDP/DLNA”后才会监听局域网端口；不访问互联网。
/// </summary>
public sealed class DlnaMediaServer : IDisposable
{
    private const string MediaServerType = "urn:schemas-upnp-org:device:MediaServer:1";
    private const string ContentDirectoryType = "urn:schemas-upnp-org:service:ContentDirectory:1";
    private const string MulticastAddress = "239.255.255.250";
    private const int SsdpPort = 1900;

    private readonly MusicLibrary _library;
    private readonly int _httpPort;
    private readonly string _udn;
    private readonly object _gate = new();
    private TcpListener? _tcp;
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private System.Threading.Timer? _notifyTimer;
    private bool _disposed;

    public DlnaMediaServer(MusicLibrary library, int httpPort)
    {
        _library = library;
        _httpPort = Math.Clamp(httpPort, 1024, 65535);
        _udn = "uuid:" + (library.GetMeta("dlna_udn") ?? CreateAndStoreUdn(library));
    }

    private static string CreateAndStoreUdn(MusicLibrary library)
    {
        string udn = Guid.NewGuid().ToString();
        library.SetMeta("dlna_udn", udn);
        return udn;
    }

    public event EventHandler<string>? Log;

    public bool IsRunning { get; private set; }

    public string Status { get; private set; } = "未启动";

    public string LocalAddress { get; private set; } = "";

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || IsRunning)
            {
                return;
            }
            try
            {
                LocalAddress = GetLocalAddress();
                _cts = new CancellationTokenSource();
                _tcp = new TcpListener(IPAddress.Any, _httpPort);
                _tcp.Start();
                _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
                StartSsdp(_cts.Token);
                _notifyTimer = new System.Threading.Timer(_ => SendNotify("ssdp:alive"), null,
                    TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(15));
                IsRunning = true;
                Status = $"运行中 · http://{LocalAddress}:{_httpPort}/description.xml";
                Log?.Invoke(this, "DLNA 媒体服务已启动：" + Status);
            }
            catch (Exception ex)
            {
                StopInternal();
                Status = "启动失败：" + ex.Message;
                Log?.Invoke(this, Status);
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!IsRunning)
            {
                return;
            }
            SendNotify("ssdp:byebye");
            StopInternal();
            Status = "已停止";
            Log?.Invoke(this, "DLNA 媒体服务已停止");
        }
    }

    private void StopInternal()
    {
        _cts?.Cancel();
        try { _tcp?.Stop(); } catch { }
        _tcp = null;
        try { _udp?.Close(); } catch { }
        _udp = null;
        _notifyTimer?.Dispose();
        _notifyTimer = null;
        _cts?.Dispose();
        _cts = null;
        IsRunning = false;
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _tcp != null)
        {
            TcpClient client;
            try
            {
                client = await _tcp.AcceptTcpClientAsync(token);
            }
            catch
            {
                break;
            }
            _ = Task.Run(() => HandleClientAsync(client, token), token);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 8192, leaveOpen: true);
                string? requestLine = await reader.ReadLineAsync(token);
                if (string.IsNullOrWhiteSpace(requestLine))
                {
                    return;
                }
                var firstLine = requestLine.Split(' ');
                string method = firstLine[0];
                string path = firstLine.Length > 1 ? firstLine[1] : "/";
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string? line;
                int contentLength = 0;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(token)))
                {
                    int colon = line.IndexOf(':');
                    if (colon > 0)
                    {
                        string key = line[..colon].Trim();
                        string value = line[(colon + 1)..].Trim();
                        headers[key] = value;
                        if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                            && int.TryParse(value, out int length))
                        {
                            contentLength = length;
                        }
                    }
                }
                if (path.StartsWith("/description.xml", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteTextAsync(stream, DescriptionXml(), "text/xml; charset=utf-8", token);
                    return;
                }
                if (path.StartsWith("/ContentDirectory.xml", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteTextAsync(stream, ScpdXml(), "text/xml; charset=utf-8", token);
                    return;
                }
                if (path.StartsWith("/ctl/ContentDir", StringComparison.OrdinalIgnoreCase))
                {
                    string body = "";
                    if (contentLength > 0)
                    {
                        var buffer = new char[contentLength];
                        int read = 0;
                        while (read < contentLength)
                        {
                            int count = await reader.ReadAsync(buffer.AsMemory(read, contentLength - read), token);
                            if (count <= 0) break;
                            read += count;
                        }
                        body = new string(buffer, 0, read);
                    }
                    string objectId = ExtractTag(body, "ObjectID") ?? "0";
                    string result = Browse(objectId, out int returned, out int total);
                    string response = SoapBrowseResponse(result, returned, total);
                    await WriteTextAsync(stream, response, "text/xml; charset=utf-8", token);
                    return;
                }
                if (path.StartsWith("/media/", StringComparison.OrdinalIgnoreCase))
                {
                    await ServeMediaAsync(stream, path, headers, token);
                    return;
                }
                await WriteTextAsync(stream, "Not Found", "text/plain", token, 404);
            }
            catch
            {
                // 局域网客户端断开 / 请求异常
            }
        }
    }

    private void StartSsdp(CancellationToken token)
    {
        try
        {
            var udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, SsdpPort));
            udp.JoinMulticastGroup(IPAddress.Parse(MulticastAddress));
            _udp = udp;
            _ = Task.Run(() => SsdpLoopAsync(token));
        }
        catch (Exception ex)
        {
            Log?.Invoke(this, "SSDP 监听失败（可能被其他媒体服务占用）：" + ex.Message);
        }
    }

    private async Task SsdpLoopAsync(CancellationToken token)
    {
        var udp = _udp;
        while (!token.IsCancellationRequested && udp != null)
        {
            try
            {
                var result = await udp.ReceiveAsync(token);
                string text = Encoding.ASCII.GetString(result.Buffer);
                if (!text.StartsWith("M-SEARCH", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!text.Contains("ssdp:discover", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!text.Contains("MediaServer", StringComparison.OrdinalIgnoreCase)
                    && !text.Contains("ssdp:all", StringComparison.OrdinalIgnoreCase)
                    && !text.Contains("upnp:rootdevice", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string response = string.Join("\r\n",
                    "HTTP/1.1 200 OK",
                    "CACHE-CONTROL: max-age=1800",
                    "EXT:",
                    $"LOCATION: http://{LocalAddress}:{_httpPort}/description.xml",
                    "SERVER: Windows/10 UPnP/1.0 MiniMonitorMusic/1.0",
                    $"ST: {MediaServerType}",
                    $"USN: {_udn}::{MediaServerType}",
                    "", "");
                byte[] bytes = Encoding.ASCII.GetBytes(response);
                await udp.SendAsync(bytes, bytes.Length, result.RemoteEndPoint);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(300, CancellationToken.None);
            }
        }
    }

    private void SendNotify(string nts)
    {
        try
        {
            using var udp = new UdpClient();
            string message = string.Join("\r\n",
                "NOTIFY * HTTP/1.1",
                $"HOST: {MulticastAddress}:{SsdpPort}",
                "CACHE-CONTROL: max-age=1800",
                $"LOCATION: http://{LocalAddress}:{_httpPort}/description.xml",
                "SERVER: Windows/10 UPnP/1.0 MiniMonitorMusic/1.0",
                $"NT: {MediaServerType}",
                $"NTS: {nts}",
                $"USN: {_udn}::{MediaServerType}",
                "", "");
            byte[] bytes = Encoding.ASCII.GetBytes(message);
            udp.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Parse(MulticastAddress), SsdpPort));
        }
        catch
        {
            // 组播发送失败不终止服务
        }
    }

    private string Browse(string objectId, out int returned, out int total)
    {
        return objectId switch
        {
            "tracks" => BuildTracksDidl(out returned, out total),
            _ => BuildRootDidl(out returned, out total)
        };
    }

    private string BuildRootDidl(out int returned, out int total)
    {
        var tracks = _library.GetAllTracks();
        returned = 1;
        total = 1;
        var builder = new StringBuilder();
        builder.Append("<DIDL-Lite xmlns=\"urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/\" ")
            .Append("xmlns:dc=\"http://purl.org/dc/elements/1.1/\" ")
            .Append("xmlns:upnp=\"urn:schemas-upnp-org:metadata-1-0/upnp/\">");
        builder.Append("<container id=\"tracks\" parentID=\"0\" restricted=\"1\" searchable=\"0\">")
            .Append("<dc:title>全部歌曲</dc:title>")
            .Append("<upnp:class>object.container</upnp:class>")
            .Append($"<childCount>{tracks.Count}</childCount>")
            .Append("</container>");
        builder.Append("</DIDL-Lite>");
        return builder.ToString();
    }

    private string BuildTracksDidl(out int returned, out int total)
    {
        var tracks = _library.GetAllTracks();
        returned = tracks.Count;
        total = tracks.Count;
        var builder = new StringBuilder();
        builder.Append("<DIDL-Lite xmlns=\"urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/\" ")
            .Append("xmlns:dc=\"http://purl.org/dc/elements/1.1/\" ")
            .Append("xmlns:upnp=\"urn:schemas-upnp-org:metadata-1-0/upnp/\">");
        foreach (var track in tracks)
        {
            string mime = MimeFor(Path.GetExtension(track.Path));
            string url = $"http://{LocalAddress}:{_httpPort}/media/{track.Id}";
            builder.Append($"<item id=\"track:{track.Id}\" parentID=\"tracks\" restricted=\"1\">")
                .Append($"<dc:title>{Xml(track.DisplayTitle)}</dc:title>")
                .Append("<upnp:class>object.item.audioItem.musicTrack</upnp:class>")
                .Append($"<upnp:artist>{Xml(track.DisplayArtist)}</upnp:artist>")
                .Append($"<upnp:album>{Xml(track.DisplayAlbum)}</upnp:album>")
                .Append($"<res protocolInfo=\"http-get:*:{mime}:*\" size=\"{track.FileSize}\">")
                .Append(Xml(url))
                .Append("</res></item>");
        }
        builder.Append("</DIDL-Lite>");
        return builder.ToString();
    }

    private async Task ServeMediaAsync(NetworkStream stream, string path, Dictionary<string, string> headers,
        CancellationToken token)
    {
        string idText = path["/media/".Length..];
        if (!long.TryParse(idText, out long id))
        {
            await WriteTextAsync(stream, "Bad Request", "text/plain", token, 400);
            return;
        }
        var track = _library.GetTrack(id);
        if (track == null || !File.Exists(track.Path))
        {
            await WriteTextAsync(stream, "Not Found", "text/plain", token, 404);
            return;
        }
        var info = new FileInfo(track.Path);
        long start = 0;
        long end = info.Length - 1;
        bool partial = false;
        if (headers.TryGetValue("Range", out string? range)
            && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = range[6..].Split('-');
            if (parts.Length == 2)
            {
                if (long.TryParse(parts[0], out long parsedStart)) start = parsedStart;
                if (long.TryParse(parts[1], out long parsedEnd)) end = parsedEnd;
                partial = true;
            }
        }
        if (start < 0) start = 0;
        if (end >= info.Length) end = info.Length - 1;
        if (start > end)
        {
            await WriteTextAsync(stream, "Range Not Satisfiable", "text/plain", token, 416);
            return;
        }
        long length = end - start + 1;
        string mime = MimeFor(Path.GetExtension(track.Path));
        var header = new StringBuilder();
        header.Append(partial ? "HTTP/1.1 206 Partial Content\r\n" : "HTTP/1.1 200 OK\r\n");
        header.Append($"Content-Type: {mime}\r\n");
        header.Append($"Content-Length: {length}\r\n");
        header.Append($"Accept-Ranges: bytes\r\n");
        header.Append($"Content-Disposition: inline; filename=\"{Xml(track.FileName)}\"\r\n");
        if (partial)
        {
            header.Append($"Content-Range: bytes {start}-{end}/{info.Length}\r\n");
        }
        header.Append("Connection: close\r\n\r\n");
        byte[] headerBytes = Encoding.ASCII.GetBytes(header.ToString());
        await stream.WriteAsync(headerBytes, token);
        using var file = new FileStream(track.Path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        file.Seek(start, SeekOrigin.Begin);
        var buffer = new byte[64 * 1024];
        long remaining = length;
        while (remaining > 0 && !token.IsCancellationRequested)
        {
            int read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), token);
            if (read <= 0) break;
            await stream.WriteAsync(buffer.AsMemory(0, read), token);
            remaining -= read;
        }
    }

    private static async Task WriteTextAsync(NetworkStream stream, string text, string contentType,
        CancellationToken token, int status = 200)
    {
        byte[] body = Encoding.UTF8.GetBytes(text);
        string reason = status switch
        {
            200 => "OK",
            400 => "Bad Request",
            404 => "Not Found",
            416 => "Range Not Satisfiable",
            _ => "Internal Server Error"
        };
        string header = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\n" +
                        $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), token);
        await stream.WriteAsync(body, token);
    }

    private string DescriptionXml()
    {
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <root xmlns="urn:schemas-upnp-org:device-1-0">
              <specVersion><major>1</major><minor>0</minor></specVersion>
              <device>
                <deviceType>{MediaServerType}</deviceType>
                <friendlyName>离线音乐播放器</friendlyName>
                <manufacturer>MiniMonitor</manufacturer>
                <modelDescription>Windows 原生离线音乐播放器</modelDescription>
                <modelName>MiniMonitor Music</modelName>
                <modelNumber>1.44.0</modelNumber>
                <UDN>{_udn}</UDN>
                <serviceList>
                  <service>
                    <serviceType>{ContentDirectoryType}</serviceType>
                    <serviceId>urn:upnp-org:serviceId:ContentDirectory</serviceId>
                    <SCPDURL>/ContentDirectory.xml</SCPDURL>
                    <controlURL>/ctl/ContentDir</controlURL>
                    <eventSubURL>/evt/ContentDir</eventSubURL>
                  </service>
                </serviceList>
              </device>
            </root>
            """;
    }

    private static string ScpdXml()
    {
        return """
            <?xml version="1.0" encoding="utf-8"?>
            <scpd xmlns="urn:schemas-upnp-org:service-1-0">
              <specVersion><major>1</major><minor>0</minor></specVersion>
              <actionList>
                <action>
                  <name>Browse</name>
                  <argumentList>
                    <argument><name>ObjectID</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_ObjectID</relatedStateVariable></argument>
                    <argument><name>BrowseFlag</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_BrowseFlag</relatedStateVariable></argument>
                    <argument><name>Filter</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_Filter</relatedStateVariable></argument>
                    <argument><name>StartingIndex</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_Index</relatedStateVariable></argument>
                    <argument><name>RequestedCount</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_Count</relatedStateVariable></argument>
                    <argument><name>SortCriteria</name><direction>in</direction><relatedStateVariable>A_ARG_TYPE_SortCriteria</relatedStateVariable></argument>
                    <argument><name>Result</name><direction>out</direction><relatedStateVariable>A_ARG_TYPE_Result</relatedStateVariable></argument>
                    <argument><name>NumberReturned</name><direction>out</direction><relatedStateVariable>A_ARG_TYPE_Count</relatedStateVariable></argument>
                    <argument><name>TotalMatches</name><direction>out</direction><relatedStateVariable>A_ARG_TYPE_Count</relatedStateVariable></argument>
                    <argument><name>UpdateID</name><direction>out</direction><relatedStateVariable>A_ARG_TYPE_UpdateID</relatedStateVariable></argument>
                  </argumentList>
                </action>
              </actionList>
              <serviceStateTable>
                <stateVariable sendEvents="no"><name>A_ARG_TYPE_ObjectID</name><dataType>string</dataType></stateVariable>
                <stateVariable sendEvents="no"><name>A_ARG_TYPE_BrowseFlag</name><dataType>string</dataType></stateVariable>
                <stateVariable sendEvents="no"><name>A_ARG_TYPE_Filter</name><dataType>string</dataType></stateVariable>
                <stateVariable sendEvents="no"><name>A_ARG_TYPE_Index</name><dataType>ui4</dataType></stateVariable>
                <stateVariable sendEvents="no"><name>A_ARG_TYPE_Count</name><dataType>ui4</dataType></stateVariable>
                <stateVariable sendEvents="no"><name>A_ARG_TYPE_SortCriteria</name><dataType>string</dataType></stateVariable>
                <stateVariable sendEvents="no"><name>A_ARG_TYPE_Result</name><dataType>string</dataType></stateVariable>
                <stateVariable sendEvents="no"><name>A_ARG_TYPE_UpdateID</name><dataType>ui4</dataType></stateVariable>
              </serviceStateTable>
            </scpd>
            """;
    }

    private static string SoapBrowseResponse(string didl, int returned, int total)
    {
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"
                        s:encodingStyle="http://schemas.xmlsoap.org/soap/encoding/">
              <s:Body>
                <u:BrowseResponse xmlns:u="{ContentDirectoryType}">
                  <Result>{Xml(didl)}</Result>
                  <NumberReturned>{returned}</NumberReturned>
                  <TotalMatches>{total}</TotalMatches>
                  <UpdateID>1</UpdateID>
                </u:BrowseResponse>
              </s:Body>
            </s:Envelope>
            """;
    }

    private static string? ExtractTag(string xml, string tag)
    {
        int start = xml.IndexOf("<" + tag + ">", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }
        start += tag.Length + 2;
        int end = xml.IndexOf("</" + tag + ">", start, StringComparison.OrdinalIgnoreCase);
        return end > start ? xml[start..end] : null;
    }

    private static string MimeFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".mp3" => "audio/mpeg",
        ".flac" => "audio/flac",
        ".wav" => "audio/wav",
        ".m4a" => "audio/mp4",
        ".aac" => "audio/aac",
        ".ogg" or ".oga" => "audio/ogg",
        ".opus" => "audio/ogg",
        ".wma" => "audio/x-ms-wma",
        ".aiff" or ".aif" => "audio/aiff",
        ".ape" => "audio/ape",
        ".wv" => "audio/x-wavpack",
        ".dsf" or ".dff" => "audio/dsd",
        _ => "application/octet-stream"
    };

    private static string Xml(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&apos;");

    private static string GetLocalAddress()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up
                    || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }
                foreach (var address in nic.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork
                        && !IPAddress.IsLoopback(address.Address))
                    {
                        return address.Address.ToString();
                    }
                }
            }
        }
        catch
        {
            // 忽略
        }
        return "127.0.0.1";
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
    }
}
