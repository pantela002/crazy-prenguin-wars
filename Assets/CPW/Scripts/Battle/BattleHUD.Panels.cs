using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>HUD panels: weapon picker with category tabs, boosters, emoticons, pause menu, pass-the-phone curtain, match over.</summary>
    public partial class BattleHUD
    {
        GameObject weaponPanel, boosterPanel, emotePanel, pausePanel, curtain, overPanel;
        string weaponTab;
        RectTransform weaponGrid;
        readonly List<Button> tabButtons = new List<Button>();

        bool AnyPanelOpen => weaponPanel || boosterPanel || emotePanel || pausePanel || curtain || chatPanel;

        void CloseTransientPanels()
        {
            bool hadWeapons = weaponPanel;
            if (weaponPanel) Destroy(weaponPanel);
            if (boosterPanel) Destroy(boosterPanel);
            weaponPanel = null; boosterPanel = null;
            if (hadWeapons) c.Tutorial?.OnWeaponPanelClosed();
        }

        void CloseAllPanels()
        {
            CloseTransientPanels();
            if (emotePanel) Destroy(emotePanel);
            emotePanel = null;
            CloseChatPanel();
            if (pausePanel) ClosePause();
        }

        /// <summary>Dim layer + window; tapping outside the window closes it.</summary>
        RectTransform Window(string title, Vector2 size, out GameObject layerGo, Action onClose)
        {
            var layer = UI.Stretch(UI.Rect(panelLayer, "Panel " + title));
            layerGo = layer.gameObject;
            var block = UI.Blocker(layer, 0.45f);
            // not a Button: the touch that opened the panel must never close it again (BackdropCloser)
            block.gameObject.AddComponent<BackdropCloser>().close = onClose;
            var win = UI.Panel(layer, Theme.PanelDark, true, "Window");
            UI.Place(win.rectTransform, new Vector2(0.5f, 0.5f), size, Vector2.zero);
            var t = UI.Label(win.transform, title, 56, Theme.Primary, TextAnchor.MiddleCenter, true);
            UI.Place(t.rectTransform, new Vector2(0.5f, 1), new Vector2(size.x - 240, 90), new Vector2(0, -10));
            var x = TapButton(win.transform, "X", onClose, UI.ButtonStyle.Danger, 48);
            UI.Place((RectTransform)x.transform, new Vector2(1, 1), new Vector2(100, 100), new Vector2(-14, -14));
            layer.gameObject.AddComponent<PopIn>().target = win.rectTransform;
            return win.rectTransform;
        }

        /// <summary>Item cell: icon (fallback colored tile with the name), count, caption.</summary>
        Button ItemCell(Transform parent, string id, Sprite icon, string count, bool selected, bool enabled, Action click)
        {
            var b = UI.Button(parent, null, click, selected ? UI.ButtonStyle.Primary : UI.ButtonStyle.Secondary);
            if (icon != null)
            {
                var img = UI.Image(b.transform, icon);
                UI.Anchor(img.rectTransform, 0.1f, 0.26f, 0.9f, 0.94f);
            }
            else
            {
                var tile = UI.Panel(b.transform, Color.HSVToRGB((Mathf.Abs(id.GetHashCode()) % 360) / 360f, 0.45f, 0.75f), true, "Tile");
                tile.raycastTarget = false;
                UI.Anchor(tile.rectTransform, 0.1f, 0.3f, 0.9f, 0.92f);
                var l = UI.Label(tile.transform, BattleItems.Name(id), 26, Color.white, TextAnchor.MiddleCenter, true);
                UI.Stretch(l.rectTransform, 6, 6, 4, 4);
            }
            var name = UI.Label(b.transform, icon != null ? BattleItems.Name(id) : "", 22, Color.white);
            UI.Anchor(name.rectTransform, 0.04f, 0.02f, 0.7f, 0.26f);
            var cnt = UI.Label(b.transform, count, 30, Theme.Primary, TextAnchor.MiddleRight, true);
            UI.Anchor(cnt.rectTransform, 0.55f, 0.02f, 0.96f, 0.28f);
            b.interactable = enabled;
            return b;
        }

        static string CountText(int n) => n == Loadout.Infinite ? "∞" : "x" + n;

        // ---------------------------------------------------------------- weapons

        void OpenWeapons()
        {
            if (!c.IsInputTurn || c.Fired) return;
            CloseAllPanels();
            var win = Window("Weapons", new Vector2(1500, 860), out weaponPanel, CloseTransientPanels);
            var tabs = UI.Rect(win, "Tabs");
            UI.Place(tabs, new Vector2(0.5f, 1), new Vector2(1400, 100), new Vector2(0, -110));
            UI.HBox(tabs, 16, TextAnchor.MiddleCenter);
            tabButtons.Clear();
            foreach (var cat in BattleRules.WeaponTabs)
            {
                string tab = cat;
                var b = TapButton(tabs, Loc.Has(cat.ToUpperInvariant()) ? Loc.T(cat.ToUpperInvariant()) : cat, () => ShowWeaponTab(tab), UI.ButtonStyle.Plain, 38);
                UI.Layout(b, 320, 90);
                tabButtons.Add(b);
            }
            var area = UI.Rect(win, "GridArea");
            UI.Anchor(area, 0.02f, 0.03f, 0.98f, 0.73f);
            UI.ScrollGrid(area, out weaponGrid, new Vector2(230, 240), new Vector2(18, 18));
            UI.Stretch((RectTransform)area.GetChild(0));

            // open on the tab of the current weapon (or of the weapon the tutorial asks for)
            string cur = c.Tutorial?.WantedWeapon ?? CurrentItem;
            weaponTab = BattleRules.WeaponTabs[0];
            if (cur != null) foreach (var t in BattleRules.WeaponTabs) if (BattleItems.InCategory(cur, t)) { weaponTab = t; break; }
            ShowWeaponTab(weaponTab);
            c.Tutorial?.OnWeaponPanelOpened();
        }

        void ShowWeaponTab(string tab)
        {
            weaponTab = tab;
            for (int i = 0; i < tabButtons.Count; i++)
                tabButtons[i].GetComponent<Image>().color = BattleRules.WeaponTabs[i] == tab ? Theme.Primary : Theme.PanelInner;
            UI.Clear(weaponGrid);
            var a = c.Active;
            if (a == null) return;
            string cur = CurrentItem;
            string wanted = c.Tutorial?.WantedWeapon;
            bool shop = CanBuyInBattle(a);
            int shown = 0;
            foreach (var w in a.Ammo.Weapons)
            {
                if (!BattleItems.InCategory(w, tab)) continue;
                string id = w;
                int n = a.Ammo.Count(w);
                shown++;
                if (n == 0 && shop && ShopRecord(id) != null) { BuyCell(weaponGrid, a, id); continue; }
                var cell = ItemCell(weaponGrid, id, BattleItems.Icon(id), CountText(n), id == cur, n != 0, () =>
                {
                    c.ActSelectWeapon(id);
                    CloseTransientPanels();
                });
                if (id == wanted) cell.gameObject.AddComponent<Pulse>().on = true;
            }
            // original in-battle weapon list: shop weapons you don't have yet, greyed with a price (BattleHUD.Shop)
            if (shop) shown += AddShopCells(weaponGrid, a, tab);
            if (shown == 0)
            {
                var l = UI.Label(weaponGrid, "No weapons here", 36, Theme.Muted);
                UI.Layout(l, 600, 100);
            }
        }

        // ---------------------------------------------------------------- boosters

        void OpenBoosters()
        {
            var a = c.Active;
            if (!c.IsInputTurn || a == null) return;
            CloseAllPanels();
            var win = Window("Boosters", new Vector2(1300, 720), out boosterPanel, CloseTransientPanels);
            var area = UI.Rect(win, "GridArea");
            UI.Anchor(area, 0.02f, 0.03f, 0.98f, 0.84f);
            UI.ScrollGrid(area, out var grid, new Vector2(230, 240), new Vector2(18, 18));
            UI.Stretch((RectTransform)area.GetChild(0));
            foreach (var bId in a.Ammo.Boosters)
            {
                string id = bId;
                int n = a.Ammo.Count(id);
                ItemCell(grid, id, BattleItems.Icon(id), CountText(n), false, n != 0 && !c.BoosterUsedThisTurn, () =>
                {
                    if (!c.ActBooster(id)) Banner("Can't use that now", Color.white, 1f);
                    CloseTransientPanels();
                });
            }
        }

        // ---------------------------------------------------------------- emoticons

        void OpenEmotes()
        {
            var me = c.ViewPenguin;
            if (me == null || !me.CanEmote) return;
            CloseAllPanels();
            var win = Window(Loc.Has("HUD_EMOTE") ? Loc.T("HUD_EMOTE") : "Emote", new Vector2(1000, 900), out emotePanel, () => { if (emotePanel) Destroy(emotePanel); emotePanel = null; });
            var grid = UI.Rect(win, "Grid");
            UI.Anchor(grid, 0.04f, 0.03f, 0.96f, 0.86f);
            var g = UI.Grid(grid, new Vector2(200, 170), new Vector2(18, 18), 6);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 4;
            g.childAlignment = TextAnchor.MiddleCenter;
            int idx = me.PlayerIndex;
            foreach (var e in BattleRules.Emoticons)
            {
                string id = e;
                var r = GameData.Get("Emoticon", id);
                var iconId = r != null ? GameData.RefId(r.Str("Icon", "")) : id;
                var icon = ModelLibrary.Icon("Emoticons/" + id) ?? ModelLibrary.Icon("Emoticons/" + iconId);
                string label = r != null ? Loc.T(r.Str("Name", id)) : id;
                Action pick = () =>
                {
                    c.ActEmote(idx, id);
                    if (emotePanel) Destroy(emotePanel);
                    emotePanel = null;
                };
                if (icon != null) UI.IconButton(grid, icon, null, pick, UI.ButtonStyle.Secondary);
                else UI.Button(grid, label, pick, UI.ButtonStyle.Secondary, 32);
            }
        }

        // ---------------------------------------------------------------- pause

        void OpenPause()
        {
            if (pausePanel || c.CurrentPhase == BattleController.Phase.Over) return;
            CloseTransientPanels();
            c.SetPaused(true);
            var win = Window(c.Online ? "Menu" : "Paused", new Vector2(900, 900), out pausePanel, ClosePause);
            var box = UI.Rect(win, "Items");
            UI.Anchor(box, 0.08f, 0.04f, 0.92f, 0.86f);
            UI.VBox(box, 22, TextAnchor.UpperCenter, 10);
            var resume = UI.Button(box, "Resume", ClosePause, UI.ButtonStyle.Primary, 48);
            UI.Layout(resume, -1, 120);
            var p = ProfileService.P;
            Toggle(box, "Music", p.musicOn, v => { p.musicOn = v; AudioManager.I?.ApplySettings(); ProfileService.Save(); });
            Toggle(box, "Sound effects", p.sfxOn, v => { p.sfxOn = v; ProfileService.Save(); });
            Toggle(box, "Trajectory guide", p.showTrajectory, v => { p.showTrajectory = v; ProfileService.Save(); });
            string quit = c.Config.mode == BattleMode.Tutorial ? "Skip tutorial" : (c.Config.mode == BattleMode.Practice ? "Quit" : "Surrender");
            var q = UI.Button(box, quit, () => UI.Confirm(quit, "Leave this battle? It will count as a loss.", () => c.Surrender()), UI.ButtonStyle.Danger, 44);
            UI.Layout(q, -1, 110);
        }

        void Toggle(RectTransform parent, string label, bool value, Action<bool> change)
        {
            var bg = UI.Panel(parent, Theme.Panel, true, "Row " + label);
            UI.Layout(bg, -1, 100);
            var t = UI.Toggle(bg.transform, label, value, change);
            UI.Stretch((RectTransform)t.transform, 20, 20, 10, 10);
        }

        void ClosePause()
        {
            if (pausePanel) Destroy(pausePanel);
            pausePanel = null;
            c.SetPaused(false);
        }

        // ---------------------------------------------------------------- pass-and-play curtain

        public void ShowCurtain(Penguin p)
        {
            CloseAllPanels();
            if (curtain) Destroy(curtain);
            var layer = UI.Stretch(UI.Rect(panelLayer, "Curtain"));
            curtain = layer.gameObject;
            var bg = UI.Panel(layer, new Color(0.05f, 0.09f, 0.16f, 1f), false, "Bg");
            UI.Stretch(bg.rectTransform);
            var dot = UI.Image(layer, UI.Circle, p.TeamColor, false, "Color");
            UI.Place(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(160, 160), new Vector2(0, 260));
            var t = UI.Label(layer, "Pass the phone to\n" + p.DisplayName, 90, Color.white, TextAnchor.MiddleCenter, true);
            UI.Place(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1500, 320), new Vector2(0, 40));
            var b = UI.Button(layer, "I'm " + p.DisplayName + " - Go!", () =>
            {
                if (curtain) Destroy(curtain);
                curtain = null;
                c.ConfirmCurtain();
            }, UI.ButtonStyle.Primary, 52);
            UI.Place((RectTransform)b.transform, new Vector2(0.5f, 0.5f), new Vector2(760, 150), new Vector2(0, -250));
        }

        // ---------------------------------------------------------------- match over

        public void ShowMatchOver(BattleResult r)
        {
            CloseAllPanels();
            HideHint();
            HideAimVisuals();
            controlsGroup.alpha = 0;
            controlsGroup.blocksRaycasts = false;
            if (overPanel) Destroy(overPanel);
            var layer = UI.Stretch(UI.Rect(panelLayer, "MatchOver"));
            overPanel = layer.gameObject;
            var win = UI.Panel(layer, new Color(0.05f, 0.1f, 0.2f, 0.88f), true, "Window");
            UI.Place(win.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1100, 260 + r.players.Count * 90), new Vector2(0, -40));
            var winner = r.players.Find(p => p.rank == 1);
            var local = r.Local;
            string title = c.PassAndPlay || local == null ? (winner != null ? winner.name + " wins!" : "Match over")
                : (local.rank == 1 ? "Victory!" : "Match over");
            var t = UI.Label(win.transform, title, 80, Theme.Primary, TextAnchor.MiddleCenter, true);
            UI.Place(t.rectTransform, new Vector2(0.5f, 1), new Vector2(1000, 120), new Vector2(0, -20));
            var sorted = new List<PlayerResult>(r.players);
            sorted.Sort((a, b) => a.rank.CompareTo(b.rank));
            for (int i = 0; i < sorted.Count; i++)
            {
                var pr = sorted[i];
                var p = c.PenguinAt(pr.slotIndex);
                var row = UI.Panel(win.transform, pr.isLocal ? new Color(1, 1, 1, 0.18f) : new Color(1, 1, 1, 0.06f), true, "Row");
                UI.Place(row.rectTransform, new Vector2(0.5f, 1), new Vector2(1000, 80), new Vector2(0, -150 - i * 90));
                var rank = UI.Label(row.transform, "#" + pr.rank, 44, Color.white, TextAnchor.MiddleLeft, true);
                UI.Anchor(rank.rectTransform, 0.03f, 0, 0.15f, 1);
                var n = UI.Label(row.transform, pr.name, 40, p != null ? p.TeamColor : Color.white, TextAnchor.MiddleLeft, true);
                UI.Anchor(n.rectTransform, 0.15f, 0, 0.6f, 1);
                var k = UI.Label(row.transform, pr.kills + " K / " + pr.deaths + " D", 32, Theme.Muted, TextAnchor.MiddleCenter);
                UI.Anchor(k.rectTransform, 0.58f, 0, 0.8f, 1);
                var s = UI.Label(row.transform, pr.score.ToString(), 44, Theme.Primary, TextAnchor.MiddleRight, true);
                UI.Anchor(s.rectTransform, 0.8f, 0, 0.97f, 1);
            }
            var cont = UI.Button(layer, "Continue", c.ContinueAfterMatch, UI.ButtonStyle.Primary, 44);
            UI.Place((RectTransform)cont.transform, new Vector2(0.5f, 0), new Vector2(420, 120), new Vector2(0, 40));
            layer.gameObject.AddComponent<PopIn>().target = win.rectTransform;
        }
    }
}
