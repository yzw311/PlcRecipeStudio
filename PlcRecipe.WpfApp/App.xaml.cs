using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Infrastructure;
using PlcRecipe.WpfApp.Messages;
using PlcRecipe.WpfApp.Services;
using PlcRecipe.WpfApp.ViewModels;

namespace PlcRecipe.WpfApp;

/// <summary>应用组合根：Generic Host + DI + 登录/主窗口切换。</summary>
public partial class App : Application
{
    public App()
    {
        // 兼容部分显卡/桌面美化软件下的 WPF 硬件渲染白屏问题
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
    }

    private IHost? _host;
    private LoginWindow? _loginWindow;
    private MainWindow? _mainWindow;
    // 登录↔主窗口切换期间置 true：此间登录窗的 Close 属正常流程，不代表用户要退出
    private bool _switching;

    public static IServiceProvider Services =>
        ((App)Current)._host?.Services
        ?? throw new InvalidOperationException("应用宿主尚未初始化完成");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterGlobalExceptionHandlers();

        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddPlcRecipeInfrastructure(); // 默认 %LOCALAPPDATA%\PlcRecipeStudio

                    // ViewModels
                    services.AddSingleton<LoginViewModel>();
                    services.AddSingleton<MainViewModel>();
                    services.AddSingleton<WorkbenchViewModel>();
                    services.AddSingleton<SystemManageViewModel>();
                    services.AddSingleton<TransferRunner>(); // 批量传输执行器（WorkbenchViewModel 的执行协作类）

                    // View 层服务
                    services.AddSingleton<IDialogService, DialogService>();
                    services.AddSingleton<IThemeService, ThemeService>();
                })
                .Build();
            // 启动宿主：承载 IHostedService（数据库自动备份调度等）
            await _host.StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // 组合根/宿主启动失败（如 SQL Server 连接串未配置）必须显式退出，避免僵尸进程
            Serilog.Log.Fatal(ex, "宿主启动失败");
            MessageBox.Show("程序启动失败：" + ex.Message, "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        try
        {
            await _host.Services.InitializeDatabaseAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // async void 中重抛只会被全局处理器吞掉且不留任何窗口（僵尸进程），必须显式退出
            Serilog.Log.Fatal(ex, "数据库初始化失败");
            MessageBox.Show("数据库初始化失败，程序即将退出：" + ex.Message, "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        ApplyTheme();
        RegisterMessenger();

        // RequireLogin=false（默认）时自动以管理员进入工作台；true 时显示登录窗口。
        // 延迟到消息循环启动后执行（与登录路径的时序一致），否则主窗口可能不渲染
        if (_host.Services.GetRequiredService<ISettingsService>().Settings.RequireLogin)
        {
            ShowLogin();
        }
        else
        {
            _ = Dispatcher.BeginInvoke(
                new Action(() => _ = AutoLoginAsync()),
                System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Serilog.Log.Error(args.Exception, "UI 未处理异常");
            MessageBox.Show("未处理异常：" + args.Exception.Message, "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            // 致命异常继续运行只会进入未知状态，交回系统终止
            args.Handled = args.Exception
                is not (OutOfMemoryException or StackOverflowException or System.Windows.Markup.XamlParseException);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Serilog.Log.Error(e.Exception, "未观察的后台任务异常");
            e.SetObserved();
        };
        // AppDomain 兜底：CommunityToolkit.Mvvm 的命令异常会重抛到线程池（Dispatcher 接不住），
        // 没有这条，进程崩溃不留任何日志
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Serilog.Log.Fatal(e.ExceptionObject as Exception, "AppDomain 未处理异常（进程即将终止，IsTerminating={0}）", e.IsTerminating);
        };
    }

    private async Task AutoLoginAsync()
    {
        try
        {
            var users = Services.GetRequiredService<IUserService>();
            var currentUser = Services.GetRequiredService<ICurrentUserService>();
            var admin = (await users.GetUsersAsync().ConfigureAwait(true))
                .FirstOrDefault(u => u.Enabled && u.Role == UserRole.Admin);
            if (admin == null) { ShowLogin(); return; }
            currentUser.Set(admin);
            Serilog.Log.Information("启动流程: 自动登录完成，显示主窗口");
            ShowMain();
        }
        catch (Exception ex)
        {
            Serilog.Log.Fatal(ex, "自动登录失败");
            ShowLogin();
        }
    }

    private void ApplyTheme()
    {
        var theme = Services.GetRequiredService<IThemeService>();
        theme.Apply(Services.GetRequiredService<ISettingsService>().Settings.Theme);
    }

    private void RegisterMessenger()
    {
        WeakReferenceMessenger.Default.Register<UserLoggedInMessage>(this, (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                _switching = true;
                _loginWindow?.Close();
                _loginWindow = null;
                ShowMain();
                _switching = false;
            });
        });
        WeakReferenceMessenger.Default.Register<UserLoggedOutMessage>(this, (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                _switching = true;
                _mainWindow?.Hide();
                ShowLogin();
                _switching = false;
            });
        });
    }

    private void ShowLogin()
    {
        _loginWindow = new LoginWindow
        {
            DataContext = Services.GetRequiredService<LoginViewModel>()
        };
        // 非切换流程下登录窗被用户关闭（Alt+F4 / 关闭按钮）= 退出应用
        _loginWindow.Closed += (_, _) => { if (!_switching) Shutdown(); };
        _loginWindow.Show();
    }

    private void ShowMain()
    {
        if (_mainWindow == null)
        {
            _mainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainViewModel>()
            };
            ((MainViewModel)_mainWindow.DataContext).Initialize();
            // 用户点主窗口关闭按钮 = 真正退出（注销走 Hide，不触发 Closed）
            _mainWindow.Closed += (_, _) => Shutdown();
        }
        Serilog.Log.Information("启动流程: 主窗口已显示（页面数据由用户切换/导航触发加载）");
        _mainWindow.Show();
        _mainWindow.Activate();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host != null)
            {
                await _host.StopAsync().ConfigureAwait(true); // 停止宿主（含备份调度等 HostedService）
                await _host.Services.ShutdownPlcAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "PLC 关闭失败");
        }
        finally
        {
            _host?.Dispose();
            base.OnExit(e);
        }
    }
}
