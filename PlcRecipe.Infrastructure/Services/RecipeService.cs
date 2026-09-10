using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;
using PlcRecipe.Drivers;

namespace PlcRecipe.Infrastructure.Services;

/// <summary>
/// 配方 CRUD 与数据行维护——配方唯一存储为配方文件库（dataDir\Pfdoc\配方名.txt，txt 即数据库）。
/// 下载/信号联动按 PLC 报的配方名到文件库取数；每次保存自动留版本快照（可回滚）。
/// 设备/用户/操作日志仍存数据库。Recipe.Id = 配方名的稳定哈希。
/// </summary>
public class RecipeService(IRecipeFileStore fileStore, IDeviceService devices) : IRecipeService
{
    private static int StableId(string? deviceName, string name)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (var c in (deviceName ?? "") + "|" + name) { h ^= c; h *= 16777619; }
            return (int)h;
        }
    }


    public async Task<List<Recipe>> GetRecipesAsync(int deviceId, CancellationToken ct = default)
    {
        var device = (await devices.GetDevicesAsync(true, ct).ConfigureAwait(false))
            .FirstOrDefault(d => d.Id == deviceId);
        if (device == null) return [];

        var result = new List<Recipe>();
        foreach (var doc in fileStore.LoadAll().Where(d => string.Equals(d.Device, device.Name, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(new Recipe
            {
                Id = StableId(device?.Name ?? doc.Device, doc.Name),
                DeviceId = deviceId,
                Device = device,
                Name = doc.Name,
                Version = doc.Version,
                Items = doc.Items.ToList(),
                PlcNo = result.Count + 1
            });
        }
        return result;
    }

    public async Task<Recipe> GetRecipeAsync(int recipeId, CancellationToken ct = default)
    {
        var allDevices = await devices.GetDevicesAsync(true, ct).ConfigureAwait(false);
        foreach (var doc in fileStore.LoadAll())
        {
            if (StableId(doc.Device, doc.Name) != recipeId) continue;
            var device = allDevices.FirstOrDefault(d => d.Name == doc.Device);
            var recipe = new Recipe
            {
                Id = recipeId,
                DeviceId = device?.Id ?? 0,
                Device = device,
                Name = doc.Name,
                Version = doc.Version,
                Items = doc.Items.ToList()
            };
            foreach (var i in recipe.Items) i.Recipe = recipe;
            return recipe;
        }
        throw new InvalidOperationException("配方不存在");
    }

    public async Task<Recipe> CreateRecipeAsync(int deviceId, string name, string? remark, string? user, CancellationToken ct = default)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("配方名不能为空");
        var device = (await devices.GetDevicesAsync(true, ct).ConfigureAwait(false))
            .FirstOrDefault(d => d.Id == deviceId) ?? throw new InvalidOperationException("设备不存在");
        if (fileStore.Exists(device.Name, name))
            throw new InvalidOperationException($"配方“{name}”已存在");

        var recipe = new Recipe
        {
            Id = StableId(device.Name, name),
            DeviceId = deviceId,
            Device = device,
            Name = name,
            Version = 1,
            Remark = remark,
            CreatedBy = user,
            CreatedAt = DateTime.UtcNow,
            UpdatedBy = user,
            UpdatedAt = DateTime.UtcNow
        };
        await fileStore.SaveAsync(device.Name, name, 1, []).ConfigureAwait(false);
        await fileStore.SaveHistoryAsync(device.Name, name, 1, [], user, "新建").ConfigureAwait(false);
        return recipe;
    }

    public async Task<Recipe> CopyRecipeAsync(int recipeId, string newName, string? user, CancellationToken ct = default)
    {
        newName = newName.Trim();
        if (newName.Length == 0) throw new ArgumentException("配方名不能为空");
        var src = await GetRecipeAsync(recipeId, ct).ConfigureAwait(false);
        if (fileStore.Exists(src.Device?.Name ?? "未分配", newName))
            throw new InvalidOperationException($"配方“{newName}”已存在");

        await fileStore.SaveAsync(src.Device?.Name ?? "未分配", newName, 1, src.Items).ConfigureAwait(false);
        await fileStore.SaveHistoryAsync(src.Device?.Name ?? "未分配", newName, 1, src.Items, user, $"复制自「{src.Name}」").ConfigureAwait(false);
        return new Recipe
        {
            Id = StableId(src.Device?.Name ?? "未分配", newName),
            DeviceId = src.DeviceId,
            Device = src.Device,
            Name = newName,
            Version = 1,
            Remark = $"复制自「{src.Name}」",
            CreatedBy = user,
            CreatedAt = DateTime.UtcNow,
            UpdatedBy = user,
            UpdatedAt = DateTime.UtcNow,
            Items = src.Items.ToList()
        };
    }

    public async Task RenameRecipeAsync(int recipeId, string newName, string? user, CancellationToken ct = default)
    {
        newName = newName.Trim();
        var src = await GetRecipeAsync(recipeId, ct).ConfigureAwait(false);
        if (src.Name == newName) return;
        await fileStore.RenameAsync(src.Device?.Name ?? "未分配", src.Name, newName).ConfigureAwait(false);
    }

    public async Task DeleteRecipeAsync(int recipeId, CancellationToken ct = default)
    {
        var src = await GetRecipeAsync(recipeId, ct).ConfigureAwait(false);
        await fileStore.DeleteAsync(src.Device?.Name ?? "未分配", src.Name).ConfigureAwait(false);
        await fileStore.DeleteHistoryAsync(src.Device?.Name ?? "未分配", src.Name).ConfigureAwait(false);
    }

    public async Task SaveRecipeAsync(int recipeId, IReadOnlyList<RecipeItem> rows, string? user, string? changeNote = null, CancellationToken ct = default)
    {
        var existing = await GetRecipeAsync(recipeId, ct).ConfigureAwait(false);

        // 乐观并发：调用方数据行携带的 Recipe.Version 为本地基准（工作台保存路径必带），
        // 文件库版本已前进说明他人先保存过——拒绝覆盖，防止互相冲掉修改。
        var expectedVersion = rows.FirstOrDefault()?.Recipe?.Version;
        if (expectedVersion.HasValue && existing.Version != expectedVersion.Value)
            throw new InvalidOperationException($"配方已被其他用户修改（服务器 v{existing.Version}，本地基于 v{expectedVersion.Value}），请刷新后重做修改");

        // 全量校验：变量名唯一、地址语法（按设备品牌）、上下限顺序、值类型
        var brand = existing.Device?.Brand;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Name))
                throw new InvalidOperationException("存在变量名为空的数据行");
            if (ContainsFileSeparator(row.Name) || ContainsFileSeparator(row.Unit) || ContainsFileSeparator(row.Remark)
                || ContainsFileSeparator(row.Value))
                throw new InvalidOperationException($"“{row.Name.Trim()}”的名称/单位/备注/值不能包含竖线“|”或换行符（配方文件格式的保留字符）");
            if (!names.Add(row.Name.Trim()))
                throw new InvalidOperationException($"变量名“{row.Name}”重复");
            if (brand.HasValue)
                PlcAddressParser.Parse(brand.Value, row.Address?.Trim() ?? "");
            if (row.LowerLimit.HasValue && row.UpperLimit.HasValue && row.LowerLimit.Value > row.UpperLimit.Value)
                throw new InvalidOperationException($"“{row.Name.Trim()}”的下限不能大于上限");
            if (!ValueCodec.TryValidate(new RecipeItem
            {
                Name = row.Name.Trim(), Address = row.Address?.Trim() ?? "",
                DataType = row.DataType, StringWords = row.StringWords
            }, row.Value, out var valueError))
                throw new InvalidOperationException($"“{row.Name.Trim()}”{valueError}");
        }

        var items = rows.Select((r, idx) => new RecipeItem
        {
            Name = r.Name.Trim(),
            Address = r.Address?.Trim() ?? "",
            DataType = r.DataType,
            StringWords = r.StringWords <= 0 ? 8 : r.StringWords,
            Unit = r.Unit,
            Access = r.Access,
            SortOrder = idx,
            Remark = r.Remark,
            Value = r.Value ?? "",
            LowerLimit = r.LowerLimit,
            UpperLimit = r.UpperLimit
        }).ToList();

        var newVersion = existing.Version + 1;
        await fileStore.CommitAsync(existing.Device?.Name ?? "未分配", existing.Name, existing.Version, newVersion, items, user,
            string.IsNullOrWhiteSpace(changeNote) ? "保存" : changeNote.Trim()).ConfigureAwait(false);
    }

    /// <summary>配方文件（txt）以竖线/换行作分隔符；名称/单位/备注等自由文本进入文件前必须过滤。</summary>
    private static bool ContainsFileSeparator(string? text) =>
        !string.IsNullOrEmpty(text) && text.IndexOfAny(ValueCodec.FileSeparatorChars) >= 0;

    /// <summary>PLC 读回的字符串值可能天然包含保留字符（如工艺文本），落库前等价替换，保证文件可往返解析。</summary>
    private static string SanitizeFileValue(string value) => value;

    public async Task ApplyReadValuesAsync(int recipeId, IReadOnlyDictionary<string, string> valuesByName, string? user, CancellationToken ct = default)
    {
        var existing = await GetRecipeAsync(recipeId, ct).ConfigureAwait(false);
        foreach (var item in existing.Items)
        {
            if (valuesByName.TryGetValue(item.Name, out var value))
                item.Value = SanitizeFileValue(value);
        }
        var newVersion = existing.Version + 1;
        await fileStore.SaveAsync(existing.Device?.Name ?? "未分配", existing.Name, newVersion, existing.Items).ConfigureAwait(false);
        await fileStore.SaveHistoryAsync(existing.Device?.Name ?? "未分配", existing.Name, newVersion, existing.Items, user, "上传落库").ConfigureAwait(false);
    }

    public Task ApplyReadValuesByNameAsync(string deviceName, string recipeName, IReadOnlyDictionary<string, string> valuesByName, string? user, CancellationToken ct = default) =>
        ApplyReadValuesAsync(StableId(deviceName, recipeName), valuesByName, user, ct);

    public async Task SaveRecipeFromAsync(int targetRecipeId, int templateRecipeId, IReadOnlyDictionary<string, string> valuesByName, string? user, CancellationToken ct = default)
    {
        var template = await GetRecipeAsync(templateRecipeId, ct).ConfigureAwait(false);
        var rows = template.Items.Select(i => new RecipeItem
        {
            Name = i.Name,
            Address = i.Address,
            DataType = i.DataType,
            StringWords = i.StringWords,
            Unit = i.Unit,
            Access = i.Access,
            Remark = i.Remark,
            Value = valuesByName.TryGetValue(i.Name, out var v) ? v : i.Value,
            LowerLimit = i.LowerLimit,
            UpperLimit = i.UpperLimit
        }).ToList();
        await SaveRecipeAsync(targetRecipeId, rows, user, "上传落库（按模板建配方）", ct).ConfigureAwait(false);
    }

    public async Task<List<RecipeVersionHistory>> GetVersionHistoryAsync(int recipeId, CancellationToken ct = default)
    {
        var src = await GetRecipeAsync(recipeId, ct).ConfigureAwait(false);
        return fileStore.GetHistory(src.Device?.Name ?? "未分配", src.Name).ToList();
    }

    public async Task<List<RecipeItem>> GetVersionSnapshotAsync(int recipeId, int version, CancellationToken ct = default)
    {
        var src = await GetRecipeAsync(recipeId, ct).ConfigureAwait(false);
        var items = await fileStore.TryLoadHistoryAsync(src.Device?.Name ?? "未分配", src.Name, version).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"版本历史 v{version} 不存在");
        foreach (var i in items) i.RecipeId = recipeId;
        return items;
    }
}
