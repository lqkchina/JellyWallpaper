using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace JellyWallpaper.Effects;

/// <summary>
/// 果冻形变 ShaderEffect。
/// 把编译好的 Jelly.ps（PS 3.0 字节码）内嵌进程序集，运行时通过 SetStreamSource 加载，
/// 保证单文件发布时无需额外文件。
///
/// 应用到壁纸图层（Grid）上后，WPF 会自动把该元素自身渲染内容送入采样器 s0，
/// 因此无需手动设置 Input 属性。鼠标位置、按压深度等通过常量寄存器 c0..c5 传入。
/// </summary>
public sealed class JellyEffect : ShaderEffect
{
    /// <summary>程序集内嵌资源名（与 csproj 中 EmbeddedResource 的 LogicalName 一致）。</summary>
    private const string ShaderResourceName = "JellyWallpaper.Shaders.Jelly.ps";

    private static readonly PixelShader StaticShader = LoadShader();

    // ---- 采样器：Input 绑定到 s0，由 WPF 自动填入元素渲染内容 ----
    public static readonly DependencyProperty InputProperty =
        ShaderEffect.RegisterPixelShaderSamplerProperty("Input", typeof(JellyEffect), 0);

    // ---- 常量寄存器 c0..c5 ----
    public static readonly DependencyProperty MouseXProperty =
        DependencyProperty.Register("MouseX", typeof(double), typeof(JellyEffect),
            new UIPropertyMetadata(0.5, PixelShaderConstantCallback(0)));

    public static readonly DependencyProperty MouseYProperty =
        DependencyProperty.Register("MouseY", typeof(double), typeof(JellyEffect),
            new UIPropertyMetadata(0.5, PixelShaderConstantCallback(1)));

    public static readonly DependencyProperty DepthProperty =
        DependencyProperty.Register("Depth", typeof(double), typeof(JellyEffect),
            new UIPropertyMetadata(0.0, PixelShaderConstantCallback(2)));

    public static readonly DependencyProperty StrengthProperty =
        DependencyProperty.Register("Strength", typeof(double), typeof(JellyEffect),
            new UIPropertyMetadata(1.0, PixelShaderConstantCallback(3)));

    public static readonly DependencyProperty FalloffProperty =
        DependencyProperty.Register("Falloff", typeof(double), typeof(JellyEffect),
            new UIPropertyMetadata(0.02, PixelShaderConstantCallback(4)));

    public static readonly DependencyProperty ShadeProperty =
        DependencyProperty.Register("Shade", typeof(double), typeof(JellyEffect),
            new UIPropertyMetadata(0.35, PixelShaderConstantCallback(5)));

    public JellyEffect()
    {
        PixelShader = StaticShader;
        // 初始化所有常量寄存器
        UpdateShaderValue(MouseXProperty);
        UpdateShaderValue(MouseYProperty);
        UpdateShaderValue(DepthProperty);
        UpdateShaderValue(StrengthProperty);
        UpdateShaderValue(FalloffProperty);
        UpdateShaderValue(ShadeProperty);
    }

    public Brush? Input
    {
        get => (Brush?)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    /// <summary>鼠标归一化 X (0..1)。</summary>
    public double MouseX
    {
        get => (double)GetValue(MouseXProperty);
        set => SetValue(MouseXProperty, value);
    }

    /// <summary>鼠标归一化 Y (0..1)。</summary>
    public double MouseY
    {
        get => (double)GetValue(MouseYProperty);
        set => SetValue(MouseYProperty, value);
    }

    /// <summary>按压深度（0 空闲，1 最大，回弹时可轻微过冲）。</summary>
    public double Depth
    {
        get => (double)GetValue(DepthProperty);
        set => SetValue(DepthProperty, value);
    }

    /// <summary>形变强度。</summary>
    public double Strength
    {
        get => (double)GetValue(StrengthProperty);
        set => SetValue(StrengthProperty, value);
    }

    /// <summary>高斯衰减 sigma^2。</summary>
    public double Falloff
    {
        get => (double)GetValue(FalloffProperty);
        set => SetValue(FalloffProperty, value);
    }

    /// <summary>按压阴影强度。</summary>
    public double Shade
    {
        get => (double)GetValue(ShadeProperty);
        set => SetValue(ShadeProperty, value);
    }

    private static PixelShader LoadShader()
    {
        var asm = typeof(JellyEffect).Assembly;
        using Stream? stream = asm.GetManifestResourceStream(ShaderResourceName);
        if (stream == null)
        {
            // 说明发布前没有编译着色器（通常是本机未安装 Windows SDK / fxc 不可用）。
            throw new InvalidOperationException(
                $"未找到内嵌像素着色器资源 {ShaderResourceName}。请确认已安装 Windows SDK 并成功编译 Jelly.fx。");
        }

        var bytes = new byte[stream.Length];
        int read = 0;
        while (read < bytes.Length)
        {
            int n = stream.Read(bytes, read, bytes.Length - read);
            if (n <= 0) break;
            read += n;
        }

        var ps = new PixelShader();
        using (var ms = new MemoryStream(bytes))
        {
            ps.SetStreamSource(ms);
        }
        return ps;
    }
}
