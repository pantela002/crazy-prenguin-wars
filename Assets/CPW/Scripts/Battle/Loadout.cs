using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>Small lookups on Item records used by the battle (icons, categories, graphics).</summary>
    public static class BattleItems
    {
        public static bool IsWeapon(string id) => GameData.Item(id)?.Str("Type") == "Weapon";
        public static bool IsBooster(string id) => GameData.Item(id)?.Str("Type") == "Booster";
        public static bool IsInfinite(string id) => GameData.Item(id)?.Bool("IsInfinite") ?? false;

        public static bool InCategory(string id, string category)
        {
            var r = GameData.Item(id);
            if (r == null) return false;
            var cats = r.List("Category");
            for (int i = 0; i < cats.Count; i++) if (cats[i] == category) return true;
            return false;
        }

        /// <summary>Icon sprite path: "Weapons/{WeaponIcon id}" or "Boosters/{BoosterIcon id}".</summary>
        public static Sprite Icon(string id)
        {
            var orig = OriginalArt.Icon(id);
            if (orig != null) return orig;
            var r = GameData.Item(id);
            if (r == null) return null;
            var icon = r.Str("Icon");
            if (string.IsNullOrEmpty(icon)) return null;
            var iconId = GameData.RefId(icon);
            if (icon.Contains("BoosterIcon")) return ModelLibrary.Icon("Boosters/" + iconId) ?? ModelLibrary.Icon("Weapons/" + iconId);
            return ModelLibrary.Icon("Weapons/" + iconId);
        }

        /// <summary>WeaponGraphic id the avatar holds for this item (Item.Graphics).</summary>
        public static string Graphic(string id)
        {
            var g = GameData.Item(id)?.Str("Graphics");
            return string.IsNullOrEmpty(g) ? null : GameData.RefId(g);
        }

        public static string Name(string id)
        {
            var r = GameData.Item(id);
            return r == null ? id : Loc.T(r.Str("Name", id));
        }

        public static int SortPriority(string id) => GameData.Item(id)?.Int("SortPriority", 99) ?? 99;

        /// <summary>Weapons/boosters offered in Practice: all real items (not the shop's Featured* copies or the
        /// placeholder Banner).</summary>
        public static bool InPractice(Record r) => r != null && !r.Id.StartsWith("Featured") && r.Id != "Banner";
        public static int RequiredLevel(string id) => GameData.Item(id)?.Int("RequiredLevel", 1) ?? 1;
    }

    /// <summary>
    /// Ammo one penguin can use in this battle. Counts come from the local profile (spent immediately),
    /// from a fixed loadout (AI, other local players, online opponents) or are infinite (practice, IsInfinite items).
    /// Ammo picked up from crates is kept separately so the meta code can add it to the profile at the end.
    /// </summary>
    public class Loadout
    {
        public const int Infinite = -1;

        readonly Dictionary<string, int> counts = new Dictionary<string, int>();   // fixed loadout or Infinite
        readonly HashSet<string> profileItems = new HashSet<string>();
        readonly Dictionary<string, int> bonus = new Dictionary<string, int>();    // picked up this battle
        readonly Dictionary<string, int> used = new Dictionary<string, int>();
        readonly Dictionary<string, int> earned = new Dictionary<string, int>();
        public readonly List<string> Weapons = new List<string>();
        public readonly List<string> Boosters = new List<string>();
        bool useProfile;

        /// <summary>Ammo is the local profile's (Quick Match / Online for the local player).</summary>
        public bool UsesProfile => useProfile;

        /// <summary>Build the loadout for a slot in a battle mode.</summary>
        public static Loadout For(PlayerSlot slot, BattleMode mode)
        {
            var l = new Loadout();
            if (mode == BattleMode.Practice || mode == BattleMode.Tutorial)
            {
                // Free ammo: Practice gives every weapon and booster (the original "Practice" category only had a
                // handful; the remake's practice is the place to try everything); the tutorial uses "Tutorial1"
                foreach (var r in GameData.Section("Item").Values)
                {
                    var type = r.Str("Type");
                    if (type != "Weapon" && type != "Booster") continue;
                    if (mode == BattleMode.Tutorial ? BattleItems.InCategory(r.Id, "Tutorial1") : BattleItems.InPractice(r)) l.counts[r.Id] = Infinite;
                }
                if (mode == BattleMode.Tutorial) l.counts["Punch"] = Infinite;
            }
            else if (slot.usesProfileInventory)
            {
                l.useProfile = true;
                var p = ProfileService.P;
                foreach (var s in p.items)
                {
                    if (s.amount <= 0 && !BattleItems.IsInfinite(s.id)) continue;
                    var type = GameData.Item(s.id)?.Str("Type");
                    if (type == "Weapon") l.profileItems.Add(s.id);
                    else if (type == "Booster" && (slot.boosters.Count == 0 || slot.boosters.Contains(s.id))) l.profileItems.Add(s.id);
                }
                // Tuner.DefaultWeaponList: always offered; IsInfinite ones (Punch) never run out
                var tuner = GameData.Tuner;
                if (tuner != null)
                    foreach (var w in tuner.List("DefaultWeaponList"))
                    {
                        var id = GameData.RefId(w);
                        if (BattleItems.IsInfinite(id)) l.counts[id] = Infinite;
                        else l.profileItems.Add(id);
                    }
            }
            else
            {
                foreach (var s in slot.loadout)
                {
                    if (string.IsNullOrEmpty(s.id) || GameData.Item(s.id) == null) continue;
                    l.counts[s.id] = BattleItems.IsInfinite(s.id) || s.amount < 0 ? Infinite : s.amount;
                }
                foreach (var b in slot.boosters) if (!l.counts.ContainsKey(b) && GameData.Item(b) != null) l.counts[b] = 1;
                if (l.counts.Count == 0)
                {
                    // Nothing given: AI falls back to the practice set, which always works
                    foreach (var r in GameData.Section("Item").Values)
                        if (r.Str("Type") == "Weapon" && BattleItems.InCategory(r.Id, "Practice")) l.counts[r.Id] = Infinite;
                }
                if (!l.counts.ContainsKey("Punch") && GameData.Item("Punch") != null) l.counts["Punch"] = Infinite;
            }
            l.Rebuild();
            return l;
        }

        void Rebuild()
        {
            Weapons.Clear(); Boosters.Clear();
            foreach (var id in counts.Keys) Add(id);
            foreach (var id in profileItems) Add(id);
            foreach (var id in bonus.Keys) Add(id);
            Weapons.Sort(Compare); Boosters.Sort(Compare);
        }

        void Add(string id)
        {
            if (BattleItems.IsWeapon(id)) { if (!Weapons.Contains(id)) Weapons.Add(id); }
            else if (BattleItems.IsBooster(id)) { if (!Boosters.Contains(id)) Boosters.Add(id); }
        }

        static int Compare(string a, string b)
        {
            int c = BattleItems.SortPriority(a).CompareTo(BattleItems.SortPriority(b));
            return c != 0 ? c : string.CompareOrdinal(a, b);
        }

        /// <summary>Ammo left; Infinite (-1) for unlimited items.</summary>
        public int Count(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            if (counts.TryGetValue(id, out var c) && c == Infinite) return Infinite;
            int n = bonus.TryGetValue(id, out var b) ? b : 0;
            if (profileItems.Contains(id)) n += ProfileService.P.Ammo(id);
            else if (c > 0) n += c;
            return n;
        }

        public bool Has(string id) { int c = Count(id); return c == Infinite || c > 0; }

        /// <summary>Spend one. Profile ammo is removed from the profile right away (as the original server did).</summary>
        public bool Consume(string id)
        {
            int c = Count(id);
            if (c == 0) return false;
            used[id] = (used.TryGetValue(id, out var u) ? u : 0) + 1;
            if (c == Infinite) return true;
            if (bonus.TryGetValue(id, out var b) && b > 0)
            {
                bonus[id] = b - 1;
                earned[id] = Mathf.Max(0, (earned.TryGetValue(id, out var e) ? e : 0) - 1);
                return true;
            }
            if (useProfile && profileItems.Contains(id))
            {
                ProfileService.P.AddAmmo(id, -1);
                ProfileService.NotifyChanged();
                return true;
            }
            if (counts.TryGetValue(id, out var k) && k > 0) counts[id] = k - 1;
            return true;
        }

        /// <summary>Ammo from a crate: usable now, reported as earnedItems if still unused at the end.</summary>
        public void AddBonus(string id, int amount)
        {
            if (amount <= 0) return;
            bonus[id] = (bonus.TryGetValue(id, out var b) ? b : 0) + amount;
            earned[id] = (earned.TryGetValue(id, out var e) ? e : 0) + amount;
            Rebuild();
        }

        /// <summary>A weapon/booster just bought in battle: its profile ammo becomes usable in this loadout.</summary>
        public void AddProfileItem(string id)
        {
            if (!useProfile || string.IsNullOrEmpty(id) || GameData.Item(id) == null) return;
            if (counts.TryGetValue(id, out var c) && c == Infinite) return;
            if (profileItems.Add(id)) Rebuild();
        }

        /// <summary>Overwrite counts from an online snapshot (fixed loadouts only).</summary>
        public void SetCount(string id, int amount)
        {
            if (profileItems.Contains(id)) return;
            counts[id] = amount;
            Rebuild();
        }

        public void FillUsed(List<ItemStack> list)
        {
            list.Clear();
            foreach (var kv in used) list.Add(new ItemStack(kv.Key, kv.Value));
        }

        public void FillEarned(List<ItemStack> list)
        {
            list.Clear();
            foreach (var kv in earned) if (kv.Value > 0) list.Add(new ItemStack(kv.Key, kv.Value));
        }

        public void FillCounts(List<ItemStack> list)
        {
            list.Clear();
            foreach (var w in Weapons) list.Add(new ItemStack(w, Count(w)));
            foreach (var b in Boosters) list.Add(new ItemStack(b, Count(b)));
        }

        /// <summary>First usable weapon (Tuner.SelectedDefaultWeapon if available).</summary>
        public string DefaultWeapon()
        {
            var sel = GameData.RefId(GameData.Tuner?.Str("SelectedDefaultWeapon") ?? "#Item.BasicNuke");
            if (Has(sel)) return sel;
            foreach (var w in Weapons) if (Has(w)) return w;
            return null;
        }
    }
}
