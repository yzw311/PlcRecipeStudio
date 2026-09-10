using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;
using PlcRecipe.WpfApp.Services;

namespace PlcRecipe.WpfApp.ViewModels;

/// <summary>用户行。</summary>
public partial class UserRowViewModel : ObservableObject
{
    public User User { get; }
    public string RoleText => User.Role switch
    {
        UserRole.Admin => "管理员",
        UserRole.Engineer => "工程师",
        _ => "操作员"
    };
    public string EnabledText => User.Enabled ? "启用" : "禁用";

    public UserRowViewModel(User user) => User = user;
}

/// <summary>操作日志行。</summary>
public partial class LogRowViewModel : ObservableObject
{
    [ObservableProperty]
    private OpLog _log = null!;

    public string TimeText => DateTime.SpecifyKind(Log.Time, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string SourceText => Log.Source == TransferSource.SignalTrigger ? "信号触发" : "手动";
    public string ResultText => Log.Success ? "成功" : "失败";

    public LogRowViewModel(OpLog log) => _log = log;
}

/// <summary>系统管理页。仅管理员可用（页面准入校验 + 服务端 UserService 管理员守卫双保险）。</summary>
public partial class SystemManageViewModel(
    IUserService users,
    IOpLogService opLogs,
    ISettingsService settings,
    IThemeService theme,
    ICurrentUserService currentUser,
    IDialogService dialogs) : ObservableObject, IActivatablePage
{
    // ---- 用户管理 ----
    public ObservableCollection<UserRowViewModel> Users { get; } = new();

    // ---- 日志查询 ----
    public ObservableCollection<LogRowViewModel> Logs { get; } = new();

    [ObservableProperty]
    private DateTime _logFrom = DateTime.Today.AddDays(-7);

    [ObservableProperty]
    private DateTime _logTo = DateTime.Today;

    [ObservableProperty]
    private string _logUserFilter = "";

    [ObservableProperty]
    private TransferSource? _logSource;

    [ObservableProperty]
    private int _logPage = 1;

    [ObservableProperty]
    private int _logTotalPages = 1;

    [ObservableProperty]
    private int _logTotalCount;

    private const int PageSize = 50;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private int _refreshGeneration;

    partial void OnLogPageChanged(int value)
    {
        PrevPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }

    partial void OnLogTotalPagesChanged(int value) => NextPageCommand.NotifyCanExecuteChanged();

    // ---- 设置 ----
    [ObservableProperty]
    private string _themeChoice = "亮色"; // 显示值（亮色/暗色），保存时映射回 light/dark

    [ObservableProperty]
    private bool _requireLogin;

    [ObservableProperty]
    private int _maxParallelDevices = 4;

    [ObservableProperty]
    private int _operationTimeoutMs = 3000;

    [ObservableProperty]
    private bool _forceCompareOnDownload = true;

    [ObservableProperty]
    private bool _verifyAfterWrite;

    [ObservableProperty]
    private int _pollIntervalMs = 1000;

    [ObservableProperty]
    private int _logKeepDays = 90;

    // ---- 数据库（SQLite 本地默认 / MySQL MES 中央库，引擎切换需重启） ----
    [ObservableProperty]
    private string _dbProviderChoice = "SQLite"; // 显示值：SQLite / MySQL

    [ObservableProperty]
    private string _dbConnectionString = "";

    [ObservableProperty]
    private int _backupKeepCount = 30;

    // ---- API 宿主（PlcRecipe.Server 的访问密钥；留空 = API 关闭） ----
    [ObservableProperty]
    private string _apiKey = "";

    [ObservableProperty]
    private string _apiKeyRoles = "";

    // ---- 登录页默认口令提示（产线部署建议关闭） ----
    [ObservableProperty]
    private bool _showDefaultCredentialHint;

    public Task LoadAsync()
    {
        if (currentUser.Current?.Role != UserRole.Admin)
        {
            dialogs.Error("系统管理仅管理员可用");
            return Task.CompletedTask;
        }
        return RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await _refreshGate.WaitAsync().ConfigureAwait(true);
        var generation = Interlocked.Increment(ref _refreshGeneration);
        try
        {
            var list = await users.GetUsersAsync().ConfigureAwait(true);
            if (generation != _refreshGeneration) return;
            Users.Clear();
            foreach (var u in list) Users.Add(new UserRowViewModel(u));
            await QueryLogsCoreAsync(generation).ConfigureAwait(true);
            if (generation != _refreshGeneration) return;
            var s = settings.Settings;
            ThemeChoice = s.Theme == "dark" ? "暗色" : "亮色";
            RequireLogin = s.RequireLogin; MaxParallelDevices = s.MaxParallelDevices;
            OperationTimeoutMs = s.OperationTimeoutMs; ForceCompareOnDownload = s.ForceCompareOnDownload;
            VerifyAfterWrite = s.VerifyAfterWrite; PollIntervalMs = s.PollIntervalMs; LogKeepDays = s.LogKeepDays;
            DbProviderChoice = (s.DatabaseProvider ?? "sqlite").Equals("mysql", StringComparison.OrdinalIgnoreCase) ? "MySQL" : "SQLite";
            DbConnectionString = s.DatabaseConnectionString; BackupKeepCount = s.BackupKeepCount;
            ApiKey = s.ApiKey; ApiKeyRoles = s.ApiKeyRoles ?? ""; ShowDefaultCredentialHint = s.ShowDefaultCredentialHint;
        }
        catch (Exception ex) { Serilog.Log.Error(ex, "系统管理刷新失败"); dialogs.Error("刷新失败：" + ex.Message); }
        finally { _refreshGate.Release(); }
    }

    // ---- 用户 ----
    [RelayCommand]
    private async Task AddUserAsync()
    {
        var name = dialogs.PromptText("新增用户", "用户名：");
        if (name == null) return;
        var pwd = dialogs.PromptPassword("新增用户", "密码（至少 6 位）：");
        if (pwd == null) return;
        if (pwd.Length < 6) { dialogs.Error("密码至少 6 位"); return; }
        var role = dialogs.PromptChoice("新增用户", "角色：", ["操作员", "工程师", "管理员"], "操作员");
        if (role == null) return;
        try
        {
            await users.AddUserAsync(name, pwd, role switch
            {
                "工程师" => UserRole.Engineer,
                "管理员" => UserRole.Admin,
                _ => UserRole.Operator
            }, null).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task ResetPasswordAsync(UserRowViewModel? row)
    {
        if (row == null) { dialogs.Info("请先选中用户"); return; }
        var pwd = dialogs.PromptPassword("重置密码", $"用户“{row.User.UserName}”的新密码（至少 6 位）：");
        if (pwd == null) return;
        if (pwd.Length < 6) { dialogs.Error("密码至少 6 位"); return; }
        try
        {
            await users.ChangePasswordAsync(row.User.Id, pwd).ConfigureAwait(true);
            dialogs.Info("密码已重置。");
        }
        catch (Exception ex) { dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync(UserRowViewModel? row)
    {
        if (row == null) { dialogs.Info("请先选中用户"); return; }
        if (row.User.Enabled &&
            !dialogs.Confirm($"确定禁用用户“{row.User.UserName}”？", "禁用确认"))
            return;
        try
        {
            await users.SetUserEnabledAsync(row.User.Id, !row.User.Enabled).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task ChangeRoleAsync(UserRowViewModel? row)
    {
        if (row == null) { dialogs.Info("请先选中用户"); return; }
        var role = dialogs.PromptChoice("修改角色", $"用户“{row.User.UserName}”的新角色：",
            ["操作员", "工程师", "管理员"], row.RoleText);
        if (role == null) return;
        try
        {
            await users.SetUserRoleAsync(row.User.Id, role switch
            {
                "工程师" => UserRole.Engineer,
                "管理员" => UserRole.Admin,
                _ => UserRole.Operator
            }).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private async Task DeleteUserAsync(UserRowViewModel? row)
    {
        if (row == null) { dialogs.Info("请先选中用户"); return; }
        if (!dialogs.Confirm($"确定删除用户“{row.User.UserName}”？", "删除确认")) return;
        try
        {
            await users.DeleteUserAsync(row.User.Id).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { dialogs.Error(ex.Message); }
    }

    // ---- 日志 ----
    [RelayCommand]
    private async Task QueryLogsAsync()
    {
        LogPage = 1; // 重新查询回到第一页，避免残留页码造成“假空页”
        await QueryLogsCoreAsync().ConfigureAwait(true);
    }

    private async Task QueryLogsCoreAsync(int? expectedGeneration = null)
    {
        try
        {
            var page = Math.Clamp(LogPage, 1, Math.Max(1, LogTotalPages));
            if (page != LogPage) LogPage = page;
            var to = LogTo.Date.AddDays(1);
            var (items, total) = await opLogs.QueryAsync(
                LogFrom.Date, to, string.IsNullOrWhiteSpace(LogUserFilter) ? null : LogUserFilter,
                LogSource, null, null, page, PageSize).ConfigureAwait(true);
            if (expectedGeneration.HasValue && expectedGeneration.Value != _refreshGeneration) return;
            var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
            if (LogPage > totalPages) { LogPage = totalPages; return; }
            Logs.Clear(); foreach (var l in items) Logs.Add(new LogRowViewModel(l));
            LogTotalCount = total; LogTotalPages = totalPages;
        }
        catch (Exception ex) { dialogs.Error(ex.Message); }
    }

    [RelayCommand(CanExecute = nameof(CanGoPrev))]
    private async Task PrevPageAsync()
    {
        LogPage--;
        await QueryLogsCoreAsync().ConfigureAwait(true);
    }

    private bool CanGoPrev() => LogPage > 1;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextPageAsync()
    {
        LogPage++;
        await QueryLogsCoreAsync().ConfigureAwait(true);
    }

    private bool CanGoNext() => LogPage < LogTotalPages;

    // ---- 设置 ----
    [RelayCommand]
    private void SaveSettings()
    {
        try
        {
            var s = settings.Settings;
            var themeValue = ThemeChoice == "暗色" ? "dark" : "light";
            var themeChanged = s.Theme != themeValue;
            var providerValue = DbProviderChoice == "MySQL" ? "mysql" : "sqlite";
            // 容忍 settings.json 手工写入 null：字符串安全比较代替实例方法调用
            var dbChanged = !string.Equals(s.DatabaseProvider, providerValue, StringComparison.OrdinalIgnoreCase)
                            || s.DatabaseConnectionString != DbConnectionString;
            s.Theme = themeValue;
            s.RequireLogin = RequireLogin;
            s.MaxParallelDevices = Math.Clamp(MaxParallelDevices, 1, 16);
            s.OperationTimeoutMs = Math.Clamp(OperationTimeoutMs, 500, 60_000);
            s.ForceCompareOnDownload = ForceCompareOnDownload;
            s.VerifyAfterWrite = VerifyAfterWrite;
            s.PollIntervalMs = Math.Clamp(PollIntervalMs, 100, 60_000);
            s.LogKeepDays = Math.Clamp(LogKeepDays, 7, 3650);
            s.DatabaseProvider = providerValue;
            s.DatabaseConnectionString = DbConnectionString;
            s.BackupKeepCount = Math.Clamp(BackupKeepCount, 1, 100);
            s.ApiKey = ApiKey.Trim();
            s.ApiKeyRoles = ApiKeyRoles.Trim();
            s.ShowDefaultCredentialHint = ShowDefaultCredentialHint;
            settings.Save();
            if (themeChanged)
                theme.Apply(themeValue); // 主题立即生效，无需重启
            dialogs.Info(dbChanged
                ? "设置已保存。注意：数据库引擎/连接串的变更将在重启程序后生效。"
                : "设置已保存。");
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "保存设置失败");
            dialogs.Error("保存设置失败：" + ex.Message);
        }
    }

    [RelayCommand]
    private async Task CleanupLogsAsync()
    {
        if (!dialogs.Confirm($"确定清理 {LogKeepDays} 天前的日志？", "清理确认")) return;
        try
        {
            var n = await opLogs.CleanupAsync(LogKeepDays).ConfigureAwait(true);
            dialogs.Info($"已清理 {n} 条历史日志。");
            await QueryLogsCoreAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { dialogs.Error(ex.Message); }
    }
}
