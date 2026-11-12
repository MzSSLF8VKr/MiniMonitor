using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MiniMonitor.Recovery;

/// <summary>可恢复的逻辑卷。</summary>
public sealed class RecoveryVolume
{
    public string RootPath = "";
    public string DevicePath = "";
    public string Label = "";
    public string FileSystem = "";
    public long TotalSize;
    public string DriveType = "";

    public override string ToString()
    {
        string label = Label.Length > 0 ? " " + Label : "";
        return RootPath + label + "（" + FileSystem + "，"
            + RecoveryDisk.FormatSize(TotalSize) + "）";
    }
}

/// <summary>物理磁盘。</summary>
public sealed class RecoveryPhysicalDisk
{
    public int Number;
    public string Model = "";
    public string Interface = "";
    public long Size;
    public string DevicePath => @"\\.\PhysicalDrive" + Number;

    public override string ToString() =>
        "磁盘 " + Number + "：" + (Model.Length > 0 ? Model : "未知型号")
        + "（" + RecoveryDisk.FormatSize(Size) + "）";
}

/// <summary>统一的恢复数据源（逻辑卷或物理磁盘）。</summary>
public sealed class RecoverySource
{
    public string Name = "";
    public bool IsPhysical;
    public int DiskNumber;
    public RecoveryVolume? Volume;
    public RecoveryPhysicalDisk? Physical;
    public long Length;

    public override string ToString() => Name;
}

