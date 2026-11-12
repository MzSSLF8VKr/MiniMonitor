using System.Diagnostics;
using System.IO.Ports;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace MiniMonitor.XShell;

/// <summary>终端连接抽象：SSH / Telnet / 串口 / 本地 Shell。</summary>
public interface IXShellTransport : IAsyncDisposable
{
    bool IsConnected { get; }
    string Kind { get; }
    event Action<string>? Output;
    event Action<string>? Closed;

    Task ConnectAsync(int columns, int rows, CancellationToken cancellationToken);
    Task WriteAsync(string text, CancellationToken cancellationToken);
    void Resize(int columns, int rows);
    void Disconnect(string reason = "连接已断开。");
    Task<SftpClient?> CreateSftpClientAsync(CancellationToken cancellationToken);
}

public static class XShellTransportFactory
{
    public static IXShellTransport Create(
        XSessionProfile profile,
        XShellStore store,
        Func<string, string, bool>? verifyHostKey = null)
    {
        return profile.Protocol switch
        {
            XProtocol.Ssh => new SshXShellTransport(profile, store, verifyHostKey),
            XProtocol.Telnet => new TelnetXShellTransport(profile),
            XProtocol.Serial => new SerialXShellTransport(profile),
            XProtocol.Local => new LocalXShellTransport(profile),
            _ => throw new NotSupportedException("不支持的连接协议。")
        };
    }
}

internal sealed class SshXShellTransport : IXShellTransport
{
    private readonly XSessionProfile _profile;
    private readonly XShellStore _store;
    private readonly Func<string, string, bool>? _verifyHostKey;
    private readonly List<IDisposable> _disposables = new();
    private readonly List<ForwardedPort> _forwards = new();
    private SshClient? _client;
    private SshClient? _jumpClient;
    private ForwardedPortLocal? _jumpForward;
    private ShellStream? _shell;
    private CancellationTokenSource? _pumpCts;
    private bool _connected;

    public SshXShellTransport(XSessionProfile profile, XShellStore store,
        Func<string, string, bool>? verifyHostKey)
    {
        _profile = profile;
        _store = store;
        _verifyHostKey = verifyHostKey;
    }

    public bool IsConnected => _connected && _shell != null;
    public string Kind => "SSH";
    public event Action<string>? Output;
    public event Action<string>? Closed;

