using System.IO.Compression;
using System.Text.Json;

namespace MiniMonitor.Rss;

/// <summary>
/// 完整数据备份 / 还原：ZIP 中包含 SQLite 数据库（订阅源、分组、文章、已读星标状态）
/// 与 RSS 设置（规则、同步频率、主题等）。
/// </summary>
public static class RssBackup
{
    private const string DatabaseEntry = "fluentrss.db";
    private const string SettingsEntry = "rss-settings.json";

    public static void Export(string path, RssStore store, RssSettings settings)
    {
        string tempDir = Path.Combine(AppPaths.Temp, "FluentRssBackup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string dbCopy = Path.Combine(tempDir, DatabaseEntry);
            store.BackupDatabaseTo(dbCopy);
            File.WriteAllText(Path.Combine(tempDir, SettingsEntry),
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            DeleteWithRetry(path);
            CreateZipWithRetry(tempDir, path);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // 临时目录清理失败忽略
            }
        }
    }

    private static void DeleteWithRetry(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }
        Exception? last = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                last = ex;
                Thread.Sleep(150);
            }
        }
        throw new IOException(
            "无法覆盖已有备份文件，请关闭正在使用该文件的程序（资源管理器预览、杀毒软件等）后重试。",
            last);
    }

    private static void CreateZipWithRetry(string sourceDir, string path)
    {
        Exception? last = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                ZipFile.CreateFromDirectory(sourceDir, path, CompressionLevel.Optimal,
                    includeBaseDirectory: false);
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new IOException(
                    "没有写入备份文件的权限，请更换保存位置后重试。", ex);
            }
            catch (IOException ex)
            {
                last = ex;
                Thread.Sleep(200);
            }
        }
        throw new IOException(
            "备份文件被占用，请关闭正在使用该文件的程序（资源管理器预览、杀毒软件等）后重试。",
            last);
    }

    public static RssSettings Import(string path, RssStore store)
    {
        string tempDir = Path.Combine(AppPaths.Temp, "FluentRssRestore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            ZipFile.ExtractToDirectory(path, tempDir, overwriteFiles: true);
            string dbFile = Path.Combine(tempDir, DatabaseEntry);
            if (!File.Exists(dbFile))
            {
                throw new InvalidDataException("备份文件中缺少 fluentrss.db。");
            }
            var settings = new RssSettings();
            string settingsFile = Path.Combine(tempDir, SettingsEntry);
            if (File.Exists(settingsFile))
            {
                var loaded = JsonSerializer.Deserialize<RssSettings>(File.ReadAllText(settingsFile));
                if (loaded != null)
                {
                    loaded.Normalize();
                    settings = loaded;
                }
            }
            store.RestoreDatabaseFrom(dbFile);
            return settings;
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // 忽略
            }
        }
    }
}
