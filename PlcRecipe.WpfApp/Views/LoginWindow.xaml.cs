using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using PlcRecipe.WpfApp.ViewModels;

namespace PlcRecipe.WpfApp;

/// <summary>
/// 登录窗口。code-behind 仅做三件事：密码框桥接（PasswordBox 不支持直接绑定）、
/// VM 清空密码时反向清空输入框、无边框窗口拖动。无业务逻辑。
/// </summary>
public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        PwdBox.PasswordChanged += (_, _) =>
        {
            if (DataContext is LoginViewModel vm)
                vm.Password = PwdBox.Password;
        };
        // VM 侧登录结束会清空 Password（安全要求），同步清空输入框，避免“看得见却提示未输入”的脱节
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is LoginViewModel old) old.PropertyChanged -= OnVmPropertyChanged;
            if (e.NewValue is LoginViewModel vm) vm.PropertyChanged += OnVmPropertyChanged;
        };
        // DataContext 永不再变，退订只能挂在窗口关闭上：VM 是 DI 单例，不退订会把整个已关闭的
        // 窗口（含视觉树）挂在 VM 的 PropertyChanged 委托上，注销/登录一轮泄漏一个窗口
        Closed += (_, _) =>
        {
            if (DataContext is LoginViewModel vm) vm.PropertyChanged -= OnVmPropertyChanged;
        };
        // 回车触发登录由 IsDefault=True 完成，无需手动 MoveFocus
        // 无边框窗口支持拖动（输入控件自身处理鼠标事件，不受影响）
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoginViewModel.Password)
            && sender is LoginViewModel { Password: "" })
        {
            PwdBox.Clear(); // 再次触发 PasswordChanged → vm.Password = ""（值相同，不会循环）
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