    public async Task ConnectAsync(int columns, int rows, CancellationToken cancellationToken)
    {
        await Task.Run(() => ConnectCore(columns, rows, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    private void ConnectCore(int columns, int rows, CancellationToken cancellationToken)
    {
        try
        {
            string targetHost = _profile.Host;
            int targetPort = _profile.Port;
            if (_profile.UseJumpHost && !string.IsNullOrWhiteSpace(_profile.JumpHost))
            {
                var jumpInfo = BuildConnectionInfo(
                    _profile.JumpHost, _profile.JumpPort, _profile.JumpUserName,
                    _profile.JumpPassword, _profile.JumpPrivateKeyPath, "",
                    string.IsNullOrWhiteSpace(_profile.JumpPrivateKeyPath)
                        ? XAuthMethod.Password : XAuthMethod.PrivateKey);
                _jumpClient = new SshClient(jumpInfo);
                AttachHostKeyVerification(_jumpClient, _profile.JumpHost, _profile.JumpPort);
                _jumpClient.KeepAliveInterval = TimeSpan.FromSeconds(30);
                _jumpClient.Connect();
                _jumpForward = new ForwardedPortLocal("127.0.0.1", 0,
                    _profile.Host, (uint)_profile.Port);
                _jumpClient.AddForwardedPort(_jumpForward);
                _jumpForward.Start();
                targetHost = "127.0.0.1";
                targetPort = (int)_jumpForward.BoundPort;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var info = BuildConnectionInfo(targetHost, targetPort, _profile.UserName,
                _profile.Password, _profile.PrivateKeyPath, _profile.PrivateKeyPassphrase,
                _profile.AuthMethod);
            _client = new SshClient(info);
            AttachHostKeyVerification(_client, _profile.Host, _profile.Port);
            _client.KeepAliveInterval = TimeSpan.FromSeconds(30);
            _client.Connect();
            cancellationToken.ThrowIfCancellationRequested();
            _shell = _client.CreateShellStream(_profile.TerminalType,
                (uint)Math.Clamp(columns, 20, 500),
                (uint)Math.Clamp(rows, 5, 300),
                0, 0, 64 * 1024);
            _connected = true;
            StartPump();
            StartTunnels();
        }
        catch
        {
            DisconnectCore();
            throw;
        }
    }

    private ConnectionInfo BuildConnectionInfo(string host, int port, string userName,
        string password, string privateKeyPath, string passphrase, XAuthMethod authMethod)
    {
        var methods = new List<AuthenticationMethod>();
        if (authMethod == XAuthMethod.PrivateKey)
        {
            if (string.IsNullOrWhiteSpace(privateKeyPath) || !File.Exists(privateKeyPath))
            {
                throw new FileNotFoundException("找不到私钥文件，请在会话属性中重新选择。", privateKeyPath);
            }
            var key = string.IsNullOrEmpty(passphrase)
                ? new PrivateKeyFile(privateKeyPath)
                : new PrivateKeyFile(privateKeyPath, passphrase);
            _disposables.Add(key);
            methods.Add(new PrivateKeyAuthenticationMethod(userName, key));
        }
        else if (authMethod == XAuthMethod.KeyboardInteractive)
        {
            var keyboard = new KeyboardInteractiveAuthenticationMethod(userName);
            keyboard.AuthenticationPrompt += (_, e) =>
            {
                foreach (var prompt in e.Prompts)
                {
                    prompt.Response = password ?? "";
                }
            };
            methods.Add(keyboard);
        }
        else if (authMethod == XAuthMethod.None)
        {
            methods.Add(new NoneAuthenticationMethod(userName));
        }
        else
        {
            methods.Add(new PasswordAuthenticationMethod(userName, password ?? ""));
        }

        // 有些服务器同时提供 password 与 keyboard-interactive，补一个后备方法可提高兼容性。
        if (authMethod == XAuthMethod.Password && !string.IsNullOrEmpty(password))
        {
            var keyboard = new KeyboardInteractiveAuthenticationMethod(userName);
            keyboard.AuthenticationPrompt += (_, e) =>
            {
                foreach (var prompt in e.Prompts)
                {
                    prompt.Response = password;
                }
            };
            methods.Add(keyboard);
        }

        return new ConnectionInfo(host, port, userName, methods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(20),
            RetryAttempts = 1
        };
    }

    private void AttachHostKeyVerification(BaseClient client, string host, int port)
    {
        client.HostKeyReceived += (_, e) =>
        {
            string fingerprint;
            try
            {
                fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(e.HostKey)).TrimEnd('=');
            }
            catch
            {
                fingerprint = "SHA256:?";
            }
            string key = host + ":" + port + " " + fingerprint;
            if (_store.IsKnownHost(key))
            {
                e.CanTrust = true;
                return;
            }
            bool trust = _verifyHostKey?.Invoke(host, fingerprint) ?? true;
            e.CanTrust = trust;
            if (trust)
            {
                _store.RememberHost(key);
            }
        };
    }

    private void StartPump()
    {
        _pumpCts = new CancellationTokenSource();
        var token = _pumpCts.Token;
        var shell = _shell!;
        _ = Task.Run(() =>
        {
            var decoder = new UTF8Encoding(false).GetDecoder();
            var bytes = new byte[8192];
            var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
            try
            {
                while (!token.IsCancellationRequested && _connected)
                {
                    if (!shell.DataAvailable)
                    {
                        Thread.Sleep(12);
                        continue;
                    }
                    int read = shell.Read(bytes, 0, bytes.Length);
                    if (read <= 0)
                    {
                        break;
                    }
                    int count = decoder.GetChars(bytes, 0, read, chars, 0);
                    if (count > 0)
                    {
                        Output?.Invoke(new string(chars, 0, count));
                    }
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    Closed?.Invoke("SSH 连接中断：" + ex.Message);
                }
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    _connected = false;
                    Closed?.Invoke("SSH 连接已关闭。");
                }
            }
        }, token);
    }

    private void StartTunnels()
    {
        if (_client == null)
        {
            return;
        }
        foreach (var tunnel in _profile.Tunnels.Where(t => t.Enabled))
        {
            try
            {
                ForwardedPort port = tunnel.Type switch
                {
                    XTunnelType.Remote => new ForwardedPortRemote(
                        tunnel.BindHost, (uint)tunnel.BindPort,
                        tunnel.TargetHost, (uint)tunnel.TargetPort),
                    XTunnelType.Dynamic => new ForwardedPortDynamic(
                        tunnel.BindHost, (uint)tunnel.BindPort),
                    _ => new ForwardedPortLocal(
                        tunnel.BindHost, (uint)tunnel.BindPort,
                        tunnel.TargetHost, (uint)tunnel.TargetPort)
                };
                _client.AddForwardedPort(port);
                port.Start();
                _forwards.Add(port);
                Output?.Invoke("\r\n\x1b[32m[隧道] " + tunnel.Name + " 已启动："
                    + tunnel.BindHost + ":" + tunnel.BindPort + "\x1b[0m\r\n");
            }
            catch (Exception ex)
            {
                Output?.Invoke("\r\n\x1b[33m[隧道] " + tunnel.Name + " 启动失败：" + ex.Message
                    + "\x1b[0m\r\n");
            }
        }
    }

    public Task WriteAsync(string text, CancellationToken cancellationToken)
    {
        var shell = _shell;
        if (shell == null || !_connected)
        {
            return Task.CompletedTask;
        }
        return Task.Run(() =>
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(text);
                lock (shell)
                {
                    shell.Write(data, 0, data.Length);
                    shell.Flush();
                }
            }
            catch (Exception ex)
            {
                Closed?.Invoke("SSH 发送失败：" + ex.Message);
            }
        }, cancellationToken);
    }

