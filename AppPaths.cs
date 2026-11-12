namespace MiniMonitor;

/// <summary>
/// MiniMonitor 统一数据根目录：%USERPROFILE%\Documents\MiniMonitor。
/// 配置、会话、日志、缓存、临时文件（视频转码代理 / 图片处理 / 格式转换 / 录屏中间文件）
/// 全部集中存储在这里，便于备份与清理。
/// </summary>
public static class AppPaths
{
    /// <summary>数据根目录：文档\MiniMonitor。</summary>
    public static string Root { get; }

    /// <summary>临时 / 中间文件目录：文档\MiniMonitor\Temp（程序内部使用，不落重要数据）。</summary>
    public static string Temp { get; }

    /// <summary>运行日志目录：文档\MiniMonitor\Logs（error.log 等）。</summary>
    public static string Logs { get; }

    static AppPaths()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents) || !Directory.Exists(documents))
        {
            // OnePlace 文件夹重定向等场景拿不到文档目录时，退回用户目录下的 Documents。
            documents = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");
        }
        Root = Ensure(Path.Combine(documents, "MiniMonitor"));
        Temp = Ensure(Path.Combine(Root, "Temp"));
        Logs = Ensure(Path.Combine(Root, "Logs"));
    }

    /// <summary>根目录下的功能子目录（自动创建）。如 Sub("xshell") → 文档\MiniMonitor\xshell。</summary>
    public static string Sub(string name)
    {
        return Ensure(Path.Combine(Root, name));
    }

    private static string Ensure(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch
        {
            // 目录创建失败时继续使用路径（由各调用方兜底处理）。
        }
        return dir;
    }

    /// <summary>
    /// 启动时执行的一次性旧数据迁移：老版本数据散落在 %APPDATA% / %LOCALAPPDATA%\MiniMonitor、
    /// 文档\web_todo、文档\yamlmind 等位置；新版本统一迁到 文档\MiniMonitor 下。
    /// 仅当目标目录不存在且旧目录存在时整体复制，保证用户手动改动不会被覆盖。
    /// </summary>
    public static void RunLegacyMigrations()
    {
        string roaming = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniMonitor");
        string local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniMonitor");
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents) || !Directory.Exists(documents))
        {
            documents = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");
        }

        // %APPDATA%\MiniMonitor 顶层文件（config.json 等）→ 根目录；子目录逐个迁移。
        MigrateFiles(Path.Combine(roaming), Root);
        MigrateFolder(Path.Combine(roaming, "xshell"), Sub("xshell"));
        MigrateFolder(Path.Combine(roaming, "xftp"), Sub("xftp"));
        MigrateFolder(Path.Combine(roaming, "navicat"), Sub("navicat"));
        MigrateFolder(Path.Combine(roaming, "BluetoothView"), Sub("BluetoothView"));
        MigrateFolder(Path.Combine(roaming, "BlueScreenView"), Sub("BlueScreenView"));
        MigrateFolder(Path.Combine(roaming, "PingInfoView"), Sub("PingInfoView"));

        // %LOCALAPPDATA%\MiniMonitor：WebView2 用户数据与错误日志。
        MigrateFolder(Path.Combine(local, "WebView2"), Sub("WebView2"));
        MigrateFolder(Path.Combine(local, "XShellWebView"), Sub("XShellWebView"));
        MigrateFolder(Path.Combine(local, "WebView2RSS"), Sub("WebView2RSS"));
        MigrateFile(Path.Combine(local, "error.log"), Path.Combine(Logs, "error.log"));

        // 文档目录下的独立数据目录。
        MigrateFolder(Path.Combine(documents, "web_todo"), Sub("WebTodo"));
        MigrateFolder(Path.Combine(documents, "yamlmind"), Sub("YamlMind"));

        // 清理 Temp 中超过 7 天的残留临时文件（视频缓存 / 转换中间文件等）。
        CleanStaleTemp();
    }

    /// <summary>整个目录一次性迁移：逐文件复制"目标缺失"的文件，可从中断处续传，不覆盖已有文件。</summary>
    private static void MigrateFolder(string legacyDir, string targetDir)
    {
        try
        {
            if (!Directory.Exists(legacyDir))
            {
                return;
            }
            CopyMissing(legacyDir, targetDir);
        }
        catch
        {
            // 迁移失败不影响启动，老数据仍保留在原位置。
        }
    }

    /// <summary>目录下的顶层文件迁移（不动子目录，子目录由各自的迁移负责）。</summary>
    private static void MigrateFiles(string legacyDir, string targetDir)
    {
        try
        {
            if (!Directory.Exists(legacyDir))
            {
                return;
            }
            foreach (string file in Directory.GetFiles(legacyDir))
            {
                string target = Path.Combine(targetDir, Path.GetFileName(file));
                if (!File.Exists(target))
                {
                    File.Copy(file, target, overwrite: false);
                }
            }
        }
        catch
        {
            // 忽略单个文件迁移失败。
        }
    }

    /// <summary>单文件迁移（目标不存在时复制）。</summary>
    private static void MigrateFile(string legacyFile, string targetFile)
    {
        try
        {
            if (File.Exists(legacyFile) && !File.Exists(targetFile))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                File.Copy(legacyFile, targetFile, overwrite: false);
            }
        }
        catch
        {
            // 忽略。
        }
    }

    /// <summary>逐文件补齐缺失文件（不覆盖目标已有文件），用于迁移续传与并发启动下的幂等。</summary>
    private static void CopyMissing(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string target = Path.Combine(targetDir, Path.GetFileName(file));
            if (!File.Exists(target))
            {
                File.Copy(file, target, overwrite: false);
            }
        }
        foreach (string dir in Directory.GetDirectories(sourceDir))
        {
            CopyMissing(dir, Path.Combine(targetDir, Path.GetFileName(dir)));
        }
    }

    /// <summary>
    /// 清理 Temp 目录中超过 7 天的临时文件（视频转码缓存 / 预览副本 / 转换中间文件等），
    /// 防止崩溃残留与缓存无限增长。
    /// </summary>
    private static void CleanStaleTemp(int days = 7)
    {
        try
        {
            DateTime cutoffUtc = DateTime.UtcNow.AddDays(-days);
            foreach (string file in Directory.GetFiles(Temp, "*", SearchOption.AllDirectories))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < cutoffUtc)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // 单个文件被占用 / 已删除时跳过。
                }
            }
        }
        catch
        {
            // Temp 清理失败不影响启动。
        }
    }
}
