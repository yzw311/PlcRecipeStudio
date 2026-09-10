using System.Buffers.Binary;
using PlcRecipe.Core;
using PlcRecipe.Drivers;
using PlcRecipe.Drivers.Modbus;

namespace PlcRecipe.Tests;

/// <summary>
/// ModbusPdu（TCP/RTU 公共 PDU 层）行为回归测试。
/// 断言口径与抽取前两客户端内联实现逐字节对齐：请求 PDU 布局、字大端+WireCodec、
/// 位 LSB-first、分块与地址回绕、功能区映射、应答校验（TCP 含 ByteCount / RTU 不含）与守卫文案。
/// </summary>
public class ModbusPduTests
{
    private static ParsedAddress WordAddr(string area, int offset, string raw = "D100") =>
        new(area, offset, -1, false, raw);

    private static ParsedAddress BitAddr(string area, int offset, string raw = "M0") =>
        new(area, offset, -1, true, raw);

    // ---------- 守卫：异常类型与文案与原实现逐字一致 ----------

    [Fact]
    public void 守卫_读字_位地址报错文案不变()
    {
        var ex = Assert.Throws<PlcAddressException>(() =>
            ModbusPdu.EnsureWordArea(BitAddr("M", 0, "M0"), write: false));
        Assert.Contains("位地址请使用 ReadBitsAsync", ex.Message);
    }

    [Fact]
    public void 守卫_写字_IR只读报错文案不变()
    {
        var ex = Assert.Throws<PlcAddressException>(() =>
            ModbusPdu.EnsureWordArea(WordAddr("IR", 0, "IR30001"), write: true));
        Assert.Contains("IR（输入寄存器）为只读区，不可写", ex.Message);
    }

    [Fact]
    public void 守卫_读位_字地址报错文案不变()
    {
        var ex = Assert.Throws<PlcAddressException>(() =>
            ModbusPdu.EnsureBitArea(WordAddr("HR", 0, "HR40001"), write: false));
        Assert.Contains("字地址请使用 ReadWordsAsync", ex.Message);
    }

    [Fact]
    public void 守卫_写位_DI只读报错文案不变()
    {
        var ex = Assert.Throws<PlcAddressException>(() =>
            ModbusPdu.EnsureBitArea(BitAddr("DI", 0, "DI10001"), write: true));
        Assert.Contains("DI（离散输入）为只读区，不可写", ex.Message);
    }

    [Fact]
    public void 守卫_合法区域与只读读访问_通过()
    {
        ModbusPdu.EnsureWordArea(WordAddr("IR", 0), write: false);  // IR 允许读
        ModbusPdu.EnsureWordArea(WordAddr("HR", 0), write: true);
        ModbusPdu.EnsureBitArea(BitAddr("C", 0), write: true);
        ModbusPdu.EnsureBitArea(BitAddr("DI", 0), write: false);    // DI 允许读
    }

    // ---------- 功能码映射 ----------

    [Theory]
    [InlineData("IR", 0x04)]
    [InlineData("HR", 0x03)]
    [InlineData("D", 0x03)]
    public void 功能码_读字映射与原实现一致(string area, byte expected) =>
        Assert.Equal(expected, ModbusPdu.ReadWordsFunction(area));

    [Theory]
    [InlineData("DI", 0x02)]
    [InlineData("C", 0x01)]
    [InlineData("Y", 0x01)]
    public void 功能码_读位映射与原实现一致(string area, byte expected) =>
        Assert.Equal(expected, ModbusPdu.ReadBitsFunction(area));

    // ---------- 请求 PDU：逐字节布局 ----------

    [Fact]
    public void 构建读请求_地址与数量大端()
    {
        var pdu = ModbusPdu.BuildReadRequest(100, 120);
        Assert.Equal(new byte[] { 0x00, 0x64, 0x00, 0x78 }, pdu);
    }

