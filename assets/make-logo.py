#!/usr/bin/env python3
"""Draw assets/logo.png.

Everything is drawn at 320x180 and scaled up with NEAREST, so the result is
actually pixel art rather than a smooth render shrunk down - which is what
sits next to it on a Core Keeper mod page.
"""

import os
import random

from PIL import Image, ImageDraw, ImageFont

W, H = 320, 180
SCALE = 4

DEEP = (22, 20, 30)
MID = (36, 32, 50)
STONE = (58, 52, 76)
STONE_LIT = (78, 70, 100)
GOLD = (242, 206, 122)
TEAL = (116, 204, 192)
TEXT = (236, 232, 244)
DIM = (146, 140, 172)

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


def background(d):
    for y in range(H):
        t = y / H
        # darker at the edges, as though lit from the middle
        d.line([(0, y), (W, y)], fill=tuple(
            int(DEEP[i] + (MID[i] - DEEP[i]) * (1 - abs(t - 0.42) * 1.7)) for i in range(3)))

    # scattered ore specks, seeded so the file does not churn between runs
    rng = random.Random(7)
    for _ in range(90):
        x, y = rng.randrange(W), rng.randrange(H)
        # keep them off the type, where they read as dirt rather than ore
        if 30 < y < 158 and 24 < x < 296:
            continue
        colour = rng.choice([STONE, STONE, STONE_LIT, GOLD, TEAL])
        size = rng.choice([1, 1, 1, 2])
        d.rectangle([(x, y), (x + size - 1, y + size - 1)], fill=colour)


def tile(d, x, y, size=26):
    """A stone plate for a glyph to sit on."""
    d.rectangle([(x, y), (x + size, y + size)], fill=STONE)
    d.rectangle([(x, y), (x + size, y)], fill=STONE_LIT)
    d.rectangle([(x, y), (x, y + size)], fill=STONE_LIT)


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
    img = Image.new("RGB", (W, H), DEEP)
    d = ImageDraw.Draw(img)

    background(d)

    centre(d, "CORE KEEPER", font(10, False), 36, DIM, spacing=2)
    centre(d, "QUALITY OF LIFE", font(25, True), 52, TEXT)

    d.rectangle([(W / 2 - 40, 88), (W / 2 + 40, 89)], fill=GOLD)

    glyphs = (rod, food, minion, meter)
    step = 34
    left = (W - (len(glyphs) * step - 8)) / 2
    for i, glyph in enumerate(glyphs):
        x = int(left + i * step)
        tile(d, x, 102)
        glyph(d, x, 102)

    centre(d, "fishing   eating   summons   dps", font(10, False), 142, DIM, spacing=1)

    img.resize((W * SCALE, H * SCALE), Image.NEAREST).save(
        os.path.join(HERE, "logo.png"))
    print(f"wrote logo.png at {W * SCALE}x{H * SCALE}")


if __name__ == "__main__":
    main()
