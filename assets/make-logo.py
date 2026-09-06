#!/usr/bin/env python3
"""Draw assets/logo.png.

Everything is composed at 320x180 and scaled up once with NEAREST, so it is
pixel art rather than a smooth render shrunk down - which is what sits beside it
on a Core Keeper mod page.

Two things have to hold for that to work: the font is a real pixel font at a
multiple of its native size, and text is drawn 1-bit. Antialiasing is what made
an earlier attempt look like a low resolution photograph, because each soft edge
pixel became a 4x4 grey block on the way up.

No font could be taken from the game itself - its UI font is a sprite atlas
inside the packed Unity assets, and no TTF ships with it.
"""

import math
import os
import random

from PIL import Image, ImageDraw, ImageFont

W, H = 1280, 720
SCALE = 4
SW, SH = W // SCALE, H // SCALE

DEEP = (22, 20, 30)
MID = (36, 32, 50)
STONE = (58, 52, 76)
STONE_LIT = (82, 74, 106)
GOLD = (242, 206, 122)
TEAL = (116, 204, 192)
TEXT = (238, 234, 246)
DIM = (150, 144, 176)

SCALE_BLUE = (110, 180, 226)
FIN = (74, 140, 196)
DEMON = (178, 74, 92)
HORN = (120, 44, 60)
STEEL = (196, 202, 220)
STEEL_LIT = (238, 242, 252)
GRIP = (120, 92, 60)

TILE = 26

HERE = os.path.dirname(os.path.abspath(__file__))


def font(size, bold):
    """Silkscreen, an OFL pixel font, at a multiple of its native 8px - anything
    else lands the strokes between pixels and the hard edges go soft."""
    name = "Silkscreen-Bold.ttf" if bold else "Silkscreen-Regular.ttf"
    return ImageFont.truetype(os.path.join(HERE, "fonts", name), size)


def centre(d, text, f, y, fill, spacing=0, width=SW):
    if spacing:
        widths = [d.textlength(c, font=f) + spacing for c in text]
        x = (width - (sum(widths) - spacing)) / 2
        for c, w in zip(text, widths):
            d.text((x, y), c, font=f, fill=fill)
            x += w
        return
    box = d.textbbox((0, 0), text, font=f)
    d.text(((width - (box[2] - box[0])) / 2 - box[0], y), text, font=f, fill=fill)


# Five steps rather than a smooth ramp: a continuous gradient at this size bands
# into visible stripes once it is scaled up, and banding that was not chosen
# looks like a compression artefact.
BG_RAMP = [(21, 19, 28), (25, 22, 33), (29, 26, 38), (33, 30, 44), (37, 34, 50)]

# Ordered dither, so the steps break up into a pixel texture instead of meeting
# along hard contour lines.
BAYER = [
    [0, 8, 2, 10],
    [12, 4, 14, 6],
    [3, 11, 1, 9],
    [15, 7, 13, 5],
]


def ground():
    """The pixel layer: a cave wall lit from where the title sits, with ore in it."""
    img = Image.new("RGB", (SW, SH))
    px = img.load()

    cx, cy = SW / 2, SH * 0.42
    far = math.hypot(SW / 2, SH / 2)
    top = len(BG_RAMP) - 1

    for y in range(SH):
        for x in range(SW):
            # Squashed vertically so the pool of light is wider than it is tall,
            # following the shape of the wordmark rather than a circle.
            fall = math.hypot(x - cx, (y - cy) * 1.4) / far
            level = (1.0 - min(fall, 1.0)) * top
            step = int(level)
            if level - step > BAYER[y % 4][x % 4] / 16:
                step += 1
            px[x, y] = BG_RAMP[min(step, top)]

    d = ImageDraw.Draw(img)

    # Clusters rather than lone pixels: a single speck reads as dirt on the
    # image, a clump reads as ore in the wall. Seeded so the file does not churn
    # between runs, and kept off the type.
    rng = random.Random(11)
    for _ in range(48):
        x, y = rng.randrange(4, SW - 6), rng.randrange(4, SH - 6)

        on_type = 34 < y < 100
        on_tiles = 104 < y < 142 and 88 < x < 232
        if on_type or on_tiles:
            continue

        seam = rng.random() < 0.65
        body, glint = (STONE, STONE_LIT) if seam else (
            (rng.choice([GOLD, TEAL]), (238, 234, 246)))

        cells = [(0, 0)] + rng.sample(
            [(1, 0), (0, 1), (1, 1), (-1, 0), (0, -1)], rng.randint(1, 3))
        for dx, dy in cells:
            d.point((x + dx, y + dy), fill=body)
        d.point((x, y), fill=glint)

    return img


