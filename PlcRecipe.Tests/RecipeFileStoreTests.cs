using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;
using PlcRecipe.Infrastructure.FileStore;

namespace PlcRecipe.Tests;

/// <summary>
/// 配方文件库（Pfdoc/配方名.txt）行为测试：写入/读取往返、字段完整性（含上下限/单位/备注）、
/// 文件名净化、手工编辑容错、删除。
/// </summary>
public class RecipeFileStoreTests
{
    private static RecipeFileStore CreateStore(string dir) =>
        new(dir, Microsoft.Extensions.Logging.Abstractions.NullLogger<RecipeFileStore>.Instance);

    private static List<RecipeItem> SampleRows() =>
    [
        new() { Name = "温度", Address = "D100", DataType = PlcDataType.Float32, StringWords = 8, Value = "12.5", Unit = "℃", Remark = "模温", LowerLimit = 10, UpperLimit = 80 },
        new() { Name = "启停", Address = "M0", DataType = PlcDataType.Bool, StringWords = 8, Value = "1" },
        new() { Name = "品名", Address = "D106", DataType = PlcDataType.String, StringWords = 4, Value = "AB12" }
    ];

    [Fact]
    public async Task 保存读取往返_字段完整保留()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"Pfdoc_{Guid.NewGuid():N}");
        try
        {
            var store = CreateStore(dir);
            await store.SaveAsync("1#机", "标准配方", 3, SampleRows()).ConfigureAwait(false);

            Assert.True(store.Exists("1#机", "标准配方"));
            Assert.True(File.Exists(Path.Combine(dir, "Pfdoc", "1#机", "标准配方.txt"))); // 文件名 = 配方名

            var doc = await store.TryLoadAsync("1#机", "标准配方").ConfigureAwait(false);
            Assert.NotNull(doc);
            Assert.Equal("标准配方", doc!.Name);
            Assert.Equal(3, doc.Version);
            Assert.Equal(3, doc.Items.Count);

            var temp = doc.Items.First(i => i.Name == "温度");
            Assert.Equal("D100", temp.Address);
            Assert.Equal(PlcDataType.Float32, temp.DataType);
            Assert.Equal("12.5", temp.Value);
            Assert.Equal(10, temp.LowerLimit);
            Assert.Equal(80, temp.UpperLimit);
            Assert.Equal("℃", temp.Unit);
            Assert.Equal("模温", temp.Remark);

            var start = doc.Items.First(i => i.Name == "启停");
            Assert.Equal(PlcDataType.Bool, start.DataType);
            Assert.Equal("1", start.Value);
        }
        finally { try { Directory.Delete(dir, true); } catch { /* 忽略 */ } }
    }

    [Fact]
    public async Task 不存在_返回null()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"Pfdoc_{Guid.NewGuid():N}");
        try
        {
            var store = CreateStore(dir);
            Assert.False(store.Exists("1#机", "没有的配方"));
            Assert.Null(await store.TryLoadAsync("1#机", "没有的配方").ConfigureAwait(false));
        }
        finally { try { Directory.Delete(dir, true); } catch { /* 忽略 */ } }
    }

    [Fact]
    public async Task 文件名非法字符_净化后仍可往返_原名保留在文件头()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"Pfdoc_{Guid.NewGuid():N}");
        try
        {
            var store = CreateStore(dir);
            await store.SaveAsync("1#机", "A/B:C*配方", 1, SampleRows()).ConfigureAwait(false);

            // 文件系统里不应有含非法字符的“文件名”（检查文件名而非全路径）
            var files = Directory.GetFiles(Path.Combine(dir, "Pfdoc"));
            Assert.All(files, f => Assert.False(Path.GetFileName(f).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0));

            var doc = await store.TryLoadAsync("1#机", "A/B:C*配方").ConfigureAwait(false);
            Assert.NotNull(doc);
            Assert.Equal("A/B:C*配方", doc!.Name); // 原名从文件头还原
        }
        finally { try { Directory.Delete(dir, true); } catch { /* 忽略 */ } }
    }

    [Fact]
    public async Task 手工编辑_注释行与缺省字段_容错解析()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"Pfdoc_{Guid.NewGuid():N}");
        try
        {
            var store = CreateStore(dir);
            await store.SaveAsync("1#机", "手编", 5, SampleRows()).ConfigureAwait(false);

            // 模拟现场手工编辑：追加注释与一行只有 5 个字段的数据，删一行
            var path = Path.Combine(dir, "Pfdoc", "1#机", "手编.txt");
            var lines = await File.ReadAllLinesAsync(path).ConfigureAwait(false);
            var edited = lines.ToList();
            edited.Add("# 现场改动：2026-09-09 操作工王");
            edited.Add("新增参数|D200|UInt16|8|7");
            await File.WriteAllLinesAsync(path, edited).ConfigureAwait(false);

            var doc = await store.TryLoadAsync("1#机", "手编").ConfigureAwait(false);
            Assert.NotNull(doc);
            Assert.Equal(4, doc!.Items.Count);
            var added = doc.Items.First(i => i.Name == "新增参数");
            Assert.Equal("7", added.Value);
            Assert.Null(added.LowerLimit);
            Assert.Equal(5, doc.Version);
        }
        finally { try { Directory.Delete(dir, true); } catch { /* 忽略 */ } }
    }

    [Fact]
    public async Task 删除_文件消失_重复删除静默()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"Pfdoc_{Guid.NewGuid():N}");
        try
        {
            var store = CreateStore(dir);
            await store.SaveAsync("1#机", "临时配方", 1, SampleRows()).ConfigureAwait(false);
            await store.DeleteAsync("1#机", "临时配方").ConfigureAwait(false);
            Assert.False(store.Exists("1#机", "临时配方"));
            await store.DeleteAsync("1#机", "临时配方").ConfigureAwait(false); // 不抛
        }
        finally { try { Directory.Delete(dir, true); } catch { /* 忽略 */ } }
    }
}
