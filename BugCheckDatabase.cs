using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MiniMonitor;

/// <summary>单个蓝屏代码的完整信息（中文解释 + 微软原文）。</summary>
public sealed class BugCheckInfo
{
    public uint Code { get; init; }
    public string Name { get; init; } = "";
    public string Url { get; init; } = "";
    public string ChineseName { get; set; } = "";
    public string Cause { get; set; } = "";
    public string Solution { get; set; } = "";
    public string EnglishCause { get; set; } = "";
    public string EnglishResolution { get; set; } = "";
    public string AppliesTo { get; set; } = "Windows 10 / 11 / Windows Server 2016-2025";
    public bool Known { get; set; } = true;

    public string CodeText => "0x" + Code.ToString("X8");
}

/// <summary>当前 Windows 版本信息。</summary>
public sealed class WindowsOsInfo
{
    public string ProductName { get; init; } = "";
    public string Family { get; init; } = "";
    public string DisplayVersion { get; init; } = "";
    public int Build { get; init; }
    public bool IsServer { get; init; }
    public string AppliesTo =>
        "Windows 10 / Windows 11 / Windows Server 2016-2025";

    public string FullName =>
        (ProductName.Length > 0 ? ProductName : Family)
        + (DisplayVersion.Length > 0 ? " " + DisplayVersion : "")
        + "（Build " + Build + "）";
}

/// <summary>
/// 蓝屏代码数据库：内置 Microsoft Learn 官方 Bug Check Code Reference（2026-11 抓取）全部条目，
/// 并为 Windows 10 / 11 / Server 2016-2025 常见代码提供简体中文名称、原因与解决方法。
/// 未单独翻译的代码会按类别生成中文说明；微软英文原文可在线获取并缓存。
/// </summary>
public static partial class BugCheckDatabase
{
    public const string MicrosoftIndexUrl =
        "https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/bug-check-code-reference2";

    private sealed record CuratedInfo(
        string ChineseName,
        string Cause,
        string Solution,
        string EnglishCause,
        string EnglishResolution);

