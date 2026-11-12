using System.Diagnostics;
using System.Globalization;
using System.Management;
using Microsoft.Win32;

namespace MiniMonitor;

/// <summary>
/// 硬件信息采集：通过 WMI + 系统 API 汇总操作系统 / 处理器 / 内存 / 主板与 BIOS /
/// 显卡 / 显示器 / 磁盘 / 分区卷 / 网络适配器 / 音频设备 / 电池 / 系统运行状态等分类，
/// 供“硬件信息查看”窗口展示。
/// 单条查询失败不影响整体，失败分类会给出“读取失败”提示行。
/// </summary>
public static class HardwareInfo
{
    public static List<HardwareCategory> Collect()
    {
        var list = new List<HardwareCategory>();
        CollectOs(list);
        CollectCpu(list);
        CollectMemory(list);
        CollectBoard(list);
        CollectGpu(list);
        CollectMonitor(list);
        CollectDisk(list);
        CollectPartitions(list);
        CollectNetwork(list);
        CollectAudio(list);
        CollectBattery(list);
        CollectRuntime(list);
        return list;
    }

    /// <summary>
    /// 从详细采集结果派生“鲁大师式”精简概览：每个硬件类别一行关键信息，一页看完。
    /// 与详细视图共用同一次 WMI 采集，不重复查询。
    /// </summary>
    public static List<HwSummaryItem> BuildSummary(List<HardwareCategory> data)
    {
        var list = new List<HwSummaryItem>();

        // 精简视图最上方：系统型号 / 系统序列号（取自“主板与 BIOS”分类）
        var boardCat = data.FirstOrDefault(c => c.Name == "主板与 BIOS");
        if (boardCat != null)
        {
            string sysModel = Value(boardCat, "系统型号");
            if (sysModel.Length > 0)
            {
                list.Add(new HwSummaryItem("系统型号", sysModel));
            }
            string sysSerial = Value(boardCat, "系统序列号");
            if (sysSerial.Length > 0)
            {
                list.Add(new HwSummaryItem("系统序列号", sysSerial));
            }
        }

        // 操作系统（多个并列值每行一条）
        var os = data.FirstOrDefault(c => c.Name == "操作系统");
        if (os != null)
        {
            string detail = Lines(
                Value(os, "系统名称"), Value(os, "版本"),
                Value(os, "内部版本"), Value(os, "系统架构"));
            if (detail.Length > 0)
            {
                list.Add(new HwSummaryItem("操作系统", detail));
            }
        }

        // 处理器（名称/核心/线程/基准时钟等并列值每行一条）
        var cpu = data.FirstOrDefault(c => c.Name == "处理器 (CPU)");
        if (cpu != null)
        {
            string detail = Lines(
                Value(cpu, "名称"),
                Suffix(Value(cpu, "物理核心数"), " 核"),
                Suffix(Value(cpu, "逻辑处理器数"), " 线程"),
                Value(cpu, "基准时钟"));
            if (detail.Length > 0)
            {
                list.Add(new HwSummaryItem("处理器", detail));
            }
        }

        // 主板
        var board = data.FirstOrDefault(c => c.Name == "主板与 BIOS");
        if (board != null)
        {
            string main = string.Join(" ", new[] { Value(board, "主板厂商"), Value(board, "主板型号") }
                .Where(s => s.Length > 0));
            string bios = Value(board, "BIOS 日期");
            string detail = main.Length > 0 && bios.Length > 0
                ? main + "（BIOS " + bios + "）"
                : main + bios;
            if (detail.Length > 0)
            {
                list.Add(new HwSummaryItem("主板", detail));
            }
        }

        // 内存
        var mem = data.FirstOrDefault(c => c.Name == "内存 (RAM)");
        if (mem != null)
        {
            string total = Value(mem, "物理内存总量");
            string type = FirstValue(mem, "内存条 类型", "内存条 1 类型");
            string speed = FirstValue(mem, "内存条 速度", "内存条 1 速度");
            int sticks = mem.Entries.Count(e => e.Name.StartsWith("内存条") && e.Name.EndsWith("容量"));
            string detail = total;
            if (sticks > 0)
            {
                string desc = string.Join(" ", new[] { type, speed }.Where(s => s.Length > 0));
                detail += "（" + desc + (sticks > 1 ? " ×" + sticks : "") + "）";
            }
            if (detail.Length > 0)
            {
                list.Add(new HwSummaryItem("内存", detail));
            }
        }

        // 显卡（多卡每张一行）
        var gpus = data.FirstOrDefault(c => c.Name == "显卡 (GPU)");
        if (gpus != null)
        {
            var names = gpus.Entries
                .Where(e => e.Name.EndsWith("名称") && e.Value.Length > 0).ToList();
            if (names.Count > 0)
            {
                var parts = new List<string>();
                foreach (var n in names)
                {
                    string prefix = n.Name.Length > 2 ? n.Name[..^2] : "";
                    string vram = gpus.Entries.FirstOrDefault(e => e.Name == prefix + "显存")?.Value ?? "";
                    parts.Add(vram.Length > 0 ? n.Value + "（" + vram + "）" : n.Value);
                }
                list.Add(new HwSummaryItem("显卡", string.Join(Environment.NewLine, parts)));
            }
        }

        // 磁盘（多块每块一行）
        var disks = data.FirstOrDefault(c => c.Name == "磁盘");
        if (disks != null)
        {
            var names = disks.Entries
                .Where(e => e.Name.EndsWith("型号") && e.Value.Length > 0).ToList();
            if (names.Count > 0)
            {
                var parts = new List<string>();
                foreach (var n in names)
                {
                    string prefix = n.Name.Length > 2 ? n.Name[..^2] : "";
                    string cap = disks.Entries.FirstOrDefault(e => e.Name == prefix + "容量")?.Value ?? "";
                    string iface = disks.Entries.FirstOrDefault(e => e.Name == prefix + "接口类型")?.Value ?? "";
                    string detail = n.Value;
                    if (cap.Length > 0)
                    {
                        detail += "（" + cap + (iface.Length > 0 ? "，" + iface : "") + "）";
                    }
                    parts.Add(detail);
                }
                list.Add(new HwSummaryItem("磁盘", string.Join(Environment.NewLine, parts)));
            }
        }

        // 显示器（每台型号/分辨率/刷新率各占一行）
        var mon = data.FirstOrDefault(c => c.Name == "显示器");
        if (mon != null)
        {
            var models = mon.Entries
                .Where(e => e.Name.EndsWith("型号") && e.Value.Length > 0)
                .Select(e => e.Value).ToList();
            string res = Value(mon, "主显示器分辨率");
            string maxHz = Value(mon, "最高刷新率（驱动报告）");
            var parts = new List<string>(models);
            if (res.Length > 0)
            {
                parts.Add(res);
            }
            if (maxHz.Length > 0)
            {
                parts.Add(maxHz);
            }
            if (parts.Count > 0)
            {
                list.Add(new HwSummaryItem("显示器", string.Join(Environment.NewLine, parts)));
            }
        }

        // 网卡（每块网卡一行）
        var nets = data.FirstOrDefault(c => c.Name == "网络适配器");
        if (nets != null)
        {
            var names = nets.Entries
                .Where(e => e.Name.EndsWith("名称") && e.Value.Length > 0).ToList();
            if (names.Count > 0)
            {
                var parts = new List<string>();
                foreach (var n in names)
                {
                    string prefix = n.Name.Length > 2 ? n.Name[..^2] : "";
                    string ip = nets.Entries.FirstOrDefault(e => e.Name == prefix + "IP 地址")?.Value ?? "";
                    parts.Add(ip.Length > 0 ? n.Value + "（" + ip + "）" : n.Value);
                }
                list.Add(new HwSummaryItem("网卡", string.Join(Environment.NewLine, parts)));
            }
        }

        // 声卡（每个设备一行）
        var auds = data.FirstOrDefault(c => c.Name == "音频设备");
        if (auds != null)
        {
            var names = auds.Entries
                .Where(e => e.Name.EndsWith("名称") && e.Value.Length > 0)
                .Select(e => e.Value).ToList();
            if (names.Count > 0)
            {
                list.Add(new HwSummaryItem("声卡", string.Join(Environment.NewLine, names)));
            }
        }

        // 电池（仅笔记本）
        var bat = data.FirstOrDefault(c => c.Name == "电池");
        if (bat != null)
        {
            string name = FirstValue(bat, "电池 名称", "名称");
            string pct = FirstValue(bat, "电池 当前电量", "当前电量");
            string detail = name;
            if (pct.Length > 0)
            {
                detail += "（当前电量 " + pct + "）";
            }
            if (detail.Length > 0)
            {
                list.Add(new HwSummaryItem("电池", detail));
            }
        }

        // 运行状态（开机时间/已运行时间各占一行）
        var run = data.FirstOrDefault(c => c.Name == "系统运行");
        if (run != null)
        {
            var parts = new List<string>();
            string boot = Value(run, "本次开机时间");
            string up = Value(run, "已运行时间");
            if (boot.Length > 0)
            {
                parts.Add(boot);
            }
            if (up.Length > 0)
            {
                parts.Add("已运行 " + up);
            }
            if (parts.Count > 0)
            {
                list.Add(new HwSummaryItem("运行状态", string.Join(Environment.NewLine, parts)));
            }
        }

        return list;
    }

