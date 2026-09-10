using PlcRecipe.Core;
using PlcRecipe.Core.Models;
using PlcRecipe.Drivers;
using PlcRecipe.Drivers.Mock;

namespace PlcRecipe.Tests;

/// <summary>配方服务/上传下载/对比/信号联动 全链路（Mock PLC + SQLite）集成测试。</summary>
public class ServiceIntegrationTests
{
    private static async Task<(TestHost Host, PlcDevice Device, Recipe Recipe)> SetupAsync(bool signal = false)
    {
        var host = await TestHost.CreateAsync();
        var device = await host.CreateMockDeviceAsync("1#机", signal);
        var recipe = await host.CreateRecipeWithRowsAsync(device, "标准配方");
        return (host, device, recipe);
    }

    [SkippableFact]
    public async Task 配方保存_版本递增_值落库()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var device = await host.CreateMockDeviceAsync("1#机");
        var recipe = await host.Recipes.CreateRecipeAsync(device.Id, "标准配方", null, "admin");
        Assert.Equal(1, recipe.Version);

        var rows = TestHost.StandardRows();
        await host.Recipes.SaveRecipeAsync(recipe.Id, rows, "admin");
        var reloaded = await host.Recipes.GetRecipeAsync(recipe.Id);
        Assert.Equal(2, reloaded.Version);
        Assert.Equal(7, reloaded.Items.Count);
        Assert.Equal("12.5", reloaded.Items.First(i => i.Name == "温度").Value);

