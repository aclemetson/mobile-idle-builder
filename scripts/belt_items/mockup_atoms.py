"""Belt-item mockups, round 1: atoms (nucleus + shell rings) and the four remaining particles. Runs INSIDE Blender.

    exec this file's text through the Blender MCP's execute_blender_code
    (or: blender --background --python scripts/belt_items/mockup_atoms.py)

Mockup only: approved looks are rebuilt in code in Unity (see docs/agents/visual-design.md, "Belt item visuals").
Reuses the materials and camera of scripts/belt_particles/mockup_blender.py so the new items sit next to the
approved electron/quark/nucleon look. Clears every object in the open scene.

Atom rule (proposal):
  - nucleus: Z <= 10 a cluster of proton/neutron beads (H is one proton); heavier a beaded ball in the element
    tile's regime colour.
  - one faint ring per occupied shell (= periodic-table row), the outer electrons as teal beads on the outer ring.
  - accents: noble gas -> soft glow; metal -> brighter, glossier nucleus; radioactive -> decay sparks.
"""
import math
import os
import pathlib
import random

import bpy
from mathutils import Euler, Vector

PARTICLES = pathlib.Path(r"E:\Programming\unity\mobile-idle-builder\scripts\belt_particles\mockup_blender.py")
_helpers = PARTICLES.read_text(encoding="utf-8").split("# Unity is Y-up")[0]
exec(compile(_helpers, str(PARTICLES), "exec"))   # rgba, flat_material, shell_material, glow_material, sphere, tube,
#                                                  clear_scene, setup_render, camera, colours, ITEM, HEIGHT

OUT = pathlib.Path(r"E:\Programming\unity\mobile-idle-builder\scripts\belt_items\renders")

# Element tile regime colours (docs/agents/visual-design.md, "Item tile icons").
REGIME = [(1, "#d2a8ff"), (26, "#3fb950"), (80, "#f0883e"), (92, "#58a6ff"), (100, "#db61a2"), (118, "#e3b341")]
NOBLE = {2, 10, 18, 36, 54, 86, 118}
METAL_RANGES = [(3, 4), (11, 13), (19, 32), (37, 51), (55, 84), (87, 118)]
RING = "#9FE8E1"          # faint teal orbit lines (electron family)
SPARK = "#E6FF7A"         # decay sparks
BEAD = 0.058              # nucleon bead diameter inside a nucleus

SUBSHELLS = ["1s", "2s", "2p", "3s", "3p", "4s", "3d", "4p", "5s", "4d", "5p", "6s", "4f", "5d", "6p", "7s", "5f",
             "6d", "7p"]
CAP = {"s": 2, "p": 6, "d": 10, "f": 14}


def shells(z):
    """Electrons per principal shell n (aufbau order; exceptions like Cr/Cu don't matter at belt size)."""
    per_n = {}
    left = z
    for sub in SUBSHELLS:
        if left <= 0:
            break
        take = min(left, CAP[sub[1]])
        per_n[int(sub[0])] = per_n.get(int(sub[0]), 0) + take
        left -= take
    return [per_n[n] for n in sorted(per_n)]


def regime(z):
    return next(c for top, c in REGIME if z <= top)


def is_metal(z):
    return any(a <= z <= b for a, b in METAL_RANGES)


def packed(n, radius, seed):
    """n points roughly filling a ball (golden-spiral shells), lightly jittered."""
    rnd = random.Random(seed)
    if n == 1:
        return [Vector((0, 0, 0))]
    pts = []
    for i in range(n):
        f = (i + 0.5) / n
        r = radius * (f ** (1 / 3))
        phi = math.acos(1 - 2 * ((i * 0.618034) % 1))
        th = i * 2.399963
        pts.append(Vector((r * math.sin(phi) * math.cos(th), r * math.sin(phi) * math.sin(th), r * math.cos(phi)))
                   + Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))) * radius * 0.08)
    return pts


def ring(name, loc, radius, tilt, mat, thickness=0.0045):
    bpy.ops.mesh.primitive_torus_add(major_radius=radius, minor_radius=thickness, major_segments=48,
                                     minor_segments=6, location=loc)
    obj = bpy.context.active_object
    obj.name = name
    obj.rotation_euler = tilt
    obj.data.materials.append(mat)
    return obj


def build_nucleus(prefix, loc, z, n_neutrons, mats):
    loc = Vector(loc)
    if z <= 10:
        count = z + n_neutrons
        radius = BEAD * 0.42 * count ** (1 / 3) if count > 1 else 0
        pts = packed(count, radius, z * 7 + n_neutrons)
        flavours = ["p"] * z + ["n"] * n_neutrons
        random.Random(z).shuffle(flavours)
        for i, (p, f) in enumerate(zip(pts, flavours)):
            sphere(f"{prefix}_{f}{i}", loc + p, BEAD, mats["proton"] if f == "p" else mats["neutron"],
                   segments=12, rings=8)
        return max(radius + BEAD / 2, BEAD / 2)
    # Heavy: a beaded ball tinted by regime; metals use the brighter, glossier material.
    radius = 0.055 + 0.03 * min(1.0, z / 92)
    key = f"heavy_{z}"
    for i, p in enumerate(packed(26, radius - 0.016, z)):
        sphere(f"{prefix}_b{i}", loc + p, 0.034, mats[key], segments=10, rings=7)
    return radius


