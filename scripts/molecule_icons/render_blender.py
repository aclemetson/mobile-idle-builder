"""Step 2: render every molecule in structures.json to renders/<id>.png. Runs INSIDE Blender.

    blender --background --python scripts/molecule_icons/render_blender.py
    (or exec this file through the Blender MCP's execute_blender_code)

Replaces every mesh and empty in the open scene, so run it in a throwaway scene. Pass item
ids after "--" to render a subset.
"""
import json
import math
import os
import pathlib
import sys

import bpy
from mathutils import Quaternion, Vector

HERE = pathlib.Path(__file__).resolve().parent
OUT = HERE / "renders"

UNIT = 1.42         # Blender units per Angstrom
BOND_RADIUS = 0.16
RESOLUTION = 1024
# Fixed camera and lights: the molecule rotates to face the camera, so shading stays consistent.
VIEW = Vector((0.28, -1.0, 0.42)).normalized()


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def hex_to_linear(h):
    return tuple(srgb_to_linear(int(h[i:i + 2], 16) / 255.0) for i in (0, 2, 4))


def make_material(name, rgb, roughness):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1)
    bsdf.inputs["Roughness"].default_value = roughness
    return mat


def aim(obj, direction):
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = (-direction).to_track_quat("-Z", "Y")


def setup_scene():
    scene = bpy.context.scene
    try:
        scene.render.engine = "BLENDER_EEVEE"
    except TypeError as exc:
        print(f"Keeping engine {scene.render.engine}: {exc}")
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.render.film_transparent = True
    scene.render.resolution_x = scene.render.resolution_y = RESOLUTION
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"

    cam = bpy.data.objects.get("Camera")
    if cam is None:
        cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
        bpy.context.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = "ORTHO"
    cam.location = VIEW * 30
    cam.data.clip_end = 100
    aim(cam, VIEW)

    for o in list(bpy.data.objects):
        if o.type == "LIGHT":
            bpy.data.objects.remove(o, do_unlink=True)
    for name, direction, energy, color, angle in (
        ("Key", (-0.5, -0.8, 1.0), 3.0, (1.0, 0.98, 0.95), 25),
        ("Rim", (0.9, 0.7, 0.3), 2.0, (0.55, 0.75, 1.0), 15),
    ):
        light = bpy.data.lights.new(name, "SUN")
        light.energy, light.color, light.angle = energy, color, math.radians(angle)
        obj = bpy.data.objects.new(name, light)
        bpy.context.collection.objects.link(obj)
        aim(obj, Vector(direction).normalized())

    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    background = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
    background.inputs[0].default_value = (0.6, 0.65, 0.75, 1)
    background.inputs[1].default_value = 0.55
    bpy.context.view_layer.update()
    return scene, cam


def best_view_direction(positions, radii, samples=500):
    """Pick the direction with the least atom overlap, preferring spread-out silhouettes."""
    golden = math.pi * (3 - math.sqrt(5))
    best, best_score = None, float("inf")
    for i in range(samples):
        z = 1 - 2 * (i + 0.5) / samples
        r = math.sqrt(1 - z * z)
        d = Vector((math.cos(golden * i) * r, math.sin(golden * i) * r, z))
        projected = [p - d * p.dot(d) for p in positions]
        penalty = 0.0
        for a in range(len(projected)):
            for b in range(a + 1, len(projected)):
                reach = radii[a] + radii[b]
                overlap = max(0.0, (reach - (projected[a] - projected[b]).length) / reach)
                penalty += overlap * overlap
        score = penalty - 0.02 * max(p.length for p in projected)
        if score < best_score:
            best, best_score = d, score
    return best


def smooth(obj):
    obj.data.polygons.foreach_set("use_smooth", [True] * len(obj.data.polygons))
    obj.data.update()


