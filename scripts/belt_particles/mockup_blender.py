"""Belt-particle mockups (electron, up/down quark, proton, neutron). Runs INSIDE Blender.

    blender --background --python scripts/belt_particles/mockup_blender.py
    (or exec this file's text through the Blender MCP's execute_blender_code)

Mockup only: the shipped visuals are code-built in Unity (see docs/agents/visual-design.md).
Builds a review sheet: one belt row per particle, three animation states along each row
(e.g. proton solid -> fading -> see-through), plus a game-distance render. Materials are
emission-based so the look approximates the game's URP Unlit shaders.

Clears every object in the open scene, so run it in a throwaway scene.
"""
import math
import os
import pathlib
import random

import bpy
from mathutils import Vector

try:
    HERE = pathlib.Path(__file__).resolve().parent
except NameError:  # exec'd through the MCP: no __file__
    HERE = pathlib.Path(r"E:\Programming\unity\mobile-idle-builder\scripts\belt_particles")
OUT = HERE / "renders"

# Colours (sRGB hex). Up/down quarks are reused inside the nucleons.
UP = "#FF6B35"
DOWN = "#6E7BFF"
ELECTRON = "#4ECDC4"
PROTON = "#D2A8FF"
NEUTRON = "#8B949E"
GLUON = "#F2E86D"
BELT = "#2B3036"
GROUND = "#161B22"

ITEM = 0.36        # belt item diameter in tiles (game: 0.3-0.4)
HEIGHT = 0.3       # item float height (ConveyorVisualizer)
QUARK = 0.14       # loose quark bead diameter
INNER_QUARK = 0.085
RESOLUTION = (1600, 1000)


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def rgba(h, a=1.0):
    h = h.lstrip("#")
    return tuple(srgb_to_linear(int(h[i:i + 2], 16) / 255) for i in (0, 2, 4)) + (a,)


def node(nt, type_name):
    return nt.nodes.new(type_name)


def set_blended(mat):
    for attr, val in (("surface_render_method", "BLENDED"), ("blend_method", "BLEND")):
        try:
            setattr(mat, attr, val)
        except (AttributeError, TypeError):
            pass
    try:
        mat.use_transparency_overlap = True
    except AttributeError:
        pass


def flat_material(name, hex_color, top_light=0.35):
    """Emission with a fake top light (like CollectorStructure): brighter on +Z normals."""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = node(nt, "ShaderNodeOutputMaterial")
    emit = node(nt, "ShaderNodeEmission")
    geo = node(nt, "ShaderNodeNewGeometry")
    sep = node(nt, "ShaderNodeSeparateXYZ")
    ramp = node(nt, "ShaderNodeMapRange")
    mix = node(nt, "ShaderNodeMix")
    mix.data_type = "RGBA"
    base = rgba(hex_color)
    lit = tuple(min(1.0, c * (1 + top_light) + top_light * 0.25) for c in base[:3]) + (1,)
    dark = tuple(c * (1 - top_light) for c in base[:3]) + (1,)
    nt.links.new(geo.outputs["Normal"], sep.inputs[0])
    nt.links.new(sep.outputs["Z"], ramp.inputs["Value"])
    ramp.inputs["From Min"].default_value = -1
    ramp.inputs["From Max"].default_value = 1
    nt.links.new(ramp.outputs["Result"], mix.inputs["Factor"])
    mix.inputs["A"].default_value = dark
    mix.inputs["B"].default_value = lit
    nt.links.new(mix.outputs["Result"], emit.inputs["Color"])
    nt.links.new(emit.outputs[0], out.inputs["Surface"])
    return mat


def shell_material(name, hex_color, opacity, rim=0.9):
    """Nucleon shell: flat colour at `opacity`, fresnel rim stays visible when see-through."""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = node(nt, "ShaderNodeOutputMaterial")
    emit = node(nt, "ShaderNodeEmission")
    emit.inputs["Color"].default_value = rgba(hex_color)
    transp = node(nt, "ShaderNodeBsdfTransparent")
    fres = node(nt, "ShaderNodeLayerWeight")
    fres.inputs["Blend"].default_value = 0.35
    # alpha = max(opacity, rim * facing)
    mx = node(nt, "ShaderNodeMath")
    mx.operation = "MAXIMUM"
    mul = node(nt, "ShaderNodeMath")
    mul.operation = "MULTIPLY"
    mul.inputs[1].default_value = rim
    nt.links.new(fres.outputs["Facing"], mul.inputs[0])
    nt.links.new(mul.outputs[0], mx.inputs[0])
    mx.inputs[1].default_value = opacity
    shader_mix = node(nt, "ShaderNodeMixShader")
    nt.links.new(mx.outputs[0], shader_mix.inputs["Fac"])
    nt.links.new(transp.outputs[0], shader_mix.inputs[1])
    nt.links.new(emit.outputs[0], shader_mix.inputs[2])
    nt.links.new(shader_mix.outputs[0], out.inputs["Surface"])
    set_blended(mat)
    return mat


