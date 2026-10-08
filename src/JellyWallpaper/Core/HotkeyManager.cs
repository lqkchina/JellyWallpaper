using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace JellyWallpaper.Core;

/// <summary>
/// 全局快捷键管理：使用 RegisterHotKey 注册系统级热键。
/// 需要绑定到某个窗口句柄接收 WM_HOTKEY（这里绑定壁纸层窗口）。
/// 热键可动态修改（改设置后重新注册）。
/// </summary>
internal sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0xBEEF;

    private readonly Logger _logger;
    private IntPtr _hwnd;
    private HwndSource? _source;
    private uint _modifiers;
    private uint _key;
    private bool _registered;

    /// <summary>热键触发事件。</summary>
    public event Action? HotkeyPressed;

    public HotkeyManager(Logger logger) => _logger = logger;

    /// <summary>绑定接收热键的窗口句柄（WM_HOTKEY 接收窗口）。注册由 Update 完成。</summary>
    public bool Bind(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
        return _hwnd != IntPtr.Zero;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private bool Register()
    {
        if (_hwnd == IntPtr.Zero) return false;
        Unregister();
        // MOD_NOREPEAT 防止按住不放时连续触发
        bool ok = NativeMethods.RegisterHotKey(_hwnd, HotkeyId, _modifiers | NativeMethods.MOD_NOREPEAT, _key);
        _registered = ok;
        if (!ok)
        {
            int err = Marshal.GetLastWin32Error();
            _logger.Error($"注册全局快捷键失败 (Err={err})。该组合可能已被其他程序占用，请换一个。");
        }
        else
        {
            _logger.Info($"全局快捷键已注册: {Describe()}");
        }
        return ok;
    }

    private void Unregister()
    {
        if (_registered && _hwnd != IntPtr.Zero)
        {
            NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
        }
        _registered = false;
    }

    /// <summary>更新快捷键（改设置后调用）。</summary>
    public void Update(uint modifiers, uint key)
    {
        if (_modifiers == modifiers && _key == key && _registered) return;
        _modifiers = modifiers;
        _key = key;
        Register();
    }

    /// <summary>人类可读描述，例如 Ctrl+Alt+W。</summary>
    public string Describe()
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((_modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((_modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((_modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((_modifiers & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
        string keyName;
        try { keyName = ((System.Windows.Forms.Keys)_key).ToString(); }
        catch { keyName = _key.ToString(); }
        parts.Add(keyName);
        return string.Join("+", parts);
    }

    public void Dispose()
    {
        Unregister();
        if (_source != null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }
}
