"""Weapons and supplies of the original Crazy Penguin Wars that the shipped config's Item table lost.

Called by Tools/build_data.py after the original config is merged, so re-running the import keeps them.
The original config still has most of their pieces (ItemPrice, WeaponIcon, WeaponGraphic, MissileGraphic,
MissilePhysic, Explosion, Follower, Sound rows and the TID strings); the Item, Emitter, EmitMissile,
EmitExplosion and Missile rows that tie them together were removed and are rebuilt here.

Every number marked "INV" is invented for the remake (balanced against existing weapons of the same tier);
everything else is the original value. Behaviours that need code use fields the C# reads
(Missile.SimpleScript "Orbital"/"Crawl", Missile.Type "Sticky", Explosion.SimpleScript "BuildWall",
Emitter.SoundOnce), see Assets/CPW/Scripts/Weapons.
"""

DT = '$DATA_TYPE'


def _sec(cfg, name):
    return cfg.setdefault(name, {})


def _put(cfg, section, row):
    """Add a row unless the original already has one with that id (the original always wins)."""
    sec = _sec(cfg, section)
    rid = row['ID']
    if rid in sec:
        return sec[rid]
    sec[rid] = row
    return row


def _override(cfg, section, rid, **fields):
    """Change fields of an original row (each use is commented with the reason)."""
    sec = _sec(cfg, section)
    if rid in sec:
        sec[rid].update(fields)


def _missile_emitter(cfg, eid, missile, affects=('all',), sound=None, number=1, delay=0, a1=0, a2=0, offset=1,
                     followers=(), spread=0, sound_once=False):
    _put(cfg, 'EmitMissile', {'ID': missile, 'Missile': '#Missile.' + missile})
    row = {'ID': eid, 'SpecialType': 'MissileEmitter', 'SpecialEffect': '#EmitMissile.' + missile,
           'AffectsObjects': list(affects), 'SoundID': sound if sound is not None else eid, 'Number': number,
           'Delay': delay, 'Spread': spread, 'AngleOne': a1, 'AngleTwo': a2, 'DirectionAndOffsetBy': offset,
           'UseHitDirection': False, 'RandomOffset': False}
    if followers:
        row['Followers'] = ['#Follower.' + f for f in followers]
    if sound_once:
        row['SoundOnce'] = True
    _put(cfg, 'Emitter', row)


def _explosion_emitter(cfg, eid, explosion, affects=('all',), sound=None, followers=(), number=1, delay=0,
                       a1=0, a2=0, offset=1):
    _put(cfg, 'EmitExplosion', {'ID': explosion, 'Explosion': '#Explosion.' + explosion, 'AnimationOpponent': 'dying'})
    row = {'ID': eid, 'SpecialType': 'ExplosionEmitter', 'SpecialEffect': '#EmitExplosion.' + explosion,
           'AffectsObjects': list(affects), 'SoundID': sound if sound is not None else eid, 'Number': number,
           'Delay': delay, 'Spread': 0, 'AngleOne': a1, 'AngleTwo': a2, 'DirectionAndOffsetBy': offset,
           'UseHitDirection': False, 'RandomOffset': False}
    if followers:
        row['Followers'] = ['#Follower.' + f for f in followers]
    _put(cfg, 'Emitter', row)


def _missile(cfg, mid, physics, mtype, emitters, graphic=None, timer=0, imin=0, imax=0, tail='', tail_dist=10,
             duration=0, interval=0, script=None, camera=True, tail_time=0):
    row = {'ID': mid, 'Physics': '#MissilePhysic.' + physics, 'Type': mtype,
           'Emitters': ['#Emitter.' + e for e in emitters], 'Activation': 250, 'Timer': timer,
           'FiringImpulseMin': imin, 'FiringImpulseMax': imax, 'ParticleEffect': tail, 'ParticleEffectLow': tail,
           'ParticleStreamSpawnDistance': tail_dist, 'ParticleStreamSpawnTime': tail_time, 'CameraFollowed': camera,
           'Duration': duration, 'Interval': interval}
    if graphic:
        row['Graphics'] = '#MissileGraphic.' + graphic
    if script:
        row['SimpleScript'] = list(script)
    _put(cfg, 'Missile', row)


