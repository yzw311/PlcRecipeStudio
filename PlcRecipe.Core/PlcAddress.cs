namespace PlcRecipe.Core;

/// <summary>解析后的 PLC 地址（具体解析规则在各品牌驱动内实现）。</summary>
public readonly record struct ParsedAddress(
    /// <summary>数据区标识（如 S7 的 DataBlock/Memory，MC 的 D/W，FINS 的 DM/CIO，Modbus 的 4x/3x/0x/1x）</summary>
    string Area,
    /// <summary>字偏移（位设备为位编号）</summary>
    int Offset,
    /// <summary>位偏移（无则为 -1）</summary>
    int Bit,
    /// <summary>true=位设备（Bool）；false=字设备</summary>
    bool IsBitDevice,
    /// <summary>原始地址字符串</summary>
    string Raw)
{
    public override string ToString() => Raw;
}

/// <summary>地址解析异常。</summary>
public class PlcAddressException(string address, string reason)
    : Exception($"地址 [{address}] 无效：{reason}")
{
    public string Address { get; } = address;
}
