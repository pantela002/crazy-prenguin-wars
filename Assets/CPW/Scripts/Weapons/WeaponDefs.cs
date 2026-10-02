using System;
using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>Object categories of the original "AffectsObjects"/"ApplyToObjects" lists.</summary>
    [Flags]
    public enum Affects
    {
        None = 0,
        Self = 1,          // "player": the penguin that fired
        Enemy = 2,         // "enemy": every other penguin
        Object = 4,        // "object"/"levelobject"
        Terrain = 8,       // "terrain"/"stone"/"ice"/"metal"/"wood"
        Weapon = 16,       // "weapon"/"missile"/"mine"/"grenade"
        Powerup = 32,
        Water = 64,
        Penguin = Self | Enemy,
        All = 0xFFFF
    }

    /// <summary>
    /// Parsed, cached view of the original weapon config (Item → Emitter → Missile/Explosion/Follower).
    /// Defs are built lazily and registered in their cache before children are resolved, so reference
    /// cycles in the data cannot recurse forever.
    /// </summary>
    public static class WeaponDefs
    {
        static readonly Dictionary<string, ItemDef> items = new Dictionary<string, ItemDef>();
        static readonly Dictionary<string, EmitterDef> emitters = new Dictionary<string, EmitterDef>();
        static readonly Dictionary<string, MissileDef> missiles = new Dictionary<string, MissileDef>();
        static readonly Dictionary<string, ExplosionDef> explosions = new Dictionary<string, ExplosionDef>();
        static readonly Dictionary<string, FollowerDef> followers = new Dictionary<string, FollowerDef>();

        public static Affects ParseAffects(List<string> list, Affects whenMissing = Affects.All)
        {
            if (list == null || list.Count == 0) return whenMissing;
            var a = Affects.None;
            foreach (var raw in list)
            {
                switch (raw.Trim().ToLowerInvariant())
                {
                    case "all": return Affects.All;
                    case "penguin": a |= Affects.Penguin; break;
                    case "player": a |= Affects.Self; break;
                    case "enemy": a |= Affects.Enemy; break;
                    case "object": case "levelobject": a |= Affects.Object; break;
                    case "powerup": a |= Affects.Powerup; break;
                    case "terrain": case "stone": case "ice": case "metal": case "wood": a |= Affects.Terrain; break;
                    case "weapon": case "missile": case "mine": case "grenade": a |= Affects.Weapon; break;
                    case "water": a |= Affects.Water; break;
                }
            }
            return a;
        }

        public static ItemDef Item(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (items.TryGetValue(id, out var d)) return d;
            var r = GameData.Item(id);
            if (r == null) { items[id] = null; return null; }
            d = new ItemDef { Id = id, Rec = r };
            items[id] = d;
            d.Type = r.Str("Type", "");
            d.Targeting = r.Str("Targeting", "PowerBar");
            d.GraphicId = GameData.RefId(r.Str("Graphics", ""));
            d.SimulationDistance = Units.W(r.Float("SimulationDistance", 0));
            foreach (var e in r.List("Emitters")) { var ed = Emitter(GameData.RefId(e)); if (ed != null) d.Emitters.Add(ed); }
            return d;
        }

        public static EmitterDef Emitter(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (emitters.TryGetValue(id, out var d)) return d;
            var r = GameData.Get("Emitter", id);
            if (r == null) { emitters[id] = null; return null; }
            d = new EmitterDef { Id = id };
            emitters[id] = d;
            d.Kind = r.Str("SpecialType", "ExplosionEmitter");
            d.Affects = ParseAffects(r.List("AffectsObjects"));
            d.Sound = r.Str("SoundID", id);
            d.Number = Mathf.Max(1, r.Int("Number", 1));
            d.DelaySec = Units.Ms(r.Float("Delay", 0));
            d.Spread = r.Float("Spread", 0);
            d.AngleOne = r.Float("AngleOne", 0);
            d.AngleTwo = r.Float("AngleTwo", 0);
            d.OffsetBy = r.Int("DirectionAndOffsetBy", 1);
            d.UseHitDirection = r.Bool("UseHitDirection");
            d.RandomOffset = r.Bool("RandomOffset");
            d.SoundOnce = r.Bool("SoundOnce");   // remake field: a stream (Number + Delay) plays its sound once
            var fx = GameData.Resolve(r.Str("SpecialEffect"));
            if (fx != null)
            {
                switch (fx.Section)
                {
                    case "EmitMissile": d.Missile = Missile(GameData.RefId(fx.Str("Missile"))); break;
                    case "EmitExplosion": d.Explosion = Explosion(GameData.RefId(fx.Str("Explosion"))); break;
                    case "EmitAnimation":
                        var anim = fx.Ref("Animation");
                        d.AnimationId = anim != null ? GameData.RefId(anim.Str("Graphics", anim.Id)) : fx.Id;
                        break;
                }
            }
            foreach (var f in r.List("Followers")) { var fd = Follower(GameData.RefId(f)); if (fd != null) d.Followers.Add(fd); }
            return d;
        }

        public static MissileDef Missile(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (missiles.TryGetValue(id, out var d)) return d;
            var r = GameData.Get("Missile", id);
            if (r == null) { missiles[id] = null; return null; }
            d = new MissileDef { Id = id };
            missiles[id] = d;
            d.Type = r.Str("Type", "Missile");
            d.TimerSec = Units.Ms(r.Float("Timer", 0));
            d.DurationSec = Units.Ms(r.Float("Duration", 0));
            d.IntervalSec = Units.Ms(r.Float("Interval", 0));
            d.RandomIntervalStart = r.Bool("RandomIntervalStart");
            d.ImpulseMin = r.Float("FiringImpulseMin", 0);
            d.ImpulseMax = r.Float("FiringImpulseMax", 0);
            d.Tail = r.Str("ParticleEffect", "");
            d.TailSpawnDistance = Units.W(r.Float("ParticleStreamSpawnDistance", 0));
            d.TailSpawnTime = Units.Ms(r.Float("ParticleStreamSpawnTime", 0));
            d.CameraFollowed = r.Bool("CameraFollowed", true);
            d.GraphicId = GameData.RefId(r.Str("Graphics", ""));
            var script = r.List("SimpleScript");
            if (script.Count >= 2 && script[0] == "Ray")
            {
                d.RayHits = int.TryParse(script[1], out var n) ? n : 1;
                d.RayAffects = ParseAffects(script.GetRange(2, script.Count - 2));
            }
            else if (script.Count > 0) d.Script = script[0];   // remake scripts: "Orbital", "Crawl"
            var p = r.Ref("Physics");
            if (p != null)
            {
                d.RadiusPx = p.Float("Radius", 5);
                d.Density = p.Float("Density", 75);
                d.Friction = p.Float("Friction", 0.3f);
                d.Restitution = p.Float("Restitution", 0);
                d.Bullet = p.Bool("Bullet");
                d.GravityScale = p.Float("GravityScale", 1);
                d.FixedRotation = p.Bool("FixedRotation", true);
            }
            foreach (var e in r.List("Emitters")) { var ed = Emitter(GameData.RefId(e)); if (ed != null) d.Emitters.Add(ed); }
            return d;
        }

        public static ExplosionDef Explosion(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (explosions.TryGetValue(id, out var d)) return d;
            var r = GameData.Get("Explosion", id);
            if (r == null) { explosions[id] = null; return null; }
            d = new ExplosionDef { Id = id };
            explosions[id] = d;
            var atk = StatMod.Parse(r.Str("Attack", ""));
            d.Attack = atk.Value;
            d.DamageType = string.IsNullOrEmpty(atk.Tag) ? "Normal" : atk.Tag;
            d.DamageRadiusPx = r.Float("DamageRadius", 0);
            d.ImpulseRadiusPx = r.Float("ImpulseRadius", 0);
            d.Impulse = r.Float("Impulse", 0);
            d.Particle = r.Str("ParticleEffect", "");
            d.ShakeSec = Units.Ms(r.Float("ShakeEffectTime", 0));
            d.ShakeStrength = r.Float("ShakeEffectStrength", 0);
            d.Flash = r.Bool("Flash");
            var shape = r.Ref("ExplosionShape");
            if (shape != null) { d.ShapeMinPx = shape.Float("MinRadius", 0); d.ShapeMaxPx = shape.Float("MaxRadius", 0); }
            var script = r.List("SimpleScript");
            d.Teleport = script.Count > 0 && script[0] == "Teleport";
            if (script.Count > 0 && !d.Teleport)
            {
                d.Script = script[0];                       // remake: "BuildWall", count, radius px
                d.ScriptArgs = script.GetRange(1, script.Count - 1);
            }
            foreach (var e in r.List("Emitters")) { var ed = Emitter(GameData.RefId(e)); if (ed != null) d.Emitters.Add(ed); }
            return d;
        }

        public static FollowerDef Follower(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (followers.TryGetValue(id, out var d)) return d;
            var r = GameData.Get("Follower", id);
            if (r == null) { followers[id] = null; return null; }
            d = new FollowerDef { Id = id };
            followers[id] = d;
            d.Type = r.Str("Type", "Agressive");
            d.EmitAt = r.Str("EmitAt", "Target");
            d.Target = r.Str("Target", "All");
            d.TargetSelection = r.Str("TargetSelection", "Random");
            d.Activations = r.Int("Activations", 0);
            d.DurationSec = Units.Ms(r.Float("Duration", 0));
            d.ActivateInSec = Units.Ms(r.Float("ActivateIn", 0));
            d.CooldownSec = Units.Ms(r.Float("ActivationCooldown", 0));
            d.MultipleEmissions = r.Bool("MultipleEmissions");
            d.Affects = ParseAffects(r.List("AffectsObjects"), Affects.None);
            d.ApplyTo = r.Has("ApplyToObjects") ? ParseAffects(r.List("ApplyToObjects")) : Affects.All;
            var trig = r.Has("Trigger") ? r.List("Trigger") : new List<string> { "Enter" };
            d.TriggerUpdate = trig.Contains("Update");
            d.TriggerEnter = trig.Contains("Enter");
            d.TriggerExit = trig.Contains("Exit");
            var script = r.List("SimpleScript");
            if (script.Count > 0)
            {
                d.Script = script[0];
                if (d.Script == "TriggerEmission") d.RawDirection = script.Count < 2 || script[1].ToLowerInvariant() != "false";
                else if (d.Script == "Homing" && script.Count > 1) float.TryParse(script[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d.HomingStrength);
            }
            var p = r.Ref("Physics");
            d.RadiusPx = p != null ? p.Float("Radius", 10) : 10;
            d.StatBonus = r.Ref("StatBonuses");
            var c = r.Ref("ColorReference");
            if (c != null) d.Tint = new Color(Mathf.Clamp01(0.35f + c.Float("RedOffset") / 255f), Mathf.Clamp01(0.35f + c.Float("GreenOffset") / 255f), Mathf.Clamp01(0.35f + c.Float("BlueOffset") / 255f));
            foreach (var e in r.List("Emitters")) { var ed = Emitter(GameData.RefId(e)); if (ed != null) d.Emitters.Add(ed); }
            foreach (var f in r.List("Followers")) { var fd = Follower(GameData.RefId(f)); if (fd != null && fd != d) d.SubFollowers.Add(fd); }
            // Status id used for IPenguin.AddEffect: "Status_Fire" → "Fire"
            d.StatusId = id.StartsWith("Status_") ? id.Substring(7) : id;
            return d;
        }
    }

    public sealed class ItemDef
    {
        public string Id, Type, Targeting, GraphicId;
        public Record Rec;
        public float SimulationDistance;     // units
        public readonly List<EmitterDef> Emitters = new List<EmitterDef>();
        public bool IsWeapon => Type == "Weapon";
        public bool IsBooster => Type == "Booster";
    }

    public sealed class EmitterDef
    {
        public string Id, Kind, Sound, AnimationId;
        public Affects Affects;
        public int Number, OffsetBy;
        public float DelaySec, Spread, AngleOne, AngleTwo;
        public bool UseHitDirection, RandomOffset, SoundOnce;
        public MissileDef Missile;
        public ExplosionDef Explosion;
        public readonly List<FollowerDef> Followers = new List<FollowerDef>();
        public bool IsMissile => Kind == "MissileEmitter" && Missile != null;
        public bool IsExplosion => Kind == "ExplosionEmitter" && Explosion != null;
    }

    public sealed class MissileDef
    {
        public string Id, Type, Tail, GraphicId;
        public string Script;                // SimpleScript other than Ray: "Orbital" (drops from the sky), "Crawl" (goo)
        public float TimerSec, DurationSec, IntervalSec, ImpulseMin, ImpulseMax, TailSpawnDistance, TailSpawnTime;
        public bool RandomIntervalStart, CameraFollowed;
        public int RayHits;                  // 0 = no Ray script, -1 = unlimited
        public Affects RayAffects;
        public float RadiusPx = 5, Density = 75, Friction = 0.3f, Restitution, GravityScale = 1;
        public bool Bullet, FixedRotation = true;
        public readonly List<EmitterDef> Emitters = new List<EmitterDef>();
        public float RadiusU => Units.W(RadiusPx);
        /// <summary>Nape mass of the body (density/1000 · area in px²).</summary>
        public float NapeMass => Mathf.Max(1e-4f, Density / 1000f * Mathf.PI * RadiusPx * RadiusPx);
        /// <summary>Unity mass, same convention as the battle's penguins (density · π · r² / 100000).</summary>
        public float UnityMass => Mathf.Max(0.01f, Density * Mathf.PI * RadiusPx * RadiusPx / 100000f);
    }

    public sealed class ExplosionDef
    {
        public string Id, DamageType, Particle;
        public float Attack, DamageRadiusPx, ImpulseRadiusPx, Impulse, ShakeSec, ShakeStrength, ShapeMinPx, ShapeMaxPx;
        public bool Flash, Teleport;
        public string Script;                // SimpleScript other than Teleport ("BuildWall", count, radius px)
        public List<string> ScriptArgs;
        public readonly List<EmitterDef> Emitters = new List<EmitterDef>();
        public int ArgInt(int i, int def) => ScriptArgs != null && i < ScriptArgs.Count && int.TryParse(ScriptArgs[i], out var v) ? v : def;
    }

    public sealed class FollowerDef
    {
        public string Id, Type, EmitAt, Target, TargetSelection, Script, StatusId;
        public int Activations;
        public float DurationSec, ActivateInSec, CooldownSec, RadiusPx, HomingStrength;
        public bool MultipleEmissions, TriggerUpdate, TriggerEnter, TriggerExit, RawDirection = true;
        public Affects Affects, ApplyTo;
        public Record StatBonus;
        public Color? Tint;
        public readonly List<EmitterDef> Emitters = new List<EmitterDef>();
        public readonly List<FollowerDef> SubFollowers = new List<FollowerDef>();
        public bool IsStatus => Type == "Status" || Type == "StatusPermanent";
        public float RadiusU => Units.W(RadiusPx);
    }
}
