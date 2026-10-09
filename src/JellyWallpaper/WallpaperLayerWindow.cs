using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using JellyWallpaper.Core;

namespace JellyWallpaper;

/// <summary>
/// 壁纸渲染层窗口。
///
/// 核心职责：
///   1. 作为桌面"壁纸 WorkerW"的子窗口，覆盖原生壁纸、位于图标层之下 —— 只渲染壁纸+果冻形变，
///      绝不遮挡/拦截桌面图标（图标选中、拖拽、新建、删除均不受影响）。
///   2. 用 CPU 果冻形变渲染器（CpuJellyRenderer）实现"按压向内凹陷 + 回弹"，
///      不依赖 GPU / ShaderEffect，兼容虚拟机、远程桌面等无硬件加速环境。
///   3. 负责壁纸按显示模式布局（填充/适应/拉伸/平铺/跨区/居中）。
///
/// 时序关键点（曾导致"壁纸不显示"）：
///   Window.Show() 是异步的 —— 返回时窗口尚未加载、尚未挂载到桌面。因此本类**不在** ShowWallpaper
///   时立即生成画面，而是等 Loaded 事件（挂载完成、尺寸确定）后再统一 RebuildBaseImage，
///   确保壁纸一定在窗口就绪后生成。
/// </summary>
internal sealed class WallpaperLayerWindow : Window
{
    private readonly Logger _logger;
    private readonly CpuJellyRenderer _renderer = new();
    private readonly JellyAnimator _animator = new();

    private readonly Grid _grid;
    private readonly Image _view;
    private DispatcherTimer? _animTimer; // 动画帧定时器（驱动果冻形变）
    private bool _reportedNoFrame;   // 已提示"无画面"（避免刷屏）
    private Point _pressNormalized;

    private WallpaperDisplayMode _mode = WallpaperDisplayMode.Fill;
    private Color _fillColor = Colors.Black;
    private BitmapSource? _wallpaperSource; // 系统壁纸源（显示模式布局前）

    // 果冻形变参数
    private double _strength = 1.0;
    private double _falloff = 0.02;
    private double _shade = 0.35;

    public WallpaperLayerWindow(Logger logger, IntPtr parentWorkerW, int renderCap)
    {
        _logger = logger;
        _ = renderCap; // 渲染分辨率由 WallpaperManager 负责源图解码上限

        Title = "JellyWallpaper";
        WindowStyle = WindowStyle.None;
        // 关键：不使用 AllowsTransparency（透明窗口）。
        // WPF 的 AllowsTransparency 使用 WS_EX_LAYERED，而 layered 窗口被 SetParent
        // 变成桌面子窗口后，在 Win10 上常出现"内容不渲染/显示空白"，导致壁纸层看不到。
        // 渲染层要完全盖住原生壁纸，本来就不需要真透明——空白区由背景填充色填补。
        Background = new SolidColorBrush(Colors.Black);
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false; // 渲染层不参与命中测试，绝不拦截鼠标

        _grid = new Grid
        {
            Background = new SolidColorBrush(Colors.Black),
            IsHitTestVisible = false,
        };
        _view = new Image
        {
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
        };
        _grid.Children.Add(_view);
        Content = _grid;

        // 关键：挂载完成后再生成画面
        Loaded += (_, _) =>
        {
            HostIntoDesktop(parentWorkerW);
            RebuildBaseImage();
        };
    }

