using System.Runtime.InteropServices;

namespace MiniMonitor;

/// <summary>本机蓝牙适配器信息。</summary>
public sealed class BluetoothRadioRecord
{
    public string Name { get; init; } = "";
    public string Address { get; init; } = "";
    public ushort Manufacturer { get; init; }
}

/// <summary>一次蓝牙询问扫描发现的设备。</summary>
public sealed class BluetoothDeviceRecord
{
    public string Address { get; set; } = "";
    public string Name { get; set; } = "";
    public int ClassOfDevice { get; set; }
    public bool Connected { get; set; }
    public bool Remembered { get; set; }
    public bool Authenticated { get; set; }
    public DateTime LastSeenFromDevice { get; set; }
    public DateTime LastUsedFromDevice { get; set; }
    public string RadioName { get; set; } = "";

    /// <summary>本次 inquiry 是否确实发现该设备（已配对但不在范围内的设备为 false）。</summary>
    public bool SeenInThisScan { get; set; }
}

/// <summary>一次蓝牙扫描的结果，包含本机适配器数量与发现设备。</summary>
public sealed class BluetoothScanResult
{
    public int RadioCount { get; set; }
    public List<BluetoothDeviceRecord> Devices { get; } = new();
}

/// <summary>
/// 使用 Windows 自带蓝牙栈（bthprops.cpl）枚举适配器并发起设备询问，
/// 对标 NirSoft BluetoothView 的扫描方式：经典蓝牙 inquiry + 已配对 / 已记忆设备。
/// 仅支持 Microsoft 蓝牙栈；未开启蓝牙或没有适配器时返回空结果。
/// </summary>
public static class BluetoothReader
{
    private const int BluetoothMaxNameSize = 248;

    [StructLayout(LayoutKind.Sequential)]
    private struct BluetoothFindRadioParams
    {
        public int dwSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BluetoothRadioInfo
    {
        public int dwSize;
        public ulong address;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = BluetoothMaxNameSize)]
        public string szName;

