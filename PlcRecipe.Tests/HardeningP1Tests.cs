using PlcRecipe.Core;
using PlcRecipe.Core.Models;
using PlcRecipe.Drivers.Mock;

namespace PlcRecipe.Tests;

/// <summary>P1 增强：参数容差（上下限越界告警）与配方水印（下载写入"配方名|v版本"）。</summary>
public class HardeningP1Tests
{
    // ---------- 参数容差 ----------

    private static RecipeItem Item(string name, string value, double? lower = null, double? upper = null,
        PlcDataType type = PlcDataType.Float32) =>
        new() { Name = name, Value = value, DataType = type, LowerLimit = lower, UpperLimit = upper };

    [SkippableFact]
    public void 越界检查_上下限内_无告警()
    {
        var items = new[] { Item("温度", "25", 0, 100) };
        Assert.Empty(RecipeLimits.Check(items, new Dictionary<string, string> { ["温度"] = "25" }));
    }

    [SkippableFact]
    public void 越界检查_超上限与低于下限_分别告警()
    {
        var items = new[] { Item("温度", "25", 0, 100), Item("压力", "1", 5) };
        var violations = RecipeLimits.Check(items, new Dictionary<string, string>
        {
            ["温度"] = "105",
            ["压力"] = "2"
        });
        Assert.Equal(2, violations.Count);
        Assert.Contains(violations, v => v.Contains("温度=105 超上限 100"));
        Assert.Contains(violations, v => v.Contains("压力=2 低于下限 5"));
    }

    [SkippableFact]
    public void 越界检查_等价数值格式_正确换算后比较()
    {
        var items = new[] { Item("温度", "25", 0, 100) };
        // PLC 读回 "25.0" 与配方 "25" 为等价值（ValuesEqual 语义），不应告警
        Assert.Empty(RecipeLimits.Check(items, new Dictionary<string, string> { ["温度"] = "25.0" }));
    }

    [SkippableFact]
    public void 越界检查_Bool与字符串_不参与限值()
    {
        var items = new[]
        {
            Item("启停", "999", 0, 10, PlcDataType.Bool),
            Item("品名", "999", 0, 10, PlcDataType.String)
        };
        var values = new Dictionary<string, string> { ["启停"] = "999", ["品名"] = "999" };
        Assert.Empty(RecipeLimits.Check(items, values));
    }

    [SkippableFact]
    public void 越界检查_值不可解析_跳过不误报()
    {
        var items = new[] { Item("温度", "25", 0, 100) };
        Assert.Empty(RecipeLimits.Check(items, new Dictionary<string, string> { ["温度"] = "N/A" }));
    }

    // ---------- 保存校验：下限不能大于上限 ----------

    [SkippableFact]
    public async Task 保存配方_下限大于上限_被拒()
    {
        var host = await TestHost.CreateAsync().ConfigureAwait(false);
        await using var _ = host;
        var device = await host.CreateMockDeviceAsync("1#机").ConfigureAwait(false);
        var recipe = await host.CreateRecipeWithRowsAsync(device, "标准配方").ConfigureAwait(false);

        var rows = (await host.Recipes.GetRecipeAsync(recipe.Id).ConfigureAwait(false)).Items;
        var temp = rows.First(r => r.Name == "温度");
        temp.UpperLimit = 10;
        temp.LowerLimit = 20;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.Recipes.SaveRecipeAsync(recipe.Id, rows, "admin")).ConfigureAwait(false);
    }

    // ---------- 配方水印（下载写入"配方名|v版本"） ----------

    [SkippableFact]
    public async Task 下载_设备配置标识地址_写入水印且回读一致()
    {
        var host = await TestHost.CreateAsync().ConfigureAwait(false);
        await using var _ = host;
        var device = await host.CreateMockDeviceAsync("1#机").ConfigureAwait(false);
        device.RecipeTagAddress = "D500";
        device.RecipeTagWords = 12;
        var recipe = await host.CreateRecipeWithRowsAsync(device, "标准配方").ConfigureAwait(false); // v2

        var result = await host.Transfers.DownloadAsync(device, recipe).ConfigureAwait(false);
        Assert.Equal(TransferStatus.Success, result.Status);
        Assert.Contains("配方标识已写入", result.Message);

        var client = (MockPlcClient)await host.Connections.GetClientAsync(device).ConfigureAwait(false);
        var tagItem = new RecipeItem
        {
            Name = "配方标识",
            Address = "D500",
            DataType = PlcDataType.String,
            StringWords = 12
        };
        var stored = Enumerable.Range(0, 12).Select(i => client.GetWordValue("D", 500 + i)).ToArray();
        Assert.Equal("标准配方|v2", ValueCodec.Decode(tagItem, stored!).TrimEnd('\0'));
    }

    [SkippableFact]
    public async Task 下载_未配置标识地址_不写水印行为不变()
    {
        var host = await TestHost.CreateAsync().ConfigureAwait(false);
        await using var _ = host;
        var device = await host.CreateMockDeviceAsync("1#机").ConfigureAwait(false);
        var recipe = await host.CreateRecipeWithRowsAsync(device, "标准配方").ConfigureAwait(false);

        var result = await host.Transfers.DownloadAsync(device, recipe).ConfigureAwait(false);
        Assert.Equal(TransferStatus.Success, result.Status);
        Assert.DoesNotContain("配方标识", result.Message);

        var client = (MockPlcClient)await host.Connections.GetClientAsync(device).ConfigureAwait(false);
        Assert.All(Enumerable.Range(0, 4).Select(i => client.GetWordValue("D", 500 + i)), w => Assert.Equal(0, w));
    }
}
