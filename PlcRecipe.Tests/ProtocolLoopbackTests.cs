using PlcRecipe.Core;
using PlcRecipe.Core.Models;
using PlcRecipe.Drivers;
using PlcRecipe.Drivers.Modbus;
using PlcRecipe.Drivers.Mitsubishi;
using PlcRecipe.Drivers.Omron;
using PlcRecipe.Tests.Simulators;

namespace PlcRecipe.Tests;

/// <summary>真实 socket 回环测试：驱动 ↔ 独立实现的协议模拟器。</summary>
public class ProtocolLoopbackTests
{
    // ---------- Modbus TCP ----------
    [Fact]
    public async Task ModbusTcp_字读写往返()
    {
        await using var sim = new ModbusTcpSimulator();
        var device = new PlcRecipe.Core.Models.PlcDevice { Id = 1, Name = "T", Ip = "127.0.0.1", Port = sim.Port, SlaveId = 1 };
        var client = new ModbusTcpPlcClient(1, device);
        await client.ConnectAsync().ConfigureAwait(false);

        var v16 = new RecipeItem { Name = "x", DataType = PlcDataType.UInt16 };
        var write = new List<ushort>();
        write.AddRange(ValueCodec.Encode(v16, "1000"));
        write.AddRange(ValueCodec.Encode(v16, "65535"));
        write.AddRange(ValueCodec.Encode(v16, "123"));

        await client.WriteWordsAsync(new ParsedAddress("HR", 0, -1, false, "HR40001"), write.ToArray()).ConfigureAwait(false);
        Assert.Equal(1000u, sim.HoldingRegisters[0]);
        Assert.Equal(65535u, sim.HoldingRegisters[1]);
        Assert.Equal(123u, sim.HoldingRegisters[2]);

        var read = await client.ReadWordsAsync(new ParsedAddress("HR", 0, -1, false, "HR40001"), 3).ConfigureAwait(false);
        Assert.Equal(write[0], read[0]);
        Assert.Equal(write[1], read[1]);
        Assert.Equal(write[2], read[2]);
        Assert.Equal("1000", ValueCodec.Decode(v16, read[0..1]));

        await client.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task ModbusTcp_位读写往返()
    {
        await using var sim = new ModbusTcpSimulator();
        var device = new PlcRecipe.Core.Models.PlcDevice { Id = 1, Name = "T", Ip = "127.0.0.1", Port = sim.Port, SlaveId = 1 };
        var client = new ModbusTcpPlcClient(1, device);
        await client.ConnectAsync().ConfigureAwait(false);

        await client.WriteBitsAsync(new ParsedAddress("C", 0, -1, true, "C00001"), [true, false, true, true]).ConfigureAwait(false);
        var bits = await client.ReadBitsAsync(new ParsedAddress("C", 0, -1, true, "C00001"), 4).ConfigureAwait(false);
        Assert.Equal(new[] { true, false, true, true }, bits);
        Assert.True(sim.Coils[0]);
        Assert.False(sim.Coils[1]);

        await client.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task ModbusTcp_异常响应_抛通讯异常()
    {
        await using var sim = new ModbusTcpSimulator();
        var device = new PlcRecipe.Core.Models.PlcDevice { Id = 1, Name = "T", Ip = "127.0.0.1", Port = sim.Port, SlaveId = 1 };
        var client = new ModbusTcpPlcClient(1, device);
        await client.ConnectAsync().ConfigureAwait(false);
        // 功能码 0x10 写 IR 只读区 → 驱动层拦截
        await Assert.ThrowsAsync<PlcAddressException>(() =>
            client.WriteWordsAsync(new ParsedAddress("IR", 0, -1, false, "IR30001"), [1])).ConfigureAwait(false);
        await client.DisposeAsync().ConfigureAwait(false);
    }

    // ---------- 三菱 MC 3E ----------
    [Fact]
    public async Task MelsecMc3E_字读写往返()
    {
        await using var sim = new MelsecMc3ESimulator();
        var client = new MelsecMc3EClient(1, "127.0.0.1", sim.Port);
        await client.ConnectAsync().ConfigureAwait(false);

        var v16 = new RecipeItem { Name = "x", DataType = PlcDataType.UInt16 };
        var w2 = new List<ushort>();
        w2.AddRange(ValueCodec.Encode(v16, "1234"));
        w2.AddRange(ValueCodec.Encode(v16, "5678"));

        await client.WriteWordsAsync(new ParsedAddress("D", 100, -1, false, "D100"), w2.ToArray()).ConfigureAwait(false);
        Assert.Equal(1234, sim.DWords[100]);
        Assert.Equal(5678, sim.DWords[101]);

        var read = await client.ReadWordsAsync(new ParsedAddress("D", 100, -1, false, "D100"), 2).ConfigureAwait(false);
        Assert.Equal(w2[0], read[0]);
        Assert.Equal(w2[1], read[1]);
        Assert.Equal("1234", ValueCodec.Decode(v16, read[0..1]));

        await client.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task MelsecMc3E_位读写往返()
    {
        await using var sim = new MelsecMc3ESimulator();
        var client = new MelsecMc3EClient(1, "127.0.0.1", sim.Port);
        await client.ConnectAsync().ConfigureAwait(false);

        await client.WriteBitsAsync(new ParsedAddress("M", 10, -1, true, "M10"), [true, false, true]).ConfigureAwait(false);
        var bits = await client.ReadBitsAsync(new ParsedAddress("M", 10, -1, true, "M10"), 3).ConfigureAwait(false);
        Assert.Equal(new[] { true, false, true }, bits);
        Assert.Equal(1, sim.MBits[10]);
        Assert.Equal(0, sim.MBits[11]);

        await client.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task MelsecMc3E_超过单帧上限_分块续传不重叠()
    {
        // 600 字 > MaxWordsPerRequest(480)：第二块必须从 start+480 续传，否则重复读写同一段。
        // 内部字约定：数值 V 的内部字 = WireCodec.ToWire(V)（与 ValueCodec.Encode 一致）。
        await using var sim = new MelsecMc3ESimulator();
        var client = new MelsecMc3EClient(1, "127.0.0.1", sim.Port);
        await client.ConnectAsync().ConfigureAwait(false);

        const int count = 600, start = 100;
        var words = new ushort[count];
        for (int i = 0; i < count; i++) words[i] = WireCodec.ToWire((ushort)(1000 + i));

        await client.WriteWordsAsync(new ParsedAddress("D", start, -1, false, $"D{start}"), words).ConfigureAwait(false);
        // 直接核对模拟器内存：第 1 块落 start..479，第 2 块落 start+480..599（客户端纯回读无法暴露错位）
        for (int i = 0; i < count; i++)
            Assert.Equal(1000 + i, sim.DWords[start + i]);
        Assert.Equal(0, sim.DWords[start + count]); // 块外未被触碰

        var read = await client.ReadWordsAsync(new ParsedAddress("D", start, -1, false, $"D{start}"), (ushort)count).ConfigureAwait(false);
        for (int i = 0; i < count; i++)
            Assert.Equal(words[i], read[i]);

        await client.DisposeAsync().ConfigureAwait(false);
    }

    // ---------- 欧姆龙 FINS ----------
    [Fact]
    public async Task OmronFins_字读写往返()
    {
        await using var sim = new OmronFinsSimulator();
        var client = new OmronFinsTcpClient(1, "127.0.0.1", sim.Port);
        await client.ConnectAsync().ConfigureAwait(false);

        var v16 = new RecipeItem { Name = "x", DataType = PlcDataType.UInt16 };
        var w3 = new List<ushort>();
        w3.AddRange(ValueCodec.Encode(v16, "4321"));
        w3.AddRange(ValueCodec.Encode(v16, "8765"));

        await client.WriteWordsAsync(new ParsedAddress("DM", 100, -1, false, "D100"), w3.ToArray()).ConfigureAwait(false);
        Assert.Equal(4321u, sim.DmWords[100]);
        Assert.Equal(8765u, sim.DmWords[101]);

        var read = await client.ReadWordsAsync(new ParsedAddress("DM", 100, -1, false, "D100"), 2).ConfigureAwait(false);
        Assert.Equal(w3[0], read[0]);
        Assert.Equal(w3[1], read[1]);
        Assert.Equal("4321", ValueCodec.Decode(v16, read[0..1]));

        await client.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task OmronFins_位读写往返()
    {
        await using var sim = new OmronFinsSimulator();
        var client = new OmronFinsTcpClient(1, "127.0.0.1", sim.Port);
        await client.ConnectAsync().ConfigureAwait(false);

        await client.WriteBitsAsync(new ParsedAddress("DM", 50, 3, true, "D50.3"), [true]).ConfigureAwait(false);
        var bits = await client.ReadBitsAsync(new ParsedAddress("DM", 50, 3, true, "D50.3"), 1).ConfigureAwait(false);
        Assert.Equal(new[] { true }, bits);

        await client.DisposeAsync().ConfigureAwait(false);
    }
}
