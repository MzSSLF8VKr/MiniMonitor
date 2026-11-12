using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace MiniMonitor;

/// <summary>
/// 内置 Nmap 兼容扫描引擎：不依赖外部 nmap.exe / Npcap。
/// - TCP：并发连接探测（-sS / -sT 均以 TCP 连接扫描实现，结果等价于开放端口探测）
/// - UDP：发送协议探测包并等待回复（-sU）
/// 支持常用 nmap 参数：-p/-p-、-T0~T5、--min-rate、-Pn、-F、--top-ports 等。
/// </summary>
public sealed class NmapScanOptions
{
    public string Command { get; set; } = "";
    public List<string> Targets { get; } = new();
    public bool ScanTcp { get; set; }
    public bool ScanUdp { get; set; }
    public List<int> Ports { get; } = new();
    public int TcpTimeoutMs { get; set; } = 2500;
    public int UdpTimeoutMs { get; set; } = 2000;
    public int Concurrency { get; set; } = 256;
    public int MinRate { get; set; }
    public int TimingTemplate { get; set; } = 3;
    public List<string> Warnings { get; } = new();
}

public sealed class NmapPortResult
{
    public string Host { get; init; } = "";
    public int Port { get; init; }
    public string Protocol { get; init; } = "tcp";
    public string State { get; init; } = "open";
    public string Service { get; init; } = "unknown";
    public double? LatencyMs { get; init; }
    public string? Detail { get; init; }

    public string PortText => $"{Port}/{Protocol}";
}

