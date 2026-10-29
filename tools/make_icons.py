#!/usr/bin/env python3
"""Generate the Splash-It ribbon icons (spec, App).

Adapted from gantt-it tools/make_icons.py (gantt-it commit aa83f35, MIT, same owner):
the same tile style (a full-bleed light-grey #D9D9D9 tile with anti-aliased rounded,
transparent corners and a flat single-colour glyph, supersampled and box-filtered),
and the same dark variant (a lighter blue on a dark tile, so the button doesn't sit as
a bright square on Revit's dark ribbon). The add-in swaps them when Revit's theme
changes. The glyph is the Splash-It splash from tools/glyph.py, which the installer
branding reuses.

Usage:  python3 tools/make_icons.py
Writes: src/SplashIt.Revit/Resources/icons/splash_32.png, splash_16.png,
        splash_32_dark.png and splash_16_dark.png
Needs:  Pillow
"""

from pathlib import Path

from PIL import Image, ImageDraw

from glyph import BLUE, BLUE_ON_DARK, TILE, TILE_DARK, draw_splash

# variant suffix -> (tile, glyph)
VARIANTS = {
    "": (TILE, BLUE),
    "_dark": (TILE_DARK, BLUE_ON_DARK),
}
SUPERSAMPLE = 8

# size -> (corner radius px, glyph inset px)
SPECS = {
    32: (3, 3),
    16: (1.5, 1),
}

OUT = Path(__file__).resolve().parent.parent / "src" / "SplashIt.Revit" / "Resources" / "icons"


def render(size: int, radius: float, inset: float, tile, glyph) -> Image.Image:
    s = SUPERSAMPLE
    big = Image.new("RGBA", (size * s, size * s), (0, 0, 0, 0))
    draw = ImageDraw.Draw(big)
    draw.rounded_rectangle((0, 0, size * s - 1, size * s - 1), radius=radius * s, fill=tile)
    draw_splash(draw, (inset * s, inset * s, (size - inset) * s, (size - inset) * s), glyph)
    # Box-filter downsampling: the tile edge and the glyph anti-alias.
    return big.reduce(s)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for suffix, (tile, glyph) in VARIANTS.items():
        for size, (radius, inset) in SPECS.items():
            path = OUT / f"splash_{size}{suffix}.png"
            render(size, radius, inset, tile, glyph).save(path, optimize=True)
            print(f"wrote {path.relative_to(OUT.parents[3])}")


if __name__ == "__main__":
    main()
