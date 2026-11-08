"""Builds Assets/Fonts/CurrencyIcons.ttf: the currency symbols as our own drawn glyphs.

  python -m pip install fonttools
  python scripts/currency_icons/build_font.py

Then run MobileIdleBuilder > Build Currency Icon Font in Unity. See docs/agents/visual-design.md.

Shapes live on a 24x24 grid with y pointing down. Each icon is a list of closed contours; solid() and
hole() set the winding, so a hole cuts out of the solid around it.

A symbol only shows from this font if the HUD's default font (NotInter) lacks it, which is why crystals
are U+2666 and not the U+25C6 NotInter already has.
"""
import math
from pathlib import Path

from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen

OUT = Path(__file__).resolve().parents[2] / "Assets" / "Fonts" / "CurrencyIcons.ttf"
UNITS_PER_EM = 1000
ASCENT, DESCENT = 800, -200
ICON_SCALE = 38.33      # the 24-unit grid fills 92% of the em
CENTER_Y = 360          # about the middle of NotInter's capitals, so icons sit level with digits
SIDE_BEARING = 40


def circle(cx, cy, r, n=48):
    return [(cx + r * math.cos(2 * math.pi * i / n), cy + r * math.sin(2 * math.pi * i / n)) for i in range(n)]


def area(contour):
    return sum(contour[i][0] * contour[(i + 1) % len(contour)][1] - contour[(i + 1) % len(contour)][0] * contour[i][1]
               for i in range(len(contour))) / 2


def solid(contour):
    return contour if area(contour) > 0 else contour[::-1]


def hole(contour):
    return contour if area(contour) < 0 else contour[::-1]


def star4(cx, cy, radius, waist, n=10):
    """Four points on the axes joined by curves that pull in to the centre: round body, sharp tips."""
    points = []
    for quarter in range(4):
        a0, a1 = quarter * math.pi / 2, (quarter + 1) * math.pi / 2
        p0 = (cx + radius * math.cos(a0), cy + radius * math.sin(a0))
        p2 = (cx + radius * math.cos(a1), cy + radius * math.sin(a1))
        mid = (a0 + a1) / 2
        ctrl = (cx + radius * waist * math.cos(mid), cy + radius * waist * math.sin(mid))
        for i in range(n):
            t = i / n
            points.append(((1 - t) ** 2 * p0[0] + 2 * t * (1 - t) * ctrl[0] + t * t * p2[0],
                           (1 - t) ** 2 * p0[1] + 2 * t * (1 - t) * ctrl[1] + t * t * p2[1]))
    return points


def arc_band(cx, cy, r0, r1, a0, a1, n=14):
    angles = [math.radians(a0 + (a1 - a0) * i / n) for i in range(n + 1)]
    return [(cx + r1 * math.cos(a), cy + r1 * math.sin(a)) for a in angles] + \
           [(cx + r0 * math.cos(a), cy + r0 * math.sin(a)) for a in reversed(angles)]


def bolt(scale):
    base = [(13.8, 1.5), (4.5, 13.4), (11, 13.4), (9.2, 22.5), (19.5, 9.8), (12.8, 9.8)]
    return [(12 + (x - 12) * scale, 12 + (y - 12) * scale) for x, y in base]


ICONS = {
    # Entropy: Maxwell's Demon's chamber, big slow particles one side of the wall, small fast ones the other.
    0x25C8: ("entropy", [
        solid(circle(12, 12, 11.3)), hole(circle(12, 12, 8.9)),
        solid([(11.05, 3.7), (12.95, 3.7), (12.95, 20.3), (11.05, 20.3)]),
        solid(circle(7.2, 9.3, 1.7)), solid(circle(7.8, 14.9, 1.7)),
        solid(circle(16.6, 8.2, 1.05)), solid(circle(15.8, 12.3, 1.05)), solid(circle(17.5, 15.9, 1.05))]),
    # Prestige: a star held by four arcs, a new cycle around the same centre.
    0x2726: ("prestige", [solid(star4(12, 12, 7.4, .2))] +
             [solid(arc_band(12, 12, 9.2, 11.3, a - 27, a + 27)) for a in (45, 135, 225, 315)]),
    # Crystals: a brilliant-cut gem with its facets as gaps.
    0x2666: ("crystal", [solid(p) for p in (
        [(7, 3.5), (8.8, 3.5), (7.7, 8.6), (2.8, 8.6)], [(10, 3.5), (14, 3.5), (15.1, 8.6), (8.9, 8.6)],
        [(15.2, 3.5), (17, 3.5), (21.2, 8.6), (16.3, 8.6)], [(2.8, 9.9), (7.6, 9.9), (11.3, 20.2)],
        [(8.9, 9.9), (15.1, 9.9), (12, 20.9)], [(16.4, 9.9), (21.2, 9.9), (12.7, 20.2)])]),
    # Power: a bolt in a ring.
    0x26A1: ("power", [solid(circle(12, 12, 11.4)), hole(circle(12, 12, 9.3)), solid(bolt(.68))]),
}


def to_font(x, y):
    # Flipping y keeps the outline visually clockwise, which is what TrueType wants for a solid.
    return round(SIDE_BEARING + x * ICON_SCALE), round(CENTER_Y + (12 - y) * ICON_SCALE)


def draw(contours):
    pen = TTGlyphPen(None)
    for contour in contours:
        pen.moveTo(to_font(*contour[0]))
        for point in contour[1:]:
            pen.lineTo(to_font(*point))
        pen.closePath()
    return pen.glyph()


def main():
    advance = round(2 * SIDE_BEARING + 24 * ICON_SCALE)
    order = [".notdef", "space"] + [name for name, _ in ICONS.values()]
    glyphs = {".notdef": TTGlyphPen(None).glyph(), "space": TTGlyphPen(None).glyph()}
    glyphs.update({name: draw(contours) for name, contours in ICONS.values()})

    fb = FontBuilder(UNITS_PER_EM, isTTF=True)
    fb.setupGlyphOrder(order)
    fb.setupCharacterMap({0x20: "space", **{code: name for code, (name, _) in ICONS.items()}})
    fb.setupGlyf(glyphs)
    fb.setupHorizontalMetrics({name: (advance if name != ".notdef" else 500, 0) for name in order})
    fb.setupHorizontalHeader(ascent=ASCENT, descent=DESCENT)
    fb.setupNameTable({"familyName": "Currency Icons", "styleName": "Regular"})
    fb.setupOS2(sTypoAscender=ASCENT, sTypoDescender=DESCENT, usWinAscent=ASCENT, usWinDescent=-DESCENT)
    fb.setupPost()
    OUT.parent.mkdir(parents=True, exist_ok=True)
    fb.save(OUT)
    print(f"Wrote {OUT} ({OUT.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
