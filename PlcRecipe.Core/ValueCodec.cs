using PlcRecipe.Core.Models;
using System.Buffers.Binary;

namespace PlcRecipe.Core;

/// <summary>
/// 值编解码：配方字符串值 ↔ PLC 字数组。
/// 统一约定：字内大端；32 位数据默认高字在前（ABCD 大端字序），
/// Modbus 设备可通过 dataFormat 切换 ABCD/BADC/CDAB/DCBA（参考 HslCommunication 约定）。
/// </summary>
public static class ValueCodec
{
    /// <summary>配方文件（txt）的保留字符：竖线/换行不可进入将写入文件的自由文本。
    /// 编解码本身不做此限制——水印等只写 PLC 不落文件的字符串允许包含竖线。</summary>
    public static readonly char[] FileSeparatorChars = ['|', '\r', '\n'];

    /// <summary>把字符串值编码为字数组（按数据行的类型与字数，默认 ABCD 字节序）。</summary>
    public static ushort[] Encode(RecipeItem item, string value, ModbusDataFormat fmt = ModbusDataFormat.ABCD)
    {
        var v = (value ?? string.Empty).Trim();
        try
        {
            switch (item.DataType)
            {
                case PlcDataType.Bool:
                {
                    var b = v switch
                    {
                        "1" => true,
                        "true" or "True" or "TRUE" => true,
                        "0" or "" => false,
                        "false" or "False" or "FALSE" => false,
                        _ => throw new FormatException($"布尔值只能是 0/1/true/false，收到“{v}”")
                    };
                    return Array.Empty<ushort>();
                }
                case PlcDataType.Int16:
                {
                    var n = short.Parse(v);
                    return new[] { ToWord((ushort)n) };
                }
                case PlcDataType.UInt16:
                    return new[] { ToWord(ushort.Parse(v)) };
                case PlcDataType.Int32:
                {
                    var n = int.Parse(v);
                    var words32 = Encode32((uint)n, fmt);
                    return words32;
                }
                case PlcDataType.UInt32:
                    return Encode32(uint.Parse(v), fmt);
                case PlcDataType.Float32:
                {
                    var f = float.Parse(v);
                    return Encode32(BitConverter.SingleToUInt32Bits(f), fmt);
                }
                case PlcDataType.String:
                {
                    int maxBytes = item.WordCount * 2;
                    var bytes = System.Text.Encoding.UTF8.GetBytes(v);
                    if (bytes.Length > maxBytes)
                        throw new FormatException($"字符串超出 {maxBytes} 字节上限（UTF-8 编码，一个汉字占 3 字节）");
                    var buf = new ushort[item.WordCount];
                    var raw = new byte[item.WordCount * 2];
                    Array.Copy(bytes, raw, bytes.Length);
                    for (int i = 0; i < item.WordCount; i++)
                        buf[i] = ToWord((ushort)((raw[2 * i] << 8) | raw[2 * i + 1]));
                    return buf;
                }
                default:
                    throw new FormatException($"不支持的类型 {item.DataType}");
            }
        }
        catch (FormatException) when (item.DataType != PlcDataType.Bool && item.DataType != PlcDataType.String)
        {
            throw new FormatException($"“{item.Name}”的值“{v}”不是有效的 {item.DataType}");
        }
        catch (OverflowException)
        {
            throw new FormatException($"“{item.Name}”的值“{v}”超出 {item.DataType} 范围");
        }
    }

    /// <summary>把字数组解码为字符串值。bool 类型直接读 bit。</summary>
    public static string Decode(RecipeItem item, ushort[] words, bool boolValue = false, ModbusDataFormat fmt = ModbusDataFormat.ABCD)
    {
        switch (item.DataType)
        {
            case PlcDataType.Bool:
                return boolValue ? "1" : "0";
            case PlcDataType.Int16:
                return ((short)FromWord(words[0])).ToString();
            case PlcDataType.UInt16:
                return FromWord(words[0]).ToString();
            case PlcDataType.Int32:
                return ((int)Decode32(words, fmt)).ToString();
            case PlcDataType.UInt32:
                return Decode32(words, fmt).ToString();
            case PlcDataType.Float32:
                return BitConverter.UInt32BitsToSingle(Decode32(words, fmt)).ToString("G7");
            case PlcDataType.String:
            {
                var raw = new byte[words.Length * 2];
                for (int i = 0; i < words.Length; i++)
                {
                    var w = FromWord(words[i]);
                    raw[2 * i] = (byte)(w >> 8);
                    raw[2 * i + 1] = (byte)(w & 0xFF);
                }
                // UTF-8：\0 只会出现在空位（多字节序列的后续字节 ≥0x80，不会误判）
                var s = System.Text.Encoding.UTF8.GetString(raw).TrimEnd('\0');
                int z = s.IndexOf('\0');
                return z >= 0 ? s[..z] : s;
            }
            default:
                throw new FormatException($"不支持的类型 {item.DataType}");
        }
    }

