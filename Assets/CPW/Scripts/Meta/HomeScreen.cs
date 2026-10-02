using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>Random hint from the LoadingScreenTips section (TipVariable placeholders filled from the config).</summary>
    public static class Tips
    {
        public static string Random()
        {
            var list = new List<Record>(GameData.Section("LoadingScreenTips").Values);
            if (list.Count == 0) return "Tip: aim for the opponent!";
            var r = list[UnityEngine.Random.Range(0, list.Count)];
            var text = Loc.T(r.Str("TipText", ""));
            var v = r.Str("TipVariable");
            if (!string.IsNullOrEmpty(v))
            {
                var parts = v.Split('.');
                if (parts.Length == 3)
                {
                    var val = GameData.Get(parts[0], parts[1])?.Str(parts[2]) ?? "";
                    if (parts[2].Contains("Expiry") && int.TryParse(val, out int secs)) val = (secs / 86400).ToString();
                    text = text.Replace("%s", val).Replace("%U", val).Replace("%u", val);
                }
            }
            return text.Replace("%s", "200").Replace("%U", "200");
        }
    }

    /// <summary>Boot splash with the game logo, a loading bar and a tip; goes to the home screen.</summary>
    public class SplashScreen : UIScreen
    {
        public static bool Shown { get; private set; }
        public override bool ShowTopBar => false;
        Image fill;
        float t;
        const float Duration = 2.4f;

        public override void Build()
        {
            Shown = true;
            MetaUI.Background(Root);
            var logo = UI.Label(Root, "CRAZY PENGUIN\nWARS", 170, Color.white, TextAnchor.MiddleCenter, true);
            UI.Anchor(logo.rectTransform, 0.1f, 0.45f, 0.9f, 0.92f);
            logo.color = new Color(1f, 0.85f, 0.2f);
            var o = logo.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0.1f, 0.15f, 0.4f); o.effectDistance = new Vector2(6, -6);
            logo.gameObject.AddComponent<UIPulse>().amount = 0.02f;
            var host = UI.Rect(Root, "Bar");
            UI.Anchor(host, 0.25f, 0.27f, 0.75f, 0.33f);
            fill = UI.Bar(host, MetaUI.Gold);
            UI.Stretch((RectTransform)fill.transform.parent);
            fill.fillAmount = 0;
            var tip = UI.Label(Root, Tips.Random(), 40, Color.white, TextAnchor.MiddleCenter);
            UI.Anchor(tip.rectTransform, 0.12f, 0.1f, 0.88f, 0.24f);
            var credit = UI.Label(Root, "A fan remake of the Crazy Penguin Wars community project", 26, new Color(1, 1, 1, 0.7f));
            UI.Anchor(credit.rectTransform, 0.1f, 0.02f, 0.9f, 0.08f);
        }

        public override void Tick(float dt)
        {
            t += dt;
            fill.fillAmount = Mathf.Clamp01(t / Duration);
            if (t >= Duration) ScreenManager.Reset(() => new HomeScreen());
        }
    }

    /// <summary>
    /// The home screen: the player's 3D penguin on a snowy hill, the big Play button and every menu feature.
    /// First launch offers the tutorial; the daily gift pops up once per day.
    /// </summary>
    public class HomeScreen : UIScreen
    {
        static bool offeredThisRun, dailyShownThisRun;
        /// <summary>Forget per-session flags (after a progress reset).</summary>
        public static void ResetSession() { offeredThisRun = false; dailyShownThisRun = false; }
        Text tipText;
        float tipTimer;
        readonly List<KeyValuePair<Text, Func<int>>> badges = new List<KeyValuePair<Text, Func<int>>>();
        float badgeTimer;

        public override void Build()
        {
            // Title (top-left under the bar)
            var title = UI.Label(Root, "CRAZY PENGUIN WARS", 78, new Color(1f, 0.86f, 0.2f), TextAnchor.UpperLeft, true);
            title.rectTransform.anchorMin = new Vector2(0, 1); title.rectTransform.anchorMax = new Vector2(0.6f, 1);
            title.rectTransform.pivot = new Vector2(0, 1);
            title.rectTransform.sizeDelta = new Vector2(0, 100);
            title.rectTransform.anchoredPosition = new Vector2(30, -MetaUI.TopBarHeight - 10);

            // ---- left column: social / rewards ----
            var left = UI.Rect(Root, "Left");
            UI.Anchor(left, 0, 0.12f, 0, 0.8f);
            left.sizeDelta = new Vector2(230, left.sizeDelta.y);
            left.anchoredPosition = new Vector2(140, left.anchoredPosition.y);
            var lv = UI.VBox(left, 16, TextAnchor.UpperCenter);
            lv.childForceExpandHeight = true;
            Side(left, "Ui/gift", "Daily", MetaUI.Pink, () => ScreenManager.Show(() => new DailyScreen()), () => DailyScreen.CanClaim ? 1 : 0);
            Side(left, "Ui/slot", "Slots", MetaUI.Purple, () => ScreenManager.Show(() => new SlotMachineScreen()), SlotMachineLogic.FreeSpinsLeft);
            Side(left, "Ui/trophy", "Awards", MetaUI.Orange, () => ScreenManager.Show(() => new AchievementsScreen()), AchievementCatalog.Claimable);
            Side(left, "Ui/leaderboard", "Ranks", MetaUI.Teal, () => ScreenManager.Show(() => new LeaderboardScreen()), null);

            // ---- right column: play + shopping ----
            var play = UI.Button(Root, Loc.T("BUTTON_PLAY").ToUpperInvariant(), () => ScreenManager.Show(() => new PlayScreen()), UI.ButtonStyle.Primary, 96, "Play");
            UI.Place((RectTransform)play.transform, new Vector2(1, 0.5f), new Vector2(520, 220), new Vector2(-40, 120));
            play.gameObject.AddComponent<UIPulse>().amount = 0.03f;
            var row = UI.Rect(Root, "Right");
            UI.Place(row, new Vector2(1, 0.5f), new Vector2(560, 210), new Vector2(-20, -140));
            var h = UI.HBox(row, 16, TextAnchor.MiddleRight);
            h.childForceExpandWidth = true;
            Big(row, "Ui/shop", Loc.T("BUTTON_SUPPLIES"), Theme.Secondary, () => ScreenManager.Show(() => new ShopScreen()), null);
            Big(row, "Ui/wardrobe", "Wardrobe", MetaUI.Pink, () => ScreenManager.Show(() => new WardrobeScreen()), null);
            Big(row, "Ui/crafting", Loc.T("BUTTON_CRAFTING"), MetaUI.Teal, () => ScreenManager.Show(() => new CraftingScreen()), CraftingCatalog.ReadyCount);

            // ---- bottom: help + tips ----
            var help = UI.Button(Root, "?", () => ScreenManager.Show(() => new HelpScreen()), UI.ButtonStyle.Secondary, 60, "Help");
            UI.Place((RectTransform)help.transform, new Vector2(1, 0), new Vector2(100, 100), new Vector2(-30, 24));
            var tipBg = UI.Panel(Root, new Color(0, 0, 0, 0.45f), true, "Tip");
            UI.Place(tipBg.rectTransform, new Vector2(0.5f, 0), new Vector2(1100, 84), new Vector2(-40, 24));
            tipBg.raycastTarget = false;
            tipText = UI.Label(tipBg.transform, Tips.Random(), 32, Color.white);
            UI.Stretch(tipText.rectTransform, 24, 24, 4, 4);
        }

        void Side(RectTransform parent, string icon, string caption, Color color, Action onClick, Func<int> badge)
        {
            var b = UI.Button(parent, null, onClick, UI.ButtonStyle.Dark, 30, "Btn " + caption);
            b.GetComponent<Image>().color = color;
            var tile = MetaUI.IconTile(b.transform, icon, caption, Color.Lerp(color, Color.white, 0.25f), false);
            UI.Anchor(tile, 0.08f, 0.1f, 0.48f, 0.9f);
            var l = UI.Label(b.transform, caption, 36, Color.white, TextAnchor.MiddleLeft, true);
            UI.Anchor(l.rectTransform, 0.5f, 0, 0.98f, 1);
            AddBadge(b.transform, badge);
        }

        void Big(RectTransform parent, string icon, string caption, Color color, Action onClick, Func<int> badge)
        {
            var b = UI.Button(parent, null, onClick, UI.ButtonStyle.Dark, 30, "Btn " + caption);
            b.GetComponent<Image>().color = color;
            UI.Layout(b, 170, 200, 1, 1);
            var tile = MetaUI.IconTile(b.transform, icon, caption, Color.Lerp(color, Color.white, 0.25f), false);
            UI.Anchor(tile, 0.12f, 0.3f, 0.88f, 0.94f);
            var l = UI.Label(b.transform, caption, 32, Color.white, TextAnchor.MiddleCenter, true);
            UI.Anchor(l.rectTransform, 0.02f, 0.02f, 0.98f, 0.3f);
            AddBadge(b.transform, badge);
        }

        void AddBadge(Transform parent, Func<int> count)
        {
            if (count == null) return;
            var t = MetaUI.Badge(parent, "", Theme.Danger, 54);
            var rt = (RectTransform)t.transform.parent;
            rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-6, -6);
            rt.gameObject.AddComponent<UIPulse>();
            badges.Add(new KeyValuePair<Text, Func<int>>(t, count));
        }

        void UpdateBadges()
        {
            foreach (var kv in badges)
            {
                int n = kv.Value();
                var go = kv.Key.transform.parent.gameObject;
                if (go.activeSelf != n > 0) go.SetActive(n > 0);
                if (n > 0) kv.Key.text = n > 9 ? "9+" : n.ToString();
            }
        }

        public override void OnShow()
        {
            MenuScene3D.Show(MenuScene3D.Layout.Home);
            UpdateBadges();
            var P = ProfileService.P;
            if (!P.tutorialDone && !offeredThisRun && P.Counter("flag.tutorialOffered") == 0)
            {
                offeredThisRun = true;
                UI.Popup(Loc.T("TUTORIAL_INTRO_TITLE"), Loc.T("TUTORIAL_INTRO_DESCRIPTION"),
                    new UI.PopupButton("Skip", () => { P.AddCounter("flag.tutorialOffered", 1); ProfileService.Save(); }, UI.ButtonStyle.Secondary),
                    new UI.PopupButton(Loc.T("TUTORIAL_INTRO_BUTTON"), () => BattleFactory.Launch(BattleFactory.Tutorial())));
                return;
            }
            Progression.ShowPendingLevelUps();
            if (!dailyShownThisRun && DailyScreen.CanClaim)
            {
                dailyShownThisRun = true;
                ScreenManager.Show(() => new DailyScreen());
            }
        }

        public override void OnHide() => MenuScene3D.Hide();

        public override void Tick(float dt)
        {
            tipTimer += dt;
            if (tipTimer > 9f) { tipTimer = 0; tipText.text = Tips.Random(); }
            badgeTimer += dt;
            if (badgeTimer > 1f) { badgeTimer = 0; UpdateBadges(); }
        }

        public override bool OnBack()
        {
            UI.Confirm("Quit?", "Leave the battlefield for now?", Application.Quit);
            return true;
        }
    }
}
