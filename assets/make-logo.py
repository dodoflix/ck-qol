#!/usr/bin/env python3
"""Draw assets/logo.png.

Two layers on purpose. The ground and the feature glyphs are drawn small and
scaled up with NEAREST, so they read as pixel art beside the rest of a Core
Keeper mod page. The type is drawn at full size instead: upscaling antialiased
glyphs turns their soft edges into grey blocks, which just looks like a low
resolution image rather than like pixel art.
"""

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

TILE = 26

HERE = os.path.dirname(os.path.abspath(__file__))


def font(size, bold):
    name = "DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf"
    for base in ("/usr/share/fonts/TTF", "/usr/share/fonts/truetype/dejavu"):
        try:
            return ImageFont.truetype(os.path.join(base, name), size)
        except OSError:
            continue
    return ImageFont.load_default()


def centre(d, text, f, y, fill, spacing=0):
    if spacing:
        widths = [d.textlength(c, font=f) + spacing for c in text]
        x = (W - (sum(widths) - spacing)) / 2
        for c, w in zip(text, widths):
            d.text((x, y), c, font=f, fill=fill)
            x += w
        return
    box = d.textbbox((0, 0), text, font=f)
    d.text(((W - (box[2] - box[0])) / 2 - box[0], y), text, font=f, fill=fill)


def ground():
    """The pixel layer: a lit cave wall with ore in it."""
    img = Image.new("RGB", (SW, SH), DEEP)
    d = ImageDraw.Draw(img)

    for y in range(SH):
        t = y / SH
        d.line([(0, y), (SW, y)], fill=tuple(
            int(DEEP[i] + (MID[i] - DEEP[i]) * (1 - abs(t - 0.42) * 1.7))
            for i in range(3)))

    # Seeded, so the file does not churn between runs. Kept clear of the type,
    # where a speck reads as dirt on the image rather than as ore in the wall.
    rng = random.Random(7)
    for _ in range(90):
        x, y = rng.randrange(SW), rng.randrange(SH)
        if 28 < y < 160 and 20 < x < 300:
            continue
        size = rng.choice([1, 1, 1, 2])
        d.rectangle([(x, y), (x + size - 1, y + size - 1)],
                    fill=rng.choice([STONE, STONE, STONE_LIT, GOLD, TEAL]))

    return img.resize((W, H), Image.NEAREST)


def plate(glyph):
    """One stone tile with a glyph on it, at pixel scale."""
    img = Image.new("RGB", (TILE + 1, TILE + 1), STONE)
    d = ImageDraw.Draw(img)
    d.rectangle([(0, 0), (TILE, 0)], fill=STONE_LIT)
    d.rectangle([(0, 0), (0, TILE)], fill=STONE_LIT)
    glyph(d, 0, 0)
    return img.resize(((TILE + 1) * SCALE, (TILE + 1) * SCALE), Image.NEAREST)


def rod(d, x, y):
    """Auto Fishing: a hook on a line."""
    d.line([(x + 13, y + 5), (x + 13, y + 15)], fill=DIM)
    for px, py in ((13, 16), (13, 17), (14, 18), (15, 18), (16, 17), (16, 16), (16, 15)):
        d.point((x + px, y + py), fill=GOLD)
    d.point((x + 15, y + 13), fill=GOLD)
    d.point((x + 16, y + 14), fill=GOLD)


def food(d, x, y):
    """Auto Eat: a berry with a leaf."""
    d.ellipse([(x + 7, y + 9), (x + 19, y + 20)], fill=(206, 84, 92))
    d.ellipse([(x + 9, y + 11), (x + 12, y + 14)], fill=(238, 150, 150))
    d.line([(x + 13, y + 6), (x + 13, y + 10)], fill=(120, 92, 60))
    d.ellipse([(x + 14, y + 5), (x + 19, y + 9)], fill=(110, 176, 96))


def minion(d, x, y):
    """Auto Summon: a small conjured thing."""
    d.ellipse([(x + 7, y + 8), (x + 19, y + 19)], fill=TEAL)
    d.rectangle([(x + 10, y + 12), (x + 11, y + 14)], fill=DEEP)
    d.rectangle([(x + 15, y + 12), (x + 16, y + 14)], fill=DEEP)
    for dx in (8, 13, 18):
        d.point((x + dx, y + 21), fill=TEAL)


def meter(d, x, y):
    """DPS Tracker: rising bars."""
    for i, h in enumerate((5, 9, 14)):
        bx = x + 7 + i * 5
        d.rectangle([(bx, y + 20 - h), (bx + 3, y + 20)], fill=GOLD)


def main():
    img = ground()

    glyphs = (rod, food, minion, meter)
    size = (TILE + 1) * SCALE
    gap = 32
    left = (W - (len(glyphs) * size + (len(glyphs) - 1) * gap)) // 2
    for i, glyph in enumerate(glyphs):
        img.paste(plate(glyph), (left + i * (size + gap), 437))

    d = ImageDraw.Draw(img)
    centre(d, "CORE KEEPER", font(36, False), 165, DIM, spacing=11)
    centre(d, "QUALITY OF LIFE", font(104, True), 223, TEXT)
    d.rectangle([(W / 2 - 160, 383), (W / 2 + 160, 388)], fill=GOLD)

    img.save(os.path.join(HERE, "logo.png"))
    print(f"wrote logo.png at {W}x{H}")


if __name__ == "__main__":
    main()
