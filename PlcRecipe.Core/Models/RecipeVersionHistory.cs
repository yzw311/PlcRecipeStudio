using System.Text.Json.Serialization;

namespace PlcRecipe.Core.Models;

/// <summary>
/// 配方版本历史：每次保存/上传落库生成一条不可变快照（版本号 + 全量数据行 JSON）。
/// MES 追溯的基础：任何时候都能查到"某版本是什么值、谁存的、何时存的"，并回滚。
/// </summary>
public class RecipeVersionHistory
{
    public long Id { get; set; }
    public int RecipeId { get; set; }
    /// <summary>快照对应的配方版本号（与 Recipe.Version 一致）</summary>
    public int Version { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>全量数据行快照（JSON，结构见 RecipeSnapshot）</summary>
    public string SnapshotJson { get; set; } = string.Empty;
    public string? SavedBy { get; set; }
    /// <summary>保存时间（UTC）</summary>
    public DateTime SavedAtUtc { get; set; }
    /// <summary>变更说明，如 "保存" / "上传落库" / "回滚自 v3"</summary>
    public string? ChangeNote { get; set; }
}

/// <summary>版本快照的序列化结构（避免序列化导航属性造成环）。</summary>
public sealed record RecipeSnapshot(int Version, string Name, IReadOnlyList<RecipeItemSnapshot> Items);

/// <summary>版本快照中的单行（与 RecipeItem 字段一一对应，不含外键/导航）。</summary>
public sealed record RecipeItemSnapshot(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("address")] string Address,
    [property: JsonPropertyName("dataType")] PlcDataType DataType,
    [property: JsonPropertyName("stringWords")] int StringWords,
    [property: JsonPropertyName("unit")] string? Unit,
    [property: JsonPropertyName("access")] VariableAccess Access,
    [property: JsonPropertyName("sortOrder")] int SortOrder,
    [property: JsonPropertyName("remark")] string? Remark,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("lowerLimit")] double? LowerLimit = null,
    [property: JsonPropertyName("upperLimit")] double? UpperLimit = null);
