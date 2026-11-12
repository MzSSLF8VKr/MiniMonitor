using System.Windows.Forms;

namespace MiniMonitor;

/// <summary>
/// 全盘数据恢复窗口：Kickass Undelete / FreeRecover / TestDisk / PhotoRec /
/// WinfrGUI调用系统工具 五个选项卡。
/// 从托盘菜单“全盘数据恢复”或主菜单“磁盘分析管理”下方打开。
/// </summary>
public sealed class DataRecoveryForm : Form
{
    private readonly TabControl _tabs;
    private readonly Recovery.KickassUndeleteTab _kickassTab;
    private readonly Recovery.CarverTab _freeRecoverTab;
    private readonly Recovery.TestDiskTab _testDiskTab;
    private readonly Recovery.CarverTab _photoRecTab;
    private readonly Recovery.WinfrGuiTab _winfrTab;

    public DataRecoveryForm()
    {
        Text = "全盘数据恢复 - MiniMonitor";
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1100, 720);
        BackColor = Color.White;
        ShowInTaskbar = true;
        KeyPreview = true;

        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            Padding = new Point(20, 8)
        };
        var content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 10, 12, 12),
            BackColor = Color.White
        };
        content.Controls.Add(_tabs);
        Controls.Add(content);

        _kickassTab = new Recovery.KickassUndeleteTab();
        AddTab("Kickass Undelete复刻", _kickassTab);
        _freeRecoverTab = new Recovery.CarverTab("FreeRecover",
            "按文件签名恢复已删除文件", includePhysical: true,
            defaultAllCategories: true);
        AddTab("FreeRecover", _freeRecoverTab);
        _testDiskTab = new Recovery.TestDiskTab();
        AddTab("TestDisk", _testDiskTab);
        _photoRecTab = new Recovery.CarverTab("PhotoRec",
            "文件签名深度恢复（照片 / 视频 / 文档 / 压缩包）",
            includePhysical: true, defaultAllCategories: true);
        AddTab("PhotoRec", _photoRecTab);
        _winfrTab = new Recovery.WinfrGuiTab();
        AddTab("WinfrGUI调用系统工具", _winfrTab);

        _tabs.SelectedIndexChanged += (_, _) => UpdateTabActivation();
        FormClosing += (_, _) =>
        {
            _kickassTab.SetActive(false);
            _freeRecoverTab.SetActive(false);
            _testDiskTab.SetActive(false);
            _photoRecTab.SetActive(false);
            _winfrTab.SetActive(false);
        };
        UpdateTabActivation();
    }

    private void AddTab(string title, Control content)
    {
        var page = new TabPage(title)
        {
            BackColor = Color.White,
            Padding = new Padding(0)
        };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        _tabs.TabPages.Add(page);
    }

    private void UpdateTabActivation()
    {
        var selected = _tabs.SelectedTab;
        _kickassTab.SetActive(selected != null
            && ReferenceEquals(_kickassTab.Parent, selected));
        _freeRecoverTab.SetActive(selected != null
            && ReferenceEquals(_freeRecoverTab.Parent, selected));
        _testDiskTab.SetActive(selected != null
            && ReferenceEquals(_testDiskTab.Parent, selected));
        _photoRecTab.SetActive(selected != null
            && ReferenceEquals(_photoRecTab.Parent, selected));
        _winfrTab.SetActive(selected != null
            && ReferenceEquals(_winfrTab.Parent, selected));
    }
}
