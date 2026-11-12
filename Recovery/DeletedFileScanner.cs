using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MiniMonitor.Recovery;

/// <summary>NTFS 数据运行（VCN → LCN）。</summary>
public sealed class DataRun
{
    public long Vcn;
    public long Lcn;
    public long LengthClusters;
}

/// <summary>扫描到的可恢复文件。</summary>
public sealed class RecoverableFile
{
    public string Name = "";
    public string OriginalPath = "";
    public string FileSystem = "";
    public string Extension = "";
    public long Size;
    public DateTime Modified;
    public string Status = "";
    public bool Resident;
    public byte[]? ResidentData;
    public List<DataRun> Runs = new();
    public long RecordNumber;
    public int Index;
    public int ClusterSize;

    public override string ToString() => Name;
}

public sealed class DeletedScanResult
{
    public List<RecoverableFile> Files = new();
    public string Volume = "";
    public string FileSystem = "";
    public string Error = "";
}

/// <summary>
/// NTFS MFT / FAT 目录表已删除文件扫描器（Kickass Undelete 核心逻辑）。
/// 读取原始卷，解析已删除文件记录、大小、时间、数据运行与簇占用情况。
/// </summary>
public static class DeletedFileScanner
{
    private sealed class NtfsRecord
    {
        public long Number;
        public bool InUse;
        public bool IsDirectory;
        public string Name = "";
        public long Parent = -1;
        public long Size;
        public DateTime Modified;
        public bool HasData;
        public bool Resident;
        public byte[]? ResidentData;
        public List<DataRun> Runs = new();
    }

    private sealed class NtfsBoot
    {
        public int BytesPerSector;
        public int SectorsPerCluster;
        public int ClusterSize;
        public long MftCluster;
        public int RecordSize;
        public long TotalSectors;
        public long VolumeSize;
    }

    // ================= 入口 =================

    public static DeletedScanResult Scan(RecoveryVolume volume,
        CancellationToken token, Action<int, string>? progress)
    {
        var result = new DeletedScanResult
        {
            Volume = volume.RootPath,
            FileSystem = volume.FileSystem
        };
        try
        {
            using var handle = RecoveryDisk.OpenVolume(volume.RootPath);
            if (handle == null)
            {
                result.Error = "无法打开卷（请以管理员身份运行，并确认磁盘未处于独占状态）";
                return result;
            }
            byte[] boot = RecoveryDisk.ReadAt(handle, 0, 512);
            if (boot.Length < 512)
            {
                result.Error = "无法读取卷引导扇区";
                return result;
            }
            string oem = Encoding.ASCII.GetString(boot, 3, 8).Trim();
            if (oem.StartsWith("NTFS", StringComparison.OrdinalIgnoreCase))
            {
                result.FileSystem = "NTFS";
                ScanNtfs(handle, boot, volume, result, token, progress);
            }
            else if (oem.StartsWith("EXFAT", StringComparison.OrdinalIgnoreCase))
            {
                result.FileSystem = "exFAT";
                result.Error = "exFAT 已删除目录项暂未支持，请使用 PhotoRec 选项卡按文件签名恢复。";
            }
            else if (oem.StartsWith("FAT", StringComparison.OrdinalIgnoreCase))
            {
                result.FileSystem = "FAT";
                ScanFat(handle, boot, volume, result, token, progress);
            }
            else
            {
                result.Error = "未识别的文件系统：" + oem + "。可尝试 PhotoRec 选项卡按签名恢复。";
            }
        }
        catch (OperationCanceledException)
        {
            result.Error = "已停止";
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }
        return result;
    }

    // ================= NTFS =================

