using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace PlcRecipe.Tests.Simulators;

/// <summary>
/// 测试专用三菱 MC 3E 帧（二进制）从站模拟器。
/// 支持批量读 0x0401 / 批量写 0x1401，字设备 D、位设备 M。
/// </summary>
public sealed class MelsecMc3ESimulator : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptTask;
    public int[] DWords { get; } = new int[10000];  // 字数据（按线上大端数值存储）
    public byte[] MBits { get; } = new byte[10000];
    public int Port { get; }

    public MelsecMc3ESimulator()
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
            var client = await _listener.AcceptTcpClientAsync(ct);
            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using var _c = client;
        var stream = client.GetStream();
        var header = new byte[9];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!await ReadExactAsync(stream, header, ct)) return;
                if (header[0] != 0x50 || header[1] != 0x00) return; // 副头部校验
                int dataLen = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(7));
                var app = new byte[dataLen];
                if (!await ReadExactAsync(stream, app, ct)) return;
                // app: watchdog(2) command(2) subcmd(2) devNo(3) code(1) points(2) [data]
                ushort command = BinaryPrimitives.ReadUInt16LittleEndian(app.AsSpan(2, 2));
                int offset = app[6] | (app[7] << 8) | (app[8] << 16);
                byte deviceCode = app[9];
                ushort points = BinaryPrimitives.ReadUInt16LittleEndian(app.AsSpan(10, 2));
                byte[] data = app.Length > 12 ? app[12..] : Array.Empty<byte>();

                byte[] body;
                if (command == 0x0401) // 读
                {
                    if (deviceCode == 0xA8) // D 字
                    {
                        body = new byte[points * 2];
                        for (int i = 0; i < points; i++)
                        {
                            ushort w = (ushort)DWords[offset + i];
                            body[i * 2] = (byte)(w >> 8);
                            body[i * 2 + 1] = (byte)(w & 0xFF);
                        }
                    }
                    else if (deviceCode == 0x90) // M 位
                    {
                        body = new byte[points];
                        for (int i = 0; i < points; i++)
                            body[i] = MBits[offset + i];
                    }
                    else { body = Array.Empty<byte>(); }
                }
                else if (command == 0x1401) // 写
                {
                    if (deviceCode == 0xA8)
                    {
                        for (int i = 0; i < points; i++)
                            DWords[offset + i] = (data[2 * i] << 8) | data[2 * i + 1];
                    }
                    else if (deviceCode == 0x90)
                    {
                        for (int i = 0; i < points; i++)
                            MBits[offset + i] = data[i];
                    }
                    body = Array.Empty<byte>();
                }
                else
                {
                    body = Array.Empty<byte>();
                }

                // 应答：D0 00 | 00 | FF | FF 03 00 | len(2LE) | endCode(2LE) | data
                var resp = new byte[9 + 2 + body.Length];
                resp[0] = 0xD0; resp[1] = 0x00;
                resp[2] = 0x00; resp[3] = 0xFF;
                resp[4] = 0xFF; resp[5] = 0x03; resp[6] = 0x00;
                BinaryPrimitives.WriteUInt16LittleEndian(resp.AsSpan(7), (ushort)(2 + body.Length));
                // endCode=0
                Array.Copy(body, 0, resp, 11, body.Length);
                await stream.WriteAsync(resp, ct);
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
            int n = await stream.ReadAsync(buffer.AsMemory(read), ct);
            if (n <= 0) return false;
            read += n;
        }
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        try { if (_acceptTask != null) await _acceptTask; } catch { /* 忽略 */ }
        _cts.Dispose();
    }
}
