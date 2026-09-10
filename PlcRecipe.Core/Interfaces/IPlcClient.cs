namespace PlcRecipe.Core;

/// <summary>
/// PLC 客户端抽象：所有品牌驱动的统一读写接口。
/// 实现类必须全异步、可取消、支持 IAsyncDisposable，并保证线程安全
/// （同一连接上的并发请求由 PlcConnectionManager 的信号量串行化）。
/// </summary>
public interface IPlcClient : IAsyncDisposable
{
    /// <summary>所属设备 Id（用于日志与事件）</summary>
    int DeviceId { get; }
    /// <summary>连接是否可用</summary>
    bool IsConnected { get; }

    /// <summary>建立连接（带超时与取消支持）。</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>主动断开并释放底层资源。</summary>
    Task DisconnectAsync();

    /// <summary>从指定字地址连续读取 count 个字（大端语义由调用方约定）。</summary>
    Task<ushort[]> ReadWordsAsync(ParsedAddress start, ushort count, CancellationToken ct = default);

    /// <summary>从指定字地址连续写入 words。</summary>
    Task WriteWordsAsync(ParsedAddress start, ushort[] words, CancellationToken ct = default);

    /// <summary>从指定位地址连续读取 count 个位。</summary>
    Task<bool[]> ReadBitsAsync(ParsedAddress start, ushort count, CancellationToken ct = default);

    /// <summary>从指定位地址连续写入 bits。</summary>
    Task WriteBitsAsync(ParsedAddress start, bool[] bits, CancellationToken ct = default);
}
