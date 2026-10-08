namespace JellyWallpaper.Core;

/// <summary>壁纸显示模式（对应 Windows 自带壁纸设置）。</summary>
public enum WallpaperDisplayMode
{
    Fill = 0,    // 填充：等比缩放填满，超出裁剪
    Fit = 1,     // 适应：完整显示整张图，留黑边（背景填充色）
    Stretch = 2, // 拉伸：强行铺满，不保留比例
    Tile = 3,    // 平铺：重复平铺
    Span = 4,    // 跨区：单张横跨多显示器
    Center = 5,  // 居中：原图居中，其余填背景色
}

/// <summary>壁纸轮换顺序。</summary>
public enum RotationOrder
{
    Sequential = 0, // 顺序
    Random = 1,     // 随机
}

/// <summary>
/// 应用设置模型。通过 System.Text.Json 序列化保存到设置文件。
/// </summary>
public sealed class AppSettings
{
    // ---- 壁纸 ----
    public string WallpaperFolder { get; set; } = string.Empty;
    public int RotationIntervalSeconds { get; set; } = 300; // 0 = 关闭自动轮换
    public RotationOrder RotationOrder { get; set; } = RotationOrder.Sequential;
    public WallpaperDisplayMode DisplayMode { get; set; } = WallpaperDisplayMode.Fill;
    public string FillColor { get; set; } = "#000000";

    // ---- 果冻形变 ----
    public double DeformationStrength { get; set; } = 1.0; // 形变强度
    public double ReboundSpeed { get; set; } = 70.0;       // 回弹速度（弹簧刚度）
    public double PressDamping { get; set; } = 12.0;       // 按压衰减系数（阻尼）
    public int RenderResolutionCap { get; set; } = 0;      // 形变渲染分辨率上限（0=原图, 1280/1920/2560）

    // ---- 全局快捷键（手动随机换壁纸）----
    public uint HotkeyModifiers { get; set; } = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT;
    public uint HotkeyKey { get; set; } = 0x57; // 'W'

    // ---- 其它 ----
    public bool AutoStart { get; set; }
    public bool StartMinimizedToTray { get; set; } = true;
}