    private static void ScanNtfs(SafeFileHandle handle, byte[] boot,
        RecoveryVolume volume, DeletedScanResult result,
        CancellationToken token, Action<int, string>? progress)
    {
        var info = new NtfsBoot
        {
            BytesPerSector = BitConverter.ToUInt16(boot, 0x0B),
            SectorsPerCluster = boot[0x0D],
            MftCluster = BitConverter.ToInt64(boot, 0x30),
            TotalSectors = BitConverter.ToInt64(boot, 0x28)
        };
        if (info.BytesPerSector <= 0)
        {
            info.BytesPerSector = 512;
        }
        if (info.SectorsPerCluster <= 0)
        {
            info.SectorsPerCluster = 8;
        }
        info.ClusterSize = info.BytesPerSector * info.SectorsPerCluster;
        info.VolumeSize = info.TotalSectors * info.BytesPerSector;
        sbyte recordClusters = unchecked((sbyte)boot[0x40]);
        info.RecordSize = recordClusters > 0
            ? recordClusters * info.ClusterSize
            : 1 << Math.Min(20, -recordClusters);
        if (info.RecordSize < 256 || info.RecordSize > 65536)
        {
            info.RecordSize = 1024;
        }
        progress?.Invoke(1, "读取 NTFS $MFT 主文件表…");
        byte[] record0 = RecoveryDisk.ReadAt(handle,
            info.MftCluster * info.ClusterSize, info.RecordSize,
            info.BytesPerSector);
        if (record0.Length < info.RecordSize)
        {
            result.Error = "无法读取 $MFT 起始记录";
            return;
        }
        ApplyFixup(record0, info.BytesPerSector);
        var mftRecord = ParseNtfsRecord(record0, 0);
        if (mftRecord == null || mftRecord.Runs.Count == 0)
        {
            result.Error = "无法解析 $MFT 数据运行";
            return;
        }
        long mftSize = mftRecord.Size > 0
            ? mftRecord.Size
            : mftRecord.Runs.Sum(run => run.LengthClusters * info.ClusterSize);
        long totalRecords = Math.Min(2_000_000, Math.Max(1, mftSize / info.RecordSize));
        var records = new Dictionary<long, NtfsRecord>();
        long parsed = 0;
        long recordIndex = 0;
        int invalidStreak = 0;
        foreach (var run in mftRecord.Runs)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }
            if (run.Lcn <= 0 || run.LengthClusters <= 0)
            {
                continue;
            }
            long bytes = run.LengthClusters * info.ClusterSize;
            long offset = run.Lcn * info.ClusterSize;
            long readBytes = 0;
            const int chunk = 4 * 1024 * 1024;
            while (readBytes < bytes && recordIndex < totalRecords)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }
                int toRead = (int)Math.Min(chunk, Math.Min(bytes - readBytes,
                    (totalRecords - recordIndex) * info.RecordSize));
                if (toRead < info.RecordSize)
                {
                    break;
                }
                byte[] buffer = RecoveryDisk.ReadAt(handle, offset + readBytes,
                    toRead, info.BytesPerSector);
                if (buffer.Length < info.RecordSize)
                {
                    break;
                }
                int recordsInBuffer = buffer.Length / info.RecordSize;
                for (int i = 0; i < recordsInBuffer && recordIndex < totalRecords; i++)
                {
                    int recordOffset = i * info.RecordSize;
                    byte[] record = new byte[info.RecordSize];
                    Buffer.BlockCopy(buffer, recordOffset, record, 0, info.RecordSize);
                    if (record[0] != (byte)'F' || record[1] != (byte)'I'
                        || record[2] != (byte)'L' || record[3] != (byte)'E')
                    {
                        invalidStreak++;
                        if (invalidStreak > 4096 && records.Count > 0)
                        {
                            break;
                        }
                        recordIndex++;
                        continue;
                    }
                    invalidStreak = 0;
                    ApplyFixup(record, info.BytesPerSector);
                    var parsedRecord = ParseNtfsRecord(record, recordIndex);
                    if (parsedRecord != null)
                    {
                        records[parsedRecord.Number] = parsedRecord;
                        parsed++;
                    }
                    recordIndex++;
                }
                readBytes += buffer.Length;
                if (invalidStreak > 4096 && records.Count > 0)
                {
                    break;
                }
                if (parsed % 5000 < recordsInBuffer)
                {
                    int percent = (int)Math.Min(95,
                        recordIndex * 100 / Math.Max(1, totalRecords));
                    progress?.Invoke(percent,
                        "解析 NTFS 记录 " + recordIndex + " / " + totalRecords);
                }
            }
            if (recordIndex >= totalRecords)
            {
                break;
            }
        }
        progress?.Invoke(96, "读取 NTFS $Bitmap 簇位图…");
        byte[]? bitmap = ReadNtfsBitmap(handle, records, info, token);
        long allocatedClusters = 0;
        if (bitmap != null)
        {
            for (int i = 0; i < bitmap.Length * 8L; i++)
            {
                if ((bitmap[i >> 3] & (1 << (i & 7))) != 0)
                {
                    allocatedClusters++;
                }
            }
        }
        progress?.Invoke(98, "生成已删除文件列表…");
        int index = 0;
        foreach (var record in records.Values)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }
            if (record.InUse || record.IsDirectory || record.Name.Length == 0
                || record.Name.StartsWith("$", StringComparison.Ordinal))
            {
                continue;
            }
            if (!record.HasData && record.Size == 0)
            {
                continue;
            }
            var file = new RecoverableFile
            {
                Name = record.Name,
                OriginalPath = BuildNtfsPath(record, records, volume.RootPath),
                FileSystem = "NTFS",
                Extension = Path.GetExtension(record.Name),
                Size = record.Size,
                Modified = record.Modified,
                Resident = record.Resident,
                ResidentData = record.ResidentData,
                Runs = record.Runs,
                RecordNumber = record.Number,
                ClusterSize = info.ClusterSize,
                Index = index++
            };
            file.Status = EvaluateNtfsStatus(file, bitmap, info.ClusterSize);
            result.Files.Add(file);
        }
        result.Files.Sort((left, right) =>
            string.Compare(left.OriginalPath, right.OriginalPath,
                StringComparison.CurrentCultureIgnoreCase));
        progress?.Invoke(100, "扫描完成");
    }

    private static void ApplyFixup(byte[] record, int sectorSize)
    {
        if (record.Length < 48)
        {
            return;
        }
        ushort usaOffset = BitConverter.ToUInt16(record, 4);
        ushort usaCount = BitConverter.ToUInt16(record, 6);
        if (usaOffset == 0 || usaCount < 2 || usaOffset + usaCount * 2 > record.Length)
        {
            return;
        }
        int sectors = Math.Min(usaCount - 1,
            (record.Length - usaOffset) / 2 - 1);
        for (int i = 0; i < sectors; i++)
        {
            int position = (i + 1) * sectorSize - 2;
            if (position + 1 >= record.Length)
            {
                break;
            }
            record[position] = record[usaOffset + 2 + i * 2];
            record[position + 1] = record[usaOffset + 3 + i * 2];
        }
    }

    private static NtfsRecord? ParseNtfsRecord(byte[] record, long fallbackNumber)
    {
        try
        {
            ushort firstAttribute = BitConverter.ToUInt16(record, 0x14);
            ushort flags = BitConverter.ToUInt16(record, 0x16);
            uint recordNumber = BitConverter.ToUInt32(record, 0x2C);
            if (recordNumber == 0 || recordNumber > 0x0FFFFFFF)
            {
                recordNumber = (uint)fallbackNumber;
            }
            var info = new NtfsRecord
            {
                Number = recordNumber,
                InUse = (flags & 0x01) != 0,
                IsDirectory = (flags & 0x02) != 0,
                Parent = -1
            };
            bool hasFileName = false;
            bool hasData = false;
            int offset = firstAttribute;
            while (offset > 0 && offset + 8 <= record.Length)
            {
                uint type = BitConverter.ToUInt32(record, offset);
                if (type == 0xFFFFFFFF)
                {
                    break;
                }
                uint length = BitConverter.ToUInt32(record, offset + 4);
                if (length < 16 || offset + length > record.Length)
                {
                    break;
                }
                bool nonResident = record[offset + 8] != 0;
                if (type == 0x30 && !hasFileName)
                {
                    int valueLength = (int)BitConverter.ToUInt32(record, offset + 0x10);
                    int valueOffset = BitConverter.ToUInt16(record, offset + 0x14);
                    if (valueOffset + valueLength <= length && valueLength >= 66)
                    {
                        ulong parentRef = BitConverter.ToUInt64(record,
                            offset + valueOffset);
                        long parent = (long)(parentRef & 0x0000FFFFFFFFFFFF);
                        int nameLength = record[offset + valueOffset + 64];
                        int nameSpace = record[offset + valueOffset + 65];
                        int maxName = Math.Min(nameLength,
                            (valueLength - 66) / 2);
                        if (nameSpace != 2 && maxName > 0)
                        {
                            info.Name = Encoding.Unicode.GetString(record,
                                offset + valueOffset + 66, maxName * 2);
                            info.Parent = parent;
                            long modified = BitConverter.ToInt64(record,
                                offset + valueOffset + 16);
                            info.Modified = FileTimeToDateTime(modified);
                            hasFileName = true;
                        }
                    }
                }
                else if (type == 0x80 && !hasData)
                {
                    int nameLength = record[offset + 9];
                    int nameOffset = BitConverter.ToUInt16(record, offset + 10);
                    bool unnamed = nameLength == 0;
                    if (!unnamed && nameOffset + nameLength * 2 <= length)
                    {
                        string attrName = Encoding.Unicode.GetString(record,
                            offset + nameOffset, nameLength * 2);
                        unnamed = attrName.Length == 0;
                    }
                    if (unnamed)
                    {
                        if (nonResident)
                        {
                            info.Size = BitConverter.ToInt64(record, offset + 0x30);
                            int runOffset = BitConverter.ToUInt16(record, offset + 0x20);
                            info.Runs = ParseRuns(record, offset + runOffset,
                                offset + (int)length);
                            info.HasData = true;
                        }
                        else
                        {
                            int valueLength = (int)BitConverter.ToUInt32(record, offset + 0x10);
                            int valueOffset = BitConverter.ToUInt16(record, offset + 0x14);
                            if (valueOffset + valueLength <= length)
                            {
                                info.Size = valueLength;
                                info.Resident = true;
                                info.ResidentData = new byte[valueLength];
                                Buffer.BlockCopy(record, offset + valueOffset,
                                    info.ResidentData, 0, valueLength);
                                info.HasData = true;
                            }
                        }
                        hasData = true;
                    }
                }
                else if (type == 0x10 && info.Modified == DateTime.MinValue)
                {
                    int valueLength = (int)BitConverter.ToUInt32(record, offset + 0x10);
                    int valueOffset = BitConverter.ToUInt16(record, offset + 0x14);
                    if (!nonResident && valueOffset + valueLength <= length
                        && valueLength >= 16)
                    {
                        info.Modified = FileTimeToDateTime(
                            BitConverter.ToInt64(record, offset + valueOffset + 8));
                    }
                }
                offset += (int)length;
            }
            return info;
        }
        catch
        {
            return null;
        }
    }

    private static List<DataRun> ParseRuns(byte[] data, int offset, int end)
    {
        var runs = new List<DataRun>();
        long vcn = 0;
        long lcn = 0;
        while (offset < end && offset < data.Length)
        {
            byte header = data[offset++];
            if (header == 0)
            {
                break;
            }
            int lengthSize = header & 0x0F;
            int offsetSize = (header >> 4) & 0x0F;
            if (lengthSize == 0 || offsetSize == 0
                || offset + lengthSize + offsetSize > data.Length)
            {
                break;
            }
            long length = ReadVariable(data, offset, lengthSize);
            offset += lengthSize;
            long delta = ReadSignedVariable(data, offset, offsetSize);
            offset += offsetSize;
            if (length <= 0)
            {
                break;
            }
            if (delta != 0)
            {
                lcn += delta;
                runs.Add(new DataRun
                {
                    Vcn = vcn,
                    Lcn = lcn,
                    LengthClusters = length
                });
            }
            vcn += length;
        }
        return runs;
    }

    private static long ReadVariable(byte[] data, int offset, int size)
    {
        long value = 0;
        for (int i = 0; i < size && offset + i < data.Length; i++)
        {
            value |= (long)data[offset + i] << (i * 8);
        }
        return value;
    }

    private static long ReadSignedVariable(byte[] data, int offset, int size)
    {
        if (size <= 0 || size > 8 || offset + size > data.Length)
        {
            return 0;
        }
        long value = ReadVariable(data, offset, size);
        long signBit = 1L << (size * 8 - 1);
        if ((value & signBit) != 0)
        {
            value |= -1L << (size * 8);
        }
        return value;
    }

    private static byte[]? ReadNtfsBitmap(SafeFileHandle handle,
        Dictionary<long, NtfsRecord> records, NtfsBoot info,
        CancellationToken token)
    {
        try
        {
            if (!records.TryGetValue(6, out var bitmapRecord)
                || bitmapRecord.Runs.Count == 0)
            {
                return null;
            }
            long total = bitmapRecord.Size > 0
                ? bitmapRecord.Size
                : bitmapRecord.Runs.Sum(run => run.LengthClusters * info.ClusterSize);
            total = Math.Min(total, 256L * 1024 * 1024);
            var data = new byte[total];
            int written = 0;
            foreach (var run in bitmapRecord.Runs)
            {
                if (token.IsCancellationRequested || written >= data.Length)
                {
                    break;
                }
                long bytes = run.LengthClusters * info.ClusterSize;
                int count = (int)Math.Min(bytes, data.Length - written);
                if (run.Lcn <= 0)
                {
                    written += count;
                    continue;
                }
                byte[] chunk = RecoveryDisk.ReadAt(handle,
                    run.Lcn * info.ClusterSize, count, info.BytesPerSector);
                Buffer.BlockCopy(chunk, 0, data, written,
                    Math.Min(chunk.Length, count));
                written += count;
            }
            return data;
        }
        catch
        {
            return null;
        }
    }

    private static string EvaluateNtfsStatus(RecoverableFile file,
        byte[]? bitmap, int clusterSize)
    {
        if (file.Resident)
        {
            return "良好（驻留数据）";
        }
        if (file.Runs.Count == 0 || bitmap == null)
        {
            return "未知";
        }
        long allocated = 0;
        long total = 0;
        // 大文件可能包含数百万个簇，按最多 1024 个采样点估算，避免扫描界面卡死。
        const long maxChecks = 1024;
        long checks = 0;
        foreach (var run in file.Runs)
        {
            if (run.LengthClusters <= 0)
            {
                continue;
            }
            long step = Math.Max(1, run.LengthClusters / 64);
            for (long i = 0; i < run.LengthClusters && checks < maxChecks; i += step)
            {
                long lcn = run.Lcn + i;
                if (lcn < 0)
                {
                    continue;
                }
                total++;
                checks++;
                long bit = lcn;
                if (bit >> 3 < bitmap.Length
                    && (bitmap[bit >> 3] & (1 << (int)(bit & 7))) != 0)
                {
                    allocated++;
                }
            }
            if (checks >= maxChecks)
            {
                break;
            }
        }
        if (total == 0)
        {
            return "未知";
        }
        if (allocated == 0)
        {
            return "良好（簇空闲）";
        }
        if (allocated >= total)
        {
            return "已覆盖";
        }
        return "部分覆盖（" + (allocated * 100 / total) + "%）";
    }

    private static string BuildNtfsPath(NtfsRecord record,
        Dictionary<long, NtfsRecord> records, string volumeRoot)
    {
        var parts = new List<string>();
        var current = record;
        var visited = new HashSet<long>();
        int depth = 0;
        while (current != null && depth++ < 64)
        {
            if (current.Number == 5)
            {
                break;
            }
            if (!visited.Add(current.Number))
            {
                break;
            }
            if (current.Name.Length > 0)
            {
                parts.Add(current.Name);
            }
            if (current.Parent < 0
                || !records.TryGetValue(current.Parent, out var parent))
            {
                break;
            }
            current = parent;
        }
        parts.Reverse();
        string relative = string.Join("\\", parts);
        return volumeRoot + relative;
    }

    private static DateTime FileTimeToDateTime(long fileTime)
    {
        try
        {
            if (fileTime <= 0)
            {
                return DateTime.MinValue;
            }
            return DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    // ================= FAT =================

    private static void ScanFat(SafeFileHandle handle, byte[] boot,
        RecoveryVolume volume, DeletedScanResult result,
        CancellationToken token, Action<int, string>? progress)
    {
        int bytesPerSector = BitConverter.ToUInt16(boot, 0x0B);
        int sectorsPerCluster = boot[0x0D];
        int reserved = BitConverter.ToUInt16(boot, 0x0E);
        int fatCount = boot[0x10];
        int rootEntries = BitConverter.ToUInt16(boot, 0x11);
        int fatSize16 = BitConverter.ToUInt16(boot, 0x16);
        long totalSectors = BitConverter.ToUInt16(boot, 0x13);
        if (totalSectors == 0)
        {
            totalSectors = BitConverter.ToUInt32(boot, 0x20);
        }
        long fatSize = fatSize16 != 0
            ? fatSize16
            : BitConverter.ToUInt32(boot, 0x24);
        bool fat32 = fatSize16 == 0;
        uint rootCluster = fat32 ? BitConverter.ToUInt32(boot, 0x2C) : 0;
        if (bytesPerSector <= 0)
        {
            bytesPerSector = 512;
        }
        if (sectorsPerCluster <= 0)
        {
            sectorsPerCluster = 8;
        }
        int clusterSize = bytesPerSector * sectorsPerCluster;
        long fatOffset = (long)reserved * bytesPerSector;
        long rootOffset = (long)(reserved + fatCount * fatSize) * bytesPerSector;
        long dataOffset = fat32
            ? rootOffset
            : rootOffset + (long)rootEntries * 32;
        long dataSectors = totalSectors
            - (reserved + fatCount * fatSize + (fat32 ? 0 : (rootEntries * 32 / bytesPerSector)));
        long clusterCount = dataSectors / sectorsPerCluster;
        progress?.Invoke(2, "读取 FAT 表…");
        byte[] fat = fatSize > 0 && fatSize * bytesPerSector < 512L * 1024 * 1024
            ? RecoveryDisk.ReadAt(handle, fatOffset,
                (int)(fatSize * bytesPerSector), bytesPerSector)
            : Array.Empty<byte>();
        var visited = new HashSet<uint>();
        int index = 0;
        void ScanDirectory(uint startCluster, int rootSize, int depth, string prefix)
        {
            if (token.IsCancellationRequested || depth > 32)
            {
                return;
            }
            byte[] buffer;
            if (rootSize > 0)
            {
                buffer = RecoveryDisk.ReadAt(handle, rootOffset, rootSize,
                    bytesPerSector);
            }
            else
            {
                buffer = ReadFatChain(handle, fat, fat32, dataOffset,
                    clusterSize, startCluster, token);
            }
            if (buffer.Length < 32)
            {
                return;
            }
            var longName = new StringBuilder();
            for (int offset = 0; offset + 32 <= buffer.Length; offset += 32)
            {
                byte first = buffer[offset];
                if (first == 0x00)
                {
                    break;
                }
                byte attr = buffer[offset + 11];
                if (attr == 0x0F)
                {
                    var chunk = new StringBuilder(13);
                    for (int p = 0; p < 13; p++)
                    {
                        int position = p switch
                        {
                            < 5 => offset + 1 + p * 2,
                            < 11 => offset + 14 + (p - 5) * 2,
                            _ => offset + 28 + (p - 11) * 2
                        };
                        char ch = (char)BitConverter.ToUInt16(buffer, position);
                        if (ch == '\0' || ch == '\uFFFF')
                        {
                            continue;
                        }
                        chunk.Append(ch);
                    }
                    // LFN 目录项按倒序出现，需要前插才能恢复正确顺序。
                    longName.Insert(0, chunk.ToString());
                    continue;
                }
                if (attr == 0x08)
                {
                    longName.Clear();
                    continue;
                }
                bool deleted = first == 0xE5;
                bool directory = (attr & 0x10) != 0;
                string shortName = DecodeFatShortName(buffer, offset);
                string name = longName.Length > 0 ? longName.ToString() : shortName;
                longName.Clear();
                uint cluster = (uint)(BitConverter.ToUInt16(buffer, offset + 26)
                    | (fat32 ? BitConverter.ToUInt16(buffer, offset + 20) << 16 : 0));
                long size = BitConverter.ToUInt32(buffer, offset + 28);
                DateTime modified = DecodeFatDateTime(buffer, offset);
                if (directory)
                {
                    if (!deleted && cluster >= 2 && visited.Add(cluster))
                    {
                        ScanDirectory(cluster, 0, depth + 1, prefix + name + "\\");
                    }
                    continue;
                }
                if (!deleted || size <= 0 || name.Length == 0)
                {
                    continue;
                }
                var file = new RecoverableFile
                {
                    Name = name,
                    OriginalPath = volume.RootPath + prefix + name,
                    FileSystem = "FAT",
                    Extension = Path.GetExtension(name),
                    Size = size,
                    Modified = modified,
                    ClusterSize = clusterSize,
                    Index = index++,
                    Status = "可能可恢复（连续簇）"
                };
                long clusters = (size + clusterSize - 1) / clusterSize;
                if (cluster >= 2)
                {
                    file.Runs.Add(new DataRun
                    {
                        Vcn = 0,
                        Lcn = cluster,
                        LengthClusters = clusters
                    });
                }
                result.Files.Add(file);
                if (result.Files.Count % 1000 == 0)
                {
                    progress?.Invoke(50, "已发现 " + result.Files.Count + " 个 FAT 删除项…");
                }
            }
        }
        if (fat32)
        {
            ScanDirectory(rootCluster, 0, 0, "");
        }
        else
        {
            ScanDirectory(0, rootEntries * 32, 0, "");
        }
        progress?.Invoke(100, "扫描完成");
    }

    private static byte[] ReadFatChain(SafeFileHandle handle, byte[] fat,
        bool fat32, long dataOffset, int clusterSize, uint start,
        CancellationToken token)
    {
        var result = new MemoryStream();
        uint cluster = start;
        int guard = 0;
        while (cluster >= 2 && guard++ < 1_000_000)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }
            byte[] data = RecoveryDisk.ReadAt(handle,
                dataOffset + (long)(cluster - 2) * clusterSize, clusterSize);
            if (data.Length == 0)
            {
                break;
            }
            result.Write(data, 0, data.Length);
            uint next = fat32
                ? ReadFat32(fat, cluster)
                : ReadFat16(fat, cluster);
            if (next >= 0x0FFFFFF8 || next == 0xFFFF || next == 0xFFF
                || next < 2 || next == cluster)
            {
                break;
            }
            cluster = next;
        }
        return result.ToArray();
    }

    private static uint ReadFat32(byte[] fat, uint cluster)
    {
        long offset = cluster * 4L;
        if (offset + 4 > fat.Length)
        {
            return 0x0FFFFFFF;
        }
        return BitConverter.ToUInt32(fat, (int)offset) & 0x0FFFFFFF;
    }

    private static uint ReadFat16(byte[] fat, uint cluster)
    {
        if (cluster * 2L + 2 > fat.Length)
        {
            return 0xFFFF;
        }
        return BitConverter.ToUInt16(fat, (int)(cluster * 2));
    }

    private static string DecodeFatShortName(byte[] buffer, int offset)
    {
        var name = new StringBuilder();
        for (int i = 0; i < 8; i++)
        {
            byte b = buffer[offset + i];
            if (b == 0x20)
            {
                break;
            }
            name.Append(i == 0 && b == 0xE5 ? '?' : (char)b);
        }
        var ext = new StringBuilder();
        for (int i = 8; i < 11; i++)
        {
            byte b = buffer[offset + i];
            if (b == 0x20)
            {
                break;
            }
            ext.Append((char)b);
        }
        return ext.Length > 0 ? name + "." + ext : name.ToString();
    }

    private static DateTime DecodeFatDateTime(byte[] buffer, int offset)
    {
        try
        {
            ushort time = BitConverter.ToUInt16(buffer, offset + 22);
            ushort date = BitConverter.ToUInt16(buffer, offset + 24);
            int year = 1980 + ((date >> 9) & 0x7F);
            int month = (date >> 5) & 0x0F;
            int day = date & 0x1F;
            int hour = (time >> 11) & 0x1F;
            int minute = (time >> 5) & 0x3F;
            int second = (time & 0x1F) * 2;
            if (month is < 1 or > 12 || day is < 1 or > 31)
            {
                return DateTime.MinValue;
            }
            return new DateTime(year, month, day,
                Math.Min(hour, 23), Math.Min(minute, 59), Math.Min(second, 59));
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    // ================= 恢复 =================

    public static bool Recover(RecoverableFile file, RecoveryVolume volume,
        string outputRoot, out string outputPath, out string error)
    {
        outputPath = "";
        error = "";
        try
        {
            string safeRoot = outputRoot.TrimEnd('\\');
            string relative = file.OriginalPath;
            if (relative.StartsWith(volume.RootPath,
                StringComparison.OrdinalIgnoreCase))
            {
                relative = relative[volume.RootPath.Length..];
            }
            relative = relative.TrimStart('\\', '/');
            if (relative.Length == 0)
            {
                relative = file.Name;
            }
            var parts = relative.Split(new[] { '\\', '/' },
                StringSplitOptions.RemoveEmptyEntries);
            var sanitized = parts.Select(SanitizeSegment).ToArray();
            string directory = Path.Combine(new[] { safeRoot, volume.RootPath[..1] }
                .Concat(sanitized.Take(Math.Max(0, sanitized.Length - 1))).ToArray());
            Directory.CreateDirectory(directory);
            string fileName = sanitized.Length > 0
                ? sanitized[^1] : "recovered_" + file.Index;
            string target = Path.Combine(directory, fileName);
            int suffix = 1;
            while (File.Exists(target))
            {
                target = Path.Combine(directory,
                    Path.GetFileNameWithoutExtension(fileName)
                    + "_" + suffix++ + Path.GetExtension(fileName));
            }
            if (file.Resident && file.ResidentData != null)
            {
                File.WriteAllBytes(target, file.ResidentData);
                outputPath = target;
                return true;
            }
            using var handle = RecoveryDisk.OpenVolume(volume.RootPath);
            if (handle == null)
            {
                error = "无法打开源卷进行恢复";
                return false;
            }
            using var output = new FileStream(target, FileMode.CreateNew,
                FileAccess.Write, FileShare.None);
            long remaining = file.Size;
            foreach (var run in file.Runs)
            {
                if (remaining <= 0)
                {
                    break;
                }
                long bytes = Math.Min(run.LengthClusters * file.ClusterSize, remaining);
                if (run.Lcn <= 0)
                {
                    byte[] zeros = new byte[Math.Min(bytes, 1024 * 1024)];
                    while (bytes > 0)
                    {
                        int count = (int)Math.Min(zeros.Length, bytes);
                        output.Write(zeros, 0, count);
                        bytes -= count;
                        remaining -= count;
                    }
                    continue;
                }
                long offset = run.Lcn * file.ClusterSize;
                while (bytes > 0)
                {
                    int count = (int)Math.Min(1024 * 1024, bytes);
                    byte[] data = RecoveryDisk.ReadAt(handle, offset, count);
                    if (data.Length == 0)
                    {
                        break;
                    }
                    output.Write(data, 0, Math.Min(data.Length, count));
                    bytes -= Math.Min(data.Length, count);
                    remaining -= Math.Min(data.Length, count);
                    offset += Math.Min(data.Length, count);
                }
            }
            output.Flush();
            outputPath = target;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string SanitizeSegment(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (char ch in name)
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }
        string result = builder.ToString().Trim();
        if (result.Length == 0)
        {
            result = "unknown";
        }
        return result;
    }
}