def glow_material(name, hex_color, strength, alpha_falloff=True):
    """Additive-looking haze: emission over transparent, fading toward silhouette edges."""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = node(nt, "ShaderNodeOutputMaterial")
    emit = node(nt, "ShaderNodeEmission")
    emit.inputs["Color"].default_value = rgba(hex_color)
    emit.inputs["Strength"].default_value = 1.0
    transp = node(nt, "ShaderNodeBsdfTransparent")
    mix = node(nt, "ShaderNodeMixShader")
    if alpha_falloff:
        fres = node(nt, "ShaderNodeLayerWeight")
        fres.inputs["Blend"].default_value = 0.5
        inv = node(nt, "ShaderNodeMath")
        inv.operation = "SUBTRACT"
        inv.inputs[0].default_value = 1.0
        nt.links.new(fres.outputs["Facing"], inv.inputs[1])
        scale = node(nt, "ShaderNodeMath")
        scale.operation = "MULTIPLY"
        scale.inputs[1].default_value = strength
        nt.links.new(inv.outputs[0], scale.inputs[0])
        nt.links.new(scale.outputs[0], mix.inputs["Fac"])
    else:
        mix.inputs["Fac"].default_value = strength
    nt.links.new(transp.outputs[0], mix.inputs[1])
    nt.links.new(emit.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs["Surface"])
    set_blended(mat)
    return mat


def sphere(name, loc, diameter, mat, segments=16, rings=10):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings,
                                         radius=diameter / 2, location=loc)
    obj = bpy.context.active_object
    obj.name = name
    bpy.ops.object.shade_smooth()
    obj.data.materials.append(mat)
    return obj


def tube(name, a, b, radius, mat):
    a, b = Vector(a), Vector(b)
    d = b - a
    bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=radius, depth=d.length,
                                        location=(a + b) / 2)
    obj = bpy.context.active_object
    obj.name = name
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized())
    obj.data.materials.append(mat)
    return obj


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for block in (bpy.data.meshes, bpy.data.materials):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def setup_render():
    scene = bpy.context.scene
    current = scene.render.engine
    for engine in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT", current):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue
    scene.render.resolution_x, scene.render.resolution_y = RESOLUTION
    scene.render.film_transparent = False
    try:
        scene.view_settings.view_transform = "Standard"
    except TypeError:
        pass
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    bg = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs["Color"].default_value = rgba(GROUND)
    bg.inputs["Strength"].default_value = 1.0


def camera(name, target, offset, fov_deg):
    cam_data = bpy.data.cameras.new(name)
    cam_data.angle = math.radians(fov_deg)
    cam = bpy.data.objects.new(name, cam_data)
    bpy.context.collection.objects.link(cam)
    cam.location = Vector(target) + Vector(offset)
    cam.rotation_mode = "QUATERNION"
    cam.rotation_quaternion = (Vector(target) - cam.location).to_track_quat("-Z", "Y")
    return cam


# --------------------------------------------------------------------------- particles
# Unity is Y-up, Blender Z-up: belt runs along +X, rows step along +Y (away from camera).

def jitter_offsets(seed, n, amp):
    rnd = random.Random(seed)
    return [Vector((rnd.uniform(-amp, amp), rnd.uniform(-amp, amp), rnd.uniform(-amp, amp)))
            for _ in range(n)]


def build_quark(prefix, loc, color, state, mats):
    """state 0: rest; 1: mid-jitter with ghosts; 2: squashed extreme."""
    loc = Vector(loc)
    main = mats[color]
    if state == 0:
        sphere(prefix, loc, QUARK, main)
        return
    offs = jitter_offsets(hash(prefix) & 0xFFFF, 4, 0.035 if state == 1 else 0.05)
    obj = sphere(prefix, loc + offs[0], QUARK, main)
    if state == 2:
        obj.scale = (1.25, 0.8, 0.9)
    ghost = mats[color + "_ghost"]
    for i, o in enumerate(offs[1:]):
        sphere(f"{prefix}_ghost{i}", loc + o, QUARK * 0.95, ghost)


