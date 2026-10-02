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
    /// The home screen, laid out like the original (HomeScreen / TopLeft / TopRight / NavigationButtons / Friends
    /// element screens): a blue top bar with the logo, Gifts, Membership, Friends, Help and the Inbox; the level star
    /// with the XP bar and the coins / Cash counters with Add buttons under it; the player's 3D penguin on a podium in
    /// an igloo doorway (HomeStage) with big orange Supplies / Character tiles on its left and Crafting / Coming Soon
    /// on its right; the green Play button (one-tap QuickPlay) with Custom game under it; settings gear; and the
    /// bottom strip with Invite, the friends row and the PING WIN slots machine. The remake's extra features sit in
    /// small round icons on the edges (Awards, Ranks, League left; Practice, Online, Modes right).
    ///
    /// Bars are fixed pixel sizes; the centre follows the penguin's projected position, so it fits 4:3 (canvas
    /// ~1920x1440) to 21:9 (~2260x970) in the safe area. First launch offers the tutorial; the daily gift pops up
    /// once per day.
    /// </summary>
    public partial class HomeScreen : UIScreen
    {
        static bool offeredThisRun, dailyShownThisRun;
        /// <summary>Forget per-session flags (after a progress reset).</summary>
        public static void ResetSession() { offeredThisRun = false; dailyShownThisRun = false; }

        public override bool ShowTopBar => false;   // the home screen has its own original-style header

        const float Edge = 20;
        static readonly Color TileOrange = new Color32(255, 168, 28, 255);
        static readonly Color PlayGreen = new Color32(124, 196, 40, 255);
        static readonly Color CustomBlue = new Color32(38, 124, 210, 255);
        static readonly Color SoonGrey = new Color32(150, 178, 196, 255);
        static readonly Color IconBlue = new Color32(52, 140, 222, 255);

        HomeStage stage;
        RectTransform tilesLeft, tilesRight, playRt, customRt;
        Text playCaption;
        bool alive;
        readonly List<KeyValuePair<Text, Func<int>>> badges = new List<KeyValuePair<Text, Func<int>>>();
        float badgeTimer;

        public override void Build()
        {
            alive = true;
            stage = new HomeStage(Root);

            // ---- centre: tiles beside the penguin, Play + Custom game under it (placed by Relayout) ----
            tilesLeft = TileColumn("TilesLeft");
            MetaUI.CartoonTile(tilesLeft, "Weapons/MiniBazooka", "Supplies", TileOrange, () => ScreenManager.Show(() => new ShopScreen()));
            MetaUI.CartoonTile(tilesLeft, "Ui/wardrobe", "Character", TileOrange, () => ScreenManager.Show(() => new WardrobeScreen()));
            tilesRight = TileColumn("TilesRight");
            var craft = MetaUI.CartoonTile(tilesRight, "Ui/crafting", Loc.T("BUTTON_CRAFTING"), TileOrange, () => ScreenManager.Show(() => new CraftingScreen()));
            AddBadge(craft.transform, CraftingCatalog.ReadyCount, new Vector2(-10, -10));
            var soon = MetaUI.CartoonTile(tilesRight, "Ui/lock", "Coming Soon", SoonGrey, () => UI.Toast("Coming soon!"));
            soon.gameObject.AddComponent<CanvasGroup>().alpha = 0.6f;

            var play = MetaUI.CartoonButton(Root, Loc.T("BUTTON_PLAY"), PlayGreen, QuickPlay.Start, 96, "Play");
            playRt = (RectTransform)play.transform;
            var pl = play.transform.Find("Caption") as RectTransform;
            if (pl) UI.Stretch(pl, 14, 14, 4, 34);
            playCaption = UI.Label(play.transform, QuickPlay.Caption, 24, Color.white, TextAnchor.MiddleCenter, false, "Mode");
            UI.Anchor(playCaption.rectTransform, 0.08f, 0.1f, 0.92f, 0.32f);
            MetaUI.Outlined(playCaption, MetaUI.Darker(PlayGreen, 0.6f), 1.5f);
            play.gameObject.AddComponent<UIPulse>().amount = 0.02f;
            var custom = MetaUI.CartoonButton(Root, "Custom game", CustomBlue, () => ScreenManager.Show(() => new CustomGameScreen()), 40, "Custom");
            customRt = (RectTransform)custom.transform;

            // ---- edges: the remake's extra features ----
            var left = SideColumn(true);
            AddBadge(MetaUI.RoundIcon(left, "Ui/trophy", "Awards", IconBlue, () => ScreenManager.Show(() => new AchievementsScreen()), 84).transform,
                AchievementCatalog.Claimable, new Vector2(4, 4));
            MetaUI.RoundIcon(left, "Ui/leaderboard", "Ranks", IconBlue, () => ScreenManager.Show(() => new LeaderboardScreen()), 84);
            AddBadge(MetaUI.RoundIcon(left, "Ui/star", "League", IconBlue, () => ScreenManager.Show(() => new TournamentScreen()), 84).transform,
                () => string.IsNullOrEmpty(ProfileService.P.leaguePendingWeek) ? 0 : 1, new Vector2(4, 4));
            var right = SideColumn(false);
            MetaUI.RoundIcon(right, "Ui/practice", Loc.T("PRACTICE"), IconBlue, () => ScreenManager.Show(() => new LoadoutScreen(BattleFactory.PracticeMatch())), 84);
            MetaUI.RoundIcon(right, "Ui/online", "Online", IconBlue, PlayScreen.OpenOnline, 84);
            MetaUI.RoundIcon(right, "Ui/quickmatch", "Modes", IconBlue, () => ScreenManager.Show(() => new PlayScreen()), 84);

            BuildHeader();
            BuildBottom();
            stage.Update(0);
            Relayout();
        }

        /// <summary>Two big tiles stacked; size and position come from Relayout.</summary>
        RectTransform TileColumn(string name)
        {
            var col = UI.Rect(Root, name);
            col.anchorMin = col.anchorMax = Vector2.zero;
            var v = UI.VBox(col, 22, TextAnchor.MiddleCenter);
            v.childForceExpandHeight = true;
            return col;
        }

        /// <summary>Round icons down the left or right edge, between the stats row and the bottom strip / gear.</summary>
        RectTransform SideColumn(bool leftSide)
        {
            var col = UI.Rect(Root, leftSide ? "EdgeLeft" : "EdgeRight");
            float x = leftSide ? 0 : 1;
            col.anchorMin = new Vector2(x, 0); col.anchorMax = new Vector2(x, 1); col.pivot = new Vector2(x, 0.5f);
            float bottom = leftSide ? StripHeight + 16 : GearTop + 12, top = HeaderHeight + 16;
            col.offsetMin = new Vector2(leftSide ? Edge + 8 : -(Edge + 8 + 120), bottom);
            col.offsetMax = new Vector2(leftSide ? Edge + 8 + 120 : -(Edge + 8), -top);
            var v = UI.VBox(col, 38, TextAnchor.MiddleCenter);
            v.childControlWidth = false; v.childControlHeight = false; v.childForceExpandWidth = false;
            return col;
        }

        /// <summary>Places the centre widgets around the penguin (after a resize or when MenuScene3D moves it).</summary>
        void Relayout()
        {
            var size = stage.RootSize;
            if (size.x < 10) return;
            var feet = stage.FeetPx;
            float door = stage.DoorHalfPx;

            // Play under the podium, Custom game under Play, both above the bottom strip
            float top = feet.y - stage.PodiumHeight * 0.45f;
            float space = Mathf.Max(0, top - (StripHeight + 12)) - 10;
            float playH = Mathf.Clamp(space * 0.62f, 84, 140), customH = Mathf.Clamp(space - playH, 54, 78);
            float playY = Mathf.Max(top, StripHeight + 12 + customH + 10 + playH);
            float playW = Mathf.Min(460, playH * 3.3f);
            PlaceTL(playRt, new Vector2(feet.x - playW / 2, playY), new Vector2(playW, playH));
            PlaceTL(customRt, new Vector2(feet.x - playW * 0.37f, playY - playH - 10), new Vector2(playW * 0.74f, customH));

            // tiles: from about the penguin's knees to under the stats row, outside the doorway, clear of the edge icons
            float bandBottom = Mathf.Max(feet.y - 40, playY + 14), bandTop = size.y - HeaderHeight - 14;
            float tileH = Mathf.Clamp((bandTop - bandBottom - 22) / 2, 120, 250);
            float tileW = Mathf.Min(tileH * 1.12f, Mathf.Max(150, feet.x - door - 24 - (Edge + 150)));
            float colH = tileH * 2 + 22;
            float y0 = Mathf.Max(bandBottom, (bandBottom + bandTop - colH) / 2);
            PlaceTL(tilesLeft, new Vector2(feet.x - door - 24 - tileW, y0 + colH), new Vector2(tileW, colH));
            PlaceTL(tilesRight, new Vector2(feet.x + door + 24, y0 + colH), new Vector2(Mathf.Min(tileW, Mathf.Max(150, size.x - feet.x - door - 24 - (Edge + 150))), colH));
        }

        /// <summary>Position by top-left corner, in pixels from the Root's bottom-left.</summary>
        static void PlaceTL(RectTransform rt, Vector2 topLeft, Vector2 sz)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = sz;
            rt.anchoredPosition = topLeft;
        }

        // ------------------------------------------------------------------ badges

        void AddBadge(Transform parent, Func<int> count, Vector2 offset)
        {
            if (count == null) return;
            var t = MetaUI.Badge(parent, "", Theme.Danger, 46);
            var rt = (RectTransform)t.transform.parent;
            rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
            rt.anchoredPosition = offset;
            rt.gameObject.AddComponent<UIPulse>();
            var o = rt.gameObject.AddComponent<Outline>();
            o.effectColor = Color.white; o.effectDistance = new Vector2(2, -2);
            badges.Add(new KeyValuePair<Text, Func<int>>(t, count));
        }

        void UpdateBadges()
        {
            foreach (var kv in badges)
            {
                if (kv.Key == null) continue;
                int n = kv.Value();
                var go = kv.Key.transform.parent.gameObject;
                if (go.activeSelf != n > 0) go.SetActive(n > 0);
                if (n > 0) kv.Key.text = n > 9 ? "9+" : n.ToString();
            }
        }

        // ------------------------------------------------------------------ lifecycle

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

        public override void OnHide()
        {
            alive = false;
            MenuScene3D.Hide();
            stage?.Dispose();
        }

        public override void Tick(float dt)
        {
            if (stage != null && stage.Update(dt)) Relayout();
            TickHeader(dt);
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
