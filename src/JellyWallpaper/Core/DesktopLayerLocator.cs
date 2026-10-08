using System;
using System.Runtime.InteropServices;
using System.Text;

namespace JellyWallpaper.Core;

/// <summary>
/// 桌面层级定位器。
/// 目标：找到"图标层之下、原生壁纸之上"的 WorkerW 窗口，作为本应用渲染层的父窗口。
///
/// 原理（经典 live wallpaper 技术）：
///   1. Progman 是桌面根窗口。
///   2. 向 Progman 发送 0x052C 消息，系统会额外生成一个 WorkerW（背景壁纸层）。
///   3. 在 WorkerW 中寻找包含 SHELLDLL_DefView（图标宿主）的那个 —— 它承载图标。
///   4. 该图标 WorkerW 的"下一个" WorkerW 即原生壁纸层。把我们的窗口挂到它下面，
///      就能盖住原生壁纸、却仍位于图标层之下 —— 图标完全不受影响。
/// </summary>
internal static class DesktopLayerLocator
{
    /// <summary>定位壁纸层 WorkerW 的句柄。</summary>
    public static IntPtr Locate()
    {
        IntPtr progman = NativeMethods.FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            // 让系统生成壁纸 WorkerW
            NativeMethods.SendMessageTimeout(progman, NativeMethods.WM_SPAWN_WORKERW,
                IntPtr.Zero, IntPtr.Zero, NativeMethods.SMTO_NORMAL, 1000, out _);
        }

        // 找到承载桌面图标(SHELLDLL_DefView)的 WorkerW
        IntPtr iconWorkerW = IntPtr.Zero;
        NativeMethods.EnumWindows((hWnd, lParam) =>
        {
            if (ClassNameEquals(hWnd, "WorkerW"))
            {
                IntPtr defView = NativeMethods.FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView != IntPtr.Zero)
                {
                    iconWorkerW = hWnd;
                    return false; // 停止枚举
                }
            }
            return true;
        }, IntPtr.Zero);

        // 图标 WorkerW 之后的第一个 WorkerW 即壁纸层
        IntPtr wallpaperWorker = IntPtr.Zero;
        if (iconWorkerW != IntPtr.Zero)
        {
            wallpaperWorker = NativeMethods.FindWindowEx(IntPtr.Zero, iconWorkerW, "WorkerW", null);
        }

        // 兜底：没找到就用 Progman 本身（极少数特殊环境）
        if (wallpaperWorker == IntPtr.Zero && progman != IntPtr.Zero)
        {
            wallpaperWorker = progman;
        }

        return wallpaperWorker;
    }

    /// <summary>判断某个窗口是否属于桌面（图标层 / 壁纸层 / 桌面根）。</summary>
    public static bool IsDesktopWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;
        return ClassNameEquals(hWnd, "Progman")
            || ClassNameEquals(hWnd, "WorkerW")
            || ClassNameEquals(hWnd, "SHELLDLL_DefView")
            || ClassNameEquals(hWnd, "SysListView32");
    }

    private static bool ClassNameEquals(IntPtr hWnd, string expected)
    {
        var sb = new StringBuilder(256);
        NativeMethods.GetClassName(hWnd, sb, sb.Capacity);
        return string.Equals(sb.ToString(), expected, StringComparison.OrdinalIgnoreCase);
    }
}
