using System.Buffers;
using Microsoft.Extensions.Logging;
using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;
using PlcRecipe.Drivers;

namespace PlcRecipe.Infrastructure.Services;

/// <summary>
/// 配方上传/下载执行服务。
/// 优化：把配方中地址连续的数据行合并成块，一次 PLC 请求读写整块，
/// 大幅减少通讯次数；下载/读取均为"整计划单次锁内原子执行"，
/// 与信号自动任务互不交错；批量缓冲使用 ArrayPool 租借归还。
/// </summary>
public class TransferService(
    IPlcConnectionManager connections,
    ILogger<TransferService> logger) : ITransferService
{
    private const int MaxChunkWords = 120; // Modbus 单帧上限内，MC/S7 驱动内部还会再分

    // ---------- 计划构建 ----------
    public TransferPlan BuildPlan(PlcBrand brand, IReadOnlyList<RecipeItem> rows)
    {
        var plan = new TransferPlan { Brand = brand, Rows = rows };

        // 字类型行按 (数据区, 字/位) 分组，组内按地址排序后合并连续块
        foreach (var group in rows.Where(r => r.DataType != PlcDataType.Bool)
                 .GroupBy(r => AreaKey(brand, r.Address)))
        {
            WordBlock? current = null;
            foreach (var item in group.OrderBy(r => OffsetOf(brand, r.Address)))
            {
                var addr = PlcAddressParser.Parse(brand, item.Address);
                if (addr.IsBitDevice)
                    throw new PlcAddressException(item.Address, $"变量“{item.Name}”类型 {item.DataType} 与位地址不匹配");
                // 合并条件按品牌归一：西门子的 Offset 是字节（1 字 = 2 字节），其余品牌 1 字 = 1 个偏移单位。
                // 判据 = 下一行起始恰好紧跟当前块尾（连续才合并，只影响请求数，不影响正确性）
                if (current != null &&
                    current.Start.Area == addr.Area &&
                    current.Start.Offset + (long)current.WordCount * AddressUnitsPerWord(brand) == addr.Offset)
                {
                    current.Items.Add((item, current.WordCount));
                    current.WordCount += item.WordCount;
                }
                else
                {
                    current = new WordBlock { Start = addr, WordCount = item.WordCount };
                    current.Items.Add((item, 0));
                    plan.WordBlocks.Add(current);
                }
            }
        }

        // Bool 行合并为位块
        foreach (var group in rows.Where(r => r.DataType == PlcDataType.Bool)
                 .GroupBy(r => AreaKey(brand, r.Address)))
        {
            BitBlock? current = null;
            foreach (var item in group.OrderBy(r => OffsetOf(brand, r.Address)))
            {
                var addr = PlcAddressParser.Parse(brand, item.Address);
                if (!addr.IsBitDevice)
                    throw new PlcAddressException(item.Address, $"变量“{item.Name}”是 Bool 类型，地址必须为位地址");
                // 位连续性按“线性位号”判（字节.位 / 字.位 品牌先展平成一维位序，再判相邻）：
                // 直接比 Offset 会把 S7 的 M0.0+M1.0（中间隔着 M0.1-M0.7）误判连续 → 读回值整体错位
                if (current != null &&
                    current.Start.Area == addr.Area &&
                    BitLinear(brand, current.Start) + current.BitCount == BitLinear(brand, addr))
                {
                    current.Items.Add((item, current.BitCount));
                    current.BitCount++;
                }
                else
                {
                    current = new BitBlock { Start = addr, BitCount = 1 };
                    current.Items.Add((item, 0));
                    plan.BitBlocks.Add(current);
                }
            }
        }
        return plan;
    }

    private static string AreaKey(PlcBrand brand, string address)
    {
        var a = PlcAddressParser.Parse(brand, address);
        return a.Area + (a.IsBitDevice ? "$B" : "$W");
    }

    private static int OffsetOf(PlcBrand brand, string address) =>
        PlcAddressParser.Parse(brand, address).Offset;

    /// <summary>1 个字占用的地址偏移单位：西门子地址偏移按字节（1 字 = 2 字节），其余品牌 1 字 = 1。</summary>
    internal static int AddressUnitsPerWord(PlcBrand brand) => brand == PlcBrand.Siemens ? 2 : 1;

    /// <summary>把“块内字偏量”换算成绝对地址（S7 的字节偏移 ×2，其余 ×1）。分块下载写址用。</summary>
    internal static ParsedAddress AdvanceWords(PlcBrand brand, ParsedAddress start, int words) =>
        new(start.Area, start.Offset + words * AddressUnitsPerWord(brand), start.Bit, start.IsBitDevice, start.Raw);

    /// <summary>位地址展平成一维线性位号：S7 = 字节×8+位，FINS = 字×16+位，位即节点的品牌 = Offset。</summary>
    internal static long BitLinear(PlcBrand brand, ParsedAddress addr)
    {
        var bitsPerUnit = brand switch
        {
            PlcBrand.Siemens => 8,
            PlcBrand.Omron => 16,
            _ => 1
        };
        return (long)addr.Offset * bitsPerUnit + Math.Max(addr.Bit, 0);
    }

    // ---------- 下载 ----------
    public async Task<DeviceTransferResult> DownloadAsync(PlcDevice device, Recipe recipe,
        IProgress<TransferProgress>? progress = null, bool verifyAfterWrite = false,
        CancellationToken ct = default)
    {
        var rows = recipe.Items
            .Where(i => i.Access == VariableAccess.ReadWrite)
            .ToList();
        var values = recipe.Items.ToDictionary(i => i.Name, i => i.Value);
        // Validate every row before any PLC I/O, including read-only rows and Bool values.
        foreach (var item in recipe.Items)
            _ = ValueCodec.Encode(item, values.GetValueOrDefault(item.Name, ""), device.DataFormat);
        var watermark = $"{recipe.Name}|v{recipe.Version}"; // 配方标识（水印）：设备配置了标识地址才会实际写入
        return await DownloadCoreAsync(device, rows, values, progress, verifyAfterWrite, watermark, ct).ConfigureAwait(false);
    }

    public Task<DeviceTransferResult> DownloadSubsetAsync(PlcDevice device, Recipe recipe,
        IReadOnlyList<RecipeItem> rows, IProgress<TransferProgress>? progress = null,
        CancellationToken ct = default)
    {
        var values = recipe.Items.ToDictionary(i => i.Name, i => i.Value);
        // Validate every row before any PLC I/O, including read-only rows and Bool values.
        foreach (var item in recipe.Items)
            _ = ValueCodec.Encode(item, values.GetValueOrDefault(item.Name, ""), device.DataFormat);
        var watermark = $"{recipe.Name}|v{recipe.Version}";
        return DownloadCoreAsync(device, [.. rows], values, progress, false, watermark, ct);
    }

    private async Task<DeviceTransferResult> DownloadCoreAsync(PlcDevice device,
        List<RecipeItem> rows, Dictionary<string, string> values,
        IProgress<TransferProgress>? progress, bool verify, string? watermark, CancellationToken ct)
    {
        var result = new DeviceTransferResult
        {
            DeviceId = device.Id,
            DeviceName = device.Name,
            Direction = TransferDirection.Download
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var plan = BuildPlan(device.Brand, rows);
        int totalUnits = plan.WordBlocks.Count + plan.BitBlocks.Count;
        if (totalUnits == 0)
        {
            result.Status = TransferStatus.Failed;
            result.Message = "没有可写入的数据行";
            return result;
        }
        int done = 0;
        bool tagWritten = false;
        try
        {
            // 整个计划在一次设备锁内原子执行：手动上传/对比读与信号自动下载互不交错，杜绝撕裂快照
            await connections.ExecuteAsync(device, async (c, opCt) =>
            {
                foreach (var block in plan.WordBlocks)
                {
                    // 按变量边界分块：一个多字变量（Float32/String 等）绝不被切到两个请求里
                    int idx = 0;
                    while (idx < block.Items.Count)
                    {
                        int chunkStartOffset = block.Items[idx].Item2;
                        int chunkEnd = chunkStartOffset;
                        int lastIdx = idx;
                        while (lastIdx < block.Items.Count)
                        {
                            var (_, off) = block.Items[lastIdx];
                            int end = off + block.Items[lastIdx].Item1.WordCount;
                            if (end - chunkStartOffset > MaxChunkWords) break;
                            chunkEnd = end;
                            lastIdx++;
                        }
                        int chunkLen = chunkEnd - chunkStartOffset;
                        if (chunkLen <= 0)
                            throw new PlcAddressException(block.Start.Raw,
                                $"单个变量“{block.Items[idx].Item1.Name}”占 {block.Items[idx].Item1.WordCount} 字，超过单次传输上限 {MaxChunkWords} 字，请减小其字数（如 String 的字数配置）");
                        var words = ArrayPool<ushort>.Shared.Rent(chunkLen);
                        try
                        {
                            Array.Clear(words, 0, chunkLen);
                            for (int k = idx; k < lastIdx; k++)
                            {
                                var (item, offset) = block.Items[k];
                                var encoded = ValueCodec.Encode(item, values.GetValueOrDefault(item.Name, "0"), device.DataFormat);
                                Array.Copy(encoded, 0, words, offset - chunkStartOffset, encoded.Length);
                            }
                            // 块内偏移一律按字计，换算成地址偏移时要过品牌归一（S7 ×2）
                            var startAddr = AdvanceWords(device.Brand, block.Start, chunkStartOffset);
                            await c.WriteWordsAsync(startAddr, words[..chunkLen], opCt).ConfigureAwait(false);
                        }
                        finally
                        {
                            ArrayPool<ushort>.Shared.Return(words);
                        }
                        idx = lastIdx;
                    }
                    done++;
                    Report(progress, device, TransferDirection.Download, done * 100 / totalUnits, $"写入 {block}");
                }

                foreach (var block in plan.BitBlocks)
                {
                    var bits = new bool[block.BitCount];
                    foreach (var (item, offset) in block.Items)
                        bits[offset] = values.GetValueOrDefault(item.Name, "0").Trim() is "1" or "true" or "True" or "TRUE";
                    await c.WriteBitsAsync(block.Start, bits, opCt).ConfigureAwait(false);
                    done++;
                    Report(progress, device, TransferDirection.Download, done * 100 / totalUnits, $"写入 {block}");
                }

                if (verify)
                {
                    var readBack = await ReadPlanValuesOnClientAsync(c, device, plan, progress, opCt).ConfigureAwait(false);
                    // 按类型语义比较（Bool "1"=="true"、数值 "100"=="100.0"），避免等价值误判为不一致
                    var mismatches = rows
                        .Where(r => !ValueCodec.ValuesEqual(r.DataType,
                            values.GetValueOrDefault(r.Name, "0"),
                            readBack.GetValueOrDefault(r.Name, "")))
                        .Select(r => r.Name).ToList();
                    if (mismatches.Count > 0)
                        throw new IOException($"回读校验不一致：{string.Join("、", mismatches.Take(5))}...");
                }

                // 配方标识（水印）：下载成功后把"配方名|v版本"写入设备配置的标识地址，供产线/MES 核对在用配方
                if (!string.IsNullOrWhiteSpace(watermark) && !string.IsNullOrWhiteSpace(device.RecipeTagAddress))
                {
                    await WriteRecipeTagAsync(c, device, watermark, opCt).ConfigureAwait(false);
                    tagWritten = true;
                }
            }, ct).ConfigureAwait(false);

            result.Status = TransferStatus.Success;
            result.Message = $"成功写入 {rows.Count} 个变量（{plan.WordBlocks.Count} 字块 / {plan.BitBlocks.Count} 位块）" +
                             (tagWritten ? "；配方标识已写入" : "");
        }
        catch (Exception ex)
        {
            result.Status = TransferStatus.Failed;
            result.Message = ex.Message;
            logger.LogWarning(ex, "下载到 {Device} 失败", device.Name);
        }
        result.DurationMs = sw.ElapsedMilliseconds;
        Report(progress, device, TransferDirection.Download, result.Status == TransferStatus.Success ? 100 : done * 100 / totalUnits,
            result.Status == TransferStatus.Success ? "下载成功" : $"失败：{result.Message}", result.Status);
        return result;
    }

    // ---------- 上传 ----------
    public async Task<DeviceTransferResult> UploadAsync(PlcDevice device, Recipe recipe,
        IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
    {
        var result = new DeviceTransferResult
        {
            DeviceId = device.Id,
            DeviceName = device.Name,
            Direction = TransferDirection.Upload
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // 按配方数据行的地址结构读取当前值（单次锁内原子快照）
            var uploadPlan = BuildPlan(device.Brand, recipe.Items);
            var values = await connections.ExecuteAsync(device,
                (c, opCt) => ReadPlanValuesOnClientAsync(c, device, uploadPlan, progress, opCt), ct).ConfigureAwait(false);
            result.ReadValues = values;
            result.Status = TransferStatus.Success;
            result.Message = $"成功读取 {values.Count} 个变量";
        }
        catch (Exception ex)
        {
            result.Status = TransferStatus.Failed;
            result.Message = ex.Message;
            logger.LogWarning(ex, "从 {Device} 上传失败", device.Name);
        }
        result.DurationMs = sw.ElapsedMilliseconds;
        Report(progress, device, TransferDirection.Upload, result.Status == TransferStatus.Success ? 100 : 0,
            result.Status == TransferStatus.Success ? "上传成功" : $"失败：{result.Message}", result.Status);
        return result;
    }

    /// <summary>按数据行读取 PLC 当前值（对比/上传共用，单次锁内原子快照）。</summary>
    public async Task<Dictionary<string, string>> ReadPlanValuesAsync(PlcDevice device, PlcBrand brand,
        IReadOnlyList<RecipeItem> rows, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var plan = BuildPlan(brand, rows);
        return await connections.ExecuteAsync(device,
            (c, opCt) => ReadPlanValuesOnClientAsync(c, device, plan, progress, opCt), ct).ConfigureAwait(false);
    }

    private static async Task<Dictionary<string, string>> ReadPlanValuesOnClientAsync(IPlcClient c, PlcDevice device,
        TransferPlan plan, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var values = new Dictionary<string, string>();
        int total = plan.WordBlocks.Count + plan.BitBlocks.Count, done = 0;
        foreach (var block in plan.WordBlocks)
        {
            var read = await c.ReadWordsAsync(block.Start, (ushort)block.WordCount, ct).ConfigureAwait(false);
            foreach (var (item, offset) in block.Items)
                values[item.Name] = ValueCodec.Decode(item, read[offset..(offset + item.WordCount)], false, device.DataFormat);
            done++;
            progress?.Report(new TransferProgress(device.Id, device.Name, TransferDirection.Upload, done * 100 / total, $"读取 {block}"));
        }
        foreach (var block in plan.BitBlocks)
        {
            var bits = await c.ReadBitsAsync(block.Start, (ushort)block.BitCount, ct).ConfigureAwait(false);
            foreach (var (item, offset) in block.Items)
                values[item.Name] = bits[offset] ? "1" : "0";
            done++;
            progress?.Report(new TransferProgress(device.Id, device.Name, TransferDirection.Upload, done * 100 / total, $"读取 {block}"));
        }
        return values;
    }

    /// <summary>把"配方名|v版本"写入设备的配方标识地址并回读核对（同锁内原子执行，失败即下载失败）。</summary>
    private static async Task WriteRecipeTagAsync(IPlcClient c, PlcDevice device, string watermark, CancellationToken ct)
    {
        var addr = PlcAddressParser.Parse(device.Brand, device.RecipeTagAddress!);
        if (addr.IsBitDevice)
            throw new PlcAddressException(device.RecipeTagAddress!, "配方标识地址必须是字地址（字符串区）");
        int words = Math.Clamp(device.RecipeTagWords <= 0 ? 12 : device.RecipeTagWords, 1, 64);
        var item = new RecipeItem
        {
            Name = "配方标识",
            Address = device.RecipeTagAddress!,
            DataType = PlcDataType.String,
            StringWords = words
        };
        var encoded = ValueCodec.Encode(item, watermark); // 超长抛 FormatException → 下载失败，提示加大标识字数
        await c.WriteWordsAsync(addr, encoded, ct).ConfigureAwait(false);
        var readBack = await c.ReadWordsAsync(addr, (ushort)encoded.Length, ct).ConfigureAwait(false);
        if (!readBack.SequenceEqual(encoded))
            throw new IOException("配方标识回读不一致（写入后校验失败）");
    }

    private static void Report(IProgress<TransferProgress>? progress, PlcDevice device,
        TransferDirection dir, int percent, string text, TransferStatus status = TransferStatus.Running) =>
        progress?.Report(new TransferProgress(device.Id, device.Name, dir, Math.Clamp(percent, 0, 100), text, status));
}
