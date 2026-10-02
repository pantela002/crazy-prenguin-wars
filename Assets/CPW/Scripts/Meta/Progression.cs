using System;
using System.Collections.Generic;
using UnityEngine;

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

    /// <summary>"Level Up!" popup with the Experience.PCReward cash reward.</summary>
    public static class LevelUpPopup
    {
        public static void Show(int level, Action closed = null)
        {
            var win = MetaUI.Window(Loc.T("LEVEL_UP_TITLE"), new Vector2(1100, 760), out var layer, MetaUI.Gold);
            var rays = UI.Image(win, UI.Circle, new Color(1, 0.85f, 0.3f, 0.35f), false, "Glow");
            UI.Place(rays.rectTransform, new Vector2(0.5f, 0.62f), new Vector2(420, 420), Vector2.zero);
            rays.gameObject.AddComponent<UIPulse>().amount = 0.08f;
            var badge = MetaUI.Badge(win, level.ToString(), Theme.Secondary, 260);
            var brt = (RectTransform)badge.transform.parent;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.62f);
            brt.anchoredPosition = Vector2.zero;
            var desc = UI.Label(win, MetaUI.Fmt("LEVELUP_DESCRIPTION", level), 40, Theme.Text);
            UI.Anchor(desc.rectTransform, 0.06f, 0.24f, 0.94f, 0.4f);
            var r = GameData.Get("Experience", level.ToString());
            int cash = r != null ? r.Int("PCReward") : 0;
            if (cash > 0)
            {
                var row = UI.Rect(win, "Reward");
                UI.Anchor(row, 0.3f, 0.17f, 0.7f, 0.26f);
                UI.HBox(row, 6, TextAnchor.MiddleCenter).childForceExpandWidth = false;
                MetaUI.Amount(row, "cash", "+" + cash, 48, Theme.Good);
            }
            var ok = UI.Button(win, Loc.T("LEVELUP_OKBUTTON"), () => { MetaUI.Close(layer); closed?.Invoke(); }, UI.ButtonStyle.Primary, 48);
            UI.Place((RectTransform)ok.transform, new Vector2(0.5f, 0), new Vector2(380, 110), new Vector2(0, 30));
            AudioManager.Sfx("LevelUpSound");
        }
    }
}
