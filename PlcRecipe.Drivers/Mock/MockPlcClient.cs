using System.Collections.Concurrent;
using PlcRecipe.Core;

namespace PlcRecipe.Drivers.Mock;

/// <summary>
/// 模拟 PLC 客户端：内存数据区 + 可配置延迟/故障注入，用于无硬件演示与自动化测试。
/// 支持字区 D{n} 与位区 M{n}；测试可直接操作内存来模拟 PLC 侧信号置位。
/// </summary>
public sealed class MockPlcClient : IPlcClient
{
    private readonly ConcurrentDictionary<string, ushort> _words = new();
    private readonly ConcurrentDictionary<string, bool> _bits = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile bool _disposed;
    private volatile bool _connected;
    private volatile bool _failAll;

    public int DeviceId { get; }
    public string Name { get; }
    public int SimulatedLatencyMs { get; set; } = 1;
    public bool IsConnected => _connected && !_disposed;

    /// <summary>故障注入：为 true 时所有读写抛异常（模拟通讯故障）。</summary>
    public bool FailAll { get => _failAll; set => _failAll = value; }

    public MockPlcClient(int deviceId, string name = "MockPLC")
    {
        DeviceId = deviceId;
        Name = name;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await Delay(ct).ConfigureAwait(false);
        _connected = true;
    }

    public Task DisconnectAsync()
    {
        _connected = false;
        return Task.CompletedTask;
    }

    private async Task Delay(CancellationToken ct) =>
        await Task.Delay(Math.Max(0, SimulatedLatencyMs), ct).ConfigureAwait(false);

    private static string WKey(ParsedAddress a, int absoluteOffset) => $"W:{a.Area}:{absoluteOffset}";
    private static string BKey(ParsedAddress a, int absoluteOffset) => $"B:{a.Area}:{absoluteOffset}";

    public async Task<ushort[]> ReadWordsAsync(ParsedAddress start, ushort count, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_connected) throw new InvalidOperationException("模拟 PLC 未连接");
        if (_failAll) throw new IOException("模拟通讯故障");
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Delay(ct).ConfigureAwait(false);
            var result = new ushort[count];
            for (int i = 0; i < count; i++)
                result[i] = _words.TryGetValue(WKey(start, start.Offset + i), out var v) ? v : (ushort)0;
            return result;
        }
        finally { _lock.Release(); }
    }

    public async Task WriteWordsAsync(ParsedAddress start, ushort[] words, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_connected) throw new InvalidOperationException("模拟 PLC 未连接");
        if (_failAll) throw new IOException("模拟通讯故障");
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Delay(ct).ConfigureAwait(false);
            for (int i = 0; i < words.Length; i++)
                _words[WKey(start, start.Offset + i)] = words[i];
        }
        finally { _lock.Release(); }
    }

    public async Task<bool[]> ReadBitsAsync(ParsedAddress start, ushort count, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_connected) throw new InvalidOperationException("模拟 PLC 未连接");
        if (_failAll) throw new IOException("模拟通讯故障");
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Delay(ct).ConfigureAwait(false);
            var result = new bool[count];
            for (int i = 0; i < count; i++)
                result[i] = _bits.TryGetValue(BKey(start, start.Offset + i), out var v) && v;
            return result;
        }
        finally { _lock.Release(); }
    }

    public async Task WriteBitsAsync(ParsedAddress start, bool[] bits, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_connected) throw new InvalidOperationException("模拟 PLC 未连接");
        if (_failAll) throw new IOException("模拟通讯故障");
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Delay(ct).ConfigureAwait(false);
            for (int i = 0; i < bits.Length; i++)
                _bits[BKey(start, start.Offset + i)] = bits[i];
        }
        finally { _lock.Release(); }
    }

    // ---- 测试辅助：模拟 PLC 侧动作 ----
    public void SetBitValue(string area, int offset, bool value) => _bits[$"B:{area}:{offset}"] = value;
    public bool GetBitValue(string area, int offset) => _bits.TryGetValue($"B:{area}:{offset}", out var v) && v;
    public void SetWordValue(string area, int offset, ushort value) => _words[$"W:{area}:{offset}"] = value;
    public ushort GetWordValue(string area, int offset) => _words.TryGetValue($"W:{area}:{offset}", out var v) ? v : (ushort)0;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _connected = false;
        _lock.Dispose();
        GC.SuppressFinalize(this);
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
