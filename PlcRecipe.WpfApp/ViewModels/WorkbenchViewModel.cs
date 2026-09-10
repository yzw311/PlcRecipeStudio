using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;
using PlcRecipe.Drivers;
using PlcRecipe.WpfApp.Services;

namespace PlcRecipe.WpfApp.ViewModels;

/// <summary>配方数据行（定义 + 值一体，可编辑）。</summary>
public partial class RecipeRowViewModel : ObservableObject
{
    [ObservableProperty]
    private RecipeItem _item = null!;

    [ObservableProperty]
    private string? _addressError;

    [ObservableProperty]
    private string? _valueError;

    public RecipeRowViewModel(RecipeItem item)
    {
        _item = item;
        // RecipeItem.Name/Address/Value 实现了 INPC：编辑后校验结果即时刷新（不再是构造时的过期快照）。
        // 处理器必须可退订：Item 由配方列表长期持有，包装行只随 Rows 重建——不退订事件链会线性累积
        if (item is INotifyPropertyChanged npc)
            npc.PropertyChanged += OnItemPropertyChanged;
        Validate();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RecipeItem.Address) or nameof(RecipeItem.Value))
            Validate();
    }

    /// <summary>退订对 Item 的监听（行离开 Rows 集合时调用，防止已弃行经由事件链存活并参与校验）。</summary>
    public void Detach()
    {
        if (Item is INotifyPropertyChanged npc)
            npc.PropertyChanged -= OnItemPropertyChanged;
    }

    public void Validate()
    {
        var brand = Item.Recipe?.Device?.Brand;
        if (brand.HasValue && !PlcAddressParser.TryParse(brand.Value, Item.Address, out _, out var error))
            AddressError = error;
        else
            AddressError = null;

        if (Item.Value.Trim().Length > 0)
            ValueError = ValueCodec.TryValidate(Item, Item.Value, out var vError) ? null : vError;
        else
            ValueError = null; // 空值按默认 0 处理，编辑中不报错
    }
}

