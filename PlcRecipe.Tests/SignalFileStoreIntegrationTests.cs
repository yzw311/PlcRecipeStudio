using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;
using PlcRecipe.Drivers.Mock;

namespace PlcRecipe.Tests;

/// <summary>
/// 信号联动 × 配方文件库（Pfdoc）集成测试：
/// 下载按 PLC 报的配方名到 Pfdoc 找同名 txt（不存在即失败位）；
/// 上传按 PLC 报的配方名更新/新建 txt。
/// </summary>
public class SignalFileStoreIntegrationTests
{
    private static async Task<(TestHost Host, PlcDevice Device, MockPlcClient Client)> SetupSignalAsync(TestHost host)
    {
        var device = await host.CreateMockDeviceAsync("1#机", signal: true, recipeNameAddress: "D920");
        var client = (MockPlcClient)await host.Connections.GetClientAsync(device);
        return (host, device, client);
    }

    private static void WriteRecipeName(MockPlcClient client, string name)
    {
        var item = new RecipeItem { Name = "n", Address = "D920", DataType = PlcDataType.String, StringWords = 8 };
        var encoded = ValueCodec.Encode(item, name);
        for (int i = 0; i < 8; i++)
            client.SetWordValue("D", 920 + i, i < encoded.Length ? encoded[i] : (ushort)0);
    }

    [SkippableFact]
    public async Task 信号下载_按Pfdoc同名txt内容下发()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var (_, device, client) = await SetupSignalAsync(host);

        // 软件内保存配方 → 自动写入 Pfdoc txt
        await host.CreateRecipeWithRowsAsync(device, "MOLD1"); // v2，温度=12.5
        Assert.True(host.FileStore.Exists("1#机", "MOLD1"));

        // PLC 报同名 + 置下载请求位
        WriteRecipeName(client, "MOLD1");
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
    public async Task 信号下载_Pfdoc无同名txt_失败位且PLC数据不被触碰()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var (_, device, client) = await SetupSignalAsync(host);

        // DB 里有配方但 Pfdoc 里删掉 txt → 下载必须失败（文件库为准）
        await host.CreateRecipeWithRowsAsync(device, "MOLD1");
        await host.FileStore.DeleteAsync("1#机", "MOLD1");
        Assert.False(host.FileStore.Exists("1#机", "MOLD1"));

        WriteRecipeName(client, "MOLD1");
        client.SetBitValue("M", 900, true);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!client.GetBitValue("M", 903) && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.True(client.GetBitValue("M", 903), "失败位应置位");
        Assert.False(client.GetBitValue("M", 902), "完成位不应置位");
        Assert.Equal(0, client.GetWordValue("D", 100)); // 配方数据未被写入
        client.SetBitValue("M", 900, false);
    }

    [SkippableFact]
    public async Task 信号上传_Pfdoc已有同名txt_按文件结构读值并更新()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var (_, device, client) = await SetupSignalAsync(host);
        var template = await host.CreateRecipeWithRowsAsync(device, "MOLD1"); // v2

        // 先把 MOLD1 下载到 PLC，再篡改一个值模拟现场调整
        await host.Transfers.DownloadAsync(device, template);
        var speedRow = template.Items.First(i => i.Name == "速度");
        var w = ValueCodec.Encode(speedRow, "77777");
        client.SetWordValue("D", 102, w[0]);
        client.SetWordValue("D", 103, w[1]);

        // PLC 报同名 + 置上传请求位 → 应按 Pfdoc 的 MOLD1.txt 结构读值并更新该 txt（确定性等待版本+1）
        WriteRecipeName(client, "MOLD1");
        client.SetBitValue("M", 901, true);

        RecipeFileDocument? doc = null;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            doc = await host.FileStore.TryLoadAsync("1#机", "MOLD1");
            if (doc != null && doc.Version >= 3) break;
            await Task.Delay(50);
        }
        Assert.NotNull(doc);
        Assert.True(doc!.Version >= 3, "txt 版本应 +1");
        Assert.Equal("77777", doc.Items.First(i => i.Name == "速度").Value);
        Assert.Equal("12.5", doc.Items.First(i => i.Name == "温度").Value); // 其余值来自 PLC 实读

        client.SetBitValue("M", 901, false);
    }

    [SkippableFact]
    public async Task 信号上传_Pfdoc无同名txt_自动新建txt()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var (_, device, client) = await SetupSignalAsync(host);
        var template = await host.CreateRecipeWithRowsAsync(device, "MOLD1");

        await host.Transfers.DownloadAsync(device, template);
        var speedRow = template.Items.First(i => i.Name == "速度");
        var w = ValueCodec.Encode(speedRow, "100000");
        client.SetWordValue("D", 102, w[0]);
        client.SetWordValue("D", 103, w[1]);

        // PLC 报一个文件库里没有的新名字 + 置上传请求位（确定性等待 txt 新建）
        WriteRecipeName(client, "MOLD2");
        client.SetBitValue("M", 901, true);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        RecipeFileDocument? created = null;
        while (DateTime.UtcNow < deadline)
        {
            created = await host.FileStore.TryLoadAsync("1#机", "MOLD2");
            if (created != null) break;
            await Task.Delay(50);
        }
        Assert.NotNull(created);
        Assert.Equal(7, created!.Items.Count);
        Assert.Equal("100000", created.Items.First(i => i.Name == "速度").Value);
        client.SetBitValue("M", 901, false);
    }
}
