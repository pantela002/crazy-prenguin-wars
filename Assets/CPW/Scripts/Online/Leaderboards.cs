using System;
using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    public enum LeaderboardPeriod { Week, Month, All }

    /// <summary>
    /// Online leaderboards like the original LeaderboardLogic (weekly / monthly / all time) with a choice of
    /// PlayerReport category and a friends-only filter. Data: leaderboard/{period}/{uid} written by
    /// FirebaseService.SubmitStats. Everything reports through done(list, error); error is null on success.
    /// </summary>
    public static class Leaderboards
    {
        public const string AllKey = "all";

        /// <summary>Category field and its label (fields are indexed in database.rules.json).</summary>
        public static readonly string[] Categories = { "xp", "wins", "games", "kills", "damage", "turns" };
        public static readonly string[] CategoryNames = { "XP", "Wins", "Games", "Knock-outs", "Damage", "Turns" };

        public static string PeriodKey(LeaderboardPeriod p) =>
            p == LeaderboardPeriod.All ? AllKey : p == LeaderboardPeriod.Week ? Periods.CurrentWeek : Periods.CurrentMonth;

        static FirebaseClient Client => Online.Service.Available ? FirebaseClient.I : null;

        public static void Get(LeaderboardPeriod period, string category, bool friendsOnly, int count, Action<List<LeaderboardEntry>, string> done)
        {
            var fb = Client;
            if (fb == null) { done?.Invoke(new List<LeaderboardEntry>(), "offline"); return; }
            string path = "leaderboard/" + PeriodKey(period);
            if (!friendsOnly)
            {
                fb.Get(path, r =>
                {
                    if (!r.ok) { done?.Invoke(new List<LeaderboardEntry>(), r.error); return; }
                    var list = new List<LeaderboardEntry>();
                    if (r.Obj != null)
                        foreach (var kv in r.Obj) { var e = Parse(kv.Key, Fb.Dict(kv.Value), category); if (e != null) list.Add(e); }
                    Finish(list, done);
                }, "orderBy=" + Fb.Q(category) + "&limitToLast=" + Mathf.Clamp(count, 1, 200));
                return;
            }

            // Friends: read our own entry and each friend's entry.
            Social.LoadFriends((friends, err) =>
            {
                if (err != null) { done?.Invoke(new List<LeaderboardEntry>(), err); return; }
                var ids = new List<string> { fb.Uid };
                var online = new Dictionary<string, bool>();
                foreach (var f in friends) { ids.Add(f.uid); online[f.uid] = f.online; }
                var list = new List<LeaderboardEntry>();
                int pending = ids.Count;
                foreach (var id in ids)
                {
                    var uid = id;
                    fb.Get(path + "/" + uid, r =>
                    {
                        var e = r.ok ? Parse(uid, r.Obj, category) : null;
                        if (e == null)
                        {
                            // a friend without games in this period still shows up, with 0
                            var f = friends.Find(x => x.uid == uid);
                            if (f != null) e = new LeaderboardEntry { playerId = uid, name = f.name, level = f.level };
                            else if (uid == fb.Uid) e = new LeaderboardEntry { playerId = uid, name = ProfileService.P.displayName, level = ProfileService.P.level };
                        }
                        if (e != null) { e.online = online.TryGetValue(uid, out var o) && o; list.Add(e); }
                        if (--pending == 0) Finish(list, done);
                    });
                }
            });
        }

        static LeaderboardEntry Parse(string uid, Dictionary<string, object> d, string category)
        {
            if (d == null) return null;
            return new LeaderboardEntry
            {
                playerId = uid,
                name = Fb.Str(d, "name", "Penguin"),
                score = Fb.Int(d, "xp"),
                level = Fb.Int(d, "level", 1),
                wins = Fb.Int(d, "wins"),
                value = Fb.Int(d, category),
            };
        }

        static void Finish(List<LeaderboardEntry> list, Action<List<LeaderboardEntry>, string> done)
        {
            list.Sort((a, b) => b.value != a.value ? b.value.CompareTo(a.value) : b.score.CompareTo(a.score));
            for (int i = 0; i < list.Count; i++) list[i].rank = i + 1;
            done?.Invoke(list, null);
        }

        /// <summary>The local player's value of a category in a period (from the profile, works offline).</summary>
        public static int LocalValue(LeaderboardPeriod period, string category)
        {
            var p = ProfileService.P;
            PlayerStatsTracker.Roll(p);
            if (period == LeaderboardPeriod.All)
            {
                switch (category)
                {
                    case "xp": return p.xp;
                    case "wins": return p.gamesWon;
                    case "games": return p.gamesPlayed;
                    case "kills": return p.kills;
                    case "damage": return (int)Math.Min(p.totalDamage, int.MaxValue);
                    default: return Value(p.statsAll, category);
                }
            }
            return Value(period == LeaderboardPeriod.Week ? p.statsWeek : p.statsMonth, category);
        }

        public static int Value(PeriodStats s, string category)
        {
            if (s == null) return 0;
            switch (category)
            {
                case "xp": return s.xp;
                case "wins": return s.wins;
                case "games": return s.games;
                case "kills": return s.kills;
                case "deaths": return s.deaths;
                case "suicides": return s.suicides;
                case "damage": return s.damage;
                case "turns": return s.turns;
                case "shots": return s.shots;
                case "boosters": return s.boosters;
                case "explosions": return s.explosions;
                default: return 0;
            }
        }
    }
}