def build_nucleon(prefix, loc, shell_hex, flavours, state, mats):
    """state 0: solid shell; 1: fading (quarks faintly visible); 2: see-through, quarks + gluons."""
    loc = Vector(loc)
    r = ITEM * 0.2
    angles = [0, 2 * math.pi / 3, 4 * math.pi / 3]
    twist = 0.4 * state  # quarks orbit between frames
    pts = [loc + Vector((r * math.cos(a + twist), r * math.sin(a + twist) * 0.8,
                         (0.03 if i == 0 else -0.02)))
           for i, a in enumerate(angles)]
    if state > 0:
        jit = jitter_offsets(hash(prefix) & 0xFFFF, 3, 0.012)
        pts = [p + j for p, j in zip(pts, jit)]
        for i, (p, f) in enumerate(zip(pts, flavours)):
            sphere(f"{prefix}_q{i}", p, INNER_QUARK, mats[f])
        for i in range(3):
            a, b = pts[i], pts[(i + 1) % 3]
            mid = (a + b) / 2 + (loc - (a + b) / 2) * -0.25 + Vector((0, 0, 0.02 * (i - 1)))
            tube(f"{prefix}_g{i}a", a, mid, 0.006, mats["gluon"])
            tube(f"{prefix}_g{i}b", mid, b, 0.006, mats["gluon"])
    opacity = (1.0, 0.45, 0.06)[state]
    shell = shell_material(f"{prefix}_shell_mat", shell_hex, opacity)
    sphere(f"{prefix}_shell", loc, ITEM, shell, segments=24, rings=16)


def build_electron(prefix, loc, state, mats):
    """state = breathing phase 0..2: haze radius and swirl offsets change."""
    loc = Vector(loc)
    breathe = (1.0, 1.15, 0.92)[state]
    sphere(f"{prefix}_core", loc, 0.05, mats["electron_core"])
    rnd = random.Random(31 + state)
    # Many small, faint, scattered puffs read as a cloud; few big ones read as a ball.
    for i in range(9):
        off = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.6, 0.6))) * 0.09
        d = ITEM * breathe * rnd.uniform(0.3, 0.6)
        obj = sphere(f"{prefix}_haze{i}", loc + off, d, mats["electron_haze"], segments=12, rings=8)
        obj.scale = (1 + rnd.uniform(-0.12, 0.12), 1 + rnd.uniform(-0.12, 0.12),
                     0.85 + rnd.uniform(-0.1, 0.1))


def build_sheet():
    mats = {
        UP: flat_material("up_quark", UP),
        DOWN: flat_material("down_quark", DOWN),
        UP + "_ghost": glow_material("up_ghost", UP, 0.25, alpha_falloff=False),
        DOWN + "_ghost": glow_material("down_ghost", DOWN, 0.25, alpha_falloff=False),
        "gluon": flat_material("gluon", GLUON, top_light=0.1),
        "electron_core": flat_material("electron_core", "#E8FFFD", top_light=0.0),
        "electron_haze": glow_material("electron_haze", ELECTRON, 0.16),
        "belt": flat_material("belt", BELT, top_light=0.0),
    }
    rows = [
        ("electron", lambda p, s: build_electron(f"electron_{s}", p, s, mats)),
        ("up_quark", lambda p, s: build_quark(f"up_{s}", p, UP, s, mats)),
        ("down_quark", lambda p, s: build_quark(f"down_{s}", p, DOWN, s, mats)),
        ("proton", lambda p, s: build_nucleon(f"proton_{s}", p, PROTON, (UP, UP, DOWN), s, mats)),
        ("neutron", lambda p, s: build_nucleon(f"neutron_{s}", p, NEUTRON, (UP, DOWN, DOWN), s, mats)),
    ]
    for row, (name, build) in enumerate(rows):
        y = row * 1.0
        bpy.ops.mesh.primitive_plane_add(size=1, location=(1.0, y, 0.0))
        belt = bpy.context.active_object
        belt.name = f"belt_{name}"
        belt.scale = (3.6, 0.7, 1)
        belt.data.materials.append(mats["belt"])
        for state in range(3):
            build(Vector((state * 1.0, y, HEIGHT)), state)
    return len(rows)


def render(cam, filename):
    scene = bpy.context.scene
    scene.camera = cam
    OUT.mkdir(parents=True, exist_ok=True)
    scene.render.filepath = os.fspath(OUT / filename)
    bpy.ops.render.render(write_still=True)


def main():
    clear_scene()
    setup_render()
    n = build_sheet()
    centre = (1.0, (n - 1) / 2, HEIGHT)
    # Same pitch as the game camera (offset 0,8,-6 in Unity == 0,-6,8 here), pulled in for review.
    close = camera("cam_close", centre, Vector((0, -6, 8)).normalized() * 8.0, 48)
    game = camera("cam_game", centre, (0, -6, 8), 60)
    render(close, "sheet_close.png")
    render(game, "sheet_game_distance.png")
    return os.fspath(OUT)


if __name__ == "__main__" or True:
    print("belt particle mockups ->", main())
