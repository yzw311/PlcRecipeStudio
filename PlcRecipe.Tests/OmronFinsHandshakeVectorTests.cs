using System.Buffers.Binary;

namespace PlcRecipe.Tests;

/// <summary>
/// 欧姆龙 FINS/TCP 握手字节偏移验证用例（重构任务4：仅测试用例，不修改产品代码）。
/// 全部 Skip，等待真机抓包报文落地后，按 docs/omron-fins-handshake.md 的决策树激活：
/// 1. 把抓包 hex 填入下方常量（空格/连字符可留）；
/// 2. TC2 的 serverNode 偏移用例：填入 PLC 实际 FINS node 号，仅保留相等的 offset 断言；
/// 3. 将命中用例的 Skip 改为 null。
/// </summary>
public class OmronFinsHandshakeVectorTests
{
    private const string SkipReason = "等待真机抓包报文（见 docs/omron-fins-handshake.md TC-1/TC-2）";

    // ↓↓↓ 真机抓包后填写（十六进制）↓↓↓
    private const string? HandshakeRequestHex = null;   // TC-1：软件发出的握手请求（从 TCP 流起点起，含 FINS 头）
    private const string? HandshakeResponseHex = null;  // TC-2：PLC 返回的握手应答（完整抓取，勿截断）
    private const string? FunctionFrameHex = null;      // TC-3：握手后首个功能帧的 FINS 帧部分（TCP 头之后 8 字节起）
    private const int PlcFinsNode = 0;                  // TC-2：真机 PLC 的 FINS node 号（1~254，CX-Programmer 可查）
    private const int ServerNodeOffset = 8;             // TC-3：抓包裁决后的 serverNode 偏移（现实现为 8）

    private static byte[] Parse(string? hex) =>
        Convert.FromHexString((hex ?? string.Empty).Replace(" ", "").Replace("-", ""));

    // ==================== HSL 13.0.0 参考实现锁定（非 Skip，可执行规格） ====================
    // 以下断言来自 ilspycmd 反编译 HslCommunication.Profinet.Omron.OmronFinsNet / OmronFinsNetHelper
    // （NuGet 13.0.0）得到的权威布局，作为本驱动修复时的"目标规格"固化。
    // 详见 docs/omron-fins-handshake.md §5。

    [Fact]
    public void HSL参考_握手请求为20字节_len12_命令0_客户端node在12到15()
    {
        // 反编译 handSingle = { 70,73,78,83, 0,0,0,12, 0,0,0,0, 0,0,0,0, 0,0,0,0 }
        byte[] handSingle =
        {
            0x46, 0x49, 0x4E, 0x53,
            0x00, 0x00, 0x00, 0x0C,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00
        };
        Assert.Equal(20, handSingle.Length);
        Assert.Equal('F', (char)handSingle[0]);
        Assert.Equal(12, BinaryPrimitives.ReadInt32BigEndian(handSingle.AsSpan(4))); // len = 数据段 12 字节
        Assert.Equal(0, BinaryPrimitives.ReadInt32BigEndian(handSingle.AsSpan(8)));  // command = 节点分配请求
        // clientNode 应写入 [12..15]（4BE），现实现错写 [11] —— 修复目标：node 字段 @12..15
    }

    [Fact]
    public void HSL参考_握手应答24字节布局_错误码12_分配node19_服务端node23()
    {
        // 反编译 InitializationOnConnectAsync：err(4BE)@12..15；len>=20 取 Content[19] 为 SA1；
        // len>=24 取 Content[23] 为 DA1。构造一个 node=37 的规约应答验证偏移口径：
        var resp = new byte[24];
        "FINS"u8.CopyTo(resp);
        BinaryPrimitives.WriteInt32BigEndian(resp.AsSpan(4), 16);
        BinaryPrimitives.WriteInt32BigEndian(resp.AsSpan(8), 1);   // command
        BinaryPrimitives.WriteInt32BigEndian(resp.AsSpan(12), 0);  // 错误码 = 0
        resp[19] = 37;                                             // PLC 分配的 clientNode（4BE 末字节）
        resp[23] = 5;                                              // serverNode（4BE 末字节）

        int err = BinaryPrimitives.ReadInt32BigEndian(resp.AsSpan(12));
        Assert.Equal(0, err);
        Assert.Equal(37, resp[19]);                                // HSL 的 SA1 来源
        Assert.Equal(5, resp[23]);                                 // HSL 的 DA1 来源（serverNode）
    }

