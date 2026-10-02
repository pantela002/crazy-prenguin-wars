using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    public enum AvatarState { Idle, Walk, Jump, Fall, Aim, Fire, Hurt, Dead, Celebrate, Drown, Sad }

    /// <summary>
    /// The 3D penguin (Blender model "Penguin/Penguin") with clothes, procedural animation and a held weapon.
    /// Used by battles (Battle/Penguin) and menus (customization preview, home screen).
    ///
    /// Model contract (see Docs/ART.md): parts Body, Belly, Head, Beak, EyeL/R, PupilL/R, FlipperL/R, FootL/R, Scarf and
    /// empties HeadSocket, ChestSocket, FootSocketL/R, HandSocket, all exported as root objects with origins at their joints;
    /// the hierarchy is rebuilt here. The model is 2.6 units tall, feet at the origin, beak towards +X, belly towards -Z.
    /// Everything falls back to primitives when the FBX (or a part of it) is missing.
    ///
    /// Transform layout: this (unscaled; HeadTop, emote bubble) / Facing (mirror + size) / Pose (whole-body pose) / model.
    /// </summary>
    public class PenguinAvatar : MonoBehaviour
    {
        public const float ModelHeight = 2.6f;

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
            { "Belly", "Body" }, { "Head", "Body" }, { "FlipperL", "Body" }, { "FlipperR", "Body" }, { "Scarf", "Body" },
            { "ChestSocket", "Body" }, { "Beak", "Head" }, { "EyeL", "Head" }, { "EyeR", "Head" }, { "HeadSocket", "Head" },
            { "PupilL", "EyeL" }, { "PupilR", "EyeR" }, { "HandSocket", "FlipperR" }, { "FootSocketL", "FootL" }, { "FootSocketR", "FootR" },
        };

        Transform facingNode, poseNode, model;
        Part body, head, flipL, flipR, footL, footR, eyeL, eyeR, pupilL, pupilR, beak, belly;
        Transform tip;                         // weapon tip in the hand (Muzzle follows it, clamped near the body)
        Transform headSocket, chestSocket, footSocketL, footSocketR, handSocket, weaponHolder, scarf;
        float restFlipR = -100f;               // rest direction of the right flipper (shoulder -> tip), degrees in the XY plane
        readonly List<Renderer> renderers = new List<Renderer>();
        Renderer scarfRenderer;
        MaterialPropertyBlock mpb;
        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        // ------------------------------------------------------------------ animation state
        float t, stateTime, flash, blinkTimer = 2f, blinkPhase = -1f, landSquash, fireKick;
        float aimTarget, aimCurrent, flipRCurrent = float.NaN;
        AvatarState prevState;
        GameObject weapon, headWear, chestWear, feetWearL, feetWearR, ghost, bubble;
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
            mpb = new MaterialPropertyBlock();
            facingNode = new GameObject("Facing").transform;
            facingNode.SetParent(transform, false);
            poseNode = new GameObject("Pose").transform;
            poseNode.SetParent(facingNode, false);
            ApplyFacingScale();

            var m = ModelLibrary.Prefab("Penguin/Penguin") != null ? ModelLibrary.Spawn("Penguin/Penguin", poseNode) : null;
            if (m != null && FindDeep(m.transform, "Body") != null) model = m.transform;
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
            tip = new GameObject("WeaponTip").transform;
            tip.SetParent(weaponHolder, false);
            tip.localPosition = new Vector3(0.25f, 0, 0);
            Muzzle = new GameObject("Muzzle").transform;
            Muzzle.SetParent(transform, false);
            UpdateMuzzle();
            RefreshRenderers();
            SetState(AvatarState.Idle);
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
            EnsurePart("HeadSocket", "Head", new Vector3(0, 1.93f, 0));
            EnsurePart("ChestSocket", "Body", new Vector3(0, 0.95f, 0));
            EnsurePart("FootSocketL", "FootL", new Vector3(0.25f, 0.13f, 0.2f));
            EnsurePart("FootSocketR", "FootR", new Vector3(-0.2f, 0.13f, -0.25f));
            EnsurePart("HandSocket", "FlipperR", new Vector3(-0.67f, 0.62f, -0.72f));
            for (int i = 0; i < Hierarchy.GetLength(0); i++)
            {
                var c = Find(Hierarchy[i, 0]);
                var p = Find(Hierarchy[i, 1]);
                if (c != null && p != null && c.parent != p) c.SetParent(p, true);
            }
            body = P("Body"); head = P("Head"); belly = P("Belly"); beak = P("Beak");
            flipL = P("FlipperL"); flipR = P("FlipperR"); footL = P("FootL"); footR = P("FootR");
            eyeL = P("EyeL"); eyeR = P("EyeR"); pupilL = P("PupilL"); pupilR = P("PupilR");
            headSocket = Find("HeadSocket"); chestSocket = Find("ChestSocket");
            footSocketL = Find("FootSocketL"); footSocketR = Find("FootSocketR"); handSocket = Find("HandSocket");
            scarf = Find("Scarf");
            scarfRenderer = scarf != null ? scarf.GetComponent<Renderer>() : null;
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
            var black = new Color(0.15f, 0.16f, 0.21f);
            var white = new Color(0.96f, 0.97f, 0.98f);
            var orange = new Color(1f, 0.6f, 0.13f);
            Transform Pivot(string name, Vector3 pos)
            {
                var t = new GameObject(name).transform;
                t.SetParent(root.transform, false);
                t.localPosition = pos;
                return t;
            }
            var bodyP = Pivot("Body", new Vector3(0, 0.1f, 0));
            Prim(PrimitiveType.Sphere, "BodyMesh", bodyP, new Vector3(0, 0.82f, 0), new Vector3(1.55f, 1.65f, 1.4f), black);
            var bellyP = Pivot("Belly", C2U(0, 0, 0.95f));
            Prim(PrimitiveType.Sphere, "BellyMesh", bellyP, C2U(0, -0.3f, 0.76f) - C2U(0, 0, 0.95f), new Vector3(1.2f, 1.35f, 1.0f), white);
            var headP = Pivot("Head", C2U(0, 0, 1.45f));
            Prim(PrimitiveType.Sphere, "HeadMesh", headP, C2U(0, -0.02f, 1.93f) - C2U(0, 0, 1.45f), Vector3.one * 1.22f, black);
            Prim(PrimitiveType.Sphere, "FaceMesh", headP, C2U(0, -0.22f, 1.85f) - C2U(0, 0, 1.45f), new Vector3(0.9f, 0.82f, 0.8f), white);
            var beakP = Pivot("Beak", C2U(0, -0.55f, 1.8f));
            var bk = Prim(PrimitiveType.Cube, "BeakMesh", beakP, C2U(0, -0.18f, -0.02f), new Vector3(0.22f, 0.16f, 0.22f), orange);
            bk.transform.localRotation = Quaternion.Euler(0, -40f, 45f);
            foreach (var s in new[] { 1, -1 })
            {
                string side = s > 0 ? "L" : "R";
                var ec = C2U(0.2f * s, -0.555f, 2.01f);
                var eye = Pivot("Eye" + side, ec);
                Prim(PrimitiveType.Sphere, "EyeMesh", eye, Vector3.zero, new Vector3(0.32f, 0.42f, 0.18f), white);
                var pup = Pivot("Pupil" + side, ec);
                Prim(PrimitiveType.Sphere, "PupilMesh", pup, C2U(0.04f, -0.06f, -0.02f), new Vector3(0.17f, 0.23f, 0.1f), Color.black);
                var sh = C2U(0.66f * s, -0.02f, 1.32f);
                var fl = Pivot("Flipper" + side, sh);
                var tip = C2U(0.98f * s, -0.12f, 0.62f);
                var fm = Prim(PrimitiveType.Sphere, "FlipperMesh", fl, (tip - sh) * 0.5f, new Vector3(0.24f, 0.95f, 0.5f), black);
                fm.transform.localRotation = Quaternion.FromToRotation(Vector3.up, tip - sh);
                var ank = C2U(0.32f * s, -0.05f, 0.13f);
                var ft = Pivot("Foot" + side, ank);
                Prim(PrimitiveType.Sphere, "FootMesh", ft, C2U(0, -0.25f, -0.07f), new Vector3(0.42f, 0.16f, 0.8f), orange)
                    .transform.localRotation = Quaternion.Euler(0, -40f, 0);
                Pivot("FootSocket" + side, ank);
                if (s < 0) Pivot("HandSocket", tip);
            }
            var scarfP = Pivot("Scarf", C2U(0, 0, 1.45f));
            Prim(PrimitiveType.Cylinder, "ScarfMesh", scarfP, new Vector3(0, -0.03f, 0), new Vector3(1.25f, 0.08f, 1.15f), Color.white).name = "Scarf";
            scarfP.name = "ScarfPivot";
            Pivot("HeadSocket", C2U(0, -0.02f, 1.93f));
            Pivot("ChestSocket", C2U(0, 0, 0.95f));
            return root;
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Wear clothes by Bonus id (e.g. "flannel_head"); empty/null removes that slot.</summary>
        public void SetClothes(string head, string chest, string feet)
        {
            Wear(ref headWear, head, headSocket);
            Wear(ref chestWear, chest, chestSocket);
            Wear(ref feetWearL, feet, footSocketL);
            Wear(ref feetWearR, feet, footSocketR);
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
        }

        public void SetState(AvatarState s)
        {
            if (s == State && stateTime > 0) return;
            prevState = State;
            State = s;
            stateTime = 0;
            if ((prevState == AvatarState.Fall || prevState == AvatarState.Jump) && (s == AvatarState.Idle || s == AvatarState.Walk || s == AvatarState.Aim))
                landSquash = 1f;
            if (s == AvatarState.Fire) fireKick = 1f;
            if (s == AvatarState.Hurt) Flash();
            if (s == AvatarState.Dead) SpawnGhost();
            else if (ghost != null) { Destroy(ghost); ghost = null; }
        }

        public void SetFacing(int dir)
        {
            Facing = dir >= 0 ? 1 : -1;
            if (facingNode != null) ApplyFacingScale();
        }

        /// <summary>Aim angle in degrees, 0 = forward (facing direction), 90 = up, -90 = down.</summary>
        public void SetAim(float degrees) { aimTarget = Mathf.Clamp(degrees, -90f, 90f); }

        /// <summary>Show a weapon model by WeaponGraphic id (null/empty = flippers empty).</summary>
        public void HoldWeapon(string weaponGraphicId)
        {
            if (HeldWeapon == weaponGraphicId && (weapon != null || string.IsNullOrEmpty(weaponGraphicId))) return;
            HeldWeapon = weaponGraphicId;
            if (weapon != null) { Destroy(weapon); weapon = null; }
            tip.localPosition = new Vector3(0.25f, 0, 0);
            if (!string.IsNullOrEmpty(weaponGraphicId))
            {
                string path = ArtCatalog.WeaponModel(weaponGraphicId);
                weapon = ModelLibrary.Spawn(path, weaponHolder, PrimitiveType.Cube, 1f, new Color(0.3f, 0.33f, 0.38f));
                weapon.transform.localRotation = Quaternion.identity;
                if (weapon.name.EndsWith("(fallback)"))
                {
                    weapon.transform.localScale = new Vector3(0.9f, 0.18f, 0.18f);
                    weapon.transform.localPosition = new Vector3(0.35f, 0.1f, 0);
                    tip.localPosition = new Vector3(0.8f, 0.1f, 0);
                }
                else
                {
                    weapon.transform.localPosition = Vector3.zero;
                    weapon.transform.localScale = Vector3.one;
                    var mz = FindPrefix(weapon.transform, "Muzzle");
                    if (mz != null) tip.position = mz.position;
                }
            }
            UpdateMuzzle();
            RefreshRenderers();
        }

        /// <summary>Flash white when hit.</summary>
        public void Flash() { flash = 1f; }

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

        public void SetTeamColor(Color c)
        {
            TeamColor = c;
            ApplyBlocks(0);
        }

        // ------------------------------------------------------------------ renderers / flash

        void RefreshRenderers()
        {
            renderers.Clear();
            if (facingNode != null) facingNode.GetComponentsInChildren(true, renderers);
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
                if (r == scarfRenderer) mpb.SetColor(ColorId, TeamColor);
                r.SetPropertyBlock(mpb);
            }
        }

        // ------------------------------------------------------------------ animation

        void LateUpdate()
        {
            if (poseNode == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            t += dt;
            stateTime += dt;

            // pose defaults
            Vector3 posePos = Vector3.zero;
            float poseRoll = 0f;
            float bodyRoll = 0f, bodyBob = 0f, sx = 1f, sy = 1f;
            float headRoll = 0f, headBob = 0f;
            float flL = 0f, flR = 0f;                // flipper raise angles (outward), degrees
            bool aimR = !string.IsNullOrEmpty(HeldWeapon);
            float aimAngle = aimTarget;
            float footLift = 0f, footPhase = 0f;
            float eyeSquash = 1f;
            Vector2 look = new Vector2(0.4f, 0f);

            float breath = Mathf.Sin(t * 2.2f);
            switch (State)
            {
                case AvatarState.Idle:
                    sy = 1f + 0.025f * breath; sx = 1f - 0.012f * breath;
                    flL = 4f + 3f * Mathf.Sin(t * 2.2f + 0.5f); flR = flL;
                    headRoll = 2f * Mathf.Sin(t * 0.9f);
                    if (aimR) aimAngle = Mathf.Lerp(-25f, aimTarget, 0.5f);
                    break;
                case AvatarState.Walk:
                {
                    float w = t * 11f;
                    bodyRoll = 9f * Mathf.Sin(w);
                    bodyBob = 0.07f * Mathf.Abs(Mathf.Sin(w));
                    headRoll = -4f * Mathf.Sin(w);
                    flL = 18f + 10f * Mathf.Sin(w); flR = 18f - 10f * Mathf.Sin(w);
                    footLift = 0.14f; footPhase = w;
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
                    headRoll = -aimTarget * 0.12f;
                    look = new Vector2(Mathf.Cos(aimTarget * Mathf.Deg2Rad), Mathf.Sin(aimTarget * Mathf.Deg2Rad));
                    break;
                case AvatarState.Fire:
                    aimR = true;
                    flL = 15f;
                    headRoll = -aimTarget * 0.12f;
                    look = new Vector2(Mathf.Cos(aimTarget * Mathf.Deg2Rad), Mathf.Sin(aimTarget * Mathf.Deg2Rad));
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

            // apply
            poseNode.localPosition = posePos;
            poseNode.localRotation = Quaternion.AngleAxis(poseRoll, Vector3.forward);
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
                float target = aimR ? (aimAngle + 12f * kick) - restFlipR : -flR;
                if (float.IsNaN(flipRCurrent)) flipRCurrent = target;
                flipRCurrent = Mathf.LerpAngle(flipRCurrent, target, 1f - Mathf.Exp(-dt * 22f));
                flipR.t.localRotation = Quaternion.AngleAxis(flipRCurrent, Vector3.forward) * flipR.rot;
            }
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

        /// <summary>World-space body center (half the avatar height above the feet).</summary>
        public Vector3 Center => transform.position + transform.up * (Height * 0.45f);

        /// <summary>Muzzle = weapon tip, pulled in to at most MuzzleReach from the body center so shots fired while
        /// pressed against a wall do not start inside the terrain (penguin collider radius is about Height * 0.43).</summary>
        public float MuzzleReach = 0.95f;

        void UpdateMuzzle()
        {
            if (Muzzle == null || tip == null) return;
            Vector3 c = Center;
            Vector3 d = tip.position - c;
            d.z = 0;
            float max = MuzzleReach * Mathf.Max(0.5f, Height / ModelHeight);
            if (d.magnitude > max) d = d.normalized * max;
            Muzzle.position = new Vector3(c.x + d.x, c.y + d.y, transform.position.z);
            Muzzle.rotation = tip.rotation;
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
            p.t.localPosition = p.pos + new Vector3(look.x * 0.045f, look.y * 0.05f, 0);
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
        }
    }
}
