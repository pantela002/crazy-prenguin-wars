using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>Everything one Fire()/UseBooster() call produced; FireHandle.Done once nothing of it is pending.</summary>
    internal sealed class Shot
    {
        public FireHandle Handle = new FireHandle();
        public ItemDef Item;
        public string ItemId;
        public IPenguin Shooter;
        public int Attacker = -1;
        public float AttackPct;          // shooter's Attack stat snapshot (percent)
        public float AttackMult = 1f;    // shooter's attack multiplier snapshot (sushi boosters)
        public int Live;                 // live projectiles + pending scheduled emissions
        public float Started;
        public Projectile Camera;        // newest camera-followed projectile

        public static Shot For(IPenguin shooter, string itemId)
        {
            var s = new Shot { Shooter = shooter, ItemId = itemId, Item = WeaponDefs.Item(itemId), Started = Time.time };
            if (shooter != null)
            {
                s.Attacker = shooter.PlayerIndex;
                if (shooter.Stats != null) { s.AttackPct = shooter.Stats.attack; s.AttackMult = shooter.Stats.attackMultiplier; }
            }
            return s;
        }
    }

    /// <summary>
    /// The emitting object as the original EmitterUtils saw it: where it is, which way it moves, what it last
    /// touched and the "Params" (dir, powerBar) it was given.
    /// </summary>
    internal sealed class Src
    {
        public Vector2 Pos;
        public Vector2 Vel;               // velocity of the emitting body when it emitted (hit direction)
        public bool HasDir;               // Params.dir present (player aim, follower trigger, explosion chain)
        public Vector2 Dir;
        public bool HasContact;           // touched something recently
        public Vector2 Normal;            // contact normal, pointing away from the touched surface
        public float Power01;             // Params.powerBar (0 for everything but the player's shot)
        public bool Primary;              // emitted directly by the item the player used
        public bool Activation;           // item targeting "Activation" (dropped at the feet)
        public HashSet<IDamageable> Exclude;  // targets already hit by this bullet's Ray pass

        public Src Copy() => (Src)MemberwiseClone();
    }

    /// <summary>
    /// Scene-side state of the weapon system: projectiles, followers, deployables, delayed emissions.
    /// Created on first use; lives on a persistent object so Fx and weapons survive battle reloads.
    /// </summary>
    internal sealed class WeaponRuntime : MonoBehaviour
    {
        static WeaponRuntime inst;
        public static WeaponRuntime I
        {
            get
            {
                if (inst) return inst;
                var go = new GameObject("CPW.Weapons");
                DontDestroyOnLoad(go);
                inst = go.AddComponent<WeaponRuntime>();
                return inst;
            }
        }
        public static bool Exists => inst;

        struct Scheduled { public float at; public EmitterDef e; public Src src; public Shot shot; public System.Action action; }

        public readonly List<Projectile> Projectiles = new List<Projectile>();
        public readonly List<FollowerRt> Followers = new List<FollowerRt>();
        public readonly List<Deployable> Deployables = new List<Deployable>();
        public readonly List<Shot> Shots = new List<Shot>();
        readonly List<Scheduled> schedule = new List<Scheduled>();
        readonly List<FollowerRt> followerScratch = new List<FollowerRt>();
        public float LastExplosion = -10f;
        float busySince = -1f;
        bool busyTimedOut;

        /// <summary>Parent for spawned battle objects (destroyed with the battle).</summary>
        public Transform Parent => BattleWorld.Root ? BattleWorld.Root : transform;

        public void Schedule(float delay, EmitterDef e, Src src, Shot shot)
        {
            schedule.Add(new Scheduled { at = Time.time + delay, e = e, src = src, shot = shot });
            if (shot != null) shot.Live++;
        }

        public void ScheduleAction(float delay, Shot shot, System.Action a)
        {
            schedule.Add(new Scheduled { at = Time.time + delay, shot = shot, action = a });
            if (shot != null) shot.Live++;
        }

        public void Track(Shot s) { if (s != null && !Shots.Contains(s)) Shots.Add(s); }

        void Update()
        {
            float now = Time.time;
            // delayed emissions (Emitter.Delay, protein bar second shot)
            for (int i = 0; i < schedule.Count; i++)
            {
                var s = schedule[i];
                if (s.at > now) continue;
                schedule.RemoveAt(i--);
                if (s.shot != null) s.shot.Live--;
                if (s.action != null) s.action();
                else EmissionEngine.EmitOne(s.e, s.src, s.shot, 0, 1);
            }

            // followers (copy: activations may add new followers)
            float dt = Time.deltaTime;
            followerScratch.Clear();
            followerScratch.AddRange(Followers);
            foreach (var f in followerScratch) if (!f.Dead) f.Tick(dt);
            for (int i = Followers.Count - 1; i >= 0; i--) if (Followers[i].Dead) Followers.RemoveAt(i);

            for (int i = Projectiles.Count - 1; i >= 0; i--) if (!Projectiles[i]) Projectiles.RemoveAt(i);
            for (int i = Deployables.Count - 1; i >= 0; i--) if (!Deployables[i]) Deployables.RemoveAt(i);

            Boosters.Tick(dt);

            // shot bookkeeping: camera target and Done
            for (int i = Shots.Count - 1; i >= 0; i--)
            {
                var s = Shots[i];
                if (s.Camera && s.Camera.Alive) s.Handle.CameraTarget = s.Camera.transform;
                else
                {
                    s.Camera = null;
                    Projectile best = null;
                    foreach (var p in Projectiles) if (p && p.Alive && p.Shot == s && p.Def.CameraFollowed) best = p;
                    s.Camera = best;
                    s.Handle.CameraTarget = best ? best.transform : null;
                }
                if (s.Live <= 0 && now - s.Started > 0.05f) { s.Handle.Done = true; s.Handle.CameraTarget = null; Shots.RemoveAt(i); }
            }
        }

        public bool Busy
        {
            get
            {
                bool b = ComputeBusy();
                if (!b) { busySince = -1; busyTimedOut = false; return false; }
                if (busyTimedOut) return false;
                if (busySince < 0) busySince = Time.time;
                if (Time.time - busySince > WeaponTuning.BusyTimeout) { busyTimedOut = true; return false; }
                return true;
            }
        }

        bool ComputeBusy()
        {
            if (schedule.Count > 0) return true;
            foreach (var p in Projectiles) if (p && p.Alive) return true;
            if (Time.time - LastExplosion < WeaponTuning.ExplosionSettleSec) return true;
            foreach (var f in Followers) if (!f.Dead && f.KeepsWorldBusy) return true;
            foreach (var s in Shots) if (s.Live > 0) return true;
            if (Boosters.Busy) return true;
            float v2 = WeaponTuning.SettleSpeed * WeaponTuning.SettleSpeed;
            foreach (var p in BattleWorld.Penguins) if (Moving(p, v2)) return true;
            foreach (var o in BattleWorld.Objects) if (Moving(o, v2)) return true;
            return false;
        }

        static bool Moving(IDamageable d, float v2)
        {
            if (d == null || !d.Alive) return false;
            var rb = d.Body;
            if (!rb || rb.bodyType != RigidbodyType2D.Dynamic || rb.IsSleeping() || !rb.simulated) return false;
            if (BattleTerrain.I != null && rb.position.y < BattleTerrain.I.WaterY - 3f) return false;   // sinking
            return rb.Vel().sqrMagnitude > v2;
        }

        public void ClearAll()
        {
            schedule.Clear();
            foreach (var f in Followers) f.Kill();
            Followers.Clear();
            foreach (var p in Projectiles) if (p) Destroy(p.gameObject);
            Projectiles.Clear();
            var deps = Deployables.ToArray();
            foreach (var d in deps) if (d) d.Remove(false);
            Deployables.Clear();
            foreach (var s in Shots) { s.Handle.Done = true; s.Handle.CameraTarget = null; }
            Shots.Clear();
            Boosters.ClearAll();
            LastExplosion = -10;
            busySince = -1; busyTimedOut = false;
        }
    }

    /// <summary>Helpers to classify colliders and damageables the way the original AffectsObjects did.</summary>
    internal static class WorldQuery
    {
        static readonly List<Collider2D> cols = new List<Collider2D>();

        /// <summary>Category of a damageable relative to the attacking player.</summary>
        public static Affects Category(IDamageable d, int attacker)
        {
            if (d is IPenguin p) return p.PlayerIndex == attacker ? Affects.Self : Affects.Enemy;
            if (d is Deployable) return Affects.Object | Affects.Weapon;
            return Affects.Object;
        }

        public static IDamageable FindDamageable(Collider2D c)
        {
            if (!c) return null;
            var t = c.transform;
            foreach (var p in BattleWorld.Penguins) if (p != null && p.gameObject && t.IsChildOf(p.gameObject.transform)) return p;
            foreach (var o in BattleWorld.Objects) if (o != null && o.gameObject && t.IsChildOf(o.gameObject.transform)) return o;
            return null;
        }

        public static bool IsTerrain(Collider2D c) => c && BattleTerrain.I != null && BattleTerrain.I.IsTerrainCollider(c);

        /// <summary>Approximate radius of a damageable (for the original "distance minus body radius" falloff).</summary>
        public static float Radius(IDamageable d)
        {
            if (d is IPenguin) return Tuning.PenguinRadius;
            if (d is Deployable dep) return dep.Radius;
            var go = d.gameObject;
            if (!go) return 0.5f;
            var c = go.GetComponentInChildren<Collider2D>();
            if (!c) return 0.5f;
            var e = c.bounds.extents;
            return Mathf.Max(0.1f, Mathf.Min(e.x, e.y));
        }

        public static List<Collider2D> Colliders(GameObject go)
        {
            cols.Clear();
            if (go) go.GetComponentsInChildren(cols);
            return cols;
        }

        public static bool InWater(Vector2 p) => BattleTerrain.I != null && BattleTerrain.I.Level != null && p.y < BattleTerrain.I.WaterY;

        public static bool OutOfWorld(Vector2 p)
        {
            var t = BattleTerrain.I;
            if (t == null || t.Level == null) return Mathf.Abs(p.x) > 500 || p.y < -200;
            float m = WeaponTuning.OutOfWorldMargin;
            var s = t.Level.size;
            return p.x < -m || p.x > s.x + m || p.y < -m;
        }
    }

    /// <summary>
    /// Port of MissileEmitter / ExplosionEmitter / AnimationEmitter + EmitterUtils: turns an Emitter row into
    /// missiles and explosions (count, delay, angles, spread, offsets, hit direction).
    /// </summary>
    internal static class EmissionEngine
    {
        /// <summary>Process a list of emitters from one source (all of them, honouring Number/Delay).</summary>
        public static void Run(List<EmitterDef> list, Src src, Shot shot)
        {
            if (list == null) return;
            for (int k = 0; k < list.Count; k++) Run(list[k], src, shot);
        }

        public static void Run(EmitterDef e, Src src, Shot shot)
        {
            if (e == null) return;
            if (e.DelaySec <= 0) { for (int i = 0; i < e.Number; i++) EmitOne(e, src, shot, i, e.Number); }
            else
            {
                // the original emits delayed copies one at a time, each with currentCount 0 of maxCount 1
                EmitOne(e, src, shot, 0, 1);
                for (int i = 1; i < e.Number; i++) WeaponRuntime.I.Schedule(e.DelaySec * i, e, src, shot);
            }
        }

        /// <summary>EmitterUtils.getFiringDirection.</summary>
        static Vector2 BaseDirection(Src src, EmitterDef e)
        {
            float sign = e.OffsetBy < 0 ? -1f : 1f;
            if (src.HasDir && src.Dir.sqrMagnitude > 1e-6f) return src.Dir.normalized * sign;
            if (e.UseHitDirection && src.Vel.sqrMagnitude > 1e-4f) return src.Vel.normalized * sign;
            if (src.HasContact) return -src.Normal * sign;               // original: contact point - body
            if (src.Vel.sqrMagnitude > 1e-4f) return src.Vel.normalized * sign;   // explosionDirection
            return Vector2.up;
        }

        /// <summary>
        /// EmitterUtils.getModifiedFiringDirection. The decompiled maths offsets the whole fan to one side;
        /// the data (e.g. Shotgun -7/14, ClusterRocketCluster -40/80, PlasmaBombMid -45/90) clearly means a
        /// fan from AngleOne to AngleOne+AngleTwo around the base direction, mirrored when facing left, so
        /// that is what is implemented. Spread > 0 gives an even fan with Spread degrees between shots.
        /// </summary>
        static Vector2 ModifiedDirection(Vector2 baseDir, EmitterDef e, int count, int index)
        {
            if (e.AngleTwo <= 0 && e.AngleOne <= 0) return baseDir;   // original: angleTwo > 0 || angleOne > 0
            float baseDeg = Mathf.Atan2(baseDir.y, baseDir.x) * Mathf.Rad2Deg;
            float mirror = baseDir.x < -1e-4f ? -1f : 1f;
            float off;
            if (e.Spread > 0 && count > 1)
            {
                float centre = e.AngleOne + e.AngleTwo * 0.5f;
                off = centre + (index - (count - 1) * 0.5f) * e.Spread;
            }
            else off = e.AngleOne + Random.Range(0f, e.AngleTwo);
            float a = (baseDeg + off * mirror) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        /// <summary>EmitterUtils.offsetLocation distance in world units.</summary>
        static float OffsetDistance(EmitterDef e, int index)
        {
            float d = Mathf.Abs(e.OffsetBy);
            if (e.RandomOffset && d > 0) d = Random.Range(0, (int)d + 1);
            if (e.Number > 0)
            {
                if (e.AngleOne == 0 && e.AngleTwo == 0) d *= index + 1;
                else if (e.Spread > 0)
                {
                    int k = (int)(e.AngleTwo / e.Spread);
                    if (k > 0 && k < e.Number)
                    {
                        int m = e.Number / k;
                        int j = (int)(m * ((float)index / e.Number));
                        d *= j + 1;
                    }
                }
            }
            return Units.W(d);
        }

        /// <summary>One activation of an emitter (one missile / one explosion).</summary>
        public static void EmitOne(EmitterDef e, Src src, Shot shot, int index, int count)
        {
            if (e == null || src == null) return;
            var baseDir = BaseDirection(src, e);
            var dir = ModifiedDirection(baseDir, e, count, index);
            // children of something lying on a surface must not be fired into it (the original fans were
            // tuned per weapon to point away; reflecting keeps every fan above ground)
            if (e.IsMissile && src.HasContact && !src.HasDir && Vector2.Dot(dir, src.Normal) < -0.05f)
                dir = Vector2.Reflect(dir, src.Normal);
            var pos = src.Pos + dir * OffsetDistance(e, index);

            if (!string.IsNullOrEmpty(e.Sound)) AudioManager.Sfx(e.Sound, e.IsMissile && e.Number > 6 ? 0.5f : 1f);

            if (e.IsMissile)
            {
                if (e.Id.StartsWith("ArtilleryStrikeShard")) { ArtilleryShell(e, src, shot, dir); return; }
                if (src.HasContact) pos += src.Normal * (e.Missile.RadiusU + 0.05f);
                Projectile.Spawn(e, e.Missile, pos, dir, src, shot, -1f);
            }
            else if (e.IsExplosion) Explosions.Explode(e, pos, dir, src, shot);
            else if (e.Kind == "AnimationEmitter") Fx.Explosion(pos, 8f, "Void" + (e.AnimationId ?? ""));
        }

        /// <summary>Artillery strike: the shells come down from the sky above the marker.</summary>
        static void ArtilleryShell(EmitterDef e, Src src, Shot shot, Vector2 dir)
        {
            float top = BattleTerrain.I != null && BattleTerrain.I.Level != null ? BattleTerrain.I.Level.size.y + 6f : src.Pos.y + WeaponTuning.ArtilleryHeight;
            var start = new Vector2(src.Pos.x + Random.Range(-1.5f, 1.5f), Mathf.Min(src.Pos.y + WeaponTuning.ArtilleryHeight, top));
            float ang = (-90f + Random.Range(e.AngleOne, e.AngleOne + e.AngleTwo)) * Mathf.Deg2Rad;
            Projectile.Spawn(e, e.Missile, start, new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)), src, shot, WeaponTuning.ArtilleryShellSpeed);
        }

        /// <summary>Launch speed (units/s) for a missile, see WeaponTuning for the model.</summary>
        public static float LaunchSpeed(MissileDef m, float power01, bool primary, bool activation, ItemDef item)
        {
            float p = Mathf.Pow(Mathf.Clamp01(power01), WeaponTuning.PowerCurve);
            float impulse = m.ImpulseMin + Mathf.Max(0, m.ImpulseMax - m.ImpulseMin) * p;
            float speedPx = impulse / m.NapeMass;
            float scale = primary ? (activation ? WeaponTuning.ActivationSpeedScale : WeaponTuning.PrimarySpeedScale) : WeaponTuning.SubSpeedScale;
            float v = Units.W(speedPx) * scale;
            if (primary && !activation && m.RayHits != 0 && m.TimerSec > 0 && item != null && item.SimulationDistance > 0 && WeaponTuning.RayWeaponsUseSimulationDistance)
                v = Mathf.Max(v, item.SimulationDistance / m.TimerSec);
            return Mathf.Min(v, WeaponTuning.MaxLaunchSpeed);
        }
    }

    /// <summary>Port of ExplosionEmitter.explode + DamageUtil: damage, impulse, terrain, followers, effects.</summary>
    internal static class Explosions
    {
        static readonly List<IDamageable> targets = new List<IDamageable>();

        public static void Explode(EmitterDef e, Vector2 pos, Vector2 dir, Src src, Shot shot)
        {
            var x = e.Explosion;
            var aff = e.Affects;
            int attacker = shot != null ? shot.Attacker : -1;
            float dR = Units.W(x.DamageRadiusPx);
            float iR = Units.W(x.ImpulseRadiusPx);

            // ---- effects
            float visPx = Mathf.Max(x.ShapeMaxPx, x.DamageRadiusPx * WeaponTuning.VisualDamageRadiusFactor);
            if (!string.IsNullOrEmpty(x.Particle) && visPx >= 3f) Fx.Explosion(pos, Units.W(visPx), x.Particle);
            else if (x.ShapeMaxPx > 2) Fx.Explosion(pos, Units.W(visPx), null);
            if (x.ShakeSec > 0 && x.ShakeStrength > 0) Fx.ShakeCamera(x.ShakeSec, x.ShakeStrength * WeaponTuning.ShakeScale);
            if (x.Flash) Fx.Glow(pos, 60f, new Color(1f, 1f, 0.95f));
            if (dR > 0.5f || x.ShapeMaxPx > 2) { BattleEvents.RaiseExplosion(pos, Mathf.Max(dR, Units.W(x.ShapeMaxPx))); WeaponRuntime.I.LastExplosion = Time.time; }
            if (WorldQuery.InWater(pos) && dR > 1f) Fx.Splash(pos, Mathf.Min(3f, dR * 0.4f));

            // ---- damage + followers + impulse on penguins / objects
            targets.Clear();
            foreach (var p in BattleWorld.Penguins) if (p != null && p.Alive) targets.Add(p);
            foreach (var o in BattleWorld.Objects) if (o != null && o.Alive) targets.Add(o);
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || !t.Alive) continue;
                var cat = WorldQuery.Category(t, attacker);
                if ((aff & cat) == 0) continue;
                if (src != null && src.Exclude != null && src.Exclude.Contains(t)) continue;
                var tpos = t.Position;
                float dist = Mathf.Max(0, Vector2.Distance(tpos, pos) - WorldQuery.Radius(t));
                bool inDamage = dR > 0 && dist <= dR;
                bool inImpulse = iR > 0 && x.Impulse != 0 && dist <= iR;
                if (!inDamage && !inImpulse) continue;

                if (inDamage)
                    foreach (var f in e.Followers) FollowerRt.Attach(f, t, shot);

                // impulse (ExplosionEmitter.handleImpulse)
                Vector2 dv = Vector2.zero;
                if (inImpulse)
                {
                    var away = tpos - pos;
                    away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector2.up;
                    float minPct = t is IPenguin ? Tuner("ImpulseMinScalingPlayer", 1) : Tuner("ImpulseMinScalingOther", 50);
                    float mag = Falloff(iR, x.Impulse * WeaponTuning.ImpulseToSpeed, dist, minPct);
                    mag = Mathf.Clamp(mag, -WeaponTuning.MaxKnockbackSpeed, WeaponTuning.MaxKnockbackSpeed);
                    dv = away * mag;
                    if (t is IPenguin) dv.y += Mathf.Abs(mag) * 0.15f;   // a little lift so shoves read as hops
                }

                float dmg = inDamage && x.Attack != 0 ? DamageReceived(x, shot, t, dist, dR) : 0f;
                if (src != null && src.Exclude != null && dmg != 0) src.Exclude.Add(t);
                if (dmg > 0 && Boosters.Absorb(t)) { dmg = 0; dv *= 0.5f; }   // shield booster blocks one hit
                if (dmg > 0)
                {
                    float mass = t.Body ? t.Body.mass : 1f;
                    t.TakeDamage(new DamageInfo
                    {
                        amount = dmg,
                        type = x.DamageType,
                        attacker = attacker,
                        itemId = shot != null ? shot.ItemId : e.Id,
                        point = pos,
                        impulse = dv * mass
                    });
                }
                else
                {
                    if (dmg < 0)
                    {
                        if (t is IPenguin hp) hp.Heal(-dmg);
                        Fx.FloatText(tpos + Vector2.up * 1.2f, "+" + Mathf.RoundToInt(-dmg), new Color(0.4f, 1f, 0.4f), 0.8f);
                    }
                    if (dv != Vector2.zero) Push(t, dv);
                }
            }

            // impulse on projectiles ("weapon")
            if ((aff & Affects.Weapon) != 0 && iR > 0 && x.Impulse != 0)
            {
                foreach (var p in WeaponRuntime.I.Projectiles)
                {
                    if (!p || !p.Alive || p.Body == null) continue;
                    float dist = Vector2.Distance(p.Pos, pos);
                    if (dist > iR) continue;
                    var away = p.Pos - pos; away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector2.up;
                    float mag = Falloff(iR, x.Impulse * WeaponTuning.ImpulseToSpeed * WeaponTuning.ProjectileImpulseScale, dist, 50);
                    p.Body.SetVel(p.Body.Vel() + away * mag);
                }
            }

            // ---- terrain (ExplosionShape: jagged circle between Min and Max radius)
            if ((aff & Affects.Terrain) != 0 && x.ShapeMaxPx > 2 && BattleTerrain.I != null)
            {
                float r = Units.W(Random.Range(Mathf.Min(x.ShapeMinPx, x.ShapeMaxPx), x.ShapeMaxPx));
                BattleTerrain.I.Carve(pos, r);
            }

            // ---- SimpleScript "Teleport": the attacker moves to the explosion
            if (x.Teleport && shot != null && shot.Shooter != null && shot.Shooter.Alive) shot.Shooter.Teleport(pos);

            // ---- chained emitters of the explosion itself (ImpactCannon 1 → 2 → 3)
            if (x.Emitters.Count > 0)
            {
                var child = new Src { Pos = pos, HasDir = true, Dir = dir, Power01 = src != null ? src.Power01 : 0, Exclude = null };
                EmissionEngine.Run(x.Emitters, child, shot);
            }
        }

        /// <summary>Velocity change on a body without damage (shoves, pulls, recoil).</summary>
        public static void Push(IDamageable t, Vector2 dv)
        {
            var rb = t.Body;
            if (!rb || rb.bodyType != RigidbodyType2D.Dynamic) return;
            if (t is IPenguin p && p.Stats != null) dv *= Mathf.Clamp01(1f - p.Stats.impulseResistance / 100f);
            rb.WakeUp();
            rb.AddForce(dv * rb.mass, ForceMode2D.Impulse);
        }

        /// <summary>DamageUtil.damageRecieved without the target defence (the Penguin applies its own defence).</summary>
        public static float DamageReceived(ExplosionDef x, Shot shot, IDamageable target, float dist, float radius)
        {
            float baseA = x.Attack * (shot != null ? shot.AttackMult : 1f);
            float pa = shot != null ? shot.AttackPct : 0f;
            pa = Mathf.Max(pa, Tuner("AttackStatMin", -51));
            float a = baseA + baseA / 135f * (125f * (pa / (pa + 100f)));
            float minPct = target is IPenguin ? Tuner("DamageMinScalingPlayer", 1) : Tuner("DamageMinScalingOther", 50);
            float v = Falloff(radius, a >= 0 ? Mathf.Ceil(a) : Mathf.Floor(a), dist, minPct);
            float cap = Tuner("DamageSingleHitMax", 300);
            return Mathf.Clamp(Mathf.Round(v), -cap, cap);
        }

        /// <summary>DamageUtil.scaleValueAccordingToDistance (dist already has the body radius removed).</summary>
        public static float Falloff(float radius, float value, float dist, float minPct)
        {
            if (radius <= 0) return value;
            if (dist >= radius * (100f - minPct) / 100f) return value * minPct / 100f;
            return value * ((radius - dist) / radius);
        }

        static Record tuner;
        public static float Tuner(string field, float def)
        {
            if (tuner == null) tuner = GameData.Tuner;
            return tuner != null ? tuner.Float(field, def) : def;
        }
    }
}
