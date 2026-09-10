using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;
using PlcRecipe.WpfApp.Messages;
using PlcRecipe.WpfApp.Services;

namespace PlcRecipe.WpfApp.ViewModels;

/// <summary>登录页 ViewModel。</summary>
public partial class LoginViewModel(
    IUserService users,
    ICurrentUserService currentUser,
    ISettingsService settings,
    IOpLogService opLogs,
    IDialogService dialogs) : ObservableObject
{
    /// <summary>登录页默认口令提示是否显示（产线部署可在系统管理里关掉）。</summary>
    public bool ShowCredentialHint => settings.Settings.ShowDefaultCredentialHint;

    [ObservableProperty]
    private string _userName = "admin";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private async Task LoginAsync(CancellationToken ct)
    {
        if (IsBusy) return;
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(UserName) || Password.Length == 0)
        {
            ErrorMessage = "请输入用户名和密码";
            return;
        }
        IsBusy = true;
        try
        {
            var verifiedPassword = Password; // VerifyAsync 通过的凭据，强制改密时服务端要复验
            var user = await users.VerifyAsync(UserName.Trim(), Password, ct).ConfigureAwait(true);
            if (user == null)
            {
                ErrorMessage = "用户名或密码错误，或账号已被禁用/锁定";
                Serilog.Log.Warning("登录失败（凭据错误、已禁用或锁定中），用户名={UserName}", UserName);
                _ = opLogs.AddAsync("登录", UserName.Trim(), "登录失败：凭据错误、账号已禁用或锁定中",
                    false).ConfigureAwait(false);
                return;
            }
            // 强制改密（内置管理员首次登录 / 新建账号 / 管理员重置后）：改完才算登录成功。
            // 注意必须用“本人凭已验证密码”的自助改密路径——此时还未 Set(currentUser)，管理员守卫必然拒绝
            if (user.MustChangePassword && !await ChangePasswordFirstAsync(user, verifiedPassword, ct).ConfigureAwait(true))
                return;
            currentUser.Set(user);
            _ = opLogs.AddAsync("登录", user.UserName, "登录成功", true).ConfigureAwait(false);
            WeakReferenceMessenger.Default.Send(new UserLoggedInMessage(user));
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "登录异常，用户名={UserName}", UserName);
            _ = opLogs.AddAsync("登录", UserName.Trim(), "登录异常：" + ex.Message, false).ConfigureAwait(false);
            ErrorMessage = "登录失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            Password = ""; // 触发 LoginWindow 的反向同步清空 PasswordBox，保持两端一致
        }
    }

    /// <summary>强制改密流程：两次输入一致并通过服务端校验后才放行登录。取消/失败返回 false（留在登录页）。</summary>
    private async Task<bool> ChangePasswordFirstAsync(User user, string verifiedPassword, CancellationToken ct)
    {
        var p1 = dialogs.PromptPassword("首次登录 / 密码已重置", $"用户“{user.UserName}”请设置新密码（至少 6 位）：");
        if (p1 == null)
        {
            ErrorMessage = "必须设置新密码后才能登录";
            return false;
        }
        if (p1.Length < 6)
        {
            ErrorMessage = "新密码至少 6 位";
            return false;
        }
        var p2 = dialogs.PromptPassword("首次登录 / 密码已重置", "再次输入新密码确认：");
        if (p2 != p1)
        {
            ErrorMessage = "两次输入的密码不一致";
            return false;
        }
        try
        {
            await users.ChangeOwnPasswordAsync(user.Id, verifiedPassword, p1, ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = "修改密码失败：" + ex.Message;
            return false;
        }
        _ = opLogs.AddAsync("修改密码", user.UserName, "首次登录/重置后修改密码", true).ConfigureAwait(false);
        dialogs.Info("密码已更新，即将进入系统。");
        return true;
    }
}
