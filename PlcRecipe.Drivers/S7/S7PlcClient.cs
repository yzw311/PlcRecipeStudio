using PlcRecipe.Core;
using PlcRecipe.Core.Models;
using S7.Net;

namespace PlcRecipe.Drivers;

/// <summary>西门子 S7 驱动（S7netplus）。支持 DB 区 / M 区 / I / Q 区。</summary>
public sealed class S7PlcClient : IPlcClient
{
    private readonly CpuType _cpuType;
    private readonly short _rack;
    private readonly short _slot;
    private Plc? _plc;

    public int DeviceId { get; }
    public string Host { get; }
    public int Port { get; }
    public bool IsConnected => _plc?.IsConnected == true;

    public S7PlcClient(int deviceId, PlcDevice device)
    {
        DeviceId = deviceId;
        Host = device.Ip;
        Port = device.Port == 0 ? 102 : device.Port;
        _rack = (short)device.Rack;
        _slot = (short)device.Slot;
        _cpuType = (device.S7CpuType ?? "S71200") switch
        {
            "S7200Smart" => CpuType.S7200Smart,
            "S7300" => CpuType.S7300,
            "S7400" => CpuType.S7400,
            "S71500" => CpuType.S71500,
            _ => CpuType.S71200
        };
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _plc = new Plc(_cpuType, Host, Port, _rack, _slot);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(5000);
        await _plc.OpenAsync(cts.Token).ConfigureAwait(false);
    }

    public Task DisconnectAsync()
    {
        _plc?.Close();
        _plc = null;
        return Task.CompletedTask;
    }

    private Plc Plc_ => _plc ?? throw new InvalidOperationException("PLC 未连接");
    private static (DataType type, int db) MapArea(string area) =>
        area.StartsWith("DB", StringComparison.Ordinal)
            ? (DataType.DataBlock, int.Parse(area[2..]))
        : area switch
        {
            "M" => (DataType.Memory, 0),
            "I" => (DataType.Input, 0),
            "Q" => (DataType.Output, 0),
            _ => throw new PlcAddressException(area, $"S7 不支持数据区 {area}")
        };

    public async Task<ushort[]> ReadWordsAsync(ParsedAddress start, ushort count, CancellationToken ct = default)
    {
        if (start.IsBitDevice) throw new PlcAddressException(start.Raw, "位地址请使用 ReadBitsAsync");
        var (type, db) = MapArea(start.Area);
        var raw = await Plc_.ReadBytesAsync(type, db, start.Offset, count * 2, ct).ConfigureAwait(false);
        var result = new ushort[count];
        for (int i = 0; i < count; i++)
            result[i] = WireCodec.ToInternal((ushort)((raw[2 * i] << 8) | raw[2 * i + 1])); // 线上大端字 → 内部字
        return result;
    }

    public async Task WriteWordsAsync(ParsedAddress start, ushort[] words, CancellationToken ct = default)
    {
        if (start.IsBitDevice) throw new PlcAddressException(start.Raw, "位地址请使用 WriteBitsAsync");
        var (type, db) = MapArea(start.Area);
        var raw = new byte[words.Length * 2];
        for (int i = 0; i < words.Length; i++)
        {
            ushort wire = WireCodec.ToWire(words[i]);
            raw[2 * i] = (byte)(wire >> 8);
            raw[2 * i + 1] = (byte)(wire & 0xFF);
        }
        await Plc_.WriteBytesAsync(type, db, start.Offset, raw, ct).ConfigureAwait(false);
    }

    public async Task<bool[]> ReadBitsAsync(ParsedAddress start, ushort count, CancellationToken ct = default)
    {
        if (!start.IsBitDevice) throw new PlcAddressException(start.Raw, "字地址请使用 ReadWordsAsync");
        var (type, db) = MapArea(start.Area);
        var raw = await ReadBitWindowAsync(type, db, start, (ushort)count, ct).ConfigureAwait(false);
        var result = new bool[count];
        for (int i = 0; i < count; i++)
        {
            int bitIndex = start.Bit + i;
            result[i] = (raw[bitIndex / 8] >> (bitIndex % 8) & 1) == 1;
        }
        return result;
    }

    public async Task WriteBitsAsync(ParsedAddress start, bool[] bits, CancellationToken ct = default)
    {
        if (!start.IsBitDevice) throw new PlcAddressException(start.Raw, "字地址请使用 WriteWordsAsync");
        var (type, db) = MapArea(start.Area);
        // 读-改-写：S7 按字节写入会覆盖整字节，先回读窗口再按位修改
        var raw = await ReadBitWindowAsync(type, db, start, (ushort)bits.Length, ct).ConfigureAwait(false);
        for (int i = 0; i < bits.Length; i++)
        {
            int bitIndex = start.Bit + i;
            int byteIdx = bitIndex / 8;
            if (bits[i]) raw[byteIdx] |= (byte)(1 << (bitIndex % 8));
            else raw[byteIdx] &= (byte)~(1 << (bitIndex % 8));
        }
        await Plc_.WriteBytesAsync(type, db, start.Offset, raw, ct).ConfigureAwait(false);
    }

    /// <summary>读取覆盖 [Bit, Bit+count) 的字节窗口。</summary>
    private Task<byte[]> ReadBitWindowAsync(DataType type, int db, ParsedAddress start, ushort count, CancellationToken ct)
    {
        int bytes = (start.Bit + count + 7) / 8;
        return Plc_.ReadBytesAsync(type, db, start.Offset, bytes, ct);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
