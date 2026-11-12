using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MiniMonitor.Recovery;

public sealed class PartitionEntry
{
    public int Index;
    public string Table = "";
    public string Type = "";
    public long StartLba;
    public long Sectors;
    public string FileSystem = "";
    public string Name = "";
    public bool IsGpt;

    public long StartOffset => StartLba * 512;
    public long Size => Sectors * 512;

    public string SizeText => RecoveryDisk.FormatSize(Size);
}

/// <summary>
/// TestDisk 核心：MBR / GPT 分区表解析、备份、恢复与丢失分区签名搜索。
/// </summary>
public static class PartitionAnalyzer
{
    private static readonly Dictionary<byte, string> MbrTypes = new()
    {
        [0x00] = "空",
        [0x01] = "FAT12",
        [0x04] = "FAT16 <32M",
        [0x05] = "扩展分区",
        [0x06] = "FAT16",
        [0x07] = "NTFS / exFAT / HPFS",
        [0x0B] = "FAT32 CHS",
        [0x0C] = "FAT32 LBA",
        [0x0E] = "FAT16 LBA",
        [0x0F] = "扩展分区 LBA",
        [0x11] = "隐藏 FAT12",
        [0x12] = "隐藏 FAT16",
        [0x14] = "隐藏 FAT16 LBA",
        [0x17] = "隐藏 NTFS",
        [0x1B] = "隐藏 FAT32",
        [0x1C] = "隐藏 FAT32 LBA",
        [0x1E] = "隐藏 FAT16 LBA",
        [0x27] = "Windows RE",
        [0x42] = "Windows 动态磁盘",
        [0x82] = "Linux Swap",
        [0x83] = "Linux",
        [0x85] = "Linux 扩展",
        [0x8E] = "Linux LVM",
        [0xA5] = "FreeBSD",
        [0xA8] = "macOS UFS",
        [0xAB] = "macOS Boot",
        [0xAF] = "macOS HFS+",
        [0xBE] = "Solaris Boot",
        [0xBF] = "Solaris",
        [0xEE] = "GPT 保护分区",
        [0xEF] = "EFI 系统分区",
        [0xFB] = "VMware VMFS",
        [0xFC] = "VMware Swap"
    };

