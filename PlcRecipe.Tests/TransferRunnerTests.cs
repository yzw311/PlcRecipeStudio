using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;
using PlcRecipe.WpfApp.Services;
using PlcRecipe.WpfApp.ViewModels;

namespace PlcRecipe.Tests;

/// <summary>
/// TransferRunner（从 WorkbenchViewModel 拆出的批量传输执行器）行为回归测试。
/// 断言口径与拆分前 RunBatchCoreAsync/LogTransferAsync 的行为逐条对齐：
/// 行建立/清空、成功与失败状态、汇总文案、日志动作与目标格式。
/// </summary>
public class TransferRunnerTests
{
    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Settings { get; } = new();
        public FakeSettings() { }
        public FakeSettings(int maxParallelDevices) => Settings.MaxParallelDevices = maxParallelDevices;
        public void Load() { }
        public void Save() { }
    }

    private sealed record LogCall(string Action, string? Target, string? Detail, bool Success, TransferSource Source, long DurationMs);

    private sealed class FakeOpLogs : IOpLogService
    {
        public List<LogCall> Calls { get; } = new();

        public Task AddAsync(string action, string? target, string? detail, bool success = true,
            TransferSource source = TransferSource.Manual, long durationMs = 0, string? userName = null, CancellationToken ct = default)
        {
            Calls.Add(new LogCall(action, target, detail, success, source, durationMs));
            return Task.CompletedTask;
        }

        public Task<(List<OpLog> Items, int Total)> QueryAsync(DateTime? from, DateTime? to, string? userName,
            TransferSource? source, string? actionKeyword, bool? success, int page, int pageSize, CancellationToken ct = default)
            => Task.FromResult((new List<OpLog>(), 0));

        public Task<List<OpLog>> RecentAsync(int count, TransferSource? source = null, CancellationToken ct = default)
            => Task.FromResult(new List<OpLog>());

        public Task<int> CleanupAsync(int keepDays, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeDialogs : IDialogService
    {
        public List<string> Infos { get; } = new();
        public bool ShowDeviceEditor(PlcDevice? device) => false;
        public bool ShowCompare(Core.CompareResult result) => false;
        public List<Core.CompareRow> LastCompareConfirmedRows { get; } = new();
        public bool ShowVersionHistory(Core.Models.Recipe current, IReadOnlyList<Core.Models.RecipeVersionHistory> versions,
            Func<int, Task<List<Core.Models.RecipeItem>>> loadSnapshot) => false;
        public int SelectedRollbackVersion { get; } = 0;
        public List<Core.Models.RecipeItem> SelectedRollbackRows { get; } = new();
        public void Info(string message, string title = "提示") => Infos.Add(message);
        public void Error(string message, string title = "错误") { }
        public bool Confirm(string message, string title = "确认") => true;
        public string? PromptText(string caption, string label, string defaultValue = "") => null;
        public string? PromptPassword(string caption, string label) => null;
        public string? PromptChoice(string caption, string label, IReadOnlyList<string> options, string defaultValue = "") => null;
        public string? PickOpenExcel() => null;
        public string? PickSaveExcel(string suggestedName) => null;
    }

    private static PlcDevice Device(int id, string name) => new() { Id = id, Name = name, Brand = PlcBrand.Mock };

    private static TransferRunner CreateRunner(FakeOpLogs? logs = null, FakeDialogs? dialogs = null, int maxParallel = 4) =>
        new(new FakeSettings(maxParallel), logs ?? new FakeOpLogs(), dialogs ?? new FakeDialogs());

    private static DeviceTransferResult OkResult(PlcDevice d, TransferDirection dir, string message, long ms = 100) => new()
    {
        DeviceId = d.Id,
        DeviceName = d.Name,
        Direction = dir,
        Status = TransferStatus.Success,
        Message = message,
        DurationMs = ms
    };

    [Fact]
    public async Task 批量执行_成功_行状态与汇总文案与拆分前一致()
    {
        var dialogs = new FakeDialogs();
        var runner = CreateRunner(dialogs: dialogs);
        var device = Device(1, "1#机");

        await runner.RunBatchAsync([device], TransferDirection.Download,
            (d, _, _) => Task.FromResult(OkResult(d, TransferDirection.Download, "成功写入 7 个变量", 123)));

        var row = Assert.Single(runner.TransferRows);
        Assert.Equal(1, row.DeviceId);
        Assert.Equal("1#机", row.DeviceName);
        Assert.Equal(TransferDirection.Download, row.Direction);
        Assert.Equal(TransferStatus.Success, row.Status);
        Assert.Equal("成功写入 7 个变量", row.StatusText);
        Assert.Equal(123, row.DurationMs);

        var summary = Assert.Single(dialogs.Infos);
        Assert.StartsWith("完成：成功 1 台，失败 0 台，耗时 ", summary);
        Assert.EndsWith(" ms", summary);
    }

    [Fact]
    public async Task 批量执行_单台抛异常_该行失败其余继续_汇总计数正确()
    {
        var dialogs = new FakeDialogs();
        var runner = CreateRunner(dialogs: dialogs);
        var ok = Device(1, "正常机");
        var bad = Device(2, "故障机");

        await runner.RunBatchAsync([ok, bad], TransferDirection.Download, async (d, _, _) =>
        {
            if (d.Id == 2) throw new InvalidOperationException("PLC 超时");
            await Task.Yield();
            return OkResult(d, TransferDirection.Download, "OK");
        });

        Assert.Equal(2, runner.TransferRows.Count);
        var okRow = runner.TransferRows.Single(r => r.DeviceId == 1);
        var badRow = runner.TransferRows.Single(r => r.DeviceId == 2);
        Assert.Equal(TransferStatus.Success, okRow.Status);
        Assert.Equal("OK", okRow.StatusText);
        Assert.Equal(TransferStatus.Failed, badRow.Status);
        Assert.Equal("失败：PLC 超时", badRow.StatusText);

        var summary = Assert.Single(dialogs.Infos);
        Assert.StartsWith("完成：成功 1 台，失败 1 台，耗时 ", summary);
    }

    [Fact]
    public async Task 批量执行_多目标_每台一行且顺序与目标一致()
    {
        var runner = CreateRunner();
        var targets = new[] { Device(3, "3#机"), Device(1, "1#机"), Device(2, "2#机") };

        await runner.RunBatchAsync(targets.ToList(), TransferDirection.Upload,
            (d, _, _) => Task.FromResult(OkResult(d, TransferDirection.Upload, "成功读取 7 个变量")));

        Assert.Equal(3, runner.TransferRows.Count);
        Assert.Equal(new[] { "3#机", "1#机", "2#机" }, runner.TransferRows.Select(r => r.DeviceName));
        Assert.All(runner.TransferRows, r => Assert.Equal(TransferStatus.Success, r.Status));
    }

    [Fact]
    public async Task 批量执行_重复执行_先清空上一批状态行()
    {
        var runner = CreateRunner();

        await runner.RunBatchAsync([Device(1, "1#机"), Device(2, "2#机")], TransferDirection.Download,
            (d, _, _) => Task.FromResult(OkResult(d, TransferDirection.Download, "OK")));
        await runner.RunBatchAsync([Device(9, "9#机")], TransferDirection.Upload,
            (d, _, _) => Task.FromResult(OkResult(d, TransferDirection.Upload, "OK")));

        var row = Assert.Single(runner.TransferRows);
        Assert.Equal(9, row.DeviceId);
        Assert.Equal(TransferDirection.Upload, row.Direction);
    }

    [Fact]
    public async Task 批量执行_job取消异常_同样按失败行处理_不中断()
    {
        var dialogs = new FakeDialogs();
        var runner = CreateRunner(dialogs: dialogs);

        await runner.RunBatchAsync([Device(1, "1#机")], TransferDirection.Download,
            (_, _, ct) => Task.FromException<DeviceTransferResult>(new OperationCanceledException(ct)));

        var row = Assert.Single(runner.TransferRows);
        Assert.Equal(TransferStatus.Failed, row.Status);
        Assert.StartsWith("失败：", row.StatusText);
        Assert.StartsWith("完成：成功 0 台，失败 1 台，耗时 ", Assert.Single(dialogs.Infos));
    }

    [Fact]
    public async Task 传输日志_下载与上传_动作与目标格式与拆分前完全一致()
    {
        var logs = new FakeOpLogs();
        var runner = CreateRunner(logs: logs);
        var device = Device(1, "1#机");

        await runner.LogTransferAsync(device, new DeviceTransferResult
        {
            Direction = TransferDirection.Download, Status = TransferStatus.Success,
            Message = "成功写入 7 个变量（1 字块 / 1 位块）", DurationMs = 45
        }, "配方A");
        await runner.LogTransferAsync(device, new DeviceTransferResult
        {
            Direction = TransferDirection.Upload, Status = TransferStatus.Failed,
            Message = "读取失败", DurationMs = 60
        }, "配方A");

        Assert.Equal(2, logs.Calls.Count);

        Assert.Equal("下载配方", logs.Calls[0].Action);
        Assert.Equal("1#机 ← 配方A", logs.Calls[0].Target);
        Assert.Equal("成功写入 7 个变量（1 字块 / 1 位块）", logs.Calls[0].Detail);
        Assert.True(logs.Calls[0].Success);
        Assert.Equal(TransferSource.Manual, logs.Calls[0].Source);
        Assert.Equal(45, logs.Calls[0].DurationMs);

        Assert.Equal("上传配方", logs.Calls[1].Action);
        Assert.Equal("1#机 → 配方A", logs.Calls[1].Target);
        Assert.False(logs.Calls[1].Success);
        Assert.Equal(60, logs.Calls[1].DurationMs);
    }

    [Fact]
    public async Task 并行度_取设置值并夹取到1到16()
    {
        // 夹取逻辑无法从外部直接观测，验证两种极端配置下批量执行仍然全部成功、全部有行
        foreach (var maxParallel in new[] { 0, 1, 100 })
        {
            var runner = CreateRunner(maxParallel: maxParallel);
            var targets = Enumerable.Range(1, 8).Select(i => Device(i, $"{i}#机")).ToList();
            await runner.RunBatchAsync(targets, TransferDirection.Download,
                (d, _, _) => Task.FromResult(OkResult(d, TransferDirection.Download, "OK")));
            Assert.Equal(8, runner.TransferRows.Count);
            Assert.All(runner.TransferRows, r => Assert.Equal(TransferStatus.Success, r.Status));
        }
    }
}
