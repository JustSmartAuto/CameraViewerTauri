# -*- coding: utf-8 -*-
"""
生成 CameraViewer BeOS 风格 .ico
BeOS 调色板：
  --bg           #2A5556   深 teal 桌面
  --bg-2         #1D3D3E   深 teal 暗影
  --chrome       #CFCFCF   浅灰 chrome 窗口
  --chrome-dark  #888888
  --chrome-darker #404040  深灰边框
  --yellow       #FFCC00
  --teal         #006666
  --ink          #000000
  --red-dot      #E04040
"""
from PIL import Image, ImageDraw, ImageFilter
import math, os

OUT_DIR = os.path.join(os.path.dirname(__file__), "assets")
OUT_ICO = os.path.join(OUT_DIR, "camera-viewer.ico")

# ---------- 色值 ----------
BG           = (0x2A, 0x55, 0x56)
BG_DEEP      = (0x1D, 0x3D, 0x3E)
CHROME       = (0xCF, 0xCF, 0xCF)
CHROME_LIGHT = (0xE8, 0xE8, 0xE8)
CHROME_DARK  = (0x88, 0x88, 0x88)
CHROME_DARKER= (0x40, 0x40, 0x40)
YELLOW       = (0xFF, 0xCC, 0x00)
TEAL         = (0x00, 0x66, 0x66)
INK          = (0x00, 0x00, 0x00)
RED_DOT      = (0xE0, 0x40, 0x40)
BLUE_DOT     = (0x3A, 0x7B, 0xD5)
GREEN_DOT    = (0x3C, 0xBB, 0x74)
TITLE_A      = (0x4A, 0x6F, 0xA5)   # 蓝紫渐变起点
TITLE_B      = (0x85, 0x7C, 0xC9)   # 蓝紫渐变终点
CONTENT      = (0xF0, 0xEA, 0xD2)   # 米色内容区
STATUS_BG    = (0x1F, 0x3A, 0x3A)   # 底部状态栏深色
STATUS_FG    = (0xE8, 0xE8, 0xE8)   # 状态栏文字

def lerp(a, b, t): return int(a + (b - a) * t)

def mix(c1, c2, t):
    return (lerp(c1[0], c2[0], t), lerp(c1[1], c2[1], t), lerp(c1[2], c2[2], t))

# ---------- 工具 ----------
def radial_gradient(img, cx, cy, r1, r2, c1, c2):
    """在 img 上叠加径向渐变光斑（原地修改）。"""
    w, h = img.size
    px = img.load()
    for y in range(max(0, cy - r2), min(h, cy + r2)):
        for x in range(max(0, cx - r2), min(w, cx + r2)):
            d = math.hypot(x - cx, y - cy)
            if d <= r1: t = 0.0
            elif d >= r2: t = 1.0
            else: t = (d - r1) / (r2 - r1)
            col = mix(c1, c2, t)
            px[x, y] = col
    return img

def linear_gradient_h(img, y1, y2, c1, c2):
    """水平线性渐变带，从上到下 c1 → c2。"""
    w, h = img.size
    draw = ImageDraw.Draw(img)
    dh = max(1, y2 - y1)
    for y in range(y1, y2 + 1):
        t = (y - y1) / dh
        col = mix(c1, c2, t)
        draw.line([(0, y), (w - 1, y)], fill=col)
    return img

def ellipse(draw, box, fill=None, outline=None, width=1):
    draw.ellipse(box, fill=fill, outline=outline, width=width)