/// <summary>
/// 工作台（唯一主页面）：设备 → 配方 → 数据行 → 上传/下载/对比，一屏完成。
/// </summary>
public partial class WorkbenchViewModel(
    IDeviceService devices,
    IRecipeService recipes,
    ITransferService transfers,
    ICompareService compare,
    IExcelService excel,
    IPlcConnectionManager connections,
    ISettingsService settings,
    IOpLogService opLogs,
    ICurrentUserService currentUser,
    IDialogService dialogs,
    TransferRunner runner) : ObservableObject, IActivatablePage
{
    private const int DefaultStringWords = 8;

    // ---- 设备 ----
    public ObservableCollection<DeviceCardViewModel> DeviceList { get; } = new();

    [ObservableProperty]
    private DeviceCardViewModel? _selectedDeviceCard;

    // ---- 配方 ----
    public ObservableCollection<Recipe> RecipeList { get; } = new();

    [ObservableProperty]
    private Recipe? _selectedRecipe;

    // ---- 数据行 ----
    public ObservableCollection<RecipeRowViewModel> Rows { get; } = new();

    [ObservableProperty]
    private RecipeRowViewModel? _selectedRow;

    // ---- 批量目标（同品牌其他设备，下载/上传共用） ----
    public ObservableCollection<DeviceSelectionViewModel> Targets { get; } = new();

    // ---- 执行状态（集合由 TransferRunner 持有，实例终生不变，绑定路径不变） ----
    public ObservableCollection<TransferRowViewModel> TransferRows => runner.TransferRows;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _canEdit;

    // true = 正在程序化重建选中项（RefreshAsync 链），抑制事件侧的级联加载，防止同一数据加载两遍
    private bool _suppressCascade;

    // 数据行“已保存基线”（name → "address|value"），用于检测网格中的未保存修改
    private Dictionary<string, string>? _savedSnapshot;

    private PlcDevice? SelectedDevice => SelectedDeviceCard?.Device;

    // StateChanged 从后台线程广播，需回 UI 线程更新卡片；首次激活时订阅一次（VM 与管理器均为单例，无泄漏）
    private bool _stateHooked;

    public Task LoadAsync()
    {
        if (!_stateHooked)
        {
            _stateHooked = true;
            connections.StateChanged += (_, e) =>
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null) return;
                _ = dispatcher.BeginInvoke(() =>
                {
                    var card = DeviceList.FirstOrDefault(c => c.Device.Id == e.DeviceId);
                    if (card != null) card.State = e.State;
                });
            };
        }
        return RefreshAsync();
    }

    // ============ 加载 ============
    public async Task RefreshAsync()
    {
        try
        {
            CanEdit = currentUser.Current is { Role: >= UserRole.Engineer };
            var list = await devices.GetDevicesAsync(false).ConfigureAwait(true);
            DeviceList.Clear();
            foreach (var d in list)
                DeviceList.Add(new DeviceCardViewModel(d, connections.GetState(d.Id)));
            _suppressCascade = true;
            try
            {
                if (SelectedDeviceCard != null)
                    SelectedDeviceCard = DeviceList.FirstOrDefault(c => c.Device.Id == SelectedDeviceCard.Device.Id);
                SelectedDeviceCard ??= DeviceList.FirstOrDefault();
                await LoadRecipesAsync().ConfigureAwait(true);
            }
            finally
            {
                _suppressCascade = false;
            }
        }
        catch (Exception ex)
        {
            // RefreshAsync 被连接/断开/设备编辑等多条命令路径复用，异常必须就地消化，
            // 否则会沿 async 命令重抛到线程池直接终止进程
            Serilog.Log.Error(ex, "工作台刷新失败");
            dialogs.Error("刷新失败：" + ex.Message);
        }
    }

    partial void OnSelectedDeviceCardChanged(DeviceCardViewModel? value)
    {
        if (!_suppressCascade) _ = LoadRecipesSafeAsync();
    }

    private async Task LoadRecipesAsync()
    {
        RecipeList.Clear();
        Rows.Clear();
        if (SelectedDevice == null) return;
        foreach (var r in await recipes.GetRecipesAsync(SelectedDevice.Id).ConfigureAwait(true))
            RecipeList.Add(r);
        _suppressCascade = true;
        try
        {
            if (SelectedRecipe != null)
                SelectedRecipe = RecipeList.FirstOrDefault(r => r.Id == SelectedRecipe.Id);
            SelectedRecipe ??= RecipeList.FirstOrDefault();
            LoadRows();
        }
        finally
        {
            _suppressCascade = false;
        }
    }

    partial void OnSelectedRecipeChanged(Recipe? value)
    {
        if (!_suppressCascade) LoadRows();
    }

    private async Task LoadRecipesSafeAsync()
    {
        try
        {
            await LoadRecipesAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "加载配方列表失败");
            dialogs.Error("加载配方列表失败：" + ex.Message);
        }
    }

    private void LoadRows()
    {
        foreach (var r in Rows) r.Detach(); // 旧包装行退订后再清空（Item 实例在配方列表里长期存活）
        Rows.Clear();
        if (SelectedRecipe == null)
        {
            LoadTargets();
            return;
        }
        foreach (var i in SelectedRecipe.Items)
        {
            i.Recipe = SelectedRecipe;
            Rows.Add(new RecipeRowViewModel(i));
        }
        LoadTargets();
        CaptureSnapshot();
    }

    private void LoadTargets()
    {
        Targets.Clear();
        if (SelectedDevice == null) return;
        // 同品牌其他设备：下载目标 / 上传来源共用
        foreach (var d in DeviceList.Where(c => c.Device.Id != SelectedDevice.Id && c.Device.Brand == SelectedDevice.Brand))
            Targets.Add(new DeviceSelectionViewModel(d.Device));
    }

    // ---- 未保存修改检测：下载/对比/导出使用数据库版本，需提示用户 ----
    private void CaptureSnapshot() =>
        _savedSnapshot = Rows.ToDictionary(
            r => r.Item.Name,
            r => $"{r.Item.Address}|{r.Item.Value}",
            StringComparer.OrdinalIgnoreCase);

    private bool HasUnsavedEdits()
    {
        if (_savedSnapshot == null) return false;
        if (_savedSnapshot.Count != Rows.Count) return true;
        return Rows.Any(r =>
            !_savedSnapshot!.TryGetValue(r.Item.Name, out var s)
            || s != $"{r.Item.Address}|{r.Item.Value}");
    }

    // ============ 设备 ============
    [RelayCommand]
    private async Task ConnectAsync(DeviceCardViewModel? card)
    {
        if (card == null) return;
        try
        {
            await connections.ConnectAsync(card.Device).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // 连接失败时连接管理器会广播 Faulted 状态（卡片实时刷新）；RefreshAsync 自带防护，这里兜住连接本身
            Serilog.Log.Warning(ex, "手动连接设备 {Device} 失败", card.Device.Name);
            dialogs.Error("连接失败：" + ex.Message);
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync(DeviceCardViewModel? card)
    {
        if (card == null) return;
        try
        {
            await connections.DisconnectAsync(card.Device.Id).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { dialogs.Error("断开失败：" + ex.Message); }
    }

    [RelayCommand]
    private async Task NewDeviceAsync()
    {
        if (currentUser.Current is not { Role: >= UserRole.Engineer })
        { dialogs.Info("新增设备需要工程师及以上权限"); return; }
        try
        {
            if (dialogs.ShowDeviceEditor(null))
            {
                await RefreshAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex) { dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task EditDeviceAsync(DeviceCardViewModel? card)
    {
        var device = card?.Device ?? SelectedDevice;
        if (device == null) { dialogs.Info("请先选择设备"); return; }
        if (!CanEdit) { dialogs.Info("编辑设备需要工程师及以上权限"); return; }
        try
        {
            if (dialogs.ShowDeviceEditor(device))
            {
                connections.UpdateDevice(device);
                await RefreshAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex) { dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task DeleteDeviceAsync(DeviceCardViewModel? card)
    {
        if (!CanEdit) { dialogs.Info("删除设备需要工程师及以上权限"); return; }
        var device = card?.Device ?? SelectedDevice;
        if (device == null) { dialogs.Info("请先选择设备"); return; }
        if (!dialogs.Confirm($"确定删除设备“{device.Name}”？\n其配方文件将保留在配方库（Pfdoc）中，仍可按配方名被下载；如需删除配方请先在配方列表操作。", "删除确认")) return;
        try
        {
            await connections.DisconnectAsync(device.Id).ConfigureAwait(true);
            await devices.DeleteDeviceAsync(device.Id).ConfigureAwait(true);
            SelectedDeviceCard = null;
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { dialogs.Error(ex.Message); }
    }

    // ============ 配方 ============
    [RelayCommand]
    private async Task NewRecipeAsync()
    {
        if (SelectedDevice == null) { dialogs.Info("请先添加并选择设备"); return; }
        var name = dialogs.PromptText("新建配方", $"为“{SelectedDevice.Name}”新建配方，配方名：");
        if (name == null) return;
        try
        {
            var r = await recipes.CreateRecipeAsync(SelectedDevice.Id, name, null, currentUser.Current?.UserName).ConfigureAwait(true);
            await LoadRecipesAsync().ConfigureAwait(true);
            SelectedRecipe = RecipeList.FirstOrDefault(x => x.Id == r.Id);
        }
        catch (Exception ex) { dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task CopyRecipeAsync(Recipe? recipe)
    {
        recipe ??= SelectedRecipe;
        if (recipe == null) return;
        var name = dialogs.PromptText("复制配方", $"“{recipe.Name}”的新名称：", recipe.Name + "_副本");
        if (name == null) return;
        try
        {
            await recipes.CopyRecipeAsync(recipe.Id, name, currentUser.Current?.UserName).ConfigureAwait(true);
            await LoadRecipesAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task RenameRecipeAsync(Recipe? recipe)
    {
        recipe ??= SelectedRecipe;
        if (recipe == null) return;
        var name = dialogs.PromptText("重命名配方", "新名称：", recipe.Name);
        if (name == null || name == recipe.Name) return;
        try
        {
            await recipes.RenameRecipeAsync(recipe.Id, name, currentUser.Current?.UserName).ConfigureAwait(true);
            await LoadRecipesAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task DeleteRecipeAsync(Recipe? recipe)
    {
        recipe ??= SelectedRecipe;
        if (recipe == null) return;
        if (!dialogs.Confirm($"确定删除配方“{recipe.Name}”？", "删除确认")) return;
        try
        {
            await recipes.DeleteRecipeAsync(recipe.Id).ConfigureAwait(true);
            await LoadRecipesAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task VersionHistoryAsync(Recipe? recipe)
    {
        recipe ??= SelectedRecipe;
        if (recipe == null) { dialogs.Info("请先选择配方"); return; }
        try
        {
            var history = await recipes.GetVersionHistoryAsync(recipe.Id).ConfigureAwait(true);
            if (history.Count == 0) { dialogs.Info("该配方暂无历史版本（保存或上传一次后自动生成）"); return; }
            var current = await recipes.GetRecipeAsync(recipe.Id).ConfigureAwait(true);

            if (!dialogs.ShowVersionHistory(current, history, v => recipes.GetVersionSnapshotAsync(recipe.Id, v)))
                return;
            if (dialogs.SelectedRollbackRows.Count == 0) return;

            if (!dialogs.Confirm($"回滚将保存为新版本（当前未保存的修改会丢弃），是否继续？", "回滚确认"))
                return;
            IsBusy = true;
            try
            {
                var note = $"回滚自 v{dialogs.SelectedRollbackVersion}";
                await recipes.SaveRecipeAsync(recipe.Id, dialogs.SelectedRollbackRows,
                    currentUser.Current?.UserName, note).ConfigureAwait(true);
                await LoadRecipesAsync().ConfigureAwait(true);
                dialogs.Info($"已回滚到 v{dialogs.SelectedRollbackVersion}（当前保存为 v{current.Version + 1}）。");
            }
            finally { IsBusy = false; }
        }
        catch (Exception ex) { dialogs.Error(ex.Message); }
    }

    // ============ 数据行 ============
    [RelayCommand]
    private void AddRow()
    {
        if (SelectedRecipe == null) { dialogs.Info("请先选择或新建配方"); return; }
        var brand = SelectedDevice?.Brand ?? PlcBrand.Mock;
        var item = new RecipeItem
        {
            RecipeId = SelectedRecipe.Id,
            Recipe = SelectedRecipe,
            Name = $"变量{Rows.Count + 1}",
            Address = brand switch
            {
                PlcBrand.Siemens => "DB1.DBW0",
                PlcBrand.ModbusTcp or PlcBrand.ModbusRtu => "HR40001",
                _ => "D100"
            },
            DataType = PlcDataType.Float32,
            StringWords = DefaultStringWords,
            Access = VariableAccess.ReadWrite,
            SortOrder = Rows.Count,
            Value = "0"
        };
        Rows.Add(new RecipeRowViewModel(item));
    }

    [RelayCommand]
    private void AddRowsRange()
    {
        if (SelectedRecipe == null) { dialogs.Info("请先选择或新建配方"); return; }
        if (SelectedDevice == null) { dialogs.Info("请先选择设备"); return; }

        var startAddr = dialogs.PromptText("批量添加数据行", "起始地址（Modbus: D100 / 100 / x=3;100；西门子: DB1.DBW0；三菱: D100）：", Rows.Count > 0 ? Rows.Last().Item.Address : "D100");
        if (startAddr == null) return;
        var countText = dialogs.PromptText("批量添加数据行", "要添加的行数（1~500）：", "10");
        if (countText == null) return;
        if (!int.TryParse(countText, out var count) || count is < 1 or > 500)
        {
            dialogs.Error("数量必须是 1~500 的整数");
            return;
        }
        try
        {
            var brand = SelectedDevice.Brand;
            var parsed = PlcAddressParser.Parse(brand, startAddr);

            // 位地址 → 自动 Bool；字地址 → 让用户选类型（按选项索引映射，避免文案改动破坏映射）
            PlcDataType type;
            if (parsed.IsBitDevice)
            {
                type = PlcDataType.Bool;
            }
            else
            {
                string[] typeOptions =
                    ["Float32（32位实数）", "UInt16（16位无符号）", "Int16（16位有符号）", "UInt32", "Int32", "String字符串"];
                PlcDataType[] typeMap =
                    [PlcDataType.Float32, PlcDataType.UInt16, PlcDataType.Int16, PlcDataType.UInt32, PlcDataType.Int32, PlcDataType.String];
                var typeChoice = dialogs.PromptChoice("批量添加数据行", "数据类型：", typeOptions);
                if (typeChoice == null) return;
                var idx = Array.IndexOf(typeOptions, typeChoice);
                type = idx >= 0 ? typeMap[idx] : PlcDataType.Float32;
            }

            var prefix = dialogs.PromptText("批量添加数据行", "变量名前缀（如 温度 / 压力）：", "参数");
            if (prefix == null) return;
            var existing = Rows.Select(r => r.Item.Name).ToList();
            var rows = PlcRecipe.Drivers.RecipeRowExpander.Expand(brand, startAddr, count,
                type, DefaultStringWords, prefix, existing);
            var baseIndex = Rows.Count;
            foreach (var row in rows)
            {
                row.RecipeId = SelectedRecipe.Id;
                row.Recipe = SelectedRecipe;
                row.SortOrder = baseIndex++; // 顺序编号（原写法在循环内叠加 Rows.Count 会产生 2 倍间隔）
                Rows.Add(new RecipeRowViewModel(row));
            }
            dialogs.Info($"已添加 {rows.Count} 行（{rows[0].Address} 起，连续地址自动递增）。\n别忘了点「保存配方」持久化。");
        }
        catch (Exception ex) { dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private void DeleteRow()
    {
        if (SelectedRow == null) { dialogs.Info("请先选中要删除的数据行"); return; }
        SelectedRow.Detach();
        Rows.Remove(SelectedRow);
    }

    [RelayCommand]
    private async Task SaveRowsAsync()
    {
        if (SelectedRecipe == null) return;
        // 编辑过程中校验列已实时刷新，保存前再整体重算一次兜底（如从外部变更进来的行）
        foreach (var row in Rows) row.Validate();
        var bad = Rows.FirstOrDefault(r => r.AddressError != null);
        if (bad != null) { dialogs.Error($"“{bad.Item.Name}”{bad.AddressError}"); return; }
        bad = Rows.FirstOrDefault(r => r.ValueError != null);
        if (bad != null) { dialogs.Error($"“{bad.Item.Name}”的值无效：{bad.ValueError}"); return; }

        IsBusy = true;
        try
        {
            for (int i = 0; i < Rows.Count; i++)
                Rows[i].Item.SortOrder = i;
            await recipes.SaveRecipeAsync(SelectedRecipe.Id, Rows.Select(r => r.Item).ToList(),
                currentUser.Current?.UserName).ConfigureAwait(true);
            await LoadRecipesAsync().ConfigureAwait(true); // 重建行集合并刷新“已保存基线”
            dialogs.Info("配方已保存。");
        }
        catch (Exception ex) { dialogs.Error(ex.Message); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ExportAsync(Recipe? recipe)
    {
        recipe ??= SelectedRecipe;
        if (recipe == null) { dialogs.Info("请先选择配方"); return; }
        if (HasUnsavedEdits() &&
            !dialogs.Confirm("网格中的修改尚未保存，导出将使用数据库中已保存的配方值。\n是否继续？", "提示"))
            return;
        var path = dialogs.PickSaveExcel(recipe.Name);
        if (path == null) return;
        try
        {
            await excel.ExportRecipeAsync(recipe, path).ConfigureAwait(true);
            dialogs.Info($"已导出到：{path}");
        }
        catch (Exception ex) { dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task ImportAsync(Recipe? recipe)
    {
        recipe ??= SelectedRecipe;
        if (recipe == null) { dialogs.Info("请先选择配方"); return; }
        var path = dialogs.PickOpenExcel();
        if (path == null) return;
        try
        {
            var (matched, unmatched, invalid) = await excel.ImportRecipeAsync(recipe.Id, path, currentUser.Current?.UserName).ConfigureAwait(true);
            var msg = $"导入完成：匹配 {matched} 个变量";
            if (unmatched.Count > 0)
                msg += $"\n未匹配：{string.Join("、", unmatched.Take(10))}{(unmatched.Count > 10 ? "..." : "")}";
            if (invalid.Count > 0)
                msg += $"\n已跳过 {invalid.Count} 个非法值：{string.Join("；", invalid.Take(5))}{(invalid.Count > 5 ? "..." : "")}";
            dialogs.Info(msg);
            await LoadRecipesAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { dialogs.Error(ex.Message); }
    }

    // ============ 上传 / 下载 / 对比 ============
    private List<PlcDevice> CheckedTargets() => Targets.Where(t => t.IsSelected).Select(t => t.Device).ToList();

    [RelayCommand]
    private async Task DownloadAsync()
    {
        var targets = CheckedTargets();
        if (SelectedDevice == null || SelectedRecipe == null) { dialogs.Info("请先选择设备和配方"); return; }
        if (targets.Count == 0) { dialogs.Info("请在「批量目标」中勾选要下发的设备"); return; }
        if (IsBusy) { dialogs.Info("已有任务在执行，请等待完成"); return; }
        if (HasUnsavedEdits() &&
            !dialogs.Confirm("网格中的修改尚未保存，下载将使用数据库中已保存的配方值。\n是否继续？", "提示"))
            return;

        // 先置位再 await：检查-后-置位之间跨 await 会让上传/下载并发跑（共享 TransferRows）
        IsBusy = true;
        try
        {
            // 首个 await 前先快照 id：批量执行期间用户切换配方不应影响本次下载源
            var recipeId = SelectedRecipe.Id;
            var sourceRecipe = await recipes.GetRecipeAsync(recipeId).ConfigureAwait(true);
            if (settings.Settings.ForceCompareOnDownload)
            {
                var diffs = new List<string>();
                foreach (var device in targets)
                {
                    var cr = await compare.CompareAsync(device, sourceRecipe).ConfigureAwait(true);
                    if (!cr.Success) { diffs.Add($"{device.Name}：读取失败 {cr.Message}"); continue; }
                    if (cr.DiffCount > 0) diffs.Add($"{device.Name}：{cr.DiffCount} 项不同");
                }
                if (diffs.Count > 0 &&
                    !dialogs.Confirm("下载前对比发现差异：\n" + string.Join("\n", diffs) + "\n\n仍要继续下载覆盖 PLC？", "下载确认"))
                    return;
            }

            await runner.RunBatchAsync(targets, TransferDirection.Download, async (device, progress, ct) =>
            {
                var r = await transfers.DownloadAsync(device, sourceRecipe, progress,
                    settings.Settings.VerifyAfterWrite, ct).ConfigureAwait(false);
                await runner.LogTransferAsync(device, r, sourceRecipe.Name).ConfigureAwait(false);
                return r;
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // 触库路径（SQLite 锁/MySQL 断连）必须就地消化：命令方法未捕获的异常会经 async void 重抛终止进程
            Serilog.Log.Error(ex, "批量下载失败");
            dialogs.Error("下载失败：" + ex.Message);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task UploadAsync()
    {
        var targets = CheckedTargets();
        if (targets.Count == 0) { dialogs.Info("请在「批量目标」中勾选要读取的设备"); return; }
        if (IsBusy) { dialogs.Info("已有任务在执行，请等待完成"); return; }

        IsBusy = true;
        try
        {
            // 开跑前快照模板配方：执行期间用户切换设备/配方，lambda 里的实时读取会让读值落到错误配方、
            // 甚至在判空之后跨 await 解引用出 NRE
            var templateRecipeId = SelectedRecipe?.Id;
            var templateRecipeName = SelectedRecipe?.Name;
            await runner.RunBatchAsync(targets, TransferDirection.Upload, async (device, progress, ct) =>
            {
                // 读回后存到各自设备的同名配方（同名不存在且选择了模板配方时，按模板自动创建）
                var deviceRecipes = await recipes.GetRecipesAsync(device.Id, ct).ConfigureAwait(false);
                var own = templateRecipeName != null
                    ? deviceRecipes.FirstOrDefault(r => r.Name == templateRecipeName)
                    : deviceRecipes.FirstOrDefault();

                DeviceTransferResult r;
                if (own != null)
                {
                    r = await transfers.UploadAsync(device, own, progress, ct).ConfigureAwait(false);
                    if (r.Status == TransferStatus.Success && r.ReadValues != null)
                    {
                        await recipes.ApplyReadValuesAsync(own.Id, r.ReadValues, currentUser.Current?.UserName, ct).ConfigureAwait(false);
                        r.Message = $"已保存到配方“{own.Name}”";
                        // 参数容差：上传值越出上下限 → 状态行提示 + 记告警日志（不阻断流程）
                        var violations = RecipeLimits.Check(own.Items, r.ReadValues);
                        if (violations.Count > 0)
                        {
                            r.Message += $"；⚠ 参数越界 {violations.Count} 项";
                            await opLogs.AddAsync("参数越界", device.Name, string.Join("；", violations),
                                true, TransferSource.Manual, 0).ConfigureAwait(false);
                        }
                    }
                }
                else if (templateRecipeId == null)
                {
                    // 目标设备无任何配方且未选择模板：无法确定上传的数据行结构
                    r = new DeviceTransferResult
                    {
                        DeviceId = device.Id,
                        DeviceName = device.Name,
                        Direction = TransferDirection.Upload,
                        Status = TransferStatus.Failed,
                        Message = "目标设备没有任何配方，且未选择模板配方，无法确定上传的数据行结构"
                    };
                }
                else
                {
                    // 同名配方不存在：以上传源配方的地址/类型结构为模板新建（值来自该 PLC 的实际读数）
                    var template = await recipes.GetRecipeAsync(templateRecipeId.Value, ct).ConfigureAwait(false);
                    r = await transfers.UploadAsync(device, template, progress, ct).ConfigureAwait(false);
                    if (r.Status == TransferStatus.Success && r.ReadValues != null)
                    {
                        var created = await recipes.CreateRecipeAsync(device.Id, templateRecipeName!, "上传自 PLC", currentUser.Current?.UserName, ct).ConfigureAwait(false);
                        await recipes.SaveRecipeFromAsync(created.Id, template.Id, r.ReadValues, currentUser.Current?.UserName, ct).ConfigureAwait(false);
                        r.Message = $"已保存到新配方“{templateRecipeName}”（地址结构复制自“{template.Name}”）";
                    }
                }
                await runner.LogTransferAsync(device, r, templateRecipeName ?? "").ConfigureAwait(false);
                return r;
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "批量上传失败");
            dialogs.Error("上传失败：" + ex.Message);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task CompareAsync()
    {
        if (SelectedDevice == null || SelectedRecipe == null) { dialogs.Info("请先选择设备和配方"); return; }
        if (IsBusy) { dialogs.Info("已有任务在执行，请等待完成"); return; }
        if (HasUnsavedEdits() &&
            !dialogs.Confirm("网格中的修改尚未保存，对比将使用数据库中已保存的配方值。\n是否继续？", "提示"))
            return;
        IsBusy = true;
        try
        {
            var recipe = await recipes.GetRecipeAsync(SelectedRecipe.Id).ConfigureAwait(true);
            var result = await compare.CompareAsync(SelectedDevice, recipe).ConfigureAwait(true);

            // 弹窗展示；用户点“写入勾选项”则执行子集下载并重新对比
            while (dialogs.ShowCompare(result))
            {
                var rows = dialogs.LastCompareConfirmedRows.Select(r => r.Item).ToList();
                var device = (await devices.GetDevicesAsync(false).ConfigureAwait(true))
                    .FirstOrDefault(d => d.Id == result.DeviceId);
                if (device == null) return;
                var write = await transfers.DownloadSubsetAsync(device, recipe, rows).ConfigureAwait(true);
                await opLogs.AddAsync("写入差异项", $"{device.Name} ← {recipe.Name}",
                    $"写入 {rows.Count} 个差异变量，结果：{write.Message}",
                    write.Status == TransferStatus.Success, TransferSource.Manual, write.DurationMs).ConfigureAwait(true);
                dialogs.Info(write.Status == TransferStatus.Success
                    ? $"差异项写入成功（{rows.Count} 个）"
                    : "写入失败：" + write.Message);
                result = await compare.CompareAsync(device, recipe).ConfigureAwait(true);
            }
        }
        catch (Exception ex) { dialogs.Error("对比失败：" + ex.Message); }
        finally { IsBusy = false; }
    }

    // ============ 批量执行 ============
    // 执行编排与传输日志已拆至 TransferRunner：VM 只负责选目标、定任务，"怎么跑"交给 runner（IsBusy 由命令统一管理）
}

/// <summary>批量目标勾选项。</summary>
public partial class DeviceSelectionViewModel : ObservableObject
{
    public PlcDevice Device { get; }

    [ObservableProperty]
    private bool _isSelected = true;

    public string Display => $"{Device.Name}（{Device.Ip}:{Device.Port}）";

    public DeviceSelectionViewModel(PlcDevice device) => Device = device;
}

/// <summary>设备卡片子 ViewModel（工作台左列）。状态由 StateChanged 事件实时刷新。</summary>
public partial class DeviceCardViewModel : ObservableObject
{
    [ObservableProperty]
    private PlcDevice _device = null!;

    [ObservableProperty]
    private ConnectionState _state;

    partial void OnStateChanged(ConnectionState value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(StateDot));
    }

    public string StateText => State switch
    {
        ConnectionState.Connected => "在线",
        ConnectionState.Connecting or ConnectionState.Reconnecting => "连接中…",
        ConnectionState.Faulted => "故障",
        _ => "离线"
    };

    public string StateDot => State switch
    {
        ConnectionState.Connected => "Brush.Success",
        ConnectionState.Faulted => "Brush.Danger",
        ConnectionState.Connecting or ConnectionState.Reconnecting => "Brush.Warning",
        _ => "Brush.Offline"
    };

    public DeviceCardViewModel(PlcDevice device, ConnectionState state)
    {
        Device = device;
        State = state;
    }
}
