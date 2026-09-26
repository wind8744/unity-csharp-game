#!/usr/bin/env python3
"""Procedural pixel-art asset generator for 쪼꼬미 공성전 (Chokkomi Siege).

Run from the project root:  python3 Tools/art/gen_sprites.py

Writes every sprite (RGBA, native size, no antialiasing) into
Assets/Resources/Sprites/ and a x3 contact sheet into the scratchpad so a
reviewer can eyeball the whole set.  Deterministic (seeded).
"""
import math
import os
import random

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "Assets", "Resources", "Sprites")
SCRATCH = ("/private/tmp/claude-501/-Users-jihyun-Projects-unity-csharp-game/"
           "17b25d7a-8120-409c-bb05-16df738f6e9b/scratchpad/art")
CONTACT = os.path.join(SCRATCH, "contact.png")

random.seed(20260926)

# --------------------------------------------------------------------------
# palette
# --------------------------------------------------------------------------
class P:
    skin = (255, 222, 196); skin_d = (236, 190, 160)
    green = (126, 200, 112); green_l = (176, 228, 146); green_d = (78, 150, 86); moss = (110, 176, 92)
    brown = (160, 116, 78); wood = (196, 146, 92); wood_l = (224, 186, 132); bark = (118, 82, 54)
    red = (232, 92, 84); red_d = (176, 56, 62); orange = (250, 152, 64); orange_d = (214, 104, 40)
    yellow = (255, 222, 96); yellow_l = (255, 244, 180)
    steel = (150, 166, 190); steel_l = (198, 208, 224); steel_d = (104, 118, 142)
    brass = (222, 182, 92); brass_d = (170, 130, 60)
    cyan = (120, 220, 240); blue = (110, 160, 230); blue_l = (170, 205, 250); navy = (52, 60, 96)
    purple = (150, 112, 196); purple_d = (96, 66, 140); purple_l = (204, 172, 240)
    grey = (168, 170, 180); grey_l = (212, 214, 222); grey_d = (112, 114, 126)
    stone = (190, 182, 172); stone_d = (142, 132, 122); stone_l = (222, 216, 206)
    white = (255, 255, 255); black = (44, 38, 52); ink = (30, 26, 40)
    pink = (255, 160, 190); pink_l = (255, 204, 222)
    cream = (246, 233, 200); cream_d = (226, 206, 160); cream_l = (255, 250, 236)
    tan = (228, 196, 140); tan_d = (190, 150, 96)
    eye = (46, 36, 52)
    shadow = (0, 0, 0, 70)


def dark(col, k=0.45):
    """Tinted dark outline colour derived from a body colour (never pure black)."""
    r, g, b = col[:3]
    return (int(r * k), int(g * k), min(255, int(b * k) + 10))


def light(col, k=0.5):
    r, g, b = col[:3]
    return (int(r + (255 - r) * k), int(g + (255 - g) * k), int(b + (255 - b) * k))


def OLC(col, ol):
    if ol is False:
        return None
    if ol is None:
        return dark(col)
    return ol


# --------------------------------------------------------------------------
# canvas
# --------------------------------------------------------------------------
class Canvas:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.im = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.im)
        self.ox = 0
        self.oy = 0

    def box(self, x0, y0, x1, y1):
        return [x0 + self.ox, y0 + self.oy, x1 + self.ox, y1 + self.oy]

    def pts(self, p):
        return [(x + self.ox, y + self.oy) for x, y in p]

    def ell(self, x0, y0, x1, y1, col, ol=None):
        self.d.ellipse(self.box(x0, y0, x1, y1), fill=col, outline=OLC(col, ol))

    def rect(self, x0, y0, x1, y1, col, ol=None):
        self.d.rectangle(self.box(x0, y0, x1, y1), fill=col, outline=OLC(col, ol))

    def rrect(self, x0, y0, x1, y1, r, col, ol=None):
        self.d.rounded_rectangle(self.box(x0, y0, x1, y1), radius=r, fill=col, outline=OLC(col, ol))

    def poly(self, p, col, ol=None):
        self.d.polygon(self.pts(p), fill=col, outline=OLC(col, ol))

    def line(self, p, col, w=1):
        self.d.line(self.pts(p), fill=col, width=w, joint="curve" if w > 2 else None)

    def oline(self, p, col, w=2, ol=None):
        """Thick line with a 1px outline (drawn as a wider line beneath)."""
        o = OLC(col, ol)
        if o:
            self.d.line(self.pts(p), fill=o, width=w + 2, joint="curve")
        self.d.line(self.pts(p), fill=col, width=w, joint="curve")

    def arc(self, x0, y0, x1, y1, s, e, col, w=1):
        self.d.arc(self.box(x0, y0, x1, y1), s, e, fill=col, width=w)

    def px(self, x, y, col):
        x += self.ox
        y += self.oy
        if 0 <= x < self.w and 0 <= y < self.h:
            self.im.putpixel((x, y), col if len(col) == 4 else col + (255,))

    def text(self, xy, s, font, col, **kw):
        self.d.text((xy[0] + self.ox, xy[1] + self.oy), s, font=font, fill=col, **kw)


# --------------------------------------------------------------------------
# reusable body-part helpers
# --------------------------------------------------------------------------
def blob(c, cx, cy, rx, ry, col, ol=None):
    c.ell(cx - rx, cy - ry, cx + rx, cy + ry, col, ol)


def eyes(c, cx, cy, gap=4, style="normal", col=P.eye, size=3, hl=True, single=False):
    """Dot eyes with a white highlight. style: normal|blink|angry|happy|glow."""
    xs = [cx] if single else [cx - gap, cx + gap]
    for i, ex in enumerate(xs):
        s = -1 if (i == 0 and not single) else 1
        ey = cy
        if style == "blink":
            c.line([(ex - 1, ey), (ex + 1, ey)], col)
            continue
        if style == "happy":
            c.px(ex - 1, ey, col); c.px(ex, ey - 1, col); c.px(ex + 1, ey, col)
            continue
        r = size // 2
        if size <= 3:
            c.rect(ex - r, ey - r, ex + r, ey + r, col, False)
        else:
            c.ell(ex - r, ey - r, ex + r, ey + r, col, False)
        if hl:
            c.px(ex - r, ey - r, P.white)
            if size >= 5:
                c.px(ex - r + 1, ey - r, P.white); c.px(ex - r, ey - r + 1, P.white)
        if style == "angry":
            c.line([(ex + 2 * s, ey - r - 2), (ex - s, ey - r - 1)], col)
        if style == "glow":
            c.px(ex, ey, P.white)


def blush(c, cx, cy, gap=6, col=P.pink):
    for s in (-1, 1):
        c.rect(cx + s * gap - (1 if s < 0 else 0), cy, cx + s * gap + (1 if s > 0 else 0), cy, col, False)


def mouth(c, x, y, kind="smile", col=P.eye):
    if kind == "smile":
        c.px(x - 1, y, col); c.px(x, y + 1, col); c.px(x + 1, y, col)
    elif kind == "flat":
        c.line([(x - 1, y), (x + 1, y)], col)
    elif kind == "o":
        c.rect(x - 1, y, x, y + 1, col, False)
    elif kind == "grin":
        c.line([(x - 2, y), (x + 2, y)], col); c.px(x - 3, y - 1, col); c.px(x + 3, y - 1, col)
    elif kind == "frown":
        c.px(x - 1, y + 1, col); c.px(x, y, col); c.px(x + 1, y + 1, col)


def feet(c, cx, y, gap=5, col=P.brown, step=0, w=3, h=2):
    """Two little feet. step=1 swaps which foot is forward (walk cycle)."""
    d = 1 if step else -1
    c.ell(cx - gap - w + d, y - h, cx - gap + w + d, y + h, col)
    c.ell(cx + gap - w - d, y - h, cx + gap + w - d, y + h, col)


def ear_tri(c, base_l, base_r, tip, col, inner=None):
    c.poly([base_l, tip, base_r], col)
    if inner:
        mx = (base_l[0] + base_r[0] + tip[0]) // 3
        my = (base_l[1] + base_r[1] + tip[1]) // 3
        c.px(mx, my, inner)