    public void Resize(int columns, int rows)
    {
        var shell = _shell;
        if (shell == null)
        {
            return;
        }
        try
        {
            var field = typeof(ShellStream).GetField("_channel",
                BindingFlags.Instance | BindingFlags.NonPublic);
            object? channel = field?.GetValue(shell);
            var method = channel?.GetType().GetMethod("SendWindowChangeRequest",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            method?.Invoke(channel, new object[]
            {
                (uint)Math.Clamp(columns, 20, 500),
                (uint)Math.Clamp(rows, 5, 300),
                0u, 0u
            });
        }
        catch
        {
            // 某些 SSH.NET 版本没有公开窗口调整 API，忽略即可。
        }
    }

    /// <summary>
    /// 通过同一 SSH 连接执行只读探测脚本，读取当前交互式 shell（或前台程序）
    /// 的工作目录；不向终端写入任何内容。仅支持 Unix 服务器。
    /// </summary>
    public async Task<string?> GetShellDirectoryAsync(CancellationToken cancellationToken)
    {
        var client = _client;
        if (client == null || !_connected)
        {
            return null;
        }
        try
        {
            using var command = client.CreateCommand(
                "me=$(id -u 2>/dev/null) || exit 0; " +
                "for p in $(ps -u \"$me\" -o pid=,tty= 2>/dev/null | " +
                "awk '$2 != \"?\" {print $1}' | sort -nr); do " +
                "c=$(readlink \"/proc/$p/cwd\" 2>/dev/null) && [ -n \"$c\" ] " +
                "&& { printf '%s\\n' \"$c\"; exit 0; }; done");
            command.CommandTimeout = TimeSpan.FromSeconds(4);
            string output = await Task.Run(() => command.Execute(), cancellationToken)
                .ConfigureAwait(false);
            string path = output.Split('\n').FirstOrDefault()?.Trim() ?? "";
            return path.StartsWith("/", StringComparison.Ordinal) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<SftpClient?> CreateSftpClientAsync(CancellationToken cancellationToken)
    {
        string host = _profile.Host;
        int port = _profile.Port;
        if (_profile.UseJumpHost && _jumpForward != null)
        {
            host = "127.0.0.1";
            port = (int)_jumpForward.BoundPort;
        }
        var info = BuildConnectionInfo(host, port, _profile.UserName,
            _profile.Password, _profile.PrivateKeyPath, _profile.PrivateKeyPassphrase,
            _profile.AuthMethod);
        var client = new SftpClient(info);
        AttachHostKeyVerification(client, _profile.Host, _profile.Port);
        await Task.Run(() =>
        {
            client.Connect();
        }, cancellationToken).ConfigureAwait(false);
        return client;
    }

    public void Disconnect(string reason = "连接已断开。")
    {
        DisconnectCore();
        Closed?.Invoke(reason);
    }

    private void DisconnectCore()
    {
        _connected = false;
        try { _pumpCts?.Cancel(); } catch { }
        foreach (var port in _forwards)
        {
            try { port.Stop(); } catch { }
            try { port.Dispose(); } catch { }
        }
        _forwards.Clear();
        try { _shell?.Dispose(); } catch { }
        _shell = null;
        try { _client?.Disconnect(); } catch { }
        try { _client?.Dispose(); } catch { }
        _client = null;
        try { _jumpForward?.Stop(); } catch { }
        try { _jumpForward?.Dispose(); } catch { }
        _jumpForward = null;
        try { _jumpClient?.Disconnect(); } catch { }
        try { _jumpClient?.Dispose(); } catch { }
        _jumpClient = null;
    }

    public async ValueTask DisposeAsync()
    {
        DisconnectCore();
        try { _pumpCts?.Dispose(); } catch { }
        foreach (var disposable in _disposables)
        {
            try { disposable.Dispose(); } catch { }
        }
        _disposables.Clear();
        await Task.CompletedTask;
    }
}

internal sealed class TelnetXShellTransport : IXShellTransport
{
    private const byte Iac = 255;
    private const byte Dont = 254;
    private const byte Do = 253;
    private const byte Wont = 252;
    private const byte Will = 251;
    private const byte Sb = 250;
    private const byte Se = 240;
    private const byte OptEcho = 1;
    private const byte OptSga = 3;
    private const byte OptTtype = 24;

    private readonly XSessionProfile _profile;
    private readonly Encoding _encoding;
    private readonly object _writeLock = new();
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private Decoder _decoder;
    private bool _connected;
    private TelnetState _state;
    private byte _pendingVerb;
    private byte _subOption;
    private readonly List<byte> _subData = new();

    public TelnetXShellTransport(XSessionProfile profile)
    {
        _profile = profile;
        _encoding = XShellEncoding.Get(profile.TelnetEncoding);
        _decoder = _encoding.GetDecoder();
    }

    public bool IsConnected => _connected && _stream != null;
    public string Kind => "Telnet";
    public event Action<string>? Output;
    public event Action<string>? Closed;

    public async Task ConnectAsync(int columns, int rows, CancellationToken cancellationToken)
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(_profile.Host, _profile.Port, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            client.Dispose();
            throw;
        }
        client.NoDelay = true;
        _client = client;
        _stream = client.GetStream();
        _connected = true;
        _state = TelnetState.Normal;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ReceiveLoopAsync(_stream, _cts.Token));
    }

    private async Task ReceiveLoopAsync(NetworkStream stream, CancellationToken token)
    {
        var buffer = new byte[8192];
        try
        {
            while (!token.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), token)
                    .ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }
                ProcessIncoming(buffer, read);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                Closed?.Invoke("Telnet 连接中断：" + ex.Message);
            }
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                _connected = false;
                Closed?.Invoke("Telnet 连接已关闭。");
            }
        }
    }

    private void ProcessIncoming(byte[] buffer, int count)
    {
        var visible = new List<byte>(count);
        for (int i = 0; i < count; i++)
        {
            byte value = buffer[i];
            switch (_state)
            {
                case TelnetState.Normal:
                    if (value == Iac)
                    {
                        _state = TelnetState.Iac;
                    }
                    else
                    {
                        visible.Add(value);
                    }
                    break;
                case TelnetState.Iac:
                    if (value == Iac)
                    {
                        visible.Add(Iac);
                        _state = TelnetState.Normal;
                    }
                    else if (value is Will or Wont or Do or Dont)
                    {
                        _pendingVerb = value;
                        _state = TelnetState.Option;
                    }
                    else if (value == Sb)
                    {
                        _subOption = 0;
                        _subData.Clear();
                        _state = TelnetState.Subnegotiation;
                    }
                    else
                    {
                        _state = TelnetState.Normal;
                    }
                    break;
                case TelnetState.Option:
                    HandleOption(_pendingVerb, value);
                    _state = TelnetState.Normal;
                    break;
                case TelnetState.Subnegotiation:
                    if (value == Iac)
                    {
                        _state = TelnetState.SubnegotiationIac;
                    }
                    else if (_subOption == 0)
                    {
                        _subOption = value;
                    }
                    else
                    {
                        _subData.Add(value);
                    }
                    break;
                case TelnetState.SubnegotiationIac:
                    if (value == Se)
                    {
                        HandleSubnegotiation();
                        _state = TelnetState.Normal;
                    }
                    else
                    {
                        _subData.Add(value);
                        _state = TelnetState.Subnegotiation;
                    }
                    break;
            }
        }
        if (visible.Count > 0)
        {
            Emit(visible);
        }
    }

    private void Emit(List<byte> data)
    {
        byte[] bytes = data.ToArray();
        char[] chars = new char[_decoder.GetCharCount(bytes, 0, bytes.Length)];
        int count = _decoder.GetChars(bytes, 0, bytes.Length, chars, 0);
        if (count > 0)
        {
            Output?.Invoke(new string(chars, 0, count));
        }
    }

    private void HandleOption(byte verb, byte option)
    {
        if (verb == Will)
        {
            SendTelnetCommand(option is OptEcho or OptSga ? Do : Dont, option);
        }
        else if (verb == Wont)
        {
            SendTelnetCommand(Dont, option);
        }
        else if (verb == Do)
        {
            SendTelnetCommand(option is OptSga or OptTtype ? Will : Wont, option);
        }
        else if (verb == Dont)
        {
            SendTelnetCommand(Wont, option);
        }
    }

    private void HandleSubnegotiation()
    {
        if (_subOption == OptTtype && _subData.Count > 0 && _subData[0] == 1)
        {
            const string terminal = "xterm-256color";
            var payload = new List<byte> { Iac, Sb, OptTtype, 0 };
            payload.AddRange(Encoding.ASCII.GetBytes(terminal));
            payload.Add(Iac);
            payload.Add(Se);
            WriteRaw(payload.ToArray());
        }
    }

    private void SendTelnetCommand(byte verb, byte option)
    {
        WriteRaw(new[] { Iac, verb, option });
    }

    private void WriteRaw(byte[] payload)
    {
        lock (_writeLock)
        {
            try
            {
                _stream?.Write(payload, 0, payload.Length);
            }
            catch
            {
                // 连接已断开时由接收循环触发 Closed。
            }
        }
    }

    public Task WriteAsync(string text, CancellationToken cancellationToken)
    {
        if (text == "\r")
        {
            text = "\r\n";
        }
        byte[] data = _encoding.GetBytes(text);
        var escaped = new List<byte>(data.Length + 8);
        foreach (byte b in data)
        {
            escaped.Add(b);
            if (b == Iac)
            {
                escaped.Add(Iac);
            }
        }
        WriteRaw(escaped.ToArray());
        return Task.CompletedTask;
    }

    public void Resize(int columns, int rows)
    {
        // Telnet NAWS 可选；大多数交互式设备不要求。
    }

    public Task<SftpClient?> CreateSftpClientAsync(CancellationToken cancellationToken) =>
        Task.FromResult<SftpClient?>(null);

    public void Disconnect(string reason = "连接已断开。")
    {
        _connected = false;
        try { _cts?.Cancel(); } catch { }
        try { _stream?.Close(); } catch { }
        try { _client?.Close(); } catch { }
        _stream = null;
        _client = null;
        Closed?.Invoke(reason);
    }

    public ValueTask DisposeAsync()
    {
        Disconnect("Telnet 窗口已关闭。");
        try { _cts?.Dispose(); } catch { }
        return ValueTask.CompletedTask;
    }

    private enum TelnetState
    {
        Normal,
        Iac,
        Option,
        Subnegotiation,
        SubnegotiationIac
    }
}

