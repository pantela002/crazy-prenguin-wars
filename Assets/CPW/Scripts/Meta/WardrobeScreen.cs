using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// Character screen (the original EquipmentScreen): style the 3D penguin. New players start bare and dress up here.
    ///
    /// Left: equipped-slot chips (hat, outfit, shoes, gloves, skin, medal), the big 3D penguin preview (drag to spin) and the
    /// Attack / Defence / Luck totals with the change the current try-on would make (like the original
    /// EquipmentStatsElement +/- modifiers), plus "Complete set" when hat, outfit and shoes match (WornItems.hasSet).
    /// Right: tabs Hats / Outfits / Shoes / Gloves / Skins / Medals / Sets, the item grid and a detail bar with Buy / Unlock / Wear /
    /// Take off. Tapping any item, owned or not, tries it on (ItemWearPreview in the original); try-ons in several
    /// slots combine so a whole look can be tested before buying. Tapping a selected owned item again wears or
    /// removes it. "Remove all" takes everything off.
    /// </summary>
    public class WardrobeScreen : MetaScreen
    {
        // tab i shows TabSlots[i]; the last tab is Sets
        static readonly ClothesSlot[] TabSlots = { ClothesSlot.Head, ClothesSlot.Chest, ClothesSlot.Feet, ClothesSlot.Hands, ClothesSlot.Skin, ClothesSlot.Trophy };
        static readonly string[] TabNames = { "Hats", "Outfits", "Shoes", "Gloves", "Skins", null, "Sets" };
        const int SetsTab = 6;
        static int tab;
        static readonly ClothesSlot[] Slots = TabSlots;

        /// <summary>
        /// The original 2D sprite penguin has no art for gloves and skins: their tabs and slot chips are hidden while
        /// it is in use (owned and worn gloves / skins stay in the profile and come back with the 3D penguin).
        /// </summary>
        static bool SpriteLook => PenguinAvatar.UseOriginalSprite && PenguinSprite.Available;
        static bool SlotShown(ClothesSlot s) => !SpriteLook || (s != ClothesSlot.Hands && s != ClothesSlot.Skin);
        static bool TabShown(int i) => i >= SetsTab || i >= TabSlots.Length || SlotShown(TabSlots[i]);

        // try-on per slot (index = (int)ClothesSlot, null = show what is worn); selection drives the detail bar
        readonly string[] tryOn = new string[6];
        ClothesDef sel;
        string selSet;

        RectTransform grid, detail, chips;
        Text statsText, setText;
        Button undoBtn, removeBtn;
        List<Button> tabs;
        PenguinAvatar avatar;
        RectTransform dragArea;
        static readonly Vector3[] corners = new Vector3[4];

        protected override string Title => MetaUI.TOr("CHARACTER_TITLE", "Character");
        protected override bool DrawBackground => false;

        protected override void BuildContent()
        {
            // ---- left: slot chips, penguin, stats ----
            chips = UI.Rect(Content, "Slots");
            UI.Anchor(chips, 0, 0.3f, 0.075f, 1);
            UI.VBox(chips, 12, TextAnchor.UpperCenter, 0, true);

            // drag over the penguin to spin it
            var drag = UI.Panel(Content, new Color(0, 0, 0, 0.001f), false, "DragArea");
            dragArea = drag.rectTransform;   // the 3D penguin is framed in it (Tick), above the stats panel
            UI.Anchor(drag.rectTransform, 0.085f, 0.3f, 0.415f, 1);
            drag.gameObject.AddComponent<DragRotate>();
            var hint = UI.Label(drag.transform, "Drag to spin", 26, new Color(1, 1, 1, 0.75f), TextAnchor.LowerCenter);
            UI.Anchor(hint.rectTransform, 0, 0, 1, 0.08f);

            var st = MetaUI.CardPanel(Content, new Color(0.05f, 0.15f, 0.35f, 0.85f), "Stats");
            UI.Anchor(st.rectTransform, 0, 0, 0.415f, 0.28f);
            statsText = UI.Label(st.transform, "", 34, Color.white, TextAnchor.UpperLeft);
            UI.Anchor(statsText.rectTransform, 0.04f, 0.26f, 0.62f, 0.95f);
            statsText.supportRichText = true;
            setText = UI.Label(st.transform, "", 26, MetaUI.Gold, TextAnchor.MiddleLeft);
            UI.Anchor(setText.rectTransform, 0.04f, 0.03f, 0.62f, 0.26f);
            setText.supportRichText = true;
            undoBtn = UI.Button(st.transform, "Undo try-on", ClearTryOn, UI.ButtonStyle.Secondary, 28);
            UI.Anchor((RectTransform)undoBtn.transform, 0.64f, 0.53f, 0.97f, 0.93f);
            removeBtn = UI.Button(st.transform, "Remove all", RemoveAll, UI.ButtonStyle.Danger, 28);
            UI.Anchor((RectTransform)removeBtn.transform, 0.64f, 0.07f, 0.97f, 0.47f);

            // ---- right: tabs, grid, detail bar ----
            var panel = MetaUI.CardPanel(Content, MetaUI.CardDark, "Items");
            UI.Anchor(panel.rectTransform, 0.43f, 0, 1, 1);
            var tabRow = UI.Rect(panel.transform, "Tabs");
            UI.Anchor(tabRow, 0.015f, 0.885f, 0.985f, 0.98f);
            BuildTabs(tabRow);
            var host = UI.Rect(panel.transform, "Grid");
            UI.Anchor(host, 0.01f, 0.27f, 0.99f, 0.875f);
            var sr = UI.ScrollGrid(host, out grid, new Vector2(200, 250), new Vector2(12, 12));
            UI.Stretch((RectTransform)sr.transform);
            var dp = UI.Panel(panel.transform, Theme.Panel, true, "Detail");
            UI.Anchor(dp.rectTransform, 0.015f, 0.015f, 0.985f, 0.26f);
            detail = dp.rectTransform;

            // open on what the penguin wears in the current tab
            if (tab < SetsTab) sel = ClothesCatalog.Get(ClothesCatalog.Worn(TabSlots[tab]));
            Refresh();
        }

        public override void OnShow() => MenuScene3D.Show(MenuScene3D.Layout.Wardrobe);

        public override void Tick(float dt)
        {
            base.Tick(dt);
            // the stats panel covered the penguin from the beak down: keep it inside the area above the panel
            // (screen-space overlay canvas: the corners are screen pixels)
            if (dragArea == null || Screen.width <= 0 || Screen.height <= 0) return;
            dragArea.GetWorldCorners(corners);
            float w = Screen.width, h = Screen.height;
            MenuScene3D.Frame(Rect.MinMaxRect(corners[0].x / w, corners[0].y / h, corners[2].x / w, corners[2].y / h));
        }

        public override void OnHide()
        {
            // drop the try-on so the home penguin wears the real outfit
            MenuScene3D.UpdateClothes();
            MenuScene3D.Hide();
        }

        void BuildTabs(RectTransform row)
        {
            var h = UI.HBox(row, 10, TextAnchor.MiddleCenter, 0, true);
            h.childForceExpandHeight = true;
            tabs = new List<Button>();
            if (!TabShown(tab)) tab = 0;
            for (int i = 0; i < TabNames.Length; i++)
            {
                int idx = i;
                string label = TabNames[i] ?? Loc.T("TAB_TROPHY");
                var b = UI.Button(row, label, () => SelectTab(idx), i == tab ? UI.ButtonStyle.Primary : UI.ButtonStyle.Dark, 26);
                UI.Layout(b, 10, -1, 1, 1);
                b.gameObject.SetActive(TabShown(i));
                tabs.Add(b);
            }
            MetaUI.SetTabSelected(tabs, tab);
        }

        void SelectTab(int i)
        {
            tab = i;
            MetaUI.SetTabSelected(tabs, i);
            sel = null; selSet = null;
            if (i < SetsTab) sel = ClothesCatalog.Get(Preview(TabSlots[i]));
            else selSet = ClothesCatalog.FullSet(Preview(ClothesSlot.Head), Preview(ClothesSlot.Chest), Preview(ClothesSlot.Feet));
            Refresh();
        }

        // ---------- try-on state ----------
        string Preview(ClothesSlot s) => tryOn[(int)s] ?? ClothesCatalog.Worn(s);

        string[] PreviewIds() => new[] { Preview(ClothesSlot.Head), Preview(ClothesSlot.Chest), Preview(ClothesSlot.Feet), Preview(ClothesSlot.Trophy) };

        static string[] WornIds() => new[] { ClothesCatalog.Worn(ClothesSlot.Head), ClothesCatalog.Worn(ClothesSlot.Chest), ClothesCatalog.Worn(ClothesSlot.Feet), ClothesCatalog.Worn(ClothesSlot.Trophy) };

        bool TryingOn()
        {
            foreach (var s in Slots) if (tryOn[(int)s] != null && tryOn[(int)s] != ClothesCatalog.Worn(s)) return true;
            return false;
        }

        void SetTryOn(ClothesSlot s, string id) => tryOn[(int)s] = string.IsNullOrEmpty(id) || id == ClothesCatalog.Worn(s) ? null : id;

        void ClearTryOn()
        {
            for (int i = 0; i < tryOn.Length; i++) tryOn[i] = null;
            Refresh();
        }

        void RemoveAll()
        {
            for (int i = 0; i < tryOn.Length; i++) tryOn[i] = null;
            ClothesCatalog.RemoveAll();
            MenuScene3D.UpdateClothes();
            UI.Toast("Back to the bare penguin!");
            Refresh();
        }

        /// <summary>Dress the menu penguin in the try-on outfit without touching the profile.</summary>
        void ApplyPreview()
        {
            if (avatar == null)
            {
                var scene = Object.FindFirstObjectByType<MenuScene3D>();
                if (scene != null) avatar = scene.GetComponentInChildren<PenguinAvatar>(true);
            }
            if (avatar == null) return;
            avatar.SetClothes(Preview(ClothesSlot.Head), Preview(ClothesSlot.Chest), Preview(ClothesSlot.Feet));
            avatar.SetLook(Preview(ClothesSlot.Hands), Preview(ClothesSlot.Skin));
        }

        void Refresh()
        {
            if (Root == null) return;  // a buy confirmation answered after leaving the screen
            ApplyPreview();
            FillChips();
            Fill();
            UpdateStats();
            UpdateDetail();
        }

        /// <summary>After the profile changed (buy/wear): re-dress, cheer, rebuild.</summary>
        void Changed(bool celebrate)
        {
            MenuScene3D.UpdateClothes();
            if (celebrate) MenuScene3D.Celebrate();
            Refresh();
        }

        // ---------- slot chips ----------
        void FillChips()
        {
            UI.Clear(chips);
            foreach (var s in Slots)
            {
                if (!SlotShown(s)) continue;
                var slot = s;
                string id = Preview(s);
                bool trying = tryOn[(int)s] != null;
                var b = UI.Button(chips, null, () => OpenSlot(slot), UI.ButtonStyle.Dark, 24, "Slot " + s);
                UI.SkinColor(b.GetComponent<Image>(), trying ? new Color(1f, 0.85f, 0.35f, 0.95f) : new Color(0.08f, 0.2f, 0.42f, 0.85f));
                UI.Layout(b, -1, 10, 1, 1);
                if (!string.IsNullOrEmpty(id))
                {
                    var tile = MetaUI.IconTile(MetaUI.Box(b.transform, 0.08f, 0.08f, 0.92f, 0.92f), ClothesCatalog.IconPath(id), ClothesCatalog.DisplayName(id));
                    MetaUI.Square(tile);
                }
                else
                {
                    var l = UI.Label(b.transform, ClothesCatalog.SlotName(s), 24, new Color(1, 1, 1, 0.6f), TextAnchor.MiddleCenter, true);
                    UI.Stretch(l.rectTransform, 4, 4, 4, 4);
                }
            }
        }

        void OpenSlot(ClothesSlot s)
        {
            tab = System.Array.IndexOf(TabSlots, s);
            MetaUI.SetTabSelected(tabs, tab);
            sel = ClothesCatalog.Get(Preview(s));
            selSet = null;
            Refresh();
        }

        // ---------- grid ----------
        void Fill()
        {
            UI.Clear(grid);
            if (tab == SetsTab) { FillSets(); return; }
            var list = ClothesCatalog.BySlot(TabSlots[tab]);
            // worn first, then owned, then by level
            list.Sort((a, b) =>
            {
                bool wa = ClothesCatalog.IsWorn(a.id), wb = ClothesCatalog.IsWorn(b.id);
                if (wa != wb) return wa ? -1 : 1;
                bool oa = Progression.OwnsClothes(a.id), ob = Progression.OwnsClothes(b.id);
                if (oa != ob) return oa ? -1 : 1;
                return a.level != b.level ? a.level.CompareTo(b.level) : string.CompareOrdinal(a.id, b.id);
            });
            foreach (var d in list) ItemCard(d);
        }

        void ItemCard(ClothesDef d)
        {
            var def = d;
            bool owned = Progression.OwnsClothes(d.id), worn = ClothesCatalog.IsWorn(d.id), selected = sel != null && sel.id == d.id;
            Color bg = selected ? new Color(1f, 0.9f, 0.55f) : owned ? MetaUI.Card : new Color(0.75f, 0.8f, 0.9f, 0.92f);
            string corner = worn ? "ON" : !owned && d.vipOnly ? "VIP" : null;
            var card = ItemCards.Card(grid, ClothesCatalog.IconPath(d.id), ClothesCatalog.DisplayName(d.id), () => TapItem(def), out var f,
                bg, corner, worn ? Theme.Good : MetaUI.Gold);
            UI.HBox(f, 6, TextAnchor.MiddleCenter).childForceExpandWidth = false;
            if (owned || d.slot == ClothesSlot.Trophy)
            {
                var l = UI.Label(f, owned ? ClothesCatalog.StatLine(d.id) : "Challenge", 22, owned ? Theme.Text : Theme.Muted);
                UI.Layout(l, 180, 40);
            }
            else MetaUI.Price(f, d.coins, d.cash, 28);
            if (!owned && d.slot == ClothesSlot.Trophy) ItemCards.LockOverlay(card.transform, "?");
            else if (!owned && !ClothesCatalog.IsUnlocked(d)) ItemCards.LockOverlay(card.transform, "Lv " + d.level);
        }

        void TapItem(ClothesDef d)
        {
            int s = (int)d.slot;
            if (sel == null || sel.id != d.id || selSet != null)
            {
                // first tap: select and try on
                sel = d; selSet = null;
                SetTryOn(d.slot, d.id);
                AudioManager.Sfx("Clothes_2", 0.6f);
                Refresh();
                return;
            }
            // second tap on the selected card
            if (Progression.OwnsClothes(d.id))
            {
                if (ClothesCatalog.IsWorn(d.id)) TakeOff(d.slot);
                else WearIt(d);
            }
            else
            {
                tryOn[s] = tryOn[s] == null ? d.id : null;
                Refresh();
            }
        }

        void WearIt(ClothesDef d)
        {
            ClothesCatalog.Wear(d);
            tryOn[(int)d.slot] = null;
            Changed(true);
        }

        void TakeOff(ClothesSlot s)
        {
            ClothesCatalog.TakeOff(s);
            tryOn[(int)s] = null;
            Changed(false);
        }

        // ---------- sets ----------
        void FillSets()
        {
            foreach (var setId in ClothesCatalog.SetIds())
            {
                var id = setId;
                var pieces = ClothesCatalog.SetPieces(id);
                int owned = 0, worn = 0, lockLevel = 0;
                bool vip = false;
                foreach (var p in pieces)
                {
                    if (Progression.OwnsClothes(p.id)) owned++;
                    else
                    {
                        if (!ClothesCatalog.IsUnlocked(p)) lockLevel = Mathf.Max(lockLevel, p.level);
                        vip |= p.vipOnly;
                    }
                    if (ClothesCatalog.IsWorn(p.id)) worn++;
                }
                bool selected = selSet == id;
                var icon = pieces[pieces.Count > 1 ? 1 : 0].id;
                Color bg = selected ? new Color(1f, 0.9f, 0.55f) : owned == pieces.Count ? MetaUI.Card : new Color(0.75f, 0.8f, 0.9f, 0.92f);
                string corner = worn == pieces.Count ? "ON" : vip ? "VIP" : null;
                var card = ItemCards.Card(grid, ClothesCatalog.IconPath(icon), ClothesCatalog.SetName(id), () => TapSet(id), out var f,
                    bg, corner, worn == pieces.Count ? Theme.Good : MetaUI.Gold);
                UI.HBox(f, 6, TextAnchor.MiddleCenter).childForceExpandWidth = false;
                if (owned == pieces.Count)
                {
                    var l = UI.Label(f, "Owned", 28, Theme.Good, TextAnchor.MiddleCenter, true);
                    UI.Layout(l, 160, 40);
                }
                else if (owned > 0)
                {
                    var l = UI.Label(f, owned + "/" + pieces.Count + " owned", 24, Theme.Text);
                    UI.Layout(l, 160, 40);
                }
                else
                {
                    MissingPrice(pieces, out int coins, out int cash);
                    MetaUI.Price(f, coins, cash, 28);
                }
                if (lockLevel > 0) ItemCards.LockOverlay(card.transform, "Lv " + lockLevel);
            }
        }

        static void MissingPrice(List<ClothesDef> pieces, out int coins, out int cash)
        {
            coins = cash = 0;
            foreach (var p in pieces)
                if (!Progression.OwnsClothes(p.id)) { coins += p.coins; cash += p.cash; }
        }

        void TapSet(string setId)
        {
            var pieces = ClothesCatalog.SetPieces(setId);
            bool tried = selSet == setId;
            if (tried)
            {
                // second tap: wear the set when it is all owned
                bool allOwned = true;
                foreach (var p in pieces) allOwned &= Progression.OwnsClothes(p.id);
                if (allOwned) { WearSet(pieces); return; }
            }
            selSet = setId; sel = null;
            foreach (var p in pieces) SetTryOn(p.slot, p.id);
            AudioManager.Sfx("Clothes_2", 0.6f);
            Refresh();
        }

        void WearSet(List<ClothesDef> pieces)
        {
            ClothesCatalog.WearAll(pieces);
            foreach (var p in pieces) tryOn[(int)p.slot] = null;
            Changed(true);
        }

        // ---------- stats ----------
        void UpdateStats()
        {
            var worn = WornIds();
            var prev = PreviewIds();
            statsText.text = StatRow(Loc.T("ATTACK_TITLE"), worn, prev, "Attack", "#ff8a7a") + "\n" +
                             StatRow(Loc.T("DEFENCE_TITLE"), worn, prev, "Defence", "#8ad0ff") + "\n" +
                             StatRow(Loc.T("LUCK_TITLE"), worn, prev, "Luck", "#9cf08a");
            var set = ClothesCatalog.FullSet(prev[0], prev[1], prev[2]);
            setText.text = set != null ? "Complete set: " + ClothesCatalog.SetName(set) + "!"
                : string.IsNullOrEmpty(prev[0]) && string.IsNullOrEmpty(prev[1]) && string.IsNullOrEmpty(prev[2]) ? "<color=#c8d8f0>Bare penguin. Pick something to wear!</color>" : "";
            bool trying = TryingOn();
            undoBtn.gameObject.SetActive(trying);
            removeBtn.interactable = ClothesCatalog.WearingAnything();
        }

        static string StatRow(string name, string[] worn, string[] prev, string stat, string color)
        {
            var now = ClothesCatalog.Stat(prev, stat);
            var was = ClothesCatalog.Stat(worn, stat);
            string v = (now.add >= 0 ? "+" : "") + now.add.ToString("0");
            if (Mathf.Abs(now.mul - 1f) > 0.001f) v += "  x" + now.mul.ToString("0.##");
            string line = "<color=" + color + ">" + name + "</color>   <b>" + v + "</b>";
            float d = now.add - was.add;
            if (Mathf.Abs(d) > 0.001f) line += d > 0 ? "  <color=#7dff6a>(+" + d.ToString("0") + ")</color>" : "  <color=#ff6a6a>(" + d.ToString("0") + ")</color>";
            if (Mathf.Abs(now.mul - was.mul) > 0.001f) line += "  <color=#ffd84a>(x" + was.mul.ToString("0.##") + " -> x" + now.mul.ToString("0.##") + ")</color>";
            return line;
        }

        // ---------- detail bar ----------
        void UpdateDetail()
        {
            UI.Clear(detail);
            if (selSet != null) { SetDetail(selSet); return; }
            if (sel == null)
            {
                string msg = tab == SetsTab ? "Tap a set to try on the whole outfit." :
                    TabSlots[tab] == ClothesSlot.Trophy ? "Medals are won by completing challenges. Tap one to see how." :
                    TabSlots[tab] == ClothesSlot.Skin ? "Tap a skin to try it on. Take it off for the classic penguin." :
                    "Tap an item to try it on. Tap it again to wear it.";
                var l = UI.Label(detail, msg, 34, Theme.Muted, TextAnchor.MiddleCenter);
                UI.Stretch(l.rectTransform, 30, 30, 10, 10);
                return;
            }
            var d = sel;
            string name = ClothesCatalog.DisplayName(d.id);
            DetailHeader(ClothesCatalog.IconPath(d.id), name, SubLine(d), ClothesCatalog.StatLine(d.id));

            var side = DetailSide();
            bool owned = Progression.OwnsClothes(d.id);
            if (owned)
            {
                if (ClothesCatalog.IsWorn(d.id)) SideButton(side, "Take off", () => TakeOff(d.slot), UI.ButtonStyle.Secondary);
                else SideButton(side, "Wear", () => WearIt(d), UI.ButtonStyle.Good);
            }
            else if (d.slot == ClothesSlot.Trophy)
                SideButton(side, "Locked", () => UI.Toast("Complete the challenge to earn this medal."), UI.ButtonStyle.Dark);
            else if (!ClothesCatalog.IsUnlocked(d))
            {
                int fish = ClothesCatalog.UnlockCash(d);
                SidePrice(side, 0, fish);
                SideButton(side, Loc.T("UNLOCK"), () => ItemInfo.ConfirmSpend(0, fish, Loc.T("UNLOCK") + " " + name,
                    () => { if (ClothesCatalog.Unlock(d)) Refresh(); }, true), UI.ButtonStyle.Good);
            }
            else
            {
                SidePrice(side, d.coins, d.cash);
                SideButton(side, Loc.T("BUY"), () => ItemInfo.ConfirmSpend(d.coins, d.cash, name, () =>
                {
                    if (!ClothesCatalog.Buy(d)) return;
                    WearIt(d);  // bought while trying it on: put it on
                }), UI.ButtonStyle.Good);
            }
        }

        static string SubLine(ClothesDef d)
        {
            if (d.slot == ClothesSlot.Trophy)
            {
                foreach (var c in ChallengeCatalog.All)
                    if (c.trophy == d.id) return "Challenge: " + c.name;
                return "Medal";
            }
            string s = ClothesCatalog.SlotName(d.slot);
            if (ClothesCatalog.IsSetPiece(d)) s += "  -  " + d.setName + " set";
            if (!Progression.OwnsClothes(d.id))
            {
                if (!ClothesCatalog.IsUnlocked(d)) s += "  -  <color=#e84c3d>Level " + d.level + "</color>";
                if (d.vipOnly) s += "  -  VIP";
            }
            return s;
        }

        void SetDetail(string setId)
        {
            var pieces = ClothesCatalog.SetPieces(setId);
            if (pieces.Count == 0) { selSet = null; UpdateDetail(); return; }
            string name = ClothesCatalog.SetName(setId) + " set";
            var ids = new List<string>();
            int owned = 0, worn = 0, lockLevel = 0;
            bool vip = false;
            foreach (var p in pieces)
            {
                ids.Add(p.id);
                if (Progression.OwnsClothes(p.id)) owned++;
                else { if (!ClothesCatalog.IsUnlocked(p)) lockLevel = Mathf.Max(lockLevel, p.level); vip |= p.vipOnly; }
                if (ClothesCatalog.IsWorn(p.id)) worn++;
            }
            string sub = pieces.Count + " pieces  -  " + owned + " owned" + (lockLevel > 0 ? "  -  <color=#e84c3d>Level " + lockLevel + "</color>" : "") + (vip ? "  -  VIP" : "");
            string stats = "ATK +" + ClothesCatalog.Stat(ids, "Attack").add.ToString("0") + "   DEF +" + ClothesCatalog.Stat(ids, "Defence").add.ToString("0") +
                           "   LUCK +" + ClothesCatalog.Stat(ids, "Luck").add.ToString("0");
            DetailHeader(ClothesCatalog.IconPath(pieces[pieces.Count > 1 ? 1 : 0].id), name, sub, stats.Replace("+-", "-"));

            var side = DetailSide();
            if (owned == pieces.Count)
            {
                if (worn == pieces.Count)
                {
                    var slots = new ClothesSlot[pieces.Count];
                    for (int i = 0; i < pieces.Count; i++) slots[i] = pieces[i].slot;
                    SideButton(side, "Take off", () => { ClothesCatalog.TakeOff(slots); foreach (var s in slots) tryOn[(int)s] = null; Changed(false); }, UI.ButtonStyle.Secondary);
                }
                else SideButton(side, "Wear set", () => WearSet(pieces), UI.ButtonStyle.Good);
            }
            else if (lockLevel > 0)
                SideButton(side, "Level " + lockLevel, () => UI.Toast("Reach level " + lockLevel + ", or unlock the pieces one by one with fish."), UI.ButtonStyle.Dark);
            else
            {
                MissingPrice(pieces, out int coins, out int cash);
                SidePrice(side, coins, cash);
                SideButton(side, owned > 0 ? "Buy rest" : "Buy set", () => ItemInfo.ConfirmSpend(coins, cash, name, () =>
                {
                    if (ClothesCatalog.BuyMany(pieces)) WearSet(pieces);
                }, true), UI.ButtonStyle.Good);
            }
        }

        void DetailHeader(string icon, string name, string sub, string stats)
        {
            var tile = MetaUI.IconTile(MetaUI.Box(detail, 0.01f, 0.07f, 0.17f, 0.93f), icon, name);
            MetaUI.Square(tile);
            var n = UI.Label(detail, name, 42, Theme.Text, TextAnchor.MiddleLeft, true);
            UI.Anchor(n.rectTransform, 0.19f, 0.62f, 0.64f, 0.95f);
            var s = UI.Label(detail, sub, 28, Theme.Secondary, TextAnchor.MiddleLeft);
            s.supportRichText = true;
            UI.Anchor(s.rectTransform, 0.19f, 0.36f, 0.64f, 0.62f);
            var t = UI.Label(detail, stats, 28, Theme.Text, TextAnchor.MiddleLeft);
            UI.Anchor(t.rectTransform, 0.19f, 0.05f, 0.64f, 0.36f);
        }

        RectTransform DetailSide()
        {
            var side = UI.Rect(detail, "Actions");
            UI.Anchor(side, 0.66f, 0.06f, 0.985f, 0.94f);
            var v = UI.VBox(side, 6, TextAnchor.MiddleCenter);
            v.childForceExpandHeight = false;
            return side;
        }

        static void SidePrice(RectTransform side, int coins, int cash)
        {
            var row = UI.Rect(side, "Price");
            UI.Layout(row, -1, 46);
            UI.HBox(row, 6, TextAnchor.MiddleCenter).childForceExpandWidth = false;
            MetaUI.Price(row, coins, cash, 32);
        }

        static void SideButton(RectTransform side, string text, System.Action onClick, UI.ButtonStyle style)
        {
            var b = UI.Button(side, text, onClick, style, 34);
            UI.Layout(b, -1, 96, 1, 1);
        }

        /// <summary>Spins the menu penguin while dragging.</summary>
        class DragRotate : MonoBehaviour, IDragHandler
        {
            public void OnDrag(PointerEventData e) => MenuScene3D.Rotate(-e.delta.x * 0.4f);
        }
    }
}
