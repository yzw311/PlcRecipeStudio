using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PlcRecipe.Core;
using PlcRecipe.Core.Models;

namespace PlcRecipe.WpfApp.Services;

/// <summary>
/// 配方版本历史弹窗（View 组件）：版本列表 + 与当前版本逐行差异 + 回滚。
/// 数据行快照通过 loadSnapshot 委托按需加载（列表本身不带正文）。
/// DialogResult=true 时 SelectedVersion/SelectedRows 即用户选择的回滚目标。
/// </summary>
public partial class VersionHistoryDialog : Window
{
    private readonly Recipe _current;
    private readonly Func<int, Task<List<RecipeItem>>> _loadSnapshot;
    private readonly ObservableCollection<VersionRow> _versions = new();
    private readonly ObservableCollection<DiffRow> _diffs = new();
    private List<RecipeItem>? _selectedSnapshot;
    private int _loadedVersion; // _selectedSnapshot 对应的版本号：回滚前核对，防止“显示 v1、套用 v2”错位
    private bool _loadingDiff;

    public int SelectedVersion { get; private set; }
    public List<RecipeItem> SelectedRows { get; private set; } = new();

    public VersionHistoryDialog(Recipe current, IReadOnlyList<RecipeVersionHistory> versions,
        Func<int, Task<List<RecipeItem>>> loadSnapshot)
    {
        InitializeComponent();
        _current = current;
        _loadSnapshot = loadSnapshot;
        HeaderText.Text = $"“{current.Name}”的版本历史（当前 v{current.Version}，共 {versions.Count} 个历史版本）";

        foreach (var v in versions)
            _versions.Add(new VersionRow(v));
        VersionsGrid.ItemsSource = _versions;
        DiffGrid.ItemsSource = _diffs;
        if (_versions.Count > 0)
            VersionsGrid.SelectedIndex = 0;
    }

    private sealed class VersionRow
    {
        public RecipeVersionHistory History { get; }
        public int Version => History.Version;
        public string SavedAtText => DateTime.SpecifyKind(History.SavedAtUtc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        public string SavedBy => History.SavedBy ?? "—";
        public string ChangeNote => History.ChangeNote ?? "—";
        public VersionRow(RecipeVersionHistory history) => History = history;
    }

    private sealed class DiffRow
    {
        public string State { get; init; } = "";
        public string Name { get; init; } = "";
        public string Address { get; init; } = "";
        public PlcDataType DataType { get; init; }
        public string HistoryValue { get; init; } = "";
        public string CurrentValue { get; init; } = "";
    }

    private async void Versions_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VersionsGrid.SelectedItem is not VersionRow row || _loadingDiff) return;
        _loadingDiff = true;
        // 加载期间禁用列表：静默忽略切换会让确认框写的版本与实际套用的快照错位
        VersionsGrid.IsEnabled = false;
        RollbackButton.Visibility = Visibility.Collapsed;
        DiffHeader.Text = "与当前版本的差异（加载中…）";
        try
        {
            _selectedSnapshot = await _loadSnapshot(row.History.Version).ConfigureAwait(true);
            _loadedVersion = row.History.Version;
            BuildDiff(row.History.Version, _selectedSnapshot);
            DiffHeader.Text = $"与当前版本的差异（v{row.History.Version} 共 {_selectedSnapshot.Count} 行；" +
                              "黄=值已修改，蓝=该版本新增，红=当前版本独有）";
            RollbackButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _selectedSnapshot = null; // 加载失败时清掉旧快照，禁止用错位数据回滚
            _loadedVersion = 0;
            MessageBox.Show("加载版本快照失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _loadingDiff = false;
            VersionsGrid.IsEnabled = true;
        }
    }

    private void BuildDiff(int historyVersion, List<RecipeItem> snapshot)
    {
        _diffs.Clear();
        // 手工编辑的 txt 可能存在重名行：ToDictionary 会直接抛错，取首行容错
        var currentByName = _current.Items
            .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var historyByName = snapshot
            .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var item in snapshot)
        {
            var state = "相同";
            string currentValue;
            if (currentByName.TryGetValue(item.Name, out var cur))
            {
                var same = cur.Address == item.Address && cur.DataType == item.DataType && cur.Value == item.Value;
                currentValue = cur.Value;
                if (!same) state = "已修改";
            }
            else
            {
                currentValue = "（当前版本无此行）";
                state = "该版本新增";
            }
            if (state != "相同" || currentByName.ContainsKey(item.Name))
                _diffs.Add(new DiffRow
                {
                    State = state,
                    Name = item.Name,
                    Address = item.Address,
                    DataType = item.DataType,
                    HistoryValue = item.Value,
                    CurrentValue = currentValue
                });
        }
        foreach (var cur in _current.Items)
        {
            if (!historyByName.ContainsKey(cur.Name))
                _diffs.Add(new DiffRow
                {
                    State = "当前版本独有",
                    Name = cur.Name,
                    Address = cur.Address,
                    DataType = cur.DataType,
                    HistoryValue = "（该版本无此行）",
                    CurrentValue = cur.Value
                });
        }
        if (_diffs.Count == 0)
            _diffs.Add(new DiffRow { State = "相同", Name = "（该版本与当前版本内容一致）", HistoryValue = "", CurrentValue = "" });
    }

    private async void Rollback_Click(object sender, RoutedEventArgs e)
    {
        if (VersionsGrid.SelectedItem is not VersionRow row)
        {
            MessageBox.Show("请先选择要回滚的版本", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_loadingDiff)
        {
            MessageBox.Show("差异加载中，请稍候再试", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_selectedSnapshot == null || _loadedVersion != row.Version)
        {
            // 选中行与已加载快照不一致（加载失败/竞态残留）：以选中行为准重新加载，绝不套用错位数据
            try
            {
                _selectedSnapshot = await _loadSnapshot(row.Version).ConfigureAwait(true);
                _loadedVersion = row.Version;
            }
            catch (Exception ex)
            {
                _selectedSnapshot = null;
                _loadedVersion = 0;
                MessageBox.Show("加载版本快照失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        var answer = MessageBox.Show(
            $"确定回滚到 v{row.Version}（{row.SavedAtText}）？\n回滚会保存为新版本，当前版本仍可在历史中找回。",
            "回滚确认", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        SelectedVersion = row.Version;
        SelectedRows = _selectedSnapshot.Select(i => new RecipeItem
        {
            Name = i.Name,
            Address = i.Address,
            DataType = i.DataType,
            StringWords = i.StringWords,
            Unit = i.Unit,
            Access = i.Access,
            SortOrder = i.SortOrder,
            Remark = i.Remark,
            Value = i.Value,
            RecipeId = _current.Id
        }).ToList();
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
