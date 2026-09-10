using System.Buffers.Binary;
using System.Net.Sockets;
using PlcRecipe.Core;
using PlcRecipe.Core.Models;

namespace PlcRecipe.Drivers.Modbus;

/// <summary>
/// Modbus TCP 客户端（自实现，真异步）。MBAP 封装，
/// 支持功能码 0x01/0x02/0x04/0x03/0x0F/0x10。
/// </summary>
public sealed class ModbusTcpPlcClient : IPlcClient
{
    private readonly string _host;
    private readonly int _port;
    private readonly byte _slaveId;
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private ushort _txId;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private volatile bool _disposed;

    public int DeviceId { get; }

    public ModbusTcpPlcClient(int deviceId, PlcDevice device)
    {
        DeviceId = deviceId;
        _host = device.Ip;
        _port = device.Port == 0 ? 502 : device.Port;
        _slaveId = device.SlaveId;
    }

    public bool IsConnected => _tcp?.Connected == true && _stream != null;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsConnected) return;
        await DisconnectCoreAsync().ConfigureAwait(false);
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(5000);
            await tcp.ConnectAsync(_host, _port, cts.Token).ConfigureAwait(false);
            _tcp = tcp;
            _stream = tcp.GetStream();
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    public Task DisconnectAsync() => DisconnectCoreAsync();

    private Task DisconnectCoreAsync()
    {
        _stream?.Dispose();
        _stream = null;
        _tcp?.Dispose();
        _tcp = null;
        return Task.CompletedTask;
    }

    private Stream Stream => _stream ?? throw new InvalidOperationException("Modbus TCP 未连接");

    private async Task<byte[]> ReadExactAsync(int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = await Stream.ReadAsync(buffer.AsMemory(read, count - read), ct).ConfigureAwait(false);
            if (n <= 0) throw new IOException("Modbus TCP 连接已断开");
            read += n;
        }
        return buffer;
    }

    /// <summary>发送请求并返回 PDU 数据（异常响应抛 PlcCommunicationException）。</summary>
    private async Task<byte[]> TransceiveAsync(byte functionCode, byte[] pduData, CancellationToken ct)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _txId = _txId == ushort.MaxValue ? (ushort)1 : (ushort)(_txId + 1);
            ushort txId = _txId;
            var adu = new byte[7 + 1 + pduData.Length];
            BinaryPrimitives.WriteUInt16BigEndian(adu.AsSpan(0), txId);
            BinaryPrimitives.WriteUInt16BigEndian(adu.AsSpan(2), 0);          // 协议号
            BinaryPrimitives.WriteUInt16BigEndian(adu.AsSpan(4), (ushort)(pduData.Length + 2));
            adu[6] = _slaveId;
            adu[7] = functionCode;
            Array.Copy(pduData, 0, adu, 8, pduData.Length);
            await Stream.WriteAsync(adu, ct).ConfigureAwait(false);

            var mbap = await ReadExactAsync(7, ct).ConfigureAwait(false);
            if (BinaryPrimitives.ReadUInt16BigEndian(mbap.AsSpan(0)) != txId)
                throw new IOException("Modbus TCP 事务号不匹配");
            int length = BinaryPrimitives.ReadUInt16BigEndian(mbap.AsSpan(4));
            if (length < 2 || length > 260) throw new IOException($"Modbus TCP 应答长度异常：{length}");
            var rest = await ReadExactAsync(length - 1, ct).ConfigureAwait(false); // 去掉 unitId
            byte respFunc = rest[0];
            if ((respFunc & 0x80) != 0)
            {
                byte code = rest.Length > 1 ? rest[1] : (byte)0;
                throw new PlcCommunicationException($"Modbus 功能码 0x{functionCode:X2} 异常响应", code);
            }
            if (respFunc != functionCode) throw new IOException($"Modbus 功能码不匹配：期望 0x{functionCode:X2} 收到 0x{respFunc:X2}");
            var data = new byte[rest.Length - 1];
            Array.Copy(rest, 1, data, 0, data.Length);
            return data;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task<ushort[]> ReadWordsAsync(ParsedAddress start, ushort count, CancellationToken ct = default)
    {
        ModbusPdu.EnsureWordArea(start, write: false);
        // IR（输入寄存器）为只读区，读取走 0x04 功能码；写入侧在守卫中拦截
        var result = new ushort[count];
        int done = 0;
        foreach (var (addr, n) in ModbusPdu.Chunk(start.Offset, count, ModbusPdu.MaxWordsPerRequest))
        {
            var pdu = ModbusPdu.BuildReadRequest(addr, (ushort)n);
            var data = await TransceiveAsync(ModbusPdu.ReadWordsFunction(start.Area), pdu, ct).ConfigureAwait(false);
            ModbusPdu.EnsureWordsResponse(data, payloadStart: 1, n, "TCP"); // 应答 PDU 首字节为 ByteCount
            ModbusPdu.ParseWords(data, 1, result, done, n);
            done += n;
        }
        return result;
    }

    public async Task WriteWordsAsync(ParsedAddress start, ushort[] words, CancellationToken ct = default)
    {
        ModbusPdu.EnsureWordArea(start, write: true);
        int done = 0;
        foreach (var (addr, n) in ModbusPdu.Chunk(start.Offset, words.Length, ModbusPdu.MaxWordsPerRequest))
        {
            var pdu = ModbusPdu.BuildWriteWordsRequest(addr, words.AsSpan(done, n));
            await TransceiveAsync(0x10, pdu, ct).ConfigureAwait(false);
            done += n;
        }
    }

    public async Task<bool[]> ReadBitsAsync(ParsedAddress start, ushort count, CancellationToken ct = default)
    {
        ModbusPdu.EnsureBitArea(start, write: false);
        var result = new bool[count];
        int done = 0;
        foreach (var (addr, n) in ModbusPdu.Chunk(start.Offset, count, ModbusPdu.MaxBitsPerRequest))
        {
            var pdu = ModbusPdu.BuildReadRequest(addr, (ushort)n);
            var data = await TransceiveAsync(ModbusPdu.ReadBitsFunction(start.Area), pdu, ct).ConfigureAwait(false);
            ModbusPdu.EnsureBitsResponse(data, n, hasByteCountPrefix: true, "TCP");
            ModbusPdu.ParseBits(data, 1, result, done, n);
            done += n;
        }
        return result;
    }

    public async Task WriteBitsAsync(ParsedAddress start, bool[] bits, CancellationToken ct = default)
    {
        ModbusPdu.EnsureBitArea(start, write: true);
        int done = 0;
        foreach (var (addr, n) in ModbusPdu.Chunk(start.Offset, bits.Length, ModbusPdu.MaxBitsPerRequest))
        {
            var pdu = ModbusPdu.BuildWriteBitsRequest(addr, bits.AsSpan(done, n));
            await TransceiveAsync(0x0F, pdu, ct).ConfigureAwait(false);
            done += n;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await DisconnectCoreAsync().ConfigureAwait(false);
        _ioLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
