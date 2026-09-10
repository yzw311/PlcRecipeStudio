using PlcRecipe.Core.Models;

namespace PlcRecipe.Core;

/// <summary>连续字地址块：把配方里地址连续的数据行合并成一次 PLC 读/写请求。</summary>
public sealed class WordBlock
{
    public required ParsedAddress Start { get; init; }
    public List<(RecipeItem Item, int OffsetInBlock)> Items { get; } = new();
    public int WordCount { get; set; }

    public override string ToString() => $"{Start.Raw} x{WordCount}";
}

/// <summary>连续位地址块。</summary>
public sealed class BitBlock
{
    public required ParsedAddress Start { get; init; }
    public List<(RecipeItem Item, int OffsetInBlock)> Items { get; } = new();
    public int BitCount { get; set; }

    public override string ToString() => $"{Start.Raw} x{BitCount}";
}

/// <summary>读写计划：按块合并后的全部读写单元。</summary>
public sealed class TransferPlan
{
    public required PlcBrand Brand { get; init; }
    public required IReadOnlyList<RecipeItem> Rows { get; init; }
    public List<WordBlock> WordBlocks { get; } = new();
    public List<BitBlock> BitBlocks { get; } = new();
}

/// <summary>单台设备的传输结果。</summary>
public sealed class DeviceTransferResult
{
    public int DeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public TransferDirection Direction { get; set; }
    public TransferStatus Status { get; set; } = TransferStatus.Pending;
    public string? Message { get; set; }
    /// <summary>上传时：变量名 → 读到的值</summary>
    public Dictionary<string, string>? ReadValues { get; set; }
    public long DurationMs { get; set; }
}

/// <summary>进度通知（线程安全值对象，经 IProgress 回传 UI）。</summary>
public sealed record TransferProgress(
    int DeviceId,
    string DeviceName,
    TransferDirection Direction,
    int Percent,
    string StatusText,
    TransferStatus Status = TransferStatus.Running);

/// <summary>对比结果行。</summary>
public sealed class CompareRow
{
    public required RecipeItem Item { get; init; }
    public string? PlcValue { get; set; }
    public string? RecipeValue { get; set; }
    public CompareState State { get; set; }
    /// <summary>写回 PLC 时是否选中（默认仅差异项）</summary>
    public bool Selected { get; set; }
}

/// <summary>整次对比结果。</summary>
public sealed class CompareResult
{
    public int DeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public int RecipeId { get; set; }
    public string RecipeName { get; set; } = string.Empty;
    /// <summary>UTC 时间（展示层负责转本地时区）。</summary>
    public DateTime Time { get; set; } = DateTime.UtcNow;
    public List<CompareRow> Rows { get; set; } = new();
    public bool Success { get; set; } = true;
    public string? Message { get; set; }
    public int DiffCount => Rows.Count(r => r.State == CompareState.Different);
    public int SameCount => Rows.Count(r => r.State == CompareState.Same);
    public int FailCount => Rows.Count(r => r.State == CompareState.ReadFailed);
}
