using System.Runtime.InteropServices;
using System.Text;

namespace MiniMonitor.Rss;

/// <summary>
/// 使用 Windows DPAPI（当前用户）加密保存 RSS 服务密码，配置文件里不出现明文。
/// 解密失败（例如配置被复制到另一台电脑）时返回空字符串。
/// </summary>
internal static class RssSecrets
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
        IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description,
        IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    public static string Protect(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return "";
        }
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(plainText);
            IntPtr buffer = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, buffer, bytes.Length);
                var input = new DataBlob { Size = bytes.Length, Data = buffer };
                if (CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        CryptProtectUiForbidden, out var output))
                {
                    try
                    {
                        byte[] cipher = new byte[output.Size];
                        Marshal.Copy(output.Data, cipher, 0, output.Size);
                        return Convert.ToBase64String(cipher);
                    }
                    finally
                    {
                        if (output.Data != IntPtr.Zero)
                        {
                            LocalFree(output.Data);
                        }
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            // DPAPI 不可用时返回空，用户需要重新输入密码
        }
        return "";
    }

    public static string Unprotect(string? cipherBase64)
    {
        if (string.IsNullOrWhiteSpace(cipherBase64))
        {
            return "";
        }
        try
        {
            byte[] cipher = Convert.FromBase64String(cipherBase64);
            IntPtr buffer = Marshal.AllocHGlobal(cipher.Length);
            try
            {
                Marshal.Copy(cipher, 0, buffer, cipher.Length);
                var input = new DataBlob { Size = cipher.Length, Data = buffer };
                if (CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        CryptProtectUiForbidden, out var output))
                {
                    try
                    {
                        byte[] plain = new byte[output.Size];
                        Marshal.Copy(output.Data, plain, 0, output.Size);
                        return Encoding.UTF8.GetString(plain);
                    }
                    finally
                    {
                        if (output.Data != IntPtr.Zero)
                        {
                            LocalFree(output.Data);
                        }
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            // 忽略损坏的密文
        }
        return "";
    }
}
