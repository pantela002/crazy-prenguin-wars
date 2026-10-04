using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    public enum AvatarState { Idle, Walk, Jump, Fall, Aim, Fire, Hurt, Dead, Celebrate, Drown, Sad }

    /// <summary>
    /// The player's penguin with clothes, gloves, a skin, animation, a held weapon, emotes and a team colour.
    /// Used by battles (Battle/Penguin) and menus (home screen, wardrobe).
    ///
    /// The look is the textured 3D penguin from Blender (Blender/scripts/penguin.py, clothes.py; textures applied by
    /// WearTextures): a simple cartoon bird whose fixed sockets carry hats, outfits, shoes and gloves through every
    /// animation, and whose skin is a texture swap. The held weapon is the textured Blender weapon model
    /// (Resources/Models/Weapons, grip at the origin, barrel +X, "Muzzle" empty at the tip) in the right flipper, with
    /// its own draw / aim / recoil / throw animation; the ORIGINAL Flash weapon clip is only the fallback when a model
    /// is missing (or with <see cref="Prefer3DWeapons"/> off). The original 2D sprite penguin (PenguinSprite) is still
    /// available behind <see cref="UseOriginalSprite"/>.
    ///
    /// Model contract (see Docs/ART.md): parts Body, Head, Beak, EyeL/R, PupilL/R, FlipperL/R, FootL/R and empties
    /// HeadSocket, ChestSocket, Belly, FootSocketL/R, HandSocket, GloveSocketL/R, all exported as root objects with origins
    /// at their joints; the hierarchy is rebuilt here. The model is 2.6 units tall, feet at the origin, beak towards +X,
    /// belly towards -Z. Everything falls back to primitives when the FBX (or a part of it) is missing.
    ///
    /// Transform layout: this (unscaled; HeadTop, emote bubble, team ring) / Facing (mirror + size) / Pose (whole-body
    /// pose) / model.
    /// </summary>
    public class PenguinAvatar : MonoBehaviour
    {
        public const float ModelHeight = 2.6f;
        /// <summary>Battle size: the original Flash penguin's height (PenguinSprite.NaturalHeight), so hit boxes, HUD anchors,
        /// the original weapon clips and effects keep the proportions of the original game.</summary>
        public static float BattleHeight => PenguinSprite.NaturalHeight;
        /// <summary>Show the original 2D sprite penguin instead of the 3D one (when its art is imported).</summary>
        public static bool UseOriginalSprite = false;
        /// <summary>Hold the textured 3D weapon models (true) or the original 2D Flash weapon clips (false). Either one
        /// falls back to the other when it is missing.</summary>
        public static bool Prefer3DWeapons = true;
        /// <summary>Held model size relative to its Blender size (the models are made for the 2.6 unit penguin).</summary>
        public const float WeaponScale = 1.1f;

        public AvatarState State { get; private set; }
        public int Facing { get; private set; } = 1;
        /// <summary>World-space point at the tip of the held weapon (where projectiles spawn). Follows the aim.</summary>
        public Transform Muzzle { get; private set; }
        /// <summary>Above the head (name tag / HP bar / emote bubble anchor).</summary>
        public Transform HeadTop { get; private set; }
        public float Height { get; private set; }
        public Color TeamColor { get; private set; } = Color.white;
        /// <summary>Aim angle in degrees relative to the facing direction (0 = forward, 90 = up).</summary>
        public float AimDegrees => aimTarget;
        public string HeldWeapon { get; private set; }
        /// <summary>Worn gloves (ClothesSlot.Hands id) and skin (ClothesSlot.Skin id, "" = classic).</summary>
        public string Gloves { get; private set; } = "";
        public string Skin { get; private set; } = "";
        /// <summary>True when the original 2D sprite penguin is shown (false = the 3D penguin).</summary>
        public bool IsSprite => sprite != null;
        /// <summary>Play the spawn pop (sprite: the original "spawn" animation) whenever the avatar is re-enabled
        /// (respawn). Off in menus.</summary>
        public bool PlaySpawnOnEnable = true;
        /// <summary>Animate with unscaled time (menus).</summary>
        public bool UnscaledTime;
        /// <summary>Team colour ellipse under the feet. Off in menus.</summary>
        public bool ShowTeamRing
        {
            get => showRing;
            set
            {
                showRing = value;
                if (sprite != null) sprite.ShowRing = value;
                if (ring != null) ring.enabled = value;
            }
        }
        bool showRing = true;
        PenguinSprite sprite;
        SpriteAnim emoteAnim;
        SpriteRenderer ring;

        // ------------------------------------------------------------------ parts
        class Part
        {
            public Transform t;
            public Vector3 pos, scale;
            public Quaternion rot;
            public Part(Transform tr) { t = tr; pos = tr.localPosition; rot = tr.localRotation; scale = tr.localScale; }
        }

        // child -> parent (mirrors Blender/scripts/penguin.py HIERARCHY)
        static readonly string[,] Hierarchy =
        {
            { "Head", "Body" }, { "FlipperL", "Body" }, { "FlipperR", "Body" }, { "ChestSocket", "Body" }, { "Belly", "Body" },
            { "Beak", "Head" }, { "EyeL", "Head" }, { "EyeR", "Head" }, { "HeadSocket", "Head" },
            { "PupilL", "EyeL" }, { "PupilR", "EyeR" }, { "HandSocket", "FlipperR" },
            { "GloveSocketL", "FlipperL" }, { "GloveSocketR", "FlipperR" },
            { "FootSocketL", "FootL" }, { "FootSocketR", "FootR" },
        };

        Transform facingNode, poseNode, model;
        Part body, head, flipL, flipR, footL, footR, eyeL, eyeR, pupilL, pupilR, beak;
        Transform tip;                         // weapon tip in holder space at the rest pose (Muzzle: AimedTipPose)
        Transform headSocket, chestSocket, footSocketL, footSocketR, handSocket, gloveSocketL, gloveSocketR, weaponHolder;
        float restFlipR = -100f;               // rest direction of the right flipper (shoulder -> tip), degrees in the XY plane
        readonly List<Renderer> renderers = new List<Renderer>();
        MaterialPropertyBlock mpb;
        static readonly int FlashId = Shader.PropertyToID("_Flash");

        // held weapon: the 3D model on weaponRig (draw / recoil / throw offsets), or the original clip on a SpriteRenderer
        SpriteRenderer weaponSr;
        Transform weaponRig;
        readonly Dictionary<string, GameObject> weaponPool = new Dictionary<string, GameObject>();
        bool uprightItem;                      // thrown items (grenades, bottles, eggs) stay upright in the flipper
        bool gunLike;                          // recoil on fire (guns, launchers, melee swing)
        float drawT = 1f, throwT = -1f;
        // rest pose (pose space) of the right shoulder and the hand, and the hand socket's scale in pose units: the
        // muzzle is computed from these for the aimed pose, so it does not wobble with idle / walk / recoil animation
        Vector3 shoulderRest, handRest;
        float handScale = 1f;
        readonly SpriteAnimPlayer weaponPlayer = new SpriteAnimPlayer();
        string hold;
        bool allowRotation = true;
        System.Action onWeaponFired;

        // ------------------------------------------------------------------ animation state
        float t, stateTime, flash, blinkTimer = 2f, blinkPhase = -1f, landSquash, fireKick, spawnPop = -1f;
        float aimTarget, flipRCurrent = float.NaN;
        AvatarState prevState;
        GameObject weapon, headWear, chestWear, feetWearL, feetWearR, gloveWear, gloveLeft, ghost, bubble;
        float bubbleUntil, bubbleStart;
        readonly System.Random rng = new System.Random();

        /// <summary>Create an avatar under parent. height = world units from feet to head top.</summary>
        public static PenguinAvatar Create(Transform parent, float height, Color teamColor)
        {
            var go = new GameObject("PenguinAvatar");
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<PenguinAvatar>();
            a.Build(height);
            a.SetTeamColor(teamColor);
            return a;
        }

        void Build(float height)
        {
            Height = Mathf.Max(0.1f, height);
            if (UseOriginalSprite && PenguinSprite.Available) { BuildSprite(); return; }
            mpb = new MaterialPropertyBlock();
            onWeaponFired = HoldWeaponPose;
            facingNode = new GameObject("Facing").transform;
            facingNode.SetParent(transform, false);
            poseNode = new GameObject("Pose").transform;
            poseNode.SetParent(facingNode, false);
            ApplyFacingScale();

            var m = ModelLibrary.Prefab("Penguin/Penguin") != null ? ModelLibrary.Spawn("Penguin/Penguin", poseNode) : null;
            if (m != null && FindDeep(m.transform, "Body") != null)
            {
                model = m.transform;
                WearTextures.Apply(m, ClothesCatalog.SkinTexture(Skin));
            }
            else
            {
                if (m != null) Destroy(m);
                model = BuildFallbackModel(poseNode).transform;
            }
            RebuildHierarchy();

            HeadTop = new GameObject("HeadTop").transform;
            HeadTop.SetParent(transform, false);
            HeadTop.localPosition = new Vector3(0, Height * 1.05f, 0);

            weaponHolder = new GameObject("WeaponHolder").transform;
            weaponHolder.SetParent(handSocket, false);
            weaponHolder.localRotation = Quaternion.AngleAxis(restFlipR, Vector3.forward);
            weaponRig = new GameObject("WeaponRig").transform;
            weaponRig.SetParent(weaponHolder, false);
            shoulderRest = flipR != null ? poseNode.InverseTransformPoint(flipR.t.position) : new Vector3(-0.45f, 1.3f, -0.3f);
            handRest = poseNode.InverseTransformPoint(handSocket.position);
            handScale = Mathf.Max(0.05f, poseNode.InverseTransformVector(handSocket.TransformVector(Vector3.right)).magnitude);
            ResetRig();
            weaponSr = new GameObject("WeaponClip").AddComponent<SpriteRenderer>();
            weaponSr.transform.SetParent(weaponHolder, false);
            // the clips are drawn for the original penguin (NaturalHeight tall at scale 1); in front of the flipper
            weaponSr.transform.localScale = Vector3.one * (ModelHeight / PenguinSprite.NaturalHeight);
            weaponSr.transform.localPosition = new Vector3(0, 0, -0.3f);
            weaponSr.sortingOrder = PenguinSprite.SortingOrder + 1;
            tip = new GameObject("WeaponTip").transform;
            tip.SetParent(weaponHolder, false);
            tip.localPosition = new Vector3(0.25f, 0, 0);
            Muzzle = new GameObject("Muzzle").transform;
            Muzzle.SetParent(transform, false);

            ring = new GameObject("TeamRing").AddComponent<SpriteRenderer>();
            ring.transform.SetParent(transform, false);
            ring.sprite = PenguinSprite.RingSprite;
            float k = Height / PenguinSprite.NaturalHeight;
            ring.transform.localPosition = new Vector3(0.05f * k, 0.05f * k, 0.45f * k);   // behind the feet
            ring.transform.localScale = Vector3.one * k;
            ring.sortingOrder = PenguinSprite.SortingOrder - 1;
            ring.enabled = showRing;

            UpdateMuzzle();
            RefreshRenderers();
            SetState(AvatarState.Idle);
        }

        void BuildSprite()
        {
            sprite = new PenguinSprite(this, Height);
            sprite.ShowRing = showRing;
            HeadTop = new GameObject("HeadTop").transform;
            HeadTop.SetParent(transform, false);
            HeadTop.localPosition = new Vector3(0, Height * 1.05f, 0);
            Muzzle = new GameObject("Muzzle").transform;
            Muzzle.SetParent(transform, false);
            SetState(AvatarState.Idle);
            UpdateMuzzle();
        }

        void OnEnable()
        {
            if (!PlaySpawnOnEnable) return;
            if (sprite != null) sprite.PlaySpawn();
            else if (poseNode != null) spawnPop = 0f;
        }

        void ApplyFacingScale()
        {
            float s = Height / ModelHeight;
            facingNode.localScale = new Vector3(s * Facing, s, s);
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindDeep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        static Transform FindPrefix(Transform root, string prefix)
        {
            if (root.name.StartsWith(prefix)) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindPrefix(root.GetChild(i), prefix);
                if (r != null) return r;
            }
            return null;
        }

        Transform Find(string name) => FindDeep(model, name);

        /// <summary>Re-parent the flat FBX parts into the animation hierarchy and record rest poses.</summary>
        void RebuildHierarchy()
        {
            var bodyT = Find("Body");
            // missing sockets/parts get an empty at a sensible place so nothing downstream breaks
            EnsurePart("HeadSocket", "Head", new Vector3(0, 1.98f, 0));
            EnsurePart("ChestSocket", "Body", new Vector3(0, 0.95f, 0));
            EnsurePart("Belly", "Body", new Vector3(0.42f, 0.85f, -0.5f));
            EnsurePart("FootSocketL", "FootL", new Vector3(0.25f, 0.12f, 0.2f));
            EnsurePart("FootSocketR", "FootR", new Vector3(-0.2f, 0.12f, -0.25f));
            EnsurePart("HandSocket", "FlipperR", new Vector3(-0.6f, 0.6f, -0.69f));
            EnsurePart("GloveSocketR", "FlipperR", new Vector3(-0.56f, 0.78f, -0.6f));
            EnsurePart("GloveSocketL", "FlipperL", new Vector3(0.62f, 0.78f, 0.38f));
            for (int i = 0; i < Hierarchy.GetLength(0); i++)
            {
                var c = Find(Hierarchy[i, 0]);
                var p = Find(Hierarchy[i, 1]);
                if (c != null && p != null && c.parent != p) c.SetParent(p, true);
            }
            body = P("Body"); head = P("Head"); beak = P("Beak");
            flipL = P("FlipperL"); flipR = P("FlipperR"); footL = P("FootL"); footR = P("FootR");
            eyeL = P("EyeL"); eyeR = P("EyeR"); pupilL = P("PupilL"); pupilR = P("PupilR");
            headSocket = Find("HeadSocket"); chestSocket = Find("ChestSocket");
            footSocketL = Find("FootSocketL"); footSocketR = Find("FootSocketR"); handSocket = Find("HandSocket");
            gloveSocketL = Find("GloveSocketL"); gloveSocketR = Find("GloveSocketR");
            if (flipR != null && handSocket != null)
            {
                Vector3 a = poseNode.InverseTransformPoint(flipR.t.position), b = poseNode.InverseTransformPoint(handSocket.position);
                Vector2 d = new Vector2(b.x - a.x, b.y - a.y);
                if (d.sqrMagnitude > 1e-6f) restFlipR = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            }
            if (bodyT == null) Debug.LogWarning("PenguinAvatar: model has no Body part");
        }

        void EnsurePart(string name, string parentName, Vector3 modelPos)
        {
            if (Find(name) != null) return;
            var e = new GameObject(name).transform;
            e.SetParent(model, false);
            e.localPosition = modelPos;
            var p = Find(parentName);
            if (p != null) e.SetParent(p, true);
        }

        Part P(string name)
        {
            var tr = Find(name);
            return tr != null ? new Part(tr) : null;
        }

        // ------------------------------------------------------------------ fallback model

        static Vector3 C2U(float x, float y, float z)
        {
            // canonical Blender frame (facing -Y) turned 40 degrees toward +X, then Blender (x, y, z) -> Unity (x, z, y)
            const float a = 40f * Mathf.Deg2Rad;
            float gx = x * Mathf.Cos(a) - y * Mathf.Sin(a), gy = x * Mathf.Sin(a) + y * Mathf.Cos(a);
            return new Vector3(gx, z, gy);
        }

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Color c)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col) Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Mats.Toon(c);
            return go;
        }

        /// <summary>A primitive penguin with the same part names/pivots as the Blender model.</summary>
        static GameObject BuildFallbackModel(Transform parent)
        {
            var root = new GameObject("Penguin (fallback)");
            root.transform.SetParent(parent, false);
            var black = new Color(0.11f, 0.23f, 0.29f);
            var white = new Color(0.96f, 0.97f, 0.98f);
            var orange = new Color(1f, 0.65f, 0.12f);
            Transform Pivot(string name, Vector3 pos)
            {
                var t = new GameObject(name).transform;
                t.SetParent(root.transform, false);
                t.localPosition = pos;
                return t;
            }
            var bodyP = Pivot("Body", new Vector3(0, 0.1f, 0));
            Prim(PrimitiveType.Sphere, "BodyMesh", bodyP, new Vector3(0, 0.82f, 0), new Vector3(1.4f, 1.72f, 1.25f), black);
            Prim(PrimitiveType.Sphere, "BellyMesh", bodyP, C2U(0, -0.3f, 0.76f) - new Vector3(0, 0.1f, 0), new Vector3(1.1f, 1.3f, 0.9f), white);
            var headP = Pivot("Head", C2U(0, 0, 1.5f));
            Prim(PrimitiveType.Sphere, "HeadMesh", headP, C2U(0, -0.02f, 1.98f) - C2U(0, 0, 1.5f), Vector3.one * 1.14f, black);
            var beakP = Pivot("Beak", C2U(0, -0.5f, 1.86f));
            var bk = Prim(PrimitiveType.Sphere, "BeakMesh", beakP, C2U(0, -0.24f, -0.01f), new Vector3(0.5f, 0.2f, 0.74f), orange);
            bk.transform.localRotation = Quaternion.Euler(0, -40f, 0);
            foreach (var s in new[] { 1, -1 })
            {
                string side = s > 0 ? "L" : "R";
                var ec = C2U(0.19f * s, -0.4f, 2.25f);
                var eye = Pivot("Eye" + side, ec);
                Prim(PrimitiveType.Sphere, "EyeMesh", eye, Vector3.zero, new Vector3(0.34f, 0.46f, 0.26f), white);
                var pup = Pivot("Pupil" + side, ec);
                Prim(PrimitiveType.Sphere, "PupilMesh", pup, C2U(0.045f, -0.1f, -0.015f), new Vector3(0.18f, 0.24f, 0.1f), Color.black);
                var sh = C2U(0.6f * s, -0.02f, 1.3f);
                var fl = Pivot("Flipper" + side, sh);
                var tip = C2U(0.9f * s, -0.14f, 0.6f);
                var fm = Prim(PrimitiveType.Sphere, "FlipperMesh", fl, (tip - sh) * 0.5f, new Vector3(0.22f, 0.95f, 0.5f), black);
                fm.transform.localRotation = Quaternion.FromToRotation(Vector3.up, tip - sh);
                var ank = C2U(0.3f * s, -0.05f, 0.12f);
                var ft = Pivot("Foot" + side, ank);
                Prim(PrimitiveType.Sphere, "FootMesh", ft, C2U(0, -0.25f, -0.07f), new Vector3(0.42f, 0.16f, 0.8f), orange)
                    .transform.localRotation = Quaternion.Euler(0, -40f, 0);
                Pivot("FootSocket" + side, ank);
                Pivot("GloveSocket" + side, sh + (tip - sh) * 0.74f);
                if (s < 0) Pivot("HandSocket", tip);
            }
            Pivot("HeadSocket", C2U(0, -0.02f, 1.98f));
            Pivot("ChestSocket", C2U(0, 0, 0.95f));
            Pivot("Belly", C2U(0, -0.66f, 0.85f));
            return root;
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Wear clothes by Bonus id (e.g. "flannel_head"); empty/null removes that slot.</summary>
        public void SetClothes(string head, string chest, string feet)
        {
            if (sprite != null) { sprite.SetClothes(head, chest, feet); return; }
            Wear(ref headWear, head, headSocket);
            Wear(ref chestWear, chest, chestSocket);
            Wear(ref feetWearL, feet, footSocketL);
            Wear(ref feetWearR, feet, footSocketR);
            RefreshRenderers();
        }

        /// <summary>Gloves (ClothesSlot.Hands id) and skin (ClothesSlot.Skin id, "" = classic). The sprite penguin ignores both.</summary>
        public void SetLook(string gloves, string skin)
        {
            SetGloves(gloves);
            SetSkin(skin);
        }

        public void SetGloves(string id)
        {
            id = id ?? "";
            if (sprite != null || (id == Gloves && (gloveWear != null || id == ""))) { Gloves = id; return; }
            Gloves = id;
            if (gloveWear != null) { Destroy(gloveWear); gloveWear = null; }
            if (gloveLeft != null) { Destroy(gloveLeft); gloveLeft = null; }
            string path = ArtCatalog.ClothesModel(id);
            if (path != null && gloveSocketR != null)
            {
                // one FBX, two objects: GloveR rides GloveSocketR, GloveL moves to GloveSocketL
                gloveWear = ModelLibrary.Spawn(path, gloveSocketR);
                gloveWear.transform.localPosition = Vector3.zero;
                gloveWear.transform.localRotation = Quaternion.identity;
                gloveWear.transform.localScale = Vector3.one;
                WearTextures.Apply(gloveWear);
                var left = FindDeep(gloveWear.transform, "GloveL");
                if (left != null && gloveSocketL != null)
                {
                    var lp = left.localPosition;
                    var lr = left.localRotation;
                    var ls = left.localScale;
                    left.SetParent(gloveSocketL, false);
                    left.localPosition = lp;
                    left.localRotation = lr;
                    left.localScale = ls;
                    left.name = "GloveL (worn)";
                    gloveLeft = left.gameObject;
                }
            }
            RefreshRenderers();
        }

        public void SetSkin(string id)
        {
            id = id ?? "";
            if (id == Skin) return;
            Skin = id;
            if (sprite != null || model == null) return;
            WearTextures.Apply(model.gameObject, ClothesCatalog.SkinTexture(id));
            RefreshRenderers();
        }

        void Wear(ref GameObject slot, string id, Transform socket)
        {
            if (slot != null) { Destroy(slot); slot = null; }
            if (string.IsNullOrEmpty(id) || socket == null) return;
            string path = ArtCatalog.ClothesModel(id);
            if (path == null) return;
            slot = ModelLibrary.Spawn(path, socket);
            slot.transform.localPosition = Vector3.zero;
            slot.transform.localRotation = Quaternion.identity;
            slot.transform.localScale = Vector3.one;
            WearTextures.Apply(slot);
        }

        public void SetState(AvatarState s)
        {
            if (s == State && stateTime > 0) return;
            prevState = State;
            State = s;
            stateTime = 0;
            if (sprite != null) { sprite.SetState(s); return; }
            if ((prevState == AvatarState.Fall || prevState == AvatarState.Jump) && (s == AvatarState.Idle || s == AvatarState.Walk || s == AvatarState.Aim))
                landSquash = 1f;
            if (s == AvatarState.Fire)
            {
                fireKick = 1f;
                // the clip's "fire" section (muzzle flash / recoil), then back to the "aim" pose
                var w = weaponPlayer.Set;
                if (w != null && w.Segment("fire", out int fa, out int fb)) { weaponPlayer.Play(w, fa, fb, false, onWeaponFired); ShowWeaponFrame(); }
                // a held item is thrown: follow-through swing, the item is gone until the next one is "drawn"
                if (weapon != null && uprightItem) throwT = 0f;
            }
            if (s == AvatarState.Hurt) Flash();
            if (s == AvatarState.Dead) SpawnGhost();
            else if (ghost != null) { Destroy(ghost); ghost = null; }
            RefreshWeaponVisibility();
        }

        public void SetFacing(int dir)
        {
            Facing = dir >= 0 ? 1 : -1;
            if (sprite != null) sprite.ApplyFacing(Facing);
            if (facingNode != null) ApplyFacingScale();
        }

        /// <summary>Aim angle in degrees, 0 = forward (facing direction), 90 = up, -90 = down.</summary>
        public void SetAim(float degrees)
        {
            aimTarget = Mathf.Clamp(degrees, -90f, 90f);
            if (sprite != null) { sprite.ApplyAim(aimTarget); UpdateMuzzle(); }
        }

        /// <summary>Show a weapon by WeaponGraphic id (null/empty = flippers empty): the textured 3D model, else the
        /// original clip, else the default model (see <see cref="Prefer3DWeapons"/>).</summary>
        public void HoldWeapon(string weaponGraphicId)
        {
            bool showing = (weapon != null && weapon.activeSelf) || (weaponSr != null && weaponSr.sprite != null) || (sprite != null && sprite.HasWeapon);
            if (HeldWeapon == weaponGraphicId && (showing || string.IsNullOrEmpty(weaponGraphicId))) return;
            HeldWeapon = weaponGraphicId;
            if (sprite != null)
            {
                sprite.HoldWeapon(weaponGraphicId);
                sprite.ApplyAim(aimTarget);
                UpdateMuzzle();
                return;
            }
            if (weapon != null) { weapon.SetActive(false); weapon = null; }
            weaponSr.sprite = null;
            weaponPlayer.Hold(null, 0);
            hold = null;
            allowRotation = true;
            uprightItem = false;
            gunLike = false;
            throwT = -1f;
            ResetRig();
            tip.localPosition = new Vector3(0.25f, 0, 0);
            if (!string.IsNullOrEmpty(weaponGraphicId))
            {
                hold = ArtCatalog.WeaponHoldType(weaponGraphicId);
                allowRotation = ArtCatalog.WeaponAllowsRotation(weaponGraphicId);
                string own = "Weapons/" + ArtCatalog.Strip(weaponGraphicId);
                var set = Prefer3DWeapons && ModelLibrary.Exists(own) ? null : OriginalArt.WeaponAnim(weaponGraphicId);
                if (set != null)
                {
                    // draw, then hold the "aim" pose (Weapon.as: AIM_LABEL)
                    if (set.Segment("draw", out int a, out int b) && set.HasLabel("aim")) weaponPlayer.Play(set, a, b, false, onWeaponFired);
                    else weaponPlayer.Hold(set, set.Label("aim", 0));
                    ShowWeaponFrame();
                    MeasureTip(set.Still("aim"));
                }
                else Hold3D(ModelLibrary.Exists(own) ? own : ArtCatalog.WeaponModel(weaponGraphicId));
            }
            RefreshWeaponVisibility();
            UpdateMuzzle();
            RefreshRenderers();
        }

        /// <summary>Weapon models sit a little in front of the flipper (towards the camera) so the flipper holds them
        /// from behind instead of cutting through them.</summary>
        const float RigDepth = -0.1f;
        /// <summary>Upright items lean this much of the aim angle.</summary>
        const float UprightLean = 0.3f;

        void ResetRig()
        {
            if (weaponRig == null) return;
            weaponRig.localPosition = new Vector3(0, 0, RigDepth / handScale);
            weaponRig.localRotation = Quaternion.identity;
            weaponRig.localScale = Vector3.one * (WeaponScale / handScale);
        }

        /// <summary>Put the textured model in the flipper (pooled per avatar: switching weapons does not re-instantiate)
        /// and measure its Muzzle empty in holder space at the rest pose.</summary>
        void Hold3D(string path)
        {
            if (!weaponPool.TryGetValue(path, out weapon) || weapon == null)
            {
                weapon = ModelLibrary.Spawn(path, weaponRig, PrimitiveType.Cube, 1f, new Color(0.3f, 0.33f, 0.38f));
                if (weapon.name.EndsWith("(fallback)"))
                {
                    weapon.transform.localScale = new Vector3(0.9f, 0.18f, 0.18f);
                    weapon.transform.localPosition = new Vector3(0.35f, 0.1f, 0);
                }
                else
                {
                    weapon.transform.localPosition = Vector3.zero;
                    weapon.transform.localScale = Vector3.one;
                }
                weapon.transform.localRotation = Quaternion.identity;
                weaponPool[path] = weapon;
            }
            weapon.SetActive(true);
            ResetRig();
            var mz = FindPrefix(weapon.transform, "Muzzle");
            Vector3 local = mz != null ? weaponRig.InverseTransformPoint(mz.position) : new Vector3(0.8f, 0.1f, 0);
            tip.position = weaponRig.TransformPoint(local);
            // thrown things (grenade, bottle, egg, rock, cat...) are short: held upright; long ones (broom, drill) aim
            uprightItem = (hold == "small_object" || hold == "large_object") && local.x < 0.45f;
            gunLike = !uprightItem;
            drawT = 0f;
        }
        void HoldWeaponPose()
        {
            var set = weaponPlayer.Set;
            if (set != null) weaponPlayer.Hold(set, set.Label("aim", 0));
            ShowWeaponFrame();
        }

        void ShowWeaponFrame()
        {
            var set = weaponPlayer.Set;
            if (weaponSr != null) weaponSr.sprite = set != null ? set.FrameAt(weaponPlayer.Frame) : null;
        }

        /// <summary>Muzzle from the "aim" frame bounds: the right-most point of the clip (barrel tip). Throwables are
        /// thrown from a point ahead of the flipper.</summary>
        void MeasureTip(Sprite aim)
        {
            float k = weaponSr.transform.localScale.x;
            tip.localPosition = new Vector3(0.6f * k, 0, 0);
            if (aim == null || hold == "small_object" || hold == "large_object") return;
            var v = aim.vertices;
            if (v == null || v.Length == 0) return;
            float maxX = float.MinValue;
            for (int i = 0; i < v.Length; i++) maxX = Mathf.Max(maxX, v[i].x);
            float sy = 0; int n = 0;
            for (int i = 0; i < v.Length; i++)
                if (v[i].x > maxX - 0.25f) { sy += v[i].y; n++; }
            if (maxX < 0.2f) return;
            tip.localPosition = new Vector3(maxX * k, (n > 0 ? sy / n : 0f) * k, 0);
        }

        void RefreshWeaponVisibility()
        {
            if (weaponHolder == null) return;
            bool show = !string.IsNullOrEmpty(HeldWeapon) && State != AvatarState.Dead && State != AvatarState.Drown &&
                        State != AvatarState.Celebrate && State != AvatarState.Sad;
            if (weaponHolder.gameObject.activeSelf != show) weaponHolder.gameObject.SetActive(show);
        }

        /// <summary>Flash white when hit.</summary>
        public void Flash()
        {
            flash = 1f;
            if (sprite != null) sprite.Flash();
        }

        /// <summary>End of match pose (won = celebrate, lost = sad).</summary>
        public void SetResultPose(bool won) { SetState(won ? AvatarState.Celebrate : AvatarState.Sad); }

        /// <summary>Show an emoticon bubble (Emoticon item id like "EmoticonLaugh") for a few seconds.</summary>
        public void ShowEmote(string emoticonId)
        {
            if (string.IsNullOrEmpty(emoticonId)) return;
            float dur = 3f;
            var rec = GameData.Loaded ? GameData.Get("Emoticon", emoticonId) : null;
            if (rec != null) dur = Mathf.Clamp(Units.Ms(rec.Float("Duration", 3000)), 1f, 8f);
            if (bubble != null) Destroy(bubble);
            if (emoteAnim != null) { Destroy(emoteAnim.gameObject); emoteAnim = null; }
            if (ShowOriginalEmote(emoticonId)) return;
            bubble = new GameObject("EmoteBubble");
            bubble.transform.SetParent(transform, false);
            var bg = new GameObject("Bg").AddComponent<SpriteRenderer>();
            bg.transform.SetParent(bubble.transform, false);
            bg.sprite = BubbleSprite;
            bg.sortingOrder = 50;
            var icon = ArtCatalog.EmoticonIcon(emoticonId);
            if (icon != null)
            {
                var ic = new GameObject("Icon").AddComponent<SpriteRenderer>();
                ic.transform.SetParent(bubble.transform, false);
                ic.sprite = icon;
                ic.sortingOrder = 51;
                ic.transform.localPosition = new Vector3(0, 0.03f, -0.01f);
                float k = 0.82f / Mathf.Max(0.01f, icon.bounds.size.x);
                ic.transform.localScale = Vector3.one * k;
            }
            bubbleStart = Time.time;
            bubbleUntil = Time.time + dur;
            UpdateBubble();
        }

        /// <summary>
        /// The original animated emote (labels Hidden, Hidden_To_Visible = the whole bubble pop-in/loop/pop-out,
        /// Visible = empty) with its registration point (the bubble's tail) just above the head, not mirrored.
        /// </summary>
        bool ShowOriginalEmote(string emoticonId)
        {
            var set = OriginalArt.Emote(emoticonId);
            if (set == null) return false;
            var a = SpriteAnim.Create(transform, set, "Emote", EmoteSortingOrder, false);
            a.UnscaledTime = UnscaledTime;
            float k = sprite != null ? sprite.Scale : Height / PenguinSprite.NaturalHeight;
            a.transform.localPosition = new Vector3(0.15f * k, Height * 0.97f, -0.2f - (sprite != null ? 0f : 0.8f * k));
            a.transform.localScale = Vector3.one * (EmoteScale * k);
            int from = set.Label("Hidden_To_Visible", 0), to = set.Label("Visible", set.Length) - 1;
            var go = a.gameObject;
            a.PlayRange(set, from, Mathf.Max(from, to), false, () => { if (go != null) Destroy(go); });
            emoteAnim = a;
            return true;
        }

        /// <summary>Emote bubbles draw above penguins, effects and water (see the sorting orders in PenguinSprite).</summary>
        public const int EmoteSortingOrder = 60;
        /// <summary>Emote size relative to the original (the original bubble is about as wide as two penguins).</summary>
        public const float EmoteScale = 0.65f;

        public void SetTeamColor(Color c)
        {
            TeamColor = c;
            if (sprite != null) { sprite.SetTeamColor(c); return; }
            if (ring != null) ring.color = new Color(c.r, c.g, c.b, 0.9f);
        }

        // ------------------------------------------------------------------ renderers / flash

        void RefreshRenderers()
        {
            renderers.Clear();
            if (facingNode != null) facingNode.GetComponentsInChildren(true, renderers);
            renderers.RemoveAll(r => r is SpriteRenderer);
            ApplyBlocks(flash);
        }

        void ApplyBlocks(float f)
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            for (int i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetFloat(FlashId, f);
                r.SetPropertyBlock(mpb);
            }
            if (weaponSr != null) weaponSr.color = Color.Lerp(Color.white, new Color(1f, 0.45f, 0.42f, 1f), f);
        }

        // ------------------------------------------------------------------ animation

        void LateUpdate()
        {
            if (sprite != null)
            {
                float sdt = UnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                stateTime += sdt;
                sprite.Tick(Mathf.Min(sdt, 0.1f));
                sprite.ApplyAim(aimTarget);
                if (bubble != null) UpdateBubble();
                UpdateMuzzle();
                return;
            }
            if (poseNode == null) return;
            float dt = Mathf.Min(UnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime, 0.05f);
            t += dt;
            stateTime += dt;
            if (weaponPlayer.Tick(dt)) ShowWeaponFrame();

            // pose defaults
            Vector3 posePos = Vector3.zero;
            float poseRoll = 0f;
            float bodyRoll = 0f, bodyBob = 0f, sx = 1f, sy = 1f;
            float headRoll = 0f, headBob = 0f;
            float flL = 0f, flR = 0f;                // flipper raise angles (outward), degrees
            bool aimR = !string.IsNullOrEmpty(HeldWeapon);
            float aimDeg = allowRotation ? aimTarget : 0f;
            float aimAngle = aimDeg;
            float footLift = 0f, footPhase = 0f;
            float eyeSquash = 1f;
            Vector2 look = new Vector2(0.4f, 0f);

            float breath = Mathf.Sin(t * 2.2f);
            switch (State)
            {
                case AvatarState.Idle:
                    sy = 1f + 0.025f * breath; sx = 1f - 0.012f * breath;
                    flL = 4f + 3f * Mathf.Sin(t * 2.2f + 0.5f); flR = flL;
                    headRoll = 2.5f * Mathf.Sin(t * 0.9f);
                    // now and then a little look around
                    look = new Vector2(0.4f + 0.5f * Mathf.Sin(t * 0.37f), 0.15f * Mathf.Sin(t * 0.53f));
                    if (aimR) aimAngle = Mathf.Lerp(-25f, aimDeg, 0.5f);
                    break;
                case AvatarState.Walk:
                {
                    float w = t * 11f;
                    bodyRoll = 9f * Mathf.Sin(w);
                    bodyBob = 0.07f * Mathf.Abs(Mathf.Sin(w));
                    headRoll = -4f * Mathf.Sin(w);
                    flL = 18f + 10f * Mathf.Sin(w); flR = 18f - 10f * Mathf.Sin(w);
                    footLift = 0.14f; footPhase = w;
                    look = new Vector2(1f, 0f);
                    if (aimR) aimAngle = -15f;
                    break;
                }
                case AvatarState.Jump:
                {
                    float k = Mathf.Clamp01(stateTime / 0.15f);
                    sy = Mathf.Lerp(0.85f, 1.12f, k); sx = Mathf.Lerp(1.1f, 0.92f, k);
                    flL = flR = 70f;
                    footLift = 0.05f;
                    look = new Vector2(0.3f, 0.5f);
                    if (aimR) aimAngle = 10f;
                    break;
                }
                case AvatarState.Fall:
                    flL = 60f + 30f * Mathf.Sin(t * 30f); flR = 60f + 30f * Mathf.Sin(t * 30f + 1f);
                    sy = 1.04f; sx = 0.97f;
                    look = new Vector2(0.2f, -0.6f);
                    eyeSquash = 1.15f;
                    if (aimR) aimAngle = 20f;
                    break;
                case AvatarState.Aim:
                    sy = 1f + 0.015f * breath;
                    flL = 10f;
                    aimR = true;
                    headRoll = -aimDeg * 0.12f;
                    look = new Vector2(Mathf.Cos(aimDeg * Mathf.Deg2Rad), Mathf.Sin(aimDeg * Mathf.Deg2Rad));
                    break;
                case AvatarState.Fire:
                    aimR = true;
                    flL = 15f;
                    headRoll = -aimDeg * 0.12f;
                    look = new Vector2(Mathf.Cos(aimDeg * Mathf.Deg2Rad), Mathf.Sin(aimDeg * Mathf.Deg2Rad));
                    eyeSquash = 0.6f;
                    break;
                case AvatarState.Hurt:
                {
                    float sh = Mathf.Clamp01(1f - stateTime / 0.5f);
                    posePos = new Vector3((float)(rng.NextDouble() - 0.5) * 0.12f * sh, (float)(rng.NextDouble() - 0.5) * 0.06f * sh, 0);
                    bodyRoll = 8f * sh;
                    flL = flR = 45f * sh;
                    eyeSquash = 0.25f;
                    if (aimR) aimAngle = -30f;
                    break;
                }
                case AvatarState.Dead:
                {
                    float k = Mathf.SmoothStep(0, 1, Mathf.Clamp01(stateTime / 0.45f));
                    poseRoll = 82f * k;
                    posePos = new Vector3(-0.15f * k, 0.25f * k, 0);
                    flL = flR = 30f * k;
                    eyeSquash = Mathf.Lerp(1f, 0.15f, k);
                    aimR = false;
                    UpdateGhost();
                    break;
                }
                case AvatarState.Drown:
                {
                    float w = t * 14f;
                    poseRoll = 12f * Mathf.Sin(t * 3f);
                    posePos = new Vector3(0, -0.25f + 0.1f * Mathf.Sin(t * 4f), 0);
                    flL = 120f + 35f * Mathf.Sin(w); flR = 120f + 35f * Mathf.Sin(w + 1.5f);
                    look = new Vector2(0f, 1f);
                    eyeSquash = 1.25f;
                    aimR = false;
                    break;
                }
                case AvatarState.Celebrate:
                {
                    float hop = Mathf.Abs(Mathf.Sin(t * 6f));
                    posePos = new Vector3(0, 0.35f * hop, 0);
                    sy = 1f + 0.08f * (hop - 0.5f); sx = 1f - 0.04f * (hop - 0.5f);
                    flL = 130f + 25f * Mathf.Sin(t * 12f); flR = 130f + 25f * Mathf.Sin(t * 12f + 2f);
                    headRoll = 6f * Mathf.Sin(t * 6f);
                    eyeSquash = 0.35f;
                    aimR = false;
                    break;
                }
                case AvatarState.Sad:
                    headRoll = -16f + 2f * Mathf.Sin(t * 1.3f);
                    headBob = -0.05f;
                    bodyRoll = -3f;
                    sy = 0.97f + 0.01f * breath;
                    flL = flR = -6f;
                    look = new Vector2(0.3f, -1f);
                    eyeSquash = 0.7f;
                    aimR = false;
                    break;
            }

            // landing squash and fire recoil
            if (landSquash > 0)
            {
                landSquash = Mathf.Max(0, landSquash - dt * 6f);
                float k = Mathf.Sin(landSquash * Mathf.PI);
                sy *= 1f - 0.18f * k; sx *= 1f + 0.12f * k;
            }
            float kick = 0f;
            if (fireKick > 0)
            {
                fireKick = Mathf.Max(0, fireKick - dt * 4f);
                kick = fireKick * fireKick;
                posePos += new Vector3(-0.18f * kick, 0, 0);
                bodyRoll += 6f * kick;
            }

            // blink
            blinkTimer -= dt;
            if (blinkTimer <= 0 && blinkPhase < 0) { blinkPhase = 0f; blinkTimer = 2f + (float)rng.NextDouble() * 3.5f; }
            float blink = 1f;
            if (blinkPhase >= 0)
            {
                blinkPhase += dt / 0.14f;
                blink = Mathf.Clamp01(Mathf.Abs(blinkPhase * 2f - 1f));
                if (blinkPhase >= 1f) blinkPhase = -1f;
            }
            eyeSquash *= Mathf.Max(0.08f, blink);

            // spawn: pop in with a little overshoot
            float pop = 1f;
            if (spawnPop >= 0f)
            {
                spawnPop += dt / 0.4f;
                float x = Mathf.Clamp01(spawnPop);
                const float c1 = 1.70158f, c3 = c1 + 1f;
                pop = 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
                if (spawnPop >= 1f) spawnPop = -1f;
            }

            // apply
            poseNode.localPosition = posePos;
            poseNode.localRotation = Quaternion.AngleAxis(poseRoll, Vector3.forward);
            poseNode.localScale = Vector3.one * Mathf.Max(0.01f, pop);
            if (body != null)
            {
                body.t.localPosition = body.pos + new Vector3(0, bodyBob, 0);
                body.t.localRotation = Quaternion.AngleAxis(bodyRoll, Vector3.forward) * body.rot;
                body.t.localScale = Vector3.Scale(body.scale, new Vector3(sx, sy, sx));
            }
            if (head != null)
            {
                head.t.localPosition = head.pos + new Vector3(0, headBob, 0);
                head.t.localRotation = Quaternion.AngleAxis(headRoll, Vector3.forward) * head.rot;
            }
            if (flipL != null) flipL.t.localRotation = Quaternion.AngleAxis(flL, Vector3.forward) * flipL.rot;
            if (flipR != null)
            {
                // aimed: the flipper (and the weapon in it) points along the aim; the body's roll is taken out so
                // the barrel stays on the aim line while the body sways
                float target = aimR ? (aimAngle + 12f * kick) - restFlipR - bodyRoll : -flR;
                float rate = 22f;
                if (throwT >= 0f && throwT < ThrowSwing)
                {
                    // overarm follow-through: from over the head down past the aim
                    float k = throwT / ThrowSwing;
                    float swing = Mathf.Lerp(95f, -40f, 1f - (1f - k) * (1f - k) * (1f - k));
                    target = aimAngle + swing - restFlipR - bodyRoll;
                    if (k < 0.05f) flipRCurrent = target;
                    rate = 60f;
                }
                if (float.IsNaN(flipRCurrent)) flipRCurrent = target;
                flipRCurrent = Mathf.LerpAngle(flipRCurrent, target, 1f - Mathf.Exp(-dt * rate));
                flipR.t.localRotation = Quaternion.AngleAxis(flipRCurrent, Vector3.forward) * flipR.rot;
            }
            if (weapon != null && weaponRig != null) AnimateRig(dt, kick, aimAngle);
            Foot(footL, footLift, footPhase);
            Foot(footR, footLift, footPhase + Mathf.PI);
            Eye(eyeL, eyeSquash);
            Eye(eyeR, eyeSquash);
            Pupil(pupilL, look);
            Pupil(pupilR, look);

            if (flash > 0)
            {
                flash = Mathf.Max(0, flash - dt / 0.18f);
                ApplyBlocks(flash);
            }
            if (bubble != null) UpdateBubble();
            UpdateMuzzle();
        }

        const float ThrowSwing = 0.24f, ThrowHidden = 0.6f, ThrowPop = 0.25f;

        static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            x = Mathf.Clamp01(x);
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }

        /// <summary>
        /// The held model on top of the flipper pose: draw (swings up from pointing down and grows in with a little
        /// overshoot), recoil for guns (kicks back along the barrel, muzzle climbs, a squash), upright items stay
        /// upright with a lean toward the aim and a little wobble, thrown items vanish on the throw and pop back.
        /// </summary>
        void AnimateRig(float dt, float kick, float aimAngle)
        {
            Vector3 pos = new Vector3(0, 0, RigDepth / handScale);
            float rot = 0f, sc = 1f;
            if (drawT < 1f)
            {
                drawT = Mathf.Min(1f, drawT + dt / 0.32f);
                rot -= 70f * (1f - EaseOutBack(drawT));
                sc *= Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(drawT * 1.7f));
            }
            if (gunLike && kick > 0f)
            {
                pos.x -= 0.16f * kick / handScale;
                rot += 14f * kick;
                sc *= 1f + 0.05f * kick;
            }
            if (uprightItem)
            {
                // the holder turns with the flipper; turn the item back to (nearly) upright in pose space
                Vector3 r = poseNode.InverseTransformVector(weaponHolder.right);
                float ha = Mathf.Atan2(r.y, r.x) * Mathf.Rad2Deg;
                float lean = aimAngle * UprightLean + 4f * Mathf.Sin(t * 2.6f);
                rot += Mathf.DeltaAngle(ha, lean);
            }
            if (throwT >= 0f)
            {
                throwT += dt;
                if (throwT < ThrowHidden) sc = 0f;
                else if (throwT < ThrowHidden + ThrowPop) sc *= EaseOutBack((throwT - ThrowHidden) / ThrowPop);
                else throwT = -1f;
            }
            weaponRig.localPosition = pos;
            weaponRig.localRotation = Quaternion.AngleAxis(rot, Vector3.forward);
            weaponRig.localScale = Vector3.one * (WeaponScale / handScale * Mathf.Max(sc, 0.0001f));
        }

        /// <summary>World-space body center (half the avatar height above the feet).</summary>
        public Vector3 Center => transform.position + transform.up * (Height * 0.45f);

        /// <summary>Muzzle = weapon tip, pulled in to at most MuzzleReach (model units, 2.6 = penguin height) from the
        /// body center; long barrels (sniper, nukes, scythe) reach about 1.3. BattleController.ShotOrigin also pulls the
        /// origin back out of terrain when the penguin is pressed against a wall.</summary>
        public float MuzzleReach = 1.5f;

        void UpdateMuzzle()
        {
            if (Muzzle == null || (tip == null && sprite == null)) return;
            Vector3 c = Center;
            Vector3 p = sprite != null ? sprite.TipWorld()
                : !string.IsNullOrEmpty(HeldWeapon) ? facingNode.TransformPoint(AimedTipPose()) : tip.position;
            Vector3 d = p - c;
            d.z = 0;
            float max = MuzzleReach * Mathf.Max(0.5f, Height / ModelHeight);
            if (d.magnitude > max) d = d.normalized * max;
            Muzzle.position = new Vector3(c.x + d.x, c.y + d.y, transform.position.z);
            Muzzle.rotation = Quaternion.Euler(0, 0, Facing > 0 ? aimTarget : 180f - aimTarget);
        }

        /// <summary>
        /// Pose-space position of the weapon tip (the model's Muzzle empty, or the clip's barrel end) when the flipper
        /// points along the aim, from the rest pose: the hand swung about the shoulder, plus the tip offset turned with
        /// the weapon (upright items only lean). This is exactly where the drawn barrel ends once the aim pose has
        /// settled, but it does not move with breathing, walking, recoil or the draw animation, so shots, the aim
        /// preview and network replays all start from the same point.
        /// </summary>
        Vector3 AimedTipPose()
        {
            float a = allowRotation ? aimTarget : 0f;
            float th = (a - restFlipR) * Mathf.Deg2Rad;
            Vector3 d = handRest - shoulderRest;
            float cs = Mathf.Cos(th), sn = Mathf.Sin(th);
            Vector3 hand = shoulderRest + new Vector3(d.x * cs - d.y * sn, d.x * sn + d.y * cs, d.z);
            float ia = (weapon != null && uprightItem ? a * UprightLean : a) * Mathf.Deg2Rad;
            Vector3 o = tip.localPosition * handScale;
            float ci = Mathf.Cos(ia), si = Mathf.Sin(ia);
            return hand + new Vector3(o.x * ci - o.y * si, o.x * si + o.y * ci, o.z);
        }

        static void Foot(Part f, float lift, float phase)
        {
            if (f == null) return;
            float up = lift * Mathf.Max(0, Mathf.Sin(phase));
            f.t.localPosition = f.pos + new Vector3(0, up, 0);
            f.t.localRotation = Quaternion.AngleAxis(up * 120f, Vector3.forward) * f.rot;
        }

        static void Eye(Part e, float squash)
        {
            if (e == null) return;
            e.t.localScale = new Vector3(e.scale.x, e.scale.y * squash, e.scale.z);
        }

        static void Pupil(Part p, Vector2 look)
        {
            if (p == null) return;
            if (look.sqrMagnitude > 1f) look.Normalize();
            p.t.localPosition = p.pos + new Vector3(look.x * 0.04f, look.y * 0.05f, 0);
        }

        // ------------------------------------------------------------------ ghost / bubble

        void SpawnGhost()
        {
            if (ghost != null) Destroy(ghost);
            ghost = new GameObject("Ghost");
            ghost.transform.SetParent(transform, false);
            var mat = Mats.Transparent(new Color(1f, 1f, 1f, 0.55f));
            var b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(b.GetComponent<Collider>());
            b.transform.SetParent(ghost.transform, false);
            b.transform.localScale = new Vector3(0.75f, 0.95f, 0.5f);
            b.GetComponent<Renderer>().sharedMaterial = mat;
            var dark = Mats.Transparent(new Color(0.1f, 0.1f, 0.15f, 0.7f));
            foreach (var s in new[] { -1f, 1f })
            {
                var e = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(e.GetComponent<Collider>());
                e.transform.SetParent(ghost.transform, false);
                e.transform.localPosition = new Vector3(0.14f * s, 0.12f, -0.24f);
                e.transform.localScale = new Vector3(0.12f, 0.18f, 0.05f);
                e.GetComponent<Renderer>().sharedMaterial = dark;
            }
            ghost.transform.localScale = Vector3.one * (Height / ModelHeight) * 1.1f;
            ghost.transform.localPosition = new Vector3(0, Height * 0.4f, 0);
        }

        void UpdateGhost()
        {
            if (ghost == null) return;
            float k = stateTime;
            ghost.transform.localPosition = new Vector3(0.15f * Mathf.Sin(k * 3f) * Height / ModelHeight, Height * (0.4f + 0.35f * k), -0.2f);
            if (k > 2.2f) { Destroy(ghost); ghost = null; }
        }

        void UpdateBubble()
        {
            float now = Time.time;
            if (now >= bubbleUntil) { Destroy(bubble); bubble = null; return; }
            float age = now - bubbleStart, left = bubbleUntil - now;
            float s = Mathf.Min(1f, age / 0.15f) * Mathf.Min(1f, left / 0.2f);
            float pop = 1f + 0.15f * Mathf.Sin(Mathf.Clamp01(age / 0.3f) * Mathf.PI);
            float size = Mathf.Max(0.8f, Height * 0.55f);
            bubble.transform.localPosition = new Vector3(Height * 0.35f, Height * 1.18f + 0.06f * Mathf.Sin(age * 3f), -0.5f);
            bubble.transform.rotation = Quaternion.identity;
            bubble.transform.localScale = Vector3.one * size * s * pop;
        }

        static Sprite bubbleSprite;
        static Sprite BubbleSprite
        {
            get
            {
                if (bubbleSprite) return bubbleSprite;
                const int n = 128;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[n * n];
                var fill = new Color(1f, 1f, 1f, 1f);
                var edge = new Color(0.12f, 0.13f, 0.2f, 1f);
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // circle with a small tail at the bottom left
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(u * u + (v - 0.08f) * (v - 0.08f));
                    bool inTail = v < -0.6f && v > -0.98f && Mathf.Abs(u + 0.45f + (v + 0.75f) * 0.6f) < 0.16f * (v + 1f) / 0.4f;
                    float a = Mathf.Clamp01((0.86f - d) * n * 0.5f);
                    float ring = Mathf.Clamp01((0.86f - d) * n * 0.5f) - Mathf.Clamp01((0.78f - d) * n * 0.5f);
                    Color c = Color.Lerp(fill, edge, ring);
                    if (inTail && a < 1f) { c = edge; a = 1f; }
                    c.a = a;
                    px[y * n + x] = c;
                }
                tex.SetPixels32(px);
                tex.Apply();
                bubbleSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
                return bubbleSprite;
            }
        }

        void OnDestroy()
        {
            if (ghost != null) Destroy(ghost);
            if (bubble != null) Destroy(bubble);
            if (emoteAnim != null) Destroy(emoteAnim.gameObject);
            sprite?.Destroy();
        }
    }
}
