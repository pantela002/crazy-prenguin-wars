using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CPW
{
    /// <summary>
    /// The original 2D Flash penguin, shown by PenguinAvatar only when PenguinAvatar.UseOriginalSprite is set (the game
    /// uses the textured 3D penguin; the team ring sprite below is shared with it): body =
    /// one SpriteRenderer playing the 46 original animations at 24 fps; clothes (sprites rendered from the Blender
    /// models) and the held weapon (original weapon clip, or the 3D model when the clip is missing) ride the
    /// animation's slots (PenguinRigData) like the original PaperDoll's addChild into "head_gear", "tool"...
    ///
    /// Transform layout under the avatar (feet at its origin):
    /// Sprite (SortingGroup) / Ring (team colour ellipse at the feet) + Facing (mirror x, size, registration point
    /// = body centre FeetPx above the feet) / Body + one node per slot (Head, Body, FootL, FootR, Tool / Aim / Weapon).
    /// Animation names follow tuxwars PlayerGameObject.getAnimation: "<action>_<hold>" when holding an item
    /// (Item.AnimationType), the plain name otherwise.
    /// </summary>
    public sealed class PenguinSprite
    {
        /// <summary>Flash px from the registration point (body centre) down to the feet and up to the head top.</summary>
        public const float FeetPx = 31f, HeadPx = 32f;
        /// <summary>Natural height (feet to head top) in units at scale 1.</summary>
        public const float NaturalHeight = (FeetPx + HeadPx) / Units.PX;
        /// <summary>Sorting order of the whole penguin (one SortingGroup, so overlapping penguins never interleave).</summary>
        public const int SortingOrder = 10;
        const int BodyOrder = -50, RingOrder = -60;
        static readonly Color HitTint = new Color(1f, 0.45f, 0.42f, 1f);

        readonly PenguinAvatar owner;
        readonly Transform spriteRoot, facing;
        readonly SortingGroup group;
        readonly SpriteRenderer body, ring;
        readonly Transform[] slotNodes = new Transform[PenguinRigData.SlotCount];
        readonly SpriteRenderer[] clothes = new SpriteRenderer[PenguinRigData.SlotCount - 1];
        readonly Transform aimNode;
        readonly SpriteRenderer weaponSr;
        readonly SpriteAnimPlayer player = new SpriteAnimPlayer();
        readonly SpriteAnimPlayer weaponPlayer = new SpriteAnimPlayer();
        readonly System.Random rng = new System.Random();
        GameObject weapon3D;

        readonly float scale;
        string animName;
        PenguinRigData.Anim rig;
        AvatarState state, prevState;
        bool introPlaying;          // spawn / landing: SetState only records the state until it is done
        bool introIsLanding;        // a landing gives way to walking, jumping and aiming at once
        string hold;                // Item.AnimationType of the held item, null = empty flippers
        bool allowRotation = true;
        Vector2 tipLocal;           // muzzle in Aim space (units)
        bool tipFromArt;
        int shownFrame = -1;
        float flash, drownT;
        bool tinted;

        readonly Action onIdleDone, onIntroDone, onWeaponFired;

        public bool HasWeapon => weaponSr.sprite != null || weapon3D != null;
        /// <summary>Scale of the sprite rig (1 = original Flash size).</summary>
        public float Scale => scale;

        public static bool Available => OriginalArt.Penguin("idle") != null;

        public PenguinSprite(PenguinAvatar owner, float height)
        {
            this.owner = owner;
            scale = height / NaturalHeight;
            onIdleDone = IdleCycle;
            onIntroDone = () => { introPlaying = false; PlayState(state, true); };
            onWeaponFired = () => HoldWeaponPose();

            spriteRoot = new GameObject("Sprite").transform;
            spriteRoot.SetParent(owner.transform, false);
            group = spriteRoot.gameObject.AddComponent<SortingGroup>();
            group.sortingOrder = SortingOrder;

            ring = new GameObject("TeamRing").AddComponent<SpriteRenderer>();
            ring.transform.SetParent(spriteRoot, false);
            ring.sprite = RingSprite;
            ring.sortingOrder = RingOrder;
            ring.transform.localPosition = new Vector3(0, 0.05f * scale, 0);
            ring.transform.localScale = Vector3.one * scale;

            facing = new GameObject("Facing").transform;
            facing.SetParent(spriteRoot, false);
            body = new GameObject("Body").AddComponent<SpriteRenderer>();
            body.transform.SetParent(facing, false);
            body.sortingOrder = BodyOrder;
            for (int i = 0; i < PenguinRigData.SlotCount; i++)
            {
                var n = new GameObject(PenguinRigData.SlotNames[i]).transform;
                n.SetParent(facing, false);
                slotNodes[i] = n;
                if (i < clothes.Length)
                {
                    // the art sits on a child so the slot matrix's shear part (squash frames) can be a second rotation
                    clothes[i] = new GameObject("Art").AddComponent<SpriteRenderer>();
                    clothes[i].transform.SetParent(n, false);
                    clothes[i].enabled = false;
                }
            }
            aimNode = new GameObject("Aim").transform;
            aimNode.SetParent(slotNodes[(int)PenguinRigData.Slot.Tool], false);
            weaponSr = new GameObject("Weapon").AddComponent<SpriteRenderer>();
            weaponSr.transform.SetParent(aimNode, false);
            ApplyFacing(1);
        }

        // ------------------------------------------------------------------ API used by PenguinAvatar

        public void ApplyFacing(int dir)
        {
            facing.localScale = new Vector3(scale * dir, scale, 1f);
            facing.localPosition = new Vector3(0, FeetPx / Units.PX * scale, 0);
        }

        public bool ShowRing { set => ring.enabled = value; }
        public void SetTeamColor(Color c) => ring.color = new Color(c.r, c.g, c.b, 0.9f);

        public void SetClothes(string head, string chest, string feet)
        {
            Wear(PenguinRigData.Slot.Head, head);
            Wear(PenguinRigData.Slot.Body, chest);
            Wear(PenguinRigData.Slot.FootL, feet);
            Wear(PenguinRigData.Slot.FootR, feet);
            shownFrame = -1;
            ApplyFrame();
        }

        void Wear(PenguinRigData.Slot slot, string id)
        {
            // no original clothes art exists: missing sprites are simply not drawn
            clothes[(int)slot].sprite = PenguinRigData.ClothesSprite(id, slot);
        }

        public void Flash() => flash = 1f;

        /// <summary>Play the spawn animation (respawn / match start), then the current state.</summary>
        public void PlaySpawn()
        {
            var set = OriginalArt.Penguin("spawn");
            if (set == null) return;
            if (state == AvatarState.Dead || state == AvatarState.Drown) state = AvatarState.Idle;   // respawn: the owner sets Idle next
            introPlaying = true;
            introIsLanding = false;
            Play("spawn", set, 0, -1, false, onIntroDone);
        }

        public void SetState(AvatarState s)
        {
            prevState = state;
            state = s;
            if (s == AvatarState.Hurt) Flash();
            bool urgent = s == AvatarState.Dead || s == AvatarState.Drown || s == AvatarState.Hurt ||
                          (introIsLanding && s != AvatarState.Idle);
            if (introPlaying && !urgent) return;
            introPlaying = false;
            bool landed = (prevState == AvatarState.Jump || prevState == AvatarState.Fall) &&
                          (s == AvatarState.Idle || s == AvatarState.Aim);
            if (landed && PlayIntro("landjump")) return;
            PlayState(s, false);
        }

        public void HoldWeapon(string graphicId)
        {
            if (weapon3D != null) { UnityEngine.Object.Destroy(weapon3D); weapon3D = null; }
            weaponSr.sprite = null;
            weaponPlayer.Hold(null, 0);
            aimNode.localRotation = Quaternion.identity;
            hold = null;
            allowRotation = true;
            tipFromArt = false;
            if (!string.IsNullOrEmpty(graphicId))
            {
                hold = ArtCatalog.WeaponHoldType(graphicId);
                allowRotation = ArtCatalog.WeaponAllowsRotation(graphicId);
                var set = OriginalArt.WeaponAnim(graphicId);
                if (set != null)
                {
                    // draw, then hold the "aim" pose (Weapon.as: AIM_LABEL)
                    if (set.Segment("draw", out int a, out int b) && set.HasLabel("aim"))
                        weaponPlayer.Play(set, a, b, false, onWeaponFired);
                    else weaponPlayer.Hold(set, set.Label("aim", 0));
                    ShowWeaponFrame();
                    MeasureTip(set.Still("aim"));
                }
                else
                {
                    // clip missing (teleport gun, scythe, shield generator, sticky bomb, teleport grenade): the 3D model
                    weapon3D = ModelLibrary.Spawn(ArtCatalog.WeaponModel(graphicId), aimNode, PrimitiveType.Cube, 0.6f,
                        new Color(0.3f, 0.33f, 0.38f));
                    var t = weapon3D.transform;
                    t.localRotation = Quaternion.identity;
                    // 3D weapons are sized for the 2.6 unit 3D penguin; this rig is NaturalHeight tall at scale 1
                    t.localScale = Vector3.one * (NaturalHeight / PenguinAvatar.ModelHeight);
                    t.localPosition = new Vector3(0, 0, -0.4f);      // in front of the sprites (depth tested)
                    if (weapon3D.name.EndsWith("(fallback)"))
                    {
                        t.localScale = new Vector3(1.1f, 0.22f, 0.22f);
                        t.localPosition = new Vector3(0.45f, 0.1f, -0.4f);
                    }
                    tipLocal = new Vector2(1.0f, 0.1f);
                    tipFromArt = true;
                }
            }
            if (!tipFromArt) tipLocal = new Vector2(0.6f, 0f);
            PlayState(state, true);
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
            if (set != null) weaponSr.sprite = set.FrameAt(weaponPlayer.Frame);
        }

        /// <summary>Muzzle from the "aim" frame bounds: the right-most point of the clip (barrel tip) in Aim space.
        /// Throwables (held behind for the wind-up) are thrown from a point ahead of the hand instead.</summary>
        void MeasureTip(Sprite aim)
        {
            if (aim == null || hold == "small_object" || hold == "large_object") return;
            var v = aim.vertices;                      // tight mesh outline, relative to the pivot, in units
            if (v == null || v.Length == 0) return;
            float maxX = float.MinValue;
            for (int i = 0; i < v.Length; i++) maxX = Mathf.Max(maxX, v[i].x);
            float sy = 0; int n = 0;
            for (int i = 0; i < v.Length; i++)
                if (v[i].x > maxX - 0.25f) { sy += v[i].y; n++; }
            if (maxX < 0.2f) return;
            tipLocal = new Vector2(maxX, n > 0 ? sy / n : 0f);
            tipFromArt = true;
        }

        /// <summary>World position of the weapon tip (before PenguinAvatar clamps it to MuzzleReach).</summary>
        public Vector3 TipWorld() => aimNode.TransformPoint(tipLocal);

        /// <summary>Aim the held clip (degrees, 0 = forward, 90 = up): Weapon.as rotates the clip in the tool slot.</summary>
        public void ApplyAim(float deg)
        {
            bool aiming = state == AvatarState.Aim || state == AvatarState.Fire || state == AvatarState.Idle;
            float a = allowRotation && aiming ? deg : 0f;
            aimNode.localRotation = Quaternion.Euler(0, 0, a);
        }

        // ------------------------------------------------------------------ animation choice

        string WithHold(string action)
        {
            if (hold == null) return action;
            string n = action + "_" + hold;
            if (OriginalArt.Penguin(n) != null) return n;
            if (action == "jump" && OriginalArt.Penguin("jump_small_weapon") != null) return "jump_small_weapon";  // no jump_large_weapon
            return "idle_" + hold;
        }

        bool PlayIntro(string action)
        {
            string n = WithHold(action);
            var set = OriginalArt.Penguin(n);
            if (set == null) return false;
            introPlaying = true;
            introIsLanding = true;
            Play(n, set, 0, -1, false, onIntroDone);
            return true;
        }

        void PlayState(AvatarState s, bool force)
        {
            switch (s)
            {
                case AvatarState.Idle:
                    if (hold == null) { if (force || !IsIdleAnim(animName)) IdleCycle(); }
                    else Loop(WithHold("idle"), force);
                    break;
                case AvatarState.Aim:
                    Loop(WithHold("idle"), force);
                    break;
                case AvatarState.Walk:
                    IntroLoop(WithHold("walk"), force);
                    break;
                case AvatarState.Jump:
                    IntroLoop(WithHold("jump"), force);
                    break;
                case AvatarState.Fall:
                    if (hold != null) LoopSegment(WithHold("jump"), "loop", force);
                    else IntroLoop("fall", force);
                    break;
                case AvatarState.Fire:
                {
                    string n = hold != null ? WithHold("fire") : null;
                    var set = OriginalArt.Penguin(n);
                    if (set != null && n.StartsWith("fire")) Play(n, set, 0, -1, false, null);
                    else Loop(WithHold("idle"), force);
                    var w = weaponPlayer.Set;
                    // the clip's "fire" section (muzzle flash / recoil), then back to the "aim" pose
                    if (w != null && w.Segment("fire", out int fa, out int fb)) { weaponPlayer.Play(w, fa, fb, false, onWeaponFired); ShowWeaponFrame(); }
                    break;
                }
                case AvatarState.Hurt:
                {
                    string n = WithHold("damagehit");
                    if (!n.StartsWith("damagehit")) n = "damagehit";
                    var set = OriginalArt.Penguin(n);
                    if (set == null) break;
                    // back to the idle/aim pose afterwards: Penguin keeps Hurt only while its hurt timer runs
                    Play(n, set, 0, -1, false, onBackToStateAfterHurt ?? (onBackToStateAfterHurt = AfterHurt));
                    break;
                }
                case AvatarState.Dead:
                    Once("dying", force);
                    break;
                case AvatarState.Drown:
                    IntroLoop("fall", force);
                    break;
                case AvatarState.Celebrate:
                    Loop("win", force);
                    break;
                case AvatarState.Sad:
                    Loop("lose_01", force);
                    break;
            }
            RefreshWeaponVisibility();
        }

        Action onBackToStateAfterHurt;
        void AfterHurt()
        {
            if (state == AvatarState.Hurt) Loop(WithHold("idle"), true);
            else PlayState(state, true);
        }

        static bool IsIdleAnim(string n) => n == "idle" || n == "idle01" || n == "idle02" || n == "idle03";

        /// <summary>tuxwars AvatarGameObject.getIdleAnimation: idle 70% of the time, else one of the three fidgets.</summary>
        void IdleCycle()
        {
            if (state != AvatarState.Idle || hold != null) { PlayState(state, true); return; }
            string n = "idle";
            if (animName != null && rng.Next(100) >= 70) n = "idle0" + (1 + rng.Next(3));
            var set = OriginalArt.Penguin(n) ?? OriginalArt.Penguin("idle");
            Play(set.Path.Substring(set.Path.LastIndexOf('/') + 1), set, 0, -1, false, onIdleDone);
        }

        void Loop(string n, bool force)
        {
            if (!force && n == animName && player.Loop) return;
            var set = OriginalArt.Penguin(n);
            if (set != null) Play(n, set, 0, -1, true, null);
        }

        void Once(string n, bool force)
        {
            if (!force && n == animName) return;
            var set = OriginalArt.Penguin(n);
            if (set != null) Play(n, set, 0, -1, false, null);
        }

        /// <summary>Lead-in then the "loop" section on repeat (walk, fall, jump with an item); once if there is no loop label.</summary>
        void IntroLoop(string n, bool force)
        {
            if (!force && n == animName) return;
            var set = OriginalArt.Penguin(n);
            if (set == null) return;
            if (!set.Segment("loop", out int a, out int b)) { Play(n, set, 0, -1, false, null); return; }
            Play(n, set, 0, b, false, () => { if (animName == n) player.Play(set, a, b, true); });
        }

        void LoopSegment(string n, string label, bool force)
        {
            if (!force && n == animName && player.Loop) return;
            var set = OriginalArt.Penguin(n);
            if (set == null) return;
            if (set.Segment(label, out int a, out int b)) Play(n, set, a, b, true, null);
            else Play(n, set, 0, -1, true, null);
        }

        void Play(string n, SpriteAnimSet set, int from, int to, bool loop, Action done)
        {
            if (n != animName)
            {
                animName = n;
                rig = PenguinRigData.Get(n);
            }
            player.Play(set, from, to, loop, done);
            shownFrame = -1;
            ApplyFrame();
            RefreshWeaponVisibility();
        }

        void RefreshWeaponVisibility()
        {
            bool show = hold != null && rig != null && rig.Has(PenguinRigData.Slot.Tool) &&
                        state != AvatarState.Dead && state != AvatarState.Drown &&
                        state != AvatarState.Celebrate && state != AvatarState.Sad;
            if (aimNode.gameObject.activeSelf != show) aimNode.gameObject.SetActive(show);
        }

        // ------------------------------------------------------------------ per frame

        public void Tick(float dt)
        {
            if (player.Tick(dt) || shownFrame != player.Frame) ApplyFrame();
            if (weaponPlayer.Tick(dt)) ShowWeaponFrame();

            // drowning: sink and bob (the original used the fall animation and splash particles)
            if (state == AvatarState.Drown)
            {
                drownT += dt;
                spriteRoot.localPosition = new Vector3(0, (-0.35f + 0.1f * Mathf.Sin(drownT * 4f)) * scale, 0);
            }
            else if (drownT != 0f) { drownT = 0f; spriteRoot.localPosition = Vector3.zero; }

            if (flash > 0f || tinted)
            {
                flash = Mathf.Max(0f, flash - dt / 0.25f);
                var c = Color.Lerp(Color.white, HitTint, flash);
                body.color = c;
                weaponSr.color = c;
                for (int i = 0; i < clothes.Length; i++) clothes[i].color = c;
                tinted = flash > 0f;
            }
        }

        void ApplyFrame()
        {
            var set = player.Set;
            int f = player.Frame;
            shownFrame = f;
            body.sprite = set != null ? set.FrameAt(f) : null;
            for (int i = 0; i < PenguinRigData.SlotCount; i++)
            {
                var track = rig != null ? rig.Slots[i] : null;
                bool on = track != null && track.Length > 0 && track[Mathf.Min(f, track.Length - 1)].on;
                if (i < clothes.Length)
                {
                    var sr = clothes[i];
                    bool vis = on && sr.sprite != null;
                    if (sr.enabled != vis) sr.enabled = vis;
                    if (!vis) continue;
                }
                else
                {
                    // the tool slot (held weapon, sprite or 3D fallback) follows the slot's presence
                    var tg = slotNodes[i].gameObject;
                    if (tg.activeSelf != on) tg.SetActive(on);
                    if (!on) continue;
                }
                var fr = track[Mathf.Min(f, track.Length - 1)];
                var n = slotNodes[i];
                n.localPosition = fr.pos;
                n.localRotation = Quaternion.Euler(0, 0, fr.angle);
                n.localScale = fr.scale;
                if (i < clothes.Length)
                {
                    clothes[i].sortingOrder = fr.order;
                    clothes[i].transform.localRotation = Quaternion.Euler(0, 0, fr.angle2);
                }
                else weaponSr.sortingOrder = fr.order;
            }
        }

        public void Destroy()
        {
            if (weapon3D != null) UnityEngine.Object.Destroy(weapon3D);
            if (spriteRoot != null) UnityEngine.Object.Destroy(spriteRoot.gameObject);
        }

        // ------------------------------------------------------------------ team ring

        static Sprite ringSprite;
        /// <summary>Flat ellipse outline drawn under the feet in the team colour (the original tinted the body's
        /// "colorable" clip, which the flattened sprites cannot do without a colour mask).</summary>
        internal static Sprite RingSprite
        {
            get
            {
                if (ringSprite) return ringSprite;
                const int w = 128, h = 40;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;
                    float d = Mathf.Sqrt(u * u + v * v);
                    float edge = Mathf.Clamp01((1f - d) * 18f) * Mathf.Clamp01((d - 0.72f) * 18f);   // ring
                    float fill = Mathf.Clamp01((1f - d) * 18f) * 0.28f;                               // soft inner disc
                    byte a = (byte)(255 * Mathf.Clamp01(Mathf.Max(edge, fill)));
                    px[y * w + x] = new Color32(255, 255, 255, a);
                }
                tex.SetPixels32(px);
                tex.Apply();
                // 1.6 x 0.5 units at scale 1 (about the original's feet width)
                ringSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), w / 1.6f);
                return ringSprite;
            }
        }
    }
}
