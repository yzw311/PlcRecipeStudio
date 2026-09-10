using PlcRecipe.Core;
using PlcRecipe.Core.Models;
using PlcRecipe.Drivers;

namespace PlcRecipe.Tests;

public class PlcAddressParserTests
{
    [Theory]
    [InlineData("DB1.DBW0", "DB1", 0, false)]
    [InlineData("DB10.DBD4", "DB10", 4, false)]
    [InlineData("DB1.DBX2.3", "DB1", 2, true)]
    [InlineData("MW10", "M", 10, false)]
    [InlineData("MD12", "M", 12, false)]
    [InlineData("M0.1", "M", 0, true)]
    [InlineData("IW0", "I", 0, false)]
    [InlineData("QW2", "Q", 2, false)]
    public void Siemens_有效地址(string addr, string area, int offset, bool isBit)
    {
        var p = PlcAddressParser.Parse(PlcBrand.Siemens, addr);
        Assert.Equal(area, p.Area);
        Assert.Equal(offset, p.Offset);
        Assert.Equal(isBit, p.IsBitDevice);
    }

    [Theory]
    [InlineData("DB1.DBB0")]   // 不支持字节
    [InlineData("DB1.DBX2.9")] // 位越界
    [InlineData("MD11")]       // 未对齐
    [InlineData("XYZ1")]
    [InlineData("MW")]
    public void Siemens_非法地址(string addr)
    {
        Assert.Throws<PlcAddressException>(() => PlcAddressParser.Parse(PlcBrand.Siemens, addr));
    }

    [Theory]
    [InlineData("D100", "D", 100, false)]
    [InlineData("ZR5000", "ZR", 5000, false)]
    [InlineData("W1F", "W", 31, false)]    // 十六进制
    [InlineData("M20", "M", 20, true)]
    [InlineData("X1F", "X", 31, true)]     // 十六进制
    [InlineData("Y0", "Y", 0, true)]
    public void Mitsubishi_有效地址(string addr, string area, int offset, bool isBit)
    {
        var p = PlcAddressParser.Parse(PlcBrand.Mitsubishi, addr);
        Assert.Equal(area, p.Area);
        Assert.Equal(offset, p.Offset);
        Assert.Equal(isBit, p.IsBitDevice);
    }

    [Theory]
    [InlineData("D100.3", "D", 100, true)]
    [InlineData("CIO0.5", "CIO", 0, true)]
    [InlineData("D100", "D", 100, false)]
    [InlineData("W10", "W", 10, false)]
    [InlineData("H20", "H", 20, false)]
    public void Omron_有效地址(string addr, string area, int offset, bool isBit)
    {
        var p = PlcAddressParser.Parse(PlcBrand.Omron, addr);
        Assert.Equal(area, p.Area);
        Assert.Equal(offset, p.Offset);
        Assert.Equal(isBit, p.IsBitDevice);
    }

    [Fact]
    public void Omron_位越界()
    {
        Assert.Throws<PlcAddressException>(() => PlcAddressParser.Parse(PlcBrand.Omron, "D100.16"));
    }

    [Theory]
    [InlineData("HR40001", "HR", 0)]
    [InlineData("HR40100", "HR", 99)]
    [InlineData("IR30001", "IR", 0)]
    [InlineData("C00001", "C", 0)]
    [InlineData("DI10001", "DI", 0)]
    [InlineData("D0", "HR", 0)]
    [InlineData("D100", "HR", 100)]
    [InlineData("I0", "IR", 0)]
    [InlineData("I20", "IR", 20)]
    [InlineData("X0", "DI", 0)]
    [InlineData("Y7", "C", 7)]
    public void Modbus_有效地址(string addr, string area, int offset)
    {
        var p = PlcAddressParser.Parse(PlcBrand.ModbusTcp, addr);
        Assert.Equal(area, p.Area);
        Assert.Equal(offset, p.Offset);
    }

    [Theory]
    [InlineData("HR39999")]
    [InlineData("C99999")]
    [InlineData("XX1")]
    public void Modbus_非法地址(string addr)
    {
        Assert.Throws<PlcAddressException>(() => PlcAddressParser.Parse(PlcBrand.ModbusTcp, addr));
    }

