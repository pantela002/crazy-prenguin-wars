using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>Item card used by the shop, loadout and inventory grids.</summary>
    public static class ItemCards
    {
        /// <summary>Card with icon, name and a footer line. Returns the button (footer rect via out).</summary>
        public static Button Card(Transform parent, string iconPath, string name, Action onClick, out RectTransform footer,
            Color? bg = null, string corner = null, Color? cornerColor = null)
        {
            var b = UI.Button(parent, null, onClick, UI.ButtonStyle.Plain, 24, "Card " + name);
            UI.SkinColor(b.GetComponent<Image>(), bg ?? MetaUI.Card);
            var tile = MetaUI.IconTile(MetaUI.Box(b.transform, 0.12f, 0.36f, 0.88f, 0.94f), iconPath, name);
            MetaUI.Square(tile);
            var l = UI.Label(b.transform, name, 28, Theme.Text, TextAnchor.MiddleCenter);
            UI.Anchor(l.rectTransform, 0.03f, 0.2f, 0.97f, 0.36f);
            footer = UI.Rect(b.transform, "Footer");
            UI.Anchor(footer, 0.04f, 0.02f, 0.96f, 0.2f);
            if (!string.IsNullOrEmpty(corner))
            {
                var t = MetaUI.Badge(b.transform, corner, cornerColor ?? Theme.Secondary, 64);
                t.fontSize = corner.Length > 2 ? 22 : 30;
                var rt = (RectTransform)t.transform.parent;
                rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
                rt.anchoredPosition = new Vector2(-30, -30);
            }
            return b;
        }

        /// <summary>Dark overlay with a lock and text (e.g. "Lv 12" or "VIP").</summary>
        public static void LockOverlay(Transform card, string text, Color? color = null)
        {
            var o = UI.Panel(card, MetaUI.Locked, true, "Lock");
            UI.Stretch(o.rectTransform);
            o.raycastTarget = false;
            var lockIcon = UI.Skin.Icon("Ui/lock");
            if (lockIcon != null)
            {
                var i = UI.Image(o.transform, lockIcon);
                UI.Anchor(i.rectTransform, 0.3f, 0.45f, 0.7f, 0.85f);
            }
            var l = UI.Label(o.transform, text, 36, color ?? Color.white, TextAnchor.MiddleCenter, true);
            UI.Anchor(l.rectTransform, 0.02f, lockIcon != null ? 0.2f : 0.35f, 0.98f, lockIcon != null ? 0.45f : 0.65f);
        }
    }

    /// <summary>
    /// Pre-battle gear screen (the original WeaponSelection / BoosterSelection): check and top up your ammo, pick
    /// up to 3 boosters, see your opponents, then fight.
    /// </summary>
    public class LoadoutScreen : MetaScreen
    {
        const int MaxBoosters = 3;
        readonly BattleConfig config;
        PlayerSlot me;
        RectTransform weaponGrid, boosterGrid;
        int weaponTab;
        bool ammoWarned;
        List<Button> tabs;
        protected override string Title => config.mode == BattleMode.Practice ? Loc.T("PRACTICE") : Loc.T("POPUP_CHOOSEWEAPON");

        public LoadoutScreen(BattleConfig c) { config = c; }

        bool Free => !me.usesProfileInventory;

        protected override void BuildContent()
        {
            me = config.players[config.LocalPlayerIndex];
            // ---- weapons ----
            var wp = MetaUI.CardPanel(Content, MetaUI.CardDark, "Weapons");
            UI.Anchor(wp.rectTransform, 0, 0.14f, 0.6f, 1);
            var tabRow = UI.Rect(wp.transform, "Tabs");
            UI.Anchor(tabRow, 0.02f, 0.87f, 0.98f, 0.98f);
            if (Free)
            {
                var t = UI.Label(tabRow, "Practice arsenal: free ammo, nothing is spent", 34, Color.white, TextAnchor.MiddleLeft, true);
                UI.Stretch(t.rectTransform);
            }
            else tabs = MetaUI.Tabs(tabRow, new[] { "My arsenal", Loc.T("TAB_ALL") }, weaponTab, i => { weaponTab = i; MetaUI.SetTabSelected(tabs, i); FillWeapons(); });
            var wh = UI.Rect(wp.transform, "Grid");
            UI.Anchor(wh, 0.01f, 0.01f, 0.99f, 0.86f);
            var sr = UI.ScrollGrid(wh, out weaponGrid, new Vector2(200, 250), new Vector2(12, 12));
            UI.Stretch((RectTransform)sr.transform);
            FillWeapons();

            // ---- boosters ----
            var bp = MetaUI.CardPanel(Content, MetaUI.CardDark, "Boosters");
            UI.Anchor(bp.rectTransform, 0.615f, 0.42f, 1, 1);
            var bt = UI.Label(bp.transform, Loc.T("BOOSTER") + " (max " + MaxBoosters + ")", 36, Color.white, TextAnchor.MiddleLeft, true);
            UI.Anchor(bt.rectTransform, 0.04f, 0.84f, 0.98f, 0.98f);
            var bh = UI.Rect(bp.transform, "Grid");
            UI.Anchor(bh, 0.01f, 0.01f, 0.99f, 0.84f);
            var bsr = UI.ScrollGrid(bh, out boosterGrid, new Vector2(170, 215), new Vector2(10, 10));
            UI.Stretch((RectTransform)bsr.transform);
            FillBoosters();

            // ---- versus ----
            var vs = MetaUI.CardPanel(Content, MetaUI.Card, "Versus");
            UI.Anchor(vs.rectTransform, 0.615f, 0.14f, 1, 0.4f);
            var vl = UI.VBox(vs.rectTransform, 4, TextAnchor.UpperLeft, 16);
            vl.childForceExpandHeight = false;
            var map = UI.Label(vs.transform, "Map: " + BattleFactory.LevelDisplayName(config.levelId), 30, Theme.Secondary, TextAnchor.MiddleLeft, true);
            UI.Layout(map, -1, 42);
            if (config.mode == BattleMode.Practice)
            {
                // practice lets you pick the map (locked maps stay locked, like the original private game map list)
                var change = UI.Button(vs.transform, "Map...", () => CustomGameScreen.PickMap(config.levelId, id =>
                {
                    config.levelId = string.IsNullOrEmpty(id) ? BattleFactory.RandomLevelFor(ProfileService.P.level) : id;
                    if (map) map.text = "Map: " + BattleFactory.LevelDisplayName(config.levelId);
                }), UI.ButtonStyle.Secondary, 30);
                UI.Layout(change).ignoreLayout = true;
                UI.Place((RectTransform)change.transform, new Vector2(1, 1), new Vector2(180, 70), new Vector2(-12, -12));
            }
            for (int i = 0; i < config.players.Count; i++)
            {
                if (i == config.LocalPlayerIndex) continue;
                var p = config.players[i];
                var l = UI.Label(vs.transform, "vs  " + p.name + "  (Lv " + p.level + (p.isAI ? ", " + new[] { "easy", "normal", "hard" }[p.aiSkill] : "") + ")", 30, Theme.Text, TextAnchor.MiddleLeft);
                UI.Layout(l, -1, 40);
            }
            if (BattleFactory.BetCoins(config.betId) + BattleFactory.BetCash(config.betId) > 0)
            {
                var b = UI.Label(vs.transform, Loc.T("BETTING_TEXT") + " " + BattleFactory.BetLabel(config.betId), 30, MetaUI.Orange, TextAnchor.MiddleLeft, true);
                UI.Layout(b, -1, 40);
            }

            var fight = UI.Button(Content, "FIGHT!", Fight, UI.ButtonStyle.Primary, 64);
            UI.Place((RectTransform)fight.transform, new Vector2(1, 0), new Vector2(480, 120), Vector2.zero);
            fight.gameObject.AddComponent<UIPulse>().amount = 0.03f;
            var shop = UI.Button(Content, Loc.T("BUTTON_SUPPLIES"), () => ScreenManager.Show(() => new ShopScreen()), UI.ButtonStyle.Secondary, 44);
            UI.Place((RectTransform)shop.transform, new Vector2(0, 0), new Vector2(300, 110), Vector2.zero);
            if (Free) shop.gameObject.SetActive(false);
        }

        /// <summary>
        /// Like the original NotEnoughAmmo popup before a match, warn once when the arsenal is nearly empty
        /// (fists still work, so the player may fight anyway) and offer the shop.
        /// </summary>
        void Fight()
        {
            if (!Free && !ammoWarned && FreeAmmoPack.TotalAmmo() < FreeAmmoPack.MinAmmo)
            {
                ammoWarned = true;
                UI.Popup("Low on ammo", "You only have " + FreeAmmoPack.TotalAmmo() + " shots left. Stock up in the shop, or fight with your fists?",
                    new UI.PopupButton(Loc.T("BUTTON_SUPPLIES"), () => ScreenManager.Show(() => new ShopScreen(2)), UI.ButtonStyle.Secondary),
                    new UI.PopupButton("Fight!", () => BattleFactory.Launch(config)));
                return;
            }
            BattleFactory.Launch(config);
        }

        void FillWeapons()
        {
            UI.Clear(weaponGrid);
            if (Free)
            {
                foreach (var s in me.loadout)
                {
                    var r = GameData.Item(s.id);
                    if (r == null) continue;
                    ItemCards.Card(weaponGrid, ItemCatalog.IconPath(r), ItemCatalog.Name(r), () => ItemInfo.Show(r), out var f);
                    var l = UI.Label(f, ItemCatalog.IsInfinite(r) ? "99+" : "x" + s.amount, 32, Theme.Good, TextAnchor.MiddleCenter, true);
                    UI.Stretch(l.rectTransform);
                }
                return;
            }
            var P = ProfileService.P;
            var list = new List<Record>();
            var punch = GameData.Item("Punch");
            if (punch != null) list.Add(punch);
            foreach (var r in ItemCatalog.ShopItems("Weapon"))
                if (weaponTab == 1 || P.Ammo(r.Id) > 0) list.Add(r);
            foreach (var r in list)
            {
                var item = r;
                int ammo = P.Ammo(r.Id);
                bool infinite = ItemCatalog.IsInfinite(r);
                bool locked = !ItemCatalog.IsUnlocked(r);
                var card = ItemCards.Card(weaponGrid, ItemCatalog.IconPath(r), ItemCatalog.Name(r), () => ItemInfo.Show(item, FillWeapons), out var f,
                    null, infinite ? "99+" : ammo.ToString(), ammo > 0 || infinite ? Theme.Good : Theme.Danger);
                if (!infinite && !locked && !ItemCatalog.VipBlocked(r))
                {
                    var buy = UI.Button(f, "+" + ItemCatalog.AmountPurchased(r), () => { if (ItemCatalog.Buy(item)) FillWeapons(); }, UI.ButtonStyle.Good, 28);
                    UI.Stretch((RectTransform)buy.transform);
                }
                if (locked) ItemCards.LockOverlay(card.transform, "Lv " + ItemCatalog.RequiredLevel(r));
                else if (ItemCatalog.VipBlocked(r)) ItemCards.LockOverlay(card.transform, "VIP", MetaUI.Gold);
            }
        }

        void FillBoosters()
        {
            UI.Clear(boosterGrid);
            var P = ProfileService.P;
            var list = new List<Record>();
            foreach (var r in ItemCatalog.ShopItems("Booster"))
                if (Free ? ItemCatalog.HasCategory(r, "Practice") : P.Ammo(r.Id) > 0) list.Add(r);
            if (list.Count == 0)
            {
                var l = UI.Label(boosterGrid, "No boosters yet. Get some in the shop!", 30, Color.white);
                UI.Layout(l, 500, 120);
                return;
            }
            foreach (var r in list)
            {
                var item = r;
                bool on = me.boosters.Contains(r.Id);
                var card = ItemCards.Card(boosterGrid, ItemCatalog.IconPath(r), ItemCatalog.Name(r), () =>
                {
                    if (me.boosters.Contains(item.Id)) me.boosters.Remove(item.Id);
                    else if (me.boosters.Count < MaxBoosters) me.boosters.Add(item.Id);
                    else UI.Toast("You can take " + MaxBoosters + " boosters.");
                    FillBoosters();
                }, out var f, on ? Theme.Primary : MetaUI.Card, Free ? null : "x" + P.Ammo(r.Id), Theme.Secondary);
                var l = UI.Label(f, on ? "Selected" : "Tap to take", 24, on ? Theme.PrimaryText : Theme.Muted, TextAnchor.MiddleCenter, on);
                UI.Stretch(l.rectTransform);
            }
        }
    }

    /// <summary>
    /// "Match loading" splash before a battle (original matchloading screen): versus line-up, map, a tip, then
    /// GameManager.StartBattle.
    /// </summary>
    public class MatchLoadingScreen : UIScreen
    {
        readonly BattleConfig config;
        Image fill;
        float t;
        bool started;
        const float Duration = 2.2f;
        public override bool ShowTopBar => false;
        public override string Music => "BriefingMusic";

        public MatchLoadingScreen(BattleConfig c) { config = c; }

        public override void Build()
        {
            MetaUI.Background(Root);
            var thumb = UI.Image(Root, LevelThumbs.Get(config.levelId), new Color(1, 1, 1, 0.35f), false, "Map");
            UI.Stretch(thumb.rectTransform);
            if (thumb.sprite == null) thumb.enabled = false;
            var mapName = config.mode == BattleMode.Tutorial ? Loc.T("TUTORIAL_INTRO_TITLE") : BattleFactory.LevelDisplayName(config.levelId);
            var title = UI.Label(Root, mapName, 80, Color.white, TextAnchor.MiddleCenter, true);
            UI.Anchor(title.rectTransform, 0.1f, 0.82f, 0.9f, 0.96f);

            var row = UI.Rect(Root, "Players");
            UI.Anchor(row, 0.05f, 0.4f, 0.95f, 0.78f);
            var h = UI.HBox(row, 30, TextAnchor.MiddleCenter);
            h.childForceExpandWidth = true; h.childForceExpandHeight = true;
            for (int i = 0; i < config.players.Count; i++)
            {
                var p = config.players[i];
                if (i > 0)
                {
                    var vs = UI.Label(row, "VS", 90, MetaUI.Gold, TextAnchor.MiddleCenter, true);
                    UI.Layout(vs, 140, -1, 0, 1);
                }
                var card = MetaUI.CardPanel(row, Theme.PlayerColors[p.colorIndex % 4]);
                UI.Layout(card, -1, -1, 1, 1);
                var n = UI.Label(card.transform, p.name, 46, Color.white, TextAnchor.MiddleCenter, true);
                UI.Anchor(n.rectTransform, 0.04f, 0.45f, 0.96f, 0.9f);
                var lv = UI.Label(card.transform, "Level " + p.level + (p.isAI ? "  (AI)" : ""), 32, Color.white);
                UI.Anchor(lv.rectTransform, 0.04f, 0.12f, 0.96f, 0.42f);
            }

            var host = UI.Rect(Root, "Bar");
            UI.Anchor(host, 0.25f, 0.28f, 0.75f, 0.34f);
            fill = UI.Bar(host, MetaUI.Gold);
            UI.Stretch((RectTransform)fill.transform.parent);
            fill.fillAmount = 0;
            var tip = UI.Label(Root, Tips.Random(), 40, Color.white);
            UI.Anchor(tip.rectTransform, 0.1f, 0.08f, 0.9f, 0.24f);
        }

        public override void Tick(float dt)
        {
            if (started) return;
            t += dt;
            fill.fillAmount = Mathf.Clamp01(t / Duration);
            if (t >= Duration)
            {
                started = true;
                MenuScene3D.Hide();
                GameManager.StartBattle(config);
            }
        }

        public override bool OnBack() => true; // can't back out while loading
    }
}