def plate(glyph):
    """One stone tile with a glyph on it, at pixel scale."""
    img = Image.new("RGB", (TILE + 1, TILE + 1), STONE)
    d = ImageDraw.Draw(img)
    d.rectangle([(0, 0), (TILE, 0)], fill=STONE_LIT)
    d.rectangle([(0, 0), (0, TILE)], fill=STONE_LIT)
    glyph(d, 0, 0)
    return img


def fish(d, x, y):
    """Auto Fishing."""
    d.polygon([(x + 9, y + 10), (x + 12, y + 5), (x + 15, y + 10)], fill=FIN)
    d.polygon([(x + 16, y + 13), (x + 22, y + 7), (x + 22, y + 19)], fill=FIN)
    d.ellipse([(x + 4, y + 9), (x + 18, y + 18)], fill=SCALE_BLUE)
    d.point((x + 8, y + 12), fill=DEEP)
    d.line([(x + 5, y + 14), (x + 6, y + 14)], fill=FIN)


def food(d, x, y):
    """Auto Eat: a berry with a leaf."""
    d.ellipse([(x + 7, y + 9), (x + 19, y + 20)], fill=(206, 84, 92))
    d.ellipse([(x + 9, y + 11), (x + 12, y + 14)], fill=(238, 150, 150))
    d.line([(x + 13, y + 6), (x + 13, y + 10)], fill=(120, 92, 60))
    d.ellipse([(x + 14, y + 5), (x + 19, y + 9)], fill=(110, 176, 96))


def demon(d, x, y):
    """Auto Summon."""
    d.polygon([(x + 8, y + 9), (x + 6, y + 3), (x + 11, y + 7)], fill=HORN)
    d.polygon([(x + 18, y + 9), (x + 20, y + 3), (x + 15, y + 7)], fill=HORN)
    d.ellipse([(x + 6, y + 7), (x + 20, y + 20)], fill=DEMON)
    d.polygon([(x + 9, y + 12), (x + 12, y + 13), (x + 9, y + 15)], fill=GOLD)
    d.polygon([(x + 17, y + 12), (x + 14, y + 13), (x + 17, y + 15)], fill=GOLD)
    d.line([(x + 10, y + 17), (x + 16, y + 17)], fill=HORN)


def sword(d, x, y):
    """DPS Tracker."""
    d.polygon([(x + 13, y + 3), (x + 16, y + 7), (x + 16, y + 16), (x + 10, y + 16),
               (x + 10, y + 7)], fill=STEEL)
    d.line([(x + 12, y + 7), (x + 12, y + 15)], fill=STEEL_LIT)
    d.rectangle([(x + 7, y + 16), (x + 19, y + 18)], fill=GOLD)
    d.rectangle([(x + 12, y + 19), (x + 14, y + 23)], fill=GRIP)
    d.rectangle([(x + 11, y + 23), (x + 15, y + 24)], fill=GOLD)


def divider(d, cx, y, half=44):
    """A rule with a cut gemstone in the middle and a pip at each end, the way a
    game menu separates a title from what it belongs to."""
    dim = (148, 122, 66)

    for dx in range(5, half):
        near = dx < half - 12
        for side in (-1, 1):
            d.point((cx + side * dx, y), fill=GOLD if near else dim)

    for dy in range(-3, 4):
        for dx in range(-3, 4):
            reach = abs(dx) + abs(dy)
            if reach <= 3:
                d.point((cx + dx, y + dy), fill=GOLD if reach < 3 else dim)
    d.point((cx - 1, y - 1), fill=(252, 240, 200))

    for side in (-1, 1):
        for dy in range(-1, 2):
            for dx in range(-1, 2):
                if abs(dx) + abs(dy) <= 1:
                    d.point((cx + side * (half + 3) + dx, y + dy), fill=dim)


def main():
    img = ground()

    glyphs = (fish, food, demon, sword)
    size = TILE + 1
    gap = 8
    left = (SW - (len(glyphs) * size + (len(glyphs) - 1) * gap)) // 2
    for i, glyph in enumerate(glyphs):
        img.paste(plate(glyph), (left + i * (size + gap), 110))

    d = ImageDraw.Draw(img)

    # 1-bit, at pixel scale, the way the game's own font is drawn. Antialiasing
    # is what made an earlier attempt look like a low resolution photograph: its
    # soft edge pixels became 4x4 grey blocks on the way up.
    d.fontmode = "1"
    centre(d, "CORE KEEPER", font(8, False), 44, DIM, spacing=3)
    centre(d, "QUALITY OF LIFE", font(24, True), 60, TEXT)
    divider(d, SW // 2, 95)

    img.resize((W, H), Image.NEAREST).save(os.path.join(HERE, "logo.png"))
    print(f"wrote logo.png at {W}x{H}")


if __name__ == "__main__":
    main()