        // 再保存一次（改值）→ 版本 3
        rows.First(r => r.Name == "模式").Value = "9";
        await host.Recipes.SaveRecipeAsync(recipe.Id, rows, "admin");
        reloaded = await host.Recipes.GetRecipeAsync(recipe.Id);
        Assert.Equal(3, reloaded.Version);
        Assert.Equal("9", reloaded.Items.First(i => i.Name == "模式").Value);
    }

    [SkippableFact]
    public async Task 配方重名_报错()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var device = await host.CreateMockDeviceAsync("1#机");
        await host.Recipes.CreateRecipeAsync(device.Id, "标准配方", null, "admin");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.Recipes.CreateRecipeAsync(device.Id, "标准配方", null, "admin"));
    }

    [SkippableFact]
    public async Task 非法变量值_保存被拒()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var device = await host.CreateMockDeviceAsync("1#机");
        var recipe = await host.Recipes.CreateRecipeAsync(device.Id, "R", null, "admin");
        var rows = TestHost.StandardRows();
        rows.First(r => r.Name == "温度").Value = "不是数字";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.Recipes.SaveRecipeAsync(recipe.Id, rows, "admin"));
    }

    [SkippableFact]
    public async Task 非法地址_保存被拒()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var device = await host.CreateMockDeviceAsync("1#机");
        var recipe = await host.Recipes.CreateRecipeAsync(device.Id, "R", null, "admin");
        var rows = TestHost.StandardRows();
        rows.First(r => r.Name == "温度").Address = "XYZ99";
        await Assert.ThrowsAnyAsync<Exception>(() =>
            host.Recipes.SaveRecipeAsync(recipe.Id, rows, "admin"));
    }

    [SkippableFact]
    public async Task 下载_写入Mock并回读校验()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var device = await host.CreateMockDeviceAsync("1#机");
        var recipe = await host.CreateRecipeWithRowsAsync(device, "标准配方");

        var result = await host.Transfers.DownloadAsync(device, recipe, null, verifyAfterWrite: true);
        Assert.Equal(TransferStatus.Success, result.Status);

        // Mock PLC 中的值（温度=12.5 内部字非零；启停 M0=1）
        var client = (MockPlcClient)await host.Connections.GetClientAsync(device);
        Assert.True(client.GetWordValue("D", 100) != 0 || client.GetWordValue("D", 101) != 0);
        Assert.True(client.GetBitValue("M", 0));
    }

    [SkippableFact]
    public async Task 上传_读到与配方一致的值()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var device = await host.CreateMockDeviceAsync("1#机");
        var recipe = await host.CreateRecipeWithRowsAsync(device, "标准配方");

        // 先下载使 PLC 有值，再上传
        await host.Transfers.DownloadAsync(device, recipe);
        var upload = await host.Transfers.UploadAsync(device, recipe);
        Assert.Equal(TransferStatus.Success, upload.Status);
        Assert.NotNull(upload.ReadValues);

        var recipeValues = recipe.Items.ToDictionary(i => i.Name, i => i.Value);
        foreach (var (name, value) in recipeValues)
            Assert.Equal(value, upload.ReadValues![name]);
    }

    [SkippableFact]
    public async Task 对比_一致时零差异_修改后报告差异()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var device = await host.CreateMockDeviceAsync("1#机");
        var recipe = await host.CreateRecipeWithRowsAsync(device, "标准配方");

        await host.Transfers.DownloadAsync(device, recipe);
        var cmp = await host.Compare.CompareAsync(device, recipe);
        Assert.True(cmp.Success);
        Assert.Equal(0, cmp.DiffCount);

        // 直接篡改 PLC 中 模式（D104）的值
        var client = (MockPlcClient)await host.Connections.GetClientAsync(device);
        var modeRow = recipe.Items.First(i => i.Name == "模式");
        var word = ValueCodec.Encode(modeRow, "3");
        client.SetWordValue("D", 104, (ushort)(word[0] ^ 0xFFFF));
        var cmp2 = await host.Compare.CompareAsync(device, recipe);
        Assert.Equal(1, cmp2.DiffCount);
        Assert.Equal("模式", cmp2.Rows.Single(r => r.State == CompareState.Different).Item.Name);
    }

    [SkippableFact]
    public async Task 写差异项_只写选中行()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var device = await host.CreateMockDeviceAsync("1#机");
        var recipe = await host.CreateRecipeWithRowsAsync(device, "标准配方");

        await host.Transfers.DownloadAsync(device, recipe);
        // 篡改 PLC 中 速度（D102/D103 Int32）
        var client = (MockPlcClient)await host.Connections.GetClientAsync(device);
        client.SetWordValue("D", 102, 111);
        client.SetWordValue("D", 103, 111);

        var cmp = await host.Compare.CompareAsync(device, recipe);
        Assert.Equal(1, cmp.DiffCount);
        var speedRow = cmp.Rows.Single(r => r.State == CompareState.Different).Item;

        var result = await host.Transfers.DownloadSubsetAsync(device, recipe, [speedRow]);
        Assert.Equal(TransferStatus.Success, result.Status);

        var cmp2 = await host.Compare.CompareAsync(device, recipe);
        Assert.Equal(0, cmp2.DiffCount);
    }

    [SkippableFact]
    public async Task 并发上传下载_同设备排队不冲突()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var device = await host.CreateMockDeviceAsync("1#机");
        var recipe = await host.CreateRecipeWithRowsAsync(device, "标准配方");

        var tasks = new List<Task<DeviceTransferResult>>();
        for (int i = 0; i < 20; i++)
        {
            tasks.Add(host.Transfers.DownloadAsync(device, recipe));
            tasks.Add(host.Transfers.UploadAsync(device, recipe));
        }
        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.Equal(TransferStatus.Success, r.Status));
    }

    [SkippableFact]
    public async Task 用户服务_哈希验证与角色()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;

        await host.Users.AddUserAsync("op1", "123456", UserRole.Operator, null);
        Assert.NotNull(await host.Users.VerifyAsync("op1", "123456"));
        Assert.Null(await host.Users.VerifyAsync("op1", "错误密码"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.Users.AddUserAsync("op1", "123456", UserRole.Operator, null));
    }

    [SkippableFact]
    public async Task 保存配方_UI路径_带Id行重复保存()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var device = await host.CreateMockDeviceAsync("1#机");
        var recipe = await host.CreateRecipeWithRowsAsync(device, "标准配方");

        // 模拟 UI：加载（行带数据库 Id）→ 原样存回 → 再存回（删旧插新同主键）
        for (int round = 0; round < 3; round++)
        {
            var loaded = await host.Recipes.GetRecipeAsync(recipe.Id);
            await host.Recipes.SaveRecipeAsync(recipe.Id, loaded.Items, "admin");
        }
        var final = await host.Recipes.GetRecipeAsync(recipe.Id);
        Assert.Equal(7, final.Items.Count);
        Assert.Equal("12.5", final.Items.First(i => i.Name == "温度").Value);
    }

    [SkippableFact]
    public async Task 信号联动握手_下载请求_完成位闭环()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var device = await host.CreateMockDeviceAsync("1#机", signal: true);
        var recipe = await host.CreateRecipeWithRowsAsync(device, "标准配方");

        var client = (MockPlcClient)await host.Connections.GetClientAsync(device);

        // PLC 把配方名写到字符串地址并置位下载请求（Mock 存内部字：写入必须经 ValueCodec 编码）
        TestHost.WriteRecipeName(client, "D", 920, "标准配方");
        client.SetBitValue("M", 900, true);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!client.GetBitValue("M", 902) && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.True(client.GetBitValue("M", 902), "完成位未置位");
        Assert.False(client.GetBitValue("M", 903), "失败位不应置位");

        // PLC 清请求位 → 完成位应被复位
        client.SetBitValue("M", 900, false);
        deadline = DateTime.UtcNow.AddSeconds(30);
        while (client.GetBitValue("M", 902) && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.False(client.GetBitValue("M", 902), "完成位未在请求清零后复位");
    }

    [SkippableFact]
    public async Task 信号联动_按配方名下载()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var device = await host.CreateMockDeviceAsync("1#机", signal: true, recipeNameAddress: "D920");
        await host.CreateRecipeWithRowsAsync(device, "MOLD1");

        var client = (MockPlcClient)await host.Connections.GetClientAsync(device);
        TestHost.WriteRecipeName(client, "D", 920, "MOLD1");
        client.SetBitValue("M", 900, true);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!client.GetBitValue("M", 902) && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.True(client.GetBitValue("M", 902), "完成位未置位");
        Assert.False(client.GetBitValue("M", 903), "失败位不应置位");
        Assert.True(client.GetWordValue("D", 100) != 0 || client.GetWordValue("D", 101) != 0, "配方数据未写入");
        client.SetBitValue("M", 900, false);
    }

    [SkippableFact]
    public async Task 信号联动_上传未知配方名_自动创建配方()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var device = await host.CreateMockDeviceAsync("1#机", signal: true, recipeNameAddress: "D920");
        var template = await host.CreateRecipeWithRowsAsync(device, "MOLD1");

        var client = (MockPlcClient)await host.Connections.GetClientAsync(device);
        // 先把 MOLD1 下载到 PLC（PLC 中有参数值），再篡改一个值模拟现场调整
        await host.Transfers.DownloadAsync(device, template);
        var speedRow = template.Items.First(i => i.Name == "速度");
        var w = ValueCodec.Encode(speedRow, "100000");
        client.SetWordValue("D", 102, w[0]);
        client.SetWordValue("D", 103, w[1]);

        // PLC 写一个新配方名 + 置上传请求 → 软件应自动创建 MOLD2（txt 即数据库，确定性等待文件出现）
        TestHost.WriteRecipeName(client, "D", 920, "MOLD2");
        client.SetBitValue("M", 901, true);

        Recipe? created = null;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var recipes = await host.Recipes.GetRecipesAsync(device.Id);
            created = recipes.FirstOrDefault(r => r.Name == "MOLD2");
            if (created != null) break;
            await Task.Delay(100);
        }
        Assert.NotNull(created);
        Assert.Equal("100000", created!.Items.First(i => i.Name == "速度").Value);
        Assert.True(host.FileStore.Exists("1#机", "MOLD2"), "Pfdoc 中应自动新建 MOLD2.txt");
        client.SetBitValue("M", 901, false);
    }

    [SkippableFact]
    public async Task 信号联动_配方名不存在_失败位置位()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var device = await host.CreateMockDeviceAsync("1#机", signal: true);
        await host.CreateRecipeWithRowsAsync(device, "标准配方");

        var client = (MockPlcClient)await host.Connections.GetClientAsync(device);
        TestHost.WriteRecipeName(client, "D", 920, "NOSUCH");  // PLC 请求一个不存在的配方名
        client.SetBitValue("M", 900, true);

        // 确定性等待：失败写入操作日志（失败位会在握手闭环后被复位，不宜直接轮询位信号）
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var failedLogged = false;
        while (DateTime.UtcNow < deadline)
        {
            var (_, total) = await host.OpLogs.QueryAsync(null, null, null, TransferSource.SignalTrigger,
                "信号下载配方", false, 1, 10);
            if (total > 0) { failedLogged = true; break; }
            await Task.Delay(100);
        }
        Assert.True(failedLogged, "下载失败应被记录到操作日志");
        client.SetBitValue("M", 900, false);
    }

    [SkippableFact]
    public async Task 设备编辑_配方名字段持久化()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var device = await host.CreateMockDeviceAsync("1#机", signal: true, recipeNameAddress: "D920");

        // 编辑：改名地址/字数 → 保存 → 回读（BUG-N1 回归：UpdateDeviceAsync 必须持久化配方名字段）
        var edited = new PlcDevice
        {
            Id = device.Id,
            Name = device.Name,
            Brand = device.Brand,
            Ip = device.Ip,
            Port = device.Port,
            Enabled = true,
            SignalEnabled = true,
            DownloadRequestAddress = "M900",
            UploadRequestAddress = "M901",
            RecipeNameAddress = "HR930",
            RecipeNameWords = 20,
            DoneBitAddress = "M902",
            FailBitAddress = "M903",
            PollIntervalMs = 500
        };
        await host.Devices.UpdateDeviceAsync(edited);

        var reloaded = (await host.Devices.GetDevicesAsync())
            .First(d => d.Id == device.Id);
        Assert.Equal("HR930", reloaded.RecipeNameAddress);
        Assert.Equal(20, reloaded.RecipeNameWords);
        Assert.Equal("M900", reloaded.DownloadRequestAddress);
        Assert.True(reloaded.SignalEnabled);
    }
}
