using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Drivers;
using PlcRecipe.Infrastructure.Backup;
using PlcRecipe.Infrastructure.Data;
using PlcRecipe.Infrastructure.FileStore;
using PlcRecipe.Infrastructure.Plc;
using PlcRecipe.Infrastructure.Services;
using Serilog;

namespace PlcRecipe.Infrastructure;

/// <summary>组合根：注册全部基础设施服务。数据库引擎由设置决定（sqlite=本地默认 / mysql=MES 中央库）。</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>默认数据目录：%LOCALAPPDATA%\PlcRecipeStudio（日志 / settings.json / Pfdoc 配方文件库）</summary>
    public static string DefaultDataDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "PlcRecipeStudio");
        }
        return Path.Combine(AppContext.BaseDirectory, "data");
    }

    /// <summary>
    /// 注册数据库、PLC 连接管理、信号监视与全部业务服务。
    /// </summary>
    /// <param name="dataDirectory">数据目录（SQLite、日志、settings.json、Pfdoc 配方文件库）</param>
    /// <param name="connectionStringOverride">直接指定数据库连接串（测试用）；默认读 settings.json</param>
    public static IServiceCollection AddPlcRecipeInfrastructure(this IServiceCollection services,
        string? dataDirectory = null, string? connectionStringOverride = null)
    {
        var dataDir = dataDirectory ?? DefaultDataDirectory();
        Directory.CreateDirectory(dataDir);

        // Serilog 先于设置加载初始化：settings.json 损坏等启动期故障才有日志可查
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(dataDir, "logs", "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.Console()
            .CreateLogger();
        services.AddLogging(lb => lb.AddSerilog(dispose: true));

        // 设置先于数据库注册：数据库引擎由设置决定
        var settings = new SettingsService(dataDir);
        settings.Load();
        if (settings.LastLoadFailed)
            Log.Warning("settings.json 加载失败（已备份为 .corrupt-* 文件），本次启动使用默认设置");
        services.AddSingleton<ISettingsService>(settings);

        var dbProvider = (settings.Settings.DatabaseProvider ?? "sqlite").Trim().ToLowerInvariant();
        var connectionString = connectionStringOverride ?? settings.Settings.DatabaseConnectionString;
        if (dbProvider == "mysql")
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "DatabaseProvider=mysql 时必须先在 settings.json 填写 DatabaseConnectionString，" +
                    "例如 Server=localhost;Database=plc_recipe;Uid=root;Pwd=你的密码");
            services.AddDbContextFactory<AppDbContext>(o => o.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));
        }
        else // sqlite（本地默认，零配置开箱即用）
        {
            var dbPath = Path.Combine(dataDir, "plc-recipe.db");
            // 不用 Cache=Shared：多连接下引入表级锁（SQLITE_LOCKED 且 busy handler 不重试），
            // 并发写场景反而更容易 "database is locked"；WAL 由 DbInitializer 启动时设置
            services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
        }

        // 配方文件库（dataDir\Pfdoc\配方名.txt）：下载/信号联动按此为准
        services.AddSingleton<IRecipeFileStore>(sp => new RecipeFileStore(
            dataDir, sp.GetRequiredService<ILogger<RecipeFileStore>>()));

        // 监视服务通过惰性工厂引用连接管理器/传输服务，断开 DI 循环
        services.AddSingleton<ISignalMonitorService>(sp => new SignalMonitorService(
            () => sp.GetRequiredService<IPlcConnectionManager>(),
            () => sp.GetRequiredService<ITransferService>(),
            sp.GetRequiredService<IRecipeService>(),
            sp.GetRequiredService<IRecipeFileStore>(),
            sp.GetRequiredService<IOpLogService>(),
            sp.GetRequiredService<ILogger<SignalMonitorService>>()));

        services.AddSingleton<IPlcClientFactory, PlcClientFactory>();
        services.AddSingleton<ICurrentUserService, CurrentUserService>();
        services.AddSingleton<IPlcConnectionManager>(sp =>
        {
            var manager = new PlcConnectionManager(
                sp.GetRequiredService<IPlcClientFactory>(),
                sp.GetRequiredService<ISettingsService>(),
                sp.GetRequiredService<ILogger<PlcConnectionManager>>());
            // 连接成功 → 自动启动信号监视；断开 → 停止（回调装配，无循环依赖）
            var monitor = sp.GetRequiredService<ISignalMonitorService>();
            manager.StateChanged += (_, e) =>
            {
                if (e.State == ConnectionState.Connected)
                {
                    var device = manager.GetDeviceSnapshot(e.DeviceId);
                    if (device != null) ((SignalMonitorService)monitor).OnDeviceConnected(device);
                }
                else if (e.State is ConnectionState.Disconnected or ConnectionState.Faulted)
                {
                    _ = ((SignalMonitorService)monitor).StopMonitoringAsync(e.DeviceId);
                }
            };
            return manager;
        });

        // 业务服务
        services.AddSingleton<IDeviceService, DeviceService>();
        services.AddSingleton<IRecipeService, RecipeService>();
        services.AddSingleton<ITransferService, TransferService>();
        services.AddSingleton<ICompareService, CompareService>();
        services.AddSingleton<IExcelService, ExcelService>();
        services.AddSingleton<IUserService, UserService>();
        services.AddSingleton<IOpLogService, OpLogService>();

        // SQLite 自动备份（每日调度，随宿主启动/停止）
        services.AddSingleton<DatabaseBackupService>(sp => new DatabaseBackupService(
            sp.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            sp.GetRequiredService<ISettingsService>(),
            dataDir,
            sp.GetRequiredService<ILogger<DatabaseBackupService>>()));
        services.AddHostedService(sp => new DatabaseBackupHostedService(sp.GetRequiredService<DatabaseBackupService>()));
        return services;
    }

    /// <summary>初始化数据库（建库/建表/内置管理员，程序启动时调用一次）。
    /// 首次启动时若 Pfdoc 文件库为空且旧数据库中有配方，自动导出为配方文件库（txt 即数据库）。</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider provider, CancellationToken ct = default)
    {
        var factory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var dbCreated = await DbInitializer.InitializeAsync(db, ct).ConfigureAwait(false);

        // 一次性迁移：旧版本把配方存在数据库里，若配方文件库（Pfdoc）为空则导出（txt 即数据库）。
        // 用 PfdocSeedDone 开关保证只执行一次——否则删光配方后重启会把已删除的配方复活。
        // 先执行平铺布局迁移（旧版把 txt 放在 Pfdoc 根目录，现按 Device= 头移入设备子目录）。
        var settings = provider.GetRequiredService<ISettingsService>();
        var fileStore = provider.GetRequiredService<IRecipeFileStore>();
        fileStore.MigrateFlatLayout();
        var hasAnyTxt = System.IO.Directory.Exists(fileStore.Directory)
                        && System.IO.Directory.EnumerateFiles(fileStore.Directory, "*.txt").Any();
        if (!settings.Settings.PfdocSeedDone)
        {
            if (!hasAnyTxt)
            {
                var legacy = await db.Recipes.AsNoTracking()
                    .Include(r => r.Items).Include(r => r.Device)
                    .ToListAsync(ct).ConfigureAwait(false);
                if (legacy.Count > 0)
                {
                    foreach (var r in legacy)
                        await fileStore.SaveAsync(r.Device?.Name ?? "未分配", r.Name, r.Version, r.Items).ConfigureAwait(false);
                    Log.Information("已将数据库中 {Count} 个配方导出到配方文件库（Pfdoc）", legacy.Count);
                }
            }
            settings.Settings.PfdocSeedDone = true;
            settings.Save();
        }

        // 时间口径统一为 UTC：旧版本写入的是本地时间，按本机时区一次性平移（受设置开关保护，只执行一次）。
        // 本次启动新建的库（全新安装）数据本就是 UTC，跳过平移，只落标记——否则内置管理员的时间会被错误偏移
        if (!settings.Settings.UtcTimeMigrated && db.Database.IsSqlite())
        {
            if (!dbCreated)
            {
                var shift = -TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalHours;
                var modifier = $"{shift:+0.#;-0.#} hours";
                // 值为程序内部计算的时区偏移（非用户输入），参数化传递
                await db.Database.ExecuteSqlRawAsync("UPDATE OpLogs SET Time = datetime(Time, {0})", modifier).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync("UPDATE Recipes SET CreatedAt = datetime(CreatedAt, {0}), UpdatedAt = datetime(UpdatedAt, {0})", modifier).ConfigureAwait(false);
                await db.Database.ExecuteSqlRawAsync("UPDATE Users SET CreatedAt = datetime(CreatedAt, {0})", modifier).ConfigureAwait(false);
                Log.Information("存量时间已按 {Modifier} 平移为 UTC", modifier);
            }
            settings.Settings.UtcTimeMigrated = true;
            settings.Save();
        }
    }

    /// <summary>程序退出时释放全部 PLC 连接与监视任务。</summary>
    public static async Task ShutdownPlcAsync(this IServiceProvider provider)
    {
        var monitor = provider.GetRequiredService<ISignalMonitorService>();
        await monitor.StopAllAsync().ConfigureAwait(false);
        var manager = provider.GetRequiredService<IPlcConnectionManager>();
        await manager.ShutdownAsync().ConfigureAwait(false);
        await Log.CloseAndFlushAsync().ConfigureAwait(false);
    }
}
