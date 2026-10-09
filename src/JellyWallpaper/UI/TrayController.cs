using System;
using System.Drawing;
using System.Windows.Forms;

namespace JellyWallpaper.UI;

/// <summary>
/// 系统托盘控制器：应用默认最小化到托盘运行。
/// 菜单：打开设置面板 / 重启程序 / 退出程序。
/// 托盘图标运行时用 GDI 绘制，避免额外图片资源。
/// </summary>
internal sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private bool _disposed;

    /// <summary>点击"打开设置面板"。</summary>
    public event Action? OpenSettings;
    /// <summary>点击"重启程序"。</summary>
    public event Action? RestartApp;
    /// <summary>点击"退出程序"。</summary>
    public event Action? ExitApp;

    public TrayController(string version)
    {
        _notifyIcon = new NotifyIcon
        {
            Text = $"JellyWallpaper 果冻壁纸 v{version}",
            Icon = CreateTrayIcon(),
            Visible = true,
        };

        _menu = new ContextMenuStrip();
        _menu.Items.Add("打开设置面板", null, (_, _) => OpenSettings?.Invoke());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("重启程序", null, (_, _) => RestartApp?.Invoke());
        _menu.Items.Add("退出程序", null, (_, _) => ExitApp?.Invoke());

        _notifyIcon.ContextMenuStrip = _menu;
        // 双击托盘图标打开设置
        _notifyIcon.DoubleClick += (_, _) => OpenSettings?.Invoke();
    }

    public void ShowBalloon(string title, string text)
    {
        try
        {
            _notifyIcon.ShowBalloonTip(2000, title, text, ToolTipIcon.Info);
        }
        catch { }
    }

    /// <summary>运行时绘制一个青绿色果冻圆点作为托盘图标。</summary>
    private static Icon CreateTrayIcon()
    {
        var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Color.FromArgb(120, 220, 200));
            g.FillEllipse(brush, 3, 3, 26, 26);
            using var hl = new SolidBrush(Color.FromArgb(200, 220, 255, 240));
            g.FillEllipse(hl, 8, 7, 9, 9);
            using var pen = new Pen(Color.FromArgb(90, 90, 90), 1);
            g.DrawEllipse(pen, 3, 3, 26, 26);
        }
        IntPtr hicon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hicon).Clone();
        bmp.Dispose();
        return icon;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }
}
