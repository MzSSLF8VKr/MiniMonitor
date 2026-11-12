using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace MiniMonitor;

/// <summary>
/// 内置 Telnet 客户端：直接使用 TCP + Telnet 协议协商，不依赖操作系统是否安装 telnet 功能。
/// 支持 IAC 选项协商、TTYPE、退格与 ANSI 转义清理。
/// </summary>
public sealed class TelnetSession : IDisposable
{
    private const byte Iac = 255;
    private const byte Dont = 254;
    private const byte Do = 253;
    private const byte Wont = 252;
    private const byte Will = 251;
    private const byte Sb = 250;
    private const byte Se = 240;
    private const byte Ga = 249;

    private const byte OptEcho = 1;
    private const byte OptSga = 3;
    private const byte OptTtype = 24;

    private static readonly Regex AnsiRegex = new(
        @"\x1B\[[0-?]*[ -/]*[@-~]|\x1B\][^\x07]*(?:\x07|\x1B\\)",
        RegexOptions.Compiled);

    private readonly object _writeLock = new();
    private readonly object _decoderLock = new();

    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private bool _connected;
    private int _closedRaised;

    private Encoding _encoding = Encoding.UTF8;
    private System.Text.Decoder _decoder = Encoding.UTF8.GetDecoder();

    private TelnetState _state = TelnetState.Normal;
    private byte _pendingVerb;
    private byte _subOption;
    private readonly List<byte> _subData = new();

    public event Action<string>? TextReceived;
    public event Action<string>? Closed;

    public bool IsConnected => _connected && _stream != null;
    public string Host { get; private set; } = "";
    public int Port { get; private set; }
    public string RemoteAddress { get; private set; } = "";

    public Encoding Encoding
    {
        get => _encoding;
        set
        {
            lock (_decoderLock)
            {
                _encoding = value;
                _decoder = value.GetDecoder();
            }
        }
    }

    /// <summary>建立 Telnet 连接；超时抛出 TimeoutException，连接失败抛出 IOException。</summary>
    public async Task ConnectAsync(string host, int port, int timeoutMs = 10000)
    {
        if (IsConnected)
        {
            throw new InvalidOperationException("当前已有 Telnet 连接。");
        }
        if (port < 1 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "端口必须在 1-65535 之间。");
        }

        var client = new TcpClient();
        try
        {
            using var timeout = new CancellationTokenSource(Math.Max(1000, timeoutMs));
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            client.Dispose();
            throw new TimeoutException($"连接 {host}:{port} 超时（{timeoutMs / 1000.0:F0} 秒）。");
        }
        catch (SocketException ex)
        {
            client.Dispose();
            throw new IOException(FriendlySocketError(ex, host, port), ex);
        }
        catch (Exception)
        {
            client.Dispose();
            throw;
        }

        client.NoDelay = true;
        _client = client;
        _stream = client.GetStream();
        Host = host;
        Port = port;
        RemoteAddress = FormatEndpoint(client.Client.RemoteEndPoint);
        _connected = true;
        _closedRaised = 0;
        _state = TelnetState.Normal;
        _subData.Clear();
        lock (_decoderLock)
        {
            _decoder = _encoding.GetDecoder();
        }

