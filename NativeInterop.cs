using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace MiniMonitor;

/// <summary>
/// 本机磁盘枚举（kernel32 P/Invoke）。不依赖 System.IO.FileSystem.DriveInfo 程序集，
/// 避免单文件发布时该程序集缺失导致功能在 JIT 阶段直接抛 FileNotFoundException。
/// </summary>
internal static class DriveEnumerator
{
    public enum DriveKind
    {
        Unknown,
        Fixed,
        Removable,
        Network,
        CdRom,
        Ram
    }

    public readonly record struct DriveEntry(
        string Root, DriveKind Kind, bool IsReady, long TotalSize, long FreeSpace);

    private const int MaxDriveStrings = 2048;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetLogicalDriveStrings(int length, [Out] char[] buffer);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetDriveType(string root);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(string directory,
        out ulong freeBytesAvailable, out ulong totalBytes, out ulong totalFreeBytes);

    public static List<DriveEntry> GetDrives()
    {
        var entries = new List<DriveEntry>();
        var buffer = new char[MaxDriveStrings];
        int length = GetLogicalDriveStrings(buffer.Length, buffer);
        if (length <= 0)
        {
            return entries;
        }
        string all = new string(buffer, 0,
            Math.Min(length, buffer.Length));
        foreach (string root in all.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            DriveKind kind = GetDriveType(root) switch
            {
                2 => DriveKind.Removable,
                3 => DriveKind.Fixed,
                4 => DriveKind.Network,
                5 => DriveKind.CdRom,
                6 => DriveKind.Ram,
                _ => DriveKind.Unknown
            };
            bool ready = GetDiskFreeSpaceEx(root,
                out ulong free, out ulong total, out _);
            entries.Add(new DriveEntry(root, kind, ready,
                ready ? ToInt64(total) : 0,
                ready ? ToInt64(free) : 0));
        }
        return entries;
    }

    public static bool TryGetSpace(string path, out long total, out long free)
    {
        total = 0;
        free = 0;
        if (!GetDiskFreeSpaceEx(path, out ulong available, out ulong totalBytes, out _))
        {
            return false;
        }
        total = ToInt64(totalBytes);
        free = ToInt64(available);
        return true;
    }

    private static long ToInt64(ulong value) =>
        value > long.MaxValue ? long.MaxValue : (long)value;
}

/// <summary>
/// Windows DPAPI（crypt32 P/Invoke，当前用户范围）。不依赖
/// System.Security.Cryptography.ProtectedData 程序集，避免单文件发布时缺失。
/// </summary>
internal static class Dpapi
{
    private const int CryptProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string? description,
        IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description,
        IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static byte[] Protect(byte[] data)
    {
        if (data.Length == 0)
        {
            return Array.Empty<byte>();
        }
        IntPtr input = Marshal.AllocHGlobal(data.Length);
        var inBlob = new DataBlob { Size = data.Length, Data = input };
        var outBlob = new DataBlob();
        try
        {
            Marshal.Copy(data, 0, input, data.Length);
            if (!CryptProtectData(ref inBlob, null, IntPtr.Zero, IntPtr.Zero,
                IntPtr.Zero, CryptProtectUiForbidden, ref outBlob))
            {
                throw new CryptographicException(Marshal.GetLastWin32Error());
            }
            byte[] result = new byte[outBlob.Size];
            Marshal.Copy(outBlob.Data, result, 0, outBlob.Size);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(input);
            if (outBlob.Data != IntPtr.Zero)
            {
                LocalFree(outBlob.Data);
            }
        }
    }

    public static byte[] Unprotect(byte[] data)
    {
        if (data.Length == 0)
        {
            return Array.Empty<byte>();
        }
        IntPtr input = Marshal.AllocHGlobal(data.Length);
        var inBlob = new DataBlob { Size = data.Length, Data = input };
        var outBlob = new DataBlob();
        try
        {
            Marshal.Copy(data, 0, input, data.Length);
            if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                IntPtr.Zero, CryptProtectUiForbidden, ref outBlob))
            {
                throw new CryptographicException(Marshal.GetLastWin32Error());
            }
            byte[] result = new byte[outBlob.Size];
            Marshal.Copy(outBlob.Data, result, 0, outBlob.Size);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(input);
            if (outBlob.Data != IntPtr.Zero)
            {
                LocalFree(outBlob.Data);
            }
        }
    }
}
