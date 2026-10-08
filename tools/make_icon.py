#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成 JellyWallpaper 的应用图标 Resources/app.ico
纯标准库实现：绘制一个"果冻团子"图标（圆形渐变 + 高光），输出 32x32 与 16x16 两个尺寸。
ICO 内嵌 BMP(DIB) 格式，兼容 Windows。
"""
import struct, os, math

SIZE_32 = 32
SIZE_16 = 16


def lerp(a, b, t):
    return int(round(a + (b - a) * t))


def clamp(v, lo=0, hi=255):
    return int(round(max(lo, min(hi, v))))


def make_bitmap(size):
    """返回 32bpp BGRA 像素矩阵 + 单色 alpha 掩码，自底向上。"""
    px = []  # 每行自左向右，行序自底向上，BGRA
    and_mask = []
    cx = cy = (size - 1) / 2.0
    r = size * 0.44

    for y in range(size):
        row = []
        and_row = 0
        for x in range(size):
            dx = x - cx
            dy = y - cy
            dist = math.hypot(dx, dy)
            # 果冻球体：径向渐变 + 左上高光 + 轻微下半部加深
            if dist <= r:
                t = dist / r
                # 基础色：青绿色果冻
                base = (120, 220, 200)  # RGB
                # 径向明暗：中心偏亮，边缘偏深
                shade = 0.72 + 0.38 * (1 - t)
                # 高光：左上 45 度方向，靠近球心处亮斑
                hl = math.exp(-((dx - size * 0.12) ** 2 + (dy - size * 0.12) ** 2) / (2 * (size * 0.16) ** 2))
                shade += hl * 0.55
                shade = clamp(shade, 0, 1)
                R = clamp(base[0] * shade)
                G = clamp(base[1] * shade)
                Bb = clamp(base[2] * shade)
                # 边缘柔化（抗锯齿）
                aa = clamp(1 - (dist - (r - 0.7)) / 1.4, 0, 1)
                row.append((Bb, G, R, int(aa * 255)))
            else:
                row.append((0, 0, 0, 0))
            # alpha 掩码（1 位）：不透明=0，透明=1
            if row[-1][3] < 128:
                and_row |= 1 << (7 - (x % 8))
        px.append(row)
        and_mask.append(and_row)
    return px, and_mask


def bmp_bytes(px, size):
    """构造 32bpp BGRA 的 BITMAPINFOHEADER + 像素 + AND 掩码。"""
    header = struct.pack('<IiiHHIIiiII', 40, size, size * 2, 1, 32, 0,
                         size * size * 4, 0, 0, 0, 0)
    # 像素数据（自底向上）
    pixels = b''
    for y in range(size - 1, -1, -1):
        row = b''.join(struct.pack('<BBBB', *px[y][x]) for x in range(size))
        # 每行按 4 字节对齐（32bpp 已对齐）
        pixels += row
    # AND 掩码（自底向上，每行按 4 字节对齐）
    and_rows = b''
    for y in range(size - 1, -1, -1):
        row = px[y]  # 我们已有 and 行数据，重新生成
    # 重新计算 and 掩码行字节
    and_list = []
    for y in range(size):
        bits = 0
        for x in range(size):
            if px[y][x][3] < 128:
                bits |= 1 << (7 - (x % 8))
        and_list.append(bits)
    for y in range(size - 1, -1, -1):
        b = and_list[y].to_bytes((size + 7) // 8, 'big')
        pad = (4 - (len(b) % 4)) % 4
        and_rows += b + b'\x00' * pad
    return header + pixels + and_rows


def build_ico(sizes=(32, 16)):
    entries = []
    datas = []
    offset = 6 + 16 * len(sizes)
    for size in sizes:
        px, _ = make_bitmap(size)
        bmp = bmp_bytes(px, size)
        datas.append(bmp)
        entries.append(struct.pack('<BBBBHHII', size, size, 0, 0, 1, 32,
                                   len(bmp), offset))
        offset += len(bmp)
    header = struct.pack('<HHH', 0, 1, len(sizes))
    return header + b''.join(entries) + b''.join(datas)


def main():
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                       '..', 'src', 'JellyWallpaper', 'Resources', 'app.ico')
    out = os.path.normpath(out)
    with open(out, 'wb') as f:
        f.write(build_ico())
    print('written:', out, os.path.getsize(out), 'bytes')


if __name__ == '__main__':
    main()
