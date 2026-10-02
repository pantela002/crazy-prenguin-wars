using System;
using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Builds BattleConfigs for every offline mode (Tutorial, Practice, Quick Match, Custom) with the original
    /// numbers (Practice section, BattleOptions, Level Min/MaxLevel, Bet), names and equips AI penguins, and
    /// launches battles (paying bets, showing the match loading screen).
    /// </summary>
    public static class BattleFactory
    {
        /// <summary>A rare AI opponent; knocking it out unlocks the "I killed the game master..." achievement (Dchoc_Kill).</summary>
        public const string GameMasterName = "Game Master";

        static readonly string[] FunNames =
        {
            "Sir Waddles", "Captain Flipper", "Tux Norris", "Pingu Khan", "Admiral Fishbreath", "Lil' Blizzard",
            "Iceberg Slim", "Madame Krill", "Sergeant Slush", "Frosty McBoom", "Professor Puffin", "Waddle Dee",
            "Mr. Tuxedo", "Chilly Willy Nilly", "Penguzilla", "Count Snowcula", "Sushi Bandit", "Nuke Duke",
            "Baroness Brrr", "Herring Bone", "Floe Rida", "Snowden", "Cool Hand Luke", "Mighty Mackerel",
            "Dr. Feathergood", "Glacier Gus", "The Emperor", "Rocky Hopper", "Pebble Pete", "Major Meltdown"
        };

        static PlayerProfile P => ProfileService.P;

        // ---------- levels ----------
        /// <summary>Playable Level rows (id + LevelFile), sorted by theme then difficulty.</summary>
        public static List<Record> Levels()
        {
            var res = new List<Record>(GameData.Section("Level").Values);
            res.RemoveAll(r => string.IsNullOrEmpty(r.Str("LevelFile")));
            res.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return res;
        }

        /// <summary>A random level allowed for this player level (Level.MinLevel..MaxLevel), like PlayNow.</summary>
        public static string RandomLevelFor(int level)
        {
            var ok = new List<string>();
            foreach (var r in Levels())
                if (level >= r.Int("MinLevel", 1) && level <= r.Int("MaxLevel", 99)) ok.Add(r.Id);
            if (ok.Count == 0) foreach (var r in Levels()) ok.Add(r.Id);
            return ok.Count == 0 ? "" : ok[UnityEngine.Random.Range(0, ok.Count)];
        }

        public static string LevelTheme(string levelId)
        {
            var s = (levelId ?? "").ToLowerInvariant();
            if (s.Contains("winter")) return "Winter";
            if (s.Contains("desert")) return "Desert";
            if (s.Contains("mountain")) return "Mountain";
            return "Forest";
        }

        public static string LevelDisplayName(string levelId)
        {
            if (string.IsNullOrEmpty(levelId)) return "Random map";
            var parts = levelId.Split('_');
            if (parts.Length >= 3) return LevelTheme(levelId) + " " + Loc.Prettify(parts[1]) + " " + parts[2].TrimStart('0');
            return Loc.Prettify(levelId);
        }

        // ---------- players ----------
        public static PlayerSlot LocalSlot()
        {
            var s = new PlayerSlot
            {
                name = string.IsNullOrEmpty(P.displayName) ? "Penguin" : P.displayName,
                isAI = false, isLocalHuman = true, usesProfileInventory = true,
                head = P.wornHead, chest = P.wornChest, feet = P.wornFeet, trophy = P.wornTrophy,
                level = P.level, colorIndex = 0
            };
            return s;
        }

        /// <summary>Opponent names: AIPlayer rows with a real name, otherwise fun penguin names.</summary>
        public static List<string> OpponentNames(int count, int playerLevel)
        {
            var pool = new List<string>();
            foreach (var r in GameData.Section("AIPlayer").Values)
            {
                if (playerLevel < r.Int("MinLevel", 1) || playerLevel > r.Int("MaxLevel", 99)) continue;
                var n = Loc.T(r.Str("Name", ""));
                if (!string.IsNullOrEmpty(n) && !n.StartsWith("Bot ")) pool.Add(n);   // skip placeholder "Bot 1" rows
            }
            pool.AddRange(FunNames);
            var res = new List<string>();
            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                int k = UnityEngine.Random.Range(0, pool.Count);
                res.Add(pool[k]);
                pool.RemoveAt(k);
            }
            // 1 in 25 quick matches has the game master in it
            if (count > 0 && UnityEngine.Random.value < 0.04f) res[res.Count - 1] = GameMasterName;
            return res;
        }

        /// <summary>An AI penguin of the given level and skill with a loadout and clothes that fit its level.</summary>
        public static PlayerSlot AiSlot(string name, int level, int skill, int colorIndex)
        {
            var s = new PlayerSlot
            {
                name = name, isAI = true, isLocalHuman = false, aiSkill = Mathf.Clamp(skill, 0, 2),
                level = Mathf.Max(1, level), colorIndex = colorIndex, usesProfileInventory = false
            };
            s.loadout = AiLoadout(s.level, skill);
            DressAi(s);
            if (s.level >= 5 && UnityEngine.Random.value < 0.25f + 0.15f * skill)
            {
                var boosters = ItemCatalog.ShopItems("Booster");
                if (boosters.Count > 0) s.boosters.Add(boosters[UnityEngine.Random.Range(0, boosters.Count)].Id);
            }
            return s;
        }

        /// <summary>Basic weapons plus random shop weapons unlocked at that level (no VIP ones).</summary>
        public static List<ItemStack> AiLoadout(int level, int skill)
        {
            var list = new List<ItemStack> { new ItemStack("Punch", 1), new ItemStack("BasicNuke", 6 + skill * 2), new ItemStack("Pistol", 5) };
            if (level >= 2) list.Add(new ItemStack("Grenade", 4));
            var pool = new List<Record>();
            foreach (var r in ItemCatalog.ShopItems("Weapon"))
                if (ItemCatalog.RequiredLevel(r) <= level && !ItemCatalog.IsVipItem(r) && r.Id != "BasicNuke" && r.Id != "Pistol" && r.Id != "Grenade") pool.Add(r);
            int extra = Mathf.Clamp(1 + level / 6 + skill, 1, 6);
            for (int i = 0; i < extra && pool.Count > 0; i++)
            {
                int k = UnityEngine.Random.Range(0, pool.Count);
                list.Add(new ItemStack(pool[k].Id, UnityEngine.Random.Range(2, 5) + skill));
                pool.RemoveAt(k);
            }
            return list;
        }

        static void DressAi(PlayerSlot s)
        {
            if (UnityEngine.Random.value < 0.25f) return;  // some penguins go au naturel
            foreach (var slot in new[] { ClothesSlot.Head, ClothesSlot.Chest, ClothesSlot.Feet })
            {
                var pool = ClothesCatalog.BySlot(slot);
                pool.RemoveAll(d => d.level > s.level || d.vipOnly);
                if (pool.Count == 0 || UnityEngine.Random.value < 0.3f) continue;
                var id = pool[UnityEngine.Random.Range(0, pool.Count)].id;
                if (slot == ClothesSlot.Head) s.head = id; else if (slot == ClothesSlot.Chest) s.chest = id; else s.feet = id;
            }
        }

        /// <summary>Generous equal loadout for Custom/pass-and-play (everything unlocked at max(level, 10)).</summary>
        public static List<ItemStack> CustomLoadout()
        {
            int lv = Mathf.Max(P.level, 10);
            var list = new List<ItemStack> { new ItemStack("Punch", 1) };
            foreach (var r in ItemCatalog.ShopItems("Weapon"))
                if (ItemCatalog.RequiredLevel(r) <= lv && !ItemCatalog.IsVipItem(r)) list.Add(new ItemStack(r.Id, r.Id == "BasicNuke" ? 20 : 5));
            return list;
        }

        /// <summary>
        /// Practice items: every weapon and booster in the game with plenty of ammo, nothing is spent.
        /// (The original only offered its "Practice" category; the remake makes Practice the place to try every gun.)
        /// </summary>
        public static List<ItemStack> PracticeLoadout()
        {
            var list = new List<ItemStack>();
            foreach (var r in GameData.Section("Item").Values)
                if (ItemCatalog.IsWeapon(r) || ItemCatalog.IsBooster(r)) list.Add(new ItemStack(r.Id, 99));
            return list;
        }

        public static List<ItemStack> TutorialLoadout()
        {
            var list = new List<ItemStack>();
            foreach (var r in GameData.Section("Item").Values)
                if (ItemCatalog.HasCategory(r, "Tutorial1") || r.Id == "Punch") list.Add(new ItemStack(r.Id, 99));
            if (list.Count == 0) { list.Add(new ItemStack("BasicNuke", 99)); list.Add(new ItemStack("Pistol", 99)); }
            return list;
        }

        // ---------- configs ----------
        static Record Practice => GameData.Get("Practice", "Default");
        static Record Options => GameData.Battle;

        public static BattleConfig Tutorial()
        {
            var c = new BattleConfig { mode = BattleMode.Tutorial, seed = Environment.TickCount, powerUps = true, betId = "1NoBet" };
            var tl = GameData.Get("PracticeLevel", "tutorial_level");
            c.levelId = tl != null ? tl.Str("LevelFile", "tutorial_level") : "tutorial_level";
            c.matchTime = Practice?.Float("MatchDuration", 300) ?? 300;
            c.turnTime = 30;
            c.winningScore = Options?.Int("WinningScore", 200) ?? 200;
            var me = LocalSlot();
            me.usesProfileInventory = false;
            me.loadout = TutorialLoadout();
            c.players.Add(me);
            var ai = AiSlot(Loc.T("TUTORIAL_DEFAULT_AI_NAME"), 1, 0, 1);
            ai.loadout = new List<ItemStack> { new ItemStack("BasicNuke", 99), new ItemStack("Pistol", 99) };
            ai.boosters.Clear();
            c.players.Add(ai);
            return c;
        }

        /// <summary>Practice: Practice.OpponentAmount AIs, Practice.TurnDuration / MatchDuration, free ammo.</summary>
        public static BattleConfig PracticeMatch(string levelId = null)
        {
            var c = new BattleConfig { mode = BattleMode.Practice, seed = Environment.TickCount, powerUps = true, betId = "1NoBet" };
            c.levelId = string.IsNullOrEmpty(levelId) ? RandomLevelFor(P.level) : levelId;
            c.turnTime = Practice?.Float("TurnDuration", 20) ?? 20;
            c.matchTime = Practice?.Float("MatchDuration", 300) ?? 300;
            c.winningScore = Options?.Int("WinningScore", 200) ?? 200;
            var me = LocalSlot();
            me.usesProfileInventory = false;
            me.loadout = PracticeLoadout();
            c.players.Add(me);
            int n = Mathf.Clamp(Practice?.Int("OpponentAmount", 3) ?? 3, 1, 3);
            var names = OpponentNames(n, P.level);
            for (int i = 0; i < n; i++)
            {
                var ai = AiSlot(names[i], Mathf.Max(1, P.level - 1), 0, i + 1);
                ai.loadout = PracticeLoadout();
                c.players.Add(ai);
            }
            return c;
        }

        /// <summary>Quick match vs AI ("Play Now"): opponents near your level, original match/turn time and rewards.</summary>
        public static BattleConfig QuickMatch(int opponents, int skill, string betId)
        {
            var c = new BattleConfig { mode = BattleMode.QuickMatch, seed = Environment.TickCount, powerUps = true };
            c.levelId = RandomLevelFor(P.level);
            c.matchTime = Options?.Float("MatchTime", 240) ?? 240;
            c.turnTime = Mathf.Max(Options?.Float("TurnTime", 10) ?? 10, 15);
            c.winningScore = Options?.Int("WinningScore", 200) ?? 200;
            c.betId = string.IsNullOrEmpty(betId) ? "1NoBet" : betId;
            c.players.Add(LocalSlot());
            opponents = Mathf.Clamp(opponents, 1, 3);
            var names = OpponentNames(opponents, P.level);
            for (int i = 0; i < opponents; i++)
            {
                int lv = Mathf.Clamp(P.level + UnityEngine.Random.Range(-2, 3), 1, GameData.MaxLevel);
                c.players.Add(AiSlot(names[i], lv, skill, i + 1));
            }
            return c;
        }

        // ---------- bets ----------
        public static int BetCoins(string betId) => GameData.Get("Bet", betId)?.Int("ValueIngame") ?? 0;
        public static int BetCash(string betId) => GameData.Get("Bet", betId)?.Int("ValuePremium") ?? 0;

        public static string BetLabel(string betId)
        {
            int c = BetCoins(betId), f = BetCash(betId);
            if (c > 0) return c + " coins";
            if (f > 0) return f + " fish";
            return Loc.T("NOBET");
        }

        // ---------- launching ----------
        /// <summary>Last launched config (for Rematch).</summary>
        public static BattleConfig Last { get; private set; }

        /// <summary>Pay the bet (if any), remember the config and start the match loading screen.</summary>
        public static void Launch(BattleConfig c)
        {
            if (c == null) return;
            int bc = BetCoins(c.betId), bf = BetCash(c.betId);
            if (bc > 0 || bf > 0)
            {
                if (!Progression.Spend(bc, bf)) { Progression.NotEnough(bf > 0); return; }
                ProfileService.Save();
            }
            if (c.seed == 0) c.seed = Environment.TickCount;
            Last = Clone(c);
            ScreenManager.Show(() => new MatchLoadingScreen(c), false);
        }

        /// <summary>Same players and settings, new seed (and a new random map when none was picked).</summary>
        public static BattleConfig Rematch(BattleConfig c)
        {
            var n = Clone(c ?? Last);
            if (n == null) return null;
            n.seed = Environment.TickCount;
            // the local player's clothes may have changed
            foreach (var s in n.players)
                if (s.usesProfileInventory) { s.head = P.wornHead; s.chest = P.wornChest; s.feet = P.wornFeet; s.trophy = P.wornTrophy; s.level = P.level; }
            return n;
        }

        public static BattleConfig Clone(BattleConfig c)
        {
            if (c == null) return null;
            var n = JsonUtility.FromJson<BattleConfig>(JsonUtility.ToJson(c));
            n.network = null;
            return n;
        }
    }
}
