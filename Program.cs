using System.Text;
using System.Windows.Forms;

namespace MiniMonitor;

internal static class Program
{
    private static Mutex? _singleInstance;

    [STAThread]
    private static void Main(string[] args)
    {
        ConfigureApplication();
        string? convertFile = ParseConvertFile(args);
        if (convertFile != null)
        {
            // 资源管理器右键“通用格式转换”：只打开转换器，不创建悬浮监控窗。
            AppPaths.RunLegacyMigrations();
            var config = AppConfig.Load();
            Application.Run(new FormatConverterForm(config, convertFile));
            return;
        }

        // 单实例：重复启动直接退出，避免多个悬浮窗同时采样；
        // 数据迁移放在互斥锁之后，避免双实例并发迁移产生竞态。
        _singleInstance = new Mutex(initiallyOwned: true,
            "Local\\MiniMonitor_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            return;
        }
        AppPaths.RunLegacyMigrations();

        Application.Run(new MonitorForm());

        GC.KeepAlive(_singleInstance);
    }

    private static void ConfigureApplication()
    {
        // Telnet 的 GB18030 / GBK 编码支持
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        // 全局兜底：任何未预期异常都弹框提示，而不是静默闪退
        Application.ThreadException += (_, args) =>
            ShowError(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ShowError(args.ExceptionObject as Exception);
    }

    private static string? ParseConvertFile(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--convert", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(args[i + 1]))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static void ShowError(Exception? ex)
    {
        // 把完整异常写入日志，方便定位“Parameter is not valid”这类没有堆栈的提示。
        try
        {
            MiniMonitor.Navicat.NvLog.Error(ex ?? new Exception("未知异常"), "MiniMonitor 未处理异常");
        }
        catch
        {
            // Navicat 日志失败时继续写通用错误日志
        }
        try
        {
            string file = Path.Combine(AppPaths.Logs, "error.log");
            if (File.Exists(file) && new FileInfo(file).Length > 1024 * 1024)
            {
                File.Delete(file);
            }
            File.AppendAllText(file,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + Environment.NewLine
                + (ex?.ToString() ?? "未知异常") + Environment.NewLine + Environment.NewLine);
        }
        catch
        {
            // 日志失败不影响错误提示
        }
        MessageBox.Show(
            "MiniMonitor 发生未预期的错误：\r\n\r\n" + ex?.Message + "\r\n\r\n" +
            "请重启程序；如反复出现请检查是否以管理员身份运行。\r\n" +
            "详细异常已写入：文档\\MiniMonitor\\Logs\\error.log",
            "MiniMonitor",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
}
