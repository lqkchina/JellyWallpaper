using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Shapes;
using JellyWallpaper.Core;
using JellyWallpaper.Effects;

namespace JellyWallpaper;

/// <summary>
/// 壁纸渲染层窗口。
///
/// 核心职责：
///   1. 作为桌面"壁纸 WorkerW"的子窗口，透明、置顶在图标层之下 —— 只渲染壁纸+果冻形变，
///      绝不遮挡/拦截桌面图标（图标选中、拖拽、新建、删除均不受影响）。
///   2. 应用 JellyEffect 像素着色器到壁纸图层，实现按压向内凹陷 + 回弹。
///   3. 负责壁纸按显示模式布局、以及丝滑淡入淡出的轮换过渡。
/// </summary>
internal sealed class WallpaperLayerWindow : Window
{
    private readonly Logger _logger;
    private readonly JellyEffect _effect = new();
    private readonly JellyAnimator _animator = new();

    private readonly Grid _grid;
    private UIElement? _current;
    private bool _renderingAttached;
    private Point _pressNormalized;

    private WallpaperDisplayMode _mode = WallpaperDisplayMode.Fill;

    public WallpaperLayerWindow(Logger logger, IntPtr parentWorkerW, int renderCap)
    {
        _logger = logger;
        _ = renderCap; // 渲染分辨率由 WallpaperManager 负责，此处预留扩展

        Title = "JellyWallpaper";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false; // 渲染层不参与命中测试，绝不拦截鼠标

        _grid = new Grid
        {
            Background = new SolidColorBrush(Colors.Black),
            IsHitTestVisible = false,
        };
        _grid.Effect = _effect;
        Content = _grid;

        Loaded += (_, _) => HostIntoDesktop(parentWorkerW);
    }

    /// <summary>窗口加载后，挂到桌面壁纸层 WorkerW 之下并铺满虚拟屏幕。</summary>
    private void HostIntoDesktop(IntPtr workerW)
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero) return;

            // 父窗口改为壁纸 WorkerW（图标层之下）
            NativeMethods.SetParent(hwnd, workerW);

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

            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, screenW, screenH,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE
                | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_FRAMECHANGED);

            _logger.Info($"壁纸渲染层已挂载到桌面 WorkerW, 虚拟屏幕 {screenW}x{screenH} @ ({x},{y})");
        }
        catch (Exception ex)
        {
            _logger.Error("挂载壁纸渲染层失败: " + ex.Message, ex);
        }
    }

    // ================= 果冻形变 =================

    /// <summary>鼠标左键在桌面按下：在按压点触发向内凹陷。</summary>
    public void OnPress(System.Windows.Point screenPoint)
    {
        // 屏幕坐标 -> 窗口本地坐标 -> 归一化 (0..1)，供着色器使用
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

        // 更新着色器常量
        _effect.MouseX = _pressNormalized.X;
        _effect.MouseY = _pressNormalized.Y;
        _effect.Depth = _animator.Depth;

        if (settled && _animator.Target == 0.0)
        {
            _effect.Depth = 0.0;
            DetachRenderLoop(); // 空闲即停止渲染循环，节省 CPU
        }
    }

    /// <summary>推送形变参数（改设置后实时生效）。</summary>
    public void ApplyEffectParams(double strength, double reboundSpeed, double damping)
    {
        _effect.Strength = strength;
        _effect.Falloff = 0.02;             // 高斯衰减（集中度），保持观感稳定
        _effect.Shade = 0.35;               // 按压阴影强度
        _animator.SetParams(reboundSpeed, damping);
    }

    // ================= 壁纸显示 =================

    /// <summary>设置壁纸，执行丝滑淡入淡出过渡。</summary>
    public void ShowWallpaper(BitmapSource bmp)
    {
        // 确保在 UI 线程
        if (Dispatcher.CheckAccess())
        {
            FadeInWallpaper(bmp);
        }
        else
        {
            Dispatcher.BeginInvoke(new Action(() => FadeInWallpaper(bmp)));
        }
    }

    private void FadeInWallpaper(BitmapSource bmp)
    {
        if (bmp == null) return;
        try
        {
            UIElement incoming = BuildWallpaperElement(bmp, _mode);

            if (_current == null)
            {
                // 第一张，直接显示
                _grid.Children.Add(incoming);
                _current = incoming;
                return;
            }

            // 交叉淡化：新图叠在上层淡入，淡入完成后移除旧图
            UIElement outgoing = _current;
            incoming.Opacity = 0.0;
            _grid.Children.Add(incoming);

            var fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            fade.Completed += (_, _) =>
            {
                _grid.Children.Remove(outgoing);
            };
            incoming.BeginAnimation(OpacityProperty, fade);

            _current = incoming;
        }
        catch (Exception ex)
        {
            _logger.Error("切换壁纸失败: " + ex.Message, ex);
        }
    }

    /// <summary>设置壁纸显示模式与背景填充色。</summary>
    public void SetDisplayOptions(WallpaperDisplayMode mode, Color fillColor)
    {
        _mode = mode;
        _grid.Background = new SolidColorBrush(fillColor);

        // 若已有当前壁纸，重建显示元素以应用新模式
        if (_current != null)
        {
            // 取出当前 BitmapSource 重新构建（Image 控件保留 Source）
            if (_current is Image img && img.Source is BitmapSource bs)
            {
                _grid.Children.Remove(_current);
                UIElement rebuilt = BuildWallpaperElement(bs, mode);
                _grid.Children.Add(rebuilt);
                _current = rebuilt;
            }
            else if (_current is Rectangle rect && rect.Fill is ImageBrush ib && ib.ImageSource is BitmapSource bs2)
            {
                _grid.Children.Remove(_current);
                UIElement rebuilt = BuildWallpaperElement(bs2, mode);
                _grid.Children.Add(rebuilt);
                _current = rebuilt;
            }
        }
    }

    private UIElement BuildWallpaperElement(BitmapSource bmp, WallpaperDisplayMode mode)
    {
        if (mode == WallpaperDisplayMode.Tile)
        {
            // 平铺：ImageBrush 平铺，视口按设备像素绝对尺寸（处理 DPI 缩放）
            double scaleX = 1.0, scaleY = 1.0;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                scaleX = source.CompositionTarget.TransformToDevice.M11;
                scaleY = source.CompositionTarget.TransformToDevice.M22;
            }
            var brush = new ImageBrush(bmp)
            {
                TileMode = TileMode.Tile,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, bmp.Width / scaleX, bmp.Height / scaleY),
            };
            return new Rectangle { Fill = brush };
        }

        Stretch stretch = mode switch
        {
            WallpaperDisplayMode.Fill => Stretch.UniformToFill,
            WallpaperDisplayMode.Fit => Stretch.Uniform,
            WallpaperDisplayMode.Stretch => Stretch.Fill,
            WallpaperDisplayMode.Span => Stretch.UniformToFill,
            _ => Stretch.None, // Center
        };

        var img = new Image
        {
            Source = bmp,
            Stretch = stretch,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        return img;
    }
}
