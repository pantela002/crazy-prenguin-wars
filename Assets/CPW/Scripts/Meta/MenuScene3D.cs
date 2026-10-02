using UnityEngine;

namespace CPW
{
    /// <summary>
    /// The 3D backdrop of the home and wardrobe screens: a snowy hill with icebergs, pine trees, a snowman, crates
    /// and ice blocks (the Blender Env/Props models, primitive fallbacks when missing), distant snowy peaks, clouds,
    /// a sun glow and falling snow, plus the player's penguin (PenguinAvatar) wearing their clothes on an ice floe. It sits in front of GameManager.MenuCamera
    /// and is hidden whenever a battle starts so battle cameras never see it.
    /// </summary>
    public class MenuScene3D : MonoBehaviour
    {
        public enum Layout { Home, Wardrobe }

        static MenuScene3D inst;
        PenguinAvatar penguin;
        Transform pivot;
        float spin, targetSpin, idleT, celebrateUntil;
        Vector3 targetPos;
        string lastHead, lastChest, lastFeet;

        /// <summary>Show the scene with the penguin placed for the given screen.</summary>
        public static void Show(Layout layout)
        {
            if (GameManager.I == null || GameManager.I.MenuCamera == null) return;
            if (inst == null) Create();
            inst.gameObject.SetActive(true);
            inst.targetPos = layout == Layout.Home ? new Vector3(0f, 0.47f, 0) : new Vector3(-3.1f, -1.55f, -0.8f);
            inst.pivot.localPosition = inst.targetPos;
            inst.targetSpin = 0;
            if (inst.penguin != null) inst.penguin.SetFacing(1);
            inst.RefreshClothes(true);
        }

        public static void Hide()
        {
            if (inst != null) inst.gameObject.SetActive(false);
        }

        /// <summary>Rotate the penguin (wardrobe drag).</summary>
        public static void Rotate(float deg) { if (inst != null) inst.targetSpin += deg; }

        /// <summary>Little happy jump (after buying/equipping).</summary>
        public static void Celebrate()
        {
            if (inst == null || inst.penguin == null) return;
            inst.celebrateUntil = Time.unscaledTime + 1.6f;
            inst.penguin.SetState(AvatarState.Celebrate);
        }

        /// <summary>Re-dress the penguin from the profile.</summary>
        public static void UpdateClothes() { if (inst != null) inst.RefreshClothes(true); }

        static void Create()
        {
            var cam = GameManager.I.MenuCamera;
            var go = new GameObject("MenuScene3D");
            go.transform.SetParent(GameManager.I.transform, false);
            inst = go.AddComponent<MenuScene3D>();
            // the scene is laid out relative to the camera (camera at z=-10 looking +z)
            go.transform.position = cam.transform.position + new Vector3(0, -1.2f, 10f);

            var light = new GameObject("MenuLight").AddComponent<Light>();
            light.transform.SetParent(go.transform, false);
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.97f, 0.92f);
            light.transform.rotation = Quaternion.Euler(40, -30, 0);
            light.shadows = LightShadows.None;

            inst.BuildBackdrop(go.transform);

            inst.pivot = new GameObject("PenguinPivot").transform;
            inst.pivot.SetParent(go.transform, false);
            // the penguin stands on a small ice disc with a soft contact shadow (HomeStage draws the podium around it)
            var floe = Prim(PrimitiveType.Cylinder, inst.pivot, Mats.Toon(new Color(0.8f, 0.93f, 1f)));
            floe.name = "Floe";
            floe.transform.localPosition = new Vector3(0, -0.1f, 0);
            floe.transform.localScale = new Vector3(1.5f, 0.1f, 1.2f);
            var floeTop = Prim(PrimitiveType.Cylinder, inst.pivot, Mats.Toon(new Color(0.97f, 0.99f, 1f)));
            floeTop.name = "FloeSnow";
            floeTop.transform.localPosition = new Vector3(0.03f, 0.0f, 0.02f);
            floeTop.transform.localScale = new Vector3(1.32f, 0.03f, 1.04f);
            var shadow = Prim(PrimitiveType.Quad, inst.pivot, Mats.TransparentTex(Mats.SoftCircle, new Color(0.15f, 0.25f, 0.4f, 0.45f)));
            shadow.name = "Shadow";
            shadow.transform.localPosition = new Vector3(0.1f, 0.04f, 0);
            shadow.transform.localRotation = Quaternion.Euler(90, 0, 0);
            shadow.transform.localScale = new Vector3(1.3f, 1.0f, 1f);
            inst.penguin = PenguinAvatar.Create(inst.pivot, 2.6f, Theme.PlayerColors[0]);
            if (inst.penguin != null) inst.penguin.SetState(AvatarState.Idle);
            // The Blender model already faces the camera turned 40 degrees toward +X (Blender/scripts/penguin.py YAW),
            // which looks right for the menus: on home it turns toward the Play button.
        }

