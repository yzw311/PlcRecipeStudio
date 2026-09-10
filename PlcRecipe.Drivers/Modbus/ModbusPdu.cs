using System.Buffers.Binary;
using PlcRecipe.Core;

namespace PlcRecipe.Drivers.Modbus;

/// <summary>
/// Modbus 公共 PDU 层：功能区映射、请求 PDU 构建、响应校验与解析、请求分块。
/// TCP（MBAP，读应答 PDU 首字节为 ByteCount）与 RTU（从站号+功能码+CRC16，传输层已剥离 ByteCount）
/// 共用本层；传输封装与链路管理仍在各自客户端。
/// 字数据大端、经 WireCodec 与内部字互转；位数据 LSB-first。
/// </summary>
public static class ModbusPdu
{
    /// <summary>单帧最大寄存器数（Modbus 规约上限内，与原实现一致）。</summary>
    public const int MaxWordsPerRequest = 120;
    /// <summary>单帧最大线圈/离散量数（与原实现一致）。</summary>
    public const int MaxBitsPerRequest = 1800;

    // ---------- 参数区守卫（异常文案与原实现逐字一致） ----------

    /// <summary>字区读写守卫：位地址不可按字访问；IR 为只读输入寄存器区。</summary>
    public static void EnsureWordArea(ParsedAddress start, bool write)
    {
        if (start.IsBitDevice)
            throw new PlcAddressException(start.Raw, write ? "位地址请使用 WriteBitsAsync" : "位地址请使用 ReadBitsAsync");
        if (write && start.Area == "IR")
            throw new PlcAddressException(start.Raw, "IR（输入寄存器）为只读区，不可写");
    }

    /// <summary>位区读写守卫：字地址不可按位访问；DI 为只读离散输入区。</summary>
    public static void EnsureBitArea(ParsedAddress start, bool write)
    {
        if (!start.IsBitDevice)
            throw new PlcAddressException(start.Raw, write ? "字地址请使用 WriteWordsAsync" : "字地址请使用 ReadWordsAsync");
        if (write && start.Area == "DI")
            throw new PlcAddressException(start.Raw, "DI（离散输入）为只读区，不可写");
    }

    // ---------- 功能码映射 ----------

    /// <summary>读寄存器：IR（输入寄存器）走 0x04，其余（保持寄存器）走 0x03。</summary>
    public static byte ReadWordsFunction(string area) => (byte)(area == "IR" ? 0x04 : 0x03);

    /// <summary>读位：DI（离散输入）走 0x02，其余（线圈）走 0x01。</summary>
    public static byte ReadBitsFunction(string area) => (byte)(area == "DI" ? 0x02 : 0x01);

    // ---------- 请求 PDU 构建 ----------

    /// <summary>读请求 PDU：起始地址(2BE) + 数量(2BE)。</summary>
    public static byte[] BuildReadRequest(ushort address, ushort count)
    {
        var pdu = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(pdu, address);
        BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(2), count);
        return pdu;
    }

    /// <summary>写寄存器请求 PDU（0x10）：地址(2BE) + 数量(2BE) + 字节数(1) + 字数据（大端，经 ToWire）。</summary>
    public static byte[] BuildWriteWordsRequest(ushort address, ReadOnlySpan<ushort> internalWords)
    {
        var pdu = new byte[5 + internalWords.Length * 2];
        BinaryPrimitives.WriteUInt16BigEndian(pdu, address);
        BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(2), (ushort)internalWords.Length);
        pdu[4] = (byte)(internalWords.Length * 2);
        for (int i = 0; i < internalWords.Length; i++)
        {
            ushort wire = WireCodec.ToWire(internalWords[i]);
            pdu[5 + 2 * i] = (byte)(wire >> 8);
            pdu[6 + 2 * i] = (byte)(wire & 0xFF);
        }
        return pdu;
    }

    /// <summary>写位请求 PDU（0x0F）：地址(2BE) + 数量(2BE) + 字节数(1) + 位数据（LSB-first）。</summary>
    public static byte[] BuildWriteBitsRequest(ushort address, ReadOnlySpan<bool> bits)
    {
        var pdu = new byte[5 + (bits.Length + 7) / 8];
        BinaryPrimitives.WriteUInt16BigEndian(pdu, address);
        BinaryPrimitives.WriteUInt16BigEndian(pdu.AsSpan(2), (ushort)bits.Length);
        pdu[4] = (byte)((bits.Length + 7) / 8);
        for (int i = 0; i < bits.Length; i++)
            if (bits[i]) pdu[5 + i / 8] |= (byte)(1 << (i % 8));
        return pdu;
    }

    // ---------- 请求分块（保留原 (ushort)(offset+done) 的地址回绕语义） ----------

    public static IEnumerable<(ushort Address, int Count)> Chunk(int startOffset, int total, int maxPerRequest)
    {
        int done = 0;
        while (done < total)
        {
            int n = Math.Min(total - done, maxPerRequest);
            yield return ((ushort)(startOffset + done), n);
            done += n;
        }
    }

    // ---------- 响应校验与解析 ----------
    // payloadStart：TCP=1（应答 PDU 首字节为 ByteCount），RTU=0（传输层已剥离 ByteCount）。

    public static void EnsureWordsResponse(byte[] data, int payloadStart, int n, string transport)
    {
        if (data.Length < payloadStart + n * 2)
            throw new IOException($"Modbus {transport} 读寄存器应答数据不足");
    }

    public static void EnsureBitsResponse(byte[] data, int n, bool hasByteCountPrefix, string transport)
    {
        int needed = (n + 7) / 8;
        if (hasByteCountPrefix)
        {
            // TCP：先有 ByteCount 字段本身，且其值必须覆盖所需字节数
            int byteCount = data.Length > 0 ? data[0] : 0;
            if (data.Length < 1 + byteCount || byteCount < needed)
                throw new IOException($"Modbus {transport} 读位应答数据不足");
        }
        else if (data.Length < needed)
        {
            throw new IOException($"Modbus {transport} 读位应答数据不足");
        }
    }

    /// <summary>解析 n 个寄存器（大端 → 内部字），写入 destination[offset..]。</summary>
    public static void ParseWords(byte[] data, int payloadStart, ushort[] destination, int offset, int count)
    {
        for (int i = 0; i < count; i++)
        {
            ushort wire = (ushort)((data[payloadStart + 2 * i] << 8) | data[payloadStart + 2 * i + 1]);
            destination[offset + i] = WireCodec.ToInternal(wire);
        }
    }

    /// <summary>解析 n 个位（LSB-first），写入 destination[offset..]。</summary>
    public static void ParseBits(byte[] data, int payloadStart, bool[] destination, int offset, int count)
    {
        for (int i = 0; i < count; i++)
            destination[offset + i] = (data[payloadStart + i / 8] >> (i % 8) & 1) == 1;
    }
}
