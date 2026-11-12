using System.IO;
using System.Text;

namespace MiniMonitor.Video;

/// <summary>视频工具排错日志（%APPDATA%\MiniMonitor\video\logs）。</summary>
internal static class VideoLog
{
    private static readonly object Gate = new();

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MiniMonitor", "video", "logs");

    public static string CurrentFile => Path.Combine(Folder,
        "video-" + DateTime.Now.ToString("yyyyMMdd") + ".log");

    public static void Info(string message)
    {
        Write("INFO", message);
    }

    public static void Warn(string message)
    {
        Write("WARN", message);
    }

    public static void Error(string message, Exception? exception = null)
    {
        Write("ERROR", exception == null ? message : message + Environment.NewLine + exception);
    }

    public static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Folder,
                UseShellExecute = true
            });
        }
        catch
        {
            // ignore
        }
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                Cleanup();
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                    + " [" + level + "] [" + Environment.CurrentManagedThreadId + "] "
                    + message + Environment.NewLine;
                File.AppendAllText(CurrentFile, line, Encoding.UTF8);
            }
        }
        catch
        {
            // 日志失败不影响功能
        }
    }

    private static void Cleanup()
    {
        try
        {
            foreach (string file in Directory.GetFiles(Folder, "video-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-14))
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // ignore
        }
    }
}
