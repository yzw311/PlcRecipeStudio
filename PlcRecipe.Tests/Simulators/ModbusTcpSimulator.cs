using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace PlcRecipe.Tests.Simulators;

/// <summary>
/// 测试专用 Modbus TCP 从站模拟器：TcpListener + MBAP 解析，
/// 支持功能码 0x01/0x02/0x03/0x04/0x0F/0x10。独立于驱动实现，用于回环校验。
/// </summary>
public sealed class ModbusTcpSimulator : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptTask;
    public ushort[] HoldingRegisters { get; } = new ushort[10000];
    public ushort[] InputRegisters { get; } = new ushort[10000];
    public bool[] Coils { get; } = new bool[10000];
    public bool[] DiscreteInputs { get; } = new bool[10000];
    public int Port { get; }

    public ModbusTcpSimulator()
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
        var header = new byte[7];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!await ReadExactAsync(stream, header, ct)) return;
                int length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
                var pdu = new byte[length - 1];
                if (!await ReadExactAsync(stream, pdu, ct)) return;
                byte unitId = header[6];
                var resp = HandlePdu(pdu);
                var adu = new byte[7 + resp.Length];
                Array.Copy(header, adu, 7);
                BinaryPrimitives.WriteUInt16BigEndian(adu.AsSpan(4), (ushort)(resp.Length + 1));
                Array.Copy(resp, 0, adu, 7, resp.Length);
                await stream.WriteAsync(adu, ct);
            }
        }
        catch
        {
            // 客户端断开
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

    private byte[] HandlePdu(byte[] pdu)
    {
        byte func = pdu[0];
        ushort addr = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(1));
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(3));
        switch (func)
        {
            case 0x03: // 读保持寄存器
            {
                var data = new byte[2 + count * 2];
                data[0] = (byte)(count * 2);
                for (int i = 0; i < count; i++)
                    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(1 + i * 2), HoldingRegisters[addr + i]);
                return Concat([func], data);
            }
            case 0x04: // 读输入寄存器
            {
                var data = new byte[2 + count * 2];
                data[0] = (byte)(count * 2);
                for (int i = 0; i < count; i++)
                    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(1 + i * 2), InputRegisters[addr + i]);
                return Concat([func], data);
            }
            case 0x01: // 读线圈
            {
                int bytes = (count + 7) / 8;
                var data = new byte[1 + bytes];
                data[0] = (byte)bytes;
                for (int i = 0; i < count; i++)
                    if (Coils[addr + i]) data[1 + i / 8] |= (byte)(1 << (i % 8));
                return Concat([func], data);
            }
            case 0x02: // 读离散输入
            {
                int bytes = (count + 7) / 8;
                var data = new byte[1 + bytes];
                data[0] = (byte)bytes;
                for (int i = 0; i < count; i++)
                    if (DiscreteInputs[addr + i]) data[1 + i / 8] |= (byte)(1 << (i % 8));
                return Concat([func], data);
            }
            case 0x10: // 写多个寄存器
            {
                int byteCount = pdu[5];
                for (int i = 0; i < count; i++)
                    HoldingRegisters[addr + i] = BinaryPrimitives.ReadUInt16BigEndian(pdu.AsSpan(6 + i * 2));
                var resp = new byte[5];
                resp[0] = func;
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(1), addr);
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(3), count);
                _ = byteCount;
                return resp;
            }
            case 0x0F: // 写多个线圈
            {
                int byteCount = pdu[5];
                for (int i = 0; i < count; i++)
                    Coils[addr + i] = (pdu[6 + i / 8] >> (i % 8) & 1) == 1;
                var resp = new byte[5];
                resp[0] = func;
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(1), addr);
                BinaryPrimitives.WriteUInt16BigEndian(resp.AsSpan(3), count);
                _ = byteCount;
                return resp;
            }
            default:
                return [(byte)(func | 0x80), 0x01]; // 非法功能
        }
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var r = new byte[a.Length + b.Length];
        Array.Copy(a, r, a.Length);
        Array.Copy(b, 0, r, a.Length, b.Length);
        return r;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        try { if (_acceptTask != null) await _acceptTask; } catch { /* 忽略 */ }
        _cts.Dispose();
    }
}