public sealed class NmapScanResult
{
    public string Command { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public bool Cancelled { get; set; }
    public bool TcpEnabled { get; set; }
    public bool UdpEnabled { get; set; }
    public List<string> Hosts { get; } = new();
    public List<NmapPortResult> OpenPorts { get; } = new();
    public List<NmapPortResult> OpenFilteredPorts { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Errors { get; } = new();
    public int TcpPortsScanned { get; set; }
    public int TcpClosed { get; set; }
    public int TcpFiltered { get; set; }
    public int UdpPortsScanned { get; set; }
    public int UdpClosed { get; set; }
    public int UdpOpenFiltered { get; set; }
    public int OpenPortCount { get; set; }

    public string[] BuildReportLines()
    {
        var lines = new List<string>();
        lines.Add("================================================================================");
        lines.Add("MiniMonitor - 内置 Nmap 兼容扫描结果");
        lines.Add("================================================================================");
        lines.Add("命令      : " + (string.IsNullOrWhiteSpace(Command) ? "(未记录)" : Command));
        lines.Add("开始时间  : " + StartedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        lines.Add("结束时间  : " + FinishedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        lines.Add("扫描用时  : " + FormatDuration(Duration));
        lines.Add("扫描类型  : " + BuildScanTypeText());
        lines.Add("主机数量  : " + Hosts.Count);
        lines.Add("扫描状态  : " + (Cancelled ? "已中止（以下为已完成部分）" : "完成"));
        lines.Add("");

        if (Errors.Count > 0)
        {
            lines.Add("[错误]");
            lines.AddRange(Errors);
            lines.Add("");
        }
        if (Warnings.Count > 0)
        {
            lines.Add("[提示]");
            lines.AddRange(Warnings);
            lines.Add("");
        }

        int tcpClosed = TcpClosed;
        int tcpFiltered = TcpFiltered;
        int udpClosed = UdpClosed;
        int udpOpenFiltered = UdpOpenFiltered;

        foreach (string host in Hosts)
        {
            lines.Add("Nmap scan report for " + host);
            lines.Add("Host is up (内置引擎跳过主机发现).");

            var hostOpen = OpenPorts
                .Where(p => string.Equals(p.Host, host, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Protocol)
                .ThenBy(p => p.Port)
                .ToList();
            var hostFiltered = OpenFilteredPorts
                .Where(p => string.Equals(p.Host, host, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Port)
                .ToList();

            if (tcpClosed > 0 || tcpFiltered > 0)
            {
                lines.Add($"Not shown: {tcpClosed} closed tcp port(s), {tcpFiltered} filtered tcp port(s)");
            }
            if (udpClosed > 0 || udpOpenFiltered > 0)
            {
                lines.Add($"Not shown: {udpClosed} closed udp port(s), {udpOpenFiltered} udp port(s) in open|filtered state");
            }

            if (hostOpen.Count == 0 && hostFiltered.Count == 0)
            {
                lines.Add("No open ports found on this host.");
                lines.Add("");
                continue;
            }

            lines.Add("PORT".PadRight(13) + "STATE".PadRight(16) + "SERVICE".PadRight(16) + "LATENCY");
            foreach (var p in hostOpen)
            {
                string latency = p.LatencyMs.HasValue ? $"{p.LatencyMs.Value:F1} ms" : "-";
                lines.Add(p.PortText.PadRight(13) + p.State.PadRight(16) + p.Service.PadRight(16) + latency);
            }
            foreach (var p in hostFiltered)
            {
                lines.Add(p.PortText.PadRight(13) + p.State.PadRight(16) + p.Service.PadRight(16) + "-");
            }
            lines.Add("");
        }

        if (OpenFilteredPorts.Count < UdpOpenFiltered)
        {
            lines.Add($"[说明] 仅列出前 {OpenFilteredPorts.Count} 个 UDP open|filtered 端口，共 {UdpOpenFiltered} 个。");
            lines.Add("");
        }

        lines.Add("================================================================================");
        lines.Add("扫描统计");
        lines.Add("  TCP 端口 : 扫描 " + TcpPortsScanned + "，开放 " + OpenPorts.Count(p => p.Protocol == "tcp")
            + "，关闭 " + TcpClosed + "，过滤 " + TcpFiltered);
        lines.Add("  UDP 端口 : 扫描 " + UdpPortsScanned + "，开放 " + OpenPorts.Count(p => p.Protocol == "udp")
            + "，关闭 " + UdpClosed + "，open|filtered " + UdpOpenFiltered);
        lines.Add("================================================================================");
        lines.Add("说明：本结果由 MiniMonitor 内置扫描引擎生成，未调用外部 Nmap / Npcap；");
        lines.Add("      -sS 在内置引擎中以并发 TCP 连接扫描实现，只判断端口是否开放；");
        lines.Add("      UDP 无回复的端口会被标记为 open|filtered，这是 UDP 扫描的固有限制。");
        lines.Add("================================================================================");
        return lines.ToArray();
    }

    private string BuildScanTypeText()
    {
        if (TcpEnabled && UdpEnabled) return "TCP + UDP 扫描（-sS/-sT + -sU）";
        if (UdpEnabled) return "UDP 探测扫描（-sU）";
        return "TCP 连接扫描（-sS/-sT）";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours} 小时 {duration.Minutes} 分 {duration.Seconds} 秒";
        }
        if (duration.TotalMinutes >= 1)
        {
            return $"{duration.Minutes} 分 {duration.Seconds} 秒";
        }
        return $"{duration.TotalSeconds:F1} 秒";
    }
}

public sealed record NmapProgress(
    string Phase,
    int HostIndex,
    int HostCount,
    string Host,
    int Done,
    int Total,
    int OpenCount,
    NmapPortResult? FoundOpenPort);

/// <summary>nmap 命令行解析器（兼容用户手动输入的常用命令）。</summary>
public static class NmapCommandParser
{
    private const int MaxTargets = 4096;

    private static readonly Dictionary<string, int> PortNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ftp"] = 21, ["ssh"] = 22, ["telnet"] = 23, ["smtp"] = 25, ["domain"] = 53,
        ["dns"] = 53, ["http"] = 80, ["https"] = 443, ["pop3"] = 110, ["imap"] = 143,
        ["ldap"] = 389, ["smb"] = 445, ["microsoft-ds"] = 445, ["smtps"] = 465,
        ["submission"] = 587, ["imaps"] = 993, ["pop3s"] = 995, ["mysql"] = 3306,
        ["mssql"] = 1433, ["oracle"] = 1521, ["postgresql"] = 5432, ["redis"] = 6379,
        ["vnc"] = 5900, ["rdp"] = 3389, ["ms-wbt-server"] = 3389, ["winrm"] = 5985,
        ["wsman"] = 5985, ["wsmans"] = 5986, ["mqtt"] = 1883, ["socks"] = 1080,
        ["snmp"] = 161, ["ntp"] = 123, ["tftp"] = 69, ["syslog"] = 514, ["ssdp"] = 1900,
        ["mdns"] = 5353
    };

    private static readonly int[] CommonTcpPorts =
    {
        7, 9, 13, 21, 22, 23, 25, 26, 37, 53, 79, 80, 81, 88, 106, 110, 111, 113, 119, 135,
        139, 143, 144, 179, 199, 389, 427, 443, 444, 445, 465, 513, 514, 515, 543, 544,
        548, 554, 587, 631, 646, 873, 990, 993, 995, 1025, 1026, 1027, 1028, 1029, 1080,
        1110, 1241, 1352, 1433, 1494, 1521, 1720, 1723, 1755, 1812, 1813, 1883, 1900,
        2000, 2049, 2121, 2222, 2375, 2376, 2425, 2483, 2484, 3000, 3128, 3306, 3389,
        3443, 3690, 4000, 4045, 4369, 4444, 4567, 4899, 5000, 5009, 5060, 5061, 5432,
        5555, 5631, 5666, 5672, 5900, 5984, 5985, 5986, 6000, 6001, 6379, 6667, 7000,
        7070, 8000, 8008, 8080, 8081, 8088, 8090, 8443, 8888, 9000, 9042, 9090, 9100,
        9200, 9300, 9443, 9981, 10000, 11211, 15672, 27017, 27018, 50000
    };

    private static readonly int[] CommonUdpPorts =
    {
        53, 67, 68, 69, 111, 123, 135, 137, 138, 161, 162, 177, 427, 443, 500, 514,
        520, 623, 631, 996, 1025, 1194, 1434, 1701, 1900, 2049, 2126, 2222, 3283,
        3389, 3702, 4500, 5060, 5353, 5355, 49152, 49154
    };

    public static NmapScanOptions Parse(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("请输入 nmap 命令，例如：nmap -sS -p- -T4 --min-rate=5000 -Pn 192.168.1.100");
        }

        string[] tokens = Tokenize(command.Trim());
        if (tokens.Length == 0)
        {
            throw new ArgumentException("命令为空。");
        }

        var options = new NmapScanOptions { Command = command.Trim() };
        int start = 0;
        if (tokens[0].Equals("nmap", StringComparison.OrdinalIgnoreCase)
            || tokens[0].Equals("nmap.exe", StringComparison.OrdinalIgnoreCase))
        {
            start = 1;
        }

        bool sawTcp = false;
        bool sawUdp = false;
        bool portsSpecified = false;
        List<int>? tcpPortList = null;
        List<int>? udpPortList = null;

        for (int i = start; i < tokens.Length; i++)
        {
            string token = tokens[i];
            if (token == "--")
            {
                for (int j = i + 1; j < tokens.Length; j++)
                {
                    AddTargets(options, tokens[j]);
                }
                break;
            }

            if (!token.StartsWith("-", StringComparison.Ordinal))
            {
                AddTargets(options, token);
                continue;
            }

            string lower = token.ToLowerInvariant();
            switch (lower)
            {
                case "-ss":
                case "-st":
                case "--syn":
                    sawTcp = true;
                    options.Warnings.Add("内置引擎不使用原始套接字，-sS 已自动按 TCP 连接扫描执行。");
                    break;
                case "-su":
                    sawUdp = true;
                    break;
                case "-ssu":
                case "-sus":
                    sawTcp = true;
                    sawUdp = true;
                    break;
                case "-sn":
                    throw new ArgumentException("内置引擎不支持 -sn（仅主机发现），请使用端口扫描参数。");
                case "-p-":
                    tcpPortList = BuildFullPorts();
                    udpPortList = BuildFullPorts();
                    portsSpecified = true;
                    break;
                case "-p":
                    if (i + 1 >= tokens.Length)
                    {
                        throw new ArgumentException("-p 后缺少端口列表。");
                    }
                    var parsed = ParsePortList(tokens[++i]);
                    tcpPortList = parsed;
                    udpPortList = parsed;
                    portsSpecified = true;
                    break;
                case "-f":
                    tcpPortList = CommonTcpPorts.Take(100).ToList();
                    udpPortList = CommonUdpPorts.Take(100).ToList();
                    portsSpecified = true;
                    break;
                case "-t0":
                case "-t1":
                case "-t2":
                case "-t3":
                case "-t4":
                case "-t5":
                    options.TimingTemplate = int.Parse(lower[^1..]);
                    break;
                case "-t":
                    if (i + 1 < tokens.Length && int.TryParse(tokens[++i], out int t) && t >= 0 && t <= 5)
                    {
                        options.TimingTemplate = t;
                    }
                    else
                    {
                        options.Warnings.Add("-T 后缺少有效模板（0-5），已使用默认 T3。");
                    }
                    break;
                case "-pn":
                case "-pe":
                case "-pp":
                case "-ps":
                case "-pa":
                case "-pr":
                case "-n":
                case "-r":
                case "-v":
                case "-vv":
                case "-d":
                case "-dd":
                case "-q":
                case "--quiet":
                case "--open":
                case "--reason":
                    // 接受但无需特殊处理（本引擎默认不进行主机发现）。
                    break;
                case "-sv":
                case "-sc":
                case "-a":
                case "-o":
                case "--osscan-guess":
                case "--version-all":
                case "--version-light":
                case "--version-intensity":
                    options.Warnings.Add($"内置引擎不支持 {token} 版本/脚本/操作系统探测，已忽略该参数。");
                    if (lower is "--version-intensity" && i + 1 < tokens.Length)
                    {
                        i++;
                    }
                    break;
                case "--top-ports":
                    if (i + 1 >= tokens.Length || !int.TryParse(tokens[++i], out int top) || top <= 0)
                    {
                        throw new ArgumentException("--top-ports 后缺少有效数量。");
                    }
                    tcpPortList = CommonTcpPorts.Take(Math.Min(top, CommonTcpPorts.Length)).ToList();
                    udpPortList = CommonUdpPorts.Take(Math.Min(top, CommonUdpPorts.Length)).ToList();
                    portsSpecified = true;
                    break;
                case "--min-rate":
                case "--max-rate":
                case "--host-timeout":
                case "--max-retries":
                case "--scan-delay":
                case "--max-scan-delay":
                case "--max-parallelism":
                case "--min-parallelism":
                case "--stats-every":
                case "--source-port":
                case "--dns-servers":
                case "--exclude":
                case "--excludefile":
                case "--resume":
                case "--script":
                case "--script-args":
                case "--ttl":
                case "-e":
                case "-g":
                case "-s":
                case "-on":
                case "-ox":
                case "-og":
                case "-oa":
                    if (i + 1 >= tokens.Length)
                    {
                        options.Warnings.Add($"{token} 缺少参数，已忽略。");
                        break;
                    }
                    string value = tokens[++i];
                    if (lower == "--min-rate" && int.TryParse(value, out int minRate) && minRate > 0)
                    {
                        options.MinRate = minRate;
                    }
                    break;
                default:
                    if (lower.StartsWith("-p", StringComparison.Ordinal) && lower.Length > 2)
                    {
                        var parsedAttachedPorts = ParsePortList(token[2..]);
                        tcpPortList = parsedAttachedPorts;
                        udpPortList = parsedAttachedPorts;
                        portsSpecified = true;
                        break;
                    }
                    if (lower.StartsWith("-t", StringComparison.Ordinal) && lower.Length == 3
                        && int.TryParse(lower[2..], out int timing) && timing >= 0 && timing <= 5)
                    {
                        options.TimingTemplate = timing;
                        break;
                    }
                    if (lower.StartsWith("--min-rate=", StringComparison.Ordinal))
                    {
                        if (int.TryParse(lower["--min-rate=".Length..], out int rate) && rate > 0)
                        {
                            options.MinRate = rate;
                        }
                        break;
                    }
                    if (lower.StartsWith("--top-ports=", StringComparison.Ordinal))
                    {
                        if (int.TryParse(lower["--top-ports=".Length..], out int topPorts) && topPorts > 0)
                        {
                            tcpPortList = CommonTcpPorts.Take(Math.Min(topPorts, CommonTcpPorts.Length)).ToList();
                            udpPortList = CommonUdpPorts.Take(Math.Min(topPorts, CommonUdpPorts.Length)).ToList();
                            portsSpecified = true;
                        }
                        break;
                    }
                    options.Warnings.Add($"未识别的参数 {token}，已忽略。");
                    break;
            }
        }

        if (sawTcp && sawUdp)
        {
            options.ScanTcp = true;
            options.ScanUdp = true;
        }
        else if (sawUdp)
        {
            options.ScanTcp = false;
            options.ScanUdp = true;
        }
        else
        {
            options.ScanTcp = true;
            options.ScanUdp = false;
        }

        if (!portsSpecified)
        {
            var defaultSet = new SortedSet<int>();
            if (options.ScanTcp)
            {
                foreach (int p in CommonTcpPorts)
                {
                    defaultSet.Add(p);
                }
            }
            if (options.ScanUdp)
            {
                foreach (int p in CommonUdpPorts)
                {
                    defaultSet.Add(p);
                }
            }
            options.Ports.AddRange(defaultSet);
            options.Warnings.Add($"未指定 -p，已使用内置常用端口列表（{options.Ports.Count} 个）。");
        }
        else
        {
            var union = new SortedSet<int>();
            if (options.ScanTcp && tcpPortList != null)
            {
                foreach (int p in tcpPortList)
                {
                    union.Add(p);
                }
            }
            if (options.ScanUdp && udpPortList != null)
            {
                foreach (int p in udpPortList)
                {
                    union.Add(p);
                }
            }
            if (!options.ScanTcp && udpPortList != null)
            {
                foreach (int p in udpPortList)
                {
                    union.Add(p);
                }
            }
            if (!options.ScanUdp && tcpPortList != null)
            {
                foreach (int p in tcpPortList)
                {
                    union.Add(p);
                }
            }
            options.Ports.AddRange(union);
        }

        if (options.Targets.Count == 0)
        {
            throw new ArgumentException("命令中缺少目标 IP / 域名，例如 192.168.1.100。");
        }

        ApplyTiming(options);
        return options;
    }

    private static void ApplyTiming(NmapScanOptions options)
    {
        switch (options.TimingTemplate)
        {
            case 0:
                options.TcpTimeoutMs = 6000;
                options.UdpTimeoutMs = 6000;
                options.Concurrency = Math.Max(options.Concurrency, 16);
                break;
            case 1:
                options.TcpTimeoutMs = 5000;
                options.UdpTimeoutMs = 5000;
                options.Concurrency = Math.Max(options.Concurrency, 32);
                break;
            case 2:
                options.TcpTimeoutMs = 4000;
                options.UdpTimeoutMs = 4000;
                options.Concurrency = Math.Max(options.Concurrency, 96);
                break;
            case 3:
                options.TcpTimeoutMs = 3000;
                options.UdpTimeoutMs = 3000;
                options.Concurrency = Math.Max(options.Concurrency, 200);
                break;
            case 4:
                options.TcpTimeoutMs = 2000;
                options.UdpTimeoutMs = 2000;
                options.Concurrency = Math.Max(options.Concurrency, 400);
                break;
            case 5:
                options.TcpTimeoutMs = 1200;
                options.UdpTimeoutMs = 1200;
                options.Concurrency = Math.Max(options.Concurrency, 512);
                break;
        }

        options.Concurrency = Math.Clamp(options.Concurrency, 8, 1024);

        if (options.MinRate > 0)
        {
            int estimate = (int)Math.Round(options.MinRate * (options.TcpTimeoutMs / 1000.0));
            int cap = options.ScanUdp ? 512 : 1024;
            options.Concurrency = Math.Clamp(Math.Max(options.Concurrency, estimate), 8, cap);
        }
        else if (options.ScanUdp)
        {
            options.Concurrency = Math.Clamp(options.Concurrency, 8, 512);
        }
    }

    private static List<int> BuildFullPorts()
    {
        var list = new List<int>(65535);
        for (int i = 1; i <= 65535; i++)
        {
            list.Add(i);
        }
        return list;
    }

    private static List<int> ParsePortList(string text)
    {
        var set = new SortedSet<int>();
        foreach (string raw in text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string part = raw.Trim();
            if (part.Length == 0)
            {
                continue;
            }
            if (part.StartsWith("T:", StringComparison.OrdinalIgnoreCase)
                || part.StartsWith("U:", StringComparison.OrdinalIgnoreCase))
            {
                part = part[2..];
            }

            if (part.Contains('-', StringComparison.Ordinal))
            {
                string[] range = part.Split('-', 2);
                if (!int.TryParse(range[0], out int from) || !int.TryParse(range[1], out int to))
                {
                    throw new ArgumentException($"无法识别的端口范围：{part}");
                }
                if (from > to)
                {
                    (from, to) = (to, from);
                }
                from = Math.Max(1, from);
                to = Math.Min(65535, to);
                for (int p = from; p <= to; p++)
                {
                    set.Add(p);
                }
            }
            else if (int.TryParse(part, out int port))
            {
                if (port >= 1 && port <= 65535)
                {
                    set.Add(port);
                }
            }
            else if (PortNames.TryGetValue(part, out int named))
            {
                set.Add(named);
            }
            else
            {
                throw new ArgumentException($"无法识别的端口：{part}");
            }
        }

        if (set.Count == 0)
        {
            throw new ArgumentException("端口列表为空。");
        }
        return set.ToList();
    }

    private static void AddTargets(NmapScanOptions options, string raw)
    {
        string target = raw.Trim().Trim('"', '\'');
        if (target.Length == 0)
        {
            return;
        }

        if (target.Contains("://", StringComparison.Ordinal))
        {
            try
            {
                target = new Uri(target).Host;
            }
            catch (UriFormatException)
            {
                throw new ArgumentException($"无法识别的目标地址：{raw}");
            }
        }

        if (target.Contains('/', StringComparison.Ordinal) && !target.Contains("::", StringComparison.Ordinal))
        {
            ExpandCidr(options, target);
            return;
        }
        if (LooksLikeIpv4Range(target))
        {
            ExpandRange(options, target);
            return;
        }
        AddTarget(options, target);
    }

    private static void AddTarget(NmapScanOptions options, string target)
    {
        if (options.Targets.Contains(target, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }
        if (options.Targets.Count >= MaxTargets)
        {
            throw new ArgumentException($"目标数量超过 {MaxTargets} 个，请缩小扫描范围。");
        }
        options.Targets.Add(target);
    }

    private static bool LooksLikeIpv4Range(string target)
    {
        int dash = target.IndexOf('-');
        if (dash <= 0)
        {
            return false;
        }
        string left = target[..dash];
        string right = target[(dash + 1)..];
        return IsIpv4(left) && (IsIpv4(right) || right.All(char.IsDigit));
    }

    private static bool IsIpv4(string text)
    {
        return IPAddress.TryParse(text, out var address)
            && address.AddressFamily == AddressFamily.InterNetwork;
    }

    private static void ExpandCidr(NmapScanOptions options, string cidr)
    {
        string[] parts = cidr.Split('/', 2);
        if (!IPAddress.TryParse(parts[0], out var address)
            || address.AddressFamily != AddressFamily.InterNetwork
            || !int.TryParse(parts[1], out int prefix)
            || prefix < 0 || prefix > 32)
        {
            throw new ArgumentException($"无法识别的网段：{cidr}（仅支持 IPv4 CIDR）。");
        }

        uint network = ToUInt32(address);
        uint mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        uint first = network & mask;
        uint last = first | ~mask;
        long count = (long)last - first + 1;
        if (count > MaxTargets)
        {
            throw new ArgumentException($"网段 {cidr} 包含 {count} 个地址，超过 {MaxTargets} 个上限，请缩小范围。");
        }

        for (uint value = first; value <= last; value++)
        {
            AddTarget(options, FromUInt32(value).ToString());
            if (value == uint.MaxValue)
            {
                break;
            }
        }
    }

    private static void ExpandRange(NmapScanOptions options, string range)
    {
        int dash = range.IndexOf('-');
        string left = range[..dash];
        string right = range[(dash + 1)..];
        if (!IPAddress.TryParse(left, out var startAddress)
            || startAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException($"无法识别的地址范围：{range}");
        }

        IPAddress? endAddress;
        if (right.Contains('.', StringComparison.Ordinal))
        {
            if (!IPAddress.TryParse(right, out endAddress)
                || endAddress.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new ArgumentException($"无法识别的地址范围：{range}");
            }
        }
        else
        {
            if (!int.TryParse(right, out int lastOctet) || lastOctet < 0 || lastOctet > 255)
            {
                throw new ArgumentException($"无法识别的地址范围：{range}");
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
        if (count > MaxTargets)
        {
            throw new ArgumentException($"地址范围 {range} 包含 {count} 个地址，超过 {MaxTargets} 个上限。");
        }

        for (uint value = start; value <= end; value++)
        {
            AddTarget(options, FromUInt32(value).ToString());
            if (value == uint.MaxValue)
            {
                break;
            }
        }
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

    private static string[] Tokenize(string command)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        char quote = '\0';
        foreach (char c in command)
        {
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(c);
                }
                continue;
            }

            if (c == '"' || c == '\'')
            {
                quote = c;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }
        return result.ToArray();
    }
}

/// <summary>内置扫描引擎实现。</summary>
public static class NmapScanner
{
    private const int MaxResolvedHosts = 4096;
    private const int MaxOpenFilteredRows = 250;

    public static async Task<NmapScanResult> ScanAsync(
        NmapScanOptions options,
        IProgress<NmapProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new NmapScanResult
        {
            Command = options.Command,
            StartedAt = DateTime.Now,
            TcpEnabled = options.ScanTcp,
            UdpEnabled = options.ScanUdp
        };
        result.Warnings.AddRange(options.Warnings);
        var started = Stopwatch.StartNew();

        var hosts = new List<IPAddress>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string target in options.Targets)
        {
            if (IPAddress.TryParse(target, out var literal))
            {
                if (seen.Add(literal.ToString()))
                {
                    hosts.Add(literal);
                }
                continue;
            }

            try
            {
                using var dnsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, dnsTimeout.Token);
                var resolved = await Dns.GetHostAddressesAsync(target, linked.Token).ConfigureAwait(false);
                foreach (var address in resolved)
                {
                    if (address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6
                        && seen.Add(address.ToString()))
                    {
                        hosts.Add(address);
                    }
                }
                if (resolved.Length == 0)
                {
                    result.Errors.Add($"无法解析主机 {target}：未返回任何 IP 地址。");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"无法解析主机 {target}：{ex.Message}");
            }
        }

        if (hosts.Count == 0)
        {
            throw new InvalidOperationException(
                "没有可扫描的目标主机。\r\n" + string.Join("\r\n", result.Errors));
        }
        if (hosts.Count > MaxResolvedHosts)
        {
            throw new InvalidOperationException($"解析后的主机数量超过 {MaxResolvedHosts} 个，请缩小扫描范围。");
        }

        foreach (var host in hosts)
        {
            result.Hosts.Add(host.ToString());
        }

        int openTotal = 0;
        for (int hostIndex = 0; hostIndex < hosts.Count && !cancellationToken.IsCancellationRequested; hostIndex++)
        {
            var host = hosts[hostIndex];
            string hostText = host.ToString();

            if (options.ScanTcp)
            {
                await ScanTcpHostAsync(options, result, host, hostText, hostIndex, hosts.Count,
                    progress, () => Volatile.Read(ref openTotal), () => Interlocked.Increment(ref openTotal),
                    cancellationToken).ConfigureAwait(false);
            }

            if (options.ScanUdp && !cancellationToken.IsCancellationRequested)
            {
                await ScanUdpHostAsync(options, result, host, hostText, hostIndex, hosts.Count,
                    progress, () => Volatile.Read(ref openTotal), () => Interlocked.Increment(ref openTotal),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        started.Stop();
        result.Duration = started.Elapsed;
        result.FinishedAt = DateTime.Now;
        result.Cancelled = cancellationToken.IsCancellationRequested;
        result.OpenPortCount = Volatile.Read(ref openTotal);
        result.OpenPorts.Sort((a, b) =>
        {
            int hostCompare = string.Compare(a.Host, b.Host, StringComparison.OrdinalIgnoreCase);
            if (hostCompare != 0) return hostCompare;
            int protoCompare = string.Compare(a.Protocol, b.Protocol, StringComparison.OrdinalIgnoreCase);
            return protoCompare != 0 ? protoCompare : a.Port.CompareTo(b.Port);
        });
        result.OpenFilteredPorts.Sort((a, b) => a.Port.CompareTo(b.Port));
        return result;
    }

    private static async Task ScanTcpHostAsync(
        NmapScanOptions options,
        NmapScanResult result,
        IPAddress host,
        string hostText,
        int hostIndex,
        int hostCount,
        IProgress<NmapProgress>? progress,
        Func<int> getOpenTotal,
        Action incrementOpenTotal,
        CancellationToken cancellationToken)
    {
        int portCount = options.Ports.Count;
        if (portCount == 0)
        {
            return;
        }

        result.TcpPortsScanned += portCount;
        int index = -1;
        int done = 0;
        int closed = 0;
        int filtered = 0;
        long lastReport = 0;

        void Report(NmapPortResult? found, bool force)
        {
            long now = Stopwatch.GetTimestamp();
            long previous = Interlocked.Read(ref lastReport);
            if (!force && now - previous < Stopwatch.Frequency / 8)
            {
                return;
            }
            Interlocked.Exchange(ref lastReport, now);
            progress?.Report(new NmapProgress("TCP", hostIndex + 1, hostCount, hostText,
                Volatile.Read(ref done), portCount, getOpenTotal(), found));
        }

        async Task WorkerAsync()
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int i = Interlocked.Increment(ref index);
                if (i >= portCount)
                {
                    return;
                }

                int port = options.Ports[i];
                var probe = await ProbeTcpAsync(host, port, options.TcpTimeoutMs, cancellationToken)
                    .ConfigureAwait(false);

                if (probe.Open)
                {
                    var open = new NmapPortResult
                    {
                        Host = hostText,
                        Port = port,
                        Protocol = "tcp",
                        State = "open",
                        Service = NmapServices.GetServiceName(port, udp: false),
                        LatencyMs = probe.ElapsedMs
                    };
                    lock (result.OpenPorts)
                    {
                        result.OpenPorts.Add(open);
                    }
                    incrementOpenTotal();
                    Interlocked.Increment(ref done);
                    Report(open, force: true);
                }
                else
                {
                    if (probe.Filtered)
                    {
                        Interlocked.Increment(ref filtered);
                    }
                    else
                    {
                        Interlocked.Increment(ref closed);
                    }
                    Interlocked.Increment(ref done);
                    Report(null, force: false);
                }
            }
        }

        int workers = Math.Min(options.Concurrency, portCount);
        var tasks = new Task[workers];
        for (int i = 0; i < workers; i++)
        {
            tasks[i] = WorkerAsync();
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);

        result.TcpClosed += Volatile.Read(ref closed);
        result.TcpFiltered += Volatile.Read(ref filtered);
        Report(null, force: true);
    }

    private static async Task ScanUdpHostAsync(
        NmapScanOptions options,
        NmapScanResult result,
        IPAddress host,
        string hostText,
        int hostIndex,
        int hostCount,
        IProgress<NmapProgress>? progress,
        Func<int> getOpenTotal,
        Action incrementOpenTotal,
        CancellationToken cancellationToken)
    {
        int portCount = options.Ports.Count;
        if (portCount == 0)
        {
            return;
        }

        result.UdpPortsScanned += portCount;
        int index = -1;
        int done = 0;
        int closed = 0;
        int openFiltered = 0;
        long lastReport = 0;

        void Report(NmapPortResult? found, bool force)
        {
            long now = Stopwatch.GetTimestamp();
            long previous = Interlocked.Read(ref lastReport);
            if (!force && now - previous < Stopwatch.Frequency / 8)
            {
                return;
            }
            Interlocked.Exchange(ref lastReport, now);
            progress?.Report(new NmapProgress("UDP", hostIndex + 1, hostCount, hostText,
                Volatile.Read(ref done), portCount, getOpenTotal(), found));
        }

        async Task WorkerAsync()
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int i = Interlocked.Increment(ref index);
                if (i >= portCount)
                {
                    return;
                }

                int port = options.Ports[i];
                var probe = await ProbeUdpAsync(host, port, options.UdpTimeoutMs, cancellationToken)
                    .ConfigureAwait(false);

                if (probe.Open)
                {
                    var open = new NmapPortResult
                    {
                        Host = hostText,
                        Port = port,
                        Protocol = "udp",
                        State = "open",
                        Service = NmapServices.GetServiceName(port, udp: true),
                        LatencyMs = probe.ElapsedMs,
                        Detail = probe.Detail
                    };
                    lock (result.OpenPorts)
                    {
                        result.OpenPorts.Add(open);
                    }
                    incrementOpenTotal();
                    Interlocked.Increment(ref done);
                    Report(open, force: true);
                }
                else if (probe.Filtered)
                {
                    Interlocked.Increment(ref openFiltered);
                    lock (result.OpenFilteredPorts)
                    {
                        if (result.OpenFilteredPorts.Count < MaxOpenFilteredRows)
                        {
                            result.OpenFilteredPorts.Add(new NmapPortResult
                            {
                                Host = hostText,
                                Port = port,
                                Protocol = "udp",
                                State = "open|filtered",
                                Service = NmapServices.GetServiceName(port, udp: true)
                            });
                        }
                    }
                    Interlocked.Increment(ref done);
                    Report(null, force: false);
                }
                else
                {
                    Interlocked.Increment(ref closed);
                    Interlocked.Increment(ref done);
                    Report(null, force: false);
                }
            }
        }

        int workers = Math.Min(options.Concurrency, portCount);
        var tasks = new Task[workers];
        for (int i = 0; i < workers; i++)
        {
            tasks[i] = WorkerAsync();
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);

        result.UdpClosed += Volatile.Read(ref closed);
        result.UdpOpenFiltered += Volatile.Read(ref openFiltered);
        Report(null, force: true);
    }

    private readonly record struct TcpProbeResult(bool Open, bool Filtered, double? ElapsedMs);

    private readonly record struct UdpProbeResult(bool Open, bool Filtered, double? ElapsedMs, string? Detail);

    private static async Task<TcpProbeResult> ProbeTcpAsync(
        IPAddress host, int port, int timeoutMs, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var socket = new Socket(host.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        socket.NoDelay = true;
        using var timeout = new CancellationTokenSource(timeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await socket.ConnectAsync(new IPEndPoint(host, port), linked.Token).ConfigureAwait(false);
            stopwatch.Stop();
            return new TcpProbeResult(true, false, stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            return new TcpProbeResult(false, !cancellationToken.IsCancellationRequested, null);
        }
        catch (SocketException ex)
        {
            bool filtered = ex.SocketErrorCode is SocketError.TimedOut
                or SocketError.HostUnreachable
                or SocketError.NetworkUnreachable;
            return new TcpProbeResult(false, filtered, null);
        }
        catch
        {
            return new TcpProbeResult(false, true, null);
        }
    }

    private static async Task<UdpProbeResult> ProbeUdpAsync(
        IPAddress host, int port, int timeoutMs, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var socket = new Socket(host.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        using var timeout = new CancellationTokenSource(timeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            socket.Connect(new IPEndPoint(host, port));
            byte[] probe = UdpProbePayloads.Build(port);
            await socket.SendAsync(probe, SocketFlags.None, linked.Token).ConfigureAwait(false);

            byte[] buffer = new byte[4096];
            int received = await socket.ReceiveAsync(buffer, SocketFlags.None, linked.Token).ConfigureAwait(false);
            stopwatch.Stop();
            string? detail = null;
            if (received > 0)
            {
                string text = Encoding.ASCII.GetString(buffer, 0, Math.Min(received, 120));
                text = Regex.Replace(text, @"[\x00-\x1F\x7F]", " ").Trim();
                if (text.Length > 60)
                {
                    text = text[..60] + "…";
                }
                detail = text.Length > 0 ? text : $"{received} 字节响应";
            }
            return new UdpProbeResult(true, false, stopwatch.Elapsed.TotalMilliseconds, detail);
        }
        catch (OperationCanceledException)
        {
            return new UdpProbeResult(false, !cancellationToken.IsCancellationRequested, null, null);
        }
        catch (SocketException ex)
        {
            bool closed = ex.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionRefused;
            return new UdpProbeResult(false, !closed, null, null);
        }
        catch
        {
            return new UdpProbeResult(false, true, null, null);
        }
    }
}

/// <summary>常用端口服务名。</summary>
internal static class NmapServices
{
    private static readonly Dictionary<int, string> TcpNames = new()
    {
        [7] = "echo", [9] = "discard", [13] = "daytime", [20] = "ftp-data", [21] = "ftp",
        [22] = "ssh", [23] = "telnet", [25] = "smtp", [37] = "time", [53] = "domain",
        [67] = "dhcps", [68] = "dhcpc", [69] = "tftp", [79] = "finger", [80] = "http",
        [81] = "http", [88] = "kerberos", [110] = "pop3", [111] = "rpcbind", [119] = "nntp",
        [123] = "ntp", [135] = "msrpc", [137] = "netbios-ns", [138] = "netbios-dgm",
        [139] = "netbios-ssn", [143] = "imap", [161] = "snmp", [162] = "snmptrap",
        [179] = "bgp", [389] = "ldap", [427] = "svrloc", [443] = "https", [445] = "microsoft-ds",
        [465] = "smtps", [500] = "isakmp", [514] = "syslog", [515] = "printer",
        [520] = "route", [543] = "klogin", [544] = "kshell", [548] = "afp",
        [554] = "rtsp", [587] = "submission", [623] = "ipmi", [631] = "ipp",
        [636] = "ldaps", [873] = "rsync", [990] = "ftps", [993] = "imaps",
        [995] = "pop3s", [1025] = "NFS-or-IIS", [1080] = "socks", [1194] = "openvpn",
        [1433] = "ms-sql-s", [1521] = "oracle", [1723] = "pptp", [1883] = "mqtt",
        [1900] = "upnp", [2049] = "nfs", [2121] = "ftp", [2222] = "EtherNet/IP",
        [2375] = "docker", [2376] = "docker-s", [3000] = "ppp", [3128] = "squid-http",
        [3306] = "mysql", [3389] = "ms-wbt-server", [3690] = "svn", [4369] = "epmd",
        [5000] = "upnp", [5060] = "sip", [5061] = "sips", [5432] = "postgresql",
        [5672] = "amqp", [5900] = "vnc", [5984] = "couchdb", [5985] = "wsman",
        [5986] = "wsmans", [6379] = "redis", [6667] = "irc", [8000] = "http-alt",
        [8080] = "http-proxy", [8081] = "http-alt", [8443] = "https-alt",
        [8888] = "http-alt", [9000] = "cslistener", [9042] = "cassandra",
        [9090] = "zeus-admin", [9100] = "jetdirect", [9200] = "elasticsearch",
        [9300] = "elasticsearch", [9443] = "https-alt", [11211] = "memcached",
        [15672] = "rabbitmq-management", [27017] = "mongodb", [27018] = "mongodb",
        [50000] = "sap"
    };

    private static readonly Dictionary<int, string> UdpNames = new()
    {
        [53] = "domain", [67] = "dhcps", [68] = "dhcpc", [69] = "tftp", [111] = "rpcbind",
        [123] = "ntp", [135] = "msrpc", [137] = "netbios-ns", [138] = "netbios-dgm",
        [161] = "snmp", [162] = "snmptrap", [177] = "xdmcp", [427] = "svrloc",
        [500] = "isakmp", [514] = "syslog", [520] = "route", [623] = "ipmi",
        [631] = "ipp", [996] = "vsinet", [1194] = "openvpn", [1434] = "ms-sql-m",
        [1701] = "l2tp", [1900] = "upnp", [2049] = "nfs", [3283] = "netassistant",
        [3702] = "ws-discovery", [4500] = "ipsec-nat-t", [5060] = "sip",
        [5353] = "mdns", [5355] = "llmnr", [49152] = "upnp"
    };

    public static string GetServiceName(int port, bool udp)
    {
        var table = udp ? UdpNames : TcpNames;
        if (table.TryGetValue(port, out string? name))
        {
            return name;
        }
        if (port is >= 1 and <= 1024)
        {
            return "unknown";
        }
        return "unknown";
    }
}

/// <summary>UDP 探测包：针对常见服务构造更容易获得回复的报文。</summary>
internal static class UdpProbePayloads
{
    public static byte[] Build(int port)
    {
        return port switch
        {
            53 => DnsQuery(),
            123 => NtpQuery(),
            161 => SnmpGet(),
            1900 => Encoding.ASCII.GetBytes(
                "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 1\r\nST: ssdp:all\r\n\r\n"),
            5353 => MdnsQuery(),
            _ => new byte[] { 0x00 }
        };
    }

    private static byte[] DnsQuery()
    {
        // version.bind CH TXT 查询
        byte[] qname = { 8, (byte)'v', (byte)'e', (byte)'r', (byte)'s', (byte)'i', (byte)'o', (byte)'n',
                         4, (byte)'b', (byte)'i', (byte)'n', (byte)'d', 0 };
        var packet = new List<byte>
        {
            0x12, 0x34, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };
        packet.AddRange(qname);
        packet.AddRange(new byte[] { 0x00, 0x10, 0x00, 0x03 });
        return packet.ToArray();
    }

    private static byte[] NtpQuery()
    {
        var packet = new byte[48];
        packet[0] = 0x23; // LI=0, VN=4, Mode=3 (client)
        return packet;
    }

    private static byte[] SnmpGet()
    {
        // SNMP v1 GetRequest: 1.3.6.1.2.1.1.1.0 (sysDescr.0), community "public"
        return new byte[]
        {
            0x30, 0x29,
            0x02, 0x01, 0x00,
            0x04, 0x06, 0x70, 0x75, 0x62, 0x6C, 0x69, 0x63,
            0xA0, 0x1C,
            0x02, 0x04, 0x71, 0x06, 0x0E, 0x0E,
            0x02, 0x01, 0x00,
            0x02, 0x01, 0x00,
            0x30, 0x0E,
            0x30, 0x0C,
            0x06, 0x08, 0x2B, 0x06, 0x01, 0x02, 0x01, 0x01, 0x01, 0x00,
            0x05, 0x00
        };
    }

    private static byte[] MdnsQuery()
    {
        byte[] qname = { 9, (byte)'_', (byte)'s', (byte)'e', (byte)'r', (byte)'v', (byte)'i', (byte)'c', (byte)'e', (byte)'s',
                         7, (byte)'_', (byte)'d', (byte)'n', (byte)'s', (byte)'-', (byte)'s', (byte)'d',
                         4, (byte)'_', (byte)'u', (byte)'d', (byte)'p',
                         5, (byte)'l', (byte)'o', (byte)'c', (byte)'a', (byte)'l', 0 };
        var packet = new List<byte>
        {
            0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };
        packet.AddRange(qname);
        packet.AddRange(new byte[] { 0x00, 0x0C, 0x00, 0x01 });
        return packet.ToArray();
    }
}

/// <summary>扫描结果文件保存：Nmap_results_日期_时间_随机数.txt。</summary>
public static class NmapResultStorage
{
    public static string Save(NmapScanResult result)
    {
        string folder = GetFolder();
        Directory.CreateDirectory(folder);

        string file;
        do
        {
            string random = Random.Shared.Next(1000, 10000).ToString();
            string name = $"Nmap_results_{DateTime.Now:yyyyMMdd_HHmmss}_{random}.txt";
            file = Path.Combine(folder, name);
        }
        while (File.Exists(file));

        File.WriteAllText(file, string.Join(Environment.NewLine, result.BuildReportLines()),
            new UTF8Encoding(false));
        return file;
    }

    public static string GetFolder()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
        {
            documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        if (string.IsNullOrWhiteSpace(documents))
        {
            documents = Environment.CurrentDirectory;
        }
        return Path.Combine(documents, "MiniMonitor", "NmapResults");
    }
}
