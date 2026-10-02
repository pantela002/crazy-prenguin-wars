"""Re-import an exported FBX with bpy and report size/orientation in Unity terms.
Usage: python3 Blender/scripts/verify_fbx.py Assets/CPW/Resources/Models/Penguin/Penguin.fbx"""
import sys

import bpy
from mathutils import Vector


def unity(v):
    # Blender (after FBX import with the same axis settings) -> Unity: Unity mirrors X on import.
    return Vector((-v.x, v.z, -v.y))


def verify(path, verbose=True):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path, axis_forward="-Z", axis_up="Y")
    bpy.context.view_layer.update()
    lo, hi = Vector((1e9,) * 3), Vector((-1e9,) * 3)
    parts = {}
    for o in bpy.data.objects:
        if o.type == "MESH":
            pts = [unity(o.matrix_world @ v.co) for v in o.data.vertices]
            plo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
            phi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
            parts[o.name] = (plo, phi)
            lo = Vector(map(min, lo, plo))
            hi = Vector(map(max, hi, phi))
        else:
            parts[o.name] = (unity(o.matrix_world.to_translation()),) * 2
    if verbose:
        print("Unity bounds min", tuple(round(x, 3) for x in lo), "max", tuple(round(x, 3) for x in hi),
              "size", tuple(round(x, 3) for x in hi - lo))
        for n, (a, b) in sorted(parts.items()):
            c = (a + b) / 2
            print("  %-12s center(%.2f, %.2f, %.2f) scale%s" % (n, c.x, c.y, c.z, tuple(round(s, 3) for s in bpy.data.objects[n].scale)))
    return lo, hi, parts


if __name__ == "__main__":
    for p in sys.argv[1:]:
        print("==", p)
        verify(p)