internal sealed class SerialXShellTransport : IXShellTransport
{
    private readonly XSessionProfile _profile;
    private readonly Encoding _encoding;
    private SerialPort? _port;
    private bool _connected;

    public SerialXShellTransport(XSessionProfile profile)
    {
        _profile = profile;
        _encoding = XShellEncoding.Get(profile.EncodingName);
    }

    public bool IsConnected => _connected && _port is { IsOpen: true };
    public string Kind => "Serial";
    public event Action<string>? Output;
    public event Action<string>? Closed;

    public Task ConnectAsync(int columns, int rows, CancellationToken cancellationToken)
    {
        var port = new SerialPort(_profile.SerialPortName, Math.Max(300, _profile.BaudRate),
            _profile.Parity, Math.Max(5, _profile.DataBits), _profile.StopBits)
        {
            Handshake = _profile.Handshake,
            DtrEnable = _profile.DtrEnable,
            RtsEnable = _profile.RtsEnable,
            Encoding = _encoding,
            ReadTimeout = 1000,
            WriteTimeout = 1000
        };
        port.DataReceived += OnDataReceived;
        port.Open();
        _port = port;
        _connected = true;
        return Task.CompletedTask;
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var port = _port;
            if (port == null || !port.IsOpen)
            {
                return;
            }
            int count = port.BytesToRead;
            if (count <= 0)
            {
                return;
            }
            byte[] buffer = new byte[count];
            int read = port.Read(buffer, 0, buffer.Length);
            if (read > 0)
            {
                Output?.Invoke(_encoding.GetString(buffer, 0, read));
            }
        }
        catch (Exception ex)
        {
            Closed?.Invoke("串口读取失败：" + ex.Message);
        }
    }

    public Task WriteAsync(string text, CancellationToken cancellationToken)
    {
        var port = _port;
        if (port is { IsOpen: true })
        {
            byte[] data = _encoding.GetBytes(text);
            port.Write(data, 0, data.Length);
        }
        return Task.CompletedTask;
    }

    public void Resize(int columns, int rows)
    {
    }

    public Task<SftpClient?> CreateSftpClientAsync(CancellationToken cancellationToken) =>
        Task.FromResult<SftpClient?>(null);

    public void Disconnect(string reason = "连接已断开。")
    {
        _connected = false;
        try
        {
            if (_port != null)
            {
                _port.DataReceived -= OnDataReceived;
                if (_port.IsOpen)
                {
                    _port.Close();
                }
                _port.Dispose();
            }
        }
        catch
        {
            // 忽略关闭异常
        }
        _port = null;
        Closed?.Invoke(reason);
    }

    public ValueTask DisposeAsync()
    {
        Disconnect("串口已关闭。");
        return ValueTask.CompletedTask;
    }
}