    private static readonly Dictionary<uint, CuratedInfo> Curated = new()
    {
        [0x0000000A] = new(
            "IRQL 不小于或等于",
            "内核模式驱动或线程在过高的 IRQL 级别访问了可分页内存、无效地址，或长时间占用自旋锁。常见于网卡、显卡、杀毒、虚拟化驱动不兼容，以及内存故障或超频。",
            "1. 卸载或更新最近安装的驱动与软件；\n2. 运行 Windows 内存诊断；\n3. 关闭 CPU / 内存超频与 XMP；\n4. 运行 sfc /scannow 与 DISM 修复系统文件；\n5. 若最近更新后出现，进入安全模式回滚驱动或更新；\n6. 使用 Driver Verifier 定位问题驱动。",
            "A kernel-mode driver or thread accessed paged memory at a too-high IRQL, used an invalid address, or held a spinlock for too long.",
            "Update or roll back recently installed drivers and software. Run Windows Memory Diagnostic, disable overclocking/XMP, run SFC and DISM, and use Driver Verifier to identify the faulty driver."),
        [0x0000001E] = new(
            "内核模式异常未处理",
            "内核模式驱动或系统组件抛出了异常，但没有对应的异常处理程序。常见于驱动不兼容、系统文件损坏或硬件故障。",
            "1. 更新或回滚显卡、芯片组、网卡等驱动；\n2. 运行 sfc /scannow 与 DISM；\n3. 运行内存诊断；\n4. 安全模式下排查最近安装的软件；\n5. 检查蓝屏参数中的异常地址与相关模块。",
            "A kernel-mode exception was raised but not handled by a trap handler. This is often caused by an incompatible driver, corrupted system files, or faulty hardware.",
            "Update or roll back drivers, run SFC/DISM, test memory, and use Safe Mode to isolate recently installed software."),
        [0x00000023] = new(
            "FAT 文件系统错误",
            "FAT 文件系统驱动检测到磁盘结构损坏、坏扇区或存储驱动问题。",
            "1. 运行 chkdsk /f /r 检查磁盘；\n2. 检查硬盘 / SSD 健康状态与数据线；\n3. 更新存储控制器驱动；\n4. 备份数据并考虑更换磁盘。",
            "The FAT file system driver detected a corrupted disk structure or a storage problem.",
            "Run chkdsk /f /r, check disk health and cables, update storage drivers, and back up data."),
        [0x00000024] = new(
            "NTFS 文件系统错误",
            "NTFS 文件系统驱动检测到磁盘结构损坏、坏扇区、存储驱动异常或文件系统元数据损坏。",
            "1. 运行 chkdsk /f /r；\n2. 检查磁盘 SMART 状态与数据线；\n3. 更新存储 / 芯片组驱动；\n4. 运行 sfc /scannow 与 DISM；\n5. 备份数据并考虑更换磁盘。",
            "The NTFS file system driver detected corruption in disk structures or a storage-related problem.",
            "Run chkdsk /f /r, check SMART status and cables, update storage drivers, run SFC/DISM, and back up data."),
        [0x0000003B] = new(
            "系统服务异常",
            "从非特权代码切换到内核模式后，内核模式例程抛出了未处理异常。常见于显卡 / 网卡驱动、第三方杀毒或虚拟化软件、系统文件损坏。",
            "1. 更新或回滚显卡、芯片组、网卡驱动；\n2. 卸载第三方杀毒 / 虚拟化 / 系统优化软件测试；\n3. 运行 sfc /scannow 与 DISM；\n4. 运行内存诊断；\n5. 回滚最近安装的 Windows 更新；\n6. 使用 Driver Verifier 定位驱动。",
            "A system service routine raised an exception that was not handled. This is commonly caused by a graphics or network driver, third-party antivirus/virtualization software, or corrupted system files.",
            "Update or roll back drivers, remove third-party security/virtualization software, run SFC/DISM, test memory, and roll back recent updates."),
        [0x00000050] = new(
            "非分页区域中的页面错误",
            "系统引用了无效、已释放或不可访问的内存地址，常见于驱动缺陷、内存条故障、系统文件损坏或软件冲突。",
            "1. 运行 Windows 内存诊断；\n2. 更新或回滚驱动（尤其是显卡、网卡、杀毒）；\n3. 运行 sfc /scannow 与 DISM；\n4. 运行 chkdsk 检查磁盘；\n5. 关闭超频 / XMP；\n6. 卸载可疑软件。",
            "The system referenced an invalid, freed, or inaccessible memory address, often due to a faulty driver, bad RAM, or corrupted system files.",
            "Run Windows Memory Diagnostic, update or roll back drivers, run SFC/DISM and chkdsk, disable overclocking, and remove suspicious software."),
        [0x00000077] = new(
            "内核栈页面错误",
            "系统从分页文件读取内核栈页面失败，常见于磁盘故障、存储驱动问题或页面文件损坏。",
            "1. 运行 chkdsk /f /r；\n2. 检查磁盘 SMART 与数据线；\n3. 更新存储驱动；\n4. 检查页面文件所在磁盘空间；\n5. 运行内存诊断。",
            "The kernel stack page could not be read from the paging file, usually because of a disk or storage driver problem.",
            "Run chkdsk /f /r, check disk health, update storage drivers, verify paging file settings, and test memory."),
        [0x0000007A] = new(
            "内核数据页面错误",
            "系统无法从页面文件读取内核数据页面，常见于磁盘坏道、存储控制器 / 驱动故障、内存故障或页面文件损坏。",
            "1. 运行 chkdsk /f /r；\n2. 检查硬盘 / SSD 健康与数据线；\n3. 更新存储控制器驱动与固件；\n4. 运行内存诊断；\n5. 检查页面文件并确保磁盘空间充足。",
            "A kernel data page could not be read from the paging file, often due to disk errors, storage driver problems, or faulty memory.",
            "Run chkdsk /f /r, check disk health, update storage drivers/firmware, test memory, and verify the paging file."),
        [0x0000007B] = new(
            "无法访问启动设备",
            "Windows 在启动时无法访问系统分区，常见于 BIOS / UEFI 中的存储模式变化（AHCI / RAID / IDE）、存储驱动缺失、引导配置损坏或磁盘故障。",
            "1. 检查 BIOS 中的 SATA / NVMe / RAID 模式是否被更改；\n2. 进入恢复环境运行启动修复；\n3. 检查磁盘是否被识别、数据线是否松动；\n4. 使用系统还原；\n5. 必要时备份数据后重装系统。",
            "Windows could not access the system partition during startup, often because of a changed storage mode, missing storage driver, boot configuration corruption, or disk failure.",
            "Check BIOS/UEFI storage settings, run Startup Repair, verify the disk is detected, use System Restore, and back up data before reinstalling."),
        [0x0000007E] = new(
            "系统线程异常未处理",
            "系统线程产生了未处理的异常，常见于驱动不兼容、软件冲突、系统文件损坏或硬件故障。",
            "1. 更新或回滚最近安装的驱动；\n2. 安全模式下卸载可疑软件；\n3. 运行 sfc /scannow 与 DISM；\n4. 运行内存诊断；\n5. 检查蓝屏参数中的异常地址对应模块。",
            "A system thread generated an exception that was not handled, commonly due to an incompatible driver or corrupted system files.",
            "Update or roll back drivers, remove suspicious software in Safe Mode, run SFC/DISM, and test memory."),
        [0x0000007F] = new(
            "意外的内核模式陷阱",
            "CPU 产生了内核无法处理的陷阱，例如双重故障、非法指令、栈溢出或硬件错误。可能与内存、CPU、主板、散热或驱动有关。",
            "1. 恢复 BIOS 默认设置并关闭超频；\n2. 运行内存诊断；\n3. 检查 CPU / 主板散热与供电；\n4. 更新 BIOS、芯片组与驱动；\n5. 运行 sfc /scannow 与 DISM；\n6. 逐件替换硬件排查。",
            "The processor generated a trap that the kernel could not handle, such as a double fault, invalid instruction, or stack overflow.",
            "Restore BIOS defaults, disable overclocking, test memory, check cooling/power, update BIOS and drivers, and run SFC/DISM."),
        [0x0000009C] = new(
            "机器检查异常",
            "CPU 或硬件检测到不可恢复的机器检查错误，常见于 CPU 故障、过热、供电不稳定、内存或主板问题。",
            "1. 检查散热、风扇与电源；\n2. 恢复 BIOS 默认设置，关闭超频；\n3. 运行内存诊断；\n4. 更新 BIOS / 固件；\n5. 查看 WHEA 日志；\n6. 逐件替换硬件测试。",
            "The processor detected a machine-check exception indicating a hardware error such as overheating, unstable power, or faulty memory.",
            "Check cooling and power, restore BIOS defaults, test memory, update firmware, review WHEA logs, and test hardware individually."),
        [0x0000009F] = new(
            "驱动程序电源状态故障",
            "驱动未能正确完成电源状态转换（睡眠、唤醒、关机或重启），常见于旧版网卡、显卡、USB、蓝牙或芯片组驱动。",
            "1. 更新主板芯片组、网卡、显卡、USB、蓝牙驱动；\n2. 关闭“快速启动”；\n3. 检查电源计划与唤醒设备；\n4. 拔除不必要的外接设备测试；\n5. 更新 BIOS。",
            "A driver failed to complete a power state transition, often caused by outdated network, graphics, USB, Bluetooth, or chipset drivers.",
            "Update chipset/network/graphics/USB/Bluetooth drivers, disable Fast Startup, review power settings, remove unnecessary devices, and update BIOS."),
        [0x000000A0] = new(
            "内部电源错误",
            "电源策略管理器或驱动在电源转换过程中发生严重错误，常见于休眠文件损坏、存储 / 显卡驱动问题、电源或电池故障。",
            "1. 关闭休眠并删除 hiberfil.sys；\n2. 更新芯片组、显卡、存储驱动；\n3. 关闭快速启动；\n4. 检查电源、电池与供电；\n5. 运行电源疑难解答。",
            "The power policy manager encountered a fatal error during a power transition, often related to hibernation files, storage/graphics drivers, or power hardware.",
            "Disable hibernation, update chipset/graphics/storage drivers, disable Fast Startup, check power/battery hardware, and run the power troubleshooter."),
        [0x000000A5] = new(
            "ACPI BIOS 错误",
            "ACPI 固件与 Windows 不兼容，或 ACPI 驱动 / 硬件发生错误。常见于老旧 BIOS、主板固件缺陷、虚拟机配置问题。",
            "1. 更新 BIOS / UEFI 与芯片组驱动；\n2. 恢复 BIOS 默认设置；\n3. 检查 ACPI 相关设备与电池；\n4. 虚拟机中更新虚拟化平台；\n5. 若为旧硬件，尝试兼容模式。",
            "The ACPI firmware is incompatible with Windows, or an ACPI driver/hardware error occurred.",
            "Update BIOS/UEFI and chipset drivers, restore BIOS defaults, check ACPI devices and battery, and update the virtualization platform."),
        [0x000000BE] = new(
            "尝试写入只读内存",
            "驱动尝试修改标记为只读的内存页，通常是驱动缺陷、内存损坏或恶意软件。",
            "1. 更新或回滚最近安装的驱动；\n2. 使用 Driver Verifier 定位驱动；\n3. 运行内存诊断；\n4. 运行 sfc /scannow 与 DISM；\n5. 安全扫描。",
            "A driver attempted to write to read-only memory, usually because of a driver bug or faulty memory.",
            "Update or roll back drivers, use Driver Verifier, test memory, run SFC/DISM, and perform a security scan."),
        [0x000000C1] = new(
            "专用池检测到内存损坏",
            "驱动在专用内存池中检测到越界写或已释放内存访问，通常由驱动缺陷引起。",
            "1. 使用 Driver Verifier；\n2. 更新或卸载最近安装的驱动；\n3. 运行内存诊断；\n4. 检查第三方内核软件 / 杀毒软件。",
            "The special pool detected memory corruption, usually caused by a driver writing beyond an allocation.",
            "Use Driver Verifier, update or remove recently installed drivers, test memory, and check third-party kernel software."),
        [0x000000C2] = new(
            "错误的池调用程序",
            "驱动以非法方式分配或释放内存池，通常由驱动缺陷或内存损坏引起。",
            "1. 更新或卸载最近安装的驱动；\n2. 使用 Driver Verifier；\n3. 运行 sfc /scannow 与 DISM；\n4. 运行内存诊断。",
            "A driver made an invalid memory pool request, usually because of a driver bug.",
            "Update or remove recently installed drivers, use Driver Verifier, run SFC/DISM, and test memory."),
        [0x000000C4] = new(
            "驱动程序验证程序检测到违规",
            "Driver Verifier 检测到驱动违反规则。参数 1 通常指明违规类型，可据此定位具体驱动。",
            "1. 根据参数与蓝屏信息定位驱动；\n2. 更新或卸载问题驱动；\n3. 运行 `verifier /reset` 关闭验证；\n4. 检查驱动兼容性与签名。",
            "Driver Verifier detected that a driver violated a rule. Parameter 1 identifies the violation type.",
            "Identify the driver from the parameters, update or remove it, and reset Driver Verifier with verifier /reset."),
        [0x000000C5] = new(
            "驱动程序破坏了执行池",
            "驱动在过高的 IRQL 访问了无效内存，或破坏了执行体内存池。",
            "1. 使用 Driver Verifier 定位驱动；\n2. 更新或回滚最近安装的驱动；\n3. 运行内存诊断；\n4. 运行 sfc /scannow 与 DISM。",
            "A driver corrupted the executive pool or accessed invalid memory at a high IRQL.",
            "Use Driver Verifier to identify the driver, update or roll it back, test memory, and run SFC/DISM."),
        [0x000000C6] = new(
            "驱动修改已释放的内存池",
            "驱动在内存池释放后继续修改该内存，通常由驱动缺陷引起。",
            "1. 使用 Driver Verifier；\n2. 更新或卸载问题驱动；\n3. 检查第三方杀毒 / 虚拟化驱动。",
            "A driver modified a pool allocation after it had been freed.",
            "Use Driver Verifier, update or remove the faulty driver, and check third-party security/virtualization drivers."),
        [0x000000C7] = new(
            "定时器或 DPC 无效",
            "驱动在无效的 IRQL 下操作定时器或 DPC，或重复排队已释放的 DPC。",
            "1. 更新或回滚最近安装的驱动；\n2. 使用 Driver Verifier；\n3. 检查网卡 / 存储 / 虚拟化驱动。",
            "A timer or DPC was used at an invalid IRQL or after being freed.",
            "Update or roll back drivers, use Driver Verifier, and check network/storage/virtualization drivers."),
        [0x000000C9] = new(
            "驱动程序验证程序 I/O 管理器违规",
            "Driver Verifier 的 I/O 管理器检测到驱动违规，通常与 IRP 处理或对象引用有关。",
            "1. 根据参数定位驱动；\n2. 更新或卸载问题驱动；\n3. 使用 Driver Verifier 复现。",
            "Driver Verifier detected an I/O manager violation, often related to IRP handling or object references.",
            "Identify the driver from the parameters, update or remove it, and reproduce with Driver Verifier."),
        [0x000000CE] = new(
            "驱动卸载前未取消挂起操作",
            "驱动在卸载时仍有未取消的挂起操作，通常由驱动缺陷引起。",
            "1. 更新或卸载问题驱动；\n2. 使用 Driver Verifier；\n3. 检查最近安装的过滤驱动 / 杀毒软件。",
            "A driver unloaded without canceling pending operations.",
            "Update or remove the faulty driver, use Driver Verifier, and check recently installed filter drivers."),
        [0x000000D1] = new(
            "驱动 IRQL 不小于或等于",
            "驱动在过高的 IRQL 访问了可分页内存或无效地址。常见于网卡、显卡、杀毒、虚拟化驱动不兼容。",
            "1. 更新或回滚最近安装的驱动；\n2. 使用 Driver Verifier；\n3. 运行内存诊断；\n4. 卸载第三方杀毒 / 虚拟化软件；\n5. 运行 sfc /scannow 与 DISM。",
            "A driver accessed paged memory or an invalid address at a high IRQL.",
            "Update or roll back drivers, use Driver Verifier, test memory, remove third-party security/virtualization software, and run SFC/DISM."),
        [0x000000D6] = new(
            "驱动页面错误超出分配末尾",
            "驱动访问了超出内存分配范围的内存，通常由缓冲区越界或驱动缺陷引起。",
            "1. 使用 Driver Verifier；\n2. 更新或卸载问题驱动；\n3. 检查第三方内核软件。",
            "A driver caused a page fault beyond the end of an allocation.",
            "Use Driver Verifier, update or remove the faulty driver, and check third-party kernel software."),
        [0x000000D8] = new(
            "驱动使用了过多页表项",
            "驱动消耗了过多系统页表项（PTE），通常由内存泄漏或大量映射引起。",
            "1. 更新或卸载问题驱动；\n2. 使用 Driver Verifier；\n3. 检查虚拟化 / 存储 / 网络驱动。",
            "A driver used an excessive number of page table entries.",
            "Update or remove the faulty driver, use Driver Verifier, and check virtualization/storage/network drivers."),
        [0x000000DA] = new(
            "系统页表项误用",
            "驱动错误地操作页表项（PTE），通常由驱动缺陷或内存损坏导致。",
            "1. 使用 Driver Verifier；\n2. 更新或回滚驱动；\n3. 运行内存诊断；\n4. 检查第三方内核软件。",
            "A driver misused a system page table entry.",
            "Use Driver Verifier, update or roll back drivers, test memory, and check third-party kernel software."),
        [0x000000E6] = new(
            "驱动程序验证程序 DMA 违规",
            "Driver Verifier 检测到驱动违反 DMA 规则。",
            "1. 更新或卸载问题驱动；\n2. 使用 Driver Verifier 定位；\n3. 检查主板芯片组与存储驱动。",
            "Driver Verifier detected a DMA violation by a driver.",
            "Update or remove the faulty driver, use Driver Verifier, and check chipset/storage drivers."),
        [0x000000ED] = new(
            "无法装载启动卷",
            "Windows 无法装载启动卷，常见于文件系统损坏、磁盘坏道、存储驱动问题或数据线故障。",
            "1. 运行 chkdsk /f /r；\n2. 检查磁盘与数据线；\n3. 进入恢复环境运行启动修复；\n4. 更新存储驱动；\n5. 备份数据。",
            "Windows could not mount the boot volume because of file system corruption, disk errors, or storage problems.",
            "Run chkdsk /f /r, check cables and disk health, run Startup Repair, update storage drivers, and back up data."),
        [0x000000EF] = new(
            "关键进程已终止",
            "关键系统进程（如 csrss、wininit、services）意外终止，常由系统文件损坏、恶意软件、驱动或硬件故障引起。",
            "1. 运行 sfc /scannow 与 DISM；\n2. 执行完整杀毒扫描；\n3. 更新驱动；\n4. 使用系统还原或修复安装；\n5. 检查内存与磁盘。",
            "A critical system process terminated unexpectedly, often due to corrupted system files, malware, drivers, or hardware failure.",
            "Run SFC/DISM, perform a full malware scan, update drivers, use System Restore or repair install, and check memory/disk."),
        [0x000000F2] = new(
            "硬件中断风暴",
            "硬件设备持续产生中断，导致系统无法前进，常见于故障设备、驱动或主板。",
            "1. 断开不必要的外接设备；\n2. 更新芯片组与设备驱动；\n3. 检查设备管理器中的冲突设备；\n4. 更新 BIOS；\n5. 逐件替换硬件。",
            "A hardware device generated an interrupt storm that prevented the system from making progress.",
            "Disconnect unnecessary devices, update chipset/device drivers, check Device Manager for conflicts, update BIOS, and test hardware."),
        [0x000000F3] = new(
            "无序关机",
            "系统在未正常关机的情况下终止，常见于电源故障、强制断电或系统挂起。",
            "1. 检查电源与供电稳定性；\n2. 关闭快速启动；\n3. 更新芯片组 / 存储驱动；\n4. 检查是否过热或死机；\n5. 查看事件日志。",
            "The system shut down without completing an orderly shutdown, often due to power loss or a hang.",
            "Check power stability, disable Fast Startup, update chipset/storage drivers, check overheating, and review event logs."),
        [0x000000F4] = new(
            "关键对象终止",
            "关键系统对象或进程被终止，通常伴随文件系统、存储或驱动问题。",
            "1. 运行 chkdsk /f /r；\n2. 运行 sfc /scannow 与 DISM；\n3. 更新存储 / 文件系统驱动；\n4. 执行杀毒扫描；\n5. 检查磁盘健康。",
            "A critical system object was terminated, often related to file system, storage, or driver problems.",
            "Run chkdsk /f /r, SFC/DISM, update storage/file system drivers, perform a malware scan, and check disk health."),
        [0x000000F5] = new(
            "FLTMGR 文件系统错误",
            "过滤管理器（fltmgr）检测到文件系统过滤驱动错误，常见于杀毒、备份、加密或虚拟化过滤驱动。",
            "1. 更新或卸载第三方杀毒 / 加密 / 备份软件；\n2. 运行 chkdsk /f /r；\n3. 运行 sfc /scannow 与 DISM；\n4. 更新存储驱动。",
            "The filter manager detected a file system filter driver error, often caused by antivirus, encryption, backup, or virtualization filter drivers.",
            "Update or remove third-party security/encryption/backup software, run chkdsk and SFC/DISM, and update storage drivers."),
        [0x000000F7] = new(
            "驱动栈缓冲区溢出",
            "驱动发生了栈缓冲区溢出，通常是驱动安全缺陷、恶意代码或内存损坏。",
            "1. 更新或卸载问题驱动；\n2. 启用 Driver Verifier；\n3. 执行完整安全扫描；\n4. 运行内存诊断。",
            "A driver overran a stack-based buffer, usually due to a driver flaw or malicious code.",
            "Update or remove the faulty driver, enable Driver Verifier, perform a security scan, and test memory."),
        [0x000000FC] = new(
            "尝试执行不可执行内存",
            "驱动尝试执行标记为不可执行的内存页，常见于驱动缺陷、恶意软件或内存损坏。",
            "1. 更新或回滚驱动；\n2. 使用 Driver Verifier；\n3. 执行杀毒扫描；\n4. 运行内存诊断。",
            "A driver attempted to execute memory that was marked non-executable.",
            "Update or roll back drivers, use Driver Verifier, perform a malware scan, and test memory."),
        [0x000000FE] = new(
            "USB 驱动错误",
            "USB 驱动或 USB 设备发生严重错误，常见于 USB 控制器、集线器、外接设备或驱动兼容性问题。",
            "1. 拔除不必要的外接 USB 设备；\n2. 更新芯片组 / USB 控制器驱动；\n3. 关闭 USB 选择性挂起；\n4. 更换 USB 线缆 / 接口测试；\n5. 更新 BIOS。",
            "A USB driver or device encountered a fatal error, often related to the USB controller, hub, device, or driver compatibility.",
            "Remove unnecessary USB devices, update chipset/USB drivers, disable USB selective suspend, test cables/ports, and update BIOS."),
        [0x00000101] = new(
            "时钟看门狗超时",
            "多处理器系统中某个 CPU 内核在预期时间内没有响应中断。常见于 CPU 超频 / 降压、散热不足、BIOS 缺陷、虚拟化或驱动问题。",
            "1. 恢复 BIOS 默认设置并关闭超频 / 降压；\n2. 检查 CPU 散热与供电；\n3. 更新 BIOS 与芯片组驱动；\n4. 更新虚拟化平台；\n5. 排查驱动与系统服务。",
            "A processor core did not respond to an interrupt within the expected time, often due to overclocking, cooling, BIOS, virtualization, or driver issues.",
            "Restore BIOS defaults, disable overclocking/undervolting, check cooling and power, update BIOS/chipset, update virtualization, and check drivers."),
        [0x00000109] = new(
            "关键内核结构损坏",
            "内核代码或关键数据结构被修改，常见于驱动打补丁、恶意软件、内存故障或内核级软件冲突。",
            "1. 更新或卸载驱动与内核级软件；\n2. 启用 Driver Verifier；\n3. 执行杀毒 / 反作弊扫描；\n4. 运行内存诊断；\n5. 运行 sfc /scannow 与 DISM。",
            "The kernel detected corruption of a critical kernel structure, often due to driver patching, malware, or faulty memory.",
            "Update or remove drivers/kernel software, enable Driver Verifier, run a security scan, test memory, and run SFC/DISM."),
        [0x0000010D] = new(
            "WDF 违规",
            "Windows 驱动程序框架（WDF）检测到驱动违规，参数 1 通常指明违规类型。",
            "1. 根据参数定位驱动；\n2. 更新或卸载问题驱动；\n3. 使用 Driver Verifier；\n4. 检查驱动版本兼容性。",
            "The Windows Driver Framework detected a driver violation. Parameter 1 identifies the violation type.",
            "Identify the driver from the parameters, update or remove it, use Driver Verifier, and verify driver compatibility."),
        [0x0000010E] = new(
            "视频内存管理内部错误",
            "显卡驱动或视频内存管理器发生内部错误，常见于显卡驱动缺陷、显存故障或超频。",
            "1. 更新或回滚显卡驱动；\n2. 关闭显卡超频；\n3. 检查显卡散热与供电；\n4. 更新主板 BIOS；\n5. 测试显卡硬件。",
            "An internal video memory manager error occurred, often caused by a graphics driver bug, faulty VRAM, or overclocking.",
            "Update or roll back the graphics driver, disable overclocking, check cooling/power, update BIOS, and test the GPU."),
        [0x00000116] = new(
            "显卡 TDR 失败",
            "显卡驱动在超时时间内未响应，系统尝试恢复显示驱动失败。常见于显卡驱动 / 硬件、过热、供电不足或超频。",
            "1. 更新或回滚显卡驱动；\n2. 检查显卡散热与电源；\n3. 关闭显卡超频；\n4. 更新主板 BIOS；\n5. 测试显卡硬件。",
            "The graphics driver stopped responding and recovery failed (TDR failure), often due to driver/hardware issues, overheating, or insufficient power.",
            "Update or roll back the graphics driver, check cooling and power, disable overclocking, update BIOS, and test the GPU."),
        [0x00000117] = new(
            "检测到显卡 TDR 超时",
            "显卡驱动响应超时，系统检测到 TDR（超时检测与恢复）。常见于驱动、显卡负载、过热或超频。",
            "1. 更新显卡驱动；\n2. 检查散热与供电；\n3. 关闭超频；\n4. 降低显存 / 核心负载测试；\n5. 必要时更换显卡测试。",
            "The graphics driver timed out and the system detected a TDR event, often due to driver, load, overheating, or overclocking.",
            "Update the graphics driver, check cooling/power, disable overclocking, reduce load, and test with another GPU if possible."),
        [0x00000119] = new(
            "视频调度器内部错误",
            "视频调度器检测到非法状态，通常由显卡驱动、硬件或超频引起。",
            "1. 更新或回滚显卡驱动；\n2. 关闭超频；\n3. 检查散热与供电；\n4. 更新系统与主板 BIOS。",
            "The video scheduler encountered an internal error, usually caused by a graphics driver/hardware issue or overclocking.",
            "Update or roll back the graphics driver, disable overclocking, check cooling/power, and update the system/BIOS."),
        [0x00000124] = new(
            "WHEA 不可纠正错误",
            "Windows 硬件错误架构（WHEA）检测到不可纠正的硬件错误，涉及 CPU、内存、PCIe、缓存或电源等。",
            "1. 检查散热与电源；\n2. 运行内存诊断；\n3. 关闭超频 / XMP；\n4. 更新 BIOS 与固件；\n5. 查看 WHEA 事件日志；\n6. 逐件替换硬件测试。",
            "Windows Hardware Error Architecture detected an uncorrectable hardware error involving the CPU, memory, PCIe, cache, or power subsystem.",
            "Check cooling and power, test memory, disable overclocking, update BIOS/firmware, review WHEA logs, and test hardware individually."),
        [0x00000133] = new(
            "DPC 看门狗违规",
            "DPC 看门狗检测到单个 DPC 运行时间过长，或系统长时间停留在 DISPATCH_LEVEL 及更高 IRQL。常见于存储、网卡、无线、显卡、虚拟化或杀毒驱动。",
            "1. 更新主板芯片组、存储、网卡、无线、显卡驱动；\n2. 卸载第三方杀毒 / 虚拟化软件测试；\n3. 更新 SSD 固件；\n4. 使用 Driver Verifier；\n5. 检查是否有驱动死循环。",
            "The DPC watchdog detected a single long-running deferred procedure call or prolonged time at DISPATCH_LEVEL or higher.",
            "Update chipset/storage/network/wireless/graphics drivers, remove third-party security/virtualization software, update SSD firmware, and use Driver Verifier."),
        [0x00000139] = new(
            "内核安全检查失败",
            "内核检测到关键数据结构被破坏，常见于驱动缺陷、内存损坏、恶意软件或内核级软件冲突。",
            "1. 使用 Driver Verifier 定位驱动；\n2. 更新或卸载最近安装的驱动；\n3. 运行内存诊断；\n4. 运行 sfc /scannow 与 DISM；\n5. 执行安全扫描。",
            "The kernel detected corruption of a critical data structure, often due to a driver bug, faulty memory, or malware.",
            "Use Driver Verifier, update or remove recently installed drivers, test memory, run SFC/DISM, and perform a security scan."),
        [0x0000013A] = new(
            "内核模式堆损坏",
            "内核模式堆检测到内存损坏，通常由驱动越界写、使用已释放内存或内存故障导致。",
            "1. 使用 Driver Verifier；\n2. 更新或卸载问题驱动；\n3. 运行内存诊断；\n4. 检查第三方内核软件 / 杀毒软件。",
            "The kernel-mode heap detected corruption, usually caused by a driver writing out of bounds or using freed memory.",
            "Use Driver Verifier, update or remove the faulty driver, test memory, and check third-party kernel software."),
        [0x00000144] = new(
            "USB 3 驱动错误",
            "所有 USB 3 严重错误的通用代码，参数 1 指明具体 USB 3 故障类型，常见于 xHCI 控制器、USB 设备、线缆或驱动问题。",
            "1. 更新芯片组 / USB 控制器驱动与固件；\n2. 拔除不必要的外接 USB 设备；\n3. 更换 USB 线缆 / 接口测试；\n4. 更新 BIOS；\n5. 检查设备管理器中的 USB 错误。",
            "A general code for USB 3 errors. Parameter 1 identifies the specific USB 3 failure, often related to the xHCI controller, device, cable, or driver.",
            "Update chipset/USB controller drivers and firmware, remove unnecessary USB devices, test cables/ports, update BIOS, and check Device Manager."),
        [0x00000154] = new(
            "意外的存储异常",
            "存储栈检测到未预期的异常，常见于存储驱动、SSD / HDD 固件、文件系统损坏或硬件故障。",
            "1. 更新存储控制器驱动与 SSD / HDD 固件；\n2. 运行 chkdsk /f /r；\n3. 检查磁盘 SMART 状态与数据线；\n4. 运行 sfc /scannow 与 DISM；\n5. 备份数据并考虑更换磁盘。",
            "The storage stack encountered an unexpected exception, often related to storage drivers, firmware, file system corruption, or hardware failure.",
            "Update storage drivers and drive firmware, run chkdsk, check SMART status and cables, run SFC/DISM, and back up data."),
        [0x00000192] = new(
            "内核自动提升锁定与 IRQL 冲突",
            "内核自动提升（Auto Boost）检测到在提升 IRQL 后仍尝试获取锁，通常由驱动同步缺陷引起。",
            "1. 更新或卸载最近安装的驱动；\n2. 使用 Driver Verifier；\n3. 检查网卡 / 存储 / 虚拟化驱动；\n4. 运行 sfc /scannow 与 DISM。",
            "Kernel Auto Boost detected a lock acquisition with a raised IRQL, usually caused by a driver synchronization bug.",
            "Update or remove recently installed drivers, use Driver Verifier, check network/storage/virtualization drivers, and run SFC/DISM.")
    };

