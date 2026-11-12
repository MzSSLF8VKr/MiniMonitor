using System.Net.NetworkInformation;

namespace MiniMonitor;

/// <summary>
/// 网卡上下行速率采样。
/// 原理：读取网卡累计字节计数器，两次采样的差值 ÷ 时间差得到字节/秒，再换算成 MB/s。
/// </summary>
public sealed class NetworkReader
{
    private readonly string _nicId;
    private long _lastRxBytes;
    private long _lastTxBytes;
    private DateTime _lastSampleUtc;
    private bool _primed;
    private double _downMBps;
    private double _upMBps;

    /// <summary>
    /// 自动模式下需要排除的虚拟/隧道/软件网卡关键字（匹配网卡名称与描述）。
    /// </summary>
    private static readonly string[] VirtualNicKeywords =
    {
        "Loopback", "Virtual", "VMware", "VBox", "VirtualBox", "Hyper-V",
        "vEthernet", "WSL", "Tailscale", "ZeroTier", "TAP", "TUN", "Tunnel",
        "Bluetooth", "蓝牙", "Pseudo", "KM-TEST", "Sangfor", "VPN",
        "WAN Miniport", "ISATAP", "Teredo", "6to4", "Direct Route",
        "Microsoft Network", "FireCube", "Clash"
    };

    public NetworkReader(string nicId)
    {
        _nicId = string.IsNullOrWhiteSpace(nicId) ? "auto" : nicId;
        _lastSampleUtc = DateTime.UtcNow;
        Prime();
    }

    /// <summary>全部处于 Up 状态的非回环、非隧道网卡（含虚拟网卡，供手动选择）。</summary>
    public static List<NetworkInterface> GetInterfaces()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .ToList();
    }

    /// <summary>自动模式使用的物理网卡（排除常见虚拟网卡）。</summary>
    public static List<NetworkInterface> GetPhysicalInterfaces()
    {
        return GetInterfaces().Where(n =>
        {
            var text = n.Name + " " + n.Description;
            return !VirtualNicKeywords.Any(k =>
                text.Contains(k, StringComparison.OrdinalIgnoreCase));
        }).ToList();
    }

    private IEnumerable<NetworkInterface> Targets()
    {
        if (_nicId == "auto")
        {
            return GetPhysicalInterfaces();
        }

        var exact = GetInterfaces().FirstOrDefault(n => n.Id == _nicId);
        return exact != null ? new[] { exact } : GetPhysicalInterfaces();
    }

    /// <summary>首次采样只建立基线，不计算速率。</summary>
    private void Prime()
    {
        var (rx, tx) = ReadCounters();
        _lastRxBytes = rx;
        _lastTxBytes = tx;
        _primed = true;
    }

    private (long Rx, long Tx) ReadCounters()
    {
        long rx = 0;
        long tx = 0;
        foreach (var nic in Targets())
        {
            try
            {
                // IPv4 累计字节计数器（.NET 8 兼容，Windows 上由 IP Helper API 提供）
                var stats = nic.GetIPv4Statistics();
                rx += stats.BytesReceived;
                tx += stats.BytesSent;
            }
            catch
            {
                // 某些虚拟网卡不支持统计接口，跳过
            }
        }
        return (rx, tx);
    }

    /// <summary>
    /// 执行一次采样，返回 (下行 MB/s, 上行 MB/s)。建议每 1000ms 调用一次。
    /// </summary>
    public (double DownMBps, double UpMBps) Sample()
    {
        var now = DateTime.UtcNow;
        var (rx, tx) = ReadCounters();
        var elapsed = (now - _lastSampleUtc).TotalSeconds;

        if (_primed && elapsed > 0)
        {
            // 网卡重启/计数器回绕时结果为负，按 0 处理
            var deltaRx = rx - _lastRxBytes;
            var deltaTx = tx - _lastTxBytes;
            _downMBps = deltaRx > 0 ? deltaRx / elapsed / 1048576.0 : 0;
            _upMBps = deltaTx > 0 ? deltaTx / elapsed / 1048576.0 : 0;
        }
        else
        {
            _downMBps = 0;
            _upMBps = 0;
        }

        _lastRxBytes = rx;
        _lastTxBytes = tx;
        _lastSampleUtc = now;
        _primed = true;
        return (_downMBps, _upMBps);
    }
}