internal sealed class LocalXShellTransport : IXShellTransport
{
    private readonly XSessionProfile _profile;
    private Process? _process;
    private bool _connected;

    public LocalXShellTransport(XSessionProfile profile)
    {
        _profile = profile;
    }

    public bool IsConnected => _connected && _process is { HasExited: false };
    public string Kind => "Local";
    public event Action<string>? Output;
    public event Action<string>? Closed;

    public Task ConnectAsync(int columns, int rows, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(_profile.LocalCommand)
                ? "powershell.exe" : _profile.LocalCommand,
            Arguments = _profile.LocalArguments ?? "",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        info.Environment["TERM"] = "xterm-256color";
        info.Environment["COLORTERM"] = "truecolor";
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                Output?.Invoke(e.Data + "\r\n");
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                Output?.Invoke(e.Data + "\r\n");
            }
        };
        process.Exited += (_, _) =>
        {
            if (_connected)
            {
                _connected = false;
                Closed?.Invoke("本地 Shell 已退出。");
            }
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process = process;
        _connected = true;
        return Task.CompletedTask;
    }

    public Task WriteAsync(string text, CancellationToken cancellationToken)
    {
        var process = _process;
        if (process is { HasExited: false })
        {
            process.StandardInput.Write(text);
            process.StandardInput.Flush();
        }
        return Task.CompletedTask;
    }

    public void Resize(int columns, int rows)
    {
    }

    public Task<SftpClient?> CreateSftpClientAsync(CancellationToken cancellationToken) =>
        Task.FromResult<SftpClient?>(null);

    public void Disconnect(string reason = "连接已断开。")
    {
        _connected = false;
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // 忽略终止异常
        }
        try { _process?.Dispose(); } catch { }
        _process = null;
        Closed?.Invoke(reason);
    }

    public ValueTask DisposeAsync()
    {
        Disconnect("本地 Shell 已关闭。");
        return ValueTask.CompletedTask;
    }
}

internal static class XShellEncoding
{
    public static Encoding Get(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Encoding.UTF8;
        }
        try
        {
            return name.ToUpperInvariant() switch
            {
                "UTF-8" or "UTF8" => new UTF8Encoding(false),
                "GB18030" or "GBK" or "GB2312" => Encoding.GetEncoding("GB18030"),
                "BIG5" => Encoding.GetEncoding("big5"),
                "SHIFT-JIS" or "SJIS" => Encoding.GetEncoding("shift_jis"),
                _ => Encoding.GetEncoding(name)
            };
        }
        catch
        {
            return Encoding.UTF8;
        }
    }
}
