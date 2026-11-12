using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace MiniMonitor;

/// <summary>一次 NTP 同步结果。</summary>
public sealed record NtpSyncRecord(
    DateTime Timestamp,
    bool Success,
    string Server,
    double OffsetMs,
    double DelayMs,
    bool TimeAdjusted,
    string Message);

/// <summary>NTP 同步历史（最近 100 条，最新在前，持久化到 APPDATA）。</summary>
public sealed class NtpHistoryStore
{
    public const int MaxRecords = 100;

    private readonly string _path;
    private readonly object _sync = new();
    private readonly List<NtpSyncRecord> _records = new();

    public NtpHistoryStore()
    {
        _path = Path.Combine(AppConfig.ConfigDir, "ntp_history.json");
        Load();
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _records.Count;
            }
        }
    }

    public IReadOnlyList<NtpSyncRecord> Snapshot()
    {
        lock (_sync)
        {
            return _records.ToList();
        }
    }

    public void Add(NtpSyncRecord record)
    {
        lock (_sync)
        {
            _records.Insert(0, record);
            if (_records.Count > MaxRecords)
            {
                _records.RemoveRange(MaxRecords, _records.Count - MaxRecords);
            }
            SaveLocked();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }
            string json = File.ReadAllText(_path);
            List<NtpSyncRecord>? records = JsonSerializer.Deserialize<List<NtpSyncRecord>>(json);
            if (records == null)
            {
                return;
            }
            _records.AddRange(records.Take(MaxRecords));
        }
        catch
        {
            _records.Clear();
        }
    }

    private void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(AppConfig.ConfigDir);
            string temp = _path + ".tmp";
            string json = JsonSerializer.Serialize(_records);
            File.WriteAllText(temp, json, Encoding.UTF8);
            File.Move(temp, _path, overwrite: true);
        }
        catch
        {
            // 历史保存失败不影响同步本身
        }
    }
}

/// <summary>
/// NTP 自动同步服务：默认每 5 分钟向主/备用 NTP 服务器校时，并校准本机系统时间。
/// 需以管理员身份运行（manifest 已要求），用于启用 SeSystemtimePrivilege。
/// </summary>
public sealed class NtpSyncManager : IDisposable
{
    public static readonly TimeSpan AutoInterval = TimeSpan.FromMinutes(5);

    private sealed record NtpServerSpec(string Host, string Label);

    private static readonly NtpServerSpec[] Servers =
    {
        new("edu.ntp.org.cn", "主服务器"),
        new("ntp.aliyun.com", "备用1"),
        new("time.cloudflare.com", "备用2")
    };

    private const double ApplyThresholdMs = 50.0;
    private const int NtpPort = 123;
    private const int ReceiveTimeoutMs = 2500;

    private readonly NtpHistoryStore _history;
    private readonly object _stateSync = new();
    private readonly bool _canSetSystemTime;
    private System.Threading.Timer? _autoTimer;
    private bool _autoEnabled;
    private bool _disposed;
    private int _busy;

    public event Action<NtpSyncRecord>? SyncCompleted;

    public NtpSyncManager(bool autoSyncEnabled, NtpHistoryStore history)
    {
        _history = history;
        _autoEnabled = autoSyncEnabled;
        _canSetSystemTime = TryEnableSystemTimePrivilege();
        if (autoSyncEnabled)
        {
            StartAutoTimer(TimeSpan.FromSeconds(10));
        }
    }

    public static string ServerSummary =>
        "主服务器 edu.ntp.org.cn · 备用1 ntp.aliyun.com · 备用2 time.cloudflare.com";

    public bool AutoSyncEnabled
    {
        get
        {
            lock (_stateSync)
            {
                return _autoEnabled;
            }
        }
    }

    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    public bool CanSetSystemTime => _canSetSystemTime;

    public void SetAutoSync(bool enabled)
    {
        lock (_stateSync)
        {
            if (_disposed)
            {
                return;
            }
            _autoEnabled = enabled;
            StopAutoTimerLocked();
            if (enabled)
            {
                StartAutoTimer(TimeSpan.Zero);
            }
        }
    }

    private void StartAutoTimer(TimeSpan dueTime)
    {
        lock (_stateSync)
        {
            if (_disposed)
            {
                return;
            }
            StopAutoTimerLocked();
            _autoTimer = new System.Threading.Timer(
                _ => { _ = SyncNowAsync(); },
                null, dueTime, AutoInterval);
        }
    }

