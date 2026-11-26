"""Builds the Google Play feature graphic (1024x500, 24-bit PNG) and hi-res icon (512x512) from the app icon.

    python scripts/store/feature_graphic.py [--font-dir DIR] [--out PATH]

Layout: the icon's atom-in-a-ring on the left, the game name and tagline on the right, on a starfield that
matches the icon's background. Text uses Inter (SIL Open Font License), which ships with the Unity Editor.
Keep the title and atom inside the middle ~80% (Play crops and overlays the edges in some placements).
"""
import argparse
import math
import random
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[2]
ICON = ROOT / "Assets/Art/Icons/quantum_foundry_icon_1024.png"
DEFAULT_FONTS = Path(r"C:/Program Files/Unity/Hub/Editor/6000.3.6f1/Editor/Data/Resources/Fonts")
DEFAULT_OUT = ROOT / "docs/store/feature-graphic.png"

W, H = 1024, 500
TITLE = "Quantum Foundry"
TAGLINE = "Build matter itself"
SUBLINE = "An idle factory from quarks to atoms"

GOLD = (255, 205, 125)
WARM_WHITE = (255, 244, 222)
ICE = (196, 232, 240)


def background(icon: Image.Image, centre) -> Image.Image:
    # The icon's radial vignette (navy behind the atom, near-black at the corners), re-centred on the atom so
    # the pasted icon disc blends in. Then a sprinkle of stars like the icon's.
    inner = icon.getpixel((512, 200))
    outer = icon.getpixel((20, 20))
    bg = Image.new("RGB", (W, H))
    px = bg.load()
    cx, cy = centre
    reach = 760.0
    for y in range(H):
        for x in range(W):
            t = min(1.0, math.hypot(x - cx, y - cy) / reach) ** 0.8
            px[x, y] = tuple(int(inner[i] + (outer[i] - inner[i]) * t) for i in range(3))
    rng = random.Random(7)
    draw = ImageDraw.Draw(bg)
    for _ in range(140):
        x, y = rng.randrange(W), rng.randrange(H)
        a = rng.randint(50, 150)
        r = rng.choice([0, 0, 0, 1])
        draw.ellipse((x - r, y - r, x + r, y + r), fill=(a, a, min(255, a + 20)))
    return bg


def atom(icon: Image.Image, size: int) -> Image.Image:
    # The ring sits on a ~380 px radius; crop a little outside it and feather the edge into the background.
    crop = icon.crop((112, 112, 912, 912)).resize((size, size), Image.LANCZOS).convert("RGBA")
    mask = Image.new("L", (size, size), 0)
    d = ImageDraw.Draw(mask)
    pad = int(size * 0.02)
    d.ellipse((pad, pad, size - pad, size - pad), fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(size * 0.02))
    crop.putalpha(mask)
    return crop


def glow_text(base: Image.Image, xy, text, font, fill, glow, radius):
    layer = Image.new("RGBA", base.size, (0, 0, 0, 0))
    ImageDraw.Draw(layer).text(xy, text, font=font, fill=glow + (150,))
    layer = layer.filter(ImageFilter.GaussianBlur(radius))
    base.alpha_composite(layer)
    ImageDraw.Draw(base).text(xy, text, font=font, fill=fill + (255,))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--font-dir", type=Path, default=DEFAULT_FONTS)
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT)
    args = ap.parse_args()

    icon = Image.open(ICON).convert("RGB")
    a = atom(icon, 380)
    ax, ay = 104, (H - a.height) // 2
    img = background(icon, (ax + a.width // 2, ay + a.height // 2)).convert("RGBA")
    img.alpha_composite(a, (ax, ay))

    semibold = ImageFont.truetype(str(args.font_dir / "Inter-SemiBold.ttf"), 76)
    regular = ImageFont.truetype(str(args.font_dir / "Inter-Regular.ttf"), 34)
    small = ImageFont.truetype(str(args.font_dir / "Inter-Regular.ttf"), 23)

    tx = ax + a.width + 36
    # Two-line title so it stays large inside the safe area.
    words = TITLE.split()
    glow_text(img, (tx, 122), words[0], semibold, WARM_WHITE, GOLD, 10)
    glow_text(img, (tx, 204), words[1], semibold, WARM_WHITE, GOLD, 10)
    d = ImageDraw.Draw(img)
    d.line((tx + 4, 304, tx + 290, 304), fill=GOLD + (220,), width=3)
    d.text((tx + 2, 320), TAGLINE, font=regular, fill=GOLD + (255,))
    d.text((tx + 2, 366), SUBLINE, font=small, fill=ICE + (230,))
    right = tx + 2 + d.textlength(SUBLINE, font=small)
    if right > W * 0.92:
        print(f"warning: text reaches x={right:.0f}, outside the safe area")

    args.out.parent.mkdir(parents=True, exist_ok=True)
    img.convert("RGB").save(args.out, "PNG")  # Play wants no alpha channel
    print(f"wrote {args.out} ({W}x{H})")

    # Play's hi-res icon: 512x512, 32-bit PNG.
    icon_out = args.out.parent / "icon-512.png"
    icon.resize((512, 512), Image.LANCZOS).convert("RGBA").save(icon_out, "PNG")
    print(f"wrote {icon_out} (512x512)")


if __name__ == "__main__":
    main()