        public uint ulClassofDevice;
        public ushort lmpSubversion;
        public ushort manufacturer;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BluetoothDeviceInfo
    {
        public int dwSize;
        public ulong Address;
        public uint ulClassofDevice;

        [MarshalAs(UnmanagedType.Bool)]
        public bool fConnected;

        [MarshalAs(UnmanagedType.Bool)]
        public bool fRemembered;

        [MarshalAs(UnmanagedType.Bool)]
        public bool fAuthenticated;

        public SystemTime stLastSeen;
        public SystemTime stLastUsed;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = BluetoothMaxNameSize)]
        public string szName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BluetoothDeviceSearchParams
    {
        public int dwSize;

        [MarshalAs(UnmanagedType.Bool)]
        public bool fReturnAuthenticated;

        [MarshalAs(UnmanagedType.Bool)]
        public bool fReturnRemembered;

        [MarshalAs(UnmanagedType.Bool)]
        public bool fReturnUnknown;

        [MarshalAs(UnmanagedType.Bool)]
        public bool fReturnConnected;

        [MarshalAs(UnmanagedType.Bool)]
        public bool fIssueInquiry;

        public byte cTimeoutMultiplier;
        public IntPtr hRadio;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort wYear;
        public ushort wMonth;
        public ushort wDayOfWeek;
        public ushort wDay;
        public ushort wHour;
        public ushort wMinute;
        public ushort wSecond;
        public ushort wMilliseconds;
    }

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstRadio(
        ref BluetoothFindRadioParams pbtfrp, out IntPtr phRadio);

    [DllImport("bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextRadio(IntPtr hFind, out IntPtr phRadio);

    [DllImport("bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindRadioClose(IntPtr hFind);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern uint BluetoothGetRadioInfo(
        IntPtr hRadio, ref BluetoothRadioInfo pRadioInfo);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstDevice(
        ref BluetoothDeviceSearchParams pbtsp, ref BluetoothDeviceInfo pbtdi);

    [DllImport("bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextDevice(
        IntPtr hFind, ref BluetoothDeviceInfo pbtdi);

    [DllImport("bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindDeviceClose(IntPtr hFind);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>将 Win32 蓝牙地址转换为 AA:BB:CC:DD:EE:FF 形式。</summary>
    public static string FormatAddress(ulong value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        return string.Join(":", new[]
        {
            bytes[5], bytes[4], bytes[3], bytes[2], bytes[1], bytes[0]
        }.Select(item => item.ToString("X2")));
    }

    /// <summary>枚举本机蓝牙适配器（不发起设备询问）。</summary>
    public static List<BluetoothRadioRecord> GetRadios()
    {
        var radios = new List<BluetoothRadioRecord>();
        if (!IsSupported)
        {
            return radios;
        }
        var parameters = new BluetoothFindRadioParams
        {
            dwSize = Marshal.SizeOf<BluetoothFindRadioParams>()
        };
        IntPtr find;
        try
        {
            find = BluetoothFindFirstRadio(ref parameters, out IntPtr radio);
            if (find == IntPtr.Zero)
            {
                return radios;
            }
            try
            {
                while (true)
                {
                    try
                    {
                        radios.Add(ReadRadio(radio));
                    }
                    finally
                    {
                        CloseHandle(radio);
                    }
                    if (!BluetoothFindNextRadio(find, out radio))
                    {
                        break;
                    }
                }
            }
            finally
            {
                BluetoothFindRadioClose(find);
            }
        }
        catch
        {
            // 蓝牙未开启 / 驱动不可用时按无适配器处理。
        }
        return radios;
    }

    /// <summary>
    /// 扫描蓝牙设备。radioAddress 为空表示扫描全部适配器。
    /// timeoutMultiplier 为 inquiry 超时倍数（1.28 秒 × 倍数）。
    /// </summary>
    public static BluetoothScanResult Scan(string? radioAddress,
        byte timeoutMultiplier, CancellationToken token)
    {
        var result = new BluetoothScanResult();
        if (!IsSupported)
        {
            return result;
        }
        var parameters = new BluetoothFindRadioParams
        {
            dwSize = Marshal.SizeOf<BluetoothFindRadioParams>()
        };
        IntPtr find;
        IntPtr radio = IntPtr.Zero;
        try
        {
            find = BluetoothFindFirstRadio(ref parameters, out radio);
        }
        catch
        {
            return result;
        }
        if (find == IntPtr.Zero)
        {
            return result;
        }
        try
        {
            while (true)
            {
                try
                {
                    result.RadioCount++;
                    var radioInfo = ReadRadio(radio);
                    bool selected = string.IsNullOrWhiteSpace(radioAddress)
                        || string.Equals(radioInfo.Address, radioAddress,
                            StringComparison.OrdinalIgnoreCase);
                    if (selected && !token.IsCancellationRequested)
                    {
                        ScanRadio(radio, radioInfo.Name, timeoutMultiplier, token, result);
                    }
                }
                catch (OperationCanceledException)
                {
                    return result;
                }
                catch
                {
                    // 单个适配器失败不影响其它适配器。
                }
                finally
                {
                    CloseHandle(radio);
                }
                if (token.IsCancellationRequested)
                {
                    break;
                }
                if (!BluetoothFindNextRadio(find, out radio))
                {
                    break;
                }
            }
        }
        finally
        {
            BluetoothFindRadioClose(find);
        }
        return result;
    }

    private static BluetoothRadioRecord ReadRadio(IntPtr radio)
    {
        var info = new BluetoothRadioInfo
        {
            dwSize = Marshal.SizeOf<BluetoothRadioInfo>(),
            szName = ""
        };
        uint result = BluetoothGetRadioInfo(radio, ref info);
        string address = FormatAddress(info.address);
        string name = result == 0 && !string.IsNullOrWhiteSpace(info.szName)
            ? info.szName.Trim()
            : "蓝牙适配器 " + address;
        return new BluetoothRadioRecord
        {
            Name = name,
            Address = address,
            Manufacturer = info.manufacturer
        };
    }

    private static void ScanRadio(IntPtr radio, string radioName,
        byte timeoutMultiplier, CancellationToken token, BluetoothScanResult result)
    {
        // 先快速读取本地缓存中的“上次看到时间”，再发起 inquiry；
        // 询问后时间戳变新，说明设备本次确实在范围内（不依赖时区判断）。
        var cachedLastSeen = SnapshotCachedDevices(radio, token);
        if (token.IsCancellationRequested)
        {
            return;
        }
        var search = new BluetoothDeviceSearchParams
        {
            dwSize = Marshal.SizeOf<BluetoothDeviceSearchParams>(),
            fReturnAuthenticated = true,
            fReturnRemembered = true,
            fReturnUnknown = true,
            fReturnConnected = true,
            fIssueInquiry = true,
            cTimeoutMultiplier = timeoutMultiplier,
            hRadio = radio
        };
        var info = new BluetoothDeviceInfo
        {
            dwSize = Marshal.SizeOf<BluetoothDeviceInfo>(),
            szName = ""
        };
        IntPtr find;
        try
        {
            find = BluetoothFindFirstDevice(ref search, ref info);
        }
        catch
        {
            return;
        }
        if (find == IntPtr.Zero)
        {
            return;
        }
        try
        {
            while (true)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }
                AddDevice(result, info, radioName, cachedLastSeen);
                info = new BluetoothDeviceInfo
                {
                    dwSize = Marshal.SizeOf<BluetoothDeviceInfo>(),
                    szName = ""
                };
                if (!BluetoothFindNextDevice(find, ref info))
                {
                    break;
                }
            }
        }
        finally
        {
            BluetoothFindDeviceClose(find);
        }
    }

    /// <summary>读取蓝牙栈缓存中设备的“上次看到时间”（不发起询问）。</summary>
    private static Dictionary<string, DateTime> SnapshotCachedDevices(
        IntPtr radio, CancellationToken token)
    {
        var map = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        var search = new BluetoothDeviceSearchParams
        {
            dwSize = Marshal.SizeOf<BluetoothDeviceSearchParams>(),
            fReturnAuthenticated = true,
            fReturnRemembered = true,
            fReturnUnknown = true,
            fReturnConnected = true,
            fIssueInquiry = false,
            cTimeoutMultiplier = 1,
            hRadio = radio
        };
        var info = new BluetoothDeviceInfo
        {
            dwSize = Marshal.SizeOf<BluetoothDeviceInfo>(),
            szName = ""
        };
        IntPtr find;
        try
        {
            find = BluetoothFindFirstDevice(ref search, ref info);
        }
        catch
        {
            return map;
        }
        if (find == IntPtr.Zero)
        {
            return map;
        }
        try
        {
            while (true)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }
                string address = FormatAddress(info.Address);
                if (address != "00:00:00:00:00:00")
                {
                    map[address] = ToDateTime(info.stLastSeen);
                }
                info = new BluetoothDeviceInfo
                {
                    dwSize = Marshal.SizeOf<BluetoothDeviceInfo>(),
                    szName = ""
                };
                if (!BluetoothFindNextDevice(find, ref info))
                {
                    break;
                }
            }
        }
        finally
        {
            BluetoothFindDeviceClose(find);
        }
        return map;
    }

    private static void AddDevice(BluetoothScanResult result,
        BluetoothDeviceInfo info, string radioName,
        Dictionary<string, DateTime> cachedLastSeen)
    {
        string address = FormatAddress(info.Address);
        if (address == "00:00:00:00:00:00")
        {
            return;
        }
        DateTime lastSeen = ToDateTime(info.stLastSeen);
        // 已配对但不在范围内的设备也会被 API 返回：
        // 询问前后“上次看到时间”没有变新，说明本次没有真正发现它。
        bool seenInThisScan = info.fConnected
            || !cachedLastSeen.TryGetValue(address, out DateTime cached)
            || lastSeen > cached;
        var existing = result.Devices.FirstOrDefault(device =>
            string.Equals(device.Address, address, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Connected |= info.fConnected;
            existing.Remembered |= info.fRemembered;
            existing.Authenticated |= info.fAuthenticated;
            existing.SeenInThisScan |= seenInThisScan;
            if (lastSeen > existing.LastSeenFromDevice)
            {
                existing.LastSeenFromDevice = lastSeen;
            }
            if (string.IsNullOrWhiteSpace(existing.Name) && !string.IsNullOrWhiteSpace(info.szName))
            {
                existing.Name = info.szName.Trim();
            }
            return;
        }
        result.Devices.Add(new BluetoothDeviceRecord
        {
            Address = address,
            Name = (info.szName ?? "").Trim(),
            ClassOfDevice = unchecked((int)info.ulClassofDevice),
            Connected = info.fConnected,
            Remembered = info.fRemembered,
            Authenticated = info.fAuthenticated,
            LastSeenFromDevice = lastSeen,
            LastUsedFromDevice = ToDateTime(info.stLastUsed),
            RadioName = radioName,
            SeenInThisScan = seenInThisScan
        });
    }

    private static DateTime ToDateTime(SystemTime value)
    {
        if (value.wYear < 1601)
        {
            return DateTime.MinValue;
        }
        try
        {
            return new DateTime(value.wYear, value.wMonth, value.wDay,
                value.wHour, value.wMinute, value.wSecond, DateTimeKind.Local);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>解析 24 位 Class of Device，返回主设备类型与次设备类型。</summary>
    public static (string Major, string Minor) DecodeClassOfDevice(int classOfDevice)
    {
        int major = (classOfDevice >> 8) & 0x1F;
        int minor = (classOfDevice >> 2) & 0x3F;
        string majorName = major switch
        {
            0x00 => "杂项设备",
            0x01 => "电脑",
            0x02 => "手机",
            0x03 => "局域网 / 网络接入点",
            0x04 => "音频 / 视频",
            0x05 => "外设",
            0x06 => "成像设备",
            0x07 => "穿戴设备",
            0x08 => "玩具",
            0x09 => "健康设备",
            0x1F => "未分类",
            _ => "未知类型（0x" + major.ToString("X2") + "）"
        };
        string minorName = major switch
        {
            0x01 => minor switch
            {
                0 => "未分类",
                1 => "台式工作站",
                2 => "服务器",
                3 => "笔记本电脑",
                4 => "手持电脑 / PDA",
                5 => "掌上电脑 / PDA",
                6 => "穿戴式电脑",
                7 => "平板电脑",
                _ => "子类 " + minor
            },
            0x02 => minor switch
            {
                0 => "未分类",
                1 => "蜂窝电话",
                2 => "无绳电话",
                3 => "智能手机",
                4 => "有线调制解调器 / 语音网关",
                5 => "通用 ISDN 接入",
                _ => "子类 " + minor
            },
            0x03 => minor == 0 ? "未分类" : "网络使用率类别 " + minor,
            0x04 => minor switch
            {
                0 => "未分类",
                1 => "穿戴式耳机",
                2 => "免提设备",
                4 => "麦克风",
                5 => "扬声器",
                6 => "耳机",
                7 => "便携音频设备",
                8 => "车载音频",
                9 => "机顶盒",
                10 => "HiFi 音频设备",
                11 => "VCR",
                12 => "摄像机",
                13 => "便携摄录机",
                14 => "视频监视器",
                15 => "视频显示与扬声器",
                16 => "视频会议",
                18 => "游戏 / 玩具",
                _ => "子类 " + minor
            },
            0x05 => minor switch
            {
                0 => "未分类",
                1 => "数字化仪",
                2 => "传感设备",
                4 => "摇杆",
                8 => "游戏手柄",
                12 => "遥控器",
                16 => "键盘",
                32 => "指点设备",
                48 => "键盘 / 指点设备组合",
                _ => "子类 " + minor
            },
            0x06 => minor switch
            {
                0 => "未分类",
                4 => "显示器",
                8 => "相机",
                16 => "扫描仪",
                32 => "打印机",
                _ => "子类 " + minor
            },
            0x07 => minor switch
            {
                0 => "未分类",
                1 => "手表",
                2 => "寻呼机",
                4 => "夹克",
                8 => "头盔",
                16 => "眼镜",
                _ => "子类 " + minor
            },
            0x08 => minor switch
            {
                0 => "未分类",
                1 => "机器人",
                2 => "车辆",
                4 => "玩偶 / 手办",
                8 => "控制器",
                16 => "游戏",
                _ => "子类 " + minor
            },
            0x09 => minor switch
            {
                0 => "未分类",
                1 => "血压计",
                2 => "体温计",
                4 => "体重秤",
                8 => "血糖仪",
                16 => "脉搏血氧仪",
                32 => "心率 / 脉搏监测器",
                _ => "子类 " + minor
            },
            _ => minor == 0 ? "未分类" : "子类 " + minor
        };
        return (majorName, minorName);
    }
}