def build_atom(prefix, loc, z, n_neutrons, state, mats, radioactive=False):
    loc = Vector(loc)
    core = build_nucleus(prefix, loc, z, n_neutrons, mats)
    occ = shells(z)
    rnd = random.Random(z * 13)
    inner, outer = core + 0.035, ITEM * (0.42 + 0.06 * min(len(occ), 7) / 7)
    # Rings spread between the nucleus and the item edge; each tilted a little differently (3D orbits).
    for k, count in enumerate(occ):
        r = inner + (outer - inner) * ((k + 1) / len(occ)) if len(occ) > 1 else outer * 0.85
        # Face the game camera (it looks down ~53 deg, so a ring tipped 37 deg about X faces it), then a small
        # per-shell wobble so the rings read as 3D orbits, never edge-on.
        tilt = Euler((math.radians(37 + rnd.uniform(-15, 15)), math.radians(rnd.uniform(-15, 15)),
                      math.radians(rnd.uniform(0, 180))))
        last = k == len(occ) - 1
        ring(f"{prefix}_ring{k}", loc, r, tilt, mats["ring_outer" if last else "ring_inner"],
             thickness=0.0045 if last else 0.003)
        if last:   # outer (valence) electrons as beads, spaced evenly, advancing with the animation state
            for e in range(count):
                a = 2 * math.pi * e / count + state * 0.7
                p = Vector((r * math.cos(a), r * math.sin(a), 0))
                p.rotate(tilt)
                sphere(f"{prefix}_e{e}", loc + p, 0.032, mats["electron"], segments=10, rings=7)
    if z in NOBLE:
        sphere(f"{prefix}_glow", loc, outer * 2.25, mats["noble_glow"], segments=20, rings=12)
    if radioactive:
        srnd = random.Random(z + state * 101)
        for s in range(4):
            d = Vector((srnd.uniform(-1, 1), srnd.uniform(-1, 1), srnd.uniform(-0.3, 1))).normalized()
            start = loc + d * (core + 0.01)
            tube(f"{prefix}_spark{s}", start, start + d * srnd.uniform(0.05, 0.11) * (0.6 + 0.4 * state), 0.006,
                 mats["spark"])


# --------------------------------------------------------------------------- particles

def build_alpha(prefix, loc, state, mats):
    """A bare helium nucleus: 2p + 2n, no rings, faint violet halo, slight shake."""
    loc = Vector(loc)
    build_nucleus(prefix, loc + Vector((0.006 * state, 0, 0)), 2, 2, mats)
    sphere(f"{prefix}_halo", loc, 0.17, mats["alpha_halo"], segments=16, rings=10)


def build_beta(prefix, loc, state, mats):
    """A fast electron: bright teal bead with a comet streak trailing upstream (-X)."""
    loc = Vector(loc)
    sphere(f"{prefix}_core", loc, 0.06, mats["beta_core"])
    for i in range(6):
        t = (i + 1) / 6
        obj = sphere(f"{prefix}_tail{i}", loc + Vector((-0.045 - 0.05 * i * (1 + 0.15 * state), 0, 0)),
                     0.055 * (1 - t * 0.75), mats["beta_tail"], segments=10, rings=7)
        obj.scale = (1.6, 1, 1)


def build_neutrino(prefix, loc, state, mats):
    """A ghost: tiny white point, faint halo that fades in and out."""
    loc = Vector(loc)
    sphere(f"{prefix}_pt", loc, 0.018, mats["neutrino_core"], segments=8, rings=6)
    sphere(f"{prefix}_halo", loc, (0.12, 0.2, 0.07)[state], mats[f"neutrino_halo{state}"], segments=14, rings=9)


def build_gamma(prefix, loc, state, mats):
    """A wave packet: a short sine ribbon under a Gaussian envelope, bright yellow-white."""
    loc = Vector(loc)
    pts = []
    for i in range(41):
        x = -0.2 + 0.4 * i / 40
        env = math.exp(-(x / 0.09) ** 2)
        pts.append(loc + Vector((x, 0, 0.07 * env * math.sin(x * 70 + state * 1.6))))
    for i in range(40):
        tube(f"{prefix}_w{i}", pts[i], pts[i + 1], 0.009, mats["gamma"])
    sphere(f"{prefix}_glow", loc, 0.26, mats["gamma_glow"], segments=16, rings=10)


# --------------------------------------------------------------------------- sheet