    /// <summary>把多个并列值合并为逐行文本（空值自动忽略，不使用分号分隔）。</summary>
    private static string Lines(params string[] parts)
    {
        return string.Join(Environment.NewLine, parts.Where(s => s.Length > 0));
    }

    private static string Value(HardwareCategory cat, string key)
    {
        return cat.Entries.FirstOrDefault(e => e.Name == key)?.Value ?? "";
    }

    private static string FirstValue(HardwareCategory cat, params string[] keys)
    {
        foreach (var k in keys)
        {
            string v = Value(cat, k);
            if (v.Length > 0)
            {
                return v;
            }
        }
        return "";
    }

    private static string Suffix(string value, string suffix)
    {
        return value.Length > 0 ? value + suffix : "";
    }

    // ================= 操作系统 =================

    private static void CollectOs(List<HardwareCategory> list)
    {
        var cat = new HardwareCategory("操作系统");
        try
        {
            var os = Query("SELECT * FROM Win32_OperatingSystem").FirstOrDefault();
            if (os == null)
            {
                cat.Entries.Add(new HwEntry("操作系统", "读取失败（WMI 无返回）"));
            }
            else
            {
                cat.Entries.Add(new HwEntry("系统名称", Str(os, "Caption")));
                cat.Entries.Add(new HwEntry("版本", Str(os, "Version")));
                cat.Entries.Add(new HwEntry("内部版本", Str(os, "BuildNumber")));
                cat.Entries.Add(new HwEntry("系统架构", Str(os, "OSArchitecture")));
                cat.Entries.Add(new HwEntry("系统目录", Str(os, "SystemDirectory")));
                cat.Entries.Add(new HwEntry("安装日期",
                    os["InstallDate"] is DateTime id ? id.ToString("yyyy-MM-dd HH:mm") : Str(os, "InstallDate")));
                cat.Entries.Add(new HwEntry("注册所有者", Str(os, "RegisteredUser")));
                cat.Entries.Add(new HwEntry("产品序列号", Str(os, "SerialNumber")));
                cat.Entries.Add(new HwEntry("系统制造商", Str(os, "Manufacturer")));
                cat.Entries.Add(new HwEntry("计算机名", Environment.MachineName));
                cat.Entries.Add(new HwEntry("当前用户", Environment.UserName));
            }
            AddTpmInfo(cat);
        }
        catch (Exception ex)
        {
            cat.Entries.Add(new HwEntry("操作系统", "读取失败：" + ex.Message));
        }
        if (cat.Entries.Count > 0)
        {
            list.Add(cat);
        }
    }

