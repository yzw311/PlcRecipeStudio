using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;

namespace PlcRecipe.Infrastructure.FileStore;

/// <summary>
/// 配方文件库实现：dataDir\Pfdoc\{设备名}\{配方名}.txt（主文件）+
/// dataDir\Pfdoc\{设备名}\history\{配方名}\v{N}.txt（历史快照）。
/// UTF-8 原子写；文件/目录名对非法字符做替换（原名保留在文件头 Name= 行）。
/// </summary>
public sealed class RecipeFileStore(string dataDirectory, ILogger<RecipeFileStore> logger) : IRecipeFileStore
{
    public string Directory => Path.Combine(dataDirectory, "Pfdoc");

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);
    private static SemaphoreSlim LockFor(string deviceName, string recipeName) =>
        Locks.GetOrAdd(Path.GetFullPath(Path.Combine("Pfdoc", Sanitize(deviceName), Sanitize(recipeName))).ToUpperInvariant(), _ => new SemaphoreSlim(1, 1));

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
        { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

    private static string NormalizeName(string name)
    {
        var s = name.Trim();
        if (s.Length == 0) s = "_";
        if (ReservedNames.Contains(s.TrimEnd('.'))) s = "_" + s;
        return s;
    }

    private string DeviceDirOf(string deviceName) => Path.Combine(Directory, Sanitize(deviceName));

    private string RecipePathOf(string deviceName, string recipeName) =>
        Path.Combine(DeviceDirOf(deviceName), Sanitize(recipeName) + ".txt");

    private string HistoryDirOf(string deviceName, string recipeName) =>
        Path.Combine(DeviceDirOf(deviceName), "history", Sanitize(recipeName));

    internal static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
        var s = sb.ToString().Trim();
        return NormalizeName(s);
    }

    public bool Exists(string deviceName, string recipeName) => File.Exists(RecipePathOf(deviceName, recipeName));

    public async Task CommitAsync(string deviceName, string recipeName, int expectedVersion, int version, IReadOnlyList<RecipeItem> rows, string? savedBy = null, string? changeNote = null)
    {
        var gate = LockFor(deviceName, recipeName); await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var current = await TryLoadAsync(deviceName, recipeName).ConfigureAwait(false);
            if ((current?.Version ?? 0) != expectedVersion) throw new InvalidOperationException("版本冲突");
            await SaveHistoryAsync(deviceName, recipeName, version, rows, savedBy, changeNote).ConfigureAwait(false);
            try { await SaveUnlockedAsync(deviceName, recipeName, version, rows).ConfigureAwait(false); }
            catch
            {
                // 主文件写失败时回删刚写的快照：历史里不能留下从未生效的孤儿版本
                try { File.Delete(Path.Combine(HistoryDirOf(deviceName, recipeName), $"v{version}.txt")); } catch { }
                throw;
            }
        }
        finally { gate.Release(); }
    }

    private async Task SaveUnlockedAsync(string deviceName, string recipeName, int version, IReadOnlyList<RecipeItem> rows)
    {
        System.IO.Directory.CreateDirectory(DeviceDirOf(deviceName));
        var path = RecipePathOf(deviceName, recipeName);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var sb = new StringBuilder();
        sb.AppendLine("#PlcRecipeFile v1"); sb.AppendLine($"Name={recipeName}"); sb.AppendLine($"Device={deviceName}");
        sb.AppendLine($"Version={version}"); sb.AppendLine($"SavedAtUtc={DateTime.UtcNow:O}");
        sb.AppendLine("# 数据行格式: 名称|地址|类型|StringWords|值|下限|上限|单位|备注"); AppendRows(sb, rows);
        await File.WriteAllTextAsync(tmp, sb.ToString(), Encoding.UTF8).ConfigureAwait(false);
        File.Move(tmp, path, overwrite: true);
    }

    public async Task SaveAsync(string deviceName, string recipeName, int version, IReadOnlyList<RecipeItem> rows)
    {
        var gate = LockFor(deviceName, recipeName); await gate.WaitAsync().ConfigureAwait(false);
        try { await SaveUnlockedAsync(deviceName, recipeName, version, rows).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    public async Task<RecipeFileDocument?> TryLoadAsync(string deviceName, string recipeName)
    {
        var path = RecipePathOf(deviceName, recipeName);
        if (!File.Exists(path)) return null;
        return await LoadFromPathAsync(path, deviceName).ConfigureAwait(false);
    }

    public IReadOnlyList<RecipeFileDocument> LoadAll()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var result = new List<RecipeFileDocument>();
        foreach (var deviceDir in System.IO.Directory.EnumerateDirectories(Directory))
        {
            var deviceName = System.IO.Path.GetFileName(deviceDir);
            if (deviceName.Equals("history", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var path in System.IO.Directory.EnumerateFiles(deviceDir, "*.txt"))
            {
                try
                {
                    var doc = LoadFromPathAsync(path, deviceName).ConfigureAwait(false).GetAwaiter().GetResult();
                    if (doc != null) result.Add(doc);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "配方文件解析失败（已跳过）：{File}", path);
                }
            }
        }
        return result;
    }

    public Task DeleteAsync(string deviceName, string recipeName)
    {
        try
        {
            var path = RecipePathOf(deviceName, recipeName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "配方文件库删除失败：{Device}/{Name}", deviceName, recipeName);
        }
        return Task.CompletedTask;
    }

    public async Task RenameAsync(string deviceName, string oldName, string newName)
    {
        if (Exists(deviceName, newName))
            throw new InvalidOperationException($"配方“{newName}”已存在");
        var doc = await TryLoadAsync(deviceName, oldName).ConfigureAwait(false)
                  ?? throw new InvalidOperationException($"配方“{oldName}”不存在");
        await SaveAsync(deviceName, newName, doc.Version, doc.Items).ConfigureAwait(false);

        var oldDir = HistoryDirOf(deviceName, oldName);
        var newDir = HistoryDirOf(deviceName, newName);
            if (System.IO.Directory.Exists(oldDir))
            {
                if (System.IO.Directory.Exists(newDir))
                    throw new InvalidOperationException("目标目录已存在");
                System.IO.Directory.Move(oldDir, newDir);
            }
        await DeleteAsync(deviceName, oldName).ConfigureAwait(false);
        await DeleteHistoryAsync(deviceName, oldName).ConfigureAwait(false);
    }

    public async Task SaveHistoryAsync(string deviceName, string recipeName, int version,
        IReadOnlyList<RecipeItem> rows, string? savedBy, string? changeNote)
    {
        try
        {
            var dir = HistoryDirOf(deviceName, recipeName);
            System.IO.Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"v{version}.txt");
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var sb = new StringBuilder();
            sb.AppendLine("#PlcRecipeFile v1");
            sb.AppendLine($"Name={recipeName}");
            sb.AppendLine($"Version={version}");
            sb.AppendLine($"SavedAtUtc={DateTime.UtcNow:O}");
            sb.AppendLine($"SavedBy={savedBy ?? ""}");
            sb.AppendLine($"ChangeNote={changeNote ?? ""}");
            sb.AppendLine("# 数据行格式: 名称|地址|类型|StringWords|值|下限|上限|单位|备注");
            AppendRows(sb, rows);
            await File.WriteAllTextAsync(tmp, sb.ToString(), Encoding.UTF8).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "配方历史快照写入失败：{Device}/{Name} v{Version}", deviceName, recipeName, version);
            throw;
        }
    }

    public IReadOnlyList<RecipeVersionHistory> GetHistory(string deviceName, string recipeName)
    {
        var dir = HistoryDirOf(deviceName, recipeName);
        if (!System.IO.Directory.Exists(dir)) return [];
        var result = new List<RecipeVersionHistory>();
        foreach (var path in System.IO.Directory.EnumerateFiles(dir, "v*.txt"))
        {
            try
            {
                var doc = LoadFromPathAsync(path, deviceName).ConfigureAwait(false).GetAwaiter().GetResult();
                if (doc == null) continue;
                string savedBy = "", changeNote = "", savedAt = "";
                foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    var line = raw.Trim();
                    var eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = line[..eq].Trim();
                    var value = line[(eq + 1)..].Trim();
                    if (key.Equals("SavedBy", StringComparison.OrdinalIgnoreCase)) savedBy = value;
                    else if (key.Equals("ChangeNote", StringComparison.OrdinalIgnoreCase)) changeNote = value;
                    else if (key.Equals("SavedAtUtc", StringComparison.OrdinalIgnoreCase)) savedAt = value;
                }
                DateTime.TryParse(savedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var at);
                result.Add(new RecipeVersionHistory
                {
                    Version = doc.Version,
                    Name = doc.Name,
                    SavedBy = savedBy,
                    SavedAtUtc = at,
                    ChangeNote = changeNote
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "配方历史解析失败（已跳过）：{File}", path);
            }
        }
        return result.OrderByDescending(h => h.Version).ToList();
    }

    public async Task<List<RecipeItem>?> TryLoadHistoryAsync(string deviceName, string recipeName, int version)
    {
        var path = Path.Combine(HistoryDirOf(deviceName, recipeName), $"v{version}.txt");
        if (!File.Exists(path)) return null;
        var doc = await LoadFromPathAsync(path, deviceName).ConfigureAwait(false);
        return doc?.Items.ToList();
    }

    public Task DeleteHistoryAsync(string deviceName, string recipeName)
    {
        try
        {
            var dir = HistoryDirOf(deviceName, recipeName);
            if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "配方历史删除失败：{Device}/{Name}", deviceName, recipeName);
        }
        return Task.CompletedTask;
    }

    public Task UpdateDeviceNameAsync(string oldDeviceName, string newDeviceName)
    {
        try
        {
            var oldDir = DeviceDirOf(oldDeviceName);
            var newDir = DeviceDirOf(newDeviceName);
            if (!System.IO.Directory.Exists(oldDir)) return Task.CompletedTask;
            System.IO.Directory.CreateDirectory(Path.Combine(Directory));
            if (System.IO.Directory.Exists(newDir))
                throw new InvalidOperationException("目标目录已存在");
            System.IO.Directory.Move(oldDir, newDir);
            logger.LogInformation("设备配方目录已迁移：{Old} → {New}", oldDeviceName, newDeviceName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "设备配方目录迁移失败：{Old} → {New}", oldDeviceName, newDeviceName);
        }
        return Task.CompletedTask;
    }

    /// <summary>旧版平铺布局迁移：Pfdoc 根目录的 txt 按头部 Device=（缺省“未分配”）移入设备子目录。</summary>
    public void MigrateFlatLayout()
    {
        System.IO.Directory.CreateDirectory(Directory);
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*.txt"))
        {
            try
            {
                var doc = LoadFromPathAsync(path, null).ConfigureAwait(false).GetAwaiter().GetResult();
                if (doc == null) continue;
                var deviceName = string.IsNullOrWhiteSpace(doc.Device) ? "未分配" : doc.Device;
                System.IO.Directory.CreateDirectory(DeviceDirOf(deviceName));
                var target = RecipePathOf(deviceName, doc.Name);
                if (File.Exists(target)) File.Delete(target); // 平铺迁移同名以文件为准覆盖
                File.Move(path, target);
                logger.LogInformation("配方文件迁移至设备目录：{Device}/{Name}", deviceName, doc.Name);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "平铺配方文件迁移失败（已跳过）：{File}", path);
            }
        }
    }

    // ---------- 内部：文件解析与数据行序列化 ----------

    private static void AppendRows(StringBuilder sb, IReadOnlyList<RecipeItem> rows)
    {
        foreach (var r in rows)
        {
            // 名称是行首字段：# 开头会被解析器当注释跳过，前置转义符保住该行（SplitEscaped 会还原）
            var name = Escape(r.Name);
            if (name.StartsWith('#')) name = "\\" + name;
            sb.Append(name).Append('|')
              .Append(Escape(r.Address)).Append('|')
              .Append(r.DataType).Append('|')
              .Append(r.StringWords).Append('|')
              .Append(Escape(r.Value ?? "")).Append('|')
              .Append(r.LowerLimit?.ToString(CultureInfo.InvariantCulture) ?? "").Append('|')
              .Append(r.UpperLimit?.ToString(CultureInfo.InvariantCulture) ?? "").Append('|')
              .Append(Escape(r.Unit ?? "")).Append('|')
              .AppendLine(Escape(r.Remark ?? ""));
        }
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("|", "\\|").Replace("\r", "\\r").Replace("\n", "\\n");
    private static string[] SplitEscaped(string line)
    {
        var parts = new List<string>(); var sb = new StringBuilder(); var escaped = false;
        foreach (var c in line) { if (escaped) { sb.Append(c switch { 'n' => '\n', 'r' => '\r', _ => c }); escaped = false; } else if (c == '\\') escaped = true; else if (c == '|') { parts.Add(sb.ToString()); sb.Clear(); } else sb.Append(c); }
        if (escaped) sb.Append('\\'); parts.Add(sb.ToString()); return parts.ToArray();
    }

    private static async Task<RecipeFileDocument?> LoadFromPathAsync(string path, string? fallbackDevice)
    {
        if (!File.Exists(path)) return null;
        string name = Path.GetFileNameWithoutExtension(path);
        string? device = fallbackDevice;
        int version = 1;
        var items = new List<RecipeItem>();
        foreach (var rawLine in await File.ReadAllLinesAsync(path, Encoding.UTF8).ConfigureAwait(false))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var eq = line.IndexOf('=');
            if (eq > 0 && !line.Contains('|'))
            {
                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();
                if (key.Equals("Name", StringComparison.OrdinalIgnoreCase)) name = value;
                else if (key.Equals("Device", StringComparison.OrdinalIgnoreCase) && fallbackDevice == null) device = value;
                else if (key.Equals("Version", StringComparison.OrdinalIgnoreCase)
                         && int.TryParse(value, out var v)) version = v;
                continue;
            }

            var parts = SplitEscaped(line);
            if (parts.Length < 5) continue;
            if (!Enum.TryParse<PlcDataType>(parts[2].Trim(), ignoreCase: true, out var type)) continue;
            if (!int.TryParse(parts[3].Trim(), out var words)) continue;

            items.Add(new RecipeItem
            {
                Name = parts[0].Trim(),
                Address = parts[1].Trim(),
                DataType = type,
                StringWords = words <= 0 ? 8 : words,
                Value = parts[4],
                LowerLimit = ParseOpt(parts, 5),
                UpperLimit = ParseOpt(parts, 6),
                Unit = parts.Length > 7 && parts[7].Trim().Length > 0 ? parts[7].Trim() : null,
                Remark = parts.Length > 8 && parts[8].Trim().Length > 0 ? parts[8].Trim() : null
            });
        }
        return new RecipeFileDocument(name, version, device, items);
    }

    private static double? ParseOpt(string[] parts, int index) =>
        parts.Length > index && parts[index].Trim().Length > 0
            && double.TryParse(parts[index].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : null;
}