    private static readonly Dictionary<uint, BugCheckInfo> Map = Build();

    public static IReadOnlyCollection<BugCheckInfo> All => Map.Values;

    private static Dictionary<uint, BugCheckInfo> Build()
    {
        var map = new Dictionary<uint, BugCheckInfo>();
        foreach (string line in RawCodeList.Split('\n'))
        {
            string text = line.Trim();
            if (text.Length == 0)
            {
                continue;
            }
            string[] parts = text.Split('|');
            if (parts.Length < 3
                || !uint.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber,
                    null, out uint code))
            {
                continue;
            }
            var info = new BugCheckInfo
            {
                Code = code,
                Name = parts[1],
                Url = parts[2]
            };
            map[code] = info;
        }
        foreach (var pair in Curated)
        {
            if (!map.TryGetValue(pair.Key, out var info))
            {
                info = new BugCheckInfo
                {
                    Code = pair.Key,
                    Name = "UNKNOWN",
                    Url = MicrosoftIndexUrl
                };
                map[pair.Key] = info;
            }
            var curated = pair.Value;
            info.ChineseName = curated.ChineseName;
            info.Cause = curated.Cause;
            info.Solution = curated.Solution;
            info.EnglishCause = curated.EnglishCause;
            info.EnglishResolution = curated.EnglishResolution;
        }
        foreach (var info in map.Values)
        {
            if (info.ChineseName.Length == 0)
            {
                info.ChineseName = BuildChineseName(info.Name);
                info.Cause = BuildGenericCause(info.Name);
                info.Solution = BuildGenericSolution(info.Name);
            }
        }
        return map;
    }

    /// <summary>按代码查询；未收录时返回通用说明。</summary>
    public static BugCheckInfo Lookup(uint code)
    {
        if (Map.TryGetValue(code, out var info))
        {
            return info;
        }
        return new BugCheckInfo
        {
            Code = code,
            Name = "UNKNOWN_BUGCHECK",
            Url = MicrosoftIndexUrl,
            Known = false,
            ChineseName = "微软代码库未收录的蓝屏代码",
            Cause = "该代码不在内置的 Microsoft Learn 蓝屏代码索引中。可能是厂商驱动自定义代码、"
                + "较新的 Windows 版本代码，或转储头损坏导致代码解析错误。",
            Solution = "1. 以管理员身份运行本工具并重新扫描转储；\n"
                + "2. 点击“打开微软官方代码库”查询最新代码；\n"
                + "3. 更新主板芯片组、显卡、网卡、存储驱动；\n"
                + "4. 运行 Windows 内存诊断、chkdsk、sfc /scannow 与 DISM；\n"
                + "5. 若问题持续，使用 Driver Verifier 或联系设备厂商。"
        };
    }

    public static int CodeCount => Map.Count;

    private static string CategoryOf(string name)
    {
        string upper = name.ToUpperInvariant();
        if (ContainsAny(upper, "VIDEO", "DXGKRNL", "DISPLAY", "GRAPHICS"))
        {
            return "显卡 / 显示驱动类";
        }
        if (ContainsAny(upper, "USB", "HID"))
        {
            return "USB / 外设类";
        }
        if (ContainsAny(upper, "NDIS", "TCPIP", "NETIO", "WIFI", "WLAN", "NETWORK"))
        {
            return "网络驱动类";
        }
        if (ContainsAny(upper, "FILE_SYSTEM", "NTFS", "FAT", "EXFAT", "FLTMGR",
            "VOLUME", "STORE", "DISK"))
        {
            return "文件系统 / 存储类";
        }
        if (ContainsAny(upper, "POWER", "ACPI", "SLEEP", "HIBERN"))
        {
            return "电源 / ACPI 类";
        }
        if (ContainsAny(upper, "MEMORY", "PAGE", "PFN", "POOL", "HEAP", "PTE"))
        {
            return "内存管理类";
        }
        if (ContainsAny(upper, "DRIVER", "DEVICE"))
        {
            return "驱动程序类";
        }
        if (ContainsAny(upper, "HYPERVISOR", "VIRTUAL"))
        {
            return "虚拟化类";
        }
        if (ContainsAny(upper, "KERNEL", "SYSTEM", "SECURITY"))
        {
            return "内核 / 系统类";
        }
        return "系统内核类";
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.Ordinal));

    private static string BuildChineseName(string name)
    {
        string category = CategoryOf(name);
        return category + "错误（" + name + "）";
    }

    private static string BuildGenericCause(string name)
    {
        string category = CategoryOf(name);
        return category switch
        {
            "显卡 / 显示驱动类" =>
                "该蓝屏代码表示显卡、显示驱动或视频内存管理器发生严重错误。常见原因包括显卡驱动缺陷、"
                + "显卡过热、供电不足、显存故障或超频不稳定。",
            "USB / 外设类" =>
                "该蓝屏代码表示 USB 控制器、USB 设备或相关驱动发生严重错误。常见原因包括 USB 设备/线缆故障、"
                + "xHCI 控制器驱动问题、供电不足或设备兼容性冲突。",
            "网络驱动类" =>
                "该蓝屏代码表示网络驱动或网络协议栈发生严重错误。常见原因包括网卡/无线驱动缺陷、VPN 或防火墙"
                + "过滤驱动冲突、驱动版本不兼容。",
            "文件系统 / 存储类" =>
                "该蓝屏代码表示文件系统、存储驱动或磁盘发生严重错误。常见原因包括磁盘坏道、SSD 固件问题、"
                + "存储控制器驱动异常、文件系统损坏或数据线接触不良。",
            "电源 / ACPI 类" =>
                "该蓝屏代码表示电源管理或 ACPI 固件/驱动发生严重错误。常见原因包括 BIOS 与 Windows 不兼容、"
                + "休眠文件损坏、电源/电池故障或驱动未能完成电源状态转换。",
            "内存管理类" =>
                "该蓝屏代码表示内核内存管理检测到异常。常见原因包括内存条故障、驱动越界访问、释放后使用、"
                + "内存池损坏或超频不稳定。",
            "驱动程序类" =>
                "该蓝屏代码表示某个内核驱动发生严重错误。常见原因包括驱动版本不兼容、驱动缺陷、"
                + "驱动与系统/其他安全软件冲突，或硬件设备故障。",
            "虚拟化类" =>
                "该蓝屏代码表示虚拟化组件或 Hyper-V 相关驱动发生错误。常见原因包括虚拟化平台版本不匹配、"
                + "CPU 虚拟化功能/BIOS 设置问题或第三方虚拟化驱动冲突。",
            _ =>
                "该蓝屏代码表示 Windows 内核检测到无法恢复的系统错误。常见原因包括驱动程序冲突、"
                + "系统文件损坏、内存或磁盘故障、超频不稳定以及恶意软件。"
        };
    }

    private static string BuildGenericSolution(string name)
    {
        string category = CategoryOf(name);
        return category switch
        {
            "显卡 / 显示驱动类" =>
                "1. 更新或回滚显卡驱动（优先使用厂商官方版本）；\n"
                + "2. 检查显卡散热、风扇与电源功率；\n"
                + "3. 关闭显卡超频 / 降压；\n"
                + "4. 更新主板 BIOS 与芯片组驱动；\n"
                + "5. 若仍出现，使用另一块显卡或集成显卡测试。",
            "USB / 外设类" =>
                "1. 拔除不必要的外接 USB 设备与集线器；\n"
                + "2. 更换 USB 线缆与接口测试；\n"
                + "3. 更新芯片组 / USB 控制器驱动与 BIOS；\n"
                + "4. 在电源选项中关闭 USB 选择性挂起；\n"
                + "5. 检查设备管理器中的黄色感叹号设备。",
            "网络驱动类" =>
                "1. 更新或回滚网卡 / 无线网卡驱动；\n"
                + "2. 暂时卸载 VPN、防火墙与第三方杀毒软件测试；\n"
                + "3. 重置网络（netsh winsock reset / netsh int ip reset）；\n"
                + "4. 更新主板芯片组与 BIOS；\n"
                + "5. 使用 Driver Verifier 定位过滤驱动。",
            "文件系统 / 存储类" =>
                "1. 运行 chkdsk /f /r；\n"
                + "2. 检查磁盘 SMART 状态与数据线；\n"
                + "3. 更新存储控制器驱动与 SSD / HDD 固件；\n"
                + "4. 运行 sfc /scannow 与 DISM；\n"
                + "5. 备份重要数据并考虑更换磁盘。",
            "电源 / ACPI 类" =>
                "1. 更新 BIOS / UEFI 与芯片组驱动；\n"
                + "2. 关闭快速启动与休眠（powercfg /h off）；\n"
                + "3. 恢复 BIOS 默认设置；\n"
                + "4. 检查电源、电池与供电稳定性；\n"
                + "5. 移除外接设备后测试睡眠 / 唤醒。",
            "内存管理类" =>
                "1. 运行 Windows 内存诊断；\n"
                + "2. 关闭内存超频 / XMP；\n"
                + "3. 更新或回滚最近安装的驱动；\n"
                + "4. 使用 Driver Verifier；\n"
                + "5. 逐条内存替换测试。",
            "驱动程序类" =>
                "1. 更新或卸载最近安装的驱动；\n"
                + "2. 使用 Driver Verifier 定位问题驱动；\n"
                + "3. 运行 sfc /scannow 与 DISM；\n"
                + "4. 检查设备管理器中的异常设备；\n"
                + "5. 若最近更新后出现，回滚驱动或系统更新。",
            "虚拟化类" =>
                "1. 更新 Hyper-V / 虚拟化平台与集成服务；\n"
                + "2. 检查 BIOS 中的 VT-x / AMD-V / IOMMU 设置；\n"
                + "3. 更新芯片组与网卡驱动；\n"
                + "4. 暂时关闭第三方虚拟化软件测试；\n"
                + "5. 检查虚拟机配置与内存分配。",
            _ =>
                "1. 运行 sfc /scannow 与 DISM；\n"
                + "2. 运行 Windows 内存诊断与 chkdsk；\n"
                + "3. 更新或回滚最近安装的驱动与软件；\n"
                + "4. 关闭超频 / XMP，检查散热与电源；\n"
                + "5. 执行完整杀毒扫描；\n"
                + "6. 若问题持续，使用系统还原、修复安装或联系厂商。"
        };
    }

    // ================= 操作系统检测 =================

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OsVersionInfoEx
    {
        public int dwOSVersionInfoSize;
        public int dwMajorVersion;
        public int dwMinorVersion;
        public int dwBuildNumber;
        public int dwPlatformId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szCSDVersion;

        public ushort wServicePackMajor;
        public ushort wServicePackMinor;
        public ushort wSuiteMask;
        public byte wProductType;
        public byte wReserved;
    }

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int RtlGetVersion(ref OsVersionInfoEx versionInfo);

    public static WindowsOsInfo DetectOperatingSystem()
    {
        int build = 0;
        int productType = 1;
        try
        {
            var info = new OsVersionInfoEx
            {
                dwOSVersionInfoSize = Marshal.SizeOf<OsVersionInfoEx>(),
                szCSDVersion = ""
            };
            if (RtlGetVersion(ref info) == 0)
            {
                build = info.dwBuildNumber;
                productType = info.wProductType;
            }
        }
        catch
        {
        }
        if (build == 0)
        {
            build = Environment.OSVersion.Version.Build;
        }
        string productName = ReadRegistry("ProductName");
        string displayVersion = ReadRegistry("DisplayVersion");
        bool server = productType != 1
            || productName.Contains("Server", StringComparison.OrdinalIgnoreCase);
        string family;
        if (server)
        {
            family = build switch
            {
                >= 26100 => "Windows Server 2025",
                >= 20348 => "Windows Server 2022",
                >= 17763 => "Windows Server 2019",
                >= 14393 => "Windows Server 2016",
                _ => "Windows Server"
            };
        }
        else
        {
            family = build >= 22000 ? "Windows 11"
                : build >= 10240 ? "Windows 10"
                : "Windows";
        }
        if (productName.Length == 0)
        {
            productName = family;
        }
        else if (!server && build >= 22000
            && productName.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase))
        {
            // Windows 11 的注册表 ProductName 仍可能显示 Windows 10。
            productName = "Windows 11" + productName["Windows 10".Length..];
        }
        return new WindowsOsInfo
        {
            ProductName = productName,
            Family = family,
            DisplayVersion = displayVersion,
            Build = build,
            IsServer = server
        };
    }

    private static string ReadRegistry(string name)
    {
        try
        {
            object? value = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                name, null);
            return value?.ToString()?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }

    // ================= 微软原文获取 / 缓存 =================

    public static string DocCachePath(uint code) => Path.Combine(
        AppPaths.Sub("BlueScreenView"), "docs",
        code.ToString("X8") + ".txt");

    public static (string Cause, string Resolution) ReadCachedDoc(uint code)
    {
        try
        {
            string path = DocCachePath(code);
            if (!File.Exists(path))
            {
                return ("", "");
            }
            string text = File.ReadAllText(path);
            int causeIndex = text.IndexOf("=== CAUSE ===", StringComparison.Ordinal);
            int resolutionIndex = text.IndexOf("=== RESOLUTION ===", StringComparison.Ordinal);
            string cause = causeIndex >= 0 && resolutionIndex > causeIndex
                ? text[(causeIndex + 13)..resolutionIndex].Trim()
                : "";
            string resolution = resolutionIndex >= 0
                ? text[(resolutionIndex + 18)..].Trim()
                : "";
            return (cause, resolution);
        }
        catch
        {
            return ("", "");
        }
    }

    public static async Task<(string Cause, string Resolution)> FetchMicrosoftDocAsync(
        BugCheckInfo info, CancellationToken token)
    {
        var cached = ReadCachedDoc(info.Code);
        if (cached.Cause.Length > 0 || cached.Resolution.Length > 0)
        {
            return cached;
        }
        if (info.Url.Length == 0)
        {
            return ("", "");
        }
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) MiniMonitor/1.66.4");
        string html = await http.GetStringAsync(info.Url, token).ConfigureAwait(false);
        string cause = ExtractMicrosoftSection(html, "cause");
        string resolution = ExtractMicrosoftSection(html, "resolution");
        if (cause.Length == 0 && resolution.Length == 0)
        {
            // 部分页面使用 "Remarks" 或不同锚点，退回到正文文本。
            string remarks = ExtractMicrosoftSection(html, "remarks");
            if (remarks.Length > 0)
            {
                resolution = remarks;
            }
        }
        if (cause.Length > 0 || resolution.Length > 0)
        {
            try
            {
                string path = DocCachePath(info.Code);
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "");
                File.WriteAllText(path,
                    "=== CAUSE ===\n" + cause + "\n=== RESOLUTION ===\n" + resolution,
                    new UTF8Encoding(false));
            }
            catch
            {
            }
        }
        return (cause, resolution);
    }

    private static string ExtractMicrosoftSection(string html, string id)
    {
        try
        {
            var match = Regex.Match(html,
                "<h2[^>]*id=\"" + Regex.Escape(id) + "\"[^>]*>(.*?)</h2>(.*?)(?=<h2|</main>|</article>|$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success)
            {
                return "";
            }
            return CleanHtml(match.Groups[2].Value);
        }
        catch
        {
            return "";
        }
    }

    private static string CleanHtml(string html)
    {
        string text = Regex.Replace(html, "<script[^>]*>.*?</script>", " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, "<style[^>]*>.*?</style>", " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "</p>|</li>|</tr>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, "[\\t ]+", " ");
        text = Regex.Replace(text, "\\n\\s*\\n\\s*\\n+", "\n\n");
        return text.Trim();
    }
}
