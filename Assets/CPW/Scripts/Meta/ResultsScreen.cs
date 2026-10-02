using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// After-battle results (MetaHooks.ResultsScreen): podium, score table, rewards with VIP and bet lines, earned
    /// items, an animated XP bar with level-ups, an after-match deal (original AfterResultSalesScreen), then Rematch or
    /// Home. Online matches get the original rematch offer: a TimeToStartRematch countdown and the ready state of every
    /// player (FirebaseService.Rematch.cs).
    /// </summary>
    public class ResultsScreen : MetaScreen
    {
        const float DealDiscount = 0.25f;   // invented: the original AfterResultSales prices are not in the config

        // online rematch
        static string offeredMatchId;       // the offer is made once per match (the screen is rebuilt after the shop)
        OnlineRematch rematch;
        Button rematchBtn;
        Text rematchText;
        RectTransform rematchLayer, rematchSlots;
        Text rematchCountdown, rematchStatus;
        bool rematchPopupOpen, startingBattle;
        int rematchShownVersion = -1, rematchShownSecs = -1;
        FirebaseService Fs => Online.Service as FirebaseService;

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
            bool online = result.config != null && result.config.mode == BattleMode.Online;
            bool canRematch = result.config != null && !online && result.config.mode != BattleMode.Tutorial;
            if (canRematch)
            {
                var re = UI.Button(Content, Loc.T("RESULTS_REMATCH"), Rematch, UI.ButtonStyle.Primary, 52);
                UI.Place((RectTransform)re.transform, new Vector2(1, 0), new Vector2(420, 110), Vector2.zero);
                re.gameObject.AddComponent<UIPulse>().amount = 0.03f;
            }
            if (online) OnlineRematchButton();
            bool rewarded = result.config != null && (result.config.mode == BattleMode.QuickMatch || online);
            var deal = rewarded ? PickDeal(won) : null;
            if (deal != null) DealCard(deal);
            else if (!won && !practice)
            {
                var shop = UI.Button(Content, Loc.T("RESULTS_GET_GUNS"), () => ScreenManager.Show(() => new ShopScreen(2)), UI.ButtonStyle.Good, 40);
                UI.Place((RectTransform)shop.transform, new Vector2(0.5f, 0), new Vector2(360, 110), new Vector2(0, 0));
            }
        }

        // ------------------------------------------------------------------ after-match deal

        /// <summary>
        /// The AfterResultsSalesWinner / AfterResultsSalesLoser lists (the loser list falls back to the winner one),
        /// keeping items the player can buy now; otherwise a random unlocked shop weapon.
        /// </summary>
        static Record PickDeal(bool won)
        {
            var pool = new List<Record>();
            var sec = GameData.Section(won ? "AfterResultsSalesWinner" : "AfterResultsSalesLoser");
            if (sec.Count == 0) sec = GameData.Section("AfterResultsSalesWinner");
            foreach (var r in sec.Values)
            {
                var it = ItemCatalog.Real(GameData.Item(r.Str("ItemId", "")));
                if (Buyable(it) && !pool.Contains(it)) pool.Add(it);
            }
            if (pool.Count == 0)
                foreach (var it in ItemCatalog.ShopItems("Weapon")) if (Buyable(it)) pool.Add(it);
            return pool.Count == 0 ? null : pool[Random.Range(0, pool.Count)];
        }

        static bool Buyable(Record it) =>
            it != null && ItemCatalog.IsUnlocked(it) && !ItemCatalog.VipBlocked(it) && ItemCatalog.PriceCoins(it) + ItemCatalog.PriceCash(it) > 0;

        static int Discounted(int price) => price <= 0 ? 0 : Mathf.Max(1, Mathf.CeilToInt(price * (1f - DealDiscount)));

        void DealCard(Record item)
        {
            int coins = Discounted(ItemCatalog.PriceCoins(item)), cash = Discounted(ItemCatalog.PriceCash(item));
            if (cash > 0) coins = 0;   // priced in Cash (like Price())
            int amount = ItemCatalog.AmountPurchased(item);
            var card = MetaUI.CardPanel(Content, MetaUI.Card, "Deal");
            var rt = card.rectTransform;
            rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0);
            rt.offsetMin = new Vector2(380, 0); rt.offsetMax = new Vector2(-440, 110);
            var tile = MetaUI.IconTile(card.transform, ItemCatalog.IconPath(item), ItemCatalog.Name(item));
            UI.Place(tile, new Vector2(0, 0.5f), new Vector2(96, 96), new Vector2(8, 0));
            var tag = MetaUI.Badge(tile, "-" + Mathf.RoundToInt(DealDiscount * 100) + "%", Theme.Danger, 52);
            tag.fontSize = 20;
            var trt = (RectTransform)tag.transform.parent;
            trt.anchorMin = trt.anchorMax = new Vector2(1, 1);
            trt.anchoredPosition = new Vector2(-6, -6);
            var name = UI.Label(card.transform, "Deal: " + amount + "x " + ItemCatalog.Name(item), 30, Theme.Text, TextAnchor.MiddleLeft, true);
            UI.Anchor(name.rectTransform, 0, 0.45f, 0.5f, 1);
            name.rectTransform.offsetMin = new Vector2(116, 0);
            var priceRow = UI.Rect(card.transform, "Price");
            UI.Anchor(priceRow, 0, 0.04f, 0.5f, 0.5f);
            priceRow.offsetMin = new Vector2(116, 0);
            UI.HBox(priceRow, 6, TextAnchor.MiddleLeft).childForceExpandWidth = false;
            MetaUI.Price(priceRow, coins, cash, 30, Theme.Good);
            var was = UI.Label(priceRow, "(was " + UI.Money(cash > 0 ? ItemCatalog.PriceCash(item) : ItemCatalog.PriceCoins(item)) + ")", 24, Theme.Muted, TextAnchor.MiddleLeft);
            UI.Layout(was, 170, 40);
            Button buy = null;
            buy = UI.Button(card.transform, Loc.T("BUY"), () =>
            {
                if (!Progression.Spend(coins, cash)) { AudioManager.Sfx("Nomoney"); Progression.NotEnough(cash > 0); return; }
                ProfileService.P.AddAmmo(item.Id, amount);
                ProfileService.Save();
                AudioManager.Sfx("Buy");
                UI.Toast("+" + amount + " " + ItemCatalog.Name(item), Theme.Good);
                buy.interactable = false;
                var l = buy.GetComponentInChildren<Text>();
                if (l) l.text = "Bought!";
            }, UI.ButtonStyle.Good, 34);
            UI.Anchor((RectTransform)buy.transform, 0.5f, 0.1f, 0.74f, 0.9f);
            var shop = UI.Button(card.transform, "Go to shop", () => ScreenManager.Show(() => new ShopScreen()), UI.ButtonStyle.Secondary, 30);
            UI.Anchor((RectTransform)shop.transform, 0.76f, 0.1f, 0.98f, 0.9f);
        }

        // ------------------------------------------------------------------ online rematch

        void OnlineRematchButton()
        {
            var fs = Fs;
            var net = result.config.network as FirebaseBattleNetwork;
            if (fs != null && fs.Available && net != null && offeredMatchId != net.MatchId)
            {
                offeredMatchId = net.MatchId;
                rematch = fs.BeginRematch(result.config);
            }
            if (rematch == null)
            {
                var l = UI.Label(Content, fs != null && fs.Available ? "" : "Connect to play online again.", 30, Color.white, TextAnchor.MiddleRight);
                UI.Place(l.rectTransform, new Vector2(1, 0), new Vector2(420, 110), Vector2.zero);
                return;
            }
            rematch.started = OnRematchStarted;
            rematchBtn = UI.Button(Content, "", RematchPressed, UI.ButtonStyle.Primary, 44);
            UI.Place((RectTransform)rematchBtn.transform, new Vector2(1, 0), new Vector2(420, 110), Vector2.zero);
            rematchBtn.gameObject.AddComponent<UIPulse>().amount = 0.03f;
            rematchText = rematchBtn.GetComponentInChildren<Text>();
            UpdateRematchUi();
        }

        void RematchPressed()
        {
            if (rematch == null || rematch.finished) return;
            if (!rematch.localReady)
            {
                ProfileService.P.AddCounter("Games_Rematch", 1);
                ProfileService.Save();
                Fs?.RematchReady(rematch);
            }
            OpenRematchPopup();
        }

        void OpenRematchPopup()
        {
            if (rematchPopupOpen) return;
            var win = MetaUI.Window(Loc.T("RESULTS_REMATCH"), new Vector2(1000, 640), out rematchLayer);
            rematchPopupOpen = true;
            rematchCountdown = UI.Label(win, "", 64, Theme.Secondary, TextAnchor.MiddleCenter, true);
            UI.Anchor(rematchCountdown.rectTransform, 0.05f, 0.7f, 0.95f, 0.82f);
            rematchSlots = UI.Rect(win, "Slots");
            UI.Anchor(rematchSlots, 0.06f, 0.3f, 0.94f, 0.69f);
            UI.VBox(rematchSlots, 8, TextAnchor.UpperCenter);
            rematchStatus = UI.Label(win, "", 30, Theme.Muted);
            UI.Anchor(rematchStatus.rectTransform, 0.05f, 0.2f, 0.95f, 0.29f);
            var no = UI.Button(win, Loc.T("REMATCH_LEFT"), () => { MetaUI.Close(rematchLayer); LeaveRematch(); }, UI.ButtonStyle.Danger, 36);
            UI.Anchor((RectTransform)no.transform, 0.3f, 0.03f, 0.7f, 0.18f);
            rematchShownVersion = -1;
            UpdateRematchUi();
        }

        void LeaveRematch()
        {
            rematchPopupOpen = false;
            if (rematch != null && !rematch.finished) Fs?.RematchLeave(rematch);
            UpdateRematchUi();
        }

        void UpdateRematchUi()
        {
            if (rematch == null) return;
            int secs = Mathf.Max(0, Mathf.CeilToInt(rematch.timeLeft));
            if (secs == rematchShownSecs && rematch.version == rematchShownVersion) return;
            bool slotsChanged = rematch.version != rematchShownVersion;
            rematchShownSecs = secs;
            rematchShownVersion = rematch.version;
            bool open = !rematch.finished;
            if (rematchBtn)
            {
                rematchBtn.interactable = open;
                rematchText.text = !open ? Loc.T("REMATCH_LEFT")
                    : Loc.T("RESULTS_REMATCH") + (secs > 0 && !rematch.starting ? " (" + secs + ")" : "") + "  " + rematch.ReadyCount + "/" + rematch.slots.Count;
            }
            if (!rematchPopupOpen || !rematchLayer) return;
            rematchCountdown.text = rematch.starting ? "Get ready!" : secs > 0 ? secs.ToString() : "...";
            rematchStatus.text = rematch.status;
            if (!slotsChanged) return;
            UI.Clear(rematchSlots);
            for (int i = 0; i < rematch.slots.Count; i++)
            {
                var sl = rematch.slots[i];
                var row = UI.Panel(rematchSlots, Theme.PanelInner, true, "Slot");
                UI.Layout(row, -1, 70);
                var dot = UI.Image(row.transform, UI.Circle, Theme.PlayerColors[i % Theme.PlayerColors.Length], false, "Color");
                UI.Place(dot.rectTransform, new Vector2(0, 0.5f), new Vector2(46, 46), new Vector2(16, 0));
                var n = UI.Label(row.transform, sl.name + (sl.isLocal ? "  (you)" : ""), 32, Theme.Text, TextAnchor.MiddleLeft);
                UI.Anchor(n.rectTransform, 0.1f, 0, 0.62f, 1);
                string st = sl.state == "ready" ? Loc.T("REMATCH_READY") + "!" : sl.state == "left" ? Loc.T("REMATCH_LEFT") : "Thinking...";
                var c = sl.state == "ready" ? Theme.Good : sl.state == "left" ? Theme.Danger : Theme.Muted;
                var sv = UI.Label(row.transform, st, 30, c, TextAnchor.MiddleRight, true);
                UI.Anchor(sv.rectTransform, 0.62f, 0, 0.96f, 1);
            }
        }

        void OnRematchStarted(BattleConfig cfg)
        {
            if (cfg == null) return;
            startingBattle = true;
            if (rematchLayer) MetaUI.Close(rematchLayer);
            rematchPopupOpen = false;
            GameManager.StartBattle(cfg);
        }

        public override void OnHide()
        {
            if (rematchLayer) MetaUI.Close(rematchLayer);
            if (!startingBattle && rematch != null && !rematch.finished) Fs?.RematchLeave(rematch);
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
                if (reward.cash > 0) Line(parent, "Cash found", "cash", reward.cash, Theme.Good);
                Line(parent, "XP", "xp", reward.TotalXp, Theme.Secondary);
                // what this match paid in each currency, with the same coin / Cash icons as the shop
                Line(parent, "Coins earned", "coin", reward.TotalCoins, Theme.Coin);
                Line(parent, "Cash earned", "cash", reward.cash + Mathf.Max(0, reward.betCash), Theme.Cash);
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
            if (rematch != null)
            {
                // the popup was closed with X / Android back: that's a "no"
                if (rematchPopupOpen && !rematchLayer) LeaveRematch();
                if (rematch.finished && rematchPopupOpen && !startingBattle)
                {
                    if (!string.IsNullOrEmpty(rematch.status)) UI.Toast(rematch.status);
                    MetaUI.Close(rematchLayer);
                    rematchPopupOpen = false;
                }
                UpdateRematchUi();
            }
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
