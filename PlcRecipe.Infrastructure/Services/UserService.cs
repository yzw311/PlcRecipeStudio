using Microsoft.EntityFrameworkCore;
using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;
using PlcRecipe.Infrastructure.Data;

namespace PlcRecipe.Infrastructure.Services;

/// <summary>用户认证与管理。全部写操作要求管理员权限（双保险：UI 页面亦有准入校验）。</summary>
public class UserService(IDbContextFactory<AppDbContext> factory, ICurrentUserService currentUser) : IUserService
{
    private void GuardAdmin()
    {
        var u = currentUser.Current;
        if (u == null || u.Role != UserRole.Admin)
            throw new UnauthorizedAccessException("该操作需要管理员权限");
    }

    public async Task<User?> VerifyAsync(string userName, string password, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == userName, ct).ConfigureAwait(false);
        if (user == null)
        {
            // 防用户名枚举：账号不存在也执行一次等价 PBKDF2，抹平响应时序差
            PasswordHasher.Hash(password);
            return null;
        }
        if (!user.Enabled) return null;
        // 锁定中：与凭据错误同响应（避免泄露账号状态）
        if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow) return null;
        if (!PasswordHasher.Verify(password, user.PasswordHash, user.Salt))
        {
            // 连续失败 5 次锁定 10 分钟（MES 基础安全：防暴力破解）。
            // 用 ExecuteUpdate 原子自增，避免并发失败登录的读-改-写丢失更新绕过锁定
            await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s
                .SetProperty(u => u.FailedLoginCount, u => u.FailedLoginCount + 1 >= 5 ? 0 : u.FailedLoginCount + 1)
                .SetProperty(u => u.LockoutUntilUtc, u => u.FailedLoginCount + 1 >= 5
                    ? (DateTime?)DateTime.UtcNow.AddMinutes(10)
                    : u.LockoutUntilUtc), ct).ConfigureAwait(false);
            return null;
        }
        user.FailedLoginCount = 0;
        user.LockoutUntilUtc = null;
        // 命中旧迭代次数的存量哈希 → 登录成功时透明升级到当前强度
        if (PasswordHasher.NeedsUpgrade(password, user.PasswordHash, user.Salt))
            (user.PasswordHash, user.Salt) = PasswordHasher.Hash(password);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return user;
    }

    public async Task<List<User>> GetUsersAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<User> AddUserAsync(string userName, string password, UserRole role, string? remark, CancellationToken ct = default)
    {
        GuardAdmin();
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        userName = userName.Trim();
        if (userName.Length == 0) throw new ArgumentException("用户名不能为空");
        if (password.Length < 6) throw new ArgumentException("密码至少 6 位");
        if (await db.Users.AnyAsync(u => u.UserName == userName, ct).ConfigureAwait(false))
            throw new InvalidOperationException($"用户“{userName}”已存在");
        var (hash, salt) = PasswordHasher.Hash(password);
        var user = new User
        {
            UserName = userName,
            PasswordHash = hash,
            Salt = salt,
            Role = role,
            Remark = remark,
            MustChangePassword = true, // 新建账号首次登录强制改密
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return user;
    }

    public async Task ChangePasswordAsync(int userId, string newPassword, CancellationToken ct = default)
    {
        GuardAdmin();
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        if (newPassword.Length < 6) throw new ArgumentException("密码至少 6 位");
        var user = await db.Users.FindAsync([userId], ct).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("用户不存在");
        (user.PasswordHash, user.Salt) = PasswordHasher.Hash(newPassword);
        user.MustChangePassword = false; // 重置/修改密码后解除强制改密
        user.FailedLoginCount = 0;
        user.LockoutUntilUtc = null;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>本人凭已验证的当前密码自助改密（首次登录强制改密走这里：登录流程已验过凭据，
    /// 此处再服务端复验当前密码；不要求管理员身份，与 ChangePasswordAsync 的管理员守卫互不影响）。</summary>
    public async Task ChangeOwnPasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        if (newPassword.Length < 6) throw new ArgumentException("密码至少 6 位");
        var user = await db.Users.FindAsync([userId], ct).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("用户不存在");
        if (!user.Enabled) throw new InvalidOperationException("账号已禁用");
        if (!PasswordHasher.Verify(currentPassword, user.PasswordHash, user.Salt))
            throw new UnauthorizedAccessException("当前密码验证失败");
        (user.PasswordHash, user.Salt) = PasswordHasher.Hash(newPassword);
        user.MustChangePassword = false;
        user.FailedLoginCount = 0;
        user.LockoutUntilUtc = null;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }


    public async Task SetUserEnabledAsync(int userId, bool enabled, CancellationToken ct = default)
    {
        GuardAdmin();
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var user = await db.Users.FindAsync([userId], ct).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("用户不存在");
        if (user.Id == currentUser.Current?.Id && !enabled)
            throw new InvalidOperationException("不能禁用当前登录账号");
        if (!enabled && user.Role == UserRole.Admin)
        {
            var otherEnabledAdmins = await db.Users
                .CountAsync(u => u.Role == UserRole.Admin && u.Enabled && u.Id != userId, ct).ConfigureAwait(false);
            if (otherEnabledAdmins == 0)
                throw new InvalidOperationException("必须保留至少一个启用的管理员");
        }
        user.Enabled = enabled;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task SetUserRoleAsync(int userId, UserRole role, CancellationToken ct = default)
    {
        GuardAdmin();
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var user = await db.Users.FindAsync([userId], ct).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("用户不存在");
        if (user.Role == UserRole.Admin && role != UserRole.Admin)
        {
            var otherAdmins = await db.Users
                .CountAsync(u => u.Role == UserRole.Admin && u.Id != userId, ct).ConfigureAwait(false);
            if (otherAdmins == 0)
                throw new InvalidOperationException("必须保留至少一个管理员");
        }
        user.Role = role;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteUserAsync(int userId, CancellationToken ct = default)
    {
        GuardAdmin();
        await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var user = await db.Users.FindAsync([userId], ct).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("用户不存在");
        if (user.UserName == DbInitializer.DefaultAdminName)
            throw new InvalidOperationException("内置管理员不可删除");
        if (user.Id == currentUser.Current?.Id)
            throw new InvalidOperationException("不能删除当前登录账号");
        if (user.Role == UserRole.Admin)
        {
            var otherAdmins = await db.Users
                .CountAsync(u => u.Role == UserRole.Admin && u.Id != userId, ct).ConfigureAwait(false);
            if (otherAdmins == 0)
                throw new InvalidOperationException("必须保留至少一个管理员");
        }
        db.Users.Remove(user);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>当前登录用户（进程级单例）。</summary>
public class CurrentUserService : ICurrentUserService
{
    private volatile User? _current;
    public User? Current => _current;
    public event Action? CurrentUserChanged;

    public void Set(User user)
    {
        _current = user;
        CurrentUserChanged?.Invoke();
    }

    public void Clear()
    {
        _current = null;
        CurrentUserChanged?.Invoke();
    }
}