        _cts = new CancellationTokenSource();
        _ = ReceiveLoopAsync(_stream, _cts.Token);
    }

    /// <summary>发送一行文本（自动追加 CRLF）。</summary>
    public void SendLine(string text)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("尚未连接。");
        }

        byte[] payload;
        lock (_decoderLock)
        {
            payload = _encoding.GetBytes(text + "\r\n");
        }

        try
        {
            WriteBytes(payload);
        }
        catch (Exception ex)
        {
            RaiseClosed("发送失败，连接已断开：" + ex.Message);
            throw new IOException("发送失败，连接已断开。", ex);
        }
    }

    /// <summary>断开连接并触发 Closed 事件（重复调用不会重复触发）。</summary>
    public void Disconnect(string reason = "已主动断开连接。")
    {
        if (Interlocked.Exchange(ref _closedRaised, 1) == 0)
        {
            _connected = false;
            Closed?.Invoke(reason);
        }

        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // 忽略取消异常
        }
        CloseTransport();
    }

    private async Task ReceiveLoopAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int read;
                try
                {
                    read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (read <= 0)
                {
                    RaiseClosed("远程主机已关闭连接。");
                    break;
                }

                ProcessIncoming(buffer, read);
            }
        }
        catch (IOException ex)
        {
            RaiseClosed("连接已中断：" + ex.Message);
        }
        catch (SocketException ex)
        {
            RaiseClosed("连接已中断：" + FriendlySocketError(ex, Host, Port));
        }
        catch (ObjectDisposedException)
        {
            RaiseClosed("连接已关闭。");
        }
        catch (Exception ex)
        {
            RaiseClosed("连接发生错误：" + ex.Message);
        }
        finally
        {
            _connected = false;
            CloseTransport();
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
                        // NOP / GA / SE 等单字节命令
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
            EmitVisible(visible);
        }
    }

    private void HandleOption(byte verb, byte option)
    {
        if (verb == Will)
        {
            if (option == OptEcho)
            {
                SendTelnetCommand(Do, option);
            }
            else if (option == OptSga)
            {
                SendTelnetCommand(Do, option);
            }
            else
            {
                SendTelnetCommand(Dont, option);
            }
        }
        else if (verb == Wont)
        {
            SendTelnetCommand(Dont, option);
        }
        else if (verb == Do)
        {
            if (option == OptSga)
            {
                SendTelnetCommand(Will, option);
            }
            else if (option == OptTtype)
            {
                SendTelnetCommand(Will, option);
            }
            else
            {
                SendTelnetCommand(Wont, option);
            }
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
            // IAC SB TTYPE IS "xterm-256color" IAC SE
            const string terminal = "xterm-256color";
            var payload = new List<byte> { Iac, Sb, OptTtype, 0 };
            payload.AddRange(Encoding.ASCII.GetBytes(terminal));
            payload.Add(Iac);
            payload.Add(Se);
            WriteBytes(payload.ToArray());
        }
    }

    private void SendTelnetCommand(byte verb, byte option)
    {
        WriteBytes(new[] { Iac, verb, option });
    }

    private void WriteBytes(byte[] payload)
    {
        lock (_writeLock)
        {
            var stream = _stream;
            if (stream == null)
            {
                return;
            }
            stream.Write(payload, 0, payload.Length);
        }
    }

    private void EmitVisible(List<byte> bytes)
    {
        string text;
        lock (_decoderLock)
        {
            byte[] data = bytes.ToArray();
            char[] chars = new char[_decoder.GetCharCount(data, 0, data.Length)];
            _decoder.GetChars(data, 0, data.Length, chars, 0);
            text = new string(chars);
        }

        if (text.Length == 0)
        {
            return;
        }

        text = text.Replace("\0", "");
        text = AnsiRegex.Replace(text, "");
        if (text.Length == 0)
        {
            return;
        }

        // Telnet 客户端通常接收 CRLF / CR；统一按换行显示（退格由控制台控件处理）。
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        TextReceived?.Invoke(text);
    }

    private void RaiseClosed(string reason)
    {
        if (Interlocked.Exchange(ref _closedRaised, 1) == 0)
        {
            _connected = false;
            Closed?.Invoke(reason);
        }
    }

    private void CloseTransport()
    {
        try
        {
            _stream?.Close();
        }
        catch
        {
            // 忽略关闭异常
        }
        try
        {
            _client?.Close();
        }
        catch
        {
            // 忽略关闭异常
        }
        _stream = null;
        _client = null;
        _connected = false;
    }

    private static string FormatEndpoint(System.Net.EndPoint? endpoint)
    {
        return endpoint is System.Net.IPEndPoint ip ? ip.Address.ToString() : "?";
    }

    private static string FriendlySocketError(SocketException ex, string host, int port)
    {
        return ex.SocketErrorCode switch
        {
            SocketError.ConnectionRefused => $"连接被拒绝：{host}:{port} 未开放或服务未监听。",
            SocketError.HostUnreachable => $"无法到达主机 {host}。",
            SocketError.NetworkUnreachable => "本机网络不可达，请检查网络连接。",
            SocketError.TimedOut => $"连接 {host}:{port} 超时。",
            SocketError.HostNotFound => $"无法解析主机名 {host}。",
            _ => $"{ex.Message}（{ex.SocketErrorCode}）"
        };
    }

    public void Dispose()
    {
        try
        {
            Disconnect("Telnet 窗口已关闭。");
        }
        catch
        {
            // 忽略释放异常
        }
        try
        {
            _cts?.Dispose();
        }
        catch
        {
            // 忽略释放异常
        }
        _cts = null;
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
