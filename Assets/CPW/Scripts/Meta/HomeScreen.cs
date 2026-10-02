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
    /// One-tap PLAY from the home screen: an online quick match when Firebase is connected, otherwise a quick match
    /// against AI penguins with the player's own arsenal (no setup or loadout screens in between).
    /// </summary>
    public static class QuickPlay
    {
        public static bool Online => CPW.Online.Service.Available;
        public static string Caption => Online ? "Online quick match" : "vs computer penguins";

        public static void Start()
        {
            if (Online)
            {
                OnlineLobbyScreen.AutoQuickMatch = true;
                ScreenManager.Show(() => new OnlineLobbyScreen());
                return;
            }
            // Same defaults as the Quick Match screen: one normal opponent, no bet.
            BattleFactory.Launch(BattleFactory.QuickMatch(1, 1, "1NoBet"));
        }
    }

    /// <summary>
    /// The home screen: the player's 3D penguin on a snowy hill with the big PLAY button in front of it, a row of
    /// play modes under it, social / reward tiles on the left and shopping on the right. First launch offers the
    /// tutorial; the daily gift pops up once per day.
    ///
    /// Layout works in the safe area from 4:3 (iPad, canvas ~1920x1440) to 21:9 (canvas ~2260x970): the side columns
    /// are fixed-width and start below the title + tip, the centre keeps PLAY and the mode row.
    /// </summary>
    public class HomeScreen : UIScreen
    {
        static bool offeredThisRun, dailyShownThisRun;
        /// <summary>Forget per-session flags (after a progress reset).</summary>
        public static void ResetSession() { offeredThisRun = false; dailyShownThisRun = false; }

        const float ColumnWidth = 236, Margin = 24, TitleTop = 6, TitleHeight = 86, TipHeight = 48;
        const float ColumnsTop = MetaUI.TopBarHeight + TitleTop + TitleHeight + TipHeight + 14;

        Text tipText, playCaption;
        float tipTimer;
        readonly List<KeyValuePair<Text, Func<int>>> badges = new List<KeyValuePair<Text, Func<int>>>();
        float badgeTimer;

        public override void Build()
        {
            // ---- title + tip (top-left, above the side column so they never overlap) ----
            var title = UI.Label(Root, "CRAZY PENGUIN WARS", 72, new Color(1f, 0.86f, 0.2f), TextAnchor.MiddleLeft, true);
            var trt = title.rectTransform;
            trt.anchorMin = new Vector2(0, 1); trt.anchorMax = new Vector2(0.62f, 1); trt.pivot = new Vector2(0, 1);
            trt.offsetMin = new Vector2(Margin + 6, -(MetaUI.TopBarHeight + TitleTop + TitleHeight));
            trt.offsetMax = new Vector2(0, -(MetaUI.TopBarHeight + TitleTop));
            tipText = UI.Label(Root, Tips.Random(), 28, new Color(1, 1, 1, 0.9f), TextAnchor.MiddleLeft);
            var tip = tipText.rectTransform;
            tip.anchorMin = new Vector2(0, 1); tip.anchorMax = new Vector2(0.62f, 1); tip.pivot = new Vector2(0, 1);
            tip.offsetMin = new Vector2(Margin + 6, -(MetaUI.TopBarHeight + TitleTop + TitleHeight + TipHeight));
            tip.offsetMax = new Vector2(0, -(MetaUI.TopBarHeight + TitleTop + TitleHeight));

            // ---- left column: rewards + social ----
            var left = Column(true);
            Side(left, "Ui/gift", "Daily", MetaUI.Pink, () => ScreenManager.Show(() => new DailyScreen()), () => DailyScreen.CanClaim ? 1 : 0);
            Side(left, "Ui/slot", "Slots", MetaUI.Purple, () => ScreenManager.Show(() => new SlotMachineScreen()), SlotMachineLogic.FreeSpinsLeft);
            Side(left, "Ui/trophy", "Awards", MetaUI.Orange, () => ScreenManager.Show(() => new AchievementsScreen()), AchievementCatalog.Claimable);
            Side(left, "Ui/leaderboard", "Ranks", MetaUI.Teal, () => ScreenManager.Show(() => new LeaderboardScreen()), null);
            Side(left, "Ui/online", Loc.T("BUTTON_NEIGHBORS"), Theme.Secondary, () => ScreenManager.Show(() => new FriendsScreen()), () => Social.InboxCount);
            Side(left, "Ui/star", "League", new Color32(205, 127, 50, 255), () => ScreenManager.Show(() => new TournamentScreen()),
                () => string.IsNullOrEmpty(ProfileService.P.leaguePendingWeek) ? 0 : 1);

            // ---- right column: shopping + help ----
            var right = Column(false);
            Side(right, "Ui/shop", Loc.T("BUTTON_SUPPLIES"), Theme.Secondary, () => ScreenManager.Show(() => new ShopScreen()), null);
            Side(right, "Ui/wardrobe", "Wardrobe", MetaUI.Pink, () => ScreenManager.Show(() => new WardrobeScreen()), null);
            Side(right, "Ui/crafting", Loc.T("BUTTON_CRAFTING"), MetaUI.Teal, () => ScreenManager.Show(() => new CraftingScreen()), CraftingCatalog.ReadyCount);
            Side(right, null, "Help ?", MetaUI.CardDark, () => ScreenManager.Show(() => new HelpScreen()), null);

            // ---- centre: PLAY (one tap) and the other modes ----
            var center = UI.Rect(Root, "Center");
            center.anchorMin = new Vector2(0, 0); center.anchorMax = new Vector2(1, 0); center.pivot = new Vector2(0.5f, 0);
            center.offsetMin = new Vector2(ColumnWidth + Margin * 2, Margin);
            center.offsetMax = new Vector2(-(ColumnWidth + Margin * 2), Margin + 290);

            var play = UI.Button(center, Loc.T("BUTTON_PLAY").ToUpperInvariant(), QuickPlay.Start, UI.ButtonStyle.Primary, 104, "Play");
            var prt = (RectTransform)play.transform;
            prt.anchorMin = new Vector2(0.5f, 1); prt.anchorMax = new Vector2(0.5f, 1); prt.pivot = new Vector2(0.5f, 1);
            prt.sizeDelta = new Vector2(620, 180);
            prt.anchoredPosition = Vector2.zero;
            var pl = play.GetComponentInChildren<Text>();
            if (pl) UI.Stretch(pl.rectTransform, 16, 16, 6, 46);
            playCaption = UI.Label(play.transform, QuickPlay.Caption, 28, Theme.PrimaryText, TextAnchor.MiddleCenter);
            UI.Anchor(playCaption.rectTransform, 0.05f, 0.04f, 0.95f, 0.28f);
            play.gameObject.AddComponent<UIPulse>().amount = 0.03f;

            var modes = UI.Rect(center, "Modes");
            modes.anchorMin = new Vector2(0, 0); modes.anchorMax = new Vector2(1, 0); modes.pivot = new Vector2(0.5f, 0);
            modes.offsetMin = new Vector2(0, 0); modes.offsetMax = new Vector2(0, 88);
            var mh = UI.HBox(modes, 12, TextAnchor.MiddleCenter);
            mh.childForceExpandWidth = false;
            Mode(modes, Loc.T("PRACTICE"), Theme.Good, () => ScreenManager.Show(() => new LoadoutScreen(BattleFactory.PracticeMatch())));
            Mode(modes, Loc.T("BUTTON_CUSTOM_GAME"), MetaUI.Purple, () => ScreenManager.Show(() => new CustomGameScreen()));
            Mode(modes, "Online", MetaUI.Teal, PlayScreen.OpenOnline);
            Mode(modes, "More >", Theme.PanelDark, () => ScreenManager.Show(() => new PlayScreen()));
        }

        /// <summary>A fixed-width column on the left or right, from under the title to the bottom of the safe area.</summary>
        RectTransform Column(bool leftSide)
        {
            var col = UI.Rect(Root, leftSide ? "Left" : "Right");
            float x = leftSide ? 0 : 1;
            col.anchorMin = new Vector2(x, 0); col.anchorMax = new Vector2(x, 1); col.pivot = new Vector2(x, 1);
            col.sizeDelta = new Vector2(ColumnWidth, -(ColumnsTop + Margin));
            col.anchoredPosition = new Vector2(leftSide ? Margin : -Margin, -ColumnsTop);
            var v = UI.VBox(col, 10, TextAnchor.UpperCenter);
            v.childForceExpandHeight = false;
            return col;
        }

        void Side(RectTransform parent, string icon, string caption, Color color, Action onClick, Func<int> badge)
        {
            var b = UI.Button(parent, null, onClick, UI.ButtonStyle.Dark, 30, "Btn " + caption);
            b.GetComponent<Image>().color = color;
            // shrinks evenly on short screens (VBox lerps between min and preferred height)
            var le = UI.Layout(b, -1, 118);
            le.minHeight = 60;
            float textLeft = 0.06f;
            if (icon != null)
            {
                var tile = MetaUI.IconTile(MetaUI.Box(b.transform, 0.04f, 0.08f, 0.4f, 0.92f), icon, caption, Color.Lerp(color, Color.white, 0.25f), false);
                MetaUI.Square(tile);
                textLeft = 0.42f;
            }
            var l = UI.Label(b.transform, caption, 36, Color.white, icon != null ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, true);
            UI.Anchor(l.rectTransform, textLeft, 0, 0.97f, 1);
            AddBadge(b.transform, badge);
        }

        static void Mode(RectTransform parent, string caption, Color color, Action onClick)
        {
            var b = UI.Button(parent, caption, onClick, UI.ButtonStyle.Dark, 30, "Mode " + caption);
            b.GetComponent<Image>().color = color;
            var le = UI.Layout(b, 210, 84);
            le.minWidth = 120;
        }

        void AddBadge(Transform parent, Func<int> count)
        {
            if (count == null) return;
            var t = MetaUI.Badge(parent, "", Theme.Danger, 50);
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
            Social.RefreshInboxCount(null);
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
            League.TrySettle();
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
            if (badgeTimer > 1f)
            {
                badgeTimer = 0;
                UpdateBadges();
                playCaption.text = QuickPlay.Caption;   // the connection may come up after the screen was built
            }
        }

        public override bool OnBack()
        {
            UI.Confirm("Quit?", "Leave the battlefield for now?", Application.Quit);
            return true;
        }
    }
}
