using System.Buffers.Binary;
using PlcRecipe.Core;

namespace PlcRecipe.Drivers.Mitsubishi;

/// <summary>
/// 三菱 MC 协议（QnA 兼容 3E 帧，二进制）客户端，适用于 Q 系列 / FX5U / iQ-R 以太网模块。
/// PLC 侧需开通 MC 协议通讯（二进制通讯、TCP 端口默认 5000）。
/// 支持字设备 D/W/R/ZR 与位设备 M/B/X/Y（W/B/X/Y 为十六进制编号）。
/// </summary>
public sealed class MelsecMc3EClient : TcpPlcClientBase
{
    public MelsecMc3EClient(int deviceId, string host, int port) : base(deviceId, host, port == 0 ? 6000 : port) { }

    // ---- 设备代码（3E 二进制）----
    private static byte DeviceCode(string area, out bool isWord) => area switch
    {
        "D" => Word(0xA8, out isWord),
        "W" => Word(0xB4, out isWord),
        "R" => Word(0xAF, out isWord),
        "ZR" => Word(0xB0, out isWord),
        "M" => Bit(0x90, out isWord),
        "B" => Bit(0xA0, out isWord),
        "X" => Bit(0x9C, out isWord),
        "Y" => Bit(0x9D, out isWord),
        _ => throw new PlcAddressException(area, $"三菱 MC 不支持数据区 {area}")
    };

    private static byte Word(byte code, out bool isWord) { isWord = true; return code; }
    private static byte Bit(byte code, out bool isWord) { isWord = false; return code; }

    /// <summary>3E 帧单次最大点数（保守取值，客户端内部分块）。</summary>
    private const int MaxWordsPerRequest = 480;
    private const int MaxBitsPerRequest = 720;

    // ---- 请求帧 ----
    // 50 00 | 00 | FF | FF 03 | 00 | dataLen(2LE) | 10 00 | command(2LE) | subcmd(2) | devNo(3LE) | code | points(2LE) [| data]
    private static byte[] BuildRequest(ushort command, ParsedAddress addr, ushort points, ushort[]? writeData, bool[]? writeBits)
    {
        byte code = DeviceCode(addr.Area, out _);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        bw.Write((byte)0x50); bw.Write((byte)0x00);          // 副头部
        bw.Write((byte)0x00);                                 // 网络号
        bw.Write((byte)0xFF);                                 // PC 号
        bw.Write((byte)0xFF); bw.Write((byte)0x03);           // 请求目标模块 I/O
        bw.Write((byte)0x00);                                 // 请求目标模块站号
        ushort appLen = (ushort)(2 /*watchdog*/ + 4 /*cmd+subcmd*/ + 4 /*dev*/ + 2 /*points*/ + (writeData?.Length * 2 ?? writeBits?.Length ?? 0));
        bw.Write(appLen);                                     // 请求数据长度（含看门狗）
        bw.Write((ushort)0x0010);                             // CPU 看门狗定时器
        bw.Write(command);                                    // 命令 0x0401/0x1401
        bw.Write((ushort)0x0000);                             // 子命令
        bw.Write((byte)(addr.Offset & 0xFF));                 // 设备号 3 字节 LE
        bw.Write((byte)((addr.Offset >> 8) & 0xFF));
        bw.Write((byte)((addr.Offset >> 16) & 0xFF));
        bw.Write(code);                                       // 设备代码
        bw.Write(points);                                     // 设备点数
        if (writeData != null)
            foreach (var w in writeData)
            {
                var wire = WireCodec.ToWire(w);
                bw.Write((byte)(wire >> 8)); bw.Write((byte)(wire & 0xFF)); // 字数据大端
            }
        if (writeBits != null)
            foreach (var b in writeBits)
                bw.Write(b ? (byte)0x01 : (byte)0x00);       // 位数据每点 1 字节
        return ms.ToArray();
    }

