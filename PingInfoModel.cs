using System.Net;
using System.Net.NetworkInformation;

namespace MiniMonitor;

/// <summary>PingInfoView 中的一条主机定义。</summary>
public sealed class PingHostDefinition
{
    public int Order { get; set; }
    public string Host { get; set; } = "";
    public int? Port { get; set; }
    public string Description { get; set; } = "";
    public string Group { get; set; } = "";

    public bool IsTcp => Port.HasValue;

    /// <summary>用于跨主机列表编辑时合并统计（主机 + 端口，不包含描述/分组）。</summary>
    public string Key => Host.ToLowerInvariant() + "|" + (Port?.ToString() ?? "");

    public string DisplayHost => Port is int port ? FormatHostPort(Host, port) : Host;

    public static string FormatHostPort(string host, int port)
    {
        return host.IndexOf(':') >= 0 ? $"[{host}]:{port}" : $"{host}:{port}";
    }
}

/// <summary>
/// 主机列表解析：
/// 每行一个主机；支持注释、描述、分组、IPv4 范围/CIDR、IPv4/IPv6、host:port（TCP Ping）。
/// </summary>
public static class PingHostParser
{
    public const int MaxHosts = 10000;

    public static List<PingHostDefinition> Parse(
        string? text, bool skipCidrFirst, bool skipCidrLast, int maxHosts = MaxHosts)
    {
        var result = new List<PingHostDefinition>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        string group = "";
        int order = 0;
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }
            if (line.StartsWith("Group:", StringComparison.OrdinalIgnoreCase))
            {
                group = line[6..].Trim();
                continue;
            }

            int separator = FindWhitespace(line);
            string targetPart = separator < 0 ? line : line[..separator];
            string description = separator < 0 ? "" : line[(separator + 1)..].Trim();

            if (TryExpandCidr(targetPart, description, group, skipCidrFirst, skipCidrLast,
                    result, ref order, maxHosts, i + 1))
            {
                continue;
            }
            if (TryExpandRange(targetPart, description, group, result, ref order, maxHosts, i + 1))
            {
                continue;
            }

