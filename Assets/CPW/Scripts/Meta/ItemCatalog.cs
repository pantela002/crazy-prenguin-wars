using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>A purchasable bundle (several items in one purchase).</summary>
    public class BundleDef
    {
        public string id, name, description, iconPath;
        public int priceCoins, priceCash, requiredLevel;
        public List<ItemStack> contents = new List<ItemStack>();
        public List<string> unlocks = new List<string>();   // items unlocked permanently (skip level lock)
    }

    /// <summary>
    /// Shop data built from the original config: Item (weapons/boosters), ItemPrice (InGame coins, Premium cash,
    /// UnlockPricePremium), RequiredLevel, IsVip and AmountPurchased; Page/Tab sections for the shop layout.
    /// Also implements buying and unlocking.
    /// </summary>
    public static class ItemCatalog
    {
        static PlayerProfile P => ProfileService.P;

        // ---------- lookups ----------
        public static string Name(Record item) => item == null ? "" : Loc.T(item.Str("Name", item.Id));
        public static string Description(Record item)
        {
            if (item == null) return "";
            var key = item.Str("Description");
            var s = MetaUI.TOr(key, "");
            return s;
        }

        public static string Type(Record item) => item?.Str("Type", "") ?? "";
        public static bool IsWeapon(Record item) => Type(item) == "Weapon";
        public static bool IsBooster(Record item) => Type(item) == "Booster";
        public static bool HasCategory(Record item, string cat) => item != null && item.List("Category").Contains(cat);
        public static bool IsVipItem(Record item) => item != null && (item.Bool("IsVip") || HasCategory(item, "VIP"));
        public static bool IsInfinite(Record item) => item != null && item.Bool("IsInfinite");
        public static int RequiredLevel(Record item) => item == null ? 1 : Mathf.Max(1, item.Int("RequiredLevel", 1));
        public static int AmountPurchased(Record item) => item == null ? 1 : Mathf.Max(1, item.Int("AmountPurchased", 1));

        /// <summary>Icon path following ModelLibrary.Icon conventions: supplies use their textured Blender render
        /// (Supplies/{item id}, Blender/scripts/supplies.py) and weapons theirs (WeaponsTextured/{item id}) when it
        /// exists; otherwise Weapons/{WeaponIcon id},
        /// Boosters/{BoosterIcon id}.</summary>
        public static string IconPath(Record item)
        {
            if (item == null) return null;
            if (IsBooster(item) && ModelLibrary.Icon("Supplies/" + item.Id) != null) return "Supplies/" + item.Id;
            if (IsWeapon(item) && ModelLibrary.Icon("WeaponsTextured/" + item.Id) != null) return "WeaponsTextured/" + item.Id;
            var icon = item.Str("Icon", "");
            var id = GameData.RefId(icon);
            if (icon.StartsWith("#BoosterIcon")) return "Boosters/" + id;
            if (icon.StartsWith("#EmoticonIcon")) return "Emoticons/" + id;
            if (icon.StartsWith("#WeaponIcon")) return "Weapons/" + id;
            return (IsBooster(item) ? "Boosters/" : "Weapons/") + item.Id;
        }

        public static Record PriceRecord(Record item) => item?.Ref("PriceInfo") ?? GameData.Get("ItemPrice", item?.Id);
        public static int PriceCoins(Record item) => PriceRecord(item)?.Int("InGame") ?? 0;
        public static int PriceCash(Record item) => PriceRecord(item)?.Int("Premium") ?? 0;
        public static int UnlockCash(Record item) => PriceRecord(item)?.Int("UnlockPricePremium") ?? 0;

        /// <summary>Featured copies (FeaturedMegaNuke) refer to the real item (MegaNuke).</summary>
        public static Record Real(Record item)
        {
            if (item == null) return null;
            if (item.Id.StartsWith("Featured"))
            {
                var r = GameData.Item(item.Id.Substring("Featured".Length));
                if (r != null) return r;
            }
            return item;
        }

        /// <summary>Items that belong in the shop (weapons + boosters with a price).</summary>
        public static List<Record> ShopItems(string type, string category = null)
        {
            var res = new List<Record>();
            foreach (var r in GameData.Section("Item").Values)
            {
                if (r.Id.StartsWith("Featured") || r.Id == "Banner") continue;
                if (Type(r) != type) continue;
                if (PriceRecord(r) == null) continue;
                if (IsInfinite(r)) continue;   // Punch: always available, never sold
                if (category != null && !HasCategory(r, category)) continue;
                res.Add(r);
            }
            res.Sort((a, b) =>
            {
                int c = RequiredLevel(a).CompareTo(RequiredLevel(b));
                if (c != 0) return c;
                c = (PriceCoins(a) + PriceCash(a) * 100).CompareTo(PriceCoins(b) + PriceCash(b) * 100);
                return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
            });
            return res;
        }

        /// <summary>Featured tab: items in the ShopFeatured category plus the Featured* copies (BigItems).</summary>
        public static List<Record> Featured()
        {
            var res = new List<Record>();
            var seen = new HashSet<string>();
            var tab = GameData.Get("Tab", "ShopFeaturedTab");
            if (tab != null)
                foreach (var r in tab.RefList("BigItems"))
                {
                    var real = Real(r);
                    if (real == null || real.Id == "Banner" || PriceRecord(real) == null || !seen.Add(real.Id)) continue;
                    res.Add(real);
                }
            foreach (var r in GameData.Section("Item").Values)
                if (HasCategory(r, "ShopFeatured") && !r.Id.StartsWith("Featured") && seen.Add(r.Id)) res.Add(r);
            return res;
        }

        /// <summary>Shop weapons then boosters whose RequiredLevel is exactly this level (level-up loot).</summary>
        public static List<Record> UnlockedAtLevel(int level)
        {
            var res = new List<Record>();
            foreach (var r in ShopItems("Weapon")) if (RequiredLevel(r) == level) res.Add(r);
            foreach (var r in ShopItems("Booster")) if (RequiredLevel(r) == level) res.Add(r);
            return res;
        }

        // ---------- lock state ----------
        public static bool IsUnlocked(Record item) => P.unlockedItems.Contains(item.Id) || P.level >= RequiredLevel(item);
        public static bool VipBlocked(Record item) => IsVipItem(item) && !Progression.IsVip;

        // ---------- actions ----------
        /// <summary>Buy one pack (AmountPurchased) of a weapon/booster. Shows feedback. Returns true on success.</summary>
        public static bool Buy(Record item, int packs = 1)
        {
            item = Real(item);
            if (item == null) return false;
            if (VipBlocked(item)) { UI.Message("VIP only", "This item is only for VIP members.", () => ScreenManager.Show(() => new VipScreen())); return false; }
            if (!IsUnlocked(item)) { UI.Toast("Reach level " + RequiredLevel(item) + " or unlock it with Cash first."); return false; }
            int coins = PriceCoins(item) * packs, cash = PriceCash(item) * packs;
            if (!Progression.Spend(coins, cash))
            {
                AudioManager.Sfx("Nomoney");
                Progression.NotEnough(cash > 0);
                return false;
            }
            int amount = AmountPurchased(item) * packs;
            P.AddAmmo(item.Id, amount);
            if (coins > 0 && IsWeapon(item)) P.AddCounter("Spend_Coins_In_Ammo", coins);
            ChallengeTracker.Report("shopBuys", 1);
            ProfileService.Save();
            AudioManager.Sfx("Buy");
            UI.Toast("+" + amount + " " + Name(item), Theme.Good);
            return true;
        }

        /// <summary>Unlock a level-locked item early with fish (UnlockPricePremium), like the original.</summary>
        public static bool Unlock(Record item)
        {
            item = Real(item);
            if (item == null || IsUnlocked(item)) return false;
            int price = UnlockCash(item);
            if (price <= 0) { UI.Toast("Reach level " + RequiredLevel(item) + " to unlock this."); return false; }
            if (!Progression.Spend(0, price)) { AudioManager.Sfx("Nomoney"); Progression.NotEnough(true); return false; }
            P.unlockedItems.Add(item.Id);
            UpdateUnlockCounter();
            ProfileService.Save();
            AudioManager.Sfx("Unlock");
            UI.Toast(MetaUI.Fmt("TRANSACTION_FEEDBACK_SUCCESSFUL_UNLOCKED_DESCRIPTION", Name(item)), Theme.Good);
            return true;
        }

        static void UpdateUnlockCounter()
        {
            int n = 0;
            foreach (var id in P.unlockedItems) if (IsWeapon(GameData.Item(id))) n++;
            int cur = P.Counter("All_Weapons_Unlocked");
            if (n > cur) P.AddCounter("All_Weapons_Unlocked", n - cur);
        }

        // ---------- weapon stats ----------
        /// <summary>Damage/radius summary found by walking Item → Emitter → Missile → Explosion.</summary>
        public struct WeaponStats { public float damage; public float radius; public int projectiles; public string targeting; }

        public static WeaponStats Stats(Record item)
        {
            var s = new WeaponStats { targeting = item?.Str("Targeting", "") ?? "" };
            if (item == null) return s;
            var visited = new HashSet<string>();
            foreach (var e in item.RefList("Emitters")) WalkEmitter(e, ref s, visited, 0, 1);
            return s;
        }

        static void WalkEmitter(Record emitter, ref WeaponStats s, HashSet<string> visited, int depth, int mult)
        {
            if (emitter == null || depth > 6 || !visited.Add(emitter.ToString())) return;
            int n = Mathf.Max(1, emitter.Int("Number", 1)) * mult;
            var special = emitter.Ref("SpecialEffect");
            if (special == null) return;
            if (special.Section == "EmitMissile")
            {
                if (depth == 0) s.projectiles += n;
                var m = special.Ref("Missile");
                if (m != null) foreach (var e in m.RefList("Emitters")) WalkEmitter(e, ref s, visited, depth + 1, n);
            }
            else if (special.Section == "EmitExplosion")
            {
                var ex = special.Ref("Explosion");
                if (ex == null) return;
                var atk = StatMod.Parse(ex.Str("Attack", ""));
                s.damage += atk.Value * n;
                s.radius = Mathf.Max(s.radius, ex.Float("DamageRadius"));
                if (depth == 0) s.projectiles += n;
            }
        }

        // ---------- bundles ----------
        static List<BundleDef> bundles;

        /// <summary>
        /// Bundles. Uses Item rows with Type "Bundle" (ItemList/ItemAmountList) when the config has them; the shipped
        /// config has none (only BundleIcon StarterBundle/MidBundle and Upsell.StarterBundlePCRequirement = 24), so the
        /// remake defines three bundles here. Prices are about 30% below buying the packs one by one.
        /// </summary>
        public static List<BundleDef> Bundles()
        {
            if (bundles != null) return bundles;
            bundles = new List<BundleDef>();
            foreach (var r in GameData.Section("Item").Values)
            {
                if (Type(r) != "Bundle") continue;
                var b = new BundleDef { id = r.Id, name = Name(r), description = Description(r), iconPath = "Ui/" + r.Id,
                    priceCoins = PriceCoins(r), priceCash = PriceCash(r), requiredLevel = RequiredLevel(r) };
                var ids = r.List("ItemList"); var amts = r.List("ItemAmountList");
                for (int i = 0; i < ids.Count; i++)
                {
                    int.TryParse(i < amts.Count ? amts[i] : "1", out int a);
                    b.contents.Add(new ItemStack(GameData.RefId(ids[i]), Mathf.Max(1, a)));
                }
                bundles.Add(b);
            }
            if (bundles.Count > 0) return bundles;

            int starterCash = GameData.Get("Upsell", "Default")?.Int("StarterBundlePCRequirement", 24) ?? 24;
            var starter = new BundleDef { id = "StarterBundle", name = "Starter Bundle", iconPath = "Ui/StarterBundle",
                description = Loc.T("BUNDLES_DESCRIPTION"), priceCash = starterCash, requiredLevel = 1 };
            starter.contents.Add(new ItemStack("Grenade", 10));
            starter.contents.Add(new ItemStack("Dynamite", 5));
            starter.contents.Add(new ItemStack("Shotgun", 5));
            starter.contents.Add(new ItemStack("Shield", 2));
            starter.contents.Add(new ItemStack("Bandage", 3));
            starter.unlocks.Add("Dynamite"); starter.unlocks.Add("Shotgun");
            bundles.Add(starter);

            var war = new BundleDef { id = "WarPack", name = Loc.T("BUNDLE_PACK"), iconPath = "Ui/MidBundle",
                description = "Heavy hitters for the seasoned penguin.", priceCash = 49, requiredLevel = 5 };
            war.contents.Add(new ItemStack("Molotov", 5));
            war.contents.Add(new ItemStack("Mortar", 5));
            war.contents.Add(new ItemStack("ClusterGrenade", 5));
            war.contents.Add(new ItemStack("Minigun", 5));
            war.contents.Add(new ItemStack("SalmonSushi", 2));
            war.unlocks.Add("Molotov"); war.unlocks.Add("Mortar"); war.unlocks.Add("ClusterGrenade"); war.unlocks.Add("Minigun");
            bundles.Add(war);

            var coin = new BundleDef { id = "SupplyCrate", name = "Supply Crate", iconPath = "Ui/StarterBundle",
                description = "Restock the basics with coins.", priceCoins = 900, requiredLevel = 1 };
            coin.contents.Add(new ItemStack("BasicNuke", 10));
            coin.contents.Add(new ItemStack("Pistol", 10));
            coin.contents.Add(new ItemStack("Grenade", 5));
            coin.contents.Add(new ItemStack("Umbrella", 2));
            bundles.Add(coin);
            return bundles;
        }

        public static bool BuyBundle(BundleDef b)
        {
            if (P.level < b.requiredLevel) { UI.Toast("Reach level " + b.requiredLevel + " first."); return false; }
            if (!Progression.Spend(b.priceCoins, b.priceCash)) { AudioManager.Sfx("Nomoney"); Progression.NotEnough(b.priceCash > 0); return false; }
            foreach (var s in b.contents) Progression.GiveItem(s.id, s.amount);
            foreach (var u in b.unlocks) if (!P.unlockedItems.Contains(u)) P.unlockedItems.Add(u);
            UpdateUnlockCounter();
            ChallengeTracker.Report("shopBuys", 1);
            ProfileService.Save();
            AudioManager.Sfx("Buy");
            UI.Toast(b.name + " purchased!", Theme.Good);
            return true;
        }

        /// <summary>Booster items the player owns (amount > 0).</summary>
        public static List<Record> OwnedBoosters()
        {
            var res = new List<Record>();
            foreach (var s in P.items)
            {
                var r = GameData.Item(s.id);
                if (s.amount > 0 && IsBooster(r)) res.Add(r);
            }
            return res;
        }
    }
}
