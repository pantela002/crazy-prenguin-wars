using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>Tracks everything one shot spawned. Done = all missiles/explosions/effects of the shot are finished.</summary>
    public class FireHandle
    {
        public bool Done;
        public Transform CameraTarget;   // what the camera should follow right now (missile), null = nothing
    }

    public enum TargetingMode { PowerBar, Aiming, Activation, Point }

    /// <summary>
    /// Data-driven weapons: interprets the original Item → Emitter → Missile/Explosion/Follower config
    /// (see WeaponDefs, EmissionEngine, Projectile, Explosions, FollowerRt) plus the booster items (Boosters).
    /// Everything runs on the "CPW.Weapons" runtime object; spawned things are parented to BattleWorld.Root.
    /// </summary>
    public static class WeaponSystem
    {
        /// <summary>How the HUD should let the player aim this item (Item.Targeting; boosters = Activation).</summary>
        public static TargetingMode Targeting(string itemId)
        {
            var item = WeaponDefs.Item(itemId);
            if (item == null) return TargetingMode.PowerBar;
            if (item.IsBooster) return TargetingMode.Activation;
            switch (item.Targeting)
            {
                case "Aiming": return TargetingMode.Aiming;
                case "Activation": return TargetingMode.Activation;
                case "Point": return TargetingMode.Point;
                default: return TargetingMode.PowerBar;
            }
        }

        /// <summary>
        /// Fire item from shooter. origin = the muzzle (WeaponMount position); angleDeg is the world angle
        /// (0 = right, 90 = up); power01 in [0,1] (ignored by Aiming/Activation items' speed curve only where the
        /// data says so). targetPoint is used by Point targeting only (no item in the data uses it today).
        /// </summary>
        public static FireHandle Fire(IPenguin shooter, string itemId, Vector2 origin, float angleDeg, float power01, Vector2 targetPoint)
        {
            var item = WeaponDefs.Item(itemId);
            if (item == null || !item.IsWeapon || item.Emitters.Count == 0) return new FireHandle { Done = true };
            var rt = WeaponRuntime.I;
            var shot = Shot.For(shooter, itemId);
            rt.Track(shot);

            var mode = Targeting(itemId);
            FireOnce(shot, item, mode, origin, angleDeg, power01, targetPoint);

            // protein bar booster: the same shot fires a second time from wherever the shooter is then
            if (shooter != null && Boosters.ConsumeProtein(shooter))
            {
                rt.ScheduleAction(WeaponTuning.ProteinRepeatDelay, shot, () =>
                {
                    if (!shooter.Alive) return;
                    var o = shooter.WeaponMount ? (Vector2)shooter.WeaponMount.position : origin;
                    FireOnce(shot, item, mode, o, angleDeg, power01, targetPoint);
                });
            }
            return shot.Handle;
        }

        static void FireOnce(Shot shot, ItemDef item, TargetingMode mode, Vector2 origin, float angleDeg, float power01, Vector2 targetPoint)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            var src = new Src
            {
                Pos = mode == TargetingMode.Point ? targetPoint : origin,
                Vel = dir,
                HasDir = true,
                Dir = dir,
                Power01 = Mathf.Clamp01(power01),
                Primary = true,
                Activation = mode == TargetingMode.Activation,
            };
            bool anyMissile = false;
            foreach (var e in item.Emitters) anyMissile |= e.IsMissile;
            if (anyMissile && mode != TargetingMode.Activation) Fx.Glow(origin, 1.3f, new Color(1f, 0.75f, 0.35f));
            EmissionEngine.Run(item.Emitters, src, shot);
        }

        /// <summary>Use a booster item (Type "Booster") on the penguin. Returns false if it can't be used now.
        /// The caller spends the ammo and raises BattleEvents.BoosterUsed when this returns true.</summary>
        public static bool UseBooster(IPenguin user, string itemId)
        {
            var item = WeaponDefs.Item(itemId);
            if (item != null && !item.IsBooster) return false;
            return Boosters.Use(user, itemId);
        }

        /// <summary>Initial speed (units/s) of the item's first missile at this power, 0 if it has none.</summary>
        public static float LaunchSpeed(string itemId, float power01)
        {
            var item = WeaponDefs.Item(itemId);
            var e = FirstMissile(item);
            if (e == null) return 0f;
            return EmissionEngine.LaunchSpeed(e.Missile, power01, true, Targeting(itemId) == TargetingMode.Activation, item);
        }

        // ------------------------------------------------------------------ prediction

        static readonly RaycastHit2D[] hits = new RaycastHit2D[16];

        static EmitterDef FirstMissile(ItemDef item)
        {
            if (item == null) return null;
            foreach (var e in item.Emitters) if (e.IsMissile) return e;
            return null;
        }

        /// <summary>Fill points with the predicted flight path (for the aiming guide). Returns false if the item has no arc
        /// (activation items dropped at the feet, boosters).</summary>
        public static bool PredictTrajectory(string itemId, Vector2 origin, float angleDeg, float power01, List<Vector2> points)
        {
            points?.Clear();
            var item = WeaponDefs.Item(itemId);
            if (item == null || !item.IsWeapon || Targeting(itemId) == TargetingMode.Activation) return false;
            return Simulate(item, origin, angleDeg, power01, points, out _, out _);
        }

        /// <summary>
        /// Where the item's first missile would hit (terrain, an affected penguin/object, water or the end of its
        /// timer) and after how many seconds. Melee/explosion-only items report the reach of their blast chain.
        /// Activation items report the ground under the origin. False = no prediction (unknown item, booster).
        /// </summary>
        public static bool PredictImpact(string itemId, Vector2 origin, float angleDeg, float power01, out Vector2 impact, out float flightTime)
        {
            impact = origin;
            flightTime = 0f;
            var item = WeaponDefs.Item(itemId);
            if (item == null || !item.IsWeapon) return false;
            if (Targeting(itemId) == TargetingMode.Activation)
            {
                if (BattleTerrain.I != null && BattleTerrain.I.GroundBelow(origin.x, origin.y, out var g)) impact = g;
                return true;
            }
            return Simulate(item, origin, angleDeg, power01, null, out impact, out flightTime);
        }

        static bool Simulate(ItemDef item, Vector2 origin, float angleDeg, float power01, List<Vector2> points, out Vector2 impact, out float time)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            impact = origin;
            time = 0f;
            var e = FirstMissile(item);
            if (e == null)
            {
                // explosions in front of the penguin (Punch, ImpactCannon chain, WandWind)
                impact = origin + dir * ExplosionReach(item);
                if (points != null) { points.Add(origin); points.Add(impact); }
                return true;
            }
            var m = e.Missile;
            float speed = EmissionEngine.LaunchSpeed(m, power01, true, false, item);
            var g = Physics2D.gravity * m.GravityScale;
            float dt = Time.fixedDeltaTime > 0 ? Time.fixedDeltaTime : 0.02f;
            float maxT = WeaponTuning.PredictMaxTime;
            if ((m.Type == "TimerMissile" || m.Type == "Grenade") && m.TimerSec > 0) maxT = Mathf.Min(maxT, m.TimerSec);
            float maxDist = m.RayHits != 0 && item.SimulationDistance > 0 ? item.SimulationDistance : float.MaxValue;
            float radius = Mathf.Max(0.05f, m.RadiusU);
            var affects = e.Affects;
            int stride = Mathf.Max(1, Mathf.CeilToInt(maxT / dt / WeaponTuning.PredictMaxPoints));

            var p = origin + dir * Units.W(Mathf.Abs(e.OffsetBy));
            var v = dir * speed;
            points?.Add(p);
            float travelled = 0f;
            int steps = 0;
            while (time < maxT)
            {
                v += g * dt;
                var next = p + v * dt;
                time += dt;
                steps++;
                if (HitBetween(p, next, radius, affects, origin, time, out var hit)) { impact = hit; points?.Add(hit); return true; }
                if (WorldQuery.InWater(next)) { impact = next; points?.Add(next); return true; }
                if (WorldQuery.OutOfWorld(next)) { impact = next; points?.Add(next); return true; }
                travelled += (next - p).magnitude;
                p = next;
                if (travelled >= maxDist) break;
                if (points != null && steps % stride == 0) points.Add(p);
            }
            impact = p;
            if (points != null && (points.Count == 0 || points[points.Count - 1] != p)) points.Add(p);
            return true;
        }

        /// <summary>Circle cast along one step against what the missile collides with.</summary>
        static bool HitBetween(Vector2 a, Vector2 b, float radius, Affects affects, Vector2 origin, float t, out Vector2 point)
        {
            point = b;
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f) return false;
            int n = Physics2D.CircleCast(a, radius, d / len, Phys.AllFilter, hits, len);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                var c = h.collider;
                if (!c || c.isTrigger) continue;
                bool hitIt;
                if (WorldQuery.IsTerrain(c)) hitIt = (affects & Affects.Terrain) != 0;
                else
                {
                    var dmg = WorldQuery.FindDamageable(c);
                    if (dmg is Deployable || c.GetComponentInParent<Projectile>() != null) continue;   // a missile ignores both
                    if (dmg == null)
                    {
                        // any other solid body (a power-up crate, a prop that is not a damageable): a solid missile
                        // bumps into it like into the ground, so the prediction must stop there too
                        hitIt = (affects & Affects.Terrain) != 0;
                        if (hitIt && h.distance < best) { best = h.distance; point = h.centroid; found = true; }
                        continue;
                    }
                    // the shooter stands at the muzzle: ignore penguins right where the shot starts
                    if (dmg is IPenguin && t < WeaponTuning.ShooterGraceSec && (dmg.Position - origin).sqrMagnitude < 4f) continue;
                    var cat = dmg is IPenguin ? Affects.Penguin : Affects.Object;
                    hitIt = (affects & cat) != 0;
                }
                if (hitIt && h.distance < best) { best = h.distance; point = h.centroid; found = true; }
            }
            if (!found && (affects & Affects.Terrain) != 0 && BattleTerrain.I != null && BattleTerrain.I.IsSolid(b)) { point = b; found = true; }
            return found;
        }

        /// <summary>Reach (units) of a melee item (only explosions in front of the user, no missile: Punch, Scythe,
        /// Wand of Wind); 0 for everything that fires a missile.</summary>
        public static float MeleeReach(string itemId)
        {
            var item = WeaponDefs.Item(itemId);
            if (item == null || !item.IsWeapon || FirstMissile(item) != null) return 0f;
            bool any = false;
            foreach (var e in item.Emitters) any |= e.IsExplosion;
            return any ? ExplosionReach(item) : 0f;
        }

        /// <summary>True when the item's first missile drops from the sky onto the target (Orbital Plasma Attack).</summary>
        public static bool FromSky(string itemId)
        {
            var e = FirstMissile(WeaponDefs.Item(itemId));
            return e != null && e.Missile.Script == "Orbital";
        }

        /// <summary>Distance in front of the origin where an explosion-only item's last blast lands.</summary>
        static float ExplosionReach(ItemDef item)
        {
            float reach = 0f, lastRadius = 1f;
            var list = item.Emitters;
            for (int depth = 0; depth < 8 && list != null && list.Count > 0; depth++)
            {
                EmitterDef next = null;
                float step = 0f;
                foreach (var e in list)
                {
                    if (!e.IsExplosion) continue;
                    step = Mathf.Max(step, Units.W(Mathf.Abs(e.OffsetBy)));
                    lastRadius = Units.W(e.Explosion.DamageRadiusPx);
                    if (e.Explosion.Emitters.Count > 0) next = e;
                }
                reach += step;
                list = next != null ? next.Explosion.Emitters : null;
            }
            return reach + lastRadius * 0.5f;
        }

        // ------------------------------------------------------------------ turn flow

        /// <summary>True while any projectile, explosion, physics object or short status is still moving
        /// (capped by WeaponTuning.BusyTimeout so a jittering body can never stall a turn).</summary>
        public static bool WorldBusy => WeaponRuntime.Exists && WeaponRuntime.I.Busy;

        /// <summary>Called by the battle at the start of each turn of player: ends one-turn boosters, ages
        /// mushrooms. Status damage itself ticks in real time.</summary>
        public static void OnTurnStart(int playerIndex)
        {
            if (!WeaponRuntime.Exists) return;
            Boosters.OnTurnStart(playerIndex);
            var deps = WeaponRuntime.I.Deployables.ToArray();
            foreach (var d in deps) if (d) d.OnTurn(playerIndex);
        }

        /// <summary>Remove every projectile/effect/deployable (battle end).</summary>
        public static void ClearAll()
        {
            if (WeaponRuntime.Exists) WeaponRuntime.I.ClearAll();
            Fx.ClearAll();
        }
    }
}