    /// <summary>
    /// TPM（可信平台模块）检测，多路径诊断：
    /// 1) Win32_Tpm 权威检测（需管理员权限；该类的 WMI 命名空间为 root\CIMV2\Security\MicrosoftTpm），
    ///    读到即显示规范版本/制造商/固件版本/PPI 版本/启用/激活/拥有状态；
    /// 2) 读不到时继续排查：ACPI 设备枚举（MSFT0101=TPM 2.0 / PNP0C31=TPM 1.2）、
    ///    启动模式（UEFI vs Legacy/CSM）、TPM 基础服务 TBS 状态，并给出可操作的 BIOS 排查建议。
    /// 独立 try/catch，不影响操作系统分类其它字段。
    /// </summary>
    private static void AddTpmInfo(HardwareCategory cat)
    {
        try
        {
            var tpm = QueryInNamespace(@"\\.\root\CIMV2\Security\MicrosoftTpm", "SELECT * FROM Win32_Tpm")
                .FirstOrDefault();
            if (tpm == null)
            {
                AddTpmDiagnosis(cat);
                return;
            }

            cat.Entries.Add(new HwEntry("TPM 支持", "是"));
            string spec = Str(tpm, "SpecVersion");
            if (!string.IsNullOrEmpty(spec))
            {
                // SpecVersion 形如 "2.0, 0.0, 0.0"，取主版本
                string ver = spec.Split(',')[0].Trim();
                if (!string.IsNullOrEmpty(ver))
                {
                    cat.Entries.Add(new HwEntry("TPM 规范版本", "TPM " + ver));
                }
            }
            string mfgId = TpmManufacturerIdText(tpm);
            if (mfgId.Length > 0)
            {
                cat.Entries.Add(new HwEntry("TPM 制造商", mfgId));
            }
            string mfg = Str(tpm, "ManufacturerVersion");
            if (!string.IsNullOrEmpty(mfg))
            {
                cat.Entries.Add(new HwEntry("TPM 固件版本", mfg));
            }
            string ppi = Str(tpm, "PhysicalPresenceVersionInfo");
            if (!string.IsNullOrEmpty(ppi))
            {
                cat.Entries.Add(new HwEntry("TPM PPI 版本", ppi));
            }
            cat.Entries.Add(new HwEntry("TPM 已启用", BoolText(tpm, "IsEnabled_InitialValue")));
            cat.Entries.Add(new HwEntry("TPM 已激活", BoolText(tpm, "IsActivated_InitialValue")));
            cat.Entries.Add(new HwEntry("TPM 已拥有", BoolText(tpm, "IsOwned_InitialValue")));
        }
        catch (Exception ex)
        {
            cat.Entries.Add(new HwEntry("TPM 检测", "读取失败：" + ex.Message));
            cat.Entries.Add(new HwEntry("TPM 提示", "请确认以管理员身份运行，且系统 TPM 服务正常"));
        }
    }

    /// <summary>把 Win32_Tpm 的 ManufacturerId（如 0x494E5443）转换为 4 位制造商代码（如 INTC）。</summary>
    private static string TpmManufacturerIdText(ManagementBaseObject tpm)
    {
        if (TryGet(tpm, "ManufacturerId") is not uint id || id == 0)
        {
            return "";
        }
        char[] chars =
        {
            (char)((id >> 24) & 0xFF),
            (char)((id >> 16) & 0xFF),
            (char)((id >> 8) & 0xFF),
            (char)(id & 0xFF)
        };
        string text = new string(chars).TrimEnd('\0', ' ');
        return text.All(c => c >= 0x20 && c <= 0x7E) ? text : "";
    }

    /// <summary>Win32_Tpm 读不到时的多路径诊断，直接给出可操作的排查提示。</summary>
    private static void AddTpmDiagnosis(HardwareCategory cat)
    {
        cat.Entries.Add(new HwEntry("TPM 支持", "否（Windows 未识别到可用 TPM 设备）"));

        string? acpi = FindTpmAcpiDevice();
        if (acpi != null)
        {
            cat.Entries.Add(new HwEntry("TPM 设备枚举", "固件已暴露（ACPI " + acpi + "）"));
            cat.Entries.Add(new HwEntry("TPM 提示",
                "固件层存在 TPM 设备但 Windows 未识别：检查设备管理器是否有带感叹号的 TPM 设备，或到 BIOS 确认 TPM 是否已启用"));
        }
        else
        {
            cat.Entries.Add(new HwEntry("TPM 设备枚举", "固件未暴露 TPM 设备"));
        }

        cat.Entries.Add(new HwEntry("启动模式", BootModeText()));

        string? tbs = TbsServiceState();
        if (!string.IsNullOrEmpty(tbs))
        {
            cat.Entries.Add(new HwEntry("TPM 基础服务 (TBS)", tbs));
        }

        cat.Entries.Add(new HwEntry("TPM 排查建议",
            "1) BIOS/UEFI 中开启 Intel PTT（Intel 平台）或 AMD fTPM（AMD 平台），台式机主板常默认关闭；" +
            "2) 若主板带 TPM 插槽，需安装 TPM 模块或将设备选择改为 Firmware TPM；" +
            "3) 使用 UEFI 启动并关闭 CSM（TPM 2.0 依赖 UEFI）；" +
            "4) 开启后到 tpm.msc 或 Windows 安全中心完成初始化"));
    }

