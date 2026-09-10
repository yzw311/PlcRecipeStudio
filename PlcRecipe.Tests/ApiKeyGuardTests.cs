using PlcRecipe.Core;
using PlcRecipe.Server;

namespace PlcRecipe.Tests;

/// <summary>API 多密钥分权映射（ApiKeyGuard.TryMapRole）：角色解析、fail closed、常量时间比较入口。</summary>
public class ApiKeyGuardTests
{
    [Theory]
    [InlineData("reader", UserRole.Operator)]
    [InlineData("engineer", UserRole.Engineer)]
    [InlineData("admin", UserRole.Admin)]
    public void 角色映射_命中合法角色_返回对应权限(string roleName, UserRole expected)
    {
        var ok = ApiKeyGuard.TryMapRole($"{roleName}:key1;engineer:key2", "key1", out var role);
        Assert.True(ok);
        Assert.Equal(expected, role);
    }

    [Fact]
    public void 角色映射_配置为空或键未命中_返回false()
    {
        Assert.False(ApiKeyGuard.TryMapRole(null, "key1", out _));
        Assert.False(ApiKeyGuard.TryMapRole("", "key1", out _));
        Assert.False(ApiKeyGuard.TryMapRole("reader:key1", "未配置的密钥", out _));
    }

    [Fact]
    public void 角色映射_未知角色或格式残缺_整条忽略_不放大权限()
    {
        // 旧实现里未知角色会落 Admin（权限放大）；现在必须整条忽略
        Assert.False(ApiKeyGuard.TryMapRole("superuser:key1;reader:key2", "key1", out _));
        Assert.False(ApiKeyGuard.TryMapRole("typo:key1", "key1", out _));
        // 缺冒号 / 冒号在开头或结尾 / 只有角色没密钥
        Assert.False(ApiKeyGuard.TryMapRole("readerkey1;:key1;reader:;reader", "key1", out _));
    }

    [Fact]
    public void 角色映射_提供的密钥原样比较_配置侧空白容错()
    {
        // 请求提供的密钥按原样、大小写敏感比较
        Assert.False(ApiKeyGuard.TryMapRole("reader:key1", "KEY1", out _));
        Assert.False(ApiKeyGuard.TryMapRole("reader:key1", " key1", out _));
        // 配置是手写的：条目与密钥两侧的多余空白修剪后再比对
        Assert.True(ApiKeyGuard.TryMapRole("reader : key1 ; engineer : key2", "key1", out var role));
        Assert.Equal(UserRole.Operator, role);
    }

    [Fact]
    public void 主密钥比较_相等与长度差异判定()
    {
        Assert.True(ApiKeyGuard.Equals("abc", "abc"));
        Assert.False(ApiKeyGuard.Equals("abc", "abd"));
        Assert.False(ApiKeyGuard.Equals("abc", "abcd")); // 长度不同直接 false
        Assert.False(ApiKeyGuard.Equals("", "x"));
        Assert.True(ApiKeyGuard.Equals("", ""));
    }
}
