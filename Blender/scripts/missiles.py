"""Projectile models (MissileGraphic ids), centered at the origin, nose pointing +X.
Variants share one mesh: each shape is exported once and copied to every id that uses it."""
import bpy  # noqa: F401  (bpy must be imported before mathutils/bmesh)
import os

from mathutils import Matrix

import common as C
import shapes as S

# shape name -> builder
SHAPES = {
    "rocket_red": lambda n: S.rocket(n, "red", "grey_light", "gun", stripe="yellow"),
    "rocket_ap": lambda n: S.rocket(n, "gun", "silver", "gun_dark", length=0.95, stripe="red"),
    "rocket_cluster": lambda n: S.rocket(n, "red", "yellow", "red_dark", length=0.9, r=0.11, stripe="yellow"),
    "rocket_cluster_small": lambda n: S.rocket(n, "yellow", "red", "red_dark", length=0.45, r=0.06),
    "rocket_frag": lambda n: S.rocket(n, "olive", "gun", "olive_dark", length=1.0, r=0.1, stripe="yellow"),
    "rocket_mini": lambda n: S.rocket(n, "green", "red", "green_dark", length=0.6, r=0.075),
    "rocket_seeker": lambda n: S.rocket(n, "white", "red", "red", length=0.8, r=0.08, stripe="red"),
    "rocket_firework": lambda n: S.rocket(n, "magenta", "yellow", "blue", length=0.6, r=0.07, stripe="yellow"),
    "artillery_shell": lambda n: S.shell(n, "olive", "red", 0.5, 0.1),
    "mortar_shell": lambda n: S.shell(n, "gun", "yellow", 0.45, 0.1) + S.fins(n, -0.2, 0.06, 0.12, C.mat("gun_dark"), height=0.07),
    "gl_shell": lambda n: S.shell(n, "gold", "olive", 0.3, 0.08),
    "nuke": lambda n: S.bomb(n, "olive", "yellow", 1.0, 0.22),
    "nuke_mega": lambda n: S.bomb(n, "gun", "red", 1.35, 0.3),
    "fuel_air": lambda n: S.bomb(n, "steel", "red", 0.75, 0.16, sign=False),
    "napalm": lambda n: S.canister(n, "orange", "gun_dark", 0.15, 0.4, "red"),
    "doomsday": lambda n: S.rock(n, 0.4, 11, "mesa_dark") + S.blob(n + "g", "fire", 0.16, 3, 3),
    "bullet": lambda n: S.bullet(n, "gold", 0.24, 0.045),
    "bullet_big": lambda n: S.bullet(n, "gold", 0.32, 0.06),
    "pellet": lambda n: [C.sphere(n + "_p", (0, 0, 0), 0.06, C.mat("grey_dark"), 8, 6)],
    "rail_slug": lambda n: S.bolt(n, "blue", 0.7, 0.06),
    "laser_red": lambda n: S.bolt(n, "red", 0.55, 0.05),
    "laser_rose": lambda n: S.bolt(n, "rose", 0.6, 0.06),
    "laser_orange": lambda n: S.bolt(n, "orange", 0.65, 0.065),
    "laser_purple": lambda n: S.bolt(n, "purple", 0.7, 0.07),
    "laser_green": lambda n: S.bolt(n, "lime", 0.75, 0.075),
    "mining_beam": lambda n: S.bolt(n, "yellow", 0.6, 0.07),
    "plasma_big": lambda n: S.orb(n, "plasma", 0.2),
    "plasma_mid": lambda n: S.orb(n, "plasma", 0.14),
    "plasma_small": lambda n: S.orb(n, "plasma", 0.09),
    "plasma_bomb": lambda n: S.orb(n, "plasma", 0.18, cage="grey_dark"),
    "cannonball": lambda n: S.cannonball(n, 0.16),
    "impact_ball": lambda n: S.cannonball(n, 0.2, "orange_dark"),
    "flare": lambda n: S.bullet(n, "orange", 0.3, 0.08) + [C.sphere(n + "_g", (0.05, 0, 0), 0.06, C.mat("yellow"), 8, 5)],
    "grenade": lambda n: S.grenade(n, "olive"),
    "grenade_cluster": lambda n: S.grenade(n, "red", 0.18),
    "grenade_cluster_small": lambda n: S.grenade(n, "red", 0.09, bumps=False),
    "grenade_teleport": lambda n: S.grenade(n, "purple", 0.16, "silver", False),
    "lemon": lambda n: S.lemon(n),
    "lemon_shard": lambda n: [C.sphere(n + "_s", (0, 0, 0), (0.09, 0.06, 0.03), C.mat("lemon"), 8, 5)],
    "gas_can": lambda n: S.canister(n, "green", "gun", label="yellow"),
    "gas_cloud": lambda n: S.blob(n, "slime", 0.2, 6, 5),
    "cinder": lambda n: S.rock(n, 0.17, 5, "rock_dark") + [C.sphere(n + "_e", (0.0, -0.08, 0.05), 0.07, C.mat("fire"), 8, 5)],
    "cinder2": lambda n: S.rock(n, 0.12, 6, "rock_dark") + [C.sphere(n + "_e", (0.0, -0.06, 0.03), 0.05, C.mat("fire"), 8, 5)],
    "sticky": lambda n: S.blob(n, "slime", 0.15),
    "sticky_blob": lambda n: S.blob(n, "slime", 0.09, 7, 2),
    "goo": lambda n: S.blob(n, "grey", 0.14, 8, 4),
    "goo_shard": lambda n: S.blob(n, "grey", 0.07, 9, 2),
    "dynamite": lambda n: S.dynamite(n),
    "molotov": lambda n: S.molotov(n),
    "fire_blob": lambda n: S.flame(n, 0.16),
    "fire_small": lambda n: S.flame(n, 0.09),
    "water": lambda n: S.blob(n, "sky", 0.13, 5, 3),
    "balloon": lambda n: S.balloon(n, "blue"),
    "mine": lambda n: S.mine(n, "red"),
    "flame_mine": lambda n: S.mine(n, "fire", "orange_dark"),
    "rock": lambda n: S.rock(n, 0.25, 1),
    "snowball": lambda n: S.snowball(n),
    "beanbag": lambda n: S.blob(n, "tan", 0.13, 9, 3),
    "egg1": lambda n: S.egg(n, ("pink", "yellow", "cyan")),
    "egg2": lambda n: S.egg(n, ("cyan", "pink", "yellow")),
    "egg3": lambda n: S.egg(n, ("lime", "purple", "orange")),
    "cat": lambda n: S.cat(n),
    "broom": lambda n: [C.cyl(n + "_h", (-0.5, 0, 0), (0.25, 0, 0), 0.025, 0.025, C.mat("wood"), 8),
                        C.cyl(n + "_b", (0.2, 0, 0), (0.5, 0, 0), 0.05, 0.15, C.mat("yellow"), 12)],
    "scythe": lambda n: _scythe_spin(n),
    "glove": lambda n: S.glove(n),
    "wind": lambda n: S.wind(n),
    "mushroom": lambda n: S.mushroom(n),
    "caltrops": lambda n: S.caltrop(n),
    "drill": lambda n: S.drill_bit(n, length=0.55, r=0.13, x0=-0.27) + [S.tube(n + "_b", -0.4, -0.27, 0.12, C.mat("yellow"))],
    "shard_metal": lambda n: S.shard(n, "steel", 0.1),
    "shard_stone": lambda n: S.shard(n, "rock_dark", 0.1, 3),
    "shard_fire": lambda n: S.shard(n, "fire", 0.08, 4),
    "shard_plasma": lambda n: S.shard(n, "plasma", 0.08, 5),
    "shard_ice": lambda n: S.shard(n, "ice_dark", 0.1, 6),
    "shield_wall": lambda n: [C.box(n + "_w", (0, 0, 0), (0.25, 0.4, 1.4), C.mat("cyan"), bevel=0.06, bevel_segs=2)],
    "void": lambda n: S.orb(n, "purple_dark", 0.16, cage="violet"),
    "radio": lambda n: [C.box(n + "_b", (0, 0, 0), (0.2, 0.12, 0.3), C.mat("olive"), bevel=0.03),
                        C.cyl(n + "_a", (-0.05, 0, 0.15), (-0.07, 0, 0.45), 0.012, 0.01, C.mat("grey"), 6)],
}

