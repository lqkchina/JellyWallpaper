using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace JellyWallpaper.Core;

/// <summary>
/// 壁纸管理器（v1.1.0 起不再扫描文件夹/轮换/切换）。
/// 职责：读取 Windows 系统当前壁纸，作为果冻形变的作用纹理。
/// 获取顺序：① 系统实际显示的 TranscodedWallpaper → ② 注册表 WallPaper 路径。
/// </summary>
internal sealed class WallpaperManager
{
    private readonly Logger _logger;
    private int _renderCap; // 形变渲染分辨率上限（0=原图）

    public WallpaperManager(Logger logger)
    {
        _logger = logger;
    }

    /// <summary>设置形变渲染分辨率上限（0=原图）。</summary>
    public void SetRenderCap(int cap) => _renderCap = cap;

    /// <summary>
    /// 加载系统当前壁纸。找不到有效壁纸时返回 null（渲染层仅显示背景填充色）。
    /// </summary>
    public BitmapSource? LoadSystemWallpaper()
    {
        string? path = FindWallpaperPath();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            _logger.Warn("未找到有效的系统壁纸文件（桌面可能为纯色，或系统壁纸缓存不可读）。");
            return null;
        }

        try
        {
            var bmp = LoadBitmap(path, _renderCap);
            if (bmp == null)
            {
                _logger.Warn($"系统壁纸无法解码，已跳过: {path}");
                return null;
            }
            _logger.Info($"已加载系统壁纸: {path} ({bmp.PixelWidth}x{bmp.PixelHeight})");
            return bmp;
        }
        catch (Exception ex)
        {
            _logger.Error("加载系统壁纸失败: " + ex.Message, ex);
            return null;
        }
    }

    /// <summary>定位系统壁纸文件路径。</summary>
    private static string? FindWallpaperPath()
    {
        // ① TranscodedWallpaper：Windows 实际显示的壁纸（含多显示器拼接/幻灯片），最可靠
        string themes = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Themes");
        string transcoded = Path.Combine(themes, "TranscodedWallpaper");
        if (File.Exists(transcoded))
            return transcoded;

        // ② 注册表单张壁纸路径
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            string? wp = key?.GetValue("WallPaper") as string;
            if (!string.IsNullOrEmpty(wp) && File.Exists(wp))
                return wp;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("读取壁纸注册表失败: " + ex.Message);
        }

        return null;
    }

    /// <summary>
    /// 返回系统壁纸的"签名"（路径+文件修改时间）。用于检测用户更换系统壁纸后自动重载。
    /// 壁纸不可读时返回 null。
    /// </summary>
    public string? GetSignature()
    {
        try
        {
            string? path = FindWallpaperPath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            var t = File.GetLastWriteTimeUtc(path);
            return $"{path}|{t.Ticks}";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>按形变渲染分辨率上限解码图片。cap&gt;0 时缩放到长边不超过 cap。</summary>
    private BitmapSource? LoadBitmap(string path, int cap)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bi.UriSource = new Uri(path);
        bi.EndInit();
        bi.Freeze();

        if (cap <= 0)
            return bi;
        if (Math.Max(bi.PixelWidth, bi.PixelHeight) <= cap)
            return bi;

        // 等比缩放到长边 = cap，降低形变着色器采样开销
        double scale = (double)cap / Math.Max(bi.PixelWidth, bi.PixelHeight);
        int w = (int)Math.Round(bi.PixelWidth * scale);
        int h = (int)Math.Round(bi.PixelHeight * scale);
        var resized = new TransformedBitmap(bi, new ScaleTransform(w / (double)bi.PixelWidth, h / (double)bi.PixelHeight));
        resized.Freeze();
        return resized;
    }
}
