using PlcRecipe.Core;

namespace PlcRecipe.Core.Models;

/// <summary>系统用户。</summary>
public class User
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    /// <summary>PBKDF2-SHA256 (100k 迭代) 派生密钥，Base64</summary>
    public string PasswordHash { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>连续登录失败次数（达到阈值锁定）</summary>
    public int FailedLoginCount { get; set; }
    /// <summary>锁定截止时间（UTC；非空且在未来 = 锁定中）</summary>
    public DateTime? LockoutUntilUtc { get; set; }
    /// <summary>强制下次登录修改密码（内置管理员/新建用户为 true）</summary>
    public bool MustChangePassword { get; set; }
    /// <summary>创建时间（UTC）</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? Remark { get; set; }
}
