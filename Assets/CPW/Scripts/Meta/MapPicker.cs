using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// Map locks shared by every map picker (Custom lobby, Practice, online host): a map is locked while its
    /// Level.MinLevel is above the player's level (original LockedLevelButtonContainer). Settings has a
    /// "Unlock all maps (testing)" switch that lifts every lock (saved in PlayerPrefs, not in the profile).
    /// </summary>
    public static class MapLocks
    {
        const string PrefKey = "cpw_unlock_all_maps";
        static int cached = -1;

        public static bool UnlockAll
        {
            get
            {
                if (cached < 0) cached = PlayerPrefs.GetInt(PrefKey, 0) != 0 ? 1 : 0;
                return cached == 1;
            }
            set
            {
                cached = value ? 1 : 0;
                PlayerPrefs.SetInt(PrefKey, cached);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Player level needed for this map ("" = random map, always 1).</summary>
        public static int MinLevel(string levelId) => string.IsNullOrEmpty(levelId) ? 1 : (GameData.Get("Level", levelId)?.Int("MinLevel", 1) ?? 1);

        public static bool Locked(string levelId) => !UnlockAll && MinLevel(levelId) > ProfileService.P.level;
    }

    /// <summary>
    /// Map picker: a row of theme chips (All, Forest, Desert, Mountain, Winter and any theme found in the Level
    /// rows, e.g. new lava maps) over a scrolling grid of big thumbnails. Built from BattleFactory.Levels() so maps
    /// added to the level data show up by themselves. Used inline by the Custom lobby and as a popup (PickMap).
    /// </summary>
    public class MapPicker
    {
        static int themeIndex;   // remembered filter while the app runs

        readonly Func<string> current;
        readonly Action<string> pick;
        readonly bool withRandom;
        readonly Vector2 cell;
        readonly List<string> themes = new List<string>();
        readonly List<Button> chips = new List<Button>();
        readonly List<string> shownIds = new List<string>();
        readonly List<Image> shownFrames = new List<Image>();
        RectTransform grid;
        ScrollRect scroll;

        /// <summary>current returns the picked level id ("" = random); pick is called with the chosen id.</summary>
        public MapPicker(RectTransform host, Func<string> current, Action<string> pick, bool withRandom = true, Vector2? cellSize = null)
        {
            this.current = current;
            this.pick = pick;
            this.withRandom = withRandom;
            cell = cellSize ?? new Vector2(300, 200);
            themes.Add("All");
            themes.AddRange(BattleFactory.LevelThemes());
            if (themeIndex >= themes.Count) themeIndex = 0;

            var chipRow = UI.Rect(host, "Themes");
            chipRow.anchorMin = new Vector2(0, 1); chipRow.anchorMax = new Vector2(1, 1); chipRow.pivot = new Vector2(0.5f, 1);
            chipRow.sizeDelta = new Vector2(0, 66);
            chipRow.anchoredPosition = Vector2.zero;
            var h = UI.HBox(chipRow, 8, TextAnchor.MiddleLeft, 0, true);
            h.childForceExpandHeight = true;
            for (int i = 0; i < themes.Count; i++)
            {
                int idx = i;
                var b = UI.Button(chipRow, themes[i], () => SetTheme(idx), UI.ButtonStyle.Dark, 28, "Theme " + themes[i]);
                b.GetComponent<Image>().color = ThemeColor(themes[i]);
                UI.Layout(b, -1, -1, 1, 1);
                chips.Add(b);
            }

            var gridHost = UI.Rect(host, "Grid");
            UI.Stretch(gridHost, 0, 0, 74, 0);
            scroll = UI.ScrollGrid(gridHost, out grid, cell, new Vector2(14, 14));
            UI.Stretch((RectTransform)scroll.transform);
            Fill();
        }

        public static Color ThemeColor(string theme)
        {
            switch (theme)
            {
                case "All": return Theme.PanelDark;
                case "Forest": return new Color32(60, 140, 60, 255);
                case "Desert": return new Color32(206, 140, 50, 255);
                case "Mountain": return new Color32(110, 110, 125, 255);
                case "Winter": return new Color32(70, 150, 210, 255);
                case "Lava": case "Volcano": return new Color32(200, 60, 30, 255);
                default: return MetaUI.TintFor(theme);
            }
        }

        void SetTheme(int i)
        {
            themeIndex = i;
            Fill();
            if (scroll) scroll.verticalNormalizedPosition = 1;
        }

        void Fill()
        {
            for (int i = 0; i < chips.Count; i++)
            {
                bool on = i == themeIndex;
                chips[i].GetComponent<Image>().color = on ? Theme.Primary : ThemeColor(themes[i]);
                var l = chips[i].GetComponentInChildren<Text>();
                if (l) l.color = on ? Theme.PrimaryText : Color.white;
            }
            UI.Clear(grid);
            shownIds.Clear();
            shownFrames.Clear();
            string filter = themes[themeIndex];
            if (withRandom && filter == "All") Add("", "Random");
            foreach (var r in BattleFactory.Levels())
                if (filter == "All" || BattleFactory.LevelTheme(r.Id) == filter) Add(r.Id, BattleFactory.LevelDisplayName(r.Id));
            Highlight();
        }

        void Add(string id, string name)
        {
            var b = Thumb(grid, id, name, () => { pick?.Invoke(id); Highlight(); });
            shownIds.Add(id);
            shownFrames.Add(b.GetComponent<Image>());
        }

        /// <summary>Recolor the frames after the selection changed elsewhere.</summary>
        public void Highlight()
        {
            string cur = current?.Invoke() ?? "";
            for (int i = 0; i < shownFrames.Count; i++)
                if (shownFrames[i]) shownFrames[i].color = shownIds[i] == cur ? Theme.Primary : Theme.PanelInner;
        }

        /// <summary>Map thumbnail button ("" = random). Locked maps show the required level and can't be picked.</summary>
        public static Button Thumb(Transform parent, string id, string name, Action onPick)
        {
            bool locked = MapLocks.Locked(id);
            int need = MapLocks.MinLevel(id);
            var b = UI.Button(parent, null, () =>
            {
                if (locked)
                {
                    AudioManager.Sfx("Nomoney");
                    UI.Toast("Reach level " + need + " to play this map (or Settings > Unlock all maps).");
                    return;
                }
                onPick?.Invoke();
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
                // theme tag in the corner so "All" stays readable
                string theme = BattleFactory.LevelTheme(id);
                var tag = UI.Panel(b.transform, ThemeColor(theme), true, "Tag");
                tag.raycastTarget = false;
                UI.Anchor(tag.rectTransform, 0.05f, 0.8f, 0.5f, 0.95f);
                var tl = UI.Label(tag.transform, theme, 22, Color.white, TextAnchor.MiddleCenter, true);
                UI.Stretch(tl.rectTransform, 4, 4, 1, 1);
            }
            var l = UI.Label(b.transform, name, 26, Theme.Text, TextAnchor.MiddleCenter, true);
            CustomGameScreen.NoOutline(l);
            UI.Anchor(l.rectTransform, 0.02f, 0.01f, 0.98f, 0.24f);
            if (locked) ItemCards.LockOverlay(b.transform, "Lv " + need);
            return b;
        }

        /// <summary>Popup picker. pick gets the level id, "" for a random map.</summary>
        public static void Popup(string title, string current, Action<string> pick, bool withRandom = true)
        {
            var win = MetaUI.Window(title, new Vector2(1500, 880), out var layer);
            var host = UI.Rect(win, "Maps");
            UI.Stretch(host, 20, 20, 124, 20);
            new MapPicker(host, () => current ?? "", id => { MetaUI.Close(layer); pick?.Invoke(id); }, withRandom);
        }
    }

    /// <summary>A little penguin drawn from circles (seat cards): team-colored disc, body, belly, eyes and beak.</summary>
    public static class PenguinGlyph
    {
        public static RectTransform Create(Transform parent, Color team)
        {
            var root = UI.Rect(parent, "Penguin");
            MetaUI.Square(root);
            var disc = UI.Image(root, UI.Circle, team, false, "Disc");
            UI.Stretch(disc.rectTransform);
            var ring = UI.Image(root, UI.Circle, Color.Lerp(team, Color.white, 0.35f), false, "Ring");
            UI.Anchor(ring.rectTransform, 0.06f, 0.06f, 0.94f, 0.94f);
            Part(root, new Color(0.1f, 0.12f, 0.2f), 0.22f, 0.08f, 0.78f, 0.88f, "Body");
            Part(root, Color.white, 0.32f, 0.1f, 0.68f, 0.62f, "Belly");
            Part(root, Color.white, 0.33f, 0.6f, 0.49f, 0.76f, "EyeL");
            Part(root, Color.white, 0.51f, 0.6f, 0.67f, 0.76f, "EyeR");
            Part(root, Color.black, 0.39f, 0.63f, 0.46f, 0.71f, "PupilL");
            Part(root, Color.black, 0.54f, 0.63f, 0.61f, 0.71f, "PupilR");
            Part(root, new Color(1f, 0.6f, 0.1f), 0.43f, 0.5f, 0.57f, 0.6f, "Beak");
            Part(root, team, 0.27f, 0.42f, 0.73f, 0.5f, "Scarf");
            return root;
        }

        static void Part(RectTransform root, Color c, float x0, float y0, float x1, float y1, string name)
        {
            var i = UI.Image(root, UI.Circle, c, false, name);
            UI.Anchor(i.rectTransform, x0, y0, x1, y1);
        }
    }
}