    private void StopAutoTimerLocked()
    {
        if (_autoTimer != null)
        {
            _autoTimer.Dispose();
            _autoTimer = null;
        }
    }

    /// <summary>立即同步；若已有同步在进行则返回 null。</summary>
    public async Task<NtpSyncRecord?> SyncNowAsync()
    {
        if (_disposed)
        {
            return null;
        }
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return null;
        }
        try
        {
            NtpSyncRecord record = await Task.Run(SyncOnce).ConfigureAwait(false);
            _history.Add(record);
            try
            {
                SyncCompleted?.Invoke(record);
            }
            catch
            {
                // 界面回调异常不影响同步记录
            }
            return record;
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private NtpSyncRecord SyncOnce()
    {
        var failures = new List<string>();
        for (int i = 0; i < Servers.Length; i++)
        {
            NtpServerSpec spec = Servers[i];
            NtpQueryResult result;
            try
            {
                result = QueryServer(spec);
            }
            catch (Exception ex)
            {
                failures.Add(spec.Label + " " + spec.Host + "：" + ex.Message);
                continue;
            }

            bool fallback = i > 0;
            bool adjusted = false;
            string message;
            if (Math.Abs(result.OffsetMs) >= ApplyThresholdMs)
            {
                if (!_canSetSystemTime)
                {
                    return new NtpSyncRecord(DateTime.Now, false,
                        spec.Host + "（" + spec.Label + "）",
                        result.OffsetMs, result.DelayMs, false,
                        "NTP 查询成功，但未能获得设置系统时间的权限。请以管理员身份运行。"
                        + (fallback ? "\n主服务器不可用，已切换 " + spec.Label + "。" : ""));
                }
                try
                {
                    SetSystemTime(result.CorrectedUtc);
                    adjusted = true;
                    message = "已校准系统时间";
                }
                catch (Exception ex)
                {
                    return new NtpSyncRecord(DateTime.Now, false,
                        spec.Host + "（" + spec.Label + "）",
                        result.OffsetMs, result.DelayMs, false,
                        "NTP 查询成功，但校准系统时间失败：" + ex.Message);
                }
            }
            else
            {
                message = "偏差在 " + ApplyThresholdMs.ToString("0", CultureInfo.InvariantCulture)
                    + " ms 以内，无需调整";
            }

            if (fallback)
            {
                message = "主服务器不可用，已切换 " + spec.Label + "。" + message;
            }
            return new NtpSyncRecord(DateTime.Now, true,
                spec.Host + "（" + spec.Label + "）",
                result.OffsetMs, result.DelayMs, adjusted, message);
        }

        return new NtpSyncRecord(DateTime.Now, false, "全部 NTP 服务器",
            0, 0, false,
            "所有 NTP 服务器均同步失败：\n" + string.Join("\n", failures));
    }

    private sealed record NtpQueryResult(double OffsetMs, double DelayMs, DateTime CorrectedUtc);

    private static NtpQueryResult QueryServer(NtpServerSpec spec)
    {
        IPAddress[] addresses = Dns.GetHostAddresses(spec.Host);
        if (addresses.Length == 0)
        {
            throw new InvalidOperationException("NTP 服务器域名未解析到地址。");
        }
        // 优先 IPv4，避免在仅支持 IPv4 的网络中被 IPv6 地址拖慢/失败。
        IPAddress address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses[0];
        using var client = new UdpClient(address.AddressFamily);
        client.Client.ReceiveTimeout = ReceiveTimeoutMs;
        client.Connect(address, NtpPort);

        byte[] request = new byte[48];
        // LI = 0, VN = 4, Mode = 3 (client)
        request[0] = 0x23;

        DateTime t1 = DateTime.UtcNow;
        WriteNtpTimestamp(request, 40, t1);
        client.Send(request, request.Length);
        IPEndPoint remote = new(IPAddress.Any, 0);
        byte[] response = client.Receive(ref remote);
        DateTime t4 = DateTime.UtcNow;

        if (response.Length < 48)
        {
            throw new InvalidOperationException("NTP 响应长度不足。");
        }
        int leap = response[0] >> 6;
        int mode = response[0] & 0x07;
        int stratum = response[1];
        if (mode != 4)
        {
            throw new InvalidOperationException("NTP 响应模式无效。");
        }
        if (leap == 3)
        {
            throw new InvalidOperationException("NTP 服务器当前未同步。");
        }
        if (stratum < 1 || stratum > 15)
        {
            throw new InvalidOperationException("NTP 服务器层级无效（stratum " + stratum + "）。");
        }

        DateTime t2 = ReadNtpTimestamp(response, 32);
        DateTime t3 = ReadNtpTimestamp(response, 40);
        if (t3.Year < 2000 || t3.Year > 2100)
        {
            throw new InvalidOperationException("NTP 服务器时间异常。");
        }

        double offset = ((t2 - t1).TotalMilliseconds + (t3 - t4).TotalMilliseconds) / 2.0;
        double delay = (t4 - t1).TotalMilliseconds - (t3 - t2).TotalMilliseconds;
        if (delay < 0)
        {
            delay = 0;
        }
        DateTime corrected = DateTime.UtcNow.AddMilliseconds(offset);
        return new NtpQueryResult(offset, delay, corrected);
    }

    private static DateTime ReadNtpTimestamp(byte[] buffer, int offset)
    {
        uint seconds = ((uint)buffer[offset] << 24)
            | ((uint)buffer[offset + 1] << 16)
            | ((uint)buffer[offset + 2] << 8)
            | buffer[offset + 3];
        uint fraction = ((uint)buffer[offset + 4] << 24)
            | ((uint)buffer[offset + 5] << 16)
            | ((uint)buffer[offset + 6] << 8)
            | buffer[offset + 7];
        double value = seconds + fraction / 4294967296.0;
        DateTime epoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return epoch.AddSeconds(value);
    }

    private static void WriteNtpTimestamp(byte[] buffer, int offset, DateTime utc)
    {
        DateTime epoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        double value = Math.Max(0d, (utc - epoch).TotalSeconds);
        uint seconds = (uint)Math.Floor(value);
        uint fraction = (uint)((value - seconds) * 4294967296.0);
        buffer[offset] = (byte)(seconds >> 24);
        buffer[offset + 1] = (byte)(seconds >> 16);
        buffer[offset + 2] = (byte)(seconds >> 8);
        buffer[offset + 3] = (byte)seconds;
        buffer[offset + 4] = (byte)(fraction >> 24);
        buffer[offset + 5] = (byte)(fraction >> 16);
        buffer[offset + 6] = (byte)(fraction >> 8);
        buffer[offset + 7] = (byte)fraction;
    }

    // ---------- Win32 系统时间权限 ----------
    private const uint TokenAdjustPrivileges = 0x00000020;
    private const uint TokenQuery = 0x00000008;
    private const uint SePrivilegeEnabled = 0x00000002;
    private const int ErrorNotAllAssigned = 1300;

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LuidAndAttributes
    {
        public Luid Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public LuidAndAttributes Privilege;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValueW(string? systemName, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle, bool disableAllPrivileges,
        ref TokenPrivileges newState, int bufferLength,
        IntPtr previousState, IntPtr returnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSystemTime(ref SystemTime systemTime);

    private static bool TryEnableSystemTimePrivilege()
    {
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out token))
            {
                return false;
            }
            if (!LookupPrivilegeValueW(null, "SeSystemtimePrivilege", out Luid luid))
            {
                return false;
            }
            var privileges = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Privilege = new LuidAndAttributes
                {
                    Luid = luid,
                    Attributes = SePrivilegeEnabled
                }
            };
            if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero))
            {
                return false;
            }
            return Marshal.GetLastWin32Error() != ErrorNotAllAssigned;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (token != IntPtr.Zero)
            {
                CloseHandle(token);
            }
        }
    }

    private static void SetSystemTime(DateTime utc)
    {
        DateTime value = utc.ToUniversalTime();
        var st = new SystemTime
        {
            Year = (ushort)value.Year,
            Month = (ushort)value.Month,
            DayOfWeek = (ushort)value.DayOfWeek,
            Day = (ushort)value.Day,
            Hour = (ushort)value.Hour,
            Minute = (ushort)value.Minute,
            Second = (ushort)value.Second,
            Milliseconds = (ushort)value.Millisecond
        };
        if (!SetSystemTime(ref st))
        {
            int code = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                "Windows 错误 " + code + "：" + new Win32Exception(code).Message);
        }
    }

    public void Dispose()
    {
        lock (_stateSync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            StopAutoTimerLocked();
        }
    }
}