    /// <summary>
    /// 业务值比较：Bool（"1"=="true"）与数值类型（"100"=="100.0"）按语义比较，
    /// 其余类型字符串直比。回读校验与配方对比共用，避免等价值被误判为差异。
    /// </summary>
    public static bool ValuesEqual(PlcDataType type, string plcValue, string recipeValue)
    {
        var a = plcValue.Trim();
        var b = recipeValue.Trim();
        if (string.Equals(a, b, StringComparison.Ordinal)) return true;

        if (type == PlcDataType.Bool)
        {
            var pa = NormalizeBool(a);
            var pb = NormalizeBool(b);
            return pa.HasValue && pb.HasValue && pa == pb;
        }
        if (type is PlcDataType.Int16 or PlcDataType.UInt16 or PlcDataType.Int32 or PlcDataType.UInt32 or PlcDataType.Float32)
        {
            return double.TryParse(a, out var da) &&
                   double.TryParse(b, out var db) &&
                   Math.Abs(da - db) < 1e-6 * Math.Max(1, Math.Abs(da));
        }
        return false;
    }

    private static bool? NormalizeBool(string v) => v switch
    {
        "1" or "true" or "True" or "TRUE" => true,
        "0" or "" or "false" or "False" or "FALSE" => false,
        _ => null
    };

    /// <summary>校验字符串值能否按类型解析（不编码）。</summary>
    public static bool TryValidate(RecipeItem item, string? value, out string error)
    {
        error = string.Empty;
        try
        {
            if (item.DataType == PlcDataType.Bool)
            {
                var v = (value ?? string.Empty).Trim();
                if (v is not ("" or "0" or "1" or "true" or "false" or "True" or "False" or "TRUE" or "FALSE"))
                {
                    error = "布尔值只能是 0/1/true/false";
                    return false;
                }
                return true;
            }
            Encode(item, value ?? string.Empty);
            return true;
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static ushort ToWord(ushort v) => BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness(v) : v;
    private static ushort FromWord(ushort v) => BitConverter.IsLittleEndian ? BinaryPrimitives.ReverseEndianness(v) : v;

    /// <summary>
    /// 32 位值 → 两个线上字（支持 HSL 风格字节序）。
    /// 值的大端字节序为 B0B1B2B3，各格式下的字排列：
    /// ABCD=B0B1|B2B3，BADC=B1B0|B3B2，CDAB=B2B3|B0B1，DCBA=B3B2|B1B0。
    /// </summary>
    private static ushort[] Encode32(uint value, ModbusDataFormat fmt)
    {
        var b0 = (byte)(value >> 24);
        var b1 = (byte)(value >> 16);
        var b2 = (byte)(value >> 8);
        var b3 = (byte)value;
        var (p0, p1) = Layout(fmt, b0, b1, b2, b3);
        return new[] { ToWord((ushort)((p0.Item1 << 8) | p0.Item2)), ToWord((ushort)((p1.Item1 << 8) | p1.Item2)) };
    }

    /// <summary>两个线上字 → 32 位值（Encode32 的逆变换，四种排列均为自逆）。</summary>
    private static uint Decode32(ushort[] words, ModbusDataFormat fmt)
    {
        var w0 = FromWord(words[0]);
        var w1 = FromWord(words[1]);
        var (p0, p1) = Layout(fmt, (byte)(w0 >> 8), (byte)w0, (byte)(w1 >> 8), (byte)w1);
        return ((uint)p0.Item1 << 24) | ((uint)p0.Item2 << 16) | ((uint)p1.Item1 << 8) | p1.Item2;
    }

    /// <summary>字节排列变换（四种格式均为自逆排列，编码/解码共用）。</summary>
    private static ((byte, byte), (byte, byte)) Layout(ModbusDataFormat fmt, byte b0, byte b1, byte b2, byte b3) =>
        fmt switch
        {
            ModbusDataFormat.BADC => ((b1, b0), (b3, b2)),
            ModbusDataFormat.CDAB => ((b2, b3), (b0, b1)),
            ModbusDataFormat.DCBA => ((b3, b2), (b1, b0)),
            _ => ((b0, b1), (b2, b3))
        };
}
