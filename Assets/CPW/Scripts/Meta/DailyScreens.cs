using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// Daily News (original dailynews screen with news, a sale and links to gifts/slot machine) plus the daily gift.
    /// INVENTED DATA: the 7-day gift calendar below (the original gifts came from Facebook friends). Claiming on
    /// consecutive days grows the streak; missing a day restarts it. VIPs get double coins.
    /// </summary>
    public class DailyScreen : MetaScreen
    {
        protected override string Title => Loc.T("BUTTON_NEWS");

        struct Gift { public int coins, cash; public string item; public int amount; }
        static readonly Gift[] Calendar =
        {
            new Gift { coins = 150 }, new Gift { coins = 250 }, new Gift { cash = 2 }, new Gift { coins = 400 },
            new Gift { item = "Shield", amount = 2 }, new Gift { coins = 600 }, new Gift { cash = 5, item = "@rare", amount = 1 }
        };

        public static bool CanClaim => ProfileService.P.lastDailyGiftDay != MetaUI.Today;

        /// <summary>Streak day (1..7) that claiming today would give.</summary>
        static int NextDay
        {
            get
            {
                var P = ProfileService.P;
                string yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
                if (P.lastDailyGiftDay == MetaUI.Today) return Mathf.Clamp(P.dailyStreak, 1, 7);
                return P.lastDailyGiftDay == yesterday ? P.dailyStreak % 7 + 1 : 1;
            }
        }

        protected override void BuildContent()
        {
            // ---- gift calendar ----
            var gift = MetaUI.CardPanel(Content, MetaUI.Pink, "Gift");
            UI.Anchor(gift.rectTransform, 0, 0.48f, 1, 1);
            var gt = UI.Label(gift.transform, "Daily gift", 46, Color.white, TextAnchor.MiddleLeft, true);
            UI.Anchor(gt.rectTransform, 0.02f, 0.82f, 0.5f, 0.98f);
            var row = UI.Rect(gift.transform, "Days");
            UI.Anchor(row, 0.01f, 0.05f, 0.8f, 0.8f);
            var h = UI.HBox(row, 12, TextAnchor.MiddleCenter);
            h.childForceExpandWidth = true; h.childForceExpandHeight = true;
            int today = NextDay;
            bool can = CanClaim;
            for (int d = 1; d <= 7; d++)
            {
                var g = Calendar[d - 1];
                bool past = d < today || (d == today && !can);
                var cell = UI.Panel(row, d == today && can ? Theme.Primary : past ? new Color(1, 1, 1, 0.5f) : Color.white, true, "Day " + d);
                if (d == today && can) cell.gameObject.AddComponent<UIPulse>().amount = 0.04f;
                var dl = UI.Label(cell.transform, "Day " + d, 28, Theme.Text, TextAnchor.MiddleCenter, true);
                dl.color = Theme.Secondary;
                UI.Anchor(dl.rectTransform, 0, 0.78f, 1, 0.98f);
                string kind = g.cash > 0 ? "cash" : g.coins > 0 ? "coin" : null;
                if (kind != null)
                {
                    var ic = MetaUI.CurrencyIcon(MetaUI.Box(cell.transform, 0.25f, 0.34f, 0.75f, 0.76f), kind);
                    MetaUI.Square(ic);
                }
                else
                {
                    var tile = MetaUI.IconTile(MetaUI.Box(cell.transform, 0.2f, 0.34f, 0.8f, 0.76f), Progression.IconOf(g.item), Progression.NameOf(g.item));
                    MetaUI.Square(tile);
                }
                var al = UI.Label(cell.transform, GiftText(g), 24, Theme.Text);
                UI.Anchor(al.rectTransform, 0.02f, 0.03f, 0.98f, 0.33f);
                if (past)
                {
                    var tick = UI.Label(cell.transform, "DONE", 40, Theme.Good, TextAnchor.MiddleCenter, true);
                    UI.Stretch(tick.rectTransform);
                }
            }
            var claim = UI.Button(gift.transform, can ? "Claim!" : "Come back tomorrow", Claim, can ? UI.ButtonStyle.Good : UI.ButtonStyle.Dark, can ? 48 : 30);
            UI.Anchor((RectTransform)claim.transform, 0.82f, 0.2f, 0.985f, 0.65f);
            claim.interactable = can;

            // ---- news ----
            var news = MetaUI.CardPanel(Content, MetaUI.Card, "News");
            UI.Anchor(news.rectTransform, 0, 0, 0.62f, 0.46f);
            var nv = UI.VBox(news.rectTransform, 6, TextAnchor.UpperLeft, 22);
            nv.childForceExpandHeight = false;
            var nt = UI.Label(news.transform, "Penguin Times", 40, Theme.Secondary, TextAnchor.MiddleLeft, true);
            UI.Layout(nt, -1, 56);
            foreach (var line in Headlines())
            {
                var l = UI.Label(news.transform, "-  " + line, 30, Theme.Text, TextAnchor.MiddleLeft);
                UI.Layout(l, -1, 52);
            }
            var links = UI.Rect(news.transform, "Links");
            UI.Layout(links, -1, 90);
            UI.HBox(links, 16, TextAnchor.MiddleLeft);
            UI.Layout(UI.Button(links, "Slot machine", () => ScreenManager.Show(() => new SlotMachineScreen()), UI.ButtonStyle.Secondary, 32), 300, 80);
            UI.Layout(UI.Button(links, "Challenges", () => ScreenManager.Show(() => new AchievementsScreen()), UI.ButtonStyle.Secondary, 32), 300, 80);

            // ---- deal of the day ----
            DealOfTheDay(Content);
        }

        static string GiftText(Gift g)
        {
            var s = "";
            if (g.coins > 0) s += g.coins + " coins";
            if (g.cash > 0) s += (s.Length > 0 ? " + " : "") + g.cash + " cash";
            if (!string.IsNullOrEmpty(g.item)) s += (s.Length > 0 ? " + " : "") + (g.item == "@rare" ? "rare ingredient" : g.amount + "x " + Progression.NameOf(g.item));
            return s;
        }

        void Claim()
        {
            if (!CanClaim) return;
            var P = ProfileService.P;
            int day = NextDay;
            var g = Calendar[day - 1];
            int coins = g.coins * (Progression.IsVip ? 2 : 1);
            Progression.AddCoins(coins);
            Progression.AddCash(g.cash);
            string item = g.item == "@rare" ? CraftingCatalog.RandomIngredient(true) : g.item;
            if (!string.IsNullOrEmpty(item)) Progression.GiveItem(item, g.amount);
            P.dailyStreak = day;
            P.lastDailyGiftDay = MetaUI.Today;
            ProfileService.Save();
            AudioManager.Sfx("Treasure");
            UI.Message("Daily gift", "Day " + day + ": " + GiftText(new Gift { coins = coins, cash = g.cash, item = item, amount = g.amount }) + "!\nSee you tomorrow!", ScreenManager.Refresh);
        }

        static List<string> Headlines()
        {
            var list = new List<string>
            {
                "Free spins at the slot machine every day: " + SlotMachineLogic.FreeSpinsLeft() + " left today.",
                "Scientists confirm: crafting labs now 87% less explodey. Try a new recipe!",
                "Fashion alert: " + ClothesCatalog.BySlot(ClothesSlot.Head).Count + " hats in the shop. Look sharp, fight sharp.",
                Tips.Random()
            };
            if (CraftingCatalog.ReadyCount() > 0) list.Insert(0, "Your research is ready to collect!");
            return list;
        }

        /// <summary>One item at 25% off each day (picked from the original BettingShopItems / AfterResultsSalesWinner lists).</summary>
        static void DealOfTheDay(RectTransform parent)
        {
            var pool = new List<Record>();
            foreach (var r in GameData.Section("BettingShopItems").Values) { var it = r.Ref("Item"); if (it != null && !pool.Contains(it)) pool.Add(it); }
            foreach (var r in GameData.Section("AfterResultsSalesWinner").Values) { var it = GameData.Item(r.Str("ItemId")); if (it != null && !pool.Contains(it)) pool.Add(it); }
            pool.RemoveAll(r => ItemCatalog.PriceRecord(r) == null || !ItemCatalog.IsUnlocked(r) || ItemCatalog.VipBlocked(r));
            var card = MetaUI.CardPanel(parent, MetaUI.Orange, "Deal");
            UI.Anchor(card.rectTransform, 0.64f, 0, 1, 0.46f);
            var t = UI.Label(card.transform, "Deal of the day  -25%", 38, Color.white, TextAnchor.MiddleCenter, true);
            UI.Anchor(t.rectTransform, 0.03f, 0.83f, 0.97f, 0.98f);
            if (pool.Count == 0) return;
            var item = pool[(DateTime.Now.DayOfYear * 7) % pool.Count];
            var tile = MetaUI.IconTile(MetaUI.Box(card.transform, 0.05f, 0.25f, 0.42f, 0.8f), ItemCatalog.IconPath(item), ItemCatalog.Name(item));
            MetaUI.Square(tile);
            var n = UI.Label(card.transform, ItemCatalog.AmountPurchased(item) + "x " + ItemCatalog.Name(item), 34, Color.white, TextAnchor.MiddleLeft, true);
            UI.Anchor(n.rectTransform, 0.45f, 0.55f, 0.97f, 0.8f);
            int coins = Mathf.CeilToInt(ItemCatalog.PriceCoins(item) * 0.75f), cash = Mathf.CeilToInt(ItemCatalog.PriceCash(item) * 0.75f);
            string key = "deal." + MetaUI.Today;
            bool bought = ProfileService.P.Counter(key) > 0;
            var b = UI.Button(card.transform, bought ? "Sold out" : Loc.T("BUY") + "  " + (cash > 0 ? cash + " cash" : coins + " coins"), () =>
            {
                if (ProfileService.P.Counter(key) > 0) return;
                if (!Progression.Spend(coins, cash)) { Progression.NotEnough(cash > 0); return; }
                ProfileService.P.AddAmmo(item.Id, ItemCatalog.AmountPurchased(item));
                ProfileService.P.AddCounter(key, 1);
                ProfileService.Save();
                AudioManager.Sfx("Buy");
                ScreenManager.Refresh();
            }, UI.ButtonStyle.Good, 32);
            UI.Anchor((RectTransform)b.transform, 0.45f, 0.25f, 0.97f, 0.5f);
            b.interactable = !bought;
            // plain light text was hard to read on the orange paper: a dark backing strip and outlined white text
            var back = UI.PanelRaw(card.transform, new Color32(70, 30, 0, 170), true, "DescriptionBack");
            back.raycastTarget = false;
            UI.Anchor(back.rectTransform, 0.03f, 0.03f, 0.97f, 0.235f);
            var d = UI.Label(back.transform, ItemCatalog.Description(item), 24, Color.white);
            UI.Stretch(d.rectTransform, 12, 12, 4, 4);
            MetaUI.Outlined(d, new Color32(60, 24, 0, 255), 1.5f);
        }
    }

    /// <summary>
    /// VIP membership. Price from VIP.Default.VIPPrice (10 fish); bonuses from BattleOptions (XPVIPMultiplier,
    /// GCVIPMultiplier). INVENTED: durations (3 days for VIPPrice, 14 days for 4x, 30 days for 7.5x) since the
    /// VIPPrice table was missing; VIP also unlocks VIP-only weapons and the King outfit, and doubles daily-gift coins.
    /// </summary>
    public class VipScreen : MetaScreen
    {
        protected override string Title => Loc.T("BUTTON_VIP");

        protected override void BuildContent()
        {
            var P = ProfileService.P;
            var vip = GameData.Get("VIP", "Default");
            int basePrice = vip?.Int("VIPPrice", 10) ?? 10;
            var opt = GameData.Battle;
            float xpMul = opt?.Float("XPVIPMultiplier", 1.5f) ?? 1.5f, gcMul = opt?.Float("GCVIPMultiplier", 1.5f) ?? 1.5f;

            var info = MetaUI.CardPanel(Content, MetaUI.Gold, "Info");
            UI.Anchor(info.rectTransform, 0, 0.42f, 1, 1);
            var crown = MetaUI.IconTile(info.transform, "Ui/vip", "VIP", new Color(1, 0.7f, 0.1f));
            UI.Place(crown, new Vector2(0, 0.5f), new Vector2(260, 260), new Vector2(40, 0));
            var status = UI.Label(info.transform, Progression.IsVip ? "You are a VIP!  Time left: " + MetaUI.Clock((P.vipUntilUnixMs - MetaUI.NowMs) / 1000.0) : Loc.T("VIP_MEMBERSHIP_SUBSCRIPTION_INACTIVE"), 40, Theme.PrimaryText, TextAnchor.UpperLeft, true);
            UI.Anchor(status.rectTransform, 0.2f, 0.72f, 0.98f, 0.95f);
            var perks = "+  x" + xpMul.ToString("0.#") + " XP from every match\n" +
                        "+  x" + gcMul.ToString("0.#") + " coins from every match\n" +
                        "+  VIP-only weapons (Mini-Bazooka, Sniper Rifle, Impact Cannon, Napalm, Cinder Grenade)\n" +
                        "+  The royal King outfit and double daily-gift coins";
            var pl = UI.Label(info.transform, perks, 34, Theme.PrimaryText, TextAnchor.UpperLeft);
            UI.Anchor(pl.rectTransform, 0.2f, 0.05f, 0.98f, 0.7f);

            var row = UI.Rect(Content, "Packs");
            UI.Anchor(row, 0.05f, 0, 0.95f, 0.38f);
            var h = UI.HBox(row, 30, TextAnchor.MiddleCenter);
            h.childForceExpandWidth = true; h.childForceExpandHeight = true;
            Pack(row, 3, basePrice);
            Pack(row, 14, basePrice * 4);
            Pack(row, 30, Mathf.RoundToInt(basePrice * 7.5f));
        }

        void Pack(RectTransform parent, int days, int price)
        {
            var card = MetaUI.CardPanel(parent, MetaUI.Card);
            var t = UI.Label(card.transform, days + " days", 52, Theme.Secondary, TextAnchor.MiddleCenter, true);
            UI.Anchor(t.rectTransform, 0, 0.6f, 1, 0.95f);
            var p = UI.Rect(card.transform, "Price");
            UI.Anchor(p, 0, 0.38f, 1, 0.6f);
            UI.HBox(p, 6, TextAnchor.MiddleCenter).childForceExpandWidth = false;
            MetaUI.Amount(p, "cash", price.ToString(), 44);
            var b = UI.Button(card.transform, Progression.IsVip ? "Extend" : "Join", () =>
            {
                if (!Progression.Spend(0, price)) { Progression.NotEnough(true); return; }
                Progression.AddVipDays(days);
                ProfileService.Save();
                AudioManager.Sfx("Unlock");
                UI.Message("Welcome, VIP!", "Enjoy " + days + " days of VIP perks.", ScreenManager.Refresh);
            }, UI.ButtonStyle.Primary, 44);
            UI.Anchor((RectTransform)b.transform, 0.12f, 0.06f, 0.88f, 0.32f);
        }
    }

    /// <summary>
    /// Bank (original money screen): exchange fish for coins with the GCPackage offers; the real-money PCPackage
    /// fish packs are listed but show "store not connected" since the remake has no in-app purchases.
    /// </summary>
    public class BankScreen : MetaScreen
    {
        static int tab;
        RectTransform body;
        List<Button> tabs;
        protected override string Title => Loc.T("BUTTON_MONEY");

        public BankScreen() { }
        public BankScreen(int startTab) { tab = startTab; }

        protected override void BuildContent()
        {
            var row = UI.Rect(Content, "Tabs");
            UI.Anchor(row, 0, 0.89f, 1, 1);
            tabs = MetaUI.Tabs(row, new[] { Loc.T("MONEY_SCREEN_COINS_TITLE"), "Cash" }, tab, i => { tab = i; MetaUI.SetTabSelected(tabs, i); Fill(); }, 36);
            body = UI.Rect(Content, "Body");
            UI.Anchor(body, 0, 0.1f, 1, 0.87f);
            var tip = UI.Label(Content, "Tip: you earn cash every time you level up, at the slot machine and from daily gifts.", 30, Color.white, TextAnchor.MiddleCenter);
            UI.Anchor(tip.rectTransform, 0, 0, 1, 0.09f);
            Fill();
        }

        void Fill()
        {
            UI.Clear(body);
            var h = UI.HBox(body, 20, TextAnchor.MiddleCenter);
            h.childForceExpandWidth = true; h.childForceExpandHeight = true;
            var list = new List<Record>(GameData.Section(tab == 0 ? "GCPackage" : "PCPackage").Values);
            list.RemoveAll(r => !r.Has("Amount"));
            if (tab == 1)
            {
                // one pack per price point (the config repeats them per vendor)
                var seen = new HashSet<int>();
                list.RemoveAll(r => !seen.Add(r.Int("USDCentCost")));
                list.Sort((a, b) => a.Int("USDCentCost").CompareTo(b.Int("USDCentCost")));
                if (list.Count > 5) list.RemoveRange(5, list.Count - 5);
            }
            else list.Sort((a, b) => a.Int("SortPriority").CompareTo(b.Int("SortPriority")));
            for (int i = 0; i < list.Count; i++) Pack(list[i], i);
        }

        /// <summary>The pack's own icon (Ui/{Export}) when rendered, otherwise the plain coin/cash icon.</summary>
        static string BankIcon(Record r, bool coins)
        {
            var own = "Ui/" + r.Str("Export", "");
            return UI.Skin.Icon(own) != null ? own : (coins ? "Ui/coin" : "Ui/cash");
        }

        void Pack(Record r, int index)
        {
            bool coins = tab == 0;
            int amount = r.Int("Amount"), extra = r.Int("ExtraAmount");
            var card = MetaUI.CardPanel(body, MetaUI.Card);
            string titleKey = coins ? "COINS_PACKAGE_" + (index + 1) + "_TITLE" : "FISH_PACKAGE_" + (index + 1) + "_TITLE";
            var t = UI.Label(card.transform, MetaUI.TOr(titleKey, MetaUI.TOr(r.Str("Name"), coins ? "Coins" : "Cash")), 32, Theme.Secondary, TextAnchor.MiddleCenter, true);
            UI.Anchor(t.rectTransform, 0.04f, 0.84f, 0.96f, 0.98f);
            var tile = MetaUI.IconTile(MetaUI.Box(card.transform, 0.18f, 0.46f, 0.82f, 0.82f), BankIcon(r, coins), coins ? "Coins" : "Cash", coins ? Theme.Coin : Theme.Cash);
            MetaUI.Square(tile);
            var a = UI.Rect(card.transform, "Amount");
            UI.Anchor(a, 0, 0.34f, 1, 0.46f);
            UI.HBox(a, 6, TextAnchor.MiddleCenter).childForceExpandWidth = false;
            MetaUI.Amount(a, coins ? "coin" : "cash", (amount + extra).ToString("N0"), 40);
            if (extra > 0)
            {
                var e = UI.Label(card.transform, MetaUI.Fmt("MONEY_SCREEN_EXTRA_TEXT", extra.ToString("N0")), 26, Theme.Good, TextAnchor.MiddleCenter, true);
                UI.Anchor(e.rectTransform, 0, 0.24f, 1, 0.34f);
            }
            if (coins)
            {
                int cost = r.Int("PCCost");
                var b = UI.Button(card.transform, cost + " cash", () =>
                {
                    if (!Progression.Spend(0, cost)) { Progression.NotEnough(true); return; }
                    Progression.AddCoins(amount + extra);
                    ProfileService.Save();
                    AudioManager.Sfx("GetCoins");
                    UI.Toast("+" + (amount + extra).ToString("N0") + " coins", Theme.Coin);
                }, UI.ButtonStyle.Good, 36);
                UI.Anchor((RectTransform)b.transform, 0.08f, 0.04f, 0.92f, 0.22f);
            }
            else
            {
                float usd = r.Int("USDCentCost") / 100f;
                var b = UI.Button(card.transform, "$" + usd.ToString("0.00"), () =>
                    UI.Message("Store not connected", "This fan remake has no in-app purchases.\nEarn cash by levelling up, the slot machine, challenges and daily gifts!"), UI.ButtonStyle.Secondary, 36);
                UI.Anchor((RectTransform)b.transform, 0.08f, 0.04f, 0.92f, 0.22f);
            }
        }
    }
}
