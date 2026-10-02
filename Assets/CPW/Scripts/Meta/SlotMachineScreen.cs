using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// Slot machine rules from the original SlotMachineLogic: three reels of 24 positions (reel k at position i
    /// shows SlotMachine "Set{i+1}".Reel{k}), the server picks a random stop per reel, the three visible rows and
    /// the two diagonals are checked against SlotWin (left-anchored 3/2/1 symbol matches, best SortOrder first).
    /// MaxDailySlotMachinePlays free spins per day, then PlayPriceInCash fish per spin.
    /// </summary>
    public static class SlotMachineLogic
    {
        public const int Positions = 24;
        static string[][] strips;
        static List<Record> wins;

        // SlotWin results point at SlotMachine.Set* rows; this is the symbol each of them stands for.
        static readonly Dictionary<string, string> SetSymbol = new Dictionary<string, string>
        {
            { "SetCash", "Cash" }, { "SetCraftRare", "Bolt" }, { "SetCraftCommon", "Bolt" }, { "SetLemon", "Lemon" },
            { "SetAmmo", "Ammo" }, { "SetCoin", "Coin" }, { "SetXP", "Xp" }
        };

        /// <summary>Rows (-1 top, 0 middle, +1 bottom) per reel for each pay line (original JACKPOT_LINES).</summary>
        public static readonly int[][] Lines =
        {
            new[] { -1, -1, -1 }, new[] { 0, 0, 0 }, new[] { 1, 1, 1 }, new[] { 1, 0, -1 }, new[] { -1, 0, 1 }
        };

        static Record Conf => GameData.Get("SlotMachineConfiguration", "Default");
        public static int MaxFree => Conf?.Int("MaxDailySlotMachinePlays", 3) ?? 3;
        public static int Price => Conf?.Int("PlayPriceInCash", 10) ?? 10;

        public static string[] Strip(int reel)
        {
            if (strips == null)
            {
                strips = new string[3][];
                string[] fallback = { "Lemon", "Coin", "Ammo", "Xp", "Bolt", "Cash" };
                for (int k = 0; k < 3; k++)
                {
                    strips[k] = new string[Positions];
                    for (int i = 0; i < Positions; i++)
                    {
                        var r = GameData.Get("SlotMachine", "Set" + (i + 1));
                        var sym = r != null ? GameData.RefId(r.Str("Reel" + k, "")) : null;
                        strips[k][i] = string.IsNullOrEmpty(sym) ? fallback[(i * 7 + k * 3) % fallback.Length] : sym;
                    }
                }
            }
            return strips[reel];
        }

        public static string Symbol(int reel, int pos) => Strip(reel)[((pos % Positions) + Positions) % Positions];

        public static List<Record> Wins
        {
            get
            {
                if (wins != null) return wins;
                wins = new List<Record>(GameData.Section("SlotWin").Values);
                wins.Sort((a, b) => a.Int("SortOrder").CompareTo(b.Int("SortOrder")));
                return wins;
            }
        }

        public static string WinSymbol(Record win, int n)
        {
            var s = win.Str("WinResult" + n);
            if (string.IsNullOrEmpty(s)) return null;
            var id = GameData.RefId(s);
            return SetSymbol.TryGetValue(id, out var sym) ? sym : id;
        }

        public static int WinLength(Record win) => WinSymbol(win, 3) != null ? 3 : WinSymbol(win, 2) != null ? 2 : 1;

        static void ResetDay()
        {
            var P = ProfileService.P;
            if (P.slotSpinsDay != MetaUI.Today) { P.slotSpinsDay = MetaUI.Today; P.slotSpinsUsedToday = 0; }
        }

        public static int FreeSpinsLeft()
        {
            ResetDay();
            return Mathf.Max(0, MaxFree - ProfileService.P.slotSpinsUsedToday);
        }

        /// <summary>Pay for a spin (free or fish) and pick the reel stops. Returns null when it can't be paid.</summary>
        public static int[] Spin()
        {
            ResetDay();
            var P = ProfileService.P;
            if (FreeSpinsLeft() <= 0 && !Progression.Spend(0, Price)) return null;
            P.slotSpinsUsedToday++;
            ProfileService.Save();
            return new[] { Random.Range(0, Positions), Random.Range(0, Positions), Random.Range(0, Positions) };
        }

        public struct LineWin { public int line; public Record win; }

        /// <summary>Best SlotWin for every pay line.</summary>
        public static List<LineWin> Evaluate(int[] stops)
        {
            var res = new List<LineWin>();
            for (int l = 0; l < Lines.Length; l++)
            {
                string a = Symbol(0, stops[0] + Lines[l][0]), b = Symbol(1, stops[1] + Lines[l][1]), c = Symbol(2, stops[2] + Lines[l][2]);
                foreach (var w in Wins)
                {
                    int n = WinLength(w);
                    if (WinSymbol(w, 1) != a) continue;
                    if (n >= 2 && WinSymbol(w, 2) != b) continue;
                    if (n >= 3 && WinSymbol(w, 3) != c) continue;
                    res.Add(new LineWin { line = l, win = w });
                    break;
                }
            }
            return res;
        }

        /// <summary>
        /// Give one SlotWin's reward. Item rewards: Lemon → 3 Grenades (the "Lemon Grenade"), Ammo → a pack of a
        /// random weapon unlocked at your level, Crafting → a common / rare ingredient (see CraftingCatalog).
        /// Returns a description.
        /// </summary>
        public static string Give(Record w)
        {
            int coins = w.Int("RewardCoin"), cash = w.Int("RewardCash"), xp = w.Int("RewardXP");
            if (coins > 0) { Progression.AddCoins(coins); return "+" + coins + " coins"; }
            if (cash > 0) { Progression.AddCash(cash); return "+" + cash + " fish"; }
            if (xp > 0) { Progression.AddXp(xp); return "+" + xp + " XP"; }
            string sym = WinSymbol(w, 1);
            if (sym == "Lemon")
            {
                var set = GameData.Get("SlotMachine", "SetLemon");
                int n = Mathf.Max(1, set?.Int("RewardItemAmount", 3) ?? 3);
                ProfileService.P.AddAmmo("Grenade", n);
                return "+" + n + " Grenades";
            }
            if (sym == "Ammo")
            {
                var pool = new List<Record>();
                foreach (var r in ItemCatalog.ShopItems("Weapon")) if (ItemCatalog.IsUnlocked(r) && !ItemCatalog.VipBlocked(r)) pool.Add(r);
                var pick = pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : GameData.Item("BasicNuke");
                int n = ItemCatalog.AmountPurchased(pick);
                ProfileService.P.AddAmmo(pick.Id, n);
                return "+" + n + " " + ItemCatalog.Name(pick);
            }
            if (sym == "Bolt")
            {
                bool rare = WinLength(w) >= 3;
                var id = CraftingCatalog.RandomIngredient(rare);
                CraftingCatalog.AddIngredient(id, 1);
                return "+1 " + CraftingCatalog.Ingredient(id).name;
            }
            return "";
        }

        public static string RewardText(Record w)
        {
            int coins = w.Int("RewardCoin"), cash = w.Int("RewardCash"), xp = w.Int("RewardXP");
            if (coins > 0) return coins + " coins";
            if (cash > 0) return cash + " fish";
            if (xp > 0) return xp + " XP";
            var sym = WinSymbol(w, 1);
            if (sym == "Lemon") return "3 Grenades";
            if (sym == "Ammo") return "Ammo pack";
            if (sym == "Bolt") return WinLength(w) >= 3 ? "Rare ingredient" : "Ingredient";
            return "";
        }

        public static Color SymbolColor(string sym)
        {
            switch (sym)
            {
                case "Lemon": return new Color32(255, 230, 60, 255);
                case "Coin": return Theme.Coin;
                case "Cash": return Theme.Cash;
                case "Xp": return Theme.Xp;
                case "Ammo": return new Color32(232, 96, 70, 255);
                case "Bolt": return new Color32(170, 120, 255, 255);
            }
            return Color.gray;
        }
    }

    /// <summary>The slot machine screen with spinning reels.</summary>
    public class SlotMachineScreen : MetaScreen
    {
        protected override string Title => "Slot Machine";
        const float CellH = 210f;

        class Cell
        {
            public RectTransform rt; public Image bg, icon; public Text label; public string sym;
            public void Set(string s)
            {
                if (s == sym) return;
                sym = s;
                var sp = ModelLibrary.Icon("Slot/" + s);
                icon.sprite = sp ?? UI.Circle;
                icon.color = sp != null ? Color.white : SlotMachineLogic.SymbolColor(s);
                label.text = sp != null ? "" : (s == "Coin" ? "$" : s == "Cash" ? "F" : s == "Xp" ? "XP" : s == "Ammo" ? "A" : s == "Bolt" ? "B" : s == "Lemon" ? "L" : "?");
            }
        }

        class Reel { public float pos, start, target, t; public int state; public Cell[] cells = new Cell[5]; public int lastBase = int.MinValue; }
        // state: 0 idle, 1 spinning, 2 stopping
        readonly Reel[] reels = { new Reel(), new Reel(), new Reel() };
        int[] stops;
        float spinTime;
        Button spinBtn;
        Text spinLabel, infoText, resultText;
        bool spinning;

        protected override void BuildContent()
        {
            var machine = MetaUI.CardPanel(Content, new Color32(170, 40, 60, 255), "Machine");
            UI.Anchor(machine.rectTransform, 0.02f, 0, 0.64f, 1);
            var top = UI.Label(machine.transform, "PENGUIN JACKPOT", 64, MetaUI.Gold, TextAnchor.MiddleCenter, true);
            UI.Anchor(top.rectTransform, 0.05f, 0.86f, 0.95f, 0.98f);
            var window = UI.Panel(machine.transform, new Color32(40, 10, 20, 255), true, "Window");
            UI.Anchor(window.rectTransform, 0.06f, 0.24f, 0.94f, 0.85f);
            var reelsRow = UI.Rect(window.transform, "Reels");
            UI.Stretch(reelsRow, 14, 14, 14, 14);
            var h = UI.HBox(reelsRow, 14, TextAnchor.MiddleCenter);
            h.childForceExpandWidth = true; h.childForceExpandHeight = true;
            for (int k = 0; k < 3; k++) BuildReel(reelsRow, reels[k], k);
            // center line marker
            var marker = UI.Panel(window.transform, new Color(1, 0.85f, 0.2f, 0.25f), false, "Payline");
            marker.raycastTarget = false;
            UI.Anchor(marker.rectTransform, 0, 0.5f, 1, 0.5f);
            marker.rectTransform.sizeDelta = new Vector2(0, 8);

            resultText = UI.Label(machine.transform, "", 40, Color.white, TextAnchor.MiddleCenter, true);
            UI.Anchor(resultText.rectTransform, 0.04f, 0.15f, 0.96f, 0.24f);
            spinBtn = UI.Button(machine.transform, "", OnSpin, UI.ButtonStyle.Primary, 54);
            UI.Anchor((RectTransform)spinBtn.transform, 0.25f, 0.02f, 0.75f, 0.14f);
            spinLabel = spinBtn.GetComponentInChildren<Text>();
            spinBtn.gameObject.AddComponent<UIPulse>().amount = 0.03f;

            // pay table
            var pay = MetaUI.CardPanel(Content, MetaUI.Card, "Paytable");
            UI.Anchor(pay.rectTransform, 0.66f, 0, 1, 1);
            var pv = UI.VBox(pay.rectTransform, 4, TextAnchor.UpperLeft, 18);
            pv.childForceExpandHeight = false;
            infoText = UI.Label(pay.transform, "", 30, Theme.Secondary, TextAnchor.MiddleLeft, true);
            UI.Layout(infoText, -1, 70);
            foreach (var w in SlotMachineLogic.Wins) PayRow(pay.rectTransform, w);
            UpdateSpinButton();
            for (int k = 0; k < 3; k++) { reels[k].pos = Random.Range(0, SlotMachineLogic.Positions); Draw(k); }
        }

        void BuildReel(RectTransform parent, Reel reel, int k)
        {
            var view = UI.Panel(parent, new Color(0.97f, 0.97f, 1f), true, "Reel " + k);
            view.gameObject.AddComponent<RectMask2D>();
            for (int j = 0; j < 5; j++)
            {
                var c = new Cell();
                c.rt = UI.Rect(view.transform, "Cell");
                c.rt.anchorMin = c.rt.anchorMax = new Vector2(0.5f, 0.5f);
                c.rt.sizeDelta = new Vector2(190, CellH - 10);
                c.bg = UI.Image(c.rt, UI.Rounded, new Color(1, 1, 1, 0), false, "Bg");
                c.bg.type = Image.Type.Sliced;
                UI.Stretch(c.bg.rectTransform);
                c.icon = UI.Image(c.rt, null, Color.white, true, "Icon");
                UI.Stretch(c.icon.rectTransform, 22, 22, 18, 18);
                c.label = UI.Label(c.icon.transform, "", 64, Color.white, TextAnchor.MiddleCenter, true);
                UI.Stretch(c.label.rectTransform);
                reel.cells[j] = c;
            }
        }

        void PayRow(RectTransform parent, Record w)
        {
            var row = UI.Rect(parent, "Win");
            UI.Layout(row, -1, 66);
            int n = SlotMachineLogic.WinLength(w);
            for (int i = 1; i <= n; i++)
            {
                var sym = SlotMachineLogic.WinSymbol(w, i);
                var sp = ModelLibrary.Icon("Slot/" + sym);
                var ic = UI.Image(row, sp ?? UI.Circle, sp != null ? Color.white : SlotMachineLogic.SymbolColor(sym), true);
                UI.Place(ic.rectTransform, new Vector2(0, 0.5f), new Vector2(56, 56), new Vector2((i - 1) * 62, 0));
            }
            var l = UI.Label(row, SlotMachineLogic.RewardText(w), 30, Theme.Text, TextAnchor.MiddleLeft);
            UI.Anchor(l.rectTransform, 0.42f, 0, 1, 1);
        }

        void UpdateSpinButton()
        {
            int free = SlotMachineLogic.FreeSpinsLeft();
            spinLabel.text = free > 0 ? Loc.T("SLOTMACHINE_PRESS_SPIN") + "  (free)" : Loc.T("SLOTMACHINE_INSERT_CASH") + "  " + SlotMachineLogic.Price + " fish";
            infoText.text = "Free spins today: " + free + " / " + SlotMachineLogic.MaxFree;
            spinBtn.interactable = !spinning;
        }

        void OnSpin()
        {
            if (spinning) return;
            if (SlotMachineLogic.FreeSpinsLeft() <= 0 && ProfileService.P.cash < SlotMachineLogic.Price) { Progression.NotEnough(true); return; }
            stops = SlotMachineLogic.Spin();
            if (stops == null) return;
            ChallengeTracker.Report("slotSpins", 1);
            spinning = true;
            spinTime = 0;
            resultText.text = "";
            foreach (var r in reels) { r.state = 1; foreach (var c in r.cells) c.bg.color = new Color(1, 1, 1, 0); }
            AudioManager.Sfx("SlotMachineInsertCoin");
            AudioManager.Loop("slot", "SlotMachineSpin");
            UpdateSpinButton();
        }

        public override void Tick(float dt)
        {
            if (!spinning) return;
            spinTime += dt;
            bool allStopped = true;
            for (int k = 0; k < 3; k++)
            {
                var r = reels[k];
                if (r.state == 1)
                {
                    r.pos -= 16f * dt;
                    if (spinTime > 0.9f + 0.45f * k)
                    {
                        // decelerate onto the served stop, at least one full turn further
                        r.state = 2; r.t = 0; r.start = r.pos;
                        int b = Mathf.FloorToInt(r.pos) - SlotMachineLogic.Positions;
                        int d = ((b - stops[k]) % SlotMachineLogic.Positions + SlotMachineLogic.Positions) % SlotMachineLogic.Positions;
                        r.target = b - d;
                    }
                }
                else if (r.state == 2)
                {
                    r.t += dt / 1.0f;
                    float e = 1 - Mathf.Pow(1 - Mathf.Clamp01(r.t), 3);
                    r.pos = Mathf.LerpUnclamped(r.start, r.target, e);
                    if (r.t >= 1) { r.pos = r.target; r.state = 0; AudioManager.Sfx("SlotMachineReelStop"); }
                }
                if (r.state != 0) allStopped = false;
                Draw(k);
            }
            if (allStopped) Finish();
        }

        void Draw(int k)
        {
            var r = reels[k];
            int b = Mathf.FloorToInt(r.pos);
            bool newBase = b != r.lastBase;
            r.lastBase = b;
            for (int j = 0; j < 5; j++)
            {
                int idx = b + j - 2;
                var c = r.cells[j];
                c.rt.anchoredPosition = new Vector2(0, (r.pos - idx) * CellH);
                if (newBase) c.Set(SlotMachineLogic.Symbol(k, idx));
            }
        }

        void Finish()
        {
            spinning = false;
            AudioManager.StopLoop("slot");
            var wins = SlotMachineLogic.Evaluate(stops);
            if (wins.Count == 0)
            {
                resultText.text = "No luck this time...";
                AudioManager.Sfx("SlotMachineNoWin");
            }
            else
            {
                var sb = new System.Text.StringBuilder();
                foreach (var w in wins)
                {
                    var line = SlotMachineLogic.Lines[w.line];
                    for (int k = 0; k < SlotMachineLogic.WinLength(w.win); k++)
                    {
                        // cell j = row + 2 (row -1 top .. +1 bottom) once the reel is at an integer stop
                        reels[k].cells[line[k] + 2].bg.color = new Color(1f, 0.85f, 0.2f, 0.9f);
                    }
                    if (sb.Length > 0) sb.Append("   ");
                    sb.Append(SlotMachineLogic.Give(w.win));
                }
                ProfileService.Save();
                resultText.text = Loc.T("SLOTMACHINE_WIN") + "  " + sb;
                AudioManager.Sfx("SlotMachineWin");
                Progression.ShowPendingLevelUps();
            }
            UpdateSpinButton();
        }

        public override void OnHide() => AudioManager.StopLoop("slot");
        public override bool OnBack() => spinning;   // finish the spin first
    }
}
