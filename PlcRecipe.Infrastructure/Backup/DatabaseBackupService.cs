using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Infrastructure.Data;

namespace PlcRecipe.Infrastructure.Backup;

/// <summary>
/// SQLite 数据库自动备份：VACUUM INTO 生成一致性快照到 backup/ 子目录，按保留份数清理。
/// MySQL 部署时使用数据库原生备份方案（mysqldump），本服务自动跳过。
/// </summary>
public sealed class DatabaseBackupService(
    IDbContextFactory<AppDbContext> factory,
    ISettingsService settings,
    string dataDirectory,
    ILogger<DatabaseBackupService> logger)
{
    private const int MinKeepCount = 1;
    private static readonly SemaphoreSlim BackupLock = new(1, 1);

    private string BackupDirectory => Path.Combine(dataDirectory, "backup");

    /// <summary>执行一次备份并按 BackupKeepCount 清理旧份。失败只记日志，不影响主业务。</summary>
    public async Task BackupAsync()
    {
        if (!await BackupLock.WaitAsync(0).ConfigureAwait(false)) return;
        string? temp = null;
        try
        {
            var keep = Math.Max(MinKeepCount, settings.Settings.BackupKeepCount);
            await using var db = await factory.CreateDbContextAsync().ConfigureAwait(false);
            if (!db.Database.IsSqlite()) return;
            Directory.CreateDirectory(BackupDirectory);
            var name = $"plc-recipe-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.db";
            var target = Path.Combine(BackupDirectory, name);
            temp = Path.Combine(BackupDirectory, $".{name}.{Guid.NewGuid():N}.tmp");
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync($"VACUUM INTO '{temp.Replace("'", "''")}'").ConfigureAwait(false);
#pragma warning restore EF1002
            var info = new FileInfo(temp);
            if (!info.Exists || info.Length == 0) throw new IOException("备份文件完整性检查失败");
            File.Move(temp, target); // 同目录移动是原子发布
            temp = null;
            logger.LogInformation("数据库备份完成：{File}", target);
            PruneOldBackups(keep);
        }
        catch (Exception ex) { logger.LogWarning(ex, "数据库备份失败（不影响主业务）"); }
        finally
        {
            if (temp != null) try { File.Delete(temp); } catch { }
            BackupLock.Release();
        }
    }

    private void PruneOldBackups(int keep)
    {
        var files = Directory.GetFiles(BackupDirectory, "plc-recipe-*.db")
            .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var old in files.Skip(keep))
        {
            try
            {
                File.Delete(old);
                logger.LogInformation("清理过期备份：{File}", old);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "删除过期备份失败：{File}", old);
            }
        }
    }
}

/// <summary>宿主化备份调度：启动后延迟 1 分钟做首次备份，此后每 24 小时一次。</summary>
public sealed class DatabaseBackupHostedService(DatabaseBackupService backup) : IHostedService
{
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            // 延迟 1 分钟：避开启动期数据库初始化与 PLC 连接建立
            await Task.Delay(TimeSpan.FromMinutes(1), ct).ConfigureAwait(false);
            while (!ct.IsCancellationRequested)
            {
                await backup.BackupAsync().ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromHours(24), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* 正常停止 */ }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "数据库备份调度异常退出");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_loop != null)
        {
            try { await _loop.ConfigureAwait(false); } catch { /* 忽略 */ }
        }
        _cts?.Dispose();
    }
}