    /// <summary>窗口加载后，挂到桌面壁纸层 WorkerW 之下并铺满虚拟屏幕。</summary>
    private void HostIntoDesktop(IntPtr workerW)
    {
        try
        {
            if (workerW == IntPtr.Zero)
            {
                _logger.Error("挂载失败：桌面壁纸层 WorkerW 句柄为空（未定位到）。");
            }

            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero)
            {
                _logger.Error("挂载失败：窗口句柄为空。");
                return;
            }

            // 父窗口改为壁纸 WorkerW（图标层之下）
            IntPtr oldParent = NativeMethods.SetParent(hwnd, workerW);
            _logger.Info($"SetParent: hwnd={hwnd.ToInt64():X}, parent={workerW.ToInt64():X}, oldParent={oldParent.ToInt64():X}");

            // 转为真正的子窗口样式（移除 WS_POPUP、加上 WS_CHILD），否则渲染可能异常
            int style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE).ToInt32();
            style = (style & ~NativeMethods.WS_POPUP) | NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE;
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_STYLE, new IntPtr(style));

            // 禁止激活、禁止命中（更保险），避免干扰桌面
            int exstyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt32();
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE,
                new IntPtr(exstyle | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT));

            // 覆盖整个虚拟屏幕（多显示器）
            NativeMethods.GetWindowRect(workerW, out var parentRect);
            int screenLeft = (int)SystemParameters.VirtualScreenLeft;
            int screenTop = (int)SystemParameters.VirtualScreenTop;
            int screenW = (int)SystemParameters.VirtualScreenWidth;
            int screenH = (int)SystemParameters.VirtualScreenHeight;

            // 子窗口坐标相对于父窗口客户区原点（父窗口屏幕原点）
            int x = screenLeft - parentRect.Left;
            int y = screenTop - parentRect.Top;

            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, screenW, screenH,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE
                | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_FRAMECHANGED);

            // 挂载后校验：可见性 + 客户区实际尺寸（像素），用于诊断是否真正铺满/可见
            bool visible = NativeMethods.IsWindowVisible(hwnd);
            NativeMethods.GetClientRect(hwnd, out var client);
            _logger.Info($"挂载完成: 可见={visible}, 客户区={client.Right}x{client.Bottom}px, " +
                         $"目标={screenW}x{screenH} @ ({x},{y}), 父窗口={workerW.ToInt64():X}");
        }
        catch (Exception ex)
        {
            _logger.Error("挂载壁纸渲染层失败: " + ex.Message, ex);
        }
    }

    // ================= 果冻形变 =================

    /// <summary>鼠标左键在桌面按下：在按压点触发向内凹陷。</summary>
    public void OnPress(Point screenPoint)
    {
        // 屏幕坐标 -> 窗口本地坐标 -> 归一化 (0..1)，供形变渲染使用
        Point local = PointFromScreen(screenPoint);
        double nx = ActualWidth > 0 ? local.X / ActualWidth : 0.5;
        double ny = ActualHeight > 0 ? local.Y / ActualHeight : 0.5;
        nx = Math.Clamp(nx, 0.0, 1.0);
        ny = Math.Clamp(ny, 0.0, 1.0);
        _pressNormalized = new Point(nx, ny);

        if (_renderer.Output == null && !_reportedNoFrame)
        {
            _reportedNoFrame = true;
            _logger.Warn("点击了桌面，但壁纸画面尚未生成（无输出位图）。请检查壁纸加载日志。");
        }

        _animator.Press();
        AttachRenderLoop();
    }

    /// <summary>鼠标左键松开：平滑回弹复原。</summary>
    public void OnRelease()
    {
        _animator.Release();
        AttachRenderLoop();
    }

    private void AttachRenderLoop()
    {
        if (_animTimer != null) return;
        // 用 DispatcherTimer 驱动动画：只依赖 Dispatcher 消息循环，稳定触发。
        // 不能依赖 CompositionTarget.Rendering —— 程序最小化到托盘、画面静止时，
        // WPF 渲染线程会暂停，该事件不触发，导致果冻动画永远不跑。
        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _animTimer.Tick += OnAnimTick;
        _animTimer.Start();
    }

    private void DetachRenderLoop()
    {
        if (_animTimer == null) return;
        _animTimer.Stop();
        _animTimer.Tick -= OnAnimTick;
        _animTimer = null;
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        double now = Environment.TickCount64 / 1000.0;
        bool settled = _animator.Step(now);

        // CPU 逐像素果冻形变重采样；WriteableBitmap 更新后引用它的 Image 会自动重绘，
        // 这里再 InvalidateVisual 兜底，确保每一帧都强制刷新到屏幕上。
        _renderer.Render(_pressNormalized.X, _pressNormalized.Y,
            _animator.Depth, _strength, _falloff, _shade);
        if (_renderer.Output != null)
        {
            _view.InvalidateVisual();
        }

        if (settled)
        {
            // 画面已稳定（无论按住凹陷还是回弹到位）即停止动画，节省 CPU；
            // 松开/再次按下时 OnRelease/OnPress 会重新挂接。
            DetachRenderLoop();
        }
    }

    /// <summary>推送形变参数（改设置后实时生效）。</summary>
    public void ApplyEffectParams(double strength, double reboundSpeed, double damping)
    {
        _strength = strength;
        // "按压衰减系数"(damping) 同时作为形变影响半径基数（像素）：
        // 影响半径 = damping * 10，默认 12 → 120px，保证肉眼可见的凹陷范围。
        _falloff = damping;
        _shade = 0.35;     // 按压阴影强度
        _animator.SetParams(reboundSpeed, damping);
    }

    // ================= 壁纸显示 =================

    /// <summary>设置壁纸（系统壁纸）。仅保存源，实际在挂载完成后生成画面。</summary>
    public void ShowWallpaper(BitmapSource bmp)
    {
        _wallpaperSource = bmp;
        if (IsLoaded)
        {
            RebuildBaseImage();
        }
        // 若窗口尚未加载，则等 Loaded 事件里统一 RebuildBaseImage
    }

    /// <summary>设置壁纸显示模式与背景填充色。</summary>
    public void SetDisplayOptions(WallpaperDisplayMode mode, Color fillColor)
    {
        _mode = mode;
        _fillColor = fillColor;
        _grid.Background = new SolidColorBrush(fillColor);
        Background = new SolidColorBrush(fillColor); // 同步窗口背景（不透明渲染层）

        // 若已有壁纸且窗口已就绪，按新模式重建画面
        if (_wallpaperSource != null && IsLoaded)
        {
            RebuildBaseImage();
        }
    }

    /// <summary>重建基图并铺到渲染层（壁纸按显示模式布局，无果冻的静态画面）。</summary>
    private void RebuildBaseImage()
    {
        if (_wallpaperSource == null) return;
        try
        {
            // 优先用窗口客户区实际像素尺寸（已挂载铺满，最准确）；失败则回退虚拟屏×DPI
            int pxW, pxH;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero && NativeMethods.GetClientRect(hwnd, out var client)
                && client.Right > 0 && client.Bottom > 0)
            {
                pxW = client.Right;
                pxH = client.Bottom;
            }
            else
            {
                (double sx, double sy) = SafeDpiScale();
                pxW = (int)Math.Round(SystemParameters.VirtualScreenWidth * sx);
                pxH = (int)Math.Round(SystemParameters.VirtualScreenHeight * sy);
            }
            pxW = Math.Max(1, pxW);
            pxH = Math.Max(1, pxH);

            BitmapSource baseImg = BuildBaseImage(_wallpaperSource, _mode, pxW, pxH, _fillColor);

            _renderer.Resize(pxW, pxH);
            _renderer.SetBaseImage(baseImg);
            _renderer.CopyBaseToOutput();
            _view.Source = _renderer.Output;

            _logger.Info($"壁纸画面已生成: {pxW}x{pxH}px, 模式={(int)_mode}, 源图={_wallpaperSource.PixelWidth}x{_wallpaperSource.PixelHeight}");
        }
        catch (Exception ex)
        {
            _logger.Error("生成壁纸画面失败: " + ex.Message, ex);
        }
    }

    /// <summary>安全获取 DPI 缩放（窗口未完全就绪时可能抛异常，兜底 1.0）。</summary>
    private (double, double) SafeDpiScale()
    {
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            return (dpi.DpiScaleX, dpi.DpiScaleY);
        }
        catch
        {
            return (1.0, 1.0);
        }
    }

    /// <summary>按显示模式把壁纸绘制成一张最终画面（含背景填充色）。</summary>
    private static BitmapSource BuildBaseImage(BitmapSource wallpaper, WallpaperDisplayMode mode,
        int w, int h, Color fill)
    {
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var rect = new Rect(0, 0, w, h);

            // 先铺背景填充色（适应/居中模式的空白区）
            dc.DrawRectangle(new SolidColorBrush(fill), null, rect);

            var brush = new ImageBrush(wallpaper)
            {
                Stretch = Stretch.UniformToFill,
            };
            switch (mode)
            {
                case WallpaperDisplayMode.Fill:
                    brush.Stretch = Stretch.UniformToFill;
                    break;
                case WallpaperDisplayMode.Fit:
                    brush.Stretch = Stretch.Uniform;
                    break;
                case WallpaperDisplayMode.Stretch:
                    brush.Stretch = Stretch.Fill;
                    break;
                case WallpaperDisplayMode.Span:
                    brush.Stretch = Stretch.UniformToFill;
                    break;
                case WallpaperDisplayMode.Center:
                    brush.Stretch = Stretch.None;
                    brush.AlignmentX = AlignmentX.Center;
                    brush.AlignmentY = AlignmentY.Center;
                    break;
                case WallpaperDisplayMode.Tile:
                    brush.Stretch = Stretch.None;
                    brush.TileMode = TileMode.Tile;
                    brush.ViewportUnits = BrushMappingMode.Absolute;
                    brush.Viewport = new Rect(0, 0, wallpaper.Width, wallpaper.Height);
                    break;
            }
            dc.DrawRectangle(brush, null, rect);
        }
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}
