"""Shared helpers for the Crazy Penguin Wars Blender pipeline (Blender 4.2 as a Python module).

Modeling frame used by every module ("game frame"):
    +X = right on screen (forward for weapons, missiles and the penguin's beak)
    +Z = up
    -Y = towards the game camera (Blender's Front view shows what the player sees)
Unity's FBX importer mirrors X, so right before exporting we rotate everything 180 degrees around Z.
With the mandated FBX axis settings (forward -Z, up Y, bake_space_transform) the result in Unity is:
    Unity +X = game +X, Unity +Y = game +Z, Unity -Z (towards camera) = game -Y.
1 Blender unit = 1 Unity world unit = 20 original Flash pixels.

All objects keep identity rotation and unit scale (geometry is baked) so that rotating the scene for export
only needs to rotate mesh data and locations.
"""
import math
import os
import shutil
import struct
import zlib

import bpy  # noqa: F401  (must be imported before bmesh when bpy is a module)
import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
RES = os.path.join(ROOT, "Assets", "CPW", "Resources")
MODELS = os.path.join(RES, "Models")
ICONS = os.path.join(RES, "Icons")
TEXTURES = os.path.join(RES, "Textures")
BLEND = os.path.join(ROOT, "Blender", "blend")
CONFIG = os.path.join(RES, "Data", "config.json")

_config = None


def config():
    global _config
    if _config is None:
        import json
        with open(CONFIG, encoding="utf-8") as f:
            _config = json.load(f)
    return _config


def ids(section):
    return [k for k in config().get(section, {}).keys() if not k.startswith("$")]


def ensure_dir(p):
    os.makedirs(p, exist_ok=True)
    return p


# --------------------------------------------------------------------------------------------- scene

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _mats.clear()


def new_collection(name):
    col = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(col)
    return col


_active_col = [None]


def use_collection(col):
    """Objects created by the helpers go into this collection (None = scene collection)."""
    _active_col[0] = col


def _link(obj):
    col = _active_col[0] or bpy.context.scene.collection
    col.objects.link(obj)
    return obj


# --------------------------------------------------------------------------------------------- colors

def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


PALETTE = {
    "black": "#262a36", "white": "#f5f7fb", "orange": "#ff9b21", "orange_dark": "#e0701a",
    "pupil": "#121318", "eye": "#ffffff", "pink": "#ff8fa8", "red": "#e83a3a", "red_dark": "#a82424",
    "blue": "#3a7be8", "blue_dark": "#24489c", "navy": "#1f2d5a", "green": "#4cbb3f", "green_dark": "#2c7a2a",
    "olive": "#6b7a3a", "olive_dark": "#4a5528", "khaki": "#c8b27a", "tan": "#d9b77e", "brown": "#8a5a32",
    "brown_dark": "#5c3a1e", "wood": "#c4884a", "wood_dark": "#8a5a2c", "yellow": "#ffd23a", "gold": "#f2b632",
    "gold_dark": "#c4861a", "silver": "#c9d1dc", "steel": "#8d99a8", "steel_dark": "#5a6472", "gun": "#3c4350",
    "gun_dark": "#262b34", "purple": "#8a4fd8", "purple_dark": "#5a2c9c", "cyan": "#4fd8e8", "ice": "#bfeaf7",
    "ice_dark": "#7cc4e0", "stone": "#9a9a92", "stone_dark": "#6c6c66", "metal": "#a4adb8", "metal_dark": "#6a7380",
    "grey": "#8a8f98", "grey_light": "#d0d4da", "grey_dark": "#4a4e56", "skin": "#ffd2a8", "lime": "#b6e83a",
    "magenta": "#e83aa8", "plaid_red": "#c8302c", "denim": "#4a6ea8", "cream": "#fff3d6", "lemon": "#ffe23a",
    "glass": "#9fe0ff", "plasma": "#6af0ff", "fire": "#ff5a1a", "slime": "#8ee84a", "teal": "#2ab3a0",
    "leaf": "#5cb83a", "leaf_dark": "#3a8a2a", "leaf_light": "#8ad84a", "bark": "#7a4e2a", "snow": "#f2f8ff",
    "sand": "#e8c47a", "sand_dark": "#c49a52", "rock": "#8a7a6a", "rock_dark": "#5e5248", "cactus": "#4ca84a",
    "cactus_dark": "#2e7a36", "mesa": "#c8643a", "mesa_dark": "#9a4628", "mountain": "#7a86a0", "mountain_dark": "#56607a",
    "pine": "#2e7a52", "pine_dark": "#1e5a3c", "hill": "#6cc04a", "hill_dark": "#4a9a3a", "rope": "#d8c08a",
    "canvas": "#f0f0e6", "rubber": "#30333a", "sky": "#8fd0ff", "cat": "#f0a040", "cat_dark": "#b8702a",
    "bone": "#f0e8d8", "rose": "#ff6a8a", "violet": "#b07ae8", "mint": "#7ae8c0", "peach": "#ffb08a",
}

