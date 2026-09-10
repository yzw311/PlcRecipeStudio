using Microsoft.Extensions.DependencyInjection;
using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;
using PlcRecipe.Infrastructure;
using PlcRecipe.Drivers;
using PlcRecipe.Drivers.Mock;
using PlcRecipe.Infrastructure.Data;

namespace PlcRecipe.Tests;

/// <summary>测试公共环境：临时 SQLite 库（零外部依赖，全测试可跑） + 完整 DI + 管理员登录。</summary>
public sealed class TestHost : IAsyncDisposable
{
    public IServiceProvider Provider { get; }
    public string DataDir { get; }
    public IDeviceService Devices { get; }
    public IRecipeService Recipes { get; }
    public ITransferService Transfers { get; }
    public ICompareService Compare { get; }
    public IPlcConnectionManager Connections { get; }
    public IUserService Users { get; }
    public IOpLogService OpLogs { get; }
    public IRecipeFileStore FileStore { get; }

    private TestHost(IServiceProvider provider, string dataDir)
    {
        Provider = provider;
        DataDir = dataDir;
        Devices = provider.GetRequiredService<IDeviceService>();
        Recipes = provider.GetRequiredService<IRecipeService>();
        Transfers = provider.GetRequiredService<ITransferService>();
        Compare = provider.GetRequiredService<ICompareService>();
        Connections = provider.GetRequiredService<IPlcConnectionManager>();
        Users = provider.GetRequiredService<IUserService>();
        OpLogs = provider.GetRequiredService<IOpLogService>();
        FileStore = provider.GetRequiredService<IRecipeFileStore>();
    }

    public static async Task<TestHost> CreateAsync(string? dataDir = null)
    {
        dataDir ??= Path.Combine(Path.GetTempPath(), $"PlcRecipeTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDir);
        var services = new ServiceCollection();
        services.AddPlcRecipeInfrastructure(dataDir); // 默认 SQLite：临时单文件库，零外部依赖
        var provider = services.BuildServiceProvider();
        var previous = Environment.GetEnvironmentVariable(DbInitializer.InitialAdminPasswordEnvironmentVariable);
        Environment.SetEnvironmentVariable(DbInitializer.InitialAdminPasswordEnvironmentVariable, "Test-only-Admin-Password-123!");
        try
        {
            await provider.InitializeDatabaseAsync().ConfigureAwait(false);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DbInitializer.InitialAdminPasswordEnvironmentVariable, previous);
        }

        var users = provider.GetRequiredService<IUserService>();
        var admin = await users.VerifyAsync(DbInitializer.DefaultAdminName, "Test-only-Admin-Password-123!").ConfigureAwait(false)
                    ?? throw new InvalidOperationException("管理员登录失败");
        provider.GetRequiredService<ICurrentUserService>().Set(admin);
        return new TestHost(provider, dataDir);
    }

    /// <summary>创建模拟 PLC 设备（可选信号联动配置，纯配方名匹配模式）。</summary>
    public async Task<PlcDevice> CreateMockDeviceAsync(string name, bool signal = false, string? recipeNameAddress = null)
    {
        return await Devices.AddDeviceAsync(new PlcDevice
        {
            Name = name,
            Brand = PlcBrand.Mock,
            Ip = "127.0.0.1",
            Port = 502,
            Enabled = true,
            SignalEnabled = signal,
            DownloadRequestAddress = signal ? "M900" : null,
            UploadRequestAddress = signal ? "M901" : null,
            RecipeNameAddress = recipeNameAddress ?? (signal ? "D920" : null),
            RecipeNameWords = 8,
            DoneBitAddress = signal ? "M902" : null,
            FailBitAddress = signal ? "M903" : null,
            PollIntervalMs = 100
        }).ConfigureAwait(false);
    }

    /// <summary>把 ASCII 名字写入模拟 PLC 的字符串区（模拟 PLC 侧写配方名）。</summary>
    public static void WriteRecipeName(MockPlcClient client, string area, int startWord, string name, int words = 8)
    {
        var item = new RecipeItem { Name = "n", Address = area + startWord, DataType = PlcDataType.String, StringWords = words };
        var encoded = ValueCodec.Encode(item, name);
        for (int i = 0; i < words; i++)
            client.SetWordValue(area, startWord + i, i < encoded.Length ? encoded[i] : (ushort)0);
    }

    /// <summary>为设备创建带标准数据行的配方。</summary>
    public async Task<Recipe> CreateRecipeWithRowsAsync(PlcDevice device, string name)
    {
        var recipe = await Recipes.CreateRecipeAsync(device.Id, name, null, "admin").ConfigureAwait(false);
        var rows = StandardRows();
        await Recipes.SaveRecipeAsync(recipe.Id, rows, "admin").ConfigureAwait(false);
        return await Recipes.GetRecipeAsync(recipe.Id).ConfigureAwait(false);
    }

    /// <summary>标准测试数据行（与 Mock 地址约定一致）。</summary>
    public static List<RecipeItem> StandardRows() => new()
    {
        new RecipeItem { Name = "温度", Address = "D100", DataType = PlcDataType.Float32, Unit = "℃", Value = "12.5", SortOrder = 0 },
        new RecipeItem { Name = "速度", Address = "D102", DataType = PlcDataType.Int32, Unit = "rpm", Value = "100000", SortOrder = 1 },
        new RecipeItem { Name = "模式", Address = "D104", DataType = PlcDataType.UInt16, Value = "3", SortOrder = 2 },
        new RecipeItem { Name = "批次", Address = "D105", DataType = PlcDataType.Int16, Value = "-50", SortOrder = 3 },
        new RecipeItem { Name = "品名", Address = "D106", DataType = PlcDataType.String, StringWords = 4, Value = "AB12", SortOrder = 4 },
        new RecipeItem { Name = "启停", Address = "M0", DataType = PlcDataType.Bool, Value = "1", SortOrder = 5 },
        new RecipeItem { Name = "报警", Address = "M1", DataType = PlcDataType.Bool, Value = "0", SortOrder = 6 },
    };

    public async ValueTask DisposeAsync()
    {
        await Provider.ShutdownPlcAsync().ConfigureAwait(false);
        try { Directory.Delete(DataDir, true); } catch { /* 忽略 */ }
    }
}