    [Fact]
    public void 构建写字请求_布局与线上字序与原实现一致()
    {
        // 内部字 0x1234 → 线上大端寄存器值 = WireCodec.ToWire = 字节反转 0x3412
        var pdu = ModbusPdu.BuildWriteWordsRequest(0, new ushort[] { 0x1234, 0xABCD });
        Assert.Equal(5 + 4, pdu.Length);
        Assert.Equal(0x00, pdu[0]);                    // addr BE
        Assert.Equal(0x00, pdu[1]);
        Assert.Equal(0x00, pdu[2]);                    // count=2 BE
        Assert.Equal(0x02, pdu[3]);
        Assert.Equal(0x04, pdu[4]);                    // byteCount = 2*2
        Assert.Equal(0x34, pdu[5]);                    // ToWire(0x1234) = 0x3412 → 34 12
        Assert.Equal(0x12, pdu[6]);
        Assert.Equal(0xCD, pdu[7]);                    // ToWire(0xABCD) = 0xCDAB → CD AB
        Assert.Equal(0xAB, pdu[8]);
    }

    [Fact]
    public void 构建写位请求_LSB填充与字节数与原实现一致()
    {
        // 9 个位：0,2,3 置位 → 字节0 = 0b00001101；位8 → 字节1 = 0x01
        var bits = new bool[9];
        bits[0] = bits[2] = bits[3] = bits[8] = true;
        var pdu = ModbusPdu.BuildWriteBitsRequest(5, bits);
        Assert.Equal(5 + 2, pdu.Length);
        Assert.Equal(0x00, pdu[0]);                    // addr=5 BE
        Assert.Equal(0x05, pdu[1]);
        Assert.Equal(0x00, pdu[2]);                    // count=9 BE
        Assert.Equal(0x09, pdu[3]);
        Assert.Equal(0x02, pdu[4]);                    // byteCount = (9+7)/8
        Assert.Equal(0b00001101, pdu[5]);
        Assert.Equal(0x01, pdu[6]);
    }

    // ---------- 分块：与原 while 循环算术一致（含 ushort 地址回绕） ----------

    [Fact]
    public void 分块_整除与非整除_数量与地址推进与原实现一致()
    {
        var chunks = ModbusPdu.Chunk(0, 300, 120).ToList();
        Assert.Equal(3, chunks.Count);
        Assert.Equal((ushort)0, chunks[0].Address);
        Assert.Equal(120, chunks[0].Count);
        Assert.Equal((ushort)120, chunks[1].Address);
        Assert.Equal(120, chunks[1].Count);
        Assert.Equal((ushort)240, chunks[2].Address);
        Assert.Equal(60, chunks[2].Count);

        var single = ModbusPdu.Chunk(50, 1, 120).ToList();
        var one = Assert.Single(single);
        Assert.Equal((ushort)50, one.Address);
        Assert.Equal(1, one.Count);
    }

    [Fact]
    public void 分块_跨65535_保留ushort回绕语义()
    {
        // 原 (ushort)(start.Offset + done) 的回绕行为原样保留
        var chunks = ModbusPdu.Chunk(65530, 200, 120).ToList();
        Assert.Equal((ushort)65530, chunks[0].Address);
        Assert.Equal(120, chunks[0].Count);
        Assert.Equal((ushort)114, chunks[1].Address);  // (ushort)(65530+120)=65650 回绕 → 65650-65536=114
        Assert.Equal(80, chunks[1].Count);
    }

    // ---------- 响应校验：TCP（含 ByteCount）/ RTU（不含）两套口径 ----------

