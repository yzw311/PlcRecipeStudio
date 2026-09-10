namespace PlcRecipe.Core;

/// <summary>解析后的 PLC 地址（具体解析规则在各品牌驱动内实现）。</summary>
/// <param name="Area">数据区标识（如 S7 的 DataBlock/Memory，MC 的 D/W，FINS 的 DM/CIO，Modbus 的 4x/3x/0x/1x）</param>
/// <param name="Offset">字偏移（位设备为位编号）</param>
/// <param name="Bit">位偏移（无则为 -1）</param>
/// <param name="IsBitDevice">true=位设备（Bool）；false=字设备</param>
/// <param name="Raw">原始地址字符串</param>
public readonly record struct ParsedAddress(string Area, int Offset, int Bit, bool IsBitDevice, string Raw)
{
    public override string ToString() => Raw;
}

/// <summary>地址解析异常。</summary>
public class PlcAddressException(string address, string reason)
    : Exception($"地址 [{address}] 无效：{reason}")
{
    public string Address { get; } = address;
}
