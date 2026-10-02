using System;
using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    public class LeagueEntry
    {
        public string uid, name;
        public int level, points, games, rank;
        public bool isLocal;
    }

    /// <summary>
    /// Weekly league, the remake of the original TournamentScreen / TournamentManager / League.as: everyone in a tier
    /// collects points from Quick Match and Online results during an ISO week (UTC); when the week is over the top of
    /// the tier is promoted, the bottom relegated, and position rewards are paid. Settled client-side the first time
    /// the player is online after the week ended (TrySettle).
    ///
    /// The original League / TournamentConfiguration / LeagueReward tables are empty in the shipped config, so every
    /// number here is invented: 5 tiers, points 20/12/6/2 online and 10/6/3/1 vs AI per place (shifted down in games with
    /// fewer than 4 penguins, like the battle server's position bonus), 50 counted games per week, top 20% promoted,
    /// bottom 20% relegated (when 5+ played), rewards below. Data: league/{week}/{tier}/{uid} {name, level, points, games}.
    /// </summary>
    public static class League
    {
        public static readonly string[] TierNames = { "Bronze", "Silver", "Gold", "Platinum", "Diamond" };
        public static readonly Color[] TierColors =
        {
            new Color32(205, 127, 50, 255), new Color32(190, 198, 210, 255), new Color32(255, 196, 36, 255),
            new Color32(120, 220, 230, 255), new Color32(170, 120, 255, 255)
        };
        public const int RequiredLevel = 3;
        public const int MaxGamesPerWeek = 50;
        public static readonly int[] OnlinePoints = { 20, 12, 6, 2 };
        public static readonly int[] QuickPoints = { 10, 6, 3, 1 };
        public const float PromoteShare = 0.2f, RelegateShare = 0.2f;
        // position rewards (coins, fish) for 1st, 2nd, 3rd; then top half and "took part" (min 1 game). × (1 + tier / 2)
        static readonly int[] RewardCoins = { 500, 300, 200, 100, 50 };
        static readonly int[] RewardCash = { 5, 3, 2, 0, 0 };

        static bool settling;
        static PlayerProfile P => ProfileService.P;
        static FirebaseClient Client => Online.Service.Available ? FirebaseClient.I : null;

        public static int Tier => Mathf.Clamp(P.leagueTier, 0, TierNames.Length - 1);
        public static string TierName(int t) => TierNames[Mathf.Clamp(t, 0, TierNames.Length - 1)];
        public static bool Unlocked => P.level >= RequiredLevel;

        /// <summary>Points of the current week (rolled over if a new week started).</summary>
        public static int Points { get { RollWeek(); return P.leaguePoints; } }
        public static int Games { get { RollWeek(); return P.leagueGames; } }

        /// <summary>Points for finishing at rank (1-based) in a match of `players` penguins.</summary>
        public static int PointsFor(BattleMode mode, int rank, int players)
        {
            var table = mode == BattleMode.Online ? OnlinePoints : QuickPoints;
            int idx = rank - 1 + (4 - Mathf.Clamp(players, 2, 4));
            return idx >= 0 && idx < table.Length ? table[idx] : 0;
        }

        /// <summary>Move a finished week into the pending slot (settled when online) and start the new one.</summary>
        public static void RollWeek()
        {
            var p = P;
            string week = Periods.CurrentWeek;
            if (p.leagueWeek == week) return;
            if (!string.IsNullOrEmpty(p.leagueWeek) && p.leagueGames > 0)
            {
                // Only the latest finished week is kept; an older unsettled one (offline for weeks) is dropped.
                p.leaguePendingWeek = p.leagueWeek;
                p.leaguePendingTier = p.leagueTier;
                p.leaguePendingPoints = p.leaguePoints;
                p.leaguePendingGames = p.leagueGames;
            }
            p.leagueWeek = week;
            p.leaguePoints = 0;
            p.leagueGames = 0;
        }

        /// <summary>Called by PlayerStatsTracker at the end of every ranked match (before the profile is saved).</summary>
        public static void RecordMatch(BattleResult r)
        {
            if (r == null || r.config == null || r.Local == null || !Unlocked) return;
            if (r.config.mode != BattleMode.Online && r.config.mode != BattleMode.QuickMatch) return;
            RollWeek();
            var p = P;
            if (p.leagueGames >= MaxGamesPerWeek) return;
            int pts = PointsFor(r.config.mode, r.Local.rank, r.players.Count);
            p.leagueGames++;
            p.leaguePoints += pts;
            if (pts > 0) UI.Toast("League: +" + pts + " points", MetaUI.Gold);
            Upload();
        }

        /// <summary>Write our standing for this week (no-op offline; the next match or the tournament screen retries).</summary>
        public static void Upload()
        {
            var fb = Client;
            if (fb == null) return;
            RollWeek();
            var p = P;
            if (p.leagueGames <= 0) return;
            fb.Put("league/" + p.leagueWeek + "/" + Tier + "/" + fb.Uid, new Dictionary<string, object>
            {
                { "name", string.IsNullOrEmpty(p.displayName) ? "Penguin" : (p.displayName.Length > 32 ? p.displayName.Substring(0, 32) : p.displayName) },
                { "level", p.level },
                { "points", p.leaguePoints },
                { "games", p.leagueGames },
                { "updated", Fb.ServerTime },
            }, r => { if (!r.ok) Debug.LogWarning("CPW: league update failed: " + r.error); });
        }

        /// <summary>Standings of a tier in a week, best first (done(list, error)).</summary>
        public static void Standings(string week, int tier, Action<List<LeagueEntry>, string> done)
        {
            var fb = Client;
            var list = new List<LeagueEntry>();
            if (fb == null) { done?.Invoke(list, "offline"); return; }
            fb.Get("league/" + week + "/" + tier, r =>
            {
                if (!r.ok) { done?.Invoke(list, r.error); return; }
                if (r.Obj != null)
                    foreach (var kv in r.Obj)
                    {
                        var d = Fb.Dict(kv.Value);
                        if (d == null || Fb.Int(d, "games") <= 0) continue;
                        list.Add(new LeagueEntry
                        {
                            uid = kv.Key, name = Fb.Str(d, "name", "Penguin"), level = Fb.Int(d, "level", 1),
                            points = Fb.Int(d, "points"), games = Fb.Int(d, "games"), isLocal = kv.Key == fb.Uid,
                        });
                    }
                Sort(list);
                done?.Invoke(list, null);
            }, "orderBy=" + Fb.Q("points") + "&limitToLast=200");
        }

        static void Sort(List<LeagueEntry> list)
        {
            // more points first; on a tie, fewer games (more efficient) first
            list.Sort((a, b) => a.points != b.points ? b.points.CompareTo(a.points) : a.games.CompareTo(b.games));
            for (int i = 0; i < list.Count; i++) list[i].rank = i + 1;
        }

        public static int PromoteCount(int players) => players <= 0 ? 0 : Mathf.Max(1, Mathf.FloorToInt(players * PromoteShare));
        public static int RelegateCount(int players) => players < 5 ? 0 : Mathf.Max(1, Mathf.FloorToInt(players * RelegateShare));

        public static void Rewards(int rank, int players, int tier, out int coins, out int cash)
        {
            int i = rank >= 1 && rank <= 3 ? rank - 1 : (rank <= Mathf.CeilToInt(players / 2f) ? 3 : 4);
            float mul = 1f + Mathf.Clamp(tier, 0, TierNames.Length - 1) * 0.5f;
            coins = Mathf.RoundToInt(RewardCoins[i] * mul);
            cash = Mathf.RoundToInt(RewardCash[i] * mul);
        }

        /// <summary>
        /// Settle the finished week once we are online: read its final standings, apply promotion / relegation and
        /// pay the position reward. Safe to call often (does nothing when there is nothing to settle).
        /// </summary>
        public static void TrySettle()
        {
            RollWeek();
            var p = P;
            if (settling || string.IsNullOrEmpty(p.leaguePendingWeek) || Client == null) return;
            settling = true;
            string week = p.leaguePendingWeek;
            int tier = Mathf.Clamp(p.leaguePendingTier, 0, TierNames.Length - 1);
            Standings(week, tier, (list, err) =>
            {
                settling = false;
                if (err != null || P.leaguePendingWeek != week) return;   // retry later
                var fb = Client;
                string me = fb != null ? fb.Uid : "";
                // Our final entry may be missing (upload failed while offline): add it from the profile.
                if (!list.Exists(e => e.uid == me))
                {
                    list.Add(new LeagueEntry { uid = me, name = P.displayName, points = P.leaguePendingPoints, games = P.leaguePendingGames, isLocal = true });
                    Sort(list);
                }
                var mine = list.Find(e => e.uid == me);
                int n = list.Count, rank = mine != null ? mine.rank : n;
                Rewards(rank, n, tier, out int coins, out int cash);
                int newTier = tier;
                string move = "You stay in " + TierName(tier) + ".";
                if (rank <= PromoteCount(n) && tier < TierNames.Length - 1) { newTier = tier + 1; move = "Promoted to " + TierName(newTier) + "!"; }
                else if (rank > n - RelegateCount(n) && tier > 0) { newTier = tier - 1; move = "Relegated to " + TierName(newTier) + "."; }

                var prof = P;
                prof.leaguePendingWeek = "";
                prof.leaguePendingPoints = prof.leaguePendingGames = 0;
                // Points already earned this week move with us to the new tier.
                if (newTier != prof.leagueTier)
                {
                    if (fb != null && prof.leagueGames > 0) fb.Delete("league/" + prof.leagueWeek + "/" + prof.leagueTier + "/" + me);
                    prof.leagueTier = newTier;
                    Upload();
                }
                Progression.AddCoins(coins);
                Progression.AddCash(cash);
                prof.leagueLastResult = Periods.WeekLabel(week) + ": " + Ordinal(rank) + " of " + n + " in " + TierName(tier) + ". " + move;
                ProfileService.Save();
                AudioManager.Sfx("LevelUpSound");
                UI.Message("League results",
                    Periods.WeekLabel(week) + " is over!\n\nYou finished " + Ordinal(rank) + " of " + n + " in the " + TierName(tier) + " league.\n" + move +
                    "\n\nReward: " + coins + " coins" + (cash > 0 ? " and " + cash + " fish" : "") + ".");
            });
        }

        public static string Ordinal(int n)
        {
            int m100 = n % 100, m10 = n % 10;
            string suf = m100 >= 11 && m100 <= 13 ? "th" : m10 == 1 ? "st" : m10 == 2 ? "nd" : m10 == 3 ? "rd" : "th";
            return n + suf;
        }
    }
}
