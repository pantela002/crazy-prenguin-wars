using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// Shop laid out like the original Screen.Shop: Featured, Bundles, Weapons (All/Rockets/Grenades/Guns/Special),
    /// Boosters and Clothes pages, plus a shortcut to the bank (coins & Cash; the code calls Cash "fish"/premium).
    /// Weapons have the original Rockets/Grenades/Guns/Special tabs; boosters (supplies) are grouped by what they
    /// do (remake grouping, the original showed one list).
    /// </summary>
    public class ShopScreen : MetaScreen
    {
        static int page, sub;
        readonly int startPage;
        RectTransform grid, subRow;
        List<Button> pageTabs, subTabs;
        protected override string Title => Loc.T("BUTTON_SUPPLIES");

        static readonly string[] WeaponCats = { null, "Rockets", "Grenades", "Guns", "Special" };
        static readonly string[] WeaponTabIds = { "ShopWeaponsTabAll", "ShopWeaponsTabRockets", "ShopWeaponsTabGrenades", "ShopWeaponsTabGuns", "ShopWeaponsTabSpecial" };

        // booster (supply) tabs: All, Power-ups, Protection, Traps; anything not listed shows under All only
        static readonly string[] BoosterTabs = { "All", "Power-ups", "Protection", "Traps" };
        static readonly string[][] BoosterGroups =
        {
            null,
            new[] { "SalmonSushi", "SpicySushi", "WasabiSushi", "ProteinBar", "Scroll", "Kamikaze", "Confetti" },
            new[] { "Shield", "Bandage", "Umbrella", "PogoStick", "Innertube" },
            new[] { "Mine", "FlameMine", "SpringMine", "Caltrops", "Mushroom", "Burrito" },
        };

        readonly int startSub;

        public ShopScreen() { startPage = -1; }
        public ShopScreen(int page, int sub = 0) { startPage = page; startSub = sub; }

        public override void OnShow() => FreeAmmoPack.Offer();

        protected override void BuildContent()
        {
            if (startPage >= 0) { page = startPage; sub = startSub; }
            var pages = new[]
            {
                Loc.T("SHOPTAB_FEATURED"), Loc.T("TAB_BUNDLES"), Loc.T("SHOPTAB_WEAPONS"), Loc.T("SHOPTAB_BOOSTERS"), Loc.T("SHOPTAB_CLOTHES"), Loc.T("TAB_FISH_COINS")
            };
            var top = UI.Rect(Content, "Pages");
            UI.Anchor(top, 0, 0.89f, 1, 1);
            pageTabs = MetaUI.Tabs(top, pages, page, i =>
            {
                if (i == 5) { ScreenManager.Show(() => new BankScreen()); return; }
                page = i; sub = 0; MetaUI.SetTabSelected(pageTabs, i); Fill();
            }, 36);

            subRow = UI.Rect(Content, "Sub");
            UI.Anchor(subRow, 0, 0.8f, 1, 0.88f);

            var panel = MetaUI.CardPanel(Content, MetaUI.CardDark);
            UI.Anchor(panel.rectTransform, 0, 0, 1, 0.79f);
            var sr = UI.ScrollGrid(panel.transform, out grid, new Vector2(250, 320), new Vector2(18, 18));
            UI.Stretch((RectTransform)sr.transform, 6, 6, 6, 6);
            Fill();
        }

        void Fill()
        {
            UI.Clear(subRow);
            UI.Clear(grid);
            var g = grid.GetComponent<GridLayoutGroup>();
            g.cellSize = page == 1 ? new Vector2(560, 330) : new Vector2(250, 320);
            switch (page)
            {
                case 0:
                    Hint("Hot picks of the day");
                    foreach (var r in ItemCatalog.Featured()) ItemCard(r);
                    break;
                case 1:
                    Hint(Loc.T("BUNDLES_DESCRIPTION"));
                    foreach (var b in ItemCatalog.Bundles()) BundleCard(b);
                    break;
                case 2:
                {
                    var names = new string[WeaponCats.Length];
                    for (int i = 0; i < names.Length; i++) names[i] = Loc.T(GameData.Get("Tab", WeaponTabIds[i])?.Str("Name") ?? (i == 0 ? "TAB_ALL" : WeaponCats[i]));
                    subTabs = MetaUI.Tabs(subRow, names, sub, i => { sub = i; Fill(); }, 30);
                    foreach (var r in ItemCatalog.ShopItems("Weapon", WeaponCats[sub])) ItemCard(r);
                    break;
                }
                case 3:
                {
                    subTabs = MetaUI.Tabs(subRow, BoosterTabs, sub, i => { sub = i; Fill(); }, 30);
                    var group = BoosterGroups[Mathf.Clamp(sub, 0, BoosterGroups.Length - 1)];
                    foreach (var r in ItemCatalog.ShopItems("Booster"))
                        if (group == null || System.Array.IndexOf(group, r.Id) >= 0) ItemCard(r);
                    break;
                }
                case 4:
                {
                    subTabs = MetaUI.Tabs(subRow, new[] { "Hats", "Outfits", "Shoes" }, sub, i => { sub = i; Fill(); }, 30);
                    var list = ClothesCatalog.BySlot((ClothesSlot)sub);
                    list.Sort((a, b) => a.level != b.level ? a.level.CompareTo(b.level) : string.CompareOrdinal(a.id, b.id));
                    foreach (var d in list) ClothesCard(d);
                    break;
                }
            }
        }

        void Hint(string text)
        {
            var l = UI.Label(subRow, text, 32, Color.white, TextAnchor.MiddleLeft, true);
            UI.Stretch(l.rectTransform, 10, 10, 0, 0);
        }

        void ItemCard(Record r)
        {
            var P = ProfileService.P;
            var item = r;
            bool locked = !ItemCatalog.IsUnlocked(r), vip = ItemCatalog.VipBlocked(r);
            int owned = P.Ammo(r.Id);
            var card = ItemCards.Card(grid, ItemCatalog.IconPath(r), ItemCatalog.Name(r), () => ItemInfo.Show(item, Fill), out var f,
                null, owned > 0 ? owned.ToString() : null, Theme.Good);
            var h = UI.HBox(f, 6, TextAnchor.MiddleCenter);
            h.childForceExpandWidth = false;
            if (locked && ItemCatalog.UnlockCash(r) > 0)
            {
                MetaUI.Amount(f, "cash", ItemCatalog.UnlockCash(r) + " to unlock", 26, Theme.Text);
                ItemCards.LockOverlay(card.transform, "Lv " + ItemCatalog.RequiredLevel(r));
            }
            else
            {
                MetaUI.Price(f, ItemCatalog.PriceCoins(r), ItemCatalog.PriceCash(r), 30);
                var amt = UI.Label(f, "x" + ItemCatalog.AmountPurchased(r), 26, Theme.Muted);
                UI.Layout(amt, 60, 36);
                if (locked) ItemCards.LockOverlay(card.transform, "Lv " + ItemCatalog.RequiredLevel(r));
            }
            if (!locked && vip) ItemCards.LockOverlay(card.transform, "VIP", MetaUI.Gold);
            if (ItemCatalog.IsVipItem(r))
            {
                var tag = UI.Label(card.transform, "VIP", 26, MetaUI.Gold, TextAnchor.UpperLeft, true);
                UI.Anchor(tag.rectTransform, 0.05f, 0.85f, 0.5f, 0.98f);
            }
        }

        void BundleCard(BundleDef b)
        {
            var bundle = b;
            // the whole card is a big touch target, so buying always asks first (no accidental buys while scrolling)
            Action buyIt = () =>
            {
                if (ProfileService.P.level < bundle.requiredLevel) { UI.Toast("Reach level " + bundle.requiredLevel + " first."); return; }
                ItemInfo.ConfirmSpend(bundle.priceCoins, bundle.priceCash, bundle.name, () => { if (ItemCatalog.BuyBundle(bundle)) Fill(); }, true);
            };
            var btn = UI.Button(grid, null, buyIt, UI.ButtonStyle.Plain, 24, "Bundle " + b.id);
            btn.GetComponent<Image>().color = MetaUI.Card;
            var tile = MetaUI.IconTile(btn.transform, b.iconPath, b.name, MetaUI.Orange);
            UI.Anchor(tile, 0.03f, 0.3f, 0.33f, 0.95f);
            var t = UI.Label(btn.transform, b.name, 40, Theme.Secondary, TextAnchor.UpperLeft, true);
            UI.Anchor(t.rectTransform, 0.36f, 0.8f, 0.98f, 0.96f);
            var contents = new System.Text.StringBuilder();
            foreach (var s in b.contents) contents.Append(s.amount).Append("x ").Append(Progression.NameOf(s.id)).Append('\n');
            if (b.unlocks.Count > 0)
            {
                contents.Append(Loc.T("BUNDLES_UNLOCKS")).Append(' ');
                for (int i = 0; i < b.unlocks.Count; i++) contents.Append(i > 0 ? ", " : "").Append(Progression.NameOf(b.unlocks[i]));
            }
            var c = UI.Label(btn.transform, contents.ToString(), 26, Theme.Text, TextAnchor.UpperLeft);
            UI.Anchor(c.rectTransform, 0.36f, 0.22f, 0.98f, 0.8f);
            var price = UI.Rect(btn.transform, "Price");
            UI.Anchor(price, 0.03f, 0.04f, 0.33f, 0.26f);
            UI.HBox(price, 6, TextAnchor.MiddleCenter).childForceExpandWidth = false;
            MetaUI.Price(price, b.priceCoins, b.priceCash, 36);
            var buy = UI.Button(btn.transform, Loc.T("BUY"), buyIt, UI.ButtonStyle.Good, 36);
            UI.Anchor((RectTransform)buy.transform, 0.64f, 0.04f, 0.97f, 0.2f);
            if (ProfileService.P.level < b.requiredLevel) ItemCards.LockOverlay(btn.transform, "Lv " + b.requiredLevel);
        }

        void ClothesCard(ClothesDef d)
        {
            var def = d;
            bool owned = Progression.OwnsClothes(d.id), locked = !ClothesCatalog.IsUnlocked(d);
            var card = ItemCards.Card(grid, ClothesCatalog.IconPath(d.id), ClothesCatalog.DisplayName(d.id), () => ClothesInfo.Show(def, Fill), out var f,
                owned ? new Color(0.85f, 1f, 0.85f) : (Color?)null, ClothesCatalog.IsWorn(d.id) ? "ON" : null, Theme.Good);
            UI.HBox(f, 6, TextAnchor.MiddleCenter).childForceExpandWidth = false;
            if (owned)
            {
                var l = UI.Label(f, "Owned", 30, Theme.Good, TextAnchor.MiddleCenter, true);
                UI.Layout(l, 200, 40);
            }
            else MetaUI.Price(f, d.coins, d.cash, 30);
            if (!owned && locked) ItemCards.LockOverlay(card.transform, "Lv " + d.level);
            else if (!owned && d.vipOnly && !Progression.IsVip) ItemCards.LockOverlay(card.transform, "VIP", MetaUI.Gold);
        }
    }

    /// <summary>
    /// Free ammo rescue (original FreeAmmoPopUp, ShopScreen.as:135): a broke player (under 50 coins and 5 fish)
    /// whose ammo is below the minimum gets a small help package, at most once a day, shown when opening the shop.
    /// The package came from the server (help_package coins + weapon) and Tuner.MinimumAmmoForMatch did not ship,
    /// so the values are INVENTED: 100 coins + 10 BasicNuke when the player has fewer than 10 shots of ammo.
    /// The day is stored in PlayerPrefs per player id (PlayerProfile has no field for it).
    /// </summary>
    public static class FreeAmmoPack
    {
        const int MaxCoins = 50, MaxCash = 5, GiftCoins = 100, GiftAmmo = 10;
        const string GiftWeapon = "BasicNuke";

        public static int MinAmmo => GameData.Tuner != null ? GameData.Tuner.Int("MinimumAmmoForMatch", 10) : 10;
        static string Key => "cpw_freeammo_" + (string.IsNullOrEmpty(ProfileService.P.playerId) ? "local" : ProfileService.P.playerId);

        /// <summary>Weapon ammo the player owns (Punch and other infinite items don't count).</summary>
        public static int TotalAmmo()
        {
            int n = 0;
            foreach (var s in ProfileService.P.items)
            {
                var r = GameData.Item(s.id);
                if (s.amount > 0 && ItemCatalog.IsWeapon(r) && !ItemCatalog.IsInfinite(r)) n += s.amount;
            }
            return n;
        }

        public static bool Eligible()
        {
            var P = ProfileService.P;
            if (P.coins >= MaxCoins || P.cash >= MaxCash || TotalAmmo() >= MinAmmo) return false;
            return PlayerPrefs.GetString(Key, "") != MetaUI.Today;
        }

        /// <summary>Give the package and show the popup when the player qualifies.</summary>
        public static void Offer()
        {
            if (!Eligible()) return;
            PlayerPrefs.SetString(Key, MetaUI.Today);
            PlayerPrefs.Save();
            var weapon = GameData.Item(GiftWeapon) != null ? GiftWeapon : null;
            Progression.AddCoins(GiftCoins);
            if (weapon != null) ProfileService.P.AddAmmo(weapon, GiftAmmo);
            ProfileService.Save();
            ProfileService.NotifyChanged();
            AudioManager.Sfx("Treasure");
            string gift = GiftCoins + " coins" + (weapon != null ? " and " + GiftAmmo + "x " + Progression.NameOf(weapon) : "");
            UI.Popup(MetaUI.TOr("FREE_AMMO_PACKAGE_HEADER", "Free ammo!"),
                MetaUI.TOr("FREE_AMMO_PACKAGE_TEXT", "Running low? The penguin quartermaster sent you a help package:") + "\n\n" + gift,
                new UI.PopupButton(MetaUI.TOr("BUTTON_MONEY", "Get Cash"), () => ScreenManager.Show(() => new BankScreen()), UI.ButtonStyle.Secondary),
                new UI.PopupButton("OK"));
        }
    }

    /// <summary>Item details popup (name, description, stats) with Buy / Unlock actions.</summary>
    public static class ItemInfo
    {
        public static void Show(Record item, Action changed = null)
        {
            item = ItemCatalog.Real(item);
            if (item == null) return;
            var P = ProfileService.P;
            var win = MetaUI.Window(ItemCatalog.Name(item), new Vector2(1240, 780), out var layer);
            var tile = MetaUI.IconTile(win, ItemCatalog.IconPath(item), ItemCatalog.Name(item));
            UI.Place(tile, new Vector2(0, 1), new Vector2(330, 330), new Vector2(40, -140));

            var info = UI.Rect(win, "Info");
            UI.Anchor(info, 0.32f, 0.2f, 0.97f, 0.84f);
            var v = UI.VBox(info, 8, TextAnchor.UpperLeft);
            v.childForceExpandHeight = false;
            Line(info, ItemCatalog.Description(item), 36, Theme.Text, 110);
            var cats = item.List("Category");
            cats.RemoveAll(c => c == "Practice" || c.StartsWith("Tutorial") || c == "ShopFeatured" || c == "VIP");
            Line(info, (ItemCatalog.IsWeapon(item) ? "Weapon" : "Booster") + (cats.Count > 0 ? "  -  " + string.Join(", ", cats) : ""), 30, Theme.Secondary, 44);
            if (ItemCatalog.IsWeapon(item))
            {
                var st = ItemCatalog.Stats(item);
                if (st.damage > 0) Line(info, Loc.T("WEAPON_MAX_POWER") + ": " + Mathf.RoundToInt(st.damage) + " damage" + (st.projectiles > 1 ? "  (" + st.projectiles + " shots)" : ""), 32, Theme.Text, 44);
                if (st.radius > 0) Line(info, "Blast radius: " + Mathf.RoundToInt(st.radius), 32, Theme.Text, 44);
                Line(info, "Aiming: " + Loc.Prettify(st.targeting), 32, Theme.Text, 44);
            }
            else
            {
                var bonus = item.Ref("StatBonuses");
                if (bonus != null) Line(info, "Effect: " + ClothesCatalog.StatLine(bonus.Id), 32, Theme.Text, 44);
                int turns = item.Int("DurationAmount");
                if (turns > 0) Line(info, "Lasts " + turns + " " + item.Str("DurationType", "turn").ToLowerInvariant() + (turns > 1 ? "s" : ""), 32, Theme.Text, 44);
            }
            Line(info, "Required level: " + ItemCatalog.RequiredLevel(item) + (ItemCatalog.IsVipItem(item) ? "   -   VIP only" : ""), 32, ItemCatalog.IsUnlocked(item) ? Theme.Text : Theme.Danger, 44);
            Line(info, ItemCatalog.IsInfinite(item) ? "Unlimited use" : "You have: " + P.Ammo(item.Id), 32, Theme.Good, 44);
            if (!ItemCatalog.IsInfinite(item))
            {
                // the price with its currency icon (gold coins or green Cash)
                var priceRow = UI.Rect(info, "Price");
                UI.Layout(priceRow, -1, 48);
                UI.HBox(priceRow, 8, TextAnchor.MiddleLeft).childForceExpandWidth = false;
                var pl = UI.Label(priceRow, "Price:", 32, Theme.Text, TextAnchor.MiddleLeft);
                UI.Layout(pl, 110, 44);
                MetaUI.Price(priceRow, ItemCatalog.PriceCoins(item), ItemCatalog.PriceCash(item), 32);
                var per = UI.Label(priceRow, "for x" + ItemCatalog.AmountPurchased(item), 30, Theme.Muted, TextAnchor.MiddleLeft);
                UI.Layout(per, 160, 44);
            }

            var row = UI.Rect(win, "Buttons");
            UI.Anchor(row, 0.05f, 0.03f, 0.95f, 0.18f);
            UI.HBox(row, 24, TextAnchor.MiddleCenter);
            var rec = item;
            if (ItemCatalog.IsInfinite(item)) return;
            if (!ItemCatalog.IsUnlocked(item) && ItemCatalog.UnlockCash(item) > 0)
            {
                var ub = UI.Button(row, Loc.T("UNLOCK") + "  " + ItemCatalog.UnlockCash(item) + " Cash", () => ConfirmSpend(0, ItemCatalog.UnlockCash(rec), Loc.T("UNLOCK") + " " + ItemCatalog.Name(rec),
                    () => { if (ItemCatalog.Unlock(rec)) { MetaUI.Close(layer); changed?.Invoke(); Show(rec, changed); } }), UI.ButtonStyle.Good, 40);
                UI.Layout(ub, 480, 110);
            }
            else if (ItemCatalog.VipBlocked(item))
            {
                var vb = UI.Button(row, "Become VIP", () => { MetaUI.Close(layer); ScreenManager.Show(() => new VipScreen()); }, UI.ButtonStyle.Primary, 40);
                UI.Layout(vb, 420, 110);
            }
            else if (ItemCatalog.IsUnlocked(item))
            {
                int coins = ItemCatalog.PriceCoins(item), cash = ItemCatalog.PriceCash(item);
                string price = cash > 0 ? cash + " Cash" : coins + " coins";
                string what = ItemCatalog.Name(item) + " x";
                var b1 = UI.Button(row, Loc.T("BUY") + " x" + ItemCatalog.AmountPurchased(item) + "  (" + price + ")", () =>
                    ConfirmSpend(coins, cash, what + ItemCatalog.AmountPurchased(rec), () =>
                    {
                        if (ItemCatalog.Buy(rec)) { MetaUI.Close(layer); changed?.Invoke(); Show(rec, changed); }
                    }), UI.ButtonStyle.Good, 36);
                UI.Layout(b1, 520, 110);
                if (ItemCatalog.AmountPurchased(item) > 1 || coins > 0)
                {
                    var b5 = UI.Button(row, Loc.T("BUY") + " x" + ItemCatalog.AmountPurchased(item) * 5, () =>
                        ConfirmSpend(coins * 5, cash * 5, what + ItemCatalog.AmountPurchased(rec) * 5, () =>
                        {
                            if (ItemCatalog.Buy(rec, 5)) { MetaUI.Close(layer); changed?.Invoke(); Show(rec, changed); }
                        }), UI.ButtonStyle.Secondary, 36);
                    UI.Layout(b5, 320, 110);
                }
            }
            else
            {
                var l = UI.Label(row, "Reach level " + ItemCatalog.RequiredLevel(item) + " to buy this.", 36, Theme.Danger);
                UI.Layout(l, 800, 100);
            }
        }

        /// <summary>
        /// Run buy right away for coin prices; ask first when it costs Cash (premium) or when always is set,
        /// so a stray tap on a phone never spends Cash.
        /// </summary>
        public static void ConfirmSpend(int coins, int cash, string what, Action buy, bool always = false)
        {
            if (cash <= 0 && !always) { buy(); return; }
            string price = cash > 0 ? cash + " Cash" : coins > 0 ? coins + " coins" : "nothing";
            UI.Confirm(Loc.T("BUY") + "?", "Get " + what + " for " + price + "?", buy, null, Loc.T("BUY"), "Cancel");
        }

        public static void Line(RectTransform parent, string text, int size, Color color, float height)
        {
            if (string.IsNullOrEmpty(text)) return;
            var l = UI.Label(parent, text, size, color, TextAnchor.UpperLeft);
            UI.Layout(l, -1, height);
        }
    }

    /// <summary>Clothes/trophy details popup with Buy / Unlock / Wear.</summary>
    public static class ClothesInfo
    {
        public static void Show(ClothesDef d, Action changed = null)
        {
            if (d == null) return;
            var name = ClothesCatalog.DisplayName(d.id);
            var win = MetaUI.Window(name, new Vector2(1100, 700), out var layer, d.slot == ClothesSlot.Trophy ? MetaUI.Gold : MetaUI.Pink);
            var tile = MetaUI.IconTile(win, ClothesCatalog.IconPath(d.id), name);
            UI.Place(tile, new Vector2(0, 1), new Vector2(300, 300), new Vector2(40, -140));
            var info = UI.Rect(win, "Info");
            UI.Anchor(info, 0.34f, 0.22f, 0.97f, 0.83f);
            UI.VBox(info, 8, TextAnchor.UpperLeft).childForceExpandHeight = false;
            string slot = d.slot == ClothesSlot.Trophy ? "Trophy" : d.slot == ClothesSlot.Head ? "Hat" : d.slot == ClothesSlot.Chest ? "Outfit" : "Shoes";
            ItemInfo.Line(info, slot + (d.slot != ClothesSlot.Trophy && d.setId != d.id ? "  -  " + d.setName + " set" : ""), 32, Theme.Secondary, 46);
            ItemInfo.Line(info, ClothesCatalog.StatLine(d.id), 36, Theme.Text, 100);
            if (d.slot == ClothesSlot.Trophy)
            {
                foreach (var c in ChallengeCatalog.All)
                    if (c.trophy == d.id) ItemInfo.Line(info, "Earned by completing the challenge \"" + c.name + "\": " + c.Text, 30, Theme.Muted, 90);
            }
            else ItemInfo.Line(info, "Required level: " + d.level + (d.vipOnly ? "  -  VIP only" : ""), 32, ClothesCatalog.IsUnlocked(d) ? Theme.Text : Theme.Danger, 46);

            var row = UI.Rect(win, "Buttons");
            UI.Anchor(row, 0.05f, 0.03f, 0.95f, 0.19f);
            UI.HBox(row, 24, TextAnchor.MiddleCenter);
            Action refresh = () => { MetaUI.Close(layer); changed?.Invoke(); Show(d, changed); };
            if (Progression.OwnsClothes(d.id))
            {
                bool worn = ClothesCatalog.IsWorn(d.id);
                var b = UI.Button(row, worn ? "Take off" : "Wear", () => { ClothesCatalog.ToggleWear(d); MenuScene3D.Celebrate(); refresh(); }, worn ? UI.ButtonStyle.Secondary : UI.ButtonStyle.Good, 40);
                UI.Layout(b, 360, 110);
            }
            else if (d.slot != ClothesSlot.Trophy)
            {
                if (!ClothesCatalog.IsUnlocked(d))
                {
                    var u = UI.Button(row, Loc.T("UNLOCK") + "  " + ClothesCatalog.UnlockCash(d) + " Cash", () => ItemInfo.ConfirmSpend(0, ClothesCatalog.UnlockCash(d), Loc.T("UNLOCK") + " " + name, () => { if (ClothesCatalog.Unlock(d)) refresh(); }), UI.ButtonStyle.Good, 38);
                    UI.Layout(u, 440, 110);
                }
                else
                {
                    string price = d.cash > 0 ? d.cash + " Cash" : d.coins + " coins";
                    var b = UI.Button(row, Loc.T("BUY") + "  (" + price + ")", () => ItemInfo.ConfirmSpend(d.coins, d.cash, name, () => { if (ClothesCatalog.Buy(d)) refresh(); }), UI.ButtonStyle.Good, 38);
                    UI.Layout(b, 440, 110);
                }
            }
        }
    }
}
