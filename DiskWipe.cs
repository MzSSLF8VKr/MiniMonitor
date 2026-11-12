using System.ComponentModel;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MiniMonitor;

public enum WipeAlgorithm
{
    Random = 0,
    Zero = 1,
    Dod3 = 2,
    Gutmann = 3
}

public enum WipeMediaKind
{
    Unknown = 0,
    Hdd = 1,
    Ssd = 2
}

/// <summary>一块可覆写的物理磁盘信息（系统盘不会出现在此列表中）。</summary>
public sealed record WipeDiskInfo(
    int Number,
    string Model,
    string Serial,
    string BusName,
    long Size,
    int LogicalSector,
    int PhysicalSector,
    bool Removable,
    WipeMediaKind Media,
    int Partitions);

public sealed record WipeEnumerationResult(
    IReadOnlyList<WipeDiskInfo> Disks,
    int ExcludedSystemDisks,
    string? Error);

public sealed record WipeProgressSnapshot(
    WipeAlgorithm Algorithm,
    bool EncryptionPrePass,
    int TotalPasses,
    int CurrentPass,
    int CompletedPasses,
    long DiskBytes,
    long TotalBytes,
    long CompletedBytes,
    long ElapsedMs,
    bool Finished,
    bool Success,
    bool Cancelled,
    string ErrorMessage);

/// <summary>
/// 物理磁盘安全覆写引擎。
/// 所有写入均针对原始物理磁盘；系统盘在扫描、准备、锁定卷后都会再次校验并拒绝。
/// 每轮任务会先执行一次 AES-256 随机密钥加密预覆盖，再按所选算法覆写。
/// </summary>
public static class DiskWipeEngine
{
    // ---------- Win32 常量 ----------
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;

    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const uint IoctlStorageGetDeviceNumber = 0x002D1080;
    private const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;
    private const uint IoctlDiskGetLengthInfo = 0x0007405C;
    private const uint IoctlDiskGetDriveGeometryEx = 0x000700A0;
    private const uint IoctlDiskGetDriveLayoutEx = 0x00070050;
    private const uint IoctlDiskGetDriveLayout = 0x0007400C;
    private const uint IoctlDiskIsWritable = 0x00070024;
    internal const uint IoctlDiskUpdateProperties = 0x00070100;
    private const uint FsctlLockVolume = 0x00090018;
    private const uint FsctlDismountVolume = 0x00090020;

    private const int StorageDeviceProperty = 0;
    private const int StorageAccessAlignmentProperty = 6;
    private const int StorageDeviceSeekPenaltyProperty = 7;

    private const int ErrorAccessDenied = 5;
    private const int ErrorWriteProtect = 19;
    private const int ErrorNoMoreFiles = 18;
    private const int ErrorInvalidFunction = 1;
    private const int ErrorNotSupported = 50;
    private const int ErrorMoreData = 234;
    private const int ErrorInsufficientBuffer = 122;
    internal const int FileBegin = 0;

    internal const uint EsContinuous = 0x80000000;
    internal const uint EsSystemRequired = 0x00000001;

    internal const int IoChunkBytes = 4 * 1024 * 1024;

