using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace MiniMonitor.XFtp;

/// <summary>统一的远程文件会话接口（FTP / FTPS / SFTP）。</summary>
internal interface IXFtpSession : IDisposable
{
    bool IsConnected { get; }
    string HomePath { get; }
    Task ConnectAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<XFtpEntry>> ListAsync(string path, CancellationToken cancellationToken);
    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken);
    Task DeleteAsync(XFtpEntry entry, CancellationToken cancellationToken);
    Task RenameAsync(string oldPath, string newPath, CancellationToken cancellationToken);
    Task DownloadFileAsync(string remotePath, string localPath,
        Action<long>? progress, CancellationToken cancellationToken);
    Task UploadFileAsync(string localPath, string remotePath,
        Action<long>? progress, CancellationToken cancellationToken);
}

internal static class XFtpSessionFactory
{
    public static IXFtpSession Create(XFtpSite site) => site.Protocol == XFtpProtocol.Sftp
        ? new XFtpSftpSession(site)
        : new XFtpFtpSession(site);
}

// =====================================================================
// FTP / FTPS
// =====================================================================

/// <summary>自研 FTP / 显式 FTPS 客户端，支持 MLSD / LIST、被动模式与断点安全的临时文件下载。</summary>
internal sealed class XFtpFtpSession : IXFtpSession
{
    private sealed record Response(int Code, string Text);

    private readonly XFtpSite _site;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TcpClient? _control;
    private Stream _stream = Stream.Null;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private bool _useMlsd = true;
    private bool _disposed;

    public bool IsConnected => !_disposed && _control?.Connected == true && _writer != null;
    public string HomePath { get; private set; } = "/";

