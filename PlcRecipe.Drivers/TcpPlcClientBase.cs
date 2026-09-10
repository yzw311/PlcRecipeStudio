using System.Buffers;
using System.Buffers.Binary;
using System.Net.Sockets;
using PlcRecipe.Core;

namespace PlcRecipe.Drivers;

/// <summary>
/// TCP 型 PLC 客户端公共基类：连接管理、超时、精确读包、资源释放。
/// 字传输约定：IPlcClient 层的 ushort 为“内部字”，驱动负责与线上的
/// 大端寄存器值互转（WireCodec），保证全品牌 32 位数据大端字序一致。
/// </summary>
public abstract class TcpPlcClientBase : IPlcClient
{
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private volatile bool _disposed;

    public int DeviceId { get; }
    public string Host { get; }
    public int Port { get; }
    public bool IsConnected => _tcp?.Connected == true && _stream != null;

    protected TcpPlcClientBase(int deviceId, string host, int port)
    {
        DeviceId = deviceId;
        Host = host;
        Port = port;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsConnected) return;
        await DisconnectCoreAsync().ConfigureAwait(false);

        var tcp = new TcpClient { NoDelay = true };
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ConnectTimeoutMs);
            await tcp.ConnectAsync(Host, Port, cts.Token).ConfigureAwait(false);
            _tcp = tcp;
            _stream = tcp.GetStream();
            await OnConnectedAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            tcp.Dispose();
            _tcp = null;
            _stream = null;
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

    /// <summary>连接建立后的协议握手（如 FINS 节点握手），默认无。</summary>
    protected virtual Task OnConnectedAsync(CancellationToken ct) => Task.CompletedTask;

    protected virtual int ConnectTimeoutMs => 3000;

    protected Stream Stream => _stream ?? throw new InvalidOperationException("PLC 未连接");

    /// <summary>底层 TcpClient（供子类获取本机端点等）。</summary>
    protected TcpClient? Tcp => _tcp;

    /// <summary>精确读取 count 字节（阻塞直到读完或取消）。</summary>
    protected async Task<byte[]> ReadExactAsync(int count, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(count);
        try
        {
            int read = 0;
            while (read < count)
            {
                int n = await Stream.ReadAsync(buffer.AsMemory(read, count - read), ct).ConfigureAwait(false);
                if (n <= 0) throw new IOException("PLC 连接已断开（对端关闭）");
                read += n;
            }
            var result = new byte[count];
            Array.Copy(buffer, result, count);
            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    protected async Task WriteAllAsync(byte[] data, CancellationToken ct)
    {
        await Stream.WriteAsync(data.AsMemory(), ct).ConfigureAwait(false);
        await Stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await DisconnectCoreAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    public abstract Task<ushort[]> ReadWordsAsync(ParsedAddress start, ushort count, CancellationToken ct = default);
    public abstract Task WriteWordsAsync(ParsedAddress start, ushort[] words, CancellationToken ct = default);
    public abstract Task<bool[]> ReadBitsAsync(ParsedAddress start, ushort count, CancellationToken ct = default);
    public abstract Task WriteBitsAsync(ParsedAddress start, bool[] bits, CancellationToken ct = default);
}
