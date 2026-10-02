using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// Crafting / Research (original crafting screen): pick ingredients into a research slot, research for
    /// Tuner.ResearchDuration, finish early for ResearchInstantCompleteCost fish, collect the result.
    /// Known recipes are listed in the recipe book.
    /// </summary>
    public class CraftingScreen : MetaScreen
    {
        protected override string Title => Loc.T("BUTTON_CRAFTING");
        RectTransform invGrid, recipeList;
        readonly RectTransform[] labRoots = new RectTransform[CraftingCatalog.Labs];
        readonly Text[] timers = new Text[CraftingCatalog.Labs];
        readonly int[] lastSeconds = new int[CraftingCatalog.Labs];
        // ingredients placed in each idle lab (not yet started)
        static readonly List<string>[] pending = { new List<string>(), new List<string>() };
        /// <summary>Drop queued ingredients (after a progress reset).</summary>
        public static void ClearPending() { foreach (var l in pending) l.Clear(); }
        int selectedLab;

        protected override void BuildContent()
        {
            // ---- ingredients (left) ----
            var inv = MetaUI.CardPanel(Content, MetaUI.CardDark, "Ingredients");
            UI.Anchor(inv.rectTransform, 0, 0, 0.3f, 1);
            var it = UI.Label(inv.transform, "Ingredients", 38, Color.white, TextAnchor.MiddleLeft, true);
            UI.Anchor(it.rectTransform, 0.05f, 0.9f, 0.95f, 0.99f);
            var ih = UI.Rect(inv.transform, "Grid");
            UI.Anchor(ih, 0.01f, 0.1f, 0.99f, 0.9f);
            var sr = UI.ScrollGrid(ih, out invGrid, new Vector2(165, 205), new Vector2(10, 10));
            UI.Stretch((RectTransform)sr.transform);
            var how = UI.Label(inv.transform, "Win them in battles and at the slot machine.", 24, new Color(1, 1, 1, 0.8f));
            UI.Anchor(how.rectTransform, 0.04f, 0.01f, 0.96f, 0.1f);

            // ---- labs (middle) ----
            var labs = UI.Rect(Content, "Labs");
            UI.Anchor(labs, 0.315f, 0, 0.7f, 1);
            var v = UI.VBox(labs, 18, TextAnchor.UpperCenter);
            v.childForceExpandHeight = true;
            for (int i = 0; i < CraftingCatalog.Labs; i++)
            {
                var card = MetaUI.CardPanel(labs, MetaUI.Card, "Lab " + i);
                labRoots[i] = card.rectTransform;
            }

            // ---- recipe book (right) ----
            var book = MetaUI.CardPanel(Content, new Color32(255, 246, 220, 245), "Recipes");
            UI.Anchor(book.rectTransform, 0.715f, 0, 1, 1);
            var bt = UI.Label(book.transform, "Recipe book", 38, MetaUI.Orange, TextAnchor.MiddleLeft, true);
            UI.Anchor(bt.rectTransform, 0.06f, 0.9f, 0.95f, 0.99f);
            var bh = UI.Rect(book.transform, "List");
            UI.Anchor(bh, 0.01f, 0.01f, 0.99f, 0.9f);
            var bsr = UI.ScrollList(bh, out recipeList, true, 8, 10);
            UI.Stretch((RectTransform)bsr.transform);

            RefreshAll();
        }

        void RefreshAll()
        {
            FillInventory();
            for (int i = 0; i < CraftingCatalog.Labs; i++) BuildLab(i);
            FillRecipes();
        }

        int Available(string id)
        {
            int n = CraftingCatalog.Count(id);
            foreach (var p in pending) foreach (var x in p) if (x == id) n--;
            return n;
        }

        void FillInventory()
        {
            UI.Clear(invGrid);
            foreach (var ing in CraftingCatalog.Ingredients)
            {
                var d = ing;
                int n = Available(d.id);
                var card = ItemCards.Card(invGrid, "Crafting/" + d.id, d.name, () => AddToLab(d.id), out var f,
                    n > 0 ? MetaUI.Card : new Color(0.7f, 0.74f, 0.82f), n.ToString(), n > 0 ? (d.rare ? MetaUI.Purple : Theme.Secondary) : Theme.Muted);
                var l = UI.Label(f, d.rare ? "Rare" : "Common", 22, d.rare ? MetaUI.Purple : Theme.Muted, TextAnchor.MiddleCenter, true);
                UI.Stretch(l.rectTransform);
                // the fallback icon tile uses the ingredient color
                var tile = card.transform.Find("Box/IconTile");
                if (tile != null && ModelLibrary.Icon("Crafting/" + d.id) == null) tile.GetComponent<Image>().color = d.color;
            }
        }

        void AddToLab(string id)
        {
            if (Available(id) <= 0) { UI.Toast("You don't have any " + CraftingCatalog.Ingredient(id).name + "."); return; }
            int lab = selectedLab;
            if (CraftingCatalog.LabBusy(lab) || pending[lab].Count >= CraftingCatalog.SlotsPerLab)
            {
                lab = -1;
                for (int i = 0; i < CraftingCatalog.Labs; i++)
                    if (!CraftingCatalog.LabBusy(i) && pending[i].Count < CraftingCatalog.SlotsPerLab) { lab = i; break; }
            }
            if (lab < 0) { UI.Toast("All research slots are busy."); return; }
            selectedLab = lab;
            pending[lab].Add(id);
            AudioManager.Sfx("ButtonClick");
            FillInventory();
            BuildLab(lab);
        }

        void BuildLab(int i)
        {
            var root = labRoots[i];
            UI.Clear(root);
            timers[i] = null;
            lastSeconds[i] = -1;
            bool busy = CraftingCatalog.LabBusy(i);
            var title = UI.Label(root, "Research slot " + (i + 1), 36, Theme.Secondary, TextAnchor.MiddleLeft, true);
            UI.Anchor(title.rectTransform, 0.04f, 0.8f, 0.7f, 0.97f);
            if (!busy && i == selectedLab)
            {
                var sel = UI.Label(root, "selected", 24, Theme.Muted, TextAnchor.MiddleRight);
                UI.Anchor(sel.rectTransform, 0.6f, 0.8f, 0.96f, 0.97f);
            }
            var slotsRow = UI.Rect(root, "Slots");
            UI.Anchor(slotsRow, 0.04f, 0.3f, 0.96f, 0.78f);
            var h = UI.HBox(slotsRow, 14, TextAnchor.MiddleCenter);
            h.childForceExpandWidth = true; h.childForceExpandHeight = true;
            var ings = busy ? new List<string>(CraftingCatalog.LabIngredients(i)) : pending[i];
            for (int s = 0; s < CraftingCatalog.SlotsPerLab; s++)
            {
                int slotIndex = s, lab = i;
                if (s < ings.Count)
                {
                    var d = CraftingCatalog.Ingredient(ings[s]);
                    var b = UI.Button(slotsRow, null, () =>
                    {
                        if (CraftingCatalog.LabBusy(lab)) return;
                        pending[lab].RemoveAt(slotIndex);
                        FillInventory(); BuildLab(lab);
                    }, UI.ButtonStyle.Plain, 24, "Slot");
                    var tile = MetaUI.IconTile(b.transform, "Crafting/" + d.id, d.name, d.color);
                    UI.Stretch(tile, 8, 8, 8, 8);
                }
                else
                {
                    var b = UI.Button(slotsRow, "+", () => { selectedLab = lab; BuildLab(0); BuildLab(1); }, UI.ButtonStyle.Plain, 70, "Empty");
                    UI.SkinColor(b.GetComponent<Image>(), new Color(0.85f, 0.9f, 0.97f));
                }
            }
            var bottom = UI.Rect(root, "Bottom");
            UI.Anchor(bottom, 0.04f, 0.04f, 0.96f, 0.27f);
            if (!busy)
            {
                var guess = CraftingCatalog.Match(pending[i]);
                bool known = guess != null && ProfileService.P.knownRecipes.Contains(guess.id);
                var hint = UI.Label(bottom, pending[i].Count == 0 ? "Tap ingredients to add them." : known ? "Makes: " + guess.name : "Unknown mix... let's find out!", 28, Theme.Muted, TextAnchor.MiddleLeft);
                UI.Anchor(hint.rectTransform, 0, 0, 0.55f, 1);
                var go = UI.Button(bottom, "Research (" + MetaUI.Clock(CraftingCatalog.DurationMs / 1000.0) + ")", () =>
                {
                    if (CraftingCatalog.StartResearch(i, pending[i])) { pending[i].Clear(); RefreshAll(); }
                }, UI.ButtonStyle.Primary, 32);
                UI.Anchor((RectTransform)go.transform, 0.56f, 0, 1, 1);
                go.interactable = pending[i].Count > 0;
            }
            else if (CraftingCatalog.LabDone(i))
            {
                var c = UI.Button(bottom, "Collect!", () => Collect(i), UI.ButtonStyle.Good, 44);
                UI.Stretch((RectTransform)c.transform, 120, 120, 0, 0);
                c.gameObject.AddComponent<UIPulse>();
            }
            else
            {
                timers[i] = UI.Label(bottom, "", 40, Theme.Text, TextAnchor.MiddleLeft, true);
                UI.Anchor(timers[i].rectTransform, 0, 0, 0.5f, 1);
                timers[i].color = Theme.Secondary;
                var fin = UI.Button(bottom, "Finish now  " + CraftingCatalog.InstantCost + " fish", () => { if (CraftingCatalog.FinishNow(i)) BuildLab(i); }, UI.ButtonStyle.Good, 30);
                UI.Anchor((RectTransform)fin.transform, 0.5f, 0, 1, 1);
            }
        }

        void Collect(int lab)
        {
            var text = CraftingCatalog.Collect(lab, out var recipe);
            if (text == null) return;
            AudioManager.Sfx(recipe != null ? "Treasure" : "SlotMachineNoWin");
            UI.Message(recipe != null ? "Success!" : "Research done", text, Progression.ShowPendingLevelUpsAction);
            RefreshAll();
        }

        void FillRecipes()
        {
            UI.Clear(recipeList);
            var P = ProfileService.P;
            int known = 0;
            foreach (var r in CraftingCatalog.Recipes)
            {
                bool k = P.knownRecipes.Contains(r.id);
                if (k) known++;
                Image row;
                if (k)
                {
                    // like the original recipe-based research: tap a known recipe to load its ingredients
                    var rec = r;
                    var rb = UI.Button(recipeList, null, () => UseRecipe(rec), UI.ButtonStyle.Plain, 24, "Recipe");
                    row = rb.GetComponent<Image>();
                    UI.SkinColor(row, new Color(1, 1, 1, 0.9f));
                }
                else row = UI.Panel(recipeList, new Color(0, 0, 0, 0.06f), true, "Recipe");
                UI.Layout(row, -1, 120);
                var name = UI.Label(row.transform, k ? r.name + "  =  " + r.resultAmount + "x " + Progression.NameOf(r.resultId) : "??? undiscovered", 28, k ? Theme.Text : Theme.Muted, TextAnchor.UpperLeft, k);
                UI.Anchor(name.rectTransform, 0.04f, 0.5f, 0.98f, 0.95f);
                string ing = "";
                if (k) foreach (var x in r.ingredients) ing += (ing.Length > 0 ? " + " : "") + CraftingCatalog.Ingredient(x).name;
                else ing = r.ingredients.Length + " ingredients";
                var il = UI.Label(row.transform, ing, 24, Theme.Muted, TextAnchor.MiddleLeft);
                UI.Anchor(il.rectTransform, 0.04f, 0.05f, 0.98f, 0.5f);
            }
            var head = UI.Label(recipeList, known + " / " + CraftingCatalog.Recipes.Count + " discovered" + (known > 0 ? "\nTap a recipe to load it" : ""), 28, MetaUI.Orange, TextAnchor.MiddleCenter, true);
            UI.Layout(head, -1, known > 0 ? 80 : 50);
            head.transform.SetAsFirstSibling();
        }

        /// <summary>Put a known recipe's ingredients in the selected (or first idle) research slot.</summary>
        void UseRecipe(RecipeDef r)
        {
            int lab = -1;
            if (!CraftingCatalog.LabBusy(selectedLab)) lab = selectedLab;
            else for (int i = 0; i < CraftingCatalog.Labs; i++) if (!CraftingCatalog.LabBusy(i)) { lab = i; break; }
            if (lab < 0) { UI.Toast("All research slots are busy."); return; }
            var old = new List<string>(pending[lab]);
            pending[lab].Clear();   // its ingredients go back to the shelf first
            var missing = new List<string>();
            var need = new Dictionary<string, int>();
            foreach (var x in r.ingredients) need[x] = (need.TryGetValue(x, out var n) ? n : 0) + 1;
            foreach (var kv in need)
                if (Available(kv.Key) < kv.Value) missing.Add((kv.Value - Mathf.Max(0, Available(kv.Key))) + "x " + CraftingCatalog.Ingredient(kv.Key).name);
            if (missing.Count > 0)
            {
                pending[lab].AddRange(old);
                AudioManager.Sfx("Nomoney");
                UI.Toast("Missing " + string.Join(", ", missing.ToArray()));
                return;
            }
            pending[lab].AddRange(r.ingredients);
            selectedLab = lab;
            AudioManager.Sfx("ButtonClick");
            FillInventory();
            for (int i = 0; i < CraftingCatalog.Labs; i++) BuildLab(i);
        }

        public override void Tick(float dt)
        {
            for (int i = 0; i < CraftingCatalog.Labs; i++)
            {
                if (timers[i] == null) continue;
                int secs = (int)CraftingCatalog.LabSecondsLeft(i);
                if (secs == lastSeconds[i]) continue;
                lastSeconds[i] = secs;
                if (secs <= 0) { BuildLab(i); continue; }
                timers[i].text = MetaUI.Clock(secs);
            }
        }
    }
}
