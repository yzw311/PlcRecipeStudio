using PlcRecipe.Core;

namespace PlcRecipe.Core.Models;

/// <summary>业务操作日志（上传/下载/配方变更/登录等）。</summary>
public class OpLog
{
    public long Id { get; set; }
    /// <summary>UTC 时间落库（展示层负责转本地时区）。</summary>
    public DateTime Time { get; set; } = DateTime.UtcNow;
    /// <summary>操作人；信号触发时为 "PLC信号"</summary>
    public string UserName { get; set; } = string.Empty;
    /// <summary>来源：手动 / 信号触发</summary>
    public TransferSource Source { get; set; }
    /// <summary>动作，如 下载配方 / 上传配方 / 新建配方 / 登录 / 修改配置</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>目标，如 "1#机" 或 "配方A"</summary>
    public string? Target { get; set; }
    /// <summary>详情（差异摘要、失败原因等）</summary>
    public string? Detail { get; set; }
    /// <summary>结果：成功 / 失败</summary>
    public bool Success { get; set; } = true;
    /// <summary>耗时（毫秒）</summary>
    public long DurationMs { get; set; }
}
