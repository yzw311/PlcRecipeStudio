using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;
using PlcRecipe.WpfApp.Messages;
using PlcRecipe.WpfApp.Services;

namespace PlcRecipe.WpfApp.ViewModels;

/// <summary>页面激活接口：导航切换时刷新数据。</summary>
public interface IActivatablePage
{
    Task LoadAsync();
}

/// <summary>导航项。</summary>
public sealed class NavItem
{
    public string Label { get; }
    public string Icon { get; }
    public ObservableObject Page { get; }
    public UserRole MinRole { get; }

    public NavItem(string label, string icon, ObservableObject page, UserRole minRole)
    {
        Label = label;
        Icon = icon;
        Page = page;
        MinRole = minRole;
    }
}

/// <summary>主窗口 ViewModel：导航（按角色过滤）+ 主题 + 用户状态。</summary>
public partial class MainViewModel(
    ICurrentUserService currentUser,
    IThemeService theme,
    ISettingsService settings,
    IDialogService dialogs,
    WorkbenchViewModel workbench,
    SystemManageViewModel systemManage) : ObservableObject
{
    private readonly ICurrentUserService _currentUser = currentUser;
    private readonly IThemeService _theme = theme;
    private readonly ISettingsService _settings = settings;
    private readonly IDialogService _dialogs = dialogs;
    private readonly List<NavItem> _allNavItems = [];

    public ObservableCollection<NavItem> NavItems { get; } = new();

    [ObservableProperty]
    private ObservableObject? _currentPage;

    [ObservableProperty]
    private NavItem? _currentNavItem;

    [ObservableProperty]
    private string _currentUserName = "";

    [ObservableProperty]
    private string _currentUserRole = "";

    [ObservableProperty]
    private string _themeToggleText = "🌙 暗色";

    public void Initialize()
    {
        _allNavItems.Add(new NavItem("工作台", "🖥", workbench, UserRole.Operator));
        _allNavItems.Add(new NavItem("系统管理", "⚙", systemManage, UserRole.Admin));
        _currentUser.CurrentUserChanged += OnUserChanged;
        OnUserChanged();
        SyncThemeToggleText();
    }

    partial void OnCurrentNavItemChanged(NavItem? value)
    {
        CurrentPage = value?.Page;
        _ = ActivateSafelyAsync(value);
    }

    // 页面加载统一由导航变更触发（含异常处理）；不再提供独立的 ActivateCurrentAsync，避免叠加造成重复加载
    private async Task ActivateSafelyAsync(NavItem? item)
    {
        if (item?.Page is not IActivatablePage activatable) return;
        try
        {
            await activatable.LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "页面 {Page} 加载失败", item.Label);
            _dialogs.Error("页面数据加载失败：" + ex.Message);
        }
    }

    private void OnUserChanged()
    {
        var user = _currentUser.Current;
        CurrentUserName = user?.UserName ?? "";
        CurrentUserRole = user?.Role switch
        {
            UserRole.Admin => "管理员",
            UserRole.Engineer => "工程师",
            UserRole.Operator => "操作员",
            _ => ""
        };
        RebuildNavItems(user);
        // 用户切换即重建导航并重置选中项：禁止残留上一个用户的页面（如操作员直接落到系统管理）
        CurrentNavItem = user == null ? null : NavItems.FirstOrDefault(n => user.Role >= n.MinRole);
    }

    private void RebuildNavItems(User? user)
    {
        NavItems.Clear();
        if (user == null) return;
        foreach (var item in _allNavItems.Where(n => user.Role >= n.MinRole))
            NavItems.Add(item);
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        var next = _theme.Current == "dark" ? "light" : "dark";
        _theme.Apply(next);
        _settings.Settings.Theme = next;
        _settings.Save(); // 持久化，重启后保持
        SyncThemeToggleText();
    }

    private void SyncThemeToggleText() =>
        ThemeToggleText = _theme.Current == "dark" ? "☀ 亮色" : "🌙 暗色";

    [RelayCommand]
    private void Logout()
    {
        _currentUser.Clear();
        WeakReferenceMessenger.Default.Send(new UserLoggedOutMessage());
    }
}
