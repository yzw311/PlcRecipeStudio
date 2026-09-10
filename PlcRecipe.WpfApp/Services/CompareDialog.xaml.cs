using System.Windows;
using PlcRecipe.Core;

namespace PlcRecipe.WpfApp.Services;

/// <summary>
/// 配方对比弹窗（View 组件）：展示差异、勾选要写入的行。
/// ShowResult 返回 true 表示用户要求写入，ConfirmedRows 即勾选的行。
/// </summary>
public partial class CompareDialog : Window
{
    public List<CompareRow> ConfirmedRows { get; private set; } = new();

    public CompareDialog(CompareResult result)
    {
        InitializeComponent();
        Grid.ItemsSource = result.Rows.Select(r => new CompareRowVM(r)).ToList();
        SummaryText.Text = result.Success
            ? $"{result.DeviceName} vs {result.RecipeName} @ {result.Time.ToLocalTime():HH:mm:ss} — 相同 {result.SameCount}，不同 {result.DiffCount}，读取失败 {result.FailCount}（红色行=不同，可勾选写回）"
            : $"对比失败：{result.Message}";
        WriteButton.Visibility = result.Success && result.DiffCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var vm in Grid.ItemsSource.Cast<CompareRowVM>())
            if (vm.Row.State == CompareState.Different && vm.Row.Item.Access == VariableAccess.ReadWrite)
                vm.Row.Selected = true;
        Grid.Items.Refresh();
    }

    private void Write_Click(object sender, RoutedEventArgs e)
    {
        ConfirmedRows = Grid.ItemsSource.Cast<CompareRowVM>()
            .Where(v => v.Row.Selected && v.Row.Item.Access == VariableAccess.ReadWrite)
            .Select(v => v.Row).ToList();
        if (ConfirmedRows.Count == 0)
        {
            MessageBox.Show("请先勾选要写入的行", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed class CompareRowVM
    {
        public CompareRow Row { get; }
        public string VarName => Row.Item.Name;
        public string Address => Row.Item.Address;
        public string Unit => Row.Item.Unit ?? "";
        public string PlcValue => Row.PlcValue ?? "（读取失败）";
        public string RecipeValue => Row.RecipeValue ?? "";
        public string StateText => Row.State switch
        {
            CompareState.Same => "相同",
            CompareState.Different => "不同",
            _ => "读取失败"
        };
        public CompareRowVM(CompareRow row) => Row = row;
    }
}
