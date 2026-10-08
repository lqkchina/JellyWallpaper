using System;
using System.Runtime.InteropServices;

namespace JellyWallpaper.Core;

/// <summary>
/// 全局低级鼠标钩子（WH_MOUSE_LL）。
///
/// 关键设计：这是一个"只观察、不拦截"的钩子 —— 回调里始终调用 CallNextHookEx 放行事件，
/// 绝不修改或吞掉任何鼠标消息。因此桌面图标的选中、拖拽、右键菜单等原生交互完全不受影响。
/// 本应用只是被动监听：当鼠标在桌面区域按下/松开左键时，通知渲染层播放果冻形变。
/// </summary>
internal sealed class MouseHook : IDisposable
{
    private readonly NativeMethods.HookProc _proc; // 持引用防 GC
    private IntPtr _hookId;

    /// <summary>左键在"桌面区域"按下（参数为屏幕坐标）。</summary>
    public event Action<System.Windows.Point>? LeftButtonDownOnDesktop;

    /// <summary>左键松开。</summary>
    public event Action? LeftButtonUp;

    public MouseHook()
    {
        _proc = HookCallback;
    }

    /// <summary>安装钩子。必须在带消息泵的 UI 线程调用。</summary>
    public bool Install()
    {
        if (_hookId != IntPtr.Zero) return true;
        // WH_MOUSE_LL 为低级全局钩子，回调在本进程安装线程上下文中执行，
        // hMod 传零即可，无需访问进程模块（避免权限异常）。
        _hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, IntPtr.Zero, 0);
        return _hookId != IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var hook = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            switch ((int)wParam)
            {
                case NativeMethods.WM_LBUTTONDOWN:
                {
                    var pt = new System.Windows.Point(hook.pt.X, hook.pt.Y);
                    // 仅当点击落在桌面区域时才触发形变
                    if (IsOverDesktop(hook.pt))
                    {
                        LeftButtonDownOnDesktop?.Invoke(pt);
                    }
                    break;
                }
                case NativeMethods.WM_LBUTTONUP:
                    LeftButtonUp?.Invoke();
                    break;
            }
        }

        // 永远放行，绝不拦截
        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool IsOverDesktop(NativeMethods.POINT pt)
    {
        IntPtr hWnd = NativeMethods.WindowFromPoint(pt);
        return DesktopLayerLocator.IsDesktopWindow(hWnd);
    }
}
