"""The Splash-It "splash" glyph and its tile, shared by the ribbon icons
(tools/make_icons.py) and the installer branding (tools/make_installer_branding.py).

A paint splash: a round body with six tapering arms ending in round tips, and three
loose droplets, drawn as a union of simple shapes so it stays readable from 16 px up.
Coordinates are in a unit square (0..1, y down) and scaled to any box.

    from glyph import draw_splash, tile
    draw_splash(draw, (x0, y0, x1, y1), fill=BLUE)   # the glyph alone, into any box
    tile(32)                                          # the icon: glyph on the rounded tile
    tile(32, glyph=draw_group_tabs)                   # the Group Tabs icon (spec #2)

Pillow only.
"""

from __future__ import annotations

import math

from PIL import Image, ImageDraw

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


# The Group Tabs glyph (spec #2): a tab strip in two groups. Each file's two tabs are short
# and wide (wider than tall, so they read as tabs even at 16 px, review n1), with square
# bottoms and rounded tops, side by side over a bar that joins them; a wider gap separates
# the two files. Unit square, y down.
TAB_TOP, TAB_BOTTOM = 0.27, 0.43
BAR_TOP, BAR_BOTTOM = 0.51, 0.67
TAB_RADIUS = 0.05
# Each file: its tabs as (left, right) spans; its bar runs under all of them. The gap
# between the files (0.12) is more than twice the gap between a file's tabs (0.05).
GROUPS = [
    [(0.00, 0.195), (0.245, 0.44)],
    [(0.56, 0.755), (0.805, 1.00)],
]


def draw_group_tabs(draw: ImageDraw.ImageDraw, box: tuple[float, float, float, float], fill) -> None:
    """Draws the Group Tabs glyph filling `box` (x0, y0, x1, y1) in `fill`."""
    x0, y0, x1, y1 = box
    w, h = x1 - x0, y1 - y0

    def rect(ax: float, ay: float, bx: float, by: float, r: float = 0.0) -> None:
        draw.rounded_rectangle((x0 + ax * w, y0 + ay * h, x0 + bx * w, y0 + by * h), radius=r * w, fill=fill)

    for group in GROUPS:
        for left, right in group:
            rect(left, TAB_TOP, right, TAB_BOTTOM, TAB_RADIUS)
            rect(left, (TAB_TOP + TAB_BOTTOM) / 2, right, TAB_BOTTOM)  # square bottom corners
        rect(group[0][0], BAR_TOP, group[-1][1], BAR_BOTTOM)


SUPERSAMPLE = 8

# Corner radius and glyph inset, in pixels, for the ribbon icon sizes; other sizes scale
# the 32 px values.
TILE_SPECS = {
    32: (3, 3),
    16: (1.5, 1),
}


def tile(size: int, tile_color=TILE, glyph_color=BLUE, glyph=draw_splash) -> Image.Image:
    """The glyph (`draw_splash` by default, or `draw_group_tabs`) on a full-bleed rounded
    tile, `size` px square, RGBA with transparent corners. Supersampled and box-filtered
    down, so the edges anti-alias (as gantt-it's icons)."""
    radius, inset = TILE_SPECS.get(size, (3 * size / 32, 3 * size / 32))
    s = SUPERSAMPLE
    big = Image.new("RGBA", (size * s, size * s), (0, 0, 0, 0))
    draw = ImageDraw.Draw(big)
    draw.rounded_rectangle((0, 0, size * s - 1, size * s - 1), radius=radius * s, fill=tile_color)
    glyph(draw, (inset * s, inset * s, (size - inset) * s, (size - inset) * s), glyph_color)
    return big.reduce(s)
