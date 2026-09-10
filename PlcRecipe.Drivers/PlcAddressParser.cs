using PlcRecipe.Core;

namespace PlcRecipe.Drivers;

/// <summary>按品牌解析地址字符串。解析失败抛 PlcAddressException。</summary>
public static class PlcAddressParser
{
    public static ParsedAddress Parse(PlcBrand brand, string address)
    {
        var raw = (address ?? string.Empty).Trim();
        if (raw.Length == 0)
            throw new PlcAddressException(address ?? string.Empty, "地址不能为空");
        // HSL 风格前缀仅 Modbus 有意义
        static ParsedAddress Parse(PlcBrand b, string raw)
        {
            if (b is PlcBrand.ModbusTcp or PlcBrand.ModbusRtu &&
                (raw.Contains(';') || int.TryParse(raw, out _)))
            {
                int area = 4; // x=1 线圈 / x=2 离散输入 / x=3 输入寄存器 / x=4 保持寄存器（默认）
                foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (part.StartsWith("x=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!int.TryParse(part[2..], out var x) || x is < 1 or > 4)
                            throw new PlcAddressException(raw, "x= 功能区仅支持 1~4（1 线圈 / 2 离散输入 / 3 输入寄存器 / 4 保持寄存器）");
                        area = x;
                    }
                    // s=站号：以设备配置的从站号为准，地址里允许出现但忽略
                }
                var addr = raw[(raw.LastIndexOf(';') + 1)..].Trim();
                return ParseModbusPrefixed(addr, area);
            }
            return b switch
            {
                PlcBrand.Siemens => ParseS7(raw),
                PlcBrand.Mitsubishi => ParseMelsec(raw),
                PlcBrand.Omron => ParseFins(raw),
                PlcBrand.ModbusTcp or PlcBrand.ModbusRtu => ParseModbus(raw),
                PlcBrand.Mock => ParseMock(raw),
                _ => throw new PlcAddressException(raw, $"未知品牌 {b}")
            };
        }

        return Parse(brand, raw);
    }

    /// <summary>仅校验不抛异常。</summary>
    public static bool TryParse(PlcBrand brand, string address, out ParsedAddress parsed, out string error)
    {
        try
        {
            parsed = Parse(brand, address);
            error = string.Empty;
            return true;
        }
        catch (PlcAddressException ex)
        {
            parsed = default;
            error = ex.Message;
            return false;
        }
    }

