using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace MiniMonitor;

public sealed class TcpingAttempt
{
    public int Index { get; init; }
    public string Host { get; init; } = "";
    public int Port { get; init; }
    public string Address { get; init; } = "";
    public bool Success { get; init; }
    public double ElapsedMs { get; init; }
    public bool Refused { get; init; }
    public string? Error { get; init; }

    public string TargetText => TcpingEngine.FormatTarget(Address, Port);
}

public sealed class TcpingSummary
{
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string Address { get; set; } = "";
    public int Sent { get; set; }
    public int Success { get; set; }
    public int Failed { get; set; }
    public double MinMs { get; set; }
    public double MaxMs { get; set; }
    public double TotalMs { get; set; }
    public bool Cancelled { get; set; }

    public double AverageMs => Success > 0 ? TotalMs / Success : 0;
    public double LossPercent => Sent > 0 ? Failed * 100.0 / Sent : 0;
}

/// <summary>
/// 内置 TCPing：通过 TCP 连接耗时判断目标端口是否开放，不依赖 tcping.exe。
/// </summary>
public static class TcpingEngine
{
    public static async Task<TcpingSummary> RunAsync(
        string host,
        int port,
        int count,
        int intervalMs,
        bool continuous,
        int connectTimeoutMs,
        Action<TcpingAttempt>? onAttempt,
        Action<string>? onInfo,
        CancellationToken cancellationToken)
    {
        if (port < 1 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "端口必须在 1-65535 之间。");
        }
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "次数必须大于 0。");
        }

        IPAddress[] addresses;
        try
        {
            using var dnsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, dnsTimeout.Token);
            addresses = await Dns.GetHostAddressesAsync(host, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new IOException($"无法解析主机名 {host}：{ex.Message}", ex);
        }

        if (addresses.Length == 0)
        {
            throw new IOException($"无法解析主机名 {host}：未返回任何 IP 地址。");
        }

        IPAddress address;
        if (IPAddress.TryParse(host, out var literal))
        {
            address = literal;
        }
        else
        {
            address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                ?? addresses[0];
        }

        string target = FormatTarget(address.ToString(), port);
        onInfo?.Invoke($"已解析 {host} -> {target}");
        onInfo?.Invoke($"TCPing {target}，{(continuous ? "持续探测直到停止" : $"次数 {count}")}，间隔 {intervalMs} ms");

        var summary = new TcpingSummary
        {
            Host = host,
            Port = port,
            Address = address.ToString()
        };
        int sent = 0;
        bool cancelled = false;

        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }
            if (!continuous && sent >= count)
            {
                break;
            }

            sent++;
            var attempt = await ProbeAsync(address, host, port, sent, connectTimeoutMs, cancellationToken)
                .ConfigureAwait(false);
            if (attempt == null)
            {
                sent--;
                cancelled = true;
                break;
            }

            summary.Sent = sent;
            if (attempt.Success)
            {
                summary.Success++;
                summary.TotalMs += attempt.ElapsedMs;
                if (summary.Success == 1 || attempt.ElapsedMs < summary.MinMs)
                {
                    summary.MinMs = attempt.ElapsedMs;
                }
                if (attempt.ElapsedMs > summary.MaxMs)
                {
                    summary.MaxMs = attempt.ElapsedMs;
                }
            }
            else
            {
                summary.Failed++;
            }

            onAttempt?.Invoke(attempt);

            if (!continuous && sent >= count)
            {
                break;
            }

            try
            {
                await Task.Delay(intervalMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                break;
            }
        }

        summary.Cancelled = cancelled;
        return summary;
    }

    public static string FormatTarget(string address, int port)
    {
        bool ipv6 = address.Contains(':', StringComparison.Ordinal);
        return ipv6 ? $"[{address}]:{port}" : $"{address}:{port}";
    }

    private static async Task<TcpingAttempt?> ProbeAsync(
        IPAddress address,
        string host,
        int port,
        int index,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        socket.NoDelay = true;
        using var timeout = new CancellationTokenSource(timeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), linked.Token).ConfigureAwait(false);
            stopwatch.Stop();
            return new TcpingAttempt
            {
                Index = index,
                Host = host,
                Port = port,
                Address = address.ToString(),
                Success = true,
                ElapsedMs = stopwatch.Elapsed.TotalMilliseconds
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return Failed(index, host, port, address, "连接超时（可能被防火墙过滤）", refused: false);
        }
        catch (SocketException ex)
        {
            bool refused = ex.SocketErrorCode == SocketError.ConnectionRefused;
            string error = ex.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => "连接被拒绝（端口未开放）",
                SocketError.HostUnreachable => "目标主机不可达",
                SocketError.NetworkUnreachable => "网络不可达",
                SocketError.TimedOut => "连接超时（可能被防火墙过滤）",
                SocketError.HostNotFound => "无法解析主机名",
                _ => $"连接失败（{ex.SocketErrorCode}）"
            };
            return Failed(index, host, port, address, error, refused);
        }
        catch (Exception ex)
        {
            return Failed(index, host, port, address, "连接失败：" + ex.Message, refused: false);
        }
    }

    private static TcpingAttempt Failed(
        int index, string host, int port, IPAddress address, string error, bool refused)
    {
        return new TcpingAttempt
        {
            Index = index,
            Host = host,
            Port = port,
            Address = address.ToString(),
            Success = false,
            Error = error,
            Refused = refused
        };
    }
}
