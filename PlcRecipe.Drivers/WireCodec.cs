using System.Buffers.Binary;

namespace PlcRecipe.Drivers;

/// <summary>内部字 ↔ 线上（大端）寄存器数值 的互转。全品牌统一。</summary>
public static class WireCodec
{
    /// <summary>内部字 → 线上寄存器数值。</summary>
    public static ushort ToWire(ushort internalWord) => BinaryPrimitives.ReverseEndianness(internalWord);

    /// <summary>线上寄存器数值 → 内部字。</summary>
    public static ushort ToInternal(ushort wireWord) => BinaryPrimitives.ReverseEndianness(wireWord);
}
