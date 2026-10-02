using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// The original Flash HUD art on top of the procedural HUD (layout, behaviour and touch handling unchanged):
    /// square Move / Shoot buttons, the gear, emote and chat buttons, the stopwatch turn timer with the
    /// turn_countdown digits, the big match_count digits, the "Your Turn" / "1 Minute Left" messages, the orange
    /// arrow over the active penguin, and the world-space attack_power_bar / crosshair aim visuals.
    /// Every piece falls back to the procedural look when its bitmap is missing.
    /// </summary>
    public partial class BattleHUD
    {
        const string Ingame = "ingame", CharUi = "character_ui";

        Image walkArtL, walkArtR;
        Sprite walkIdle, walkHeld;
        bool walkHeldL, walkHeldR;
        UISpriteAnim turnCount, matchCount, bannerAnim;
        SpriteAnimSet powerBarSet;
        SpriteRenderer powerBar, crossArt;
        int shownPowerFrame = -1;
        string yourTurnText, minuteLeftText;

        /// <summary>Weapon / booster icon: the original item icon, else the 3D-rendered one.</summary>
        static Sprite ItemIcon(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return (UI.Skin.Off ? null : OriginalArt.Icon(id)) ?? BattleItems.Icon(id);
        }

        static Sprite HudArt(string swf, int bitmap) => UI.Skin.Bitmap(swf, bitmap);

        static SpriteAnimSet HudAnim(string swf, string symbol) => UI.Skin.Off ? null : OriginalArt.Ui(swf, symbol);

        /// <summary>
        /// Make art the whole button: the button image becomes a transparent hit area (same rect, so the tap target is
        /// unchanged) with the art on a child; icon children from HudButton and the caption are hidden.
        /// </summary>
        static Image ArtButton(Button b, Sprite art, bool mirror = false, bool keepChildren = false)
        {
            var bg = b.GetComponent<Image>();
            var fit = b.GetComponent<SkinFit>();
            if (fit != null) fit.enabled = false;
            foreach (var sh in b.GetComponents<Shadow>()) sh.enabled = false;
            if (!keepChildren)
                for (int i = 0; i < b.transform.childCount; i++)
                {
                    var ch = b.transform.GetChild(i);
                    if (ch.GetComponent<Text>() != null || ch.GetComponent<Image>() != null) ch.gameObject.SetActive(false);
                }
            bg.sprite = UI.WhiteSprite;
            bg.type = Image.Type.Simple;
            bg.color = new Color(1, 1, 1, 0);
            var img = UI.Image(b.transform, art, Color.white, true, "Art");
            UI.Stretch(img.rectTransform);
            img.raycastTarget = false;
            img.transform.SetAsFirstSibling();
            if (mirror) img.rectTransform.localScale = new Vector3(-1, 1, 1);
            b.targetGraphic = img;   // pressed / disabled tint on the art
            return img;
        }

        /// <summary>Replace (or add) the icon of a skinned HUD button.</summary>
        static void SetIcon(Button b, Sprite icon, float inset = 0.16f)
        {
            if (icon == null) return;
            var old = b.transform.Find("Image");
            Image img = old != null ? old.GetComponent<Image>() : null;
            if (img == null) img = UI.Image(b.transform, icon, Color.white, true, "Image");
            img.sprite = icon; img.color = Color.white; img.preserveAspect = true; img.raycastTarget = false;
            img.gameObject.SetActive(true);
            UI.Anchor(img.rectTransform, inset, inset, 1 - inset, 1 - inset);
            var cap = b.GetComponentInChildren<Text>();
            if (cap) cap.enabled = false;
        }

        void SkinHud()
        {
            if (UI.Skin.Off || !OriginalArt.Available) return;

            // top: gear pause, laughing emote, speech-bubble chat
            var gear = HudArt(Ingame, 497); if (gear != null) ArtButton(pauseBtn, gear);
            var emo = HudArt(Ingame, 403); if (emo != null) ArtButton(emoteBtn, emo);
            var chatArt = chatBtn != null ? HudArt(Ingame, 422) : null;
            if (chatArt != null)
            {
                ArtButton(chatBtn, chatArt, false, true);   // keeps the unread badge
                for (int i = 0; i < chatBtn.transform.childCount; i++)
                {
                    var cap = chatBtn.transform.GetChild(i).GetComponent<Text>();
                    if (cap != null) cap.enabled = false;   // the "Chat" caption (the badge count lives under Badge)
                }
            }

            // players board on the original dark glass panel
            var board = safe.Find("Players") as RectTransform;
            if (board != null) UI.Skin.Apply(board.GetComponent<Image>(), "panel.tooltip");

            // match clock pill with the stopwatch
            var clock = safe.Find("MatchClock") as RectTransform;
            var watch = HudArt(Ingame, 166);
            if (clock != null && UI.Skin.Apply(clock.GetComponent<Image>(), "pill.dark"))
                UI.StyleText(matchClock, new Color32(6, 30, 70, 255), 3f);

            // turn timer: the stopwatch, its face showing the remaining time; turn_countdown digits for the last 5 s
            var ringBg = safe.Find("TurnTimer") as RectTransform;
            if (ringBg != null && watch != null)
            {
                var rb = ringBg.GetComponent<Image>();
                rb.sprite = watch; rb.color = Color.white; rb.preserveAspect = true;
                ringBg.sizeDelta = new Vector2(108, 133);      // same band as the old 124 ring: the name below stays put
                ringBg.anchoredPosition = new Vector2(0, -96);
                UI.Anchor(turnRing.rectTransform, 0.13f, 0.1f, 0.85f, 0.69f);
                var inner = ringBg.Find("Inner") as RectTransform;
                if (inner != null) UI.Anchor(inner, 0.25f, 0.2f, 0.73f, 0.59f);
                if (inner != null) inner.GetComponent<Image>().color = new Color(1, 1, 1, 0.92f);
                UI.Anchor(turnSeconds.rectTransform, 0.08f, 0.08f, 0.9f, 0.71f);
                turnSeconds.fontSize = 40;
                UI.StyleText(turnSeconds, new Color32(6, 30, 70, 255), 3f);
                UI.StyleText(turnName, new Color32(6, 30, 70, 255), 3f);
            }

            // walk: the original blue feet squares (yellow while held), the left one mirrored
            walkIdle = HudArt(Ingame, 346);
            walkHeld = HudArt(Ingame, 353) ?? walkIdle;
            if (walkIdle != null)
            {
                walkArtL = ArtButton(leftHold.GetComponent<Button>(), walkIdle, true);
                walkArtR = ArtButton(rightHold.GetComponent<Button>(), walkIdle);
            }
            SetIcon(jumpBtn, HudArt(CharUi, 227), 0.2f);          // spring on the green button
            SetIcon(fireBtn, HudArt(CharUi, 118), 0.2f);          // crosshair on the orange button

            // weapon: the blank blue Shoot square, icon and ammo on it
            var shoot = HudArt(Ingame, 360);
            if (shoot != null)
            {
                ArtButton(weaponBtn, shoot, false, true);
                UI.Anchor(weaponIcon.rectTransform, 0.16f, 0.26f, 0.84f, 0.9f);
                UI.StyleText(weaponAmmo, new Color32(6, 30, 70, 255), 3f, Color.white);
                weaponFallback.color = new Color32(10, 50, 100, 255);
            }

            // energy bar on the original track
            var apBack = apFill.transform.parent.GetComponent<Image>();
            if (apBack != null) UI.Skin.Apply(apBack, "bar.back");

            // banner: text styled like the original; message animations when one matches
            UI.StyleText(bannerText, new Color32(6, 30, 70, 255), 5f);
            yourTurnText = Loc.Has("TID_YOUR_TURN") ? Loc.T("TID_YOUR_TURN") : "Your turn!";
            minuteLeftText = Loc.T("MATCH_MINUTE_LEFT");

            // overhead tags: orange attack_arrow pointing down at the active penguin
            var arrowArt = OriginalArt.UiSprite(CharUi, "attack_arrow");
            foreach (var t in tags)
            {
                UI.StyleText(t.name, new Color32(6, 30, 70, 255), 2.5f);
                if (arrowArt == null) continue;
                t.arrow.text = "";
                var ai = UI.Image(t.arrow.transform, arrowArt, Color.white, true, "Art");
                UI.Anchor(ai.rectTransform, 0.08f, 0.2f, 0.92f, 0.8f);
                ai.rectTransform.localEulerAngles = new Vector3(0, 0, 180);
                ai.raycastTarget = false;
            }

            BuildAimArt();
        }

        // ---------------------------------------------------------------- per frame hooks

        /// <summary>Walk buttons turn yellow while held (sprite swap only when the state changes).</summary>
        void UpdateWalkArt()
        {
            if (walkArtL == null) return;
            bool l = leftHold.Held, r = rightHold.Held;
            if (l != walkHeldL) { walkHeldL = l; walkArtL.sprite = l ? walkHeld : walkIdle; }
            if (r != walkHeldR) { walkHeldR = r; walkArtR.sprite = r ? walkHeld : walkIdle; }
        }

        /// <summary>Turn timer second changed: the original countdown digit for the last 5 seconds.</summary>
        void OnTurnSecond(int ts, bool running)
        {
            if (UI.Skin.Off) return;
            var set = running && ts >= 1 && ts <= 5 ? HudAnim(CharUi, "turn_countdown_" + ts) : null;
            turnSeconds.enabled = set == null;
            if (set == null) { if (turnCount != null) turnCount.gameObject.SetActive(false); return; }
            if (turnCount == null)
            {
                turnCount = UISpriteAnim.Create(turnSeconds.transform.parent, null, "TurnCountdown", false);
                turnCount.FlashPx = UISpriteAnim.StageScale * 2.2f;   // the 25 px digit sized for the stopwatch face
                ((RectTransform)turnCount.transform).anchorMin = ((RectTransform)turnCount.transform).anchorMax = new Vector2(0.49f, 0.4f);
            }
            turnCount.gameObject.SetActive(true);
            turnCount.Play(set, false);
        }

        /// <summary>Match clock second changed: the big fading match_count digit for the last 9 seconds.</summary>
        void OnMatchSecond(int ms)
        {
            if (UI.Skin.Off || c.CurrentPhase == BattleController.Phase.Over) return;
            var set = ms >= 1 && ms <= 9 ? HudAnim(Ingame, "match_count_0" + ms) : null;
            if (set == null) { if (matchCount != null) matchCount.gameObject.SetActive(false); return; }
            if (matchCount == null)
            {
                matchCount = UISpriteAnim.Create(safe, null, "MatchCountdown", false);
                matchCount.transform.SetSiblingIndex(0);   // under the HUD controls; never takes touches (Image raycast off)
            }
            matchCount.gameObject.SetActive(true);
            matchCount.Play(set, false, () => { if (matchCount != null) matchCount.gameObject.SetActive(false); });
        }

        /// <summary>Banner: the original "Your Turn" / "1 Minute Left" message instead of text when it matches.</summary>
        void BannerArt(string text)
        {
            if (UI.Skin.Off) return;
            SpriteAnimSet set = null;
            if (text == yourTurnText) set = HudAnim(Ingame, "message_your_turn");
            else if (!string.IsNullOrEmpty(minuteLeftText) && text == minuteLeftText) set = HudAnim(Ingame, "message_time_alert");
            bannerText.enabled = set == null;
            if (set == null) { if (bannerAnim != null) bannerAnim.gameObject.SetActive(false); return; }
            if (bannerAnim == null)
            {
                bannerAnim = UISpriteAnim.Create(bannerRt, null, "Message", false);
                var rt = (RectTransform)bannerAnim.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.35f);
            }
            // size the message to ~300 canvas units tall (its last frame)
            var last = set.FrameAt(set.Length - 1);
            float flashH = last != null ? last.rect.height / Mathf.Max(0.01f, last.pixelsPerUnit / Units.PX) : 160f;
            bannerAnim.FlashPx = 300f / Mathf.Max(20f, flashH);
            bannerAnim.gameObject.SetActive(true);
            bannerAnim.Play(set, false);
        }

        // ---------------------------------------------------------------- world aim art

        void BuildAimArt()
        {
            powerBarSet = HudAnim(CharUi, "attack_power_bar");
            if (powerBarSet != null && powerBarSet.Length > 1) powerBar = AimSprite("PowerBar", powerBarSet.FrameAt(0));
            var cs = OriginalArt.UiSprite(CharUi, "crosshair");
            if (cs != null) { crossArt = AimSprite("CrosshairArt", cs); crossArt.transform.localScale = Vector3.one * 0.8f; }
        }

        SpriteRenderer AimSprite(string name, Sprite s)
        {
            var go = new GameObject(name);
            go.transform.SetParent(c.transform, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = s;
            r.sortingOrder = 60;   // over terrain, penguins and effects (Fx uses 50)
            r.enabled = false;
            return r;
        }

        bool AimArtShown => (powerBar != null && powerBar.enabled) || (crossArt != null && crossArt.enabled);

        void HideAimArt()
        {
            if (powerBar != null) powerBar.enabled = false;
            if (crossArt != null) crossArt.enabled = false;
        }

        /// <summary>Power-bar mode: the original arrow (frame by power) instead of the line. False without the art.</summary>
        bool ShowPowerArt(Vector2 origin, float angle, float power)
        {
            if (crossArt != null && crossArt.enabled) crossArt.enabled = false;
            if (powerBar == null) return false;
            int last = powerBarSet.Length - 1;
            // frame 0 is the full red-tipped arrow, the last frame empty: more power = earlier frame
            int f = Mathf.Clamp(Mathf.RoundToInt((1f - Mathf.Clamp01(power)) * (last - 1)), 0, last - 1);
            if (f != shownPowerFrame) { shownPowerFrame = f; powerBar.sprite = powerBarSet.FrameAt(f); }
            var tr = powerBar.transform;
            tr.position = new Vector3(origin.x, origin.y, -1.1f);
            tr.rotation = Quaternion.Euler(0, 0, angle - 90f);
            powerBar.enabled = true;
            return true;
        }

        /// <summary>Point mode: the original crosshair at the target. False without the art.</summary>
        bool ShowCrossArt(bool on, Vector2 at)
        {
            if (powerBar != null && powerBar.enabled) powerBar.enabled = false;
            if (crossArt == null) return false;
            crossArt.enabled = on;
            if (on)
            {
                crossArt.transform.position = new Vector3(at.x, at.y, -1.1f);
                crossArt.transform.rotation = Quaternion.Euler(0, 0, Time.unscaledTime * 40f);
            }
            return true;
        }
    }
}
