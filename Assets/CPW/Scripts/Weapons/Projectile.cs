using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// A flying missile (port of tuxwars.battle.missiles.*). Behaviour by Missile.Type:
    /// Missile = explodes on first contact; TimerMissile = on contact or when Timer runs out;
    /// Grenade = bounces, explodes on Timer; Enviroment = emits its Emitters every Interval for Duration
    /// (burning patches, drill bits, void nodes); Mine = treated as a Grenade (boosters use Deployable).
    /// Remake additions: Sticky = sticks to the first penguin/object/wall it touches and explodes on Timer (Glue
    /// Bomb); SimpleScript "Crawl" on an Enviroment missile = creeps sideways after every emission (Grey Goo).
    /// MissileEmitter.AffectsObjects is the collision mask: categories not in it are passed through
    /// (a projectile that does not affect terrain becomes a trigger and hits targets by sweep tests).
    /// </summary>
    internal sealed class Projectile : MonoBehaviour
    {
        public MissileDef Def;
        public EmitterDef Emitter;
        public Shot Shot;
        public Rigidbody2D Body;
        public bool Alive => !exploded && !removed;
        public Vector2 Pos => Body ? Body.position : (Vector2)transform.position;
        public Vector2 LastVelocity => lastVel;

        CircleCollider2D col;
        Affects collide;
        bool triggerMode, exploded, removed, graceOver, hasContact, rocketLike;
        Vector2 origin, lastVel, normal, prevPos, lastTailPos;
        float age, contactAt = -10, intervalElapsed, durationElapsed, tailTimer, spinAngle;
        int attacker = -1, crawlDir = 1;
        bool stuck;
        IDamageable stuckTo;
        Vector2 stuckOffset;
        Transform visual;
        bool spriteVisual;
        TrailRenderer trail;
        readonly List<Collider2D> shooterCols = new List<Collider2D>();

        static readonly RaycastHit2D[] castBuf = new RaycastHit2D[16];
        static readonly Dictionary<long, PhysicsMaterial2D> materials = new Dictionary<long, PhysicsMaterial2D>();

        public static Projectile Spawn(EmitterDef e, MissileDef m, Vector2 pos, Vector2 dir, Src src, Shot shot, float speedOverride)
        {
            var rt = WeaponRuntime.I;
            var go = new GameObject("Missile." + m.Id);
            go.transform.SetParent(rt.Parent, false);
            go.transform.position = new Vector3(pos.x, pos.y, 0);
            var p = go.AddComponent<Projectile>();
            p.Def = m; p.Emitter = e; p.Shot = shot;
            p.attacker = shot != null ? shot.Attacker : -1;
            p.origin = pos; p.prevPos = pos; p.lastTailPos = pos;
            p.collide = e.Affects;
            p.triggerMode = (e.Affects & Affects.Terrain) == 0;
            p.rocketLike = m.Type == "Missile" || m.Type == "TimerMissile";
            if (m.RandomIntervalStart && m.IntervalSec > 0) p.intervalElapsed = Random.Range(0, m.IntervalSec);
            p.crawlDir = dir.x >= 0 ? 1 : -1;

            float speed = speedOverride > 0 ? speedOverride
                : EmissionEngine.LaunchSpeed(m, src != null ? src.Power01 : 0, src != null && src.Primary, src != null && src.Activation, shot?.Item);

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = m.GravityScale;
            rb.mass = m.UnityMass;
            rb.SetDrag(0, 0.05f);
            rb.freezeRotation = m.FixedRotation;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;   // terrain is thin EdgeCollider2D lines
            rb.sleepMode = RigidbodySleepMode2D.StartAwake;
            p.Body = rb;

            var c = go.AddComponent<CircleCollider2D>();
            c.radius = Mathf.Max(m.RadiusU, 0.08f);
            c.isTrigger = p.triggerMode;
            c.sharedMaterial = Material(m.Friction, m.Restitution);
            p.col = c;

            p.SetupCollisionFilter(shot != null ? shot.Shooter : null);
            rb.SetVel(dir.normalized * speed);
            p.lastVel = rb.Vel();

            p.BuildVisual();
            rt.Projectiles.Add(p);
            if (shot != null)
            {
                shot.Live++;
                rt.Track(shot);
                if (m.CameraFollowed) shot.Camera = p;
            }
            foreach (var f in e.Followers) FollowerRt.AttachToProjectile(f, p, shot);
            return p;
        }

        static PhysicsMaterial2D Material(float friction, float restitution)
        {
            restitution = Mathf.Clamp01(restitution);
            long key = (long)(friction * 1000) * 100000 + (long)(restitution * 1000);
            if (materials.TryGetValue(key, out var pm) && pm) return pm;
            pm = new PhysicsMaterial2D("CPW.Missile") { friction = friction, bounciness = restitution };
            materials[key] = pm;
            return pm;
        }

        /// <summary>Ignore what this missile does not affect, other missiles, deployables and (briefly) the shooter.</summary>
        void SetupCollisionFilter(IPenguin shooter)
        {
            foreach (var pen in BattleWorld.Penguins)
            {
                if (pen == null || !pen.gameObject) continue;
                bool isShooter = shooter != null && ReferenceEquals(pen, shooter);
                var cat = WorldQuery.Category(pen, attacker);
                bool ignore = (collide & cat) == 0;
                foreach (var pc in WorldQuery.Colliders(pen.gameObject))
                {
                    if (ignore) Physics2D.IgnoreCollision(col, pc, true);
                    else if (isShooter) { Physics2D.IgnoreCollision(col, pc, true); shooterCols.Add(pc); }
                }
            }
            if (shooterCols.Count == 0) graceOver = true;
            foreach (var o in BattleWorld.Objects)
            {
                if (o == null || !o.gameObject) continue;
                bool ignore = o is Deployable || (collide & Affects.Object) == 0;
                if (!ignore) continue;
                foreach (var oc in WorldQuery.Colliders(o.gameObject)) Physics2D.IgnoreCollision(col, oc, true);
            }
            foreach (var other in WeaponRuntime.I.Projectiles)
                if (other && other.col) Physics2D.IgnoreCollision(col, other.col, true);
        }

        void FixedUpdate()
        {
            if (!Alive || !Body) return;
            float dt = Time.fixedDeltaTime;
            age += dt;
            var v = Body.Vel();
            if (v.sqrMagnitude > 1e-4f) lastVel = v;
            var pos = Body.position;

            if (!graceOver && age >= WeaponTuning.ShooterGraceSec)
            {
                graceOver = true;
                foreach (var sc in shooterCols) if (sc && col) Physics2D.IgnoreCollision(col, sc, false);
            }

            if (triggerMode && SweepTargets(prevPos, pos)) return;

            if (WorldQuery.InWater(pos))
            {
                Fx.Splash(pos, Mathf.Clamp(Def.RadiusU * 4f, 0.4f, 2f));
                AudioManager.Sfx(Def.RadiusPx > 10 ? "WaterHitMediumFast" : "WaterHitSmallFast", 0.7f);
                Remove();
                return;
            }
            if (WorldQuery.OutOfWorld(pos)) { Remove(); return; }

            switch (Def.Type)
            {
                case "TimerMissile":
                case "Grenade":
                case "Mine":
                    if (age >= Def.TimerSec) { Explode(); return; }
                    break;
                case "Sticky":
                    if (stuck && stuckTo != null)
                    {
                        if (stuckTo.Alive && stuckTo.gameObject) Body.MovePosition(stuckTo.Position + stuckOffset);
                        else Unstick();
                    }
                    if (age >= Def.TimerSec) { Explode(); return; }
                    break;
                case "Enviroment":
                    durationElapsed += dt;
                    if (Def.IntervalSec > 0)
                    {
                        intervalElapsed += dt;
                        if (intervalElapsed >= Def.IntervalSec && durationElapsed <= Def.DurationSec + 0.06f)
                        {
                            intervalElapsed -= Def.IntervalSec;
                            EmitCopy(null);
                            if (Def.Script == "Crawl" && Alive) Crawl();
                        }
                    }
                    if (durationElapsed >= Def.DurationSec) { Remove(); return; }
                    break;
            }

            if (age > WeaponTuning.MaxProjectileLifetime)
            {
                if (rocketLike) Explode(); else Remove();
                return;
            }
            prevPos = pos;
        }

        /// <summary>Trigger-mode hit test: sweep the circle along this step's motion.</summary>
        bool SweepTargets(Vector2 from, Vector2 to)
        {
            var delta = to - from;
            float len = delta.magnitude;
            int n = len > 1e-4f
                ? Physics2D.CircleCast(from, col.radius, delta / len, Phys.AllFilter, castBuf, len)
                : Physics2D.CircleCast(from, col.radius, Vector2.right, Phys.AllFilter, castBuf, 0f);
            for (int i = 0; i < n; i++)
            {
                var h = castBuf[i];
                var c = h.collider;
                if (!c || c == col || c.isTrigger) continue;
                var d = WorldQuery.FindDamageable(c);
                if (d == null || !d.Alive || d is Deployable) continue;
                if ((collide & WorldQuery.Category(d, attacker)) == 0) continue;
                if (!graceOver && Shot != null && ReferenceEquals(d, Shot.Shooter)) continue;
                hasContact = true; contactAt = Time.time;
                normal = h.normal.sqrMagnitude > 0.5f ? h.normal : -delta.normalized;
                if (rocketLike)
                {
                    Body.position = h.centroid;
                    Explode();
                    return true;
                }
            }
            return false;
        }

        void OnCollisionEnter2D(Collision2D c)
        {
            Contact(c);
            if (rocketLike && Alive) Explode();
            else if (Def.Type == "Sticky" && Alive && !stuck) Stick(c.collider);
        }

        /// <summary>Glue Bomb: stop and ride on what was hit (a penguin or object keeps carrying it).</summary>
        void Stick(Collider2D other)
        {
            stuck = true;
            var d = WorldQuery.FindDamageable(other);
            stuckTo = d != null && !(d is Deployable) ? d : null;
            stuckOffset = stuckTo != null ? Body.position - stuckTo.Position : Vector2.zero;
            Body.SetVel(Vector2.zero);
            Body.angularVelocity = 0;
            Body.bodyType = RigidbodyType2D.Kinematic;
            if (col) col.isTrigger = true;   // no more pushing what it sticks to
            AudioManager.Sfx("StickyBombBlob", 0.8f);
            Fx.Bubbles(Body.position, new Color(0.35f, 0.85f, 0.25f, 0.9f), 6);
        }

        /// <summary>What it stuck to is gone (died, removed): fall again.</summary>
        void Unstick()
        {
            stuckTo = null;
            stuck = false;
            Body.bodyType = RigidbodyType2D.Dynamic;
            if (col) col.isTrigger = triggerMode;
        }

        /// <summary>Grey Goo: after each bite creep sideways (and keep falling into the hole it just ate).</summary>
        void Crawl()
        {
            var v = Body.Vel();
            if (Mathf.Abs(v.x) < WeaponTuning.GooCrawlSpeed * 0.25f && hasContact && Time.time - contactAt < 0.5f && Mathf.Abs(normal.x) > 0.7f)
                crawlDir = normal.x > 0 ? 1 : -1;   // pressed against a wall: turn around
            Body.SetVel(new Vector2(crawlDir * WeaponTuning.GooCrawlSpeed, Mathf.Min(v.y, 0f)));
        }
        void OnCollisionStay2D(Collision2D c) { if (Alive && (Time.frameCount & 3) == 0) Contact(c); }

        void Contact(Collision2D c)
        {
            if (!Alive || c.contactCount == 0) return;
            var cp = c.GetContact(0);
            var n = Pos - cp.point;
            normal = n.sqrMagnitude > 1e-6f ? n.normalized : cp.normal;
            hasContact = true;
            contactAt = Time.time;
        }

        Src MakeSrc(bool withDir, Vector2 dir)
        {
            return new Src
            {
                Pos = Pos,
                Vel = lastVel,
                HasDir = withDir,
                Dir = dir,
                HasContact = hasContact && Time.time - contactAt < 0.35f,
                Normal = normal,
                Power01 = 0
            };
        }

        /// <summary>Explode now: Ray pass (bullets), then the missile's Emitters, then disappear.</summary>
        public void Explode()
        {
            if (!Alive) return;
            exploded = true;
            var src = MakeSrc(false, Vector2.zero);
            if (Def.RayHits != 0) RayPass(src);
            EmissionEngine.Run(Def.Emitters, src, Shot);
            Remove();
        }

        /// <summary>EmissionSpawn: emit this missile's Emitters at its position without removing it.</summary>
        public void EmitCopy(Vector2? dir)
        {
            if (!Alive) return;
            EmissionEngine.Run(Def.Emitters, MakeSrc(dir.HasValue, dir ?? Vector2.zero), Shot);
        }

        /// <summary>
        /// SimpleScript "Ray": cast from the bullet back to where it was fired and emit the bullet's explosion
        /// on up to N things along the way (-1 = all) that match the Ray affects list. Targets hit here are not
        /// hit again by the bullet's own explosion.
        /// </summary>
        void RayPass(Src src)
        {
            var end = Pos;
            var path = origin - end;
            float len = path.magnitude;
            src.Exclude = new HashSet<IDamageable>();
            var shotDir = len > 1e-4f ? -path / len : lastVel.normalized;
            Color beam = Def.Tail.Contains("Railgun") ? new Color(0.4f, 0.8f, 1f) : new Color(1f, 0.95f, 0.7f);
            Fx.Beam(origin, end, beam, Def.Tail.Contains("Railgun") ? 0.25f : 0.07f, Def.Tail.Contains("Railgun") ? 0.4f : 0.12f);
            if (len < 1e-3f) return;
            int n = Physics2D.Raycast(end, path / len, Phys.AllFilter, castBuf, len);
            int limit = Def.RayHits < 0 ? n : Mathf.Min(n, Def.RayHits);
            var seen = new HashSet<Object>();
            for (int i = 0, used = 0; i < n && used < limit; i++)
            {
                var c = castBuf[i].collider;
                if (!c || c == col || c.isTrigger) continue;
                Affects cat; Object key;
                if (WorldQuery.IsTerrain(c)) { cat = Affects.Terrain; key = BattleTerrain.I; }
                else
                {
                    var d = WorldQuery.FindDamageable(c);
                    if (d == null || d is Deployable) continue;
                    cat = WorldQuery.Category(d, attacker); key = d.gameObject;
                }
                if (!seen.Add(key)) continue;
                used++;
                if ((Def.RayAffects & cat) == 0) continue;
                var hitSrc = new Src { Pos = castBuf[i].point, Vel = lastVel, HasDir = true, Dir = shotDir, Exclude = src.Exclude };
                EmissionEngine.Run(Def.Emitters, hitSrc, Shot);
            }
        }

        /// <summary>Disappear (no explosion). The trail is detached so it fades out.</summary>
        public void Remove()
        {
            if (removed) return;
            removed = true;
            if (trail)
            {
                trail.transform.SetParent(WeaponRuntime.I.Parent, true);
                trail.emitting = false;
                trail.autodestruct = true;
            }
            if (Shot != null) Shot.Live--;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (!removed) { removed = true; if (Shot != null) Shot.Live--; }
        }

        // ------------------------------------------------------------------ visuals

        void Update()
        {
            if (!Alive) return;
            var pos = Pos;
            var v = lastVel;
            if (visual && spriteVisual)
            {
                // the original: body rotation when the body may rotate, else the art's "up" turned to the velocity
                if (!Def.FixedRotation) visual.localRotation = Quaternion.identity;
                else if (v.sqrMagnitude > 0.01f) visual.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg - 90f);
            }
            else if (visual)
            {
                if (rocketLike || Def.Bullet)
                {
                    if (v.sqrMagnitude > 0.01f) visual.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg);
                }
                else if (Def.FixedRotation)
                {
                    spinAngle -= Body.Vel().x / Mathf.Max(0.1f, col.radius) * Mathf.Rad2Deg * Time.deltaTime;
                    visual.localRotation = Quaternion.Euler(0, 0, spinAngle);
                }
            }
            // tail particles by distance travelled (ParticleStreamSpawnDistance) and/or time (ParticleStreamSpawnTime)
            if (!string.IsNullOrEmpty(Def.Tail))
            {
                float step = Mathf.Max(Def.TailSpawnDistance, 0.35f);
                int guard = 0;
                while ((pos - lastTailPos).sqrMagnitude >= step * step && guard++ < 4)
                {
                    lastTailPos = Vector2.MoveTowards(lastTailPos, pos, step);
                    Fx.Tail(Def.Tail, lastTailPos, v);
                }
                if ((pos - lastTailPos).sqrMagnitude > step * step * 16) lastTailPos = pos;
                if (Def.TailSpawnTime > 0)
                {
                    tailTimer += Time.deltaTime;
                    if (tailTimer >= Mathf.Max(Def.TailSpawnTime, 0.05f)) { tailTimer = 0; Fx.Tail(Def.Tail, pos, Vector2.up); }
                }
            }
        }

        void BuildVisual()
        {
            var color = ColorFor(Def, Shot != null ? Shot.ItemId : "");
            float size = Mathf.Max(Def.RadiusU * 2f, Def.Bullet ? 0.2f : 0.34f);
            bool capsule = rocketLike && !Def.Bullet && Def.RadiusPx >= 4 && Def.Tail.Length > 0 && !Def.Tail.Contains("Grenade") && !Def.Tail.Contains("Molotov");
            var holder = new GameObject("Visual").transform;
            holder.SetParent(transform, false);
            holder.localPosition = new Vector3(0, 0, 0);
            visual = holder;
            if (BuildSpriteVisual(holder, color)) { AddTrail(color); return; }
            string path = string.IsNullOrEmpty(Def.GraphicId) ? null : "Missiles/" + Def.GraphicId;
            var model = ModelLibrary.Spawn(path, holder, capsule ? PrimitiveType.Capsule : PrimitiveType.Sphere, size, color);
            if (!ModelLibrary.Exists(path))
            {
                if (capsule)
                {
                    model.transform.localScale = new Vector3(size * 0.75f, size * 1.1f, size * 0.75f);
                    model.transform.localRotation = Quaternion.Euler(0, 0, -90);
                }
                else if (Def.Bullet) model.transform.localScale = new Vector3(size * 1.6f, size * 0.7f, size * 0.7f);
                // Enviroment fire bits are just glowing particles
                if (Def.Type == "Enviroment" && Def.Tail.Contains("Molotov")) model.transform.localScale *= 0.6f;
                if (Def.Type == "Enviroment" && string.IsNullOrEmpty(Def.Tail)) model.SetActive(false);   // invisible nodes
            }
            AddTrail(color);
        }

        /// <summary>sortingOrder of missile sprites (above penguins and props, below effects).</summary>
        public const int SpriteOrder = 40;
        const float SpriteZ = -0.3f;
        static Sprite glowSprite;

        /// <summary>
        /// The original ammo art (OriginalArt.MissileAnim: missiles/ammo, Flash size, registration point = pivot):
        /// animated ammo loops, ammo clips with weapon labels hold their "launch" frame. Ammo whose original graphic
        /// is an empty placeholder (molotovshard: plasma, laser, flame bits) gets a small glow so the tail particles
        /// have a core. False = no original art (OrbitalLaser, mines, remake-only Grey Goo): use the 3D model.
        /// </summary>
        bool BuildSpriteVisual(Transform holder, Color color)
        {
            if (Def.Script == "Crawl" || Def.Id.Contains("GreyGoo")) return false;   // remake-only: the rock art would be wrong
            var set = OriginalArt.MissileAnim(Def.Id);
            if (set == null) return false;
            holder.localPosition = new Vector3(0, 0, SpriteZ);
            spriteVisual = true;
            if (set.CanvasPx.x <= 2f && set.CanvasPx.y <= 2f)
            {
                string t = Def.Tail ?? "";
                bool glow = t.Contains("Plasma") || t.Contains("Laser") || t.Contains("Molotov") || t.Contains("Flaregun") || t.Contains("Railgun") || t.Contains("Acid");
                if (!glow || (Def.Type == "Enviroment" && !t.Contains("Plasma"))) return true;   // invisible node / burning patch: particles only
                if (!glowSprite)
                {
                    var tex = Mats.SoftCircle;
                    glowSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
                }
                var go = new GameObject("Glow");
                go.transform.SetParent(holder, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = glowSprite;
                sr.sharedMaterial = Mats.Additive(Color.white);
                sr.color = Color.Lerp(color, Color.white, 0.35f);
                sr.sortingOrder = SpriteOrder;
                go.transform.localScale = Vector3.one * Mathf.Max(Def.RadiusU * 3f, 0.45f);
                return true;
            }
            var anim = SpriteAnim.Create(holder, set, "Sprite", SpriteOrder, loop: true);
            if (set.HasLabel("launch")) anim.Hold("launch");
            if (Def.FixedRotation && lastVel.sqrMagnitude > 0.01f) holder.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(lastVel.y, lastVel.x) * Mathf.Rad2Deg - 90f);
            return true;
        }

        void AddTrail(Color color)
        {
            string t = Def.Tail ?? "";
            if (Def.Type == "Enviroment" && !t.Contains("Missile") && !t.Contains("Grenade")) return;
            float width, time; Color c;
            if (t.Contains("Laser")) { width = 0.16f; time = 0.18f; c = new Color(1f, 0.15f, 0.15f); }
            else if (t.Contains("Railgun")) { width = 0.2f; time = 0.25f; c = new Color(0.4f, 0.8f, 1f); }
            else if (t.Contains("Bullet") || t.Contains("Minigun")) { width = 0.07f; time = 0.08f; c = new Color(1f, 0.95f, 0.6f); }
            else if (t.Contains("Plasma")) { width = 0.3f; time = 0.25f; c = new Color(0.4f, 0.9f, 1f); }
            else if (t.Contains("Missile") || t.Contains("Trapezoid") || t.Contains("Fireworks")) { width = 0.28f; time = 0.3f; c = new Color(1f, 0.75f, 0.35f); }
            else if (t.Contains("Molotov") || t.Contains("Flaregun") || t.Contains("Cat")) { width = 0.18f; time = 0.25f; c = new Color(1f, 0.5f, 0.15f); }
            else if (t.Contains("Wind")) { width = 0.5f; time = 0.35f; c = new Color(0.9f, 0.95f, 1f, 0.5f); }
            else if (t.Contains("Grenade")) { width = 0.08f; time = 0.25f; c = new Color(1f, 1f, 1f, 0.5f); }
            else if (Def.Id.Contains("FireHose") || Def.Id.Contains("Water")) { width = 0.2f; time = 0.2f; c = new Color(0.5f, 0.8f, 1f, 0.8f); }
            else return;
            var go = new GameObject("Trail");
            go.transform.SetParent(transform, false);
            trail = go.AddComponent<TrailRenderer>();
            trail.time = time;
            trail.minVertexDistance = 0.15f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0.1f));
            trail.widthMultiplier = width;
            trail.sharedMaterial = Mats.AdditiveTex(Mats.SoftCircle, Color.white);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0), new GradientColorKey(c, 1) }, new[] { new GradientAlphaKey(c.a, 0), new GradientAlphaKey(0, 1) });
            trail.colorGradient = g;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.numCapVertices = 2;
        }

        /// <summary>Fallback colour per weapon family (used when a Missiles/ model is missing).</summary>
        static Color ColorFor(MissileDef m, string item)
        {
            string id = m.Id;
            if (id.Contains("Plasma")) return new Color(0.35f, 0.9f, 1f);
            if (id.Contains("Laser") || id.Contains("Railgun")) return new Color(1f, 0.25f, 0.25f);
            if (id.Contains("Molotov") || id.Contains("Napalm") || id.Contains("Cinder") || id.Contains("FuelAir") || id.Contains("Flare")) return new Color(1f, 0.5f, 0.12f);
            if (id.Contains("FireHose") || id.Contains("WaterBalloon")) return new Color(0.35f, 0.65f, 1f);
            if (id.Contains("Nuke") || id.Contains("Doomsday")) return new Color(0.95f, 0.85f, 0.2f);
            if (id.Contains("Cluster") || id.Contains("Grenade") || id.Contains("Fragment")) return new Color(0.3f, 0.45f, 0.2f);
            if (id.Contains("Rock")) return new Color(0.5f, 0.47f, 0.43f);
            if (id.Contains("Drill")) return new Color(0.7f, 0.7f, 0.75f);
            if (id.Contains("Dynamite")) return new Color(0.85f, 0.15f, 0.12f);
            if (id.Contains("Cat")) return new Color(1f, 0.6f, 0.2f);
            if (id.Contains("Wind")) return new Color(0.85f, 0.95f, 1f);
            if (id.Contains("Void")) return new Color(0.4f, 0.1f, 0.7f);
            if (id.Contains("Fireworks")) return Color.HSVToRGB(VisualRandom.Value, 0.7f, 1f);
            if (id.Contains("Artillery")) return new Color(0.35f, 0.38f, 0.3f);
            if (id.Contains("Flame")) return new Color(1f, 0.55f, 0.15f);
            if (id.Contains("Lemon")) return new Color(0.95f, 0.9f, 0.2f);
            if (id.Contains("Gas")) return new Color(0.5f, 0.85f, 0.3f);
            if (id.Contains("Sticky")) return new Color(0.35f, 0.8f, 0.25f);
            if (id.Contains("GreyGoo")) return new Color(0.6f, 0.62f, 0.66f);
            if (id.Contains("Teleport")) return new Color(0.6f, 0.4f, 1f);
            if (id.Contains("ShieldWall")) return new Color(0.4f, 0.7f, 1f);
            if (id.Contains("Snowball")) return new Color(0.95f, 0.97f, 1f);
            if (id.Contains("EasterEgg")) return Color.HSVToRGB(VisualRandom.Value, 0.45f, 1f);
            if (id.Contains("Cannon")) return new Color(0.4f, 0.24f, 0.12f);
            if (id.Contains("HeatSeeker")) return new Color(0.85f, 0.3f, 0.2f);
            if (m.Bullet) return new Color(1f, 0.85f, 0.4f);
            return new Color(0.55f, 0.55f, 0.6f);
        }
    }
}