_mats = {}


def mat(name, hexcol=None, emission=0.0):
    """One material per color. name is a palette key or any name with an explicit hex color."""
    key = name
    if key in _mats:
        try:
            if _mats[key].name in bpy.data.materials:
                return _mats[key]
        except ReferenceError:  # purged
            pass
    hexcol = hexcol or PALETTE.get(name, "#ff00ff")
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    rgb = hex_rgb(hexcol)
    m["srgb"] = list(rgb)
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    lin = [srgb_to_linear(c) for c in rgb]
    bsdf.inputs["Base Color"].default_value = (*lin, 1)
    bsdf.inputs["Roughness"].default_value = 0.55
    try:
        bsdf.inputs["Specular IOR Level"].default_value = 0.25
    except KeyError:
        pass
    if emission > 0:
        bsdf.inputs["Emission Color"].default_value = (*lin, 1)
        bsdf.inputs["Emission Strength"].default_value = emission
    m.diffuse_color = (*lin, 1)
    _mats[key] = m
    return m


def set_color_mode(mode):
    """'linear' for Blender rendering, 'srgb' for FBX export (Unity reads FBX colors as sRGB values)."""
    for m in bpy.data.materials:
        if "srgb" not in m:
            continue
        rgb = list(m["srgb"])
        vals = rgb if mode == "srgb" else [srgb_to_linear(c) for c in rgb]
        if m.node_tree:
            b = m.node_tree.nodes.get("Principled BSDF")
            if b:
                b.inputs["Base Color"].default_value = (*vals, 1)
        m.diffuse_color = (*vals, 1)


# --------------------------------------------------------------------------------------------- meshes

def _obj_from_bm(name, bm, material, smooth=True):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    if material is not None:
        me.materials.append(material)
    if smooth:
        me.shade_smooth()
    ob = bpy.data.objects.new(name, me)
    return _link(ob)


def _xf_bm(bm, m):
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)


def look_matrix(direction):
    """Rotation matrix taking +Z to direction."""
    d = Vector(direction).normalized()
    return Vector((0, 0, 1)).rotation_difference(d).to_matrix().to_4x4()


def sphere(name, center, radii, material, segs=16, rings=10, smooth=True, rot=None):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=1.0)
    if isinstance(radii, (int, float)):
        radii = (radii, radii, radii)
    m = Matrix.Diagonal((*radii, 1))
    if rot is not None:
        m = rot @ m
    _xf_bm(bm, Matrix.Translation(center) @ m)
    return _obj_from_bm(name, bm, material, smooth)


def ico(name, center, radius, material, subdiv=1, smooth=False, scale=(1, 1, 1), seed=None, jitter=0.0):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    if jitter and seed is not None:
        import random
        rnd = random.Random(seed)
        for v in bm.verts:
            v.co *= 1.0 + rnd.uniform(-jitter, jitter)
    _xf_bm(bm, Matrix.Translation(center) @ Matrix.Diagonal((radius * scale[0], radius * scale[1], radius * scale[2], 1)))
    return _obj_from_bm(name, bm, material, smooth)


def cyl(name, p0, p1, r0, r1=None, material=None, segs=12, smooth=True, caps=True, sharp=True):
    """Cylinder/cone between points p0 and p1 (radius r0 at p0, r1 at p1)."""
    r1 = r0 if r1 is None else r1
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=caps, cap_tris=False, segments=segs, radius1=r0, radius2=r1, depth=d.length)
    # create_cone is centered on origin along Z
    m = Matrix.Translation((p0 + p1) / 2) @ look_matrix(d)
    _xf_bm(bm, m)
    ob = _obj_from_bm(name, bm, material, smooth)
    if smooth and sharp:
        ob.data.set_sharp_from_angle(angle=math.radians(50))
    return ob


