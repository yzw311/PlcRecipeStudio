using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PlcRecipe.Infrastructure.Data;

namespace PlcRecipe.Server.Health;

/// <summary>数据库健康检查（供 /health 端点与容器编排探针使用）。</summary>
public sealed class DatabaseHealthCheck(IDbContextFactory<AppDbContext> factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            return await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false)
                ? HealthCheckResult.Healthy("数据库可连接")
                : HealthCheckResult.Degraded("数据库无法连接");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("数据库健康检查异常", ex);
        }
    }
}
