using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Port of tuxwars.battle.gameobjects.Follower: an invisible sensor that rides on a missile, a penguin or
    /// a level object and fires emissions when things are inside its radius (Trigger Update/Enter/Exit),
    /// honouring ActivateIn, ActivationCooldown (per target), Activations and Duration.
    /// Status followers (Status_Fire, Status_Poison, Status_Cat...) live on penguins, change their stats
    /// while active and tick their damage in real time like the original.
    /// Types: Agressive (emit at the target), Defencive/Status (emit at the followed object), Self.
    /// </summary>
    internal sealed class FollowerRt
    {
        public FollowerDef Def;
        public Shot Shot;
        public bool Dead;
        public IDamageable Host;          // followed penguin / object (status)
        public Projectile Proj;           // followed missile

        float activateIn, life, fxTimer;
        int activations;
        bool unlimitedLife, unlimitedAct;
        int attacker = -1;
        readonly List<object> cdKeys = new List<object>();
        readonly List<float> cdTime = new List<float>();
        HashSet<object> inside, insideNow;
        StatChange stats;

        static readonly object TerrainTarget = new object();
        static readonly List<object> targets = new List<object>();

        /// <summary>The world waits for short statuses (burning, cat attack) to finish before ending the turn.</summary>
        public bool KeepsWorldBusy => Host != null && Def.IsStatus && !unlimitedLife && life <= WeaponTuning.StatusBusyMaxSec && (unlimitedAct || activations > 0);

        public Vector2 Pos => Proj ? Proj.Pos : Host != null ? Host.Position : Vector2.zero;

        // ------------------------------------------------------------------ creation (Followers.createFollower)

        public static FollowerRt AttachToProjectile(FollowerDef def, Projectile p, Shot shot)
        {
            if (def == null || !p) return null;
            if ((def.ApplyTo & Affects.Weapon) == 0) return null;
            var f = Create(def, shot);
            f.Proj = p;
            return f;
        }

        public static FollowerRt Attach(FollowerDef def, IDamageable target, Shot shot)
        {
            if (def == null || target == null || !target.Alive) return null;
            int attacker = shot != null ? shot.Attacker : -1;
            if ((def.ApplyTo & WorldQuery.Category(target, attacker)) == 0) return null;
            var rt = WeaponRuntime.I;
            if (def.IsStatus)
            {
                foreach (var other in rt.Followers)
                    if (!other.Dead && other.Def == def && ReferenceEquals(other.Host, target)) { other.ResetLifetime(shot); return null; }
            }
            var f = Create(def, shot);
            f.Host = target;
            if (target is IPenguin pen)
            {
                if (def.StatBonus != null) f.stats = StatChange.Apply(pen, def.StatBonus);
                if (def.IsStatus)
                {
                    pen.Stats?.flags.Add(def.StatusId);
                    float turnTime = GameData.Battle != null ? Mathf.Max(5f, GameData.Battle.Float("TurnTime", 10)) : 10f;
                    int turns = def.DurationSec <= 0 ? 99 : Mathf.Max(1, Mathf.CeilToInt(def.DurationSec / turnTime));
                    pen.AddEffect(def.StatusId, turns);
                }
            }
            foreach (var sub in def.SubFollowers) Attach(sub, target, shot);
            return f;
        }

        static FollowerRt Create(FollowerDef def, Shot shot)
        {
            var f = new FollowerRt
            {
                Def = def,
                Shot = shot,
                attacker = shot != null ? shot.Attacker : -1,
                activateIn = def.ActivateInSec,
                life = def.DurationSec,
                unlimitedLife = def.DurationSec <= 0,
                activations = def.Activations,
                unlimitedAct = def.Activations <= 0,
            };
            if (def.TriggerEnter || def.TriggerExit) { f.inside = new HashSet<object>(); f.insideNow = new HashSet<object>(); }
            WeaponRuntime.I.Followers.Add(f);
            return f;
        }

        void ResetLifetime(Shot shot)
        {
            life = Def.DurationSec;
            activations = Def.Activations;
            if (shot != null) { Shot = shot; attacker = shot.Attacker; }
        }

        // ------------------------------------------------------------------ update (Follower.physicsUpdate)

        public void Tick(float dt)
        {
            if (Dead) return;
            if (Proj != null ? !Proj || !Proj.Alive : (Host == null || !Host.Alive || !Host.gameObject))
            {
                if (Def.Type != "StatusPermanent" || Host == null || !Host.gameObject) { Kill(); return; }
                return;   // permanent status waits for the respawn
            }
            activateIn -= dt;
            for (int i = 0; i < cdTime.Count; i++) cdTime[i] -= dt;

            if (activateIn <= 0)
            {
                if (Def.TriggerUpdate)
                {
                    Gather(targets);
                    SelectSingle(targets);
                    for (int i = 0; i < targets.Count && !Dead; i++) PossibleActivate(targets[i]);
                }
                if (inside != null)
                {
                    Gather(targets);
                    insideNow.Clear();
                    foreach (var t in targets) insideNow.Add(t);
                    if (Def.TriggerEnter) foreach (var t in insideNow) if (!inside.Contains(t)) PossibleActivate(t);
                    if (Def.TriggerExit) foreach (var t in inside) if (!insideNow.Contains(t)) PossibleActivate(t);
                    var swap = inside; inside = insideNow; insideNow = swap;
                }
            }

            if (Host != null && Def.IsStatus) StatusFx(dt);

            if (!unlimitedLife) { life -= dt; if (life <= 0) { Kill(); return; } }
            if (!unlimitedAct && activations <= 0) Kill();
        }

        /// <summary>Things inside the sensor that the follower affects (Follower.getTargets).</summary>
        void Gather(List<object> list)
        {
            list.Clear();
            if (Def.IsStatus) { if (Host != null) list.Add(Host); return; }
            var pos = Pos;
            float r = Def.RadiusU;
            var aff = Def.Affects;
            if ((aff & Affects.Penguin) != 0)
                foreach (var p in BattleWorld.Penguins)
                {
                    if (p == null || !p.Alive || ReferenceEquals(p, Host)) continue;
                    if ((aff & WorldQuery.Category(p, attacker)) == 0) continue;
                    if ((p.Position - pos).sqrMagnitude <= Sq(r + Tuning.PenguinRadius)) list.Add(p);
                }
            if ((aff & (Affects.Object | Affects.Weapon)) != 0)
                foreach (var o in BattleWorld.Objects)
                {
                    if (o == null || !o.Alive || ReferenceEquals(o, Host)) continue;
                    if ((aff & WorldQuery.Category(o, attacker)) == 0) continue;
                    if ((o.Position - pos).sqrMagnitude <= Sq(r + WorldQuery.Radius(o))) list.Add(o);
                }
            if ((aff & Affects.Weapon) != 0)
                foreach (var p in WeaponRuntime.I.Projectiles)
                {
                    if (!p || !p.Alive || p == Proj) continue;
                    if ((p.Pos - pos).sqrMagnitude <= Sq(r + p.Def.RadiusU)) list.Add(p);
                }
            // the original sensors touch the terrain body; huge trigger sensors (OneSecTrigger 1000 px) always do
            if ((aff & Affects.Terrain) != 0 && (r > 5f || (BattleTerrain.I != null && BattleTerrain.I.IsSolid(pos + Vector2.down * r))))
                list.Add(TerrainTarget);
        }

        static float Sq(float x) => x * x;

        void SelectSingle(List<object> list)
        {
            if (Def.Target == "All" || list.Count <= 1) return;
            var pos = Pos;
            object best = null;
            float bestD = Def.TargetSelection == "Farthest" ? -1 : float.MaxValue;
            if (Def.TargetSelection != "Closest" && Def.TargetSelection != "Farthest") best = list[Random.Range(0, list.Count)];
            else
                foreach (var t in list)
                {
                    float d = (TargetPos(t, pos) - pos).sqrMagnitude;
                    if (Def.TargetSelection == "Closest" ? d < bestD : d > bestD) { bestD = d; best = t; }
                }
            list.Clear();
            if (best != null) list.Add(best);
        }

        static Vector2 TargetPos(object t, Vector2 fallback)
        {
            if (t is IDamageable d) return d.Position;
            if (t is Projectile p && p) return p.Pos;
            return fallback;
        }

        void PossibleActivate(object t)
        {
            if (Dead || activateIn > 0) return;
            if (!unlimitedLife && life <= 0) return;
            if (!unlimitedAct && activations <= 0) return;
            int idx = cdKeys.IndexOf(t);
            if (idx >= 0 && cdTime[idx] > 0) return;
            Activate(t);
            if (idx >= 0) cdTime[idx] = Def.CooldownSec;
            else { cdKeys.Add(t); cdTime.Add(Def.CooldownSec); }
            if (!unlimitedAct) activations--;
        }

        /// <summary>Follower.activate + the TriggerEmission / Homing SimpleScripts.</summary>
        void Activate(object target)
        {
            var followedPos = Pos;
            var tpos = TargetPos(target, followedPos);
            if (Def.Script == "Homing") { Homing(target, tpos); return; }
            if (Def.Script != "TriggerEmission") return;   // e.g. stat-only statuses

            var loc = Def.EmitAt == "Target" && (target is IDamageable || target is Projectile) ? tpos : followedPos;

            // direction: velocity of the followed object, else towards the target (TriggerEmission)
            Vector2 dir = Proj ? Proj.Body.Vel() : (Host != null && Host.Body ? Host.Body.Vel() : Vector2.zero);
            if (dir.sqrMagnitude < 1e-3f && Proj) dir = Proj.LastVelocity;
            if (dir.sqrMagnitude < 1e-3f) dir = tpos - followedPos;
            if (dir.sqrMagnitude < 1e-3f) dir = Vector2.up;
            dir.Normalize();
            if (!Def.RawDirection) dir = Snap4(dir);

            if (Def.Emitters.Count > 0)
            {
                var src = new Src { Pos = loc, Vel = Proj ? Proj.LastVelocity : dir, HasDir = true, Dir = dir, Power01 = 0 };
                EmissionEngine.Run(Def.Emitters, src, Shot);
                return;
            }
            // no own emitters: make the target (EmitAt Target) or the followed object emit its emissions
            if (Def.EmitAt == "Target" && target is Projectile tp && tp) { if (Def.MultipleEmissions) tp.EmitCopy(dir); else tp.Explode(); return; }
            if (Proj) { if (Def.MultipleEmissions) Proj.EmitCopy(dir); else Proj.Explode(); return; }
            if (Host is Deployable dep) dep.Trigger(0f);
        }

        /// <summary>EmitterUtils.convertDirection: snap to one of the 4 axes.</summary>
        static Vector2 Snap4(Vector2 d)
        {
            if (d.x > 0.4f) return Vector2.right;
            if (d.x < -0.4f) return Vector2.left;
            if (d.y > 0.4f) return Vector2.up;
            if (d.y < -0.4f) return Vector2.down;
            return Vector2.up;
        }

        /// <summary>SimpleScript Homing: positive strength pulls the followed missile towards the target, negative
        /// pushes the target (a missile) away from the follower (repulse shield).</summary>
        void Homing(object target, Vector2 tpos)
        {
            float s = Def.HomingStrength;
            if (s > 0 && Proj)
            {
                var to = tpos - Proj.Pos;
                if (to.sqrMagnitude < 1e-4f) return;
                float dv = Units.W(s / Proj.Def.NapeMass) * WeaponTuning.SubSpeedScale * WeaponTuning.HomingScale;
                Proj.Body.SetVel(Proj.Body.Vel() + to.normalized * dv);
            }
            else if (s < 0 && target is Projectile tp && tp)
            {
                var away = tp.Pos - Pos;
                if (away.sqrMagnitude < 1e-4f) away = Vector2.up;
                float dv = Units.W(-s / tp.Def.NapeMass) * WeaponTuning.SubSpeedScale;
                tp.Body.SetVel(tp.Body.Vel() + away.normalized * dv);
            }
        }

        void StatusFx(float dt)
        {
            fxTimer -= dt;
            if (fxTimer > 0) return;
            fxTimer = 0.22f;
            // original StatusEffect* particles (flames, poison clouds, acid spit, stars); visual randomness only
            Fx.Status(Def.StatusId, Host.Position);
        }

        public void Kill()
        {
            if (Dead) return;
            Dead = true;
            stats?.Revert();
            stats = null;
            if (Host is IPenguin pen && Def.IsStatus && pen.Stats != null) pen.Stats.flags.Remove(Def.StatusId);
        }
    }

    /// <summary>Temporary change of a penguin's StatBlock from a Bonus row (statuses and boosters).</summary>
    internal sealed class StatChange
    {
        IPenguin p;
        float attack, defence, impulse, mult = 1f;
        readonly List<string> flags = new List<string>();
        readonly List<StatMod> typedAtk = new List<StatMod>(), typedDef = new List<StatMod>();

        public static StatChange Apply(IPenguin pen, Record bonus)
        {
            var c = new StatChange { p = pen };
            var s = pen?.Stats;
            if (s == null || bonus == null) return c;
            foreach (var kv in bonus.Raw)
            {
                if (kv.Key == "ID" || !(kv.Value is string str)) continue;
                var m = StatMod.Parse(str);
                // typed modifiers ("Add:-25:Ice") only count for hits matching the tag (StatBlock.TagMatches)
                if (!string.IsNullOrEmpty(m.Tag) && m.Tag != "Normal")
                {
                    if (kv.Key == "Attack") { s.typedAttack.Add(m); c.typedAtk.Add(m); }
                    else if (kv.Key == "Defence") { s.typedDefence.Add(m); c.typedDef.Add(m); }
                    continue;
                }
                switch (kv.Key)
                {
                    case "Attack":
                        if (m.Op == "Multiply") { c.mult = m.Value; s.attackMultiplier *= m.Value; }
                        else { c.attack = m.Value; s.attack += m.Value; }
                        break;
                    case "Defence": if (m.Op != "Multiply") { c.defence = m.Value; s.defence += m.Value; } break;
                    case "ImpulseResistance": if (m.Op != "Multiply") { c.impulse = m.Value; s.impulseResistance += m.Value; } break;
                    case "JumpPower": case "WalkSpeed": case "MaxSpeed": case "Density":
                        var flag = kv.Key + (m.Value >= 0 ? "Up" : "Down");
                        if (s.flags.Add(flag)) c.flags.Add(flag);
                        break;
                }
            }
            return c;
        }

        public void Revert()
        {
            var s = p?.Stats;
            if (s == null) return;
            s.attack -= attack;
            s.defence -= defence;
            s.impulseResistance -= impulse;
            if (mult != 0 && mult != 1f) s.attackMultiplier /= mult;
            foreach (var f in flags) s.flags.Remove(f);
            foreach (var m in typedAtk) s.typedAttack.Remove(m);
            foreach (var m in typedDef) s.typedDefence.Remove(m);
            p = null;
        }
    }
}