def box(name, center, size, material, bevel=0.0, bevel_segs=1, rot=None):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    m = Matrix.Diagonal((*size, 1))
    if rot is not None:
        m = rot @ m
    _xf_bm(bm, Matrix.Translation(center) @ m)
    ob = _obj_from_bm(name, bm, material, smooth=False)
    if bevel > 0:
        add_bevel(ob, bevel, bevel_segs)
    return ob


def torus(name, center, major, minor, material, segs=16, rsegs=8, axis=(0, 0, 1), scale=(1, 1, 1)):
    bm = bmesh.new()
    verts = []
    for i in range(segs):
        a = 2 * math.pi * i / segs
        ring = []
        for j in range(rsegs):
            b = 2 * math.pi * j / rsegs
            r = major + minor * math.cos(b)
            ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), minor * math.sin(b))))
        verts.append(ring)
    for i in range(segs):
        for j in range(rsegs):
            a, b = verts[i][j], verts[(i + 1) % segs][j]
            c, d = verts[(i + 1) % segs][(j + 1) % rsegs], verts[i][(j + 1) % rsegs]
            bm.faces.new((a, b, c, d))
    _xf_bm(bm, Matrix.Translation(center) @ look_matrix(axis) @ Matrix.Diagonal((*scale, 1)))
    return _obj_from_bm(name, bm, material, True)


