using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace PlcRecipe.Tests.Simulators;

/// <summary>
/// 测试专用欧姆龙 FINS/TCP 从站模拟器：节点握手 + 0x0101 读 / 0x0102 写（DM 区）。
/// </summary>
public sealed class OmronFinsSimulator : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptTask;
    public ushort[] DmWords { get; } = new ushort[10000]; // 按线上数值存储
    public int Port { get; }

    public OmronFinsSimulator()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptTask = AcceptLoopAsync(_cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using var _c = client;
        var stream = client.GetStream();

        // 节点握手：请求 20 字节 = FINS + len=12 + cmd(4)=0 + clientNode(4BE)@[12..15]
        var hs = new byte[20];
        if (!await ReadExactAsync(stream, hs, ct).ConfigureAwait(false)) return;
        if (hs[0] != (byte)'F' || hs[1] != (byte)'I' || hs[2] != (byte)'N' || hs[3] != (byte)'S') return;
        if (hs[11] != 0) return; // cmd 字段必须为 0（节点分配请求）
        byte assignedClientNode = hs[15]; // 采纳请求携带的 node（4BE 末字节）

        // 应答 24 字节 = FINS + len=16 + cmd echo + err=0 + clientNode(4) + serverNode(4)
        var hsResp = new byte[24];
        hsResp[0] = (byte)'F'; hsResp[1] = (byte)'I'; hsResp[2] = (byte)'N'; hsResp[3] = (byte)'S';
        BinaryPrimitives.WriteInt32BigEndian(hsResp.AsSpan(4), 16);
        BinaryPrimitives.WriteInt32BigEndian(hsResp.AsSpan(8), 0);        // command echo
        BinaryPrimitives.WriteInt32BigEndian(hsResp.AsSpan(12), 0);       // 错误码 = 0
        hsResp[19] = assignedClientNode;                                  // 分配的 client node（4BE 末字节）
        hsResp[23] = 10;                                                  // 服务器节点号（4BE 末字节）
        await stream.WriteAsync(hsResp, ct).ConfigureAwait(false);

        var header = new byte[8];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!await ReadExactAsync(stream, header, ct).ConfigureAwait(false)) return;
                if (header[0] != (byte)'F' || header[1] != (byte)'I' || header[2] != (byte)'N' || header[3] != (byte)'S') return;
                int len = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4));
                var seg = new byte[len];
                if (!await ReadExactAsync(stream, seg, ct).ConfigureAwait(false)) return;
                // 数据段：cmd(4)@0 + 保留(4)@4 + FINS 帧@8（ICF@8 ... SID@17、MRC SRC@18..19、指令@20..）
                if (len < 20) return; // 最短：cmd4+res4+FINS头10+MRC2（空指令体）
                if (BinaryPrimitives.ReadInt32BigEndian(seg.AsSpan(0)) != 2) return; // 仅支持 FINS 帧发送
                var frame = seg[8..];

                // frame: ICF RSV GCT DNA DA1 SNA SA1 SID | MRC SRC | data
                ushort command = (ushort)((frame[8] << 8) | frame[9]);
                byte[] body = frame.Length > 10 ? frame[10..] : Array.Empty<byte>();
                byte[] data;

                if (command == 0x0101) // 读
                {
                    byte area = body[0];
                    ushort word = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(1));
                    byte bit = body[3];
                    ushort count = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(4));
                    if (bit == 0)
                    {
                        data = new byte[count * 2];
                        for (int i = 0; i < count; i++)
                            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(i * 2), DmWords[word + i]);
                    }
                    else
                    {
                        // 位读：每点 1 字节 0/1
                        data = new byte[count];
                        for (int i = 0; i < count; i++)
                        {
                            int w = word + (bit + i) / 16, b = (bit + i) % 16;
                            data[i] = (DmWords[w] >> b & 1) == 1 ? (byte)1 : (byte)0;
                        }
                    }
                }
                else if (command == 0x0102) // 写
                {
                    byte area = body[0];
                    ushort word = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(1));
                    byte bit = body[3];
                    ushort count = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(4));
                    int payload = body.Length - 6;
                    if (bit == 0)
                    {
                        for (int i = 0; i < count; i++)
                            DmWords[word + i] = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(6 + i * 2));
                    }
                    else
                    {
                        // 位写：每点 1 字节 0/1
                        for (int i = 0; i < count && i < payload; i++)
                        {
                            int w = word + (bit + i) / 16, b = (bit + i) % 16;
                            if (body[6 + i] != 0) DmWords[w] |= (ushort)(1 << b);
                            else DmWords[w] &= (ushort)~(1 << b);
                        }
                    }
                    data = Array.Empty<byte>();
                }
                else
                {
                    data = Array.Empty<byte>();
                }

                // 应答数据段：cmd echo(4) + err=0(4) + FINS 应答帧（标准 10 字节头：ICF..SID | MRC SRC | endCode | data）
                var respFrame = new byte[14 + data.Length];
                respFrame[0] = 0xC0;      // ICF（应答）
                respFrame[2] = 0x02;      // GCT
                respFrame[4] = frame[6];  // DA1 ← 请求 SA1
                respFrame[7] = frame[4];  // SA1 ← 请求 DA1
                respFrame[9] = frame[7];  // SID 回显
                respFrame[10] = frame[8]; // MRC/SRC 回显
                respFrame[11] = frame[9];
                // [12..13] endCode = 0
                Array.Copy(data, 0, respFrame, 14, data.Length);
                var respSeg = new byte[8 + respFrame.Length];
                BinaryPrimitives.WriteInt32BigEndian(respSeg.AsSpan(0), 2);       // command echo
                // [4..7] 错误码 = 0
                Array.Copy(respFrame, 0, respSeg, 8, respFrame.Length);
                var packet = new byte[8 + respSeg.Length];
                packet[0] = (byte)'F'; packet[1] = (byte)'I'; packet[2] = (byte)'N'; packet[3] = (byte)'S';
                BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(4), respSeg.Length);
                Array.Copy(respSeg, 0, packet, 8, respSeg.Length);
                await stream.WriteAsync(packet, ct).ConfigureAwait(false);
            }
        }
        catch
        {
            // 断开
        }
    }

    private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        try { if (_acceptTask != null) await _acceptTask.ConfigureAwait(false); } catch { /* 忽略 */ }
        _cts.Dispose();
    }
}
