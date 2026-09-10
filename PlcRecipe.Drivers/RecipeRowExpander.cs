using PlcRecipe.Core;
using PlcRecipe.Core.Models;

namespace PlcRecipe.Drivers;

/// <summary>
/// 配方数据行批量展开：给定起始地址 + 数量 + 类型，生成连续的地址序列。
/// 各品牌地址约定见 PlcAddressParser。
/// </summary>
public static class RecipeRowExpander
{
    /// <summary>展开为连续数据行。</summary>
    public static IReadOnlyList<RecipeItem> Expand(
        PlcBrand brand,
        string startAddress,
        int count,
        PlcDataType type,
        int stringWords,
        string namePrefix,
        IReadOnlyList<string> existingNames)
    {
        if (count <= 0) throw new ArgumentException("数量必须大于 0");
        if (string.IsNullOrWhiteSpace(namePrefix)) namePrefix = "参数";
        if (type == PlcDataType.String) stringWords = Math.Max(1, stringWords);

        var start = PlcAddressParser.Parse(brand, startAddress);

        // 位地址 → 强制 Bool；字地址 → 禁止 Bool
        if (start.IsBitDevice && type != PlcDataType.Bool) type = PlcDataType.Bool;
        if (!start.IsBitDevice && type == PlcDataType.Bool)
            throw new InvalidOperationException("起始地址是字地址，类型不能为 Bool");

        var nameSet = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        var list = new List<RecipeItem>(count);

        int offset = start.Offset;
        int bit = start.Bit >= 0 ? start.Bit : 0;

        for (int i = 0; i < count; i++)
        {
            string name = BuildName(namePrefix, i + 1, nameSet);
            nameSet.Add(name);

            string address = start.IsBitDevice
                ? FormatBit(brand, start.Area, offset, bit)
                : FormatWord(brand, start.Area, offset, type);

            list.Add(new RecipeItem
            {
                Name = name,
                Address = address,
                DataType = type,
                StringWords = type == PlcDataType.String ? stringWords : 8,
                Access = VariableAccess.ReadWrite,
                Value = type == PlcDataType.String ? "" : "0",
                SortOrder = i
            });

            if (start.IsBitDevice)
                (offset, bit) = AdvanceBit(brand, offset, bit);
            else
                offset += WordStep(brand, type, stringWords);
        }
        return list;
    }

    private static string BuildName(string prefix, int seq, HashSet<string> existing)
    {
        string candidate = $"{prefix}{seq}";
        if (!existing.Contains(candidate)) return candidate;
        int n = 2;
        while (existing.Contains($"{candidate}_{n}")) n++;
        return $"{candidate}_{n}";
    }

    // ---------- 字步进 ----------
    private static int WordStep(PlcBrand brand, PlcDataType type, int stringWords)
    {
        int words = type switch
        {
            PlcDataType.Int16 or PlcDataType.UInt16 => 1,
            PlcDataType.Int32 or PlcDataType.UInt32 or PlcDataType.Float32 => 2,
            PlcDataType.String => stringWords,
            _ => 1
        };
        // 西门子的字地址按字节偏移，16 位占 2 字节、32 位占 4 字节
        if (brand == PlcBrand.Siemens)
            words = words * 2;
        return words;
    }

    // ---------- 字地址格式化 ----------
    private static string FormatWord(PlcBrand brand, string area, int offset, PlcDataType type) =>
        brand switch
        {
            PlcBrand.Siemens when area.StartsWith("DB", StringComparison.OrdinalIgnoreCase) =>
                $"DB{area[2..]}.{(type is PlcDataType.Int32 or PlcDataType.UInt32 or PlcDataType.Float32 ? "DBD" : "DBW")}{offset}",
            PlcBrand.Siemens when area is "M" =>
                $"{(type is PlcDataType.Int32 or PlcDataType.UInt32 or PlcDataType.Float32 ? "MD" : "MW")}{offset}",
            PlcBrand.Siemens when area is "I" => $"IW{offset}",
            PlcBrand.Siemens when area is "Q" => $"QW{offset}",
            PlcBrand.ModbusTcp or PlcBrand.ModbusRtu => $"D{offset}",
            PlcBrand.Mitsubishi when area is "W" => $"W{offset:X}",
            _ => $"{area}{offset}"
        };

    // ---------- 位地址格式化 ----------
    private static string FormatBit(PlcBrand brand, string area, int offset, int bit) =>
        brand switch
        {
            PlcBrand.Siemens when area.StartsWith("DB", StringComparison.OrdinalIgnoreCase) =>
                $"{area}.DBX{offset}.{bit}",
            PlcBrand.Siemens => $"{area}{offset}.{bit}",
            PlcBrand.Omron => $"{area}{offset}.{bit}",
            PlcBrand.ModbusTcp or PlcBrand.ModbusRtu => $"Y{offset}",
            PlcBrand.Mitsubishi when area is "B" or "X" or "Y" => $"{area}{offset:X}",
            _ => $"{area}{offset}"
        };

    // ---------- 位推进 ----------
    // 各品牌位数不同：S7 字节 8 位、欧姆龙字 16 位、三菱/Modbus/Mock 位即一个节点
    private static (int offset, int bit) AdvanceBit(PlcBrand brand, int offset, int bit)
    {
        return brand switch
        {
            PlcBrand.Siemens => bit >= 7 ? (offset + 1, 0) : (offset, bit + 1),
            PlcBrand.Omron => bit >= 15 ? (offset + 1, 0) : (offset, bit + 1),
            _ => (offset + 1, 0)   // 位号直接 +1，不拆字节/字
        };
    }
}