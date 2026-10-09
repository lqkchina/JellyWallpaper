using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JellyWallpaper.Core;

/// <summary>
/// CPU 果冻形变渲染器（不依赖 GPU / ShaderEffect）。
///
/// 背景：WPF 的 ShaderEffect 依赖硬件 GPU 加速；在虚拟机、远程桌面或无 GPU 驱动的环境下，
/// 应用 ShaderEffect 的元素会被渲染成空白/黑色，导致"壁纸层全黑"。本类改为纯 CPU 逐像素
/// 重采样，任何环境都能工作，代价是动画期间占用少量 CPU。
///
/// 原理：把"壁纸基图"（已按显示模式铺好、尺寸=输出尺寸）作为源，对输出每个像素计算
/// 到按压点的归一化距离 → 高斯衰减 → 向按压点偏移采样 → 双线性插值，形成向内凹陷的果冻形变。
/// 空闲时不重算，仅动画期间运行，满足低资源占用。
/// </summary>
internal sealed class CpuJellyRenderer : IDisposable
{
    private byte[]? _src;   // 壁纸基图 BGRA32
    private int _srcW, _srcH;
    private byte[]? _dst;   // 输出缓冲 BGRA32
    private WriteableBitmap? _wb;

    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>输出位图（作为 Image.Source 显示）。</summary>
    public WriteableBitmap? Output => _wb;

    /// <summary>设置输出尺寸并创建输出位图。</summary>
    public void Resize(int w, int h)
    {
        w = Math.Max(1, w);
        h = Math.Max(1, h);
        if (w == Width && h == Height && _wb != null) return;
        Width = w; Height = h;
        _dst = new byte[w * h * 4];
        _wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
    }

    /// <summary>
    /// 设置壁纸基图（已按显示模式铺好、尺寸与输出一致）。拷贝一份作为形变源。
    /// </summary>
    public void SetBaseImage(BitmapSource baseImage)
    {
        int w = baseImage.PixelWidth, h = baseImage.PixelHeight;
        BitmapSource bgra = baseImage.Format == PixelFormats.Bgra32
            ? baseImage
            : new FormatConvertedBitmap(baseImage, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[w * h * 4];
        bgra.CopyPixels(pixels, w * 4, 0);
        _src = pixels;
        _srcW = w; _srcH = h;
    }

    /// <summary>空闲时直接把基图作为输出（memcpy，零形变）。</summary>
    public void CopyBaseToOutput()
    {
        if (_dst == null || _src == null || _wb == null) return;
        Buffer.BlockCopy(_src, 0, _dst, 0, Math.Min(_src.Length, _dst.Length));
        _wb.WritePixels(new Int32Rect(0, 0, Width, Height), _dst, Width * 4, 0);
    }

    /// <summary>
    /// 对输出逐像素做果冻形变重采样（高斯径向凹陷 + 双线性插值 + 按压阴影）。
    /// </summary>
    public unsafe void Render(double mouseX, double mouseY, double depth,
        double strength, double falloff, double shade)
    {
        if (_dst == null || _src == null || _wb == null || _srcW < 2 || _srcH < 2) return;

        double fw = _srcW - 1.0, fh = _srcH - 1.0;
        double mouseU = mouseX * fw, mouseV = mouseY * fh;
        double k = strength * depth;
        double inv = 1.0 / Math.Max(falloff, 0.0001);
        double shdMul = 1.0 - shade * Math.Abs(depth); // 阴影随深度线性变暗（乘高斯衰减）

        int sw = _srcW, sh = _srcH, ow = Width, oh = Height;
        double ox = fw / Math.Max(1, ow - 1);
        double oy = fh / Math.Max(1, oh - 1);

        fixed (byte* sp = _src)
        fixed (byte* dp = _dst)
        {
            for (int y = 0; y < oh; y++)
            {
                double v = y * oy;
                int row = y * ow;
                for (int x = 0; x < ow; x++)
                {
                    double u = x * ox;
                    double dx = u - mouseU;
                    double dy = v - mouseV;
                    double r2 = dx * dx + dy * dy;
                    double f = Math.Exp(-r2 * inv);

                    double su = u - dx * (k * f);
                    double sv = v - dy * (k * f);

                    // 双线性采样边界钳制
                    int x0 = (int)su, y0 = (int)sv;
                    if (x0 < 0) x0 = 0; else if (x0 > sw - 2) x0 = sw - 2;
                    if (y0 < 0) y0 = 0; else if (y0 > sh - 2) y0 = sh - 2;
                    double fx = su - x0, fy = sv - y0;
                    double fx1 = 1.0 - fx, fy1 = 1.0 - fy;

                    byte* p00 = sp + (y0 * sw + x0) * 4;
                    byte* p01 = p00 + 4;          // x+1
                    byte* p10 = p00 + sw * 4;     // y+1
                    byte* p11 = p10 + 4;          // y+1,x+1

                    double b = (p00[0] * fx1 + p01[0] * fx) * fy1 + (p10[0] * fx1 + p11[0] * fx) * fy;
                    double g = (p00[1] * fx1 + p01[1] * fx) * fy1 + (p10[1] * fx1 + p11[1] * fx) * fy;
                    double r = (p00[2] * fx1 + p01[2] * fx) * fy1 + (p10[2] * fx1 + p11[2] * fx) * fy;

                    double s = shdMul < 1.0 ? 1.0 - (1.0 - shdMul) * f : 1.0;
                    if (s < 0) s = 0;

                    byte* d = dp + (row + x) * 4;
                    d[0] = (byte)(b * s);
                    d[1] = (byte)(g * s);
                    d[2] = (byte)(r * s);
                    d[3] = 255;
                }
            }
        }

        _wb.WritePixels(new Int32Rect(0, 0, Width, Height), _dst, Width * 4, 0);
    }

    public void Dispose()
    {
        _src = null;
        _dst = null;
        _wb = null;
    }
}
