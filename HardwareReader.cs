using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

namespace MiniMonitor;

/// <summary>
/// CPU / GPU 温度读取。
///
/// CPU 温度多路径回退（保证 Win10/11、Server 2016~2025 都能尽量读到）：
///   1) LibreHardwareMonitor（Package / Tctl / 核心温度，最精确）
///   2) WMI 性能计数器 Win32_PerfFormattedData_Counters_ThermalZoneInformation（Win10 1809+ 内置，摄氏度）
///   3) WMI ACPI 热区 MSAcpi_ThermalZoneTemperature（十分之一开尔文换算，老系统也有）
///
/// GPU 温度：
///   1) LibreHardwareMonitor（NVIDIA NVAPI / AMD ADL / Intel）
///   2) nvidia-smi（NVIDIA 卡后备，5 秒节流）
///
/// LHM 依赖内核驱动（WinRing0）：无管理员权限 / 杀软拦截 / Secure Boot 阻止 / 虚拟机
/// 都会导致 LHM 读不到，此时自动落到 WMI 后备，温度仍能显示（热区温度可能略低）。
/// </summary>
public sealed class HardwareReader : IDisposable
{
    private readonly Computer _computer;
    private IHardware? _cpu;
    private readonly List<IHardware> _gpus = new();
    private bool _available;
    private string _openError = "未初始化";

    // nvidia-smi 后备的节流状态
    private float? _smiTempCache;
    private DateTime _smiLastCheckUtc = DateTime.MinValue;
    private static readonly TimeSpan SmiInterval = TimeSpan.FromSeconds(5);

    // nvidia-smi 显存后备的节流缓存（显存使用量 2 秒刷新一次足够，避免频繁启动进程）
    private static double _gpuMemTotalCacheGB;
    private static double _gpuMemUsedCacheGB;
    private static DateTime _gpuMemCacheTimeUtc = DateTime.MinValue;
    private static readonly TimeSpan GpuMemCacheInterval = TimeSpan.FromSeconds(2);

    // Intel CPU 首选 Package 温度；AMD Ryzen 首选 Tctl/Tdie
    private static readonly string[] CpuPreferredSensors =
    {
        "CPU Package", "Package", "Tctl", "Tdie", "CPU Total"
    };

    // 独显/核显首选 GPU Core 温度
    private static readonly string[] GpuPreferredSensors =
    {
        "GPU Core", "GPU Temperature", "GPU", "Core"
    };

    /// <summary>诊断报告（OS 版本 / LHM 状态 / 硬件列表 / 各路温度读数），用于排查。</summary>
    public string DiagnosticReport { get; }

