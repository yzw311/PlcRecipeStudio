using PlcRecipe.Core.Models;

namespace PlcRecipe.Core.Interfaces;

/// <summary>设备 CRUD（工程师以上可写）。</summary>
public interface IDeviceService
{
    Task<List<PlcDevice>> GetDevicesAsync(bool includeDisabled = true, CancellationToken ct = default);
    Task<PlcDevice> AddDeviceAsync(PlcDevice device, CancellationToken ct = default);
    Task UpdateDeviceAsync(PlcDevice device, CancellationToken ct = default);
    Task DeleteDeviceAsync(int deviceId, CancellationToken ct = default);
}

/// <summary>配方 CRUD 与数据行维护（配方挂在设备下）。</summary>
public interface IRecipeService
{
    /// <summary>按设备列出配方（含数据行）。</summary>
    Task<List<Recipe>> GetRecipesAsync(int deviceId, CancellationToken ct = default);
    Task<Recipe> GetRecipeAsync(int recipeId, CancellationToken ct = default);
    Task<Recipe> CreateRecipeAsync(int deviceId, string name, string? remark, string? user, CancellationToken ct = default);
    Task<Recipe> CopyRecipeAsync(int recipeId, string newName, string? user, CancellationToken ct = default);
    Task RenameRecipeAsync(int recipeId, string newName, string? user, CancellationToken ct = default);
    Task DeleteRecipeAsync(int recipeId, CancellationToken ct = default);
    /// <summary>整体保存配方（数据行定义 + 值，全量校验后替换写入，版本 +1，并写入版本历史快照）。
    /// 行携带 Recipe 导航时以其 Version 作为乐观并发基准：服务器版本不一致即拒绝（防止互相覆盖）。</summary>
    Task SaveRecipeAsync(int recipeId, IReadOnlyList<RecipeItem> rows, string? user, string? changeNote = null, CancellationToken ct = default);
    /// <summary>把读到的 PLC 值按变量名写入配方（上传落库，版本+1 并留历史）。</summary>
    Task ApplyReadValuesAsync(int recipeId, IReadOnlyDictionary<string, string> valuesByName, string? user, CancellationToken ct = default);
    /// <summary>按配方名把读到的 PLC 值写入配方（文件库为准；信号联动使用，无需数据库记录）。</summary>
    Task ApplyReadValuesByNameAsync(string deviceName, string recipeName, IReadOnlyDictionary<string, string> valuesByName, string? user, CancellationToken ct = default);
    /// <summary>以另一配方的数据行结构 + 当前读值创建新配方（信号触发上传另存时使用）。</summary>
    Task SaveRecipeFromAsync(int targetRecipeId, int templateRecipeId, IReadOnlyDictionary<string, string> valuesByName, string? user, CancellationToken ct = default);
    /// <summary>配方版本历史（按版本倒序；不含数据行正文）。</summary>
    Task<List<RecipeVersionHistory>> GetVersionHistoryAsync(int recipeId, CancellationToken ct = default);
    /// <summary>取某版本的历史数据行（用于查看/回滚）。</summary>
    Task<List<RecipeItem>> GetVersionSnapshotAsync(int recipeId, int version, CancellationToken ct = default);
}

/// <summary>配方上传/下载执行服务。</summary>
public interface ITransferService
{
    /// <summary>把配方数据行合并成读写计划（块合并优化）。地址语法按设备品牌解析。</summary>
    TransferPlan BuildPlan(PlcBrand brand, IReadOnlyList<RecipeItem> rows);

    /// <summary>下载：把配方值写入 PLC（整个计划单次锁内原子执行）。</summary>
    Task<DeviceTransferResult> DownloadAsync(PlcDevice device, Recipe recipe,
        IProgress<TransferProgress>? progress = null, bool verifyAfterWrite = false,
        CancellationToken ct = default);

    /// <summary>上传：按配方数据行读 PLC 当前值（返回 变量名→值）。</summary>
    Task<DeviceTransferResult> UploadAsync(PlcDevice device, Recipe recipe,
        IProgress<TransferProgress>? progress = null, CancellationToken ct = default);

    /// <summary>下载若干行子集（对比后只写差异项）。</summary>
    Task<DeviceTransferResult> DownloadSubsetAsync(PlcDevice device, Recipe recipe,
        IReadOnlyList<RecipeItem> rows, IProgress<TransferProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>按数据行读取 PLC 当前值（对比/上传共用，单次锁内原子快照）。</summary>
    Task<Dictionary<string, string>> ReadPlanValuesAsync(PlcDevice device, PlcBrand brand,
        IReadOnlyList<RecipeItem> rows, IProgress<TransferProgress>? progress, CancellationToken ct);
}

/// <summary>配方对比服务。</summary>
public interface ICompareService
{
    Task<CompareResult> CompareAsync(PlcDevice device, Recipe recipe, CancellationToken ct = default);
}

/// <summary>Excel 导入导出。</summary>
public interface IExcelService
{
    Task ExportRecipeAsync(Recipe recipe, string filePath, CancellationToken ct = default);
    /// <summary>按变量名匹配导入值，返回 匹配数/未匹配名列表/因非法值被跳过的行。</summary>
    Task<(int matched, List<string> unmatched, List<string> invalid)> ImportRecipeAsync(int recipeId, string filePath, string? user, CancellationToken ct = default);
}

/// <summary>用户认证与当前用户上下文。</summary>
public interface IUserService
{
    Task<User?> VerifyAsync(string userName, string password, CancellationToken ct = default);
    Task<List<User>> GetUsersAsync(CancellationToken ct = default);
    Task<User> AddUserAsync(string userName, string password, UserRole role, string? remark, CancellationToken ct = default);
    Task ChangePasswordAsync(int userId, string newPassword, CancellationToken ct = default);
    /// <summary>本人凭已验证的当前密码自助改密（首次登录强制改密 / 修改自己密码）。
    /// 与 ChangePasswordAsync（管理员改他人）的区别：不要求管理员身份，但必须验证当前密码。</summary>
    Task ChangeOwnPasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken ct = default);
    Task SetUserEnabledAsync(int userId, bool enabled, CancellationToken ct = default);
    Task SetUserRoleAsync(int userId, UserRole role, CancellationToken ct = default);
    Task DeleteUserAsync(int userId, CancellationToken ct = default);
}

/// <summary>当前登录用户（进程级）。</summary>
public interface ICurrentUserService
{
    User? Current { get; }
    void Set(User user);
    void Clear();
    event Action? CurrentUserChanged;
}

/// <summary>操作日志服务。</summary>
public interface IOpLogService
{
    Task AddAsync(string action, string? target, string? detail, bool success = true,
        TransferSource source = TransferSource.Manual, long durationMs = 0, string? userName = null, CancellationToken ct = default);
    Task<(List<OpLog> Items, int Total)> QueryAsync(DateTime? from, DateTime? to, string? userName,
        TransferSource? source, string? actionKeyword, bool? success, int page, int pageSize, CancellationToken ct = default);
    Task<List<OpLog>> RecentAsync(int count, TransferSource? source = null, CancellationToken ct = default);
    /// <summary>清理指定天数前的日志。</summary>
    Task<int> CleanupAsync(int keepDays, CancellationToken ct = default);
}
