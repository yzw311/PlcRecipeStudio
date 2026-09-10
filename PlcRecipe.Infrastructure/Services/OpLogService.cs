using PlcRecipe.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;

namespace PlcRecipe.Infrastructure.Services;

/// <summary>操作日志服务。</summary>
public class OpLogService(IDbContextFactory<AppDbContext> factory, ICurrentUserService currentUser) : IOpLogService
{
    public async Task AddAsync(string action, string? target, string? detail, bool success = true,
        TransferSource source = TransferSource.Manual, long durationMs = 0, string? userName = null, CancellationToken ct = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            db.OpLogs.Add(new OpLog
            {
                Time = DateTime.UtcNow, // 统一 UTC 落库，查询边界由调用方给本地时间、此处换算
                UserName = userName ?? currentUser.Current?.UserName ?? "未登录",
                Source = source,
                Action = action,
                Target = target,
                Detail = detail,
                Success = success,
                DurationMs = durationMs
            });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 日志写入失败不影响主业务，但审计丢失必须可见：Warning 级（Debug 会被全局 Information 阈值吞掉）
            Serilog.Log.Warning(ex, "操作日志写入失败");
        }
    }

    public async Task<(List<OpLog> Items, int Total)> QueryAsync(DateTime? from, DateTime? to, string? userName,
        TransferSource? source, string? actionKeyword, bool? success, int page, int pageSize, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var query = db.OpLogs.AsNoTracking().AsQueryable();
        // 库内为 UTC；调用方给的日期边界按本地时间解释（DatePicker 语义）。
        // MinValue/MaxValue 的 ToUniversalTime 在正偏移时区会抛 ArgumentOutOfRangeException，直接跳过该过滤
        if (from.HasValue && from.Value > DateTime.MinValue) query = query.Where(l => l.Time >= from.Value.ToUniversalTime());
        if (to.HasValue && to.Value < DateTime.MaxValue) query = query.Where(l => l.Time <= to.Value.ToUniversalTime());
        if (!string.IsNullOrWhiteSpace(userName)) query = query.Where(l => l.UserName.Contains(userName));
        if (source.HasValue) query = query.Where(l => l.Source == source.Value);
        if (!string.IsNullOrWhiteSpace(actionKeyword)) query = query.Where(l => l.Action.Contains(actionKeyword));
        if (success.HasValue) query = query.Where(l => l.Success == success.Value);

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(l => l.Time).ThenByDescending(l => l.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);
        return (items, total);
    }

    public async Task<List<OpLog>> RecentAsync(int count, TransferSource? source = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var query = db.OpLogs.AsNoTracking();
        if (source.HasValue) query = query.Where(l => l.Source == source.Value);
        return await query.OrderByDescending(l => l.Time).ThenByDescending(l => l.Id)
            .Take(count).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> CleanupAsync(int keepDays, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        // Time 统一 UTC 落库，阈值必须同为 UTC（本地时间会差出一个时区偏移，多删保留窗口内的日志）
        var threshold = DateTime.UtcNow.AddDays(-keepDays);
        var old = db.OpLogs.Where(l => l.Time < threshold);
        db.OpLogs.RemoveRange(old);
        return await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
