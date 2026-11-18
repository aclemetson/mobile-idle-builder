"""Belt-item mockups, round 2: every molecule as a ball-and-stick model. Runs INSIDE Blender.

    exec this file's text through the Blender MCP's execute_blender_code

Reads Assets/Resources/BeltMolecules.json (built by build_molecules.py), the same data the game uses, and
applies the same sizing rule as BeltMoleculeData.cs so the mockup matches what ships. Clears the open scene.
"""
import json
import math
import os
import pathlib

import bpy
from mathutils import Vector

ROOT = pathlib.Path(r"E:\Programming\unity\mobile-idle-builder")
PARTICLES = ROOT / "scripts" / "belt_particles" / "mockup_blender.py"
exec(compile(PARTICLES.read_text(encoding="utf-8").split("# Unity is Y-up")[0], str(PARTICLES), "exec"))

OUT = ROOT / "scripts" / "belt_items" / "renders"
DATA = json.loads((ROOT / "Assets" / "Resources" / "BeltMolecules.json").read_text(encoding="utf-8"))

# Sizing rule (keep in step with BeltMoleculeData.cs)
BALL = 0.6            # atom ball radius = element radius x BALL (Angstrom)
BOND = 0.11           # bond tube radius (Angstrom); x1.35 for double / triple bonds
CHUNK_BALL, CHUNK_BOND = 1.35, 1.6   # hide_h molecules: thicker so the skeleton reads
MAX_SCALE = 0.1       # tiles per Angstrom for small molecules
FIT_RADIUS = 0.17     # the whole model fits in this radius (item diameter 0.36 minus a margin)
FROST = "#CDEBFF"


def layout(mol, radii):
    atoms = [a for a in mol["atoms"] if not (mol["hideH"] and a["e"] == "H")]
    keep = {id(a): i for i, a in enumerate(atoms)}
    index = {i: keep.get(id(a)) for i, a in enumerate(mol["atoms"])}
    chunk_ball = CHUNK_BALL if mol["hideH"] else 1.0
    chunk_bond = CHUNK_BOND if mol["hideH"] else 1.0
    balls = [(Vector((a["x"], a["y"], a["z"])), radii[a["e"]] * BALL * chunk_ball, a["e"]) for a in atoms]
    extent = max(p.length + r for p, r, _ in balls)
    scale = min(MAX_SCALE, FIT_RADIUS / extent)
    bonds = []
    for b in mol["bonds"]:
        i, j = index[b["a"]], index[b["b"]]
        if i is None or j is None:
            continue
        bonds.append((i, j, BOND * chunk_bond * (1.35 if b["o"] >= 2 else 1.0)))
    return balls, bonds, scale


def build_molecule(prefix, loc, mol, mats, radii, tumble):
    balls, bonds, s = layout(mol, radii)
    from mathutils import Euler
    e = Euler((math.radians(20), math.radians(10), tumble))
    pts = []
    for k, (p, r, el) in enumerate(balls):
        q = p.copy()
        q.rotate(e)
        q = Vector(loc) + q * s
        pts.append(q)
        sphere(f"{prefix}_a{k}", q, 2 * r * s, mats[el], segments=14, rings=9)
    for k, (i, j, br) in enumerate(bonds):
        mid = (pts[i] + pts[j]) / 2
        tube(f"{prefix}_b{k}a", pts[i], mid, br * s, mats[balls[i][2]])
        tube(f"{prefix}_b{k}b", mid, pts[j], br * s, mats[balls[j][2]])
    if mol["id"] == "liquid_hydrogen":
        sphere(f"{prefix}_frost", Vector(loc), 0.27, mats["frost"], segments=20, rings=12)


def label(text, loc):
    curve = bpy.data.curves.new(f"lbl_{text}", "FONT")
    curve.body = text
    curve.size = 0.085
    curve.align_x = "CENTER"
    obj = bpy.data.objects.new(f"lbl_{text}", curve)
    bpy.context.collection.objects.link(obj)
    obj.location = loc
    obj.rotation_euler = (math.radians(53), 0, 0)
    obj.data.materials.append(flat_material(f"lbl_mat_{text}", "#C9D1D9", top_light=0.0))


def render(cam, filename):
    scene = bpy.context.scene
    scene.camera = cam
    OUT.mkdir(parents=True, exist_ok=True)
    scene.render.filepath = os.fspath(OUT / filename)
    bpy.ops.render.render(write_still=True)


def main():
    clear_scene()
    setup_render()
    radii = {e["symbol"]: e["radius"] for e in DATA["elements"]}
    mats = {e["symbol"]: flat_material(f"el_{e['symbol']}", "#" + e["color"], top_light=0.3) for e in DATA["elements"]}
    mats["frost"] = shell_material("frost", FROST, 0.18, rim=0.7)
    mats["belt"] = flat_material("belt", BELT, top_light=0.0)
    mols = DATA["molecules"]
    per_row = 6
    rows = math.ceil(len(mols) / per_row)
    for r in range(rows):
        bpy.ops.mesh.primitive_plane_add(size=1, location=(1.25 * (per_row - 1) / 2, r * 0.8, 0.0))
        belt = bpy.context.active_object
        belt.scale = (1.25 * per_row + 0.4, 0.5, 1)
        belt.data.materials.append(mats["belt"])
    for k, mol in enumerate(mols):
        x, y = (k % per_row) * 1.25, (k // per_row) * 0.8
        build_molecule(mol["id"], (x, y, HEIGHT), mol, mats, radii, tumble=0.6 * k)
        label(mol["id"].replace("_", " "), (x, y - 0.28, 0.02))
    bpy.context.scene.render.resolution_x, bpy.context.scene.render.resolution_y = (1800, 1100)
    centre = Vector((1.25 * (per_row - 1) / 2, (rows - 1) * 0.8 / 2, HEIGHT))
    render(camera("cam_close", centre, Vector((0, -6, 8)).normalized() * 9.0, 48), "molecules.png")
    render(camera("cam_game", centre, Vector((0, -6, 8)).normalized() * 10.0, 60), "molecules_game.png")
    return os.fspath(OUT)


if __name__ == "__main__" or True:
    print("molecule mockups ->", main())
