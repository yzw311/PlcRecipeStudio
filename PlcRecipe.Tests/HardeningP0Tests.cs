using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;
using PlcRecipe.Infrastructure;
using PlcRecipe.Infrastructure.Backup;
using PlcRecipe.Infrastructure.Data;
using PlcRecipe.Infrastructure.Services;

namespace PlcRecipe.Tests;

/// <summary>P0 加固包回归测试：配方版本历史、乐观并发、账号锁定、UTC 时间口径。</summary>
public class HardeningP0Tests
{
    // ---------- 版本历史 ----------

    [SkippableFact]
    public async Task 保存配方_生成版本历史快照_含全部数据行()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var device = await host.CreateMockDeviceAsync("1#机");
        var recipe = await host.Recipes.CreateRecipeAsync(device.Id, "标准配方", null, "admin");

        var rows = TestHost.StandardRows();
        rows.ForEach(r => r.Recipe = recipe);
        await host.Recipes.SaveRecipeAsync(recipe.Id, rows, "admin", "首次保存");

        var history = await host.Recipes.GetVersionHistoryAsync(recipe.Id);
        Assert.Equal(2, history.Count); // 新建 v1 + 保存 v2（新语义：新建也留档）
        var entry = history.First(h => h.Version == 2);
        Assert.Equal("标准配方", entry.Name);
        Assert.Equal("admin", entry.SavedBy);
        Assert.Equal("首次保存", entry.ChangeNote);
        Assert.True(entry.SavedAtUtc > DateTime.UtcNow.AddMinutes(-1));

