using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace JellyWallpaper.Core;

/// <summary>
/// 壁纸管理器：扫描文件夹图片、按顺序/随机轮换、解码限长（形变渲染分辨率）、
/// 切换时交给渲染层做丝滑淡入淡出。
/// </summary>
internal sealed class WallpaperManager : IDisposable
{
    // 支持格式：jpg/jpeg/png/bmp/gif/tiff，webp 依赖系统 WIC 编解码器（Win10 22H2+ 或安装 WebP 扩展）
    private static readonly string[] SupportedExt =
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp" };

    private readonly Logger _logger;
    private readonly Action<BitmapSource> _showWallpaper;
    private DispatcherTimer? _rotateTimer;
    private readonly Random _rng = new();
    private List<string> _files = new();
    private int _index = -1;

    public event Action<string>? WallpaperChanged;

    public WallpaperManager(Logger logger, Action<BitmapSource> showWallpaper)
    {
        _logger = logger;
        _showWallpaper = showWallpaper;
    }

    /// <summary>刷新文件夹文件列表，并立即显示一张（保持当前索引尽量不变）。</summary>
    public void Reload(string folder)
    {
        var files = ScanFolder(folder);
        _files = files;

        if (files.Count == 0)
        {
            _logger.Warn(folder.Length == 0
                ? "壁纸文件夹为空，未加载任何壁纸。"
                : $"文件夹中未找到支持的图片: {folder}");
            return;
        }

        _logger.Info($"已加载 {files.Count} 张壁纸: {folder}");
        Show(0);
    }

    private List<string> ScanFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return new List<string>();
        }
        try
        {
            return Directory.EnumerateFiles(folder)
                .Where(f => SupportedExt.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.Error("扫描壁纸文件夹失败: " + ex.Message, ex);
            return new List<string>();
        }
    }

    /// <summary>启动自动轮换定时器（间隔由设置决定）。</summary>
    public void StartRotation(int intervalSeconds)
    {
        StopRotation();
        if (intervalSeconds <= 0) return;
        _rotateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(intervalSeconds)
        };
        _rotateTimer.Tick += (_, _) => Next(useRandom: _settingsRandom);
        _rotateTimer.Start();
        _logger.Info($"自动轮换已启动，间隔 {intervalSeconds} 秒。");
    }

    private bool _settingsRandom;

    public void SetRotationOrder(RotationOrder order)
    {
        _settingsRandom = order == RotationOrder.Random;
    }

    public void StopRotation()
    {
        _rotateTimer?.Stop();
        _rotateTimer = null;
    }

    /// <summary>切换到下一张（顺序或随机）。</summary>
    public void Next(bool useRandom)
    {
        if (_files.Count == 0) return;
        int next = useRandom ? _rng.Next(_files.Count) : (_index + 1) % _files.Count;
        Show(next);
    }

    private void Show(int index)
    {
        if (_files.Count == 0) return;
        if (index < 0 || index >= _files.Count) index = 0;
        _index = index;

        string file = _files[index];
        try
        {
            BitmapSource? bmp = LoadDecoded(file, _renderCap);
            if (bmp == null)
            {
                _logger.Warn($"无法解码壁纸，已跳过: {file}");
                return;
            }
            _showWallpaper(bmp);
            WallpaperChanged?.Invoke(file);
        }
        catch (Exception ex)
        {
            _logger.Error($"加载壁纸失败: {file} -> " + ex.Message, ex);
        }
    }

    private int _renderCap;

    /// <summary>设置形变渲染分辨率上限（长边像素，0=原图）。</summary>
    public void SetRenderCap(int cap) => _renderCap = Math.Max(0, cap);

    private static BitmapSource? LoadDecoded(string file, int cap)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad; // 解码后即释放文件句柄
            bi.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            bi.UriSource = new Uri(file, UriKind.Absolute);
            if (cap > 0)
            {
                // 限制解码长边，降低显存/内存占用（形变渲染分辨率）
                using var img = System.Drawing.Image.FromFile(file);
                int w = img.Width, h = img.Height;
                int max = Math.Max(w, h);
                if (max > cap)
                {
                    double k = (double)cap / max;
                    bi.DecodePixelWidth = Math.Max(1, (int)(w * k));
                    bi.DecodePixelHeight = Math.Max(1, (int)(h * k));
                }
            }
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            // 解码失败（含 webp 缺少编解码器）
            return null;
        }
    }

    public int Count => _files.Count;

    public void Dispose()
    {
        StopRotation();
    }
}
