using System.Windows;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;

namespace PlcRecipe.WpfApp.Services;

/// <summary>
/// 设备新增/编辑对话框（View 组件）：字段回填、品牌切换显隐、信号联动展开、字段收集
/// 均已迁至 DeviceEditViewModel（XAML 绑定）。code-behind 仅保留：
/// DataContext 装配、首字段焦点、保存编排（校验 + 服务调用 + DialogResult）。
/// </summary>
public partial class DeviceEditWindow : Window
{
    private readonly IDeviceService _deviceService;
    private readonly DeviceEditViewModel _vm;
    // 保存进行中防重入（保存按钮未接 Command，无法依赖 AsyncRelayCommand 自动禁用）
    private bool _saving;

    public DeviceEditWindow(IDeviceService deviceService, PlcDevice? existing, ISettingsService settings)
    {
        InitializeComponent();
        _deviceService = deviceService;
        _vm = new DeviceEditViewModel(existing, settings);
        DataContext = _vm;
        Loaded += (_, _) => NameBox.Focus();
    }

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_saving) return;
        var device = _vm.BuildDevice();
        if (device.Name.Length == 0)
        {
            MessageBox.Show("设备名不能为空", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (device.Port is < 1 or > 65535)
        {
            MessageBox.Show("端口必须是 1~65535 的整数", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _saving = true;
        try
        {
            if (_vm.IsExisting)
                await _deviceService.UpdateDeviceAsync(device);
            else
                await _deviceService.AddDeviceAsync(device);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _saving = false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