            if (!TryParseHostPort(targetPart, out string host, out int? port, out string error))
            {
                throw new ArgumentException($"第 {i + 1} 行：{error}");
            }
            AddHost(result, host, port, description, group, ref order, maxHosts, i + 1);
        }

        return result;
    }

    private static void AddHost(
        List<PingHostDefinition> result,
        string host,
        int? port,
        string description,
        string group,
        ref int order,
        int maxHosts,
        int lineNumber)
    {
        if (result.Count >= maxHosts)
        {
            throw new ArgumentException($"主机数量超过上限 {maxHosts} 个，请缩小范围。");
        }
        result.Add(new PingHostDefinition
        {
            Order = order++,
            Host = host,
            Port = port,
            Description = description,
            Group = group
        });
    }

    private static bool TryParseHostPort(
        string target, out string host, out int? port, out string error)
    {
        host = "";
        port = null;
        error = "";
        target = target.Trim();
        if (target.Length == 0)
        {
            error = "主机为空。";
            return false;
        }

        if (target.StartsWith("[", StringComparison.Ordinal))
        {
            int close = target.IndexOf(']');
            if (close < 2)
            {
                error = "IPv6 地址缺少右方括号。";
                return false;
            }
            host = target[1..close];
            string rest = target[(close + 1)..];
            if (rest.Length > 0)
            {
                if (!rest.StartsWith(":", StringComparison.Ordinal)
                    || !int.TryParse(rest[1..], out int p)
                    || p < 1 || p > 65535)
                {
                    error = "端口必须是 1-65535 的整数。";
                    return false;
                }
                port = p;
            }
            return true;
        }

        int firstColon = target.IndexOf(':');
        int lastColon = target.LastIndexOf(':');
        if (firstColon >= 0 && firstColon == lastColon)
        {
            string portText = target[(lastColon + 1)..];
            if (!int.TryParse(portText, out int p) || p < 1 || p > 65535)
            {
                error = "端口必须是 1-65535 的整数。";
                return false;
            }
            host = target[..lastColon].Trim();
            if (host.Length == 0)
            {
                error = "主机为空。";
                return false;
            }
            port = p;
            return true;
        }

        // 多个冒号且没有方括号：按 IPv6 地址处理（不带端口）。
        host = target;
        return true;
    }

    private static bool TryExpandCidr(
        string target,
        string description,
        string group,
        bool skipFirst,
        bool skipLast,
        List<PingHostDefinition> result,
        ref int order,
        int maxHosts,
        int lineNumber)
    {
        if (!target.Contains('/', StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = target.Split('/', 2);
        if (!IPAddress.TryParse(parts[0], out IPAddress? address)
            || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || !int.TryParse(parts[1], out int prefix)
            || prefix < 0 || prefix > 32)
        {
            throw new ArgumentException($"第 {lineNumber} 行：无法识别 IPv4 网段 {target}。");
        }

        uint network = ToUInt32(address);
        uint mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        uint first = network & mask;
        uint last = first | ~mask;
        long total = (long)last - first + 1;
        if (total > maxHosts)
        {
            throw new ArgumentException(
                $"第 {lineNumber} 行：网段 {target} 包含 {total} 个地址，超过上限 {maxHosts} 个。");
        }

        long begin = first;
        long end = last;
        if (total >= 3)
        {
            if (skipFirst) begin++;
            if (skipLast) end--;
        }
        if (end < begin)
        {
            throw new ArgumentException($"第 {lineNumber} 行：跳过首尾地址后网段为空。");
        }
        long count = end - begin + 1;
        if (result.Count + count > maxHosts)
        {
            throw new ArgumentException($"主机数量超过上限 {maxHosts} 个，请缩小范围。");
        }

        for (long value = begin; value <= end; value++)
        {
            AddHost(result, FromUInt32((uint)value).ToString(), null, description, group,
                ref order, maxHosts, lineNumber);
        }
        return true;
    }

    private static bool TryExpandRange(
        string target,
        string description,
        string group,
        List<PingHostDefinition> result,
        ref int order,
        int maxHosts,
        int lineNumber)
    {
        int dash = target.IndexOf('-');
        if (dash <= 0)
        {
            return false;
        }
        string left = target[..dash];
        string right = target[(dash + 1)..];
        if (!IPAddress.TryParse(left, out IPAddress? startAddress)
            || startAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        IPAddress? endAddress;
        if (right.Contains('.', StringComparison.Ordinal))
        {
            if (!IPAddress.TryParse(right, out endAddress)
                || endAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                throw new ArgumentException($"第 {lineNumber} 行：无法识别的地址范围 {target}。");
            }
        }
        else
        {
            if (!int.TryParse(right, out int lastOctet) || lastOctet < 0 || lastOctet > 255)
            {
                throw new ArgumentException($"第 {lineNumber} 行：无法识别的地址范围 {target}。");
            }
            byte[] bytes = startAddress.GetAddressBytes();
            bytes[3] = (byte)lastOctet;
            endAddress = new IPAddress(bytes);
        }

        uint start = ToUInt32(startAddress);
        uint end = ToUInt32(endAddress);
        if (start > end)
        {
            (start, end) = (end, start);
        }
        long count = (long)end - start + 1;
        if (count > maxHosts || result.Count + count > maxHosts)
        {
            throw new ArgumentException(
                $"第 {lineNumber} 行：地址范围 {target} 超过上限 {maxHosts} 个。");
        }

        for (uint value = start; value <= end; value++)
        {
            AddHost(result, FromUInt32(value).ToString(), null, description, group,
                ref order, maxHosts, lineNumber);
            if (value == uint.MaxValue)
            {
                break;
            }
        }
        return true;
    }

    private static int FindWhitespace(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }
        return -1;
    }

    private static uint ToUInt32(IPAddress address)
    {
        byte[] b = address.GetAddressBytes();
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }

    private static IPAddress FromUInt32(uint value)
    {
        return new IPAddress(new[]
        {
            (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value
        });
    }
}

/// <summary>单台主机的 Ping 统计。</summary>
public sealed class PingHostState
{
    public readonly object Sync = new();
    public PingHostDefinition Definition { get; private set; }

    // 仅 UI 调度线程读写
    public bool Enabled = true;
    public volatile bool InFlight;
    public long NextDue;
    public bool MacInFlight;
    public DateTime LastMacAttemptUtc = DateTime.MinValue;
    public Ping? PingClient;
    public int PingClientUses;

    // 以下字段在 Sync 锁内读写
    public string Address = "-";
    public IPAddress? CachedAddress;
    public string ReplyAddress = "-";
    public string MacAddress = "-";
    public long SuccessCount;
    public long FailureCount;
    public long TotalCount;
    public int ConsecutiveFailures;
    public int MaxConsecutiveFailures;
    public DateTime? MaxConsecutiveFailuresTime;
    public string LastStatus = "尚未 Ping";
    public string LastError = "";
    public double? LastRttMs;
    public double SumRttMs;
    public double? MinRttMs;
    public double? MaxRttMs;
    public int? LastTtl;
    public DateTime? LastSuccessTime;
    public DateTime? LastFailureTime;
    public bool? PreviousSuccess;

    public PingHostState(PingHostDefinition definition)
    {
        Definition = definition;
    }

    public void SetDefinition(PingHostDefinition definition)
    {
        lock (Sync)
        {
            Definition = definition;
        }
    }

    public void SetAddress(IPAddress address)
    {
        lock (Sync)
        {
            CachedAddress = address;
            Address = address.ToString();
        }
    }

    public void SetMac(string mac)
    {
        lock (Sync)
        {
            MacAddress = mac;
        }
    }

    public void UpdateSuccess(
        IPAddress replyAddress, double rttMs, int? ttl, DateTime at)
    {
        lock (Sync)
        {
            SuccessCount++;
            TotalCount++;
            ConsecutiveFailures = 0;
            LastStatus = "成功";
            LastError = "";
            LastRttMs = rttMs;
            SumRttMs += rttMs;
            if (MinRttMs == null || rttMs < MinRttMs) MinRttMs = rttMs;
            if (MaxRttMs == null || rttMs > MaxRttMs) MaxRttMs = rttMs;
            LastTtl = ttl;
            LastSuccessTime = at;
            ReplyAddress = replyAddress.ToString();
            PreviousSuccess = true;
        }
    }

    public void UpdateFailure(string status, string error, DateTime at)
    {
        lock (Sync)
        {
            FailureCount++;
            TotalCount++;
            ConsecutiveFailures++;
            if (ConsecutiveFailures > MaxConsecutiveFailures)
            {
                MaxConsecutiveFailures = ConsecutiveFailures;
                MaxConsecutiveFailuresTime = at;
            }
            LastStatus = "失败：" + status;
            LastError = error;
            LastRttMs = null;
            LastTtl = null;
            LastFailureTime = at;
            ReplyAddress = "-";
            PreviousSuccess = false;
        }
    }

    public void ResetStatistics()
    {
        lock (Sync)
        {
            SuccessCount = 0;
            FailureCount = 0;
            TotalCount = 0;
            ConsecutiveFailures = 0;
            MaxConsecutiveFailures = 0;
            MaxConsecutiveFailuresTime = null;
            LastStatus = "尚未 Ping";
            LastError = "";
            LastRttMs = null;
            SumRttMs = 0;
            MinRttMs = null;
            MaxRttMs = null;
            LastTtl = null;
            LastSuccessTime = null;
            LastFailureTime = null;
            PreviousSuccess = null;
            ReplyAddress = "-";
        }
    }

    public PingHostSnapshot Snapshot()
    {
        lock (Sync)
        {
            double percentFailed = TotalCount > 0 ? FailureCount * 100.0 / TotalCount : 0;
            double? average = SuccessCount > 0 ? SumRttMs / SuccessCount : null;
            return new PingHostSnapshot
            {
                HostName = Definition.Host,
                DisplayHost = Definition.DisplayHost,
                Address = Address,
                ReplyAddress = ReplyAddress,
                Group = Definition.Group,
                Description = Definition.Description,
                Succeed = SuccessCount,
                Failed = FailureCount,
                Total = TotalCount,
                PercentFailed = percentFailed,
                ConsecutiveFailed = ConsecutiveFailures,
                MaxConsecutiveFailed = MaxConsecutiveFailures,
                LastStatus = LastStatus,
                LastError = LastError,
                LastRttMs = LastRttMs,
                AverageMs = average,
                MinMs = MinRttMs,
                MaxMs = MaxRttMs,
                Ttl = LastTtl,
                LastSucceedOn = LastSuccessTime,
                LastFailedOn = LastFailureTime,
                MacAddress = MacAddress,
                Order = Definition.Order,
                Disabled = !Enabled,
                IsTcp = Definition.IsTcp,
                Port = Definition.Port
            };
        }
    }
}

public sealed class PingHostSnapshot
{
    public string HostName { get; init; } = "";
    public string DisplayHost { get; init; } = "";
    public string Address { get; init; } = "-";
    public string ReplyAddress { get; init; } = "-";
    public string Group { get; init; } = "";
    public string Description { get; init; } = "";
    public long Succeed { get; init; }
    public long Failed { get; init; }
    public long Total { get; init; }
    public double PercentFailed { get; init; }
    public int ConsecutiveFailed { get; init; }
    public int MaxConsecutiveFailed { get; init; }
    public string LastStatus { get; init; } = "";
    public string LastError { get; init; } = "";
    public double? LastRttMs { get; init; }
    public double? AverageMs { get; init; }
    public double? MinMs { get; init; }
    public double? MaxMs { get; init; }
    public int? Ttl { get; init; }
    public DateTime? LastSucceedOn { get; init; }
    public DateTime? LastFailedOn { get; init; }
    public string MacAddress { get; init; } = "-";
    public int Order { get; init; }
    public bool Disabled { get; init; }
    public bool IsTcp { get; init; }
    public int? Port { get; init; }
}

public sealed class PingLogEntry
{
    public long Sequence { get; init; }
    public DateTime Time { get; init; }
    public string HostKey { get; init; } = "";
    public string HostText { get; init; } = "";
    public bool Success { get; init; }
    public string Address { get; init; } = "-";
    public string Status { get; init; } = "";
    public double? Ms { get; init; }
    public int? Ttl { get; init; }
    public string Error { get; init; } = "";
}

/// <summary>有界 Ping 日志，常时间运行时限制内存占用。</summary>
public sealed class PingLogBuffer
{
    private readonly object _sync = new();
    private readonly Queue<PingLogEntry> _entries = new();
    private long _sequence;
    private int _limit = 5000;

    public int Limit
    {
        get
        {
            lock (_sync) return _limit;
        }
        set
        {
            lock (_sync)
            {
                _limit = Math.Max(0, value);
                Trim();
            }
        }
    }

    public void Add(
        string hostKey,
        string hostText,
        bool success,
        string address,
        string status,
        double? ms,
        int? ttl,
        string error)
    {
        lock (_sync)
        {
            if (_limit <= 0)
            {
                return;
            }
            _sequence++;
            _entries.Enqueue(new PingLogEntry
            {
                Sequence = _sequence,
                Time = DateTime.Now,
                HostKey = hostKey,
                HostText = hostText,
                Success = success,
                Address = address,
                Status = status,
                Ms = ms,
                Ttl = ttl,
                Error = error
            });
            Trim();
        }
    }

    public List<PingLogEntry> GetRecent(string hostKey, int maxEntries)
    {
        var result = new List<PingLogEntry>();
        lock (_sync)
        {
            foreach (PingLogEntry entry in _entries)
            {
                if (string.Equals(entry.HostKey, hostKey, StringComparison.Ordinal))
                {
                    result.Add(entry);
                }
            }
        }
        if (result.Count > maxEntries)
        {
            result.RemoveRange(0, result.Count - maxEntries);
        }
        return result;
    }

    public List<PingLogEntry> GetSince(string hostKey, long sequence, int maxEntries)
    {
        var result = new List<PingLogEntry>();
        lock (_sync)
        {
            foreach (PingLogEntry entry in _entries)
            {
                if (entry.Sequence > sequence
                    && string.Equals(entry.HostKey, hostKey, StringComparison.Ordinal))
                {
                    result.Add(entry);
                }
            }
        }
        if (result.Count > maxEntries)
        {
            result.RemoveRange(0, result.Count - maxEntries);
        }
        return result;
    }

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
            // 保留单调递增的序列号，避免清空后“新增日志”无法被下方窗格追加。
        }
    }

    private void Trim()
    {
        while (_entries.Count > _limit)
        {
            _entries.Dequeue();
        }
    }
}
