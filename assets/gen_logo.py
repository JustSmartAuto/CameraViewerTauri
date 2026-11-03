# -*- coding: utf-8 -*-
"""
生成 CameraViewer BeOS 风格 .ico（2026-11-03 版：圆角 + 毛玻璃边）

BeOS 调色板（来自 ThemeManager.Beos + retro-design-system tokens）：
  --bg            #2A5556   深 teal 桌面
  --chrome        #CFCFCF   浅灰 chrome 窗口
  --chrome-darker #404040   深灰边框
  --yellow        #FFCC00
  --red-dot       #E04040
  --teal          #006666

ICO 规范：主画 512x512 → 多尺寸 LANCZOS 缩放 → 写 11 个尺寸。
"""
from PIL import Image, ImageDraw, ImageFilter
import math, os

# ---------- 路径 ----------
OUT_DIR = os.path.dirname(os.path.abspath(__file__))
OUT_ICO = os.path.join(OUT_DIR, "camera-viewer.ico")

# ---------- BeOS 色值 ----------
BG             = (0x2A, 0x55, 0x56)
BG_DEEP        = (0x1D, 0x3D, 0x3E)
CHROME         = (0xCF, 0xCF, 0xCF)
CHROME_DARKER  = (0x40, 0x40, 0x40)
INK            = (0x00, 0x00, 0x00)
RED_DOT        = (0xE0, 0x40, 0x40)
BLUE_DOT       = (0x3A, 0x7B, 0xD5)
GREEN_DOT      = (0x3C, 0xBB, 0x74)
TITLE_A        = (0x4A, 0x6F, 0xA5)
TITLE_B        = (0x85, 0x7C, 0xC9)
CONTENT        = (0xF0, 0xEA, 0xD2)
STATUS_BG      = (0x1F, 0x3A, 0x3A)

def lerp(a, b, t): return int(a + (b - a) * t)
def mix(c1, c2, t): return (lerp(c1[0], c2[0], t), lerp(c1[1], c2[1], t), lerp(c1[2], c2[2], t))

def radial_gradient(img, cx, cy, r1, r2, c1, c2):
    """在 img（RGB/RGBA 皆可）上叠加径向渐变光斑（原地修改 RGB 通道）。"""
    px = img.load()
    w, h = img.size
    rgb_mode = img.mode
    for y in range(max(0, cy - r2), min(h, cy + r2)):
        for x in range(max(0, cx - r2), min(w, cx + r2)):
            d = math.hypot(x - cx, y - cy)
            if d <= r1: t = 0.0
            elif d >= r2: t = 1.0
            else: t = (d - r1) / (r2 - r1)
            col = mix(c1, c2, t)
            p = px[x, y]
            if rgb_mode == "RGBA":
                px[x, y] = (col[0], col[1], col[2], p[3])
            else:
                px[x, y] = col
    return img

def linear_gradient_h(img, y1, y2, c1, c2):
    w, h = img.size
    dh = max(1, y2 - y1)
    draw = ImageDraw.Draw(img)
    for y in range(y1, y2 + 1):
        t = (y - y1) / dh
        col = mix(c1, c2, t)
        draw.line([(0, y), (w - 1, y)], fill=col)
    return img

