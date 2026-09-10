using PlcRecipe.Core;
using PlcRecipe.Core.Models;
using PlcRecipe.Drivers;
using PlcRecipe.Infrastructure.Services;

namespace PlcRecipe.Tests;

/// <summary>块合并逻辑测试：连续地址合并为一次请求，间隔地址分块。</summary>
public class BlockMergeTests
{
    private static TransferService CreateService() =>
        new(null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<TransferService>.Instance);

    private static RecipeItem Row(string name, string addr, PlcDataType type, int stringWords = 8) => new()
    {
        Name = name,
        Address = addr,
        DataType = type,
        StringWords = stringWords
    };

    [Fact]
    public void 连续字地址合并为一块()
    {
        var svc = CreateService();
        var rows = new List<RecipeItem>
        {
            Row("a", "D100", PlcDataType.Float32),  // D100-D101
            Row("b", "D102", PlcDataType.Float32),  // D102-D103
            Row("c", "D104", PlcDataType.UInt16),   // D104
            Row("d", "D105", PlcDataType.Int16),    // D105
        };
        var plan = svc.BuildPlan(PlcBrand.Mock, rows);
        var block = Assert.Single(plan.WordBlocks);
        Assert.Equal(100, block.Start.Offset);
        Assert.Equal(6, block.WordCount);
        Assert.Empty(plan.BitBlocks);
    }

    [Fact]
    public void 地址不连续_分为多块()
    {
        var svc = CreateService();
        var rows = new List<RecipeItem>
        {
            Row("a", "D100", PlcDataType.UInt16),
            Row("b", "D105", PlcDataType.UInt16),   // 空洞 D101-D104
            Row("c", "D200", PlcDataType.UInt16),   // 另一区域
        };
        var plan = svc.BuildPlan(PlcBrand.Mock, rows);
        Assert.Equal(3, plan.WordBlocks.Count);
    }

    [Fact]
    public void Bool变量合并为位块()
    {
        var svc = CreateService();
        var rows = new List<RecipeItem>
        {
            Row("a", "M0", PlcDataType.Bool),
            Row("b", "M1", PlcDataType.Bool),
            Row("c", "M2", PlcDataType.Bool),
            Row("d", "M10", PlcDataType.Bool),  // 不连续
        };
        var plan = svc.BuildPlan(PlcBrand.Mock, rows);
        Assert.Equal(2, plan.BitBlocks.Count);
        Assert.Equal(3, plan.BitBlocks[0].BitCount);
        Assert.Equal(1, plan.BitBlocks[1].BitCount);
    }

    [Fact]
    public void 不同数据区_分块()
    {
        var svc = CreateService();
        var rows = new List<RecipeItem>
        {
            Row("a", "D100", PlcDataType.UInt16),
            Row("b", "M100", PlcDataType.Bool),
        };
        var plan = svc.BuildPlan(PlcBrand.Mock, rows);
        Assert.Single(plan.WordBlocks);
        Assert.Single(plan.BitBlocks);
    }

    [Fact]
    public void 变量顺序无关_自动排序()
    {
        var svc = CreateService();
        var rows = new List<RecipeItem>
        {
            Row("c", "D102", PlcDataType.UInt16),
            Row("a", "D100", PlcDataType.UInt16),
            Row("b", "D101", PlcDataType.UInt16),
        };
        var plan = svc.BuildPlan(PlcBrand.Mock, rows);
        var block = Assert.Single(plan.WordBlocks);
        Assert.Equal(3, block.WordCount);
        Assert.Equal("a", block.Items[0].Item.Name); // 按地址排序
        Assert.Equal("c", block.Items[2].Item.Name);
    }

    [Fact]
    public void 类型与地址不匹配_抛异常()
    {
        var svc = CreateService();
        var rows = new List<RecipeItem> { Row("a", "D100", PlcDataType.Bool) }; // Bool 配字地址
        Assert.Throws<PlcAddressException>(() => svc.BuildPlan(PlcBrand.Mock, rows));
    }

    // ---------- 品牌偏移归一（P1：S7 字节偏移 / 位线性化） ----------