    // ---- 应答帧 ----
    // D0 00 | 00 | FF | FF 03 00 | respLen(2LE) | endCode(2LE) | data...
    private async Task<byte[]> ExchangeAsync(byte[] request, CancellationToken ct)
    {
        await WriteAllAsync(request, ct).ConfigureAwait(false);
        var header = await ReadExactAsync(9, ct).ConfigureAwait(false);
        if (header[0] != 0xD0 && header[0] != 0x80)
            throw new IOException($"三菱 MC 应答副头部异常：0x{header[0]:X2} {header[1]:X2}");
        ushort respLen = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(7, 2));
        if (respLen < 2)
            throw new IOException($"三菱 MC 应答长度异常：{respLen}");
        var rest = await ReadExactAsync(respLen, ct).ConfigureAwait(false);
        ushort endCode = BinaryPrimitives.ReadUInt16LittleEndian(rest.AsSpan(0, 2));
        if (endCode != 0)
            throw new PlcCommunicationException($"三菱 MC 应答错误码 0x{endCode:X4}", endCode);
        var data = new byte[respLen - 2];
        Array.Copy(rest, 2, data, 0, data.Length);
        return data;
    }

    public override async Task<ushort[]> ReadWordsAsync(ParsedAddress start, ushort count, CancellationToken ct = default)
    {
        if (start.IsBitDevice) throw new PlcAddressException(start.Raw, "位设备请用 ReadBitsAsync");
        var result = new ushort[count];
        int done = 0;
        while (done < count)
        {
            int n = Math.Min(count - done, MaxWordsPerRequest);
            var req = BuildRequest(0x0401, Advance(start, done), (ushort)n, null, null);
            var data = await ExchangeAsync(req, ct).ConfigureAwait(false);
            if (data.Length < n * 2) throw new IOException($"三菱 MC 读应答数据不足：{data.Length}/{n * 2}");
            for (int i = 0; i < n; i++)
            {
                ushort wire = (ushort)((data[2 * i] << 8) | data[2 * i + 1]);
                result[done + i] = WireCodec.ToInternal(wire);
            }
            done += n;
        }
        return result;
    }

    public override async Task WriteWordsAsync(ParsedAddress start, ushort[] words, CancellationToken ct = default)
    {
        if (start.IsBitDevice) throw new PlcAddressException(start.Raw, "位设备请用 WriteBitsAsync");
        int done = 0;
        while (done < words.Length)
        {
            int n = Math.Min(words.Length - done, MaxWordsPerRequest);
            var chunk = words[done..(done + n)];
            var req = BuildRequest(0x1401, Advance(start, done), (ushort)n, chunk, null);
            await ExchangeAsync(req, ct).ConfigureAwait(false);
            done += n;
        }
    }

    /// <summary>分块续传：把起点推进 done 个设备号（同一区内）。</summary>
    private static ParsedAddress Advance(ParsedAddress start, int done) =>
        done == 0 ? start : new ParsedAddress(start.Area, start.Offset + done, start.Bit, start.IsBitDevice, start.Raw);

    public override async Task<bool[]> ReadBitsAsync(ParsedAddress start, ushort count, CancellationToken ct = default)
    {
        if (!start.IsBitDevice) throw new PlcAddressException(start.Raw, "字设备请用 ReadWordsAsync");
        var result = new bool[count];
        int done = 0;
        while (done < count)
        {
            int n = Math.Min(count - done, MaxBitsPerRequest);
            var req = BuildRequest(0x0401, Advance(start, done), (ushort)n, null, null);
            var data = await ExchangeAsync(req, ct).ConfigureAwait(false);
            if (data.Length < n) throw new IOException($"三菱 MC 位读应答数据不足：{data.Length}/{n}");
            for (int i = 0; i < n; i++) result[done + i] = data[i] != 0;
            done += n;
        }
        return result;
    }

    public override async Task WriteBitsAsync(ParsedAddress start, bool[] bits, CancellationToken ct = default)
    {
        if (!start.IsBitDevice) throw new PlcAddressException(start.Raw, "字设备请用 WriteWordsAsync");
        int done = 0;
        while (done < bits.Length)
        {
            int n = Math.Min(bits.Length - done, MaxBitsPerRequest);
            var chunk = bits[done..(done + n)];
            var req = BuildRequest(0x1401, Advance(start, done), (ushort)n, null, chunk);
            await ExchangeAsync(req, ct).ConfigureAwait(false);
            done += n;
        }
    }
}
