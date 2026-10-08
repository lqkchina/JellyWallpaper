using System;
using Microsoft.Win32;

namespace JellyWallpaper.Core;

/// <summary>
/// 开机自启：写入/移除 HKCU\...\Run 注册表项（当前用户，无需管理员权限）。
/// 启动命令为 当前exe路径（带引号），使其开机即进入托盘后台运行。
/// </summary>
internal static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "JellyWallpaper";

    /// <summary>读取当前是否已设置开机自启。</summary>
    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            object? v = key?.GetValue(ValueName);
            return v is string s && s.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>设置或取消开机自启。</summary>
    public static bool SetEnabled(bool enable)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath)!;
            if (enable)
            {
                string exe = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
                key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("AutoStart failed: " + ex.Message);
            return false;
        }
    }
}
