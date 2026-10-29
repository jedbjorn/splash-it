"""The Splash-It "splash" glyph, shared by the ribbon icons (tools/make_icons.py) and the
installer branding (Lane D).

A paint splash: a round body with six tapering arms ending in round tips, and three
loose droplets, drawn as a union of simple shapes so it stays readable from 16 px up. Coordinates are in a unit square
(0..1, y down) and scaled to any box, so callers draw it at any size.

    from glyph import draw_splash
    draw_splash(draw, (x0, y0, x1, y1), fill=(0x4F, 0x8E, 0xF7, 0xFF))

Pillow only. The tile and the colors live in the callers.
"""

from __future__ import annotations

import math

from PIL import ImageDraw

# Glyph blue, the tile greys and the dark-theme variant (spec, App: a #4F8EF7 glyph on a
# #D9D9D9 tile; the dark tile matches gantt-it's dark icon tile).
BLUE = (0x4F, 0x8E, 0xF7, 0xFF)
BLUE_ON_DARK = (0x6F, 0xA3, 0xFA, 0xFF)
TILE = (0xD9, 0xD9, 0xD9, 0xFF)
TILE_DARK = (0x2A, 0x30, 0x3A, 0xFF)

# The body: (centre x, centre y, radius) in the unit square.
BODY = (0.50, 0.53, 0.25)

# Arms reaching out of the body: (angle in degrees, 0 = right, clockwise as y runs down;
# reach from the centre; radius of the round tip).
ARMS = [
    (-100, 0.40, 0.075),
    (-35, 0.42, 0.070),
    (15, 0.36, 0.080),
    (70, 0.40, 0.070),
    (125, 0.38, 0.075),
    (190, 0.40, 0.070),
]

# Loose droplets beyond the arms: (angle, distance from the centre, radius).
DROPS = [
    (-65, 0.46, 0.055),
    (160, 0.47, 0.050),
    (45, 0.45, 0.045),
]


def draw_splash(draw: ImageDraw.ImageDraw, box: tuple[float, float, float, float], fill) -> None:
    """Draws the splash glyph filling `box` (x0, y0, x1, y1) in `fill`."""
    x0, y0, x1, y1 = box
    w, h = x1 - x0, y1 - y0

    def point(x: float, y: float) -> tuple[float, float]:
        return x0 + x * w, y0 + y * h

    def circle(cx: float, cy: float, r: float) -> None:
        (ax, ay), (bx, by) = point(cx - r, cy - r), point(cx + r, cy + r)
        draw.ellipse((ax, ay, bx, by), fill=fill)

    bx, by, br = BODY
    circle(bx, by, br)
    for angle, reach, tip in ARMS:
        a = math.radians(angle)
        tx, ty = bx + reach * math.cos(a), by + reach * math.sin(a)
        # A tapering arm: wide where it leaves the body, the tip's width at the end.
        nx, ny = -math.sin(a), math.cos(a)
        base = br * 0.55
        draw.polygon(
            [
                point(bx + nx * base, by + ny * base),
                point(tx + nx * tip, ty + ny * tip),
                point(tx - nx * tip, ty - ny * tip),
                point(bx - nx * base, by - ny * base),
            ],
            fill=fill,
        )
        circle(tx, ty, tip)
    for angle, distance, r in DROPS:
        a = math.radians(angle)
        circle(bx + distance * math.cos(a), by + distance * math.sin(a), r)