    // ---------- 西门子 ----------
    // 支持：DB1.DBW0 / DB1.DBD4 / DB1.DBX0.1 / MW10 / MD12 / M0.1 / IW0 / QW2 / I0.0 / Q0.1
    private static ParsedAddress ParseS7(string s)
    {
        static ParsedAddress Bit(string area, int off, int bit, string raw) =>
            new(area, off, bit, true, raw);

        if (s.StartsWith("DB", StringComparison.OrdinalIgnoreCase))
        {
            var dot = s.IndexOf('.');
            if (dot < 0) throw new PlcAddressException(s, "DB 地址需要形如 DB1.DBW0");
            var dbPart = s[..dot].Substring(2);
            if (!int.TryParse(dbPart, out var dbNo) || dbNo <= 0)
                throw new PlcAddressException(s, "DB 编号无效");
            var rest = s[(dot + 1)..].ToUpperInvariant();
            if (rest.StartsWith("DBW", StringComparison.Ordinal))
            {
                var off = ParseNonNegative(rest[3..], s);
                return new ParsedAddress($"DB{dbNo}", off, -1, false, s);
            }
            if (rest.StartsWith("DBD", StringComparison.Ordinal))
            {
                var off = ParseNonNegative(rest[3..], s);
                if (off % 2 != 0) throw new PlcAddressException(s, "DBD 地址必须按 2 对齐");
                return new ParsedAddress($"DB{dbNo}", off, -1, false, s);
            }
            if (rest.StartsWith("DBX", StringComparison.Ordinal))
            {
                var parts = rest[3..].Split('.');
                if (parts.Length != 2)
                    throw new PlcAddressException(s, "DBX 地址必须显式包含 .bit（例如 DB1.DBX0.1）");
                var off = ParseNonNegative(parts[0], s);
                var bit = parts.Length > 1 ? ParseNonNegative(parts[1], s) : 0;
                if (bit > 7) throw new PlcAddressException(s, "位号必须为 0-7");
                return Bit($"DB{dbNo}", off, bit, s);
            }
            throw new PlcAddressException(s, "仅支持 DBW/DBD/DBX 寻址");
        }

        if (s.StartsWith("MW", StringComparison.OrdinalIgnoreCase))
            return new ParsedAddress("M", ParseNonNegative(s[2..], s), -1, false, s);
        if (s.StartsWith("MD", StringComparison.OrdinalIgnoreCase))
        {
            var off = ParseNonNegative(s[2..], s);
            if (off % 2 != 0) throw new PlcAddressException(s, "MD 地址必须按 2 对齐");
            return new ParsedAddress("M", off, -1, false, s);
        }
        if (s.StartsWith("IW", StringComparison.OrdinalIgnoreCase))
            return new ParsedAddress("I", ParseNonNegative(s[2..], s), -1, false, s);
        if (s.StartsWith("QW", StringComparison.OrdinalIgnoreCase))
            return new ParsedAddress("Q", ParseNonNegative(s[2..], s), -1, false, s);
        if (s.StartsWith("M", StringComparison.OrdinalIgnoreCase))
        {
            var parts = s[1..].Split('.');
            var off = ParseNonNegative(parts[0], s);
            var bit = parts.Length > 1 ? ParseNonNegative(parts[1], s) : 0;
            if (bit > 7) throw new PlcAddressException(s, "位号必须为 0-7");
            return new ParsedAddress("M", off, bit, true, s);
        }
        if (s.StartsWith('I') || s.StartsWith('Q'))
        {
            var area = char.ToUpperInvariant(s[0]).ToString();
            var parts = s[1..].Split('.');
            var off = ParseNonNegative(parts[0], s);
            var bit = parts.Length > 1 ? ParseNonNegative(parts[1], s) : 0;
            if (bit > 7) throw new PlcAddressException(s, "位号必须为 0-7");
            return new ParsedAddress(area, off, bit, true, s);
        }
        throw new PlcAddressException(s, "支持 DB1.DBW0 / DB1.DBX0.1 / MW10 / MD12 / M0.1 / IW0 / QW0 等格式");
    }

    // ---------- 三菱 MC ----------
    // 字设备：D/W/R/ZR（W 十六进制）；位设备：M/B（B 十六进制）/X/Y（十六进制）
    private static ParsedAddress ParseMelsec(string s)
    {
        var prefix = TakeAlpha(s);
        var numberPart = s[prefix.Length..];
        if (prefix.Length == 0 || numberPart.Length == 0)
            throw new PlcAddressException(s, "三菱地址形如 D100 / W10 / M20 / X1F / B0");
        bool hex = prefix is "W" or "X" or "Y" or "B";
        var code = prefix.ToUpperInvariant();
        int off;
        if (hex)
        {
            if (!int.TryParse(numberPart, System.Globalization.NumberStyles.HexNumber, null, out off) || off < 0)
                throw new PlcAddressException(s, $"{prefix} 设备号必须为十六进制");
        }
        else
        {
            off = ParseNonNegative(numberPart, s);
        }
        bool isBit = code is "M" or "B" or "X" or "Y";
        return new ParsedAddress(code, off, -1, isBit, s);
    }

    // ---------- 欧姆龙 FINS ----------
    // 字：D100 / CIO100 / W10 / H10 / A10；位：D100.3 / CIO0.5 / W10.2
    private static ParsedAddress ParseFins(string s)
    {
        string area;
        string numberPart;
        int bit = -1;

        var dot = s.IndexOf('.');
        if (dot >= 0)
        {
            bit = ParseNonNegative(s[(dot + 1)..], s);
            if (bit > 15) throw new PlcAddressException(s, "FINS 位号必须为 0-15");
            s = s[..dot];
        }
        if (s.StartsWith("CIO", StringComparison.OrdinalIgnoreCase))
        {
            area = "CIO";
            numberPart = s[3..];
        }
        else
        {
            area = TakeAlpha(s).ToUpperInvariant();
            numberPart = s[area.Length..];
        }
        if (area is not ("D" or "CIO" or "W" or "H" or "A") || numberPart.Length == 0)
            throw new PlcAddressException(s, "欧姆龙地址形如 D100 / CIO0 / W10 / H0 / A0，位用 .3 后缀");
        var off = ParseNonNegative(numberPart, s);
        bool isBit = bit >= 0;
        return new ParsedAddress(area, off, bit, isBit, s);
    }

