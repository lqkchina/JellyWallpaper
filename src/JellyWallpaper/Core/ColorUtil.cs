using System;
using System.Globalization;

namespace JellyWallpaper.Core;

/// <summary>
/// 颜色解析工具：把 "#RRGGBB" 或 "#AARRGGBB" 字符串解析为 WPF Color。
/// 手动解析避免依赖 ColorConverter（其在静态/实例调用上的兼容性差异）。
/// </summary>
internal static class ColorUtil
{
    /// <summary>解析成功返回 true，否则 false（输出黑色）。</summary>
    public static bool TryParse(string text, out System.Windows.Media.Color color)
    {
        color = System.Windows.Media.Colors.Black;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string t = text.Trim();
        if (t.StartsWith('#')) t = t.Substring(1);
        if (t.Length != 6 && t.Length != 8) return false;
        if (!uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
            return false;

        byte a = t.Length == 8 ? (byte)((v >> 24) & 0xFF) : (byte)255;
        byte r = (byte)((v >> 16) & 0xFF);
        byte g = (byte)((v >> 8) & 0xFF);
        byte b = (byte)(v & 0xFF);
        color = System.Windows.Media.Color.FromArgb(a, r, g, b);
        return true;
    }

    /// <summary>解析失败时返回黑色。</summary>
    public static System.Windows.Media.Color Parse(string text)
    {
        TryParse(text, out var c);
        return c;
    }
}
