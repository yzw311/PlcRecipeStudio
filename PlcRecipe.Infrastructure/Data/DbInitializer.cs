using Microsoft.EntityFrameworkCore;
using PlcRecipe.Core;
using PlcRecipe.Core.Models;
using Serilog;

namespace PlcRecipe.Infrastructure.Data;

/// <summary>
/// 建库与初始数据（内置管理员账号）。
/// SQLite：旧库缺列时增量迁移（ALTER TABLE ADD COLUMN），不破坏数据；
/// MySQL：新库由 EnsureCreated 建完整结构。
/// </summary>
public static class DbInitializer
{
    public const string DefaultAdminName = "admin";
    public const string InitialAdminPasswordEnvironmentVariable = "PLCRECIPE_INITIAL_ADMIN_PASSWORD";
    public const int MinimumInitialAdminPasswordLength = 12;

    /// <summary>增量迁移清单：表名 → (列名, 建列 SQL)。新增列时在此登记，启动时自动补列（仅 SQLite）。</summary>
    private static readonly (string Table, string Column, string Definition)[] ColumnMigrations =
    [
        // PlcDevices
        ("PlcDevices", "Brand", "INTEGER NOT NULL DEFAULT 99"),               // 旧行视为模拟 PLC
        ("PlcDevices", "S7CpuType", "TEXT NOT NULL DEFAULT 'S71200'"),
        ("PlcDevices", "SignalEnabled", "INTEGER NOT NULL DEFAULT 0"),
        ("PlcDevices", "DataFormat", "INTEGER NOT NULL DEFAULT 0"),           // ABCD
        ("PlcDevices", "RecipeNameAddress", "TEXT"),
        ("PlcDevices", "RecipeNameWords", "INTEGER NOT NULL DEFAULT 8"),
        ("PlcDevices", "RecipeTagAddress", "TEXT"),                           // 配方标识（水印）地址
        ("PlcDevices", "RecipeTagWords", "INTEGER NOT NULL DEFAULT 12"),
        // RecipeItems（参数上下限）
        ("RecipeItems", "UpperLimit", "REAL"),
        ("RecipeItems", "LowerLimit", "REAL"),
        // Users（登录锁定 + 强制改密）
        ("Users", "FailedLoginCount", "INTEGER NOT NULL DEFAULT 0"),
        ("Users", "LockoutUntilUtc", "TEXT"),
        ("Users", "MustChangePassword", "INTEGER NOT NULL DEFAULT 0"),
    ];

    /// <summary>建库与初始数据。返回 true = 本次启动新建了数据库（用于区分“全新安装”与“存量旧库”）。</summary>
    public static async Task<bool> InitializeAsync(AppDbContext db, CancellationToken ct = default, string? initialAdminPassword = null)
    {
        var created = await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);
        await MigrateColumnsAsync(db, ct).ConfigureAwait(false);
        await EnsureWalModeAsync(db, ct).ConfigureAwait(false);

        if (!await db.Users.AnyAsync(ct).ConfigureAwait(false))
        {
            var password = initialAdminPassword ?? Environment.GetEnvironmentVariable(InitialAdminPasswordEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(password) || password.Length < MinimumInitialAdminPasswordLength)
                throw new InvalidOperationException($"首次初始化需要设置环境变量 {InitialAdminPasswordEnvironmentVariable}，且密码至少 {MinimumInitialAdminPasswordLength} 个字符。");
            var (hash, salt) = PasswordHasher.Hash(password);
            db.Users.Add(new User
            {
                UserName = DefaultAdminName,
                PasswordHash = hash,
                Salt = salt,
                Role = UserRole.Admin,
                Enabled = true,
                MustChangePassword = true, // 内置管理员首次登录强制改密
                Remark = "内置管理员，首次登录后请修改密码",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return created;
    }

    /// <summary>对缺失的列逐条执行 ALTER TABLE ADD COLUMN（SQLite 支持，数据保留）。</summary>
    private static async Task MigrateColumnsAsync(AppDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlite()) return; // MySQL 新库由 EnsureCreated 建完整结构

        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var (table, column, definition) in ColumnMigrations)
            {
                var cols = new List<string>();
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"SELECT name FROM pragma_table_info('{table}')";
                    try
                    {
                        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                        while (await reader.ReadAsync(ct).ConfigureAwait(false))
                            cols.Add(reader.GetString(0));
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "数据库迁移：读取 {Table} 表结构失败", table);
                        continue;
                    }
                }

