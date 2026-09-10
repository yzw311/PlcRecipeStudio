using PlcRecipe.Core.Models;

namespace PlcRecipe.Core.Interfaces;

/// <summary>软件设置（持久化为 JSON 文件）。</summary>
public sealed class AppSettings
{
    /// <summary>light / dark</summary>
    public string Theme { get; set; } = "light";
    /// <summary>启动是否需要登录；false = 自动以管理员身份直接进入</summary>
    public bool RequireLogin { get; set; } = false;
    /// <summary>批量下发最大并行 PLC 数</summary>
    public int MaxParallelDevices { get; set; } = 4;
    /// <summary>PLC 单次操作超时（毫秒）</summary>
    public int OperationTimeoutMs { get; set; } = 3000;
    /// <summary>下载前是否强制对比确认</summary>
    public bool ForceCompareOnDownload { get; set; } = true;
    /// <summary>下载后是否回读校验</summary>
    public bool VerifyAfterWrite { get; set; } = false;
    /// <summary>默认信号轮询间隔（毫秒）</summary>
    public int PollIntervalMs { get; set; } = 1000;
    /// <summary>日志保留天数</summary>
    public int LogKeepDays { get; set; } = 90;
    /// <summary>数据库引擎：sqlite（本地默认，零配置）/ mysql（MES 中央库）</summary>
    public string DatabaseProvider { get; set; } = "sqlite";
    /// <summary>MySQL 连接串（DatabaseProvider=mysql 时必填，如 Server=localhost;Database=plc_recipe;Uid=root;Pwd=xxx）</summary>
    public string DatabaseConnectionString { get; set; } = "";
    /// <summary>SQLite 自动备份保留份数（每日备份到数据目录 backup/）</summary>
    public int BackupKeepCount { get; set; } = 30;
    /// <summary>存量本地时间 → UTC 的一次性迁移是否已执行</summary>
    public bool UtcTimeMigrated { get; set; }
    /// <summary>旧数据库配方 → Pfdoc 文件库的一次性导出是否已执行（防止删光配方后重启复活）</summary>
    public bool PfdocSeedDone { get; set; }
    /// <summary>API 宿主（PlcRecipe.Server）的访问密钥；非空时 /api/* 请求必须携带 X-Api-Key 头。留空 = API 关闭（安全默认）</summary>
    public string ApiKey { get; set; } = "";
    /// <summary>登录页是否显示默认账号口令提示（产线部署建议关闭）</summary>
    public bool ShowDefaultCredentialHint { get; set; } = false;
}

/// <summary>设置读写服务。</summary>
public interface ISettingsService
{
    AppSettings Settings { get; }
    void Load();
    void Save();
}