    public HardwareReader()
    {
        // IsMotherboardEnabled：AMD 平台 CPU 温度有时需经主板 SuperIO/SMBus 读取
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true
        };
        try
        {
            _computer.Open();
            EnumerateHardware();
            _available = true;
        }
        catch (Exception ex)
        {
            // 驱动加载失败（无管理员 / 杀软 / 签名 / 虚拟机）时不崩溃，
            // 温度自动走 WMI 后备，网速不受影响
            _available = false;
            _openError = ex.Message;
        }
        DiagnosticReport = BuildDiagnosticReport();
    }

    private void EnumerateHardware()
    {
        foreach (var hw in _computer.Hardware)
        {
            switch (hw.HardwareType)
            {
                case HardwareType.Cpu:
                    _cpu ??= hw;
                    break;
                case HardwareType.GpuNvidia:
                case HardwareType.GpuAmd:
                case HardwareType.GpuIntel:
                    _gpus.Add(hw);
                    break;
            }
        }
    }

    // ================= 对外读取入口（多路径回退） =================

    /// <summary>CPU 温度（摄氏度）。LHM → WMI 性能计数器 → WMI ACPI 热区。</summary>
    public float? CpuTemperature =>
        ReadCpuLhm() ?? ReadCpuFromWmiPerfCounter() ?? ReadCpuFromWmiAcpi();

    /// <summary>GPU 温度（摄氏度）。LHM → nvidia-smi。</summary>
    public float? GpuTemperature =>
        ReadGpuLhm() ?? ReadGpuFromNvidiaSmi();

    // ================= GPU 专属显存（GPUMEM，多卡总和） =================

    /// <summary>
    /// GPU 专属显存（Dedicated VRAM）总容量（GB）。
    /// 多张显卡时返回各卡专属显存之和。
    /// 主路径：注册表驱动信息 qwMemorySize（64 位，无 4GB 溢出问题）；
    /// 兜底 1：DXGI 枚举适配器 GetDesc1.DedicatedVideoMemory；
    /// 兜底 2：nvidia-smi（NVIDIA 卡）。
    /// </summary>
    public static double GpuMemoryTotalGB() => GpuMemoryInfo().TotalGB;

    /// <summary>
    /// GPU 专属显存使用情况：(总容量 GB, 已使用 GB)。
    /// 总量：注册表 qwMemorySize → DXGI → nvidia-smi；
    /// 使用量：WDDM 性能计数器 "GPU Adapter Memory"/"Dedicated Usage"（Win10 1809+ 内置，轻量）
    /// → nvidia-smi memory.used 兜底。
    /// 多卡时均自动求和；2 秒节流缓存。
    /// </summary>
    public static (double TotalGB, double UsedGB) GpuMemoryInfo()
    {
        // 命中缓存直接返回（显存使用变化不快，2 秒刷新足够）
        if (_gpuMemCacheTimeUtc != DateTime.MinValue
            && DateTime.UtcNow - _gpuMemCacheTimeUtc < GpuMemCacheInterval
            && _gpuMemTotalCacheGB > 0)
        {
            return (_gpuMemTotalCacheGB, _gpuMemUsedCacheGB);
        }

        double totalBytes = GpuMemoryFromRegistry();
        double usedBytes = GpuMemoryUsedFromPerfCounter();

        // nvidia-smi：使用量兜底 + 总量兜底（NVIDIA 卡，total/used 一次拿齐）
        if (usedBytes <= 0 || totalBytes <= 0)
        {
            (double totalMB, double usedMB) = GpuMemoryFromNvidiaSmi();
            if (totalMB > 0)
            {
                totalBytes = totalMB * 1024.0 * 1024.0;
            }
            if (usedMB > 0)
            {
                usedBytes = usedMB * 1024.0 * 1024.0;
            }
        }

        // 总量最终兜底：DXGI
        if (totalBytes <= 0)
        {
            totalBytes = GpuMemoryFromDxgi();
        }

        if (totalBytes > 0)
        {
            _gpuMemTotalCacheGB = totalBytes / 1073741824.0;
            _gpuMemUsedCacheGB = usedBytes / 1073741824.0;
            _gpuMemCacheTimeUtc = DateTime.UtcNow;
        }
        return (_gpuMemTotalCacheGB, _gpuMemUsedCacheGB);
    }

    /// <summary>
    /// WDDM 性能计数器读取 GPU 专用显存使用量（字节）。
    /// Win10 1809+ 系统内置 "GPU Adapter Memory" 类别，实例为各显卡适配器名，
    /// "Dedicated Usage" 即专属显存已用字节；多卡逐实例求和。
    /// </summary>
    private static double GpuMemoryUsedFromPerfCounter()
    {
        double total = 0;
        try
        {
            if (!PerformanceCounterCategory.Exists("GPU Adapter Memory"))
            {
                return 0;
            }
            var category = new PerformanceCounterCategory("GPU Adapter Memory");
            foreach (var instance in category.GetInstanceNames())
            {
                try
                {
                    using var counter = new PerformanceCounter(
                        "GPU Adapter Memory", "Dedicated Usage", instance, true);
                    float value = counter.NextValue();
                    if (value > 0)
                    {
                        total += value;
                    }
                }
                catch
                {
                    // 单个实例读取失败跳过，不影响其它显卡
                }
            }
        }
        catch
        {
            return 0;
        }
        return total;
    }

    /// <summary>
    /// 从显卡类注册表键读取专属显存：
    /// HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\000N
    /// 的 HardwareInformation.qwMemorySize（REG_QWORD / REG_BINARY，64 位字节数）。
    /// 注意：.NET RegistryKey.GetValue 对 REG_QWORD 返回 long（Int64）而非 ulong。
    /// NVIDIA / AMD / Intel 驱动都会写入该值，取所有有效显卡之和。
    /// </summary>
    private static double GpuMemoryFromRegistry()
    {
        double total = 0;
        try
        {
            const string displayClass =
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            using var root = Registry.LocalMachine.OpenSubKey(displayClass);
            if (root == null)
            {
                return 0;
            }
            foreach (var subName in root.GetSubKeyNames())
            {
                try
                {
                    using var card = root.OpenSubKey(subName);
                    if (card == null)
                    {
                        continue;
                    }
                    var raw = card.GetValue("HardwareInformation.qwMemorySize");
                    ulong size = raw switch
                    {
                        ulong q => q,
                        // REG_QWORD 在 .NET 中返回带符号 long
                        long q when q > 0 => (ulong)q,
                        byte[] bytes when bytes.Length >= 8 => BitConverter.ToUInt64(bytes, 0),
                        _ => 0UL
                    };
                    // 排除基础显示适配器/虚拟显卡等无专属显存的条目
                    if (size > 0)
                    {
                        total += size;
                    }
                }
                catch
                {
                    // 单个子项异常不影响其它显卡
                }
            }
        }
        catch
        {
            // 注册表不可读时回退 DXGI
        }
        return total;
    }

    /// <summary>DXGI 兜底：枚举所有适配器，累加 DedicatedVideoMemory（排除 0 显存的虚拟适配器）。</summary>
    private static double GpuMemoryFromDxgi()
    {
        double total = 0;
        try
        {
            // CreateDXGIFactory(REFIID riid, void** ppFactory)：必须显式传 riid，
            // 漏传会在 x64 下把随机寄存器值当输出指针写入，导致访问冲突崩溃
            Guid riid = IID_IDXGIFactory;
            if (CreateDXGIFactory(ref riid, out IntPtr pFactory) != 0
                || pFactory == IntPtr.Zero)
            {
                return 0;
            }

            IDXGIFactory factory;
            try
            {
                factory = (IDXGIFactory)Marshal.GetObjectForIUnknown(pFactory);
            }
            finally
            {
                // RCW 已接管引用计数，释放原始指针
                Marshal.Release(pFactory);
            }

            try
            {
                uint index = 0;
                while (factory.EnumAdapters(index, out IDXGIAdapter? adapter) == 0 && adapter != null)
                {
                    try
                    {
                        if (adapter.GetDesc1(out var desc) == 0 && desc.DedicatedVideoMemory > 0)
                        {
                            total += desc.DedicatedVideoMemory;
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(adapter);
                    }
                    index++;
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch
        {
            // DXGI 不可用时返回 0，界面显示 --G
        }
        return total;
    }

    /// <summary>
    /// nvidia-smi 兜底：查询所有 NVIDIA 卡显存 (总 MB, 已用 MB)，多卡逐行求和。
    /// 仅在前两条路径失败时启用；调用方 GpuMemoryInfo 已有 2 秒节流缓存。
    /// </summary>
    private static (double TotalMB, double UsedMB) GpuMemoryFromNvidiaSmi()
    {
        try
        {
            var psi = new ProcessStartInfo("nvidia-smi",
                "--query-gpu=memory.total,memory.used --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null)
            {
                return (0, 0);
            }
            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // 进程已退出
                }
            }

            double totalMB = 0;
            double usedMB = 0;
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                // 每行格式: "6144, 3072"（总 MB, 已用 MB）
                var parts = line.Split(',');
                if (parts.Length < 2)
                {
                    continue;
                }
                if (double.TryParse(parts[0].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double t) && t > 0)
                {
                    totalMB += t;
                }
                if (double.TryParse(parts[1].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double u) && u > 0)
                {
                    usedMB += u;
                }
            }
            return (totalMB, usedMB);
        }
        catch
        {
            // nvidia-smi 不存在或启动失败（非 NVIDIA 卡），返回 0
            return (0, 0);
        }
    }

    // IID_IDXGIFactory：7b7166ec-21c7-44ae-b603-76b60997d2de
    private static readonly Guid IID_IDXGIFactory = new("7b7166ec-21c7-44ae-b603-76b60997d2de");

    [DllImport("dxgi.dll", EntryPoint = "CreateDXGIFactory")]
    private static extern int CreateDXGIFactory(ref Guid riid, out IntPtr ppFactory);

    // IID_IDXGIFactory：7b7166ec-21c7-44ae-b603-76b60997d2de
    [ComImport, Guid("7b7166ec-21c7-44ae-b603-76b60997d2de"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory
    {
        [PreserveSig] int QueryInterface(ref Guid riid, out IntPtr ppvObject);
        [PreserveSig] uint AddRef();
        [PreserveSig] uint Release();
        [PreserveSig] int SetPrivateData(ref Guid name, uint dataSize, IntPtr data);
        [PreserveSig] int SetPrivateDataInterface(ref Guid name, IntPtr pUnknown);
        [PreserveSig] int GetPrivateData(ref Guid name, ref uint dataSize, IntPtr data);
        [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);
        [PreserveSig] int EnumAdapters(uint adapter, out IDXGIAdapter ppAdapter);
    }

    // IID_IDXGIAdapter1：29038f61-3831-4f68-9169-84fe51a316ba
    // GetDesc1 属于 IDXGIAdapter1，声明 GUID 用 Adapter1 使 RCW 正确 QI
    [ComImport, Guid("29038f61-3831-4f68-9169-84fe51a316ba"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter
    {
        [PreserveSig] int QueryInterface(ref Guid riid, out IntPtr ppvObject);
        [PreserveSig] uint AddRef();
        [PreserveSig] uint Release();
        [PreserveSig] int SetPrivateData(ref Guid name, uint dataSize, IntPtr data);
        [PreserveSig] int SetPrivateDataInterface(ref Guid name, IntPtr pUnknown);
        [PreserveSig] int GetPrivateData(ref Guid name, ref uint dataSize, IntPtr data);
        [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);
        [PreserveSig] int EnumOutputs(uint output, out IntPtr ppOutput);
        [PreserveSig] int GetDesc(out DXGI_ADAPTER_DESC pDesc);
        [PreserveSig] int CheckInterfaceSupport(ref Guid interfaceName, out long pUMDVersion);
        [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 pDesc);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public LUID AdapterLuid;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public LUID AdapterLuid;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    // ================= LHM 路径 =================

    private float? ReadCpuLhm()
    {
        try
        {
            return _available ? ReadTemperature(_cpu, CpuPreferredSensors) : null;
        }
        catch
        {
            return null;
        }
    }

    private float? ReadGpuLhm()
    {
        if (!_available)
        {
            return null;
        }
        try
        {
            foreach (var gpu in _gpus)
            {
                var t = ReadTemperature(gpu, GpuPreferredSensors);
                if (t.HasValue)
                {
                    return t;
                }
            }
        }
        catch
        {
            // 单次读取异常回落到 --，下个采样周期自动重试
        }
        return null;
    }

    private static float? ReadTemperature(IHardware? hardware, string[] preferredNames)
    {
        if (hardware == null)
        {
            return null;
        }

        hardware.Update();

        var temps = hardware.Sensors
            .Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue)
            .ToList();

        // 部分主板/显卡的传感器挂在子硬件下
        foreach (var sub in hardware.SubHardware)
        {
            sub.Update();
            temps.AddRange(sub.Sensors
                .Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue));
        }

        if (temps.Count == 0)
        {
            return null;
        }

        // 1. 按优先名称匹配
        foreach (var key in preferredNames)
        {
            var hit = temps.FirstOrDefault(s =>
                s.Name.Contains(key, StringComparison.OrdinalIgnoreCase));
            if (hit?.Value.HasValue == true)
            {
                return hit.Value.Value;
            }
        }

        // 2. 取各核心温度的最大值（避免只报某个低温核心）
        var coreTemps = temps
            .Where(s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (coreTemps.Count > 0)
        {
            return coreTemps.Max(s => s.Value!.Value);
        }

        // 3. 兜底：任意温度传感器
        return temps[0].Value!.Value;
    }

    // ================= WMI 后备路径（Windows 10/11、Server 全系内置，无需驱动） =================

    /// <summary>
    /// 路径 2：Win32_PerfFormattedData_Counters_ThermalZoneInformation 性能计数器。
    /// Windows 10 1809+ 提供，Temperature 属性直接是摄氏度整数。
    /// </summary>
    private static float? ReadCpuFromWmiPerfCounter()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\cimv2",
                "SELECT Name, Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
            float? best = null;
            foreach (var obj in searcher.Get())
            {
                using (obj)
                {
                    var raw = obj.Properties["Temperature"]?.Value;
                    if (raw == null)
                    {
                        continue;
                    }
                    float val;
                    if (raw is uint u)
                    {
                        val = u;
                    }
                    else if (raw is int i)
                    {
                        val = i;
                    }
                    else if (!float.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out val))
                    {
                        continue;
                    }
                    if (val is >= -50 and <= 150)
                    {
                        best = Math.Max(best ?? val, val);
                    }
                }
            }
            return best;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 路径 3：MSAcpi_ThermalZoneTemperature（root\WMI）。
    /// CurrentTemperature 单位为十分之一开尔文：摄氏度 = 值/10 - 273.15。
    /// </summary>
    private static float? ReadCpuFromWmiAcpi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\WMI",
                "SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            float? best = null;
            foreach (var obj in searcher.Get())
            {
                using (obj)
                {
                    var raw = obj.Properties["CurrentTemperature"]?.Value;
                    if (raw == null)
                    {
                        continue;
                    }
                    double kelvin10;
                    try
                    {
                        kelvin10 = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                        continue;
                    }
                    if (kelvin10 <= 0)
                    {
                        continue;
                    }
                    var c = (kelvin10 / 10.0) - 273.15;
                    if (c is >= -50 and <= 150)
                    {
                        best = Math.Max(best ?? (float)c, (float)c);
                    }
                }
            }
            return best;
        }
        catch
        {
            return null;
        }
    }

    // ================= nvidia-smi 后备 =================

    /// <summary>
    /// NVIDIA 显卡后备温度源。进程调用开销较大，5 秒节流并缓存结果。
    /// </summary>
    private float? ReadGpuFromNvidiaSmi()
    {
        var now = DateTime.UtcNow;
        if (now - _smiLastCheckUtc < SmiInterval)
        {
            return _smiTempCache;
        }
        _smiLastCheckUtc = now;

        float? result = null;
        try
        {
            var psi = new ProcessStartInfo("nvidia-smi",
                "--query-gpu=temperature.gpu --format=csv,noheader,nounits")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                var output = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(3000))
                {
                    p.Kill();
                }
                var line = output.Trim();
                if (float.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out var t)
                    && t is >= -50 and <= 150)
                {
                    result = t;
                }
            }
        }
        catch
        {
            // 无 nvidia-smi（非 NVIDIA 环境或未安装驱动）或调用失败，忽略
        }
        _smiTempCache = result;
        return result;
    }

    // ================= 诊断报告 =================

    private string BuildDiagnosticReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("MiniMonitor 温度诊断");
        sb.AppendLine(new string('-', 32));
        sb.AppendLine("操作系统: " + GetOsVersion());
        sb.AppendLine("LibreHardwareMonitor 初始化: " + (_available ? "成功" : "失败"));
        if (!_available)
        {
            sb.AppendLine("  失败原因: " + _openError);
            sb.AppendLine("  说明: 无管理员权限/杀软拦截/Secure Boot/VBS/虚拟机都会导致 LHM 内核驱动无法加载");
        }
        sb.AppendLine("检测到的硬件:");
        sb.AppendLine("  CPU: " + (_cpu?.Name ?? "未检测到"));
        if (_gpus.Count == 0)
        {
            sb.AppendLine("  GPU: 未检测到");
        }
        else
        {
            foreach (var g in _gpus)
            {
                sb.AppendLine("  GPU: " + g.Name + " [" + g.HardwareType + "]");
            }
        }
        sb.AppendLine("当前温度读数（各路来源）:");
        sb.AppendLine("  LHM CPU:       " + Format(ReadCpuLhm()) + "   ← 最精确（Package/Tctl）");
        sb.AppendLine("  WMI 性能计数:  " + Format(ReadCpuFromWmiPerfCounter()) + "   ← Win10 1809+ 内置");
        sb.AppendLine("  WMI ACPI 热区: " + Format(ReadCpuFromWmiAcpi()) + "   ← 老系统后备，可能偏低");
        sb.AppendLine("  LHM GPU:       " + Format(ReadGpuLhm()));
        sb.AppendLine("  nvidia-smi:    " + Format(ReadGpuFromNvidiaSmi()) + "   ← NVIDIA 卡后备");
        sb.AppendLine("GPU 专属显存（GPUMEM）诊断:");
        sb.Append(BuildGpuMemDiagnostic());
        return sb.ToString();
    }

    /// <summary>逐路径记录 GPU 显存读取过程，便于定位 --G 的原因。</summary>
    private static string BuildGpuMemDiagnostic()
    {
        var sb = new StringBuilder();
        try
        {
            // 1) 注册表路径
            double regTotal = 0;
            const string displayClass =
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            using (var root = Registry.LocalMachine.OpenSubKey(displayClass))
            {
                if (root == null)
                {
                    sb.AppendLine("  注册表: 显卡类键不存在 " + displayClass);
                }
                else
                {
                    foreach (var subName in root.GetSubKeyNames())
                    {
                        try
                        {
                            using var card = root.OpenSubKey(subName);
                            if (card == null)
                            {
                                continue;
                            }
                            var raw = card.GetValue("HardwareInformation.qwMemorySize");
                            string shown;
                            if (raw == null)
                            {
                                shown = "（无 qwMemorySize）";
                            }
                            else
                            {
                                ulong size = raw switch
                                {
                                    ulong q => q,
                                    long q when q > 0 => (ulong)q,
                                    byte[] bytes when bytes.Length >= 8 =>
                                        BitConverter.ToUInt64(bytes, 0),
                                    _ => 0UL
                                };
                                if (size > 0)
                                {
                                    regTotal += size;
                                }
                                shown = raw switch
                                {
                                    ulong q => $"REG_QWORD(ulong) {q}",
                                    long q => $"REG_QWORD(long) {q}",
                                    byte[] bytes => $"REG_BINARY {bytes.Length}B -> {size}",
                                    _ => $"类型 {raw.GetType().Name} 值 {raw}"
                                };
                            }
                            sb.AppendLine($"  注册表 {displayClass}\\{subName}: {shown}");
                        }
                        catch (Exception ex)
                        {
                            sb.AppendLine($"  注册表 {subName}: 异常 {ex.Message}");
                        }
                    }
                    sb.AppendLine($"  注册表路径合计: {(regTotal / 1073741824.0):0.0} GB");
                }
            }

            // 2) DXGI 路径
            double dxgiTotal = 0;
            try
            {
                Guid riid = IID_IDXGIFactory;
                if (CreateDXGIFactory(ref riid, out IntPtr pFactory) == 0 && pFactory != IntPtr.Zero)
                {
                    IDXGIFactory factory;
                    try
                    {
                        factory = (IDXGIFactory)Marshal.GetObjectForIUnknown(pFactory);
                    }
                    finally
                    {
                        Marshal.Release(pFactory);
                    }
                    try
                    {
                        uint index = 0;
                        while (factory.EnumAdapters(index, out IDXGIAdapter? adapter) == 0
                               && adapter != null)
                        {
                            try
                            {
                                if (adapter.GetDesc1(out var desc) == 0)
                                {
                                    sb.AppendLine(
                                        $"  DXGI 适配器[{index}]: Vendor 0x{desc.VendorId:X4} " +
                                        $"Device 0x{desc.DeviceId:X4} 显存 {desc.DedicatedVideoMemory} B");
                                    if (desc.DedicatedVideoMemory > 0)
                                    {
                                        dxgiTotal += desc.DedicatedVideoMemory;
                                    }
                                }
                                else
                                {
                                    sb.AppendLine($"  DXGI 适配器[{index}]: GetDesc1 失败");
                                }
                            }
                            finally
                            {
                                Marshal.ReleaseComObject(adapter);
                            }
                            index++;
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(factory);
                    }
                    sb.AppendLine($"  DXGI 路径合计: {(dxgiTotal / 1073741824.0):0.0} GB");
                }
                else
                {
                    sb.AppendLine("  DXGI: CreateDXGIFactory 失败或返回空");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("  DXGI: 异常 " + ex.Message);
            }

            // 3) 使用量性能计数器路径
            double usedPerf = GpuMemoryUsedFromPerfCounter();
            sb.AppendLine($"  使用量[性能计数器]: {usedPerf:0} B = {(usedPerf / 1073741824.0):0.00} GB" +
                          (usedPerf <= 0 ? "（无 GPU Adapter Memory 计数器）" : ""));

            // 4) nvidia-smi 路径
            (double smiTotalMB, double smiUsedMB) = GpuMemoryFromNvidiaSmi();
            sb.AppendLine($"  nvidia-smi: 总 {smiTotalMB:0} MB = {(smiTotalMB / 1024.0):0.0} GB, " +
                          $"已用 {smiUsedMB:0} MB = {(smiUsedMB / 1024.0):0.0} GB" +
                          (smiTotalMB <= 0 ? "（未安装 NVIDIA 驱动或启动失败）" : ""));

            // 5) 最终显示值
            (double totalGB, double usedGB) = GpuMemoryInfo();
            double percent = totalGB > 0 ? usedGB / totalGB * 100.0 : 0;
            sb.AppendLine($"  → 最终 GPUMEM 显示: {usedGB:0.0}/{totalGB:0.0}G {Math.Round(percent):0}%");
        }
        catch (Exception ex)
        {
            sb.AppendLine("  GPUMEM 诊断异常: " + ex.Message);
        }
        return sb.ToString();
    }

    private static string Format(float? v) =>
        v.HasValue ? v.Value.ToString("0.0", CultureInfo.InvariantCulture) + " ℃" : "--";

    private static string GetOsVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var name = key?.GetValue("ProductName")?.ToString() ?? "Windows";
            var ver = key?.GetValue("DisplayVersion")?.ToString();
            var build = key?.GetValue("CurrentBuildNumber")?.ToString();
            return string.IsNullOrEmpty(ver)
                ? $"{name} (Build {build})"
                : $"{name} {ver} (Build {build})";
        }
        catch
        {
            return Environment.OSVersion.VersionString;
        }
    }

    public void Dispose()
    {
        try
        {
            _computer.Close();
        }
        catch
        {
            // 关闭驱动失败无需处理
        }
    }
}
