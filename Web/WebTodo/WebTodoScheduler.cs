using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace MiniMonitor.Web.WebTodo;

/// <summary>NTP 校时（SNTP，多服务器 + 多次采样取最小往返延迟）。</summary>
public sealed class NtpClock
{
    private const uint NtpEpoch = 2208988800;
    private readonly TodoConfig _config;
    private readonly object _lock = new();
    private readonly ManualResetEventSlim _stop = new(false);
    private readonly ManualResetEventSlim _wake = new(false);
    private Thread? _thread;

    public List<string> Servers { get; } = TodoConfig.NtpServers.ToList();
    public int Interval { get; } = TodoConfig.NtpSyncIntervalSeconds;
    public double OffsetSeconds { get; private set; }
    public string? Server { get; private set; }
    public string? LastSyncAt { get; private set; }
    public string? LastError { get; private set; }
    public double? RoundTripMs { get; private set; }
    public int SyncCount { get; private set; }
    public int FailCount { get; private set; }
    public bool Running { get; private set; }
    public string StartedAt { get; } = TodoClock.IsoNow();

    public NtpClock(TodoConfig config)
    {
        _config = config;
    }

    public bool Enabled
    {
        get
        {
            JsonNode? node = _config.Settings["clock"]?["useNtp"];
            return node == null || TodoClock.ToBool(node, true);
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_thread != null && _thread.IsAlive) return;
            if (!Enabled)
            {
                TodoClock.SetOffset(0);
                return;
            }
            Running = true;
            _stop.Reset();
            _thread = new Thread(Loop) { IsBackground = true, Name = "WebTodo-ntp" };
            _thread.Start();
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _stop.Set();
            _wake.Set();
            Running = false;
            try { _thread?.Join(2000); } catch { }
            _thread = null;
        }
    }

    public void Trigger() => _wake.Set();

    private void Loop()
    {
        Sync();
        while (!_stop.IsSet)
        {
            _wake.Wait(Interval);
            _wake.Reset();
            if (_stop.IsSet) break;
            Sync();
        }
    }

    public bool Sync()
    {
        var errors = new List<string>();
        foreach (string server in Servers)
        {
            try
            {
                var (offset, roundTrip) = QueryBest(server);
                lock (_lock)
                {
                    OffsetSeconds = offset;
                    Server = server;
                    LastSyncAt = TodoClock.Iso(TodoClock.LocalNow());
                    LastError = null;
                    RoundTripMs = roundTrip;
                    SyncCount++;
                }
                TodoClock.SetOffset(offset);
                return true;
            }
            catch (Exception ex)
            {
                errors.Add($"{server}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        lock (_lock)
        {
            FailCount++;
            LastError = TodoClock.Clip(string.Join("；", errors.Count > 0 ? errors : new List<string> { "无可用 NTP 服务器" }), 300);
        }
        TodoClock.SetOffset(OffsetSeconds);
        return false;
    }

    public JsonObject Status()
    {
        lock (_lock)
        {
            string? nextAt = null;
            if (LastSyncAt != null && TodoClock.ParseDt(LastSyncAt) is DateTime parsed)
            {
                nextAt = TodoClock.Iso(parsed.AddSeconds(Interval));
            }
            return new JsonObject
            {
                ["useNtp"] = Enabled,
                ["synced"] = LastSyncAt != null,
                ["current"] = Server,
                ["servers"] = new JsonArray(Servers.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
                ["offsetSeconds"] = Math.Round(OffsetSeconds, 3),
                ["roundTripMs"] = RoundTripMs,
                ["lastSyncAt"] = LastSyncAt,
                ["nextSyncAt"] = nextAt,
                ["intervalSeconds"] = Interval,
                ["syncCount"] = SyncCount,
                ["failCount"] = FailCount,
                ["lastError"] = LastError,
                ["running"] = Running,
                ["startedAt"] = StartedAt,
                ["ntpTime"] = TodoClock.IsoNow(),
                ["localTime"] = TodoClock.Iso(TodoClock.LocalNow())
            };
        }
    }

    private static (double OffsetSeconds, double RoundTripMs) QueryBest(string server)
    {
        Exception? lastError = null;
        double bestOffset = 0, bestDelay = double.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            try
            {
                var (offset, delay) = Query(server, TodoConfig.NtpTimeoutSeconds);
                if (delay < bestDelay)
                {
                    bestDelay = delay;
                    bestOffset = offset;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }
        if (bestDelay == double.MaxValue)
        {
            throw lastError ?? new InvalidOperationException($"{server} 无响应");
        }
        return (bestOffset, Math.Round(bestDelay, 2));
    }

    private static (double OffsetSeconds, double RoundTripMs) Query(string server, int timeoutSeconds)
    {
        IPAddress[] addresses = Dns.GetHostAddresses(server);
        IPAddress address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                            ?? addresses.FirstOrDefault() ?? throw new InvalidOperationException("域名未解析到地址");
        using var client = new UdpClient(address.AddressFamily);
        client.Client.ReceiveTimeout = timeoutSeconds * 1000;
        client.Connect(address, 123);
        byte[] packet = new byte[48];
        packet[0] = 0x23;
        DateTime sentLocal = DateTime.Now;
        double sentUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        uint seconds = (uint)(sentUnix + NtpEpoch);
        packet[40] = (byte)(seconds >> 24);
        packet[41] = (byte)(seconds >> 16);
        packet[42] = (byte)(seconds >> 8);
        packet[43] = (byte)seconds;
        client.Send(packet, packet.Length);
        IPEndPoint remote = new(IPAddress.Any, 0);
        byte[] data = client.Receive(ref remote);
        DateTime receivedLocal = DateTime.Now;
        double receivedUnix = sentUnix + (receivedLocal - sentLocal).TotalSeconds;
        if (data.Length < 48)
        {
            throw new InvalidOperationException($"响应长度异常：{data.Length} 字节");
        }
        int mode = data[0] & 0x07;
        if (mode is not (4 or 5))
        {
            throw new InvalidOperationException($"响应模式异常：{mode}");
        }
        if (data[1] == 0)
        {
            throw new InvalidOperationException("服务器返回 Kiss-o'-the-Death，已拒绝");
        }
        uint serverSeconds = ((uint)data[40] << 24) | ((uint)data[41] << 16) | ((uint)data[42] << 8) | data[43];
        uint fraction = ((uint)data[44] << 24) | ((uint)data[45] << 16) | ((uint)data[46] << 8) | data[47];
        if (serverSeconds == 0)
        {
            throw new InvalidOperationException("服务器未提供时间戳");
        }
        double ntpUnix = serverSeconds - (double)NtpEpoch + fraction / 4294967296.0;
        double delay = receivedUnix - sentUnix;
        double offset = ntpUnix + delay / 2 - receivedUnix;
        return (offset, delay * 1000);
    }
}
