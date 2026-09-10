using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;
using PlcRecipe.Drivers;

namespace PlcRecipe.Infrastructure.Plc;

/// <summary>
/// PLC 信号联动监视服务：轮询下载/上传请求位（上升沿触发），
/// 读配方号 → 执行下载/上传 → 置完成位/失败位 → 等请求位清零后复位完成/失败位。
/// 与手动操作共用连接管理器的串行锁，同一 PLC 上绝不并发请求。
/// </summary>
public sealed class SignalMonitorService(
    Func<IPlcConnectionManager> connectionsFactory,
    Func<ITransferService> transfersFactory,
    IRecipeService recipes,
    IRecipeFileStore fileStore,
    IOpLogService opLogs,
    ILogger<SignalMonitorService> logger) : ISignalMonitorService
{
    // 惰性解析，避免与 IPlcConnectionManager 构成 DI 循环（会死锁）
    private IPlcConnectionManager Connections => connectionsFactory();
    private ITransferService Transfers => transfersFactory();

    private sealed class MonitorState
    {
        public required PlcDevice Device { get; set; }
        public CancellationTokenSource? Cts;
        public Task? LoopTask;
        public int JobRunning;          // 0/1 防重入
        public bool LastDownloadReq;
        public bool LastUploadReq;
        public bool PendingResetDone;   // 等请求位清零后复位完成位
        public bool PendingResetFail;
        public bool Running;
    }

    private readonly ConcurrentDictionary<int, MonitorState> _monitors = new();

    /// <summary>设备连接成功后由连接管理器回调（组合根装配）。</summary>
    public void OnDeviceConnected(PlcDevice device) => StartMonitoring(device);

    public void StartMonitoring(PlcDevice device)
    {
        if (!device.SignalEnabled) return;
        if (string.IsNullOrWhiteSpace(device.DownloadRequestAddress) &&
            string.IsNullOrWhiteSpace(device.UploadRequestAddress)) return;

        var state = _monitors.GetOrAdd(device.Id, _ => new MonitorState { Device = device });
        state.Device = device;
        if (state.Running) return;

        state.Cts = new CancellationTokenSource();
        state.Running = true;
        var token = state.Cts.Token;
        state.LoopTask = Task.Run(() => LoopAsync(state, token), CancellationToken.None);
        logger.LogInformation("设备 {Device} 信号联动监视已启动（间隔 {Ms}ms）", device.Name, device.PollIntervalMs);
    }

    public async Task StopMonitoringAsync(int deviceId)
    {
        if (!_monitors.TryRemove(deviceId, out var state)) return;
        state.Running = false;
        state.Cts?.Cancel();
        if (state.LoopTask != null)
        {
            try { await state.LoopTask.ConfigureAwait(false); } catch { /* 取消异常忽略 */ }
        }
        state.Cts?.Dispose();
        logger.LogInformation("设备 {Device} 信号联动监视已停止", state.Device.Name);
    }

    public async Task StopAllAsync()
    {
        foreach (var id in _monitors.Keys.ToList())
            await StopMonitoringAsync(id).ConfigureAwait(false);
    }


    // ---------------- 主循环 ----------------
    private async Task LoopAsync(MonitorState state, CancellationToken ct)
    {
        int consecutiveErrors = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // 每轮取最新设备配置：编辑设备（地址/间隔）后无需重连即可生效
                var device = state.Device;
                var configuredMs = Math.Max(100, device.PollIntervalMs);
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(configuredMs));
                while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                {
                    // 轮询间隔配置变化 → 跳出内层，重建定时器
                    if (Math.Max(100, state.Device.PollIntervalMs) != configuredMs) break;

                    if (Volatile.Read(ref state.JobRunning) == 1) continue; // 任务执行中，本轮跳过

                    bool downloadReq = false, uploadReq = false;
                    try
                    {
                        downloadReq = await ReadBitSafeAsync(device, device.DownloadRequestAddress, ct).ConfigureAwait(false);
                        uploadReq = await ReadBitSafeAsync(device, device.UploadRequestAddress, ct).ConfigureAwait(false);
                        consecutiveErrors = 0;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        consecutiveErrors++;
                        if (consecutiveErrors == 1)
                            logger.LogWarning("设备 {Device} 信号轮询失败：{Msg}", device.Name, ex.Message);
                        if (consecutiveErrors >= 3)
                            await WaitUntilRecoveredAsync(state, device, consecutiveErrors, ct).ConfigureAwait(false);
                        continue;
                    }

                    // 握手闭环：请求位已清零且有待复位的信号 → 复位完成/失败位
                    if ((state.PendingResetDone || state.PendingResetFail) && !downloadReq && !uploadReq)
                    {
                        await ClearResultBitsAsync(device, ct).ConfigureAwait(false);
                        state.PendingResetDone = false;
                        state.PendingResetFail = false;
                        logger.LogInformation("设备 {Device} 握手闭环完成（请求位已清零，结果位已复位）", device.Name);
                    }

                    // 下载请求：上升沿触发
                    if (downloadReq && !state.LastDownloadReq)
                    {
                        if (Interlocked.CompareExchange(ref state.JobRunning, 1, 0) == 0)
                        {
                            try
                            {
                                await HandleDownloadRequestAsync(state, ct).ConfigureAwait(false);
                                state.PendingResetDone = true;
                            }
                            catch (OperationCanceledException) when (ct.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "设备 {Device} 信号下载任务失败", device.Name);
                                state.PendingResetFail = true;
                                state.PendingResetDone = true; // 失败也要进入闭环：请求位清零后复位失败位
                                await TrySetFailBitAsync(device, ct).ConfigureAwait(false);
                            }
                            finally
                            {
                                Volatile.Write(ref state.JobRunning, 0);
                            }
                        }
                    }
                    // 上传请求：上升沿触发
                    if (uploadReq && !state.LastUploadReq && !downloadReq)
                    {
                        if (Interlocked.CompareExchange(ref state.JobRunning, 1, 0) == 0)
                        {
                            try
                            {
                                await HandleUploadRequestAsync(state, ct).ConfigureAwait(false);
                                state.PendingResetDone = true;
                            }
                            catch (OperationCanceledException) when (ct.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "设备 {Device} 信号上传任务失败", device.Name);
                                state.PendingResetFail = true;
                                state.PendingResetDone = true; // 失败也要进入闭环：请求位清零后复位失败位
                                await TrySetFailBitAsync(device, ct).ConfigureAwait(false);
                            }
                            finally
                            {
                                Volatile.Write(ref state.JobRunning, 0);
                            }
                        }
                    }

                    state.LastDownloadReq = downloadReq;
                    // 上传请求因下载优先被跳过时，不记“已见过”，否则该上升沿被永久吞掉；
                    // 等下载请求位清零后，仍为真的上传位会在下一轮被识别为上升沿执行
                    state.LastUploadReq = uploadReq && !downloadReq;
                }
            }
        }
        catch (OperationCanceledException) { /* 正常停止 */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "设备 {Device} 信号监视循环异常退出", state.Device.Name);
        }
        finally
        {
            // 无论正常停止还是异常退出都复位标记，否则重连成功后 StartMonitoring 会直接 return，监视永久失效
            state.Running = false;
        }
    }

    /// <summary>连续失败后的恢复探测：每 30s 试读一次请求位，成功即归零继续轮询。</summary>
    private async Task WaitUntilRecoveredAsync(MonitorState state, PlcDevice device, int consecutiveErrors, CancellationToken ct)
    {
        logger.LogWarning("设备 {Device} 连续 {N} 次轮询失败，进入 30s 周期探测重连", device.Name, consecutiveErrors);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                var probe = device.DownloadRequestAddress ?? device.UploadRequestAddress;
                if (!string.IsNullOrWhiteSpace(probe))
                    await ReadBitSafeAsync(device, probe, ct).ConfigureAwait(false);
                logger.LogInformation("设备 {Device} 信号轮询已恢复", device.Name);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch { /* 探测失败，继续下一个 30s 周期 */ }
        }
    }

    private async Task<bool> ReadBitSafeAsync(PlcDevice device, string? address, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        var addr = PlcAddressParser.Parse(device.Brand, address);
        if (!addr.IsBitDevice)
            throw new PlcAddressException(address, "信号位地址必须是位地址");
        var r = await Connections.ExecuteAsync(device,
            (c, opCt) => c.ReadBitsAsync(addr, 1, opCt), ct).ConfigureAwait(false);
        return r is [true, ..];
    }

    private async Task HandleDownloadRequestAsync(MonitorState state, CancellationToken ct)
    {
        var device = state.Device;
        var sw = Stopwatch.StartNew();
        try
        {
            // 配方文件库（Pfdoc）为准：按 PLC 报上来的配方名查找同名 txt，不存在即失败（置失败位）
            var name = await ReadRecipeNameAsync(device, ct).ConfigureAwait(false);
            var doc = await fileStore.TryLoadAsync(device.Name, name).ConfigureAwait(false)
                      ?? throw new InvalidOperationException($"配方库（Pfdoc）中不存在配方“{name}”的文件");
            string matchDesc = $"配方名“{name}”（文件库 v{doc.Version}）";

            // 文件内容即下载源：虚拟配方（数据行含定义+值）
            var recipe = new Recipe
            {
                Id = 0,
                DeviceId = device.Id,
                Name = doc.Name,
                Version = doc.Version,
                Items = doc.Items.ToList()
            };

            logger.LogInformation("设备 {Device} 信号触发下载：配方 {Recipe}（{Match}）", device.Name, recipe.Name, matchDesc);
            var result = await Transfers.DownloadAsync(device, recipe, null, false, ct).ConfigureAwait(false);

            if (result.Status == TransferStatus.Success)
            {
                await SetBitAsync(device, device.DoneBitAddress, ct).ConfigureAwait(false);
                await opLogs.AddAsync("信号下载配方", $"{device.Name} ← {recipe.Name}",
                    $"{matchDesc}，{result.Message}", true, TransferSource.SignalTrigger,
                    sw.ElapsedMilliseconds, "PLC信号", ct).ConfigureAwait(false);
                state.PendingResetFail = false;
            }
            else
            {
                state.PendingResetFail = true;
                await TrySetFailBitAsync(device, ct).ConfigureAwait(false);
                await opLogs.AddAsync("信号下载配方", $"{device.Name} ← {recipe.Name}",
                    $"{matchDesc}，{result.Message}", false, TransferSource.SignalTrigger,
                    sw.ElapsedMilliseconds, "PLC信号", ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await opLogs.AddAsync("信号下载配方", device.Name, "执行失败（详见软件日志）",
                false, TransferSource.SignalTrigger, sw.ElapsedMilliseconds, "PLC信号", CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task HandleUploadRequestAsync(MonitorState state, CancellationToken ct)
    {
        var device = state.Device;
        var sw = Stopwatch.StartNew();
        try
        {
            var name = await ReadRecipeNameAsync(device, ct).ConfigureAwait(false);
            // 配方文件库为准：同名 txt 存在 → 按其结构读 PLC 值并更新 txt（版本+1）；不存在 → 新建
            var doc = await fileStore.TryLoadAsync(device.Name, name).ConfigureAwait(false);

            if (doc != null)
            {
                string matchDesc = $"配方名“{name}”（文件库 v{doc.Version} 更新）";
                var template = new Recipe
                {
                    Id = 0,
                    DeviceId = device.Id,
                    Name = doc.Name,
                    Version = doc.Version,
                    Items = doc.Items.ToList()
                };
                var upload = await Transfers.UploadAsync(device, template, null, ct).ConfigureAwait(false);
                if (upload.Status != TransferStatus.Success || upload.ReadValues == null)
                    throw new InvalidOperationException(upload.Message ?? "上传读取失败");

                // 乐观校验：读取期间若配方文件被人工修改（版本变化），信号回写让位人工修改，放弃覆盖
                var latest = await fileStore.TryLoadAsync(device.Name, name).ConfigureAwait(false);
                if (latest == null || latest.Version != doc.Version)
                    throw new InvalidOperationException(
                        $"配方“{name}”在上传读取期间被人工修改（文件库 v{latest?.Version ?? 0}），本次上传结果未落库，请重试");

                var updated = doc.Items.Select(i => new RecipeItem
                {
                    Name = i.Name,
                    Address = i.Address,
                    DataType = i.DataType,
                    StringWords = i.StringWords,
                    Unit = i.Unit,
                    Access = i.Access,
                    SortOrder = i.SortOrder,
                    Remark = i.Remark,
                    Value = upload.ReadValues.TryGetValue(i.Name, out var v) ? v : i.Value,
                    LowerLimit = i.LowerLimit,
                    UpperLimit = i.UpperLimit
                }).ToList();

                // 值写回文件库（txt 即数据库，版本 +1 并留历史快照）。
                // 注意：必须用文件头里的规范名（doc.Name）——PLC 报的名字大小写可能不一致，
                // TryLoad 靠文件系统大小写不敏感能命中，但按名哈希不行。
                await recipes.ApplyReadValuesByNameAsync(device.Name, doc.Name, upload.ReadValues, "PLC信号", ct).ConfigureAwait(false);

                // 参数容差：对带回上下限的数据行做越界告警（不阻断信号流程）
                var violations = RecipeLimits.Check(updated, upload.ReadValues);
                if (violations.Count > 0)
                {
                    logger.LogWarning("设备 {Device} 上传参数越界：{Violations}", device.Name, string.Join("；", violations));
                    await opLogs.AddAsync("参数越界", device.Name, string.Join("；", violations),
                        true, TransferSource.SignalTrigger, 0, "PLC信号", ct).ConfigureAwait(false);
                }

                await SetBitAsync(device, device.DoneBitAddress, ct).ConfigureAwait(false);
                await opLogs.AddAsync("信号上传配方", $"{device.Name} → {doc.Name}",
                    $"{matchDesc}，更新 {updated.Count} 个变量", true, TransferSource.SignalTrigger,
                    sw.ElapsedMilliseconds, "PLC信号", ct).ConfigureAwait(false);
                state.PendingResetFail = false;
                return;
            }

            // 文件库无此配方 → 新建：按设备第一个 DB 配方为结构模板，建配方（自动写入 txt）
            var templateCandidates = await recipes.GetRecipesAsync(device.Id, ct).ConfigureAwait(false);
            var structureTemplate = templateCandidates.FirstOrDefault()
                ?? throw new InvalidOperationException("设备下没有任何配方，无法确定上传的数据结构（请先建一个配方）");
            string newDesc = $"配方名“{name}”（新配方）";

            var uploadResult = await Transfers.UploadAsync(device, structureTemplate, null, ct).ConfigureAwait(false);
            if (uploadResult.Status != TransferStatus.Success || uploadResult.ReadValues == null)
                throw new InvalidOperationException(uploadResult.Message ?? "上传读取失败");

            var created = await recipes.CreateRecipeAsync(device.Id, name, "PLC 信号触发自动创建",
                "PLC信号", CancellationToken.None).ConfigureAwait(false);
            await recipes.SaveRecipeFromAsync(created.Id, structureTemplate.Id, uploadResult.ReadValues, "PLC信号", ct).ConfigureAwait(false);

            // 参数容差：按模板配方的数据行（含上下限）对读回值做越界核对（新建配方的 created.Items 为空，不能用它）
            var newViolations = RecipeLimits.Check(structureTemplate.Items, uploadResult.ReadValues);
            if (newViolations.Count > 0)
            {
                logger.LogWarning("设备 {Device} 上传参数越界：{Violations}", device.Name, string.Join("；", newViolations));
                await opLogs.AddAsync("参数越界", device.Name, string.Join("；", newViolations),
                    true, TransferSource.SignalTrigger, 0, "PLC信号", ct).ConfigureAwait(false);
            }

            await SetBitAsync(device, device.DoneBitAddress, ct).ConfigureAwait(false);
            await opLogs.AddAsync("信号上传配方", $"{device.Name} → {created.Name}",
                $"{newDesc}，保存 {uploadResult.ReadValues.Count} 个变量", true, TransferSource.SignalTrigger,
                sw.ElapsedMilliseconds, "PLC信号", ct).ConfigureAwait(false);
            state.PendingResetFail = false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await opLogs.AddAsync("信号上传配方", device.Name, "执行失败（详见软件日志）",
                false, TransferSource.SignalTrigger, sw.ElapsedMilliseconds, "PLC信号", CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>从 PLC 的配方名字符串地址读取配方名（UTF-8，按设备配置的字数）。</summary>
    private async Task<string> ReadRecipeNameAsync(PlcDevice device, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(device.RecipeNameAddress))
            throw new InvalidOperationException("未配置配方名字符串地址");
        var addr = PlcAddressParser.Parse(device.Brand, device.RecipeNameAddress);
        if (addr.IsBitDevice)
            throw new PlcAddressException(device.RecipeNameAddress, "配方名地址必须是字地址（字符串区）");
        int words = Math.Clamp(device.RecipeNameWords <= 0 ? 8 : device.RecipeNameWords, 1, 64);
        var data = await Connections.ExecuteAsync(device,
            (c, opCt) => c.ReadWordsAsync(addr, (ushort)words, opCt), ct).ConfigureAwait(false);
        var item = new RecipeItem { Name = "配方名", Address = device.RecipeNameAddress, DataType = PlcDataType.String, StringWords = words };
        var name = ValueCodec.Decode(item, data).Trim('\0').Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("PLC 配方名为空（PLC 侧尚未写入配方名）");
        return name;
    }

    private async Task SetBitAsync(PlcDevice device, string? address, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(address)) return;
        var addr = PlcAddressParser.Parse(device.Brand, address);
        if (!addr.IsBitDevice)
            throw new PlcAddressException(address, "信号结果位地址必须是位地址");
        await Connections.ExecuteAsync(device, (c, opCt) => c.WriteBitsAsync(addr, [true], opCt), ct).ConfigureAwait(false);
    }

    private async Task TrySetFailBitAsync(PlcDevice device, CancellationToken ct)
    {
        try { await SetBitAsync(device, device.FailBitAddress, ct).ConfigureAwait(false); }
        catch (Exception ex) { logger.LogWarning(ex, "设备 {Device} 写失败位异常", device.Name); }
    }

    private async Task ClearResultBitsAsync(PlcDevice device, CancellationToken ct)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(device.DoneBitAddress))
            {
                var done = PlcAddressParser.Parse(device.Brand, device.DoneBitAddress);
                await Connections.ExecuteAsync(device, (c, opCt) => c.WriteBitsAsync(done, [false], opCt), ct).ConfigureAwait(false);
            }
            if (!string.IsNullOrWhiteSpace(device.FailBitAddress))
            {
                var fail = PlcAddressParser.Parse(device.Brand, device.FailBitAddress);
                await Connections.ExecuteAsync(device, (c, opCt) => c.WriteBitsAsync(fail, [false], opCt), ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "设备 {Device} 复位结果位异常", device.Name);
        }
    }
}
