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

SLOT_EDGE = (13, 12, 18)
SLOT_FRAME = (74, 67, 94)
SLOT_FRAME_LIT = (104, 95, 130)
SLOT_SHADOW = (30, 27, 39)
SLOT_FILL = (43, 39, 56)
GOLD_DIM = (148, 122, 66)

TILE = 29

HERE = os.path.dirname(os.path.abspath(__file__))


def font(size, bold):
    """Silkscreen, an OFL pixel font, at a multiple of its native 8px - anything
    else lands the strokes between pixels and the hard edges go soft."""
    name = "Silkscreen-Bold.ttf" if bold else "Silkscreen-Regular.ttf"
    return ImageFont.truetype(os.path.join(HERE, "fonts", name), size)


def ink(text, f, spacing=0):
    """The text as a mask cropped to the pixels it actually marks.

    Drawing straight to the canvas centres the font's advance box, not its marks,
    so a line lands up to a pixel off and no two lines land off by the same
    amount. Measuring first is the only way to get them to agree.
    """
    pad = 40
    mask = Image.new("L", (SW + pad * 2, f.size * 3 + pad))
    d = ImageDraw.Draw(mask)
    d.fontmode = "1"

    if spacing:
        x = float(pad)
        for ch in text:
            d.text((x, pad // 2), ch, font=f, fill=255)
            x += d.textlength(ch, font=f) + spacing
    else:
        d.text((pad, pad // 2), text, font=f, fill=255)

    return mask.crop(mask.getbbox())


def stamp(img, mask, top, fill):
    """Centres a mask horizontally at an integer offset."""
    img.paste(fill, ((SW - mask.width) // 2, top), mask)


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


def ground(clear=()):
    """The pixel layer: a cave wall lit from where the title sits, with ore in it.

    `clear` are rectangles the composition occupies, which ore keeps out of.
    """
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

        if any(x0 <= x <= x1 and y0 <= y <= y1 for x0, y0, x1, y1 in clear):
            continue

        seam = rng.random() < 0.65
        body, glint = (STONE, STONE_LIT) if seam else (
            rng.choice([GOLD, TEAL]), (238, 234, 246))

        cells = [(0, 0)] + rng.sample(
            [(1, 0), (0, 1), (1, 1), (-1, 0), (0, -1)], rng.randint(1, 3))
        for dx, dy in cells:
            d.point((x + dx, y + dy), fill=body)
        d.point((x, y), fill=glint)

    return img


def plate(glyph):
    """One framed slot with a glyph in it, the way an inventory holds an item.

    Four rings, outside in: a hard outline with the corner pixels cut so it reads
    as rounded, a lit stone frame, a shadow where the frame meets the recess, and
    the recess itself.
    """
    size = TILE + 1
    last = TILE
    img = Image.new("RGBA", (size, size), SLOT_FILL + (255,))
    d = ImageDraw.Draw(img)

    d.rectangle([(0, 0), (last, last)], outline=SLOT_EDGE)

    # Cut to transparent rather than to a colour: any fixed colour is wrong
    # against a dithered wall, and shows up as four bright specks.
    for x, y in ((0, 0), (last, 0), (0, last), (last, last)):
        d.point((x, y), fill=(0, 0, 0, 0))

    d.rectangle([(1, 1), (last - 1, last - 1)], outline=SLOT_FRAME)
    for x, y in ((1, 1), (last - 1, 1), (1, last - 1), (last - 1, last - 1)):
        d.point((x, y), fill=SLOT_EDGE)

    # Lit from above: the frame catches light on top, the recess is shadowed
    # under it.
    d.line([(2, 1), (last - 2, 1)], fill=SLOT_FRAME_LIT)
    d.rectangle([(2, 2), (last - 2, last - 2)], outline=SLOT_SHADOW)
    d.line([(2, last - 2), (last - 2, last - 2)], fill=SLOT_FRAME)

    for x, y in ((2, 2), (last - 2, 2)):
        d.point((x, y), fill=GOLD_DIM)

    art = sprite(*glyph)
    img.paste(art, ((size - art.width) // 2, (size - art.height) // 2), art)
    return img


# Sprites are hand placed on an 8px grid, not drawn with ellipses and polygons.
# Those calls decide their own edges, and at this size their idea of a curve is
# a few stray pixels rather than a shape.
#
# Every sprite is ART on its long axis, so no icon reads bigger than its
# neighbour however well each one is centred.
ART = 8
ART_SCALE = 2


def sprite(rows, palette):
    """A pixel map into an image. '.' is a hole."""
    img = Image.new("RGBA", (len(rows[0]), len(rows)), (0, 0, 0, 0))
    px = img.load()
    for y, row in enumerate(rows):
        for x, key in enumerate(row):
            if key != ".":
                px[x, y] = palette[key] + (255,)

    img = img.crop(img.getbbox())
    assert max(img.size) == ART, f"sprite is {img.size}, long axis must be {ART}"
    return img.resize((img.width * ART_SCALE, img.height * ART_SCALE), Image.NEAREST)


FISH = ([
    "...bb..d",
    ".bbbbbdd",
    "bkbbbbdd",
    "bbbbbbdd",
    ".bbbbbdd",
    "...bb..d",
], {
    "b": (110, 180, 226),
    "d": (74, 140, 196),
    "k": (22, 46, 74),
})

# Seven wide, not eight: an even sprite has no centre column, so the dip in the
# crown and the stem above it can never sit on the body's axis.
BERRY = ([
    "...s...",
    "...sgg.",
    ".rrrrr.",
    "rrrrrrr",
    "rrrrrrr",
    "rrrrrrr",
    ".rrrrr.",
    "..rrr..",
], {
    "r": (206, 84, 92),
    "s": (120, 92, 60),
    "g": (110, 176, 96),
})

DEMON_ART = ([
    "d......d",
    "dmmmmmmd",
    "mmmmmmmm",
    "myymmyym",
    "mmmmmmmm",
    "mmmmmmmm",
    ".mmmmmm.",
    "..mmmm..",
], {
    "m": (188, 78, 96),
    "d": (120, 44, 60),
    "y": (250, 214, 120),
})

CHEST = ([
    ".dddddd.",
    "dlllllld",
    "dlllllld",
    "dddggddd",
    "dwwggwwd",
    "dwwwwwwd",
    ".dddddd.",
], {
    "l": (176, 124, 72),
    "w": (140, 96, 56),
    "d": (84, 54, 34),
    "g": (242, 206, 122),
})

SWORD = ([
    "..s..",
    ".sls.",
    ".sls.",
    ".sls.",
    ".sls.",
    "ggggg",
    "..b..",
    ".ggg.",
], {
    "s": (128, 136, 158),
    "l": (232, 238, 250),
    "g": (242, 206, 122),
    "b": (120, 92, 60),
})


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
    glyphs = (FISH, BERRY, DEMON_ART, SWORD, CHEST)
    size = TILE + 1
    gap = 8

    # Even, so the row divides onto the 320px grid without a half pixel.
    span = len(glyphs) * size + (len(glyphs) - 1) * gap
    assert span % 2 == 0, "odd tile row cannot be centred exactly"
    left = (SW - span) // 2

    # 1-bit type at pixel scale, the way the game's own font is drawn.
    # Antialiasing is what made an earlier attempt look like a low resolution
    # photograph: its soft edge pixels became 4x4 grey blocks on the way up.
    kicker = ink("CORE KEEPER", font(8, False), spacing=3)
    title = ink("QUALITY OF LIFE", font(24, True))

    # Measured rather than guessed, so the whole block sits on the canvas centre
    # instead of drifting up as the parts change.
    gem = 3
    block = kicker.height + 8 + title.height + 10 + gem * 2 + 12 + size
    start = (SH - block) // 2

    rule = start + kicker.height + 8 + title.height + 10 + gem
    top = rule + gem + 12

    img = ground(clear=[
        (0, start - 2, SW, start + kicker.height + 8 + title.height + 2),
        (left - 6, rule - 5, left + span + 6, rule + 5),
        (left - 3, top - 3, left + span + 3, top + size + 3),
    ])

    stamp(img, kicker, start, DIM)
    stamp(img, title, start + kicker.height + 8, TEXT)

    # The rule ends on the tile row's own edges, so the two read as one block.
    divider(ImageDraw.Draw(img), SW // 2, rule, half=span // 2 - 3)

    for i, glyph in enumerate(glyphs):
        slot = plate(glyph)
        img.paste(slot, (left + i * (size + gap), top), slot)

    img.resize((W, H), Image.NEAREST).save(os.path.join(HERE, "logo.png"))
    print(f"wrote logo.png at {W}x{H}")


if __name__ == "__main__":
    main()
