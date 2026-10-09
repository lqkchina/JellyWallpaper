using System;
using System.Windows;
using System.Windows.Media;
using JellyWallpaper.Core;
using JellyWallpaper.UI;

namespace JellyWallpaper;

/// <summary>
/// 应用编排控制器：把渲染层、系统壁纸加载、鼠标钩子、托盘、设置面板串起来。
/// v1.1.0 起去掉了换壁纸功能（文件夹轮换 / 手动切换 / 全局快捷键），只保留果冻形变。
/// </summary>
internal sealed class AppController : IDisposable
{
    private readonly Logger _logger;
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;

    private WallpaperLayerWindow? _layer;
    private WallpaperManager? _wallpaper;
    private MouseHook? _mouseHook;
    private TrayController? _tray;
    private SettingsWindow? _settingsWindow;

    private bool _disposed;

    public AppController()
    {
        _logger = new Logger();
        _store = new SettingsStore(AppContext.BaseDirectory, _logger);
        _settings = _store.Load();
        _logger.Info($"程序启动 v{GetVersion()}，日志: {_logger.LogPath}");
    }

    /// <summary>初始化并进入托盘后台运行。</summary>
    public void Start()
    {
        // 1. 定位桌面壁纸层（WorkerW，位于图标层之下）
        IntPtr workerW = DesktopLayerLocator.Locate();
        if (workerW == IntPtr.Zero)
        {
            _logger.Error("未找到桌面壁纸层 WorkerW，无法挂载渲染层。");
        }

        // 2. 渲染层
        _layer = new WallpaperLayerWindow(_logger, workerW, _settings.RenderResolutionCap);
        _layer.ApplyEffectParams(_settings.DeformationStrength, _settings.ReboundSpeed, _settings.PressDamping);
        _layer.SetDisplayOptions(_settings.DisplayMode, ParseColor(_settings.FillColor));
        _layer.Show();

        // 3. 加载系统当前壁纸作为果冻作用纹理（仅一次，不再轮换）
        _wallpaper = new WallpaperManager(_logger);
        _wallpaper.SetRenderCap(_settings.RenderResolutionCap);
        var bmp = _wallpaper.LoadSystemWallpaper();
        if (bmp != null)
            _layer.ShowWallpaper(bmp);
        else
            _logger.Warn("未读取到系统壁纸，渲染层仅显示背景填充色（果冻形变无可见纹理）。");

        // 4. 鼠标钩子（只观察，不拦截）
        _mouseHook = new MouseHook();
        _mouseHook.LeftButtonDownOnDesktop += pt => _layer?.OnPress(pt);
        _mouseHook.LeftButtonUp += () => _layer?.OnRelease();
        if (!_mouseHook.Install())
        {
            _logger.Warn("安装全局鼠标钩子失败，果冻形变将不可用。");
        }

        // 5. 托盘
        _tray = new TrayController(GetVersion());
        _tray.OpenSettings += OpenSettingsWindow;
        _tray.RestartApp += Restart;
        _tray.ExitApp += Exit;

        _logger.Info("启动完成，已最小化到系统托盘后台运行。");
    }

    /// <summary>记录未处理异常到日志（由全局异常捕获调用）。</summary>
    public void LogCrash(string context, Exception? ex)
    {
        if (ex != null) _logger.Error($"[异常] {context}", ex);
        else _logger.Error($"[异常] {context}（异常对象为空）");
        _tray?.ShowBalloon("JellyWallpaper 发生异常", "详情已写入日志文件。");
    }

    private void OpenSettingsWindow()
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(_logger);
            _settingsWindow.SettingsChanged += s => ApplySettings(s, save: true);
            // 展示历史日志
            foreach (var line in _logger.Snapshot()) _settingsWindow.AppendLog(line);
            _logger.RecentChanged += line => _settingsWindow.AppendLog(line);
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>把设置应用到各个组件（实时预览），并保存。</summary>
    private void ApplySettings(AppSettings s, bool save)
    {
        // 壁纸显示与果冻形变参数（实时预览）
        _layer?.ApplyEffectParams(s.DeformationStrength, s.ReboundSpeed, s.PressDamping);
        _layer?.SetDisplayOptions(s.DisplayMode, ParseColor(s.FillColor));

        // 渲染分辨率
        _wallpaper?.SetRenderCap(s.RenderResolutionCap);

        // 开机自启
        if (AutoStart.IsEnabled() != s.AutoStart)
        {
            AutoStart.SetEnabled(s.AutoStart);
        }

        if (save) _store.Save(s);
    }

    private static Color ParseColor(string hex) => ColorUtil.Parse(hex);

    private static string GetVersion()
    {
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return v == null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>重启程序。</summary>
    private void Restart()
    {
        string exe = Environment.ProcessPath
            ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
        _logger.Info("重启程序: " + exe);
        Dispose();
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Error("重启失败: " + ex.Message, ex);
        }
        Application.Current.Shutdown();
    }

    /// <summary>退出：移除渲染层后系统原生壁纸自动还原，不留残留。</summary>
    private void Exit()
    {
        _logger.Info("退出程序，恢复原始桌面壁纸。");
        Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _settingsWindow?.Close(); } catch { }
        try { _tray?.Dispose(); } catch { }
        try { _mouseHook?.Dispose(); } catch { }
        try { _wallpaper = null; } catch { }
        try { _layer?.Close(); } catch { }
        try { _store.Save(_settings); } catch { }
        try { _logger.Dispose(); } catch { }
    }
}
