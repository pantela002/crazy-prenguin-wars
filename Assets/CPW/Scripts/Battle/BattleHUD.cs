using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// Battle HUD and input, built from UI.* on its own overlay canvas (landscape, safe area).
    /// Touch: hold the arrow buttons to walk, JUMP, drag on the battlefield to aim (direction and distance from
    /// the penguin = angle and power, like the original power bar), FIRE. Two fingers pinch-zoom and pan;
    /// one finger pans when not aiming. Keyboard (editor): A/D or arrows walk, W/Space jump, Enter fire,
    /// Q/E change weapon, Esc pause, mouse wheel zoom, right mouse pans.
    /// </summary>
    [DefaultExecutionOrder(1000)]   // after BattleCamera, so tags/aim use this frame's camera
    public partial class BattleHUD : MonoBehaviour
    {
        BattleController c;
        BattleCamera cam;
        RectTransform root, safe, tagsLayer, controls, panelLayer, overlay;
        CanvasGroup controlsGroup;

        // top
        Text matchClock, turnSeconds, turnName;
        Image turnRing;
        class Row
        {
            public Image bg, ring, hp, hpTrail; public Text name, score, turnMark; public CanvasGroup group;
            public float shownScore = float.NaN, shownHp = -1f, trailHp = -1f;
        }
        readonly List<Row> rows = new List<Row>();
        int shownMatchSec = -1, shownTurnSec = -1, shownActive = -2;

        // bottom
        HoldButton leftHold, rightHold;
        Button jumpBtn, fireBtn, weaponBtn, boosterBtn, emoteBtn, pauseBtn;
        Image weaponIcon, apFill;
        Text weaponFallback, weaponAmmo, boosterLabel;
        string shownWeapon;
        int shownAmmo = int.MinValue;
        readonly Dictionary<string, Pulse> highlights = new Dictionary<string, Pulse>();

        // banner / hint
        Text bannerText;
        RectTransform bannerRt;
        BannerFade banner;
        bool hintCompact;
        RectTransform hintBox;
        Text hintTitle, hintBody;

        // overhead name tags
        class Tag { public RectTransform rt; public Text name; public Image hp, hpTrail; public Text arrow; public GameObject go; public float shownHp = -1f, trailHp = -1f; }
        readonly List<Tag> tags = new List<Tag>();

        // aim visuals (world space)
        LineRenderer traj, arrow, cross;
        readonly List<Vector2> trajPts = new List<Vector2>(256);
        readonly Vector3[] lineBuf = new Vector3[256];
        float shownAimAngle = float.NaN, shownAimPower = float.NaN;
        string shownTrajItem;
        Vector2 shownTrajOrigin;
        Vector2 pointTarget;
        bool pointSet;

        // input state
        readonly bool[] fingerOverUI = new bool[16];
        bool pinching, mouseOverUI, mouseDown;
        PointerEventData uiPointer;
        EventSystem uiPointerEs;
        static readonly List<RaycastResult> uiHits = new List<RaycastResult>(8);
        float lastPinchDist;
        Vector2 lastPinchMid, lastMouse;
        int lastDir;

        static readonly string[] Nums = BuildNums();
        static string[] BuildNums() { var a = new string[1000]; for (int i = 0; i < a.Length; i++) a[i] = i.ToString(); return a; }
        static string Num(int n) => n >= 0 && n < Nums.Length ? Nums[n] : n.ToString();

        public static BattleHUD Create(Transform parent, BattleController controller)
        {
            var go = new GameObject("BattleHUD", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;     // below UI popups (10)
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = UI.ScalerMatch;
            go.AddComponent<GraphicRaycaster>();
            var h = go.AddComponent<BattleHUD>();
            h.c = controller;
            h.cam = controller.Cam;
            h.root = (RectTransform)go.transform;
            h.Build();
            return h;
        }

        // ================================================================ build

        void Build()
        {
            tagsLayer = UI.Stretch(UI.Rect(root, "Tags"));
            safe = UI.Stretch(UI.Rect(root, "Safe"));
            safe.gameObject.AddComponent<SafeAreaFitter>();
            BuildTags();
            BuildTop();
            BuildControls();
            BuildFeatures();
            BuildBanner();
            panelLayer = UI.Stretch(UI.Rect(root, "Panels"));
            // above the panels (never takes touches): tutorial hints must stay readable over the weapon menu
            overlay = UI.Stretch(UI.Rect(root, "Overlay"));
            overlay.gameObject.AddComponent<SafeAreaFitter>();
            BuildHint();
            BuildAimVisuals();
        }

        /// <summary>A HUD button that acts on finger lift (TapAction) instead of Button.onClick.</summary>
        Button TapButton(Transform parent, string text, System.Action click, UI.ButtonStyle style, int fontSize = 40)
        {
            var b = UI.Button(parent, text, null, style, fontSize);
            if (click != null) TapAction.On(b, click);
            return b;
        }

        Button HudButton(RectTransform parent, string iconPath, string text, System.Action click, UI.ButtonStyle style,
            Vector2 anchor, Vector2 size, Vector2 offset, int fontSize = 48)
        {
            var icon = string.IsNullOrEmpty(iconPath) ? null : ModelLibrary.Icon(iconPath);
            var b = TapButton(parent, icon != null ? null : text, click, style, fontSize);
            UI.Place((RectTransform)b.transform, anchor, size, offset);
            if (icon != null)
            {
                var img = UI.Image(b.transform, icon);
                UI.Anchor(img.rectTransform, 0.12f, 0.12f, 0.88f, 0.88f);
            }
            return b;
        }

        void BuildTop()
        {
            // players panel (top-left, left of the match clock, above the earnings counter; the challenges hang on
            // the right): one compact row per penguin (up to 4) with a team-colour portrait, name, points and an
            // HP bar; the penguin whose turn it is gets a team-colour row, a white portrait ring and a marker
            var board = UI.Panel(safe, new Color(0, 0, 0, 0.45f), true, "Players");
            int n = c.Penguins.Count;
            UI.Place(board.rectTransform, new Vector2(0, 1), new Vector2(PlayersW, 20 + n * RowPitch), new Vector2(20, -20));
            board.raycastTarget = false;
            var face = ModelLibrary.Icon("Emoticons/EmoticonNice");
            for (int i = 0; i < n; i++)
            {
                var p = c.Penguins[i];
                var row = new Row();
                row.bg = UI.Panel(board.transform, new Color(1, 1, 1, 0f), true, "Row" + i);
                UI.Place(row.bg.rectTransform, new Vector2(0, 1), new Vector2(PlayersW - 20, RowPitch - 6), new Vector2(10, -10 - i * RowPitch));
                row.group = row.bg.gameObject.AddComponent<CanvasGroup>();
                // portrait: team-colour disc (its ring doubles as the turn highlight) with the penguin face on top
                row.ring = UI.Image(row.bg.transform, UI.Circle, p.TeamColor, false, "Portrait");
                UI.Place(row.ring.rectTransform, new Vector2(0, 0.5f), new Vector2(50, 50), new Vector2(4, 0));
                var disc = UI.Image(row.ring.transform, UI.Circle, Color.Lerp(p.TeamColor, Color.black, 0.35f), false, "Disc");
                UI.Stretch(disc.rectTransform, 4, 4, 4, 4);
                if (face != null)
                {
                    var fi = UI.Image(disc.transform, face, Color.white, true, "Face");
                    UI.Stretch(fi.rectTransform, 2, 2, 2, 2);
                }
                else
                {
                    var init = UI.Label(disc.transform, string.IsNullOrEmpty(p.DisplayName) ? "?" : p.DisplayName.Substring(0, 1).ToUpperInvariant(), 28, Color.white, TextAnchor.MiddleCenter, true);
                    UI.Stretch(init.rectTransform);
                }
                row.name = UI.Label(row.bg.transform, p.DisplayName, 28, Color.white, TextAnchor.MiddleLeft, true);
                UI.Anchor(row.name.rectTransform, 0.15f, 0.42f, 0.72f, 1f);
                row.name.resizeTextForBestFit = true; row.name.resizeTextMinSize = 16; row.name.resizeTextMaxSize = 28;
                row.hp = UI.Bar(row.bg.transform, Theme.Good);
                UI.Anchor((RectTransform)row.hp.transform.parent, 0.15f, 0.1f, 0.72f, 0.4f);
                row.hpTrail = TrailUnder(row.hp, 3);
                row.score = UI.Label(row.bg.transform, "0", 34, Theme.Primary, TextAnchor.MiddleRight, true);
                UI.Anchor(row.score.rectTransform, 0.72f, 0, 0.97f, 1);
                row.turnMark = UI.Label(row.bg.transform, ">", 30, Color.white, TextAnchor.MiddleCenter, true);
                UI.Place(row.turnMark.rectTransform, new Vector2(0, 0.5f), new Vector2(24, 40), new Vector2(-22, 0));
                row.turnMark.gameObject.SetActive(false);
                foreach (var g in row.bg.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
                rows.Add(row);
            }

            // match clock and turn timer (top-center)
            var clockBg = UI.Panel(safe, new Color(0, 0, 0, 0.5f), true, "MatchClock");
            clockBg.raycastTarget = false;
            UI.Place(clockBg.rectTransform, new Vector2(0.5f, 1), new Vector2(240, 80), new Vector2(0, -16));
            matchClock = UI.Label(clockBg.transform, "0:00", 52, Color.white, TextAnchor.MiddleCenter, true);
            UI.Stretch(matchClock.rectTransform);

            var ringBg = UI.Image(safe, UI.Circle, new Color(0, 0, 0, 0.5f), false, "TurnTimer");
            UI.Place(ringBg.rectTransform, new Vector2(0.5f, 1), new Vector2(124, 124), new Vector2(0, -104));
            turnRing = UI.Image(ringBg.transform, UI.Circle, Theme.Good, false, "Ring");
            turnRing.type = Image.Type.Filled;
            turnRing.fillMethod = Image.FillMethod.Radial360;
            turnRing.fillOrigin = (int)Image.Origin360.Top;
            UI.Stretch(turnRing.rectTransform, 6, 6, 6, 6);
            var inner = UI.Image(ringBg.transform, UI.Circle, new Color(0.08f, 0.12f, 0.2f, 1f), false, "Inner");
            UI.Stretch(inner.rectTransform, 18, 18, 18, 18);
            turnSeconds = UI.Label(ringBg.transform, "", 54, Color.white, TextAnchor.MiddleCenter, true);
            UI.Stretch(turnSeconds.rectTransform);
            turnName = UI.Label(safe, "", 34, Color.white, TextAnchor.MiddleCenter, true);
            UI.Place(turnName.rectTransform, new Vector2(0.5f, 1), new Vector2(700, 50), new Vector2(0, -232));

            // pause + emote (top-right)
            pauseBtn = HudButton(safe, "Hud/Pause", "II", OpenPause, UI.ButtonStyle.Dark, new Vector2(1, 1), new Vector2(112, 112), new Vector2(-20, -20), 52);
            emoteBtn = HudButton(safe, "Hud/Emote", ":)", OpenEmotes, UI.ButtonStyle.Secondary, new Vector2(1, 1), new Vector2(112, 112), new Vector2(-148, -20), 52);
            // chat (online only) sits left of the emote button; challenges list hangs below these (BattleHUD.Challenges)
        }

        void BuildControls()
        {
            controls = UI.Stretch(UI.Rect(safe, "Controls"));
            controlsGroup = controls.gameObject.AddComponent<CanvasGroup>();

            // walk + jump (bottom-left)
            var left = HudButton(controls, "Hud/WalkLeft", "<", null, UI.ButtonStyle.Dark, new Vector2(0, 0), new Vector2(210, 190), new Vector2(24, 24), 96);
            leftHold = left.gameObject.AddComponent<HoldButton>();
            var right = HudButton(controls, "Hud/WalkRight", ">", null, UI.ButtonStyle.Dark, new Vector2(0, 0), new Vector2(210, 190), new Vector2(254, 24), 96);
            rightHold = right.gameObject.AddComponent<HoldButton>();
            jumpBtn = HudButton(controls, "Hud/Jump", "JUMP", () => c.ActJump(lastDir != 0 ? lastDir : (c.Active != null ? c.Active.Facing : 1)),
                UI.ButtonStyle.Good, new Vector2(0, 0), new Vector2(210, 150), new Vector2(140, 234), 44);
            AddPulse("walk", left); AddPulse("walk2", right); AddPulse("jump", jumpBtn);

            // energy (action points) bar (bottom-center)
            var apLabel = UI.Label(controls, "ENERGY", 26, Color.white, TextAnchor.MiddleCenter, true);
            UI.Place(apLabel.rectTransform, new Vector2(0.5f, 0), new Vector2(300, 36), new Vector2(0, 74));
            apFill = UI.Bar(controls, Theme.Xp);
            apFill.transform.parent.GetComponent<Image>().raycastTarget = false;
            UI.Place((RectTransform)apFill.transform.parent, new Vector2(0.5f, 0), new Vector2(560, 44), new Vector2(0, 24));

            // fire, weapon, booster (bottom-right)
            fireBtn = HudButton(controls, "Hud/Fire", "FIRE", Fire, UI.ButtonStyle.Danger, new Vector2(1, 0), new Vector2(250, 250), new Vector2(-24, 24), 64);
            weaponBtn = TapButton(controls, null, OpenWeapons, UI.ButtonStyle.Dark);
            UI.Place((RectTransform)weaponBtn.transform, new Vector2(1, 0), new Vector2(200, 200), new Vector2(-294, 24));
            weaponIcon = UI.Image(weaponBtn.transform, null);
            UI.Anchor(weaponIcon.rectTransform, 0.1f, 0.22f, 0.9f, 0.95f);
            weaponFallback = UI.Label(weaponBtn.transform, "", 28, Color.white);
            UI.Anchor(weaponFallback.rectTransform, 0.05f, 0.25f, 0.95f, 0.95f);
            weaponAmmo = UI.Label(weaponBtn.transform, "", 34, Theme.Primary, TextAnchor.MiddleCenter, true);
            UI.Anchor(weaponAmmo.rectTransform, 0.05f, 0.0f, 0.95f, 0.26f);
            boosterBtn = TapButton(controls, null, OpenBoosters, UI.ButtonStyle.Secondary);
            UI.Place((RectTransform)boosterBtn.transform, new Vector2(1, 0), new Vector2(200, 110), new Vector2(-294, 236));
            boosterLabel = UI.Label(boosterBtn.transform, Loc.Has("HUD_CHANGE_BOOSTER") ? "BOOST" : "BOOST", 34, Color.white, TextAnchor.MiddleCenter, true);
            UI.Stretch(boosterLabel.rectTransform, 8, 8, 4, 4);
            AddPulse("fire", fireBtn); AddPulse("weapon", weaponBtn);
            BuildBoosterCooldown();
        }

        void AddPulse(string key, Component b) => highlights[key] = b.gameObject.AddComponent<Pulse>();

        const float BannerY = 170f, BannerYBelowHint = -70f;

        void BuildBanner()
        {
            bannerRt = UI.Rect(safe, "Banner");
            UI.Place(bannerRt, new Vector2(0.5f, 0.5f), new Vector2(1400, 160), new Vector2(0, BannerY));
            var g = bannerRt.gameObject.AddComponent<CanvasGroup>();
            g.blocksRaycasts = false; g.interactable = false;
            bannerText = UI.Label(bannerRt, "", 96, Color.white, TextAnchor.MiddleCenter, true);
            UI.Stretch(bannerText.rectTransform);
            banner = bannerRt.gameObject.AddComponent<BannerFade>();
            banner.group = g;
            bannerRt.gameObject.SetActive(false);
        }

        void BuildHint()
        {
            var hint = UI.Panel(overlay, Theme.Panel, true, "Hint");
            hint.raycastTarget = false;
            hintBox = hint.rectTransform;
            hintTitle = UI.Label(hintBox, "", 44, Theme.Secondary, TextAnchor.MiddleCenter, true);
            hintBody = UI.Label(hintBox, "", 34, Theme.Text, TextAnchor.MiddleCenter);
            hintCompact = true;
            LayoutHint(false);
            hintBox.gameObject.SetActive(false);
        }

        /// <summary>Full hint under the turn timer, or a slim strip above the window while a panel is open.</summary>
        void LayoutHint(bool compact)
        {
            if (compact == hintCompact) return;
            hintCompact = compact;
            if (compact)
            {
                UI.Place(hintBox, new Vector2(0.5f, 1), new Vector2(1100, 100), new Vector2(0, -4));   // clear of the window X button
                hintTitle.gameObject.SetActive(false);
                UI.Anchor(hintBody.rectTransform, 0.02f, 0.04f, 0.98f, 0.96f);
            }
            else
            {
                UI.Place(hintBox, new Vector2(0.5f, 1), new Vector2(1040, 220), new Vector2(0, -290));
                hintTitle.gameObject.SetActive(true);
                UI.Anchor(hintTitle.rectTransform, 0.03f, 0.66f, 0.97f, 0.97f);
                UI.Anchor(hintBody.rectTransform, 0.04f, 0.05f, 0.96f, 0.68f);
            }
        }

        void BuildTags()
        {
            foreach (var p in c.Penguins)
            {
                var t = new Tag();
                t.rt = UI.Rect(tagsLayer, "Tag " + p.PlayerIndex);
                t.rt.sizeDelta = new Vector2(220, 80);
                t.rt.pivot = new Vector2(0.5f, 0f);
                t.go = t.rt.gameObject;
                t.name = UI.Label(t.rt, p.DisplayName, 28, p.TeamColor, TextAnchor.MiddleCenter, true);
                UI.Anchor(t.name.rectTransform, 0, 0.4f, 1, 1);
                t.hp = UI.Bar(t.rt, Theme.Good);
                UI.Anchor((RectTransform)t.hp.transform.parent, 0.15f, 0.08f, 0.85f, 0.36f);
                t.hpTrail = TrailUnder(t.hp, 6);
                // team-colour frame around the bar so the bar itself says whose penguin this is
                var frame = t.hp.transform.parent.GetComponent<Image>();
                if (frame != null) frame.color = new Color(p.TeamColor.r * 0.5f, p.TeamColor.g * 0.5f, p.TeamColor.b * 0.5f, 0.85f);
                t.arrow = UI.Label(t.rt, "v", 48, Theme.Primary, TextAnchor.MiddleCenter, true);
                UI.Place(t.arrow.rectTransform, new Vector2(0.5f, 1), new Vector2(60, 60), new Vector2(0, 56));
                foreach (var g in t.go.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
                tags.Add(t);
            }
        }

        void BuildAimVisuals()
        {
            traj = MakeLine("Trajectory", new Color(1, 1, 1, 0.55f), 0.12f);
            arrow = MakeLine("AimArrow", new Color(1f, 0.8f, 0.2f, 0.9f), 0.28f);
            cross = MakeLine("Crosshair", new Color(1f, 0.3f, 0.2f, 0.9f), 0.15f);
            cross.loop = true;
        }

        LineRenderer MakeLine(string name, Color col, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(c.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.sharedMaterial = Mats.Transparent(Color.white);   // tint comes from vertex colors only
            lr.startColor = lr.endColor = col;
            lr.startWidth = width;
            lr.endWidth = width * 0.6f;
            lr.numCapVertices = 2;
            lr.positionCount = 0;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return lr;
        }

        // ================================================================ events from the controller

        public void OnTurnStart(Penguin p)
        {
            pointSet = false;
            shownAimAngle = float.NaN;
            shownWeapon = null;
            CloseTransientPanels();
            turnName.text = p.DisplayName;
            turnName.color = p.TeamColor;
        }

        public void OnTurnEnd()
        {
            CloseTransientPanels();
            HideAimVisuals();
        }

        public void Banner(string text, Color color, float hold)
        {
            bannerText.text = text;
            bannerText.color = color;
            PlaceBanner();
            banner.Restart(hold);
        }

        /// <summary>The banner and the tutorial hint share the upper middle: move the banner under the hint while one shows.</summary>
        void PlaceBanner()
        {
            float y = hintBox != null && hintBox.gameObject.activeSelf && !hintCompact ? BannerYBelowHint : BannerY;
            if (bannerRt.anchoredPosition.y != y) bannerRt.anchoredPosition = new Vector2(0, y);
        }

        public void ShowHint(string title, string body)
        {
            hintTitle.text = title;
            hintBody.text = body;
            hintBox.gameObject.SetActive(true);
        }

        public void HideHint() => hintBox.gameObject.SetActive(false);

        /// <summary>Pulse a control: "walk", "jump", "fire", "weapon"; null clears.</summary>
        public void Highlight(string key)
        {
            foreach (var kv in highlights) kv.Value.on = key != null && (kv.Key == key || (key == "walk" && kv.Key == "walk2"));
        }

        // ================================================================ per frame

        void Update()
        {
            if (c == null) return;
            UpdateInput();
            UpdateTop();
            UpdateControls();
            if (hintBox.gameObject.activeSelf) LayoutHint(AnyPanelOpen);
            if (bannerRt.gameObject.activeSelf) PlaceBanner();
            UpdateFeatures(Time.unscaledDeltaTime);
        }

        void LateUpdate()
        {
            if (c == null) return;
            UpdateTags();
            UpdateAimVisuals();
        }

        void UpdateTop()
        {
            int ms = Mathf.Max(0, Mathf.CeilToInt(c.MatchTimeLeft));
            if (ms != shownMatchSec)
            {
                shownMatchSec = ms;
                matchClock.text = (ms / 60) + ":" + (ms % 60).ToString("00");
                matchClock.color = ms <= 12 ? Theme.Danger : Color.white;
            }
            bool turnRunning = c.CurrentPhase == BattleController.Phase.Turn;
            int ts = turnRunning ? Mathf.Max(0, Mathf.CeilToInt(c.TurnTimeLeft)) : 0;
            if (ts != shownTurnSec)
            {
                shownTurnSec = ts;
                turnSeconds.text = turnRunning ? Num(ts) : "";
                turnRing.color = ts <= 3 ? Theme.Danger : Theme.Good;
            }
            float frac = c.Fired ? c.TurnTimeLeft / Mathf.Max(0.1f, BattleRules.TimeAfterFiring) : c.TurnTimeLeft / Mathf.Max(0.1f, c.TurnDuration);
            turnRing.fillAmount = turnRunning ? Mathf.Clamp01(frac) : 0f;

            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < rows.Count; i++)
            {
                var p = c.Penguins[i];
                var r = rows[i];
                // points tick up (or down) towards the real score instead of jumping
                if (float.IsNaN(r.shownScore)) { r.shownScore = p.Score; r.score.text = Num(p.Score); }
                else if (Mathf.RoundToInt(r.shownScore) != p.Score)
                {
                    float step = Mathf.Max(12f, Mathf.Abs(p.Score - r.shownScore) * 4f) * dt;
                    r.shownScore = Mathf.MoveTowards(r.shownScore, p.Score, step);
                    r.score.text = Num(Mathf.RoundToInt(r.shownScore));
                    r.score.color = Color.white;
                }
                else if (r.score.color != Theme.Primary) r.score.color = Theme.Primary;
                float f = p.Alive ? Mathf.Clamp01(p.HP / Mathf.Max(1f, p.MaxHP)) : 0f;
                AnimateHp(r.hp, r.hpTrail, ref r.shownHp, ref r.trailHp, f, dt);
                float alpha = p.Alive && !p.Left ? 1f : 0.45f;
                if (r.group.alpha != alpha) r.group.alpha = alpha;
            }
            if (c.ActiveIndex != shownActive)
            {
                shownActive = c.ActiveIndex;
                for (int i = 0; i < rows.Count; i++)
                {
                    bool on = i == shownActive;
                    var tc = c.Penguins[i].TeamColor;
                    rows[i].bg.color = on ? new Color(tc.r, tc.g, tc.b, 0.35f) : new Color(1, 1, 1, 0f);
                    rows[i].ring.color = on ? Color.white : tc;
                    rows[i].turnMark.gameObject.SetActive(on);
                }
            }
            if (shownActive >= 0 && shownActive < rows.Count)
                rows[shownActive].turnMark.rectTransform.anchoredPosition = new Vector2(-22 + Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f)) * 6f, 0);
        }

        void UpdateControls()
        {
            bool input = c.IsInputTurn;
            if (c.CurrentPhase == BattleController.Phase.Over) return;
            float alpha = input ? 1f : 0.35f;
            if (controlsGroup.alpha != alpha) { controlsGroup.alpha = alpha; controlsGroup.interactable = input; controlsGroup.blocksRaycasts = input; }

            var a = c.Active;
            apFill.fillAmount = a != null && input ? Mathf.Clamp01(a.ActionPoints / Mathf.Max(1, a.MaxActionPoints)) : 0f;
            fireBtn.interactable = input && c.AttacksLeft > 0 && a != null && a.Ammo.Has(c.SelectedItem(c.ActiveIndex));
            jumpBtn.interactable = input && a != null && a.ActionPoints >= a.JumpCost;
            boosterBtn.interactable = input && !c.BoosterUsedThisTurn && a != null && a.Ammo.Boosters.Count > 0;
            weaponBtn.interactable = input && !c.Fired;

            var vp = input ? a : c.ViewPenguin;
            string w = vp != null ? c.SelectedItem(vp.PlayerIndex) : null;
            int ammo = vp != null && w != null ? vp.Ammo.Count(w) : 0;
            if (w != shownWeapon || ammo != shownAmmo)
            {
                shownWeapon = w; shownAmmo = ammo;
                var icon = w != null ? BattleItems.Icon(w) : null;
                weaponIcon.sprite = icon;
                weaponIcon.enabled = icon != null;
                weaponFallback.text = icon == null && w != null ? BattleItems.Name(w) : "";
                weaponAmmo.text = w == null ? "" : ammo == Loadout.Infinite ? "∞" : "x" + Num(ammo);
            }
            emoteBtn.interactable = c.ViewPenguin != null && c.ViewPenguin.CanEmote && c.CurrentPhase != BattleController.Phase.Over;
        }

        const float PlayersW = 440f, RowPitch = 62f;   // the earnings counter (BattleHUD.Rewards) sits below

        /// <summary>A pale "damage taken" bar behind an HP fill; it trails the fill down after a hit.</summary>
        static Image TrailUnder(Image fill, float inset)
        {
            var trail = UI.Image(fill.transform.parent, UI.WhiteSprite, new Color(1f, 0.95f, 0.8f, 0.85f), false, "Trail");
            trail.type = Image.Type.Filled;
            trail.fillMethod = Image.FillMethod.Horizontal;
            trail.fillAmount = 1;
            UI.Stretch(trail.rectTransform, inset, inset, inset, inset);
            trail.raycastTarget = false;
            trail.transform.SetSiblingIndex(fill.transform.GetSiblingIndex());
            return trail;
        }

        /// <summary>Smooth HP: the fill slides to the new value, the pale trail lags behind and follows; heals grow
        /// the fill smoothly. Display only (no effect on the simulation).</summary>
        static void AnimateHp(Image fill, Image trail, ref float shown, ref float trailed, float target, float dt)
        {
            if (shown < 0) { shown = trailed = target; }
            shown = Mathf.MoveTowards(shown, target, Mathf.Max(0.6f, Mathf.Abs(target - shown) * 6f) * dt);
            if (trailed < shown) trailed = shown;
            else trailed = Mathf.MoveTowards(trailed, shown, (trailed - shown > 0.02f ? 0.35f : 1f) * dt);
            fill.fillAmount = shown;
            if (trail != null) trail.fillAmount = trailed;
            var hc = shown > 0.5f ? Theme.Good : (shown > 0.25f ? Theme.Primary : Theme.Danger);
            if (fill.color != hc) fill.color = hc;
        }

        void UpdateTags()
        {
            if (cam == null) cam = c.Cam;
            bool turn = c.CurrentPhase == BattleController.Phase.Turn;
            float lift = BattleRules.Radius * 2.8f;
            for (int i = 0; i < tags.Count; i++)
            {
                var p = c.Penguins[i];
                var t = tags[i];
                bool show = p.Alive && c.CurrentPhase != BattleController.Phase.Over;
                if (t.go.activeSelf != show) t.go.SetActive(show);
                if (!show) continue;
                Vector2 sp = cam.WorldToScreen((Vector2)p.transform.position + Vector2.up * lift);
                t.rt.position = new Vector3(sp.x, sp.y, 0);
                float f = Mathf.Clamp01(p.HP / Mathf.Max(1f, p.MaxHP));
                AnimateHp(t.hp, t.hpTrail, ref t.shownHp, ref t.trailHp, f, Time.unscaledDeltaTime);
                bool act = turn && i == c.ActiveIndex;
                if (t.arrow.gameObject.activeSelf != act) t.arrow.gameObject.SetActive(act);
                if (act) t.arrow.rectTransform.anchoredPosition = new Vector2(0, 56 + Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f)) * 14f);
            }
        }

        // ================================================================ input

        string CurrentItem => c.Active != null ? c.SelectedItem(c.ActiveIndex) : null;

        bool CanAimNow
        {
            get
            {
                if (!c.IsInputTurn || c.Fired || c.AttacksLeft <= 0) return false;
                var item = CurrentItem;
                return !string.IsNullOrEmpty(item) && WeaponSystem.Targeting(item) != TargetingMode.Activation;
            }
        }

        void UpdateInput()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && c.CurrentPhase != BattleController.Phase.Over
                && ScreenManager.EscapeUsedFrame != Time.frameCount)
            {
                if (AnyPanelOpen) CloseAllPanels(); else OpenPause();
            }
            bool input = c.IsInputTurn && !AnyPanelOpen;

            // walking: hold buttons or keys
            int dir = 0;
            if (rightHold.Held || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) dir++;
            if (leftHold.Held || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) dir--;
            if (input) c.ActWalk(dir);
            else if (lastDir != 0 && c.IsInputTurn) c.ActWalk(0);
            lastDir = dir;

            if (input)
            {
                if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.UpArrow))
                    c.ActJump(dir != 0 ? dir : 0);
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Fire();
                if (Input.GetKeyDown(KeyCode.Q)) CycleWeapon(-1);
                if (Input.GetKeyDown(KeyCode.E)) CycleWeapon(1);
            }
            if (!AnyPanelOpen && c.CurrentPhase != BattleController.Phase.Curtain) HandlePointers();
        }

        void HandlePointers()
        {
            var es = EventSystem.current;
            int tc = Input.touchCount;
            if (tc > 0)
            {
                Touch t0 = default, t1 = default;
                int n = 0;
                for (int i = 0; i < tc; i++)
                {
                    var t = Input.GetTouch(i);
                    int id = Mathf.Clamp(t.fingerId, 0, fingerOverUI.Length - 1);
                    if (t.phase == TouchPhase.Began) fingerOverUI[id] = OverUI(es, t.position);
                    if (fingerOverUI[id]) continue;
                    if (n == 0) t0 = t; else if (n == 1) t1 = t;
                    n++;
                }
                if (n >= 2)
                {
                    float dist = Vector2.Distance(t0.position, t1.position);
                    Vector2 mid = (t0.position + t1.position) * 0.5f;
                    if (pinching && lastPinchDist > 1f)
                    {
                        cam.ZoomBy(dist / lastPinchDist);
                        cam.Pan(cam.ScreenToWorld(lastPinchMid) - cam.ScreenToWorld(mid));
                    }
                    pinching = true;
                    lastPinchDist = dist;
                    lastPinchMid = mid;
                }
                else if (n == 1)
                {
                    if (pinching) { pinching = false; return; }   // lifting one finger of a pinch shouldn't aim
                    OnePointer(t0.position, t0.deltaPosition, t0.phase == TouchPhase.Began);
                }
                else pinching = false;
                return;
            }
            pinching = false;

            // mouse (editor / desktop)
            Vector2 mp = Input.mousePosition;
            if (Input.GetMouseButtonDown(0)) { mouseOverUI = OverUI(es, mp); mouseDown = true; lastMouse = mp; }
            if (Input.GetMouseButtonUp(0)) mouseDown = false;
            if (mouseDown && Input.GetMouseButton(0) && !mouseOverUI) OnePointer(mp, mp - lastMouse, Input.GetMouseButtonDown(0));
            if (Input.GetMouseButton(1) && !Input.GetMouseButtonDown(1)) cam.Pan(-(mp - lastMouse) * cam.WorldPerPixel);
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f) cam.ZoomBy(1f + wheel * 0.1f);
            lastMouse = mp;
        }

        /// <summary>Manual UI raycast: IsPointerOverGameObject is unreliable on TouchPhase.Began.</summary>
        bool OverUI(EventSystem es, Vector2 screen)
        {
            if (es == null) return false;
            if (uiPointer == null || uiPointerEs != es) { uiPointer = new PointerEventData(es); uiPointerEs = es; }
            uiPointer.Reset();
            uiPointer.position = screen;
            uiHits.Clear();
            es.RaycastAll(uiPointer, uiHits);
            int n = uiHits.Count;
            uiHits.Clear();
            return n > 0;
        }

        void OnePointer(Vector2 screen, Vector2 delta, bool began)
        {
            if (CanAimNow) AimAt(screen);
            else if (!began) cam.Pan(-delta * cam.WorldPerPixel);
        }

        /// <summary>Aim like the original power bar: direction and distance from the penguin give angle and power.</summary>
        void AimAt(Vector2 screen)
        {
            var a = c.Active;
            if (a == null) return;
            var item = CurrentItem;
            var mode = WeaponSystem.Targeting(item);
            Vector2 ps = cam.WorldToScreen((Vector2)a.transform.position);
            Vector2 d = screen - ps;
            if (mode == TargetingMode.Point)
            {
                pointTarget = cam.ScreenToWorld(screen);
                pointSet = true;
                var w = pointTarget - a.Position;
                c.ActAim(Mathf.Atan2(w.y, w.x) * Mathf.Rad2Deg, 1f);
                return;
            }
            if (d.magnitude < 30f) return;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float power = mode == TargetingMode.PowerBar ? Mathf.Clamp01(d.magnitude / (Screen.height * 0.36f)) : 1f;
            c.ActAim(angle, Mathf.Max(0.05f, power));
        }

        void Fire()
        {
            var a = c.Active;
            if (a == null || !c.IsInputTurn) return;
            var item = CurrentItem;
            if (string.IsNullOrEmpty(item)) { OpenWeapons(); return; }
            var mode = WeaponSystem.Targeting(item);
            Vector2 target;
            if (mode == TargetingMode.Point)
            {
                if (!pointSet) { Banner("Tap the target first", Color.white, 1f); return; }
                target = pointTarget;
            }
            else
            {
                float rad = a.AimAngle * Mathf.Deg2Rad;
                target = a.Position + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * 10f;
            }
            if (c.ActFire(target)) HideAimVisuals();
        }

        void CycleWeapon(int step)
        {
            var a = c.Active;
            if (a == null || a.Ammo.Weapons.Count == 0) return;
            var list = a.Ammo.Weapons;
            int i = Mathf.Max(0, list.IndexOf(CurrentItem));
            for (int k = 0; k < list.Count; k++)
            {
                i = (i + step + list.Count) % list.Count;
                if (a.Ammo.Has(list[i])) { c.ActSelectWeapon(list[i]); return; }
            }
        }

        // ================================================================ aim visuals

        void HideAimVisuals()
        {
            traj.enabled = false; arrow.enabled = false; cross.enabled = false;
        }

        void UpdateAimVisuals()
        {
            var a = c.Active;
            var item = CurrentItem;
            bool show = a != null && CanAimNow && a.Aiming;
            if (!show) { if (traj.enabled || arrow.enabled || cross.enabled) HideAimVisuals(); return; }
            var mode = WeaponSystem.Targeting(item);
            Vector2 origin = BattleController.ShotOrigin(a);   // same clamped origin DoFire uses
            float rad = a.AimAngle * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

            if (mode == TargetingMode.Point)
            {
                arrow.enabled = false; traj.enabled = false;
                cross.enabled = pointSet;
                if (pointSet)
                {
                    cross.positionCount = 12;
                    for (int i = 0; i < 12; i++)
                    {
                        float ang = i / 12f * Mathf.PI * 2f;
                        lineBuf[i] = new Vector3(pointTarget.x + Mathf.Cos(ang) * 1.2f, pointTarget.y + Mathf.Sin(ang) * 1.2f, -1f);
                    }
                    cross.SetPositions(lineBuf);
                }
                return;
            }
            cross.enabled = false;

            // power arrow
            arrow.enabled = true;
            float len = 1.2f + a.AimPower * 4f;
            arrow.positionCount = 2;
            arrow.SetPosition(0, new Vector3(origin.x, origin.y, -1f));
            arrow.SetPosition(1, new Vector3(origin.x + dir.x * len, origin.y + dir.y * len, -1f));
            var col = Color.Lerp(new Color(1f, 0.9f, 0.2f), new Color(1f, 0.25f, 0.15f), a.AimPower);
            arrow.startColor = arrow.endColor = col;

            // trajectory guide (only recomputed when the aim changes)
            if (!ProfileService.P.showTrajectory) { traj.enabled = false; return; }
            if (a.AimAngle != shownAimAngle || a.AimPower != shownAimPower || item != shownTrajItem || origin != shownTrajOrigin)
            {
                shownAimAngle = a.AimAngle; shownAimPower = a.AimPower; shownTrajItem = item; shownTrajOrigin = origin;
                bool ok = WeaponSystem.PredictTrajectory(item, origin, a.AimAngle, a.AimPower, trajPts);
                int n = ok ? Mathf.Min(trajPts.Count, lineBuf.Length) : 0;
                for (int i = 0; i < n; i++) lineBuf[i] = new Vector3(trajPts[i].x, trajPts[i].y, -1f);
                traj.positionCount = n;
                if (n > 0) traj.SetPositions(lineBuf);
                traj.enabled = n > 1;
            }
        }
    }
}
