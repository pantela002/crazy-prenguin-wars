using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// Level thumbnails. Uses Terrain's LevelPreview.Render(levelIdOrPath, w, h) through reflection (so Meta compiles
    /// without it); falls back to a simple drawn landscape in the level theme's colors.
    /// </summary>
    public static class LevelThumbs
    {
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        static MethodInfo render;
        static bool looked;

        public static Sprite Get(string levelId, int w = 320, int h = 180)
        {
            if (string.IsNullOrEmpty(levelId)) return null;
            if (cache.TryGetValue(levelId, out var s)) return s;
            Texture2D tex = null;
            if (!looked)
            {
                looked = true;
                var t = Type.GetType("CPW.LevelPreview");
                render = t?.GetMethod("Render", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), typeof(int), typeof(int) }, null);
            }
            if (render != null)
            {
                try { tex = render.Invoke(null, new object[] { levelId, w, h }) as Texture2D; }
                catch (Exception e) { Debug.LogWarning("CPW: LevelPreview failed for " + levelId + ": " + e.Message); }
            }
            if (tex == null) tex = Placeholder(levelId, w / 2, h / 2);
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            cache[levelId] = s;
            return s;
        }

        static Texture2D Placeholder(string id, int w, int h)
        {
            string theme = BattleFactory.LevelTheme(id);
            bool lava = theme == "Lava";
            Color sky = theme == "Desert" ? new Color(1f, 0.8f, 0.5f) : theme == "Winter" ? new Color(0.7f, 0.85f, 1f) : theme == "Mountain" ? new Color(0.55f, 0.7f, 0.9f)
                : lava ? new Color(0.45f, 0.15f, 0.12f) : new Color(0.55f, 0.85f, 1f);
            Color ground = theme == "Desert" ? new Color(0.85f, 0.65f, 0.35f) : theme == "Winter" ? new Color(0.95f, 0.97f, 1f) : theme == "Mountain" ? new Color(0.5f, 0.48f, 0.45f)
                : lava ? new Color(0.25f, 0.2f, 0.2f) : new Color(0.35f, 0.65f, 0.3f);
            Color water = lava ? new Color(1f, 0.45f, 0.1f) : new Color(0.2f, 0.45f, 0.85f);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            int seed = 0; foreach (char c in id) seed = seed * 31 + c;
            float p1 = (seed & 0xff) / 40f, p2 = ((seed >> 8) & 0xff) / 50f;
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w;
                float top = h * (0.35f + 0.18f * Mathf.Sin(u * 7 + p1) + 0.1f * Mathf.Sin(u * 17 + p2));
                bool gap = Mathf.Sin(u * 5 + p2) > 0.85f;
                for (int y = 0; y < h; y++)
                {
                    Color c = Color.Lerp(sky, Color.white, y / (float)h * 0.4f);
                    if (!gap && y < top) c = y > top - 3 ? Color.Lerp(ground, Color.white, 0.4f) : ground * (0.8f + 0.2f * y / top);
                    if (y < h * 0.12f && (gap || y >= top)) c = water;
                    c.a = 1;
                    px[y * w + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }

    /// <summary>Mode select: Tutorial, Practice, Quick Match, Custom Game, Online.</summary>
    public class PlayScreen : MetaScreen
    {
        protected override string Title => Loc.T("BUTTON_PLAY");

        protected override void BuildContent()
        {
            var row = UI.Rect(Content, "Modes");
            UI.Stretch(row, 0, 0, 20, 30);
            var h = UI.HBox(row, 24, TextAnchor.MiddleCenter);
            h.childForceExpandWidth = true; h.childForceExpandHeight = true;
            Card(row, "Ui/quickmatch", "Quick Match", "Battle AI penguins near your level. Earn coins, XP and loot. Place a bet if you dare!", Theme.Primary,
                () => ScreenManager.Show(() => new QuickMatchScreen()), true);
            Card(row, "Ui/practice", Loc.T("PRACTICE"), Loc.T("CUSTOM_GAME_PRACTICE_DESCRIPTION"), Theme.Good,
                () => ScreenManager.Show(() => new LoadoutScreen(BattleFactory.PracticeMatch())), false);
            Card(row, "Ui/custom", Loc.T("BUTTON_CUSTOM_GAME"), "Lobby for up to 4 penguins: pick the map and rules, play every seat yourself (pass the phone) or fill seats with AI.", MetaUI.Purple,
                () => ScreenManager.Show(() => new CustomGameScreen()), false);
            Card(row, "Ui/online", "Online", "Fight real penguins over the internet.", MetaUI.Teal, OpenOnline, false);
            Card(row, "Ui/tutorial", "Tutorial", Loc.T("TUTORIAL_INTRO_TITLE"), MetaUI.Orange,
                () => BattleFactory.Launch(BattleFactory.Tutorial()), false);
        }

        void Card(RectTransform parent, string icon, string title, string desc, Color color, Action onClick, bool featured)
        {
            var b = UI.Button(parent, null, onClick, UI.ButtonStyle.Dark, 30, "Mode " + title);
            b.GetComponent<Image>().color = color;
            UI.Layout(b, -1, -1, featured ? 1.35f : 1f, 1);
            var tile = MetaUI.IconTile(MetaUI.Box(b.transform, 0.15f, 0.5f, 0.85f, 0.94f), icon, title, Color.Lerp(color, Color.white, 0.35f));
            MetaUI.Square(tile);
            bool darkText = color == Theme.Primary;
            var t = UI.Label(b.transform, title, 50, darkText ? Theme.PrimaryText : Color.white, TextAnchor.MiddleCenter, !darkText);
            UI.Anchor(t.rectTransform, 0.04f, 0.36f, 0.96f, 0.5f);
            var d = UI.Label(b.transform, desc, 30, darkText ? Theme.PrimaryText : new Color(1, 1, 1, 0.92f), TextAnchor.UpperCenter);
            UI.Anchor(d.rectTransform, 0.06f, 0.04f, 0.94f, 0.35f);
        }

        /// <summary>Opens the online lobby (written by the Online engineer) without a compile-time dependency.</summary>
        public static void OpenOnline()
        {
            var t = Type.GetType("CPW.OnlineLobbyScreen");
            if (t == null || Online.Service is OfflineService)
            {
                UI.Message("Online", "Online battles need a Firebase project.\nConnect one (see Docs/FIREBASE.md) and restart the game.\n\nStatus: " + Online.Service.Status);
                return;
            }
            ScreenManager.Show(() => (UIScreen)Activator.CreateInstance(t));
        }
    }

    /// <summary>Quick match setup: number of opponents, AI difficulty and an optional bet (Bet section).</summary>
    public class QuickMatchScreen : MetaScreen
    {
        protected override string Title => "Quick Match";
        static int opponents = 1, skill = 1;
        static string bet = "1NoBet";
        readonly List<List<Button>> groups = new List<List<Button>>();

        protected override void BuildContent()
        {
            var panel = MetaUI.CardPanel(Content);
            UI.Anchor(panel.rectTransform, 0.12f, 0.16f, 0.88f, 1f);
            var v = UI.VBox(panel.rectTransform, 26, TextAnchor.UpperCenter, 40);
            v.childForceExpandHeight = false;

            Choice(panel.rectTransform, "Opponents", new[] { "1", "2", "3" }, opponents - 1, i => opponents = i + 1);
            Choice(panel.rectTransform, "Difficulty", new[] { "Easy", "Normal", "Hard" }, skill, i => skill = i);
            var bets = new List<Record>(GameData.Section("Bet").Values);
            bets.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            if (bets.Count == 0) bets.Add(new Record("Bet", "1NoBet", null));
            var labels = new string[bets.Count];
            int sel = 0;
            for (int i = 0; i < bets.Count; i++) { labels[i] = BattleFactory.BetLabel(bets[i].Id); if (bets[i].Id == bet) sel = i; }
            Choice(panel.rectTransform, Loc.T("MATCH_LOADING_BET_TITLE"), labels, sel, i => bet = bets[i].Id);
            var note = UI.Label(panel.rectTransform, "Win the match and take the whole pot! Lose and your stake is gone.", 30, Theme.Muted);
            UI.Layout(note, -1, 50);

            var go = UI.Button(Content, "Choose gear  >", () =>
            {
                var c = BattleFactory.QuickMatch(opponents, skill, bet);
                ScreenManager.Show(() => new LoadoutScreen(c));
            }, UI.ButtonStyle.Primary, 54);
            UI.Place((RectTransform)go.transform, new Vector2(0.5f, 0), new Vector2(520, 120), new Vector2(0, 0));
        }

        void Choice(RectTransform parent, string label, string[] options, int selected, Action<int> set)
        {
            var row = UI.Rect(parent, label);
            UI.Layout(row, -1, 110);
            var t = UI.Label(row, label, 42, Theme.Text, TextAnchor.MiddleLeft, true);
            UI.Anchor(t.rectTransform, 0, 0, 0.32f, 1);
            t.color = Theme.Secondary;
            var opts = UI.Rect(row, "Options");
            UI.Anchor(opts, 0.34f, 0.05f, 1, 0.95f);
            List<Button> tabs = null;
            tabs = MetaUI.Tabs(opts, options, selected, i => { set(i); MetaUI.SetTabSelected(tabs, i); }, 38);
            groups.Add(tabs);
        }
    }

    /// <summary>
    /// Custom game lobby: 4 seat cards (penguin, name, Human for pass-and-play or AI easy/normal/hard, add/remove),
    /// a map picker grouped by theme, match time (BattleOptions Min/MaxMatchTime), turn time, winning score and
    /// power-ups. "Test: 4 players, all me" fills every seat with a human so one phone controls all 4 penguins.
    /// </summary>
    public class CustomGameScreen : MetaScreen
    {
        protected override string Title => Loc.T("BUTTON_CUSTOM_GAME") + " Lobby";

        class Seat { public string name; public int type; } // type: 0 human, 1 AI easy, 2 AI normal, 3 AI hard
        static readonly string[] TypeNames = { "Human", "AI Easy", "AI Normal", "AI Hard" };
        const int MinSeats = 2;
        static List<Seat> seats;
        /// <summary>Forget the remembered seats (after a progress reset).</summary>
        public static void ClearRemembered() => seats = null;
        static string levelId = "";
        static int matchTime = 240, turnTime = 20, winScore = 200;
        static bool powerUps = true;

        RectTransform seatsBox;
        Text seatsHead, mapName, startText, passNote;

        static int MaxSeats => Mathf.Clamp(GameData.Battle?.Int("MaxNumberOfPlayers", 4) ?? 4, MinSeats, 4);
        static string MyName => string.IsNullOrEmpty(ProfileService.P.displayName) ? "Player 1" : ProfileService.P.displayName;

        protected override void BuildContent()
        {
            if (seats == null)
            {
                seats = new List<Seat> { new Seat { name = MyName, type = 0 }, new Seat { name = "Player 2", type = 0 } };
                matchTime = (int)(GameData.Battle?.Float("MatchTime", 240) ?? 240);
            }
            while (seats.Count > MaxSeats) seats.RemoveAt(seats.Count - 1);
            // remembered map got locked again (progress reset, testing switch off) or is gone from the level data
            if (!string.IsNullOrEmpty(levelId) && (MapLocked(levelId) || GameData.Get("Level", levelId) == null)) levelId = "";

            // ---- players (left) ----
            var players = MetaUI.CardPanel(Content, MetaUI.CardDark, "Players");
            UI.Anchor(players.rectTransform, 0, 0, 0.37f, 1, 0);
            seatsHead = UI.Label(players.transform, "", 40, Color.white, TextAnchor.MiddleLeft, true);
            UI.Anchor(seatsHead.rectTransform, 0.04f, 0.88f, 0.42f, 0.99f);
            var test = UI.Button(players.transform, "Test: 4 players, all me", FillTestSeats, UI.ButtonStyle.Good, 28, "TestPreset");
            UI.Anchor((RectTransform)test.transform, 0.43f, 0.885f, 0.98f, 0.985f);
            seatsBox = UI.Rect(players.transform, "Seats");
            UI.Anchor(seatsBox, 0.02f, 0.015f, 0.98f, 0.87f);
            BuildSeats();

            // ---- map (middle) ----
            var maps = MetaUI.CardPanel(Content, MetaUI.CardDark, "Maps");
            UI.Anchor(maps.rectTransform, 0.38f, 0, 0.74f, 1, 0);
            var mt = UI.Label(maps.transform, Loc.T("GAME_SETTINGS_MAP"), 40, Color.white, TextAnchor.MiddleLeft, true);
            UI.Anchor(mt.rectTransform, 0.04f, 0.88f, 0.3f, 0.99f);
            mapName = UI.Label(maps.transform, "", 32, MetaUI.Gold, TextAnchor.MiddleRight, true);
            UI.Anchor(mapName.rectTransform, 0.3f, 0.88f, 0.96f, 0.99f);
            var pickHost = UI.Rect(maps.transform, "Picker");
            UI.Anchor(pickHost, 0.02f, 0.015f, 0.98f, 0.87f);
            new MapPicker(pickHost, () => levelId, id => { levelId = id; UpdateTexts(); });

            // ---- rules (right) ----
            var rules = MetaUI.CardPanel(Content, null, "Rules");
            UI.Anchor(rules.rectTransform, 0.75f, 0.2f, 1, 1, 0);
            var v = UI.VBox(rules.rectTransform, 8, TextAnchor.UpperCenter, 18);
            v.childForceExpandHeight = false;
            var opt = GameData.Battle;
            int minM = (int)(opt?.Float("MinMatchTime", 180) ?? 180), maxM = (int)(opt?.Float("MaxMatchTime", 480) ?? 480);
            if (maxM < minM) maxM = minM;
            matchTime = Mathf.Clamp(matchTime, minM, maxM);
            int step = Mathf.Max(5, (int)(opt?.Float("TurnTimeIncrement", 5) ?? 5));
            Stepper(rules.rectTransform, Loc.T("GAME_SETTINGS_MATCH_TIME"), () => MetaUI.Clock(matchTime), d => matchTime = Mathf.Clamp(matchTime + d * 60, minM, maxM));
            Stepper(rules.rectTransform, Loc.T("GAME_SETTINGS_TURN_TIME"), () => turnTime + " s", d => turnTime = Mathf.Clamp(turnTime + d * step, 10, 60));
            Stepper(rules.rectTransform, "Winning score", () => winScore == 0 ? "Off" : winScore.ToString(), d => winScore = Mathf.Clamp(winScore + d * 100, 0, 1000));
            var tg = UI.Toggle(rules.transform, "Power-ups", powerUps, b => powerUps = b);
            UI.Layout(tg, -1, 70);
            passNote = UI.Label(rules.transform, "", 26, Theme.Muted, TextAnchor.UpperLeft);
            UI.Layout(passNote, -1, 130);

            var start = UI.Button(Content, "", StartGame, UI.ButtonStyle.Primary, 54, "Start");
            UI.Anchor((RectTransform)start.transform, 0.75f, 0, 1, 0.18f, 0);
            startText = UI.Label(start.transform, "", 54, Theme.PrimaryText, TextAnchor.MiddleCenter, true);
            NoOutline(startText);
            UI.Stretch(startText.rectTransform, 12, 12, 6, 6);
            UpdateTexts();
        }

        void UpdateTexts()
        {
            if (seatsHead) seatsHead.text = "Players " + seats.Count + "/" + MaxSeats;
            if (mapName) mapName.text = BattleFactory.LevelDisplayName(levelId);
            int humans = 0;
            foreach (var s in seats) if (s.type == 0) humans++;
            if (startText) startText.text = "Start!  (" + seats.Count + " penguins)";
            if (passNote)
                passNote.text = humans > 1
                    ? humans + " humans on this phone: pass it over when the screen shows whose turn it is. No rewards in local games."
                    : "Everyone gets the same arsenal.";
        }

        /// <summary>Testing preset: 4 human seats (you + Player 2-4), all controlled on this phone.</summary>
        bool soloTest;   // "all me" preset: one person plays every seat, so no pass-the-phone curtain

        void FillTestSeats()
        {
            seats = new List<Seat> { new Seat { name = MyName, type = 0 } };
            while (seats.Count < MaxSeats) seats.Add(new Seat { name = "Player " + (seats.Count + 1), type = 0 });
            BuildSeats();
            soloTest = true;
            UI.Toast(seats.Count + " human players: you control every penguin.");
        }

        void BuildSeats()
        {
            UI.Clear(seatsBox);
            int max = MaxSeats;
            float gap = 0.025f, hEach = (1f - gap * (max - 1)) / max;
            for (int i = 0; i < max; i++)
            {
                float top = 1f - i * (hEach + gap);
                var box = MetaUI.Box(seatsBox, 0, top - hEach, 1, top);
                if (i < seats.Count) SeatCard(box, i);
                else EmptySeat(box, i);
            }
            UpdateTexts();
        }

        void SeatCard(RectTransform box, int idx)
        {
            var seat = seats[idx];
            Color team = Theme.PlayerColors[idx % Theme.PlayerColors.Length];
            var card = UI.Panel(box, Theme.Panel, true, "Seat " + (idx + 1));
            UI.Stretch(card.rectTransform);
            var strip = UI.Panel(card.transform, team, true, "Color");
            strip.raycastTarget = false;
            UI.Anchor(strip.rectTransform, 0, 0, 0.025f, 1);

            var pic = MetaUI.Box(card.transform, 0.035f, 0.06f, 0.23f, 0.94f);
            PenguinGlyph.Create(pic, team);
            var num = MetaUI.Badge(pic, "P" + (idx + 1), team, 46);
            UI.Place((RectTransform)num.transform.parent, new Vector2(1, 0), new Vector2(46, 46), new Vector2(-2, 2));

            var input = UI.Input(card.transform, seat.name, "Name", s =>
            {
                s = (s ?? "").Trim();
                seat.name = s.Length == 0 ? "Player " + (idx + 1) : (s.Length > 16 ? s.Substring(0, 16) : s);
            });
            input.characterLimit = 16;
            UI.Anchor(input.GetComponent<RectTransform>(), 0.25f, 0.53f, idx >= MinSeats ? 0.84f : 0.98f, 0.94f);
            if (idx >= MinSeats)
            {
                var rm = UI.Button(card.transform, "X", () => { if (idx < seats.Count) seats.RemoveAt(idx); BuildSeats(); }, UI.ButtonStyle.Danger, 36, "Remove");
                UI.Anchor((RectTransform)rm.transform, 0.86f, 0.53f, 0.98f, 0.94f);
            }

            var types = UI.Rect(card.transform, "Type");
            UI.Anchor(types, 0.25f, 0.06f, 0.98f, 0.46f);
            var h = UI.HBox(types, 6, TextAnchor.MiddleCenter, 0, true);
            h.childForceExpandHeight = true;
            if (idx == 0)
            {
                // seat 1 is always you
                var me = UI.Label(types, "Human (you)", 30, Theme.Good, TextAnchor.MiddleLeft, true);
                NoOutline(me);
                return;
            }
            var btns = new List<Button>();
            for (int t = 0; t < TypeNames.Length; t++)
            {
                int type = t;
                var b = UI.Button(types, TypeNames[t], () =>
                {
                    seat.type = type;
                    StyleTypes(btns, type);
                    UpdateTexts();
                }, UI.ButtonStyle.Plain, 26, "Type " + TypeNames[t]);
                UI.Layout(b, -1, -1, 1, 1);
                btns.Add(b);
            }
            StyleTypes(btns, seat.type);
        }

        static void StyleTypes(List<Button> btns, int selected)
        {
            for (int i = 0; i < btns.Count; i++)
            {
                bool on = i == selected;
                btns[i].GetComponent<Image>().color = on ? (i == 0 ? Theme.Good : Theme.Secondary) : Theme.PanelInner;
                var l = btns[i].GetComponentInChildren<Text>();
                if (l) l.color = on ? Color.white : Theme.Text;
            }
        }

        void EmptySeat(RectTransform box, int idx)
        {
            var b = UI.Button(box, "+ Add penguin", () =>
            {
                if (seats.Count >= MaxSeats) return;
                seats.Add(new Seat { name = "Player " + (seats.Count + 1), type = 0 });
                BuildSeats();
            }, UI.ButtonStyle.Dark, 36, "Empty seat " + (idx + 1));
            UI.Stretch((RectTransform)b.transform);
            b.GetComponent<Image>().color = new Color(1, 1, 1, 0.15f);
        }

        // ---------- map locks (MapLocks: Level.MinLevel above the player's level, unless unlocked for testing) ----------
        public static int MapLevel(string id) => MapLocks.MinLevel(id);
        public static bool MapLocked(string id) => MapLocks.Locked(id);

        /// <summary>Map thumbnail button ("" = random). Locked maps show the required level and can't be picked.</summary>
        public static Button MapButton(Transform grid, string id, string name, Action pick) => MapPicker.Thumb(grid, id, name, pick);

        /// <summary>Popup map picker (used by Practice). pick gets the level id, "" for a random map.</summary>
        public static void PickMap(string current, Action<string> pick) => MapPicker.Popup(Loc.T("GAME_SETTINGS_MAP"), current, pick);

        public static void NoOutline(Text l) { var o = l.GetComponent<Outline>(); if (o) UnityEngine.Object.Destroy(o); }

        void Stepper(RectTransform parent, string label, Func<string> value, Action<int> change)
        {
            var row = UI.Rect(parent, label);
            UI.Layout(row, -1, 76);
            var t = UI.Label(row, label, 32, Theme.Text, TextAnchor.MiddleLeft);
            UI.Anchor(t.rectTransform, 0, 0, 0.44f, 1);
            Text v = null;
            var minus = UI.Button(row, "-", () => { change(-1); v.text = value(); }, UI.ButtonStyle.Secondary, 44);
            UI.Anchor((RectTransform)minus.transform, 0.45f, 0.06f, 0.6f, 0.94f);
            v = UI.Label(row, value(), 36, Theme.Text, TextAnchor.MiddleCenter, true);
            v.color = Theme.Secondary;
            UI.Anchor(v.rectTransform, 0.6f, 0, 0.83f, 1);
            var plus = UI.Button(row, "+", () => { change(1); v.text = value(); }, UI.ButtonStyle.Secondary, 44);
            UI.Anchor((RectTransform)plus.transform, 0.83f, 0.06f, 0.98f, 0.94f);
        }

        void StartGame()
        {
            if (seats.Count < MinSeats) { UI.Toast("Add at least one more penguin."); return; }
            var c = new BattleConfig
            {
                mode = BattleMode.Custom, seed = Environment.TickCount, matchTime = matchTime, turnTime = turnTime,
                winningScore = winScore, powerUps = powerUps, betId = "1NoBet",
                levelId = string.IsNullOrEmpty(levelId) ? BattleFactory.RandomLevelFor(ProfileService.P.level) : levelId
            };
            var P = ProfileService.P;
            for (int i = 0; i < seats.Count; i++)
            {
                var s = seats[i];
                string name = string.IsNullOrEmpty(s.name) ? "Player " + (i + 1) : s.name;
                PlayerSlot slot;
                if (s.type == 0)
                {
                    slot = new PlayerSlot { name = name, isAI = false, isLocalHuman = true, usesProfileInventory = false, level = P.level, colorIndex = i };
                    if (i == 0) { slot.head = P.wornHead; slot.chest = P.wornChest; slot.feet = P.wornFeet; slot.trophy = P.wornTrophy; }
                }
                else slot = BattleFactory.AiSlot(name, P.level, s.type - 1, i);
                slot.loadout = BattleFactory.CustomLoadout();
                slot.boosters.Clear();
                c.players.Add(slot);
            }
            c.skipPassCurtain = soloTest && seats.TrueForAll(x => x.type == 0);
            BattleFactory.Launch(c);
        }
    }
}