    public XFtpFtpSession(XFtpSite site)
    {
        _site = site;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var tcp = new TcpClient
        {
            NoDelay = true
        };
        await tcp.ConnectAsync(_site.Host, _site.Port, cancellationToken);
        _control = tcp;
        _stream = tcp.GetStream();
        SetupControlStream(_stream);

        var greeting = await ReadResponseAsync(cancellationToken);
        if (greeting.Code != 220)
        {
            throw new IOException("FTP 服务器拒绝连接：" + greeting.Text);
        }

        if (_site.Protocol == XFtpProtocol.Ftps)
        {
            var auth = await SendAsync("AUTH TLS", cancellationToken);
            if (auth.Code != 234)
            {
                throw new IOException("服务器不支持显式 FTPS：" + FirstLine(auth.Text));
            }
            var ssl = new SslStream(_stream, false, (_, _, _, _) => true);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = _site.Host
            }, cancellationToken);
            _stream = ssl;
            SetupControlStream(ssl);
        }

        await LoginAsync(cancellationToken);

        if (_site.Protocol == XFtpProtocol.Ftps)
        {
            var pbsz = await SendAsync("PBSZ 0", cancellationToken);
            if (pbsz.Code / 100 != 2)
            {
                throw new IOException("FTPS PBSZ 失败：" + FirstLine(pbsz.Text));
            }
            var prot = await SendAsync("PROT P", cancellationToken);
            if (prot.Code / 100 != 2)
            {
                throw new IOException("FTPS PROT P 失败：" + FirstLine(prot.Text));
            }
        }

        // UTF-8 与二进制模式；失败不影响基本功能。
        await SendAsync("OPTS UTF8 ON", cancellationToken);
        var type = await SendAsync("TYPE I", cancellationToken);
        if (type.Code / 100 != 2)
        {
            throw new IOException("无法切换到二进制传输模式：" + FirstLine(type.Text));
        }
        var feat = await SendAsync("FEAT", cancellationToken);
        _useMlsd = feat.Text.Contains("MLSD", StringComparison.OrdinalIgnoreCase);
        var pwd = await SendAsync("PWD", cancellationToken);
        HomePath = ParsePwd(pwd.Text) ?? "/";
    }

    private void SetupControlStream(Stream stream)
    {
        _reader = new StreamReader(stream, new UTF8Encoding(false), false, 512, true);
        _writer = new StreamWriter(stream, new UTF8Encoding(false), 512, true)
        {
            NewLine = "\r\n",
            AutoFlush = false
        };
    }

    private async Task LoginAsync(CancellationToken cancellationToken)
    {
        string user = string.IsNullOrWhiteSpace(_site.User) ? "anonymous" : _site.User;
        string password = string.IsNullOrWhiteSpace(_site.Password)
            ? "anonymous@" : _site.Password;
        var userResponse = await SendAsync("USER " + user, cancellationToken);
        if (userResponse.Code == 331)
        {
            userResponse = await SendAsync("PASS " + password, cancellationToken);
        }
        if (userResponse.Code / 100 != 2 && userResponse.Code != 230)
        {
            throw new IOException("FTP 登录失败：" + FirstLine(userResponse.Text));
        }
    }

    private async Task<Response> SendAsync(string command, CancellationToken cancellationToken)
    {
        if (_writer == null)
        {
            throw new IOException("FTP 控制连接未建立。");
        }
        await _writer.WriteLineAsync(command.AsMemory(), cancellationToken);
        await _writer.FlushAsync();
        return await ReadResponseAsync(cancellationToken);
    }

    private async Task<Response> ReadResponseAsync(CancellationToken cancellationToken)
    {
        if (_reader == null)
        {
            throw new IOException("FTP 控制连接未建立。");
        }
        string? line = await _reader.ReadLineAsync(cancellationToken);
        if (line == null)
        {
            throw new IOException("FTP 服务器已关闭连接。");
        }
        string text = line;
        int code = ParseCode(line);
        if (line.Length > 3 && line[3] == '-')
        {
            string end = line[..3] + " ";
            var builder = new StringBuilder(line);
            while (true)
            {
                string? next = await _reader.ReadLineAsync(cancellationToken);
                if (next == null)
                {
                    throw new IOException("FTP 服务器已关闭连接。");
                }
                builder.Append('\n').Append(next);
                if (next.StartsWith(end, StringComparison.Ordinal))
                {
                    break;
                }
            }
            text = builder.ToString();
        }
        return new Response(code, text);
    }

    private static int ParseCode(string line) =>
        line.Length >= 3 && int.TryParse(line[..3], out int code) ? code : 0;

    private static string FirstLine(string text) =>
        text.Split('\n').FirstOrDefault()?.Trim() ?? text;

    private static string? ParsePwd(string text)
    {
        var match = Regex.Match(text, "\"([^\"]*)\"");
        return match.Success ? match.Groups[1].Value : null;
    }

    private async Task<(TcpClient Client, Stream Stream)> OpenDataAsync(
        CancellationToken cancellationToken)
    {
        string host = _site.Host;
        int port;
        var epsv = await SendAsync("EPSV", cancellationToken);
        if (epsv.Code == 229)
        {
            var match = Regex.Match(epsv.Text, @"\(\|\|\|(\d+)\|\)");
            if (!match.Success)
            {
                throw new IOException("无法解析 EPSV 响应：" + FirstLine(epsv.Text));
            }
            port = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        else
        {
            var pasv = await SendAsync("PASV", cancellationToken);
            if (pasv.Code != 227)
            {
                throw new IOException("服务器不支持被动模式：" + FirstLine(pasv.Text));
            }
            var match = Regex.Match(pasv.Text,
                @"(\d+),(\d+),(\d+),(\d+),(\d+),(\d+)");
            if (!match.Success)
            {
                throw new IOException("无法解析 PASV 响应：" + FirstLine(pasv.Text));
            }
            string ip = string.Join(".",
                match.Groups[1].Value, match.Groups[2].Value,
                match.Groups[3].Value, match.Groups[4].Value);
            if (!string.Equals(ip, "0.0.0.0", StringComparison.Ordinal))
            {
                host = ip;
            }
            port = int.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture) * 256
                + int.Parse(match.Groups[6].Value, CultureInfo.InvariantCulture);
        }

        var data = new TcpClient { NoDelay = true };
        try
        {
            await data.ConnectAsync(host, port, cancellationToken);
        }
        catch when (!string.Equals(host, _site.Host, StringComparison.OrdinalIgnoreCase))
        {
            // 某些服务器在 NAT 后返回内网地址，退回控制连接主机。
            data.Dispose();
            data = new TcpClient { NoDelay = true };
            await data.ConnectAsync(_site.Host, port, cancellationToken);
        }

        Stream stream = data.GetStream();
        if (_site.Protocol == XFtpProtocol.Ftps)
        {
            var ssl = new SslStream(stream, false, (_, _, _, _) => true);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = _site.Host
            }, cancellationToken);
            stream = ssl;
        }
        return (data, stream);
    }

    /// <summary>FTP 参数不需要加引号：路径原样发送，空格由服务器按整行解析。</summary>
    private static string Quote(string path) =>
        (path ?? "").Replace("\r", "").Replace("\n", "");

    private static string Combine(string path, string name) =>
        string.IsNullOrEmpty(path) || path == "/" ? "/" + name
            : path.TrimEnd('/') + "/" + name;

    public async Task<IReadOnlyList<XFtpEntry>> ListAsync(string path,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_useMlsd)
            {
                try
                {
                    return await ListMlsdCoreAsync(path, cancellationToken);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    _useMlsd = false;
                }
            }
            return await ListListCoreAsync(path, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<XFtpEntry>> ListMlsdCoreAsync(string path,
        CancellationToken cancellationToken)
    {
        var (data, stream) = await OpenDataAsync(cancellationToken);
        try
        {
            var response = await SendAsync("MLSD " + Quote(path), cancellationToken);
            if (response.Code != 125 && response.Code != 150)
            {
                throw new IOException("MLSD 失败：" + FirstLine(response.Text));
            }
            var entries = new List<XFtpEntry>();
            using (var reader = new StreamReader(stream, new UTF8Encoding(false),
                       false, 8192, true))
            {
                while (true)
                {
                    string? line = await reader.ReadLineAsync(cancellationToken);
                    if (line == null)
                    {
                        break;
                    }
                    var entry = ParseMlsd(line, path);
                    if (entry != null)
                    {
                        entries.Add(entry);
                    }
                }
            }
            try { stream.Dispose(); } catch { }
            try { data.Close(); } catch { }
            var done = await ReadResponseAsync(cancellationToken);
            if (done.Code != 226 && done.Code != 250)
            {
                throw new IOException("MLSD 传输未完成：" + FirstLine(done.Text));
            }
            return entries;
        }
        finally
        {
            try { data.Dispose(); } catch { }
        }
    }

    private static XFtpEntry? ParseMlsd(string line, string basePath)
    {
        int space = line.IndexOf(' ');
        if (space <= 0)
        {
            return null;
        }
        string facts = line[..space];
        string name = line[(space + 1)..];
        if (name is "." or "..")
        {
            return null;
        }
        bool isDirectory = false;
        bool skip = false;
        long size = 0;
        DateTime modified = DateTime.MinValue;
        string permissions = "";
        foreach (string fact in facts.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = fact.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }
            string key = fact[..eq].Trim().ToLowerInvariant();
            string value = fact[(eq + 1)..].Trim();
            switch (key)
            {
                case "type":
                    if (value.Equals("dir", StringComparison.OrdinalIgnoreCase))
                    {
                        isDirectory = true;
                    }
                    else if (value.Equals("cdir", StringComparison.OrdinalIgnoreCase)
                        || value.Equals("pdir", StringComparison.OrdinalIgnoreCase))
                    {
                        skip = true;
                    }
                    break;
                case "size":
                    long.TryParse(value, out size);
                    break;
                case "modify":
                    if (value.Length >= 14
                        && DateTime.TryParseExact(value[..14], "yyyyMMddHHmmss",
                            CultureInfo.InvariantCulture, DateTimeStyles.None,
                            out DateTime time))
                    {
                        modified = time;
                    }
                    break;
                case "perm":
                    permissions = value;
                    break;
            }
        }
        if (skip)
        {
            return null;
        }
        return new XFtpEntry
        {
            Name = name,
            FullPath = Combine(basePath, name),
            IsDirectory = isDirectory,
            Size = size,
            Modified = modified,
            Permissions = permissions
        };
    }

    private async Task<List<XFtpEntry>> ListListCoreAsync(string path,
        CancellationToken cancellationToken)
    {
        var (data, stream) = await OpenDataAsync(cancellationToken);
        try
        {
            var response = await SendAsync("LIST " + Quote(path), cancellationToken);
            if (response.Code != 125 && response.Code != 150)
            {
                throw new IOException("LIST 失败：" + FirstLine(response.Text));
            }
            var entries = new List<XFtpEntry>();
            using (var reader = new StreamReader(stream, Encoding.UTF8, false, 8192, true))
            {
                while (true)
                {
                    string? line = await reader.ReadLineAsync(cancellationToken);
                    if (line == null)
                    {
                        break;
                    }
                    var entry = ParseListLine(line, path);
                    if (entry != null)
                    {
                        entries.Add(entry);
                    }
                }
            }
            try { stream.Dispose(); } catch { }
            try { data.Close(); } catch { }
            var done = await ReadResponseAsync(cancellationToken);
            if (done.Code != 226 && done.Code != 250)
            {
                throw new IOException("LIST 传输未完成：" + FirstLine(done.Text));
            }
            return entries;
        }
        finally
        {
            try { data.Dispose(); } catch { }
        }
    }

    private static XFtpEntry? ParseListLine(string line, string basePath)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }
        var unix = Regex.Match(line,
            @"^([\-dlbcps])([rwxstST\-]{9})[+.@]?\s+\d+\s+\S+\s+\S+\s+(\d+)\s+(\w{3})\s+(\d{1,2})\s+(\d{1,2}:\d{2}|\d{4})\s+(.+)$");
        if (unix.Success)
        {
            string name = unix.Groups[8].Value;
            int arrow = name.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow > 0)
            {
                name = name[..arrow];
            }
            if (name is "." or "..")
            {
                return null;
            }
            string month = unix.Groups[4].Value;
            string day = unix.Groups[5].Value;
            string timeOrYear = unix.Groups[6].Value;
            DateTime modified = DateTime.MinValue;
            if (timeOrYear.Contains(':'))
            {
                DateTime.TryParseExact(month + " " + day + " " + timeOrYear,
                    "MMM d HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out modified);
            }
            else
            {
                DateTime.TryParseExact(month + " " + day + " " + timeOrYear,
                    "MMM d yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out modified);
            }
            return new XFtpEntry
            {
                Name = name,
                FullPath = Combine(basePath, name),
                IsDirectory = unix.Groups[1].Value == "d",
                Size = long.TryParse(unix.Groups[3].Value, out long size) ? size : 0,
                Modified = modified,
                Permissions = unix.Groups[2].Value
            };
        }

        var dos = Regex.Match(line,
            @"^(\d{2})-(\d{2})-(\d{2,4})\s+(\d{1,2}):(\d{2})\s*(AM|PM)\s+(<DIR>|\d+)\s+(.+)$",
            RegexOptions.IgnoreCase);
        if (dos.Success)
        {
            string name = dos.Groups[8].Value;
            if (name is "." or "..")
            {
                return null;
            }
            int year = int.Parse(dos.Groups[3].Value, CultureInfo.InvariantCulture);
            if (year < 100)
            {
                year += year < 70 ? 2000 : 1900;
            }
            int hour = int.Parse(dos.Groups[4].Value, CultureInfo.InvariantCulture);
            if (dos.Groups[6].Value.Equals("PM", StringComparison.OrdinalIgnoreCase)
                && hour < 12)
            {
                hour += 12;
            }
            else if (dos.Groups[6].Value.Equals("AM", StringComparison.OrdinalIgnoreCase)
                && hour == 12)
            {
                hour = 0;
            }
            DateTime modified = DateTime.MinValue;
            try
            {
                modified = new DateTime(year,
                    int.Parse(dos.Groups[1].Value, CultureInfo.InvariantCulture),
                    int.Parse(dos.Groups[2].Value, CultureInfo.InvariantCulture),
                    hour, int.Parse(dos.Groups[5].Value, CultureInfo.InvariantCulture), 0);
            }
            catch
            {
                // 日期非法时保持默认值。
            }
            bool isDirectory = dos.Groups[7].Value.Equals("<DIR>",
                StringComparison.OrdinalIgnoreCase);
            return new XFtpEntry
            {
                Name = name,
                FullPath = Combine(basePath, name),
                IsDirectory = isDirectory,
                Size = isDirectory || !long.TryParse(dos.Groups[7].Value, out long size)
                    ? 0 : size,
                Modified = modified
            };
        }
        return null;
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        await ExecuteAsync("MKD " + Quote(path), cancellationToken);
    }

    public async Task RenameAsync(string oldPath, string newPath,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var first = await SendAsync("RNFR " + Quote(oldPath), cancellationToken);
            if (first.Code != 350)
            {
                throw new IOException("重命名失败：" + FirstLine(first.Text));
            }
            var second = await SendAsync("RNTO " + Quote(newPath), cancellationToken);
            if (second.Code / 100 != 2)
            {
                throw new IOException("重命名失败：" + FirstLine(second.Text));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(XFtpEntry entry, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await DeleteCoreAsync(entry, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DeleteCoreAsync(XFtpEntry entry, CancellationToken cancellationToken)
    {
        if (entry.IsDirectory)
        {
            var children = _useMlsd
                ? await TryListMlsdAsync(entry.FullPath, cancellationToken)
                : await ListListCoreAsync(entry.FullPath, cancellationToken);
            foreach (var child in children)
            {
                await DeleteCoreAsync(child, cancellationToken);
            }
            var result = await SendAsync("RMD " + Quote(entry.FullPath), cancellationToken);
            if (result.Code / 100 != 2)
            {
                throw new IOException("删除目录失败：" + FirstLine(result.Text));
            }
            return;
        }
        var file = await SendAsync("DELE " + Quote(entry.FullPath), cancellationToken);
        if (file.Code / 100 != 2)
        {
            throw new IOException("删除文件失败：" + FirstLine(file.Text));
        }
    }

    private async Task<List<XFtpEntry>> TryListMlsdAsync(string path,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ListMlsdCoreAsync(path, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            _useMlsd = false;
            return await ListListCoreAsync(path, cancellationToken);
        }
    }

    private async Task ExecuteAsync(string command, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var response = await SendAsync(command, cancellationToken);
            if (response.Code / 100 != 2)
            {
                throw new IOException(FirstLine(response.Text));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DownloadFileAsync(string remotePath, string localPath,
        Action<long>? progress, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        string temp = localPath + ".xftp.part";
        try
        {
            var directory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var (data, stream) = await OpenDataAsync(cancellationToken);
            try
            {
                var response = await SendAsync("RETR " + Quote(remotePath), cancellationToken);
                if (response.Code != 125 && response.Code != 150)
                {
                    throw new IOException("下载失败：" + FirstLine(response.Text));
                }
                long total = 0;
                await using (var file = new FileStream(temp, FileMode.Create,
                    FileAccess.Write, FileShare.None, 81920, true))
                {
                    byte[] buffer = new byte[81920];
                    while (true)
                    {
                        int read = await stream.ReadAsync(buffer, cancellationToken);
                        if (read <= 0)
                        {
                            break;
                        }
                        await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        total += read;
                        progress?.Invoke(total);
                    }
                }
                // 先释放数据连接，再读取控制连接的完成响应。
                try { stream.Dispose(); } catch { }
                try { data.Close(); } catch { }
                var done = await ReadResponseAsync(cancellationToken);
                if (done.Code != 226 && done.Code != 250)
                {
                    throw new IOException("下载未完成：" + FirstLine(done.Text));
                }
            }
            finally
            {
                try { data.Dispose(); } catch { }
            }
            if (File.Exists(localPath))
            {
                File.Delete(localPath);
            }
            File.Move(temp, localPath);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UploadFileAsync(string localPath, string remotePath,
        Action<long>? progress, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var (data, stream) = await OpenDataAsync(cancellationToken);
            try
            {
                var response = await SendAsync("STOR " + Quote(remotePath), cancellationToken);
                if (response.Code != 125 && response.Code != 150)
                {
                    throw new IOException("上传失败：" + FirstLine(response.Text));
                }
                long total = 0;
                await using (var file = new FileStream(localPath, FileMode.Open,
                    FileAccess.Read, FileShare.Read, 81920, true))
                {
                    byte[] buffer = new byte[81920];
                    while (true)
                    {
                        int read = await file.ReadAsync(buffer, cancellationToken);
                        if (read <= 0)
                        {
                            break;
                        }
                        await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        total += read;
                        progress?.Invoke(total);
                    }
                    await stream.FlushAsync(cancellationToken);
                }
                // 上传完成后必须先关闭数据连接（发送 EOF），
                // 否则部分服务器会一直等待数据连接结束，导致读不到 226 响应。
                try { stream.Dispose(); } catch { }
                try { data.Close(); } catch { }
                var done = await ReadResponseAsync(cancellationToken);
                if (done.Code != 226 && done.Code != 250)
                {
                    throw new IOException("上传未完成：" + FirstLine(done.Text));
                }
            }
            finally
            {
                try { data.Dispose(); } catch { }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        bool wasConnected = _control?.Connected == true && _writer != null;
        _disposed = true;
        try
        {
            if (wasConnected)
            {
                _writer?.WriteLine("QUIT");
                _writer?.Flush();
            }
        }
        catch
        {
            // 忽略断开异常。
        }
        try { _reader?.Dispose(); } catch { }
        try { _writer?.Dispose(); } catch { }
        try { _stream.Dispose(); } catch { }
        try { _control?.Dispose(); } catch { }
        _gate.Dispose();
    }
}

// =====================================================================
// SFTP
// =====================================================================

/// <summary>基于 SSH.NET 的 SFTP 会话。</summary>
internal sealed class XFtpSftpSession : IXFtpSession
{
    private readonly XFtpSite _site;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SftpClient? _client;
    private SshClient? _jumpClient;
    private ForwardedPortLocal? _jumpForward;
    private bool _disposed;

    public bool IsConnected => !_disposed && _client?.IsConnected == true;
    public string HomePath => _client?.WorkingDirectory ?? "/";

    public XFtpSftpSession(XFtpSite site)
    {
        _site = site;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var client = await Task.Run(() =>
        {
            string host = _site.Host;
            int port = _site.Port > 0 ? _site.Port : 22;
            try
            {
                if (!string.IsNullOrWhiteSpace(_site.JumpHost))
                {
                    var jumpInfo = BuildConnectionInfo(_site.JumpHost,
                        _site.JumpPort > 0 ? _site.JumpPort : 22,
                        _site.JumpUser, _site.JumpPassword,
                        _site.JumpPrivateKeyPath, "");
                    _jumpClient = new SshClient(jumpInfo);
                    _jumpClient.HostKeyReceived += (_, e) => e.CanTrust = true;
                    _jumpClient.KeepAliveInterval = TimeSpan.FromSeconds(30);
                    _jumpClient.Connect();
                    _jumpForward = new ForwardedPortLocal("127.0.0.1", 0,
                        _site.Host, (uint)port);
                    _jumpClient.AddForwardedPort(_jumpForward);
                    _jumpForward.Start();
                    host = "127.0.0.1";
                    port = (int)_jumpForward.BoundPort;
                }

                var info = BuildConnectionInfo(host, port, _site.User,
                    _site.Password, _site.PrivateKeyPath, _site.PrivateKeyPassphrase);
                var sftp = new SftpClient(info);
                sftp.HostKeyReceived += (_, e) => e.CanTrust = true;
                sftp.Connect();
                return sftp;
            }
            catch
            {
                try { _jumpForward?.Stop(); } catch { }
                try { _jumpForward?.Dispose(); } catch { }
                try { _jumpClient?.Disconnect(); } catch { }
                try { _jumpClient?.Dispose(); } catch { }
                _jumpForward = null;
                _jumpClient = null;
                throw;
            }
        }, cancellationToken);
        _client = client;
    }

    private static ConnectionInfo BuildConnectionInfo(string host, int port,
        string user, string password, string privateKeyPath, string passphrase)
    {
        if (!string.IsNullOrWhiteSpace(privateKeyPath) && File.Exists(privateKeyPath))
        {
            var keyFile = string.IsNullOrEmpty(passphrase)
                ? new PrivateKeyFile(privateKeyPath)
                : new PrivateKeyFile(privateKeyPath, passphrase);
            var keyInfo = new PrivateKeyConnectionInfo(host, port, user, keyFile);
            keyInfo.Timeout = TimeSpan.FromSeconds(25);
            return keyInfo;
        }
        if (!string.IsNullOrWhiteSpace(password))
        {
            var passwordInfo = new PasswordConnectionInfo(host, port, user, password);
            passwordInfo.Timeout = TimeSpan.FromSeconds(25);
            return passwordInfo;
        }
        var noneInfo = new ConnectionInfo(host, port, user,
            new NoneAuthenticationMethod(user));
        noneInfo.Timeout = TimeSpan.FromSeconds(25);
        return noneInfo;
    }

    public async Task<IReadOnlyList<XFtpEntry>> ListAsync(string path,
        CancellationToken cancellationToken)
    {
        var client = await EnsureClientAsync();
        return await Task.Run(() =>
        {
            var result = new List<XFtpEntry>();
            foreach (var file in client.ListDirectory(path))
            {
                if (file.Name is "." or "..")
                {
                    continue;
                }
                result.Add(new XFtpEntry
                {
                    Name = file.Name,
                    FullPath = file.FullName,
                    IsDirectory = file.IsDirectory,
                    Size = file.IsDirectory ? 0 : file.Length,
                    Modified = file.LastWriteTime,
                    Permissions = Permissions(file.Attributes),
                    Owner = file.Attributes.UserId.ToString(CultureInfo.InvariantCulture)
                });
            }
            return (IReadOnlyList<XFtpEntry>)result;
        }, cancellationToken);
    }

    private static string Permissions(SftpFileAttributes attributes)
    {
        char[] value = new char[9];
        value[0] = attributes.OwnerCanRead ? 'r' : '-';
        value[1] = attributes.OwnerCanWrite ? 'w' : '-';
        value[2] = attributes.OwnerCanExecute ? 'x' : '-';
        value[3] = attributes.GroupCanRead ? 'r' : '-';
        value[4] = attributes.GroupCanWrite ? 'w' : '-';
        value[5] = attributes.GroupCanExecute ? 'x' : '-';
        value[6] = attributes.OthersCanRead ? 'r' : '-';
        value[7] = attributes.OthersCanWrite ? 'w' : '-';
        value[8] = attributes.OthersCanExecute ? 'x' : '-';
        return new string(value);
    }

    private async Task<SftpClient> EnsureClientAsync()
    {
        var client = _client;
        if (client == null || !client.IsConnected)
        {
            throw new IOException("SFTP 未连接。");
        }
        await Task.CompletedTask;
        return client;
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        var client = await EnsureClientAsync();
        await Task.Run(() => client.CreateDirectory(path), cancellationToken);
    }

    public async Task RenameAsync(string oldPath, string newPath,
        CancellationToken cancellationToken)
    {
        var client = await EnsureClientAsync();
        await Task.Run(() => client.RenameFile(oldPath, newPath), cancellationToken);
    }

    public async Task DeleteAsync(XFtpEntry entry, CancellationToken cancellationToken)
    {
        var client = await EnsureClientAsync();
        await Task.Run(() =>
        {
            if (entry.IsDirectory)
            {
                DeleteDirectory(client, entry.FullPath);
            }
            else
            {
                client.DeleteFile(entry.FullPath);
            }
        }, cancellationToken);
    }

    private static void DeleteDirectory(SftpClient client, string path)
    {
        foreach (var file in client.ListDirectory(path))
        {
            if (file.Name is "." or "..")
            {
                continue;
            }
            if (file.IsDirectory)
            {
                DeleteDirectory(client, file.FullName);
            }
            else
            {
                client.DeleteFile(file.FullName);
            }
        }
        client.DeleteDirectory(path);
    }

    public async Task DownloadFileAsync(string remotePath, string localPath,
        Action<long>? progress, CancellationToken cancellationToken)
    {
        var client = await EnsureClientAsync();
        var directory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
        await Task.Run(() =>
        {
            using var stream = new FileStream(localPath, FileMode.Create,
                FileAccess.Write, FileShare.None);
            client.DownloadFile(remotePath, stream,
                uploaded => progress?.Invoke((long)uploaded));
        }, cancellationToken);
    }

    public async Task UploadFileAsync(string localPath, string remotePath,
        Action<long>? progress, CancellationToken cancellationToken)
    {
        var client = await EnsureClientAsync();
        await Task.Run(() =>
        {
            using var stream = new FileStream(localPath, FileMode.Open,
                FileAccess.Read, FileShare.Read);
            client.UploadFile(stream, remotePath, true,
                uploaded => progress?.Invoke((long)uploaded));
        }, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        try { _client?.Disconnect(); } catch { }
        try { _client?.Dispose(); } catch { }
        try { _jumpForward?.Stop(); } catch { }
        try { _jumpForward?.Dispose(); } catch { }
        try { _jumpClient?.Disconnect(); } catch { }
        try { _jumpClient?.Dispose(); } catch { }
        _gate.Dispose();
    }
}