    // ---------- P/Invoke ----------
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        byte[]? inBuffer,
        int inBufferSize,
        byte[]? outBuffer,
        int outBufferSize,
        out int bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadFile(
        SafeFileHandle file,
        IntPtr buffer,
        int bytesToRead,
        out int bytesRead,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WriteFile(
        SafeFileHandle file,
        IntPtr buffer,
        int bytesToWrite,
        out int bytesWritten,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetFilePointerEx(
        SafeFileHandle file,
        long distance,
        IntPtr newFilePointer,
        uint moveMethod);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FlushFileBuffers(SafeFileHandle file);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstVolumeW(StringBuilder volumeName, uint bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextVolumeW(
        IntPtr findVolume,
        StringBuilder volumeName,
        uint bufferLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindVolumeClose(IntPtr findVolume);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetWindowsDirectoryW(StringBuilder buffer, uint size);

    [DllImport("kernel32.dll")]
    internal static extern uint SetThreadExecutionState(uint flags);

    private static readonly byte[][] GutmannPatterns =
    {
        new byte[] { 0x55 },
        new byte[] { 0xAA },
        new byte[] { 0x92, 0x49, 0x24 },
        new byte[] { 0x49, 0x24, 0x92 },
        new byte[] { 0x24, 0x92, 0x49 },
        new byte[] { 0x00 },
        new byte[] { 0x11 },
        new byte[] { 0x22 },
        new byte[] { 0x33 },
        new byte[] { 0x44 },
        new byte[] { 0x55 },
        new byte[] { 0x66 },
        new byte[] { 0x77 },
        new byte[] { 0x88 },
        new byte[] { 0x99 },
        new byte[] { 0xAA },
        new byte[] { 0xBB },
        new byte[] { 0xCC },
        new byte[] { 0xDD },
        new byte[] { 0xEE },
        new byte[] { 0xFF },
        new byte[] { 0x92, 0x49, 0x24 },
        new byte[] { 0x49, 0x24, 0x92 },
        new byte[] { 0x24, 0x92, 0x49 },
        new byte[] { 0x6D, 0xB6, 0xDB },
        new byte[] { 0xB6, 0xDB, 0x6D },
        new byte[] { 0xDB, 0x6D, 0xB6 }
    };

    public static int AlgorithmPasses(WipeAlgorithm algorithm)
    {
        return algorithm switch
        {
            WipeAlgorithm.Random => 1,
            WipeAlgorithm.Zero => 1,
            WipeAlgorithm.Dod3 => 3,
            WipeAlgorithm.Gutmann => 35,
            _ => 1
        };
    }

    public static string AlgorithmName(WipeAlgorithm algorithm)
    {
        return algorithm switch
        {
            WipeAlgorithm.Random => "随机数覆写",
            WipeAlgorithm.Zero => "全零覆写",
            WipeAlgorithm.Dod3 => "DoD 5220.22-M",
            WipeAlgorithm.Gutmann => "Gutmann",
            _ => "随机数覆写"
        };
    }

    public static string AlgorithmDetail(WipeAlgorithm algorithm)
    {
        return algorithm switch
        {
            WipeAlgorithm.Random => "每个扇区写入加密安全随机数据，1 遍完成，速度较快。",
            WipeAlgorithm.Zero => "用全零数据覆盖整块磁盘，1 遍完成。",
            WipeAlgorithm.Dod3 => "全零 → 全一 → 随机数据，共 3 遍。",
            WipeAlgorithm.Gutmann => "Gutmann 35 遍模式，包含 27 种固定图样与 8 遍随机数据，耗时最长。",
            _ => ""
        };
    }

    public static string PassLabel(WipeAlgorithm algorithm, int passNumber)
    {
        int index = passNumber - 1;
        if (index < 0)
        {
            return "—";
        }
        switch (algorithm)
        {
            case WipeAlgorithm.Random:
                return "随机数据";
            case WipeAlgorithm.Zero:
                return "全零 0x00";
            case WipeAlgorithm.Dod3:
                return index switch
                {
                    0 => "全零 0x00",
                    1 => "全一 0xFF",
                    2 => "随机数据",
                    _ => "—"
                };
            case WipeAlgorithm.Gutmann:
                if (index < 4 || index >= 31)
                {
                    return "随机数据";
                }
                return BytePatternLabel(GutmannPatterns[index - 4]);
            default:
                return "—";
        }
    }

    private static string BytePatternLabel(byte[] pattern)
    {
        var parts = new string[pattern.Length];
        for (int i = 0; i < pattern.Length; i++)
        {
            parts[i] = "0x" + pattern[i].ToString("X2", CultureInfo.InvariantCulture);
        }
        return string.Join(" ", parts);
    }

    /// <summary>扫描物理磁盘；系统盘会被自动排除。读取失败不影响其它磁盘。</summary>
    public static WipeEnumerationResult EnumerateDisks()
    {
        try
        {
            HashSet<int> systemDisks = GetSystemDiskNumbers();
            var disks = new List<WipeDiskInfo>();
            for (int number = 0; number < 64; number++)
            {
                using SafeFileHandle? handle = TryOpenPhysicalDrive(number, GenericRead);
                if (handle == null)
                {
                    continue;
                }
                WipeDiskInfo? info = QueryDiskInfo(handle, number);
                if (info == null || systemDisks.Contains(info.Number) || systemDisks.Contains(number))
                {
                    continue;
                }
                disks.Add(info);
            }
            disks.Sort((a, b) => a.Number.CompareTo(b.Number));
            return new WipeEnumerationResult(disks, systemDisks.Count, null);
        }
        catch (Exception ex)
        {
            return new WipeEnumerationResult(Array.Empty<WipeDiskInfo>(), 0,
                "无法安全识别系统盘，已停止磁盘扫描：" + ex.Message);
        }
    }

    private static HashSet<int> GetSystemDiskNumbers()
    {
        string windowsDir = GetWindowsDirectory();
        if (windowsDir.Length < 2 || windowsDir[1] != ':')
        {
            throw new WipeException("无法确定当前 Windows 所在的卷，出于安全原因已停止扫描磁盘。");
        }
        char drive = char.ToUpperInvariant(windowsDir[0]);
        if (drive < 'A' || drive > 'Z')
        {
            throw new WipeException("无法确定当前 Windows 所在的卷，出于安全原因已停止扫描磁盘。");
        }

        string volumePath = @"\\.\" + drive + ":";
        using SafeFileHandle? volume = TryOpenPhysicalDrivePath(volumePath, GenericRead, readShareOnly: false);
        if (volume == null)
        {
            throw new WipeException("无法打开当前系统卷，出于安全原因已停止扫描磁盘。");
        }

        byte[] buffer = new byte[1024 * 1024];
        if (!DeviceIoControl(volume, IoctlVolumeGetVolumeDiskExtents,
                null, 0, buffer, buffer.Length, out int bytes, IntPtr.Zero))
        {
            throw new WipeException("无法查询当前系统盘所在的物理磁盘，出于安全原因已停止扫描磁盘。",
                Marshal.GetLastWin32Error());
        }
        if (bytes < 8)
        {
            throw new WipeException("系统盘所在物理磁盘信息不完整，出于安全原因已停止扫描磁盘。");
        }

        int count = BitConverter.ToInt32(buffer, 0);
        if (count <= 0 || count > (bytes - 8) / 24)
        {
            throw new WipeException("系统盘所在物理磁盘信息异常，出于安全原因已停止扫描磁盘。");
        }
        var result = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            int offset = 8 + i * 24;
            result.Add(BitConverter.ToInt32(buffer, offset));
        }
        return result;
    }

    private static string GetWindowsDirectory()
    {
        var buffer = new StringBuilder(260);
        if (GetWindowsDirectoryW(buffer, (uint)buffer.Capacity) > 0)
        {
            return buffer.ToString();
        }
        return Environment.SystemDirectory;
    }

    private static SafeFileHandle? TryOpenPhysicalDrive(int number, uint access)
    {
        return TryOpenPhysicalDrivePath(@"\\.\PhysicalDrive" + number, access, readShareOnly: false);
    }

    private static SafeFileHandle? TryOpenPhysicalDrivePath(string path, uint access, bool readShareOnly)
    {
        SafeFileHandle handle = CreateFileW(path, access,
            FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }
        return handle;
    }

    private sealed record DiskDescriptorInfo(
        string Model,
        string Serial,
        bool Removable,
        int BusType,
        string BusName);

    private static WipeDiskInfo? QueryDiskInfo(SafeFileHandle handle, int number)
    {
        long size = QueryDiskLength(handle);
        if (size <= 0)
        {
            return null;
        }

        int deviceNumber = number;
        byte[] numberBuffer = new byte[16];
        if (DeviceIoControl(handle, IoctlStorageGetDeviceNumber,
                null, 0, numberBuffer, numberBuffer.Length, out int numberBytes, IntPtr.Zero)
            && numberBytes >= 12)
        {
            deviceNumber = BitConverter.ToInt32(numberBuffer, 4);
        }

        DiskDescriptorInfo desc = QueryDescriptor(handle);
        int logical = 512;
        int physical = 512;
        QuerySectorSizes(handle, out logical, out physical);
        WipeMediaKind media = QueryMediaKind(handle, desc.BusType);
        int partitions = QueryPartitionCount(handle);

        // 某些存储驱动对 IOCTL_STORAGE_QUERY_PROPERTY 返回不完整信息，
        // 用 WMI Win32_DiskDrive 按物理磁盘编号补齐型号 / 序列号 / 接口 / 分区数。
        if (desc.Model == "未知设备" || desc.Serial.Length == 0
            || desc.BusName == "Unknown" || partitions < 0)
        {
            WmiDiskInfo? wmi = TryQueryWmiDisk(deviceNumber);
            if (wmi != null)
            {
                string model = desc.Model;
                if ((model.Length == 0 || model == "未知设备") && wmi.Model.Length > 0)
                {
                    model = wmi.Model;
                }
                string serial = desc.Serial.Length > 0 ? desc.Serial : wmi.Serial;
                string busName = desc.BusName;
                if (busName == "Unknown" && wmi.Interface.Length > 0)
                {
                    busName = wmi.Interface;
                }
                if (partitions < 0)
                {
                    partitions = wmi.Partitions;
                }
                desc = new DiskDescriptorInfo(model, serial, desc.Removable, desc.BusType, busName);
            }
        }

        return new WipeDiskInfo(deviceNumber, desc.Model, desc.Serial, desc.BusName,
            size, logical, physical, desc.Removable, media, partitions);
    }

    private sealed record WmiDiskInfo(string Model, string Serial, string Interface, int Partitions);

    private static WmiDiskInfo? TryQueryWmiDisk(int index)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Index, Model, SerialNumber, InterfaceType, Partitions FROM Win32_DiskDrive");
            using ManagementObjectCollection results = searcher.Get();
            foreach (ManagementBaseObject obj in results)
            {
                object? indexValue = obj["Index"];
                if (indexValue == null)
                {
                    continue;
                }
                int diskIndex;
                try
                {
                    diskIndex = Convert.ToInt32(indexValue, CultureInfo.InvariantCulture);
                }
                catch
                {
                    continue;
                }
                if (diskIndex != index)
                {
                    continue;
                }
                string model = (obj["Model"]?.ToString() ?? "").Trim();
                string serial = (obj["SerialNumber"]?.ToString() ?? "").Trim();
                string intf = (obj["InterfaceType"]?.ToString() ?? "").Trim();
                int partitions = -1;
                object? p = obj["Partitions"];
                if (p != null)
                {
                    try
                    {
                        partitions = Convert.ToInt32(p, CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                        // 忽略分区数解析失败
                    }
                }
                return new WmiDiskInfo(model, serial, intf, partitions);
            }
        }
        catch
        {
            // WMI 不可用时静默回退到存储描述符结果
        }
        return null;
    }

    private static long QueryDiskLength(SafeFileHandle handle)
    {
        byte[] buffer = new byte[16];
        if (DeviceIoControl(handle, IoctlDiskGetLengthInfo,
                null, 0, buffer, buffer.Length, out int bytes, IntPtr.Zero) && bytes >= 8)
        {
            long length = BitConverter.ToInt64(buffer, 0);
            if (length > 0)
            {
                return length;
            }
        }

        byte[] geometry = new byte[512];
        if (DeviceIoControl(handle, IoctlDiskGetDriveGeometryEx,
                null, 0, geometry, geometry.Length, out int geoBytes, IntPtr.Zero) && geoBytes >= 32)
        {
            return BitConverter.ToInt64(geometry, 24);
        }
        return 0;
    }

    private static DiskDescriptorInfo QueryDescriptor(SafeFileHandle handle)
    {
        string model = "未知设备";
        string serial = "";
        bool removable = false;
        int busType = 0;
        string busName = "Unknown";

        byte[] buffer = new byte[4096];
        if (!QueryProperty(handle, StorageDeviceProperty, buffer, out int bytes) || bytes < 36)
        {
            return new DiskDescriptorInfo(model, serial, removable, busType, busName);
        }

        int descSize = BitConverter.ToInt32(buffer, 4);
        if (descSize < 36 || descSize > bytes)
        {
            return new DiskDescriptorInfo(model, serial, removable, busType, busName);
        }

        // STORAGE_DEVICE_DESCRIPTOR:
        // Version(0) Size(4) DeviceType(8) DeviceTypeModifier(9)
        // RemovableMedia(10, BOOLEAN) CommandQueueing(11, BOOLEAN)
        // VendorIdOffset(12) ProductIdOffset(16) ProductRevisionOffset(20)
        // SerialNumberOffset(24) BusType(28) RawPropertiesLength(32)
        removable = buffer[10] != 0;
        busType = BitConverter.ToInt32(buffer, 28);
        busName = BusTypeName(busType);
        int vendorOffset = BitConverter.ToInt32(buffer, 12);
        int productOffset = BitConverter.ToInt32(buffer, 16);
        int serialOffset = BitConverter.ToInt32(buffer, 24);

        string vendor = ReadAnsiString(buffer, vendorOffset, descSize);
        string product = ReadAnsiString(buffer, productOffset, descSize);
        serial = ReadAnsiString(buffer, serialOffset, descSize);
        if (vendor.Length > 0 && product.Length > 0)
        {
            model = vendor + " " + product;
        }
        else if (product.Length > 0)
        {
            model = product;
        }
        else if (vendor.Length > 0)
        {
            model = vendor;
        }
        if (model.Length == 0)
        {
            model = "未知设备";
        }
        return new DiskDescriptorInfo(model.Trim(), serial.Trim(), removable, busType, busName);
    }

    private static string ReadAnsiString(byte[] buffer, int offset, int limit)
    {
        if (offset <= 0 || offset >= limit || offset >= buffer.Length)
        {
            return "";
        }
        int end = offset;
        while (end < limit && end < buffer.Length && buffer[end] != 0)
        {
            end++;
        }
        if (end <= offset)
        {
            return "";
        }
        return System.Text.Encoding.ASCII.GetString(buffer, offset, end - offset).Trim();
    }

    private static void QuerySectorSizes(SafeFileHandle handle, out int logical, out int physical)
    {
        logical = 0;
        physical = 0;
        byte[] buffer = new byte[512];
        if (QueryProperty(handle, StorageAccessAlignmentProperty, buffer, out int bytes) && bytes >= 24)
        {
            int l = BitConverter.ToInt32(buffer, 16);
            int p = BitConverter.ToInt32(buffer, 20);
            if (l > 0)
            {
                logical = l;
            }
            if (p > 0)
            {
                physical = p;
            }
        }

        if (logical <= 0)
        {
            byte[] geometry = new byte[512];
            if (DeviceIoControl(handle, IoctlDiskGetDriveGeometryEx,
                    null, 0, geometry, geometry.Length, out int geoBytes, IntPtr.Zero) && geoBytes >= 24)
            {
                int l = BitConverter.ToInt32(geometry, 20);
                if (l > 0)
                {
                    logical = l;
                }
            }
        }
        if (logical <= 0)
        {
            logical = 512;
        }
        if (physical <= 0)
        {
            physical = logical;
        }
    }

    private static WipeMediaKind QueryMediaKind(SafeFileHandle handle, int busType)
    {
        if (busType is 8 or 14 or 15 or 16 or 18)
        {
            return WipeMediaKind.Unknown;
        }
        byte[] buffer = new byte[512];
        if (QueryProperty(handle, StorageDeviceSeekPenaltyProperty, buffer, out int bytes) && bytes >= 9)
        {
            return buffer[8] != 0 ? WipeMediaKind.Hdd : WipeMediaKind.Ssd;
        }
        return WipeMediaKind.Unknown;
    }

    private static int QueryPartitionCount(SafeFileHandle handle)
    {
        byte[] buffer = new byte[256 * 1024];
        if (DeviceIoControl(handle, IoctlDiskGetDriveLayoutEx,
                null, 0, buffer, buffer.Length, out int bytes, IntPtr.Zero) && bytes >= 8)
        {
            return BitConverter.ToInt32(buffer, 4);
        }
        if (DeviceIoControl(handle, IoctlDiskGetDriveLayout,
                null, 0, buffer, buffer.Length, out int oldBytes, IntPtr.Zero) && oldBytes >= 4)
        {
            return BitConverter.ToInt32(buffer, 0);
        }
        return -1;
    }

    private static byte[] BuildPropertyQuery(int propertyId)
    {
        // STORAGE_PROPERTY_QUERY 含 AdditionalParameters[1]，sizeof 为 12；
        // 使用 8 字节会让部分存储驱动拒绝查询，导致型号/序列号读取失败。
        byte[] query = new byte[12];
        BitConverter.GetBytes(propertyId).CopyTo(query, 0);
        // QueryType = PropertyStandardQuery (0)，AdditionalParameters 全零
        return query;
    }

    private static bool QueryProperty(SafeFileHandle handle, int propertyId,
        byte[] outBuffer, out int bytesReturned)
    {
        byte[] query = BuildPropertyQuery(propertyId);
        return DeviceIoControl(handle, IoctlStorageQueryProperty,
            query, query.Length, outBuffer, outBuffer.Length,
            out bytesReturned, IntPtr.Zero);
    }

    private static string BusTypeName(int busType)
    {
        return busType switch
        {
            1 => "SCSI",
            2 => "ATAPI",
            3 => "ATA",
            4 => "1394",
            5 => "SSA",
            6 => "Fibre",
            7 => "USB",
            8 => "RAID",
            9 => "iSCSI",
            10 => "SAS",
            11 => "SATA",
            12 => "SD",
            13 => "MMC",
            14 => "Virtual",
            15 => "VHD",
            16 => "Spaces",
            17 => "NVMe",
            18 => "SCM",
            19 => "UFS",
            _ => "Unknown"
        };
    }

    /// <summary>准备覆写任务：打开磁盘、校验身份、锁定并卸载卷。此阶段不会写入任何数据。</summary>
    public static WipeJob Prepare(WipeDiskInfo target, WipeAlgorithm algorithm, bool encryptPrePass)
    {
        HashSet<int> systemDisks = GetSystemDiskNumbers();
        if (systemDisks.Contains(target.Number))
        {
            throw new WipeException("安全校验失败：目标磁盘是当前系统盘，已拒绝操作。");
        }

        SafeFileHandle? disk = null;
        List<SafeFileHandle>? volumes = null;
        try
        {
            disk = TryOpenPhysicalDrive(target.Number, GenericRead | GenericWrite);
            if (disk == null)
            {
                throw new WipeException(
                    "无法以读写方式打开目标物理磁盘。\n\n" +
                    "请确认已以管理员身份运行，并关闭正在占用该磁盘的程序。\n" +
                    "Windows 错误 5：拒绝访问。");
            }

            byte[] writableBuffer = new byte[16];
            if (!DeviceIoControl(disk, IoctlDiskIsWritable,
                    null, 0, writableBuffer, writableBuffer.Length, out _, IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == ErrorWriteProtect)
                {
                    throw new WipeException(
                        "目标磁盘处于写保护状态，无法执行覆写。\n\n" +
                        "请检查 U 盘/存储卡上的物理写保护开关，并确认没有被 BitLocker 策略或安全软件设置为只读。");
                }
            }

            long length = QueryDiskLength(disk);
            if (length <= 0)
            {
                throw new WipeException("无法读取目标磁盘容量。");
            }
            if (length != target.Size)
            {
                throw new WipeException(
                    "目标磁盘容量与扫描结果不一致，磁盘可能已被更换。\n请刷新磁盘列表后重新选择。");
            }

            DiskDescriptorInfo fresh = QueryDescriptor(disk);
            if (!string.IsNullOrEmpty(target.Serial) && !string.IsNullOrEmpty(fresh.Serial)
                && !string.Equals(target.Serial, fresh.Serial, StringComparison.OrdinalIgnoreCase))
            {
                throw new WipeException(
                    "目标磁盘的序列号与扫描结果不一致，磁盘可能已被更换。\n请刷新磁盘列表后重新选择。");
            }
            if (string.IsNullOrEmpty(target.Serial) && !string.IsNullOrEmpty(target.Model)
                && !string.Equals(target.Model, "未知设备", StringComparison.Ordinal)
                && !string.IsNullOrEmpty(fresh.Model)
                && !string.Equals(target.Model, fresh.Model, StringComparison.OrdinalIgnoreCase))
            {
                throw new WipeException(
                    "目标磁盘的设备型号与扫描结果不一致，磁盘可能已被更换。\n请刷新磁盘列表后重新选择。");
            }

            QuerySectorSizes(disk, out int logical, out int physical);
            if (logical < 1 || logical > 65536 || physical < 1 || physical > 65536)
            {
                throw new WipeException("目标磁盘报告了不支持的扇区大小，操作已停止。");
            }
            int alignment = Math.Max(logical, physical);
            long writeLength = length - length % logical;
            if (writeLength <= 0)
            {
                throw new WipeException("目标磁盘没有可写入的完整扇区。");
            }
            int bufferLength = IoChunkBytes;
            bufferLength -= bufferLength % alignment;
            if (bufferLength < alignment)
            {
                bufferLength = alignment;
            }

            volumes = LockTargetVolumes(target.Number);

            HashSet<int> recheck = GetSystemDiskNumbers();
            if (recheck.Contains(target.Number))
            {
                throw new WipeException("安全校验失败：目标磁盘是当前系统盘，已拒绝操作。");
            }

            var job = new WipeJob(target, algorithm, encryptPrePass, disk, volumes,
                length, writeLength, bufferLength);
            disk = null;
            volumes = null;
            return job;
        }
        finally
        {
            if (volumes != null)
            {
                foreach (SafeFileHandle volume in volumes)
                {
                    volume.Dispose();
                }
            }
            disk?.Dispose();
        }
    }

    private static List<SafeFileHandle> LockTargetVolumes(int targetDiskNumber)
    {
        var locked = new List<SafeFileHandle>();
        var volumeName = new StringBuilder(512);
        IntPtr findHandle = FindFirstVolumeW(volumeName, (uint)volumeName.Capacity);
        if (findHandle == new IntPtr(-1))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == ErrorNoMoreFiles)
            {
                return locked;
            }
            throw new WipeException("无法枚举目标磁盘上的卷。", error);
        }

        try
        {
            while (true)
            {
                string name = volumeName.ToString();
                string openName = name.TrimEnd('\\');
                if (openName.Length == 0)
                {
                    openName = name;
                }

                SafeFileHandle? volume = TryOpenPhysicalDrivePath(
                    openName, GenericRead | GenericWrite, readShareOnly: false);
                bool openedForWrite = volume != null;
                if (volume == null)
                {
                    volume = TryOpenPhysicalDrivePath(openName, 0, readShareOnly: false);
                }

                bool onTarget = false;
                bool disposeVolume = true;
                if (volume != null)
                {
                    byte[] extentBuffer = new byte[1024 * 1024];
                    if (DeviceIoControl(volume, IoctlVolumeGetVolumeDiskExtents,
                            null, 0, extentBuffer, extentBuffer.Length, out int bytes, IntPtr.Zero))
                    {
                        if (bytes >= 8)
                        {
                            int count = BitConverter.ToInt32(extentBuffer, 0);
                            if (count > 0 && count <= (bytes - 8) / 24)
                            {
                                for (int i = 0; i < count; i++)
                                {
                                    int offset = 8 + i * 24;
                                    if (BitConverter.ToInt32(extentBuffer, offset) == targetDiskNumber)
                                    {
                                        onTarget = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error is ErrorMoreData or ErrorInsufficientBuffer)
                        {
                            volume.Dispose();
                            throw new WipeException("无法完整枚举某个卷所在的物理磁盘，出于安全原因已停止操作。",
                                error);
                        }
                    }

                    if (onTarget)
                    {
                        if (!openedForWrite)
                        {
                            volume.Dispose();
                            volume = TryOpenPhysicalDrivePath(openName,
                                GenericRead | GenericWrite, readShareOnly: false);
                            if (volume == null)
                            {
                                throw new WipeException(
                                    "目标磁盘上存在无法以读写方式打开的卷，操作已停止。",
                                    Marshal.GetLastWin32Error());
                            }
                        }
                        LockAndDismountVolume(volume, name);
                        locked.Add(volume);
                        disposeVolume = false;
                    }
                }

                if (volume != null && disposeVolume)
                {
                    volume.Dispose();
                }

                if (!FindNextVolumeW(findHandle, volumeName, (uint)volumeName.Capacity))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != ErrorNoMoreFiles)
                    {
                        throw new WipeException("枚举目标磁盘上的卷时发生错误，操作已停止。", error);
                    }
                    break;
                }
            }
        }
        catch
        {
            foreach (SafeFileHandle handle in locked)
            {
                handle.Dispose();
            }
            throw;
        }
        finally
        {
            FindVolumeClose(findHandle);
        }
        return locked;
    }

    private static void LockAndDismountVolume(SafeFileHandle volume, string displayName)
    {
        int lastError = 0;
        bool locked = TryLockVolume(volume, 4);
        if (!locked)
        {
            lastError = Marshal.GetLastWin32Error();
            if (DeviceIoControl(volume, FsctlDismountVolume,
                    null, 0, null, 0, out _, IntPtr.Zero))
            {
                locked = TryLockVolume(volume, 5);
                if (!locked)
                {
                    lastError = Marshal.GetLastWin32Error();
                }
            }
            else
            {
                lastError = Marshal.GetLastWin32Error();
            }
        }

        if (!locked)
        {
            throw new WipeException(
                "无法锁定目标磁盘上的卷 " + displayName + "，可能有程序正在使用该磁盘。\n" +
                "请关闭资源管理器窗口、磁盘管理、杀毒扫描或其他占用该磁盘的程序后重试。",
                lastError);
        }

        if (!DeviceIoControl(volume, FsctlDismountVolume,
                null, 0, null, 0, out _, IntPtr.Zero))
        {
            int error = Marshal.GetLastWin32Error();
            if (error is not ErrorInvalidFunction and not ErrorNotSupported)
            {
                throw new WipeException(
                    "卷 " + displayName + " 已锁定，但 Windows 无法卸载该卷；为避免破坏文件系统，操作已停止。",
                    error);
            }
        }
    }

    private static bool TryLockVolume(SafeFileHandle volume, int attempts)
    {
        for (int i = 0; i < attempts; i++)
        {
            if (DeviceIoControl(volume, FsctlLockVolume,
                    null, 0, null, 0, out _, IntPtr.Zero))
            {
                return true;
            }
            Thread.Sleep(120);
        }
        return false;
    }

    internal static (bool Random, byte[]? Pattern) GetPassSpec(WipeAlgorithm algorithm, int pass)
    {
        switch (algorithm)
        {
            case WipeAlgorithm.Random:
                return (true, null);
            case WipeAlgorithm.Zero:
                return (false, new byte[] { 0x00 });
            case WipeAlgorithm.Dod3:
                return pass switch
                {
                    0 => (false, new byte[] { 0x00 }),
                    1 => (false, new byte[] { 0xFF }),
                    _ => (true, null)
                };
            case WipeAlgorithm.Gutmann:
                if (pass < 4 || pass >= 31)
                {
                    return (true, null);
                }
                return (false, GutmannPatterns[pass - 4]);
            default:
                return (true, null);
        }
    }
}

public sealed class WipeException : Exception
{
    public WipeException(string message) : base(message)
    {
    }

