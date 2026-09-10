namespace PlcRecipe.Drivers;

/// <summary>PLC 通讯异常（带协议错误码）。</summary>
public class PlcCommunicationException(string message, int errorCode)
    : Exception($"{message}（协议错误码 0x{errorCode:X4}）")
{
    public int ErrorCode { get; } = errorCode;
}