# ---------- 主绘制 ----------
def draw_logo(size=512):
    s = size
    # 使用 RGBA 画布，背景完全透明（圆角外区域 alpha=0）
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    # 1. 桌面背景层（RGB 绘制，之后再贴回 RGBA 的 RGB 通道）
    desk = Image.new("RGB", (s, s), BG)
    # 左上 teal 光斑
    radial_gradient(desk, int(s * 0.22), int(s * 0.28),
                    int(s * 0.02), int(s * 0.55),
                    mix(BG, (0x3C, 0x70, 0x72), 0.0), BG)
    # 右下暗影
    deep_mask = Image.new("L", (s, s), 0)
    dm = ImageDraw.Draw(deep_mask)
    dm.ellipse([int(s*0.25), int(s*0.18), int(s*1.35), int(s*1.26)], fill=255)
    deep_mask = deep_mask.filter(ImageFilter.GaussianBlur(radius=int(s*0.25)))
    deep = Image.new("RGB", (s, s), BG_DEEP)
    desk2 = Image.composite(deep, desk, deep_mask)  # 用 alpha 合成
    desk2 = Image.eval(desk, lambda p: p)  # 占位
    # 直接把 deep_mask 当 alpha 合成：逐像素
    px1 = desk.load()
    px2 = deep.load()
    pxm = deep_mask.load()
    for y in range(s):
        for x in range(s):
            m = pxm[x, y] / 255.0
            p1, p2 = px1[x, y], px2[x, y]
            px1[x, y] = (int(p1[0]*(1-m)+p2[0]*m),
                         int(p1[1]*(1-m)+p2[1]*m),
                         int(p1[2]*(1-m)+p2[2]*m))
    # 把桌面贴回 RGBA 画布
    img_rgb = Image.new("RGB", (s, s), (0, 0, 0))
    img_rgb.paste(desk)
    img.paste(img_rgb, (0, 0))

    # 2. BeOS 窗口
    margin = int(s * 0.09)
    win_x1 = margin
    win_y1 = margin
    win_x2 = s - margin
    win_y2 = s - margin
    win_w = win_x2 - win_x1
    win_h = win_y2 - win_y1
    win_corner = int(s * 0.03)

    # 外阴影：单独画个圆角矩形黑色 + blur + 偏移
    shadow = Image.new("RGBA", (win_w, win_h), (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    sd.rounded_rectangle([0, 0, win_w - 1, win_h - 1], radius=win_corner, fill=(0, 0, 0, 135))
    shadow = shadow.filter(ImageFilter.GaussianBlur(radius=max(2, s // 64)))
    img.paste(shadow, (win_x1 + int(s * 0.012), win_y1 + int(s * 0.012)), shadow)

    # 窗口 chrome 主体（圆角）
    draw.rounded_rectangle([win_x1, win_y1, win_x2, win_y2], radius=win_corner,
                           fill=CHROME, outline=CHROME_DARKER, width=max(2, s // 128))

    # 3. 标题栏（蓝紫渐变，圆角顶部自动继承）
    title_h = int(win_h * 0.115)
    title_x1 = win_x1 + max(2, s // 128)
    title_x2 = win_x2 - max(2, s // 128)
    # 渐变带（避开圆角角落用 clip）
    title_band = Image.new("RGBA", (win_w, title_h + 1), (0, 0, 0, 0))
    tb_draw = ImageDraw.Draw(title_band)
    dh = max(1, title_h - 2)
    for y in range(1, title_h):
        t = (y - 1) / dh
        col = mix(TITLE_A, TITLE_B, t)
        tb_draw.line([(0, y), (win_w - 1, y)], fill=(col[0], col[1], col[2], 255))
    # 顶部高光
    tb_draw.rectangle([0, 1, win_w - 1, 1 + max(1, s // 256)], fill=(255, 255, 255, 80))
    # 贴到主图（避开圆角角落）
    img.paste(title_band, (win_x1, win_y1), title_band)
    # 标题栏底边框
    draw.line([(title_x1, win_y1 + title_h), (title_x2, win_y1 + title_h)],
              fill=CHROME_DARKER, width=max(1, s // 256))

    # 4. 左上三色圆点
    dot_r = max(3, s // 60)
    dot_gap = dot_r * 2 + max(2, s // 100)
    dot_y = win_y1 + title_h // 2
    dot_x_start = win_x1 + int(s * 0.042)
    colors = [RED_DOT, BLUE_DOT, GREEN_DOT]
    for i, col in enumerate(colors):
        cx = dot_x_start + i * dot_gap
        cy = dot_y
        draw.ellipse([cx - dot_r, cy - dot_r, cx + dot_r, cy + dot_r],
                     fill=col, outline=CHROME_DARKER, width=max(1, s // 300))
        # 左上角高光（小椭圆）
        hr = max(1, dot_r // 3)
        draw.ellipse([cx - dot_r + hr, cy - dot_r + hr,
                      cx - dot_r + hr + dot_r, cy - dot_r + hr + dot_r // 2],
                     fill=(255, 255, 255))

    # 5. 内容区（米色，圆角自动由 chrome 继承）
    content_y1 = win_y1 + title_h + 1
    content_y2 = win_y2 - int(win_h * 0.075) - 1
    content_x1 = win_x1 + max(2, s // 128)
    content_x2 = win_x2 - max(2, s // 128)
    draw.rectangle([content_x1, content_y1, content_x2, content_y2],
                   fill=CONTENT, outline=CHROME_DARKER, width=max(1, s // 256))

    # 6. 相机图标
    cx_area = (content_x1 + content_x2) // 2
    cy_area = (content_y1 + content_y2) // 2
    cam_w = int(win_w * 0.44)
    cam_h = int(win_h * 0.34)
    x1 = cx_area - cam_w // 2
    y1 = cy_area - cam_h // 2
    x2 = cx_area + cam_w // 2
    y2 = cy_area + cam_h // 2

    # 梯形机身
    trapezoid = [
        (x1 + cam_w * 0.05, y1 + cam_h * 0.20),
        (x2 - cam_w * 0.05, y1 + cam_h * 0.20),
        (x2, y2),
        (x1, y2),
    ]
    draw.polygon(trapezoid, fill=INK, outline=INK)
    # 顶部提手/取景器凸出
    finder = [
        (x1 + cam_w * 0.22, y1),
        (x2 - cam_w * 0.22, y1),
        (x2 - cam_w * 0.18, y1 + cam_h * 0.20),
        (x1 + cam_w * 0.18, y1 + cam_h * 0.20),
    ]
    draw.polygon(finder, fill=INK, outline=INK)
    # 镜头外圈
    lens_cx = cx_area
    lens_cy = cy_area + int(cam_h * 0.06)
    lens_r = int(min(cam_w, cam_h) * 0.30)
    draw.ellipse([lens_cx - lens_r, lens_cy - lens_r,
                  lens_cx + lens_r, lens_cy + lens_r],
                 fill=CONTENT, outline=INK, width=max(3, s // 96))
    # 镜头内圈
    lens_r2 = int(lens_r * 0.48)
    draw.ellipse([lens_cx - lens_r2, lens_cy - lens_r2,
                  lens_cx + lens_r2, lens_cy + lens_r2], fill=INK)
    # 镜头高光
    hl_w = int(lens_r2 * 0.6)
    hl_h = int(lens_r2 * 0.45)
    draw.ellipse([lens_cx - lens_r2 + max(1, s // 160), lens_cy - lens_r2 + max(1, s // 160),
                  lens_cx - lens_r2 + hl_w, lens_cy - lens_r2 + hl_h + 2], fill=(255, 255, 255))
    # 快门按钮
    sw = int(cam_w * 0.12)
    sh = int(cam_h * 0.07)
    sx = x2 - cam_w * 0.22
    sy = y1 + cam_h * 0.05
    draw.rounded_rectangle([sx, sy, sx + sw, sy + sh], radius=max(2, s // 128), fill=INK)
    # 左右凸块
    for side in (-1, 1):
        bx = lens_cx + side * int(lens_r * 1.08)
        bw = int(cam_w * 0.055)
        bh = int(cam_h * 0.06)
        by = lens_cy - bh // 2
        draw.rounded_rectangle([bx - bw // 2, by, bx + bw // 2, by + bh],
                               radius=max(1, s // 256), fill=INK)

    # 7. 底部状态栏
    status_y1 = content_y2 + 1
    status_y2 = win_y2 - max(2, s // 128)
    status_h = status_y2 - status_y1
    if status_h >= max(6, s // 24):
        draw.rectangle([win_x1 + max(2, s//128), status_y1, win_x2 - max(2, s//128), status_y2],
                       fill=STATUS_BG, outline=CHROME_DARKER, width=max(1, s // 256))
        # 顶部高光
        hi_h = max(1, s // 256)
        hi = Image.new("RGBA", (win_w - 2 * max(2, s//128), hi_h), (255, 255, 255, 14))
        img.paste(hi, (win_x1 + max(2, s//128), status_y1 + 1), hi)

    # ==========================================================
    # 8. 圆角裁剪 + 毛玻璃边缘（核心步骤）
    # ==========================================================
    # 圆角 mask（iOS 风格大圆角）
    corner_r = int(s * 0.14)
    border = max(2, s // 256)
    mask = Image.new("L", (s, s), 0)
    md = ImageDraw.Draw(mask)
    md.rounded_rectangle([border, border, s - border - 1, s - border - 1],
                          radius=corner_r, fill=255)
    # mask 轻微模糊做抗锯齿
    mask_smooth = mask.filter(ImageFilter.GaussianBlur(radius=max(1, s // 128)))

    # 毛玻璃层：在 mask 边缘（alpha 120~240 的过渡带）叠加半透明白 + blur
    # 先找 mask 的边缘带：mask 模糊 - mask 稍微模糊 = 边缘差
    mask_inner = mask.filter(ImageFilter.GaussianBlur(radius=max(2, s // 80)))
    edge_mask = Image.new("L", (s, s), 0)
    e1 = mask_smooth.load()
    e2 = mask_inner.load()
    edge_px = edge_mask.load()
    for y in range(s):
        for x in range(s):
            v1, v2 = e1[x, y], e2[x, y]
            diff = v1 - v2
            if diff > 0:
                # 边缘越靠外 diff 越大 → 毛玻璃强度越高
                edge_px[x, y] = min(255, diff * 3)
    edge_mask = edge_mask.filter(ImageFilter.GaussianBlur(radius=max(2, s // 64)))

    # 半透明白色毛玻璃（白色在边缘带叠加）
    frosted = Image.new("RGBA", (s, s), (255, 255, 255, 0))
    frosted.putalpha(Image.eval(edge_mask, lambda v: min(255, v * 140 // 255)))

    # 把 mask 作为整体 alpha 通道贴到主图（圆角外透明）
    img.putalpha(mask_smooth)

    # 合成毛玻璃层（在主体之上、mask 裁剪之内）
    final = Image.alpha_composite(img, frosted)

    return final


def build_ico():
    os.makedirs(OUT_DIR, exist_ok=True)
    master = draw_logo(512)
    master.save(os.path.join(OUT_DIR, "camera-viewer-512.png"))

    sizes = [16, 20, 24, 32, 40, 48, 64, 72, 96, 128, 256]
    imgs = []
    for sz in sizes:
        resized = master.resize((sz, sz), Image.LANCZOS)
        imgs.append(resized)
        resized.save(os.path.join(OUT_DIR, f"camera-viewer-{sz}.png"))
        print(f"  + {sz}x{sz}")

    # ICO 要求主尺寸 256 单独放一张
    master_256 = master.resize((256, 256), Image.LANCZOS)
    master_256.save(OUT_ICO, format="ICO", sizes=[(s, s) for s in sizes], append_images=imgs)
    print(f"\n=> {OUT_ICO}")
    print(f"   共 {len(sizes)} 个尺寸: {sizes}")
    print(f"   文件大小: {os.path.getsize(OUT_ICO):,} bytes")


if __name__ == "__main__":
    build_ico()
