"""Icon rendering with Cycles: transparent film, orthographic 3/4 camera, fixed three-light rig,
then a numpy post pass that adds a dark outline and a soft drop shadow (the Flash UI look)."""
import math
import os
import tempfile

import bpy
import numpy as np
from mathutils import Vector

import common as C

SAMPLES = int(os.environ.get("CPW_SAMPLES", "32"))
VIEW_34 = (0.42, -1.0, 0.5)       # from front-right-above
VIEW_SIDE = (-0.22, -1.0, 0.32)   # weapons/missiles: mostly side-on, barrel to the right
VIEW_FRONT = (0.0, -1.0, 0.12)

_tmp = tempfile.mkdtemp(prefix="cpw_icons_")


def setup_scene(transparent=True, bg=None):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    cy = sc.cycles
    cy.device = "CPU"
    cy.samples = SAMPLES
    cy.use_adaptive_sampling = True
    cy.adaptive_threshold = 0.05
    cy.max_bounces = 4
    cy.diffuse_bounces = 2
    cy.glossy_bounces = 1
    cy.transmission_bounces = 2
    cy.transparent_max_bounces = 4
    cy.use_denoising = True
    try:
        cy.denoiser = "OPENIMAGEDENOISE"
    except Exception:
        cy.use_denoising = False
    sc.render.threads_mode = "AUTO"
    sc.render.film_transparent = transparent
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    sc.render.image_settings.color_depth = "8"
    sc.render.resolution_percentage = 100
    world = bpy.data.worlds.get("IconWorld") or bpy.data.worlds.new("IconWorld")
    world.use_nodes = True
    bgn = world.node_tree.nodes.get("Background")
    col = bg or (0.85, 0.9, 1.0)
    bgn.inputs[0].default_value = (*col, 1)
    bgn.inputs[1].default_value = 0.55
    sc.world = world
    _ensure_rig()


def _ensure_rig():
    if "IconCam" in bpy.data.objects:
        return
    rig = bpy.data.collections.new("IconRig")
    bpy.context.scene.collection.children.link(rig)
    cam = bpy.data.cameras.new("IconCam")
    cam.type = "ORTHO"
    co = bpy.data.objects.new("IconCam", cam)
    rig.objects.link(co)
    bpy.context.scene.camera = co

    def sun(name, d, strength, angle=10):
        l = bpy.data.lights.new(name, "SUN")
        l.energy = strength
        l.angle = math.radians(angle)
        o = bpy.data.objects.new(name, l)
        o.rotation_euler = Vector(d).normalized().to_track_quat("Z", "Y").to_euler()
        rig.objects.link(o)
    # direction vectors point FROM the scene TO the light
    sun("KeyLight", (-0.55, -0.75, 1.0), 2.6, 15)
    sun("FillLight", (1.0, -0.4, 0.2), 0.7, 30)
    sun("RimLight", (0.3, 1.0, 0.6), 1.8, 10)


def visible_points(cols):
    pts = []
    dg = bpy.context.evaluated_depsgraph_get()
    for col in cols:
        for o in col.all_objects:
            if o.type != "MESH":
                continue
            mw = o.matrix_world
            ev = o.evaluated_get(dg)
            for v in ev.data.vertices:
                pts.append(mw @ v.co)
    return pts


def frame_camera(cols, view=VIEW_34, pad=0.1, size=(256, 256)):
    sc = bpy.context.scene
    cam = sc.camera
    d = Vector(view).normalized()
    up0 = Vector((0, 0, 1))
    right = d.cross(up0).normalized() * -1  # camera right
    right = up0.cross(d).normalized()
    up = d.cross(right).normalized()
    pts = visible_points(cols)
    if not pts:
        pts = [Vector((0, 0, 0))]
    xs = [p.dot(right) for p in pts]
    ys = [p.dot(up) for p in pts]
    zs = [p.dot(d) for p in pts]
    cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
    w, h = max(xs) - min(xs), max(ys) - min(ys)
    aspect = size[0] / size[1]
    scale = max(w / aspect, h) if aspect >= 1 else max(w, h * aspect)
    scale = max(scale, 1e-3) / (1 - 2 * pad)
    cam.data.ortho_scale = scale * (aspect if aspect >= 1 else 1)
    center = right * cx + up * cy + d * max(zs)
    cam.location = center + d * 10
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    cam.data.clip_start = 0.01
    cam.data.clip_end = 10 + (max(zs) - min(zs)) + 20
    sc.render.resolution_x, sc.render.resolution_y = size


def render_raw(path):
    sc = bpy.context.scene
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return C.read_image(path)


# ------------------------------------------------------------------------------------- post process

def _dilate(a, r):
    out = a.copy()
    h, w = a.shape
    p = np.pad(a, r)
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            if dx * dx + dy * dy > r * r + r * 0.5:
                continue
            out = np.maximum(out, p[r + dy:r + dy + h, r + dx:r + dx + w])
    return out


def _blur(a, r, passes=2):
    for _ in range(passes):
        k = 2 * r + 1
        p = np.pad(a, ((r, r), (0, 0)), mode="edge")
        c = np.cumsum(p, axis=0)
        c = np.vstack([np.zeros((1, a.shape[1])), c])
        a = (c[k:] - c[:-k]) / k
        p = np.pad(a, ((0, 0), (r, r)), mode="edge")
        c = np.cumsum(p, axis=1)
        c = np.hstack([np.zeros((a.shape[0], 1)), c])
        a = (c[:, k:] - c[:, :-k]) / k
    return a


def _over(top, bottom):
    """Straight-alpha 'over'."""
    ta, ba = top[..., 3:4], bottom[..., 3:4]
    oa = ta + ba * (1 - ta)
    rgb = (top[..., :3] * ta + bottom[..., :3] * ba * (1 - ta)) / np.maximum(oa, 1e-6)
    return np.concatenate([rgb, oa], axis=-1)


def stylize(img, outline=True, shadow=True, outline_color=(0.1, 0.11, 0.16)):
    h, w, _ = img.shape
    s = w / 256.0
    a = img[..., 3]
    res = img.copy()
    if outline:
        r = max(1, int(round(3 * s)))
        o = _dilate(a, r)
        o = np.clip(_blur(o, max(1, int(s)), 1) * 1.15, 0, 1)
        ol = np.zeros_like(img)
        ol[..., :3] = outline_color
        ol[..., 3] = o
        res = _over(res, ol)
    if shadow:
        sa = res[..., 3]
        dx, dy = int(round(3 * s)), int(round(5 * s))
        sh = np.zeros_like(sa)
        sh[dy:, dx:] = sa[:h - dy, :w - dx]
        sh = _blur(sh, max(1, int(3 * s))) * 0.35
        sl = np.zeros_like(img)
        sl[..., 3] = sh
        res = _over(res, sl)
    return res


def render_icon(cols, out_path, view=VIEW_34, size=256, outline=True, shadow=True, pad=0.1):
    """Render the given collections (others hidden) to out_path with the icon style."""
    cols = list(cols)
    for c in bpy.context.scene.collection.children:
        if c.name == "IconRig":
            continue
        c.hide_render = c not in cols
    frame_camera(cols, view, pad=pad, size=(size, size))
    tmp = os.path.join(_tmp, "r.png")
    img = render_raw(tmp)
    img = stylize(img, outline, shadow)
    C.write_png(out_path, img)
    return out_path
