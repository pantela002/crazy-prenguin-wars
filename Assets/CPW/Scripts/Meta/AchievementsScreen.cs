using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>Challenges (4 chains with trophies), the original 95 Achievements with coin rewards, and the trophy case.</summary>
    public class AchievementsScreen : MetaScreen
    {
        static int tab;
        RectTransform body;
        List<Button> tabs;
        protected override string Title => Loc.T("ACHIEVEMENTS_HEADER");

        protected override void BuildContent()
        {
            var row = UI.Rect(Content, "Tabs");
            UI.Anchor(row, 0, 0.89f, 1, 1);
            tabs = MetaUI.Tabs(row, new[] { "Challenges", Loc.T("ACHIEVEMENTS_HEADER"), Loc.T("TAB_TROPHY") }, tab, i => { tab = i; MetaUI.SetTabSelected(tabs, i); Fill(); }, 36);
            body = UI.Rect(Content, "Body");
            UI.Anchor(body, 0, 0, 1, 0.87f);
            Fill();
        }

        void Fill()
        {
            UI.Clear(body);
            // the challenges tab puts a layout group on body; the other tabs must not inherit it
            var lg = body.GetComponent<LayoutGroup>();
            if (lg) Object.DestroyImmediate(lg);
            if (tab == 0) Challenges();
            else if (tab == 1) Achievements();
            else Trophies();
        }

        // ---------- challenges ----------
        void Challenges()
        {
            var h = UI.HBox(body, 18, TextAnchor.MiddleCenter);
            h.childForceExpandWidth = true; h.childForceExpandHeight = true;
            for (int c = 0; c < ChallengeCatalog.ChainNames.Length; c++)
            {
                var col = ChallengeCatalog.ChainColors[c];
                var card = MetaUI.CardPanel(body, col, "Chain " + c);
                var name = UI.Label(card.transform, ChallengeCatalog.ChainNames[c], 44, Color.white, TextAnchor.MiddleCenter, true);
                UI.Anchor(name.rectTransform, 0.04f, 0.88f, 0.96f, 0.98f);
                int done = 0, total = 0;
                foreach (var d in ChallengeCatalog.All) if (d.chain == c) { total++; if (ChallengeCatalog.Completed(d)) done++; }
                var prog = UI.Label(card.transform, done + " / " + total + " complete", 28, Color.white);
                UI.Anchor(prog.rectTransform, 0.04f, 0.82f, 0.96f, 0.88f);

                var inner = UI.Panel(card.transform, new Color(1, 1, 1, 0.95f), true, "Active");
                UI.Anchor(inner.rectTransform, 0.05f, 0.22f, 0.95f, 0.8f);
                var a = ChallengeCatalog.Active(c);
                if (a == null)
                {
                    var l = UI.Label(inner.transform, "All done!\nYou are a legend.", 40, col, TextAnchor.MiddleCenter, true);
                    UI.Stretch(l.rectTransform, 10, 10, 10, 10);
                }
                else
                {
                    var tile = MetaUI.IconTile(MetaUI.Box(inner.transform, 0.3f, 0.6f, 0.7f, 0.96f), ClothesCatalog.IconPath(a.trophy), ClothesCatalog.DisplayName(a.trophy), MetaUI.Gold);
                    MetaUI.Square(tile);
                    var t = UI.Label(inner.transform, a.name, 36, Theme.Text, TextAnchor.MiddleCenter, true);
                    t.color = col;
                    UI.Anchor(t.rectTransform, 0.04f, 0.48f, 0.96f, 0.6f);
                    var d = UI.Label(inner.transform, a.Text, 30, Theme.Text);
                    UI.Anchor(d.rectTransform, 0.06f, 0.28f, 0.94f, 0.48f);
                    var barHost = UI.Rect(inner.transform, "Bar");
                    UI.Anchor(barHost, 0.08f, 0.17f, 0.92f, 0.26f);
                    int p = Mathf.Min(ChallengeCatalog.Progress(a), a.target);
                    MetaUI.ProgressBar(barHost, p / (float)a.target, col, p + " / " + a.target);
                    UI.Stretch((RectTransform)barHost.GetChild(0));
                    var rw = UI.Label(inner.transform, "Reward: " + a.coins + " coins, " + a.xp + " XP" + (a.cash > 0 ? ", " + a.cash + " fish" : "") + "\n+ " + ClothesCatalog.DisplayName(a.trophy), 24, Theme.Muted);
                    UI.Anchor(rw.rectTransform, 0.04f, 0.01f, 0.96f, 0.16f);
                }
                // upcoming in the chain
                var next = UI.Rect(card.transform, "Upcoming");
                UI.Anchor(next, 0.05f, 0.02f, 0.95f, 0.2f);
                var hb = UI.HBox(next, 6, TextAnchor.MiddleCenter);
                hb.childForceExpandWidth = true; hb.childForceExpandHeight = true;
                foreach (var d in ChallengeCatalog.All)
                {
                    if (d.chain != c) continue;
                    var dot = UI.Image(next, UI.Circle, ChallengeCatalog.Completed(d) ? MetaUI.Gold : d == a ? Color.white : new Color(1, 1, 1, 0.3f), true);
                    dot.preserveAspect = true;
                }
            }
        }

        // ---------- achievements ----------
        void Achievements()
        {
            var sr = UI.ScrollList(body, out var list, true, 10, 10);
            UI.Stretch((RectTransform)sr.transform);
            int claimable = AchievementCatalog.Claimable();
            if (claimable >= 2)
            {
                // one tap instead of a long scroll of Claim buttons
                UI.Anchor((RectTransform)sr.transform, 0, 0, 1, 0.89f);
                var claimAll = UI.Button(body, Loc.T("ACHIEVEMENTS_CLAIM") + " all (" + claimable + ")", ClaimAll, UI.ButtonStyle.Good, 34);
                UI.Anchor((RectTransform)claimAll.transform, 0.68f, 0.9f, 1, 1);
                claimAll.gameObject.AddComponent<UIPulse>();
            }
            var all = new List<Record>(AchievementCatalog.All);
            // claimable first, then unfinished, then claimed
            all.Sort((x, y) => Rank(x).CompareTo(Rank(y)));
            foreach (var a in all) AchievementRow(list, a);
        }

        void ClaimAll()
        {
            int coins = 0;
            foreach (var a in AchievementCatalog.All)
                if (AchievementCatalog.Done(a) && !AchievementCatalog.Claimed(a)) { coins += a.Int("GCReward"); AchievementCatalog.Claim(a); }
            if (coins > 0) UI.Toast("+" + coins + " coins", Theme.Coin);
            Fill();
        }

        static int Rank(Record a)
        {
            if (AchievementCatalog.Done(a) && !AchievementCatalog.Claimed(a)) return 0;
            if (!AchievementCatalog.Done(a)) return 1;
            return 2;
        }

        void AchievementRow(RectTransform list, Record a)
        {
            bool done = AchievementCatalog.Done(a), claimed = AchievementCatalog.Claimed(a);
            bool hidden = a.Bool("IsHidden") && !done;
            var row = MetaUI.CardPanel(list, claimed ? new Color(0.85f, 0.95f, 0.85f, 0.95f) : MetaUI.Card, "Ach " + a.Id);
            UI.Layout(row, -1, 130);
            var tile = MetaUI.IconTile(row.transform, "Achievements/" + a.Id, AchievementCatalog.Title(a), done ? MetaUI.Gold : Theme.Muted);
            UI.Place(tile, new Vector2(0, 0.5f), new Vector2(110, 110), new Vector2(12, 0));
            var t = UI.Label(row.transform, hidden ? "???" : AchievementCatalog.Title(a), 36, Theme.Text, TextAnchor.MiddleLeft, true);
            t.color = Theme.Secondary;
            UI.Anchor(t.rectTransform, 0.09f, 0.52f, 0.6f, 0.95f);
            var d = UI.Label(row.transform, hidden ? "A secret achievement." : AchievementCatalog.Description(a), 28, Theme.Text, TextAnchor.MiddleLeft);
            UI.Anchor(d.rectTransform, 0.09f, 0.08f, 0.6f, 0.52f);
            var barHost = UI.Rect(row.transform, "Bar");
            UI.Anchor(barHost, 0.62f, 0.3f, 0.8f, 0.7f);
            int p = Mathf.Min(AchievementCatalog.Progress(a), AchievementCatalog.Target(a));
            MetaUI.ProgressBar(barHost, p / (float)AchievementCatalog.Target(a), done ? Theme.Good : Theme.Secondary, p + "/" + AchievementCatalog.Target(a));
            UI.Stretch((RectTransform)barHost.GetChild(0));
            var right = UI.Rect(row.transform, "Right");
            UI.Anchor(right, 0.81f, 0.12f, 0.99f, 0.88f);
            if (claimed)
            {
                var l = UI.Label(right, Loc.T("ACHIEVEMENTS_CLAIMED"), 32, Theme.Good, TextAnchor.MiddleCenter, true);
                UI.Stretch(l.rectTransform);
            }
            else if (done)
            {
                var rec = a;
                var b = UI.Button(right, Loc.T("ACHIEVEMENTS_CLAIM") + " " + a.Int("GCReward"), () => { AchievementCatalog.Claim(rec); UI.Toast("+" + rec.Int("GCReward") + " coins", Theme.Coin); Fill(); }, UI.ButtonStyle.Good, 32);
                UI.Stretch((RectTransform)b.transform);
                b.gameObject.AddComponent<UIPulse>();
            }
            else
            {
                var host = UI.Rect(right, "Reward");
                UI.Stretch(host);
                UI.HBox(host, 6, TextAnchor.MiddleCenter).childForceExpandWidth = false;
                MetaUI.Amount(host, "coin", a.Int("GCReward").ToString(), 32);
            }
        }

        // ---------- trophies ----------
        void Trophies()
        {
            var panel = MetaUI.CardPanel(body, MetaUI.CardDark);
            UI.Stretch(panel.rectTransform);
            var sr = UI.ScrollGrid(panel.transform, out var grid, new Vector2(220, 270), new Vector2(14, 14));
            UI.Stretch((RectTransform)sr.transform, 6, 6, 6, 6);
            foreach (var d in ClothesCatalog.BySlot(ClothesSlot.Trophy))
            {
                var def = d;
                bool owned = Progression.OwnsClothes(d.id);
                var card = ItemCards.Card(grid, ClothesCatalog.IconPath(d.id), ClothesCatalog.DisplayName(d.id), () => ClothesInfo.Show(def, Fill), out var f,
                    owned ? MetaUI.Card : new Color(0.6f, 0.65f, 0.75f), ClothesCatalog.IsWorn(d.id) ? "ON" : null, Theme.Good);
                var l = UI.Label(f, owned ? ClothesCatalog.StatLine(d.id) : "Locked", 22, owned ? Theme.Text : Theme.Muted);
                UI.Stretch(l.rectTransform);
                if (!owned) ItemCards.LockOverlay(card.transform, "?");
            }
        }
    }
}