def leaf(c, x, y, col=P.green_l, flip=False, size=4):
    s = -1 if flip else 1
    pts = [(x, y), (x + 2 * s, y - size + 1), (x + 4 * s, y - size), (x + 5 * s, y - size // 2), (x + 3 * s, y + 1)]
    c.poly(pts, col)
    c.line([(x + s, y), (x + 4 * s, y - size + 1)], dark(col))


def star_pts(cx, cy, ro, ri, n=5, rot=-90):
    pts = []
    for i in range(n * 2):
        r = ro if i % 2 == 0 else ri
        a = math.radians(rot + i * 180 / n)
        pts.append((round(cx + r * math.cos(a)), round(cy + r * math.sin(a))))
    return pts


def star(c, cx, cy, ro, ri=None, col=P.yellow, ol=None, n=5):
    ri = ri if ri is not None else max(1, ro * 0.45)
    c.poly(star_pts(cx, cy, ro, ri, n), col, ol)


def spark4(c, cx, cy, r, col=P.yellow_l, ol=False):
    c.poly([(cx, cy - r), (cx + 1, cy - 1), (cx + r, cy), (cx + 1, cy + 1), (cx, cy + r), (cx - 1, cy + 1),
            (cx - r, cy), (cx - 1, cy - 1)], col, ol)


def flame(c, cx, base, w, h, phase=0, col=P.orange, inner=P.yellow, ol=None, core=None):
    """Vertical flame with base at y=base, tip wobbling with phase."""
    t = 1 if phase % 2 else -1
    hw = max(1, w // 2)
    pts = [(cx - hw, base), (cx - hw - 1, base - h // 3), (cx - hw + 1, base - (2 * h) // 3), (cx + t, base - h),
           (cx + hw - 1, base - (2 * h) // 3), (cx + hw + 1, base - h // 3), (cx + hw, base)]
    c.poly(pts, col, P.red_d if ol is None else ol)
    ih = max(3, h // 2)
    iw = max(1, hw // 2)
    c.poly([(cx - iw, base - 1), (cx - iw, base - ih // 2), (cx, base - ih), (cx + iw, base - ih // 2),
            (cx + iw, base - 1)], inner, False)
    if core and ih >= 5:
        c.ell(cx - 1, base - ih // 2 - 1, cx + 1, base - ih // 2 + 1, core, False)


def flame_h(c, x0, cy, L, h, phase=0, col=P.orange, inner=P.yellow, ol=None):
    """Horizontal flame pointing right from x0."""
    t = 1 if phase % 2 else -1
    hh = max(1, h // 2)
    pts = [(x0, cy - hh), (x0 + L // 3, cy - hh - 1), (x0 + (2 * L) // 3, cy - hh + 1), (x0 + L, cy + t),
           (x0 + (2 * L) // 3, cy + hh - 1), (x0 + L // 3, cy + hh + 1), (x0, cy + hh)]
    c.poly(pts, col, P.red_d if ol is None else ol)
    il = max(3, L // 2)
    ih = max(1, hh // 2)
    c.poly([(x0 + 1, cy - ih), (x0 + il // 2, cy - ih), (x0 + il, cy), (x0 + il // 2, cy + ih), (x0 + 1, cy + ih)],
           inner, False)


def gear(c, cx, cy, r, col=P.brass, teeth=8, phase=0):
    off = (360 / teeth / 2) * (phase % 2)
    for i in range(teeth):
        a = math.radians(i * 360 / teeth + off)
        x = round(cx + (r + 1) * math.cos(a))
        y = round(cy + (r + 1) * math.sin(a))
        c.rect(x - 1, y - 1, x + 1, y + 1, col)
    c.ell(cx - r, cy - r, cx + r, cy + r, col)
    if r >= 4:
        c.ell(cx - r + 2, cy - r + 2, cx + r - 2, cy + r - 2, P.brass_d, False)
        c.ell(cx - 1, cy - 1, cx + 1, cy + 1, col, False)


def wheel(c, cx, cy, r, col=P.wood):
    c.ell(cx - r, cy - r, cx + r, cy + r, col, P.bark)
    c.ell(cx - r + 1, cy - r + 1, cx + r - 1, cy + r - 1, col, P.bark)
    c.line([(cx - r + 2, cy), (cx + r - 2, cy)], P.bark)
    c.line([(cx, cy - r + 2), (cx, cy + r - 2)], P.bark)
    c.rect(cx - 1, cy - 1, cx + 1, cy + 1, P.brass, False)


def bow(c, x, cy, h, drawn=False, fire=False, wood=P.wood, phase=0):
    """Bow that bows out to the right; string on the left side (x)."""
    c.arc(x - 6, cy - h // 2, x + 6, cy + h // 2, 270, 90, dark(wood), 3)
    c.arc(x - 5, cy - h // 2 + 1, x + 5, cy + h // 2 - 1, 270, 90, wood, 1)
    if drawn:
        c.line([(x, cy - h // 2), (x - 5, cy), (x, cy + h // 2)], P.grey_l)
        arrow(c, x - 6, cy, 14, fire, phase)
    else:
        c.line([(x, cy - h // 2), (x, cy + h // 2)], P.grey_l)
    if fire:
        flame(c, x, cy - h // 2 + 2, 4, 6, phase)
        flame(c, x, cy + h // 2 + 2, 4, 6, phase + 1)


def arrow(c, x, y, L, fire=False, phase=0):
    if fire:
        flame_h(c, x - 4, y, 6, 4, phase)
    c.line([(x, y), (x + L - 3, y)], P.wood)
    c.poly([(x + L - 4, y - 2), (x + L, y), (x + L - 4, y + 2)], P.steel_l)
    c.line([(x, y - 1), (x + 2, y - 1)], P.red)
    c.line([(x, y + 1), (x + 2, y + 1)], P.red)


def propeller(c, cx, y, phase=0, col=P.steel_d):
    c.rect(cx - 1, y, cx + 1, y + 3, col)
    if phase % 2 == 0:
        c.rect(cx - 12, y - 1, cx + 12, y, P.steel_l, P.steel_d)
    else:
        c.rect(cx - 6, y - 1, cx + 6, y, P.steel_l, P.steel_d)


def shadow(c, cx, y, rx=10, ry=2):
    c.ell(cx - rx, y - ry, cx + rx, y + ry, P.shadow, False)


def wrench(c, x, y, up=False):
    col, ol = P.steel_d, dark(P.steel_d)
    if up:
        c.oline([(x, y), (x + 3, y - 9)], col, 2, ol)
        c.ell(x + 1, y - 13, x + 6, y - 8, col, ol)
        c.px(x + 4, y - 10, (0, 0, 0, 0)); c.px(x + 5, y - 11, (0, 0, 0, 0))
    else:
        c.oline([(x, y), (x + 8, y + 3)], col, 2, ol)
        c.ell(x + 7, y + 1, x + 12, y + 6, col, ol)
        c.px(x + 11, y + 3, (0, 0, 0, 0)); c.px(x + 12, y + 4, (0, 0, 0, 0))


def hood_face(c, cx, cy, hood_col, face_col=P.skin, r=11):
    blob(c, cx, cy - 4, r, r - 1, hood_col)
    blob(c, cx, cy + 1, r - 4, r - 5, face_col)


def robe(c, cx, top, bottom, half, col, trim=None):
    c.poly([(cx, top), (cx - half, bottom - 4), (cx - half + 1, bottom), (cx + half - 1, bottom), (cx + half, bottom - 4)], col)
    if trim:
        c.line([(cx - half + 2, bottom - 2), (cx + half - 2, bottom - 2)], trim)


def skull(c, cx, cy, r, col=P.cream_l, eye=P.eye):
    c.ell(cx - r, cy - r, cx + r, cy + r - 1, col)
    c.rect(cx - r + 2, cy + r - 2, cx + r - 2, cy + r + 1, col)
    c.rect(cx - r // 2 - 1, cy - 1, cx - r // 2 + 1, cy + 1, eye, False)
    c.rect(cx + r // 2 - 1, cy - 1, cx + r // 2 + 1, cy + 1, eye, False)
    for tx in range(cx - r + 3, cx + r - 2, 2):
        c.px(tx, cy + r, dark(col))


def heart(c, cx, cy, r, col=P.red):
    c.ell(cx - r, cy - r, cx, cy, col)
    c.ell(cx, cy - r, cx + r, cy, col)
    c.poly([(cx - r, cy - r // 2), (cx + r, cy - r // 2), (cx, cy + r)], col)
    c.rect(cx - r + 1, cy - r // 2, cx + r - 1, cy - r // 2 + 1, col, False)
    c.px(cx - r + 2, cy - r + 2, P.white)


def coin(c, cx, cy, r):
    c.ell(cx - r, cy - r, cx + r, cy + r, P.brass, P.brass_d)
    c.ell(cx - r + 2, cy - r + 2, cx + r - 2, cy + r - 2, P.yellow, P.brass_d)
    c.px(cx - r + 2, cy - r + 3, P.yellow_l)
    c.px(cx - r + 3, cy - r + 2, P.yellow_l)


# --------------------------------------------------------------------------
# towers  (f: 0 idle A, 1 idle B, 2 attack)
# --------------------------------------------------------------------------
def archer(c, f, hood, tunic, fire):
    atk = f == 2
    c.rect(9, 22, 14, 36, P.bark)                      # quiver
    for i in range(3):
        c.px(10 + i * 2, 21, P.red)
    feet(c, 23, 44, 4, P.brown)
    blob(c, 23, 35, 9, 8, tunic)                       # tunic
    c.rect(15, 36, 31, 37, P.bark, False)              # belt
    c.oline([(30, 32), (37, 31)], tunic, 3)            # arm
    bow(c, 38, 30, 16, drawn=atk, fire=fire, phase=f)
    blob(c, 38, 30, 2, 2, P.skin)                      # hand
    hood_face(c, 23, 22, hood, P.skin, 11)
    leaf(c, 27, 12, P.green_l if not fire else P.yellow)
    eyes(c, 23, 23, 3, style="blink" if f == 1 else "normal")
    blush(c, 23, 25, 5)
    mouth(c, 23, 27, "smile")


def tower_1(c, f):
    archer(c, f, P.green_d, P.green, False)


def tower_10(c, f):
    archer(c, f, P.red_d, P.tan, True)


def tower_2(c, f):
    atk = f == 2
    L = 6 if atk else 3
    c.oy = -1 if f == 1 else 0
    for ang in range(0, 360, 30):
        if 50 < ang < 130:
            continue
        a = math.radians(ang)
        x0 = round(24 + 12 * math.cos(a)); y0 = round(30 + 11 * math.sin(a))
        x1 = round(24 + (13 + L) * math.cos(a)); y1 = round(30 + (12 + L) * math.sin(a))
        c.oline([(x0, y0), (x1, y1)], P.wood, 2, P.bark)
    blob(c, 24, 30, 14, 12, P.green_d)
    blob(c, 15, 25, 5, 4, P.green)
    blob(c, 33, 25, 5, 4, P.green)
    blob(c, 24, 22, 6, 5, P.green_l)
    blob(c, 24, 32, 11, 8, P.green, False)
    eyes(c, 24, 30, 5, style="angry")
    mouth(c, 24, 35, "frown")
    blush(c, 24, 32, 8)
    if atk:
        c.oline([(40, 18), (46, 12)], P.wood, 2, P.bark)
    c.oy = 0


def tower_3(c, f):
    atk = f == 2
    robe(c, 24, 20, 44, 11, P.green_d, P.green_l)
    c.rect(19, 30, 29, 31, P.brass, False)             # sash
    c.line([(37, 14), (37, 44)], P.bark, 2)            # staff
    blob(c, 35, 30, 2, 2, P.skin)                      # hand
    blob(c, 24, 17, 8, 7, P.skin)                      # head
    c.oline([(19, 12), (16, 6), (13, 2)], P.wood, 1, P.bark)   # antlers
    c.oline([(16, 6), (19, 3)], P.wood, 1, P.bark)
    c.oline([(29, 12), (32, 6), (35, 2)], P.wood, 1, P.bark)
    c.oline([(32, 6), (29, 3)], P.wood, 1, P.bark)
    c.rect(18, 10, 30, 12, P.moss, P.green_d)          # leaf band
    eyes(c, 24, 18, 3, style="blink" if f == 1 else "normal")
    blush(c, 24, 20, 5)
    mouth(c, 24, 22, "smile")
    if atk:
        blob(c, 37, 10, 7, 7, (140, 255, 160, 120), False)
        blob(c, 37, 10, 4, 4, P.green_l, P.green_d)
        c.px(36, 8, P.white); c.px(36, 9, P.white)
        spark4(c, 44, 5, 2); spark4(c, 30, 4, 2)
    else:
        blob(c, 37, 10, 4, 4, P.green, P.green_d)
        c.px(36, 8, P.white)


def tower_4(c, f):
    atk = f == 2
    stone = (208, 108, 92)
    c.rect(12, 44, 36, 46, dark(stone, 0.6), dark(stone))
    c.rect(14, 22, 34, 44, stone)
    for y in (28, 34, 40):
        c.line([(15, y), (33, y)], dark(stone, 0.7))
    for x in (20, 28):
        c.line([(x, 23), (x, 27)], dark(stone, 0.7)); c.line([(x, 35), (x, 39)], dark(stone, 0.7))
    c.line([(24, 29), (24, 33)], dark(stone, 0.7))
    c.rect(12, 19, 36, 22, light(stone, 0.2), dark(stone))
    eyes(c, 24, 31, 4, style="blink" if f == 1 else "normal")
    blush(c, 24, 33, 7)
    mouth(c, 24, 36, "smile")
    lift = 2 if atk else 0
    c.oline([(24, 20 - lift), (24, 9 - lift)], P.wood, 2, P.bark)
    if atk:
        flame(c, 24, 14, 12, 15, f, core=P.white)
    else:
        flame(c, 24, 13, 8, 10, f)
    c.poly([(24, 1 - lift), (21, 8 - lift), (27, 8 - lift)], P.steel_l)


def tower_5(c, f):
    atk = f == 2
    feet(c, 23, 45, 4, P.brown)
    robe(c, 23, 22, 44, 11, P.red, P.yellow)
    c.rect(18, 32, 28, 33, P.brass, False)
    blob(c, 23, 21, 8, 7, P.skin)
    c.poly([(12, 19), (34, 19), (28, 2)], P.red_d)     # hat cone
    c.ell(10, 16, 36, 21, P.red_d)                     # brim
    c.rect(15, 17, 31, 18, P.yellow, False)            # band
    star(c, 22, 10, 3, 1.3, P.yellow, False)
    eyes(c, 23, 24, 3, style="blink" if f == 1 else "normal")
    blush(c, 23, 26, 5)
    mouth(c, 23, 28, "o" if atk else "smile")
    c.oline([(30, 32), (36, 31)], P.red, 3)
    blob(c, 36, 31, 2, 2, P.skin)
    if atk:
        blob(c, 37, 22, 8, 8, (255, 200, 90, 110), False)
        flame(c, 37, 29, 10, 14, f, core=P.white)
    else:
        flame(c, 37, 29, 6, 9, f)


def tower_6(c, f):
    atk = f == 2
    c.rect(22, 37, 26, 46, P.wood, P.bark)             # post
    c.rect(9, 34, 39, 37, P.wood, P.bark)              # bar
    # tail feathers (flame tongues to the left/back)
    for i, (dx, col) in enumerate(((0, P.red), (3, P.orange), (6, P.yellow))):
        c.poly([(20 + dx, 28 + i), (8 + dx - (2 if atk else 0), 20 + i * 3), (14 + dx, 32 + i)], col, dark(col))
    if atk:
        c.poly([(18, 22), (4, 8), (10, 26)], P.orange, P.orange_d)     # left wing spread
        c.poly([(34, 22), (46, 8), (42, 26)], P.orange, P.orange_d)    # right wing
    else:
        c.poly([(19, 22), (11, 18), (14, 28)], P.orange_d)
        c.poly([(33, 22), (41, 18), (38, 28)], P.orange_d)
    blob(c, 26, 26, 9, 8, P.orange)
    blob(c, 26, 28, 5, 5, P.yellow, False)
    blob(c, 30, 16, 6, 6, P.orange)
    flame(c, 30, 11, 4, 6, f, ol=P.orange_d)           # crest
    c.poly([(35, 15), (40, 17), (35, 19)], P.yellow, P.brass_d)   # beak
    eyes(c, 30, 16, 2, style="blink" if f == 1 else "normal")
    blush(c, 30, 18, 4)
    c.line([(22, 33), (22, 36)], P.brass_d); c.line([(29, 33), (29, 36)], P.brass_d)   # talons


def tower_7(c, f):
    atk = f == 2
    c.rrect(11, 39, 37, 46, 3, P.steel_d)
    gear(c, 9, 26, 5, phase=f)
    recoil = -1 if atk else 0
    blob(c, 24, 27, 12, 11, P.steel)
    c.rect(34 + recoil, 24, 43 + recoil, 30, P.steel_d)           # barrel
    c.rect(42 + recoil, 23, 44 + recoil, 31, P.steel_l, P.steel_d)
    if atk:
        spark4(c, 46, 27, 4, P.yellow_l, False)
        c.px(45, 27, P.orange); c.px(46, 27, P.orange)
    blob(c, 23, 27, 7, 7, P.steel_d)                   # lens rim
    blob(c, 23, 27, 5, 5, P.cyan, P.steel_d)
    blob(c, 24, 28, 2, 2, P.navy, False)
    c.px(20, 24, P.white); c.px(21, 24, P.white); c.px(20, 25, P.white)
    for (x, y) in ((14, 20), (33, 20), (14, 34), (33, 34)):
        c.px(x, y, P.brass)
    c.rect(20, 14, 28, 16, P.brass, P.brass_d)          # brass plate
    c.rect(22, 17, 26, 39, P.steel_l, False) if False else None


def tower_8(c, f):
    atk = f == 2
    back = 2 if atk else 0
    wheel(c, 13, 40, 6)
    wheel(c, 35, 40, 6)
    c.rrect(9, 27, 39, 43, 4, P.steel)
    c.oline([(26 - back, 31 + back), (40 - back, 15 + back)], P.steel_d, 8)
    c.ell(37 - back, 12 + back, 44 - back, 19 + back, P.steel_d)
    c.ell(39 - back, 14 + back, 42 - back, 17 + back, P.ink, False)
    if atk:
        spark4(c, 43, 10, 5, P.yellow_l, False)
        blob(c, 42, 11, 2, 2, P.orange, False)
    c.rect(12, 30, 20, 31, P.brass, P.brass_d)          # hatch
    eyes(c, 19, 36, 4, style="blink" if f == 1 else "normal")
    blush(c, 19, 38, 7)
    mouth(c, 19, 40, "smile")


def drone(c, f, sniper=False):
    atk = f == 2
    cx = 18 if sniper else 24
    shadow(c, cx, 44, 9, 2)
    c.oy = -1 if f == 1 else 0
    propeller(c, cx, 13, f)
    blob(c, cx, 26, 10, 9, P.steel_l)
    c.rect(cx - 6, 30, cx + 6, 33, P.steel, P.steel_d)
    c.px(cx - 7, 20, P.white); c.px(cx - 6, 19, P.white)
    if sniper:
        c.rect(cx + 8, 26, 45, 29, P.steel_d)                     # long barrel
        c.rect(cx + 8, 25, cx + 12, 30, P.steel, P.steel_d)
        c.rect(cx + 10, 21, cx + 17, 24, P.steel_d)               # scope
        c.px(cx + 16, 22, P.cyan)
        if atk:
            spark4(c, 46, 27, 3, P.yellow_l, False)
        eyes(c, cx, 24, 4, style="normal", size=3)
        c.px(cx + 9, 23, P.red)
    else:
        wrench(c, cx + 9, 27, up=atk)
        if atk:
            spark4(c, cx + 17, 12, 2, P.green_l, False)
            spark4(c, cx + 11, 10, 2, P.green_l, False)
        eyes(c, cx, 24, 4, style="happy" if atk else "normal")
    blush(c, cx, 26, 7)
    c.px(cx + 7, 30, P.red if f == 0 else P.red_d)
    c.oy = 0


def tower_9(c, f):
    drone(c, f, False)


def tower_15(c, f):
    drone(c, f, True)


def tower_11(c, f):
    atk = f == 2
    c.oy = -1 if f == 1 else 0
    c.rect(23, 26, 25, 44, P.green_d, dark(P.green_d))
    for y in (32, 38):
        c.poly([(23, y), (19, y - 1), (23, y + 2)], P.bark, False)
        c.poly([(25, y + 3), (29, y + 2), (25, y + 5)], P.bark, False)
    leaf(c, 22, 40, P.green, flip=True, size=5)
    leaf(c, 26, 36, P.green, size=5)
    if atk:
        for (x, y) in ((6, 6), (42, 8), (4, 28), (44, 30)):
            spark4(c, x, y, 3, P.yellow_l, False)
    for i in range(6):
        a = math.radians(i * 60 - 90)
        px_ = round(24 + 9 * math.cos(a)); py_ = round(18 + 8 * math.sin(a))
        blob(c, px_, py_, 5, 4, P.pink, dark(P.pink, 0.55))
    if atk:
        blob(c, 24, 18, 9, 8, (255, 240, 150, 170), False)
    blob(c, 24, 18, 7, 6, P.yellow_l if atk else P.yellow, P.brass_d)
    eyes(c, 24, 17, 3, style="angry" if atk else ("blink" if f == 1 else "normal"))
    blush(c, 24, 19, 5)
    mouth(c, 24, 21, "grin" if atk else "smile")
    if atk:
        for (x0, y0, x1, y1) in ((40, 10, 46, 6), (42, 24, 47, 26), (4, 10, 1, 5)):
            c.oline([(x0, y0), (x1, y1)], P.wood, 1, P.bark)
    c.oy = 0


def tower_12(c, f):
    atk = f == 2
    c.rrect(6, 40, 36, 46, 2, P.steel_d)
    for x in range(8, 35, 4):
        c.px(x, 43, P.ink)
    c.rrect(4, 21, 14, 40, 4, P.red)                      # fuel tank
    c.rect(5, 28, 13, 30, P.yellow, False); c.rect(5, 32, 13, 34, P.yellow, False)
    c.rrect(14, 24, 31, 41, 3, P.steel)
    c.rect(31, 29, 38, 34, P.steel_d)                     # nozzle
    c.rect(37, 28, 39, 35, P.brass, P.brass_d)
    eyes(c, 22, 30, 3, style="blink" if f == 1 else "normal")
    blush(c, 22, 32, 5)
    mouth(c, 22, 35, "o" if atk else "smile")
    c.rect(16, 18, 24, 24, P.steel_d)                     # chimney
    if atk:
        flame_h(c, 40, 31, 7, 10, f)
    else:
        flame_h(c, 40, 31, 4, 4, f)
    c.px(20, 15, P.grey_l); c.px(21, 13, P.grey_l)


def tower_13(c, f):
    atk = f == 2
    wheel(c, 11, 41, 5)
    wheel(c, 37, 41, 5)
    c.rect(6, 35, 42, 39, P.wood, P.bark)
    c.oline([(13, 37), (24, 19)], P.wood, 3, P.bark)
    c.oline([(35, 37), (24, 19)], P.wood, 3, P.bark)
    c.rect(16, 27, 32, 34, P.wood_l, P.bark)              # cross beam with face
    if atk:
        c.oline([(24, 30), (40, 8)], P.bark, 3, dark(P.bark))
        blob(c, 41, 7, 3, 3, P.bark)
    else:
        c.oline([(24, 30), (8, 12)], P.bark, 3, dark(P.bark))
        blob(c, 7, 11, 3, 3, P.bark)
        blob(c, 7, 8, 3, 3, P.grey)                       # rock in bucket
    eyes(c, 24, 30, 4, style="blink" if f == 1 else "normal")
    blush(c, 24, 32, 7)
    leaf(c, 30, 20, P.green, size=5)
    leaf(c, 17, 24, P.green, flip=True, size=4)
    leaf(c, 38, 35, P.green_l, size=4)
    c.px(20, 36, P.green_l); c.px(28, 37, P.green)


def tower_14(c, f):
    atk = f == 2
    c.oy = -1 if f == 1 else 0
    c.line([(16, 27), (19, 34)], P.bark); c.line([(32, 27), (29, 34)], P.bark)
    c.rrect(17, 34, 31, 41, 2, P.wood, P.bark)             # gondola
    c.px(20, 37, P.brass); c.px(24, 37, P.brass); c.px(28, 37, P.brass)
    c.poly([(11, 15), (3, 11), (3, 21)], P.red)            # fin
    blob(c, 24, 17, 15, 11, P.blue)
    c.line([(17, 8), (15, 26)], dark(P.blue, 0.7)); c.line([(31, 8), (33, 26)], dark(P.blue, 0.7))
    c.rect(24, 7, 24, 27, dark(P.blue, 0.7), False)
    c.ell(12, 9, 20, 14, P.blue_l, False)
    c.line([(24, 6), (24, 1)], P.steel_l)
    c.px(24, 0, P.yellow)
    eyes(c, 24, 18, 4, style="blink" if f == 1 else "normal")
    blush(c, 24, 20, 7)
    mouth(c, 24, 22, "smile")
    if atk:
        c.line([(27, 1), (30, 4), (26, 6), (31, 9)], P.yellow, 2)
        c.line([(21, 1), (18, 4), (22, 6), (17, 9)], P.yellow, 2)
        spark4(c, 24, 2, 3, P.yellow_l, False)
    else:
        c.line([(27, 2), (29, 4), (26, 5)], P.yellow)
    c.oy = 0


def tower_16(c, f):
    atk = f == 2
    L = 8 if atk else 5
    for ang in range(0, 360, 45):
        a = math.radians(ang + 22)
        x0 = round(24 + 11 * math.cos(a)); y0 = round(28 + 11 * math.sin(a))
        x1 = round(24 + (13 + L) * math.cos(a)); y1 = round(28 + (13 + L) * math.sin(a))
        c.oline([(x0, y0), (x1, y1)], P.steel_l, 2, P.steel_d)
    blob(c, 24, 28, 13, 13, P.steel)
    c.rect(11, 28, 37, 30, P.steel_d, False)
    for ang in range(0, 360, 45):
        a = math.radians(ang)
        c.px(round(24 + 9 * math.cos(a)), round(28 + 9 * math.sin(a)), P.brass)
    c.px(16, 20, P.white); c.px(17, 19, P.white)
    eyes(c, 24, 25, 4, style="angry" if atk else ("blink" if f == 1 else "normal"))
    blush(c, 24, 27, 7)
    mouth(c, 24, 34, "grin" if atk else "smile")


def tower_17(c, f):
    atk = f == 2
    shadow(c, 24, 45, 8, 2)
    if atk:
        blob(c, 24, 26, 17, 17, (255, 190, 80, 90), False)
        flame(c, 24, 42, 24, 38, f, core=P.white)
    else:
        flame(c, 24, 41, 20, 32, f)
    for (x, y) in ((6, 20), (42, 14), (10, 8), (40, 30)):
        c.px(x, y + (1 if f == 1 else 0), P.yellow)
    eyes(c, 24, 31, 4, style="angry" if atk else ("blink" if f == 1 else "normal"))
    blush(c, 24, 33, 7)
    mouth(c, 24, 36, "o" if atk else "smile")


# --- tier-3 fusions (18-21): taller silhouettes, gold trim, glowing gems, floating sparks ---
def tower_18(c, f):
    """유성 저격수: hooded fire archer on a hovering drone platform with a long scoped bow."""
    atk = f == 2
    shadow(c, 22, 46, 13, 2)
    c.oy = -1 if f == 1 else 0
    # hover platform (drone disc with brass band and thruster glow)
    c.rect(11, 46, 14, 47, P.cyan, False); c.rect(30, 46, 33, 47, P.cyan, False)
    c.ell(5, 40, 39, 46, P.steel_l, P.steel_d)
    c.rect(7, 42, 37, 43, P.brass, False)
    c.px(9, 41, P.white); c.px(10, 41, P.white)
    c.rect(20, 44, 24, 45, P.red if f == 0 else P.red_d, False)     # status light
    # archer
    c.rect(8, 20, 13, 34, P.bark)                       # quiver
    for i in range(3):
        c.px(9 + i * 2, 19, P.yellow)
    feet(c, 22, 39, 4, P.brown)
    blob(c, 22, 31, 9, 7, P.orange_d)                   # tunic
    c.rect(14, 32, 30, 33, P.brass, False)              # gold belt
    c.px(22, 32, P.yellow)
    c.oline([(29, 28), (36, 27)], P.orange_d, 3)        # arm
    bow(c, 37, 26, 22, drawn=atk, fire=True, phase=f)   # long sniper bow
    c.rect(38, 18, 43, 20, P.steel_d)                   # scope
    c.px(43, 19, P.cyan)
    blob(c, 37, 26, 2, 2, P.skin)                       # hand
    hood_face(c, 22, 18, P.red_d, P.skin, 11)
    c.rect(21, 7, 23, 9, P.yellow, P.brass_d)           # forehead gem
    flame(c, 25, 6, 3, 5, f, ol=P.orange_d)             # flame plume on hood
    eyes(c, 22, 19, 3, style="blink" if f == 1 else "normal")
    blush(c, 22, 21, 5)
    mouth(c, 22, 23, "smile")
    if atk:
        blob(c, 44, 26, 6, 6, (255, 200, 90, 110), False)
        spark4(c, 45, 26, 5, P.yellow_l, False)
        blob(c, 45, 26, 1, 1, P.white, False)
        c.px(41, 21, P.yellow); c.px(39, 32, P.yellow); c.px(35, 20, P.yellow_l)
    else:
        c.px(44, 22 + f, P.yellow_l); c.px(46, 30 - f, P.yellow); c.px(45, 14 + f, P.yellow_l)
    c.oy = 0


def tower_19(c, f):
    """가시 거목: thorny tree stump with steel-plated spikes and a pink flower on top."""
    atk = f == 2
    L = 8 if atk else 5
    # spikes (drawn first so bases hide under the body)
    for by in (26, 34, 41):
        dy = -3 if by == 26 else (3 if by == 41 else 0)
        c.poly([(10, by - 2), (10 - L, by + dy), (10, by + 2)], P.steel_l, P.steel_d)
        c.poly([(38, by - 2), (38 + L, by + dy), (38, by + 2)], P.steel_l, P.steel_d)
    c.poly([(12, 21), (9 - L // 2, 14 - L // 2), (16, 19)], P.steel_l, P.steel_d)
    c.poly([(36, 21), (39 + L // 2, 14 - L // 2), (32, 19)], P.steel_l, P.steel_d)
    # roots
    c.ell(4, 42, 16, 47, P.bark, dark(P.bark))
    c.ell(32, 42, 44, 47, P.bark, dark(P.bark))
    # stump body
    c.rrect(9, 19, 39, 45, 5, P.brown, dark(P.brown))
    c.line([(15, 26), (14, 40)], P.bark); c.line([(33, 25), (34, 39)], P.bark)
    c.line([(24, 40), (24, 43)], P.bark)
    # steel plates with brass rivets
    for by in (26, 34, 41):
        for x0 in (9, 36):
            c.rect(x0, by - 3, x0 + 3, by + 3, P.steel, P.steel_d)
            c.px(x0 + 1, by - 2, P.brass); c.px(x0 + 1, by + 2, P.brass)
    c.rect(11, 21, 37, 22, P.brass, False)              # gold band under the rim
    # cut surface with growth rings
    c.ell(9, 13, 39, 23, P.wood_l, P.bark)
    c.arc(14, 15, 34, 21, 0, 360, P.tan_d)
    c.arc(19, 16, 29, 20, 0, 360, P.tan_d)
    # face
    eyes(c, 24, 31, 5, style="angry" if atk else ("blink" if f == 1 else "normal"))
    blush(c, 24, 33, 8)
    mouth(c, 24, 36, "grin" if atk else "smile")
    # flower (bobs on idle B)
    c.oy = -1 if f == 1 else 0
    c.line([(24, 16), (24, 11)], P.green_d)
    leaf(c, 24, 14, P.green, size=4)
    leaf(c, 24, 13, P.green, flip=True, size=4)
    for i in range(5):
        a = math.radians(i * 72 - 90)
        blob(c, round(24 + 4 * math.cos(a)), round(7 + 4 * math.sin(a)), 3, 3, P.pink, dark(P.pink, 0.55))
    blob(c, 24, 7, 3, 2, P.yellow, P.brass_d)
    c.px(23, 6, P.yellow_l)
    c.oy = 0
    # floating sparks / thorn projectiles
    if atk:
        for (x0, y0, x1, y1) in ((5, 9, 2, 5), (43, 9, 46, 5), (14, 6, 11, 2), (34, 6, 37, 2), (2, 22, 0, 18), (46, 22, 47, 18)):
            c.oline([(x0, y0), (x1, y1)], P.wood, 1, P.bark)
        spark4(c, 6, 3, 2, P.green_l, False); spark4(c, 42, 3, 2, P.green_l, False)
    else:
        spark4(c, 4, 12 + f, 2, P.green_l, False)
        spark4(c, 44, 10 - f, 2, P.green_l, False)


def tower_20(c, f):
    """화산 심장: volcano body with a molten heart window and a flame-spirit face on top."""
    atk = f == 2
    rock = (132, 90, 86)
    shadow(c, 24, 46, 14, 2)
    if atk:
        blob(c, 24, 10, 15, 12, (255, 190, 80, 90), False)
    # volcano slopes
    c.poly([(15, 17), (33, 17), (46, 46), (2, 46)], rock, dark(rock))
    c.line([(20, 19), (21, 30)], light(rock, 0.25)); c.line([(11, 36), (8, 44)], light(rock, 0.25))
    # lava streaks + drips
    c.line([(17, 19), (12, 30), (10, 39)], P.orange, 2)
    c.line([(31, 19), (36, 28), (39, 37)], P.orange, 2)
    c.px(14, 25, P.yellow); c.px(35, 26, P.yellow)
    d = 1 if f == 1 else 0
    c.ell(9, 39 + d, 11, 43 + d, P.orange, P.orange_d)
    c.ell(38, 37 + d, 40, 41 + d, P.orange, P.orange_d)
    c.px(10, 40 + d, P.yellow); c.px(39, 38 + d, P.yellow)
    # chest window with glowing heart (brass frame)
    c.rrect(17, 25, 31, 40, 3, P.ink, P.brass)
    for (x, y) in ((17, 25), (31, 25), (17, 40), (31, 40)):
        c.px(x, y, P.yellow)
    if f == 0:
        blob(c, 24, 32, 5, 5, (255, 180, 70, 130), False)
    else:
        blob(c, 24, 32, 6, 6, (255, 220, 110, 190), False)
    heart(c, 24, 32, 4, P.orange if f == 0 else P.yellow)
    c.px(24, 32, P.yellow_l if f == 0 else P.white)
    # crater + flame spirit
    c.ell(14, 14, 34, 20, dark(rock, 0.6), dark(rock))
    c.ell(16, 15, 32, 19, P.orange_d, False)
    if atk:
        flame(c, 24, 19, 20, 19, f, core=P.white)
    else:
        flame(c, 24, 19, 16, 18, f)
    eyes(c, 24, 11, 3, style="angry" if atk else ("blink" if f == 1 else "normal"))
    blush(c, 24, 13, 5)
    mouth(c, 24, 15, "o" if atk else "smile")
    # eruption sparks / floating embers
    if atk:
        for (x, y, r) in ((8, 7, 3), (40, 5, 3), (13, 1, 2), (36, 1, 2), (4, 16, 2), (44, 14, 2)):
            spark4(c, x, y, r, P.yellow_l, False)
        c.px(10, 12, P.orange); c.px(38, 10, P.orange); c.px(24, 0, P.yellow)
    else:
        c.px(6, 24 + f, P.yellow); c.px(42, 18 - f, P.yellow); c.px(38, 8 + f, P.yellow_l); c.px(9, 6 - f, P.yellow_l)


def tower_21(c, f):
    """천둥 요새: fortress turret with lightning rod, twin brass cannons and a tethered balloon."""
    atk = f == 2
    bob = -1 if f == 1 else 0
    # tethered balloon (top-left)
    c.line([(9, 13 + bob), (12, 25)], P.grey_l)
    blob(c, 9, 7 + bob, 5, 4, P.blue)
    c.line([(9, 3 + bob), (9, 11 + bob)], dark(P.blue, 0.7))
    c.px(6, 5 + bob, P.blue_l); c.px(7, 4 + bob, P.blue_l)
    c.rect(8, 12 + bob, 10, 13 + bob, P.wood, P.bark)
    # lightning rod + roof + tower
    c.line([(24, 4), (24, 12)], P.steel_l)
    c.px(24, 3, P.yellow)
    if f == 1:
        spark4(c, 24, 3, 1, P.yellow_l, False)
    blob(c, 24, 12, 1, 1, P.brass, False)
    c.poly([(17, 17), (31, 17), (24, 11)], P.navy, dark(P.navy))
    c.rect(19, 17, 29, 29, P.steel_l, P.steel_d)
    c.rect(23, 20, 25, 23, P.navy, False)
    c.rect(19, 25, 29, 26, P.brass, False)
    # cannon mounts + brass barrels (recoil on attack)
    r = 1 if atk else 0
    c.rect(8, 25, 15, 31, P.steel_d, dark(P.steel_d))
    c.rect(33, 25, 40, 31, P.steel_d, dark(P.steel_d))
    c.rect(4 - r, 23, 14 - r, 28, P.brass, P.brass_d)
    c.rect(4 - r, 22, 6 - r, 29, P.brass_d, dark(P.brass))
    c.line([(7 - r, 24), (12 - r, 24)], light(P.brass, 0.5))
    c.rect(34 + r, 23, 44 + r, 28, P.brass, P.brass_d)
    c.rect(42 + r, 22, 44 + r, 29, P.brass_d, dark(P.brass))
    c.line([(36 + r, 24), (41 + r, 24)], light(P.brass, 0.5))
    # fortress base with crenellations
    for x in (8, 14, 31, 37):
        c.rect(x, 26, x + 3, 30, P.steel, P.steel_d)
    c.rrect(6, 29, 42, 46, 3, P.steel, P.steel_d)
    c.rect(7, 30, 41, 30, P.steel_l, False)
    for (x, y) in ((8, 32), (40, 32), (8, 44), (40, 44)):
        c.px(x, y, P.brass)
    c.rect(10, 34, 13, 37, P.navy, False); c.rect(35, 34, 38, 37, P.navy, False)   # arrow slits
    eyes(c, 24, 37, 4, style="blink" if f == 1 else "normal")
    blush(c, 24, 39, 7)
    mouth(c, 24, 42, "o" if atk else "smile")
    if atk:
        c.line([(27, 4), (32, 7), (28, 9), (34, 13)], P.yellow, 2)
        c.line([(21, 4), (16, 7), (20, 9), (14, 13)], P.yellow, 2)
        spark4(c, 24, 3, 3, P.yellow_l, False)
        spark4(c, 2, 25, 3, P.yellow_l, False); c.px(3, 25, P.orange)
        spark4(c, 46, 25, 3, P.yellow_l, False); c.px(45, 25, P.orange)
        c.px(36, 15, P.yellow_l); c.px(12, 16, P.yellow_l)
    else:
        c.px(30, 8 - f, P.yellow_l); c.px(18, 9 + f, P.yellow_l)


TOWERS = {1: tower_1, 2: tower_2, 3: tower_3, 4: tower_4, 5: tower_5, 6: tower_6, 7: tower_7, 8: tower_8,
          9: tower_9, 10: tower_10, 11: tower_11, 12: tower_12, 13: tower_13, 14: tower_14, 15: tower_15,
          16: tower_16, 17: tower_17, 18: tower_18, 19: tower_19, 20: tower_20, 21: tower_21}

# --------------------------------------------------------------------------
# creeps  (walk left->right, so they face right; f: 0/1 walk frames)
# --------------------------------------------------------------------------
def legs4(c, xs, top, bottom, col, f, w=1):
    for i, x in enumerate(xs):
        dx = 1 if (i % 2 == 0) == (f == 0) else -1
        c.rect(x - w + dx, top, x + w + dx, bottom, col)


def creep_1(c, f):
    c.oy = -1 if f else 0
    c.oline([(11, 30), (6, 24), (5, 17)], P.grey, 3, P.grey_d)
    c.px(5, 16, P.grey_l)
    legs4(c, (14, 19, 28, 33), 34, 43, P.grey, f)
    blob(c, 23, 31, 13, 7, P.grey)
    blob(c, 27, 33, 6, 4, P.grey_l, False)
    ear_tri(c, (29, 19), (35, 18), (31, 10), P.grey, P.pink)
    ear_tri(c, (36, 18), (42, 19), (40, 10), P.grey, P.pink)
    blob(c, 35, 23, 8, 7, P.grey)
    blob(c, 41, 27, 4, 3, P.grey_l)
    c.rect(43, 26, 44, 27, P.ink, False)
    eyes(c, 35, 22, 2)
    blush(c, 35, 24, 4)
    c.oy = 0


def creep_2(c, f):
    c.oy = -1 if f else 0
    feet(c, 24, 45, 4, P.green_d, f)
    blob(c, 24, 36, 8, 7, P.green_d)
    c.rect(18, 38, 30, 42, P.bark, dark(P.bark))
    c.oline([(31, 33), (41, 41)], P.wood, 3, P.bark)
    blob(c, 42, 42, 3, 3, P.bark)
    ear_tri(c, (14, 18), (14, 26), (4, 15), P.green, P.pink)
    ear_tri(c, (34, 18), (34, 26), (44, 15), P.green, P.pink)
    blob(c, 24, 22, 10, 9, P.green)
    eyes(c, 26, 22, 3)
    blush(c, 26, 24, 5)
    mouth(c, 27, 27, "grin")
    c.px(29, 28, P.white)
    c.oy = 0


def creep_3(c, f):
    c.oy = -1 if f else 0
    if f == 0:
        c.poly([(17, 21), (4, 6), (5, 19), (9, 23), (15, 25)], P.purple_d)
        c.poly([(31, 21), (44, 6), (43, 19), (39, 23), (33, 25)], P.purple_d)
    else:
        c.poly([(17, 20), (3, 30), (7, 29), (11, 32), (15, 26)], P.purple_d)
        c.poly([(31, 20), (45, 30), (41, 29), (37, 32), (33, 26)], P.purple_d)
    ear_tri(c, (18, 15), (24, 14), (18, 7), P.purple, P.pink)
    ear_tri(c, (24, 14), (30, 15), (30, 7), P.purple, P.pink)
    blob(c, 24, 21, 8, 8, P.purple)
    eyes(c, 26, 20, 3)
    blush(c, 26, 22, 5)
    c.px(24, 26, P.white); c.px(28, 26, P.white)
    c.line([(24, 25), (28, 25)], P.eye)
    c.oy = 0


def creep_4(c, f):
    c.oy = -1 if f else 0
    for i, x in enumerate((14, 34)):
        dx = 1 if (i == 0) == (f == 0) else -1
        blob(c, x + dx, 40, 3, 3, P.green)
    c.poly([(9, 34), (3, 30), (10, 30)], P.green)          # tail
    blob(c, 22, 30, 13, 10, P.green_d)
    c.rect(9, 36, 35, 38, P.moss, dark(P.moss))
    for (x, y) in ((22, 22), (15, 28), (29, 28), (22, 33)):
        c.poly([(x - 3, y), (x, y - 3), (x + 3, y), (x, y + 3)], P.green_d, dark(P.green_d, 0.6))
    for i, x in enumerate((20, 30)):
        dx = -1 if (i == 0) == (f == 0) else 1
        blob(c, x + dx, 41, 3, 3, P.green)
    blob(c, 38, 32, 6, 5, P.green)
    eyes(c, 39, 31, 2)
    blush(c, 39, 33, 4)
    c.oy = 0


def creep_5(c, f):
    c.oy = -1 if f else 0
    feet(c, 24, 44, 8, P.brown, f, 4, 2)
    c.oline([(38, 34), (43, 20)], P.wood, 3, P.bark)
    blob(c, 44, 16, 4, 5, P.bark, dark(P.bark))
    c.px(41, 12, P.steel_l); c.px(47, 13, P.steel_l); c.px(46, 20, P.steel_l)
    blob(c, 24, 28, 17, 15, P.brown)
    blob(c, 24, 34, 10, 7, P.tan, False)
    c.rect(14, 38, 34, 43, P.bark, dark(P.bark))
    ear_tri(c, (8, 22), (9, 28), (3, 24), P.brown)
    ear_tri(c, (40, 22), (39, 28), (45, 24), P.brown)
    c.px(22, 12, P.bark); c.px(24, 11, P.bark); c.px(26, 12, P.bark)
    eyes(c, 26, 22, 5, style="angry")
    blush(c, 26, 24, 8)
    c.line([(20, 29), (32, 29)], P.eye)
    c.rect(29, 26, 31, 29, P.white, dark(P.cream))
    c.oy = 0


def creep_6(c, f):
    c.oy = -1 if f else 0
    if f == 0:
        c.poly([(22, 26), (8, 4), (3, 12), (12, 28)], P.tan_d)
        c.poly([(24, 24), (16, 8), (12, 30)], P.tan)
    else:
        c.poly([(22, 24), (4, 30), (8, 37), (18, 32)], P.tan_d)
    c.oline([(11, 32), (4, 38)], P.tan, 2)
    c.px(3, 39, P.bark); c.px(4, 39, P.bark)
    legs4(c, (15, 21, 30, 36), 36, 44, P.tan, f, w=2)
    blob(c, 24, 32, 12, 7, P.tan)
    blob(c, 31, 22, 9, 8, P.tan_d)                      # mane / head feathers
    for (x, y) in ((24, 16), (22, 22), (24, 28)):
        c.poly([(x + 3, y - 2), (x - 2, y), (x + 3, y + 2)], P.tan_d, dark(P.tan_d))
    blob(c, 35, 22, 8, 7, P.cream)
    c.poly([(41, 21), (47, 24), (41, 26)], P.yellow, P.brass_d)
    eyes(c, 36, 21, 2)
    blush(c, 36, 23, 4)
    c.oy = 0


def creep_7(c, f):
    c.oy = -1 if f else 0
    feet(c, 24, 45, 4, P.navy, f)
    robe(c, 24, 18, 44, 11, P.navy, dark(P.navy))
    c.rect(18, 24, 32, 27, P.red_d, False)                     # scarf
    c.oline([(32, 32), (43, 25)], P.steel_l, 2, P.steel_d)
    c.rect(32, 31, 35, 34, P.bark, dark(P.bark))
    blob(c, 24, 15, 10, 9, P.navy)
    blob(c, 26, 18, 6, 5, P.skin_d)
    c.rect(19, 12, 33, 18, P.navy, False)                      # hood shadow
    c.poly([(19, 18), (33, 18), (31, 20), (21, 20)], (54, 48, 70), False)
    c.rect(28, 17, 30, 18, P.white, False)                      # one glinting eye
    c.px(28, 17, P.red)
    mouth(c, 27, 22, "flat")
    c.oy = 0


def creep_8(c, f):
    c.oy = -1 if f else 0
    feet(c, 24, 45, 4, P.purple_d, f)
    c.line([(38, 44), (38, 12)], P.bark, 2)
    robe(c, 24, 14, 44, 13, P.purple_d, P.purple_l)
    c.rect(20, 30, 28, 31, P.brass, False)
    blob(c, 36, 30, 2, 2, P.skin_d)
    blob(c, 24, 17, 10, 9, P.purple_d)
    blob(c, 26, 20, 6, 5, P.skin_d)
    c.rect(18, 12, 32, 17, P.purple_d, False)
    eyes(c, 27, 20, 2, size=3)
    mouth(c, 27, 24, "flat")
    skull(c, 38, 8, 4)
    c.oy = 0


def creep_9(c, f):
    c.oy = -1 if f else 0
    if f == 0:
        c.poly([(26, 30), (12, 6), (4, 20), (10, 32), (20, 36)], P.red_d)
        c.line([(24, 30), (12, 8)], dark(P.red_d)); c.line([(24, 31), (6, 20)], dark(P.red_d))
    else:
        c.poly([(26, 30), (10, 38), (6, 50), (18, 46), (24, 40)], P.red_d)
        c.line([(24, 32), (10, 38)], dark(P.red_d)); c.line([(24, 34), (8, 48)], dark(P.red_d))
    c.oline([(16, 44), (6, 50), (2, 58)], P.red, 4)
    c.poly([(1, 55), (6, 58), (1, 62)], P.red_d)
    for i, x in enumerate((22, 38)):
        dx = 1 if (i == 0) == (f == 0) else -1
        blob(c, x + dx, 56, 5, 4, P.red)
        c.px(x + dx - 3, 59, P.cream_l); c.px(x + dx, 59, P.cream_l); c.px(x + dx + 3, 59, P.cream_l)
    blob(c, 30, 42, 16, 12, P.red)
    blob(c, 31, 46, 10, 7, P.cream, False)
    c.line([(24, 44), (38, 44)], P.tan_d); c.line([(23, 48), (39, 48)], P.tan_d)
    c.poly([(40, 18), (38, 8), (45, 17)], P.cream, P.tan_d)
    c.poly([(48, 17), (54, 8), (51, 18)], P.cream, P.tan_d)
    for i in range(3):
        c.poly([(34 + i * 5, 17), (36 + i * 5, 13), (38 + i * 5, 17)], P.red_d)
    blob(c, 44, 26, 12, 10, P.red)
    blob(c, 52, 30, 7, 4, P.red)
    c.px(56, 29, P.red_d); c.px(58, 30, P.red_d)
    eyes(c, 45, 25, 3, size=3)
    blush(c, 45, 27, 6)
    c.px(47, 31, P.eye); c.px(48, 32, P.eye); c.px(49, 31, P.eye)
    c.oy = 0


def creep_10(c, f):
    c.oy = -1 if f else 0
    for i, x in enumerate((20, 42)):
        dx = 2 if (i == 0) == (f == 0) else -2
        c.rrect(x - 6 + dx, 48, x + 6 + dx, 60, 3, P.stone_d)
    c.rrect(2, 22, 13, 48, 4, P.stone_d)                     # left arm
    c.rrect(52, 22, 62, 48, 4, P.stone_d)                    # right arm
    c.rrect(11, 18, 53, 52, 8, P.stone)
    c.rrect(21, 5, 45, 24, 5, P.stone)
    c.line([(18, 30), (22, 36), (19, 42)], P.stone_d)
    c.line([(44, 34), (40, 40)], P.stone_d)
    c.line([(26, 8), (24, 12)], P.stone_d)
    for (x, y, rx, ry) in ((15, 25, 5, 3), (44, 46, 6, 3), (36, 6, 6, 2), (56, 24, 4, 2), (30, 51, 5, 2)):
        blob(c, x, y, rx, ry, P.moss, dark(P.moss, 0.6))
    for (x, y) in ((29, 15), (39, 15)):
        blob(c, x, y, 3, 3, (120, 200, 255, 130), False)
        c.rect(x - 1, y - 1, x + 1, y + 1, P.cyan, False)
        c.px(x, y, P.white)
    c.line([(31, 21), (39, 21)], P.stone_d)
    c.oy = 0


def creep_11(c, f):
    c.oy = -1 if f else 0
    dk = (48, 40, 66)
    feet(c, 23, 45, 4, dk, f)
    robe(c, 23, 20, 44, 11, dk, P.purple)
    c.rect(18, 31, 28, 32, P.purple, False)
    blob(c, 23, 21, 8, 7, P.skin_d)
    c.oline([(30, 32), (36, 30)], dk, 3)
    blob(c, 36, 30, 2, 2, P.skin_d)
    blob(c, 39, 25, 6, 6, (170, 110, 240, 110), False)
    blob(c, 39, 25, 4, 4, P.purple_d, dark(P.purple_d))
    c.px(38, 23, P.purple_l); c.px(40, 26, P.purple_l)
    c.poly([(12, 19), (34, 19), (21, 1)], dk)
    c.ell(9, 16, 37, 21, dk)
    c.rect(15, 17, 31, 18, P.purple, False)
    eyes(c, 25, 24, 3)
    mouth(c, 25, 28, "flat")
    c.oy = 0


# ---- legend bosses (64x64) -------------------------------------------------
def creep_12(c, f):
    """맹수 왕 - big golden-maned lion with a small crown, walking."""
    c.oy = -1 if f else 0
    fur, fur_l, fur_d = (240, 196, 110), (252, 226, 160), (196, 140, 62)
    mane, mane_d = (206, 118, 52), (150, 78, 38)
    # tail (behind body) with a tuft
    c.oline([(14, 40), (7, 32), (6, 22)], fur, 3, fur_d)
    blob(c, 6, 20, 4, 4, mane, mane_d)
    # legs: 4, alternate
    legs4(c, (18, 25, 36, 43), 46, 58, fur, f, w=2)
    for i, x in enumerate((18, 25, 36, 43)):
        dx = 1 if (i % 2 == 0) == (f == 0) else -1
        c.rect(x - 3 + dx, 57, x + 3 + dx, 60, fur_d, dark(fur_d))
    # body
    blob(c, 30, 42, 17, 10, fur)
    blob(c, 34, 46, 10, 5, fur_l, False)
    # mane: chunky spiky ring around the head
    for ang in range(0, 360, 30):
        a = math.radians(ang)
        tx = round(42 + 17 * math.cos(a)); ty = round(27 + 16 * math.sin(a))
        bx = round(42 + 10 * math.cos(a)); by = round(27 + 9 * math.sin(a))
        px_ = -math.sin(a); py_ = math.cos(a)
        c.poly([(round(bx - 4 * px_), round(by - 4 * py_)), (tx, ty), (round(bx + 4 * px_), round(by + 4 * py_))],
               mane, mane_d)
    blob(c, 42, 27, 13, 12, mane, mane_d)
    blob(c, 40, 25, 8, 7, (222, 136, 66), False)
    # head + muzzle
    blob(c, 45, 28, 9, 8, fur)
    blob(c, 51, 32, 6, 4, fur_l)
    c.rect(54, 30, 56, 31, P.ink, False)                    # nose
    c.line([(49, 34), (54, 34)], P.eye)                      # mouth
    c.rect(49, 35, 49, 36, P.white, False); c.rect(53, 35, 53, 36, P.white, False)   # fangs
    c.px(49, 37, fur_d); c.px(53, 37, fur_d)
    eyes(c, 45, 27, 3, style="angry")
    blush(c, 45, 29, 6)
    # small crown
    c.rect(41, 14, 49, 17, P.brass, P.brass_d)
    c.poly([(41, 14), (43, 10), (45, 14)], P.brass, P.brass_d)
    c.poly([(45, 14), (47, 10), (49, 14)], P.brass, P.brass_d)
    c.px(45, 15, P.red); c.px(42, 15, P.cyan); c.px(48, 15, P.cyan)
    c.oy = 0


def creep_13(c, f):
    """리치 - floating skeletal sorcerer, bone crown, green skull staff, tattered hem."""
    c.oy = -2 if f else 0
    robe, robe_d, trim = (46, 92, 104), (28, 60, 72), (118, 82, 168)
    glow, glow_d = (120, 240, 120), (60, 170, 80)
    # mist under the hem (hover)
    blob(c, 28, 57 - (c.oy), 12, 3, (120, 200, 140, 90), False)
    # staff (behind robe)
    c.line([(46, 58), (46, 14)], P.bark, 2)
    # robe with tattered hem
    hem = [(28, 20), (14, 44), (13, 56), (16, 51), (19, 57), (22, 50), (25, 58), (28, 51), (31, 57),
           (34, 50), (37, 58), (40, 52), (43, 56), (42, 44)]
    if f:
        hem = [(x, y - (1 if i % 2 else 0)) for i, (x, y) in enumerate(hem)]
    c.poly(hem, robe, robe_d)
    c.line([(18, 44), (38, 44)], trim)
    c.rect(24, 34, 32, 35, trim, False)                     # sash
    # arm + bony hand on staff
    c.oline([(35, 34), (44, 30)], robe, 3, robe_d)
    blob(c, 45, 30, 2, 2, P.cream_l, P.cream_d)
    # hood shape then skull face
    blob(c, 28, 18, 11, 10, robe)
    skull(c, 28, 20, 7, P.cream_l, P.eye)
    for (x, y) in ((25, 20), (31, 20)):
        blob(c, x, y, 2, 2, (120, 255, 120, 130), False)
        c.rect(x - 1, y - 1, x + 1, y + 1, glow, False)
        c.px(x, y, P.white)
    # bone crown
    c.rect(20, 11, 36, 13, P.cream, P.cream_d)
    for x in (21, 26, 31, 35):
        c.poly([(x - 1, 11), (x, 6), (x + 1, 11)], P.cream, P.cream_d)
    c.px(28, 12, glow)
    # green skull orb on the staff
    blob(c, 46, 10, 7, 7, (120, 255, 120, 90), False)
    skull(c, 46, 10, 4, glow, P.ink)
    c.px(43, 7, P.white)
    c.oy = 0


def creep_14(c, f):
    """고대 드래곤 - larger bronze dragon, broad wings, horns, back spikes, amber eyes."""
    c.oy = -1 if f else 0
    body, body_d, belly = (150, 102, 52), (98, 62, 32), (226, 196, 128)
    wing, wing_d = (122, 74, 40), (86, 48, 28)
    amber = (255, 196, 70)
    # wings (behind body)
    if f == 0:
        c.poly([(30, 30), (14, 1), (2, 8), (1, 22), (10, 32), (24, 36)], wing, wing_d)
        c.line([(26, 32), (13, 3)], wing_d); c.line([(26, 33), (3, 10)], wing_d); c.line([(24, 34), (2, 21)], wing_d)
        c.poly([(32, 28), (26, 6), (36, 12), (38, 22)], wing, wing_d)
        c.line([(34, 26), (27, 8)], wing_d)
    else:
        c.poly([(30, 34), (4, 30), (0, 40), (10, 46), (22, 46)], wing, wing_d)
        c.line([(26, 36), (5, 31)], wing_d); c.line([(26, 38), (2, 39)], wing_d); c.line([(24, 40), (10, 45)], wing_d)
        c.poly([(32, 30), (34, 40), (26, 44)], wing, wing_d)
    # tail with spade tip
    c.oline([(18, 50), (8, 54), (3, 61)], body, 4, body_d)
    c.poly([(0, 58), (7, 60), (2, 63)], body_d)
    # legs
    for i, x in enumerate((24, 40)):
        dx = 1 if (i == 0) == (f == 0) else -1
        blob(c, x + dx, 57, 5, 4, body)
        for k in (-3, 0, 3):
            c.px(x + dx + k, 60, P.cream_l)
    # body + belly plates
    blob(c, 32, 44, 17, 12, body)
    blob(c, 33, 48, 11, 7, belly, False)
    for y in (45, 49, 53):
        c.line([(24, y), (42, y)], P.tan_d)
    # back spikes
    for i in range(4):
        x = 20 + i * 6
        c.poly([(x, 34 - i), (x + 2, 28 - i), (x + 4, 34 - i)], body_d, dark(body_d))
    # neck + head
    blob(c, 44, 30, 8, 8, body)
    blob(c, 47, 24, 12, 10, body)
    blob(c, 56, 28, 7, 4, body)
    c.px(60, 27, body_d); c.px(62, 28, body_d)
    # horns + a small crest spike between them
    c.poly([(41, 17), (37, 3), (46, 15)], P.cream, P.tan_d)
    c.poly([(50, 15), (57, 3), (55, 17)], P.cream, P.tan_d)
    c.poly([(46, 16), (48, 11), (50, 16)], body_d, dark(body_d))
    # amber glowing eyes
    for (x, y) in ((45, 23), (51, 23)):
        blob(c, x, y, 3, 3, (255, 200, 80, 100), False)
        c.rect(x - 1, y - 1, x + 1, y + 1, amber, False)
        c.px(x, y - 1, P.white)
    c.line([(50, 30), (55, 31)], P.eye)
    c.px(52, 32, P.white); c.px(55, 32, P.white)
    # smoke wisp from the nostril
    c.px(61, 24, P.grey_l); c.px(62, 22, P.grey_l)
    c.oy = 0


def creep_15(c, f):
    """타이탄 - huge stone-and-steel giant, blue rune lines, one huge fist, slit visor."""
    c.oy = -1 if f else 0
    stone, stone_d = P.stone, P.stone_d
    steel, steel_d = P.steel, P.steel_d
    rune = P.cyan
    # legs, alternate (heavy stomp)
    for i, x in enumerate((20, 40)):
        dx = 2 if (i == 0) == (f == 0) else -2
        c.rrect(x - 7 + dx, 44, x + 7 + dx, 61, 3, stone_d)
        c.rect(x - 7 + dx, 57, x + 7 + dx, 61, steel_d, dark(steel_d))
        c.line([(x + dx, 47), (x + dx, 54)], rune)
    # small left arm (back)
    c.rrect(2, 22, 11, 44, 4, stone_d)
    c.rect(3, 40, 10, 45, steel_d, dark(steel_d))
    # torso
    c.rrect(10, 16, 48, 50, 8, stone)
    c.rrect(14, 18, 44, 24, 3, steel, steel_d)                 # shoulder plate
    c.rect(12, 32, 46, 34, steel_d, False)                     # belt
    # rune lines on torso
    c.line([(18, 26), (22, 30), (18, 36), (22, 42)], rune)
    c.line([(40, 26), (36, 32), (40, 38)], rune)
    c.rect(28, 38, 30, 44, rune, False)
    c.px(29, 41, P.white)
    # huge right arm + fist (front)
    c.rrect(44, 18, 62, 40, 6, stone)
    c.rrect(46, 20, 60, 28, 4, steel, steel_d)                 # pauldron
    c.line([(52, 28), (52, 38)], rune)
    c.rrect(46, 38, 63, 54, 5, stone_d)                        # fist
    c.rect(48, 40, 61, 42, steel_d, False)                     # knuckle plate
    for kx in (50, 54, 58):
        c.rect(kx - 1, 44, kx + 1, 46, dark(stone_d), False)
    # small head with slit visor
    c.rrect(22, 6, 38, 18, 4, steel, steel_d)
    c.rect(24, 4, 36, 6, steel_d, False)
    c.rect(24, 11, 36, 13, (30, 40, 60), False)
    c.line([(26, 12), (34, 12)], rune)
    c.px(27, 12, P.white); c.px(33, 12, P.white)
    c.oy = 0


CREEPS = {1: creep_1, 2: creep_2, 3: creep_3, 4: creep_4, 5: creep_5, 6: creep_6, 7: creep_7, 8: creep_8,
          9: creep_9, 10: creep_10, 11: creep_11,
          12: creep_12, 13: creep_13, 14: creep_14, 15: creep_15}
CREEP_SIZE = {9: 64, 10: 64, 12: 64, 13: 64, 14: 64, 15: 64}

# --------------------------------------------------------------------------
# world tiles
# --------------------------------------------------------------------------
def tile_grass(c, variant):
    rng = random.Random(7 + variant)
    base = (140, 206, 120)
    c.rect(0, 0, 31, 31, base, False)
    for _ in range(14):
        x, y = rng.randrange(1, 30), rng.randrange(1, 30)
        c.px(x, y, (124, 190, 106)); c.px(x + 1, y - 1, (124, 190, 106)); c.px(x - 1, y - 1, (124, 190, 106))
    for _ in range(8):
        x, y = rng.randrange(0, 32), rng.randrange(0, 32)
        c.px(x, y, (156, 218, 134))
    if variant == 1:
        for _ in range(10):
            c.px(rng.randrange(0, 32), rng.randrange(0, 32), (176, 230, 150))
        for (x, y, col) in ((7, 9, P.pink_l), (22, 20, P.white), (25, 6, P.yellow_l)):
            c.px(x - 1, y, col); c.px(x + 1, y, col); c.px(x, y - 1, col); c.px(x, y + 1, col)
            c.px(x, y, P.yellow)


def tile_path(c):
    rng = random.Random(11)
    c.rect(0, 0, 31, 31, (226, 200, 146), False)
    for _ in range(30):
        c.px(rng.randrange(0, 32), rng.randrange(0, 32), (212, 184, 128))
    for _ in range(12):
        c.px(rng.randrange(0, 32), rng.randrange(0, 32), (238, 216, 168))
    for (x, y) in ((5, 6), (21, 10), (12, 23), (26, 26)):
        c.ell(x, y, x + 3, y + 2, P.grey_l, P.grey_d)


def tile_slot(c, hover):
    c.rect(0, 0, 31, 31, (120, 108, 100), False)
    c.rect(1, 1, 30, 30, P.stone_d, False)
    c.rect(1, 1, 30, 2, P.stone_l, False); c.rect(1, 1, 2, 30, P.stone_l, False)
    c.rect(3, 3, 28, 28, (176, 168, 160), False)
    c.rect(4, 4, 27, 27, (196, 188, 178), False)
    c.rect(4, 4, 27, 4, P.stone_l, False); c.rect(4, 4, 4, 27, P.stone_l, False)
    c.rect(27, 4, 27, 27, (150, 140, 130), False); c.rect(4, 27, 27, 27, (150, 140, 130), False)
    for (x, y) in ((9, 10), (20, 18), (14, 22)):
        c.px(x, y, (166, 158, 148))
    if hover:
        c.rect(0, 0, 31, 31, None, P.yellow)
        c.rect(1, 1, 30, 30, None, P.yellow)
        c.rect(2, 2, 29, 29, None, P.yellow_l)


def base_castle(c):
    for x in (6, 44):
        c.rect(x, 20, x + 14, 62, P.stone, P.stone_d)
        for i in range(4):
            c.rect(x + i * 4, 16, x + i * 4 + 2, 20, P.stone, P.stone_d)
        c.rect(x + 5, 30, x + 9, 36, P.navy, dark(P.navy))
        c.line([(x + 1, 44), (x + 13, 44)], P.stone_d)
        c.line([(x + 1, 52), (x + 13, 52)], P.stone_d)
    c.rect(18, 32, 46, 62, P.stone, P.stone_d)
    for i in range(4):
        c.rect(20 + i * 7, 28, 23 + i * 7, 32, P.stone, P.stone_d)
    for y in (40, 48, 56):
        c.line([(19, y), (45, y)], P.stone_d)
    for (x, y) in ((24, 44), (34, 52), (40, 44)):
        c.line([(x, y - 3), (x, y - 1)], P.stone_d)
    c.rect(26, 46, 38, 62, P.bark, dark(P.bark))
    c.ell(26, 40, 38, 52, P.bark, dark(P.bark))
    c.rect(28, 46, 36, 62, (74, 50, 36), False)
    c.ell(28, 42, 36, 50, (74, 50, 36), False)
    c.line([(32, 46), (32, 61)], dark(P.bark))
    c.line([(13, 3), (13, 16)], P.bark)
    c.poly([(14, 3), (24, 6), (14, 9)], P.red)
    c.rect(4, 62, 60, 63, P.stone_d, False)


def gate_portal(c):
    c.rect(2, 20, 29, 63, P.stone_d, dark(P.stone_d))
    c.ell(2, 8, 29, 34, P.stone_d, dark(P.stone_d))
    c.rect(3, 21, 28, 62, P.stone_d, False)
    for y in range(24, 62, 6):
        c.line([(3, y), (6, y)], dark(P.stone_d)); c.line([(25, y), (28, y)], dark(P.stone_d))
    c.rect(7, 26, 24, 62, (60, 40, 92), (36, 24, 56))
    c.ell(7, 14, 24, 38, (60, 40, 92), (36, 24, 56))
    c.rect(8, 27, 23, 61, (60, 40, 92), False)
    c.arc(9, 22, 22, 50, 0, 300, P.purple_l, 2)
    c.arc(12, 27, 19, 45, 90, 360, P.purple, 2)
    c.ell(14, 33, 17, 38, P.white, False)
    c.rect(0, 62, 31, 63, P.stone_d, False)
    c.poly([(15, 8), (12, 12), (18, 12)], P.stone_d)


def flag(c, col):
    c.line([(3, 2), (3, 23)], P.bark, 2)
    c.poly([(5, 3), (14, 7), (5, 11)], col)
    c.px(2, 1, P.brass)


# --------------------------------------------------------------------------
# projectiles (16x16, pointing right)
# --------------------------------------------------------------------------
def proj_arrow(c, fire=False):
    if fire:
        flame_h(c, 0, 8, 6, 5, 0)
    c.line([(3, 8), (12, 8)], P.wood)
    c.poly([(11, 5), (15, 8), (11, 11)], P.steel_l)
    c.line([(2, 6), (4, 8)], P.red); c.line([(2, 10), (4, 8)], P.red)


def proj_fireball(c):
    c.poly([(9, 4), (0, 8), (9, 12)], P.orange, P.red_d)
    c.poly([(9, 6), (4, 8), (9, 10)], P.yellow, False)
    c.ell(7, 3, 15, 11, P.orange, P.red_d)
    c.ell(9, 5, 13, 9, P.yellow, False)
    c.px(10, 6, P.white)


def proj_bullet(c):
    c.rect(2, 5, 10, 11, P.brass, P.brass_d)
    c.ell(8, 5, 14, 11, P.brass, P.brass_d)
    c.rect(3, 6, 9, 10, P.brass, False)
    c.line([(4, 6), (10, 6)], P.yellow_l)


def proj_cannon(c):
    c.ell(2, 2, 13, 13, (58, 56, 70), (30, 28, 40))
    c.rect(5, 5, 6, 6, P.grey_l, False)
    c.px(7, 5, P.grey)


def proj_leaf(c):
    c.poly([(1, 10), (5, 4), (11, 2), (15, 5), (10, 10), (5, 13)], P.green, P.green_d)
    c.line([(3, 10), (13, 4)], P.green_d)


def proj_bolt(c):
    pts = [(1, 9), (6, 4), (6, 9), (11, 5), (15, 8)]
    c.line(pts, dark(P.yellow, 0.6), 4)
    c.line(pts, P.yellow, 2)


def proj_spark(c):
    c.poly([(8, 1), (10, 6), (15, 8), (10, 10), (8, 15), (6, 10), (1, 8), (6, 6)], P.white, P.cyan)
    c.rect(7, 7, 8, 8, P.cyan, False)


def proj_thorn(c):
    c.poly([(1, 5), (14, 8), (1, 11), (4, 8)], P.wood, P.bark)
    c.line([(4, 8), (12, 8)], P.wood_l)


def proj_rock(c):
    c.poly([(3, 6), (7, 2), (12, 3), (14, 8), (11, 13), (5, 13), (2, 10)], P.grey, P.grey_d)
    c.line([(6, 5), (9, 4)], P.grey_l)
    c.line([(8, 9), (11, 11)], P.grey_d)


PROJ = {"proj_arrow": lambda c: proj_arrow(c), "proj_fire_arrow": lambda c: proj_arrow(c, True),
        "proj_fireball": proj_fireball, "proj_bullet": proj_bullet, "proj_cannon": proj_cannon,
        "proj_leaf": proj_leaf, "proj_bolt": proj_bolt, "proj_spark": proj_spark, "proj_thorn": proj_thorn,
        "proj_rock": proj_rock}


# --------------------------------------------------------------------------
# effects
# --------------------------------------------------------------------------
def fx_hit(c, i):
    if i == 0:
        spark4(c, 12, 12, 5, P.yellow, dark(P.yellow, 0.7))
        c.rect(11, 11, 12, 12, P.white, False)
    elif i == 1:
        spark4(c, 12, 12, 11, P.yellow, dark(P.yellow, 0.7))
        spark4(c, 12, 12, 6, P.yellow_l, False)
        c.rect(11, 11, 13, 13, P.white, False)
        for (x, y) in ((4, 4), (20, 4), (4, 20), (20, 20)):
            c.px(x, y, P.yellow_l)
    else:
        for (x, y) in ((12, 1), (23, 12), (12, 23), (1, 12), (4, 4), (20, 4), (4, 20), (20, 20)):
            c.rect(x - 1, y - 1, x, y, (255, 240, 170, 170), False)
        spark4(c, 12, 12, 4, (255, 244, 180, 160), False)


def fx_boom(c, i):
    if i == 0:
        blob(c, 16, 16, 6, 6, P.orange, P.red_d)
        blob(c, 16, 16, 3, 3, P.yellow_l, False)
    elif i == 1:
        blob(c, 16, 16, 12, 12, P.orange, P.red_d)
        blob(c, 16, 16, 8, 8, P.yellow, False)
        blob(c, 16, 16, 4, 4, P.white, False)
        for (x, y) in ((4, 4), (28, 6), (26, 27), (5, 26)):
            blob(c, x, y, 2, 2, P.orange, P.red_d)
    elif i == 2:
        blob(c, 16, 16, 15, 15, P.orange, P.red_d)
        blob(c, 16, 16, 11, 11, P.yellow, False)
        blob(c, 16, 16, 7, 7, (255, 236, 170, 200), False)
        for (x, y) in ((5, 8), (27, 9), (9, 26), (24, 25)):
            blob(c, x, y, 4, 4, P.grey_l, P.grey_d)
    else:
        for (x, y, r) in ((9, 18, 6), (22, 17, 7), (15, 9, 5), (16, 23, 5)):
            blob(c, x, y, r, r, (200, 200, 208, 190), (130, 130, 140, 190))
        for (x, y) in ((6, 6), (26, 5), (28, 27)):
            c.px(x, y, (255, 200, 120, 150))


def fx_smoke(c, i):
    a = 220 - i * 50
    grey = (215, 215, 222, a); ol = (150, 150, 160, a)
    if i == 0:
        blob(c, 9, 17, 4, 4, grey, ol); blob(c, 15, 16, 5, 5, grey, ol)
    elif i == 1:
        blob(c, 7, 13, 5, 5, grey, ol); blob(c, 16, 12, 6, 6, grey, ol); blob(c, 11, 18, 4, 4, grey, ol)
    else:
        blob(c, 6, 8, 5, 5, grey, ol); blob(c, 16, 6, 6, 6, grey, ol); blob(c, 12, 14, 5, 5, grey, ol)


def fx_star(c, i):
    if i == 0:
        star(c, 16, 17, 6, 2.5, P.yellow, P.brass_d)
    elif i == 1:
        star(c, 16, 17, 14, 6, P.yellow, P.brass_d)
        star(c, 16, 17, 7, 3, P.yellow_l, False)
    elif i == 2:
        star(c, 16, 17, 12, 5, P.yellow, P.brass_d)
        for (x, y) in ((4, 5), (28, 4), (3, 27), (28, 28)):
            spark4(c, x, y, 3, P.yellow_l, False)
    else:
        for (x, y, r) in ((4, 4, 3), (28, 3, 2), (2, 26, 2), (29, 28, 3), (16, 16, 4)):
            spark4(c, x, y, r, (255, 240, 170, 170), False)


def fx_heal(c, i):
    def plus(x, y, r, a=255):
        col = (120, 230, 120, a)
        c.rect(x - r, y - 1, x + r, y + 1, col, False)
        c.rect(x - 1, y - r, x + 1, y + r, col, False)
        c.px(x, y, (220, 255, 220, a))
    if i == 0:
        plus(12, 17, 3)
    elif i == 1:
        plus(8, 14, 3); plus(16, 10, 4)
    else:
        plus(6, 9, 2, 180); plus(13, 4, 3, 180); plus(18, 13, 2, 180)


def fx_slow(c):
    col = (150, 215, 255)
    for k in range(3):
        a = math.radians(k * 60)
        dx, dy = round(7 * math.cos(a)), round(7 * math.sin(a))
        c.line([(8 - dx, 8 - dy), (8 + dx, 8 + dy)], col)
    for k in range(6):
        a = math.radians(k * 60)
        x, y = round(8 + 5 * math.cos(a)), round(8 + 5 * math.sin(a))
        c.px(x, y, P.white)
        c.px(round(8 + 7.5 * math.cos(a)), round(8 + 7.5 * math.sin(a)), P.white)
    c.rect(7, 7, 9, 9, P.white, False)


def fx_burn(c):
    flame(c, 8, 15, 8, 13, 0, core=P.white)


def fx_silence(c):
    def z(x, y, s, col):
        c.line([(x, y), (x + s, y)], col)
        c.line([(x + s, y), (x, y + s)], col)
        c.line([(x, y + s), (x + s, y + s)], col)
    z(1, 10, 4, P.grey_d); z(6, 5, 4, P.grey); z(11, 1, 3, P.grey_l)


def fx_stealth(c):
    c.poly([(1, 8), (5, 4), (11, 4), (15, 8), (11, 12), (5, 12)], (240, 240, 250, 150), (120, 120, 140, 170))
    c.ell(6, 6, 10, 10, (60, 60, 80, 170), False)
    c.px(7, 7, (255, 255, 255, 190))


def fx_shadow(c):
    c.ell(0, 0, 23, 11, (0, 0, 0, 50), False)
    c.ell(2, 2, 21, 9, (0, 0, 0, 90), False)


# --------------------------------------------------------------------------
# UI
# --------------------------------------------------------------------------
def ui_panel(c, dark_variant=False):
    if dark_variant:
        c.rrect(0, 0, 47, 47, 10, (120, 90, 40))
        c.rrect(1, 1, 46, 46, 9, P.brass, False)
        c.rrect(3, 3, 44, 44, 8, (36, 42, 72, 230), False)
        c.rrect(4, 4, 43, 43, 7, None, (250, 230, 170, 120))
    else:
        c.rrect(0, 0, 47, 47, 10, (104, 66, 36))
        c.rrect(1, 1, 46, 46, 9, (170, 118, 72), False)
        c.rrect(3, 3, 44, 44, 8, P.cream, False)
        c.rrect(4, 4, 43, 43, 7, None, P.cream_l)


def ui_button(c, col):
    c.rrect(0, 0, 47, 47, 10, dark(col, 0.55))
    c.rrect(1, 1, 46, 46, 9, dark(col, 0.8), False)
    c.rrect(1, 1, 46, 42, 9, col, False)
    c.rrect(3, 3, 44, 40, 7, None, light(col, 0.45))
    c.rect(8, 4, 39, 4, light(col, 0.7), False)


def ui_card(c, border, hero=False):
    c.rrect(0, 0, 95, 127, 8, dark(border, 0.55))
    c.rrect(1, 1, 94, 126, 7, border, False)
    c.rrect(4, 4, 91, 123, 5, P.cream, False)
    c.rrect(7, 7, 88, 76, 4, P.cream_l, P.cream_d)
    c.rrect(7, 80, 88, 120, 4, P.cream, P.cream_d)
    c.rect(10, 83, 60, 84, P.cream_d, False)
    c.rect(10, 90, 80, 91, (232, 216, 176), False)
    c.rect(10, 96, 70, 97, (232, 216, 176), False)
    if hero:
        for (x, y) in ((4, 4), (91, 4), (4, 123), (91, 123)):
            spark4(c, x, y, 3, P.yellow_l, False)


def ui_card_legend(c):
    """Same layout as ui_card_hero: deep purple border, thin gold inner line, corner gems."""
    border = (86, 48, 134)
    ui_card(c, border, False)
    c.rrect(3, 3, 92, 124, 6, None, P.brass)
    for (x, y) in ((5, 5), (90, 5), (5, 122), (90, 122)):
        c.poly([(x, y - 2), (x + 2, y), (x, y + 2), (x - 2, y)], P.cyan, (60, 110, 150))
        c.px(x, y - 1, P.white)


def ui_slot(c):
    c.rrect(0, 0, 39, 39, 6, (120, 90, 60))
    c.rrect(1, 1, 38, 38, 5, (214, 196, 160), False)
    c.rrect(2, 2, 37, 37, 4, None, (176, 154, 116))
    c.rect(4, 36, 35, 36, P.cream_l, False)
    c.rect(36, 4, 36, 35, P.cream_l, False)
    c.rrect(3, 3, 36, 36, 4, (206, 188, 152), False)
    c.rect(4, 3, 35, 3, (170, 148, 110), False)
    c.rect(3, 4, 3, 35, (170, 148, 110), False)


def icon(name, c):
    if name == "icon_coin":
        coin(c, 8, 8, 7)
        c.rect(7, 5, 8, 10, P.brass, False)
    elif name == "icon_heart":
        heart(c, 8, 7, 6)
    elif name == "icon_income":
        c.poly([(6, 0), (11, 5), (8, 5), (8, 9), (4, 9), (4, 5), (1, 5)], P.green, P.green_d)
        coin(c, 11, 11, 4)
    elif name == "icon_star":
        star(c, 8, 8, 7, 3, P.yellow, P.brass_d)
    elif name == "icon_clock":
        c.ell(1, 1, 14, 14, P.white, P.grey_d)
        c.ell(2, 2, 13, 13, P.white, P.grey_l)
        c.line([(8, 4), (8, 8), (11, 10)], P.eye)
        c.px(8, 2, P.grey_d); c.px(8, 13, P.grey_d); c.px(2, 8, P.grey_d); c.px(13, 8, P.grey_d)
    elif name == "icon_skull":
        skull(c, 8, 7, 6)
    elif name == "icon_wing":
        c.poly([(1, 12), (2, 6), (6, 2), (14, 2), (12, 5), (14, 7), (10, 8), (12, 11), (7, 11), (5, 14)],
               P.cream_l, P.grey_d)
        c.line([(3, 11), (10, 4)], P.grey_l)
    elif name == "icon_shield":
        c.poly([(2, 1), (13, 1), (13, 8), (8, 14), (3, 8)], P.blue, dark(P.blue))
        c.rect(7, 3, 8, 11, P.blue_l, False); c.rect(4, 5, 11, 6, P.blue_l, False)
    elif name == "icon_sword":
        c.oline([(4, 11), (13, 2)], P.steel_l, 2, P.steel_d)
        c.oline([(2, 9), (6, 13)], P.brass, 2, P.brass_d)
        c.oline([(3, 12), (1, 14)], P.bark, 2)
    elif name == "icon_range":
        c.ell(1, 1, 14, 14, None, P.red_d)
        c.ell(2, 2, 13, 13, None, P.red)
        c.line([(8, 0), (8, 4)], P.red_d); c.line([(8, 11), (8, 15)], P.red_d)
        c.line([(0, 8), (4, 8)], P.red_d); c.line([(11, 8), (15, 8)], P.red_d)
        c.rect(7, 7, 8, 8, P.red, False)
    elif name == "icon_speed":
        for dx in (0, 6):
            c.poly([(1 + dx, 2), (7 + dx, 8), (1 + dx, 14), (3 + dx, 8)], P.yellow, P.brass_d)
    elif name == "icon_leaf":
        c.poly([(1, 13), (3, 6), (9, 1), (15, 1), (14, 7), (9, 12), (4, 13)], P.green, P.green_d)
        c.line([(2, 13), (12, 3)], P.green_d)
    elif name == "icon_fire":
        flame(c, 8, 15, 9, 13, 0, core=P.white)
    elif name == "icon_gear":
        gear(c, 8, 8, 5, P.steel_l, 8, 0)
        c.ell(6, 6, 10, 10, P.steel_d, False)
        c.ell(7, 7, 9, 9, P.steel_l, False)
    elif name == "icon_lock":
        c.arc(3, 0, 12, 11, 180, 360, dark(P.steel_d), 4)
        c.arc(4, 1, 11, 10, 180, 360, P.steel_l, 2)
        c.rrect(2, 7, 13, 14, 2, P.brass, P.brass_d)
        c.rect(7, 9, 8, 11, P.brass_d, False)
    elif name == "icon_check":
        c.line([(2, 8), (6, 12), (14, 3)], P.green_d, 4)
        c.line([(2, 8), (6, 12), (14, 3)], P.green_l, 2)
    elif name == "icon_eye":
        c.poly([(1, 8), (5, 3), (11, 3), (15, 8), (11, 13), (5, 13)], P.white, P.eye)
        c.ell(5, 5, 11, 11, P.blue, dark(P.blue))
        c.rect(7, 7, 8, 8, P.eye, False)
        c.px(6, 6, P.white)
    elif name == "icon_merge":
        c.rect(1, 1, 5, 5, P.blue_l, P.blue)
        c.rect(1, 10, 5, 14, P.blue_l, P.blue)
        c.line([(7, 3), (8, 8), (7, 12)], P.grey_d)
        c.poly([(8, 5), (10, 8), (8, 11)], P.grey_d, False)
        c.rect(9, 3, 15, 12, P.yellow, P.brass_d)
        c.px(10, 4, P.yellow_l)


def aug(name, c):
    if name == "aug_resource":
        c.ell(4, 10, 27, 30, P.tan, P.tan_d)
        c.poly([(11, 11), (20, 11), (23, 4), (8, 4)], P.tan, P.tan_d)
        c.rect(9, 9, 22, 11, P.bark, dark(P.bark))
        c.px(6, 14, P.cream_l); c.px(7, 13, P.cream_l)
        coin(c, 16, 21, 5)
        c.rect(15, 18, 16, 24, P.brass, False)
    elif name == "aug_power":
        c.poly([(16, 2), (29, 15), (22, 15), (22, 29), (10, 29), (10, 15), (3, 15)], P.orange, P.orange_d)
        c.poly([(16, 6), (25, 15), (20, 15), (20, 26), (12, 26), (12, 15), (7, 15)], P.yellow, False)
        c.rect(14, 8, 15, 14, P.yellow_l, False)
        spark4(c, 27, 5, 3, P.yellow_l, False)
        spark4(c, 4, 26, 2, P.yellow_l, False)
    elif name == "aug_hinder":
        skull(c, 16, 13, 11, P.purple_l, P.eye)
        c.px(9, 6, P.white); c.px(10, 5, P.white)
    elif name == "aug_info":
        c.ell(3, 3, 22, 22, P.blue_l, P.steel_d)
        c.ell(5, 5, 20, 20, P.white, P.blue_l)
        c.ell(6, 6, 19, 19, (200, 235, 255), False)
        c.px(8, 9, P.white); c.px(9, 8, P.white); c.px(8, 8, P.white)
        c.oline([(20, 20), (28, 28)], P.bark, 4, dark(P.bark))
        c.poly([(12, 9), (13, 15), (16, 15), (12, 9)], P.blue, False)
        c.ell(11, 9, 14, 12, P.blue, False)
        c.rect(12, 13, 13, 16, P.blue, False)


def stars_badge(c, n):
    xs = {1: (12,), 2: (7, 17), 3: (4, 12, 20)}[n]
    for x in xs:
        star(c, x, 5, 4, 1.8, P.yellow, P.brass_d)


# --------------------------------------------------------------------------
# title screen
# --------------------------------------------------------------------------
def load_font(paths, size):
    for path, idx in paths:
        try:
            f = ImageFont.truetype(path, size, index=idx)
            probe = Image.new("L", (size * 3, size * 3), 0)
            ImageDraw.Draw(probe).text((10, 10), "쪼", font=f, fill=255)
            if probe.getbbox():
                return f
        except Exception:
            continue
    return None


KR_FONTS = [("/System/Library/Fonts/AppleSDGothicNeo.ttc", 6), ("/System/Library/Fonts/Supplemental/AppleGothic.ttf", 0),
            ("/Library/Fonts/NanumGothicBold.ttf", 0), ("/System/Library/Fonts/Supplemental/Arial Unicode.ttf", 0)]
LAT_FONTS = [("/System/Library/Fonts/Supplemental/Arial Rounded Bold.ttf", 0),
             ("/System/Library/Fonts/Supplemental/Arial Bold.ttf", 0), ("/System/Library/Fonts/Helvetica.ttc", 1)]


def logo(c):
    text = "쪼꼬미 공성전"
    size = 104
    font = load_font(KR_FONTS, size)
    while font is not None and font.getlength(text) > 560:
        size -= 4
        font = load_font(KR_FONTS, size)
    if font is None:
        font = ImageFont.load_default()
    brown = (104, 62, 32)
    shadow_col = (80, 46, 24, 150)
    fill = (255, 248, 226)
    stroke = 7
    total = sum(font.getlength(ch) for ch in text) + 6 * (len(text) - 1)
    x = (640 - total) / 2
    base_y = 62
    glyphs = []
    for i, ch in enumerate(text):
        adv = font.getlength(ch)
        if ch != " ":
            tilt = (-6, 5, -4, 6, -5, 4, -6)[i % 7]
            wave = (0, -6, 2, -4, 4, -6, 0)[i % 7]
            g = Image.new("RGBA", (size + 60, size + 60), (0, 0, 0, 0))
            gd = ImageDraw.Draw(g)
            gd.text((30, 20), ch, font=font, fill=fill, stroke_width=stroke, stroke_fill=brown)
            g = g.rotate(tilt, resample=Image.BICUBIC, expand=False)
            glyphs.append((g, int(x) - 30, base_y + wave - 20))
        x += adv + 6
    for g, gx, gy in glyphs:
        sh = Image.new("RGBA", g.size, (0, 0, 0, 0))
        sh.paste(shadow_col, mask=g.split()[3])
        c.im.alpha_composite(sh, (gx + 5, gy + 7))
    for g, gx, gy in glyphs:
        c.im.alpha_composite(g, (gx, gy))
    lat = load_font(LAT_FONTS, 30) or ImageFont.load_default()
    sub = "Chokkomi Siege"
    w = lat.getlength(sub)
    c.d.text(((640 - w) / 2 + 2, 158), sub, font=lat, fill=shadow_col, stroke_width=4, stroke_fill=shadow_col)
    c.d.text(((640 - w) / 2, 156), sub, font=lat, fill=brown, stroke_width=4, stroke_fill=fill)
    for (sx, sy, r) in ((44, 40, 6), (600, 36, 8), (585, 120, 4), (58, 118, 5)):
        star(c, sx, sy, r, r * 0.45, P.yellow, P.brass_d)


def title_bg(c):
    d = c.d
    W, H = 1280, 720
    top = (150, 205, 250); bot = (255, 226, 200)
    for y in range(430):
        t = y / 430
        col = tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3))
        d.line([(0, y), (W, y)], fill=col)
    d.rectangle([0, 430, W, H], fill=bot)
    d.ellipse([960, 60, 1120, 220], fill=(255, 240, 190, 90))
    d.ellipse([985, 85, 1095, 195], fill=P.yellow_l, outline=P.yellow, width=4)
    rng = random.Random(3)
    for (cx, cy, s) in ((180, 120, 1.0), (460, 80, 0.8), (760, 150, 1.2), (1150, 260, 0.7), (620, 290, 0.6)):
        for (dx, dy, w, h) in ((0, 0, 140, 46), (30, -28, 70, 40), (80, -18, 60, 36), (-40, 8, 60, 30)):
            d.rectangle([cx + dx * s, cy + dy * s, cx + (dx + w) * s, cy + (dy + h) * s], fill=P.white)
        d.rectangle([cx - 40 * s, cy + 30 * s, cx + 140 * s, cy + 46 * s], fill=(232, 236, 248))
    d.ellipse([-300, 400, 700, 900], fill=(150, 210, 130))
    d.ellipse([600, 380, 1500, 900], fill=(140, 200, 120))
    d.ellipse([-200, 500, 600, 1000], fill=(120, 190, 110))
    d.ellipse([500, 520, 1400, 1100], fill=(110, 180, 100))
    # dirt path winding between gate (left) and castle (right)
    path = [(190, 470), (330, 520), (500, 560), (660, 600), (820, 570), (960, 520), (1060, 470)]
    d.line(path, fill=(190, 150, 96), width=46, joint="curve")
    d.line(path, fill=(226, 196, 140), width=34, joint="curve")
    for _ in range(40):
        px_ = rng.randrange(180, 1080)
        t = (px_ - 190) / (1060 - 190)
        idx = min(len(path) - 2, int(t * (len(path) - 1)))
        (x0, y0), (x1, y1) = path[idx], path[idx + 1]
        lt = (px_ - x0) / max(1, x1 - x0)
        py_ = y0 + (y1 - y0) * lt + rng.randrange(-12, 12)
        d.ellipse([px_, py_, px_ + 5, py_ + 3], fill=(200, 172, 120))
    sil = (60, 78, 70)
    # castle silhouette on the right hill
    d.rectangle([1000, 380, 1130, 470], fill=sil)
    d.rectangle([980, 350, 1020, 470], fill=sil)
    d.rectangle([1110, 350, 1150, 470], fill=sil)
    for x in range(982, 1020, 12):
        d.rectangle([x, 340, x + 6, 352], fill=sil)
    for x in range(1112, 1150, 12):
        d.rectangle([x, 340, x + 6, 352], fill=sil)
    for x in range(1024, 1110, 14):
        d.rectangle([x, 372, x + 7, 382], fill=sil)
    d.rectangle([1050, 430, 1080, 470], fill=(40, 52, 48))
    d.ellipse([1050, 416, 1080, 444], fill=(40, 52, 48))
    d.line([(1000, 300), (1000, 350)], fill=sil, width=3)
    d.polygon([(1002, 300), (1030, 310), (1002, 320)], fill=P.red)
    # dark gate on the left hill
    d.rectangle([150, 400, 230, 480], fill=(48, 40, 66))
    d.ellipse([150, 372, 230, 428], fill=(48, 40, 66))
    d.rectangle([166, 412, 214, 480], fill=(90, 60, 130))
    d.ellipse([166, 392, 214, 432], fill=(90, 60, 130))
    d.ellipse([180, 412, 200, 436], fill=(180, 150, 230))
    # blocky trees
    for (tx, ty, s) in ((80, 470, 1.0), (340, 440, 0.7), (700, 470, 0.8), (880, 440, 0.6), (1220, 480, 0.9)):
        d.rectangle([tx - 6 * s, ty, tx + 6 * s, ty + 40 * s], fill=P.bark)
        d.ellipse([tx - 34 * s, ty - 50 * s, tx + 34 * s, ty + 10 * s], fill=(90, 160, 90))
        d.ellipse([tx - 22 * s, ty - 62 * s, tx + 22 * s, ty - 20 * s], fill=(110, 180, 100))
    for _ in range(60):
        gx, gy = rng.randrange(0, W), rng.randrange(470, H)
        d.line([(gx, gy), (gx, gy - 8)], fill=(96, 160, 90), width=2)
        d.line([(gx + 5, gy), (gx + 7, gy - 7)], fill=(96, 160, 90), width=2)


# --------------------------------------------------------------------------
# build everything
# --------------------------------------------------------------------------
SPRITES = []   # (name, image) in generation order


def make(name, w, h, fn):
    c = Canvas(w, h)
    fn(c)
    SPRITES.append((name, c.im))


def build():
    for tid, fn in TOWERS.items():
        for f, suffix in ((0, "0"), (1, "1"), (2, "atk")):
            make(f"tower_{tid}_{suffix}", 48, 48, lambda c, fn=fn, f=f: fn(c, f))
    for cid, fn in CREEPS.items():
        s = CREEP_SIZE.get(cid, 48)
        for f in (0, 1):
            make(f"creep_{cid}_{f}", s, s, lambda c, fn=fn, f=f: fn(c, f))
    make("tile_grass_0", 32, 32, lambda c: tile_grass(c, 0))
    make("tile_grass_1", 32, 32, lambda c: tile_grass(c, 1))
    make("tile_path", 32, 32, tile_path)
    make("tile_slot", 32, 32, lambda c: tile_slot(c, False))
    make("tile_slot_hover", 32, 32, lambda c: tile_slot(c, True))
    make("base", 64, 64, base_castle)
    make("gate", 32, 64, gate_portal)
    make("flag_blue", 16, 24, lambda c: flag(c, P.blue))
    make("flag_red", 16, 24, lambda c: flag(c, P.red))
    for name, fn in PROJ.items():
        make(name, 16, 16, fn)
    for i in range(3):
        make(f"fx_hit_{i}", 24, 24, lambda c, i=i: fx_hit(c, i))
    for i in range(4):
        make(f"fx_boom_{i}", 32, 32, lambda c, i=i: fx_boom(c, i))
    for i in range(3):
        make(f"fx_smoke_{i}", 24, 24, lambda c, i=i: fx_smoke(c, i))
    for i in range(4):
        make(f"fx_star_{i}", 32, 32, lambda c, i=i: fx_star(c, i))
    for i in range(3):
        make(f"fx_heal_{i}", 24, 24, lambda c, i=i: fx_heal(c, i))
    make("fx_slow", 16, 16, fx_slow)
    make("fx_burn", 16, 16, fx_burn)
    make("fx_silence", 16, 16, fx_silence)
    make("fx_stealth", 16, 16, fx_stealth)
    make("fx_shadow", 24, 12, fx_shadow)
    make("ui_panel", 48, 48, lambda c: ui_panel(c, False))
    make("ui_panel_dark", 48, 48, lambda c: ui_panel(c, True))
    make("ui_button", 48, 48, lambda c: ui_button(c, P.orange))
    make("ui_button_green", 48, 48, lambda c: ui_button(c, (110, 190, 92)))
    make("ui_button_blue", 48, 48, lambda c: ui_button(c, (96, 150, 232)))
    make("ui_button_grey", 48, 48, lambda c: ui_button(c, (164, 164, 176)))
    make("ui_card", 96, 128, lambda c: ui_card(c, (170, 118, 72)))
    make("ui_card_rare", 96, 128, lambda c: ui_card(c, (90, 140, 220)))
    make("ui_card_hero", 96, 128, lambda c: ui_card(c, P.brass, True))
    make("ui_card_legend", 96, 128, ui_card_legend)
    make("ui_slot", 40, 40, ui_slot)
    for name in ("icon_coin", "icon_heart", "icon_income", "icon_star", "icon_clock", "icon_skull", "icon_wing",
                 "icon_shield", "icon_sword", "icon_range", "icon_speed", "icon_leaf", "icon_fire", "icon_gear",
                 "icon_lock", "icon_check", "icon_eye", "icon_merge"):
        make(name, 16, 16, lambda c, n=name: icon(n, c))
    for name in ("aug_resource", "aug_power", "aug_hinder", "aug_info"):
        make(name, 32, 32, lambda c, n=name: aug(n, c))
    for n in (1, 2, 3):
        make(f"star_{n}", 24, 10, lambda c, n=n: stars_badge(c, n))
    make("logo", 640, 200, logo)
    make("title_bg", 1280, 720, title_bg)


def expected_names():
    names = []
    for t in range(1, 22):
        names += [f"tower_{t}_0", f"tower_{t}_1", f"tower_{t}_atk"]
    for k in range(1, 16):
        names += [f"creep_{k}_0", f"creep_{k}_1"]
    names += ["tile_grass_0", "tile_grass_1", "tile_path", "tile_slot", "tile_slot_hover", "base", "gate",
              "flag_blue", "flag_red"]
    names += ["proj_arrow", "proj_fire_arrow", "proj_fireball", "proj_bullet", "proj_cannon", "proj_leaf",
              "proj_bolt", "proj_spark", "proj_thorn", "proj_rock"]
    names += [f"fx_hit_{i}" for i in range(3)] + [f"fx_boom_{i}" for i in range(4)] + [f"fx_smoke_{i}" for i in range(3)]
    names += [f"fx_star_{i}" for i in range(4)] + [f"fx_heal_{i}" for i in range(3)]
    names += ["fx_slow", "fx_burn", "fx_silence", "fx_stealth", "fx_shadow"]
    names += ["ui_panel", "ui_panel_dark", "ui_button", "ui_button_green", "ui_button_blue", "ui_button_grey",
              "ui_card", "ui_card_rare", "ui_card_hero", "ui_card_legend", "ui_slot"]
    names += ["icon_coin", "icon_heart", "icon_income", "icon_star", "icon_clock", "icon_skull", "icon_wing",
              "icon_shield", "icon_sword", "icon_range", "icon_speed", "icon_leaf", "icon_fire", "icon_gear",
              "icon_lock", "icon_check", "icon_eye", "icon_merge"]
    names += ["aug_resource", "aug_power", "aug_hinder", "aug_info", "star_1", "star_2", "star_3", "logo", "title_bg"]
    return names


def contact_sheet(sprites, path, scale=3, max_w=1600, label_font=None):
    label_font = label_font or (load_font(LAT_FONTS[1:], 12) or ImageFont.load_default())
    cells = []
    for name, im in sprites:
        if im.width * scale <= 400:
            big = im.resize((im.width * scale, im.height * scale), Image.NEAREST)
        else:
            k = 480 / im.width
            big = im.resize((int(im.width * k), int(im.height * k)), Image.LANCZOS)
        cells.append((name, big))
    pad, lab = 10, 16
    rows, row, rw, rh = [], [], 0, 0
    for name, big in cells:
        w = max(big.width, int(label_font.getlength(name)) + 4) + pad
        if row and rw + w > max_w:
            rows.append((row, rh)); row, rw, rh = [], 0, 0
        row.append((name, big)); rw += w; rh = max(rh, big.height + lab + pad)
    if row:
        rows.append((row, rh))
    H = sum(h for _, h in rows) + pad
    sheet = Image.new("RGBA", (max_w, H), (128, 128, 128, 255))
    d = ImageDraw.Draw(sheet)
    y = pad
    for row, h in rows:
        x = pad
        for name, big in row:
            sheet.alpha_composite(big, (x, y))
            d.text((x, y + big.height + 2), name, font=label_font, fill=(255, 255, 255))
            x += max(big.width, int(label_font.getlength(name)) + 4) + pad
        y += h
    sheet.save(path)


def main():
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(SCRATCH, exist_ok=True)
    build()
    written = set()
    for name, im in SPRITES:
        im.save(os.path.join(OUT, name + ".png"))
        written.add(name)
    exp = expected_names()
    missing = [n for n in exp if not os.path.exists(os.path.join(OUT, n + ".png"))]
    extra = sorted(written - set(exp))
    print(f"wrote {len(written)} sprites to {OUT}  (expected {len(exp)})")
    print("missing:", missing if missing else "none")
    if extra:
        print("extra:", extra)
    contact_sheet(SPRITES, CONTACT)
    # per-category close-up sheets for review (scratchpad only)
    groups = {"towers": "tower_", "creeps": "creep_", "world": ("tile_", "base", "gate", "flag_", "proj_", "fx_"),
              "ui": ("ui_", "icon_", "aug_", "star_")}
    for g, prefix in groups.items():
        sel = [(n, im) for n, im in SPRITES if n.startswith(prefix)]
        contact_sheet(sel, os.path.join(SCRATCH, f"contact_{g}.png"), scale=4, max_w=1400)
    legends = tuple(f"creep_{k}_" for k in (12, 13, 14, 15))
    sel = [(n, im) for n, im in SPRITES if n.startswith(legends)]
    contact_sheet(sel, os.path.join(SCRATCH, "contact_legends.png"), scale=4, max_w=1400)
    tier3 = tuple(f"tower_{t}_" for t in (18, 19, 20, 21))
    sel = [(n, im) for n, im in SPRITES if n.startswith(tier3)]
    contact_sheet(sel, os.path.join(SCRATCH, "contact_tier3.png"), scale=4, max_w=1400)
    print("contact sheet:", CONTACT)


if __name__ == "__main__":
    main()