    [Fact]
    public void 应答校验_读字_TCP与RTU文案与阈值不变()
    {
        var shortData = new byte[3];
        var exTcp = Assert.Throws<IOException>(() =>
            ModbusPdu.EnsureWordsResponse(shortData, payloadStart: 1, n: 2, "TCP"));
        Assert.Equal("Modbus TCP 读寄存器应答数据不足", exTcp.Message);

        var exRtu = Assert.Throws<IOException>(() =>
            ModbusPdu.EnsureWordsResponse(shortData, payloadStart: 0, n: 2, "RTU"));
        Assert.Equal("Modbus RTU 读寄存器应答数据不足", exRtu.Message);

        // TCP：data[0]=ByteCount 前缀，1+4=5 字节即通过
        ModbusPdu.EnsureWordsResponse(new byte[5], 1, 2, "TCP");
        ModbusPdu.EnsureWordsResponse(new byte[4], 0, 2, "RTU");
    }

    [Fact]
    public void 应答校验_读位_TCP校验ByteCount字段_RTU只校验长度()
    {
        // TCP：ByteCount=2，n=9 需要 2 字节 → 满足；但数据总长 < 1+ByteCount → 仍报错
        var ex1 = Assert.Throws<IOException>(() =>
            ModbusPdu.EnsureBitsResponse(new byte[] { 0x02, 0x00 }, 9, hasByteCountPrefix: true, "TCP"));
        Assert.Equal("Modbus TCP 读位应答数据不足", ex1.Message);

        // TCP：ByteCount=1 < 所需 2 字节 → 报错
        var ex2 = Assert.Throws<IOException>(() =>
            ModbusPdu.EnsureBitsResponse(new byte[] { 0x01, 0x00 }, 9, hasByteCountPrefix: true, "TCP"));
        Assert.Equal("Modbus TCP 读位应答数据不足", ex2.Message);

        // RTU：长度不足即报错
        var ex3 = Assert.Throws<IOException>(() =>
            ModbusPdu.EnsureBitsResponse(new byte[1], 9, hasByteCountPrefix: false, "RTU"));
        Assert.Equal("Modbus RTU 读位应答数据不足", ex3.Message);

        // 通过路径
        ModbusPdu.EnsureBitsResponse(new byte[] { 0x02, 0xFF, 0x01 }, 9, true, "TCP");
        ModbusPdu.EnsureBitsResponse(new byte[] { 0xFF, 0x01 }, 9, false, "RTU");
    }

    // ---------- 解析：与 WireCodec/LSB-first 往返一致 ----------

    [Fact]
    public void 解析字_TCP含ByteCount前缀_RTU不含_结果一致()
    {
        var wireWords = new ushort[] { 0x3412, 0xCDAB }; // ToWire 后的线上值
        var payload = new byte[4];
        for (int i = 0; i < 2; i++)
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2 * i), wireWords[i]);

        var tcpData = new byte[] { 0x04 }.Concat(payload).ToArray(); // 前缀 ByteCount=4
        var resultTcp = new ushort[2];
        ModbusPdu.ParseWords(tcpData, 1, resultTcp, 0, 2);
        Assert.Equal(new ushort[] { 0x1234, 0xABCD }, resultTcp);

        var resultRtu = new ushort[2];
        ModbusPdu.ParseWords(payload, 0, resultRtu, 0, 2);
        Assert.Equal(new ushort[] { 0x1234, 0xABCD }, resultRtu);
    }

    [Fact]
    public void 解析位_LSBfirst_含目标偏移写入()
    {
        var data = new byte[] { 0b00001101, 0x01 }; // 位0,2,3 与 位8
        var result = new bool[11];
        ModbusPdu.ParseBits(data, 0, result, 0, 9);

        Assert.True(result[0]);
        Assert.False(result[1]);
        Assert.True(result[2]);
        Assert.True(result[3]);
        Assert.False(result[7]);
        Assert.True(result[8]);

        // 带 payloadStart / 目标偏移（TCP 分块第二段的场景）：data[1]=0b00000010 → 位1 置位
        var result2 = new bool[10];
        ModbusPdu.ParseBits(new byte[] { 0xFF, 0b00000010 }, 1, result2, 5, 3);
        Assert.False(result2[5]);
        Assert.True(result2[6]);
        Assert.False(result2[7]);
        Assert.False(result2[8]);
    }
}
