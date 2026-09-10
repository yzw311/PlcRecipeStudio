using System.Text.Json;
using PlcRecipe.Core.Interfaces;

namespace PlcRecipe.Infrastructure.Services;

/// <summary>软件设置（JSON 文件持久化，线程安全）。</summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly object _lock = new();

    public SettingsService(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "settings.json");
    }

    public AppSettings Settings { get; private set; } = new();

    /// <summary>最近一次 Load 是否因文件损坏/不可读而回退默认值（组合根可据此在日志就绪后补一条告警）。</summary>
    public bool LastLoadFailed { get; private set; }

    public void Load()
    {
        lock (_lock)
        {
            LastLoadFailed = false;
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                Settings = new AppSettings(); // 文件损坏时回退默认值
                LastLoadFailed = true;
                // 留下现场供人工恢复，并尽可能在任何日志系统就绪前给出可见线索
                try { File.Copy(_filePath, $"{_filePath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}", overwrite: true); } catch { }
                try { Console.Error.WriteLine($"settings.json 加载失败，已回退默认设置：{ex.Message}"); } catch { }
            }
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            // 先写临时文件再原子替换，避免写一半崩溃导致 settings.json 损坏（Load 只能回退默认值）
            var json = JsonSerializer.Serialize(Settings, JsonOpts);
            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _filePath, overwrite: true);
        }
    }
}
