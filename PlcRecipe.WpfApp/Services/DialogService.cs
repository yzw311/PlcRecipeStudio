using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;
using Microsoft.Win32;
using System.Windows;

namespace PlcRecipe.WpfApp.Services;

/// <summary>对话框服务（View 层实现，ViewModel 仅依赖接口）。</summary>
public interface IDialogService
{
    /// <summary>设备新增(null)/编辑对话框，返回 true 表示已保存。</summary>
    bool ShowDeviceEditor(PlcDevice? device);
    /// <summary>对比结果弹窗，返回 true 表示用户要求写入 LastCompareConfirmedRows。</summary>
    bool ShowCompare(Core.CompareResult result);
    /// <summary>最近一次对比弹窗中用户勾选的写入行。</summary>
    List<Core.CompareRow> LastCompareConfirmedRows { get; }
    /// <summary>配方版本历史弹窗，返回 true 表示用户选择回滚（结果在 SelectedRollback* 属性）。</summary>
    bool ShowVersionHistory(Core.Models.Recipe current, IReadOnlyList<Core.Models.RecipeVersionHistory> versions,
        Func<int, Task<List<Core.Models.RecipeItem>>> loadSnapshot);
    /// <summary>最近一次版本历史弹窗中用户选择的回滚版本号。</summary>
    int SelectedRollbackVersion { get; }
    /// <summary>最近一次版本历史弹窗中用户选择的回滚数据行。</summary>
    List<Core.Models.RecipeItem> SelectedRollbackRows { get; }
    /// <summary>信息提示。</summary>
    void Info(string message, string title = "提示");
    /// <summary>错误提示。</summary>
    void Error(string message, string title = "错误");
    /// <summary>确认框（是/否）。</summary>
    bool Confirm(string message, string title = "确认");
    /// <summary>单行文本输入，取消返回 null。</summary>
    string? PromptText(string caption, string label, string defaultValue = "");
    /// <summary>密码输入（掩码显示），取消返回 null。</summary>
    string? PromptPassword(string caption, string label);
    /// <summary>从候选项中选择一个，取消返回 null。</summary>
    string? PromptChoice(string caption, string label, IReadOnlyList<string> options, string defaultValue = "");
    /// <summary>选择 xlsx 文件（打开）。</summary>
    string? PickOpenExcel();
    /// <summary>选择 xlsx 保存路径。</summary>
    string? PickSaveExcel(string suggestedName);
}

public sealed class DialogService(IDeviceService deviceService, ISettingsService settings) : IDialogService
{
    /// <summary>当前激活窗口（模态框 Owner，避免弹窗被主窗遮挡后找不到）。</summary>
    private static Window? ActiveWindow() =>
        System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);

    public bool ShowDeviceEditor(PlcDevice? device)
    {
        var dlg = new DeviceEditWindow(deviceService, device, settings) { Owner = ActiveWindow() };
        return dlg.ShowDialog() == true;
    }

    public List<Core.CompareRow> LastCompareConfirmedRows { get; private set; } = new();

    public int SelectedRollbackVersion { get; private set; }

    public List<Core.Models.RecipeItem> SelectedRollbackRows { get; private set; } = new();

    public bool ShowCompare(Core.CompareResult result)
    {
        var dlg = new CompareDialog(result) { Owner = ActiveWindow() };
        var ok = dlg.ShowDialog() == true;
        LastCompareConfirmedRows = dlg.ConfirmedRows;
        return ok;
    }

    public bool ShowVersionHistory(Core.Models.Recipe current, IReadOnlyList<Core.Models.RecipeVersionHistory> versions,
        Func<int, Task<List<Core.Models.RecipeItem>>> loadSnapshot)
    {
        var dlg = new VersionHistoryDialog(current, versions, loadSnapshot) { Owner = ActiveWindow() };
        var ok = dlg.ShowDialog() == true;
        SelectedRollbackVersion = dlg.SelectedVersion;
        SelectedRollbackRows = dlg.SelectedRows;
        return ok;
    }

    public void Info(string message, string title = "提示") =>
        System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

    public void Error(string message, string title = "错误") =>
        System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);

    public bool Confirm(string message, string title = "确认") =>
        // 危险操作确认框：默认焦点落在「否」，防止连点/回车误确认
        System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question, System.Windows.MessageBoxResult.No)
        == System.Windows.MessageBoxResult.Yes;

    public string? PromptText(string caption, string label, string defaultValue = "") =>
        InputDialog.ShowText(caption, label, defaultValue);

    public string? PromptPassword(string caption, string label) =>
        InputDialog.ShowPassword(caption, label);

    public string? PromptChoice(string caption, string label, IReadOnlyList<string> options, string defaultValue = "") =>
        InputDialog.ShowChoice(caption, label, options, defaultValue);

    public string? PickOpenExcel()
    {
        var dlg = new OpenFileDialog { Filter = "Excel 文件 (*.xlsx)|*.xlsx", Title = "选择导入的 Excel 文件" };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? PickSaveExcel(string suggestedName)
    {
        var save = new SaveFileDialog
        {
            Filter = "Excel 文件 (*.xlsx)|*.xlsx",
            Title = "保存配方到 Excel",
            FileName = suggestedName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? suggestedName : suggestedName + ".xlsx"
        };
        return save.ShowDialog() == true ? save.FileName : null;
    }
}
