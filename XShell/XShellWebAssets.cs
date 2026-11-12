using System.Reflection;
using Microsoft.Web.WebView2.Core;

namespace MiniMonitor.XShell;

/// <summary>把嵌入的 xterm.js 静态资源释放到本地，并共享 WebView2 环境。</summary>
internal static class XShellWebAssets
{
    public const string HostName = "xshell.local";
    private static readonly object Gate = new();
    private static bool _extracted;
    private static Task<CoreWebView2Environment>? _environmentTask;

    public static string EnsureExtracted()
    {
        lock (Gate)
        {
            if (_extracted)
            {
                return XShellPaths.WebRoot;
            }
            Assembly assembly = typeof(XShellWebAssets).Assembly;
            string root = XShellPaths.WebRoot;
            foreach (string name in assembly.GetManifestResourceNames())
            {
                if (!name.StartsWith("xs/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string relative = name[3..].Replace('\\', '/');
                if (relative.Length == 0 || relative.Contains("..", StringComparison.Ordinal))
                {
                    continue;
                }
                string target = Path.Combine(root, relative.Replace('/',
                    Path.DirectorySeparatorChar));
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using Stream? stream = assembly.GetManifestResourceStream(name);
                    if (stream == null)
                    {
                        continue;
                    }
                    using var output = new FileStream(target, FileMode.Create, FileAccess.Write,
                        FileShare.Read);
                    stream.CopyTo(output);
                }
                catch
                {
                    // 单个资源失败不阻断其它资源；WebView 会显示加载错误。
                }
            }
            _extracted = true;
            return root;
        }
    }

    public static Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        if (_environmentTask != null)
        {
            return _environmentTask;
        }
        string dataFolder = AppPaths.Sub("XShellWebView");
        Directory.CreateDirectory(dataFolder);
        _environmentTask = CoreWebView2Environment.CreateAsync(null, dataFolder);
        return _environmentTask;
    }
}
