using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    public enum ClothesSlot { Head, Chest, Feet, Trophy }

    /// <summary>One wearable: a Bonus row (stats) plus shop info.</summary>
    public class ClothesDef
    {
        public string id, setId, setName;
        public ClothesSlot slot;
        public int level = 1, coins, cash;
        public bool vipOnly;
        public Record Bonus => GameData.Get("Bonus", id);
    }

    /// <summary>
    /// Clothes and trophies. The original config only kept the Bonus rows (stat mods) for the 22 outfit sets
    /// (*_head/_chest/_feet), the singles (RedSweater, Skates, RedHat, army_*) and the 32 trophies; their shop
    /// prices/levels were in data that did not survive. INVENTED TABLE (documented here):
    ///
    ///   Set            Lv   Coins/piece   |  Set            Lv   Coins/piece
    ///   flannel         1     300         |  welder         18    2900
    ///   paper           2     400         |  tuxedo         20    3500
    ///   rain            3     500         |  bunny          22    4000
    ///   clown           4     600         |  desert         24    4600
    ///   sm              6     800         |  elvis          26    5200
    ///   polar           8    1000         |  specialforce   28    5800
    ///   hockey         10    1300         |  football       32    7000
    ///   cowboy         12    1600         |  space          36    9000
    ///   schoolgirl     14    2000         |  king           30    25 fish, VIP only
    ///   wizard         16    2400         |  dark_assassin  34    30 fish
    ///                                     |  gladiator      40    45 fish
    ///   Singles: RedSweater/Skates/RedHat lv1 150 coins; army_* lv 42-50, 35-60 fish each.
    ///
    /// The order follows the summed Attack+Defence+Luck of each set so stronger gear costs more and unlocks later.
    /// Like weapons, a level-locked piece can be unlocked early for fish (UnlockCash = 2 + level/2).
    /// Trophies are never sold: ChallengeCatalog awards them for completed challenges.
    /// </summary>
    public static class ClothesCatalog
    {
        static List<ClothesDef> all;
        static Dictionary<string, ClothesDef> byId;

        static readonly string[] Trophies =
        {
            "BandaidBadge", "MedalofPain", "OverkillTrophy", "TerraformerCertificate", "UnderdogBadge", "ExplosivesExpertMedal",
            "WeaponsExpertMedal", "EfficiencyTrophy", "TrophyofWar", "FlameBadge", "EagleEyeBadge", "MarineCertificate",
            "TrapMasterTrophy", "GrenadierMedal", "CreativityMedal", "PilotsLicense", "SharpshooterTrophy", "EliteTrophy",
            "TrophyofWealth", "TrophyofVeteran", "RibbonofExpertise", "MarkofAssassin", "MedalofVeteran", "PurpleHeart",
            "SnackTrophy", "Pinofcrafting", "TrophyofPerseverance", "TelekinesisMedal", "IndomitableMedal", "InsanityMedal",
            "ThreadsofFateMedal", "TrophyoftheMaster"
        };

        // set id, display name, level, coins per piece, cash per piece, vip
        static readonly object[][] Sets =
        {
            new object[] { "flannel", "Lumberjack", 1, 300, 0, false },
            new object[] { "paper", "Paper", 2, 400, 0, false },
            new object[] { "rain", "Rainy Day", 3, 500, 0, false },
            new object[] { "clown", "Clown", 4, 600, 0, false },
            new object[] { "sm", "Secret Mission", 6, 800, 0, false },
            new object[] { "polar", "Polar", 8, 1000, 0, false },
            new object[] { "hockey", "Hockey", 10, 1300, 0, false },
            new object[] { "cowboy", "Cowboy", 12, 1600, 0, false },
            new object[] { "schoolgirl", "School", 14, 2000, 0, false },
            new object[] { "wizard", "Wizard", 16, 2400, 0, false },
            new object[] { "welder", "Welder", 18, 2900, 0, false },
            new object[] { "tuxedo", "Tuxedo", 20, 3500, 0, false },
            new object[] { "bunny", "Bunny", 22, 4000, 0, false },
            new object[] { "desert", "Desert", 24, 4600, 0, false },
            new object[] { "elvis", "Rock Star", 26, 5200, 0, false },
            new object[] { "specialforce", "Special Forces", 28, 5800, 0, false },
            new object[] { "king", "King", 30, 0, 25, true },
            new object[] { "football", "Football", 32, 7000, 0, false },
            new object[] { "dark_assassin", "Dark Assassin", 34, 0, 30, false },
            new object[] { "space", "Space", 36, 9000, 0, false },
            new object[] { "gladiator", "Gladiator", 40, 0, 45, false },
        };

        // single pieces: id, display name, slot, level, coins, cash
        static readonly object[][] Singles =
        {
            new object[] { "RedHat", "Red Hat", ClothesSlot.Head, 1, 150, 0 },
            new object[] { "RedSweater", "Red Sweater", ClothesSlot.Chest, 1, 150, 0 },
            new object[] { "Skates", "Skates", ClothesSlot.Feet, 1, 150, 0 },
            new object[] { "army_helmet_red", "Red Army Helmet", ClothesSlot.Head, 42, 0, 45 },
            new object[] { "army_jacket_red", "Red Army Jacket", ClothesSlot.Chest, 44, 0, 50 },
            new object[] { "army_boots_red", "Red Army Boots", ClothesSlot.Feet, 42, 0, 35 },
            new object[] { "army_helmet_blue", "Blue Army Helmet", ClothesSlot.Head, 48, 0, 60 },
            new object[] { "army_jacket_blue", "Blue Army Jacket", ClothesSlot.Chest, 46, 0, 50 },
            new object[] { "army_boots_blue", "Blue Army Boots", ClothesSlot.Feet, 50, 0, 40 },
        };

        static void Build()
        {
            if (all != null) return;
            all = new List<ClothesDef>();
            byId = new Dictionary<string, ClothesDef>();
            foreach (var s in Sets)
            {
                string set = (string)s[0];
                foreach (var slot in new[] { ClothesSlot.Head, ClothesSlot.Chest, ClothesSlot.Feet })
                {
                    string id = set + (slot == ClothesSlot.Head ? "_head" : slot == ClothesSlot.Chest ? "_chest" : "_feet");
                    if (GameData.Get("Bonus", id) == null) continue;
                    Add(new ClothesDef { id = id, setId = set, setName = (string)s[1], slot = slot, level = (int)s[2], coins = (int)s[3], cash = (int)s[4], vipOnly = (bool)s[5] });
                }
            }
            foreach (var s in Singles)
            {
                string id = (string)s[0];
                if (GameData.Get("Bonus", id) == null) continue;
                Add(new ClothesDef { id = id, setId = id, setName = (string)s[1], slot = (ClothesSlot)s[2], level = (int)s[3], coins = (int)s[4], cash = (int)s[5] });
            }
            foreach (var t in Trophies)
            {
                if (GameData.Get("Bonus", t) == null && GameData.Loaded && GameData.Section("Bonus").Count > 0) continue;
                Add(new ClothesDef { id = t, setId = t, setName = Loc.Prettify(t).Replace("of ", " of ").Replace("  ", " "), slot = ClothesSlot.Trophy });
            }
        }

        static void Add(ClothesDef d) { all.Add(d); byId[d.id] = d; }

        public static List<ClothesDef> All { get { Build(); return all; } }

        public static ClothesDef Get(string id)
        {
            Build();
            return id != null && byId.TryGetValue(id, out var d) ? d : null;
        }

        public static List<ClothesDef> BySlot(ClothesSlot slot)
        {
            Build();
            var res = new List<ClothesDef>();
            foreach (var d in all) if (d.slot == slot) res.Add(d);
            return res;
        }

        /// <summary>Shop clothes (not trophies) whose required level is exactly this level (level-up loot).</summary>
        public static List<ClothesDef> UnlockedAtLevel(int level)
        {
            Build();
            var res = new List<ClothesDef>();
            foreach (var d in all) if (d.slot != ClothesSlot.Trophy && d.level == level) res.Add(d);
            return res;
        }

        public static bool IsTrophy(string id) { var d = Get(id); return d != null && d.slot == ClothesSlot.Trophy; }

        public static string DisplayName(string id)
        {
            var d = Get(id);
            if (d == null) return Loc.Prettify(id);
            if (d.slot == ClothesSlot.Trophy || d.setId == d.id) return TrophyName(d);
            string piece = d.slot == ClothesSlot.Head ? "Hat" : d.slot == ClothesSlot.Chest ? "Outfit" : "Shoes";
            return d.setName + " " + piece;
        }

        static string TrophyName(ClothesDef d)
        {
            // "TrophyofWar" → "Trophy of War", "Pinofcrafting" → "Pin of Crafting"
            var s = d.setName;
            if (d.slot != ClothesSlot.Trophy) return s;
            s = d.id.Replace("of", " of ").Replace("the", " the ");
            s = Loc.Prettify(s.Replace("  ", " ").Trim());
            var words = s.Split(' ');
            for (int i = 0; i < words.Length; i++)
                if (words[i].Length > 0 && words[i] != "of" && words[i] != "the") words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            return string.Join(" ", words).Replace("  ", " ");
        }

        public static string IconPath(string id) => (IsTrophy(id) ? "Trophies/" : "Clothes/") + id;

        public static int UnlockCash(ClothesDef d) => 2 + d.level / 2;

        public static bool IsUnlocked(ClothesDef d) => ProfileService.P.level >= d.level || ProfileService.P.unlockedItems.Contains(d.id);

        // ---------- stats ----------
        /// <summary>Sum of Add mods and product of Multiply mods for one stat.</summary>
        public struct StatSum { public float add; public float mul; }

        public static StatSum Stat(IEnumerable<string> ids, string stat)
        {
            var s = new StatSum { mul = 1f };
            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id)) continue;
                var b = GameData.Get("Bonus", id);
                if (b == null || !b.Has(stat)) continue;
                var m = StatMod.Parse(b.Str(stat));
                if (m.Op == "Multiply") s.mul *= m.Value;
                else if (m.Op == "Add") s.add += m.Value;
            }
            return s;
        }

        /// <summary>Short stat text for one item, e.g. "ATK +2  DEF +2  LUCK +5".</summary>
        public static string StatLine(string id)
        {
            var b = GameData.Get("Bonus", id);
            if (b == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var kv in b.Raw)
            {
                if (kv.Key == "ID") continue;
                var m = StatMod.Parse(kv.Value?.ToString());
                string name = kv.Key == "Attack" ? "ATK" : kv.Key == "Defence" ? "DEF" : kv.Key == "Luck" ? "LUCK" : Loc.Prettify(kv.Key);
                if (sb.Length > 0) sb.Append("   ");
                if (m.Op == "Multiply") sb.Append(name).Append(" x").Append(m.Value.ToString("0.##"));
                else sb.Append(name).Append(m.Value >= 0 ? " +" : " ").Append(m.Value.ToString("0.##"));
                if (!string.IsNullOrEmpty(m.Tag)) sb.Append(" (").Append(m.Tag).Append(')');
            }
            return sb.Length == 0 ? "Bragging rights" : sb.ToString();
        }

        // ---------- actions ----------
        public static bool Buy(ClothesDef d)
        {
            var P = ProfileService.P;
            if (d == null || d.slot == ClothesSlot.Trophy || Progression.OwnsClothes(d.id)) return false;
            if (d.vipOnly && !Progression.IsVip) { UI.Message("VIP only", "This outfit is only for VIP members.", () => ScreenManager.Show(() => new VipScreen())); return false; }
            if (!IsUnlocked(d)) { UI.Toast("Reach level " + d.level + " or unlock it with fish."); return false; }
            if (!Progression.Spend(d.coins, d.cash)) { AudioManager.Sfx("Nomoney"); Progression.NotEnough(d.cash > 0); return false; }
            Progression.GiveClothes(d.id);
            ChallengeTracker.Report("shopBuys", 1);
            ProfileService.Save();
            AudioManager.Sfx("Clothes_1");
            UI.Toast(DisplayName(d.id) + " is yours!", Theme.Good);
            return true;
        }

        public static bool Unlock(ClothesDef d)
        {
            if (d == null || IsUnlocked(d)) return false;
            if (!Progression.Spend(0, UnlockCash(d))) { Progression.NotEnough(true); return false; }
            ProfileService.P.unlockedItems.Add(d.id);
            ProfileService.Save();
            AudioManager.Sfx("Unlock");
            return true;
        }

        /// <summary>Equip (or unequip when already worn) an owned item in its slot.</summary>
        public static void ToggleWear(ClothesDef d)
        {
            var P = ProfileService.P;
            if (d == null || !Progression.OwnsClothes(d.id)) return;
            switch (d.slot)
            {
                case ClothesSlot.Head: P.wornHead = P.wornHead == d.id ? "" : d.id; break;
                case ClothesSlot.Chest: P.wornChest = P.wornChest == d.id ? "" : d.id; break;
                case ClothesSlot.Feet: P.wornFeet = P.wornFeet == d.id ? "" : d.id; break;
                case ClothesSlot.Trophy: P.wornTrophy = P.wornTrophy == d.id ? "" : d.id; break;
            }
            ProfileService.Save();
            AudioManager.Sfx("Clothes_2");
        }

        public static bool IsWorn(string id)
        {
            var P = ProfileService.P;
            return !string.IsNullOrEmpty(id) && (P.wornHead == id || P.wornChest == id || P.wornFeet == id || P.wornTrophy == id);
        }
    }
}
