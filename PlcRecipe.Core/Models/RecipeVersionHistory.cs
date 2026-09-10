namespace PlcRecipe.Core.Models;

/// <summary>
/// 配方版本历史：每次保存/上传落库生成一条不可变快照（存于 Pfdoc/{设备}/history/ 的 txt 文件）。
/// MES 追溯的基础：任何时候都能查到"某版本是什么值、谁存的、何时存的"，并回滚。
/// </summary>
public class RecipeVersionHistory
{
    /// <summary>快照对应的配方版本号（与 Recipe.Version 一致）</summary>
    public int Version { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? SavedBy { get; set; }
    /// <summary>保存时间（UTC）</summary>
    public DateTime SavedAtUtc { get; set; }
    /// <summary>变更说明，如 "保存" / "上传落库" / "回滚自 v3"</summary>
    public string? ChangeNote { get; set; }
}