    // ---------- Modbus ----------
    // HR40001 保持寄存器(字) / IR30001 输入寄存器(字,只读) / C00001 线圈(位) / DI10001 离散输入(位,只读)
    // 1 基址；寄存器/位号上限 65536
    private static ParsedAddress ParseModbus(string s)
    {
        const int baseHolding = 40001, baseInput = 30001, baseCoil = 1, baseDiscrete = 10001;
        const int maxOffset = 65535;

        if (s.StartsWith("HR", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(s[2..], out var n) && n >= baseHolding && n - baseHolding <= maxOffset)
            return new ParsedAddress("HR", n - baseHolding, -1, false, s);

        // 简写 D0 等价于 HR40001（常用写法：D 表示保持寄存器）
        if (s.StartsWith('D') && int.TryParse(s[1..], out var dShort) && dShort >= 0 && dShort <= maxOffset)
            return new ParsedAddress("HR", dShort, -1, false, s);

        if (s.StartsWith("IR", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(s[2..], out var i) && i >= baseInput && i - baseInput <= maxOffset)
            return new ParsedAddress("IR", i - baseInput, -1, false, s);

        // 简写 I0 等价于 IR30001（输入寄存器）
        if (s.StartsWith('I') && int.TryParse(s[1..], out var iShort) && iShort >= 0 && iShort <= maxOffset)
            return new ParsedAddress("IR", iShort, -1, false, s);

        if (s.StartsWith("DI", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(s[2..], out var d) && d >= baseDiscrete && d - baseDiscrete <= maxOffset)
            return new ParsedAddress("DI", d - baseDiscrete, -1, true, s);

        // 简写 X0 等价于 DI10001（离散输入）
        if (s.StartsWith('X') && int.TryParse(s[1..], out var xShort) && xShort >= 0 && xShort <= maxOffset)
            return new ParsedAddress("DI", xShort, -1, true, s);

        if (s.StartsWith('C') && int.TryParse(s[1..], out var c) && c >= baseCoil && c - baseCoil <= maxOffset)
            return new ParsedAddress("C", c - baseCoil, -1, true, s);

        // 简写 Y0 等价于 C00001（线圈）
        if (s.StartsWith('Y') && int.TryParse(s[1..], out var yShort) && yShort >= 0 && yShort <= maxOffset)
            return new ParsedAddress("C", yShort, -1, true, s);

        throw new PlcAddressException(s, "Modbus 地址形如 D0 / D100 / I0 / X0 / Y0 或标准写 HR40001 / IR30001 / C00001 / DI10001（保持寄存器/输入寄存器/线圈/离散输入）");
    }

    /// <summary>HSL 风格：x= 功能区 + 裸数字地址（0 基址）。area: 1 线圈 / 2 离散输入 / 3 输入寄存器 / 4 保持寄存器（默认）。</summary>
    private static ParsedAddress ParseModbusPrefixed(string s, int area)
    {
        if (!int.TryParse(s, out var n) || n < 0 || n > 65535)
            throw new PlcAddressException(s, "x= 前缀地址须为 0~65535 的裸数字（0 基址）");
        return area switch
        {
            1 => new ParsedAddress("C", n, -1, true, s),
            2 => new ParsedAddress("DI", n, -1, true, s),
            3 => new ParsedAddress("IR", n, -1, false, s),
            _ => new ParsedAddress("HR", n, -1, false, s)
        };
    }

    // ---------- Mock ----------
    // D100 字 / M100 位（十进制）
    private static ParsedAddress ParseMock(string s)
    {
        if (s.StartsWith('D')) return new ParsedAddress("D", ParseNonNegative(s[1..], s), -1, false, s);
        if (s.StartsWith('M')) return new ParsedAddress("M", ParseNonNegative(s[1..], s), -1, true, s);
        throw new PlcAddressException(s, "模拟 PLC 地址形如 D100 / M100");
    }

    // ---------- helpers ----------
    private static string TakeAlpha(string s)
    {
        int i = 0;
        while (i < s.Length && char.IsLetter(s[i])) i++;
        return s[..i];
    }

    private static int ParseNonNegative(string text, string raw)
    {
        if (!int.TryParse(text, out var v) || v < 0)
            throw new PlcAddressException(raw, $"设备号“{text}”无效");
        return v;
    }
}