                if (cols.Contains(column, StringComparer.OrdinalIgnoreCase)) continue;
                try
                {
                    await using var alter = conn.CreateCommand();
                    alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
                    await alter.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    Log.Information("数据库增量迁移: {Table} 补列 {Column}", table, column);
                }
                catch (Exception ex)
                {
                    // 列已存在（并发启动等）可忽略；若复查后仍缺失说明是真实故障，带残缺 schema 运行会把错误
                    // 推迟到业务请求时更难定位——这里直接失败退出
                    var stillMissing = true;
                    await using (var check = conn.CreateCommand())
                    {
                        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}' COLLATE NOCASE";
                        stillMissing = Convert.ToInt64(await check.ExecuteScalarAsync(ct).ConfigureAwait(false)) == 0;
                    }
                    if (stillMissing)
                        throw new InvalidOperationException($"数据库增量迁移失败：{table}.{column} 补列未生效（磁盘/权限/锁库？）", ex);
                    Log.Warning(ex, "数据库迁移补列 {Table}.{Column} 疑似并发已存在，复查确认无需处理", table, column);
                }
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    /// <summary>开启 SQLite WAL 日志模式（持久化到库文件）：Server 宿主是多写者（API 写 + 信号联动日志 +
    /// 每日备份读），默认回滚日志模式写锁互斥易报 "database is locked"；WAL 读写不互斥。</summary>
    private static async Task EnsureWalModeAsync(AppDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsSqlite()) return;
        try
        {
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct).ConfigureAwait(false);
            Log.Information("SQLite journal_mode 已设为 WAL");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "设置 SQLite WAL 模式失败（继续以默认日志模式运行）");
        }
    }
}

/// <summary>PBKDF2-SHA256 密码哈希。</summary>
public static class PasswordHasher
{
        /// <summary>新哈希的迭代次数（OWASP 对 PBKDF2-SHA256 的现行建议）。</summary>
        public const int Iterations = 600_000;
        /// <summary>历史版本的迭代次数（仅用于验证存量哈希，成功登录时透明升级）。</summary>
        private const int LegacyIterations = 100_000;
        private const int SaltSize = 16;
        private const int KeySize = 32;

        public static (string hash, string salt) Hash(string password)
        {
            var salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(SaltSize);
            var key = Rfc2898(password, salt, Iterations);
            return (Convert.ToBase64String(key), Convert.ToBase64String(salt));
        }

        public static bool Verify(string password, string storedHash, string storedSalt) =>
            VerifyWith(password, storedHash, storedSalt, Iterations) ||
            VerifyWith(password, storedHash, storedSalt, LegacyIterations);

        /// <summary>存量哈希是否仍为旧迭代次数（验证通过但非当前强度，调用方应在登录成功后透明重哈希）。</summary>
        public static bool NeedsUpgrade(string password, string storedHash, string storedSalt) =>
            !VerifyWith(password, storedHash, storedSalt, Iterations) &&
            VerifyWith(password, storedHash, storedSalt, LegacyIterations);

        private static bool VerifyWith(string password, string storedHash, string storedSalt, int iterations)
        {
            try
            {
                var salt = Convert.FromBase64String(storedSalt);
                var expected = Convert.FromBase64String(storedHash);
                var actual = Rfc2898(password, salt, iterations);
                return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch
            {
                return false;
            }
        }

        private static byte[] Rfc2898(string password, byte[] salt, int iterations)
        {
            using var derive = new System.Security.Cryptography.Rfc2898DeriveBytes(
                password, salt, iterations, System.Security.Cryptography.HashAlgorithmName.SHA256);
            return derive.GetBytes(KeySize);
        }
    }