MAP = {
    "ArmorPiercingRocket": "rocket_ap", "BasicNuke": "nuke", "Beanbag": "beanbag", "CinderGrenade1": "cinder",
    "CinderGrenade2": "cinder2", "ClusterGrenade": "grenade_cluster", "ClusterGrenadeCluster": "grenade_cluster_small",
    "ClusterRocket": "rocket_cluster", "ClusterRocketCluster": "rocket_cluster_small", "DoomsdayDevice": "doomsday",
    "DoomsdayDeviceShard": "shard_stone", "Drill": "drill", "DrillShard": "shard_stone", "Dynamite": "dynamite",
    "FlameMine": "flame_mine", "FlameMineBurning": "fire_blob", "FlameMineShard": "shard_fire", "Flamethrower": "fire_blob",
    "FlamethrowerShard": "fire_small", "FlareGun": "flare", "FragmentationMissile": "rocket_frag",
    "FragmentationMissileFragment": "shard_metal", "GasGrenade": "gas_can", "GasGrenadeGas": "gas_cloud",
    "Grenade": "grenade", "GreyGoo": "goo", "GreyGooShard": "goo_shard", "HeatSeeker": "rocket_seeker",
    "ImpactCannon": "impact_ball", "LemonGrenade": "lemon", "LemonGrenadeShard": "lemon_shard", "MegaNuke": "nuke_mega",
    "Mine": "mine", "MiniBazooka": "rocket_mini", "Minigun": "bullet", "Molotov": "molotov", "MolotovBurning": "fire_blob",
    "MolotovShard": "fire_small", "Mortar": "mortar_shell", "Napalm": "napalm", "Pistol": "bullet",
    "PlasmaCannon": "plasma_big", "PlasmaCannonMid": "plasma_mid", "PlasmaCannonSmall": "plasma_small", "Punch": "glove",
    "Railgun": "rail_slug", "Rock": "rock", "ShieldWall": "shield_wall", "ShieldWallShard": "shard_ice",
    "Shotgun": "pellet", "SniperRifle": "bullet_big", "StickyBomb": "sticky", "StickyBombBlob": "sticky_blob",
    "StickyBombShard": "sticky_blob", "TeleportationGrenade": "grenade_teleport", "VoidGenerator": "void",
    "EasterEgg1": "egg1", "EasterEgg2": "egg2", "EasterEgg3": "egg3", "Cannon": "cannonball", "LaserPistol": "laser_red",
    "LaserPistol2": "laser_rose", "LaserPistol3": "laser_orange", "LaserPistol4": "laser_purple",
    "LaserPistol5": "laser_green", "PlasmaMortar": "plasma_big", "PlasmaMortarShard": "shard_plasma",
    "PlasmaMortarMid": "plasma_mid", "PlasmaMortarSmall": "plasma_small", "FireHose": "water", "MiningLaser": "mining_beam",
    "PlasmaBomb": "plasma_bomb", "PlasmaBombShard": "shard_plasma", "PlasmaBombMid": "plasma_mid",
    "PlasmaBombSmall": "plasma_small", "GrenadeLauncher": "gl_shell", "FuelAirBomb": "fuel_air",
    "FuelAirBombShard": "shard_fire", "ArtilleryStrike": "artillery_shell", "ArtilleryStrikeLauncher": "radio",
    "ArtilleryStrikeLauncherEmit": "artillery_shell", "ArtilleryStrikeShard": "shard_metal", "WaterBalloon": "balloon",
    "WandWind": "wind", "Scythe": "scythe", "Cat": "cat", "Broom": "broom", "Mushroom": "mushroom", "Snowball": "snowball",
    "Fireworks": "rocket_firework", "Caltrops": "caltrops",
}


