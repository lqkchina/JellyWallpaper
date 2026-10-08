using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using JellyWallpaper.Core;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace JellyWallpaper.UI;

/// <summary>
/// 可视化参数设置面板。
/// 所有参数改动立即通过 SettingsChanged 事件实时预览，并由 AppController 保存。
/// 关闭窗口只隐藏，应用继续在托盘后台运行。
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private readonly Logger _logger;
    private bool _capturingHotkey;
    private uint _capturedVk;
    private AppSettings _settings = new();

    /// <summary>任意参数被用户修改时触发（携带最新设置）。</summary>
    public event Action<AppSettings>? SettingsChanged;
    /// <summary>用户点击"立即随机切换壁纸"。</summary>
    public event Action? SwitchNowRequested;

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

        OrderBox.Items.Add("顺序");
        OrderBox.Items.Add("随机");

        ResBox.Items.Add("原图（不限制）");
        ResBox.Items.Add("高（长边 2560）");
        ResBox.Items.Add("中（长边 1920）");
        ResBox.Items.Add("低（长边 1280）");

        // 事件
        BrowseBtn.Click += (_, _) => BrowseFolder();
        PickColorBtn.Click += (_, _) => PickColor();
        SwitchNowBtn.Click += (_, _) => SwitchNowRequested?.Invoke();
        CloseBtn.Click += (_, _) => Hide();
        RestoreBtn.Click += (_, _) => RestoreDefaults();

        StrengthSlider.ValueChanged += (_, _) => { StrengthVal.Text = StrengthSlider.Value.ToString("0.00"); RaiseChanged(); };
        SpeedSlider.ValueChanged += (_, _) => { SpeedVal.Text = SpeedSlider.Value.ToString("0"); RaiseChanged(); };
        DampingSlider.ValueChanged += (_, _) => { DampingVal.Text = DampingSlider.Value.ToString("0"); RaiseChanged(); };

        FolderBox.TextChanged += (_, _) => RaiseChanged();
        FillColorBox.TextChanged += (_, _) => RaiseChanged();
        IntervalBox.TextChanged += (_, _) => RaiseChanged();
        ModeBox.SelectionChanged += (_, _) => RaiseChanged();
        OrderBox.SelectionChanged += (_, _) => RaiseChanged();
        ResBox.SelectionChanged += (_, _) => RaiseChanged();
        AutoStartBox.Checked += (_, _) => RaiseChanged();
        AutoStartBox.Unchecked += (_, _) => RaiseChanged();
        ModCtrl.Checked += (_, _) => RaiseChanged();
        ModCtrl.Unchecked += (_, _) => RaiseChanged();
        ModAlt.Checked += (_, _) => RaiseChanged();
        ModAlt.Unchecked += (_, _) => RaiseChanged();
        ModShift.Checked += (_, _) => RaiseChanged();
        ModShift.Unchecked += (_, _) => RaiseChanged();
        ModWin.Checked += (_, _) => RaiseChanged();
        ModWin.Unchecked += (_, _) => RaiseChanged();

        // 热键捕获
        HotkeyKeyBox.GotKeyboardFocus += (_, _) => BeginHotkeyCapture();
        HotkeyKeyBox.LostKeyboardFocus += (_, _) => _capturingHotkey = false;
        HotkeyKeyBox.KeyDown += OnHotkeyKeyDown;
        HotkeyKeyBox.PreviewMouseLeftButtonDown += (_, e) => { HotkeyKeyBox.Focus(); e.Handled = false; };
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

        FolderBox.Text = s.WallpaperFolder;
        FillColorBox.Text = string.IsNullOrWhiteSpace(s.FillColor) ? "#000000" : s.FillColor;
        IntervalBox.Text = s.RotationIntervalSeconds.ToString();
        ModeBox.SelectedIndex = (int)s.DisplayMode;
        OrderBox.SelectedIndex = (int)s.RotationOrder;
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

        ModCtrl.IsChecked = (s.HotkeyModifiers & NativeMethods.MOD_CONTROL) != 0;
        ModAlt.IsChecked = (s.HotkeyModifiers & NativeMethods.MOD_ALT) != 0;
        ModShift.IsChecked = (s.HotkeyModifiers & NativeMethods.MOD_SHIFT) != 0;
        ModWin.IsChecked = (s.HotkeyModifiers & NativeMethods.MOD_WIN) != 0;
        _capturedVk = s.HotkeyKey;
        HotkeyKeyBox.Text = KeyFromVirtual(s.HotkeyKey);
        HotkeyStatus.Text = "";

        AutoStartBox.IsChecked = s.AutoStart;
    }

    /// <summary>把面板内容写入 _settings 并触发变更事件。</summary>
    private void RaiseChanged()
    {
        _settings.WallpaperFolder = FolderBox.Text.Trim();
        _settings.FillColor = string.IsNullOrWhiteSpace(FillColorBox.Text) ? "#000000" : FillColorBox.Text.Trim();
        if (int.TryParse(IntervalBox.Text, out int iv)) _settings.RotationIntervalSeconds = Math.Max(0, iv);
        if (ModeBox.SelectedIndex >= 0) _settings.DisplayMode = (WallpaperDisplayMode)ModeBox.SelectedIndex;
        if (OrderBox.SelectedIndex >= 0) _settings.RotationOrder = (RotationOrder)OrderBox.SelectedIndex;
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

        uint mods = 0;
        if (ModCtrl.IsChecked == true) mods |= NativeMethods.MOD_CONTROL;
        if (ModAlt.IsChecked == true) mods |= NativeMethods.MOD_ALT;
        if (ModShift.IsChecked == true) mods |= NativeMethods.MOD_SHIFT;
        if (ModWin.IsChecked == true) mods |= NativeMethods.MOD_WIN;
        _settings.HotkeyModifiers = mods;
        if (_capturedVk != 0) _settings.HotkeyKey = _capturedVk;

        _settings.AutoStart = AutoStartBox.IsChecked == true;

        SettingsChanged?.Invoke(_settings);
    }

    private void BrowseFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "选择壁纸图片文件夹",
            Multiselect = false,
        };
        if (dlg.ShowDialog(this) == true)
        {
            FolderBox.Text = dlg.FolderName;
        }
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

    // ---------- 热键捕获 ----------
    private void BeginHotkeyCapture()
    {
        _capturingHotkey = true;
        HotkeyStatus.Text = "请按下新快捷键（Esc 取消）…";
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturingHotkey) return;

        if (e.Key == Key.Escape)
        {
            _capturingHotkey = false;
            HotkeyStatus.Text = "已取消";
            Keyboard.ClearFocus();
            e.Handled = true;
            return;
        }

        // 忽略单独按修饰键
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        _capturedVk = (uint)KeyInterop.VirtualKeyFromKey(e.Key);
        HotkeyKeyBox.Text = KeyFromVirtual(_capturedVk);
        _capturingHotkey = false;
        HotkeyStatus.Text = "已设置，点击“关闭”或直接使用时生效";
        Keyboard.ClearFocus();
        RaiseChanged();
        e.Handled = true;
    }

    private static string KeyFromVirtual(uint vk)
    {
        return ((WinForms.Keys)vk).ToString();
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

    /// <summary>显示热键注册结果。</summary>
    public void SetHotkeyRegisterResult(bool ok, string message)
    {
        HotkeyStatus.Text = message;
        HotkeyStatus.Foreground = new SolidColorBrush(ok ? Colors.Green : Colors.DarkOrange);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // 关闭即隐藏，应用继续在托盘运行
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
