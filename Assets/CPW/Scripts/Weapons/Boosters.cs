using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Booster items (Item.Type "Booster"). Every booster row in the original data points at the placeholder
    /// Bonus.SalmonSushi, so behaviour is built from the item descriptions plus the leftover config rows
    /// (Explosion Kamikaze/Scroll/Mine/FlameMine/Mushroom/Confetti, Bonus Shield/PogoStick/*Sushi, Status_Poison).
    /// Turn-limited boosters end at the next OnTurnStart (i.e. when the user's turn is over).
    /// </summary>
    internal static class Boosters
    {
        sealed class Active
        {
            public IPenguin P;
            public string Id;
            public int Turns;             // > 0: ends after that many turn starts; -1: until consumed
            public StatChange Stats;
            public GameObject Visual;
            public Vector3 VisualOffset;
            public float Timer, Tick;
            public Shot Shot;
            public HashSet<IPenguin> Hit;
            public bool Ended;
        }

        static readonly List<Active> active = new List<Active>();
        static readonly List<GameObject> banners = new List<GameObject>();

        /// <summary>Apply booster itemId to user. False = can't be used now (already active, full HP...).</summary>
        public static bool Use(IPenguin p, string id)
        {
            if (p == null || !p.Alive || string.IsNullOrEmpty(id)) return false;
            var rt = WeaponRuntime.I;
            var pos = p.Position;
            int face = p.Facing >= 0 ? 1 : -1;
            switch (id)
            {
                case "SalmonSushi":
                case "SpicySushi":
                case "WasabiSushi":
                {
                    if (Find(p, "SalmonSushi") != null || Find(p, "SpicySushi") != null || Find(p, "WasabiSushi") != null) return false;
                    var bonus = GameData.Get("Bonus", id);
                    var a = Add(p, id, 1);
                    if (bonus != null) a.Stats = StatChange.Apply(p, bonus);
                    else if (p.Stats != null)
                    {
                        float m = id == "SalmonSushi" ? 1.5f : id == "SpicySushi" ? 2f : 3f;
                        p.Stats.attackMultiplier *= m;
                        a.Stats = null; a.Timer = m;   // remembered for revert
                    }
                    p.AddEffect(id, 1);
                    Fx.Sparks(pos + Vector2.up, id == "SpicySushi" ? new Color(1f, 0.4f, 0.1f) : id == "WasabiSushi" ? new Color(0.5f, 1f, 0.2f) : new Color(1f, 0.6f, 0.5f), 16);
                    Fx.FloatText(pos + Vector2.up * 1.6f, id == "SalmonSushi" ? "ATTACK +50%" : id == "SpicySushi" ? "ATTACK x2" : "ATTACK x3", new Color(1f, 0.75f, 0.3f), 0.8f);
                    AudioManager.Sfx(id);
                    return true;
                }
                case "Shield":
                {
                    if (Find(p, id) != null) return false;
                    var a = Add(p, id, -1);
                    var bonus = GameData.Get("Bonus", "Shield");
                    if (bonus != null) a.Stats = StatChange.Apply(p, bonus);
                    a.Visual = Bubble(Tuning.PenguinRadius * 2.6f, new Color(0.45f, 0.8f, 1f, 0.28f));
                    a.VisualOffset = new Vector3(0, 0, -0.5f);
                    p.AddEffect(id, 1);
                    AudioManager.Sfx(id);
                    return true;
                }
                case "Umbrella":
                {
                    if (Find(p, id) != null) return false;
                    var a = Add(p, id, 1);
                    p.Stats?.flags.Add("NoFall");
                    a.Visual = UmbrellaVisual();
                    a.VisualOffset = new Vector3(0, Tuning.PenguinRadius + 0.9f, -0.3f);
                    p.AddEffect(id, 1);
                    p.AddEffect("NoFall", 1);
                    AudioManager.Sfx(id);
                    return true;
                }
                case "PogoStick":
                {
                    if (Find(p, id) != null) return false;
                    var a = Add(p, id, 1);
                    var bonus = GameData.Get("Bonus", "PogoStick");
                    if (bonus != null) a.Stats = StatChange.Apply(p, bonus);
                    p.AddEffect(id, 1);
                    Fx.FloatText(pos + Vector2.up * 1.6f, "JUMP!", new Color(0.6f, 0.9f, 1f), 0.8f);
                    AudioManager.Sfx(id);
                    return true;
                }
                case "ProteinBar":
                {
                    if (Find(p, id) != null) return false;
                    Add(p, id, 1);
                    p.AddEffect(id, 1);
                    Fx.FloatText(pos + Vector2.up * 1.6f, "DOUBLE SHOT", new Color(1f, 0.85f, 0.3f), 0.8f);
                    AudioManager.Sfx(id);
                    return true;
                }
                case "Bandage":
                {
                    float missing = p.MaxHP - p.HP;
                    if (missing < 1f) return false;
                    float amount = Mathf.Min(50f, missing);
                    p.Heal(amount);
                    Fx.FloatText(pos + Vector2.up * 1.2f, "+" + Mathf.RoundToInt(amount), new Color(0.4f, 1f, 0.4f), 0.9f);
                    Fx.Sparks(pos + Vector2.up * 0.5f, new Color(0.5f, 1f, 0.5f), 14);
                    AudioManager.Sfx(id);
                    return true;
                }
                case "Scroll":
                {
                    // "explosion around you pushes things": impulse-only blast that spares the user
                    var e = Deployable.Synthetic("Scroll", Affects.Enemy | Affects.Object | Affects.Weapon, null, "Scroll");
                    EmissionEngine.Run(e, new Src { Pos = pos, HasDir = true, Dir = Vector2.up }, Shot.For(p, id));
                    Fx.Explosion(pos, 3f, "Wind");
                    AudioManager.Sfx(id);
                    return true;
                }
                case "Burrito":
                {
                    if (Find(p, id) != null) return false;
                    var a = Add(p, id, -1);
                    a.Timer = WeaponTuning.GasSeconds;
                    a.Shot = Shot.For(p, id);
                    a.Hit = new HashSet<IPenguin>();
                    Fx.Explosion(pos, WeaponTuning.GasRadius * 0.6f, "PoisonExplosion");
                    AudioManager.Sfx(id);
                    return true;
                }
                case "Kamikaze":
                {
                    var shot = Shot.For(p, id);
                    var e = Deployable.Synthetic("Kamikaze", Affects.All & ~Affects.Self, null, "Kamikaze");
                    EmissionEngine.Run(e, new Src { Pos = pos, HasDir = true, Dir = Vector2.up }, shot);
                    rt.ScheduleAction(0.05f, shot, () =>
                    {
                        if (p.Alive)
                            p.TakeDamage(new DamageInfo { amount = 100000f, type = "Normal", attacker = p.PlayerIndex, itemId = id, point = p.Position, impulse = Vector2.zero });
                    });
                    rt.Track(shot);
                    AudioManager.Sfx(id);
                    return true;
                }
                case "Caltrops":
                {
                    for (int i = 0; i < 3; i++)
                    {
                        var d = Deployable.Spawn(Deployable.Kind.Caltrops, pos + new Vector2(face * 0.6f, 0.6f), p, id);
                        d.Body.SetVel(new Vector2(face * (2.5f + i * 2.2f), 4.5f + i * 0.6f));
                    }
                    AudioManager.Sfx(id);
                    return true;
                }
                case "Mine":
                case "FlameMine":
                case "Mushroom":
                {
                    var kind = id == "Mine" ? Deployable.Kind.Mine : id == "FlameMine" ? Deployable.Kind.FlameMine : Deployable.Kind.Mushroom;
                    var d = Deployable.Spawn(kind, pos + new Vector2(face * (Tuning.PenguinRadius + 0.5f), 0.1f), p, id);
                    d.Body.SetVel(new Vector2(face * 1.5f, 1.5f));
                    AudioManager.Sfx(id);
                    return true;
                }
                case "Confetti":
                    Fx.Confetti(pos + Vector2.up * 1.2f, 60);
                    Fx.Explosion(pos + Vector2.up, 1.5f, "ConfettiBoosterBlast");
                    AudioManager.Sfx(id);
                    return true;
                case "Banner":
                {
                    // cosmetic: plant the team banner next to the penguin for the rest of the battle
                    var flag = BannerVisual(p);
                    Vector2 spot = pos + new Vector2(-face * 0.9f, 0);
                    if (BattleTerrain.I != null && BattleTerrain.I.GroundBelow(spot.x, pos.y + 1f, out var g)) spot = g;
                    flag.transform.position = new Vector3(spot.x, spot.y, 0.4f);
                    banners.Add(flag);
                    Fx.Confetti(spot + Vector2.up * 2f, 20);
                    AudioManager.Sfx(id);
                    return true;
                }
            }
            return false;
        }

        static Active Find(IPenguin p, string id)
        {
            foreach (var a in active) if (!a.Ended && a.Id == id && ReferenceEquals(a.P, p)) return a;
            return null;
        }

        static Active Add(IPenguin p, string id, int turns)
        {
            var a = new Active { P = p, Id = id, Turns = turns };
            active.Add(a);
            return a;
        }

        static void End(Active a)
        {
            if (a.Ended) return;
            a.Ended = true;
            a.Stats?.Revert();
            if (a.Stats == null && a.Timer > 1f && a.Id.EndsWith("Sushi") && a.P?.Stats != null) a.P.Stats.attackMultiplier /= a.Timer;
            if (a.Id == "Umbrella") a.P?.Stats?.flags.Remove("NoFall");
            if (a.Visual) Object.Destroy(a.Visual);
            if (a.Id == "Shield" && Live(a.P)) Fx.Sparks(a.P.Position, new Color(0.5f, 0.85f, 1f), 18);
        }

        /// <summary>Not null, not a destroyed Unity object (battle teardown) and alive.</summary>
        static bool Live(IPenguin p) => p is Object o ? o != null && p.Alive : p != null && p.Alive;

        /// <summary>The shield booster blocks the next damaging hit completely. True = hit absorbed.</summary>
        public static bool Absorb(IDamageable t)
        {
            if (!(t is IPenguin p)) return false;
            var a = Find(p, "Shield");
            if (a == null) return false;
            End(a);
            Fx.FloatText(p.Position + Vector2.up * 1.4f, "BLOCKED", new Color(0.5f, 0.85f, 1f), 0.8f);
            AudioManager.Sfx("ShieldHit");
            return true;
        }

        /// <summary>Protein bar: true once if the shooter's shot should fire a second time.</summary>
        public static bool ConsumeProtein(IPenguin p)
        {
            var a = Find(p, "ProteinBar");
            if (a == null) return false;
            End(a);
            return true;
        }

        public static void OnTurnStart(int playerIndex)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var a = active[i];
                if (a.Turns > 0 && --a.Turns <= 0) End(a);
                if (a.Ended) active.RemoveAt(i);
            }
        }

        /// <summary>Per-frame: follow visuals, umbrella fall clamp, burrito gas.</summary>
        public static void Tick(float dt)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var a = active[i];
                if (!a.Ended && !Live(a.P)) End(a);
                if (a.Ended) { active.RemoveAt(i); continue; }
                var pos = a.P.Position;
                if (a.Visual) a.Visual.transform.position = new Vector3(pos.x, pos.y, 0) + a.VisualOffset;

                switch (a.Id)
                {
                    case "Umbrella":
                        var rb = a.P.Body;
                        if (rb)
                        {
                            var v = rb.Vel();
                            if (v.y < -WeaponTuning.UmbrellaFallSpeed) rb.SetVel(new Vector2(v.x, -WeaponTuning.UmbrellaFallSpeed));
                            if (a.Visual) a.Visual.transform.localScale = Vector3.one * (v.y < -1f ? 1.15f : 0.75f);
                        }
                        break;
                    case "Burrito":
                        a.Timer -= dt;
                        a.Tick -= dt;
                        if (a.Tick <= 0)
                        {
                            a.Tick = 0.35f;
                            Fx.Smoke(pos + Random.insideUnitCircle * WeaponTuning.GasRadius * 0.6f, 1.6f, new Color(0.55f, 0.75f, 0.2f, 0.45f));
                            var poison = WeaponDefs.Follower("Status_Poison");
                            foreach (var e in BattleWorld.Penguins)
                            {
                                if (e == null || !e.Alive || ReferenceEquals(e, a.P) || a.Hit.Contains(e)) continue;
                                if ((e.Position - pos).sqrMagnitude > WeaponTuning.GasRadius * WeaponTuning.GasRadius) continue;
                                a.Hit.Add(e);
                                if (poison != null) FollowerRt.Attach(poison, e, a.Shot);
                                else e.AddEffect("Poison", 2);
                                Fx.Bubbles(e.Position + Vector2.up, new Color(0.45f, 0.95f, 0.25f), 8);
                            }
                        }
                        if (a.Timer <= 0) End(a);
                        break;
                }
            }
        }

        /// <summary>True while a short booster effect (burrito gas) is still running.</summary>
        public static bool Busy
        {
            get
            {
                foreach (var a in active) if (!a.Ended && a.Id == "Burrito") return true;
                return false;
            }
        }

        public static void ClearAll()
        {
            foreach (var a in active) End(a);
            active.Clear();
            foreach (var b in banners) if (b) Object.Destroy(b);
            banners.Clear();
        }

        // ------------------------------------------------------------------ visuals

        static GameObject Bubble(float size, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "CPW.ShieldBubble";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(WeaponRuntime.I.Parent, false);
            go.transform.localScale = Vector3.one * size;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = Mats.Transparent(c);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        static GameObject UmbrellaVisual()
        {
            var root = new GameObject("CPW.Umbrella");
            root.transform.SetParent(WeaponRuntime.I.Parent, false);
            var canopy = ModelLibrary.Spawn("Missiles/Umbrella", root.transform, PrimitiveType.Sphere, 1f, new Color(0.9f, 0.2f, 0.3f));
            if (!ModelLibrary.Exists("Missiles/Umbrella"))
            {
                canopy.transform.localScale = new Vector3(2.2f, 0.7f, 1.2f);
                var stick = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(stick.GetComponent<Collider>());
                stick.transform.SetParent(root.transform, false);
                stick.transform.localPosition = new Vector3(0, -0.6f, 0);
                stick.transform.localScale = new Vector3(0.06f, 0.6f, 0.06f);
                stick.GetComponent<Renderer>().sharedMaterial = Mats.Toon(new Color(0.3f, 0.2f, 0.15f));
            }
            return root;
        }

        static GameObject BannerVisual(IPenguin p)
        {
            var root = new GameObject("CPW.Banner");
            root.transform.SetParent(WeaponRuntime.I.Parent, false);
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(pole.GetComponent<Collider>());
            pole.transform.SetParent(root.transform, false);
            pole.transform.localPosition = new Vector3(0, 1.2f, 0);
            pole.transform.localScale = new Vector3(0.07f, 1.2f, 0.07f);
            pole.GetComponent<Renderer>().sharedMaterial = Mats.Toon(new Color(0.45f, 0.3f, 0.2f));
            var cloth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(cloth.GetComponent<Collider>());
            cloth.transform.SetParent(root.transform, false);
            cloth.transform.localPosition = new Vector3(0.45f, 2.05f, 0);
            cloth.transform.localScale = new Vector3(0.85f, 0.55f, 0.04f);
            Color[] team = { new Color(0.9f, 0.25f, 0.25f), new Color(0.25f, 0.5f, 0.95f), new Color(0.3f, 0.8f, 0.3f), new Color(0.95f, 0.8f, 0.2f) };
            cloth.GetComponent<Renderer>().sharedMaterial = Mats.Toon(team[Mathf.Abs(p.PlayerIndex) % team.Length]);
            return root;
        }
    }
}
