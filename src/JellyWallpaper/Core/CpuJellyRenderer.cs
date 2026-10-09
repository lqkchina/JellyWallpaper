using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JellyWallpaper.Core;

/// <summary>
/// CPU 果冻形变渲染器（不依赖 GPU / ShaderEffect）。
///
/// 关键设计（性能）：
///   - 高斯形变只影响按压点周围一个小范围（falloff 决定影响半径）。
///   - 因此**只对受影响的小矩形区域做逐像素重采样**，其余画面保持不变，
///     并用 WriteableBitmap.WritePixels 只刷新该局部矩形。
///   - 每帧只处理约几十万像素（毫秒级），空闲时不重算 —— 鼠标不会卡顿、CPU 占用低。
///
/// 原理：以"壁纸基图"（已按显示模式铺好、尺寸=输出尺寸）为源，对受影响像素计算
/// 到按压点的归一化距离 → 高斯衰减 → 向按压点偏移采样 → 双线性插值，形成向内凹陷的果冻。
/// </summary>
internal sealed class CpuJellyRenderer : IDisposable
{
    private byte[]? _src;   // 壁纸基图 BGRA32（尺寸=输出尺寸）
    private int _srcW, _srcH;
    private WriteableBitmap? _wb;
    private byte[] _rectBuf = Array.Empty<byte>(); // 复用缓冲，避免每帧分配引发 GC

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
        _wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
    }

    /// <summary>设置壁纸基图（已按显示模式铺好、尺寸与输出一致）。拷贝一份作为形变源。</summary>
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

    /// <summary>空闲时把基图直接铺满输出（memcpy，零形变）。</summary>
    public void CopyBaseToOutput()
    {
        if (_src == null || _wb == null || _srcW != Width || _srcH != Height) return;
        _wb.WritePixels(new Int32Rect(0, 0, Width, Height), _src, Width * 4, 0);
    }

    /// <summary>
    /// 对按压点周围的局部区域做果冻形变重采样（径向向内凹陷 + 双线性插值 + 按压阴影）。
    /// 只更新受影响的小矩形，性能高、不卡鼠标。
    ///
    /// 参数说明：
    ///   mouseX/mouseY：按压点归一化坐标 (0..1)
    ///   depth：动画深度 (0=复原, 1=完全按下)
    ///   strength：形变强度（位移幅度基数）
    ///   falloff：影响半径基数（像素）—— 实际影响半径 = falloff * 10
    ///   shade：按压阴影强度
    /// </summary>
    public unsafe void Render(double mouseX, double mouseY, double depth,
        double strength, double falloff, double shade)
    {
        if (_src == null || _wb == null || _srcW != Width || _srcH != Height || Width < 2 || Height < 2) return;

        double fw = _srcW - 1.0, fh = _srcH - 1.0;
        double mouseU = mouseX * fw, mouseV = mouseY * fh;

        // 影响半径（像素）：由"按压衰减系数"映射，保证肉眼可见的凹陷范围
        double sigma = Math.Max(falloff * 10.0, 20.0);
        sigma = Math.Min(sigma, 400.0);
        double inv2 = 1.0 / (2.0 * sigma * sigma);

        // 位移幅度（像素）：随形变强度与按压力度线性变化
        double amp = strength * depth * 40.0;

        // 重采样范围：高斯衰减到可忽略处 + 位移幅度，限制为小矩形；上限 500px 防过大
        double rPx = sigma * 3.0 + Math.Abs(amp) + 4.0;
        rPx = Math.Min(rPx, 500.0);

        // 受影响的目标像素矩形
        int ox0 = Math.Max(0, (int)Math.Floor(mouseU - rPx));
        int ox1 = Math.Min(Width - 1, (int)Math.Ceiling(mouseU + rPx));
        int oy0 = Math.Max(0, (int)Math.Floor(mouseV - rPx));
        int oy1 = Math.Min(Height - 1, (int)Math.Ceiling(mouseV + rPx));
        if (ox0 > ox1 || oy0 > oy1) return;

        int rectW = ox1 - ox0 + 1, rectH = oy1 - oy0 + 1;
        // 复用缓冲，避免动画期间每帧分配大数组引发 GC
        int need = rectW * rectH * 4;
        if (_rectBuf.Length < need) _rectBuf = new byte[need];
        byte[] rectBuf = _rectBuf;

        int sw = _srcW, sh = _srcH;
        double ox = fw / Math.Max(1, Width - 1);
        double oy = fh / Math.Max(1, Height - 1);

        fixed (byte* sp = _src)
        fixed (byte* rb = rectBuf)
        {
            for (int yy = 0; yy < rectH; yy++)
            {
                int gy = oy0 + yy;
                double v = gy * oy;
                for (int xx = 0; xx < rectW; xx++)
                {
                    int gx = ox0 + xx;
                    double u = gx * ox;
                    double dx = u - mouseU, dy = v - mouseV;
                    double r2 = dx * dx + dy * dy;
                    double f = Math.Exp(-r2 * inv2); // 高斯径向衰减

                    // 径向向外凹陷：像素沿 (dx,dy) 方向被推开，幅度 = amp * f（中心处位移为 0，
                    // 四周随高斯衰减，形成"按压向内凹陷 + 周边被挤开"的果冻效果）
                    double dis = amp * f;
                    double su, sv;
                    double r = Math.Sqrt(r2);
                    if (r > 1e-6)
                    {
                        su = u - (dx / r) * dis;
                        sv = v - (dy / r) * dis;
                    }
                    else
                    {
                        su = u; sv = v;
                    }

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
                    double r_ = (p00[2] * fx1 + p01[2] * fx) * fy1 + (p10[2] * fx1 + p11[2] * fx) * fy;

                    // 按压阴影：按下的区域整体变暗，增强"凹陷"立体感
                    double s = 1.0 - shade * Math.Abs(depth) * f;
                    if (s < 0) s = 0;

                    byte* d = rb + (yy * rectW + xx) * 4;
                    d[0] = (byte)(b * s);
                    d[1] = (byte)(g * s);
                    d[2] = (byte)(r_ * s);
                    d[3] = 255;
                }
            }
        }

        // 只刷新受影响的小矩形，其余画面保持不动
        _wb.WritePixels(new Int32Rect(ox0, oy0, rectW, rectH), rectBuf, rectW * 4, 0);
    }

    public void Dispose()
    {
        _src = null;
        _wb = null;
    }
}
