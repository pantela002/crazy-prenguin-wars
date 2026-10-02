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
            Color sky = theme == "Desert" ? new Color(1f, 0.8f, 0.5f) : theme == "Winter" ? new Color(0.7f, 0.85f, 1f) : theme == "Mountain" ? new Color(0.55f, 0.7f, 0.9f) : new Color(0.55f, 0.85f, 1f);
            Color ground = theme == "Desert" ? new Color(0.85f, 0.65f, 0.35f) : theme == "Winter" ? new Color(0.95f, 0.97f, 1f) : theme == "Mountain" ? new Color(0.5f, 0.48f, 0.45f) : new Color(0.35f, 0.65f, 0.3f);
            Color water = new Color(0.2f, 0.45f, 0.85f);
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
            Card(row, "Ui/custom", Loc.T("BUTTON_CUSTOM_GAME"), "Pick the map, rules and up to 4 penguins. Pass the phone around or fill seats with AI.", MetaUI.Purple,
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
    /// Custom game: map picker with thumbnails, 2-4 players (Human for pass-and-play, or AI easy/normal/hard),
    /// match time (BattleOptions Min/MaxMatchTime), turn time, power-ups and winning score.
    /// </summary>
    public class CustomGameScreen : MetaScreen
    {
        protected override string Title => Loc.T("BUTTON_CUSTOM_GAME");

        class Seat { public string name; public int type; } // type: 0 human, 1 AI easy, 2 AI normal, 3 AI hard
        static readonly string[] TypeNames = { "Human", "AI Easy", "AI Normal", "AI Hard" };
        static List<Seat> seats;
        /// <summary>Forget the remembered seats (after a progress reset).</summary>
        public static void ClearRemembered() => seats = null;
        static string levelId = "";
        static int matchTime = 240, turnTime = 20, winScore = 200;
        static bool powerUps = true;

        RectTransform playersBox;
        readonly List<Image> mapFrames = new List<Image>();
        readonly List<string> mapIds = new List<string>();

        protected override void BuildContent()
        {
            if (seats == null)
            {
                seats = new List<Seat> { new Seat { name = ProfileService.P.displayName, type = 0 }, new Seat { name = "Player 2", type = 0 } };
                matchTime = (int)(GameData.Battle?.Float("MatchTime", 240) ?? 240);
            }
            if (MapLocked(levelId)) levelId = "";   // remembered map got locked again (progress reset)

            // ---- maps (left) ----
            var mapsPanel = MetaUI.CardPanel(Content, MetaUI.CardDark);
            UI.Anchor(mapsPanel.rectTransform, 0, 0.13f, 0.5f, 1, 0);
            var mt = UI.Label(mapsPanel.transform, Loc.T("GAME_SETTINGS_MAP"), 40, Color.white, TextAnchor.UpperLeft, true);
            UI.Anchor(mt.rectTransform, 0.03f, 0.9f, 1, 0.99f);
            var scrollHost = UI.Rect(mapsPanel.transform, "Maps");
            UI.Anchor(scrollHost, 0.01f, 0.01f, 0.99f, 0.9f);
            var sr = UI.ScrollGrid(scrollHost, out var grid, new Vector2(280, 200), new Vector2(14, 14));
            UI.Stretch((RectTransform)sr.transform);
            AddMap(grid, "", "Random");
            foreach (var r in BattleFactory.Levels()) AddMap(grid, r.Id, BattleFactory.LevelDisplayName(r.Id));
            HighlightMap();

            // ---- rules + players (right) ----
            var right = MetaUI.CardPanel(Content);
            UI.Anchor(right.rectTransform, 0.515f, 0.13f, 1, 1, 0);
            var v = UI.VBox(right.rectTransform, 10, TextAnchor.UpperCenter, 22);
            v.childForceExpandHeight = false;
            var pl = UI.Label(right.transform, Loc.T("CUSTOM_PLAYER_NUMBER"), 38, Theme.Secondary, TextAnchor.MiddleLeft, true);
            UI.Layout(pl, -1, 50);
            playersBox = UI.Rect(right.transform, "Players");
            UI.Layout(playersBox, -1, 4 * 84 + 3 * 8 + 70);
            UI.VBox(playersBox, 8, TextAnchor.UpperCenter).childForceExpandHeight = false;
            BuildPlayers();

            var opt = GameData.Battle;
            int minM = (int)(opt?.Float("MinMatchTime", 180) ?? 180), maxM = (int)(opt?.Float("MaxMatchTime", 480) ?? 480);
            int step = Mathf.Max(5, (int)(opt?.Float("TurnTimeIncrement", 5) ?? 5));
            Stepper(right.rectTransform, Loc.T("GAME_SETTINGS_MATCH_TIME"), () => MetaUI.Clock(matchTime), d => matchTime = Mathf.Clamp(matchTime + d * 60, minM, maxM));
            Stepper(right.rectTransform, Loc.T("GAME_SETTINGS_TURN_TIME"), () => turnTime + " s", d => turnTime = Mathf.Clamp(turnTime + d * step, 10, 60));
            Stepper(right.rectTransform, "Winning score", () => winScore == 0 ? "Off" : winScore.ToString(), d => winScore = Mathf.Clamp(winScore + d * 100, 0, 1000));
            var tg = UI.Toggle(right.transform, "Power-up crates", powerUps, b => powerUps = b);
            UI.Layout(tg, -1, 70);

            var start = UI.Button(Content, "Start!", StartGame, UI.ButtonStyle.Primary, 54);
            UI.Place((RectTransform)start.transform, new Vector2(1, 0), new Vector2(420, 110), Vector2.zero);
            var note = UI.Label(Content, "Everyone gets the same arsenal. Custom games give no rewards.", 28, Color.white, TextAnchor.MiddleLeft);
            UI.Anchor(note.rectTransform, 0, 0, 0.7f, 0.11f);
        }

        void AddMap(RectTransform grid, string id, string name)
        {
            int index = mapIds.Count;
            mapIds.Add(id);
            var b = MapButton(grid, id, name, () => { levelId = mapIds[index]; HighlightMap(); });
            mapFrames.Add(b.GetComponent<Image>());
        }

        // ---------- map locks (original LockedLevelButtonContainer: Level.MinLevel above the player's level) ----------
        public static int MapLevel(string id) => string.IsNullOrEmpty(id) ? 1 : (GameData.Get("Level", id)?.Int("MinLevel", 1) ?? 1);
        public static bool MapLocked(string id) => MapLevel(id) > ProfileService.P.level;

        /// <summary>Map thumbnail button ("" = random). Locked maps show the required level and can't be picked.</summary>
        public static Button MapButton(Transform grid, string id, string name, Action pick)
        {
            bool locked = MapLocked(id);
            int need = MapLevel(id);
            var b = UI.Button(grid, null, () =>
            {
                if (locked) { AudioManager.Sfx("Nomoney"); UI.Toast("Reach level " + need + " to play this map."); return; }
                pick();
            }, UI.ButtonStyle.Plain, 24, "Map " + name);
            if (string.IsNullOrEmpty(id))
            {
                var q = UI.Label(b.transform, "?", 110, Theme.Secondary, TextAnchor.MiddleCenter, true);
                UI.Anchor(q.rectTransform, 0.05f, 0.25f, 0.95f, 0.95f);
            }
            else
            {
                var img = UI.Image(b.transform, LevelThumbs.Get(id), Color.white, false);
                UI.Anchor(img.rectTransform, 0.04f, 0.24f, 0.96f, 0.96f);
            }
            var l = UI.Label(b.transform, name, 26, Theme.Text, TextAnchor.MiddleCenter, true);
            NoOutline(l);
            UI.Anchor(l.rectTransform, 0.02f, 0.01f, 0.98f, 0.24f);
            if (locked) ItemCards.LockOverlay(b.transform, "Lv " + need);
            return b;
        }

        /// <summary>Popup map picker (used by Practice). pick gets the level id, "" for a random map.</summary>
        public static void PickMap(string current, Action<string> pick)
        {
            var win = MetaUI.Window(Loc.T("GAME_SETTINGS_MAP"), new Vector2(1500, 860), out var layer);
            var host = UI.Rect(win, "Maps");
            UI.Stretch(host, 16, 16, 124, 16);
            var sr = UI.ScrollGrid(host, out var grid, new Vector2(280, 200), new Vector2(14, 14));
            UI.Stretch((RectTransform)sr.transform);
            var ids = new List<string> { "" };
            foreach (var r in BattleFactory.Levels()) ids.Add(r.Id);
            foreach (var id in ids)
            {
                var chosen = id;
                var b = MapButton(grid, id, string.IsNullOrEmpty(id) ? "Random" : BattleFactory.LevelDisplayName(id), () => { MetaUI.Close(layer); pick(chosen); });
                b.GetComponent<Image>().color = id == (current ?? "") ? Theme.Primary : Theme.PanelInner;
            }
        }

        public static void NoOutline(Text l) { var o = l.GetComponent<Outline>(); if (o) UnityEngine.Object.Destroy(o); }

        void HighlightMap()
        {
            for (int i = 0; i < mapFrames.Count; i++) mapFrames[i].color = mapIds[i] == levelId ? Theme.Primary : Theme.PanelInner;
        }

        void BuildPlayers()
        {
            UI.Clear(playersBox);
            for (int i = 0; i < seats.Count; i++)
            {
                int idx = i;
                var seat = seats[i];
                var row = UI.Rect(playersBox, "Seat " + i);
                UI.Layout(row, -1, 84);
                var dot = UI.Image(row, UI.Circle, Theme.PlayerColors[i % 4], false);
                UI.Place(dot.rectTransform, new Vector2(0, 0.5f), new Vector2(60, 60), new Vector2(4, 0));
                var input = UI.Input(row, seat.name, "Name", s => seat.name = string.IsNullOrEmpty(s) ? "Penguin " + (idx + 1) : s);
                UI.Anchor(input.GetComponent<RectTransform>(), 0, 0.05f, 0.5f, 0.95f);
                input.GetComponent<RectTransform>().offsetMin = new Vector2(80, 0);
                Button typeBtn = null;
                typeBtn = UI.Button(row, TypeNames[seat.type], () =>
                {
                    seat.type = (seat.type + 1) % TypeNames.Length;
                    if (idx == 0 && seat.type != 0) seat.type = 0;   // seat 1 is always you
                    typeBtn.GetComponentInChildren<Text>().text = TypeNames[seat.type];
                }, seat.type == 0 ? UI.ButtonStyle.Good : UI.ButtonStyle.Secondary, 32);
                UI.Anchor((RectTransform)typeBtn.transform, 0.52f, 0.05f, 0.86f, 0.95f);
                if (i >= 2)
                {
                    var rm = UI.Button(row, "-", () => { seats.RemoveAt(idx); BuildPlayers(); }, UI.ButtonStyle.Danger, 44);
                    UI.Anchor((RectTransform)rm.transform, 0.88f, 0.05f, 1f, 0.95f);
                }
            }
            if (seats.Count < (GameData.Battle?.Int("MaxNumberOfPlayers", 4) ?? 4))
            {
                var add = UI.Button(playersBox, "+ Add penguin", () =>
                {
                    seats.Add(new Seat { name = "Penguin " + (seats.Count + 1), type = 2 });
                    BuildPlayers();
                }, UI.ButtonStyle.Secondary, 34);
                UI.Layout(add, -1, 64);
            }
        }

        void Stepper(RectTransform parent, string label, Func<string> value, Action<int> change)
        {
            var row = UI.Rect(parent, label);
            UI.Layout(row, -1, 76);
            var t = UI.Label(row, label, 34, Theme.Text, TextAnchor.MiddleLeft);
            UI.Anchor(t.rectTransform, 0, 0, 0.5f, 1);
            Text v = null;
            var minus = UI.Button(row, "-", () => { change(-1); v.text = value(); }, UI.ButtonStyle.Secondary, 44);
            UI.Anchor((RectTransform)minus.transform, 0.52f, 0.06f, 0.64f, 0.94f);
            v = UI.Label(row, value(), 38, Theme.Text, TextAnchor.MiddleCenter, true);
            v.color = Theme.Secondary;
            UI.Anchor(v.rectTransform, 0.64f, 0, 0.86f, 1);
            var plus = UI.Button(row, "+", () => { change(1); v.text = value(); }, UI.ButtonStyle.Secondary, 44);
            UI.Anchor((RectTransform)plus.transform, 0.86f, 0.06f, 0.98f, 0.94f);
        }

        void StartGame()
        {
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
                PlayerSlot slot;
                if (s.type == 0)
                {
                    slot = new PlayerSlot { name = s.name, isAI = false, isLocalHuman = true, usesProfileInventory = false, level = P.level, colorIndex = i };
                    if (i == 0) { slot.head = P.wornHead; slot.chest = P.wornChest; slot.feet = P.wornFeet; slot.trophy = P.wornTrophy; }
                }
                else slot = BattleFactory.AiSlot(s.name, P.level, s.type - 1, i);
                slot.loadout = BattleFactory.CustomLoadout();
                slot.boosters.Clear();
                c.players.Add(slot);
            }
            BattleFactory.Launch(c);
        }
    }
}