# ---------- 绘制 ----------
def draw_logo(size=512):
    s = size
    img = Image.new("RGB", (s, s), BG)

    # 1. 桌面径向光斑
    # 左上亮色光斑
    radial_gradient(img, int(s * 0.22), int(s * 0.28),
                    int(s * 0.02), int(s * 0.55),
                    mix(BG, (0x3C, 0x70, 0x72), 0.0), BG)
    # 右下暗影光斑（加深）
    img2 = Image.new("RGB", (s, s), BG_DEEP)
    radial_gradient(img2, int(s * 0.78), int(s * 0.72),
                    int(s * 0.02), int(s * 0.60),
                    BG_DEEP, BG)
    # 用 alpha 方式合成第二光斑：逐像素取较暗者
    px1 = img.load()
    px2 = img2.load()
    for y in range(s):
        for x in range(s):
            p1, p2 = px1[x, y], px2[x, y]
            if sum(p2) < sum(p1):
                px1[x, y] = p2

    draw = ImageDraw.Draw(img)

    # 2. BeOS 窗口
    # 窗口区域：居中，留出边距做桌面背景
    margin = int(s * 0.08)
    win_x1 = margin
    win_y1 = margin
    win_x2 = s - margin
    win_y2 = s - margin
    win_w = win_x2 - win_x1
    win_h = win_y2 - win_y1

    # 外阴影（先画，用黑色偏 45° 偏移）
    shadow = Image.new("RGBA", (win_w, win_h), (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    sd.rectangle([0, 0, win_w - 1, win_h - 1], fill=(0, 0, 0, 120))
    shadow = shadow.filter(ImageFilter.GaussianBlur(radius=max(1, s // 64)))
    # 偏移 4px 向下右
    img.paste(shadow, (win_x1 + int(s * 0.012), win_y1 + int(s * 0.012)), shadow)

    # 窗口 chrome 主体
    draw.rectangle([win_x1, win_y1, win_x2, win_y2], fill=CHROME, outline=CHROME_DARKER, width=max(2, s // 128))

    # 3. 标题栏（蓝紫渐变）
    title_h = int(win_h * 0.11)
    linear_gradient_h(img, win_y1 + 1, win_y1 + title_h - 1, TITLE_A, TITLE_B)
    # 标题栏底边框
    draw.line([(win_x1, win_y1 + title_h), (win_x2, win_y1 + title_h)], fill=CHROME_DARKER, width=max(1, s // 256))
    # 标题栏顶部高光
    hl = Image.new("RGBA", (win_w, max(1, s // 128)), (255, 255, 255, 70))
    img.paste(hl, (win_x1, win_y1 + 1), hl)

    # 4. 左上三色圆点（红/蓝/绿）
    dot_r = max(3, s // 64)
    dot_gap = dot_r + max(2, s // 96)
    dot_y = win_y1 + title_h // 2
    # 红
    cx = win_x1 + int(s * 0.03)
    cy = dot_y
    draw.ellipse([cx - dot_r, cy - dot_r, cx + dot_r, cy + dot_r],
                 fill=RED_DOT, outline=(0, 0, 0, 180) if False else CHROME_DARKER, width=max(1, s // 256))
    # 高光
    draw.ellipse([cx - dot_r + max(1, s // 160), cy - dot_r + max(1, s // 160),
                  cx + dot_r // 2, cy],
                 fill=(255, 255, 255, 140) if False else (255, 255, 255))
    # 蓝
    cx += dot_gap * 2
    draw.ellipse([cx - dot_r, cy - dot_r, cx + dot_r, cy + dot_r],
                 fill=BLUE_DOT, outline=CHROME_DARKER, width=max(1, s // 256))
    draw.ellipse([cx - dot_r + max(1, s // 160), cy - dot_r + max(1, s // 160),
                  cx + dot_r // 2, cy], fill=(255, 255, 255))
    # 绿
    cx += dot_gap * 2
    draw.ellipse([cx - dot_r, cy - dot_r, cx + dot_r, cy + dot_r],
                 fill=GREEN_DOT, outline=CHROME_DARKER, width=max(1, s // 256))
    draw.ellipse([cx - dot_r + max(1, s // 160), cy - dot_r + max(1, s // 160),
                  cx + dot_r // 2, cy], fill=(255, 255, 255))

    # 5. 内容区（米色）
    content_y1 = win_y1 + title_h + 1
    content_y2 = win_y2 - int(win_h * 0.07) - 1  # 底部留状态栏空间
    content_x1 = win_x1 + 1
    content_x2 = win_x2 - 1
    draw.rectangle([content_x1, content_y1, content_x2, content_y2],
                   fill=CONTENT, outline=CHROME_DARKER, width=max(1, s // 256))

    # 6. 相机图标（黑色扁平风格，居中于内容区）
    cx_area = (content_x1 + content_x2) // 2
    cy_area = (content_y1 + content_y2) // 2
    cam_w = int(win_w * 0.42)
    cam_h = int(win_h * 0.32)
    x1 = cx_area - cam_w // 2
    y1 = cy_area - cam_h // 2
    x2 = cx_area + cam_w // 2
    y2 = cy_area + cam_h // 2

    # 相机主体：梯形（机身略向上收窄）
    trapezoid = [
        (x1 + cam_w * 0.05, y1 + cam_h * 0.20),
        (x2 - cam_w * 0.05, y1 + cam_h * 0.20),
        (x2 - cam_w * 0.00, y2),
        (x1 + cam_w * 0.00, y2),
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
    lens_cy = cy_area + int(cam_h * 0.05)
    lens_r = int(min(cam_w, cam_h) * 0.28)
    draw.ellipse([lens_cx - lens_r, lens_cy - lens_r,
                  lens_cx + lens_r, lens_cy + lens_r],
                 fill=CONTENT, outline=INK, width=max(3, s // 96))
    # 镜头内圈（黑色）
    lens_r2 = int(lens_r * 0.48)
    draw.ellipse([lens_cx - lens_r2, lens_cy - lens_r2,
                  lens_cx + lens_r2, lens_cy + lens_r2],
                 fill=INK)
    # 镜头高光（左上小三角/月牙）
    hl_w = int(lens_r2 * 0.6)
    hl_h = int(lens_r2 * 0.45)
    # 高光用白色弧形
    draw.ellipse([lens_cx - lens_r2 + max(1, s // 128), lens_cy - lens_r2 + max(1, s // 128),
                  lens_cx - lens_r2 + hl_w, lens_cy - lens_r2 + hl_h + 2],
                 fill=(255, 255, 255))
    # 快门按钮（右上角）
    sw = int(cam_w * 0.12)
    sh = int(cam_h * 0.07)
    sx = x2 - cam_w * 0.20
    sy = y1 + cam_h * 0.05
    draw.rounded_rectangle([sx, sy, sx + sw, sy + sh], radius=max(2, s // 128), fill=INK)
    # 镜头左右小凸块
    for side in (-1, 1):
        bx = lens_cx + side * int(lens_r * 1.05)
        bw = int(cam_w * 0.05)
        bh = int(cam_h * 0.06)
        by = lens_cy - bh // 2
        draw.rounded_rectangle([bx - bw // 2, by, bx + bw // 2, by + bh],
                               radius=max(1, s // 256), fill=INK)

    # 7. 底部状态栏
    status_y1 = content_y2 + 1
    status_y2 = win_y2 - 1
    status_h = status_y2 - status_y1
    if status_h > s // 32:
        draw.rectangle([win_x1, status_y1, win_x2, status_y2],
                       fill=STATUS_BG, outline=CHROME_DARKER, width=max(1, s // 256))
        # 状态栏顶部高光
        if max(1, s // 128) >= 1:
            hl2 = Image.new("RGBA", (win_w, max(1, s // 256)), (255, 255, 255, 12))
            img.paste(hl2, (win_x1, status_y1 + 1), hl2)

    # 8. 窗口角落 chrome 内描边（让它更像 BeOS 经典质感）
    corner = int(s * 0.006)
    if corner >= 2:
        # 左上
        draw.rectangle([win_x1 + corner, win_y1 + corner, win_x1 + corner * 2, win_y1 + corner * 2],
                       outline=(255, 255, 255), width=1)

    return img

# ---------- 生成多尺寸 ----------
def build_ico():
    os.makedirs(OUT_DIR, exist_ok=True)

    # 主画 512x512，然后缩放
    master = draw_logo(512)
    master.save(os.path.join(OUT_DIR, "camera-viewer-512.png"))

    sizes = [16, 20, 24, 32, 40, 48, 64, 72, 96, 128, 256]
    imgs = []
    for sz in sizes:
        # LANCZOS 高质量缩放
        resized = master.resize((sz, sz), Image.LANCZOS)
        imgs.append(resized)
        resized.save(os.path.join(OUT_DIR, f"camera-viewer-{sz}.png"))
        print(f"  + {sz}x{sz}")

    # 第一张为 256（Windows 推荐 ICO 最大尺寸），其余按顺序写入 .ico
    # PIL 的 save(ico) 会把 imgs 列表里所有尺寸都写进去
    master_256 = master.resize((256, 256), Image.LANCZOS)
    master_256.save(OUT_ICO, format="ICO", sizes=[(s, s) for s in sizes], append_images=imgs)
    print(f"\n=> {OUT_ICO}")
    print(f"   共 {len(sizes)} 个尺寸: {sizes}")
    print(f"   文件大小: {os.path.getsize(OUT_ICO):,} bytes")

if __name__ == "__main__":
    build_ico()