    public WipeException(string message, int win32Error)
        : base(message + "\n\nWindows 错误 " + win32Error + "：" +
               new Win32Exception(win32Error).Message)
    {
    }
}

/// <summary>运行中的覆写任务。后台线程执行，UI 轮询 GetProgress()。</summary>
public sealed class WipeJob
{
    private readonly WipeDiskInfo _target;
    private readonly WipeAlgorithm _algorithm;
    private readonly bool _encryptPrePass;
    private readonly int _totalPasses;
    private readonly SafeFileHandle _disk;
    private readonly List<SafeFileHandle> _volumes;
    private readonly long _diskLength;
    private readonly long _writeLength;
    private readonly int _bufferLength;
    private readonly byte[] _buffer;
    private readonly GCHandle _bufferHandle;
    private readonly IntPtr _bufferAddress;
    private readonly long _totalBytes;
    private readonly object _sync = new();

    private Thread? _thread;
    private volatile bool _cancel;
    private bool _finished;
    private bool _success;
    private bool _cancelled;
    private string _error = "";
    private long _completedBytes;
    private int _currentPass;
    private int _completedPasses;
    private long _startTick;
    private long _endTick;
    private bool _disposed;
    private int _lastIoError;

    internal WipeJob(WipeDiskInfo target, WipeAlgorithm algorithm, bool encryptPrePass,
        SafeFileHandle disk, List<SafeFileHandle> volumes,
        long diskLength, long writeLength, int bufferLength)
    {
        _target = target;
        _algorithm = algorithm;
        _encryptPrePass = encryptPrePass;
        _totalPasses = DiskWipeEngine.AlgorithmPasses(algorithm);
        _disk = disk;
        _volumes = volumes;
        _diskLength = diskLength;
        _writeLength = writeLength;
        _bufferLength = bufferLength;
        _buffer = new byte[bufferLength];
        _bufferHandle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
        _bufferAddress = _bufferHandle.AddrOfPinnedObject();
        _totalBytes = writeLength * (long)(_totalPasses + (encryptPrePass ? 1 : 0));
        _currentPass = encryptPrePass ? 0 : 1;
    }

