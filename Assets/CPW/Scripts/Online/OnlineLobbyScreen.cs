using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// Online menu: Quick Match, host a public or private game (private games show a short code), join by code,
    /// the list of open games, and the waiting room (players, Start for the host, invite friends, Leave).
    /// The host's map picker shows maps above the player's level locked (Level.MinLevel, like LockedLevelButtonContainer).
    /// Opened by the menus by reflection ("CPW.OnlineLobbyScreen"), so it keeps a public parameterless constructor.
    /// Works with any IOnlineService; when online play is not available it explains how to switch it on.
    /// </summary>
    public class OnlineLobbyScreen : UIScreen
    {
        public override bool ShowTopBar => false;

        /// <summary>Start a quick match as soon as the lobby is ready (the home screen's one-tap PLAY).</summary>
        public static bool AutoQuickMatch;
        /// <summary>Join this private game code as soon as the lobby is ready (accepted inbox invite).</summary>
        public static string PendingJoinCode;

        enum Mode { Offline, Connecting, Lobby, Searching, Room }

        static readonly int[] TurnTimes = { 10, 15, 20, 30, 45 };
        static readonly int[] MatchMinutes = { 3, 5, 8, 10 };
        // Remembered while the app runs so the host's choices survive screen rebuilds.
        static int levelChoice = -1, turnChoice = 2, timeChoice = 1;

        Mode mode = Mode.Connecting;
        RectTransform body;
        Text statusLabel;
        RectTransform matchList;
        float listTimer;
        int roomVersion = -1;
        bool alive, starting;
        string searchText = "";
        OnlineRoom lastRoom;   // kept so a failure reason written into the room after it closed can still be shown
        string codeInput = "";
        List<string> levels;

        IOnlineService S => Online.Service;

        public override void Build()
        {
            alive = true;
            var bg = UI.Panel(Root, Theme.Bg, false, "Bg");
            UI.Stretch(bg.rectTransform);

            var back = UI.Button(Root, "Back", OnBackPressed, UI.ButtonStyle.Secondary, 40);
            UI.Place((RectTransform)back.transform, new Vector2(0, 1), new Vector2(230, 100), new Vector2(30, -24));

            var title = UI.Label(Root, "Online Battle", 72, Theme.TextLight, TextAnchor.MiddleCenter, true);
            UI.Place(title.rectTransform, new Vector2(0.5f, 1), new Vector2(1000, 110), new Vector2(0, -20));

            statusLabel = UI.Label(Root, "", 34, Theme.Xp, TextAnchor.MiddleCenter);
            UI.Place(statusLabel.rectTransform, new Vector2(0.5f, 1), new Vector2(1500, 60), new Vector2(0, -135));

            body = UI.Rect(Root, "Body");
            UI.Anchor(body, 0.03f, 0.04f, 0.97f, 0.8f);

            levels = new List<string>();
            foreach (var r in BattleFactory.Levels()) levels.Add(r.Id);

            if (S.Room != null) { AutoQuickMatch = false; PendingJoinCode = null; mode = Mode.Room; Rebuild(); return; }
            if (S.Available) { mode = Mode.Lobby; Rebuild(); RunPendingAction(); return; }
            mode = Mode.Connecting;
            Rebuild();
            S.Init(ok =>
            {
                if (!alive) return;
                mode = ok ? Mode.Lobby : Mode.Offline;
                Rebuild();
                if (ok) RunPendingAction();
                else { AutoQuickMatch = false; PendingJoinCode = null; }
            });
        }

        void RunPendingAction()
        {
            if (AutoQuickMatch) { AutoQuickMatch = false; StartQuickMatch(); return; }
            if (!string.IsNullOrEmpty(PendingJoinCode)) { var c = PendingJoinCode; PendingJoinCode = null; Join(c); }
        }

        public override void OnHide()
        {
            alive = false;
            // Leaving the screen leaves the waiting room, unless we are leaving because the battle starts.
            if (!starting && (S.Room != null || mode == Mode.Searching)) S.CancelMatchmaking();
        }

        public override bool OnBack()
        {
            if (mode == Mode.Room || mode == Mode.Searching)
            {
                LeaveRoom();
                return true;
            }
            return false;
        }

        void OnBackPressed()
        {
            if (!OnBack()) ScreenManager.Back();
        }

        public override void Tick(float dt)
        {
            if (!alive) return;
            var room = S.Room;
            if (room != null)
            {
                lastRoom = room;
                if (mode != Mode.Room || room.version != roomVersion) { mode = Mode.Room; Rebuild(); }
                return;
            }
            if (mode == Mode.Room) { mode = S.Available ? Mode.Lobby : Mode.Offline; Rebuild(); return; }
            if (mode == Mode.Lobby && (listTimer -= dt) <= 0) RefreshList();
        }

        void SetStatus(string s)
        {
            if (statusLabel) statusLabel.text = s ?? "";
        }

        void Rebuild()
        {
            if (!alive || body == null) return;
            UI.Clear(body);
            switch (mode)
            {
                case Mode.Offline: BuildOffline(); break;
                case Mode.Connecting: BuildMessage("Connecting to the online service..."); SetStatus(S.Status); break;
                case Mode.Searching: BuildSearching(); break;
                case Mode.Lobby: BuildLobby(); break;
                case Mode.Room: BuildRoom(); break;
            }
        }

        // ------------------------------------------------------------------ offline

        void BuildMessage(string text)
        {
            var panel = UI.Panel(body, Theme.PanelDark, true, "Message");
            UI.Anchor(panel.rectTransform, 0.15f, 0.3f, 0.85f, 0.8f);
            var l = UI.Label(panel.transform, text, 44, Theme.TextLight);
            UI.Stretch(l.rectTransform, 40, 40, 30, 30);
        }

        void BuildOffline()
        {
            SetStatus(S.Status);
            var panel = UI.Panel(body, Theme.Panel, true, "Offline");
            UI.Anchor(panel.rectTransform, 0.1f, 0.12f, 0.9f, 0.95f);
            var t = UI.Label(panel.transform, "Online play is not available", 56, Theme.Secondary, TextAnchor.MiddleCenter, true);
            UI.Anchor(t.rectTransform, 0.05f, 0.78f, 0.95f, 0.95f);
            var msg = UI.Label(panel.transform,
                "Everything else works offline: practice, quick matches against computer penguins and local games.\n\n" +
                "Online battles, cloud save and the world leaderboard need a Firebase project. " +
                "The game's owner sets it up once (see Docs/FIREBASE.md). If it is set up, check your internet connection.\n\n" +
                S.Status, 36, Theme.Text);
            UI.Anchor(msg.rectTransform, 0.06f, 0.26f, 0.94f, 0.78f);
            var row = UI.Rect(panel.transform, "Buttons");
            UI.Anchor(row, 0.1f, 0.05f, 0.9f, 0.22f);
            UI.HBox(row, 40);
            var retry = UI.Button(row, "Try again", () =>
            {
                mode = Mode.Connecting;
                Rebuild();
                S.Init(ok => { if (!alive) return; mode = ok ? Mode.Lobby : Mode.Offline; Rebuild(); });
            }, UI.ButtonStyle.Primary);
            UI.Layout(retry, 380, 120);
            var back = UI.Button(row, "Back", () => ScreenManager.Back(), UI.ButtonStyle.Secondary);
            UI.Layout(back, 380, 120);
        }

        // ------------------------------------------------------------------ lobby

        void BuildLobby()
        {
            SetStatus(S.Status);
            // Left column: actions and host settings
            var left = UI.Panel(body, Theme.PanelDark, true, "Actions");
            UI.Anchor(left.rectTransform, 0, 0, 0.42f, 1, 6);
            var col = UI.Rect(left.transform, "Col");
            UI.Stretch(col, 30, 30, 26, 26);
            UI.VBox(col, 18, TextAnchor.UpperCenter);

            UI.Layout(UI.Button(col, "Quick Match", StartQuickMatch, UI.ButtonStyle.Primary, 48), -1, 120);

            var hostRow = UI.Rect(col, "HostRow");
            UI.HBox(hostRow, 18, TextAnchor.MiddleCenter, 0, true);
            UI.Layout(hostRow, -1, 105);
            UI.Layout(UI.Button(hostRow, "Host Public", () => Host(false), UI.ButtonStyle.Secondary, 36), -1, 105, 1);
            UI.Layout(UI.Button(hostRow, "Host Private", () => Host(true), UI.ButtonStyle.Secondary, 36), -1, 105, 1);

            // Host settings (map, turn time, match time) as tap-to-cycle buttons
            var setRow = UI.Rect(col, "Settings");
            UI.HBox(setRow, 12, TextAnchor.MiddleCenter, 0, true);
            UI.Layout(setRow, -1, 90);
            Button mapB = null, turnB = null, timeB = null;
            mapB = UI.Button(setRow, MapText(), () => PickMap(() => { if (mapB) mapB.GetComponentInChildren<Text>().text = MapText(); }), UI.ButtonStyle.Plain, 28);
            UI.Layout(mapB, -1, 90, 1.6f);
            turnB = UI.Button(setRow, TurnText(), () => { turnChoice = (turnChoice + 1) % TurnTimes.Length; turnB.GetComponentInChildren<Text>().text = TurnText(); }, UI.ButtonStyle.Plain, 28);
            UI.Layout(turnB, -1, 90, 1);
            timeB = UI.Button(setRow, TimeText(), () => { timeChoice = (timeChoice + 1) % MatchMinutes.Length; timeB.GetComponentInChildren<Text>().text = TimeText(); }, UI.ButtonStyle.Plain, 28);
            UI.Layout(timeB, -1, 90, 1);

            var hint = UI.Label(col, "Tap a setting to change it (for games you host).", 26, Theme.Muted);
            UI.Layout(hint, -1, 40);

            var joinLabel = UI.Label(col, "Join a friend's game", 36, Theme.TextLight, TextAnchor.MiddleLeft, true);
            UI.Layout(joinLabel, -1, 60);
            var joinRow = UI.Rect(col, "JoinRow");
            UI.HBox(joinRow, 18, TextAnchor.MiddleCenter, 0, true);
            UI.Layout(joinRow, -1, 105);
            var input = UI.Input(joinRow, codeInput, "CODE", v => codeInput = v);
            input.characterLimit = 6;
            input.characterValidation = InputField.CharacterValidation.Alphanumeric;
            input.onValueChanged.AddListener(v => codeInput = v);
            UI.Layout(input, -1, 105, 1.4f);
            UI.Layout(UI.Button(joinRow, "Join", () => Join(codeInput), UI.ButtonStyle.Good, 40), -1, 105, 1);

            // Right column: open public games
            var right = UI.Panel(body, Theme.Panel, true, "OpenGames");
            UI.Anchor(right.rectTransform, 0.44f, 0, 1, 1, 6);
            var head = UI.Label(right.transform, "Open games", 48, Theme.Secondary, TextAnchor.MiddleLeft, true);
            UI.Anchor(head.rectTransform, 0.04f, 0.86f, 0.6f, 0.98f);
            var refresh = UI.Button(right.transform, "Refresh", RefreshList, UI.ButtonStyle.Secondary, 32);
            UI.Anchor((RectTransform)refresh.transform, 0.68f, 0.87f, 0.96f, 0.98f);
            var sr = UI.ScrollList(right.transform, out matchList, true, 14, 14);
            UI.Anchor((RectTransform)sr.transform, 0.02f, 0.03f, 0.98f, 0.85f);
            var loading = UI.Label(matchList, "Loading...", 34, Theme.Muted);
            UI.Layout(loading, -1, 90);
            RefreshList();
        }

        string MapText() => "Map: " + (!ChoiceUnlocked() ? "Random" : BattleFactory.LevelDisplayName(levels[levelChoice]));

        static int MinLevel(string levelId) => GameData.Get("Level", levelId)?.Int("MinLevel", 1) ?? 1;
        static bool Unlocked(string levelId) => ProfileService.P.level >= MinLevel(levelId);
        bool ChoiceUnlocked() => levelChoice >= 0 && levelChoice < levels.Count && Unlocked(levels[levelChoice]);

        /// <summary>Map picker for hosted games: Random + every map; maps above the player's level are locked.</summary>
        void PickMap(System.Action changed)
        {
            var win = MetaUI.Window("Choose a map", new Vector2(1500, 860), out var layer);
            var host = UI.Rect(win, "Maps");
            UI.Stretch(host, 20, 20, 120, 20);
            UI.ScrollGrid(host, out var grid, new Vector2(270, 210), new Vector2(14, 14));
            UI.Stretch((RectTransform)host.GetChild(0));
            for (int i = -1; i < levels.Count; i++)
            {
                int idx = i;
                string id = i < 0 ? null : levels[i];
                bool locked = id != null && !Unlocked(id);
                bool selected = i < 0 ? !ChoiceUnlocked() : levelChoice == i && !locked;
                var b = UI.Button(grid, null, () =>
                {
                    if (locked) { UI.Toast("Reach level " + MinLevel(id) + " to host this map."); return; }
                    levelChoice = idx;
                    MetaUI.Close(layer);
                    changed?.Invoke();
                }, selected ? UI.ButtonStyle.Primary : UI.ButtonStyle.Plain, 24, "Map " + (id ?? "random"));
                var thumb = id != null ? LevelThumbs.Get(id) : null;
                if (thumb != null)
                {
                    var img = UI.Image(b.transform, thumb, null, false);
                    UI.Anchor(img.rectTransform, 0.04f, 0.26f, 0.96f, 0.95f);
                }
                else
                {
                    var q = UI.Label(b.transform, "?", 90, Theme.Secondary, TextAnchor.MiddleCenter, true);
                    UI.Anchor(q.rectTransform, 0.04f, 0.26f, 0.96f, 0.95f);
                }
                var name = UI.Label(b.transform, id == null ? "Random" : BattleFactory.LevelDisplayName(id), 26, selected ? Theme.PrimaryText : Theme.Text);
                UI.Anchor(name.rectTransform, 0.03f, 0.02f, 0.97f, 0.25f);
                if (!locked) continue;
                var shade = UI.Panel(b.transform, MetaUI.Locked, true, "Locked");
                UI.Stretch(shade.rectTransform);
                shade.raycastTarget = false;
                var lockIcon = ModelLibrary.Icon("Ui/lock");
                if (lockIcon != null)
                {
                    var li = UI.Image(shade.transform, lockIcon);
                    UI.Anchor(li.rectTransform, 0.35f, 0.42f, 0.65f, 0.85f);
                }
                var lv = UI.Label(shade.transform, "Level " + MinLevel(id), 34, Color.white, TextAnchor.MiddleCenter, true);
                UI.Anchor(lv.rectTransform, 0.05f, lockIcon != null ? 0.18f : 0.3f, 0.95f, lockIcon != null ? 0.42f : 0.7f);
            }
        }
        string TurnText() => "Turn: " + TurnTimes[turnChoice] + "s";
        string TimeText() => "Time: " + MatchMinutes[timeChoice] + "m";

        void RefreshList()
        {
            listTimer = 6f;
            S.ListOpenMatches(list =>
            {
                if (!alive || mode != Mode.Lobby || matchList == null) return;
                UI.Clear(matchList);
                if (list == null || list.Count == 0)
                {
                    var none = UI.Label(matchList, "No open games right now.\nTap Quick Match or host one!", 34, Theme.Muted);
                    UI.Layout(none, -1, 140);
                    return;
                }
                foreach (var m in list) AddMatchRow(m);
            });
        }

        void AddMatchRow(OnlineMatchInfo m)
        {
            var row = UI.Panel(matchList, Theme.PanelInner, true, "Match");
            UI.Layout(row, -1, 110);
            var name = UI.Label(row.transform, m.hostName + "'s game", 36, Theme.Text, TextAnchor.MiddleLeft, false);
            UI.Anchor(name.rectTransform, 0.03f, 0.45f, 0.62f, 0.95f);
            var info = UI.Label(row.transform,
                (string.IsNullOrEmpty(m.levelId) ? "Random map" : Loc.Prettify(m.levelId)) + "   " + m.players + "/" + m.maxPlayers + " players",
                28, Theme.Muted, TextAnchor.MiddleLeft);
            UI.Anchor(info.rectTransform, 0.03f, 0.05f, 0.62f, 0.5f);
            var id = m.matchId;
            var join = UI.Button(row.transform, "Join", () => Join(id), UI.ButtonStyle.Good, 38);
            UI.Anchor((RectTransform)join.transform, 0.7f, 0.12f, 0.97f, 0.88f);
        }

        BattleConfig HostSettings()
        {
            var c = FirebaseService.DefaultSettings();
            c.levelId = ChoiceUnlocked() ? levels[levelChoice] : "";
            c.turnTime = TurnTimes[turnChoice];
            c.matchTime = MatchMinutes[timeChoice] * 60;
            return c;
        }

        // ------------------------------------------------------------------ actions

        void Host(bool isPrivate)
        {
            searchText = "Creating game...";
            mode = Mode.Searching;
            Rebuild();
            S.HostMatch(HostSettings(), isPrivate, info => { if (alive) Rebuild(); }, OnStarted);
        }

        void Join(string idOrCode)
        {
            if (string.IsNullOrWhiteSpace(idOrCode)) { UI.Toast("Enter the code your friend sees."); return; }
            searchText = "Joining...";
            mode = Mode.Searching;
            Rebuild();
            S.JoinMatch(idOrCode.Trim(), OnStarted, err =>
            {
                if (!alive) return;
                mode = S.Available ? Mode.Lobby : Mode.Offline;
                Rebuild();
                UI.Message("Can't join", err);
            });
        }

        void StartQuickMatch()
        {
            searchText = "Looking for a game...";
            mode = Mode.Searching;
            Rebuild();
            S.QuickMatch(OnStarted, s =>
            {
                searchText = s;
                if (alive && mode == Mode.Searching) SetStatus(s);
            });
        }

        void OnStarted(BattleConfig config)
        {
            if (!alive) return;
            if (config == null)
            {
                // cancelled or failed (the reason is the room status)
                var why = lastRoom != null && !string.IsNullOrEmpty(lastRoom.status) ? lastRoom.status : searchText;
                lastRoom = null;
                mode = S.Available ? Mode.Lobby : Mode.Offline;
                Rebuild();
                if (!string.IsNullOrEmpty(why)) UI.Toast(why);
                return;
            }
            starting = true;
            GameManager.StartBattle(config);
            starting = false;
        }

        void LeaveRoom()
        {
            S.CancelMatchmaking();
            mode = S.Available ? Mode.Lobby : Mode.Offline;
            Rebuild();
        }

        // ------------------------------------------------------------------ searching / room

        void BuildSearching()
        {
            SetStatus(searchText);
            var panel = UI.Panel(body, Theme.PanelDark, true, "Searching");
            UI.Anchor(panel.rectTransform, 0.2f, 0.2f, 0.8f, 0.85f);
            var l = UI.Label(panel.transform, "Please wait...", 52, Theme.TextLight, TextAnchor.MiddleCenter, true);
            UI.Anchor(l.rectTransform, 0.05f, 0.45f, 0.95f, 0.85f);
            var cancel = UI.Button(panel.transform, "Cancel", LeaveRoom, UI.ButtonStyle.Danger, 40);
            UI.Anchor((RectTransform)cancel.transform, 0.3f, 0.1f, 0.7f, 0.32f);
        }

        void BuildRoom()
        {
            var room = S.Room;
            if (room == null) return;
            roomVersion = room.version;
            SetStatus(room.status);

            // Left: code and settings
            var left = UI.Panel(body, Theme.PanelDark, true, "Info");
            UI.Anchor(left.rectTransform, 0, 0, 0.4f, 1, 6);
            string kind = room.isQuickMatch ? "Quick Match" : room.isPrivate ? "Private game" : "Public game";
            var k = UI.Label(left.transform, kind, 52, Theme.TextLight, TextAnchor.MiddleCenter, true);
            UI.Anchor(k.rectTransform, 0.05f, 0.82f, 0.95f, 0.97f);
            if (!string.IsNullOrEmpty(room.code))
            {
                var cl = UI.Label(left.transform, "Game code", 34, Theme.Muted);
                UI.Anchor(cl.rectTransform, 0.05f, 0.7f, 0.95f, 0.8f);
                var codeBox = UI.Panel(left.transform, Theme.Panel, true, "Code");
                UI.Anchor(codeBox.rectTransform, 0.12f, 0.45f, 0.88f, 0.69f);
                var code = UI.Label(codeBox.transform, room.code, 96, Theme.Secondary, TextAnchor.MiddleCenter, true);
                UI.Stretch(code.rectTransform, 10, 10, 6, 6);
                var share = UI.Label(left.transform, "Friends can join with this code\n(Online > Join).", 30, Theme.TextLight);
                UI.Anchor(share.rectTransform, 0.05f, 0.27f, 0.95f, 0.44f);
            }
            var map = UI.Label(left.transform, "Map: " + (string.IsNullOrEmpty(room.levelId) ? "Random" : Loc.Prettify(room.levelId)), 32, Theme.Xp);
            UI.Anchor(map.rectTransform, 0.05f, 0.08f, 0.95f, 0.25f);

            // Right: players and buttons
            var right = UI.Panel(body, Theme.Panel, true, "Players");
            UI.Anchor(right.rectTransform, 0.42f, 0, 1, 1, 6);
            var head = UI.Label(right.transform, "Players " + room.players.Count + "/" + room.maxPlayers, 48, Theme.Secondary, TextAnchor.MiddleLeft, true);
            UI.Anchor(head.rectTransform, 0.04f, 0.86f, 0.96f, 0.98f);
            var listRt = UI.Rect(right.transform, "List");
            UI.Anchor(listRt, 0.04f, 0.26f, 0.96f, 0.85f);
            UI.VBox(listRt, 12, TextAnchor.UpperCenter);
            for (int i = 0; i < room.maxPlayers; i++)
            {
                var row = UI.Panel(listRt, i < room.players.Count ? Theme.PanelInner : new Color(1, 1, 1, 0.35f), true, "Slot");
                UI.Layout(row, -1, 92);
                if (i >= room.players.Count)
                {
                    var w = UI.Label(row.transform, "waiting for a penguin...", 30, Theme.Muted);
                    UI.Stretch(w.rectTransform, 30, 30, 4, 4);
                    continue;
                }
                var p = room.players[i];
                var dot = UI.Image(row.transform, UI.Circle, Theme.PlayerColors[i % Theme.PlayerColors.Length], false, "Color");
                UI.Place(dot.rectTransform, new Vector2(0, 0.5f), new Vector2(56, 56), new Vector2(22, 0));
                string tag = (p.isHost ? "  (host)" : "") + (p.isLocal ? "  (you)" : "");
                var n = UI.Label(row.transform, p.name + tag, 36, Theme.Text, TextAnchor.MiddleLeft);
                UI.Anchor(n.rectTransform, 0.12f, 0, 0.78f, 1);
                var lv = UI.Label(row.transform, "Lv " + p.level, 32, Theme.Secondary, TextAnchor.MiddleRight, true);
                UI.Anchor(lv.rectTransform, 0.78f, 0, 0.96f, 1);
            }

            var buttons = UI.Rect(right.transform, "Buttons");
            UI.Anchor(buttons, 0.04f, 0.03f, 0.96f, 0.22f);
            UI.HBox(buttons, 30, TextAnchor.MiddleCenter, 0, true);
            UI.Layout(UI.Button(buttons, "Leave", LeaveRoom, UI.ButtonStyle.Danger, 40), -1, 115, 1);
            if (!room.isQuickMatch && !string.IsNullOrEmpty(room.code) && room.players.Count < room.maxPlayers)
                UI.Layout(UI.Button(buttons, "Invite", () => InviteFriends(room.code, room.matchId), UI.ButtonStyle.Secondary, 40), -1, 115, 1);
            if (room.isHost && !room.isQuickMatch)
            {
                var start = UI.Button(buttons, room.players.Count < 2 ? "Need 2+ players" : "Start!", () => S.StartHostedMatch(), UI.ButtonStyle.Primary, 44);
                start.interactable = room.CanStart;
                UI.Layout(start, -1, 115, 1.4f);
            }
            else
            {
                var wait = UI.Label(buttons, room.isQuickMatch ? "Starts automatically" : "Waiting for the host", 32, Theme.Muted);
                UI.Layout(wait, -1, 115, 1.4f);
            }
        }

        /// <summary>Send friends an inbox invite carrying this room's join code (original FriendSelector invites).</summary>
        void InviteFriends(string code, string matchId)
        {
            var win = MetaUI.Window("Invite friends", new Vector2(1100, 760), out var layer);
            var host = UI.Rect(win, "List");
            UI.Stretch(host, 24, 24, 124, 24);
            UI.ScrollList(host, out var list, true, 10, 8);
            UI.Stretch((RectTransform)host.GetChild(0));
            var loading = UI.Label(list, "Loading friends...", 34, Theme.Muted);
            UI.Layout(loading, -1, 90);
            Social.LoadFriends((friends, err) =>
            {
                if (!layer || !list) return;
                UI.Clear(list);
                if (err != null || friends.Count == 0)
                {
                    var l = UI.Label(list, err != null ? "Could not load your friends.\n" + err : "No friends yet. Add friends with their friend code (Home > Friends), then invite them here.", 32, Theme.Muted);
                    UI.Layout(l, -1, 160);
                    return;
                }
                foreach (var f in friends)
                {
                    var fr = f;
                    var row = UI.Panel(list, Theme.PanelInner, true, "Friend");
                    UI.Layout(row, -1, 96);
                    var dot = UI.Image(row.transform, UI.Circle, fr.online ? Theme.Good : Theme.Muted, false, "Online");
                    UI.Place(dot.rectTransform, new Vector2(0, 0.5f), new Vector2(30, 30), new Vector2(24, 0));
                    var n = UI.Label(row.transform, fr.name + "   Lv " + fr.level + (fr.online ? "" : "   (away)"), 34, Theme.Text, TextAnchor.MiddleLeft);
                    UI.Anchor(n.rectTransform, 0.08f, 0, 0.7f, 1);
                    Button b = null;
                    b = UI.Button(row.transform, "Invite", () =>
                    {
                        b.interactable = false;
                        Social.SendInvite(fr, code, matchId, e =>
                        {
                            if (!b) return;
                            var t = b.GetComponentInChildren<Text>();
                            if (e == null) { if (t) t.text = "Sent!"; }
                            else { b.interactable = true; UI.Toast("Invite failed: " + e); }
                        });
                    }, UI.ButtonStyle.Good, 34);
                    UI.Anchor((RectTransform)b.transform, 0.72f, 0.12f, 0.97f, 0.88f);
                }
            });
        }
    }
}
