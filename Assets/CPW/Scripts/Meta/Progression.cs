using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// Wallet, XP/level-ups, VIP state and small profile helpers shared by all menu features.
    /// Every method changes ProfileService.P; callers decide when to Save() (most do it right away).
    /// </summary>
    public static class Progression
    {
        static PlayerProfile P => ProfileService.P;

        /// <summary>Levels reached but whose popup was not shown yet (shown by ShowPendingLevelUps).</summary>
        static readonly List<int> pendingLevelUps = new List<int>();

        // ---------- money ----------
        public static bool CanAfford(int coins, int cash) => P.coins >= coins && P.cash >= cash;

        /// <summary>Spend coins and/or cash. Returns false (and changes nothing) if the player can't afford it.</summary>
        public static bool Spend(int coins, int cash)
        {
            if (!CanAfford(coins, cash)) return false;
            P.coins -= coins;
            P.cash -= cash;
            if (cash > 0)
            {
                P.AddCounter("Use_X_Fish", cash);
                ChallengeTracker.Report("fishSpent", cash);
            }
            return true;
        }

        public static void AddCoins(int amount) { if (amount != 0) P.coins = Mathf.Max(0, P.coins + amount); }
        public static void AddCash(int amount) { if (amount != 0) P.cash = Mathf.Max(0, P.cash + amount); }

        /// <summary>Offer the bank when the player lacks money for something.</summary>
        public static void NotEnough(bool cash)
        {
            UI.Confirm(cash ? "Need more fish?" : Loc.T("NOT_ENOUGH_COINS_POP_UP_TITLE"),
                cash ? "You don't have enough fish for this. Visit the bank?" : Loc.T("NOT_ENOUGH_COINS_POP_UP_MESSAGE"),
                () => ScreenManager.Show(() => new BankScreen()), null, Loc.T("BUTTON_YES_PLEASE"), Loc.T("BUTTON_LATER"));
        }

        // ---------- xp / levels ----------
        /// <summary>Add XP; level-ups grant the Experience.PCReward cash and queue a popup. Returns levels gained.</summary>
        public static int AddXp(int amount)
        {
            if (amount <= 0) return 0;
            int before = P.level;
            P.xp += amount;
            int after = Mathf.Max(before, GameData.LevelForXp(P.xp));
            for (int lv = before + 1; lv <= after; lv++)
            {
                var r = GameData.Get("Experience", lv.ToString());
                if (r != null) AddCash(r.Int("PCReward"));
                pendingLevelUps.Add(lv);
            }
            P.level = after;
            if (after > before) ChallengeTracker.Report("level", 0);
            return after - before;
        }

        /// <summary>XP progress inside the current level, 0..1.</summary>
        public static float LevelProgress(int xp, int level)
        {
            int a = GameData.XpForLevel(level), b = GameData.XpForLevel(level + 1);
            if (b == int.MaxValue || b <= a) return 1f;
            return Mathf.Clamp01((xp - a) / (float)(b - a));
        }

        public static bool HasPendingLevelUps => pendingLevelUps.Count > 0;

        /// <summary>Parameterless form for button/popup callbacks.</summary>
        public static void ShowPendingLevelUpsAction() => ShowPendingLevelUps();

        /// <summary>Show queued level-up popups one after another (then call done).</summary>
        public static void ShowPendingLevelUps(Action done = null)
        {
            if (pendingLevelUps.Count == 0) { done?.Invoke(); return; }
            int lv = pendingLevelUps[0];
            pendingLevelUps.RemoveAt(0);
            LevelUpPopup.Show(lv, () => ShowPendingLevelUps(done));
        }

        // ---------- VIP ----------
        /// <summary>VIP is active (membership time left). Keeps the profile flag in sync.</summary>
        public static bool IsVip
        {
            get
            {
                bool v = P.vipUntilUnixMs > MetaUI.NowMs;
                if (P.vip != v) P.vip = v;
                return v;
            }
        }

        public static void AddVipDays(int days)
        {
            long start = Math.Max(P.vipUntilUnixMs, MetaUI.NowMs);
            P.vipUntilUnixMs = start + days * 86400000L;
            P.vip = true;
        }

        // ---------- inventory ----------
        public static bool OwnsClothes(string id) => P.ownedClothes.Contains(id) || P.trophies.Contains(id);

        public static void GiveClothes(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (ClothesCatalog.IsTrophy(id)) { if (!P.trophies.Contains(id)) P.trophies.Add(id); }
            else if (!P.ownedClothes.Contains(id)) P.ownedClothes.Add(id);
        }

        /// <summary>
        /// Give any reward id: weapon/booster ammo (Item), clothes/trophy (Bonus) or crafting ingredient.
        /// </summary>
        public static void GiveItem(string id, int amount)
        {
            if (string.IsNullOrEmpty(id) || amount <= 0) return;
            if (CraftingCatalog.IsIngredient(id)) { CraftingCatalog.AddIngredient(id, amount); return; }
            if (GameData.Item(id) != null) { P.AddAmmo(id, amount); return; }
            if (GameData.Get("Bonus", id) != null) { GiveClothes(id); return; }
            P.AddAmmo(id, amount);
        }

        /// <summary>Human readable name of any reward id.</summary>
        public static string NameOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            if (CraftingCatalog.IsIngredient(id)) return CraftingCatalog.Ingredient(id).name;
            var it = GameData.Item(id);
            if (it != null) return Loc.T(it.Str("Name", id));
            return ClothesCatalog.DisplayName(id);
        }

        /// <summary>Icon path of any reward id (see ModelLibrary.Icon naming).</summary>
        public static string IconOf(string id)
        {
            if (CraftingCatalog.IsIngredient(id)) return "Crafting/" + id;
            var it = GameData.Item(id);
            if (it != null) return ItemCatalog.IconPath(it);
            return ClothesCatalog.IconPath(id);
        }
    }

    /// <summary>
    /// "Level Up!" popup like the original LevelUpPopUpScreen: the level badge, the Experience.PCReward cash, the
    /// loot container of weapons/boosters/clothes unlocked at the new level (tap one for details) and a
    /// LevelUpSalesSlot style deal on one of them. The original ItemLevelUpSales table did not ship, so the deal is
    /// INVENTED: the first buyable unlock at 30% off (one pack), bought right here or looked up in the shop.
    /// Closing it any way (OK, X, Android back) continues with the next queued level.
    /// </summary>
    public static class LevelUpPopup
    {
        const float SaleFactor = 0.7f;

        public static void Show(int level, Action closed = null)
        {
            var items = ItemCatalog.UnlockedAtLevel(level);
            var clothes = ClothesCatalog.UnlockedAtLevel(level);
            bool loot = items.Count + clothes.Count > 0;
            var win = MetaUI.Window(Loc.T("LEVEL_UP_TITLE"), loot ? new Vector2(1400, 860) : new Vector2(1100, 760), out var layer, MetaUI.Gold);
            layer.gameObject.AddComponent<PopupClosed>().onClosed = closed;
            float cx = loot ? 0.17f : 0.5f;
            var rays = UI.Image(win, UI.Circle, new Color(1, 0.85f, 0.3f, 0.35f), false, "Glow");
            UI.Place(rays.rectTransform, new Vector2(cx, 0.62f), new Vector2(loot ? 360 : 420, loot ? 360 : 420), Vector2.zero);
            rays.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rays.gameObject.AddComponent<UIPulse>().amount = 0.08f;
            var badge = MetaUI.Badge(win, level.ToString(), Theme.Secondary, loot ? 220 : 260);
            var brt = (RectTransform)badge.transform.parent;
            brt.anchorMin = brt.anchorMax = new Vector2(cx, 0.62f);
            brt.anchoredPosition = Vector2.zero;

            var r = GameData.Get("Experience", level.ToString());
            int cash = r != null ? r.Int("PCReward") : 0;
            if (cash > 0)
            {
                var row = UI.Rect(win, "Reward");
                if (loot) UI.Anchor(row, 0.02f, 0.3f, 0.32f, 0.4f);
                else UI.Anchor(row, 0.3f, 0.17f, 0.7f, 0.26f);
                UI.HBox(row, 6, TextAnchor.MiddleCenter).childForceExpandWidth = false;
                MetaUI.Amount(row, "cash", "+" + cash, 48, Theme.Good);
            }

            Action close = () => MetaUI.Close(layer);
            if (!loot)
            {
                var desc = UI.Label(win, MetaUI.TOr("NEW_LEVEL_NO_UNLOCK", "Well done! You reached level " + level + "! Keep fighting to unlock new gear."), 40, Theme.Text);
                UI.Anchor(desc.rectTransform, 0.06f, 0.26f, 0.94f, 0.4f);
            }
            else
            {
                var desc = UI.Label(win, MetaUI.Fmt("LEVELUP_DESCRIPTION", level), 38, Theme.Text, TextAnchor.MiddleLeft);
                UI.Anchor(desc.rectTransform, 0.33f, 0.77f, 0.97f, 0.86f);
                var strip = UI.Panel(win, Theme.PanelInner, true, "Unlocked");
                UI.Anchor(strip.rectTransform, 0.33f, 0.44f, 0.97f, 0.76f);
                var sr = UI.ScrollList(strip.transform, out var list, false, 14, 10);
                UI.Stretch((RectTransform)sr.transform, 4, 4, 4, 4);
                foreach (var it in items)
                {
                    var rec = it;
                    LootTile(list, ItemCatalog.IconPath(it), ItemCatalog.Name(it), () => ItemInfo.Show(rec));
                }
                foreach (var c in clothes)
                {
                    var def = c;
                    LootTile(list, ClothesCatalog.IconPath(c.id), ClothesCatalog.DisplayName(c.id), () => ClothesInfo.Show(def));
                }
                Sale(win, items, clothes, close);
            }
            var ok = UI.Button(win, Loc.T("LEVELUP_OKBUTTON"), close, UI.ButtonStyle.Primary, 48);
            UI.Place((RectTransform)ok.transform, new Vector2(cx, 0), new Vector2(380, 104), new Vector2(0, 24));
            ((RectTransform)ok.transform).pivot = new Vector2(0.5f, 0);
            AudioManager.Sfx("LevelUpSound");
        }

        static void LootTile(RectTransform list, string icon, string name, Action onClick)
        {
            var b = UI.Button(list, null, onClick, UI.ButtonStyle.Plain, 24, "Loot " + name);
            UI.SkinColor(b.GetComponent<Image>(), MetaUI.Card);
            UI.Layout(b, 190, -1);
            var tile = MetaUI.IconTile(MetaUI.Box(b.transform, 0.08f, 0.3f, 0.92f, 0.96f), icon, name);
            MetaUI.Square(tile);
            var l = UI.Label(b.transform, name, 24, Theme.Text);
            UI.Anchor(l.rectTransform, 0.03f, 0.02f, 0.97f, 0.3f);
        }

        /// <summary>One discounted pack of the first unlock the player may buy (weapons/boosters first, then clothes).</summary>
        static void Sale(RectTransform win, List<Record> items, List<ClothesDef> clothes, Action closePopup)
        {
            Record item = null; ClothesDef cloth = null;
            foreach (var it in items)
                if (!ItemCatalog.VipBlocked(it) && ItemCatalog.PriceCoins(it) + ItemCatalog.PriceCash(it) > 0) { item = it; break; }
            if (item == null)
                foreach (var c in clothes)
                    if (!Progression.OwnsClothes(c.id) && !(c.vipOnly && !Progression.IsVip) && c.coins + c.cash > 0) { cloth = c; break; }
            if (item == null && cloth == null) return;

            int coins = item != null ? ItemCatalog.PriceCoins(item) : cloth.coins;
            int cash = item != null ? ItemCatalog.PriceCash(item) : cloth.cash;
            bool premium = cash > 0;
            int oldPrice = premium ? cash : coins;
            int newPrice = Mathf.Max(1, Mathf.RoundToInt(oldPrice * SaleFactor));
            string name = item != null ? ItemCatalog.Name(item) + " x" + ItemCatalog.AmountPurchased(item) : ClothesCatalog.DisplayName(cloth.id);

            var card = MetaUI.CardPanel(win, new Color32(255, 246, 220, 255), "Sale");
            UI.Anchor(card.rectTransform, 0.33f, 0.155f, 0.97f, 0.42f);
            var tile = MetaUI.IconTile(MetaUI.Box(card.transform, 0.01f, 0.06f, 0.2f, 0.94f), item != null ? ItemCatalog.IconPath(item) : ClothesCatalog.IconPath(cloth.id), name);
            MetaUI.Square(tile);
            var tag = UI.Label(card.transform, "Level-up deal  -" + Mathf.RoundToInt((1 - SaleFactor) * 100) + "%", 32, MetaUI.Orange, TextAnchor.MiddleLeft, true);
            UI.Anchor(tag.rectTransform, 0.21f, 0.62f, 0.62f, 0.95f);
            var nl = UI.Label(card.transform, name, 30, Theme.Text, TextAnchor.MiddleLeft);
            UI.Anchor(nl.rectTransform, 0.21f, 0.35f, 0.62f, 0.62f);
            var was = UI.Label(card.transform, "was " + oldPrice + (premium ? " fish" : " coins"), 24, Theme.Muted, TextAnchor.MiddleLeft);
            UI.Anchor(was.rectTransform, 0.21f, 0.05f, 0.4f, 0.35f);
            var now = UI.Rect(card.transform, "Price");
            UI.Anchor(now, 0.4f, 0.05f, 0.62f, 0.35f);
            UI.HBox(now, 6, TextAnchor.MiddleLeft).childForceExpandWidth = false;
            MetaUI.Price(now, premium ? 0 : newPrice, premium ? newPrice : 0, 32, Theme.Good);

            Button buy = null;
            buy = UI.Button(card.transform, Loc.T("BUY"), () =>
            {
                if (!BuyDeal(item, cloth, premium ? 0 : newPrice, premium ? newPrice : 0)) return;
                buy.interactable = false;
                var t = buy.GetComponentInChildren<Text>();
                if (t) t.text = "Bought!";
            }, UI.ButtonStyle.Good, 36);
            UI.Anchor((RectTransform)buy.transform, 0.64f, 0.52f, 0.98f, 0.94f);
            var shop = UI.Button(card.transform, Loc.T("BUTTON_SUPPLIES"), () =>
            {
                closePopup();
                if (item != null)
                {
                    ScreenManager.Show(() => new ShopScreen(ItemCatalog.IsBooster(item) ? 3 : 2));
                    ItemInfo.Show(item, ScreenManager.Refresh);
                }
                else
                {
                    ScreenManager.Show(() => new ShopScreen(4, (int)cloth.slot));
                    ClothesInfo.Show(cloth, ScreenManager.Refresh);
                }
            }, UI.ButtonStyle.Secondary, 32);
            UI.Anchor((RectTransform)shop.transform, 0.64f, 0.06f, 0.98f, 0.46f);
        }

        static bool BuyDeal(Record item, ClothesDef cloth, int coins, int cash)
        {
            if (cloth != null && Progression.OwnsClothes(cloth.id)) return false;
            if (!Progression.Spend(coins, cash))
            {
                AudioManager.Sfx("Nomoney");
                Progression.NotEnough(cash > 0);
                return false;
            }
            if (item != null)
            {
                int amount = ItemCatalog.AmountPurchased(item);
                ProfileService.P.AddAmmo(item.Id, amount);
                UI.Toast("+" + amount + " " + ItemCatalog.Name(item), Theme.Good);
            }
            else
            {
                Progression.GiveClothes(cloth.id);
                UI.Toast(ClothesCatalog.DisplayName(cloth.id) + " is yours!", Theme.Good);
            }
            ChallengeTracker.Report("shopBuys", 1);
            ProfileService.Save();
            AudioManager.Sfx("Buy");
            return true;
        }
    }

    /// <summary>
    /// Runs a callback once when its popup layer is destroyed, whether by its own button, the X or Android back
    /// (ScreenManager destroys the top popup), but not while the app is quitting.
    /// </summary>
    public class PopupClosed : MonoBehaviour
    {
        public Action onClosed;
        static bool quitting;

        [RuntimeInitializeOnLoadMethod]
        static void Init() => Application.quitting += () => quitting = true;

        void OnDestroy()
        {
            if (quitting || onClosed == null) return;
            var a = onClosed;
            onClosed = null;
            a();
        }
    }
}