        var snapshot = await host.Recipes.GetVersionSnapshotAsync(recipe.Id, entry.Version);
        Assert.Equal(7, snapshot.Count);
        var temp = snapshot.First(i => i.Name == "温度");
        Assert.Equal("D100", temp.Address);
        Assert.Equal("12.5", temp.Value);
        Assert.Equal(PlcDataType.Float32, temp.DataType);
    }

    [SkippableFact]
    public async Task 版本历史_按版本倒序_且多条保留()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var device = await host.CreateMockDeviceAsync("1#机");
        var recipe = await host.CreateRecipeWithRowsAsync(device, "标准配方"); // v2

        var rows = (await host.Recipes.GetRecipeAsync(recipe.Id)).Items;
        rows.First(r => r.Name == "模式").Value = "9";
        await host.Recipes.SaveRecipeAsync(recipe.Id, rows, "admin", "修改模式"); // v3

        var history = await host.Recipes.GetVersionHistoryAsync(recipe.Id);
        Assert.Equal(new[] { 3, 2, 1 }, history.Select(h => h.Version)); // 新建 v1 也留档
        var v2 = history.Single(h => h.Version == 2);
        var v2Rows = await host.Recipes.GetVersionSnapshotAsync(recipe.Id, v2.Version);
        Assert.Equal("3", v2Rows.First(r => r.Name == "模式").Value); // 修改前的值还在
    }

    [SkippableFact]
    public async Task 回滚_把历史快照保存为新版本_值还原()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var device = await host.CreateMockDeviceAsync("1#机");
        var recipe = await host.CreateRecipeWithRowsAsync(device, "标准配方"); // v2：温度=12.5

        // 模拟现场改值后保存（v3：温度=99）
        var rows = (await host.Recipes.GetRecipeAsync(recipe.Id)).Items;
        rows.First(r => r.Name == "温度").Value = "99";
        await host.Recipes.SaveRecipeAsync(recipe.Id, rows, "admin", "现场改值");

        // 回滚到 v2 快照
        var history = await host.Recipes.GetVersionHistoryAsync(recipe.Id);
        var v2 = history.Single(h => h.Version == 2);
        var snapshot = await host.Recipes.GetVersionSnapshotAsync(recipe.Id, v2.Version);
        await host.Recipes.SaveRecipeAsync(recipe.Id, snapshot, "admin", "回滚自 v2");

        var final = await host.Recipes.GetRecipeAsync(recipe.Id);
        Assert.Equal(4, final.Version);
        Assert.Equal("12.5", final.Items.First(i => i.Name == "温度").Value);
        Assert.Equal("回滚自 v2",
            (await host.Recipes.GetVersionHistoryAsync(recipe.Id)).First(h => h.Version == 4).ChangeNote);
    }

    // ---------- 乐观并发（防互相覆盖） ----------

    [SkippableFact]
    public async Task 保存配方_基于过期版本_被拒绝()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var device = await host.CreateMockDeviceAsync("1#机");
        var recipe = await host.CreateRecipeWithRowsAsync(device, "标准配方"); // v2

        // 工作站 A 拿到 v2 的行，尚未保存
        var staleRows = (await host.Recipes.GetRecipeAsync(recipe.Id)).Items;
        staleRows.First(r => r.Name == "温度").Value = "1";
        staleRows.ForEach(r => r.Recipe = recipe);

        // 工作站 B 先保存成功 → v3
        var freshRows = (await host.Recipes.GetRecipeAsync(recipe.Id)).Items;
        freshRows.First(r => r.Name == "速度").Value = "200000";
        await host.Recipes.SaveRecipeAsync(recipe.Id, freshRows, "B");

        // A 再保存（基于 v2）→ 拒绝
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.Recipes.SaveRecipeAsync(recipe.Id, staleRows, "A"));
        Assert.Contains("已被其他用户修改", ex.Message);

        // 行不带 Recipe 导航（程序化调用，如信号路径）→ 跳过检查，兼容旧行为
        var legacyRows = TestHost.StandardRows();
        await host.Recipes.SaveRecipeAsync(recipe.Id, legacyRows, "signal");
    }

    // ---------- 配方名保留字符 ----------

    [SkippableFact]
    public async Task 建配方_复制_改名_名称含保留字符_被拒绝()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var device = await host.CreateMockDeviceAsync("1#机");
        var recipe = await host.Recipes.CreateRecipeAsync(device.Id, "原始配方", null, "admin");

        foreach (var bad in new[] { "a|b", "a\nb", "a\rb" })
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                host.Recipes.CreateRecipeAsync(device.Id, bad, null, "admin"));
            Assert.Contains("保留字符", ex.Message);

            var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                host.Recipes.CopyRecipeAsync(recipe.Id, bad, "admin"));
            Assert.Contains("保留字符", ex2.Message);

            var ex3 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                host.Recipes.RenameRecipeAsync(recipe.Id, bad, "admin"));
            Assert.Contains("保留字符", ex3.Message);
        }
    }

    // ---------- 账号锁定与强制改密 ----------

    [SkippableFact]
    public async Task 登录_连续失败5次锁定_正确密码也被拒()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        await host.Users.AddUserAsync("op1", "123456", UserRole.Operator, null);

        for (var i = 0; i < 5; i++)
            Assert.Null(await host.Users.VerifyAsync("op1", "wrong"));

        // 锁定中：正确密码也拒绝
        Assert.Null(await host.Users.VerifyAsync("op1", "123456"));

        // 锁定到期后恢复
        await using var db = await host.Provider.GetRequiredService<IDbContextFactory<AppDbContext>>()
            .CreateDbContextAsync();
        var user = await db.Users.FirstAsync(u => u.UserName == "op1");
        user.LockoutUntilUtc = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();
        Assert.NotNull(await host.Users.VerifyAsync("op1", "123456"));
    }

    [SkippableFact]
    public async Task 登录成功_清零失败计数()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        await host.Users.AddUserAsync("op1", "123456", UserRole.Operator, null);
        Assert.Null(await host.Users.VerifyAsync("op1", "wrong"));
        Assert.Null(await host.Users.VerifyAsync("op1", "wrong"));
        Assert.NotNull(await host.Users.VerifyAsync("op1", "123456"));
        Assert.Null(await host.Users.VerifyAsync("op1", "wrong")); // 若计数未清零，这次会触发锁定
        Assert.NotNull(await host.Users.VerifyAsync("op1", "123456"));
    }

    [SkippableFact]
    public async Task 新建用户与内置管理员_强制首次改密_改密后解除()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;
        var admin = await host.Users.VerifyAsync(DbInitializer.DefaultAdminName, "Test-only-Admin-Password-123!");
        Assert.NotNull(admin);
        Assert.True(admin!.MustChangePassword, "内置管理员应强制首次改密");

        await host.Users.AddUserAsync("op1", "123456", UserRole.Operator, null);
        await using var db = await host.Provider.GetRequiredService<IDbContextFactory<AppDbContext>>()
            .CreateDbContextAsync();
        var op = await db.Users.FirstAsync(u => u.UserName == "op1");
        Assert.True(op.MustChangePassword);

        await host.Users.ChangePasswordAsync(op.Id, "newpass1");
        db.Entry(op).State = EntityState.Detached;
        op = await db.Users.FirstAsync(u => u.UserName == "op1");
        Assert.False(op.MustChangePassword);
        Assert.Equal(0, op.FailedLoginCount);
    }

    [Fact]
    public async Task 操作日志_新写入为UTC_查询边界按本地时间换算()
    {
        var host = await TestHost.CreateAsync();
        await using var _ = host;

        await host.OpLogs.AddAsync("测试动作", "目标", "详情", true);
        await using var db = await host.Provider.GetRequiredService<IDbContextFactory<AppDbContext>>()
            .CreateDbContextAsync();
        var log = await db.OpLogs.AsNoTracking().OrderByDescending(l => l.Id).FirstAsync();
        // SQLite 的 DateTime 读回不带 Kind（Unspecified，显示层统一 SpecifyKind(Utc) 转本地），
        // 因此这里校验"值是 UTC 墙钟"而非 Kind
        Assert.True(log.Time >= DateTime.UtcNow.AddMinutes(-1));
        Assert.True(log.Time <= DateTime.UtcNow.AddSeconds(1));

        // 查询边界为本地时间（今天）→ 应能查到刚写入的 UTC 记录
        var (_, total) = await host.OpLogs.QueryAsync(
            DateTime.Today, DateTime.Today.AddDays(1), null, null, null, null, 1, 50);
        Assert.True(total >= 1);
    }

    // ---------- 自动备份（SQLite） ----------

    [Fact]
    public async Task 自动备份_生成快照并按保留份数清理()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"PlcRecipeBackup_{Guid.NewGuid():N}");
        try
        {
            var services = new ServiceCollection();
            services.AddPlcRecipeInfrastructure(dir);
            await using var provider = services.BuildServiceProvider();
            // 本测试不走 TestHost（不经过组合根初始化），须自行提供初始管理员密码；
            // 否则只能依赖并行测试恰好处于环境变量设置窗口内才能通过（时序依赖的假通过）
            var previous = Environment.GetEnvironmentVariable(DbInitializer.InitialAdminPasswordEnvironmentVariable);
            Environment.SetEnvironmentVariable(DbInitializer.InitialAdminPasswordEnvironmentVariable, "Test-only-Admin-Password-123!");
            try
            {
                await provider.InitializeDatabaseAsync();

                var backup = provider.GetRequiredService<DatabaseBackupService>();
                await backup.BackupAsync();
                await Task.Delay(1100); // 文件名精确到毫秒，确保文件名不同
                await backup.BackupAsync();

                var files = Directory.GetFiles(Path.Combine(dir, "backup"), "plc-recipe-*.db");
                Assert.Equal(2, files.Length);

                // 清理到只保留 1 份
                var settings = provider.GetRequiredService<ISettingsService>();
                settings.Settings.BackupKeepCount = 1;
                await backup.BackupAsync();
                Assert.Single(Directory.GetFiles(Path.Combine(dir, "backup"), "plc-recipe-*.db"));
            }
            finally
            {
                Environment.SetEnvironmentVariable(DbInitializer.InitialAdminPasswordEnvironmentVariable, previous);
            }
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* 忽略 */ }
        }
    }
}
