using System;
using System.Globalization;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Leaderboard / league periods in UTC so every player agrees on them: ISO weeks ("2026-W40", Monday 00:00 UTC to
    /// Monday) and months ("2026-10"). Uses the Firebase server clock when it is known.
    /// </summary>
    public static class Periods
    {
        public static DateTime UtcNow
        {
            get
            {
                var fb = FirebaseClient.I;
                long ms = fb != null ? fb.ServerNowMs : Fb.NowMs;
                return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
            }
        }

        /// <summary>ISO 8601 week: the week belongs to the year of its Thursday.</summary>
        public static string WeekKey(DateTime utc)
        {
            var d = utc.Date;
            int dow = ((int)d.DayOfWeek + 6) % 7;   // Monday = 0
            var thursday = d.AddDays(3 - dow);
            int week = (thursday.DayOfYear - 1) / 7 + 1;
            return thursday.Year.ToString(CultureInfo.InvariantCulture) + "-W" + week.ToString("00", CultureInfo.InvariantCulture);
        }

        public static string MonthKey(DateTime utc) => utc.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        public static string CurrentWeek => WeekKey(UtcNow);
        public static string CurrentMonth => MonthKey(UtcNow);
        /// <summary>Today as yyyyMMdd (UTC), e.g. for one gift per friend per day.</summary>
        public static string Today => UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        /// <summary>When the current week ends (next Monday 00:00 UTC).</summary>
        public static DateTime WeekEnd
        {
            get
            {
                var d = UtcNow.Date;
                int dow = ((int)d.DayOfWeek + 6) % 7;
                return d.AddDays(7 - dow);
            }
        }

        /// <summary>"Week 40" from "2026-W40".</summary>
        public static string WeekLabel(string key)
        {
            int i = string.IsNullOrEmpty(key) ? -1 : key.IndexOf("-W", StringComparison.Ordinal);
            return i < 0 ? (key ?? "") : "Week " + key.Substring(i + 2).TrimStart('0');
        }
    }

    /// <summary>
    /// Collects the original PlayerReport statistics (games, wins, XP, kills, deaths, suicides, turns, damage to
    /// opponents, shots, boosters, explosions) for the local player into the profile's weekly, monthly and all-time
    /// PeriodStats, from the global BattleEvents. Only ranked modes (Quick Match and Online) count.
    /// Stats without a battle event (distance walked, jumps, missile air time, terrain destroyed, emoticons) are not
    /// collected. Feeds the weekly league (League.RecordMatch) at the end of each counted match.
    /// </summary>
    public static class PlayerStatsTracker
    {
        static bool installed, tracking;
        static int me = -1;
        static PlayerProfile seenProfile;
        static int seenXp;

        static PlayerProfile P => ProfileService.P;

        public static void Install()
        {
            if (installed) return;
            installed = true;
            BattleEvents.BattleStarted += OnStarted;
            BattleEvents.BattleEnded += OnEnded;
            BattleEvents.TurnStarted += OnTurn;
            BattleEvents.WeaponFired += OnFired;
            BattleEvents.PenguinDamaged += OnDamaged;
            BattleEvents.PenguinKilled += OnKilled;
            BattleEvents.BoosterUsed += OnBooster;
            BattleEvents.Explosion += OnExplosion;
            ProfileService.Changed += OnProfileChanged;
            seenProfile = P;
            seenXp = P.xp;
        }

        /// <summary>Start new counters when the week or month changed (call before reading or uploading them).</summary>
        public static void Roll(PlayerProfile p)
        {
            if (p == null) return;
            if (p.statsWeek == null) p.statsWeek = new PeriodStats();
            if (p.statsMonth == null) p.statsMonth = new PeriodStats();
            if (p.statsAll == null) p.statsAll = new PeriodStats();
            var now = Periods.UtcNow;
            string w = Periods.WeekKey(now), m = Periods.MonthKey(now);
            if (p.statsWeek.key != w) p.statsWeek.Reset(w);
            if (p.statsMonth.key != m) p.statsMonth.Reset(m);
            if (p.statsAll.key != Leaderboards.AllKey) p.statsAll.key = Leaderboards.AllKey;
        }

        static void Each(Action<PeriodStats> a)
        {
            var p = P;
            Roll(p);
            a(p.statsWeek);
            a(p.statsMonth);
            a(p.statsAll);
        }

        static void OnStarted(BattleConfig c)
        {
            tracking = c != null && (c.mode == BattleMode.QuickMatch || c.mode == BattleMode.Online);
            me = c != null ? c.LocalPlayerIndex : -1;
        }

        static void OnTurn(int player) { if (tracking && player == me) Each(s => s.turns++); }
        static void OnFired(int player, string item) { if (tracking && player == me) Each(s => s.shots++); }
        static void OnBooster(int player, string item) { if (tracking && player == me) Each(s => s.boosters++); }

        static void OnDamaged(int victim, int attacker, float amount, string item)
        {
            if (!tracking || attacker != me || victim == me || amount <= 0) return;
            int a = Mathf.RoundToInt(amount);
            Each(s => s.damage += a);
        }

        static void OnKilled(int victim, int killer, string item)
        {
            if (!tracking) return;
            if (victim == me)
            {
                Each(s => s.deaths++);
                if (killer == me) Each(s => s.suicides++);
            }
            else if (killer == me) Each(s => s.kills++);
        }

        static void OnExplosion(Vector2 pos, float radius)
        {
            if (tracking && BattleWorld.ActivePlayer == me) Each(s => s.explosions++);
        }

        static void OnEnded(BattleResult r)
        {
            bool counted = tracking && r != null && !r.aborted;
            tracking = false;
            if (!counted) return;
            var local = r.Local;
            bool won = local != null && local.rank == 1;
            Each(s => { s.games++; if (won) s.wins++; });
            try { League.RecordMatch(r); }
            catch (Exception e) { Debug.LogException(e); }
            // RewardService saves the profile (and uploads stats) right after this.
        }

        /// <summary>XP of the period = every XP gain seen on the profile (battles, achievements, daily rewards...).</summary>
        static void OnProfileChanged()
        {
            var p = P;
            if (!ReferenceEquals(p, seenProfile))
            {
                // profile replaced (cloud restore / reset): new baseline
                seenProfile = p;
                seenXp = p.xp;
                return;
            }
            int delta = p.xp - seenXp;
            seenXp = p.xp;
            if (delta > 0) Each(s => s.xp += delta);
        }
    }
}
