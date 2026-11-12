using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// 开机自启管理。
/// 核心修复：旧版本写入 HKCU\...\Run 注册表项，但本程序 manifest 为 requireAdministrator，
/// 在 Win10 / Win11（含 LTSC/IoT LTSC）登录时 UAC 无法为 Run 项弹出提权确认，导致静默不自启；
/// Windows Server 部分策略下恰好可用，造成“服务器能自启、Win10/11 不能”的差异。
/// 现改为写入任务计划程序：登录触发 + 最高权限运行（TASK_RUNLEVEL_HIGHEST），登录时无 UAC 弹窗，
/// 在 Win10 / Win11 / Windows Server 2016-2025 上行为一致。
/// 任务计划程序 COM 不可用时回退到 Run 注册表项（保留老机器兜底能力）。
/// </summary>
internal static class AutoStartManager
{
    /// <summary>计划任务名称（任务计划程序库根目录下）。</summary>
    public const string TaskName = "MiniMonitor_AutoStart";

    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "MiniMonitor";

    /// <summary>当前是否已配置开机自启（计划任务或旧版 Run 项任一存在即视为已开启）。</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey);
            if (key?.GetValue(RunValueName) != null)
            {
                return true;
            }
        }
        catch
        {
            // 注册表不可读时继续检查计划任务。
        }
        return ScheduledTaskExists();
    }

    /// <summary>
    /// 开 / 关机自启。开启时优先创建（或更新）计划任务并清理旧版 Run 项，避免双重启动；
    /// 关闭时两者一并清理。返回 true 表示已通过计划任务生效；
    /// 返回 false 表示任务计划程序不可用、已回退 Run 注册表项（requireAdministrator 程序
    /// 在 Win10/11 登录时可能被 UAC 静默拦截，调用方应提示用户）。
    /// </summary>
    public static bool SetEnabled(bool enabled)
    {
        if (enabled)
        {
            DeleteRunValue();
            if (CreateOrUpdateScheduledTask())
            {
                return true;
            }
            SetRunValue();
            return false;
        }
        DeleteScheduledTask();
        DeleteRunValue();
        return true;
    }

    /// <summary>
    /// 启动时确保自启生效：计划任务已存在且指向当前 exe 时不再重复注册（省去 COM 往返）；
    /// 不存在 / exe 路径变化 / COM 失败时按 SetEnabled 逻辑处理。返回是否由计划任务生效。
    /// </summary>
    public static bool EnsureEnabled()
    {
        if (ScheduledTaskActionPath(out string? actionPath)
            && string.Equals(actionPath, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            DeleteRunValue();
            return true;
        }
        return SetEnabled(true);
    }

    // ================= 任务计划程序（Task Scheduler 2.0 COM） =================

    private static bool CreateOrUpdateScheduledTask()
    {
        try
        {
            Type? serviceType = Type.GetTypeFromProgID("Schedule.Service");
            if (serviceType == null)
            {
                return false;
            }
            dynamic service = Activator.CreateInstance(serviceType)!;
            service.Connect();
            dynamic folder = service.GetFolder(@"\");

            dynamic definition = service.NewTask(0);
            definition.RegistrationInfo.Description =
                "MiniMonitor 开机自动启动（登录时触发，最高权限运行，无 UAC 弹窗）";
            definition.Principal.RunLevel = 1;      // TASK_RUNLEVEL_HIGHEST
            definition.Principal.LogonType = 3;     // TASK_LOGON_INTERACTIVE_TOKEN
            definition.Settings.StartWhenAvailable = true;
            definition.Settings.StopIfGoingOnBatteries = false;
            definition.Settings.DisallowStartIfOnBatteries = false;
            definition.Settings.AllowHardTerminate = true;
            definition.Settings.ExecutionTimeLimit = "PT0S"; // 不限制运行时长
            definition.Settings.Compatibility = 3;  // TASK_COMPATIBILITY_V2

            dynamic trigger = definition.Triggers.Create(9); // TASK_TRIGGER_LOGON
            trigger.UserId = Environment.UserDomainName + "\\" + Environment.UserName;
            trigger.Enabled = true;

            dynamic action = definition.Actions.Create(0);   // TASK_ACTION_EXEC
            action.Path = Application.ExecutablePath;

            // 6 = TASK_CREATE_OR_UPDATE；3 = TASK_LOGON_INTERACTIVE_TOKEN
            folder.RegisterTaskDefinition(TaskName, definition, 6, null, null, 3);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ScheduledTaskExists()
    {
        return ScheduledTaskActionPath(out _);
    }

    /// <summary>读取计划任务的第一个执行动作路径；任务不存在 / COM 不可用时返回 false。</summary>
    private static bool ScheduledTaskActionPath(out string? actionPath)
    {
        actionPath = null;
        try
        {
            Type? serviceType = Type.GetTypeFromProgID("Schedule.Service");
            if (serviceType == null)
            {
                return false;
            }
            dynamic service = Activator.CreateInstance(serviceType)!;
            service.Connect();
            dynamic folder = service.GetFolder(@"\");
            try
            {
                dynamic task = folder.GetTask(TaskName);
                dynamic actions = task.Definition.Actions;
                for (int i = 1; i <= (int)actions.Count; i++)
                {
                    dynamic action = actions[i];
                    actionPath = (string?)action.Path;
                    if (!string.IsNullOrEmpty(actionPath))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static void DeleteScheduledTask()
    {
        try
        {
            Type? serviceType = Type.GetTypeFromProgID("Schedule.Service");
            if (serviceType == null)
            {
                return;
            }
            dynamic service = Activator.CreateInstance(serviceType)!;
            service.Connect();
            dynamic folder = service.GetFolder(@"\");
            try
            {
                folder.DeleteTask(TaskName, 0);
            }
            catch
            {
                // 任务不存在时忽略。
            }
        }
        catch
        {
            // COM 不可用时忽略。
        }
    }

    // ================= Run 注册表项（兜底 / 旧版清理） =================

    private static void SetRunValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: true);
            key?.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
        }
        catch
        {
            // 注册表不可写时忽略。
        }
    }

    private static void DeleteRunValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: true);
            key?.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
        catch
        {
            // 忽略。
        }
    }
}
