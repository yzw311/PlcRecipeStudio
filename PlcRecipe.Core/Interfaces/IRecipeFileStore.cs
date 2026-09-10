using PlcRecipe.Core.Models;

namespace PlcRecipe.Core.Interfaces;

/// <summary>配方文件库文档：解析结果（Device = 归属设备名，来自所在目录，权威）。</summary>
public sealed record RecipeFileDocument(string Name, int Version, string? Device, IReadOnlyList<RecipeItem> Items);

/// <summary>
/// 配方文件库（dataDir\Pfdoc\设备名\配方名.txt）——配方唯一存储，按设备分目录（同设备内配方名唯一）。
/// 版本历史：Pfdoc\设备名\history\配方名\v{N}.txt。
/// 文本格式（UTF-8，可手工编辑）：元数据行 "Key=值"，数据行 9 字段竖线分隔：
/// 名称|地址|类型|StringWords|值|下限|上限|单位|备注（尾部字段可省略）。
/// </summary>
public interface IRecipeFileStore
{
    /// <summary>文件库根目录（dataDir\Pfdoc）。</summary>
    string Directory { get; }

    bool Exists(string deviceName, string recipeName);

    /// <summary>写入/覆盖配方 txt（原子写）。</summary>
    Task SaveAsync(string deviceName, string recipeName, int version, IReadOnlyList<RecipeItem> rows);

    /// <summary>Compare-and-commit: history is written before the recoverable main copy.</summary>
    Task CommitAsync(string deviceName, string recipeName, int expectedVersion, int version, IReadOnlyList<RecipeItem> rows, string? savedBy = null, string? changeNote = null);

    /// <summary>读取配方 txt；不存在返回 null。</summary>
    Task<RecipeFileDocument?> TryLoadAsync(string deviceName, string recipeName);

    /// <summary>枚举全部配方（扫描所有设备目录；Device 取自所在目录名）。</summary>
    IReadOnlyList<RecipeFileDocument> LoadAll();

    Task DeleteAsync(string deviceName, string recipeName);

    /// <summary>同设备内重命名配方（主文件 + 历史整体迁移）。目标已存在则抛异常。</summary>
    Task RenameAsync(string deviceName, string oldName, string newName);

    Task SaveHistoryAsync(string deviceName, string recipeName, int version, IReadOnlyList<RecipeItem> rows, string? savedBy, string? changeNote);

    /// <summary>某配方的全部版本历史（按版本倒序；不含数据行正文）。</summary>
    IReadOnlyList<RecipeVersionHistory> GetHistory(string deviceName, string recipeName);

    Task<List<RecipeItem>?> TryLoadHistoryAsync(string deviceName, string recipeName, int version);

    Task DeleteHistoryAsync(string deviceName, string recipeName);

    /// <summary>设备改名：整个设备目录重命名（配方与历史随之迁移）。</summary>
    Task UpdateDeviceNameAsync(string oldDeviceName, string newDeviceName);

    /// <summary>旧版平铺布局迁移：把 Pfdoc 根目录下的 txt 按 Device= 头移入设备子目录。</summary>
    void MigrateFlatLayout();
}
