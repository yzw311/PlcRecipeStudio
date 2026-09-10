using PlcRecipe.Core.Models;

namespace PlcRecipe.WpfApp.Messages;

/// <summary>登录成功（App 切换到主窗口）。</summary>
public sealed record UserLoggedInMessage(User User);

/// <summary>已注销（App 切回登录窗口）。</summary>
public sealed record UserLoggedOutMessage;