def build_molecule(structure, spec, manifest, materials, cam):
    atoms, bonds = structure["atoms"], structure["bonds"]
    if spec.get("hide_h"):
        keep = [i for i, a in enumerate(atoms) if a[0] != "H"]
        remap = {old: new for new, old in enumerate(keep)}
        bonds = [[remap[a], remap[b], o] for a, b, o in bonds if a in remap and b in remap]
        atoms = [atoms[i] for i in keep]

    chunk = spec.get("chunk", 1.0)
    bond_radius = BOND_RADIUS * spec.get("bond_chunk", 1.0)
    elements = [a[0] for a in atoms]
    radii = [manifest["element_radii"][e] * chunk for e in elements]
    positions = [Vector(a[1:]) * UNIT for a in atoms]
    centre = sum(positions, Vector()) / len(positions)
    positions = [p - centre for p in positions]

    rotation = best_view_direction(positions, radii).rotation_difference(VIEW)
    positions = [rotation @ p for p in positions]

    basis = cam.matrix_world.to_3x3()
    right, up = basis @ Vector((1, 0, 0)), basis @ Vector((0, 1, 0))

    if spec.get("diagonal"):
        # Lay a long chain along the tile's diagonal, which fits about 40% more length.
        far_a, far_b = max(
            ((p, q) for i, p in enumerate(positions) for q in positions[i + 1:]),
            key=lambda pair: ((pair[0] - pair[1]) - VIEW * (pair[0] - pair[1]).dot(VIEW)).length)
        span = far_b - far_a
        angle = math.atan2(span.dot(up), span.dot(right))
        roll = Quaternion(VIEW, math.radians(45) - angle)  # +VIEW points at the eye, so this is CCW
        positions = [roll @ p for p in positions]

    for element, position, radius in zip(elements, positions, radii):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=24, radius=radius, location=position)
        obj = bpy.context.active_object
        smooth(obj)
        obj.data.materials.append(materials[element])

    for a, b, order in bonds:
        start, end = positions[a], positions[b]
        axis = (end - start).normalized()
        side = axis.cross(VIEW)
        side = side.normalized() if side.length > 1e-4 else Vector((1, 0, 0))
        offsets = {1: [0.0], 2: [-0.2, 0.2]}.get(order, [-0.28, 0.0, 0.28])
        radius = bond_radius if order == 1 else bond_radius * 0.72
        for offset in offsets:
            bpy.ops.mesh.primitive_cylinder_add(
                vertices=24, radius=radius, depth=(end - start).length,
                location=(start + end) / 2 + side * offset)
            obj = bpy.context.active_object
            obj.rotation_mode = "QUATERNION"
            obj.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(axis)
            smooth(obj)
            obj.data.materials.append(materials["bond"])

    extent = max(max(abs(p.dot(right)) + r, abs(p.dot(up)) + r) for p, r in zip(positions, radii))
    cam.data.ortho_scale = extent * 2 * 1.06


def render_all(only=None):
    manifest = json.loads((HERE / "molecules.json").read_text(encoding="utf-8"))
    structures = json.loads((HERE / "structures.json").read_text(encoding="utf-8"))
    OUT.mkdir(exist_ok=True)
    scene, cam = setup_scene()
    materials = {e: make_material("Atom_" + e, hex_to_linear(c), 0.3)
                 for e, c in manifest["element_colors"].items()}
    materials["bond"] = make_material("Atom_Bond", (0.75, 0.78, 0.82), 0.4)

    for item_id, spec in manifest["molecules"].items():
        if only and item_id not in only:
            continue
        for obj in list(bpy.data.objects):
            if obj.type in {"MESH", "EMPTY"}:
                bpy.data.objects.remove(obj, do_unlink=True)
        build_molecule(structures[item_id], spec, manifest, materials, cam)
        bpy.context.view_layer.update()
        scene.render.filepath = os.fspath(OUT / (item_id + ".png"))
        bpy.ops.render.render(write_still=True)
        print(f"rendered {item_id}")


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else None
    render_all(args)
