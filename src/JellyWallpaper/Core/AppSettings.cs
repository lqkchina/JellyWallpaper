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

/// <summary>
/// 应用设置模型。通过 System.Text.Json 序列化保存到设置文件。
/// v1.1.0 起去掉了换壁纸相关功能（文件夹/轮换/快捷键），只保留壁纸显示与果冻形变，
/// 壁纸来源为 Windows 系统当前壁纸。
/// </summary>
public sealed class AppSettings
{
    // ---- 壁纸显示 ----
    public WallpaperDisplayMode DisplayMode { get; set; } = WallpaperDisplayMode.Fill;
    public string FillColor { get; set; } = "#000000";

    // ---- 果冻形变 ----
    public double DeformationStrength { get; set; } = 1.0; // 形变强度
    public double ReboundSpeed { get; set; } = 70.0;       // 回弹速度（弹簧刚度）
    public double PressDamping { get; set; } = 12.0;       // 按压衰减系数（阻尼）
    public int RenderResolutionCap { get; set; } = 0;      // 形变渲染分辨率上限（0=原图, 1280/1920/2560）

    // ---- 其它 ----
    public bool AutoStart { get; set; }
    public bool StartMinimizedToTray { get; set; } = true;
}
