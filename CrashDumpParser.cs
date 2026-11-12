using System.Diagnostics;
using System.Text;

namespace MiniMonitor;

/// <summary>一个蓝屏转储文件的解析结果。</summary>
public sealed class CrashDumpEntry
{
    public string FilePath = "";
    public string FileName = "";
    public DateTime CrashTime;
    public uint BugCheckCode;
    public ulong[] Parameters = new ulong[4];
    public string DumpType = "";
    public long FileSize;
    public string SuspectDriver = "";
    public string SuspectPath = "";
    public string ParseError = "";
    public BugCheckInfo? Info;
}

/// <summary>
/// Windows 内核转储（Minidump / MEMORY.DMP）解析器：
/// 读取 DUMP_HEADER64 / DUMP_HEADER32 的蓝屏代码与 4 个参数，
/// 并从内嵌的 MINIDUMP 模块列表中按参数地址推断可能相关驱动。
/// </summary>
public static class CrashDumpParser
{
    private sealed class ModuleRange
    {
        public ulong Base;
        public uint Size;
        public string Name = "";
    }

    /// <summary>扫描文件夹中的 .dmp；folder 为空时使用 C:\Windows\Minidump 与 MEMORY.DMP。</summary>
    public static List<CrashDumpEntry> Scan(string folder, CancellationToken token)
    {
        var list = new List<CrashDumpEntry>();
        var files = CollectDumpFiles(folder);
        foreach (string file in files)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }
            try
            {
                var entry = Parse(file);
                if (entry != null)
                {
                    list.Add(entry);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("解析转储失败 " + file + "：" + ex);
            }
        }
        list.Sort((left, right) => right.CrashTime.CompareTo(left.CrashTime));
        return list;
    }

    private static List<string> CollectDumpFiles(string folder)
    {
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (folder.Length > 0 && Directory.Exists(folder))
        {
            try
            {
                files.AddRange(Directory.EnumerateFiles(folder, "*.dmp",
                    SearchOption.TopDirectoryOnly));
            }
            catch
            {
            }
        }
        else
        {
            string windows = Environment.GetFolderPath(
                Environment.SpecialFolder.Windows);
            string minidump = Path.Combine(windows, "Minidump");
            if (Directory.Exists(minidump))
            {
                try
                {
                    files.AddRange(Directory.EnumerateFiles(minidump, "*.dmp",
                        SearchOption.TopDirectoryOnly));
                }
                catch
                {
                }
            }
            string memory = Path.Combine(windows, "MEMORY.DMP");
            if (File.Exists(memory))
            {
                files.Add(memory);
            }
        }
        var result = new List<string>();
        foreach (string file in files)
        {
            if (seen.Add(file))
            {
                result.Add(file);
            }
        }
        return result;
    }

    public static CrashDumpEntry? Parse(string path)
    {
        using var stream = new FileStream(path, FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite);
        long length = stream.Length;
        int headLength = (int)Math.Min(length, 0x10000);
        byte[] head = new byte[headLength];
        ReadExactly(stream, head, 0, headLength);
        var entry = new CrashDumpEntry
        {
            FilePath = path,
            FileName = Path.GetFileName(path),
            CrashTime = File.GetLastWriteTime(path),
            FileSize = length,
            DumpType = InferDumpType(length)
        };
        uint signature = headLength >= 4 ? BitConverter.ToUInt32(head, 0) : 0;
        int mdmpOffset = FindSignature(head, "MDMP");
        if (signature == 0x45474150 && headLength >= 0x60)
        {
            uint validDump = BitConverter.ToUInt32(head, 4);
            if (validDump == 0x504D5544) // "DUMP" = 32 位转储头
            {
                entry.BugCheckCode = BitConverter.ToUInt32(head, 0x28);
                for (int i = 0; i < 4; i++)
                {
                    entry.Parameters[i] = BitConverter.ToUInt32(head, 0x2C + i * 4);
                }
            }
            else
            {
                entry.BugCheckCode = BitConverter.ToUInt32(head, 0x38);
                for (int i = 0; i < 4; i++)
                {
                    entry.Parameters[i] = BitConverter.ToUInt64(head, 0x40 + i * 8);
                }
            }
        }
        else if (signature == 0x504D444D)
        {
            mdmpOffset = 0;
            ParseMinidumpException(stream, 0, head, entry);
        }
        else if (mdmpOffset >= 0)
        {
            ParseMinidumpException(stream, mdmpOffset, head, entry);
        }
        else
        {
            entry.ParseError = "无法识别的转储格式（缺少 PAGE / MDMP 签名）";
        }
        if (entry.BugCheckCode == 0 || entry.BugCheckCode == 0xFFFFFFFF)
        {
            entry.ParseError = entry.ParseError.Length > 0
                ? entry.ParseError : "未能从转储中解析出蓝屏代码";
        }
        else
        {
            entry.Info = BugCheckDatabase.Lookup(entry.BugCheckCode);
            if (mdmpOffset >= 0)
            {
                try
                {
                    var modules = ParseModules(stream, mdmpOffset);
                    AssignSuspectDriver(entry, modules);
                }
                catch
                {
                }
            }
        }
        return entry;
    }

    private static string InferDumpType(long length)
    {
        if (length < 8 * 1024 * 1024)
        {
            return "小型内存转储";
        }
        if (length < 512L * 1024 * 1024)
        {
            return "内核内存转储";
        }
        return "完整内存转储";
    }

    private static int FindSignature(byte[] data, string signature)
    {
        byte[] pattern = Encoding.ASCII.GetBytes(signature);
        for (int i = 0; i <= data.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                return i;
            }
        }
        return -1;
    }

    private static void ParseMinidumpException(FileStream stream, int mdmpOffset,
        byte[] head, CrashDumpEntry entry)
    {
        if (mdmpOffset + 32 > head.Length)
        {
            return;
        }
        uint streamCount = BitConverter.ToUInt32(head, mdmpOffset + 8);
        uint directoryRva = BitConverter.ToUInt32(head, mdmpOffset + 12);
        for (uint i = 0; i < streamCount && i < 512; i++)
        {
            long dirOffset = mdmpOffset + directoryRva + i * 12L;
            if (dirOffset + 12 > head.Length)
            {
                break;
            }
            int index = (int)dirOffset;
            uint streamType = BitConverter.ToUInt32(head, index);
            uint dataSize = BitConverter.ToUInt32(head, index + 4);
            uint dataRva = BitConverter.ToUInt32(head, index + 8);
            if (streamType != 6 || dataSize < 48)
            {
                continue;
            }
            byte[] data = ReadAt(stream, mdmpOffset + (long)dataRva,
                (int)Math.Min(dataSize, 256));
            if (data.Length < 48)
            {
                return;
            }
            entry.BugCheckCode = BitConverter.ToUInt32(data, 8);
            uint numberParameters = BitConverter.ToUInt32(data, 32);
            int count = (int)Math.Min(numberParameters, 4u);
            for (int p = 0; p < count; p++)
            {
                entry.Parameters[p] = BitConverter.ToUInt64(data, 40 + p * 8);
            }
            return;
        }
    }

    private static List<ModuleRange> ParseModules(FileStream stream, int mdmpOffset)
    {
        var modules = new List<ModuleRange>();
        byte[] header = ReadAt(stream, mdmpOffset, 0x4000);
        if (header.Length < 32)
        {
            return modules;
        }
        uint streamCount = BitConverter.ToUInt32(header, 8);
        uint directoryRva = BitConverter.ToUInt32(header, 12);
        for (uint i = 0; i < streamCount && i < 512; i++)
        {
            int dirIndex = (int)directoryRva + (int)i * 12;
            if (dirIndex + 12 > header.Length)
            {
                break;
            }
            uint streamType = BitConverter.ToUInt32(header, dirIndex);
            uint dataSize = BitConverter.ToUInt32(header, dirIndex + 4);
            uint dataRva = BitConverter.ToUInt32(header, dirIndex + 8);
            if (streamType != 4 || dataSize < 4)
            {
                continue;
            }
            byte[] listHeader = ReadAt(stream, mdmpOffset + (long)dataRva, 4);
            if (listHeader.Length < 4)
            {
                return modules;
            }
            uint count = BitConverter.ToUInt32(listHeader, 0);
            if (count > 4096)
            {
                count = 4096;
            }
            byte[] rows = ReadAt(stream, mdmpOffset + (long)dataRva + 4,
                (int)Math.Min(count * 108u, 16u * 1024 * 1024));
            for (int m = 0; m < count; m++)
            {
                int offset = m * 108;
                if (offset + 24 > rows.Length)
                {
                    break;
                }
                ulong baseAddress = BitConverter.ToUInt64(rows, offset);
                uint size = BitConverter.ToUInt32(rows, offset + 8);
                uint nameRva = BitConverter.ToUInt32(rows, offset + 20);
                string name = ReadMinidumpString(stream, mdmpOffset + nameRva);
                if (name.Length > 0)
                {
                    modules.Add(new ModuleRange
                    {
                        Base = baseAddress,
                        Size = size,
                        Name = name
                    });
                }
            }
            return modules;
        }
        return modules;
    }

    private static string ReadMinidumpString(FileStream stream, long offset)
    {
        byte[] lengthBytes = ReadAt(stream, offset, 4);
        if (lengthBytes.Length < 4)
        {
            return "";
        }
        uint length = BitConverter.ToUInt32(lengthBytes, 0);
        if (length == 0 || length > 2048)
        {
            return "";
        }
        byte[] text = ReadAt(stream, offset + 4, (int)length);
        return Encoding.Unicode.GetString(text).TrimEnd('\0');
    }

    private static void AssignSuspectDriver(CrashDumpEntry entry,
        List<ModuleRange> modules)
    {
        if (modules.Count == 0)
        {
            return;
        }
        // 常见蓝屏参数中，第 4 / 第 2 / 第 1 / 第 3 个参数可能指向出错指令或地址。
        int[] order = { 3, 1, 0, 2 };
        foreach (int index in order)
        {
            ulong address = entry.Parameters[index];
            if (address < 0xFFFF000000000000)
            {
                continue;
            }
            foreach (var module in modules)
            {
                if (address < module.Base || module.Size == 0)
                {
                    continue;
                }
                if (address >= module.Base + module.Size)
                {
                    continue;
                }
                string file = module.Name.Replace('/', '\\');
                int slash = file.LastIndexOf('\\');
                string shortName = slash >= 0 ? file[(slash + 1)..] : file;
                if (shortName.Equals("ntoskrnl.exe", StringComparison.OrdinalIgnoreCase)
                    || shortName.Equals("ntkrnlmp.exe", StringComparison.OrdinalIgnoreCase)
                    || shortName.Equals("hal.dll", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                entry.SuspectDriver = shortName;
                entry.SuspectPath = file;
                return;
            }
        }
    }

    private static byte[] ReadAt(FileStream stream, long offset, int count)
    {
        if (offset < 0 || offset >= stream.Length || count <= 0)
        {
            return Array.Empty<byte>();
        }
        count = (int)Math.Min(count, stream.Length - offset);
        var buffer = new byte[count];
        stream.Seek(offset, SeekOrigin.Begin);
        ReadExactly(stream, buffer, 0, count);
        return buffer;
    }

    private static void ReadExactly(FileStream stream, byte[] buffer,
        int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read = stream.Read(buffer, offset + total, count - total);
            if (read <= 0)
            {
                break;
            }
            total += read;
        }
    }
}