        void BuildBackdrop(Transform root)
        {
            // sky: a big gradient quad far away, with a soft sun glow
            var sky = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(sky.GetComponent<Collider>());
            sky.name = "Sky";
            sky.transform.SetParent(root, false);
            sky.transform.localPosition = new Vector3(0, 6, 40);
            sky.transform.localScale = new Vector3(110, 50, 1);
            sky.GetComponent<Renderer>().sharedMaterial = Mats.UnlitTex(MetaUI.Gradient.texture, Color.white);
            var sun = Prim(PrimitiveType.Quad, root, Mats.AdditiveTex(Mats.SoftCircle, new Color(1f, 0.96f, 0.85f, 0.5f)));
            sun.name = "SunGlow";
            sun.transform.localPosition = new Vector3(9f, 9f, 39f);
            sun.transform.localScale = new Vector3(26f, 26f, 1f);

            // distant snowy peaks and hills (no outline that far), primitives as fallback
            if (!SpawnEnv(root, "Env/Mountain_Peak1", new Vector3(-17f, -1.5f, 30f), 15f, 10f, false) |
                !SpawnEnv(root, "Env/Mountain_Peak2", new Vector3(16f, -1.5f, 32f), 12f, -12f, false) |
                !SpawnEnv(root, "Env/Mountain_Peak1", new Vector3(2f, -1.5f, 36f), 11f, 180f, false))
            {
                var mtnCol = new Color(0.78f, 0.86f, 0.96f);
                for (int i = 0; i < 7; i++)
                {
                    var m = Prim(PrimitiveType.Cube, root, Mats.Unlit(Color.Lerp(mtnCol, new Color(0.6f, 0.72f, 0.9f), (i % 3) / 2f)));
                    float x = -26 + i * 9f;
                    float h = 6 + (i * 37 % 5);
                    m.transform.localPosition = new Vector3(x, 0.5f, 28);
                    m.transform.localRotation = Quaternion.Euler(0, 0, 45);
                    m.transform.localScale = new Vector3(h, h, 1);
                }
            }
            SpawnEnv(root, "Env/Winter_Hills1", new Vector3(-10f, -2.2f, 20f), 5f, 0f, false);
            SpawnEnv(root, "Env/Winter_Hills1", new Vector3(12f, -2.2f, 22f), 4.5f, 160f, false);
            SpawnEnv(root, "Env/Winter_Cloud1", new Vector3(-12f, 7.5f, 30f), 3.2f, 0f, false);
            SpawnEnv(root, "Env/Winter_Cloud1", new Vector3(7f, 9.5f, 33f), 2.6f, 180f, false);
            SpawnEnv(root, "Env/Winter_Cloud1", new Vector3(20f, 6.5f, 31f), 2.2f, 20f, false);

            // snowy ground: a huge flattened sphere
            var ground = Prim(PrimitiveType.Sphere, root, Mats.Toon(new Color(0.93f, 0.97f, 1f)));
            ground.transform.localPosition = new Vector3(0, -21.6f, 8);
            ground.transform.localScale = new Vector3(70, 40, 40);

            // icebergs and ice blocks
            var ice = Mats.Toon(new Color(0.62f, 0.86f, 1f));
            if (!SpawnEnv(root, "Env/Winter_Iceberg1", new Vector3(-7.5f, -1.75f, 6f), 3.2f, 20f, true))
                PlaceBlock(root, ice, new Vector3(-6.5f, -1.3f, 3f), new Vector3(1.6f, 1.4f, 1.4f), 20);
            if (!SpawnEnv(root, "Props/CubeMediumIce", new Vector3(-5.3f, -1.6f, 2.2f), 1.0f, 25f, true))
                PlaceBlock(root, ice, new Vector3(-5.3f, -1.5f, 2.2f), new Vector3(0.8f, 0.8f, 0.8f), 40);
            if (!SpawnEnv(root, "Env/Winter_Iceberg1", new Vector3(7f, -1.75f, 6.5f), 3.6f, -15f, true))
                PlaceBlock(root, ice, new Vector3(6.2f, -1.2f, 4f), new Vector3(1.8f, 1.8f, 1.6f), -15);
            if (!SpawnEnv(root, "Props/CubeSmallIce", new Vector3(4.8f, -1.7f, 1.2f), 0.6f, 10f, true))
                PlaceBlock(root, ice, new Vector3(4.8f, -1.6f, 1.2f), new Vector3(0.6f, 0.6f, 0.6f), 10);
            SpawnEnv(root, "Props/Crate", new Vector3(-4.4f, -1.25f, 3.6f), 1.1f, -20f, true, "Parachute");
            SpawnEnv(root, "Props/CubeSmallIce", new Vector3(-4.6f, -0.45f, 3.7f), 0.55f, 35f, true);

            // pine trees
            if (!SpawnEnv(root, "Env/Winter_PineSnow1", new Vector3(-9f, -1.8f, 7f), 4.2f, 0f, true)) PlaceTree(root, new Vector3(-9f, -1.6f, 7f), 1.3f);
            if (!SpawnEnv(root, "Env/Winter_PineSnow2", new Vector3(-3.6f, -1.6f, 9f), 3.2f, 40f, true)) PlaceTree(root, new Vector3(-3.6f, -1.4f, 9f), 1.0f);
            if (!SpawnEnv(root, "Env/Winter_PineSnow1", new Vector3(8.5f, -1.7f, 7.5f), 4.5f, 70f, true)) PlaceTree(root, new Vector3(8.5f, -1.5f, 7.5f), 1.4f);
            if (!SpawnEnv(root, "Env/Winter_PineSnow2", new Vector3(3.5f, -1.6f, 10f), 2.9f, 10f, true)) PlaceTree(root, new Vector3(3.5f, -1.4f, 10f), 0.9f);
            SpawnEnv(root, "Env/Winter_PineSnow2", new Vector3(12f, -1.8f, 12f), 3.4f, 120f, true);
            SpawnEnv(root, "Env/Winter_PineSnow1", new Vector3(-13f, -1.9f, 13f), 4.0f, 200f, true);

            // a snowman watching the action
            if (!SpawnEnv(root, "Env/Winter_Snowman1", new Vector3(3.2f, -1.75f, 3.5f), 2.3f, -25f, true))
            {
                var snow = Mats.Toon(Color.white);
                var b1 = Prim(PrimitiveType.Sphere, root, snow); b1.transform.localPosition = new Vector3(3.2f, -1.2f, 3.5f); b1.transform.localScale = Vector3.one * 1.1f;
                var b2 = Prim(PrimitiveType.Sphere, root, snow); b2.transform.localPosition = new Vector3(3.2f, -0.35f, 3.5f); b2.transform.localScale = Vector3.one * 0.75f;
                var b3 = Prim(PrimitiveType.Sphere, root, snow); b3.transform.localPosition = new Vector3(3.2f, 0.25f, 3.5f); b3.transform.localScale = Vector3.one * 0.5f;
                var nose = Prim(PrimitiveType.Capsule, root, Mats.Toon(new Color(1f, 0.55f, 0.1f)));
                nose.transform.localPosition = new Vector3(3.15f, 0.25f, 3.2f); nose.transform.localRotation = Quaternion.Euler(90, 0, 0); nose.transform.localScale = new Vector3(0.08f, 0.15f, 0.08f);
            }

            // falling snow
            var psGo = new GameObject("Snowfall");
            psGo.transform.SetParent(root, false);
            psGo.transform.localPosition = new Vector3(0, 9, 6);
            var ps = psGo.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 9f;
            main.startSpeed = 1.2f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.18f);
            main.startColor = new Color(1, 1, 1, 0.9f);
            main.maxParticles = 300;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            var em = ps.emission; em.rateOverTime = 28;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(28, 1, 14);
            psGo.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.4f; noise.frequency = 0.3f;
            var r = psGo.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Mats.TransparentTex(Mats.SoftCircle, Color.white);
            ps.Play();
        }

        /// <summary>
        /// Place a Blender model standing on pos (bottom center), scaled to height, turned yaw degrees.
        /// outline false drops the ink outline (distant scenery). Children named hideChild are removed.
        /// Returns false (and places nothing) when the model is missing.
        /// </summary>
        static bool SpawnEnv(Transform root, string path, Vector3 pos, float height, float yaw, bool outline, string hideChild = null)
        {
            if (!ModelLibrary.Exists(path)) return false;
            var holder = new GameObject(path).transform;
            holder.SetParent(root, false);
            holder.localPosition = pos;
            holder.localRotation = Quaternion.Euler(0, yaw, 0);
            var go = ModelLibrary.Spawn(path, holder);
            if (!string.IsNullOrEmpty(hideChild))
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    if (t != go.transform && t.name.StartsWith(hideChild)) t.gameObject.SetActive(false);
            if (!outline) Mats.ApplyToon(go, false);
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return true;
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            float s = height / Mathf.Max(0.05f, b.size.y);
            var lc = holder.InverseTransformPoint(b.center);
            var lmin = holder.InverseTransformPoint(b.min);
            go.transform.localScale = Vector3.one * s;
            go.transform.localPosition = new Vector3(-lc.x * s, -lmin.y * s, -lc.z * s);
            foreach (var rr in rs)
            {
                rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rr.receiveShadows = false;
            }
            return true;
        }

        static GameObject Prim(PrimitiveType t, Transform parent, Material m)
        {
            var g = GameObject.CreatePrimitive(t);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return g;
        }

        static void PlaceBlock(Transform root, Material m, Vector3 pos, Vector3 scale, float yaw)
        {
            var b = Prim(PrimitiveType.Cube, root, m);
            b.transform.localPosition = pos;
            b.transform.localScale = scale;
            b.transform.localRotation = Quaternion.Euler(8, yaw, 6);
        }

        static void PlaceTree(Transform root, Vector3 pos, float s)
        {
            var trunk = Prim(PrimitiveType.Cylinder, root, Mats.Toon(new Color(0.45f, 0.28f, 0.15f)));
            trunk.transform.localPosition = pos + new Vector3(0, 0.3f * s, 0);
            trunk.transform.localScale = new Vector3(0.25f, 0.35f, 0.25f) * s;
            var green = Mats.Toon(new Color(0.16f, 0.5f, 0.32f));
            var cap = Mats.Toon(Color.white);
            for (int i = 0; i < 3; i++)
            {
                float w = (1.6f - i * 0.45f) * s;
                var leaf = Prim(PrimitiveType.Sphere, root, green);
                leaf.transform.localPosition = pos + new Vector3(0, (0.9f + i * 0.75f) * s, 0);
                leaf.transform.localScale = new Vector3(w, 0.9f * s, w);
                var top = Prim(PrimitiveType.Sphere, root, cap);
                top.transform.localPosition = pos + new Vector3(0, (1.15f + i * 0.75f) * s, 0);
                top.transform.localScale = new Vector3(w * 0.7f, 0.35f * s, w * 0.7f);
            }
        }

        void RefreshClothes(bool force)
        {
            if (penguin == null) return;
            var P = ProfileService.P;
            if (!force && P.wornHead == lastHead && P.wornChest == lastChest && P.wornFeet == lastFeet) return;
            lastHead = P.wornHead; lastChest = P.wornChest; lastFeet = P.wornFeet;
            penguin.SetClothes(P.wornHead, P.wornChest, P.wornFeet);
        }

        void OnEnable() { ProfileService.Changed += OnProfileChanged; BattleEvents.BattleStarted += OnBattle; }
        void OnDisable() { ProfileService.Changed -= OnProfileChanged; BattleEvents.BattleStarted -= OnBattle; }
        void OnProfileChanged() => RefreshClothes(false);
        void OnBattle(BattleConfig c) => Hide();

        void Update()
        {
            if (pivot == null) return;
            float dt = Time.unscaledDeltaTime;
            idleT += dt;
            // ease rotation toward the target and drift back to facing the camera when idle
            spin = Mathf.Lerp(spin, targetSpin, 1 - Mathf.Exp(-8 * dt));
            targetSpin = Mathf.Lerp(targetSpin, Mathf.Sin(idleT * 0.4f) * 12f, 1 - Mathf.Exp(-0.6f * dt));
            pivot.localRotation = Quaternion.Euler(0, spin, 0);
            if (celebrateUntil > 0 && Time.unscaledTime > celebrateUntil)
            {
                celebrateUntil = 0;
                if (penguin != null) penguin.SetState(AvatarState.Idle);
            }
            // tapping the penguin on the home screen makes it cheer
            if (Input.GetMouseButtonDown(0) && penguin != null && GameManager.I != null && GameManager.I.MenuCamera != null)
            {
                var sp = GameManager.I.MenuCamera.WorldToScreenPoint(pivot.position + Vector3.up * 1.3f);
                if (sp.z > 0 && Vector2.Distance(sp, Input.mousePosition) < Screen.height * 0.12f) Celebrate();
            }
        }
    }
}