    public WipeDiskInfo Target => _target;

    public WipeAlgorithm Algorithm => _algorithm;

    public int TotalPasses => _totalPasses;

    public bool IsFinished
    {
        get
        {
            lock (_sync)
            {
                return _finished;
            }
        }
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_thread != null || _disposed)
            {
                throw new InvalidOperationException("覆写任务已启动或已释放。");
            }
            _startTick = Environment.TickCount64;
            _thread = new Thread(Run) { IsBackground = true, Name = "DiskWipe" };
            _thread.Start();
        }
    }

    public void Cancel()
    {
        _cancel = true;
    }

    public WipeProgressSnapshot GetProgress()
    {
        lock (_sync)
        {
            long now = _endTick > 0 ? _endTick : Environment.TickCount64;
            long elapsed = _startTick > 0 && now >= _startTick ? now - _startTick : 0;
            return new WipeProgressSnapshot(_algorithm, _encryptPrePass, _totalPasses,
                _currentPass, _completedPasses, _diskLength, _totalBytes,
                _completedBytes, elapsed, _finished, _success, _cancelled, _error);
        }
    }

    private void Run()
    {
        DiskWipeEngine.SetThreadExecutionState(DiskWipeEngine.EsContinuous | DiskWipeEngine.EsSystemRequired);
        bool failed = false;
        bool cancelled = false;
        string error = "";
        try
        {
            if (_encryptPrePass)
            {
                using Aes aes = Aes.Create();
                aes.KeySize = 256;
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                byte[] key = new byte[32];
                byte[] nonce = new byte[8];
                RandomNumberGenerator.Fill(key);
                RandomNumberGenerator.Fill(nonce);
                aes.Key = key;

                using var encryptor = aes.CreateEncryptor();
                byte[] counter = new byte[((_bufferLength + 15) / 16) * 16];
                byte[] stream = new byte[counter.Length];

                long offset = 0;
                long blockIndex = 0;
                while (offset < _writeLength)
                {
                    if (_cancel)
                    {
                        cancelled = true;
                        break;
                    }
                    int chunk = (int)Math.Min(_writeLength - offset, _bufferLength);
                    if (!ReadAt(_disk, _bufferAddress, offset, chunk))
                    {
                        error = "读取目标磁盘进行加密时发生错误，操作已停止。\n磁盘可能已被部分加密。"
                            + Win32ErrorSuffix();
                        failed = true;
                        break;
                    }

                    int blocks = (chunk + 15) / 16;
                    int streamBytes = blocks * 16;
                    for (int i = 0; i < blocks; i++)
                    {
                        int blockOffset = i * 16;
                        Buffer.BlockCopy(nonce, 0, counter, blockOffset, 8);
                        ulong value = (ulong)blockIndex + (ulong)i;
                        for (int b = 0; b < 8; b++)
                        {
                            counter[blockOffset + 8 + b] = (byte)(value >> (b * 8));
                        }
                    }
                    encryptor.TransformBlock(counter, 0, streamBytes, stream, 0);
                    for (int i = 0; i < chunk; i++)
                    {
                        _buffer[i] ^= stream[i];
                    }
                    if (!WriteAt(_disk, _bufferAddress, offset, chunk))
                    {
                        error = "写入加密数据到目标磁盘时发生错误，操作已停止。\n磁盘可能已被部分加密。"
                            + Win32ErrorSuffix();
                        failed = true;
                        break;
                    }

                    offset += chunk;
                    blockIndex += blocks;
                    lock (_sync)
                    {
                        _completedBytes = offset;
                    }
                }

                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(nonce);
                CryptographicOperations.ZeroMemory(counter);
                CryptographicOperations.ZeroMemory(stream);

                if (!failed && !cancelled)
                {
                    if (!DiskWipeEngine.FlushFileBuffers(_disk))
                    {
                        _lastIoError = Marshal.GetLastWin32Error();
                        error = "全盘加密预覆盖完成，但刷新磁盘缓存失败。\n磁盘可能只被部分写入。"
                            + Win32ErrorSuffix();
                        failed = true;
                    }
                }
            }
            if (!failed && !cancelled)
            {
                for (int pass = 0; pass < _totalPasses && !failed && !cancelled; pass++)
                {
                    lock (_sync)
                    {
                        _currentPass = pass + 1;
                    }
                    (bool random, byte[]? pattern) = DiskWipeEngine.GetPassSpec(_algorithm, pass);
                    long offset = 0;
                    while (offset < _writeLength)
                    {
                        if (_cancel)
                        {
                            cancelled = true;
                            break;
                        }
                        int chunk = (int)Math.Min(_writeLength - offset, _bufferLength);
                        if (random)
                        {
                            RandomNumberGenerator.Fill(_buffer.AsSpan(0, chunk));
                        }
                        else if (pattern is { Length: 1 })
                        {
                            Array.Fill(_buffer, pattern[0], 0, chunk);
                        }
                        else if (pattern != null)
                        {
                            FillPattern(_buffer, chunk, pattern, offset);
                        }
                        if (!WriteAt(_disk, _bufferAddress, offset, chunk))
                        {
                            error = "写入目标磁盘扇区时发生错误，操作已停止。\n磁盘可能只被部分覆写。"
                                + Win32ErrorSuffix();
                            failed = true;
                            break;
                        }
                        offset += chunk;
                        lock (_sync)
                        {
                            _completedBytes = _writeLength
                                * (long)(pass + (_encryptPrePass ? 1 : 0)) + offset;
                        }
                    }

                    if (failed || cancelled)
                    {
                        break;
                    }
                    if (!DiskWipeEngine.FlushFileBuffers(_disk))
                    {
                        error = "扇区覆写完成，但刷新磁盘缓存失败。\n磁盘可能只被部分写入。"
                            + Win32ErrorSuffix();
                        failed = true;
                        break;
                    }
                    lock (_sync)
                    {
                        _completedPasses = pass + 1;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            failed = true;
            error = "覆写过程中发生异常：" + ex.Message;
        }
        finally
        {
            try
            {
                DiskWipeEngine.FlushFileBuffers(_disk);
            }
            catch
            {
                // 忽略收尾刷新错误
            }
            DiskWipeEngine.SetThreadExecutionState(DiskWipeEngine.EsContinuous);
            lock (_sync)
            {
                _endTick = Environment.TickCount64;
                _finished = true;
                _success = !failed && !cancelled;
                _cancelled = cancelled && !failed;
                if (_success)
                {
                    _error = "";
                }
                else if (cancelled)
                {
                    _error = "操作已由用户中止。\n目标磁盘可能已被部分加密或覆写，原有数据可能已无法恢复。";
                }
                else
                {
                    _error = error.Length > 0 ? error : "覆写过程被异常终止。";
                }
                if (_success)
                {
                    _completedBytes = _totalBytes;
                    _completedPasses = _totalPasses;
                }
            }
        }
    }

    private static void FillPattern(byte[] buffer, int length, byte[] pattern, long offset)
    {
        int phase = (int)(offset % pattern.Length);
        for (int i = 0; i < length; i++)
        {
            buffer[i] = pattern[(phase + i) % pattern.Length];
        }
    }

    private bool ReadAt(SafeFileHandle disk, IntPtr buffer, long offset, int count)
    {
        if (!DiskWipeEngine.SetFilePointerEx(disk, offset, IntPtr.Zero, DiskWipeEngine.FileBegin))
        {
            _lastIoError = Marshal.GetLastWin32Error();
            return false;
        }
        int total = 0;
        while (total < count)
        {
            if (!DiskWipeEngine.ReadFile(disk, IntPtr.Add(buffer, total), count - total, out int read, IntPtr.Zero))
            {
                _lastIoError = Marshal.GetLastWin32Error();
                return false;
            }
            if (read <= 0)
            {
                _lastIoError = 0;
                return false;
            }
            total += read;
        }
        _lastIoError = 0;
        return true;
    }

    private bool WriteAt(SafeFileHandle disk, IntPtr buffer, long offset, int count)
    {
        if (!DiskWipeEngine.SetFilePointerEx(disk, offset, IntPtr.Zero, DiskWipeEngine.FileBegin))
        {
            _lastIoError = Marshal.GetLastWin32Error();
            return false;
        }
        int total = 0;
        while (total < count)
        {
            if (!DiskWipeEngine.WriteFile(disk, IntPtr.Add(buffer, total), count - total, out int written, IntPtr.Zero))
            {
                _lastIoError = Marshal.GetLastWin32Error();
                return false;
            }
            if (written <= 0)
            {
                _lastIoError = 0;
                return false;
            }
            total += written;
        }
        _lastIoError = 0;
        return true;
    }

    private string Win32ErrorSuffix()
    {
        int code = _lastIoError;
        return code == 0
            ? ""
            : "\nWindows 错误 " + code + "：" + new Win32Exception(code).Message;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _cancel = true;
        Thread? thread;
        lock (_sync)
        {
            thread = _thread;
        }
        if (thread != null && thread.IsAlive)
        {
            thread.Join();
        }

        foreach (SafeFileHandle volume in _volumes)
        {
            volume.Dispose();
        }
        _volumes.Clear();

        try
        {
            if (!_disk.IsInvalid)
            {
                DiskWipeEngine.DeviceIoControl(_disk, DiskWipeEngine.IoctlDiskUpdateProperties, null, 0, null, 0, out _, IntPtr.Zero);
            }
        }
        catch
        {
            // 忽略刷新分区表错误
        }
        _disk.Dispose();

        if (_bufferHandle.IsAllocated)
        {
            _bufferHandle.Free();
        }
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