    public static bool Analyze(int diskNumber,
        out List<PartitionEntry> partitions, out string summary, out string error)
    {
        partitions = new List<PartitionEntry>();
        summary = "";
        error = "";
        try
        {
            using var handle = RecoveryDisk.OpenPhysicalDisk(diskNumber);
            if (handle == null)
            {
                error = "无法打开物理磁盘（请以管理员身份运行）";
                return false;
            }
            long length = RecoveryDisk.GetLength(handle);
            byte[] mbr = RecoveryDisk.ReadAt(handle, 0, 512);
            if (mbr.Length < 512)
            {
                error = "无法读取 MBR";
                return false;
            }
            bool protective = mbr[0x1BE + 4] == 0xEE;
            summary = "磁盘 " + diskNumber + " · 容量 " + RecoveryDisk.FormatSize(length)
                + " · " + (protective ? "GPT（含保护性 MBR）" : "MBR");
            if (mbr[510] == 0x55 && mbr[511] == 0xAA)
            {
                ParseMbr(handle, mbr, partitions, protective);
            }
            if (protective)
            {
                ParseGpt(handle, partitions, out string gptError);
                if (gptError.Length > 0)
                {
                    summary += " · GPT 解析：" + gptError;
                }
            }
            for (int i = 0; i < partitions.Count; i++)
            {
                partitions[i].Index = i + 1;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void ParseMbr(SafeFileHandle handle, byte[] mbr,
        List<PartitionEntry> partitions, bool protective)
    {
        var extendedBases = new List<long>();
        for (int i = 0; i < 4; i++)
        {
            int offset = 0x1BE + i * 16;
            byte type = mbr[offset + 4];
            long start = BitConverter.ToUInt32(mbr, offset + 8);
            long sectors = BitConverter.ToUInt32(mbr, offset + 12);
            if (type == 0 || sectors == 0)
            {
                continue;
            }
            if (protective && type == 0xEE)
            {
                continue;
            }
            if (type is 0x05 or 0x0F or 0x85)
            {
                extendedBases.Add(start);
                continue;
            }
            partitions.Add(CreateMbrEntry(handle, "MBR", type, start, sectors));
        }
        foreach (long extendedBase in extendedBases)
        {
            long ebrBase = extendedBase;
            int guard = 0;
            while (guard++ < 64)
            {
                byte[] ebr = RecoveryDisk.ReadAt(handle, ebrBase * 512, 512);
                if (ebr.Length < 512 || ebr[510] != 0x55 || ebr[511] != 0xAA)
                {
                    break;
                }
                byte type = ebr[0x1BE + 4];
                long start = BitConverter.ToUInt32(ebr, 0x1BE + 8);
                long sectors = BitConverter.ToUInt32(ebr, 0x1BE + 12);
                if (type != 0 && sectors != 0)
                {
                    partitions.Add(CreateMbrEntry(handle, "EBR",
                        type, ebrBase + start, sectors));
                }
                byte nextType = ebr[0x1CE + 4];
                long nextStart = BitConverter.ToUInt32(ebr, 0x1CE + 8);
                if (nextType is not (0x05 or 0x0F or 0x85) || nextStart == 0)
                {
                    break;
                }
                ebrBase = extendedBase + nextStart;
            }
        }
    }

    private static PartitionEntry CreateMbrEntry(SafeFileHandle handle,
        string table, byte type, long startLba, long sectors)
    {
        string fs = DetectFileSystem(handle, startLba);
        return new PartitionEntry
        {
            Table = table,
            Type = MbrTypes.TryGetValue(type, out string? name)
                ? name : "类型 0x" + type.ToString("X2"),
            StartLba = startLba,
            Sectors = sectors,
            FileSystem = fs,
            Name = fs
        };
    }

    private static void ParseGpt(SafeFileHandle handle,
        List<PartitionEntry> partitions, out string error)
    {
        error = "";
        try
        {
            byte[] header = RecoveryDisk.ReadAt(handle, 512, 512);
            if (header.Length < 92
                || Encoding.ASCII.GetString(header, 0, 8) != "EFI PART")
            {
                byte[] header4k = RecoveryDisk.ReadAt(handle, 4096, 512);
                if (header4k.Length >= 92
                    && Encoding.ASCII.GetString(header4k, 0, 8) == "EFI PART")
                {
                    header = header4k;
                }
                else
                {
                    error = "未找到 GPT 头";
                    return;
                }
            }
            long entryLba = BitConverter.ToInt64(header, 72);
            int count = BitConverter.ToInt32(header, 80);
            int entrySize = BitConverter.ToInt32(header, 84);
            if (count <= 0 || entrySize < 128 || count > 512)
            {
                error = "GPT 分区表参数无效";
                return;
            }
            long tableOffset = entryLba * 512;
            long totalBytes = Math.Min((long)count * entrySize, 1024 * 1024);
            byte[] table = RecoveryDisk.ReadAt(handle, tableOffset,
                (int)totalBytes);
            for (int i = 0; i < count; i++)
            {
                int offset = i * entrySize;
                if (offset + entrySize > table.Length)
                {
                    break;
                }
                bool empty = true;
                for (int b = 0; b < 16; b++)
                {
                    if (table[offset + b] != 0)
                    {
                        empty = false;
                        break;
                    }
                }
                if (empty)
                {
                    continue;
                }
                long first = BitConverter.ToInt64(table, offset + 32);
                long last = BitConverter.ToInt64(table, offset + 40);
                if (first <= 0 || last < first)
                {
                    continue;
                }
                string name = "";
                try
                {
                    name = Encoding.Unicode.GetString(table,
                        offset + 56, 72).TrimEnd('\0').Trim();
                }
                catch
                {
                }
                string fs = DetectFileSystem(handle, first);
                partitions.Add(new PartitionEntry
                {
                    Table = "GPT",
                    Type = "GPT 分区",
                    StartLba = first,
                    Sectors = last - first + 1,
                    FileSystem = fs,
                    Name = name.Length > 0 ? name : fs,
                    IsGpt = true
                });
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
    }

    public static string DetectFileSystem(SafeFileHandle handle, long startLba)
    {
        try
        {
            byte[] boot = RecoveryDisk.ReadAt(handle, startLba * 512, 4096);
            if (boot.Length < 512)
            {
                return "";
            }
            string oem = Encoding.ASCII.GetString(boot, 3, 8).Trim();
            if (oem.StartsWith("NTFS", StringComparison.OrdinalIgnoreCase))
            {
                return "NTFS";
            }
            if (oem.StartsWith("EXFAT", StringComparison.OrdinalIgnoreCase))
            {
                return "exFAT";
            }
            if (oem.StartsWith("FAT32", StringComparison.OrdinalIgnoreCase)
                || (boot.Length >= 0x52 + 8
                    && Encoding.ASCII.GetString(boot, 0x52, 8)
                        .StartsWith("FAT32", StringComparison.OrdinalIgnoreCase)))
            {
                return "FAT32";
            }
            if (oem.StartsWith("FAT", StringComparison.OrdinalIgnoreCase)
                || (boot.Length >= 0x36 + 5
                    && Encoding.ASCII.GetString(boot, 0x36, 5)
                        .StartsWith("FAT", StringComparison.OrdinalIgnoreCase)))
            {
                return "FAT";
            }
            if (Encoding.ASCII.GetString(boot, 0, 4) == "XFSB")
            {
                return "XFS";
            }
            if (boot.Length >= 0x43A
                && BitConverter.ToUInt16(boot, 0x438) == 0xEF53)
            {
                return "ext2/3/4";
            }
            if (Encoding.ASCII.GetString(boot, 0, 8) == "CD001")
            {
                return "ISO9660";
            }
            return "";
        }
        catch
        {
            return "";
        }
    }

    public static bool BackupPartitionTable(int diskNumber, string path,
        out string error)
    {
        error = "";
        try
        {
            using var handle = RecoveryDisk.OpenPhysicalDisk(diskNumber);
            if (handle == null)
            {
                error = "无法打开物理磁盘";
                return false;
            }
            const int size = 4 * 1024 * 1024;
            byte[] data = RecoveryDisk.ReadAt(handle, 0, size);
            if (data.Length < 512)
            {
                error = "读取分区表失败";
                return false;
            }
            File.WriteAllBytes(path, data);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool RestorePartitionTable(int diskNumber, string path,
        out string error)
    {
        error = "";
        try
        {
            if (!File.Exists(path))
            {
                error = "备份文件不存在";
                return false;
            }
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 512 || data.Length % 512 != 0)
            {
                error = "备份文件不是有效的扇区对齐镜像";
                return false;
            }
            using var handle = RecoveryDisk.OpenPhysicalDisk(diskNumber, write: true);
            if (handle == null)
            {
                error = "无法以写入方式打开物理磁盘（请以管理员身份运行）";
                return false;
            }
            if (!RecoveryDisk.WriteRaw(handle, 0, data, data.Length))
            {
                error = "写入分区表失败（磁盘可能被系统占用）";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static List<PartitionEntry> SearchLostPartitions(int diskNumber,
        long maxBytes, CancellationToken token,
        Action<int, string>? progress)
    {
        var results = new List<PartitionEntry>();
        try
        {
            using var handle = RecoveryDisk.OpenPhysicalDisk(diskNumber);
            if (handle == null)
            {
                return results;
            }
            long length = RecoveryDisk.GetLength(handle);
            long limit = maxBytes > 0 ? Math.Min(length, maxBytes) : length;
            const int chunk = 1024 * 1024;
            long position = 0;
            var seen = new HashSet<long>();
            while (position < limit && !token.IsCancellationRequested)
            {
                int want = (int)Math.Min(chunk, limit - position);
                byte[] data = RecoveryDisk.ReadAt(handle, position, want, 4096);
                if (data.Length < 4096)
                {
                    break;
                }
                for (int offset = 0; offset + 4096 <= data.Length; offset += 512)
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }
                    long absolute = position + offset;
                    long lba = absolute / 512;
                    string fs = "";
                    if (data[offset + 3] == (byte)'N'
                        && data[offset + 4] == (byte)'T'
                        && data[offset + 5] == (byte)'F'
                        && data[offset + 6] == (byte)'S')
                    {
                        fs = "NTFS";
                    }
                    else if (data[offset + 0x52] == (byte)'F'
                        && data[offset + 0x53] == (byte)'A'
                        && data[offset + 0x54] == (byte)'T'
                        && data[offset + 0x55] == (byte)'3'
                        && data[offset + 0x56] == (byte)'2')
                    {
                        fs = "FAT32";
                    }
                    else if (data[offset + 0x36] == (byte)'F'
                        && data[offset + 0x37] == (byte)'A'
                        && data[offset + 0x38] == (byte)'T'
                        && data[offset + 0x39] == (byte)'1'
                        && data[offset + 0x3A] == (byte)'6')
                    {
                        fs = "FAT16";
                    }
                    else if (data[offset + 0x438] == 0x53
                        && data[offset + 0x439] == 0xEF)
                    {
                        fs = "ext2/3/4";
                    }
                    if (fs.Length == 0 || !seen.Add(lba))
                    {
                        continue;
                    }
                    long sectors = 0;
                    if (fs == "NTFS")
                    {
                        sectors = BitConverter.ToInt64(data, offset + 0x28);
                    }
                    else if (fs is "FAT32" or "FAT16")
                    {
                        sectors = BitConverter.ToUInt16(data, offset + 0x13);
                        if (sectors == 0)
                        {
                            sectors = BitConverter.ToUInt32(data, offset + 0x20);
                        }
                    }
                    results.Add(new PartitionEntry
                    {
                        Table = "搜索",
                        Type = fs + " 候选分区",
                        StartLba = lba,
                        Sectors = Math.Max(1, sectors),
                        FileSystem = fs,
                        Name = fs + "（偏移 " + RecoveryDisk.FormatSize(absolute) + "）"
                    });
                }
                position += chunk;
                progress?.Invoke((int)Math.Min(100,
                    position * 100 / Math.Max(1, limit)),
                    "搜索偏移 " + RecoveryDisk.FormatSize(position)
                    + "，已找到 " + results.Count + " 个候选");
            }
            progress?.Invoke(100, "搜索完成");
        }
        catch (Exception ex)
        {
            progress?.Invoke(100, "搜索失败：" + ex.Message);
        }
        return results;
    }
}
