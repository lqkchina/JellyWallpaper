using System;
using System.Windows;
using System.Windows.Media;
using JellyWallpaper.Core;
using WinForms = System.Windows.Forms;

namespace JellyWallpaper.UI;

/// <summary>
/// 可视化参数设置面板。
/// 所有参数改动立即通过 SettingsChanged 事件实时预览，并由 AppController 保存。
/// 关闭窗口只隐藏，应用继续在托盘后台运行。
/// v1.1.0 起去掉了壁纸文件夹/轮换/快捷键设置，只保留显示模式、填充色、果冻参数。
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private readonly Logger _logger;
    private AppSettings _settings = new();

    /// <summary>任意参数被用户修改时触发（携带最新设置）。</summary>
    public event Action<AppSettings>? SettingsChanged;

    internal SettingsWindow(Logger logger)
    {
        _logger = logger;
        InitializeComponent();

        VersionText.Text = "版本 " + GetVersion();
        Title = $"JellyWallpaper 设置 v{GetVersion()}";

        // 下拉项
        ModeBox.Items.Add("填充");
        ModeBox.Items.Add("适应");
        ModeBox.Items.Add("拉伸");
        ModeBox.Items.Add("平铺");
        ModeBox.Items.Add("跨区（多显示器扩展）");
        ModeBox.Items.Add("居中");

        ResBox.Items.Add("原图（不限制）");
        ResBox.Items.Add("高（长边 2560）");
        ResBox.Items.Add("中（长边 1920）");
        ResBox.Items.Add("低（长边 1280）");

        // 事件
        PickColorBtn.Click += (_, _) => PickColor();
        CloseBtn.Click += (_, _) => Hide();
        RestoreBtn.Click += (_, _) => RestoreDefaults();

        StrengthSlider.ValueChanged += (_, _) => { StrengthVal.Text = StrengthSlider.Value.ToString("0.00"); RaiseChanged(); };
        SpeedSlider.ValueChanged += (_, _) => { SpeedVal.Text = SpeedSlider.Value.ToString("0"); RaiseChanged(); };
        DampingSlider.ValueChanged += (_, _) => { DampingVal.Text = DampingSlider.Value.ToString("0"); RaiseChanged(); };

        FillColorBox.TextChanged += (_, _) => RaiseChanged();
        ModeBox.SelectionChanged += (_, _) => RaiseChanged();
        ResBox.SelectionChanged += (_, _) => RaiseChanged();
        AutoStartBox.Checked += (_, _) => RaiseChanged();
        AutoStartBox.Unchecked += (_, _) => RaiseChanged();
    }

    private static string GetVersion()
    {
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return v == null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>把设置载入面板。</summary>
    public void LoadFrom(AppSettings s)
    {
        _settings = s;

        FillColorBox.Text = string.IsNullOrWhiteSpace(s.FillColor) ? "#000000" : s.FillColor;
        ModeBox.SelectedIndex = (int)s.DisplayMode;
        ResBox.SelectedIndex = s.RenderResolutionCap switch
        {
            0 => 0,
            2560 => 1,
            1920 => 2,
            _ => 3,
        };

        StrengthSlider.Value = Math.Clamp(s.DeformationStrength, 0, 3);
        SpeedSlider.Value = Math.Clamp(s.ReboundSpeed, 20, 300);
        DampingSlider.Value = Math.Clamp(s.PressDamping, 2, 40);

        AutoStartBox.IsChecked = s.AutoStart;
    }

    /// <summary>把面板内容写入 _settings 并触发变更事件。</summary>
    private void RaiseChanged()
    {
        _settings.FillColor = string.IsNullOrWhiteSpace(FillColorBox.Text) ? "#000000" : FillColorBox.Text.Trim();
        if (ModeBox.SelectedIndex >= 0) _settings.DisplayMode = (WallpaperDisplayMode)ModeBox.SelectedIndex;
        _settings.RenderResolutionCap = ResBox.SelectedIndex switch
        {
            1 => 2560,
            2 => 1920,
            3 => 1280,
            _ => 0,
        };

        _settings.DeformationStrength = StrengthSlider.Value;
        _settings.ReboundSpeed = SpeedSlider.Value;
        _settings.PressDamping = DampingSlider.Value;

        _settings.AutoStart = AutoStartBox.IsChecked == true;

        SettingsChanged?.Invoke(_settings);
    }

    private void PickColor()
    {
        var dlg = new WinForms.ColorDialog { AnyColor = true, FullOpen = true };
        if (ColorUtil.TryParse(_settings.FillColor, out var cur))
        {
            dlg.Color = System.Drawing.Color.FromArgb(cur.R, cur.G, cur.B);
        }
        if (dlg.ShowDialog() == WinForms.DialogResult.OK)
        {
            var c = dlg.Color;
            FillColorBox.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }
    }

    private void RestoreDefaults()
    {
        LoadFrom(new AppSettings());
        RaiseChanged();
    }

    /// <summary>追加一行日志。</summary>
    public void AppendLog(string line)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action<string>(AppendLog), line);
            return;
        }
        LogBox.AppendText(line + Environment.NewLine);
        LogBox.ScrollToEnd();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // 关闭即隐藏，应用继续在托盘运行
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
