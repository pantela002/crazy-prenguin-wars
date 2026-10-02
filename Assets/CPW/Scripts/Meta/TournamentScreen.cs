using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// Weekly league (original TournamentScreen / League.as): your tier, points and games this week, time until the
    /// week ends, how points are earned, promotion / relegation zones and the standings of your tier. Points are
    /// counted offline too; the standings and the end-of-week settlement need the online service.
    /// </summary>
    public class TournamentScreen : MetaScreen
    {
        bool alive;
        Text timeLeft;
        RectTransform list;
        Text status;
        float clockTimer;
        protected override string Title => "Weekly League";

        protected override void BuildContent()
        {
            alive = true;
            League.RollWeek();
            var P = ProfileService.P;
            int tier = League.Tier;

            // ---- left: my league ----
            var info = MetaUI.CardPanel(Content, MetaUI.CardDark, "MyLeague");
            UI.Anchor(info.rectTransform, 0, 0, 0.4f, 1);
            // scrolls when the screen is short (21:9 phones)
            var infoHost = UI.Rect(info.transform, "Scroll");
            UI.Stretch(infoHost, 6, 6, 6, 6);
            UI.ScrollList(infoHost, out var col, true, 8, 20);
            UI.Stretch((RectTransform)infoHost.GetChild(0));
            var badgeRow = UI.Rect(col, "Tier");
            UI.Layout(badgeRow, -1, 110);
            var badge = UI.Image(badgeRow, UI.Circle, League.TierColors[tier], false, "Badge");
            UI.Place(badge.rectTransform, new Vector2(0, 0.5f), new Vector2(100, 100), Vector2.zero);
            var roman = UI.Label(badge.transform, (tier + 1).ToString(), 54, Color.white, TextAnchor.MiddleCenter, true);
            UI.Stretch(roman.rectTransform);
            var tn = UI.Label(badgeRow, League.TierName(tier) + " League", 50, League.TierColors[tier], TextAnchor.MiddleLeft, true);
            UI.Anchor(tn.rectTransform, 0, 0, 1, 1);
            tn.rectTransform.offsetMin = new Vector2(120, 0);

            Line(col, Periods.WeekLabel(P.leagueWeek), 32, Color.white, true);
            timeLeft = Line(col, "", 30, Theme.Xp, false);
            UpdateClock();
            if (!League.Unlocked)
                Line(col, "The league opens at level " + League.RequiredLevel + ". Keep battling!", 30, MetaUI.Orange, false, 90);
            else
            {
                Line(col, "Your points: " + League.Points, 40, MetaUI.Gold, true);
                Line(col, "Games counted: " + League.Games + " / " + League.MaxGamesPerWeek, 30, Color.white, false);
            }
            Line(col, "Points per place (1st / 2nd / 3rd / 4th):", 28, new Color(1, 1, 1, 0.85f), false);
            Line(col, "Online  " + string.Join(" / ", Array.ConvertAll(League.OnlinePoints, x => x.ToString())) +
                                    "     vs AI  " + string.Join(" / ", Array.ConvertAll(League.QuickPoints, x => x.ToString())), 28, Color.white, false);
            Line(col, "Top " + Mathf.RoundToInt(League.PromoteShare * 100) + "% move up, bottom " + Mathf.RoundToInt(League.RelegateShare * 100) +
                                    "% move down. Rewards for the top places every week!", 26, new Color(1, 1, 1, 0.8f), false, 76);
            if (!string.IsNullOrEmpty(P.leagueLastResult)) Line(col, P.leagueLastResult, 28, Theme.Good, false, 76);
            var play = UI.Button(col, "Play now", QuickPlay.Start, UI.ButtonStyle.Primary, 42);
            UI.Layout(play, -1, 104);

            // ---- right: standings ----
            var panel = MetaUI.CardPanel(Content, MetaUI.Card, "Standings");
            UI.Anchor(panel.rectTransform, 0.42f, 0, 1, 1);
            var head = UI.Label(panel.transform, League.TierName(tier) + " standings", 44, Theme.Secondary, TextAnchor.MiddleLeft, true);
            UI.Anchor(head.rectTransform, 0.03f, 0.88f, 0.6f, 0.99f);
            status = UI.Label(panel.transform, "", 28, Theme.Muted, TextAnchor.MiddleRight);
            UI.Anchor(status.rectTransform, 0.6f, 0.88f, 0.97f, 0.99f);
            var host = UI.Rect(panel.transform, "List");
            UI.Anchor(host, 0.01f, 0.01f, 0.99f, 0.87f);
            UI.ScrollList(host, out list, true, 6, 10);
            UI.Stretch((RectTransform)host.GetChild(0));
            LoadStandings();
        }

        static Text Line(RectTransform p, string text, int size, Color c, bool title, float height = -1)
        {
            var l = UI.Label(p, text, size, c, TextAnchor.MiddleLeft, title);
            UI.Layout(l, -1, height > 0 ? height : size + 18);
            return l;
        }

        void UpdateClock()
        {
            var left = Periods.WeekEnd - Periods.UtcNow;
            if (left.TotalSeconds < 0) left = TimeSpan.Zero;
            timeLeft.text = "Ends in " + (left.Days > 0 ? left.Days + "d " : "") + left.Hours + "h " + left.Minutes.ToString("00") + "m";
        }

        void LoadStandings()
        {
            if (!Social.Available)
            {
                status.text = "";
                var l = UI.Label(list, "Connect to play online to see the standings.\nYour points still count and are sent when you're back online.", 32, Theme.Muted);
                UI.Layout(l, -1, 200);
                return;
            }
            League.Upload();
            League.TrySettle();
            status.text = "Loading...";
            var P = ProfileService.P;
            League.Standings(P.leagueWeek, League.Tier, (entries, err) =>
            {
                if (!alive || !list) return;
                UI.Clear(list);
                if (err != null) { status.text = ""; var e = UI.Label(list, "Could not load the standings.\n" + err, 32, Theme.Muted); UI.Layout(e, -1, 160); return; }
                // our own (possibly not yet uploaded) score
                if (League.Games > 0 && !entries.Exists(x => x.isLocal))
                {
                    entries.Add(new LeagueEntry { uid = "me", name = P.displayName, level = P.level, points = League.Points, games = League.Games, isLocal = true });
                    entries.Sort((a, b) => a.points != b.points ? b.points.CompareTo(a.points) : a.games.CompareTo(b.games));
                    for (int i = 0; i < entries.Count; i++) entries[i].rank = i + 1;
                }
                status.text = entries.Count + " penguins";
                if (entries.Count == 0)
                {
                    var l = UI.Label(list, "Nobody has played in this league this week yet.\nWin a match to take the lead!", 32, Theme.Muted);
                    UI.Layout(l, -1, 160);
                    return;
                }
                int n = entries.Count, up = League.PromoteCount(n), down = League.RelegateCount(n);
                foreach (var e in entries) Row(e, e.rank <= up && League.Tier < League.TierNames.Length - 1, e.rank > n - down && League.Tier > 0);
            });
        }

        void Row(LeagueEntry e, bool promote, bool relegate)
        {
            Color bg = e.isLocal ? Theme.Primary : promote ? new Color(0.82f, 0.95f, 0.8f) : relegate ? new Color(1f, 0.85f, 0.83f) : (e.rank % 2 == 0 ? new Color(0.93f, 0.96f, 1f) : Color.white);
            var row = UI.Panel(list, bg, true, "Row");
            UI.Layout(row, -1, 80);
            var rb = MetaUI.Badge(row.transform, e.rank.ToString(), promote ? Theme.Good : relegate ? Theme.Danger : Theme.Secondary, 60);
            var brt = (RectTransform)rb.transform.parent;
            brt.anchorMin = brt.anchorMax = new Vector2(0, 0.5f);
            brt.anchoredPosition = new Vector2(46, 0);
            var n = UI.Label(row.transform, e.name, 32, Theme.Text, TextAnchor.MiddleLeft, e.isLocal);
            UI.Anchor(n.rectTransform, 0.12f, 0, 0.55f, 1);
            var lv = UI.Label(row.transform, "Lv " + e.level, 28, Theme.Secondary, TextAnchor.MiddleCenter, true);
            UI.Anchor(lv.rectTransform, 0.55f, 0, 0.67f, 1);
            var g = UI.Label(row.transform, e.games + " games", 26, Theme.Muted, TextAnchor.MiddleRight);
            UI.Anchor(g.rectTransform, 0.67f, 0, 0.82f, 1);
            var pts = UI.Label(row.transform, e.points + " pts", 32, Theme.Text, TextAnchor.MiddleRight, true);
            UI.Anchor(pts.rectTransform, 0.82f, 0, 0.97f, 1);
        }

        public override void OnHide() => alive = false;

        public override void Tick(float dt)
        {
            if ((clockTimer += dt) < 20f) return;
            clockTimer = 0;
            UpdateClock();
        }
    }
}
