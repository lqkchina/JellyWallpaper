using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using JellyWallpaper.Core;

namespace JellyWallpaper;

/// <summary>
/// 应用入口：单实例保护 + 全局异常捕获（记录到日志，并提示）。
/// 启动默认进入托盘后台运行，不显示任何主窗口。
/// </summary>
public partial class App : Application
{
    private Mutex? _singleInstance;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 单实例：已运行时提示并退出
        _singleInstance = new Mutex(true, "JellyWallpaper_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("JellyWallpaper 已经在运行，请从系统托盘操作。",
                "JellyWallpaper", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // 全局异常捕获：写日志 + 面板可见时展示
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _controller?.LogCrash("AppDomain 未处理异常", args.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _controller?.LogCrash("后台任务未观察异常", args.Exception);
            args.SetObserved();
        };

        try
        {
            _controller = new AppController();
            _controller.Start();
        }
        catch (Exception ex)
        {
            // 启动失败也要尽量记录
            try
            {
                using var log = new Logger();
                log.Error("启动失败: ", ex);
            }
            catch { }
            MessageBox.Show("JellyWallpaper 启动失败：\n" + ex.Message,
                "JellyWallpaper", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _controller?.LogCrash("UI 线程未处理异常", e.Exception);
        e.Handled = true; // 不崩溃，记录后继续运行
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _controller?.Dispose(); } catch { }
        try { _singleInstance?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
