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

from glyph import BLUE, BLUE_ON_DARK, TILE, TILE_DARK, tile

# variant suffix -> (tile, glyph)
VARIANTS = {
    "": (TILE, BLUE),
    "_dark": (TILE_DARK, BLUE_ON_DARK),
}
SIZES = (32, 16)

OUT = Path(__file__).resolve().parent.parent / "src" / "SplashIt.Revit" / "Resources" / "icons"


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for suffix, (tile_color, glyph_color) in VARIANTS.items():
        for size in SIZES:
            path = OUT / f"splash_{size}{suffix}.png"
            tile(size, tile_color, glyph_color).save(path, optimize=True)
            print(f"wrote {path.relative_to(OUT.parents[3])}")


if __name__ == "__main__":
    main()
