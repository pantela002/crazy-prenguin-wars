using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// A breakable physics prop placed by the level (DynamicObject element: crates, balls, planks, triangles of
    /// Wood/Stone/Ice/Metal). Shape, density, friction and restitution come from the original PhysicsEditor files
    /// (DynamicObjectDefs), hit points from LevelObject.Toughness (HP = Toughness * 3 like LevelGameObject.as),
    /// with 3 visible damage stages. Registered in BattleWorld.Objects; breaks into debris when HP reaches 0.
    /// </summary>
    public class DynamicObjectEntity : MonoBehaviour, IDamageable
    {
        /// <summary>
        /// Nape density → Rigidbody2D collider density (mass per world unit²). Water uses the same scale.
        /// 1/250 keeps the original penguin : prop mass ratio (BattleRules.PenguinMass is density * px² / 100000 and
        /// 1 unit² = 400 px²), so a small wood crate weighs ~0.1 against a 1.1 penguin and slides when walked into,
        /// while a metal cube (density 100) stays heavy.
        /// </summary>
        public static float DensityScale = 1f / 250f;
        /// <summary>Extra horizontal force a walking penguin adds to a prop it pushes, so light props get going
        /// despite their friction (heavy ones need many times more than this).</summary>
        public static float PushAssistForce = 14f;
        /// <summary>Explosion knockback arrives as a velocity change for everything; props scale it by sqrt(KnockRefMass / mass)
        /// (clamped to KnockScaleMin..Max) so light crates fly and heavy metal barely shifts.</summary>
        public static float KnockRefMass = 1f, KnockScaleMin = 0.3f, KnockScaleMax = 1.35f;
        /// <summary>Hit points per second a burnable prop (Wood, Ice, CustomObjects) loses while in lava.</summary>
        public static float LavaBurnPerSecond = 70f;
        /// <summary>Relative speed above which a collision plays the material's collision sound.</summary>
        public static float CollisionSoundSpeed = 4f;
        /// <summary>Raised when an object breaks or falls out of the world (object, last attacker player index or -1, item id).</summary>
        public static event System.Action<DynamicObjectEntity, int, string> Destroyed;

        public string Id { get; private set; }
        public string LevelObjectId { get; private set; }
        public string Material { get; private set; }         // Wood, Stone, Ice, Metal, CustomObjects
        public LevelData.LevelObjectPlacement Placement { get; private set; }
        public float HP { get; private set; }
        public float MaxHP { get; private set; }
        public float Toughness { get; private set; }
        public int Score { get; private set; }
        public bool Unbreakable { get; private set; }
        public float Area { get; private set; }               // world units² of all fixtures
        public Vector2 Size { get; private set; }             // collider bounding box (unrotated)
        public int DamageStage { get; private set; } = 1;    // 1..3 like the original _1/_2/_3 frames
        public int LastAttacker { get; private set; } = -1;
        public string LastItem { get; private set; }

        public Rigidbody2D Body { get; private set; }
        public Vector2 Position => Body ? Body.position : (Vector2)transform.position;
        public bool Alive { get; private set; }

        Transform visual;
        Renderer[] renderers;
        Color[] baseColors;
        MaterialPropertyBlock mpb;
        float flash, soundCooldown, burnFx;
        Color materialColor;
        Mesh ownedMesh;

        static readonly Dictionary<string, PhysicsMaterial2D> physMats = new Dictionary<string, PhysicsMaterial2D>();
        static readonly int ColorId = Shader.PropertyToID("_Color"), FlashId = Shader.PropertyToID("_Flash");

        /// <summary>Spawn a level object from its placement. Returns null if the placement is unusable.</summary>
        public static DynamicObjectEntity Create(LevelData.LevelObjectPlacement p, Transform parent)
        {
            if (p == null) return null;
            var def = DynamicObjectDef.Find(p.theme, p.fixture);
            if (def == null) { Debug.LogWarning("CPW: unknown level object fixture " + p.theme + "/" + p.fixture); return null; }
            var go = new GameObject("LevelObject_" + p.id);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(p.position.x, p.position.y, 0);
            var e = go.AddComponent<DynamicObjectEntity>();
            e.Setup(p, def);   // built unrotated so the visual fits the collider exactly
            go.transform.rotation = Quaternion.Euler(0, 0, p.angleDeg);
            e.Body.rotation = p.angleDeg;
            return e;
        }

        void Setup(LevelData.LevelObjectPlacement p, DynamicObjectDef def)
        {
            Placement = p;
            Id = p.id;
            Material = p.theme;
            LevelObjectId = p.LevelObjectId;
            var rec = GameData.Get("LevelObject", LevelObjectId);
            Toughness = rec != null ? rec.Float("Toughness", 50) : 50;
            Score = rec != null ? rec.Int("Score") : 0;
            MaxHP = HP = Mathf.Max(1, Toughness * 3);
            Unbreakable = p.unbreakable || Toughness >= 1e6f;
            materialColor = ColorFor(Material);

            Body = gameObject.AddComponent<Rigidbody2D>();
            Body.bodyType = RigidbodyType2D.Dynamic;
            Body.useAutoMass = true;
            Body.interpolation = RigidbodyInterpolation2D.Interpolate;
            Body.sleepMode = p.sleep ? RigidbodySleepMode2D.StartAsleep : RigidbodySleepMode2D.StartAwake;
            Body.SetDrag(0.05f, 0.3f);   // a little angular drag so props settle instead of rocking

            var pm = PhysMat(def);
            float density = def.density * DensityScale;
            float area = 0;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
            if (def.circle != null)
            {
                var cc = gameObject.AddComponent<CircleCollider2D>();
                float r = Units.W(def.circle[0]);
                cc.radius = r;
                cc.offset = new Vector2(Units.W(def.circle[1]), -Units.W(def.circle[2]));
                cc.density = density;
                cc.sharedMaterial = pm;
                area += Mathf.PI * r * r;
                min = cc.offset - Vector2.one * r; max = cc.offset + Vector2.one * r;
            }
            else if (def.polys != null)
            {
                var pc = gameObject.AddComponent<PolygonCollider2D>();
                pc.pathCount = def.polys.Length;
                for (int k = 0; k < def.polys.Length; k++)
                {
                    var src = def.polys[k];
                    var path = new Vector2[src.Length / 2];
                    for (int q = 0; q < path.Length; q++)
                    {
                        path[q] = new Vector2(Units.W(src[q * 2]), -Units.W(src[q * 2 + 1]));
                        min = Vector2.Min(min, path[q]); max = Vector2.Max(max, path[q]);
                    }
                    pc.SetPath(k, path);
                    area += Mathf.Abs(PolyArea(path));
                }
                pc.density = density;
                pc.sharedMaterial = pm;
            }
            Area = Mathf.Max(0.01f, area);
            Size = max - min;
            // small props are light and get flung fast: continuous collision so a blast can't tunnel them through the terrain edges
            if (Mathf.Min(Size.x, Size.y) < 1.6f) Body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            BuildVisual(def, (min + max) * 0.5f);
            Alive = true;
            BattleWorld.Objects.Add(this);
        }

        static float PolyArea(Vector2[] p)
        {
            float a = 0;
            for (int i = 0; i < p.Length; i++) { var u = p[i]; var v = p[(i + 1) % p.Length]; a += u.x * v.y - v.x * u.y; }
            return a * 0.5f;
        }

        static PhysicsMaterial2D PhysMat(DynamicObjectDef def)
        {
            var key = def.theme + "/" + def.friction + "/" + def.restitution;
            if (physMats.TryGetValue(key, out var m) && m) return m;
            m = new PhysicsMaterial2D("LevelObject_" + def.theme) { friction = def.friction, bounciness = def.restitution };
            physMats[key] = m;
            return m;
        }

        /// <summary>Flat color of a level object material (fallback visuals, debris).</summary>
        public static Color ColorFor(string material)
        {
            switch (material)
            {
                case "Wood": return new Color(0.66f, 0.45f, 0.25f);
                case "Stone": return new Color(0.60f, 0.58f, 0.55f);
                case "Ice": return new Color(0.70f, 0.88f, 0.98f);
                case "Metal": return new Color(0.58f, 0.62f, 0.68f);
                default: return new Color(0.35f, 0.55f, 0.25f);
            }
        }

        // ------------------------------------------------------------------ visuals

        void BuildVisual(DynamicObjectDef def, Vector2 center)
        {
            var root = new GameObject("Visual").transform;
            root.SetParent(transform, false);
            visual = root;
            float depth = Mathf.Clamp(Mathf.Min(Size.x, Size.y), 0.8f, 2.6f);
            string path = "Props/" + LevelObjectId;
            if (ModelLibrary.Exists(path))
            {
                var fit = new GameObject("Fit").transform;
                fit.SetParent(root, false);
                ModelLibrary.Spawn(path, fit);
                FitToBox(fit, root, center, new Vector3(Size.x, Size.y, depth));
            }
            else
            {
                GameObject m;
                if (def.circle != null)
                {
                    m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Destroy(m.GetComponent<Collider>());
                    m.transform.SetParent(root, false);
                    m.transform.localPosition = new Vector3(center.x, center.y, 0);
                    m.transform.localScale = new Vector3(Size.x, Size.y, Size.x);
                }
                else
                {
                    m = new GameObject("Shape");
                    m.transform.SetParent(root, false);
                    ownedMesh = ExtrudedMesh(def, depth);
                    m.AddComponent<MeshFilter>().sharedMesh = ownedMesh;
                    m.AddComponent<MeshRenderer>();
                }
                m.name = path + " (fallback)";
                var r = m.GetComponent<Renderer>();
                r.sharedMaterial = Mats.Toon(materialColor);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            renderers = root.GetComponentsInChildren<Renderer>();
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var sm = renderers[i].sharedMaterial;
                baseColors[i] = sm != null && sm.HasProperty(ColorId) ? sm.GetColor(ColorId) : Color.white;
            }
            mpb = new MaterialPropertyBlock();
        }

        /// <summary>
        /// Scale/move the axis-aligned holder of a model so the model's renderer bounds fill the collider box
        /// (models may use any size, pivot or import rotation).
        /// </summary>
        static void FitToBox(Transform model, Transform root, Vector2 center, Vector3 size)
        {
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;
            model.localScale = Vector3.one;
            var rs = model.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            // bounds in root space (root has identity local transform relative to the object)
            var inv = root.worldToLocalMatrix;
            Bounds b = new Bounds();
            bool first = true;
            foreach (var r in rs)
            {
                var wb = r.bounds;
                var c = inv.MultiplyPoint3x4(wb.center);
                var e = inv.MultiplyVector(wb.extents);
                var lb = new Bounds(c, new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z)) * 2);
                if (first) { b = lb; first = false; } else b.Encapsulate(lb);
            }
            var s = new Vector3(size.x / Mathf.Max(0.01f, b.size.x), size.y / Mathf.Max(0.01f, b.size.y), size.z / Mathf.Max(0.01f, b.size.z));
            model.localScale = s;
            model.localPosition = new Vector3(center.x - b.center.x * s.x, center.y - b.center.y * s.y, -b.center.z * s.z);
        }

        /// <summary>A prism from the collider polygons (front, back and side faces with flat normals).</summary>
        static Mesh ExtrudedMesh(DynamicObjectDef def, float depth)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            float hz = depth * 0.5f;
            foreach (var src in def.polys)
            {
                int c = src.Length / 2;
                var p = new Vector2[c];
                for (int q = 0; q < c; q++) p[q] = new Vector2(Units.W(src[q * 2]), -Units.W(src[q * 2 + 1]));
                bool ccw = PolyArea(p) > 0;
                // front (z = -hz, faces the camera) and back
                for (int side = 0; side < 2; side++)
                {
                    int b = v.Count;
                    float z = side == 0 ? -hz : hz;
                    for (int q = 0; q < c; q++) { v.Add(new Vector3(p[q].x, p[q].y, z)); n.Add(new Vector3(0, 0, side == 0 ? -1 : 1)); }
                    for (int q = 1; q < c - 1; q++)
                    {
                        bool flip = (side == 0) == ccw;
                        t.Add(b); t.Add(flip ? b + q + 1 : b + q); t.Add(flip ? b + q : b + q + 1);
                    }
                }
                // sides
                for (int q = 0; q < c; q++)
                {
                    Vector2 a = p[q], bb = p[(q + 1) % c];
                    Vector2 e = bb - a;
                    Vector3 nn = ccw ? new Vector3(e.y, -e.x, 0).normalized : new Vector3(-e.y, e.x, 0).normalized;
                    int b = v.Count;
                    v.Add(new Vector3(a.x, a.y, -hz)); v.Add(new Vector3(bb.x, bb.y, -hz));
                    v.Add(new Vector3(bb.x, bb.y, hz)); v.Add(new Vector3(a.x, a.y, hz));
                    for (int k = 0; k < 4; k++) n.Add(nn);
                    if (ccw) { t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3); }
                    else { t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b); t.Add(b + 3); t.Add(b + 2); }
                }
            }
            var m = new Mesh { name = "LevelObjectPrism" };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        void ApplyLook()
        {
            if (renderers == null) return;
            float dark = 1f - 0.16f * (DamageStage - 1);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                renderers[i].GetPropertyBlock(mpb);
                var c = baseColors[i] * dark; c.a = baseColors[i].a;
                mpb.SetColor(ColorId, c);
                mpb.SetFloat(FlashId, flash);
                renderers[i].SetPropertyBlock(mpb);
            }
        }

        // ------------------------------------------------------------------ damage

        public void TakeDamage(DamageInfo d)
        {
            if (!Alive) return;
            if (Body != null && d.impulse.sqrMagnitude > 1e-6f)
            {
                d.impulse *= KnockScale(Body.mass);
                Body.WakeUp();
                var b = Size.magnitude * 0.5f + 0.5f;
                if ((d.point - Body.position).sqrMagnitude < b * b) Body.AddForceAtPosition(d.impulse, d.point, ForceMode2D.Impulse);
                else Body.AddForce(d.impulse, ForceMode2D.Impulse);
            }
            if (d.attacker >= 0) { LastAttacker = d.attacker; LastItem = d.itemId; }
            if (Unbreakable || d.amount <= 0) return;
            HP -= d.amount;
            flash = 0.7f;
            if (HP <= 0) { Break(); return; }
            int stage = Mathf.Clamp(3 - Mathf.CeilToInt(HP / Toughness) + 1, 1, 3);
            if (stage != DamageStage)
            {
                DamageStage = stage;
                PlaySound("Damage", 0.8f);
                Fx.Debris(Position, materialColor, 4);
            }
            ApplyLook();
        }

        /// <summary>Knockback multiplier for a prop of this mass (light props fly further, heavy ones barely move).</summary>
        public static float KnockScale(float mass) => Mathf.Clamp(Mathf.Sqrt(KnockRefMass / Mathf.Max(0.01f, mass)), KnockScaleMin, KnockScaleMax);

        /// <summary>Wood, ice and custom objects burn/melt in lava; stone and metal just sink.</summary>
        public bool Burnable => Material == "Wood" || Material == "Ice" || Material == "CustomObjects";

        /// <summary>Destroy with debris, sound and the Destroyed event.</summary>
        public void Break()
        {
            if (!Alive) return;
            Alive = false;
            var pos = Position;
            Fx.Debris(pos, materialColor, Mathf.Clamp(Mathf.RoundToInt(6 + Area * 2), 8, 20));
            Fx.Smoke(pos, Mathf.Clamp(Size.magnitude * 0.5f, 0.6f, 3f));
            PlaySound("End", 1f);
            Destroyed?.Invoke(this, LastAttacker, LastItem);
            Remove();
        }

        /// <summary>Remove silently (snapshot sync, out of the world).</summary>
        public void Remove()
        {
            Alive = false;
            BattleWorld.Objects.Remove(this);
            if (BattleTerrain.I != null) BattleTerrain.I.LevelObjects.Remove(this);
            Destroy(gameObject);
        }

        void PlaySound(string list, float volume)
        {
            var rec = GameData.Get("Sound", Material == "CustomObjects" ? "Wood" : Material);
            if (rec == null) return;
            var l = rec.List(list);
            if (l.Count > 0) AudioManager.SfxPath(l[Random.Range(0, l.Count)], volume);
        }

        void Update()
        {
            if (flash > 0)
            {
                flash = Mathf.Max(0, flash - Time.deltaTime * 6f);
                ApplyLook();
            }
            if (soundCooldown > 0) soundCooldown -= Time.deltaTime;
        }

        void FixedUpdate()
        {
            if (!Alive || Body == null) return;
            var t = BattleTerrain.I;
            if (t == null) return;
            var p = Body.position;
            // out of the world: deep under water or far outside the level
            var lvl = t.Level;
            // lava: burnable props lose HP while their center is under the surface (unbreakable ones too: lava wins)
            if (lvl != null && lvl.IsLava && Burnable && p.y < t.WaterY)
            {
                HP -= LavaBurnPerSecond * Time.fixedDeltaTime;
                burnFx -= Time.fixedDeltaTime;
                if (burnFx <= 0)
                {
                    burnFx = 0.35f;
                    Fx.Smoke(new Vector2(p.x, t.WaterY + 0.3f), Mathf.Clamp(Size.magnitude * 0.35f, 0.5f, 2f));
                    flash = 0.4f;
                }
                if (HP <= 0) { Break(); return; }
                int stage = Mathf.Clamp(3 - Mathf.CeilToInt(HP / Mathf.Max(1f, Toughness)) + 1, 1, 3);
                if (stage != DamageStage) { DamageStage = stage; ApplyLook(); }
            }
            if (p.y < t.WaterY - 20f || p.x < lvl.cameraBounds.xMin - 30f || p.x > lvl.cameraBounds.xMax + 30f)
            {
                Destroyed?.Invoke(this, LastAttacker, LastItem);
                Remove();
            }
        }

        // A walking penguin pushing into the side of the prop: add a small assist force in the walk direction
        // (contact physics already transfers the penguin's momentum; this only gets light props over static friction).
        void OnCollisionStay2D(Collision2D c)
        {
            if (!Alive || Body == null || c.rigidbody == null || PushAssistForce <= 0 || c.contactCount == 0) return;
            var pen = c.rigidbody.GetComponent<Penguin>();
            if (pen == null || !pen.Alive || !pen.Walking || !pen.Grounded) return;
            int dir = pen.WalkDir;
            if (dir == 0 || (Body.position.x - c.rigidbody.position.x) * dir <= 0) return;   // only the prop in front
            if (Mathf.Abs(c.GetContact(0).normal.x) < 0.6f) return;   // side contact, not standing on / under it
            if (Body.Vel().x * dir > 4f) return;                        // already moving along
            Body.AddForce(new Vector2(dir * PushAssistForce, 0));
        }

        void OnCollisionEnter2D(Collision2D c)
        {
            if (soundCooldown > 0 || c.relativeVelocity.sqrMagnitude < CollisionSoundSpeed * CollisionSoundSpeed) return;
            soundCooldown = 0.3f;
            PlaySound("Collision", Mathf.Clamp01(c.relativeVelocity.magnitude / 15f) * 0.7f);
        }

        void OnDestroy()
        {
            BattleWorld.Objects.Remove(this);
            if (ownedMesh) Destroy(ownedMesh);
        }

        // ------------------------------------------------------------------ snapshots

        public DynamicObjectState Capture()
        {
            var p = Position;
            return new DynamicObjectState { id = Id, x = p.x, y = p.y, angle = Body ? Body.rotation : transform.eulerAngles.z, hp = HP, alive = Alive };
        }

        public void Apply(DynamicObjectState s)
        {
            if (s == null) return;
            if (Body)
            {
                Body.position = new Vector2(s.x, s.y);
                Body.rotation = s.angle;
                Body.SetVel(Vector2.zero);
                Body.angularVelocity = 0;
            }
            transform.SetPositionAndRotation(new Vector3(s.x, s.y, 0), Quaternion.Euler(0, 0, s.angle));
            HP = s.hp;
            DamageStage = Mathf.Clamp(3 - Mathf.CeilToInt(Mathf.Max(0.01f, HP) / Toughness) + 1, 1, 3);
            ApplyLook();
            if (!s.alive) Remove();
        }
    }
}
