using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// Leaderboard: the online one (Online.Service.GetLeaderboard) when Firebase is connected; otherwise (or on the
    /// second tab) a local ladder where you climb past AI penguins by earning XP.
    /// </summary>
    public class LeaderboardScreen : MetaScreen
    {
        static int tab;
        RectTransform list;
        List<Button> tabs;
        Text status;
        protected override string Title => Loc.T("BUTTON_LEADERBOARD");

        protected override void BuildContent()
        {
            var row = UI.Rect(Content, "Tabs");
            UI.Anchor(row, 0, 0.89f, 0.7f, 1);
            if (!Online.Service.Available) tab = 1;
            tabs = MetaUI.Tabs(row, new[] { "Online", "Penguin ladder" }, tab, i => { tab = i; MetaUI.SetTabSelected(tabs, i); Fill(); }, 36);
            status = UI.Label(Content, "", 28, Color.white, TextAnchor.MiddleRight);
            UI.Anchor(status.rectTransform, 0.6f, 0.89f, 1, 1);

            var panel = MetaUI.CardPanel(Content, MetaUI.Card);
            UI.Anchor(panel.rectTransform, 0, 0, 0.66f, 0.87f);
            var sr = UI.ScrollList(panel.transform, out list, true, 6, 12);
            UI.Stretch((RectTransform)sr.transform, 4, 4, 4, 4);

            // my stats
            var me = MetaUI.CardPanel(Content, MetaUI.CardDark);
            UI.Anchor(me.rectTransform, 0.68f, 0, 1, 0.87f);
            var P = ProfileService.P;
            var v = UI.VBox(me.rectTransform, 6, TextAnchor.UpperLeft, 26);
            v.childForceExpandHeight = false;
            Stat(me.rectTransform, P.displayName, 46, MetaUI.Gold);
            Stat(me.rectTransform, "Level " + P.level + "   -   " + P.xp.ToString("N0") + " XP", 32, Color.white);
            Stat(me.rectTransform, "Matches played: " + P.gamesPlayed, 32, Color.white);
            Stat(me.rectTransform, "Wins: " + P.gamesWon + (P.gamesPlayed > 0 ? "  (" + Mathf.RoundToInt(100f * P.gamesWon / P.gamesPlayed) + "%)" : ""), 32, Color.white);
            Stat(me.rectTransform, "Knock-outs: " + P.kills + "   Deaths: " + P.deaths, 32, Color.white);
            Stat(me.rectTransform, "Damage dealt: " + P.totalDamage.ToString("N0"), 32, Color.white);
            Stat(me.rectTransform, "Best score: " + P.bestScore, 32, Color.white);
            Stat(me.rectTransform, "Trophies: " + P.trophies.Count + " / " + ClothesCatalog.BySlot(ClothesSlot.Trophy).Count, 32, Color.white);
            Fill();
        }

        static void Stat(RectTransform parent, string text, int size, Color c)
        {
            var l = UI.Label(parent, text, size, c, TextAnchor.MiddleLeft, size > 40);
            UI.Layout(l, -1, size + 22);
        }

        void Fill()
        {
            UI.Clear(list);
            if (tab == 0)
            {
                if (!Online.Service.Available)
                {
                    status.text = "";
                    var l = UI.Label(list, "The online leaderboard needs Firebase.\n" + Online.Service.Status, 34, Theme.Muted);
                    UI.Layout(l, -1, 200);
                    return;
                }
                status.text = "Loading...";
                var root = Root;
                Online.Service.GetLeaderboard(50, entries =>
                {
                    if (root == null || tab != 0) return;
                    status.text = entries.Count + " penguins";
                    UI.Clear(list);
                    if (entries.Count == 0) { var l = UI.Label(list, "Nobody here yet. Be the first!", 34, Theme.Muted); UI.Layout(l, -1, 120); return; }
                    entries.Sort((a, b) => a.rank > 0 && b.rank > 0 ? a.rank.CompareTo(b.rank) : b.score.CompareTo(a.score));
                    for (int i = 0; i < entries.Count; i++)
                    {
                        var e = entries[i];
                        Row(i + 1, e.name, e.level, e.score, e.wins, e.playerId == ProfileService.P.playerId);
                    }
                });
                return;
            }
            status.text = "Offline ladder";
            var ladder = LocalLadder();
            for (int i = 0; i < ladder.Count; i++) Row(i + 1, ladder[i].name, ladder[i].level, ladder[i].score, ladder[i].wins, ladder[i].playerId == "me");
        }

        void Row(int rank, string name, int level, int xp, int wins, bool me)
        {
            var row = UI.Panel(list, me ? Theme.Primary : (rank % 2 == 0 ? new Color(0.93f, 0.96f, 1f) : Color.white), true, "Row");
            UI.Layout(row, -1, 84);
            Color medal = rank == 1 ? new Color32(255, 200, 40, 255) : rank == 2 ? new Color32(200, 205, 215, 255) : rank == 3 ? new Color32(215, 140, 70, 255) : Theme.Secondary;
            var badge = MetaUI.Badge(row.transform, rank.ToString(), medal, 66);
            var brt = (RectTransform)badge.transform.parent;
            brt.anchorMin = brt.anchorMax = new Vector2(0, 0.5f);
            brt.anchoredPosition = new Vector2(50, 0);
            var n = UI.Label(row.transform, name, 34, Theme.Text, TextAnchor.MiddleLeft, me);
            UI.Anchor(n.rectTransform, 0.1f, 0, 0.52f, 1);
            var lv = UI.Label(row.transform, "Lv " + level, 30, Theme.Secondary, TextAnchor.MiddleCenter, true);
            UI.Anchor(lv.rectTransform, 0.52f, 0, 0.64f, 1);
            var x = UI.Label(row.transform, xp.ToString("N0") + " XP", 30, Theme.Text, TextAnchor.MiddleRight);
            UI.Anchor(x.rectTransform, 0.64f, 0, 0.83f, 1);
            var w = UI.Label(row.transform, wins + " wins", 28, Theme.Muted, TextAnchor.MiddleRight);
            UI.Anchor(w.rectTransform, 0.83f, 0, 0.97f, 1);
        }

        /// <summary>
        /// 30 AI penguins spread from beginners to veterans (stable per player id), with the player inserted by XP.
        /// </summary>
        static List<LeaderboardEntry> LocalLadder()
        {
            var P = ProfileService.P;
            var res = new List<LeaderboardEntry>();
            int seed = 0; foreach (char c in P.playerId) seed = seed * 31 + c;
            var rnd = new System.Random(seed);
            string[] names =
            {
                "Sir Waddles", "Captain Flipper", "Tux Norris", "Pingu Khan", "Admiral Fishbreath", "Lil' Blizzard", "Iceberg Slim",
                "Madame Krill", "Sergeant Slush", "Frosty McBoom", "Professor Puffin", "Mr. Tuxedo", "Penguzilla", "Count Snowcula",
                "Sushi Bandit", "Nuke Duke", "Baroness Brrr", "Herring Bone", "Floe Rida", "Cool Hand Luke", "Mighty Mackerel",
                "Dr. Feathergood", "Glacier Gus", "The Emperor", "Rocky Hopper", "Pebble Pete", "Major Meltdown", "Game Master",
                "Waddle Dee", "Snowden"
            };
            int maxXp = GameData.XpForLevel(Mathf.Min(GameData.MaxLevel, 60));
            if (maxXp == int.MaxValue) maxXp = 500000;
            for (int i = 0; i < names.Length; i++)
            {
                // quadratic spread: lots of beginners, a few legends
                float t = (float)rnd.NextDouble();
                int xp = Mathf.RoundToInt(t * t * maxXp) + rnd.Next(0, 300);
                int lv = GameData.LevelForXp(xp);
                res.Add(new LeaderboardEntry { playerId = "ai" + i, name = names[i], score = xp, level = lv, wins = xp / 180 + rnd.Next(0, 10) });
            }
            res.Add(new LeaderboardEntry { playerId = "me", name = P.displayName, score = P.xp, level = P.level, wins = P.gamesWon });
            res.Sort((a, b) => b.score.CompareTo(a.score));
            return res;
        }
    }

    /// <summary>Player card: name, level, XP, stats and worn gear (opened from the top bar).</summary>
    public class ProfileScreen : MetaScreen
    {
        protected override string Title => "Your Penguin";

        protected override void BuildContent()
        {
            var P = ProfileService.P;
            var card = MetaUI.CardPanel(Content);
            UI.Anchor(card.rectTransform, 0.15f, 0.05f, 0.85f, 1);
            var v = UI.VBox(card.rectTransform, 10, TextAnchor.UpperCenter, 30);
            v.childForceExpandHeight = false;
            var name = UI.Input(card.transform, P.displayName, "Your name", s =>
            {
                s = (s ?? "").Trim();
                if (s.Length == 0 || s == P.displayName) return;
                P.displayName = s.Length > 16 ? s.Substring(0, 16) : s;
                ProfileService.Save();
            });
            UI.Layout(name, -1, 90);
            Line(card.rectTransform, "Level " + P.level, 52, Theme.Secondary, true);
            var barHost = UI.Rect(card.transform, "Xp");
            UI.Layout(barHost, -1, 56);
            int next = GameData.XpForLevel(P.level + 1);
            MetaUI.ProgressBar(barHost, Progression.LevelProgress(P.xp, P.level), Theme.Xp, next == int.MaxValue ? "MAX" : P.xp.ToString("N0") + " / " + next.ToString("N0") + " XP");
            UI.Stretch((RectTransform)barHost.GetChild(0));
            Line(card.rectTransform, "Wins " + P.gamesWon + " / " + P.gamesPlayed + " matches   -   Knock-outs " + P.kills, 34, Theme.Text, false);
            Line(card.rectTransform, "Challenges " + ChallengeCatalog.CompletedCount() + " / " + ChallengeCatalog.All.Count + "   -   Achievements claimed " + P.claimedAchievements.Count + " / " + AchievementCatalog.All.Count, 34, Theme.Text, false);
            Line(card.rectTransform, "Recipes " + P.knownRecipes.Count + " / " + CraftingCatalog.Recipes.Count + "   -   Clothes " + P.ownedClothes.Count + "   -   Trophies " + P.trophies.Count, 34, Theme.Text, false);
            Line(card.rectTransform, Progression.IsVip ? "VIP until " + System.DateTimeOffset.FromUnixTimeMilliseconds(P.vipUntilUnixMs).LocalDateTime.ToString("g") : "Not a VIP yet", 34, MetaUI.Orange, false);
            var row = UI.Rect(card.transform, "Buttons");
            UI.Layout(row, -1, 120);
            UI.HBox(row, 20, TextAnchor.MiddleCenter);
            UI.Layout(UI.Button(row, "Wardrobe", () => ScreenManager.Show(() => new WardrobeScreen()), UI.ButtonStyle.Secondary, 40), 340, 110);
            UI.Layout(UI.Button(row, "Awards", () => ScreenManager.Show(() => new AchievementsScreen()), UI.ButtonStyle.Secondary, 40), 340, 110);
        }

        static void Line(RectTransform p, string text, int size, Color c, bool title)
        {
            var l = UI.Label(p, text, size, c, TextAnchor.MiddleCenter, title);
            UI.Layout(l, -1, size + 30);
        }
    }
}