def _item(cfg, iid, sort, cats, level, amount, targeting, emitters, anim, sim=2000, graphic=None, icon=None,
          name=None, desc=None, drop=1, vip=False):
    row = {'ID': iid, 'Name': name or iid.upper(), 'Description': desc or iid.upper() + 'DESC', 'SortPriority': sort,
           'Type': 'Weapon', 'Category': list(cats) + ['Practice'], 'RequiredLevel': level, 'DropRatio': drop,
           'Icon': '#WeaponIcon.' + (icon or iid), 'PriceInfo': '#ItemPrice.' + iid, 'AmountPurchased': amount,
           'Graphics': '#WeaponGraphic.' + (graphic or iid), 'AnimationType': anim, 'Targeting': targeting,
           'Emitters': ['#Emitter.' + e for e in emitters], 'AllowRotation': True, 'SimulationDistance': sim,
           'SimulationSpawnDistance': 20, 'RemakeAdded': True}
    if vip:
        row['IsVip'] = True
    _put(cfg, 'Item', row)


def patch_config(cfg):
    # ------------------------------------------------------------------ shared pieces
    _put(cfg, 'ExplosionShape', {'ID': 'GreyGooBite', 'MinRadius': 12, 'MaxRadius': 18, 'Angle': 20})     # INV
    _put(cfg, 'ExplosionShape', {'ID': 'ShieldWallBuild', 'MinRadius': 1, 'MaxRadius': 2, 'Angle': 40})   # INV (no crater)
    # The original glue lasted 500 s (practically for the rest of the match); 12 s is about one of the
    # victim's turns at the default turn time. INV
    _override(cfg, 'Follower', 'Status_SlowGoo', Duration=12000)

    # ------------------------------------------------------------------ Orbital Plasma Attack (Item "OrbitalLaser")
    # Tap a point: a plasma bolt drops from the top of the level straight down onto it and hits the first
    # thing in its way ("Make sure the path is clear"). Explosion OrbitalLaser is original (500 Fire, capped by
    # Tuner.DamageSingleHitMax 300; radius 50 px).
    _explosion_emitter(cfg, 'OrbitalLaserExplosion', 'OrbitalLaser', sound='OrbitalLaserExplosion')
    _put(cfg, 'MissilePhysic', {'ID': 'OrbitalLaser', 'FixedRotation': True, 'BodyType': 'dynamic', 'Shape': 'Circle',
                                'Radius': 6, 'Density': 75, 'Friction': 0.3, 'Restitution': 0, 'Bullet': True,
                                'GravityScale': 0})                                                       # INV
    _put(cfg, 'MissileGraphic', {'ID': 'OrbitalLaser', 'SWF': 'flash/weapons/ammo.swf', 'Export': 'ammo_orbital_laser'})  # INV export name
    _missile(cfg, 'OrbitalLaser', 'OrbitalLaser', 'Missile', ['OrbitalLaserExplosion'], graphic='OrbitalLaser',
             imin=0, imax=0, tail='LaserTail', tail_dist=12, script=['Orbital'])
    _missile_emitter(cfg, 'OrbitalLaser', 'OrbitalLaser', sound='OrbitalLaser')
    _item(cfg, 'OrbitalLaser', 40, ['Special'], 42, 1, 'Point', ['OrbitalLaser'], 'small_weapon', sim=0)  # INV level/amount

    # ------------------------------------------------------------------ Heat Seeking Missile
    # A bazooka rocket with the original Follower.HeatSeeker (Homing 60 towards the closest enemy within 500 px,
    # after 500 ms). Explosion HeatSeeker is original (65, radius 100).
    _explosion_emitter(cfg, 'HeatSeekerExplosion', 'HeatSeeker')
    _missile(cfg, 'HeatSeeker', 'HeatSeeker', 'Missile', ['HeatSeekerExplosion'], graphic='HeatSeeker',
             imin=200, imax=2475, tail='MissileTail', tail_dist=14)                                     # = BasicNuke
    _missile_emitter(cfg, 'HeatSeeker', 'HeatSeeker', followers=['HeatSeeker'])
    _put(cfg, 'WeaponGraphic', {'ID': 'HeatSeeker', 'SWF': 'flash/weapons/weapon_animations.swf', 'Export': 'bazooka'})
    _put(cfg, 'WeaponIcon', {'ID': 'HeatSeeker', 'SWF': 'flash/ui/icons_weapons.swf', 'Export': 'icon_wpn_heat_seeker'})
    _item(cfg, 'HeatSeeker', 41, ['Rockets'], 23, 3, 'PowerBar', ['HeatSeeker'], 'large_weapon')        # INV level/amount

    # ------------------------------------------------------------------ Gas Grenade
    # Bouncy grenade, 3 s timer, small pop, then 5 drifting gas clouds for 4 s that poison (Status_Poison)
    # everyone they touch, the thrower included.
    _put(cfg, 'Explosion', {'ID': 'GasGrenadePop', 'Attack': 'Add:15:Normal', 'ExplosionShape': '#ExplosionShape.None',
                            'DamageRadius': 80, 'ImpulseRadius': 80, 'Impulse': 80, 'ParticleEffect': 'PoisonExplosion',
                            'ParticleEffectLow': 'PoisonExplosion'})                                     # INV
    _explosion_emitter(cfg, 'GasGrenadeExplosion', 'GasGrenadePop', sound='GasGrenadeGasExplosion')
    _explosion_emitter(cfg, 'GasGrenadeGasExplosion', 'GasGrenadeGas', affects=('penguin',),
                       followers=['Status_Poison'], sound='')
    _missile(cfg, 'GasGrenadeGas', 'GasGrenadeGas', 'Enviroment', ['GasGrenadeGasExplosion'], graphic='GasGrenadeGas',
             imin=1.5, duration=4000, interval=700, tail='PoisonGasTail', tail_time=200, camera=False)   # INV
    _missile_emitter(cfg, 'GasGrenadeGas', 'GasGrenadeGas', number=5, a1=-70, a2=140, sound='')         # INV fan
    _missile(cfg, 'GasGrenade', 'GasGrenade', 'Grenade', ['GasGrenadeExplosion', 'GasGrenadeGas'],
             graphic='GasGrenade', timer=3000, imin=200, imax=2475, tail='GrenadeTail', tail_dist=8)
    _missile_emitter(cfg, 'GasGrenade', 'GasGrenade')
    _item(cfg, 'GasGrenade', 42, ['Grenades'], 14, 5, 'PowerBar', ['GasGrenade'], 'small_object', sim=2500)  # INV level

    # ------------------------------------------------------------------ Glue Bomb (Item "StickyBomb")
    # Sticks to the first penguin or wall it touches, goes off 2.5 s after the throw, damages and glues
    # (Status_SlowGoo: no jumping, slow walking) everyone around.
    _put(cfg, 'Explosion', {'ID': 'StickyBombBlast', 'Attack': 'Add:45:Normal', 'ExplosionShape': '#ExplosionShape.Grenade',
                            'DamageRadius': 150, 'ImpulseRadius': 150, 'Impulse': 120, 'ParticleEffect': 'GreenGooExplosion',
                            'ParticleEffectLow': 'GreenGooExplosion', 'ShakeEffectTime': 500, 'ShakeEffectStrength': 5})  # INV
    _explosion_emitter(cfg, 'StickyBombExplosion', 'StickyBombBlast', followers=['Status_SlowGoo'], sound='StickyBombBlob')
    _missile(cfg, 'StickyBomb', 'StickyBomb', 'Sticky', ['StickyBombExplosion'], graphic='StickyBomb',
             timer=2500, imin=200, imax=2475, tail='GrenadeTail', tail_dist=8)                          # INV timer
    _missile_emitter(cfg, 'StickyBomb', 'StickyBomb')
    _item(cfg, 'StickyBomb', 43, ['Grenades'], 9, 5, 'PowerBar', ['StickyBomb'], 'small_object', sim=2500)  # INV level

    # ------------------------------------------------------------------ Lemon Grenade
    # Bouncy grenade (3 s): acid blast (original Explosion LemonGrenade, 50 Acid + Status_Acid damage over time)
    # and 6 acid drops that keep eating the terrain under them for 3 s.
    _explosion_emitter(cfg, 'LemonGrenadeExplosion', 'LemonGrenade', followers=['Status_Acid'], sound='LemonGrenadeShard')
    _explosion_emitter(cfg, 'LemonGrenadeShardExplosion', 'LemonGrenadeShard', sound='')
    _missile(cfg, 'LemonGrenadeShard', 'LemonGrenadeShard', 'Enviroment', ['LemonGrenadeShardExplosion'],
             graphic='LemonGrenadeShard', imin=9, duration=3000, interval=500, tail='AcidTail', tail_time=150,
             camera=False)                                                                               # INV
    _missile_emitter(cfg, 'LemonGrenadeShard', 'LemonGrenadeShard', number=6, a1=-75, a2=150, sound='')  # INV fan
    _missile(cfg, 'LemonGrenade', 'LemonGrenade', 'Grenade', ['LemonGrenadeExplosion', 'LemonGrenadeShard'],
             graphic='LemonGrenade', timer=3000, imin=200, imax=2475, tail='GrenadeTail', tail_dist=8)
    _missile_emitter(cfg, 'LemonGrenade', 'LemonGrenade')
    _item(cfg, 'LemonGrenade', 44, ['Grenades'], 22, 5, 'PowerBar', ['LemonGrenade'], 'small_object', sim=2500)  # INV level
    # slot machine: three lemons pay Lemon Grenades (the shipped row pointed at the bazooka placeholder)
    _override(cfg, 'SlotMachine', 'SetLemon', RewardItem=['#Item.LemonGrenade'])

    # ------------------------------------------------------------------ Point to Point Teleporter
    # Tap a point: the original Explosion Teleport (50 damage + knockback around the destination, SimpleScript
    # Teleport) moves the user there.
    _explosion_emitter(cfg, 'PointTeleport', 'Teleport', affects=('enemy', 'object'))
    _item(cfg, 'PointTeleport', 45, ['Special'], 12, 3, 'Point', ['PointTeleport'], 'small_weapon', sim=0)  # INV level/amount

    # ------------------------------------------------------------------ Teleportation Grenade
    _explosion_emitter(cfg, 'TeleportationGrenadeExplosion', 'Teleport', affects=('enemy', 'object'), sound='PointTeleport')
    _missile(cfg, 'TeleportationGrenade', 'TeleportationGrenade', 'Grenade', ['TeleportationGrenadeExplosion'],
             graphic='TeleportationGrenade', timer=3000, imin=200, imax=2475, tail='GrenadeTail', tail_dist=8)
    _missile_emitter(cfg, 'TeleportationGrenade', 'TeleportationGrenade')
    _item(cfg, 'TeleportationGrenade', 46, ['Grenades'], 6, 3, 'PowerBar', ['TeleportationGrenade'], 'small_object',
          sim=2500)                                                                                       # INV level/amount

    # ------------------------------------------------------------------ Flamethrower
    # A short stream of 12 bouncing flames (0.65 s each, about 15 units of reach). A flame that touches an enemy
    # burns it (original Explosion Flamethrower 4 Fire + Status_Fire); where a flame dies it burns anyone, the
    # user too.
    _put(cfg, 'MissilePhysic', {'ID': 'FlamethrowerFlame', 'FixedRotation': True, 'BodyType': 'dynamic',
                                'Shape': 'Circle', 'Radius': 2, 'Density': 5, 'Friction': 0.3, 'Restitution': 0.6,
                                'GravityScale': 0.35})                                                   # INV bounce/gravity
    _put(cfg, 'FollowerPhysic', {'ID': 'FlamethrowerBurn', 'Radius': 20, 'IsSensor': True, 'BodyType': 'dynamic',
                                 'Shape': 'Circle'})                                                     # INV
    _explosion_emitter(cfg, 'FlamethrowerExplosion', 'Flamethrower', followers=['Status_Fire'], sound='')
    _put(cfg, 'Follower', {'ID': 'FlamethrowerBurn', 'Physics': '#FollowerPhysic.FlamethrowerBurn',
                           'AffectsObjects': ['enemy'], 'ApplyToObjects': ['weapon'], 'ActivationCooldown': 400,
                           'SimpleScript': ['TriggerEmission', 'true'], 'MultipleEmissions': True,
                           'Emitters': ['#Emitter.FlamethrowerExplosion']})                               # INV (like FireBurning)
    _missile(cfg, 'Flamethrower', 'FlamethrowerFlame', 'Grenade', ['FlamethrowerExplosion'], graphic='Flamethrower',
             timer=650, imin=11.5, imax=11.5, tail='MolotovTail', tail_dist=6, camera=False)             # INV ~22 u/s
    _missile_emitter(cfg, 'Flamethrower', 'Flamethrower', number=12, delay=50, a1=-5, a2=10,
                     followers=['FlamethrowerBurn'], sound_once=True)                                     # INV stream
    _item(cfg, 'Flamethrower', 47, ['Guns'], 10, 5, 'Aiming', ['Flamethrower'], 'large_weapon', sim=320)  # INV level

    # ------------------------------------------------------------------ Grey Goo
    # A canister that bursts into 8 goo blobs; each crawls along and eats the terrain (a 0.6-0.9 unit bite every
    # 0.25 s for 4 s) and nibbles penguins it touches.
    _put(cfg, 'Explosion', {'ID': 'GreyGooSplash', 'Attack': 'Add:10:Normal', 'ExplosionShape': '#ExplosionShape.GreyGooBite',
                            'DamageRadius': 60, 'ImpulseRadius': 60, 'Impulse': 50, 'ParticleEffect': 'GreyGooExplosion',
                            'ParticleEffectLow': 'GreyGooExplosion'})                                    # INV
    _put(cfg, 'Explosion', {'ID': 'GreyGooBite', 'Attack': 'Add:3:Normal', 'ExplosionShape': '#ExplosionShape.GreyGooBite',
                            'DamageRadius': 24, 'ParticleEffect': 'GreyGooBite', 'ParticleEffectLow': 'GreyGooBite'})  # INV
    _explosion_emitter(cfg, 'GreyGooSplash', 'GreyGooSplash', sound='StickyBombBlob')
    _explosion_emitter(cfg, 'GreyGooBite', 'GreyGooBite', sound='')
    _missile(cfg, 'GreyGooShard', 'GreyGooShard', 'Enviroment', ['GreyGooBite'], graphic='GreyGooShard',
             imin=0.97, duration=4000, interval=250, tail='GreyGooTail', tail_time=250, script=['Crawl'],
             camera=False)                                                                               # INV ~5 u/s
    _missile_emitter(cfg, 'GreyGooShard', 'GreyGooShard', number=8, a1=-80, a2=160, sound='')            # INV fan
    _missile(cfg, 'GreyGoo', 'BasicNuke', 'Missile', ['GreyGooSplash', 'GreyGooShard'], graphic='GreyGoo',
             imin=200, imax=2475, tail='GrenadeTail', tail_dist=8)
    _missile_emitter(cfg, 'GreyGoo', 'GreyGoo', sound='Grenade')
    _put(cfg, 'WeaponGraphic', {'ID': 'GreyGoo', 'SWF': 'flash/weapons/weapon_animations.swf', 'Export': 'grey_goo'})
    _put(cfg, 'WeaponIcon', {'ID': 'GreyGoo', 'SWF': 'flash/ui/icons_weapons.swf', 'Export': 'icon_wpn_grey_goo'})
    _item(cfg, 'GreyGoo', 48, ['Special'], 44, 1, 'PowerBar', ['GreyGoo'], 'small_object', sim=2500)     # INV level/amount

    # ------------------------------------------------------------------ Shield Wall
    # A generator that lands and raises a wall of stone (7 stacked blobs, about 9 units high) that blocks
    # weapons and penguins until it is blown away. The wall is terrain, so online snapshots carry it.
    _put(cfg, 'Explosion', {'ID': 'ShieldWallBuild', 'ExplosionShape': '#ExplosionShape.ShieldWallBuild',
                            'DamageRadius': 0, 'ParticleEffect': 'StoneExplosion', 'ParticleEffectLow': 'StoneExplosion',
                            'ShakeEffectTime': 400, 'ShakeEffectStrength': 6,
                            'SimpleScript': ['BuildWall', '7', '20']})                                   # INV 7 x 20 px
    _explosion_emitter(cfg, 'ShieldWallBuild', 'ShieldWallBuild', affects=('terrain',), sound='Shield')
    _missile(cfg, 'ShieldWall', 'ShieldWall', 'Grenade', ['ShieldWallBuild'], graphic='ShieldWall', timer=1500,
             imin=40, imax=495, tail='GrenadeTail', tail_dist=8)                                         # INV ~BasicNuke speed
    _missile_emitter(cfg, 'ShieldWall', 'ShieldWall', sound='Grenade')
    _put(cfg, 'WeaponIcon', {'ID': 'ShieldWall', 'SWF': 'flash/ui/icons_weapons.swf', 'Export': 'icon_wpn_shield_wall'})
    _item(cfg, 'ShieldWall', 49, ['Special'], 5, 3, 'PowerBar', ['ShieldWall'], 'small_object', sim=2500)  # INV level/amount

    # ------------------------------------------------------------------ Easter Eggs (Easter event)
    # A bouncy rubber egg (2.5 s) with the original Explosion EasterEgg (50, radius 200) that hatches two
    # smaller eggs (1.5 s, 20 damage each).
    _put(cfg, 'Explosion', {'ID': 'EasterEggSmall', 'Attack': 'Add:20:Normal', 'ExplosionShape': '#ExplosionShape.BasicNuke',
                            'DamageRadius': 100, 'ImpulseRadius': 100, 'Impulse': 120, 'ParticleEffect': 'ChocolateExplosion',
                            'ParticleEffectLow': 'ChocolateExplosion', 'ShakeEffectTime': 500, 'ShakeEffectStrength': 8})  # INV
    _explosion_emitter(cfg, 'EasterEggExplosion', 'EasterEgg', sound='ChocolateExplosion')
    _explosion_emitter(cfg, 'EasterEggSmallExplosion', 'EasterEggSmall', sound='ChocolateExplosion')
    _missile(cfg, 'EasterEgg2', 'EasterEgg', 'Grenade', ['EasterEggSmallExplosion'], graphic='EasterEgg2', timer=1500,
             imin=725, tail='GrenadeTail', tail_dist=8, camera=False)                                   # INV ~8 u/s
    _missile(cfg, 'EasterEgg3', 'EasterEgg', 'Grenade', ['EasterEggSmallExplosion'], graphic='EasterEgg3', timer=1700,
             imin=725, tail='GrenadeTail', tail_dist=8, camera=False)                                   # INV
    _missile_emitter(cfg, 'EasterEggHatch2', 'EasterEgg2', a1=15, a2=30, sound='')                     # INV angles
    _missile_emitter(cfg, 'EasterEggHatch3', 'EasterEgg3', a1=-45, a2=30, sound='')
    _missile(cfg, 'EasterEgg1', 'EasterEgg', 'Grenade', ['EasterEggExplosion', 'EasterEggHatch2', 'EasterEggHatch3'],
             graphic='EasterEgg1', timer=2500, imin=200, imax=2475, tail='GrenadeTail', tail_dist=8)     # INV timer
    _missile_emitter(cfg, 'EasterEgg', 'EasterEgg1', sound='EasterEgg')
    _item(cfg, 'EasterEgg', 50, ['Grenades'], 9, 5, 'PowerBar', ['EasterEgg'], 'small_object', sim=2500)  # INV level

    # ------------------------------------------------------------------ Choco-Cannon (Item "Cannon", Easter event)
    # A big chocolate cannonball (original MissilePhysic Cannon, Explosion Cannon 75, radius 150, no crater).
    _explosion_emitter(cfg, 'CannonExplosion', 'Cannon', sound='ChocolateExplosion')
    _missile(cfg, 'Cannon', 'Cannon', 'Missile', ['CannonExplosion'], graphic='Cannon', imin=25, imax=270,
             tail='TrapezoidTailSmall', tail_dist=14)                                                    # INV ~380 px/s
    _missile_emitter(cfg, 'Cannon', 'Cannon', sound='ChocolateCannon')
    _item(cfg, 'Cannon', 51, ['Rockets'], 20, 3, 'PowerBar', ['Cannon'], 'large_weapon')                # INV level/amount

    # ------------------------------------------------------------------ Snowball (winter event)
    # Original Missile/Emitter Snowball: 50 Ice damage and Status_Ice (slower, weaker against ice).
    _item(cfg, 'Snowball', 52, ['Grenades'], 4, 5, 'PowerBar', ['Snowball'], 'small_object')            # INV level
    _put(cfg, 'ItemPrice', {'ID': 'Snowball', 'InGame': 290, 'UnlockPricePremium': 6})                  # INV

    # ------------------------------------------------------------------ Broom (Halloween event)
    # Original Emitter Broom chain: the user is launched along the aim, and for 5 s rams every enemy it flies
    # past (Follower Broom → BroomExplosion 125). The launch blast is redone (the original offset it from the
    # body centre, here shots start at the muzzle).
    _put(cfg, 'Explosion', {'ID': 'BroomLaunch', 'ExplosionShape': '#ExplosionShape.None', 'DamageRadius': 60,
                            'ImpulseRadius': 60, 'Impulse': 700, 'ParticleEffect': 'BroomTail',
                            'ParticleEffectLow': 'BroomTail'})                                           # INV ~22 u/s launch
    _explosion_emitter(cfg, 'BroomLaunch', 'BroomLaunch', affects=('player',), sound='Broom', followers=['BroomStop'],
                       offset=-40)
    _item(cfg, 'Broom', 53, ['Special'], 15, 3, 'Aiming', ['BroomLaunch'], 'large_object', sim=0)       # INV level/amount
    _put(cfg, 'ItemPrice', {'ID': 'Broom', 'InGame': 690, 'UnlockPricePremium': 12})                    # INV

    # ------------------------------------------------------------------ Scythe (Halloween event, melee)
    # Original ScytheExplosion1-4: a swing from overhead to the front (3 x 10, then 40).
    _item(cfg, 'Scythe', 54, ['Special'], 11, 5, 'Aiming',
          ['ScytheExplosion1', 'ScytheExplosion2', 'ScytheExplosion3', 'ScytheExplosion4'], 'punch', sim=0)  # INV level
    _put(cfg, 'ItemPrice', {'ID': 'Scythe', 'InGame': 390, 'UnlockPricePremium': 8})                    # INV

    # ------------------------------------------------------------------ supplies (boosters)
    # Spring Mine: the in-match hazard of the original (Explosion SpringMine, Sound SpringMine) as a booster
    # mine that throws whoever steps on it into the air.
    _put(cfg, 'Item', {'ID': 'SpringMine', 'Name': 'SPRINGMINE', 'Description': 'SPRINGMINEDESC', 'SortPriority': 26,
                       'Type': 'Booster', 'Category': ['Special', 'Practice'], 'RequiredLevel': 7,
                       'Icon': '#BoosterIcon.SpringMine', 'PriceInfo': '#ItemPrice.SpringMine', 'AmountPurchased': 1,
                       'Graphics': '#WeaponGraphic.Punch', 'DurationType': 'Turn', 'DurationAmount': 1,
                       'RemakeAdded': True})                                                              # INV level
    _put(cfg, 'ItemPrice', {'ID': 'SpringMine', 'InGame': 690})                                         # INV
    _put(cfg, 'BoosterIcon', {'ID': 'SpringMine', 'SWF': 'flash/ui/icons_boosters.swf', 'Export': 'springmine'})
    # Innertube: Bonus.Innertube (JumpPower -120, Density -25) is all that is left; as a supply it saves its
    # wearer from drowning once (the penguin is washed ashore instead) until the end of their next turn.
    _put(cfg, 'Item', {'ID': 'Innertube', 'Name': 'INNERTUBE', 'Description': 'INNERTUBEDESC', 'SortPriority': 27,
                       'Type': 'Booster', 'Category': ['Special', 'Practice'], 'RequiredLevel': 3,
                       'Icon': '#BoosterIcon.Innertube', 'PriceInfo': '#ItemPrice.Innertube', 'AmountPurchased': 1,
                       'Graphics': '#WeaponGraphic.Punch', 'StatBonuses': '#Bonus.Innertube', 'DurationType': 'Turn',
                       'DurationAmount': 2, 'RemakeAdded': True})                                         # INV
    _put(cfg, 'ItemPrice', {'ID': 'Innertube', 'InGame': 450})                                          # INV
    _put(cfg, 'BoosterIcon', {'ID': 'Innertube', 'SWF': 'flash/ui/icons_boosters.swf', 'Export': 'innertube'})


STRINGS = {
    # names/descriptions the old localization did not have (INV)
    'SNOWBALL': 'Snowball',
    'SNOWBALLDESC': 'A hard-packed snowball. Freezes whoever it hits: slower and weaker against ice.',
    'BROOM': 'Broom',
    'BROOMDESC': 'Ride the witch\'s broom! Aim to fly off and ram every enemy on the way.',
    'SCYTHE': 'Scythe',
    'SCYTHEDESC': 'Swing a spooky scythe from overhead. Only reaches penguins standing right next to you.',
    'SPRINGMINE': 'Spring Mine',
    'SPRINGMINEDESC': 'A mine with a spring in it. Throws whoever steps on it high into the air.',
    'INNERTUBE': 'Innertube',
    'INNERTUBEDESC': 'Saves you from drowning once: you wash up on dry land instead. Lasts until the end of your next turn.',
}


def patch_strings(strings):
    for k, v in STRINGS.items():
        strings.setdefault(k, v)
