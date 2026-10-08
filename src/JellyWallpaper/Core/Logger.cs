using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace JellyWallpaper.Core;

/// <summary>
/// 日志器：写入程序同目录 logs\app_YYYYMMDD.log，并保留最近若干条供设置面板显示。
/// 采用轻量文件写 + 内存环形缓冲，避免频繁 IO 影响性能。
/// </summary>
internal sealed class Logger : IDisposable
{
    private readonly object _lock = new();
    private readonly Queue<string> _recent = new();
    private readonly string _logPath;
    private StreamWriter? _writer;
    private bool _disposed;

    public event Action<string>? RecentChanged;

    public Logger(string? exeDirectory = null)
    {
        string baseDir = exeDirectory ?? AppContext.BaseDirectory;
        string logDir = Path.Combine(baseDir, "logs");
        try
        {
            Directory.CreateDirectory(logDir);
            _logPath = Path.Combine(logDir, $"app_{DateTime.Now:yyyyMMdd}.log");
        }
        catch
        {
            // 目录不可写时退化为临时目录，保证不崩溃
            _logPath = Path.Combine(Path.GetTempPath(), $"jellywallpaper_{DateTime.Now:yyyyMMdd}.log");
        }
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);
    public void Error(string message, Exception ex) =>
        Write("ERROR", message + Environment.NewLine + ex);

    private void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
        lock (_lock)
        {
            _recent.Enqueue(line);
            while (_recent.Count > 500) _recent.Dequeue();
            try
            {
                if (_writer == null)
                {
                    _writer = new StreamWriter(_logPath, append: true, Encoding.UTF8);
                    _writer.AutoFlush = true;
                }
                _writer.WriteLine(line);
            }
            catch
            {
                // 写入失败忽略，避免级联异常
            }
        }
        try { RecentChanged?.Invoke(line); } catch { }
    }

    /// <summary>当前内存中的日志行（供设置面板展示）。</summary>
    public string[] Snapshot()
    {
        lock (_lock) return _recent.ToArray();
    }

    public string LogPath => _logPath;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lock)
        {
            try { _writer?.Dispose(); } catch { }
            _writer = null;
        }
    }
}
