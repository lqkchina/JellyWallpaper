using System;
using System.IO;
using System.Text.Json;

namespace JellyWallpaper.Core;

/// <summary>
/// 设置持久化：读写 settings.json。
/// 优先存放到程序同目录（便携模式），目录不可写时退化为 %AppData%。
/// </summary>
internal sealed class SettingsStore
{
    private readonly string _path;
    private readonly Logger _logger;

    public SettingsStore(string exeDirectory, Logger logger)
    {
        _logger = logger;

        string primary = Path.Combine(exeDirectory, "settings.json");
        string fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JellyWallpaper", "settings.json");

        // 若同目录可写则用同目录，否则用 AppData
        _path = CanWrite(exeDirectory) ? primary : fallback;
    }

    private static bool CanWrite(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return false;
            string probe = Path.Combine(dir, ".write_probe.tmp");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>加载设置；文件不存在或解析失败时返回默认设置。</summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                string json = File.ReadAllText(_path);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s != null)
                {
                    _logger.Info($"已加载设置: {_path}");
                    return s;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("读取设置失败，使用默认设置: " + ex.Message, ex);
        }
        return new AppSettings();
    }

    /// <summary>保存设置。</summary>
    public void Save(AppSettings settings)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var opts = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, opts));
        }
        catch (Exception ex)
        {
            _logger.Error("保存设置失败: " + ex.Message, ex);
        }
    }

    public string SettingsPath => _path;
}
