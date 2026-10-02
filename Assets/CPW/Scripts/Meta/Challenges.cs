using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>One challenge in a chain; completing it awards its trophy and unlocks the next one.</summary>
    public class ChallengeDef
    {
        public string id, name, description;
        public int chain, indexInChain;
        public string metric;       // see ChallengeTracker.Eval
        public bool perMatch;       // true: best single match counts; false: accumulates over matches
        public int target;
        public int coins, xp, cash;
        public string trophy;       // Bonus id of the trophy awarded
        public string Text => description.Replace("{0}", target.ToString("N0"));
    }

    /// <summary>
    /// Challenges like the original tuxwars/challenges system: four chains (types Battle, Grind, Skill and
    /// Impossible) that start at Tuner.FirstChallenges (Splat, HoorayMoney, CarefulCalculations, Turnabout); each
    /// completed challenge activates the next in its chain (NextChallengeIds) and awards a trophy, which is what
    /// TrophyManager did with TrophyDef.RequiredChallenges.
    ///
    /// INVENTED DATA: the Challenge rows were not in the shipped config. The 32 challenges below (one per trophy
    /// Bonus row) use the original counter ideas (HitsCounter, KillsByDamageIdsCounter, MatchesWonRowCounter,
    /// UseBoosterCounter, CraftedCounter, ReachLevelCounter...). Progress is saved in counters "chp.{id}",
    /// completion in "chd.{id}".
    /// </summary>
    public static class ChallengeCatalog
    {
        public static readonly string[] ChainNames = { "Battle", "Grind", "Skill", "Impossible" };
        public static readonly Color[] ChainColors =
        {
            new Color32(232, 76, 61, 255), new Color32(255, 170, 30, 255), new Color32(64, 152, 236, 255), new Color32(150, 92, 220, 255)
        };

        static List<ChallengeDef> all;

        public static List<ChallengeDef> All
        {
            get
            {
                if (all != null) return all;
                all = new List<ChallengeDef>();
                var first = GameData.Tuner?.List("FirstChallenges") ?? new List<string>();
                string F(int i, string def) => i < first.Count ? first[i] : def;

                Chain(0,
                    C(F(0, "Splat"), "Splat!", "Hit opponents {0} times", "hits", false, 5, "BandaidBadge"),
                    C("FirstBlood", "First Blood", "Knock out {0} penguins", "kills", false, 3, "MedalofPain"),
                    C("Overkill", "Overkill", "Deal {0} damage in a single match", "damage", true, 300, "OverkillTrophy"),
                    C("Terraformer", "Terraformer", "Cause {0} explosions", "explosions", false, 60, "TerraformerCertificate"),
                    C("Underdog", "Underdog", "Win a match against 3 opponents", "winVs3", false, 1, "UnderdogBadge"),
                    C("RocketScience", "Rocket Science", "Knock out {0} penguins with rockets", "killsRockets", false, 10, "ExplosivesExpertMedal"),
                    C("Arsenal", "Arsenal", "Fire {0} different weapons", "distinctWeapons", true, 10, "WeaponsExpertMedal"),
                    C("Warlord", "Warlord", "Win {0} matches", "wins", false, 25, "TrophyofWar"));
                Chain(1,
                    C(F(1, "HoorayMoney"), "Hooray Money!", "Earn {0} coins in matches", "coinsEarned", false, 500, "TrophyofWealth"),
                    C("SnackTime", "Snack Time", "Use {0} boosters", "boostersUsed", false, 5, "SnackTrophy"),
                    C("Tinkerer", "Tinkerer", "Craft {0} item in the lab", "crafted", false, 1, "Pinofcrafting"),
                    C("ShopTillYouDrop", "Shop Till You Drop", "Buy {0} things in the shop", "shopBuys", false, 10, "EliteTrophy"),
                    C("Veteran", "Veteran", "Play {0} matches", "matches", false, 30, "MedalofVeteran"),
                    C("Seasoned", "Seasoned", "Reach level {0}", "level", true, 10, "TrophyofVeteran"),
                    C("Expert", "Expert", "Reach level {0}", "level", true, 20, "RibbonofExpertise"),
                    C("Perseverance", "Perseverance", "Play {0} matches", "matches", false, 100, "TrophyofPerseverance"));
                Chain(2,
                    C(F(2, "CarefulCalculations"), "Careful Calculations", "Deal {0} damage with a single hit", "maxShotDamage", true, 60, "EagleEyeBadge"),
                    C("Grenadier", "Grenadier", "Knock out {0} penguins with grenades", "killsGrenades", false, 5, "GrenadierMedal"),
                    C("Sharpshooter", "Sharpshooter", "Knock out {0} penguins with guns", "killsGuns", false, 5, "SharpshooterTrophy"),
                    C("FlameOn", "Flame On", "Hit opponents {0} times with fire weapons", "fireHits", false, 10, "FlameBadge"),
                    C("TrapMaster", "Trap Master", "Use {0} mines or caltrops", "trapBoosters", false, 5, "TrapMasterTrophy"),
                    C("Marine", "Man Overboard", "Knock {0} opponents into the water", "waterKnockouts", false, 3, "MarineCertificate"),
                    C("RocketMan", "Rocket Man", "Fire {0} rockets", "rocketsFired", false, 50, "PilotsLicense"),
                    C("Creative", "Creative Destruction", "Fire {0} special weapons", "specialFired", false, 20, "CreativityMedal"));
                Chain(3,
                    C(F(3, "Turnabout"), "Turnabout", "Win a match after being knocked out", "winAfterDeath", false, 1, "PurpleHeart"),
                    C("HatTrick", "Hat Trick", "Knock out {0} penguins in a row", "killStreak", true, 3, "MarkofAssassin"),
                    C("Telekinesis", "Telekinesis", "Knock out a penguin with the Wind Wand", "killsWandWind", false, 1, "TelekinesisMedal"),
                    C("Indomitable", "Indomitable", "Win a match without being knocked out", "winNoDeath", false, 1, "IndomitableMedal"),
                    C("Efficiency", "Efficiency", "Win a match firing 5 shots or fewer", "winFewShots", false, 1, "EfficiencyTrophy"),
                    C("Insanity", "Insanity", "Beat 3 hard AI penguins in one match", "winVsHard3", false, 1, "InsanityMedal"),
                    C("ThreadsOfFate", "Threads of Fate", "Win {0} matches in a row", "winStreak", true, 5, "ThreadsofFateMedal"),
                    C("Master", "The Master", "Win {0} matches", "wins", false, 100, "TrophyoftheMaster"));
                return all;
            }
        }

        static ChallengeDef C(string id, string name, string desc, string metric, bool perMatch, int target, string trophy)
            => new ChallengeDef { id = id, name = name, description = desc, metric = metric, perMatch = perMatch, target = target, trophy = trophy };

        static void Chain(int chain, params ChallengeDef[] defs)
        {
            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                d.chain = chain; d.indexInChain = i;
                d.coins = 100 + 100 * i + 50 * chain;
                d.xp = 50 + 50 * i;
                d.cash = i >= 4 ? 1 + (i - 3) : 0;
                all.Add(d);
            }
        }

        public static int Progress(ChallengeDef d) => ProfileService.P.Counter("chp." + d.id);
        public static bool Completed(ChallengeDef d) => ProfileService.P.Counter("chd." + d.id) > 0;

        /// <summary>The active (first incomplete) challenge of a chain, or null when the chain is finished.</summary>
        public static ChallengeDef Active(int chain)
        {
            foreach (var d in All) if (d.chain == chain && !Completed(d)) return d;
            return null;
        }

        public static int CompletedCount() { int n = 0; foreach (var d in All) if (Completed(d)) n++; return n; }
    }

    /// <summary>Original Achievements section (95 rows): counter thresholds with a GCReward (coins) to claim.</summary>
    public static class AchievementCatalog
    {
        static List<Record> list;

        public static List<Record> All
        {
            get
            {
                if (list != null) return list;
                list = new List<Record>(GameData.Section("Achievements").Values);
                list.RemoveAll(r => !r.Has("Strategy") || r.Str("Strategy") == "String");
                list.Sort((a, b) =>
                {
                    int sa = CounterOf(a)?.Int("SortOrder", 99) ?? 99, sb = CounterOf(b)?.Int("SortOrder", 99) ?? 99;
                    if (sa != sb) return sa.CompareTo(sb);
                    if (a.Str("Strategy") != b.Str("Strategy")) return string.CompareOrdinal(a.Str("Strategy"), b.Str("Strategy"));
                    return a.Int("Number").CompareTo(b.Int("Number"));
                });
                return list;
            }
        }

        public static Record CounterOf(Record a) => a.Ref("Strategy");
        public static string CounterId(Record a) => GameData.RefId(a.Str("Strategy"));
        public static int Progress(Record a) => ProfileService.P.Counter(CounterId(a));
        public static int Target(Record a) => Mathf.Max(1, a.Int("Number", 1));
        public static bool Done(Record a) => Progress(a) >= Target(a);
        public static bool Claimed(Record a) => ProfileService.P.claimedAchievements.Contains(a.Id);
        public static string Title(Record a) => Loc.T(a.Str("Name", a.Id));
        public static string Description(Record a) => MetaUI.Fmt(a.Str("Description", ""), Target(a));

        public static int Claimable()
        {
            int n = 0;
            foreach (var a in All) if (Done(a) && !Claimed(a)) n++;
            return n;
        }

        public static void Claim(Record a)
        {
            if (!Done(a) || Claimed(a)) return;
            ProfileService.P.claimedAchievements.Add(a.Id);
            Progression.AddCoins(a.Int("GCReward"));
            ProfileService.Save();
            AudioManager.Sfx("AchievementClaim");
        }

        /// <summary>Snapshot of which achievements are done, to detect new unlocks after counters change.</summary>
        public static HashSet<string> DoneSet()
        {
            var s = new HashSet<string>();
            foreach (var a in All) if (Done(a)) s.Add(a.Id);
            return s;
        }

        /// <summary>Toast every achievement completed since the snapshot.</summary>
        public static void AnnounceNew(HashSet<string> before)
        {
            foreach (var a in All)
                if (Done(a) && !before.Contains(a.Id))
                {
                    UI.Toast(Loc.T("ACHIEVEMENTS_ACHIEVEMENT_UNLOCKED") + ": " + Title(a), MetaUI.Gold);
                    AudioManager.Sfx("AchievementUnlocked");
                }
        }
    }

    /// <summary>What the local player did in the current match (filled from BattleEvents).</summary>
    public class MatchStats
    {
        public int hits, kills, deaths, explosions, shots, rocketsFired, specialFired, boostersUsed, trapBoosters;
        public int fireHits, waterKnockouts, killsRockets, killsGrenades, killsGuns, killsWandWind;
        public int streak, bestStreak;
        public float damage, maxShotDamage;
        public readonly HashSet<string> weaponsFired = new HashSet<string>();
        // filled at the end
        public bool won;
        public int opponents, hardAis, coinsEarned;
        public void Clear()
        {
            hits = kills = deaths = explosions = shots = rocketsFired = specialFired = boostersUsed = trapBoosters = 0;
            fireHits = waterKnockouts = killsRockets = killsGrenades = killsGuns = killsWandWind = 0;
            streak = bestStreak = 0; damage = maxShotDamage = 0;
            weaponsFired.Clear(); won = false; opponents = hardAis = coinsEarned = 0;
        }
    }

    /// <summary>
    /// Listens to BattleEvents for the local player, updates the original Counters (Hits_Bazooka, Use_Ammo,
    /// Suicide, Make_X_Damage_Once...) and challenge progress. RewardService calls EndMatch after a battle.
    /// Non-battle actions (shop, crafting, levels) call Report().
    /// </summary>
    public static class ChallengeTracker
    {
        public static readonly MatchStats Stats = new MatchStats();
        static BattleConfig config;
        static int me = -1;
        static bool tracking, installed;

        static readonly HashSet<string> FireWeapons = new HashSet<string> { "Molotov", "Napalm", "FlareGun", "CinderGrenade", "FlameMine", "FuelAirBomb", "Fireworks" };
        static readonly HashSet<string> TrapBoosters = new HashSet<string> { "Mine", "FlameMine", "Caltrops" };
        static readonly Dictionary<string, string> HitCounters = new Dictionary<string, string>
        {
            { "BasicNuke", "Hits_Bazooka" }, { "ClusterRocket", "Hits_Cluster_Rocket" }, { "Dynamite", "Hits_Dynamite" },
            { "Molotov", "Hits_Molotov" }, { "MiniBazooka", "Hits_Mini_Bazooka" }, { "MegaNuke", "Hits_Nuke" }
        };

        public static void Install()
        {
            if (installed) return;
            installed = true;
            BattleEvents.BattleStarted += OnStarted;
            BattleEvents.WeaponFired += OnFired;
            BattleEvents.PenguinDamaged += OnDamaged;
            BattleEvents.PenguinKilled += OnKilled;
            BattleEvents.BoosterUsed += OnBooster;
            BattleEvents.Explosion += OnExplosion;
            DynamicObjectEntity.Destroyed += OnObjectDestroyed;
        }

        /// <summary>Original Destroy_Ice / Destroy_Wood / Destroy_Stone counters for smashing level objects.</summary>
        static void OnObjectDestroyed(DynamicObjectEntity obj, int attacker, string item)
        {
            if (!tracking || attacker != me || obj == null) return;
            switch (obj.Material)
            {
                case "Ice": ProfileService.P.AddCounter("Destroy_Ice", 1); break;
                case "Wood": ProfileService.P.AddCounter("Destroy_Wood", 1); break;
                case "Stone": ProfileService.P.AddCounter("Destroy_Stone", 1); break;
            }
        }

        static void OnStarted(BattleConfig c)
        {
            config = c;
            Stats.Clear();
            me = c != null ? c.LocalPlayerIndex : -1;
            int humans = 0;
            if (c != null) foreach (var p in c.players) if (!p.isAI) humans++;
            // pass-and-play between several humans on one device doesn't count (too easy to farm)
            tracking = c != null && !(c.mode == BattleMode.Custom && humans > 1);
        }

        static void OnFired(int player, string item)
        {
            if (!tracking || player != me) return;
            Stats.shots++;
            Stats.weaponsFired.Add(item);
            var r = GameData.Item(item);
            if (ItemCatalog.HasCategory(r, "Rockets")) Stats.rocketsFired++;
            if (ItemCatalog.HasCategory(r, "Special")) Stats.specialFired++;
            if (r != null && !ItemCatalog.IsInfinite(r)) ProfileService.P.AddCounter("Use_Ammo", 1);
            SetMax("wpn." + item, 1);
        }

        static void OnDamaged(int victim, int attacker, float amount, string item)
        {
            if (!tracking) return;
            if (attacker == me && victim != me)
            {
                Stats.hits++;
                Stats.damage += amount;
                if (amount > Stats.maxShotDamage) Stats.maxShotDamage = amount;
                if (item != null && FireWeapons.Contains(item)) Stats.fireHits++;
                if (item != null && HitCounters.TryGetValue(item, out var counter)) ProfileService.P.AddCounter(counter, 1);
                SetMax("Make_X_Damage_Once", Mathf.RoundToInt(amount));
            }
            else if (victim == me && attacker == me && item != null && FireWeapons.Contains(item))
                SetMax("Fire_Damage_Self", 1);
        }

        static void OnKilled(int victim, int killer, string item)
        {
            if (!tracking) return;
            if (victim == me)
            {
                Stats.deaths++;
                Stats.streak = 0;
                if (killer == me) SetMax("Suicide", 1);
                return;
            }
            if (killer == me)
            {
                Stats.kills++;
                Stats.streak++;
                if (Stats.streak > Stats.bestStreak) Stats.bestStreak = Stats.streak;
                if (Stats.streak >= 3) SetMax("Kills_Match", 1);
                var r = GameData.Item(item);
                if (ItemCatalog.HasCategory(r, "Rockets")) Stats.killsRockets++;
                if (ItemCatalog.HasCategory(r, "Grenades")) Stats.killsGrenades++;
                if (ItemCatalog.HasCategory(r, "Guns")) Stats.killsGuns++;
                if (item == "WandWind") Stats.killsWandWind++;
                // the "game master" AI penguin (see BattleFactory) counts for the Dchoc_Kill achievement
                if (config != null && victim >= 0 && victim < config.players.Count && config.players[victim].name == BattleFactory.GameMasterName)
                    SetMax("Dchoc_Kill", 1);
            }
            else if (killer < 0 && BattleWorld.ActivePlayer == me) Stats.waterKnockouts++;
        }

        static void OnBooster(int player, string item)
        {
            if (!tracking || player != me) return;
            Stats.boostersUsed++;
            if (TrapBoosters.Contains(item)) Stats.trapBoosters++;
        }

        static void OnExplosion(Vector2 pos, float radius)
        {
            if (tracking && BattleWorld.ActivePlayer == me) Stats.explosions++;
        }

        public static void SetMax(string counter, int value)
        {
            int cur = ProfileService.P.Counter(counter);
            if (value > cur) ProfileService.P.AddCounter(counter, value - cur);
        }

        /// <summary>Evaluate a metric for the match just played.</summary>
        static int Eval(string metric, MatchStats s)
        {
            var P = ProfileService.P;
            switch (metric)
            {
                case "hits": return s.hits;
                case "kills": return s.kills;
                case "damage": return Mathf.RoundToInt(s.damage);
                case "explosions": return s.explosions;
                case "winVs3": return s.won && s.opponents >= 3 ? 1 : 0;
                case "killsRockets": return s.killsRockets;
                case "killsGrenades": return s.killsGrenades;
                case "killsGuns": return s.killsGuns;
                case "killsWandWind": return s.killsWandWind;
                case "wins": return s.won ? 1 : 0;
                case "matches": return 1;
                case "coinsEarned": return s.coinsEarned;
                case "boostersUsed": return s.boostersUsed;
                case "trapBoosters": return s.trapBoosters;
                case "maxShotDamage": return Mathf.RoundToInt(s.maxShotDamage);
                case "fireHits": return s.fireHits;
                case "waterKnockouts": return s.waterKnockouts;
                case "rocketsFired": return s.rocketsFired;
                case "specialFired": return s.specialFired;
                case "winAfterDeath": return s.won && s.deaths > 0 ? 1 : 0;
                case "killStreak": return s.bestStreak;
                case "winNoDeath": return s.won && s.deaths == 0 ? 1 : 0;
                case "winFewShots": return s.won && s.shots <= 5 ? 1 : 0;
                case "winVsHard3": return s.won && s.hardAis >= 3 ? 1 : 0;
                case "winStreak": return P.Counter("stat.winStreak");
                case "level": return P.level;
                case "distinctWeapons": return CountPrefix("wpn.");
            }
            return 0;
        }

        static int CountPrefix(string prefix)
        {
            int n = 0;
            foreach (var c in ProfileService.P.counters) if (c.value > 0 && c.id.StartsWith(prefix)) n++;
            return n;
        }

        /// <summary>Called by RewardService once the battle's rewards are known.</summary>
        public static void EndMatch(BattleResult result, bool won, int coinsEarned)
        {
            if (!tracking || result == null) return;
            Stats.won = won;
            Stats.coinsEarned = coinsEarned;
            Stats.opponents = Mathf.Max(0, result.config.players.Count - 1);
            Stats.hardAis = 0;
            foreach (var p in result.config.players) if (p.isAI && p.aiSkill >= 2) Stats.hardAis++;
            if (won) ProfileService.P.AddCounter("stat.winStreak", 1);
            else ProfileService.P.AddCounter("stat.winStreak", -ProfileService.P.Counter("stat.winStreak"));
            for (int c = 0; c < ChallengeCatalog.ChainNames.Length; c++) Advance(c, Stats, null, 0);
            tracking = false;
        }

        /// <summary>Report progress made outside battles (metric: shopBuys, crafted, level, fishSpent...).</summary>
        public static void Report(string metric, int amount)
        {
            for (int c = 0; c < ChallengeCatalog.ChainNames.Length; c++) Advance(c, null, metric, amount);
        }

        /// <summary>Update the active challenge of a chain; completes it (and following ones) when reached.</summary>
        static void Advance(int chain, MatchStats s, string metric, int amount)
        {
            var P = ProfileService.P;
            for (int guard = 0; guard < 10; guard++)
            {
                var d = ChallengeCatalog.Active(chain);
                if (d == null) return;
                int value;
                if (s != null) value = Eval(d.metric, s);
                else if (d.metric == "level" || d.metric == "distinctWeapons") value = Eval(d.metric, Stats);   // state, not a delta
                else if (d.metric == metric) value = amount;
                else return;

                int cur = ChallengeCatalog.Progress(d);
                int next = d.perMatch || d.metric == "level" || d.metric == "winStreak" || d.metric == "distinctWeapons"
                    ? Mathf.Max(cur, value) : cur + value;
                if (next != cur) P.AddCounter("chp." + d.id, next - cur);
                if (next < d.target) return;
                Complete(d);
                // the next challenge in the chain starts counting from now (no carry-over), like the original
                s = null; metric = "__none";
            }
        }

        static void Complete(ChallengeDef d)
        {
            var P = ProfileService.P;
            P.AddCounter("chd." + d.id, 1);
            Progression.AddCoins(d.coins);
            Progression.AddCash(d.cash);
            Progression.AddXp(d.xp);
            Progression.GiveClothes(d.trophy);
            UI.Toast("Challenge complete: " + d.name + "!  Trophy: " + ClothesCatalog.DisplayName(d.trophy), MetaUI.Gold);
            AudioManager.Sfx("AchievementUnlocked");
        }
    }
}