    [Fact]
    public void HSL参考_功能帧在FINS头后有8字节命令段_ICF位于16()
    {
        // 反编译 PackCommand：总长 = 26 + cmd.Length；[8..11]=0x00000002；ICF@16..SID@25；指令@26..
        // 现实现 FINS 帧从 @8 开始（缺 8 字节命令段）—— R5 命中，修复目标以本断言为规格。
        int cmdLen = 4; // MRC SRC + 2 字节数据
        var packet = new byte[26 + cmdLen];
        "FINS"u8.CopyTo(packet);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(4), packet.Length - 8);
        packet[11] = 2;            // command = FINS 帧发送
        packet[16] = 0x80;         // ICF
        packet[20] = 5;            // DA1
        packet[23] = 37;           // SA1
        packet[25] = 1;            // SID

        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(packet.AsSpan(8)));
        Assert.Equal(0x80, packet[16]);
        Assert.Equal(5, packet[20]);
        Assert.Equal(37, packet[23]);
        Assert.Equal(1, packet[25]);
        Assert.Equal(packet.Length - 8, BinaryPrimitives.ReadInt32BigEndian(packet.AsSpan(4)));
    }

    // ---------- TC-1：握手请求（裁决 R1 长度字段口径 / R2 clientNode 落点） ----------

    [Fact(Skip = SkipReason)]
    public void TC1_握手请求_FINS魔数位于0到3()
    {
        var req = Parse(HandshakeRequestHex);
        Assert.True(req.Length >= 8, $"请求仅 {req.Length} 字节");
        Assert.Equal((byte)'F', req[0]);
        Assert.Equal((byte)'I', req[1]);
        Assert.Equal((byte)'N', req[2]);
        Assert.Equal((byte)'S', req[3]);
    }

    [Fact(Skip = SkipReason)]
    public void TC1_握手请求_长度字段与header后数据段一致()
    {
        // R1 判定：规约口径下长度字段应等于 header(8B) 之后的字节数。
        // 现实现发送 16 字节、长度字段=16（数据段仅 8 字节）——若本用例失败即 R1 命中。
        var req = Parse(HandshakeRequestHex);
        int len = BinaryPrimitives.ReadInt32BigEndian(req.AsSpan(4));
        Assert.Equal(req.Length - 8, len);
    }

    [Fact(Skip = SkipReason)]
    public void TC1_握手请求_clientNode落点与PLC侧收到的一致()
    {
        // R2 判定：现实现把 clientNode 写在 [11]（[8..11] 大端字段的末字节）。
        // 对照 PLC 侧实际收到的 node 值；若不等，检查 [12..15] 等其他落点。
        var req = Parse(HandshakeRequestHex);
        Assert.Equal(PlcFinsNode, req[11]);
    }

    // ---------- TC-2：握手应答（裁决 R3 serverNode 偏移 / R4 应答长度） ----------

    [Fact(Skip = SkipReason)]
    public void TC2_握手应答_FINS魔数位于0到3()
    {
        var resp = Parse(HandshakeResponseHex);
        Assert.True(resp.Length >= 8, $"应答仅 {resp.Length} 字节——若 <16 说明 R4（长度假设）命中");
        Assert.Equal((byte)'F', resp[0]);
        Assert.Equal((byte)'I', resp[1]);
        Assert.Equal((byte)'N', resp[2]);
        Assert.Equal((byte)'S', resp[3]);
    }

    [Fact(Skip = SkipReason)]
    public void TC2_握手应答_长度字段与header后数据段一致()
    {
        var resp = Parse(HandshakeResponseHex);
        int len = BinaryPrimitives.ReadInt32BigEndian(resp.AsSpan(4));
        Assert.Equal(resp.Length - 8, len);
    }

    [Fact(Skip = SkipReason)]
    public void TC2_握手应答_总长与现实现ReadExact假设一致()
    {
        // R4 判定：现实现按 16 字节读取应答。若实际总长 ≠ 16（如 24），
        // 剩余字节会撕帧并污染下一个应答解析——必须先修长度再谈偏移。
        var resp = Parse(HandshakeResponseHex);
        Assert.Equal(16, resp.Length);
    }

    [Theory(Skip = SkipReason)]
    [InlineData(8)]   // 现实现偏移：resp[8]（cmd(1)+node(1)@8 布局）
    [InlineData(11)]  // 候选：cmd(4)+node(4)@8..11 布局的大端末字节
    [InlineData(15)]  // 候选：node 位于 [12..15] 的末字节
    public void TC2_serverNode偏移判定_与PLC实际node一致(int offset)
    {
        // 激活后仅保留与 PlcFinsNode 相等的 offset（其余删除/改 Skip）——唯一存留者即正确偏移
        var resp = Parse(HandshakeResponseHex);
        Assert.Equal(PlcFinsNode, resp[offset]);
    }

    // ---------- TC-3：功能帧 DA1/SA1 反向验证 ----------

    [Fact(Skip = SkipReason)]
    public void TC3_功能帧_DA1等于握手解析出的serverNode()
    {
        // 功能帧布局：ICF RSV GCT DNA DA1 SA1 SID ...
        // frame[4]=DA1（=握手应答解析出的 serverNode）；frame[6]=SA1（=clientNode）
        var resp = Parse(HandshakeResponseHex);
        var frame = Parse(FunctionFrameHex);
        Assert.True(frame.Length >= 8, $"功能帧仅 {frame.Length} 字节");
        Assert.Equal(resp[ServerNodeOffset], frame[4]);
    }

    [Fact(Skip = SkipReason)]
    public void TC3_功能帧_SA1等于握手请求携带的clientNode()
    {
        var req = Parse(HandshakeRequestHex);
        var frame = Parse(FunctionFrameHex);
        Assert.Equal(req[11], frame[6]); // 与 TC-1 裁决的 clientNode 落点保持同偏移
    }
}
