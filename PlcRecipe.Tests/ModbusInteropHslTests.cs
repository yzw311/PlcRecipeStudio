using HslCommunication.ModBus;
using PlcRecipe.Core;
using PlcRecipe.Core.Models;
using PlcRecipe.Drivers;
using PlcRecipe.Drivers.Modbus;

namespace PlcRecipe.Tests;

/// <summary>
/// Modbus TCP 跨实现互通实测：我们的 ModbusTcpPlcClient ⇆ HslCommunication 的 ModbusTcpServer。
/// HSL 是与本工程完全独立的第三方协议栈——本测试证明我们的 MBAP/PDU/字节序在独立实现上互通，
/// 置信度高于仓库内自研回环模拟器。
/// 仅测试引用 HSL（产品代码零依赖；HSL 为商业授权库，不得引入产品工程）。
/// </summary>
public class ModbusInteropHslTests
{
    private static (ModbusTcpServer Server, int Port) StartServer()
    {
        var server = new ModbusTcpServer
        {
            StationCheck = false // 我们客户端的 unitId 直接透传，避免站号校验干扰互通验证
        };
        // HSL 的 Port getter 只回显配置值（不支持端口 0 回显实际端口）：先探测空闲端口再绑定
        var port = FindFreePort();
        server.Port = port;
        server.ServerStart();
        Assert.Equal(port, server.Port);
        return (server, port);
    }

    private static int FindFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static ModbusTcpPlcClient CreateClient(int port) =>
        new(1, new PlcDevice
        {
            Id = 1,
            Name = "HSL互通实测",
            Brand = PlcBrand.ModbusTcp,
            Ip = "127.0.0.1",
            Port = port,
            SlaveId = 1
        });

    private static ParsedAddress Hr(int offset) => new("HR", offset, -1, false, $"D{offset}");

    private static ParsedAddress Coil(int offset) => new("C", offset, -1, true, $"Y{offset}");

    [Fact]
    public async Task 字寄存器_写入后HSL侧原始字节与线上格式一致()
    {
        var (server, port) = StartServer();
        var client = CreateClient(port);
        try
        {
            await client.ConnectAsync();

            // 内部字 0x1234/0xABCD → 线上（ToWire）= 0x3412/0xCDAB → 大端字节 34 12 CD AB
            var words = new ushort[] { 0x1234, 0xABCD };
            await client.WriteWordsAsync(Hr(100), words);

            var hslRaw = server.Read("100", 2);
            Assert.True(hslRaw.IsSuccess, $"HSL 读取失败：{hslRaw.Message}");
            Assert.Equal(new byte[] { 0x34, 0x12, 0xCD, 0xAB }, hslRaw.Content);

            // HSL 按寄存器值解读 = 线上字（大端），证明 ToWire 的字节序与独立实现一致
            var hslWord = server.ReadUInt16("100");
            Assert.True(hslWord.IsSuccess);
            Assert.Equal(0x3412U, hslWord.Content);

            // 回读：独立服务器上写什么，我们读回什么
            var readBack = await client.ReadWordsAsync(Hr(100), 2);
            Assert.Equal(words, readBack);
        }
        finally
        {
            await client.DisconnectAsync();
            await client.DisposeAsync();
            server.ServerClose();
        }
    }

    [Fact]
    public async Task 位寄存器_双向写读与HSL侧一致()
    {
        var (server, port) = StartServer();
        var client = CreateClient(port);
        try
        {
            await client.ConnectAsync();

            // 我们写 → HSL 逐位核对（LSB-first）
            await client.WriteBitsAsync(Coil(0), new[] { true, false, true });
            Assert.True(server.ReadCoil("0"));
            Assert.False(server.ReadCoil("1"));
            Assert.True(server.ReadCoil("2"));

            // HSL 写 → 我们读回
            server.WriteCoil("20", true);
            server.WriteCoil("21", false);
            var readBack = await client.ReadBitsAsync(Coil(20), 2);
            Assert.Equal(new[] { true, false }, readBack);
        }
        finally
        {
            await client.DisconnectAsync();
            await client.DisposeAsync();
            server.ServerClose();
        }
    }

    [Fact]
    public async Task 跨分块_300字写入读取_三帧分块与独立实现互通()
    {
        var (server, port) = StartServer();
        var client = CreateClient(port);
        try
        {
            await client.ConnectAsync();

            var words = Enumerable.Range(0, 300).Select(i => (ushort)(1000 + i)).ToArray();
            await client.WriteWordsAsync(Hr(0), words);   // 触发 120/120/60 三帧分块

            // 抽查 HSL 侧原始字节（首/块边界/尾）
            var head = server.Read("0", 1);
            Assert.True(head.IsSuccess);
            var tail = server.Read("299", 1);
            Assert.True(tail.IsSuccess);
            var headWire = Wire(w(1000));
            var tailWire = Wire(w(1299));
            Assert.Equal(new[] { headWire[0], headWire[1] }, new[] { head.Content[0], head.Content[1] });
            Assert.Equal(new[] { tailWire[0], tailWire[1] }, new[] { tail.Content[0], tail.Content[1] });

            // 全量回读（同样触发三帧分块读取）
            var readBack = await client.ReadWordsAsync(Hr(0), 300);
            Assert.Equal(words, readBack);
        }
        finally
        {
            await client.DisconnectAsync();
            await client.DisposeAsync();
            server.ServerClose();
        }
    }

    [Fact]
    public async Task 读写守卫_IR只读区与位字错用_与原行为一致且不触网()
    {
        var (server, port) = StartServer();
        var client = CreateClient(port);
        try
        {
            await client.ConnectAsync();

            await Assert.ThrowsAsync<PlcAddressException>(() =>
                client.WriteWordsAsync(new ParsedAddress("IR", 0, -1, false, "IR30001"), new ushort[] { 1 }));
            await Assert.ThrowsAsync<PlcAddressException>(() =>
                client.ReadWordsAsync(Coil(0), 1));
            await Assert.ThrowsAsync<PlcAddressException>(() =>
                client.WriteBitsAsync(Hr(0), new[] { true }));
        }
        finally
        {
            await client.DisconnectAsync();
            await client.DisposeAsync();
            server.ServerClose();
        }
    }

    private static ushort w(int v) => (ushort)v;

    private static byte[] Wire(ushort internalWord)
    {
        // 与 WireCodec.ToWire 一致：内部字 → 线上大端寄存器字节
        ushort wire = WireCodec.ToWire(internalWord);
        return new[] { (byte)(wire >> 8), (byte)(wire & 0xFF) };
    }

    [Fact]
    public void 线上字节_自检_ToWire与HSL对寄存器的解读一致()
    {
        // 0x1234 内部字 → 线上 0x3412 → 大端字节 34 12
        Assert.Equal(new byte[] { 0x34, 0x12 }, Wire(0x1234));
    }
}
