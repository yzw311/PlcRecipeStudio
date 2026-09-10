using System.Collections.Concurrent;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;
using PlcRecipe.Drivers;

namespace PlcRecipe.Infrastructure.Plc;

/// <summary>
/// PLC 连接管理器：每台设备一个客户端实例 + 一把串行锁。
/// 断线后懒重连（下次 ExecuteAsync 时重建），状态变化通过 StateChanged 事件广播。
/// </summary>
public sealed class PlcConnectionManager(
    IPlcClientFactory clientFactory,
    ISettingsService settingsService,
    ILogger<PlcConnectionManager> logger) : IPlcConnectionManager
{
    private sealed class DeviceConnection
    {
        public required PlcDevice Device { get; set; }
        public IPlcClient? Client; // 字段（供 Interlocked 原子交换）
        public SemaphoreSlim IoLock { get; } = new(1, 1);
        public volatile ConnectionState State = ConnectionState.Disconnected;
        public int ConnectInFlight; // 0/1，防止并发重连风暴
    }

    private readonly ConcurrentDictionary<int, DeviceConnection> _connections = new();

    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    private DeviceConnection GetOrAdd(PlcDevice device) =>
        _connections.GetOrAdd(device.Id, _ => new DeviceConnection { Device = device });

    private void SetState(DeviceConnection conn, ConnectionState state, string? message = null)
    {
        if (conn.State == state) return;
        conn.State = state;
        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(conn.Device.Id, conn.Device.Name, state, message));
    }

    public async Task<IPlcClient> GetClientAsync(PlcDevice device, CancellationToken ct = default)
    {
        var conn = GetOrAdd(device);
        conn.Device = device;
        if (conn.Client is { IsConnected: true })
        {
            SetState(conn, ConnectionState.Connected);
            return conn.Client;
        }

        if (Interlocked.CompareExchange(ref conn.ConnectInFlight, 1, 0) != 0)
        {
            // 其他请求正在连接，轮询等待其完成（对方成功后 Client 可用；失败会标记 Faulted）
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (conn.Client is not { IsConnected: true })
            {
                if (ct.IsCancellationRequested)
                    throw new OperationCanceledException(ct);
                if (conn.State is ConnectionState.Faulted or ConnectionState.Disconnected ||
                    DateTime.UtcNow > deadline)
                    throw new IOException($"设备“{device.Name}”连接失败（状态：{conn.State}）");
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
            return conn.Client!;
        }

        try
        {
            SetState(conn, ConnectionState.Connecting);
            await conn.IoLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (conn.Client is { IsConnected: true }) return conn.Client;
                var old = conn.Client;
                if (old != null) await old.DisposeAsync().ConfigureAwait(false);
                var client = clientFactory.Create(device);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(settingsService.Settings.OperationTimeoutMs + 2000);
                await client.ConnectAsync(cts.Token).ConfigureAwait(false);
                conn.Client = client;
                SetState(conn, ConnectionState.Connected);
                logger.LogInformation("PLC {Device} ({Ip}:{Port}) 已连接", device.Name, device.Ip, device.Port);
                return client;
            }
            finally
            {
                conn.IoLock.Release();
            }
        }
        catch (Exception ex)
        {
            SetState(conn, ConnectionState.Faulted, ex.Message);
            logger.LogWarning(ex, "PLC {Device} 连接失败", device.Name);
            throw;
        }
        finally
        {
            Volatile.Write(ref conn.ConnectInFlight, 0);
        }
    }

    public async Task<T> ExecuteAsync<T>(PlcDevice device, Func<IPlcClient, CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        var conn = GetOrAdd(device);
        conn.Device = device;
        var client = await GetClientAsync(device, ct).ConfigureAwait(false);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Math.Max(500, settingsService.Settings.OperationTimeoutMs));
        var acquired = false;
        try
        {
            await conn.IoLock.WaitAsync(cts.Token).ConfigureAwait(false);
            acquired = true;
            client = conn.Client;
            if (client is null || !client.IsConnected)
                throw new IOException($"设备“{device.Name}”连接在执行前已断开，将在下次操作时重连");
            var result = await operation(client, cts.Token).ConfigureAwait(false);
            SetState(conn, ConnectionState.Connected);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException
            or PlcCommunicationException or PlcAddressException or TimeoutException or OperationCanceledException)
        {
            // 通讯类异常：丢弃客户端，下次自动重建；标记故障。业务异常（如校验失败）不销毁连接。
            var old = Interlocked.Exchange(ref conn.Client, null!);
            if (old != null) await old.DisposeAsync().ConfigureAwait(false);
            SetState(conn, ConnectionState.Faulted, ex.Message);
            logger.LogWarning(ex, "PLC {Device} 通讯异常", device.Name);
            throw;
        }
        finally
        {
            if (acquired) conn.IoLock.Release();
        }
    }

    public async Task ExecuteAsync(PlcDevice device, Func<IPlcClient, CancellationToken, Task> operation, CancellationToken ct = default) =>
        await ExecuteAsync<object?>(device, async (c, opCt) =>
        {
            await operation(c, opCt).ConfigureAwait(false);
            return null;
        }, ct).ConfigureAwait(false);

    public async Task ConnectAsync(PlcDevice device, CancellationToken ct = default)
    {
        await GetClientAsync(device, ct).ConfigureAwait(false);
    }

    public async Task DisconnectAsync(int deviceId)
    {
        if (!_connections.TryRemove(deviceId, out var conn)) return;
        SetState(conn, ConnectionState.Disconnected);
        // 等待进行中的操作结束（带超时）；信号量不 Dispose（避免与进行中操作的 Release 竞态），交由 GC 回收
        var acquired = await conn.IoLock.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        try
        {
            await DisposeClientAsync(conn).ConfigureAwait(false);
        }
        finally
        {
            if (acquired) conn.IoLock.Release();
        }
    }

    public ConnectionState GetState(int deviceId) =>
        _connections.TryGetValue(deviceId, out var conn) ? conn.State : ConnectionState.Disconnected;

    /// <summary>获取设备当前配置快照（无连接返回 null）。</summary>
    public PlcDevice? GetDeviceSnapshot(int deviceId) =>
        _connections.TryGetValue(deviceId, out var conn) ? conn.Device : null;


    public async Task ShutdownAsync()
    {
        foreach (var kvp in _connections)
        {
            if (_connections.TryRemove(kvp.Key, out var conn))
            {
                SetState(conn, ConnectionState.Disconnected);
                var acquired = await conn.IoLock.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                try
                {
                    await DisposeClientAsync(conn).ConfigureAwait(false);
                }
                finally
                {
                    if (acquired) conn.IoLock.Release();
                    // 信号量不 Dispose：交由 GC 回收（避免与进行中操作的 Release 竞态）
                }
            }
        }
    }

    /// <summary>清空连接条目持有的客户端并释放（调用方需已持有 IoLock）。</summary>
    private static async Task DisposeClientAsync(DeviceConnection conn)
    {
        var client = conn.Client;
        conn.Client = null;
        if (client != null) await client.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>线程安全地替换设备配置（配置修改后同步到连接条目）。</summary>
    public void UpdateDevice(PlcDevice device)
    {
        if (_connections.TryGetValue(device.Id, out var conn))
            conn.Device = device;
    }
}
