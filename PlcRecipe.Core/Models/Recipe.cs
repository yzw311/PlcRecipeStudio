namespace PlcRecipe.Core.Models;

/// <summary>配方：挂在设备下。一台设备可有多个配方（不同产品/工艺的参数组）。</summary>
public class Recipe
{
    public int Id { get; set; }
    /// <summary>所属设备</summary>
    public int DeviceId { get; set; }
    public PlcDevice? Device { get; set; }

    public string Name { get; set; } = string.Empty;
    /// <summary>版本号，每次保存自动 +1</summary>
    public int Version { get; set; } = 1;
    public string? Remark { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public List<RecipeItem> Items { get; set; } = new();

    /// <summary>PLC 信号联动使用的配方号 = 设备内序号（1-based，非持久化，加载时填充）。</summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public int PlcNo { get; set; }
}