    [Fact]
    public void 西门子_字块按字节偏移合并()
    {
        // S7 地址偏移是字节：DBW0/DBW2/DBW4 相邻（每 Int16 占 2 字节）
        var svc = CreateService();
        var rows = new List<RecipeItem>
        {
            Row("a", "DB1.DBW0", PlcDataType.Int16),
            Row("b", "DB1.DBW2", PlcDataType.Int16),
            Row("c", "DB1.DBW4", PlcDataType.Int16),
        };
        var plan = svc.BuildPlan(PlcBrand.Siemens, rows);
        var block = Assert.Single(plan.WordBlocks);
        Assert.Equal(0, block.Start.Offset);
        Assert.Equal(3, block.WordCount);
        // 块内偏移仍按字计（读回切片/写缓冲的语义，与品牌无关）
        Assert.Equal(0, block.Items[0].Item2);
        Assert.Equal(1, block.Items[1].Item2);
        Assert.Equal(2, block.Items[2].Item2);
    }

    [Fact]
    public void 西门子_字节间隔不合并()
    {
        // DBW0(字节0-1) 与 DBW3(字节3-4) 中间隔着字节 2：不应合并
        var svc = CreateService();
        var rows = new List<RecipeItem>
        {
            Row("a", "DB1.DBW0", PlcDataType.Int16),
            Row("b", "DB1.DBW3", PlcDataType.Int16),
        };
        var plan = svc.BuildPlan(PlcBrand.Siemens, rows);
        Assert.Equal(2, plan.WordBlocks.Count);
    }

    [Fact]
    public void 西门子_位块按线性位号合并与隔离()
    {
        var svc = CreateService();
        // M0.0 与 M1.0 之间隔着 M0.1-M0.7：绝不能按 Offset 判连续合并（否则读回 M0.1 的值）
        var rows1 = new List<RecipeItem>
        {
            Row("a", "M0.0", PlcDataType.Bool),
            Row("b", "M1.0", PlcDataType.Bool),
        };
        var plan1 = svc.BuildPlan(PlcBrand.Siemens, rows1);
        Assert.Equal(2, plan1.BitBlocks.Count);

        // M0.6/M0.7/M1.0 线性连续（6/7/8）：应合并成一块，驱动按 bitIndex 跨字节读取
        var rows2 = new List<RecipeItem>
        {
            Row("a", "M0.6", PlcDataType.Bool),
            Row("b", "M0.7", PlcDataType.Bool),
            Row("c", "M1.0", PlcDataType.Bool),
        };
        var plan2 = svc.BuildPlan(PlcBrand.Siemens, rows2);
        var block = Assert.Single(plan2.BitBlocks);
        Assert.Equal(3, block.BitCount);
    }

    [Fact]
    public void 欧姆龙_位块按字内线性位号隔离()
    {
        var svc = CreateService();
        // D100.0 与 D101.0 之间隔着 D100.1-D100.15：不能按 Offset(100+1==101) 误判连续
        var rows1 = new List<RecipeItem>
        {
            Row("a", "D100.0", PlcDataType.Bool),
            Row("b", "D101.0", PlcDataType.Bool),
        };
        var plan1 = svc.BuildPlan(PlcBrand.Omron, rows1);
        Assert.Equal(2, plan1.BitBlocks.Count);

        // 字内相邻位 D100.0/D100.1：合并
        var rows2 = new List<RecipeItem>
        {
            Row("a", "D100.0", PlcDataType.Bool),
            Row("b", "D100.1", PlcDataType.Bool),
        };
        var plan2 = svc.BuildPlan(PlcBrand.Omron, rows2);
        var block = Assert.Single(plan2.BitBlocks);
        Assert.Equal(2, block.BitCount);
    }

    [Fact]
    public void 位节点型品牌_合并语义不变()
    {
        // Modbus/Mitsubishi/Mock 位即独立节点：线性位号 = Offset，与旧行为完全一致
        var svc = CreateService();
        var rows = new List<RecipeItem>
        {
            Row("a", "Y0", PlcDataType.Bool),
            Row("b", "Y1", PlcDataType.Bool),
            Row("c", "Y3", PlcDataType.Bool),
        };
        var plan = svc.BuildPlan(PlcBrand.ModbusTcp, rows);
        Assert.Equal(2, plan.BitBlocks.Count);
        Assert.Equal(2, plan.BitBlocks[0].BitCount);
    }

    [Fact]
    public void 字偏量换算地址_西门子按字节翻倍()
    {
        var mock = PlcAddressParser.Parse(PlcBrand.Mock, "D100");
        var advancedMock = TransferService.AdvanceWords(PlcBrand.Mock, mock, 2);
        Assert.Equal(102, advancedMock.Offset);

        var s7 = PlcAddressParser.Parse(PlcBrand.Siemens, "DB1.DBW0");
        var advancedS7 = TransferService.AdvanceWords(PlcBrand.Siemens, s7, 2);
        Assert.Equal(4, advancedS7.Offset); // 2 字 = 4 字节
    }
}
