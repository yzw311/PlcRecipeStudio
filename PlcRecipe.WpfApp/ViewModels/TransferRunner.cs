using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;
using PlcRecipe.WpfApp.Services;

namespace PlcRecipe.WpfApp.ViewModels;

/// <summary>
/// 批量传输执行器（从 WorkbenchViewModel 拆出的执行协作类）：
/// 按配置并行度逐台执行调用方给定的传输任务，维护执行状态行并写操作日志。
/// 只负责"怎么跑"；目标选择、配方解析、业务决策仍留在 WorkbenchViewModel。
/// </summary>
public sealed class TransferRunner(
    ISettingsService settings,
    IOpLogService opLogs,
    IDialogService dialogs)
{
    /// <summary>执行状态行集合：整批执行前 Clear 后按目标重建；集合实例终生不变，供界面绑定。</summary>
    public ObservableCollection<TransferRowViewModel> TransferRows { get; } = new();

    /// <summary>
    /// 批量执行：为每台目标设备建立状态行，按 MaxParallelDevices（1~16）并行执行 job，
    /// 单台异常只标记该行失败不中断整批，结束时弹汇总。
    /// </summary>
    public async Task RunBatchAsync(List<PlcDevice> targets, TransferDirection direction,
        Func<PlcDevice, IProgress<TransferProgress>, CancellationToken, Task<DeviceTransferResult>> job)
    {
        TransferRows.Clear();
        var rowsByDevice = new Dictionary<int, TransferRowViewModel>();
        var progressByDevice = new Dictionary<int, IProgress<TransferProgress>>();
        foreach (var d in targets)
        {
            var row = new TransferRowViewModel { DeviceId = d.Id, DeviceName = d.Name, Direction = direction };
            rowsByDevice[d.Id] = row;
            // Progress<T> 必须在调用方（UI）线程创建才捕获得到 SynchronizationContext；
            // 在 ForEachAsync 的线程池委托里创建会让回调直接在线程池线程上跑
            progressByDevice[d.Id] = new Progress<TransferProgress>(p =>
            {
                row.Progress = p.Percent;
                row.StatusText = p.StatusText;
                row.Status = p.Status;
            });
            TransferRows.Add(row);
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(settings.Settings.MaxParallelDevices, 1, 16) };
        await Parallel.ForEachAsync(targets, options, async (device, ct) =>
        {
            var row = rowsByDevice[device.Id];
            var progress = progressByDevice[device.Id];
            try
            {
                row.Status = TransferStatus.Running;
                var result = await job(device, progress, ct).ConfigureAwait(false);
                row.Status = result.Status;
                row.StatusText = result.Message ?? "";
                row.DurationMs = result.DurationMs;
            }
            catch (Exception ex)
            {
                row.Status = TransferStatus.Failed;
                row.StatusText = "失败：" + ex.Message;
            }
        }).ConfigureAwait(true);

        var ok = TransferRows.Count(r => r.Status == TransferStatus.Success);
        var fail = TransferRows.Count(r => r.Status == TransferStatus.Failed);
        dialogs.Info($"完成：成功 {ok} 台，失败 {fail} 台，耗时 {sw.ElapsedMilliseconds} ms");
    }

    /// <summary>把一次传输结果写入操作日志（动作名/目标格式与既有日志完全一致）。</summary>
    public async Task LogTransferAsync(PlcDevice device, DeviceTransferResult r, string recipeName)
    {
        await opLogs.AddAsync(
            r.Direction == TransferDirection.Download ? "下载配方" : "上传配方",
            r.Direction == TransferDirection.Download ? $"{device.Name} ← {recipeName}" : $"{device.Name} → {recipeName}",
            r.Message, r.Status == TransferStatus.Success, TransferSource.Manual, r.DurationMs).ConfigureAwait(false);
    }
}

/// <summary>单台设备的传输执行行。</summary>
public partial class TransferRowViewModel : ObservableObject
{
    public int DeviceId { get; init; }
    public string DeviceName { get; init; } = "";
    public TransferDirection Direction { get; set; }

    [ObservableProperty]
    private TransferStatus _status = TransferStatus.Pending;

    [ObservableProperty]
    private int _progress;

    [ObservableProperty]
    private string _statusText = "等待执行";

    [ObservableProperty]
    private long _durationMs;
}
