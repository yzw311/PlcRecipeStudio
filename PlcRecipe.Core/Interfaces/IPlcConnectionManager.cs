using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;

namespace PlcRecipe.Core.Interfaces;

/// <summary>PLC 连接状态。</summary>
public enum ConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2,
    Reconnecting = 3,
    Faulted = 4
}

/// <summary>连接状态变化事件参数。</summary>
public sealed record ConnectionStateChangedEventArgs(int DeviceId, string DeviceName, ConnectionState State, string? Message);

/// <summary>
/// PLC 连接管理器：每台设备维护一个 IPlcClient 与一把串行锁。
/// 所有对同一 PLC 的读写（手动/批量/信号监视）都经 ExecuteAsync 排队，
/// 保证同一 socket 上绝不并发请求。
/// </summary>
public interface IPlcConnectionManager
{
    /// <summary>获取已连接客户端（未连接则自动连接；信号监视由组合根经 StateChanged 事件装配）。</summary>
    Task<IPlcClient> GetClientAsync(PlcDevice device, CancellationToken ct = default);

    /// <summary>在设备串行锁内执行操作（统一排队入口；回调收到管理器的超时令牌）。</summary>
    Task<T> ExecuteAsync<T>(PlcDevice device, Func<IPlcClient, CancellationToken, Task<T>> operation, CancellationToken ct = default);

    /// <summary>在设备串行锁内执行无返回值操作（回调收到管理器的超时令牌）。</summary>
    Task ExecuteAsync(PlcDevice device, Func<IPlcClient, CancellationToken, Task> operation, CancellationToken ct = default);

    /// <summary>建立/维持连接并启动信号监视（若启用）。</summary>
    Task ConnectAsync(PlcDevice device, CancellationToken ct = default);

    /// <summary>断开并释放设备连接（含信号监视任务）。</summary>
    Task DisconnectAsync(int deviceId);

    ConnectionState GetState(int deviceId);

    /// <summary>线程安全地替换设备配置（配置修改后同步到连接条目）。</summary>
    void UpdateDevice(PlcDevice device);

    /// <summary>断开全部连接并释放（程序退出时调用）。</summary>
    Task ShutdownAsync();

    event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;
}

/// <summary>PLC 信号联动监视服务。</summary>
public interface ISignalMonitorService
{
    /// <summary>为设备启动信号监视轮询（未启用信号联动的设备直接返回）。</summary>
    void StartMonitoring(PlcDevice device);

    /// <summary>停止设备监视并释放资源。</summary>
    Task StopMonitoringAsync(int deviceId);

    Task StopAllAsync();

}
