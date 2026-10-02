using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// Wardrobe / Equipment: dress the 3D penguin (head, chest, feet, trophy) with a live preview and the total
    /// Attack / Defence / Luck from the Bonus stat mods of everything worn.
    /// </summary>
    public class WardrobeScreen : MetaScreen
    {
        static int tab;
        RectTransform grid;
        Text statsText;
        List<Button> tabs;
        protected override string Title => Loc.T("BUTTON_CHARACTER");
        protected override bool DrawBackground => false;

        protected override void BuildContent()
        {
            // drag on the left half to spin the penguin
            var drag = UI.Panel(Content, new Color(0, 0, 0, 0.001f), false, "DragArea");
            UI.Anchor(drag.rectTransform, 0, 0.3f, 0.44f, 1);
            drag.gameObject.AddComponent<DragRotate>();

            // stats card (bottom-left)
            var st = MetaUI.CardPanel(Content, new Color(0.05f, 0.15f, 0.35f, 0.82f), "Stats");
            UI.Anchor(st.rectTransform, 0, 0, 0.42f, 0.28f);
            statsText = UI.Label(st.transform, "", 34, Color.white, TextAnchor.MiddleLeft);
            UI.Stretch(statsText.rectTransform, 30, 20, 10, 10);
            statsText.supportRichText = true;

            // items (right)
            var panel = MetaUI.CardPanel(Content, MetaUI.CardDark, "Items");
            UI.Anchor(panel.rectTransform, 0.45f, 0, 1, 1);
            var tabRow = UI.Rect(panel.transform, "Tabs");
            UI.Anchor(tabRow, 0.02f, 0.88f, 0.98f, 0.98f);
            tabs = MetaUI.Tabs(tabRow, new[] { "Hats", "Outfits", "Shoes", Loc.T("TAB_TROPHY") }, tab, i => { tab = i; MetaUI.SetTabSelected(tabs, i); Fill(); }, 32);
            var host = UI.Rect(panel.transform, "Grid");
            UI.Anchor(host, 0.01f, 0.01f, 0.99f, 0.87f);
            var sr = UI.ScrollGrid(host, out grid, new Vector2(220, 270), new Vector2(14, 14));
            UI.Stretch((RectTransform)sr.transform);
            Fill();
            UpdateStats();
        }

        public override void OnShow() => MenuScene3D.Show(MenuScene3D.Layout.Wardrobe);
        public override void OnHide() => MenuScene3D.Hide();

        void Fill()
        {
            UI.Clear(grid);
            var list = ClothesCatalog.BySlot((ClothesSlot)tab);
            // owned first, then by level
            list.Sort((a, b) =>
            {
                bool oa = Progression.OwnsClothes(a.id), ob = Progression.OwnsClothes(b.id);
                if (oa != ob) return oa ? -1 : 1;
                return a.level != b.level ? a.level.CompareTo(b.level) : string.CompareOrdinal(a.id, b.id);
            });
            foreach (var d in list)
            {
                var def = d;
                bool owned = Progression.OwnsClothes(d.id), worn = ClothesCatalog.IsWorn(d.id);
                var card = ItemCards.Card(grid, ClothesCatalog.IconPath(d.id), ClothesCatalog.DisplayName(d.id), () =>
                {
                    if (Progression.OwnsClothes(def.id))
                    {
                        ClothesCatalog.ToggleWear(def);
                        MenuScene3D.UpdateClothes();
                        MenuScene3D.Celebrate();
                        Fill();
                        UpdateStats();
                    }
                    else ClothesInfo.Show(def, () => { Fill(); UpdateStats(); });
                }, out var f, worn ? Theme.Primary : owned ? MetaUI.Card : new Color(0.75f, 0.8f, 0.9f), worn ? "ON" : null, Theme.Good);
                var l = UI.Label(f, owned ? ClothesCatalog.StatLine(d.id) : (d.slot == ClothesSlot.Trophy ? "Win a challenge" : "In the shop"), 22, owned ? Theme.Text : Theme.Muted);
                UI.Stretch(l.rectTransform);
                if (!owned) card.GetComponent<Image>().color = new Color(0.7f, 0.75f, 0.85f, 0.9f);
                if (!owned && d.slot == ClothesSlot.Trophy) ItemCards.LockOverlay(card.transform, "?");
            }
        }

        void UpdateStats()
        {
            var P = ProfileService.P;
            var worn = new[] { P.wornHead, P.wornChest, P.wornFeet, P.wornTrophy };
            statsText.text = StatRow(Loc.T("ATTACK_TITLE"), ClothesCatalog.Stat(worn, "Attack"), "#ff8a7a") + "\n" +
                             StatRow(Loc.T("DEFENCE_TITLE"), ClothesCatalog.Stat(worn, "Defence"), "#8ad0ff") + "\n" +
                             StatRow(Loc.T("LUCK_TITLE"), ClothesCatalog.Stat(worn, "Luck"), "#9cf08a");
        }

        static string StatRow(string name, ClothesCatalog.StatSum s, string color)
        {
            string v = (s.add >= 0 ? "+" : "") + s.add.ToString("0");
            if (Mathf.Abs(s.mul - 1f) > 0.001f) v += "  x" + s.mul.ToString("0.##");
            return "<color=" + color + ">" + name + "</color>   <b>" + v + "</b>";
        }

        /// <summary>Spins the menu penguin while dragging.</summary>
        class DragRotate : MonoBehaviour, IDragHandler
        {
            public void OnDrag(PointerEventData e) => MenuScene3D.Rotate(-e.delta.x * 0.4f);
        }
    }
}