def _scythe_spin(n):
    import math
    o = [C.cyl(n + "_h", (-0.5, 0, 0), (0.5, 0, 0), 0.03, 0.03, C.mat("wood_dark"), 8)]
    pts = []
    for i in range(9):
        t = i / 8
        a = math.radians(90 - 150 * t)
        pts.append((0.4 * math.cos(a), 0.4 * math.sin(a)))
    inner = [(x * 0.72, y * 0.72 - 0.04) for (x, y) in reversed(pts)]
    b = C.extrude(n + "_b", pts + inner, 0.025, C.mat("silver"))
    b.data.transform(Matrix.Translation((0.5, 0, -0.05)))
    o.append(b)
    return o


def shape_for(mid):
    return MAP.get(mid, "bullet")


def build(mid, col):
    """Preview/single build by missile id or shape name."""
    shape = mid if mid in SHAPES else shape_for(mid)
    C.use_collection(col)
    ob = C.join(SHAPES[shape](shape + "_"), shape)
    C.use_collection(None)
    return ob


def run():
    C.reset()
    ids = C.ids("MissileGraphic")
    by_shape = {}
    for mid in ids:
        by_shape.setdefault(shape_for(mid), []).append(mid)
    out_dir = os.path.join(C.MODELS, "Missiles")
    cols = {}
    stats = {}
    for shape, mids in by_shape.items():
        col = C.new_collection("M_" + shape)
        ob = build(shape, col)
        stats[shape] = C.tri_count([ob])
        paths = [os.path.join(out_dir, m + ".fbx") for m in mids]
        C.export_collection(col, paths[0], aliases=paths[1:])
        cols[shape] = col
    C.save_blend("missiles")
    print("[missiles] %d ids from %d shapes, max tris %d" % (len(ids), len(by_shape), max(stats.values())))
    return cols, by_shape