    /// <summary>ACPI 枚举 TPM 设备：MSFT0101 = TPM 2.0，PNP0C31 = TPM 1.2。</summary>
    private static string? FindTpmAcpiDevice()
    {
        try
        {
            using var acpi = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\ACPI");
            if (acpi == null)
            {
                return null;
            }
            foreach (var name in acpi.GetSubKeyNames())
            {
                if (name.StartsWith("MSFT0101", StringComparison.OrdinalIgnoreCase))
                {
                    return "TPM 2.0 设备（MSFT0101）";
                }
                if (name.StartsWith("PNP0C31", StringComparison.OrdinalIgnoreCase))
                {
                    return "TPM 1.2 设备（PNP0C31）";
                }
            }
        }
        catch
        {
        }
        return null;
    }

    /// <summary>启动模式：UEFI（含 Secure Boot 状态）或 Legacy/CSM。UEFI 系统通常存在 SecureBoot 状态键。</summary>
    private static string BootModeText()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            if (key == null)
            {
                return "Legacy / CSM（未找到 SecureBoot 状态键，通常为传统启动；TPM 2.0 依赖 UEFI，需关闭 CSM）";
            }
            int? sb = key.GetValue("UEFISecureBootEnabled") as int?;
            return sb == 1 ? "UEFI（Secure Boot 已开启）" : "UEFI（Secure Boot 未开启）";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>TPM 基础服务（TBS）状态；服务缺失返回空串。</summary>
    private static string? TbsServiceState()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT State, StartMode FROM Win32_Service WHERE Name = 'TBS'");
            using var results = searcher.Get();
            foreach (var o in results)
            {
                string? state = o["State"]?.ToString();
                string? mode = o["StartMode"]?.ToString();
                if (!string.IsNullOrEmpty(state))
                {
                    return state + (string.IsNullOrEmpty(mode) ? "" : "（启动类型 " + mode + "）");
                }
            }
        }
        catch
        {
        }
        return null;
    }

    private static string BoolText(ManagementBaseObject o, string prop)
    {
        return TryGet(o, prop) is bool b ? (b ? "是" : "否") : "";
    }

    // ================= 处理器 =================

    private static void CollectCpu(List<HardwareCategory> list)
    {
        var cat = new HardwareCategory("处理器 (CPU)");
        try
        {
            var cpu = Query("SELECT * FROM Win32_Processor").FirstOrDefault();
            if (cpu == null)
            {
                cat.Entries.Add(new HwEntry("处理器", "读取失败（WMI 无返回）"));
            }
            else
            {
                cat.Entries.Add(new HwEntry("名称", Str(cpu, "Name")));
                cat.Entries.Add(new HwEntry("制造商", Str(cpu, "Manufacturer")));
                cat.Entries.Add(new HwEntry("物理核心数", Str(cpu, "NumberOfCores")));
                cat.Entries.Add(new HwEntry("逻辑处理器数", Str(cpu, "NumberOfLogicalProcessors")));
                cat.Entries.Add(new HwEntry("基准时钟", AppendUnit(Str(cpu, "MaxClockSpeed"), " MHz")));
                cat.Entries.Add(new HwEntry("当前时钟", AppendUnit(Str(cpu, "CurrentClockSpeed"), " MHz")));
                cat.Entries.Add(new HwEntry("插槽", Str(cpu, "SocketDesignation")));
                cat.Entries.Add(new HwEntry("L2 缓存", AppendUnit(Str(cpu, "L2CacheSize"), " KB")));
                cat.Entries.Add(new HwEntry("L3 缓存", AppendUnit(Str(cpu, "L3CacheSize"), " KB")));
                cat.Entries.Add(new HwEntry("地址宽度", AppendUnit(Str(cpu, "AddressWidth"), " 位")));
                cat.Entries.Add(new HwEntry("虚拟化固件启用",
                    Str(cpu, "VirtualizationFirmwareEnabled") == "True" ? "是" : "否"));
                if (TryGet(cpu, "MaxCapacity") is uint maxKb && maxKb > 0)
                {
                    cat.Entries.Add(new HwEntry("最大支持内存", FormatBytes((long)maxKb * 1024)));
                }
            }
        }
        catch (Exception ex)
        {
            cat.Entries.Add(new HwEntry("处理器", "读取失败：" + ex.Message));
        }
        if (cat.Entries.Count > 0)
        {
            list.Add(cat);
        }
    }

    // ================= 内存 =================

    private static void CollectMemory(List<HardwareCategory> list)
    {
        var cat = new HardwareCategory("内存 (RAM)");
        try
        {
            var cs = Query("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem").FirstOrDefault();
            if (cs?["TotalPhysicalMemory"] is ulong total && total > 0)
            {
                cat.Entries.Add(new HwEntry("物理内存总量", FormatBytes((long)total)));
            }

            var os = Query("SELECT FreePhysicalMemory, TotalVisibleMemorySize FROM Win32_OperatingSystem").FirstOrDefault();
            if (os != null)
            {
                if (os["FreePhysicalMemory"] is ulong freeKb && freeKb > 0)
                {
                    cat.Entries.Add(new HwEntry("当前可用内存", FormatBytes((long)freeKb * 1024)));
                }
                if (os["TotalVisibleMemorySize"] is ulong visKb && visKb > 0)
                {
                    cat.Entries.Add(new HwEntry("可见内存总量", FormatBytes((long)visKb * 1024)));
                }
            }

            var sticks = Query("SELECT * FROM Win32_PhysicalMemory");
            for (int i = 0; i < sticks.Count; i++)
            {
                var s = sticks[i];
                string p = sticks.Count > 1 ? $"内存条 {i + 1} " : "内存条 ";
                if (s["Capacity"] is ulong cap && cap > 0)
                {
                    cat.Entries.Add(new HwEntry(p + "容量", FormatBytes((long)cap)));
                }
                cat.Entries.Add(new HwEntry(p + "速度", AppendUnit(Str(s, "Speed"), " MHz")));
                cat.Entries.Add(new HwEntry(p + "类型", MapMemoryType(Str(s, "SMBIOSMemoryType"))));
                cat.Entries.Add(new HwEntry(p + "厂商", Str(s, "Manufacturer")));
                cat.Entries.Add(new HwEntry(p + "部件号", Str(s, "PartNumber")));
                cat.Entries.Add(new HwEntry(p + "序列号", Str(s, "SerialNumber")));
                cat.Entries.Add(new HwEntry(p + "插槽", Str(s, "DeviceLocator")));
            }
            if (sticks.Count == 0)
            {
                cat.Entries.Add(new HwEntry("内存条", "未检测到"));
            }
        }
        catch (Exception ex)
        {
            cat.Entries.Add(new HwEntry("内存", "读取失败：" + ex.Message));
        }
        if (cat.Entries.Count > 0)
        {
            list.Add(cat);
        }
    }

    // ================= 主板与 BIOS =================

    private static void CollectBoard(List<HardwareCategory> list)
    {
        var cat = new HardwareCategory("主板与 BIOS");
        try
        {
            var board = Query("SELECT * FROM Win32_BaseBoard").FirstOrDefault();
            if (board != null)
            {
                cat.Entries.Add(new HwEntry("主板厂商", Str(board, "Manufacturer")));
                cat.Entries.Add(new HwEntry("主板型号", Str(board, "Product")));
                cat.Entries.Add(new HwEntry("主板版本", Str(board, "Version")));
                cat.Entries.Add(new HwEntry("主板序列号", Str(board, "SerialNumber")));
            }

            var bios = Query("SELECT * FROM Win32_BIOS").FirstOrDefault();
            if (bios != null)
            {
                cat.Entries.Add(new HwEntry("BIOS 厂商", Str(bios, "Manufacturer")));
                cat.Entries.Add(new HwEntry("BIOS 版本", Str(bios, "SMBIOSBIOSVersion")));
                cat.Entries.Add(new HwEntry("BIOS 日期",
                    TryGet(bios, "ReleaseDate") is DateTime rd ? rd.ToString("yyyy-MM-dd") : Str(bios, "ReleaseDate")));
            }

            // 注意：Win32_ComputerSystem 没有 SerialNumber / UUID 属性，
            // 在 SELECT 中引用这两个字段会让 WMI 返回“无效查询”，
            // 系统序列号与 UUID 需从 Win32_ComputerSystemProduct 读取。
            var cs = Query("SELECT Manufacturer,Model,SystemType FROM Win32_ComputerSystem").FirstOrDefault();
            var product = Query("SELECT IdentifyingNumber,UUID FROM Win32_ComputerSystemProduct").FirstOrDefault();
            if (cs != null)
            {
                cat.Entries.Add(new HwEntry("系统制造商", Str(cs, "Manufacturer")));
                cat.Entries.Add(new HwEntry("系统型号", Str(cs, "Model")));
            }
            if (product != null)
            {
                cat.Entries.Add(new HwEntry("系统序列号", Str(product, "IdentifyingNumber")));
                cat.Entries.Add(new HwEntry("系统 UUID", Str(product, "UUID")));
            }
            if (cs != null)
            {
                cat.Entries.Add(new HwEntry("系统类型", Str(cs, "SystemType")));
            }
        }
        catch (Exception ex)
        {
            cat.Entries.Add(new HwEntry("主板与 BIOS", "读取失败：" + ex.Message));
        }
        if (cat.Entries.Count > 0)
        {
            list.Add(cat);
        }
    }

    // ================= 显卡 =================

    private static void CollectGpu(List<HardwareCategory> list)
    {
        var cat = new HardwareCategory("显卡 (GPU)");
        try
        {
            var gpus = Query("SELECT * FROM Win32_VideoController");
            if (gpus.Count == 0)
            {
                cat.Entries.Add(new HwEntry("显卡", "未检测到"));
            }
            for (int i = 0; i < gpus.Count; i++)
            {
                var g = gpus[i];
                string p = gpus.Count > 1 ? $"显卡 {i + 1} " : "";
                cat.Entries.Add(new HwEntry(p + "名称", Str(g, "Name")));
                cat.Entries.Add(new HwEntry(p + "显存", GpuMemory(g)));
                cat.Entries.Add(new HwEntry(p + "驱动版本", Str(g, "DriverVersion")));
                cat.Entries.Add(new HwEntry(p + "驱动日期", Str(g, "DriverDate")));
                cat.Entries.Add(new HwEntry(p + "分辨率", VideoResolution(g)));
                cat.Entries.Add(new HwEntry(p + "刷新率", AppendUnit(Str(g, "CurrentRefreshRate"), " Hz")));
                cat.Entries.Add(new HwEntry(p + "最高刷新率", MaxRefreshRate(g)));
                cat.Entries.Add(new HwEntry(p + "状态", Str(g, "Status")));
            }
        }
        catch (Exception ex)
        {
            cat.Entries.Add(new HwEntry("显卡", "读取失败：" + ex.Message));
        }
        if (cat.Entries.Count > 0)
        {
            list.Add(cat);
        }
    }

    /// <summary>
    /// 显存容量：优先读注册表 HardwareInformation.qwMemorySize（正确识别 >4GB 显存，
    /// WMI AdapterRAM 为 32 位会溢出），失败回退 WMI 值并标注来源。
    /// </summary>
    private static string GpuMemory(ManagementBaseObject g)
    {
        string? pnp = g["PNPDeviceID"]?.ToString();
        if (!string.IsNullOrEmpty(pnp))
        {
            try
            {
                const string clsKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
                using var root = Registry.LocalMachine.OpenSubKey(clsKey);
                if (root != null)
                {
                    foreach (var sub in root.GetSubKeyNames())
                    {
                        using var k = root.OpenSubKey(sub);
                        if (k == null)
                        {
                            continue;
                        }
                        string? mid = k.GetValue("MatchingDeviceId")?.ToString();
                        if (string.IsNullOrEmpty(mid)
                            || !pnp.StartsWith(mid, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        if (k.GetValue("HardwareInformation.qwMemorySize") is byte[] bytes && bytes.Length == 8)
                        {
                            ulong size = BitConverter.ToUInt64(bytes, 0);
                            if (size > 0)
                            {
                                return FormatBytes((long)size);
                            }
                        }
                        break;
                    }
                }
            }
            catch
            {
                // 注册表读取失败时回退 WMI
            }
        }
        if (g["AdapterRAM"] is uint ram && ram > 0)
        {
            return FormatBytes(ram) + "（WMI）";
        }
        return "";
    }

    private static string VideoResolution(ManagementBaseObject g)
    {
        int w = g["CurrentHorizontalResolution"] is uint u1 ? (int)u1 : 0;
        int h = g["CurrentVerticalResolution"] is uint u2 ? (int)u2 : 0;
        return w > 0 && h > 0 ? $"{w} × {h}" : "";
    }

    /// <summary>当前驱动模式下的最大刷新率（Win32_VideoController.MaxRefreshRate）。</summary>
    private static string MaxRefreshRate(ManagementBaseObject g)
    {
        if (g["MaxRefreshRate"] is uint mr && mr > 0)
        {
            return mr + " Hz";
        }
        return "—（驱动未报告）";
    }

    // ================= 显示器 =================

    private static void CollectMonitor(List<HardwareCategory> list)
    {
        var cat = new HardwareCategory("显示器");
        try
        {
            var scr = Screen.PrimaryScreen;
            if (scr != null)
            {
                cat.Entries.Add(new HwEntry("主显示器", scr.DeviceName ?? ""));
                cat.Entries.Add(new HwEntry("主显示器分辨率", $"{scr.Bounds.Width} × {scr.Bounds.Height}"));
                cat.Entries.Add(new HwEntry("工作区", $"{scr.WorkingArea.Width} × {scr.WorkingArea.Height}"));
            }

            var mons = Query("SELECT Name, ScreenWidth, ScreenHeight, Status FROM Win32_DesktopMonitor");
            for (int i = 0; i < mons.Count; i++)
            {
                var m = mons[i];
                string p = mons.Count > 1 ? $"显示器 {i + 1} " : "";
                string? name = Str(m, "Name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = "通用即插即用监视器";
                }
                cat.Entries.Add(new HwEntry(p + "型号", name));
                int w = m["ScreenWidth"] is uint sw ? (int)sw : 0;
                int h = m["ScreenHeight"] is uint sh ? (int)sh : 0;
                if (w > 0 && h > 0)
                {
                    cat.Entries.Add(new HwEntry(p + "屏幕分辨率", $"{w} × {h}"));
                }
                cat.Entries.Add(new HwEntry(p + "状态", Str(m, "Status")));
            }

            // 最高刷新率：取所有显卡驱动报告 MaxRefreshRate 的最大值（多显示器/多显卡场景的近似上限）
            uint maxRate = 0;
            foreach (var g in Query("SELECT MaxRefreshRate FROM Win32_VideoController"))
            {
                if (g["MaxRefreshRate"] is uint mr && mr > maxRate)
                {
                    maxRate = mr;
                }
            }
            if (maxRate > 0)
            {
                cat.Entries.Add(new HwEntry("最高刷新率（驱动报告）", maxRate + " Hz"));
            }
        }
        catch (Exception ex)
        {
            cat.Entries.Add(new HwEntry("显示器", "读取失败：" + ex.Message));
        }
        if (cat.Entries.Count > 0)
        {
            list.Add(cat);
        }
    }

    // ================= 磁盘 =================

    private static void CollectDisk(List<HardwareCategory> list)
    {
        var cat = new HardwareCategory("磁盘");
        try
        {
            var disks = Query("SELECT * FROM Win32_DiskDrive");
            if (disks.Count == 0)
            {
                cat.Entries.Add(new HwEntry("磁盘", "未检测到"));
            }
            for (int i = 0; i < disks.Count; i++)
            {
                var d = disks[i];
                string p = disks.Count > 1 ? $"磁盘 {i + 1} " : "";
                cat.Entries.Add(new HwEntry(p + "型号", Str(d, "Model")));
                cat.Entries.Add(new HwEntry(p + "接口类型", Str(d, "InterfaceType")));
                cat.Entries.Add(new HwEntry(p + "容量", d["Size"] is ulong sz ? FormatBytes((long)sz) : ""));
                cat.Entries.Add(new HwEntry(p + "分区数", Str(d, "Partitions")));
                cat.Entries.Add(new HwEntry(p + "序列号", Str(d, "SerialNumber")));
                cat.Entries.Add(new HwEntry(p + "介质类型", Str(d, "MediaType")));
                cat.Entries.Add(new HwEntry(p + "状态", Str(d, "Status")));
            }
        }
        catch (Exception ex)
        {
            cat.Entries.Add(new HwEntry("磁盘", "读取失败：" + ex.Message));
        }
        if (cat.Entries.Count > 0)
        {
            list.Add(cat);
        }
    }

    // ================= 分区 / 卷 =================

    private static void CollectPartitions(List<HardwareCategory> list)
    {
        var cat = new HardwareCategory("分区 / 卷");
        try
        {
            var vols = Query("SELECT * FROM Win32_LogicalDisk WHERE DriveType = 3");
            if (vols.Count == 0)
            {
                cat.Entries.Add(new HwEntry("本地磁盘", "未检测到"));
            }
            foreach (var v in vols)
            {
                string letter = Str(v, "DeviceID") ?? "";
                cat.Entries.Add(new HwEntry(letter + " 卷标", Str(v, "VolumeName")));
                cat.Entries.Add(new HwEntry(letter + " 文件系统", Str(v, "FileSystem")));
                ulong? size = v["Size"] is ulong s ? s : null;
                ulong? free = v["FreeSpace"] is ulong f ? f : null;
                if (size is > 0)
                {
                    cat.Entries.Add(new HwEntry(letter + " 总容量", FormatBytes((long)size.Value)));
                }
                if (free is > 0)
                {
                    string pct = size is > 0 ? "（" + (free.Value * 100.0 / size.Value).ToString("0", CultureInfo.InvariantCulture) + "% 可用）" : "";
                    cat.Entries.Add(new HwEntry(letter + " 剩余空间", FormatBytes((long)free.Value) + pct));
                }
            }
        }
        catch (Exception ex)
        {
            cat.Entries.Add(new HwEntry("分区", "读取失败：" + ex.Message));
        }
        if (cat.Entries.Count > 0)
        {
            list.Add(cat);
        }
    }

    // ================= 网络适配器 =================

    private static void CollectNetwork(List<HardwareCategory> list)
    {
        var cat = new HardwareCategory("网络适配器");
        try
        {
            var cfgs = Query("SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");
            if (cfgs.Count == 0)
            {
                cat.Entries.Add(new HwEntry("网络", "未检测到启用的网络连接"));
            }
            for (int i = 0; i < cfgs.Count; i++)
            {
                var c = cfgs[i];
                string p = cfgs.Count > 1 ? $"连接 {i + 1} " : "";
                cat.Entries.Add(new HwEntry(p + "名称", Str(c, "Description")));
                cat.Entries.Add(new HwEntry(p + "MAC 地址", Str(c, "MACAddress")));
                cat.Entries.Add(new HwEntry(p + "IP 地址", JoinArray(c, "IPAddress")));
                cat.Entries.Add(new HwEntry(p + "子网掩码", JoinArray(c, "IPSubnet")));
                cat.Entries.Add(new HwEntry(p + "默认网关", JoinArray(c, "DefaultIPGateway")));
                cat.Entries.Add(new HwEntry(p + "DNS 服务器", JoinArray(c, "DNSServerSearchOrder")));
                cat.Entries.Add(new HwEntry(p + "DHCP 启用", c["DHCPEnabled"] is bool dh && dh ? "是" : "否"));
            }
        }
        catch (Exception ex)
        {
            cat.Entries.Add(new HwEntry("网络", "读取失败：" + ex.Message));
        }
        if (cat.Entries.Count > 0)
        {
            list.Add(cat);
        }
    }

    // ================= 音频设备 =================

    private static void CollectAudio(List<HardwareCategory> list)
    {
        var cat = new HardwareCategory("音频设备");
        try
        {
            var auds = Query("SELECT * FROM Win32_SoundDevice");
            if (auds.Count == 0)
            {
                cat.Entries.Add(new HwEntry("音频设备", "未检测到"));
            }
            for (int i = 0; i < auds.Count; i++)
            {
                var a = auds[i];
                string p = auds.Count > 1 ? $"设备 {i + 1} " : "";
                cat.Entries.Add(new HwEntry(p + "名称", Str(a, "Name")));
                cat.Entries.Add(new HwEntry(p + "制造商", Str(a, "Manufacturer")));
                cat.Entries.Add(new HwEntry(p + "状态", Str(a, "Status")));
            }
        }
        catch (Exception ex)
        {
            cat.Entries.Add(new HwEntry("音频", "读取失败：" + ex.Message));
        }
        if (cat.Entries.Count > 0)
        {
            list.Add(cat);
        }
    }

    // ================= 电池（仅笔记本） =================

    private static void CollectBattery(List<HardwareCategory> list)
    {
        var cat = new HardwareCategory("电池");
        try
        {
            var bats = Query("SELECT * FROM Win32_Battery");
            if (bats.Count == 0)
            {
                return; // 台式机 / 虚拟机无电池，不显示该分类
            }
            for (int i = 0; i < bats.Count; i++)
            {
                var b = bats[i];
                string p = bats.Count > 1 ? $"电池 {i + 1} " : "";
                cat.Entries.Add(new HwEntry(p + "名称", Str(b, "Name")));
                if (b["DesignCapacity"] is ushort dc && dc > 0)
                {
                    cat.Entries.Add(new HwEntry(p + "设计容量", dc + " mWh"));
                }
                if (b["FullChargeCapacity"] is ushort fc && fc > 0)
                {
                    cat.Entries.Add(new HwEntry(p + "满充容量", fc + " mWh"));
                }
                if (b["EstimatedChargeRemaining"] is ushort ec)
                {
                    cat.Entries.Add(new HwEntry(p + "当前电量", ec + " %"));
                }
                if (b["DesignVoltage"] is ushort dv && dv > 0)
                {
                    cat.Entries.Add(new HwEntry(p + "设计电压", dv + " mV"));
                }
                cat.Entries.Add(new HwEntry(p + "状态", MapBatteryStatus(Str(b, "BatteryStatus"))));
            }
        }
        catch (Exception ex)
        {
            cat.Entries.Add(new HwEntry("电池", "读取失败：" + ex.Message));
        }
        if (cat.Entries.Count > 0)
        {
            list.Add(cat);
        }
    }

    // ================= 系统运行 =================

    private static void CollectRuntime(List<HardwareCategory> list)
    {
        var cat = new HardwareCategory("系统运行");
        try
        {
            var os = Query("SELECT LastBootUpTime FROM Win32_OperatingSystem").FirstOrDefault();
            if (os?["LastBootUpTime"] is DateTime bt)
            {
                cat.Entries.Add(new HwEntry("本次开机时间", bt.ToString("yyyy-MM-dd HH:mm:ss")));
                cat.Entries.Add(new HwEntry("已运行时间", FormatRunTime(DateTime.Now - bt)));
            }
            cat.Entries.Add(new HwEntry("当前时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            try
            {
                cat.Entries.Add(new HwEntry("当前进程数", Process.GetProcesses().Length.ToString(CultureInfo.InvariantCulture)));
            }
            catch
            {
                // 进程数读取失败不阻塞
            }
        }
        catch (Exception ex)
        {
            cat.Entries.Add(new HwEntry("系统运行", "读取失败：" + ex.Message));
        }
        if (cat.Entries.Count > 0)
        {
            list.Add(cat);
        }
    }

    // ================= 工具方法 =================

    private static List<ManagementBaseObject> Query(string wql)
    {
        using var searcher = new ManagementObjectSearcher(wql);
        using var results = searcher.Get();
        return results.Cast<ManagementBaseObject>().ToList();
    }

    /// <summary>在指定 WMI 命名空间中查询（例如 Win32_Tpm 所在的 root\CIMV2\Security\MicrosoftTpm）。</summary>
    private static List<ManagementBaseObject> QueryInNamespace(string ns, string wql)
    {
        var scope = new ManagementScope(ns);
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(wql));
        using var results = searcher.Get();
        return results.Cast<ManagementBaseObject>().ToList();
    }

    /// <summary>安全读取 WMI 属性；属性不存在时返回 null，避免“找不到”中断整类采集。</summary>
    private static object? TryGet(ManagementBaseObject o, string prop)
    {
        try
        {
            return o[prop];
        }
        catch
        {
            return null;
        }
    }

    /// <summary>读取 WMI 属性文本；缺失或空白时返回空字符串（避免可空性告警）。</summary>
    private static string Str(ManagementBaseObject o, string prop)
    {
        object? v = TryGet(o, prop);
        string? s = v?.ToString();
        return string.IsNullOrWhiteSpace(s) ? "" : s;
    }

    private static string JoinArray(ManagementBaseObject o, string prop)
    {
        if (o[prop] is string[] arr)
        {
            return string.Join(", ", arr);
        }
        return o[prop]?.ToString() ?? "";
    }

    private static string AppendUnit(string? value, string unit)
    {
        return string.IsNullOrEmpty(value) ? "" : value + unit;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1L << 40)
        {
            return (bytes / (double)(1L << 40)).ToString("0.00", CultureInfo.InvariantCulture) + " TB";
        }
        if (bytes >= 1L << 30)
        {
            return (bytes / (double)(1L << 30)).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
        }
        if (bytes >= 1L << 20)
        {
            return (bytes / (double)(1L << 20)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }
        if (bytes >= 1L << 10)
        {
            return (bytes / (double)(1L << 10)).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        }
        return bytes.ToString(CultureInfo.InvariantCulture) + " B";
    }

    private static string FormatRunTime(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
        {
            t = TimeSpan.Zero;
        }
        if (t.TotalDays >= 1)
        {
            return (int)t.TotalDays + "天 " + t.Hours.ToString("00", CultureInfo.InvariantCulture)
                + ":" + t.Minutes.ToString("00", CultureInfo.InvariantCulture);
        }
        return t.Hours.ToString("00", CultureInfo.InvariantCulture) + ":"
            + t.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":"
            + t.Seconds.ToString("00", CultureInfo.InvariantCulture);
    }

    private static string MapMemoryType(string? type)
    {
        return type switch
        {
            "20" => "DDR",
            "21" => "DDR2",
            "22" => "DDR2 FB-DIMM",
            "24" => "DDR3",
            "25" => "DDR3 FBD2",
            "26" => "DDR4",
            "27" => "LPDDR",
            "28" => "LPDDR2",
            "29" => "LPDDR3",
            "30" => "LPDDR4",
            "32" => "HBM",
            "33" => "HBM2",
            "34" => "DDR5",
            "35" => "LPDDR5",
            _ => string.IsNullOrEmpty(type) ? "" : "类型代码 " + type
        };
    }

    private static string MapBatteryStatus(string? status)
    {
        return status switch
        {
            "1" => "放电中",
            "2" => "交流电源",
            "3" => "已充满",
            "4" => "电量低",
            "5" => "电量严重不足",
            "6" => "充电中",
            "7" => "充电中（高）",
            "8" => "充电中（低）",
            "9" => "充电中（严重不足）",
            "10" => "未知",
            "11" => "部分充电",
            _ => string.IsNullOrEmpty(status) ? "" : "状态码 " + status
        };
    }
}

/// <summary>硬件信息的一个分类（如“处理器 (CPU)”）。</summary>
public sealed class HardwareCategory
{
    public string Name { get; }

    public List<HwEntry> Entries { get; } = new();

    public HardwareCategory(string name)
    {
        Name = name;
    }
}

/// <summary>硬件信息条目：项目名 + 值。</summary>
public sealed record HwEntry(string Name, string Value);

/// <summary>精简视图条目：类别名 + 一句话概览（鲁大师风格）。</summary>
public sealed record HwSummaryItem(string Title, string Detail);
