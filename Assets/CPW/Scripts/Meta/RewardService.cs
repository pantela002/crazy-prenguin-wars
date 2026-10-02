using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>What the local player got from the last battle (shown by ResultsScreen).</summary>
    public class RewardSummary
    {
        public int baseCoins, baseXp, cash;
        public int vipCoins, vipXp;          // extra from VIP (BattleOptions GCVIPMultiplier / XPVIPMultiplier)
        public int betCoins, betCash;        // > 0 won, < 0 lost
        public bool betPlaced;
        public int oldXp, oldLevel, newXp, newLevel;
        public List<ItemStack> items = new List<ItemStack>();
        public bool won, practice;
        public int TotalCoins => baseCoins + vipCoins + Mathf.Max(0, betCoins);
        public int TotalXp => baseXp + vipXp;
    }

    /// <summary>
    /// MetaHooks.ApplyRewards: puts the battle's rewards into the profile. Battle code already gives base
    /// coins/xp/cash (and has deducted the ammo used); here we add VIP bonuses, bet payouts, earned items, a chance
    /// at a crafting ingredient, the original Counters, challenge progress and level-ups, then save.
    /// </summary>
    public static class RewardService
    {
        public static RewardSummary Last { get; private set; }

        public static void Apply(BattleResult result)
        {
            var P = ProfileService.P;
            var s = new RewardSummary { oldXp = P.xp, oldLevel = P.level };
            Last = s;
            if (result == null || result.aborted) { s.newXp = P.xp; s.newLevel = P.level; return; }

            var achBefore = AchievementCatalog.DoneSet();
            var cfg = result.config ?? new BattleConfig();
            var me = result.Local;
            bool rewarded = cfg.mode == BattleMode.QuickMatch || cfg.mode == BattleMode.Online;
            s.practice = !rewarded;
            s.won = me != null && me.rank == 1;

            if (me != null)
            {
                s.baseCoins = Mathf.Max(0, me.coins);
                s.baseXp = Mathf.Max(0, me.xp);
                s.cash = Mathf.Max(0, me.cash);
                if (Progression.IsVip && rewarded)
                {
                    var opt = GameData.Battle;
                    s.vipCoins = Mathf.RoundToInt(s.baseCoins * ((opt?.Float("GCVIPMultiplier", 1.5f) ?? 1.5f) - 1f));
                    s.vipXp = Mathf.RoundToInt(s.baseXp * ((opt?.Float("XPVIPMultiplier", 1.5f) ?? 1.5f) - 1f));
                }
                foreach (var it in me.earnedItems) if (it != null && it.amount > 0) s.items.Add(new ItemStack(it.id, it.amount));
            }

            // Bets: the stake was paid in BattleFactory.Launch; the winner takes the pot (stake × players).
            int bc = BattleFactory.BetCoins(cfg.betId), bf = BattleFactory.BetCash(cfg.betId);
            if ((bc > 0 || bf > 0) && cfg.mode != BattleMode.Online)
            {
                s.betPlaced = true;
                int pot = Mathf.Max(2, cfg.players.Count);
                if (s.won) { s.betCoins = bc * pot; s.betCash = bf * pot; }
                else { s.betCoins = -bc; s.betCash = -bf; }
            }

            // Crafting ingredient drop (see CraftingCatalog notes)
            if (rewarded)
            {
                float luck = 1f + ClothesCatalog.Stat(new[] { P.wornHead, P.wornChest, P.wornFeet, P.wornTrophy }, "Luck").add / 100f;
                if (Random.value < (s.won ? 0.35f : 0.15f) * luck) s.items.Add(new ItemStack(CraftingCatalog.RandomIngredient(false), 1));
                if (Random.value < 0.05f * luck) s.items.Add(new ItemStack(CraftingCatalog.RandomIngredient(true), 1));
            }

            // ---- apply ----
            Progression.AddCoins(s.baseCoins + s.vipCoins + Mathf.Max(0, s.betCoins));
            Progression.AddCash(s.cash + Mathf.Max(0, s.betCash));
            foreach (var it in s.items) Progression.GiveItem(it.id, it.amount);

            // ---- original counters ----
            if (cfg.mode == BattleMode.Tutorial)
            {
                ChallengeTracker.SetMax("Completed_Tutorial", 1);
                P.tutorialDone = true;
            }
            if (cfg.mode != BattleMode.Tutorial)
            {
                P.AddCounter("Games_Played", 1);
                P.gamesPlayed++;
                if (s.won) { P.AddCounter("Games_Won", 1); P.gamesWon++; }
                if (me != null)
                {
                    P.kills += me.kills;
                    P.deaths += me.deaths;
                    P.totalDamage += me.damageDealt;
                    P.bestScore = Mathf.Max(P.bestScore, me.score);
                }
                if (s.baseCoins + s.vipCoins > 0) P.AddCounter("Earn_Coins_Matches", s.baseCoins + s.vipCoins);
                if (!string.IsNullOrEmpty(cfg.levelId))
                {
                    if (P.Counter("map." + cfg.levelId) == 0)
                    {
                        P.AddCounter("map." + cfg.levelId, 1);
                        P.AddCounter("Levels", 1);
                    }
                }
                if (s.won) CountWinWeapons();
                if (s.won) CountBeatSame(result);
            }

            ChallengeTracker.EndMatch(result, s.won, s.baseCoins + s.vipCoins);

            Progression.AddXp(s.baseXp + s.vipXp);
            s.newXp = P.xp;
            s.newLevel = P.level;
            ProfileService.Save();
            AchievementCatalog.AnnounceNew(achBefore);
            if (Online.Service.Available) Online.Service.SubmitStats(P);
        }

        /// <summary>Win_Grenade / Win_Bazooka / Win_Pistol / Win_Shotgun / Win_Impact_Cannon: won using only that weapon(s).</summary>
        static void CountWinWeapons()
        {
            var fired = ChallengeTracker.Stats.weaponsFired;
            if (fired.Count == 0) return;
            bool allGrenades = true;
            foreach (var w in fired) if (!ItemCatalog.HasCategory(GameData.Item(w), "Grenades")) { allGrenades = false; break; }
            var P = ProfileService.P;
            if (allGrenades) P.AddCounter("Win_Grenade", 1);
            if (fired.Count == 1)
            {
                foreach (var w in fired)
                {
                    if (w == "BasicNuke") P.AddCounter("Win_Bazooka", 1);
                    else if (w == "Pistol") P.AddCounter("Win_Pistol", 1);
                    else if (w == "Shotgun") P.AddCounter("Win_Shotgun", 1);
                    else if (w == "ImpactCannon") P.AddCounter("Win_Impact_Cannon", 1);
                }
            }
        }

        /// <summary>Beat_Same: most wins against the same opponent name.</summary>
        static void CountBeatSame(BattleResult r)
        {
            var P = ProfileService.P;
            foreach (var pr in r.players)
            {
                if (pr.isLocal || string.IsNullOrEmpty(pr.name)) continue;
                string key = "beat." + pr.name;
                P.AddCounter(key, 1);
                ChallengeTracker.SetMax("Beat_Same", P.Counter(key));
            }
        }
    }
}
