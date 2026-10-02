using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Things boosters leave in the level: land mines, flame mines, caltrops and poisonous mushrooms.
    /// They are damageable level objects (registered in BattleWorld.Objects) with a small dynamic body that
    /// rests on the terrain, ignores penguins and missiles, and can be blown around or set off by explosions.
    /// </summary>
    internal sealed class Deployable : MonoBehaviour, IDamageable
    {
        public enum Kind { Mine, FlameMine, Caltrops, Mushroom }

        public Kind Type;
        public int Owner = -1;
        public Shot Shot;
        public float Radius = 0.35f;
        public Rigidbody2D Body { get; private set; }
        public Vector2 Position => Body ? Body.position : (Vector2)transform.position;
        public bool Alive => !removed;

        bool removed, triggered;
        float armTimer, fuse, blink, cooldown;
        int charges = 2, age;
        Transform visual;
        Renderer lamp;

        // ------------------------------------------------------------------ creation

        public static Deployable Spawn(Kind kind, Vector2 pos, IPenguin owner, string itemId)
        {
            var go = new GameObject("CPW." + kind);
            go.transform.SetParent(WeaponRuntime.I.Parent, false);
            go.transform.position = new Vector3(pos.x, pos.y, 0f);
            var d = go.AddComponent<Deployable>();
            d.Type = kind;
            d.Owner = owner != null ? owner.PlayerIndex : -1;
            d.Shot = Shot.For(owner, itemId);
            d.Radius = kind == Kind.Caltrops ? 0.45f : kind == Kind.Mushroom ? 0.4f : 0.35f;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.mass = 0.4f;
            rb.gravityScale = 1f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.SetDrag(0.5f, 0.5f);
            rb.freezeRotation = kind != Kind.Caltrops;
            d.Body = rb;
            var col = go.AddComponent<CircleCollider2D>();
            col.radius = d.Radius;
            d.IgnorePenguins(col);

            d.armTimer = WeaponTuning.MineArmSec;
            d.BuildVisual();
            WeaponRuntime.I.Deployables.Add(d);
            BattleWorld.Objects.Add(d);
            return d;
        }

        /// <summary>Penguins walk over deployables; missiles already ignore them (Projectile collision filter).</summary>
        void IgnorePenguins(Collider2D col)
        {
            foreach (var p in BattleWorld.Penguins)
            {
                if (p == null || !p.gameObject) continue;
                foreach (var c in WorldQuery.Colliders(p.gameObject)) Physics2D.IgnoreCollision(col, c, true);
            }
            foreach (var o in WeaponRuntime.I.Deployables)
            {
                if (!o) continue;
                foreach (var c in WorldQuery.Colliders(o.gameObject)) Physics2D.IgnoreCollision(col, c, true);
            }
        }

        void BuildVisual()
        {
            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            switch (Type)
            {
                case Kind.Mine:
                case Kind.FlameMine:
                {
                    var bodyCol = Type == Kind.Mine ? new Color(0.25f, 0.27f, 0.3f) : new Color(0.75f, 0.25f, 0.1f);
                    var b = ModelLibrary.Spawn("Missiles/" + Type, visual, PrimitiveType.Cylinder, 1f, bodyCol);
                    if (!ModelLibrary.Exists("Missiles/" + Type)) b.transform.localScale = new Vector3(0.75f, 0.12f, 0.75f);
                    var l = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Destroy(l.GetComponent<Collider>());
                    l.transform.SetParent(visual, false);
                    l.transform.localPosition = new Vector3(0, 0.15f, -0.1f);
                    l.transform.localScale = Vector3.one * 0.16f;
                    lamp = l.GetComponent<Renderer>();
                    lamp.sharedMaterial = Mats.Unlit(new Color(0.3f, 0.1f, 0.1f));
                    break;
                }
                case Kind.Caltrops:
                    for (int i = 0; i < 3; i++)
                    {
                        var s = ModelLibrary.Spawn("Missiles/Caltrop", visual, PrimitiveType.Cube, 0.22f, new Color(0.55f, 0.58f, 0.62f));
                        s.transform.localPosition = new Vector3((i - 1) * 0.28f, -0.2f, 0);
                        s.transform.localRotation = Quaternion.Euler(35 + i * 20, 45, 45 + i * 30);
                    }
                    break;
                case Kind.Mushroom:
                {
                    var stem = ModelLibrary.Spawn("Missiles/Mushroom", visual, PrimitiveType.Cylinder, 1f, new Color(0.95f, 0.92f, 0.8f));
                    if (!ModelLibrary.Exists("Missiles/Mushroom"))
                    {
                        stem.transform.localScale = new Vector3(0.22f, 0.2f, 0.22f);
                        stem.transform.localPosition = new Vector3(0, -0.15f, 0);
                        var cap = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        Destroy(cap.GetComponent<Collider>());
                        cap.transform.SetParent(visual, false);
                        cap.transform.localPosition = new Vector3(0, 0.12f, 0);
                        cap.transform.localScale = new Vector3(0.7f, 0.4f, 0.7f);
                        cap.GetComponent<Renderer>().sharedMaterial = Mats.Toon(new Color(0.55f, 0.85f, 0.2f));
                    }
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ behaviour

        void FixedUpdate()
        {
            if (removed) return;
            var pos = Position;
            if (WorldQuery.InWater(pos) || WorldQuery.OutOfWorld(pos)) { if (WorldQuery.InWater(pos)) Fx.Splash(pos, 0.6f); Remove(true); return; }
            float dt = Time.fixedDeltaTime;
            armTimer -= dt;
            cooldown -= dt;

            if (triggered)
            {
                fuse -= dt;
                if (fuse <= 0) Detonate();
                return;
            }
            switch (Type)
            {
                case Kind.Mine:
                case Kind.FlameMine:
                    if (armTimer <= 0 && AnyPenguinWithin(WeaponTuning.MineTriggerRadius, false)) Trigger(WeaponTuning.MineFuseSec);
                    break;
                case Kind.Caltrops:
                    if (armTimer > WeaponTuning.MineArmSec - 0.5f || cooldown > 0) break;
                    foreach (var p in BattleWorld.Penguins)
                    {
                        if (p == null || !p.Alive || p.PlayerIndex == Owner) continue;
                        if ((p.Position - pos).sqrMagnitude > Sq(Radius + Tuning.PenguinRadius + 0.1f)) continue;
                        Spike(p);
                        break;
                    }
                    break;
                case Kind.Mushroom:
                    if (armTimer <= 0 && AnyPenguinWithin(Radius + 0.15f, true)) Trigger(0.05f);
                    break;
            }
        }

        void Update()
        {
            if (removed || lamp == null) return;
            blink += Time.deltaTime * (triggered ? 14f : armTimer > 0 ? 0f : 2.5f);
            bool on = armTimer <= 0 && Mathf.Sin(blink * Mathf.PI) > 0;
            if (on == lampShown && lampSet) return;   // assign only on change (Mats lookup allocates a key string)
            lampShown = on; lampSet = true;
            if (!lampOnMat) lampOnMat = Mats.Unlit(new Color(1f, 0.15f, 0.1f));
            if (!lampOffMat) lampOffMat = Mats.Unlit(new Color(0.3f, 0.1f, 0.1f));
            lamp.sharedMaterial = on ? lampOnMat : lampOffMat;
        }

        static Material lampOnMat, lampOffMat;
        bool lampShown, lampSet;

        static float Sq(float x) => x * x;

        bool AnyPenguinWithin(float r, bool body)
        {
            var pos = Position;
            float rr = body ? r + Tuning.PenguinRadius : r;
            foreach (var p in BattleWorld.Penguins)
                if (p != null && p.Alive && (p.Position - pos).sqrMagnitude <= rr * rr) return true;
            return false;
        }

        /// <summary>Caltrop shard hit: small damage + slowing goo (the original CaltropShard follower, 2 activations).</summary>
        void Spike(IPenguin p)
        {
            cooldown = 1.5f;
            p.TakeDamage(new DamageInfo { amount = 10, type = "Normal", attacker = Owner, itemId = "Caltrops", point = Position, impulse = Vector2.up * 2f * (p.Body ? p.Body.mass : 1f) });
            p.AddEffect("SlowGoo", 2);
            Fx.Sparks(Position, new Color(0.8f, 0.85f, 0.9f), 8);
            AudioManager.Sfx("Caltrops");
            if (--charges <= 0) Remove(true);
        }

        /// <summary>Start the fuse (proximity, follower trigger or damage).</summary>
        public void Trigger(float delay)
        {
            if (removed || triggered) return;
            if (Type == Kind.Caltrops) { Remove(true); return; }
            triggered = true;
            fuse = delay;
            if (delay > 0.1f) AudioManager.Sfx("MineBeep");
        }

        void Detonate()
        {
            if (removed) return;
            var pos = Position + Vector2.up * 0.1f;
            var src = new Src { Pos = pos, HasDir = true, Dir = Vector2.up, HasContact = true, Normal = Vector2.up };
            var shot = Shot;
            Remove(false);
            switch (Type)
            {
                case Kind.Mine:
                    var e = WeaponDefs.Emitter("MineExplosion") ?? Synthetic("Mine", Affects.All, null);
                    EmissionEngine.Run(e, src, shot);
                    break;
                case Kind.FlameMine:
                    EmissionEngine.Run(Synthetic("FlameMine", Affects.All, "Status_Fire", "MolotovExplosion"), src, shot);
                    var shards = WeaponDefs.Emitter("MolotovBurning");
                    if (shards != null) EmissionEngine.Run(shards, src.Copy(), shot);
                    break;
                case Kind.Mushroom:
                    EmissionEngine.Run(Synthetic("Mushroom", Affects.Penguin, "Status_Poison", "Mushroom"), src, shot);
                    Fx.Smoke(pos, 1.5f, new Color(0.45f, 0.8f, 0.2f, 0.6f));
                    break;
            }
        }

        /// <summary>An ExplosionEmitter built in code (booster config rows are placeholders in the original data).</summary>
        public static EmitterDef Synthetic(string explosionId, Affects affects, string followerId, string sound = null)
        {
            var e = new EmitterDef
            {
                Id = "Synthetic" + explosionId,
                Kind = "ExplosionEmitter",
                Sound = sound ?? explosionId,
                Affects = affects,
                Number = 1,
                OffsetBy = 1,
                Explosion = WeaponDefs.Explosion(explosionId),
            };
            var f = string.IsNullOrEmpty(followerId) ? null : WeaponDefs.Follower(followerId);
            if (f != null) e.Followers.Add(f);
            return e;
        }

        // ------------------------------------------------------------------ IDamageable

        public void TakeDamage(DamageInfo d)
        {
            if (removed) return;
            if (d.impulse != Vector2.zero && Body) Body.AddForce(d.impulse * (Body.mass / 1.14f), ForceMode2D.Impulse);
            switch (Type)
            {
                case Kind.Mine:
                case Kind.FlameMine: Trigger(0.15f); break;      // chain reactions
                case Kind.Mushroom: Fx.Debris(Position, new Color(0.55f, 0.85f, 0.2f), 8); Remove(true); break;
                case Kind.Caltrops: if (d.amount >= 25) Remove(true); break;
            }
        }

        /// <summary>Called at the start of every turn: mushrooms grow and reproduce if nobody touches them.</summary>
        public void OnTurn(int playerIndex)
        {
            if (removed || Type != Kind.Mushroom || playerIndex != Owner) return;
            age++;
            if (age % WeaponTuning.MushroomReproduceTurns != 0) return;
            int count = 0;
            foreach (var o in WeaponRuntime.I.Deployables) if (o && o.Alive && o.Type == Kind.Mushroom) count++;
            if (count >= WeaponTuning.MushroomMax) return;
            float x = Position.x + (Random.value < 0.5f ? -1 : 1) * Random.Range(1.2f, WeaponTuning.MushroomRadius);
            Vector2 spot = new Vector2(x, Position.y + 0.5f);
            if (BattleTerrain.I != null && BattleTerrain.I.GroundBelow(x, Position.y + 4f, out var g)) spot = g + Vector2.up * 0.45f;
            else if (BattleTerrain.I != null) return;
            if (WorldQuery.InWater(spot)) return;
            var owner = Shot != null ? Shot.Shooter : null;
            var m = Spawn(Kind.Mushroom, spot, owner, "Mushroom");
            m.armTimer = 0.5f;
            Fx.Bubbles(spot, new Color(0.5f, 0.9f, 0.3f), 6);
        }

        /// <summary>Remove from the world (fx = small puff).</summary>
        public void Remove(bool fx)
        {
            if (removed) return;
            removed = true;
            if (fx) Fx.Smoke(Position, 0.6f);
            BattleWorld.Objects.Remove(this);
            if (WeaponRuntime.Exists) WeaponRuntime.I.Deployables.Remove(this);
            if (gameObject) Destroy(gameObject);
        }

        void OnDestroy()
        {
            removed = true;
            BattleWorld.Objects.Remove(this);
        }
    }
}