ATOMS = [  # label, Z, neutrons, radioactive
    ("H  hydrogen", 1, 0, False),
    ("D  deuterium (isotope)", 1, 1, False),
    ("He helium (noble)", 2, 2, False),
    ("C  carbon", 6, 6, False),
    ("O  oxygen", 8, 8, False),
    ("Ne neon (noble)", 10, 10, False),
    ("Fe iron (metal)", 26, 30, False),
    ("U  uranium (radioactive)", 92, 146, True),
]


def materials():
    mats = {
        "proton": flat_material("proton", PROTON),
        "neutron": flat_material("neutron", NEUTRON),
        "electron": flat_material("electron_bead", ELECTRON, top_light=0.2),
        "ring_inner": glow_material("ring_inner", RING, 0.35, alpha_falloff=False),
        "ring_outer": glow_material("ring_outer", RING, 0.7, alpha_falloff=False),
        "noble_glow": glow_material("noble_glow", "#C9F7FF", 0.16),
        "spark": flat_material("spark", SPARK, top_light=0.0),
        "alpha_halo": glow_material("alpha_halo", PROTON, 0.18),
        "beta_core": flat_material("beta_core", "#E8FFFD", top_light=0.0),
        "beta_tail": glow_material("beta_tail", ELECTRON, 0.45, alpha_falloff=False),
        "neutrino_core": flat_material("neutrino_core", "#FFFFFF", top_light=0.0),
        "gamma": flat_material("gamma", "#FFF3B0", top_light=0.0),
        "gamma_glow": glow_material("gamma_glow", "#FFE27A", 0.22),
        "belt": flat_material("belt", BELT, top_light=0.0),
    }
    for s, a in enumerate((0.22, 0.32, 0.1)):
        mats[f"neutrino_halo{s}"] = glow_material(f"neutrino_halo{s}", "#DDE6FF", a)
    for _, z, _, _ in ATOMS:
        if z > 10:
            mats[f"heavy_{z}"] = flat_material(f"heavy_{z}", regime(z), top_light=0.55 if is_metal(z) else 0.35)
    return mats


def label(text, loc):
    curve = bpy.data.curves.new(f"lbl_{text}", "FONT")
    curve.body = text
    curve.size = 0.16
    obj = bpy.data.objects.new(f"lbl_{text}", curve)
    bpy.context.collection.objects.link(obj)
    obj.location = loc
    obj.rotation_euler = (math.radians(53), 0, 0)
    obj.data.materials.append(flat_material(f"lbl_mat_{text}", "#C9D1D9", top_light=0.0))


def build_rows(rows, mats, y0=0.0):
    for row, (name, build) in enumerate(rows):
        y = y0 + row * 0.75
        bpy.ops.mesh.primitive_plane_add(size=1, location=(1.0, y, 0.0))
        belt = bpy.context.active_object
        belt.name = f"belt_{name}"
        belt.scale = (3.6, 0.55, 1)
        belt.data.materials.append(mats["belt"])
        label(name, (-1.75, y - 0.05, 0.02))
        for state in range(3):
            build(Vector((state * 1.0, y, HEIGHT)), state)
    return len(rows)


def render(cam, filename):
    scene = bpy.context.scene
    scene.camera = cam
    OUT.mkdir(parents=True, exist_ok=True)
    scene.render.filepath = os.fspath(OUT / filename)
    bpy.ops.render.render(write_still=True)


def sheet(filename, make_rows):
    clear_scene()
    setup_render()
    mats = materials()            # after clear_scene, which deletes unused materials
    rows = make_rows(mats)
    bpy.context.scene.render.resolution_x, bpy.context.scene.render.resolution_y = (1600, 300 + 150 * len(rows))
    n = build_rows(rows, mats)
    centre = Vector((0.3, (n - 1) * 0.75 / 2, HEIGHT))
    span = n * 0.75 + 3.5
    close = camera("cam_close", centre, Vector((0, -6, 8)).normalized() * span * 0.95, 48)
    render(close, filename)
    # Game distance: the game camera (Unity offset 0,8,-6), centred on the first three rows.
    game = camera("cam_game", Vector((1.0, 0.75, HEIGHT)), (0, -6, 8), 60)
    render(game, filename.replace(".png", "_game.png"))


def atom_rows(mats):
    def row(z, nn, rad):
        return lambda p, s: build_atom(f"z{z}_{nn}_{s}", p, z, nn, s, mats, rad)
    return [(lbl, row(z, nn, rad)) for lbl, z, nn, rad in ATOMS]


def particle_rows(mats):
    return [
        ("alpha particle", lambda p, s: build_alpha(f"alpha_{s}", p, s, mats)),
        ("beta particle", lambda p, s: build_beta(f"beta_{s}", p, s, mats)),
        ("neutrino", lambda p, s: build_neutrino(f"nu_{s}", p, s, mats)),
        ("gamma photon", lambda p, s: build_gamma(f"gamma_{s}", p, s, mats)),
    ]


def main():
    sheet("atoms.png", atom_rows)
    sheet("particles.png", particle_rows)
    return os.fspath(OUT)


if __name__ == "__main__" or True:
    print("belt item mockups ->", main())
