#!/usr/bin/env python3
"""Step 3: composite renders/<id>.png onto item tiles and install them in the game.

Writes Assets/Art/Icons/Elements/<id>.png, lists the ids in Assets/Art/Icons/rendered_icons.txt
(ElementIconGenerator skips those ids so it can't overwrite them with text tiles), and saves
renders/contact_sheet.png showing each icon at the sizes the UI draws. Requires Pillow.
"""
import json
import pathlib

from PIL import Image, ImageDraw, ImageFont

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
ICONS = ROOT / "Assets" / "Art" / "Icons"
FONT = ROOT / "Assets" / "TextMesh Pro" / "Fonts" / "LiberationSans.ttf"

# Same tile as ElementIconGenerator draws for non-element items.
OUTER, BORDER, INTERIOR = (13, 17, 23, 255), (110, 118, 129, 255), (66, 73, 81, 255)
SIZE, SUPERSAMPLE = 256, 4
MOLECULE_BOX = 196
UI_SIZES = (48, 28, 18)  # .codex-icon, .item-icon, .recipe-input-icon


def make_tile():
    ss = SUPERSAMPLE
    side = SIZE * ss
    tile = Image.new("RGBA", (side, side), OUTER)
    draw = ImageDraw.Draw(tile)
    draw.rounded_rectangle([12 * ss, 12 * ss, side - 12 * ss, side - 12 * ss], radius=30 * ss, fill=BORDER)
    draw.rounded_rectangle([20 * ss, 20 * ss, side - 20 * ss, side - 20 * ss], radius=23 * ss, fill=INTERIOR)
    return tile


def compose(render_path):
    tile = make_tile()
    side = SIZE * SUPERSAMPLE
    molecule = Image.open(render_path).convert("RGBA")
    molecule = molecule.crop(molecule.getbbox())
    scale = MOLECULE_BOX * SUPERSAMPLE / max(molecule.size)
    molecule = molecule.resize((int(molecule.width * scale), int(molecule.height * scale)), Image.LANCZOS)
    tile.alpha_composite(molecule, ((side - molecule.width) // 2, (side - molecule.height) // 2))
    return tile.resize((SIZE, SIZE), Image.LANCZOS)


def contact_sheet(icons, path):
    cell, pad, per_row = 150, 14, 7
    rows = (len(icons) + per_row - 1) // per_row
    row_height = cell + 100
    sheet = Image.new("RGBA", (per_row * (cell + pad) + pad, rows * row_height), (24, 28, 34, 255))
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.truetype(str(FONT), 13)
    for index, (item_id, icon) in enumerate(icons.items()):
        x = pad + (index % per_row) * (cell + pad)
        y = (index // per_row) * row_height
        draw.text((x, y + 6), item_id, font=font, fill="white")
        sheet.alpha_composite(icon.resize((cell, cell), Image.LANCZOS), (x, y + 24))
        cursor = x
        for size in UI_SIZES:
            sheet.alpha_composite(icon.resize((size, size), Image.LANCZOS), (cursor, y + cell + 34))
            cursor += size + 10
    sheet.save(path)


def main():
    manifest = json.loads((HERE / "molecules.json").read_text(encoding="utf-8"))
    icons = {}
    for item_id in manifest["molecules"]:
        icons[item_id] = compose(HERE / "renders" / f"{item_id}.png")
        icons[item_id].save(ICONS / "Elements" / f"{item_id}.png")
    ids = sorted(icons)
    (ICONS / "rendered_icons.txt").write_text("\n".join(ids) + "\n", encoding="utf-8", newline="\n")
    contact_sheet(icons, HERE / "renders" / "contact_sheet.png")
    print(f"Installed {len(ids)} icons: {', '.join(ids)}")


if __name__ == "__main__":
    main()
