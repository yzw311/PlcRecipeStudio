using System.Windows;
using System.Windows.Controls;

namespace PlcRecipe.WpfApp.Services;

/// <summary>
/// 通用输入对话框（文本输入 / 密码输入 / 选项选择）。静态方法便于服务调用，
/// 属于纯 View 组件，不承载业务逻辑。回车=确定、Esc=取消。
/// </summary>
public partial class InputDialog : Window
{
    private enum InputMode { Text, Password, Choice }

    private readonly InputMode _mode;

    private InputDialog(string caption, string label, InputMode mode,
        string defaultValue = "", IReadOnlyList<string>? options = null)
    {
        InitializeComponent();
        Title = caption;
        CaptionText.Text = label;
        _mode = mode;
        switch (mode)
        {
            case InputMode.Choice when options != null:
                ChoiceBox.Visibility = Visibility.Visible;
                TextBox.Visibility = Visibility.Collapsed;
                ChoiceBox.ItemsSource = options;
                ChoiceBox.SelectedItem = options.Contains(defaultValue) ? defaultValue : options.FirstOrDefault();
                break;
            case InputMode.Password:
                PwdBox.Visibility = Visibility.Visible;
                TextBox.Visibility = Visibility.Collapsed;
                Loaded += (_, _) => PwdBox.Focus();
                break;
            default:
                TextBox.Text = defaultValue;
                Loaded += (_, _) => { TextBox.Focus(); TextBox.SelectAll(); };
                break;
        }
    }

    public string? Result { get; private set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = _mode switch
        {
            InputMode.Choice => ChoiceBox.SelectedItem as string,
            // 密码不做 Trim：密码可能包含首尾空格
            InputMode.Password => PwdBox.Password,
            _ => TextBox.Text.Trim()
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>当前激活窗口（模态框 Owner，避免弹窗被主窗遮挡）。</summary>
    private static Window? ActiveWindow() =>
        System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);

    public static string? ShowText(string caption, string label, string defaultValue = "")
    {
        var dlg = new InputDialog(caption, label, InputMode.Text, defaultValue) { Owner = ActiveWindow() };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }

    public static string? ShowPassword(string caption, string label)
    {
        var dlg = new InputDialog(caption, label, InputMode.Password) { Owner = ActiveWindow() };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }

    public static string? ShowChoice(string caption, string label, IReadOnlyList<string> options, string defaultValue = "")
    {
        var dlg = new InputDialog(caption, label, InputMode.Choice, defaultValue, options) { Owner = ActiveWindow() };
        return dlg.ShowDialog() == true ? dlg.Result : null;
    }
}
