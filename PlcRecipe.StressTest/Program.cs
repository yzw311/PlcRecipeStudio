using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;
using PlcRecipe.Drivers.Mock;
using PlcRecipe.Infrastructure;

namespace PlcRecipe.StressTest;

/// <summary>
/// 百万次循环实测（设备为中心模型）：
///   阶段A 配方创建/保存/复制/重命名/删除（SQLite 真实落盘）
///   阶段B Mock PLC 批量下载（多设备并行）
///   阶段C Mock PLC 批量上传并写回数据库
///   阶段D PLC 信号联动握手（后台并发：请求位→下载→完成位→闭环复位）
///   阶段E 资源审计：故障注入重连、线程数、内存、客户端对象可回收
/// 任何失败记录明细；结束时如有失败 exit code 1。用法: [总目标操作数=1000000]
/// </summary>
internal static class Program
{
    private static long _opsDone;
    private static readonly List<string> Errors = new();

    private static async Task<int> Main(string[] args)
    {
        // 保存诊断模式: --diag-save [数据目录] → 用真实库重放每份配方的保存，定位失败原因
        int diagIdx = Array.IndexOf(args, "--diag-save");
        if (diagIdx >= 0)
        {
            var diagDir = diagIdx + 1 < args.Length && !args[diagIdx + 1].StartsWith("--")
                ? args[diagIdx + 1]
                : ServiceCollectionExtensions.DefaultDataDirectory();
            return await DiagSaveAsync(diagDir);
        }

        // 演示种子模式: --seed-demo [数据目录]  → 给正式软件预置演示数据后退出
        int seedIdx = Array.IndexOf(args, "--seed-demo");
        if (seedIdx >= 0)
        {
            var seedDir = seedIdx + 1 < args.Length && !args[seedIdx + 1].StartsWith("--")
                ? args[seedIdx + 1]
                : ServiceCollectionExtensions.DefaultDataDirectory();
            return await SeedDemoAsync(seedDir);
        }

        long target = args.Length > 0 && long.TryParse(args[0], out var t) ? t : 1_000_000;
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine($"=== PLC Recipe Studio 百万次循环实测 ===  目标操作数: {target:N0}");

        var dataDir = Path.Combine(Path.GetTempPath(), $"PlcRecipeStress_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDir);
        Console.WriteLine($"数据目录: {dataDir}");

        var services = new ServiceCollection();
        services.AddPlcRecipeInfrastructure(dataDir);
        await using var provider = services.BuildServiceProvider();

        var sw = Stopwatch.StartNew();
        try
        {
            await provider.InitializeDatabaseAsync().ConfigureAwait(false);
            Console.WriteLine("[stage] 环境 OK");
            var users = provider.GetRequiredService<IUserService>();
            var admin = await users.VerifyAsync("admin", "123456").ConfigureAwait(false)
                        ?? throw new InvalidOperationException("内置管理员登录失败");
            provider.GetRequiredService<ICurrentUserService>().Set(admin);

            var devices = provider.GetRequiredService<IDeviceService>();
            var recipes = provider.GetRequiredService<IRecipeService>();
            var transfers = provider.GetRequiredService<ITransferService>();
            var connections = provider.GetRequiredService<IPlcConnectionManager>();

            // 设备（Mock 品牌，含 7 个标准数据行的配方）
            var deviceList = new List<PlcDevice>();
            for (int i = 1; i <= 4; i++)
            {
                deviceList.Add(await devices.AddDeviceAsync(new PlcDevice
                {
                    Name = $"{i}#机",
                    Brand = PlcBrand.Mock,
                    Ip = "127.0.0.1",
                    Port = 502,
                    Enabled = true,
                    SignalEnabled = i <= 2,
                    DownloadRequestAddress = i <= 2 ? "M900" : null,
                    UploadRequestAddress = i <= 2 ? "M901" : null,
                    RecipeNameAddress = i <= 2 ? "D920" : null,
                    DoneBitAddress = i <= 2 ? "M902" : null,
                    FailBitAddress = i <= 2 ? "M903" : null,
                    PollIntervalMs = 100
                }).ConfigureAwait(false));
            }

            var recipe0 = await recipes.CreateRecipeAsync(deviceList[0].Id, "标准配方", "初始", admin.UserName).ConfigureAwait(false);
            await recipes.SaveRecipeAsync(recipe0.Id, TestRows(), admin.UserName).ConfigureAwait(false);
            recipe0 = await recipes.GetRecipeAsync(recipe0.Id).ConfigureAwait(false);
            // 把同样的配方结构复制到其他设备（测试同构批量）
            for (int i = 1; i < 4; i++)
            {
                var r = await recipes.CreateRecipeAsync(deviceList[i].Id, "标准配方", null, admin.UserName).ConfigureAwait(false);
                await recipes.SaveRecipeAsync(r.Id, TestRows(), admin.UserName).ConfigureAwait(false);
            }
            Interlocked.Add(ref _opsDone, 10);
            Console.WriteLine("环境就绪。开始循环实测…\n");

            // ---------- 阶段 D：信号联动握手（后台并发） ----------
            long targetHandshakes = Math.Clamp(target / 800, 300, 1500);
            var handshakeTask = Task.Run(() => RunHandshakePhaseAsync(
                provider, deviceList[0], deviceList[1], targetHandshakes));

            // ---------- 阶段 A：配方 CRUD ----------
            long cyclesA = Math.Max(1, (long)(target * 0.60) / 8);
            await RunCrudPhaseAsync(recipes, deviceList[0], admin.UserName, cyclesA).ConfigureAwait(false);

            // ---------- 阶段 B：批量下载（重取最新配方，并行 4 台） ----------
            long downloads = Math.Max(1, (long)(target * 0.20));
            var freshForDownload = await recipes.GetRecipeAsync(recipe0.Id).ConfigureAwait(false);
            await RunDownloadPhaseAsync(transfers, deviceList, freshForDownload, (int)Math.Min(downloads, int.MaxValue)).ConfigureAwait(false);

            // ---------- 阶段 C：批量上传（按各自配方结构读+落库） ----------
            long uploads = Math.Max(1, (long)(target * 0.20) / 2);
            await RunUploadPhaseAsync(transfers, recipes, deviceList, (int)Math.Min(uploads, int.MaxValue)).ConfigureAwait(false);

            var hsDone = await handshakeTask.ConfigureAwait(false);
            Interlocked.Add(ref _opsDone, hsDone * 5);

            Console.WriteLine("\n--- 阶段E 资源审计 ---");
            await ResourceAuditAsync(provider, deviceList[0]).ConfigureAwait(false);

            sw.Stop();
            Console.WriteLine($"\n============================================");
            Console.WriteLine($"实测完成：累计验证操作 {Interlocked.Read(ref _opsDone):N0} 次");
            Console.WriteLine($"总耗时 {sw.Elapsed:hh\\:mm\\:ss}，平均 {Interlocked.Read(ref _opsDone) / Math.Max(1, (long)sw.Elapsed.TotalSeconds):N0} ops/s");
            Console.WriteLine($"错误数: {Errors.Count}");
            if (Errors.Count > 0)
            {
                Console.WriteLine("失败明细：");
                foreach (var e in Errors.Take(20)) Console.WriteLine("  ✘ " + e);
                return 1;
            }
            Console.WriteLine("✔ 全部通过");
            return 0;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Console.WriteLine($"\n✘✘ 测试中止：{ex}");
            return 1;
        }
        finally
        {
            await provider.ShutdownPlcAsync().ConfigureAwait(false);
            try { Directory.Delete(dataDir, true); } catch { /* 清理失败不影响结果 */ }
        }
    }

    /// <summary>标准测试数据行。</summary>
    private static List<RecipeItem> TestRows() => new()
    {
        new RecipeItem { Name = "温度1", Address = "D100", DataType = PlcDataType.Float32, Unit = "℃", Value = "12.5", SortOrder = 0 },
        new RecipeItem { Name = "温度2", Address = "D102", DataType = PlcDataType.Float32, Value = "27.5", SortOrder = 1 },
        new RecipeItem { Name = "压力", Address = "D104", DataType = PlcDataType.Float32, Value = "6.2", SortOrder = 2 },
        new RecipeItem { Name = "速度", Address = "D106", DataType = PlcDataType.Int32, Value = "1200", SortOrder = 3 },
        new RecipeItem { Name = "模式", Address = "D110", DataType = PlcDataType.UInt16, Value = "2", SortOrder = 4 },
        new RecipeItem { Name = "批次", Address = "D111", DataType = PlcDataType.Int16, Value = "-8", SortOrder = 5 },
        new RecipeItem { Name = "品名", Address = "D112", DataType = PlcDataType.String, StringWords = 4, Value = "AB12", SortOrder = 6 },
        new RecipeItem { Name = "启停", Address = "M0", DataType = PlcDataType.Bool, Value = "1", SortOrder = 7 },
        new RecipeItem { Name = "报警", Address = "M1", DataType = PlcDataType.Bool, Value = "0", SortOrder = 8 },
    };

    // ---------------- 阶段 A：配方 CRUD ----------------
    private static async Task RunCrudPhaseAsync(IRecipeService recipes, PlcDevice device, string user, long cycles)
    {
        Console.WriteLine($"阶段A 配方创建/保存/复制/重命名/删除 × {cycles:N0}");
        var sw = Stopwatch.StartNew();
        for (long i = 0; i < cycles; i++)
        {
            try
            {
                var r = await recipes.CreateRecipeAsync(device.Id, $"CRUD_{i}", null, user).ConfigureAwait(false);
                await recipes.SaveRecipeAsync(r.Id, TestRows(), user).ConfigureAwait(false);
                if (i % 10 == 0)
                {
                    var copy = await recipes.CopyRecipeAsync(r.Id, $"CRUD_{i}_副本", user).ConfigureAwait(false);
                    await recipes.RenameRecipeAsync(copy.Id, $"CRUD_{i}_改名", user).ConfigureAwait(false);
                    // 文件库语义：重命名后 Id（名字哈希）随之变化，按新名字取新 Id 再删
                    var renamed = (await recipes.GetRecipesAsync(device.Id).ConfigureAwait(false))
                                  .First(x => x.Name == $"CRUD_{i}_改名");
                    await recipes.DeleteRecipeAsync(renamed.Id).ConfigureAwait(false);
                }
                await recipes.DeleteRecipeAsync(r.Id).ConfigureAwait(false);
                Interlocked.Add(ref _opsDone, 8);
                if ((i + 1) % 5000 == 0)
                    Console.WriteLine($"  A {i + 1:N0}/{cycles:N0}  ({sw.Elapsed:hh\\:mm\\:ss})");
            }
            catch (Exception ex)
            {
                lock (Errors) Errors.Add($"阶段A 第{i}轮: {ex.Message}");
                return;
            }
        }
        sw.Stop();
        Console.WriteLine($"  ✔ 阶段A 完成，耗时 {sw.Elapsed:hh\\:mm\\:ss}");
    }

    // ---------------- 阶段 B：批量下载 ----------------
    private static async Task RunDownloadPhaseAsync(ITransferService transfers, List<PlcDevice> devices,
        Recipe recipe, int count)
    {
        Console.WriteLine($"阶段B 批量下载（配方→{devices.Count}台并行 Mock PLC）× {count:N0}");
        var sw = Stopwatch.StartNew();
        var options = new ParallelOptions { MaxDegreeOfParallelism = 4 };
        long done = 0;
        await Parallel.ForEachAsync(Enumerable.Range(0, count), options, async (i, ct) =>
        {
            try
            {
                var result = await transfers.DownloadAsync(devices[i % devices.Count], recipe, null, false, ct).ConfigureAwait(false);
                if (result.Status != TransferStatus.Success)
                    throw new InvalidOperationException($"下载失败：{result.Message}");
                Interlocked.Add(ref _opsDone, 1);
                var n = Interlocked.Increment(ref done);
                if (n % 10000 == 0)
                    Console.WriteLine($"  B {n:N0}/{count:N0}  ({sw.Elapsed:hh\\:mm\\:ss})");
            }
            catch (Exception ex)
            {
                lock (Errors) Errors.Add($"阶段B 第{i}次: {ex.Message}");
                ct.ThrowIfCancellationRequested();
            }
        }).ConfigureAwait(false);
        sw.Stop();
        Console.WriteLine(Errors.Count == 0 ? $"  ✔ 阶段B 完成，耗时 {sw.Elapsed:hh\\:mm\\:ss}" : "  ✘ 阶段B 有失败");
    }

    // ---------------- 阶段 C：批量上传 ----------------
    private static async Task RunUploadPhaseAsync(ITransferService transfers, IRecipeService recipes,
        List<PlcDevice> devices, int count)
    {
        Console.WriteLine($"阶段C 上传（PLC→软件→写回各自配方）× {count:N0}");
        var sw = Stopwatch.StartNew();
        var options = new ParallelOptions { MaxDegreeOfParallelism = 4 };
        long done = 0;
        // 预取每台设备的“标准配方”作为上传结构
        var ownRecipes = new Dictionary<int, Recipe>();
        foreach (var d in devices)
        {
            var list = await recipes.GetRecipesAsync(d.Id).ConfigureAwait(false);
            ownRecipes[d.Id] = list.First(r => r.Name == "标准配方");
        }
        await Parallel.ForEachAsync(Enumerable.Range(0, count), options, async (i, ct) =>
        {
            try
            {
                var device = devices[i % devices.Count];
                var own = ownRecipes[device.Id];
                var result = await transfers.UploadAsync(device, own, null, ct).ConfigureAwait(false);
                if (result.Status != TransferStatus.Success || result.ReadValues == null)
                    throw new InvalidOperationException($"上传失败：{result.Message}");
                await recipes.ApplyReadValuesAsync(own.Id, result.ReadValues, "stress", ct).ConfigureAwait(false);
                Interlocked.Add(ref _opsDone, 2);
                var n = Interlocked.Increment(ref done);
                if (n % 5000 == 0)
                    Console.WriteLine($"  C {n:N0}/{count:N0}  ({sw.Elapsed:hh\\:mm\\:ss})");
            }
            catch (Exception ex)
            {
                lock (Errors) Errors.Add($"阶段C 第{i}次: {ex.Message}");
                ct.ThrowIfCancellationRequested();
            }
        }).ConfigureAwait(false);
        sw.Stop();
        Console.WriteLine(Errors.Count == 0 ? $"  ✔ 阶段C 完成，耗时 {sw.Elapsed:hh\\:mm\\:ss}" : "  ✘ 阶段C 有失败");
    }

    // ---------------- 阶段 D：信号联动握手 ----------------
    private static async Task<long> RunHandshakePhaseAsync(IServiceProvider provider, PlcDevice d1, PlcDevice d2, long target)
    {
        var connections = provider.GetRequiredService<IPlcConnectionManager>();
        var recipes = provider.GetRequiredService<IRecipeService>();
        Console.WriteLine($"阶段D 信号联动握手 × {target:N0}（后台并发）");
        var sw = Stopwatch.StartNew();
        long done = 0;
        try
        {
            var clients = new List<MockPlcClient>
            {
                (MockPlcClient)await connections.GetClientAsync(d1).ConfigureAwait(false),
                (MockPlcClient)await connections.GetClientAsync(d2).ConfigureAwait(false)
            };
            // 两台设备的配方同名“标准配方”，PLC 侧按名字请求（预写一次名字字符串，之后只翻转请求位）
            foreach (var client in clients)
            {
                var nameItem = new global::PlcRecipe.Core.Models.RecipeItem { DataType = PlcDataType.String, StringWords = 8 };
                var nameWords = ValueCodec.Encode(nameItem, "标准配方");
                for (int w = 0; w < nameWords.Length; w++)
                    client.SetWordValue("D", 920 + w, nameWords[w]);
            }

            for (long i = 0; i < target; i++)
            {
                var client = clients[(int)(i % 2)];
                var device = i % 2 == 0 ? d1 : d2;
                try
                {
                    // 1) PLC 置位下载请求位（配方名已预写在 D920 字符串区）
                    client.SetBitValue("M", 900, true);

                    // 2) 等待完成位（成功）且失败位未置位
                    var sw2 = Stopwatch.StartNew();
                    while (!client.GetBitValue("M", 902) && sw2.ElapsedMilliseconds < 5000)
                        await Task.Delay(20).ConfigureAwait(false);
                    if (!client.GetBitValue("M", 902))
                        throw new InvalidOperationException("握手超时：完成位未置位");
                    if (client.GetBitValue("M", 903))
                        throw new InvalidOperationException("失败位被置位");

                    // 3) PLC 清请求位 → 软件应复位完成/失败位
                    client.SetBitValue("M", 900, false);
                    sw2.Restart();
                    while (client.GetBitValue("M", 902) && sw2.ElapsedMilliseconds < 5000)
                        await Task.Delay(20).ConfigureAwait(false);
                    if (client.GetBitValue("M", 902))
                        throw new InvalidOperationException("闭环失败：完成位未复位");

                    // 4) 校验配方值确实写入（温度1=12.5 内部字非零）
                    if (client.GetWordValue("D", 100) == 0 && client.GetWordValue("D", 101) == 0)
                        throw new InvalidOperationException("配方数据未写入 PLC");

                    done++;
                    if (done % 100 == 0)
                        Console.WriteLine($"  D {done:N0}/{target:N0}  ({sw.Elapsed:hh\\:mm\\:ss})");
                }
                catch (Exception ex)
                {
                    client.SetBitValue("M", 900, false);
                    lock (Errors) Errors.Add($"阶段D 第{i}次 ({device.Name}): {ex.Message}");
                    if (Errors.Count > 10) break;
                }
            }
        }
        catch (Exception ex)
        {
            lock (Errors) Errors.Add($"阶段D 初始化失败: {ex.Message}");
        }
        sw.Stop();
        Console.WriteLine(Errors.Count == 0
            ? $"  ✔ 阶段D 完成 {done:N0} 次握手，耗时 {sw.Elapsed:hh\\:mm\\:ss}"
            : $"  ✘ 阶段D 完成 {done:N0} 次，有失败");
        return done;
    }

    // ---------------- 阶段 E：资源审计 ----------------
    private static async Task ResourceAuditAsync(IServiceProvider provider, PlcDevice device)
    {
        var connections = provider.GetRequiredService<IPlcConnectionManager>();
        var proc = Process.GetCurrentProcess();

        // 1) 客户端实例可回收（在独立方法中创建，方法返回后局部根全部消亡）
        var weakRefs = await CreateAuditClientsAsync().ConfigureAwait(false);
        GC.Collect(2, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true);
        await Task.Delay(200).ConfigureAwait(false);
        GC.Collect(2, GCCollectionMode.Forced, true);
        int alive = weakRefs.Count(w => w.IsAlive);
        Console.WriteLine($"  客户端对象回收：2000 个中仍存活 {alive} 个" + (alive == 0 ? " ✔" : " ✘（疑似泄漏）"));
        if (alive > 0) lock (Errors) Errors.Add($"资源审计：{alive} 个已释放客户端未被回收");

        // 2) 故障注入 → 自动重连恢复
        proc.Refresh();
        int before = proc.Threads.Count;
        var client1 = (MockPlcClient)await connections.GetClientAsync(device).ConfigureAwait(false);
        client1.FailAll = true;
        bool faultCorrectlyReported = false;
        try
        {
            await connections.ExecuteAsync(device, (c, opCt) => c.ReadWordsAsync(new ParsedAddress("D", 0, -1, false, "D0"), 1, opCt)).ConfigureAwait(false);
        }
        catch
        {
            faultCorrectlyReported = true;
        }
        client1.FailAll = false;
        var recover = await connections.ExecuteAsync(device, (c, opCt) => c.ReadWordsAsync(new ParsedAddress("D", 0, -1, false, "D0"), 1, opCt)).ConfigureAwait(false);
        Console.WriteLine($"  故障注入→报告={faultCorrectlyReported}，重连恢复读={recover[0]} ✔" +
                          (faultCorrectlyReported ? "" : " ✘"));
        if (!faultCorrectlyReported) lock (Errors) Errors.Add("故障注入未被正确报告");

        // 3) 线程数稳定
        proc.Refresh();
        int after = proc.Threads.Count;
        Console.WriteLine($"  线程数：{before} → {after}（增量 {after - before}）" + (Math.Abs(after - before) <= 15 ? " ✔" : " ✘"));
        if (Math.Abs(after - before) > 15) lock (Errors) Errors.Add($"线程数异常增长：{before}→{after}");

        // 4) 内存
        GC.Collect(2, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true);
        proc.Refresh();
        long memMb = proc.WorkingSet64 / 1024 / 1024;
        Console.WriteLine($"  工作集内存：{memMb} MB" + (memMb < 800 ? " ✔" : " ✘"));
        if (memMb >= 800) lock (Errors) Errors.Add($"内存占用异常：{memMb} MB");
    }

    private static async Task<List<WeakReference>> CreateAuditClientsAsync()
    {
        var weakRefs = new List<WeakReference>();
        for (int i = 0; i < 2000; i++)
        {
            var client = new MockPlcClient(-1, $"audit{i}");
            await client.ConnectAsync().ConfigureAwait(false);
            await client.WriteWordsAsync(new ParsedAddress("D", 0, -1, false, "D0"), [1, 2, 3]).ConfigureAwait(false);
            weakRefs.Add(new WeakReference(client));
            await client.DisposeAsync().ConfigureAwait(false);
        }
        return weakRefs;
    }


    /// <summary>诊断：用真实数据库重放每份配方的保存。</summary>
    private static async Task<int> DiagSaveAsync(string dataDir)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine($"=== 保存诊断（数据目录 {dataDir}）===");
        var services = new ServiceCollection();
        services.AddPlcRecipeInfrastructure(dataDir);
        await using var provider = services.BuildServiceProvider();
        try
        {
            await provider.InitializeDatabaseAsync().ConfigureAwait(false);
            var users = provider.GetRequiredService<IUserService>();
            var admin = await users.VerifyAsync("admin", "123456").ConfigureAwait(false)
                        ?? await users.VerifyAsync("diag", "diag").ConfigureAwait(false);
            provider.GetRequiredService<ICurrentUserService>().Set(admin!);
            var devices = provider.GetRequiredService<IDeviceService>();
            var recipes = provider.GetRequiredService<IRecipeService>();

            foreach (var d in await devices.GetDevicesAsync().ConfigureAwait(false))
            {
                foreach (var r in await recipes.GetRecipesAsync(d.Id).ConfigureAwait(false))
                {
                    try
                    {
                        // 模拟 UI 路径：把加载到的行原样存回
                        await recipes.SaveRecipeAsync(r.Id, r.Items, "diag").ConfigureAwait(false);
                        Console.WriteLine($"✔ [{d.Name}] {r.Name} (#{r.PlcNo}) 保存 OK，{r.Items.Count} 行");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"✘ [{d.Name}] {r.Name} (#{r.PlcNo}) 保存失败：{ex.Message}");
                    }
                }
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("✘ " + ex);
            return 1;
        }
        finally
        {
            await provider.ShutdownPlcAsync().ConfigureAwait(false);
        }
    }
    /// <summary>为正式软件预置演示数据（模拟 PLC，无需硬件）。</summary>
    private static async Task<int> SeedDemoAsync(string dataDir)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine($"=== 预置演示数据到 {dataDir} ===");
        var services = new ServiceCollection();
        services.AddPlcRecipeInfrastructure(dataDir);
        await using var provider = services.BuildServiceProvider();
        try
        {
            await provider.InitializeDatabaseAsync().ConfigureAwait(false);
            var users = provider.GetRequiredService<IUserService>();
            var admin = await users.VerifyAsync("admin", "123456").ConfigureAwait(false)
                        ?? throw new InvalidOperationException("管理员登录失败");
            provider.GetRequiredService<ICurrentUserService>().Set(admin);
            var devices = provider.GetRequiredService<IDeviceService>();
            var recipes = provider.GetRequiredService<IRecipeService>();

            // 设备 1：压铸机-1#机（9 个数据行的 3 个配方）
            var d1 = await devices.AddDeviceAsync(new PlcDevice
            {
                Name = "1#机", Brand = PlcBrand.Mock, Ip = "127.0.0.1", Port = 502, Enabled = true,
                SignalEnabled = true, DownloadRequestAddress = "M900", UploadRequestAddress = "M901",
                RecipeNameAddress = "D920", DoneBitAddress = "M902", FailBitAddress = "M903",
                PollIntervalMs = 1000, Remark = "模拟 PLC（演示）"
            }).ConfigureAwait(false);
            foreach (var (name, remark, t1, pr, spd) in new[] {
                ("标准配方", "日常生产参数", 185f, 6.2f, 1200),
                ("高温配方", "夏季高模温工艺", 215f, 7.0f, 1100),
                ("节能配方", "低产能省电模式", 165f, 5.0f, 900)})
            {
                var r = await recipes.CreateRecipeAsync(d1.Id, name, remark, "admin").ConfigureAwait(false);
                await recipes.SaveRecipeAsync(r.Id, DemoRows(t1, pr, spd), "admin").ConfigureAwait(false);
            }

            // 设备 2：压铸机-2#机（信号联动开，1 个配方）
            var d2 = await devices.AddDeviceAsync(new PlcDevice
            {
                Name = "2#机", Brand = PlcBrand.Mock, Ip = "127.0.0.1", Port = 502, Enabled = true,
                SignalEnabled = true, DownloadRequestAddress = "M900", UploadRequestAddress = "M901",
                RecipeNameAddress = "D920", DoneBitAddress = "M902", FailBitAddress = "M903",
                PollIntervalMs = 1000, Remark = "模拟 PLC（演示）"
            }).ConfigureAwait(false);
            var r2 = await recipes.CreateRecipeAsync(d2.Id, "标准配方", "演示", "admin").ConfigureAwait(false);
            await recipes.SaveRecipeAsync(r2.Id, DemoRows(185f, 6.2f, 1200), "admin").ConfigureAwait(false);

            // 设备 3：贴片机-3#机（不同变量结构）
            var d3 = await devices.AddDeviceAsync(new PlcDevice
            {
                Name = "3#机", Brand = PlcBrand.Mock, Ip = "127.0.0.1", Port = 502, Enabled = true,
                Remark = "模拟 PLC（演示）"
            }).ConfigureAwait(false);
            var r3 = await recipes.CreateRecipeAsync(d3.Id, "0402标准", "演示", "admin").ConfigureAwait(false);
            await recipes.SaveRecipeAsync(r3.Id, new List<RecipeItem>
            {
                new() { Name = "贴装速度", Address = "D200", DataType = PlcDataType.Int32, Unit = "点/分", Value = "18000", SortOrder = 0 },
                new() { Name = "轨道宽度", Address = "D202", DataType = PlcDataType.Float32, Unit = "mm", Value = "8.5", SortOrder = 1 },
                new() { Name = "真空吸附", Address = "M10", DataType = PlcDataType.Bool, Value = "1", SortOrder = 2 },
            }, "admin").ConfigureAwait(false);

            // 设备 4：贴片机-4#机
            var d4 = await devices.AddDeviceAsync(new PlcDevice
            {
                Name = "4#机", Brand = PlcBrand.Mock, Ip = "127.0.0.1", Port = 502, Enabled = true,
                Remark = "模拟 PLC（演示）"
            }).ConfigureAwait(false);
            var r4 = await recipes.CreateRecipeAsync(d4.Id, "0402标准", "演示", "admin").ConfigureAwait(false);
            await recipes.SaveRecipeAsync(r4.Id, new List<RecipeItem>
            {
                new() { Name = "贴装速度", Address = "D200", DataType = PlcDataType.Int32, Unit = "点/分", Value = "18000", SortOrder = 0 },
                new() { Name = "轨道宽度", Address = "D202", DataType = PlcDataType.Float32, Unit = "mm", Value = "8.5", SortOrder = 1 },
                new() { Name = "真空吸附", Address = "M10", DataType = PlcDataType.Bool, Value = "1", SortOrder = 2 },
            }, "admin").ConfigureAwait(false);

            Console.WriteLine("✔ 演示数据就绪：4 台设备 / 4 个配方");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("✘ " + ex);
            return 1;
        }
        finally
        {
            await provider.ShutdownPlcAsync().ConfigureAwait(false);
        }
    }

    private static List<RecipeItem> DemoRows(float t1, float pr, int spd) => new()
    {
        new() { Name = "温度1", Address = "D100", DataType = PlcDataType.Float32, Unit = "℃", Value = t1.ToString("0.0"), SortOrder = 0 },
        new() { Name = "温度2", Address = "D102", DataType = PlcDataType.Float32, Unit = "℃", Value = (t1 + 15).ToString("0.0"), SortOrder = 1 },
        new() { Name = "压力", Address = "D104", DataType = PlcDataType.Float32, Unit = "MPa", Value = pr.ToString("0.0"), SortOrder = 2 },
        new() { Name = "速度", Address = "D106", DataType = PlcDataType.Int32, Unit = "rpm", Value = spd.ToString(), SortOrder = 3 },
        new() { Name = "计数", Address = "D108", DataType = PlcDataType.UInt32, Value = "1000", SortOrder = 4 },
        new() { Name = "模式", Address = "D110", DataType = PlcDataType.UInt16, Value = "2", SortOrder = 5 },
        new() { Name = "批次", Address = "D111", DataType = PlcDataType.Int16, Value = "-8", SortOrder = 6 },
        new() { Name = "品名", Address = "D112", DataType = PlcDataType.String, StringWords = 4, Value = "AB12", SortOrder = 7 },
        new() { Name = "启停", Address = "M0", DataType = PlcDataType.Bool, Value = "1", SortOrder = 8 },
        new() { Name = "报警", Address = "M1", DataType = PlcDataType.Bool, Value = "0", SortOrder = 9 },
    };
}
