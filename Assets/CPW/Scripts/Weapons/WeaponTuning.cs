using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Every number the weapons port adds on top of the original config. The config itself (impulses, radii,
    /// timers, damage) is used as-is; these constants only convert the Flash/Nape units to the Unity battle
    /// and make the game read well on a phone.
    ///
    /// Launch speed model (matches the original MissileEmitter + Nape):
    ///   impulse  = FiringImpulseMin + max(0, FiringImpulseMax - FiringImpulseMin) * powerCurve(power01)
    ///   massNape = Density / 1000 * shapeAreaPx²            (Nape stores density / 1000)
    ///   speedPx  = impulse / massNape                       (px/s)
    ///   speed    = speedPx / Units.PX * scale               (units/s), scale = Primary/Sub/Activation scale
    /// With gravity 500 px/s² the original rockets only flew ~350 px at full power; the remake's levels are
    /// 80-120 units (1600-2400 px) wide, so player-fired shots are scaled up while sub-munitions keep (close to)
    /// the original speed so cluster spreads stay the size of the explosions they belong to.
    /// </summary>
    public static class WeaponTuning
    {
        /// <summary>Speed multiplier for missiles fired by the player with PowerBar/Aiming targeting.
        /// Full-power BasicNuke: 420 px/s → 21 u/s × 2.4 ≈ 50 u/s → ~100 units range at 45° (gravity 25 u/s²).</summary>
        public static float PrimarySpeedScale = 2.4f;
        /// <summary>Speed multiplier for missiles emitted by other missiles/explosions/followers (clusters, shards).</summary>
        public static float SubSpeedScale = 1.3f;
        /// <summary>Speed multiplier for "Activation" items (dynamite, plasma bomb, napalm, artillery marker): a gentle toss.</summary>
        public static float ActivationSpeedScale = 1.0f;
        /// <summary>power01 → impulse curve exponent. 0.5 makes range roughly linear in power (half power ≈ half distance).</summary>
        public static float PowerCurve = 0.5f;
        /// <summary>Hard cap on any launch speed (units/s) so hitscan-like bullets stay stable in Box2D.</summary>
        public static float MaxLaunchSpeed = 220f;
        /// <summary>Player-fired bullets with a "Ray" script fly at least fast enough to cover the item's
        /// SimulationDistance before their timer (the original Ray hit everything between muzzle and bullet).</summary>
        public static bool RayWeaponsUseSimulationDistance = true;

        /// <summary>Explosion "Impulse" (config units) → velocity change in units/s at the explosion centre.
        /// Grenade (200) ≈ 9 u/s shove, ImpactCannon (1000) ≈ 45 u/s launch.</summary>
        public static float ImpulseToSpeed = 0.045f;
        /// <summary>Clamp for the velocity change one explosion can give a body (units/s).</summary>
        public static float MaxKnockbackSpeed = 45f;
        /// <summary>Multiplier for explosion impulses applied to projectiles (WandWind, void generator pulling bombs).</summary>
        public static float ProjectileImpulseScale = 0.6f;

        /// <summary>Seconds a fresh projectile ignores collisions with its shooter (original "Activation": 250 ms).</summary>
        public static float ShooterGraceSec = 0.25f;
        /// <summary>Missiles older than this are removed (safety net).</summary>
        public static float MaxProjectileLifetime = 14f;
        /// <summary>Projectiles further than this outside the level rect are removed.</summary>
        public static float OutOfWorldMargin = 40f;

        /// <summary>WorldBusy: rigidbodies faster than this (units/s) count as still moving.</summary>
        public static float SettleSpeed = 0.5f;
        /// <summary>WorldBusy stays true at least this long after the last explosion.</summary>
        public static float ExplosionSettleSec = 0.6f;
        /// <summary>Status effects with at most this much time left keep the world busy (burning, cat scratching).</summary>
        public static float StatusBusyMaxSec = 6f;
        /// <summary>WorldBusy gives up after being busy this long without a break.</summary>
        public static float BusyTimeout = 16f;

        /// <summary>Protein Bar: delay before the second copy of the shot.</summary>
        public static float ProteinRepeatDelay = 0.6f;
        /// <summary>Artillery strike: how high above the marker the shells appear (units) and their fall speed.</summary>
        public static float ArtilleryHeight = 40f;
        public static float ArtilleryShellSpeed = 30f;

        /// <summary>Orbital Plasma Attack: the bolt starts this far above the level top and falls this fast (units/s). INVENTED.</summary>
        public static float OrbitalStartAbove = 4f;
        public static float OrbitalSpeed = 90f;
        /// <summary>Homing followers (Heat Seeking Missile): the original Homing impulse is multiplied by this because
        /// player rockets fly PrimarySpeedScale times faster than in Flash, so the same pull would hardly bend them. INVENTED.</summary>
        public static float HomingScale = 2.4f;
        /// <summary>Grey Goo ("Crawl" missiles): sideways creep speed (units/s) after each bite. INVENTED.</summary>
        public static float GooCrawlSpeed = 1.4f;
        /// <summary>Spring Mine: the blast is centred this far below the mine so it throws penguins upward. INVENTED.</summary>
        public static float SpringMineDepth = 0.7f;

        /// <summary>Mines/flame mines: seconds until armed, trigger radius (units) and fuse (s).</summary>
        public static float MineArmSec = 2.5f;
        public static float MineTriggerRadius = 2.0f;
        public static float MineFuseSec = 0.6f;
        /// <summary>Mushrooms reproduce every N turns (max count) and poison penguins this close (units).</summary>
        public static int MushroomReproduceTurns = 3;
        public static int MushroomMax = 6;
        public static float MushroomRadius = 3.5f;
        /// <summary>Umbrella booster: maximum fall speed (units/s).</summary>
        public static float UmbrellaFallSpeed = 3.5f;
        /// <summary>Burrito gas cloud: radius (units) and lifetime (s).</summary>
        public static float GasRadius = 5f;
        public static float GasSeconds = 6f;

        /// <summary>Camera shake: config ShakeEffectStrength (px) → world units.</summary>
        public static float ShakeScale = 0.03f;
        /// <summary>Visual explosion radius = max(ExplosionShape.MaxRadius, DamageRadius * this) in px.</summary>
        public static float VisualDamageRadiusFactor = 0.45f;

        /// <summary>Prediction step and limits (the AI and the aiming guide share them).</summary>
        public static float PredictMaxTime = 8f;
        public static int PredictMaxPoints = 240;
    }
}