/// <summary>只读原始磁盘 / 卷访问以及物理磁盘枚举。</summary>
public static class RecoveryDisk
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint IoctlDiskGetLengthInfo = 0x0007405C;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName,
        uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(SafeFileHandle file, IntPtr buffer,
        int bytesToRead, out int bytesRead, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(SafeFileHandle file, IntPtr buffer,
        int bytesToWrite, out int bytesWritten, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFilePointerEx(SafeFileHandle file,
        long distance, IntPtr newFilePointer, uint moveMethod);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileSizeEx(SafeFileHandle file, out long size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device,
        uint ioControlCode, byte[]? inBuffer, int inBufferSize,
        byte[]? outBuffer, int outBufferSize, out int bytesReturned,
        IntPtr overlapped);

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024 * 1024)
        {
            return (bytes / 1024.0 / 1024 / 1024 / 1024).ToString("0.00") + " TB";
        }
        if (bytes >= 1024L * 1024 * 1024)
        {
            return (bytes / 1024.0 / 1024 / 1024).ToString("0.00") + " GB";
        }
        if (bytes >= 1024L * 1024)
        {
            return (bytes / 1024.0 / 1024).ToString("0.0") + " MB";
        }
        return (bytes / 1024.0).ToString("0.0") + " KB";
    }

    public static List<RecoveryVolume> GetVolumes()
    {
        var list = new List<RecoveryVolume>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady)
                    {
                        continue;
                    }
                    if (drive.DriveType != DriveType.Fixed
                        && drive.DriveType != DriveType.Removable)
                    {
                        continue;
                    }
                    string root = drive.RootDirectory.FullName;
                    if (root.Length < 2)
                    {
                        continue;
                    }
                    list.Add(new RecoveryVolume
                    {
                        RootPath = root,
                        DevicePath = @"\\.\" + root[..2],
                        Label = drive.VolumeLabel,
                        FileSystem = drive.DriveFormat,
                        TotalSize = drive.TotalSize,
                        DriveType = drive.DriveType.ToString()
                    });
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
        return list;
    }

    public static List<RecoveryPhysicalDisk> GetPhysicalDisks()
    {
        var list = new List<RecoveryPhysicalDisk>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Index, Model, Size, InterfaceType, Partitions FROM Win32_DiskDrive");
            foreach (ManagementObject disk in searcher.Get())
            {
                try
                {
                    int number = Convert.ToInt32(disk["Index"] ?? 0);
                    long size = Convert.ToInt64(disk["Size"] ?? 0L);
                    list.Add(new RecoveryPhysicalDisk
                    {
                        Number = number,
                        Model = Convert.ToString(disk["Model"]) ?? "",
                        Interface = Convert.ToString(disk["InterfaceType"]) ?? "",
                        Size = size
                    });
                }
                catch
                {
                }
                finally
                {
                    disk.Dispose();
                }
            }
        }
        catch
        {
        }
        list.Sort((left, right) => left.Number.CompareTo(right.Number));
        return list;
    }

    public static List<RecoverySource> GetSources(bool includePhysicalDisks)
    {
        var sources = new List<RecoverySource>();
        foreach (var volume in GetVolumes())
        {
            sources.Add(new RecoverySource
            {
                Name = "卷 " + volume.RootPath + "  " + volume.Label
                    + "（" + volume.FileSystem + "，"
                    + FormatSize(volume.TotalSize) + "）",
                IsPhysical = false,
                Volume = volume,
                Length = volume.TotalSize
            });
        }
        if (includePhysicalDisks)
        {
            foreach (var disk in GetPhysicalDisks())
            {
                sources.Add(new RecoverySource
                {
                    Name = disk.ToString(),
                    IsPhysical = true,
                    DiskNumber = disk.Number,
                    Physical = disk,
                    Length = disk.Size
                });
            }
        }
        return sources;
    }

    public static SafeFileHandle? OpenSource(RecoverySource source, bool write = false)
    {
        if (source.IsPhysical)
        {
            return OpenPhysicalDisk(source.DiskNumber, write);
        }
        if (source.Volume != null)
        {
            return write
                ? OpenDevice(@"\\.\" + source.Volume.RootPath[..2],
                    GenericRead | GenericWrite)
                : OpenVolume(source.Volume.RootPath);
        }
        return null;
    }

    public static SafeFileHandle? OpenVolume(string rootPath)
    {
        if (rootPath.Length < 2)
        {
            return null;
        }
        return OpenDevice(@"\\.\" + rootPath[..2], GenericRead);
    }

    public static SafeFileHandle? OpenPhysicalDisk(int number, bool write = false)
    {
        return OpenDevice(@"\\.\PhysicalDrive" + number,
            write ? GenericRead | GenericWrite : GenericRead);
    }

    public static SafeFileHandle? OpenDevice(string path, uint access = GenericRead)
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

    public static long GetLength(SafeFileHandle handle)
    {
        if (GetFileSizeEx(handle, out long size) && size > 0)
        {
            return size;
        }
        var buffer = new byte[8];
        if (DeviceIoControl(handle, IoctlDiskGetLengthInfo, null, 0,
            buffer, buffer.Length, out int returned, IntPtr.Zero) && returned >= 8)
        {
            return BitConverter.ToInt64(buffer, 0);
        }
        return 0;
    }

    /// <summary>按扇区对齐读取；offset/count 可不对齐。</summary>
    public static byte[] ReadAt(SafeFileHandle handle, long offset, int count,
        int sectorSize = 512)
    {
        if (count <= 0 || offset < 0)
        {
            return Array.Empty<byte>();
        }
        long alignedOffset = offset / sectorSize * sectorSize;
        int prefix = (int)(offset - alignedOffset);
        int total = prefix + count;
        int alignedTotal = (total + sectorSize - 1) / sectorSize * sectorSize;
        byte[] buffer = new byte[alignedTotal];
        if (!ReadRaw(handle, alignedOffset, buffer, alignedTotal))
        {
            // 少数卷允许非对齐直接读取，失败时再尝试一次。
            if (!ReadRaw(handle, offset, buffer, count))
            {
                return Array.Empty<byte>();
            }
            return buffer[..count];
        }
        byte[] result = new byte[count];
        Buffer.BlockCopy(buffer, prefix, result, 0, count);
        return result;
    }

    public static bool ReadRaw(SafeFileHandle handle, long offset,
        byte[] buffer, int count)
    {
        if (!SetFilePointerEx(handle, offset, IntPtr.Zero, 0))
        {
            return false;
        }
        IntPtr unmanaged = Marshal.AllocHGlobal(count);
        try
        {
            if (!ReadFile(handle, unmanaged, count, out int read, IntPtr.Zero)
                || read <= 0)
            {
                return false;
            }
            Marshal.Copy(unmanaged, buffer, 0, Math.Min(read, count));
            if (read < count)
            {
                Array.Clear(buffer, read, count - read);
            }
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(unmanaged);
        }
    }

    public static bool WriteRaw(SafeFileHandle handle, long offset,
        byte[] buffer, int count)
    {
        if (!SetFilePointerEx(handle, offset, IntPtr.Zero, 0))
        {
            return false;
        }
        IntPtr unmanaged = Marshal.AllocHGlobal(count);
        try
        {
            Marshal.Copy(buffer, 0, unmanaged, count);
            return WriteFile(handle, unmanaged, count, out int written, IntPtr.Zero)
                && written == count;
        }
        finally
        {
            Marshal.FreeHGlobal(unmanaged);
        }
    }
}
