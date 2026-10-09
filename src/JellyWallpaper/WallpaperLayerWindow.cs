using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using JellyWallpaper.Core;

namespace JellyWallpaper;

/// <summary>
/// 壁纸渲染层窗口。
///
/// 核心职责：
///   1. 作为桌面"壁纸 WorkerW"的子窗口，覆盖原生壁纸、位于图标层之下 —— 只渲染壁纸+果冻形变，
///      绝不遮挡/拦截桌面图标（图标选中、拖拽、新建、删除均不受影响）。
///   2. 用 CPU 果冻形变渲染器（CpuJellyRenderer）实现"按压向内凹陷 + 回弹"。
///      不依赖 GPU / ShaderEffect，兼容虚拟机、远程桌面等无硬件加速环境（避免壁纸层全黑）。
///   3. 负责壁纸按显示模式布局（填充/适应/拉伸/平铺/跨区/居中）。
/// </summary>
internal sealed class WallpaperLayerWindow : Window
{
    private readonly Logger _logger;
    private readonly CpuJellyRenderer _renderer = new();
    private readonly JellyAnimator _animator = new();

    private readonly Grid _grid;
    private readonly Image _view;
    private bool _renderingAttached;
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

        Loaded += (_, _) => HostIntoDesktop(parentWorkerW);
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
            style = (style & ~NativeMethods.WS_POPUP) | NativeMethods.WS_CHILD;
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

            bool ok = NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, screenW, screenH,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE
                | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_FRAMECHANGED);

            _logger.Info($"壁纸渲染层已挂载, SetWindowPos={ok}, 虚拟屏幕 {screenW}x{screenH} @ ({x},{y}), 父窗口={workerW.ToInt64():X}");
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
        if (_renderingAttached) return;
        _renderingAttached = true;
        CompositionTarget.Rendering += OnRenderFrame;
    }

    private void DetachRenderLoop()
    {
        if (!_renderingAttached) return;
        _renderingAttached = false;
        CompositionTarget.Rendering -= OnRenderFrame;
    }

    private void OnRenderFrame(object? sender, EventArgs e)
    {
        double now = (e as RenderingEventArgs)?.RenderingTime.TotalSeconds ?? 0.0;
        bool settled = _animator.Step(now);

        // CPU 逐像素果冻形变重采样
        _renderer.Render(_pressNormalized.X, _pressNormalized.Y,
            _animator.Depth, _strength, _falloff, _shade);
        if (_renderer.Output != null)
        {
            // 重新赋值 Source 强制刷新 WriteableBitmap 的最新像素
            _view.Source = _renderer.Output;
        }

        if (settled && _animator.Target == 0.0)
        {
            DetachRenderLoop(); // 空闲即停止渲染循环，节省 CPU
        }
    }

    /// <summary>推送形变参数（改设置后实时生效）。</summary>
    public void ApplyEffectParams(double strength, double reboundSpeed, double damping)
    {
        _strength = strength;
        _falloff = 0.02;   // 高斯衰减（集中度），保持观感稳定
        _shade = 0.35;     // 按压阴影强度
        _animator.SetParams(reboundSpeed, damping);
    }

    // ================= 壁纸显示 =================

    /// <summary>设置壁纸（系统壁纸），并按当前显示模式重建画面。</summary>
    public void ShowWallpaper(BitmapSource bmp)
    {
        _wallpaperSource = bmp;
        RebuildBaseImage();
    }

    /// <summary>设置壁纸显示模式与背景填充色。</summary>
    public void SetDisplayOptions(WallpaperDisplayMode mode, Color fillColor)
    {
        _mode = mode;
        _fillColor = fillColor;
        _grid.Background = new SolidColorBrush(fillColor);
        Background = new SolidColorBrush(fillColor); // 同步窗口背景（不透明渲染层）

        // 若已有壁纸，按新模式重建画面
        if (_wallpaperSource != null)
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
            var dpi = VisualTreeHelper.GetDpi(this);
            int pxW = (int)Math.Round(SystemParameters.VirtualScreenWidth * dpi.DpiScaleX);
            int pxH = (int)Math.Round(SystemParameters.VirtualScreenHeight * dpi.DpiScaleY);
            pxW = Math.Max(1, pxW);
            pxH = Math.Max(1, pxH);

            BitmapSource baseImg = BuildBaseImage(_wallpaperSource, _mode, pxW, pxH, _fillColor);

            _renderer.Resize(pxW, pxH);
            _renderer.SetBaseImage(baseImg);
            _renderer.CopyBaseToOutput();
            _view.Source = _renderer.Output;

            _logger.Info($"壁纸画面已生成: {pxW}x{pxH}, 模式={(int)_mode}");
        }
        catch (Exception ex)
        {
            _logger.Error("生成壁纸画面失败: " + ex.Message, ex);
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
