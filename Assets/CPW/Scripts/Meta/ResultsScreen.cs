using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// After-battle results (MetaHooks.ResultsScreen): podium, score table, rewards with VIP and bet lines, earned
    /// items, an animated XP bar with level-ups, then Rematch or Home.
    /// </summary>
    public class ResultsScreen : MetaScreen
    {
        readonly BattleResult result;
        readonly RewardSummary reward;
        Image xpFill;
        Text levelText, xpText;
        int[] thresholds;           // XP needed for levels [oldLevel .. newLevel + 1]
        float animXp, animT;
        int shownLevel, lastShownXp = -1;
        bool animDone;
        protected override bool ShowBack => false;
        protected override string Title => "";

        public ResultsScreen(BattleResult r)
        {
            result = r;
            reward = RewardService.Last ?? new RewardSummary { oldXp = ProfileService.P.xp, newXp = ProfileService.P.xp, oldLevel = ProfileService.P.level, newLevel = ProfileService.P.level };
        }

        PlayerResult Local => result?.Local;

        protected override void BuildContent()
        {
            var me = Local;
            bool won = me != null && me.rank == 1;
            bool practice = result.config != null && (result.config.mode == BattleMode.Practice || result.config.mode == BattleMode.Tutorial);
            string head = result.config != null && result.config.mode == BattleMode.Custom ? (WinnerName() + " wins!") : won ? "Victory!" : me != null && me.rank == 2 ? "So close!" : "Defeat";
            var title = UI.Label(Root, head, 110, won ? MetaUI.Gold : Color.white, TextAnchor.MiddleCenter, true);
            title.rectTransform.anchorMin = new Vector2(0, 1); title.rectTransform.anchorMax = new Vector2(1, 1);
            title.rectTransform.pivot = new Vector2(0.5f, 1);
            title.rectTransform.sizeDelta = new Vector2(0, 130);
            title.rectTransform.anchoredPosition = new Vector2(0, -MetaUI.TopBarHeight);
            title.gameObject.AddComponent<UIPulse>().amount = 0.03f;
            Content.offsetMax = new Vector2(Content.offsetMax.x, -(MetaUI.TopBarHeight + 130));

            // ---- podium + table (left) ----
            var left = MetaUI.CardPanel(Content, MetaUI.CardDark, "Ranking");
            UI.Anchor(left.rectTransform, 0, 0.14f, 0.55f, 1);
            Podium(left.rectTransform);
            Table(left.rectTransform);

            // ---- rewards (right) ----
            var right = MetaUI.CardPanel(Content, MetaUI.Card, "Rewards");
            UI.Anchor(right.rectTransform, 0.57f, 0.14f, 1, 1);
            Rewards(right.rectTransform, practice);

            // ---- buttons ----
            var home = UI.Button(Content, Loc.T("RESULTS_HOME"), GameManager.GoHome, UI.ButtonStyle.Secondary, 48);
            UI.Place((RectTransform)home.transform, new Vector2(0, 0), new Vector2(360, 110), Vector2.zero);
            bool canRematch = result.config != null && result.config.mode != BattleMode.Online && result.config.mode != BattleMode.Tutorial;
            if (canRematch)
            {
                var re = UI.Button(Content, Loc.T("RESULTS_REMATCH"), Rematch, UI.ButtonStyle.Primary, 52);
                UI.Place((RectTransform)re.transform, new Vector2(1, 0), new Vector2(420, 110), Vector2.zero);
                re.gameObject.AddComponent<UIPulse>().amount = 0.03f;
            }
            if (!won && !practice)
            {
                var shop = UI.Button(Content, Loc.T("RESULTS_GET_GUNS"), () => ScreenManager.Show(() => new ShopScreen(2)), UI.ButtonStyle.Good, 40);
                UI.Place((RectTransform)shop.transform, new Vector2(0.5f, 0), new Vector2(360, 110), new Vector2(0, 0));
            }
        }

        string WinnerName()
        {
            foreach (var p in result.players) if (p.rank == 1) return p.name;
            return "Nobody";
        }

        void Podium(RectTransform parent)
        {
            var area = UI.Rect(parent, "Podium");
            UI.Anchor(area, 0.05f, 0.42f, 0.95f, 0.98f);
            var sorted = new List<PlayerResult>(result.players);
            sorted.Sort((a, b) => a.rank.CompareTo(b.rank));
            // order on screen: 2nd, 1st, 3rd
            int[] place = { 1, 0, 2 };
            float[] heights = { 0.55f, 0.75f, 0.4f };
            for (int col = 0; col < 3; col++)
            {
                int idx = place[col];
                if (idx >= sorted.Count) continue;
                var p = sorted[idx];
                float x0 = col / 3f, x1 = (col + 1) / 3f;
                var block = UI.Panel(area, idx == 0 ? MetaUI.Gold : idx == 1 ? new Color32(205, 210, 220, 255) : new Color32(215, 140, 70, 255), true, "Place " + (idx + 1));
                UI.Anchor(block.rectTransform, x0 + 0.02f, 0, x1 - 0.02f, heights[col] * 0.62f);
                var num = UI.Label(block.transform, (idx + 1).ToString(), 80, Color.white, TextAnchor.MiddleCenter, true);
                UI.Stretch(num.rectTransform);
                var color = Theme.PlayerColors[ColorOf(p) % 4];
                var dot = UI.Image(area, UI.Circle, color, true, "Penguin");
                UI.Anchor(dot.rectTransform, x0 + 0.08f, heights[col] * 0.62f + 0.02f, x1 - 0.08f, heights[col] * 0.62f + 0.24f);
                var initials = UI.Label(dot.transform, MetaUI.Initials(p.name), 44, Color.white, TextAnchor.MiddleCenter, true);
                UI.Stretch(initials.rectTransform);
                var name = UI.Label(area, p.name, 32, p.isLocal ? MetaUI.Gold : Color.white, TextAnchor.MiddleCenter, true);
                UI.Anchor(name.rectTransform, x0, heights[col] * 0.62f + 0.25f, x1, heights[col] * 0.62f + 0.36f);
                var score = UI.Label(area, p.score + " pts", 28, Color.white);
                UI.Anchor(score.rectTransform, x0, heights[col] * 0.62f + 0.35f, x1, heights[col] * 0.62f + 0.44f);
                if (idx == 0) AudioManager.Sfx("Position_1");
            }
        }

        int ColorOf(PlayerResult p)
        {
            var c = result.config;
            if (c != null && p.slotIndex >= 0 && p.slotIndex < c.players.Count) return c.players[p.slotIndex].colorIndex;
            return p.slotIndex;
        }

        void Table(RectTransform parent)
        {
            var table = UI.Rect(parent, "Table");
            UI.Anchor(table, 0.03f, 0.02f, 0.97f, 0.4f);
            var v = UI.VBox(table, 4, TextAnchor.UpperCenter);
            v.childForceExpandHeight = false;
            Row(table, "#", "Penguin", "Score", "KOs", "Deaths", Theme.Xp, false);
            var sorted = new List<PlayerResult>(result.players);
            sorted.Sort((a, b) => a.rank.CompareTo(b.rank));
            foreach (var p in sorted) Row(table, p.rank.ToString(), p.name, p.score.ToString(), p.kills.ToString(), p.deaths.ToString(), p.isLocal ? MetaUI.Gold : Color.white, true);
        }

        static void Row(RectTransform parent, string rank, string name, string score, string kills, string deaths, Color c, bool body)
        {
            var row = UI.Rect(parent, "Row");
            UI.Layout(row, -1, 50);
            float[] xs = { 0, 0.08f, 0.52f, 0.7f, 0.85f, 1 };
            string[] vals = { rank, name, score, kills, deaths };
            for (int i = 0; i < vals.Length; i++)
            {
                var l = UI.Label(row, vals[i], body ? 30 : 26, c, i == 1 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, !body);
                UI.Anchor(l.rectTransform, xs[i], 0, xs[i + 1], 1);
            }
        }

        void Rewards(RectTransform parent, bool practice)
        {
            var v = UI.VBox(parent, 6, TextAnchor.UpperLeft, 26);
            v.childForceExpandHeight = false;
            var head = UI.Label(parent, "Rewards", 44, Theme.Secondary, TextAnchor.MiddleLeft, true);
            UI.Layout(head, -1, 60);
            if (practice || reward.practice)
            {
                var l = UI.Label(parent, result.config != null && result.config.mode == BattleMode.Tutorial
                    ? "Training complete! You're ready for real battles."
                    : "Practice and custom games don't give coins or XP. Play a Quick Match to earn rewards!", 32, Theme.Text, TextAnchor.UpperLeft);
                UI.Layout(l, -1, 120);
            }
            else
            {
                Line(parent, Loc.T("RESULTS_GAME"), "coin", reward.baseCoins, Theme.Text);
                if (reward.vipCoins > 0) Line(parent, Loc.T("RESULTS_VIP"), "coin", reward.vipCoins, MetaUI.Orange);
                if (reward.betPlaced)
                {
                    bool betCash = reward.betCash != 0;
                    int amt = betCash ? reward.betCash : reward.betCoins;
                    Line(parent, amt > 0 ? "Bet won!" : Loc.T("BETLOST"), betCash ? "cash" : "coin", amt, amt > 0 ? Theme.Good : Theme.Danger);
                }
                if (reward.cash > 0) Line(parent, "Fish found", "cash", reward.cash, Theme.Good);
                Line(parent, "XP", "xp", reward.TotalXp, Theme.Secondary);
            }
            if (reward.items.Count > 0)
            {
                var il = UI.Label(parent, "Loot", 32, Theme.Secondary, TextAnchor.MiddleLeft, true);
                UI.Layout(il, -1, 44);
                var row = UI.Rect(parent, "Items");
                UI.Layout(row, -1, 120);
                var h = UI.HBox(row, 10, TextAnchor.MiddleLeft);
                h.childForceExpandWidth = false;
                foreach (var it in reward.items)
                {
                    var tile = MetaUI.IconTile(row, Progression.IconOf(it.id), Progression.NameOf(it.id));
                    UI.Layout(tile, 110, 110);
                    var c = MetaUI.Badge(tile, "x" + it.amount, Theme.Secondary, 46);
                    c.fontSize = 22;
                    var rt = (RectTransform)c.transform.parent;
                    rt.anchorMin = rt.anchorMax = new Vector2(1, 0);
                    rt.anchoredPosition = new Vector2(-10, 10);
                }
            }

            // animated XP bar
            var xpRow = UI.Rect(parent, "XpRow");
            UI.Layout(xpRow, -1, 90);
            var badge = MetaUI.Badge(xpRow, reward.oldLevel.ToString(), Theme.Xp, 84);
            levelText = badge;
            var brt = (RectTransform)badge.transform.parent;
            brt.anchorMin = brt.anchorMax = new Vector2(0, 0.5f);
            brt.anchoredPosition = new Vector2(44, 0);
            var barHost = UI.Rect(xpRow, "Bar");
            UI.Anchor(barHost, 0.15f, 0.2f, 1, 0.8f);
            xpFill = UI.Bar(barHost, Theme.Xp);
            UI.Stretch((RectTransform)xpFill.transform.parent);
            xpText = UI.Label(barHost, "", 26, Color.white, TextAnchor.MiddleCenter, true);
            UI.Stretch(xpText.rectTransform);

            thresholds = new int[Mathf.Max(2, reward.newLevel - reward.oldLevel + 2)];
            for (int i = 0; i < thresholds.Length; i++) thresholds[i] = GameData.XpForLevel(reward.oldLevel + i);
            animXp = reward.oldXp;
            shownLevel = reward.oldLevel;
            SetXpBar();
        }

        static void Line(RectTransform parent, string label, string kind, int amount, Color color)
        {
            var row = UI.Rect(parent, label);
            UI.Layout(row, -1, 54);
            var l = UI.Label(row, label, 32, Theme.Text, TextAnchor.MiddleLeft);
            UI.Anchor(l.rectTransform, 0, 0, 0.55f, 1);
            var a = UI.Rect(row, "Amount");
            UI.Anchor(a, 0.55f, 0, 1, 1);
            UI.HBox(a, 6, TextAnchor.MiddleRight).childForceExpandWidth = false;
            MetaUI.Amount(a, kind, (amount >= 0 ? "+" : "") + amount.ToString("N0"), 34, color);
        }

        void SetXpBar()
        {
            int li = Mathf.Clamp(shownLevel - reward.oldLevel, 0, thresholds.Length - 2);
            int a = thresholds[li], b = thresholds[li + 1];
            xpFill.fillAmount = b == int.MaxValue || b <= a ? 1f : Mathf.Clamp01((animXp - a) / (b - a));
            int xi = Mathf.RoundToInt(animXp);
            if (xi != lastShownXp && (animDone || Time.frameCount % 6 == 0))
            {
                lastShownXp = xi;
                xpText.text = b == int.MaxValue ? "MAX" : xi.ToString("N0") + " / " + b.ToString("N0");
            }
        }

        public override void Tick(float dt)
        {
            if (animDone || thresholds == null) return;
            animT += dt;
            if (animT < 0.6f) return;   // let the screen settle first
            float k = Mathf.Clamp01((animT - 0.6f) / 2f);
            animXp = Mathf.Lerp(reward.oldXp, reward.newXp, 1 - (1 - k) * (1 - k));
            int li = shownLevel - reward.oldLevel;
            if (li + 1 < thresholds.Length && animXp >= thresholds[li + 1] && shownLevel < reward.newLevel)
            {
                shownLevel++;
                levelText.text = shownLevel.ToString();
                AudioManager.Sfx("LevelUpSound");
            }
            if (k >= 1)
            {
                animDone = true;
                animXp = reward.newXp;
                lastShownXp = -1;
                Progression.ShowPendingLevelUps();
            }
            SetXpBar();
        }

        void Rematch()
        {
            ProfileService.P.AddCounter("Games_Rematch", 1);
            ProfileService.Save();
            var c = BattleFactory.Rematch(result.config);
            if (c == null) { GameManager.GoHome(); return; }
            if (c.mode == BattleMode.QuickMatch || c.mode == BattleMode.Practice) ScreenManager.Show(() => new LoadoutScreen(c));
            else BattleFactory.Launch(c);
        }

        public override bool OnBack() { GameManager.GoHome(); return true; }
    }
}
