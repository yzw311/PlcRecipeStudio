using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using PlcRecipe.Core;

namespace PlcRecipe.Drivers.Omron;

/// <summary>
/// 欧姆龙 FINS/TCP 客户端（端口默认 9600）。支持 CIO/W/H/A/D 区（位用 .bit 后缀）。
/// 帧布局与 HslCommunication 13.0.0 / FINS/TCP 规约对齐（2026-09 按 docs/omron-fins-handshake.md §5 重写）：
/// 握手 = FINS(4)+len(4)=12+cmd(4)=0+clientNode(4)；功能帧 = FINS(4)+len(4)+cmd(4)=2+保留(4)+FINS帧；
/// 应答数据段 = cmd(4)+err(4)+FINS帧，结束码在 FINS 帧 MRC/SRC 之后。
/// </summary>
public sealed class OmronFinsTcpClient : TcpPlcClientBase
{
    private byte _clientNode = 1;
    private byte _serverNode = 1;
    private byte _sid;

    public OmronFinsTcpClient(int deviceId, string host, int port) : base(deviceId, host, port == 0 ? 9600 : port) { }

    private const int HandshakeTimeoutMs = 5000;
    protected override int ConnectTimeoutMs => HandshakeTimeoutMs;

    // ---- 连接握手（20 字节）：FINS | len=12 | cmd=0 | clientNode(4BE) ----
    protected override async Task OnConnectedAsync(CancellationToken ct)
    {
        _clientNode = ResolveLocalNode();
        var hs = new byte[20];
        hs[0] = (byte)'F'; hs[1] = (byte)'I'; hs[2] = (byte)'N'; hs[3] = (byte)'S';
        BinaryPrimitives.WriteInt32BigEndian(hs.AsSpan(4), 12);           // len = header 后数据段
        // [8..11] command = 0（节点分配请求）
        BinaryPrimitives.WriteInt32BigEndian(hs.AsSpan(12), _clientNode); // clientNode(4BE)
        await WriteAllAsync(hs, ct).ConfigureAwait(false);

        var header = await ReadExactAsync(8, ct).ConfigureAwait(false);
        if (header[0] != (byte)'F' || header[1] != (byte)'I' || header[2] != (byte)'N' || header[3] != (byte)'S')
            throw new IOException("欧姆龙 FINS 握手应答异常（缺少 FINS 头）");
        int len = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4));
        if (len < 0 || len > 4096)
            throw new IOException($"欧姆龙 FINS 握手应答长度异常：{len}");
        var rest = await ReadExactAsync(len, ct).ConfigureAwait(false);   // 数据段
        if (len == 2)
        {
            int tcpErr = (rest[0] << 8) | rest[1];
            throw new PlcCommunicationException("欧姆龙 FINS/TCP 错误帧", tcpErr);
        }
        if (len < 8)
            throw new IOException($"欧姆龙 FINS 握手应答数据段过短：{len}");

        // 数据段：cmd(4)@[0..3]、错误码(4BE)@[4..7]、PLC 分配 clientNode@[8..11]、serverNode@[12..15]
        // （对应 HSL 完整帧口径：err=Content[12..15]、SA1=Content[19]、DA1=Content[23]）
        int errCode = BinaryPrimitives.ReadInt32BigEndian(rest.AsSpan(4));
        if (errCode != 0)
            throw new PlcCommunicationException("欧姆龙 FINS/TCP 握手错误码", errCode);
        if (len >= 12) _clientNode = rest[11]; // PLC 分配的节点
        if (len >= 16) _serverNode = rest[15];
        _sid = 0;
    }

    private byte ResolveLocalNode()
    {
        try
        {
            var local = ((IPEndPoint?)Tcp?.Client?.LocalEndPoint)?.Address;
            var bytes = local?.GetAddressBytes();
            if (bytes != null && bytes.Length == 4) return bytes[3];
        }
        catch { /* 忽略，用默认节点号 */ }
        return 1;
    }

    private static byte AreaCode(string area) => area switch
    {
        "DM" or "D" => 0x82,
        "CIO" => 0xB0,
        "W" => 0xB1,
        "H" => 0xB2,
        "A" => 0xB3,
        _ => throw new PlcAddressException(area, $"FINS 不支持数据区 {area}")
    };

    // ---- FINS 帧：ICF RSV GCT DNA DA1 SNA SA1 SID | MRC SRC | data ----
    private byte[] BuildFinsFrame(ushort command, byte[] data)
    {
        var frame = new byte[10 + data.Length];
        frame[0] = 0x80;          // ICF：请求带应答
        frame[1] = 0x00;          // RSV
        frame[2] = 0x02;          // GCT
        frame[3] = 0x00;          // DNA
        frame[4] = _serverNode;   // DA1
        frame[5] = 0x00;          // SNA
        frame[6] = _clientNode;   // SA1
        _sid = _sid == 255 ? (byte)1 : (byte)(_sid + 1);
        frame[7] = _sid;          // SID
        frame[8] = (byte)(command >> 8);
        frame[9] = (byte)(command & 0xFF);
        Array.Copy(data, 0, frame, 10, data.Length);
        return frame;
    }

    private async Task<byte[]> ExchangeAsync(byte[] finsData, ushort command, CancellationToken ct)
    {
        var frame = BuildFinsFrame(command, finsData);
        // 功能帧（规约/HSL 布局）：FINS(4) + len(4) + cmd(4)=0x00000002 + 保留(4) + FINS 帧（ICF@16 起）
        var packet = new byte[16 + frame.Length];
        packet[0] = (byte)'F'; packet[1] = (byte)'I'; packet[2] = (byte)'N'; packet[3] = (byte)'S';
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(4), packet.Length - 8);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(8), 2);          // command = FINS 帧发送
        // [12..15] 保留 = 0
        Array.Copy(frame, 0, packet, 16, frame.Length);
        await WriteAllAsync(packet, ct).ConfigureAwait(false);

        var header = await ReadExactAsync(8, ct).ConfigureAwait(false);
        if (header[0] != (byte)'F' || header[1] != (byte)'I' || header[2] != (byte)'N' || header[3] != (byte)'S')
            throw new IOException("欧姆龙 FINS 应答缺少 FINS 头");
        int len = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4));
        if (len < 0 || len > 4096) throw new IOException($"欧姆龙 FINS 应答长度异常：{len}");
        var rest = await ReadExactAsync(len, ct).ConfigureAwait(false);     // 数据段
        if (len == 2)
        {
            int tcpErr = (rest[0] << 8) | rest[1];
            throw new PlcCommunicationException($"欧姆龙 FINS/TCP 错误帧", tcpErr);
        }
        // 数据段：cmd(4)@[0..3] + err(4BE)@[4..7] + FINS 帧@[8..]（ICF@8、SID@17、MRC/SRC@18..19、结束码@20..21、数据@22..）
        // （对应 HSL 完整帧口径：err=Content[12..15]、结束码=Content[28..29]、数据从 Content[30] 起）
        if (len < 22) throw new IOException($"欧姆龙 FINS 应答过短：{len}");
        int errCode = BinaryPrimitives.ReadInt32BigEndian(rest.AsSpan(4));
        if (errCode != 0)
            throw new PlcCommunicationException("欧姆龙 FINS 应答错误码", errCode);
        ushort endCode = (ushort)((rest[20] << 8) | rest[21]);
        if (endCode != 0)
            throw new PlcCommunicationException($"欧姆龙 FINS 结束码 0x{endCode:X4}", endCode);
        var data = new byte[len - 22];
        Array.Copy(rest, 22, data, 0, data.Length);
        return data;
    }

    private static void BuildAddress(ParsedAddress addr, out byte areaCode, out ushort word, out byte bit)
    {
        areaCode = AreaCode(addr.Area);
        word = (ushort)Math.Clamp(addr.Offset, 0, ushort.MaxValue);
        bit = (byte)(addr.Bit < 0 ? 0 : addr.Bit);
    }

    public override async Task<ushort[]> ReadWordsAsync(ParsedAddress start, ushort count, CancellationToken ct = default)
    {
        if (start.IsBitDevice) throw new PlcAddressException(start.Raw, "位地址请使用 ReadBitsAsync");
        BuildAddress(start, out var area, out var word, out var bit);
        var data = new byte[6];
        data[0] = area;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(1), word);
        data[3] = bit;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), count);
        var resp = await ExchangeAsync(data, 0x0101, ct).ConfigureAwait(false);
        if (resp.Length < count * 2) throw new IOException($"FINS 读应答数据不足：{resp.Length}/{count * 2}");
        var result = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            ushort wire = (ushort)((resp[2 * i] << 8) | resp[2 * i + 1]);
            result[i] = WireCodec.ToInternal(wire);
        }
        return result;
    }

    public override async Task WriteWordsAsync(ParsedAddress start, ushort[] words, CancellationToken ct = default)
    {
        if (start.IsBitDevice) throw new PlcAddressException(start.Raw, "位地址请使用 WriteBitsAsync");
        BuildAddress(start, out var area, out var word, out var bit);
        var data = new byte[6 + words.Length * 2];
        data[0] = area;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(1), word);
        data[3] = bit;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), (ushort)words.Length);
        for (int i = 0; i < words.Length; i++)
        {
            ushort wire = WireCodec.ToWire(words[i]);
            data[6 + 2 * i] = (byte)(wire >> 8);
            data[7 + 2 * i] = (byte)(wire & 0xFF);
        }
        await ExchangeAsync(data, 0x0102, ct).ConfigureAwait(false);
    }

    public override async Task<bool[]> ReadBitsAsync(ParsedAddress start, ushort count, CancellationToken ct = default)
    {
        if (!start.IsBitDevice) throw new PlcAddressException(start.Raw, "字地址请使用 ReadWordsAsync");
        BuildAddress(start, out var area, out var word, out var bit);
        var data = new byte[6];
        data[0] = area;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(1), word);
        data[3] = bit;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), count);
        var resp = await ExchangeAsync(data, 0x0101, ct).ConfigureAwait(false);
        if (resp.Length < count) throw new IOException($"FINS 位读应答数据不足：{resp.Length}/{count}");
        var result = new bool[count];
        for (int i = 0; i < count; i++) result[i] = resp[i] != 0;
        return result;
    }

    public override async Task WriteBitsAsync(ParsedAddress start, bool[] bits, CancellationToken ct = default)
    {
        if (!start.IsBitDevice) throw new PlcAddressException(start.Raw, "字地址请使用 WriteWordsAsync");
        BuildAddress(start, out var area, out var word, out var bit);
        var data = new byte[6 + bits.Length];
        data[0] = area;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(1), word);
        data[3] = bit;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), (ushort)bits.Length);
        for (int i = 0; i < bits.Length; i++) data[6 + i] = bits[i] ? (byte)0x01 : (byte)0x00;
        await ExchangeAsync(data, 0x0102, ct).ConfigureAwait(false);
    }
}
