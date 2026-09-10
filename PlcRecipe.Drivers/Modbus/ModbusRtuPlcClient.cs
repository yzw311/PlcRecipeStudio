using System.IO.Ports;
using PlcRecipe.Core;
using PlcRecipe.Core.Models;

namespace PlcRecipe.Drivers.Modbus;

/// <summary>
/// Modbus RTU（串口）客户端（自实现）。帧：地址+功能码+数据+CRC16。
/// 仅封装传输层；字/位语义与 ModbusTcpPlcClient 完全一致。
/// </summary>
public sealed class ModbusRtuPlcClient : IPlcClient
{
    private readonly string _portName;
    private readonly int _baudRate;
    private readonly Parity _parity;
    private readonly int _dataBits;
    private readonly StopBits _stopBits;
    private readonly byte _slaveId;
    private SerialPort? _serial;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private volatile bool _disposed;

    public int DeviceId { get; }
    public bool IsConnected => _serial?.IsOpen == true;

    public ModbusRtuPlcClient(int deviceId, PlcDevice device)
    {
        DeviceId = deviceId;
        _portName = device.SerialPortName ?? "COM1";
        _baudRate = device.BaudRate;
        _parity = (device.Parity ?? "None") switch
        {
            "Odd" => Parity.Odd,
            "Even" => Parity.Even,
            _ => Parity.None
        };
        _dataBits = device.DataBits;
        _stopBits = (device.StopBits ?? "One") switch
        {
            "Two" => StopBits.Two,
            _ => StopBits.One
        };
        _slaveId = device.SlaveId;
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsConnected) return Task.CompletedTask;
        _serial = new SerialPort(_portName, _baudRate, _parity, _dataBits, _stopBits)
        {
            ReadTimeout = 2000,
            WriteTimeout = 2000
        };
        _serial.Open();
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        try { _serial?.Close(); } catch { /* 串口可能已异常关闭 */ }
        return Task.CompletedTask;
    }

    private Stream Stream => _serial?.BaseStream ?? throw new InvalidOperationException("Modbus RTU 未连接");

    private async Task<int> ReadAtLeastAsync(byte[] buffer, int count, CancellationToken ct)
    {
        int read = 0;
        while (read < count)
        {
            int n = await Stream.ReadAsync(buffer.AsMemory(read, count - read), ct).ConfigureAwait(false);
            if (n <= 0) throw new IOException("Modbus RTU 串口已断开");
            read += n;
        }
        return read;
    }

    private async Task<byte[]> TransceiveAsync(byte functionCode, byte[] pduData, CancellationToken ct)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var adu = new byte[2 + pduData.Length + 2];
            adu[0] = _slaveId;
            adu[1] = functionCode;
            Array.Copy(pduData, 0, adu, 2, pduData.Length);
            ushort crc = ModbusCrc.Compute(adu, adu.Length - 2);
            adu[^2] = (byte)(crc & 0xFF);
            adu[^1] = (byte)(crc >> 8);
            await Stream.WriteAsync(adu, ct).ConfigureAwait(false);

            var head = new byte[2];
            await ReadAtLeastAsync(head, 2, ct).ConfigureAwait(false);
            if (head[0] != _slaveId) throw new IOException($"Modbus RTU 从站号不匹配：期望 {_slaveId} 收到 {head[0]}");
            if ((head[1] & 0x80) != 0)
            {
                var tail = new byte[3];
                await ReadAtLeastAsync(tail, 3, ct).ConfigureAwait(false);
                VerifyCrc(head, tail);
                throw new PlcCommunicationException($"Modbus RTU 功能码 0x{functionCode:X2} 异常响应", tail[0]);
            }
            byte respFunc = head[1];
            if (respFunc != functionCode) throw new IOException($"Modbus RTU 功能码不匹配：期望 0x{functionCode:X2} 收到 0x{respFunc:X2}");

            byte[] data;
            if (respFunc is 0x01 or 0x02 or 0x03 or 0x04)
            {
                var lenBuf = new byte[1];
                await ReadAtLeastAsync(lenBuf, 1, ct).ConfigureAwait(false);
                int len = lenBuf[0];
                var rest = new byte[len + 2];
                await ReadAtLeastAsync(rest, rest.Length, ct).ConfigureAwait(false);
                var full = new byte[3 + len + 2];
                full[0] = head[0]; full[1] = head[1]; full[2] = lenBuf[0];
                Array.Copy(rest, 0, full, 3, rest.Length);
                VerifyCrc(full, Array.Empty<byte>());
                data = new byte[len];
                Array.Copy(full, 3, data, 0, len);
            }
            else if (respFunc is 0x0F or 0x10)
            {
                var rest = new byte[4 + 2];
                await ReadAtLeastAsync(rest, rest.Length, ct).ConfigureAwait(false);
                var full = new byte[2 + rest.Length];
                full[0] = head[0]; full[1] = head[1];
                Array.Copy(rest, 0, full, 2, rest.Length);
                VerifyCrc(full, Array.Empty<byte>());
                data = Array.Empty<byte>();
            }
            else
            {
                throw new IOException($"Modbus RTU 未知功能码 0x{respFunc:X2}");
            }
            return data;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private void VerifyCrc(byte[] beforeCrc, byte[] tail2)
    {
        // beforeCrc 已含数据；实际 CRC 校验对完整帧进行
        var frame = tail2.Length == 0 ? beforeCrc : Concat(beforeCrc, tail2);
        if (frame.Length < 4) throw new IOException("Modbus RTU 帧过短");
        ushort expect = ModbusCrc.Compute(frame, frame.Length - 2);
        ushort actual = (ushort)(frame[^2] | (frame[^1] << 8));
        if (expect != actual) throw new IOException("Modbus RTU CRC 校验失败");
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var r = new byte[a.Length + b.Length];
        Array.Copy(a, r, a.Length);
        Array.Copy(b, 0, r, a.Length, b.Length);
        return r;
    }

    public async Task<ushort[]> ReadWordsAsync(ParsedAddress start, ushort count, CancellationToken ct = default)
    {
        ModbusPdu.EnsureWordArea(start, write: false);
        var result = new ushort[count];
        int done = 0;
        foreach (var (addr, n) in ModbusPdu.Chunk(start.Offset, count, ModbusPdu.MaxWordsPerRequest))
        {
            var pdu = ModbusPdu.BuildReadRequest(addr, (ushort)n);
            var data = await TransceiveAsync(ModbusPdu.ReadWordsFunction(start.Area), pdu, ct).ConfigureAwait(false);
            ModbusPdu.EnsureWordsResponse(data, payloadStart: 0, n, "RTU"); // Transceive 已剥离 ByteCount
            ModbusPdu.ParseWords(data, 0, result, done, n);
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
            ModbusPdu.EnsureBitsResponse(data, n, hasByteCountPrefix: false, "RTU");
            ModbusPdu.ParseBits(data, 0, result, done, n);
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
        await DisconnectAsync().ConfigureAwait(false);
        _serial?.Dispose();
        _ioLock.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Modbus CRC16（多项式 0xA001）。</summary>
public static class ModbusCrc
{
    public static ushort Compute(byte[] data, int length)
    {
        ushort crc = 0xFFFF;
        for (int i = 0; i < length; i++)
        {
            crc ^= data[i];
            for (int j = 0; j < 8; j++)
            {
                bool lsb = (crc & 1) == 1;
                crc >>= 1;
                if (lsb) crc ^= 0xA001;
            }
        }
        return crc;
    }
}