def lathe(name, profile, material, segs=16, center=(0, 0, 0), axis=(0, 0, 1), smooth=True, scale=(1, 1, 1)):
    """Revolve a profile [(radius, height), ...] around axis. Radius 0 points close the ends."""
    bm = bmesh.new()
    rings = []
    for (r, h) in profile:
        if r <= 1e-5:
            rings.append([bm.verts.new((0, 0, h))])
        else:
            rings.append([bm.verts.new((r * math.cos(2 * math.pi * i / segs), r * math.sin(2 * math.pi * i / segs), h))
                          for i in range(segs)])
    for a, b in zip(rings, rings[1:]):
        if len(a) == 1 and len(b) == 1:
            continue
        if len(a) == 1:
            for i in range(segs):
                bm.faces.new((a[0], b[i], b[(i + 1) % segs]))
        elif len(b) == 1:
            for i in range(segs):
                bm.faces.new((a[i], b[0], a[(i + 1) % segs]))
        else:
            for i in range(segs):
                bm.faces.new((a[i], a[(i + 1) % segs], b[(i + 1) % segs], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    _xf_bm(bm, Matrix.Translation(center) @ look_matrix(axis) @ Matrix.Diagonal((*scale, 1)))
    ob = _obj_from_bm(name, bm, material, smooth)
    if smooth:
        ob.data.set_sharp_from_angle(angle=math.radians(55))
    return ob


def extrude(name, pts, depth, material, y0=None, bevel=0.0, bevel_segs=1, plane="xz"):
    """Extrude a 2D polygon. plane 'xz': points are (x, z), extruded along Y (towards/away from camera),
    centered on y=0 unless y0 given (front face at y0). plane 'xy': points (x, y), extruded along Z from 0."""
    bm = bmesh.new()
    if plane == "xz":
        yc = 0.0 if y0 is None else y0 + depth / 2
        front = [bm.verts.new((x, yc - depth / 2, z)) for (x, z) in pts]
    else:
        front = [bm.verts.new((x, y, 0.0)) for (x, y) in pts]
    f = bm.faces.new(front)
    bmesh.ops.recalc_face_normals(bm, faces=[f])
    vec = (0, depth, 0) if plane == "xz" else (0, 0, depth)
    r = bmesh.ops.extrude_face_region(bm, geom=[f])
    nv = [g for g in r["geom"] if isinstance(g, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, vec=vec, verts=nv)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = _obj_from_bm(name, bm, material, smooth=False)
    if bevel > 0:
        add_bevel(ob, bevel, bevel_segs)
    return ob


def add_bevel(ob, width, segs=1, material_index=-1, apply=True):
    mod = ob.modifiers.new("Bevel", "BEVEL")
    mod.width = width
    mod.segments = segs
    mod.limit_method = "ANGLE"
    mod.angle_limit = math.radians(40)
    mod.harden_normals = False
    if material_index >= 0:
        mod.material = material_index
    if apply:
        apply_modifiers(ob)
    return ob


def add_subsurf(ob, levels=1, apply=True):
    mod = ob.modifiers.new("Subsurf", "SUBSURF")
    mod.levels = levels
    mod.render_levels = levels
    if apply:
        apply_modifiers(ob)
    return ob


def apply_modifiers(ob):
    if not ob.modifiers:
        return ob
    dg = bpy.context.evaluated_depsgraph_get()
    dg.update()
    ev = ob.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
    old = ob.data
    ob.modifiers.clear()
    ob.data = me
    me.name = old.name
    if old.users == 0:
        bpy.data.meshes.remove(old)
    return ob


def smooth(ob, angle=None):
    ob.data.shade_smooth()
    if angle is not None:
        ob.data.set_sharp_from_angle(angle=math.radians(angle))
    return ob


def flat(ob):
    ob.data.shade_flat()
    return ob


def join(objs, name):
    """Join meshes (world-space geometry) into one object named name, origin at world origin."""
    objs = [o for o in objs if o is not None]
    if not objs:
        return None
    bm = bmesh.new()
    mats = []
    for o in objs:
        apply_modifiers(o)
        me = o.data.copy()
        me.transform(world_matrix(o))
        remap = []
        for m in me.materials:
            if m not in mats:
                mats.append(m)
            remap.append(mats.index(m))
        for p in me.polygons:
            p.material_index = remap[p.material_index] if remap else 0
        bm.from_mesh(me)
        bpy.data.meshes.remove(me)
    for o in objs:
        me = o.data
        bpy.data.objects.remove(o)
        if me.users == 0:
            bpy.data.meshes.remove(me)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(m)
    ob = bpy.data.objects.new(name, me)
    return _link(ob)


def world_matrix(o):
    m = Matrix.Translation(o.location)
    p = o.parent
    while p is not None:
        m = Matrix.Translation(p.location) @ m
        p = p.parent
    return m


def world_loc(o):
    return world_matrix(o).to_translation()


def set_origin(ob, point):
    """Move the object's origin to the world-space point without moving geometry (top-level, unparented)."""
    p = Vector(point)
    cur = world_loc(ob)
    ob.data.transform(Matrix.Translation(cur - p))
    ob.location = ob.location + (p - cur)
    return ob


def parent(child, par):
    wc = world_loc(child)
    wp = world_loc(par)
    child.parent = par
    child.matrix_parent_inverse = Matrix.Identity(4)
    child.location = wc - wp
    bpy.context.view_layer.update()
    return child


def empty(name, loc, size=0.15):
    e = bpy.data.objects.new(name, None)
    e["export_name"] = name
    e.empty_display_type = "PLAIN_AXES"
    e.empty_display_size = size
    e.location = Vector(loc)
    return _link(e)


def transform_objects(objs, m3):
    """Apply a rotation/scale (3x3) about the world origin to a set of objects that only use translations."""
    m4 = m3.to_4x4()
    done = set()
    for o in objs:
        if o.type == "MESH" and o.data.name not in done:
            o.data.transform(m4)
            done.add(o.data.name)
        o.location = m3 @ o.location
    for o in objs:
        if o.type == "MESH":
            o.data.update()
    bpy.context.view_layer.update()


def tri_count(objs):
    n = 0
    for o in objs:
        if o.type == "MESH":
            for p in o.data.polygons:
                n += len(p.vertices) - 2
    return n


def col_objects(col):
    return list(col.all_objects)


# --------------------------------------------------------------------------------------------- export

ROT_EXPORT = Matrix.Rotation(math.pi, 3, "Z")


def _clean_meshes(objs):
    """Swap each mesh for a cleaned copy of its evaluated mesh (modifiers applied, duplicate verts merged,
    zero-area faces removed). Zero-area faces have no normal and Unity warns "invalid normals" on import.
    Returns [(obj, original_data)] for restoring."""
    dg = bpy.context.evaluated_depsgraph_get()
    swapped = []
    for o in objs:
        if o.type != "MESH":
            continue
        ev = o.evaluated_get(dg)
        bm = bmesh.new()
        bm.from_mesh(ev.to_mesh())
        ev.to_mesh_clear()
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
        bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=1e-6)
        # triangulate here (not in Unity) so concave or non-planar n-gons can't turn into zero-area triangles
        bmesh.ops.triangulate(bm, faces=bm.faces, quad_method="BEAUTY", ngon_method="BEAUTY")
        bad = [f for f in bm.faces if f.calc_area() < 1e-10]
        if bad:
            bmesh.ops.delete(bm, geom=bad, context="FACES_ONLY")
        loose = [v for v in bm.verts if not v.link_faces]
        if loose:
            bmesh.ops.delete(bm, geom=loose, context="VERTS")
        me = bpy.data.meshes.new(o.data.name + "__export")  # renamed back below so the FBX keeps the mesh name
        bm.to_mesh(me)
        bm.free()
        if me.normals_domain == "POINT":
            # fully smooth mesh: the exporter would write ByVertice+IndexToDirect normals, which Unity rejects
            # ("has no normals"). Identical custom normals force per-corner normals without changing shading.
            me.normals_split_custom_set_from_vertices([v.normal[:] for v in me.vertices])
        if not me.materials:
            for m in o.data.materials:
                me.materials.append(m)
        swapped.append((o, o.data, [md.show_viewport for md in o.modifiers]))
        name = o.data.name
        o.data.name = name + "__orig"
        me.name = name
        o.data = me
        for md in o.modifiers:
            md.show_viewport = False
    return swapped


def _restore_meshes(swapped):
    for o, data, vis in reversed(swapped):  # reversed: undoes the renames of shared meshes in order
        tmp = o.data
        name = tmp.name
        o.data = data
        bpy.data.meshes.remove(tmp)
        data.name = name
        for md, v in zip(o.modifiers, vis):
            md.show_viewport = v


def export_collection(col, path, aliases=()):
    """Export one collection to FBX (Unity orientation, see module doc) and copy to alias paths."""
    ensure_dir(os.path.dirname(path))
    objs = col_objects(col)
    renamed = []
    for o in objs:  # objects tagged with export_name get that exact name in the FBX (Blender names are unique)
        want = o.get("export_name")
        if want and o.name != want:
            other = bpy.data.objects.get(want)
            if other is not None:
                other.name = want + "__tmp"
                renamed.append((other, want))
            old = o.name
            o.name = want
            renamed.insert(0, (o, old))
    transform_objects(objs, ROT_EXPORT)
    set_color_mode("srgb")
    swapped = _clean_meshes(objs)
    try:
        lc = _find_layer_collection(bpy.context.view_layer.layer_collection, col.name)
        bpy.context.view_layer.active_layer_collection = lc
        bpy.ops.export_scene.fbx(
            filepath=path, use_active_collection=True, use_selection=False,
            object_types={"MESH", "EMPTY"}, apply_scale_options="FBX_SCALE_ALL",
            axis_forward="-Z", axis_up="Y", bake_space_transform=True, use_mesh_modifiers=True,
            mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False, use_custom_props=False,
            path_mode="AUTO", embed_textures=False, use_tspace=False)
    finally:
        _restore_meshes(swapped)
        set_color_mode("linear")
        transform_objects(objs, ROT_EXPORT.inverted())
        for o, name in renamed:
            o.name = name
    for a in aliases:
        ensure_dir(os.path.dirname(a))
        shutil.copyfile(path, a)


def _find_layer_collection(lc, name):
    if lc.collection.name == name:
        return lc
    for c in lc.children:
        r = _find_layer_collection(c, name)
        if r:
            return r
    return None


def save_blend(name):
    ensure_dir(BLEND)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(BLEND, name + ".blend"), compress=True, check_existing=False)
    old = os.path.join(BLEND, name + ".blend1")
    if os.path.exists(old):
        os.remove(old)


# --------------------------------------------------------------------------------------------- PNG io

def write_png(path, rgba):
    """rgba: float array (h, w, 4) in 0..1, top row first."""
    ensure_dir(os.path.dirname(path))
    a = (np.clip(rgba, 0, 1) * 255 + 0.5).astype(np.uint8)
    h, w, c = a.shape
    raw = b"".join(b"\x00" + a[y].tobytes() for y in range(h))
    ctype = 6 if c == 4 else 2

    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, ctype, 0, 0, 0)))
        f.write(chunk(b"IDAT", zlib.compress(raw, 9)))
        f.write(chunk(b"IEND", b""))


def read_image(path):
    """Return float array (h, w, 4), top row first (via bpy image loader)."""
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(h, w, 4)[::-1].copy()
