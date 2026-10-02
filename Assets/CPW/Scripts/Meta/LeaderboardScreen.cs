using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// Leaderboards like the original LeaderboardLogic: this week / this month / all time, a PlayerReport category
    /// (XP, wins, games, knock-outs, damage, turns) and Everyone / Friends (Leaderboards.Get, needs Firebase).
    /// Offline, or on the Ladder filter, a local ladder where you climb past AI penguins by earning XP.
    /// </summary>
    public class LeaderboardScreen : MetaScreen
    {
        static LeaderboardPeriod period = LeaderboardPeriod.Week;
        static int category, filter;     // filter: 0 everyone, 1 friends, 2 offline ladder
        static readonly string[] PeriodNames = { "This week", "This month", "All time" };
        static readonly string[] FilterNames = { "Everyone", "Friends", "Ladder" };
        RectTransform list;
        List<Button> periodTabs, filterTabs, categoryTabs;
        Text status, mine;
        int requestId;
        protected override string Title => Loc.T("BUTTON_LEADERBOARD");

        protected override void BuildContent()
        {
            if (!Online.Service.Available) filter = 2;
            var row = UI.Rect(Content, "Periods");
            UI.Anchor(row, 0, 0.9f, 0.55f, 1);
            periodTabs = MetaUI.Tabs(row, PeriodNames, (int)period, i => { period = (LeaderboardPeriod)i; MetaUI.SetTabSelected(periodTabs, i); Fill(); }, 32);
            var frow = UI.Rect(Content, "Filter");
            UI.Anchor(frow, 0.56f, 0.9f, 1, 1);
            filterTabs = MetaUI.Tabs(frow, FilterNames, filter, i =>
            {
                if (i < 2 && !Online.Service.Available) { UI.Toast("Connect to play online to see the world and friends boards."); return; }
                filter = i;
                MetaUI.SetTabSelected(filterTabs, i);
                Fill();
            }, 32);
            var crow = UI.Rect(Content, "Categories");
            UI.Anchor(crow, 0, 0.8f, 1, 0.89f);
            categoryTabs = MetaUI.Tabs(crow, Leaderboards.CategoryNames, category, i => { category = i; MetaUI.SetTabSelected(categoryTabs, i); Fill(); }, 28);

            var panel = MetaUI.CardPanel(Content, MetaUI.Card);
            UI.Anchor(panel.rectTransform, 0, 0, 0.66f, 0.78f);
            status = UI.Label(panel.transform, "", 26, Theme.Muted, TextAnchor.MiddleRight);
            UI.Anchor(status.rectTransform, 0.5f, 0.92f, 0.98f, 1);
            var host = UI.Rect(panel.transform, "List");
            UI.Anchor(host, 0.005f, 0.01f, 0.995f, 0.92f);
            UI.ScrollList(host, out list, true, 6, 10);
            UI.Stretch((RectTransform)host.GetChild(0));

            // my numbers
            var me = MetaUI.CardPanel(Content, MetaUI.CardDark);
            UI.Anchor(me.rectTransform, 0.68f, 0, 1, 0.78f);
            var P = ProfileService.P;
            var v = UI.VBox(me.rectTransform, 6, TextAnchor.UpperLeft, 24);
            v.childForceExpandHeight = false;
            Stat(me.rectTransform, P.displayName, 44, MetaUI.Gold);
            Stat(me.rectTransform, "Level " + P.level + "   -   " + P.xp.ToString("N0") + " XP", 30, Color.white);
            mine = Stat(me.rectTransform, "", 30, Theme.Xp);
            Stat(me.rectTransform, "Wins: " + P.gamesWon + (P.gamesPlayed > 0 ? "  (" + Mathf.RoundToInt(100f * P.gamesWon / P.gamesPlayed) + "%)" : "") + "   Matches: " + P.gamesPlayed, 30, Color.white);
            Stat(me.rectTransform, "Knock-outs: " + P.kills + "   Deaths: " + P.deaths, 30, Color.white);
            Stat(me.rectTransform, "Best score: " + P.bestScore + "   Trophies: " + P.trophies.Count, 30, Color.white);
            var stats = UI.Button(me.transform, "All my stats", () => ScreenManager.Show(() => new StatsScreen()), UI.ButtonStyle.Primary, 36);
            UI.Layout(stats, -1, 96);
            Fill();
        }

        static Text Stat(RectTransform parent, string text, int size, Color c)
        {
            var l = UI.Label(parent, text, size, c, TextAnchor.MiddleLeft, size > 40);
            UI.Layout(l, -1, size + 20);
            return l;
        }

        string Cat => Leaderboards.Categories[Mathf.Clamp(category, 0, Leaderboards.Categories.Length - 1)];
        string CatName => Leaderboards.CategoryNames[Mathf.Clamp(category, 0, Leaderboards.CategoryNames.Length - 1)];

        void Fill()
        {
            if (list == null) return;
            UI.Clear(list);
            int req = ++requestId;
            mine.text = PeriodNames[(int)period] + ": " + Leaderboards.LocalValue(period, Cat).ToString("N0") + " " + CatName;
            if (filter == 2 || !Online.Service.Available)
            {
                status.text = Online.Service.Available ? "Offline ladder (XP)" : "Offline ladder - connect to play online for the world board";
                var ladder = LocalLadder();
                for (int i = 0; i < ladder.Count; i++) Row(i + 1, ladder[i].name, ladder[i].level, ladder[i].score.ToString("N0") + " XP", ladder[i].playerId == "me", false, false);
                return;
            }
            status.text = "Loading...";
            var root = Root;
            Leaderboards.Get(period, Cat, filter == 1, 50, (entries, err) =>
            {
                if (root == null || req != requestId || list == null) return;
                UI.Clear(list);
                if (err != null)
                {
                    status.text = "";
                    var e = UI.Label(list, "Could not load the leaderboard.\n" + err, 32, Theme.Muted);
                    UI.Layout(e, -1, 160);
                    return;
                }
                status.text = entries.Count + " penguins";
                if (entries.Count == 0)
                {
                    var l = UI.Label(list, filter == 1 ? "No friends here yet. Add some on the Friends screen!" : "Nobody here yet. Be the first!", 32, Theme.Muted);
                    UI.Layout(l, -1, 120);
                    return;
                }
                string me = FirebaseClient.I != null ? FirebaseClient.I.Uid : "";
                foreach (var e in entries) Row(e.rank, e.name, e.level, e.value.ToString("N0") + " " + CatName, e.playerId == me, filter == 1, e.online);
            });
        }

        void Row(int rank, string name, int level, string value, bool me, bool showOnline, bool online)
        {
            var row = UI.Panel(list, me ? Theme.Primary : (rank % 2 == 0 ? new Color(0.93f, 0.96f, 1f) : Color.white), true, "Row");
            UI.Layout(row, -1, 80);
            Color medal = rank == 1 ? new Color32(255, 200, 40, 255) : rank == 2 ? new Color32(200, 205, 215, 255) : rank == 3 ? new Color32(215, 140, 70, 255) : Theme.Secondary;
            var badge = MetaUI.Badge(row.transform, rank.ToString(), medal, 62);
            var brt = (RectTransform)badge.transform.parent;
            brt.anchorMin = brt.anchorMax = new Vector2(0, 0.5f);
            brt.anchoredPosition = new Vector2(48, 0);
            if (showOnline && !me)
            {
                var dot = UI.Image(row.transform, UI.Circle, online ? Theme.Good : Theme.Muted, false, "Online");
                UI.Place(dot.rectTransform, new Vector2(0.1f, 0.5f), new Vector2(22, 22), Vector2.zero);
            }
            var n = UI.Label(row.transform, name, 32, Theme.Text, TextAnchor.MiddleLeft, me);
            UI.Anchor(n.rectTransform, 0.12f, 0, 0.55f, 1);
            var lv = UI.Label(row.transform, "Lv " + level, 28, Theme.Secondary, TextAnchor.MiddleCenter, true);
            UI.Anchor(lv.rectTransform, 0.55f, 0, 0.67f, 1);
            var x = UI.Label(row.transform, value, 30, Theme.Text, TextAnchor.MiddleRight, true);
            UI.Anchor(x.rectTransform, 0.67f, 0, 0.97f, 1);
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

    /// <summary>Personal statistics (original PlayerReport personal tab): this week, this month and all time.</summary>
    public class StatsScreen : MetaScreen
    {
        protected override string Title => "My Stats";

        protected override void BuildContent()
        {
            var P = ProfileService.P;
            PlayerStatsTracker.Roll(P);
            var card = MetaUI.CardPanel(Content, MetaUI.Card);
            UI.Anchor(card.rectTransform, 0.08f, 0.1f, 0.92f, 1);
            var host = UI.Rect(card.transform, "Table");
            UI.Stretch(host, 10, 10, 10, 10);
            UI.ScrollList(host, out var table, true, 4, 14);
            UI.Stretch((RectTransform)host.GetChild(0));
            Row(table, "", Periods.WeekLabel(P.statsWeek.key), "This month", "All time", true);
            string[] cats = { "games", "wins", "xp", "kills", "deaths", "suicides", "damage", "turns", "shots", "boosters", "explosions" };
            string[] names = { "Games", "Wins", "XP earned", "Knock-outs", "Deaths", "Self knock-outs", "Damage to opponents", "Turns played", "Shots fired", "Boosters used", "Explosions" };
            for (int i = 0; i < cats.Length; i++)
            {
                int w = Leaderboards.Value(P.statsWeek, cats[i]), m = Leaderboards.Value(P.statsMonth, cats[i]);
                int a = i <= 3 || cats[i] == "damage" || cats[i] == "deaths" ? AllTime(P, cats[i]) : Leaderboards.Value(P.statsAll, cats[i]);
                Row(table, names[i], w.ToString("N0"), m.ToString("N0"), a.ToString("N0"), false);
            }
            Row(table, "Win rate", Rate(P.statsWeek.wins, P.statsWeek.games), Rate(P.statsMonth.wins, P.statsMonth.games), Rate(P.gamesWon, P.gamesPlayed), false);
            var note = UI.Label(Content, "Week and month count Quick Match and Online games (UTC weeks). All-time totals include every match since you started.", 26, Color.white);
            UI.Anchor(note.rectTransform, 0.08f, 0, 0.92f, 0.09f);
        }

        static int AllTime(PlayerProfile p, string cat)
        {
            if (cat == "deaths") return p.deaths;
            return Leaderboards.LocalValue(LeaderboardPeriod.All, cat);
        }

        static string Rate(int wins, int games) => games > 0 ? Mathf.RoundToInt(100f * wins / games) + "%" : "-";

        static void Row(RectTransform parent, string label, string a, string b, string c, bool head)
        {
            var row = UI.Panel(parent, head ? Theme.Secondary : new Color(0.93f, 0.96f, 1f), true, "Row");
            UI.Layout(row, -1, head ? 64 : 58);
            float[] xs = { 0.02f, 0.4f, 0.6f, 0.8f, 0.98f };
            string[] vals = { label, a, b, c };
            for (int i = 0; i < 4; i++)
            {
                var l = UI.Label(row.transform, vals[i], head ? 30 : 30, head ? Color.white : Theme.Text, i == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, head || i == 0);
                UI.Anchor(l.rectTransform, xs[i], 0, xs[i + 1], 1);
            }
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
            UI.Anchor(card.rectTransform, 0.08f, 0.02f, 0.92f, 1);
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
            var codeLine = Line(card.rectTransform, Social.Available ? "Friend code: ..." : "Friend code: connect to play online", 34, Theme.Secondary, true);
            Social.GetMyCode(c => { if (codeLine) codeLine.text = string.IsNullOrEmpty(c) ? "Friend code: connect to play online" : "Friend code: " + c; });
            Line(card.rectTransform, Progression.IsVip ? "VIP until " + System.DateTimeOffset.FromUnixTimeMilliseconds(P.vipUntilUnixMs).LocalDateTime.ToString("g") : "Not a VIP yet", 34, MetaUI.Orange, false);
            var row = UI.Rect(card.transform, "Buttons");
            UI.Layout(row, -1, 120);
            UI.HBox(row, 20, TextAnchor.MiddleCenter);
            UI.Layout(UI.Button(row, "Wardrobe", () => ScreenManager.Show(() => new WardrobeScreen()), UI.ButtonStyle.Secondary, 38), 300, 104);
            UI.Layout(UI.Button(row, "Awards", () => ScreenManager.Show(() => new AchievementsScreen()), UI.ButtonStyle.Secondary, 38), 300, 104);
            UI.Layout(UI.Button(row, Loc.T("BUTTON_NEIGHBORS"), () => ScreenManager.Show(() => new FriendsScreen()), UI.ButtonStyle.Secondary, 38), 300, 104);
            UI.Layout(UI.Button(row, "My stats", () => ScreenManager.Show(() => new StatsScreen()), UI.ButtonStyle.Secondary, 38), 300, 104);
        }

        static Text Line(RectTransform p, string text, int size, Color c, bool title)
        {
            var l = UI.Label(p, text, size, c, TextAnchor.MiddleCenter, title);
            UI.Layout(l, -1, size + 30);
            return l;
        }
    }
}