    [Theory]
    // HSL 风格：x= 功能区 + 裸数字（0 基址），s= 站号允许但忽略（以设备从站号为准）
    [InlineData("100", "HR", 100)]
    [InlineData("x=4;100", "HR", 100)]
    [InlineData("x=3;100", "IR", 100)]
    [InlineData("x=1;5", "C", 5)]
    [InlineData("x=2;5", "DI", 5)]
    [InlineData("s=2;100", "HR", 100)]
    [InlineData("s=2;x=3;100", "IR", 100)]
    public void Modbus_HSL风格地址(string addr, string area, int offset)
    {
        var p = PlcAddressParser.Parse(PlcBrand.ModbusTcp, addr);
        Assert.Equal(area, p.Area);
        Assert.Equal(offset, p.Offset);
    }

    [Fact]
    public void Modbus_HSL风格_非法功能区()
    {
        Assert.Throws<PlcAddressException>(() => PlcAddressParser.Parse(PlcBrand.ModbusTcp, "x=5;100"));
    }

    [Fact]
    public void 空地址_抛异常()
    {
        Assert.Throws<PlcAddressException>(() => PlcAddressParser.Parse(PlcBrand.Siemens, "  "));
    }
}

public class ValueCodecTests
{
    private static RecipeItem Var(PlcDataType type, int stringWords = 8) => new()
    {
        Name = "测试",
        Address = "D0",
        DataType = type,
        StringWords = stringWords
    };

    [Theory]
    [InlineData(PlcDataType.Int16, "-1234")]
    [InlineData(PlcDataType.UInt16, "65535")]
    [InlineData(PlcDataType.Int32, "-123456789")]
    [InlineData(PlcDataType.UInt32, "4294967295")]
    [InlineData(PlcDataType.Float32, "3.141593")]
    [InlineData(PlcDataType.String, "ABCD1234")]
    public void 编解码往返一致(PlcDataType type, string value)
    {
        var v = Var(type, type == PlcDataType.String ? 4 : 8);
        var words = ValueCodec.Encode(v, value);
        var decoded = ValueCodec.Decode(v, words);
        Assert.Equal(value, decoded);
    }

    [Fact]
    public void Float32_两个字_大端字序()
    {
        var v = Var(PlcDataType.Float32);
        var words = ValueCodec.Encode(v, "12.5");
        Assert.Equal(2, words.Length);
        var decoded = ValueCodec.Decode(v, words);
        Assert.Equal("12.5", decoded);
    }

    [Theory]
    [InlineData(ModbusDataFormat.ABCD)]
    [InlineData(ModbusDataFormat.BADC)]
    [InlineData(ModbusDataFormat.CDAB)]
    [InlineData(ModbusDataFormat.DCBA)]
    public void 字节序_四格式编解码往返一致(ModbusDataFormat fmt)
    {
        var v = Var(PlcDataType.Float32);
        var words = ValueCodec.Encode(v, "123.456", fmt);
        Assert.Equal("123.456", ValueCodec.Decode(v, words, false, fmt));
    }

    [Fact]
    public void 字节序_不同格式产生不同线上数据()
    {
        var v = Var(PlcDataType.Float32);
        var abcd = ValueCodec.Encode(v, "12.5", ModbusDataFormat.ABCD);
        var cdab = ValueCodec.Encode(v, "12.5", ModbusDataFormat.CDAB);
        // CDAB = 字交换
        Assert.Equal(abcd[0], cdab[1]);
        Assert.Equal(abcd[1], cdab[0]);
    }

    [Fact]
    public void 超范围值_报错()
    {
        Assert.Throws<FormatException>(() => ValueCodec.Encode(Var(PlcDataType.Int16), "70000"));
        Assert.Throws<FormatException>(() => ValueCodec.Encode(Var(PlcDataType.UInt16), "-1"));
    }

    [Fact]
    public void 字符串超长_报错()
    {
        Assert.Throws<FormatException>(() => ValueCodec.Encode(Var(PlcDataType.String, 2), "TOOLONGSTRING"));
    }

    [Fact]
    public void 非数字_报错()
    {
        Assert.Throws<FormatException>(() => ValueCodec.Encode(Var(PlcDataType.Float32), "abc"));
    }

    [Fact]
    public void 校验接口_非法值返回错误()
    {
        Assert.False(ValueCodec.TryValidate(Var(PlcDataType.Int16), "abc", out var error));
        Assert.False(string.IsNullOrEmpty(error));
        Assert.True(ValueCodec.TryValidate(Var(PlcDataType.Int16), "100", out _));
    }
}
