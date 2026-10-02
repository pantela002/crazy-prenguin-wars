using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// Buying ammo during a battle (original ServerServices in-battle weapon list): shop weapons without ammo show in
    /// the weapon menu as greyed cells with their price; tapping one buys a pack (ItemCatalog.Buy) into the profile and
    /// selects it. Only when the penguin's ammo is the profile's (Quick Match, Online), never in Practice/Custom/tutorial.
    /// Online the ammo stays local: other devices replay our "weapon"/"fire" actions without checking our counts.
    /// </summary>
    public partial class BattleHUD
    {
        static readonly Color BuyTint = new Color(0.55f, 0.55f, 0.6f, 1f);

        bool CanBuyInBattle(Penguin a)
        {
            if (a == null || !a.Ammo.UsesProfile || !c.IsLocalHuman(a.PlayerIndex)) return false;
            var m = c.Config.mode;
            return m == BattleMode.QuickMatch || m == BattleMode.Online;
        }

        /// <summary>The Item record if this id can be bought in the shop at all (has a price, not Punch-like infinite).</summary>
        static Record ShopRecord(string id)
        {
            var r = GameData.Item(id);
            if (r == null || ItemCatalog.IsInfinite(r) || ItemCatalog.PriceRecord(r) == null) return null;
            return ItemCatalog.PriceCoins(r) > 0 || ItemCatalog.PriceCash(r) > 0 ? r : null;
        }

        /// <summary>Shop weapons of a tab the penguin doesn't have in its list yet. Returns how many cells were added.</summary>
        int AddShopCells(RectTransform grid, Penguin a, string tab)
        {
            int n = 0;
            foreach (var r in ItemCatalog.ShopItems("Weapon", tab))
            {
                if (a.Ammo.Weapons.Contains(r.Id) || ShopRecord(r.Id) == null) continue;
                if (ItemCatalog.VipBlocked(r)) continue;   // VIP needs the bank screen: not offered mid-battle
                BuyCell(grid, a, r.Id);
                n++;
            }
            return n;
        }

        /// <summary>Greyed cell with the pack price (or the unlock level). Tapping buys one pack.</summary>
        void BuyCell(RectTransform grid, Penguin a, string id)
        {
            var r = ShopRecord(id);
            bool unlocked = r != null && ItemCatalog.IsUnlocked(r);
            int coins = r != null ? ItemCatalog.PriceCoins(r) : 0, cash = r != null ? ItemCatalog.PriceCash(r) : 0;
            string price = !unlocked ? "Lv " + ItemCatalog.RequiredLevel(r) : (cash > 0 ? cash + " fish" : UI.Money(coins));
            var b = ItemCell(grid, id, BattleItems.Icon(id), "", false, unlocked, () => BuyInBattle(a, id));
            // greyed icon, price tag instead of the count
            foreach (var img in b.GetComponentsInChildren<Image>(true))
                if (img.gameObject != b.gameObject) img.color *= BuyTint;
            var tag = UI.Panel(b.transform, cash > 0 ? new Color(0.1f, 0.45f, 0.2f, 0.92f) : new Color(0.45f, 0.32f, 0.02f, 0.92f), true, "Price");
            tag.raycastTarget = false;
            UI.Anchor(tag.rectTransform, 0.3f, 0.02f, 0.98f, 0.27f);
            var pl = UI.Label(tag.transform, unlocked ? Loc.T("BUY") + " " + price : price, 24, unlocked ? (cash > 0 ? Theme.Cash : Theme.Coin) : Color.white, TextAnchor.MiddleCenter, true);
            UI.Stretch(pl.rectTransform, 6, 6, 2, 2);
        }

        void BuyInBattle(Penguin a, string id)
        {
            var r = ShopRecord(id);
            if (r == null || a == null) return;
            if (!ItemCatalog.IsUnlocked(r)) { UI.Toast("Reach level " + ItemCatalog.RequiredLevel(r) + " to unlock this."); return; }
            int coins = ItemCatalog.PriceCoins(r), cash = ItemCatalog.PriceCash(r);
            if (!Progression.CanAfford(coins, cash))
            {
                // Progression.NotEnough would offer the bank screen, which can't open over a battle
                AudioManager.Sfx("Nomoney");
                UI.Toast(cash > 0 ? "Not enough fish" : (Loc.Has("NOT_ENOUGH_COINS_POP_UP_TITLE") ? Loc.T("NOT_ENOUGH_COINS_POP_UP_TITLE") : "Not enough coins"), Theme.Danger);
                return;
            }
            System.Action buy = () =>
            {
                if (!ItemCatalog.Buy(r)) return;
                a.Ammo.AddProfileItem(id);
                // select it right away if it is still this player's turn and the weapon may change
                if (c.IsInputTurn && c.Active == a && !c.Fired) c.ActSelectWeapon(id);
                CloseTransientPanels();
            };
            // premium currency always asks first; coins buy on the tap like the original Buy button
            if (cash > 0)
                UI.Confirm(Loc.T("BUY") + " " + BattleItems.Name(id),
                    "x" + ItemCatalog.AmountPurchased(r) + " for " + cash + " fish?", buy);
            else buy();
        }
    }
}
