using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// IOnlineService on Firebase (Auth + Realtime Database over REST). Installed automatically before the first scene
    /// loads when Resources/firebase_config.json exists and is filled in; otherwise the game stays on OfflineService.
    ///
    /// Database layout (see Docs/FIREBASE.md and Docs/firebase/database.rules.json):
    ///   users/{uid}/profile      {json, updated, name}     cloud save (PlayerProfile as JsonUtility text)
    ///   leaderboard/{uid}        {name, xp, level, wins, updated}
    ///   lobby/{matchId}          {host, hostName, levelId, players, maxPlayers, quick, hb}   public waiting matches
    ///   codes/{CODE}             {match, host, hb}          short join codes
    ///   matches/{matchId}        {host, hostName, isPrivate, quick, code, state, created, settings{...},
    ///                             players/{uid}{name, level, head, chest, feet, trophy, items, joined, hb, left},
    ///                             order[uid...], turn{...}, actions/..., snapshots/...}
    /// </summary>
    public sealed class FirebaseService : IOnlineService
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            var cfg = FirebaseConfig.Load();
            if (cfg == null) return;
            if (!cfg.IsValid)
            {
                Debug.LogWarning("CPW: firebase_config.json is incomplete; online features stay off. See Docs/FIREBASE.md.");
                return;
            }
            Online.Service = new FirebaseService(cfg);
        }

        const int MaxPlayers = 4;
        const float RoomPoll = 1f, RoomHeartbeat = 3f;
        const long StaleMs = 30000;          // lobby entries / room players without a heartbeat for this long are ignored
        const float QuickStartDelay = 15f;   // quick match: start this long after the last player joined (original: 15 s extra wait)
        const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no I/O/0/1 to avoid confusion

        readonly FirebaseConfig cfg;
        FirebaseClient fb;
        bool signedIn, initBusy;
        string status = "Online: connecting...";
        readonly List<Action<bool>> initWaiters = new List<Action<bool>>();

        // cloud save debounce
        string pendingProfileJson;
        PlayerProfile pendingProfile;
        float pushTimer;

        // waiting room
        RoomState room;

        public FirebaseService(FirebaseConfig config) { cfg = config; }

        public bool Available => signedIn && fb != null && fb.SignedIn;
        public string Status => status;
        public string UserId => fb != null ? fb.Uid : "";
        public OnlineRoom Room => room?.info;

        // ------------------------------------------------------------------ init / profile

        public void Init(Action<bool> done)
        {
            if (Available) { done?.Invoke(true); return; }
            if (done != null) initWaiters.Add(done);
            if (initBusy) return;
            initBusy = true;
            if (fb == null)
            {
                fb = FirebaseClient.Create(cfg);
                fb.Updated += Update;
            }
            status = "Online: connecting...";
            fb.EnsureAuth(ok =>
            {
                if (!ok) { Finish(false, "Offline: " + fb.LastError); return; }
                // Touch users/{uid}/lastSeen with a server timestamp: checks the database URL/rules and syncs the clock.
                fb.Put("users/" + fb.Uid + "/lastSeen", Fb.ServerTime, r =>
                {
                    if (r.ok) fb.LearnServerTime(r.Json);
                    if (r.ok) Finish(true, "Online (Firebase), player id " + Short(fb.Uid));
                    else Finish(false, "Offline: database error: " + r.error);
                });
            });
        }

        void Finish(bool ok, string text)
        {
            signedIn = ok;
            status = text;
            initBusy = false;
            if (!ok) Debug.LogWarning("CPW: " + text);
            var w = initWaiters.ToArray();
            initWaiters.Clear();
            foreach (var a in w)
            {
                try { a(ok); } catch (Exception e) { Debug.LogException(e); }
            }
        }

        static string Short(string uid) => string.IsNullOrEmpty(uid) ? "?" : (uid.Length > 6 ? uid.Substring(0, 6) : uid);

        /// <summary>Run ok() when signed in (signing in first if needed), otherwise fail(reason).</summary>
        void Ready(Action ok, Action<string> fail)
        {
            if (Available) { ok(); return; }
            Init(r => { if (r) ok(); else fail?.Invoke(status); });
        }

        /// <summary>Cloud save. Saves happen often (every purchase), so uploads are batched: at most one every 4 s.</summary>
        public void PushProfile(PlayerProfile p)
        {
            if (p == null || !Available) return;
            pendingProfile = p;
            pendingProfileJson = JsonUtility.ToJson(p);
            if (pushTimer <= 0) pushTimer = 4f;
        }

        void FlushProfile()
        {
            if (pendingProfileJson == null || !Available) return;
            var body = new Dictionary<string, object>
            {
                { "json", pendingProfileJson },
                { "name", Clip(pendingProfile.displayName, 32) },
                { "updated", Fb.ServerTime },
            };
            var p = pendingProfile;
            pendingProfileJson = null;
            pendingProfile = null;
            fb.Put("users/" + fb.Uid + "/profile", body, r =>
            {
                if (!r.ok) Debug.LogWarning("CPW: cloud save failed: " + r.error);
            });
            SubmitStats(p);
        }

        public void PullProfile(Action<PlayerProfile> done)
        {
            Ready(() => fb.Get("users/" + fb.Uid + "/profile", r =>
            {
                PlayerProfile p = null;
                var json = Fb.Str(r.Obj, "json");
                if (r.ok && !string.IsNullOrEmpty(json))
                {
                    try { p = JsonUtility.FromJson<PlayerProfile>(json); }
                    catch (Exception e) { Debug.LogWarning("CPW: cloud profile unreadable: " + e.Message); }
                }
                done?.Invoke(p);
            }), _ => done?.Invoke(null));
        }

        public void SubmitStats(PlayerProfile p)
        {
            if (p == null || !Available) return;
            var body = new Dictionary<string, object>
            {
                { "name", Clip(p.displayName, 32) },
                { "xp", p.xp },
                { "level", p.level },
                { "wins", p.gamesWon },
                { "updated", Fb.ServerTime },
            };
            fb.Put("leaderboard/" + fb.Uid, body, r =>
            {
                if (!r.ok) Debug.LogWarning("CPW: leaderboard update failed: " + r.error);
            });
        }

        public void GetLeaderboard(int count, Action<List<LeaderboardEntry>> done)
        {
            var list = new List<LeaderboardEntry>();
            Ready(() => fb.Get("leaderboard", r =>
            {
                if (r.ok && r.Obj != null)
                {
                    foreach (var kv in r.Obj)
                    {
                        var d = Fb.Dict(kv.Value);
                        if (d == null) continue;
                        list.Add(new LeaderboardEntry
                        {
                            playerId = kv.Key,
                            name = Fb.Str(d, "name", "Penguin"),
                            score = Fb.Int(d, "xp"),
                            level = Fb.Int(d, "level", 1),
                            wins = Fb.Int(d, "wins"),
                        });
                    }
                    list.Sort((a, b) => b.score != a.score ? b.score.CompareTo(a.score) : b.wins.CompareTo(a.wins));
                    for (int i = 0; i < list.Count; i++) list[i].rank = i + 1;
                }
                done?.Invoke(list);
            }, "orderBy=" + Fb.Q("xp") + "&limitToLast=" + Mathf.Clamp(count, 1, 200)), _ => done?.Invoke(list));
        }

        // ------------------------------------------------------------------ lobby

        public void ListOpenMatches(Action<List<OnlineMatchInfo>> done)
        {
            var list = new List<OnlineMatchInfo>();
            Ready(() => fb.Get("lobby", r =>
            {
                if (r.ok && r.Obj != null)
                {
                    long now = fb.ServerNowMs;
                    foreach (var kv in r.Obj)
                    {
                        var d = Fb.Dict(kv.Value);
                        if (d == null) continue;
                        long hb = Fb.Long(d, "hb");
                        int players = Fb.Int(d, "players", 1), max = Fb.Int(d, "maxPlayers", MaxPlayers);
                        if (now - hb > StaleMs)
                        {
                            // The host vanished. After 2 minutes the rules let anyone clear the entry.
                            if (now - hb > 130000) fb.Delete("lobby/" + kv.Key);
                            continue;
                        }
                        if (players >= max) continue;
                        list.Add(new OnlineMatchInfo
                        {
                            matchId = kv.Key,
                            hostName = Fb.Str(d, "hostName", "Penguin"),
                            levelId = Fb.Str(d, "levelId"),
                            players = players,
                            maxPlayers = max,
                            state = "waiting",
                            isPrivate = false,
                            code = Fb.Str(d, "code"),
                        });
                    }
                    // newest first
                    list.Reverse();
                }
                done?.Invoke(list);
            }, "orderBy=" + Fb.Q("hb") + "&limitToLast=30"), _ => done?.Invoke(list));
        }

        public void HostMatch(BattleConfig settings, bool isPrivate, Action<OnlineMatchInfo> created, Action<BattleConfig> started)
            => Host(settings, isPrivate, false, created, started, null);

        void Host(BattleConfig settings, bool isPrivate, bool quick, Action<OnlineMatchInfo> created, Action<BattleConfig> started, Action<string> statusCb)
        {
            CancelMatchmaking();
            var r = room = new RoomState { started = started, statusCb = statusCb };
            r.info.isHost = true;
            r.info.isPrivate = isPrivate;
            r.info.isQuickMatch = quick;
            r.info.levelId = settings != null ? settings.levelId ?? "" : "";
            r.settings = SettingsToDict(settings ?? DefaultSettings());
            SetStatus(r, "Creating game...");
            Ready(() =>
            {
                if (room != r) return;
                CleanupPreviousMatch();
                r.matchId = r.info.matchId = FirebaseClient.NewId();
                ReserveCode(r, 0, () =>
                {
                    var match = new Dictionary<string, object>
                    {
                        { "host", fb.Uid },
                        { "hostName", Clip(ProfileService.P.displayName, 32) },
                        { "isPrivate", isPrivate },
                        { "quick", quick },
                        { "code", r.info.code },
                        { "state", "waiting" },
                        { "created", Fb.ServerTime },
                        { "settings", r.settings },
                        { "players", new Dictionary<string, object> { { fb.Uid, LocalPlayerRecord() } } },
                    };
                    fb.Put("matches/" + r.matchId, match, res =>
                    {
                        if (room != r) return;
                        if (!res.ok) { Fail(r, "Could not create the game: " + res.error); return; }
                        PlayerPrefs.SetString("cpw_fb_last_hosted", r.matchId);
                        r.info.state = "waiting";
                        r.ready = true;
                        r.info.players.Add(new OnlineRoomPlayer { userId = fb.Uid, name = ProfileService.P.displayName, level = ProfileService.P.level, isHost = true, isLocal = true });
                        if (!isPrivate) WriteLobby(r);
                        SetStatus(r, isPrivate ? "Share the code " + r.info.code + " with your friends." : "Waiting for players...");
                        created?.Invoke(InfoOf(r));
                    });
                });
            }, err => Fail(r, err));
        }

        /// <summary>Pick a free short code; a taken code is refused by the rules, so just try another.</summary>
        void ReserveCode(RoomState r, int attempt, Action then)
        {
            var rng = new System.Random(Guid.NewGuid().GetHashCode());
            var chars = new char[5];
            for (int i = 0; i < chars.Length; i++) chars[i] = CodeChars[rng.Next(CodeChars.Length)];
            var code = new string(chars);
            fb.Get("codes/" + code, g =>
            {
                if (room != r) return;
                if (g.ok && g.Obj != null && attempt < 6) { ReserveCode(r, attempt + 1, then); return; }
                var body = new Dictionary<string, object> { { "match", r.matchId }, { "host", fb.Uid }, { "hb", Fb.ServerTime } };
                fb.Put("codes/" + code, body, res =>
                {
                    if (room != r) return;
                    if (!res.ok && attempt < 6) { ReserveCode(r, attempt + 1, then); return; }
                    if (!res.ok) { Fail(r, "Could not create the game: " + res.error); return; }
                    r.info.code = code;
                    then();
                });
            });
        }

        void WriteLobby(RoomState r)
        {
            var body = new Dictionary<string, object>
            {
                { "host", fb.Uid },
                { "hostName", Clip(ProfileService.P.displayName, 32) },
                { "levelId", r.info.levelId ?? "" },
                { "players", Mathf.Max(1, r.info.players.Count) },
                { "maxPlayers", MaxPlayers },
                { "quick", r.info.isQuickMatch },
                { "code", r.info.code },
                { "hb", Fb.ServerTime },
            };
            fb.Put("lobby/" + r.matchId, body, res => { if (res.ok) fb.LearnServerTime(Fb.Num(res.Obj, "hb")); });
        }

        /// <summary>Delete the match this device hosted last time if it is still around (crash, app killed).</summary>
        void CleanupPreviousMatch()
        {
            var last = PlayerPrefs.GetString("cpw_fb_last_hosted", "");
            if (string.IsNullOrEmpty(last)) return;
            PlayerPrefs.DeleteKey("cpw_fb_last_hosted");
            fb.Get("matches/" + last + "/code", res =>
            {
                var code = res.ok ? res.Json as string : null;
                if (!string.IsNullOrEmpty(code)) fb.Delete("codes/" + code);
                // The lobby rule checks we host the match, so remove the lobby entry before the match itself.
                fb.Delete("lobby/" + last, _ => fb.Delete("matches/" + last));
            });
        }

        public void JoinMatch(string matchIdOrCode, Action<BattleConfig> started, Action<string> failed)
            => Join(matchIdOrCode, started, failed, null);

        void Join(string idOrCode, Action<BattleConfig> started, Action<string> failed, Action<string> statusCb)
        {
            CancelMatchmaking();
            var key = (idOrCode ?? "").Trim();
            if (key.Length == 0) { failed?.Invoke("Enter a game code."); return; }
            var r = room = new RoomState { started = started, failed = failed, statusCb = statusCb };
            SetStatus(r, "Joining...");
            Ready(() =>
            {
                if (room != r) return;
                if (key.Length <= 6)
                {
                    var code = key.ToUpperInvariant();
                    fb.Get("codes/" + code, res =>
                    {
                        if (room != r) return;
                        var id = Fb.Str(res.Obj, "match");
                        if (!res.ok) { Fail(r, "Could not look up the code: " + res.error); return; }
                        if (string.IsNullOrEmpty(id)) { Fail(r, "No game with code " + code + "."); return; }
                        JoinById(r, id);
                    });
                }
                else JoinById(r, key);
            }, err => Fail(r, err));
        }

        void JoinById(RoomState r, string id)
        {
            r.matchId = r.info.matchId = id;
            fb.Get("matches/" + id, res =>
            {
                if (room != r) return;
                var m = res.Obj;
                if (!res.ok) { Fail(r, "Could not join: " + res.error); return; }
                if (m == null) { Fail(r, "That game does not exist any more."); return; }
                if (Fb.Str(m, "state") != "waiting") { Fail(r, "That game has already started or was closed."); return; }
                var players = Fb.Dict(m, "players");
                int count = 0;
                if (players != null) foreach (var kv in players) if (!Fb.Bool(Fb.Dict(kv.Value), "left")) count++;
                if (count >= MaxPlayers && (players == null || !players.ContainsKey(fb.Uid))) { Fail(r, "That game is full."); return; }
                r.info.code = Fb.Str(m, "code");
                r.info.isPrivate = Fb.Bool(m, "isPrivate");
                r.info.isQuickMatch = Fb.Bool(m, "quick");
                r.settings = Fb.Dict(m, "settings") ?? SettingsToDict(DefaultSettings());
                r.info.levelId = Fb.Str(r.settings, "levelId");
                fb.Put("matches/" + id + "/players/" + fb.Uid, LocalPlayerRecord(), put =>
                {
                    if (room != r) return;
                    if (!put.ok) { Fail(r, "Could not join: " + put.error); return; }
                    r.info.state = "waiting";
                    r.ready = true;
                    if (players == null) m["players"] = players = new Dictionary<string, object>();
                    players[fb.Uid] = LocalPlayerRecord();
                    SetStatus(r, "Waiting for " + Fb.Str(m, "hostName", "the host") + " to start...");
                    ApplyMatch(r, m);
                    r.pollTimer = 0;
                });
            });
        }

        public void QuickMatch(Action<BattleConfig> started, Action<string> statusCb)
        {
            CancelMatchmaking();
            statusCb?.Invoke("Looking for a game...");
            Ready(() => ListOpenMatches(list =>
            {
                // Join the fullest open public game.
                OnlineMatchInfo pick = null;
                foreach (var m in list) if (pick == null || m.players > pick.players) pick = m;
                if (pick != null)
                {
                    Join(pick.matchId, started, err =>
                    {
                        // Somebody else took the last seat: host our own instead.
                        HostQuick(started, statusCb);
                    }, statusCb);
                }
                else HostQuick(started, statusCb);
            }), err => statusCb?.Invoke(err));
        }

        void HostQuick(Action<BattleConfig> started, Action<string> statusCb)
        {
            Host(DefaultSettings(), false, true, null, started, statusCb);
            if (room != null) room.lastJoinTime = Time.realtimeSinceStartup;
        }

        public void CancelMatchmaking()
        {
            var r = room;
            room = null;
            if (r == null) return;
            r.info.state = "closed";
            r.info.version++;
            if (fb == null || string.IsNullOrEmpty(r.matchId) || r.startedBattle) return;
            if (r.info.isHost)
            {
                fb.Patch("matches/" + r.matchId, new Dictionary<string, object> { { "state", "cancelled" } }, _ =>
                    fb.After(10f, () => fb.Delete("matches/" + r.matchId)));
                fb.Delete("lobby/" + r.matchId);
                if (!string.IsNullOrEmpty(r.info.code)) fb.Delete("codes/" + r.info.code);
                PlayerPrefs.DeleteKey("cpw_fb_last_hosted");
            }
            else fb.Delete("matches/" + r.matchId + "/players/" + fb.Uid);
        }

        public void StartHostedMatch()
        {
            var r = room;
            if (r == null || !r.info.isHost || r.info.state != "waiting") return;
            if (r.info.players.Count < 2) { SetStatus(r, "You need at least 2 players."); return; }
            r.info.state = "starting";
            SetStatus(r, "Starting...");

            // Host is slot 0, the others in join order (max 4).
            var order = new List<object>();
            foreach (var p in r.info.players) if (order.Count < MaxPlayers) order.Add(p.userId);
            int seed = new System.Random(Guid.NewGuid().GetHashCode()).Next(1, int.MaxValue);
            string level = Fb.Str(r.settings, "levelId");
            if (string.IsNullOrEmpty(level)) level = PickLevel(seed);
            var patch = new Dictionary<string, object>
            {
                { "state", "playing" },
                { "order", order },
                { "settings/seed", seed },
                { "settings/levelId", level },
                { "turn", new Dictionary<string, object> { { "index", 0 }, { "player", 0 }, { "by", 0 }, { "at", Fb.ServerTime } } },
                { "startedAt", Fb.ServerTime },
            };
            fb.Patch("matches/" + r.matchId, patch, res =>
            {
                if (room != r) return;
                if (!res.ok) { r.info.state = "waiting"; SetStatus(r, "Could not start: " + res.error); return; }
                fb.Delete("lobby/" + r.matchId);
                if (!string.IsNullOrEmpty(r.info.code)) fb.Delete("codes/" + r.info.code);
                // Read it back so host and guests build the config from exactly the same data.
                fb.Get("matches/" + r.matchId, g =>
                {
                    if (room != r) return;
                    if (g.ok && g.Obj != null) ApplyMatch(r, g.Obj);
                    else { r.info.state = "waiting"; SetStatus(r, "Could not start: " + g.error); }
                });
            });
        }

        static string PickLevel(int seed)
        {
            var ids = new List<string>(GameData.Section("Level").Keys);
            if (ids.Count == 0) return "";
            ids.Sort(string.CompareOrdinal);
            return ids[new System.Random(seed).Next(ids.Count)];
        }

        // ------------------------------------------------------------------ room polling

        void Update(float dt)
        {
            if (pushTimer > 0 && (pushTimer -= dt) <= 0) FlushProfile();

            var r = room;
            if (r == null || !r.ready || r.startedBattle || r.info.state == "closed") return;

            if ((r.hbTimer += dt) >= RoomHeartbeat)
            {
                r.hbTimer = 0;
                fb.Put("matches/" + r.matchId + "/players/" + fb.Uid + "/hb", Fb.ServerTime, res => { if (res.ok) fb.LearnServerTime(res.Json); });
                if (r.info.isHost && r.info.state == "waiting")
                {
                    if (!r.info.isPrivate) WriteLobby(r);
                    if (!string.IsNullOrEmpty(r.info.code)) fb.Put("codes/" + r.info.code + "/hb", Fb.ServerTime);
                }
            }

            if (!r.polling && (r.pollTimer += dt) >= RoomPoll)
            {
                r.pollTimer = 0;
                r.polling = true;
                fb.Get("matches/" + r.matchId, res =>
                {
                    r.polling = false;
                    if (room != r) return;
                    if (!res.ok) { SetStatus(r, "Connection problem: " + res.error); return; }
                    if (res.Obj == null) { Fail(r, "The game was closed by the host."); return; }
                    ApplyMatch(r, res.Obj);
                });
            }

            // Two people pressing Quick Match at the same moment both end up hosting: while alone, look for an older
            // waiting room and move there (the older one stays, so both sides agree on who moves).
            if (r.info.isHost && r.info.isQuickMatch && r.info.state == "waiting" && r.info.players.Count == 1 && !r.merging &&
                (r.mergeTimer += dt) >= 5f)
            {
                r.mergeTimer = 0;
                r.merging = true;
                ListOpenMatches(list =>
                {
                    r.merging = false;
                    if (room != r || r.info.players.Count != 1 || r.info.state != "waiting") return;
                    OnlineMatchInfo older = null;
                    foreach (var m in list)
                        if (m.matchId != r.matchId && string.CompareOrdinal(m.matchId, r.matchId) < 0 && m.players < m.maxPlayers) { older = m; break; }
                    if (older == null) return;
                    var started = r.started; var statusCb = r.statusCb;
                    Join(older.matchId, started, _ => HostQuick(started, statusCb), statusCb);
                });
            }

            // Quick match rooms start on their own once enough people are in.
            if (r.info.isHost && r.info.isQuickMatch && r.info.state == "waiting" && r.info.players.Count >= 2 &&
                (r.info.players.Count >= MaxPlayers || Time.realtimeSinceStartup - r.lastJoinTime >= QuickStartDelay))
                StartHostedMatch();
        }

        /// <summary>Update the room from the match document; start the battle when it is playing.</summary>
        void ApplyMatch(RoomState r, Dictionary<string, object> m)
        {
            var state = Fb.Str(m, "state");
            if (state == "cancelled" || state == "finished") { Fail(r, "The host closed the game."); return; }
            var playersDict = Fb.Dict(m, "players") ?? new Dictionary<string, object>();
            if (!r.info.isHost && !playersDict.ContainsKey(fb.Uid)) { Fail(r, "You were removed from the game."); return; }

            string hostUid = Fb.Str(m, "host");
            long now = fb.ServerNowMs;
            var list = new List<(long joined, OnlineRoomPlayer p)>();
            foreach (var kv in playersDict)
            {
                var d = Fb.Dict(kv.Value);
                if (d == null || Fb.Bool(d, "left")) continue;
                bool isLocal = kv.Key == fb.Uid;
                long hb = Fb.Long(d, "hb", Fb.Long(d, "joined"));
                if (!isLocal && hb > 0 && now - hb > StaleMs && state == "waiting")
                {
                    // Gone without saying goodbye: the host removes them.
                    if (r.info.isHost && kv.Key != hostUid) fb.Delete("matches/" + r.matchId + "/players/" + kv.Key);
                    continue;
                }
                list.Add((Fb.Long(d, "joined"), new OnlineRoomPlayer
                {
                    userId = kv.Key,
                    name = Fb.Str(d, "name", "Penguin"),
                    level = Fb.Int(d, "level", 1),
                    isHost = kv.Key == hostUid,
                    isLocal = isLocal,
                }));
            }
            list.Sort((a, b) => a.p.isHost != b.p.isHost ? (a.p.isHost ? -1 : 1) : a.joined.CompareTo(b.joined));

            bool changed = list.Count != r.info.players.Count;
            if (!changed) for (int i = 0; i < list.Count; i++) if (list[i].p.userId != r.info.players[i].userId) { changed = true; break; }
            if (changed)
            {
                r.info.players.Clear();
                foreach (var e in list) r.info.players.Add(e.p);
                r.lastJoinTime = Time.realtimeSinceStartup;
                if (r.info.isHost && r.info.state == "waiting" && !r.info.isPrivate) WriteLobby(r);
                r.info.version++;
            }
            if (!r.info.isHost && !string.IsNullOrEmpty(hostUid) && !playersDict.ContainsKey(hostUid)) { Fail(r, "The host left."); return; }

            if (state == "playing" && !r.startedBattle)
            {
                var order = new List<string>();
                foreach (var o in Fb.Items(m.TryGetValue("order", out var ov) ? ov : null)) if (o is string s) order.Add(s);
                if (!order.Contains(fb.Uid)) { Fail(r, "The game started without you (it was full)."); return; }
                var config = BuildConfig(r, m, order, playersDict, hostUid);
                r.startedBattle = true;
                r.info.state = "playing";
                SetStatus(r, "Starting the battle!");
                room = null; // the battle owns the match now
                var cb = r.started;
                try { cb?.Invoke(config); } catch (Exception e) { Debug.LogException(e); }
            }
        }

        BattleConfig BuildConfig(RoomState r, Dictionary<string, object> m, List<string> order, Dictionary<string, object> players, string hostUid)
        {
            var s = Fb.Dict(m, "settings") ?? r.settings ?? new Dictionary<string, object>();
            var def = DefaultSettings();
            var c = new BattleConfig
            {
                mode = BattleMode.Online,
                levelId = Fb.Str(s, "levelId", def.levelId),
                matchTime = (float)Fb.Num(s, "matchTime", def.matchTime),
                turnTime = (float)Fb.Num(s, "turnTime", def.turnTime),
                winningScore = Fb.Int(s, "winningScore", def.winningScore),
                powerUps = Fb.Bool(s, "powerUps", def.powerUps),
                betId = Fb.Str(s, "betId", def.betId),
                seed = Fb.Int(s, "seed", 1),
            };
            var prof = ProfileService.P;
            for (int i = 0; i < order.Count; i++)
            {
                var uid = order[i];
                var d = Fb.Dict(players, uid);
                bool local = uid == fb.Uid;
                var slot = new PlayerSlot
                {
                    name = local ? prof.displayName : Fb.Str(d, "name", "Penguin " + (i + 1)),
                    isAI = false,
                    isLocalHuman = local,
                    onlineUserId = uid,
                    colorIndex = i,
                    team = -1,
                    level = local ? prof.level : Fb.Int(d, "level", 1),
                };
                if (local)
                {
                    slot.usesProfileInventory = true;
                    slot.head = prof.wornHead; slot.chest = prof.wornChest; slot.feet = prof.wornFeet; slot.trophy = prof.wornTrophy;
                }
                else
                {
                    slot.head = Fb.Str(d, "head"); slot.chest = Fb.Str(d, "chest"); slot.feet = Fb.Str(d, "feet"); slot.trophy = Fb.Str(d, "trophy");
                    // Their ammo, so their weapon menu and replays look right (the snapshot carries the real counts later).
                    foreach (var o in Fb.Items(d != null && d.TryGetValue("items", out var iv) ? iv : null))
                    {
                        var it = Fb.Dict(o);
                        var id = Fb.Str(it, "id");
                        if (!string.IsNullOrEmpty(id)) slot.loadout.Add(new ItemStack(id, Fb.Int(it, "n")));
                    }
                }
                c.players.Add(slot);
            }
            c.network = new FirebaseBattleNetwork(fb, r.matchId, fb.Uid, order, hostUid == fb.Uid);
            return c;
        }

        // ------------------------------------------------------------------ helpers

        Dictionary<string, object> LocalPlayerRecord()
        {
            var p = ProfileService.P;
            var items = new List<object>();
            foreach (var s in p.items)
                if (s != null && s.amount > 0 && !string.IsNullOrEmpty(s.id))
                    items.Add(new Dictionary<string, object> { { "id", s.id }, { "n", s.amount } });
            return new Dictionary<string, object>
            {
                { "name", Clip(p.displayName, 32) },
                { "level", p.level },
                { "head", p.wornHead ?? "" },
                { "chest", p.wornChest ?? "" },
                { "feet", p.wornFeet ?? "" },
                { "trophy", p.wornTrophy ?? "" },
                { "items", items },
                { "joined", Fb.ServerTime },
                { "hb", Fb.ServerTime },
            };
        }

        /// <summary>Settings used by Quick Match (the original picked 5-8 minute matches and 10-30 s turns).</summary>
        public static BattleConfig DefaultSettings()
        {
            var b = GameData.Battle;
            return new BattleConfig
            {
                mode = BattleMode.Online,
                levelId = "",
                matchTime = 300,
                turnTime = b != null ? Mathf.Clamp(b.Int("TurnTime", 20), 10, 60) : 20,
                winningScore = 0,
                powerUps = true,
                betId = "1NoBet",
            };
        }

        static Dictionary<string, object> SettingsToDict(BattleConfig c) => new Dictionary<string, object>
        {
            { "levelId", c.levelId ?? "" },
            { "matchTime", c.matchTime },
            { "turnTime", c.turnTime },
            { "winningScore", c.winningScore },
            { "powerUps", c.powerUps },
            { "betId", string.IsNullOrEmpty(c.betId) ? "1NoBet" : c.betId },
            { "maxPlayers", MaxPlayers },
        };

        static OnlineMatchInfo InfoOf(RoomState r) => new OnlineMatchInfo
        {
            matchId = r.matchId,
            hostName = ProfileService.P.displayName,
            levelId = r.info.levelId,
            players = Mathf.Max(1, r.info.players.Count),
            maxPlayers = MaxPlayers,
            state = r.info.state,
            isPrivate = r.info.isPrivate,
            code = r.info.code,
        };

        void SetStatus(RoomState r, string text)
        {
            r.info.status = text;
            r.info.version++;
            try { r.statusCb?.Invoke(text); } catch (Exception e) { Debug.LogException(e); }
        }

        void Fail(RoomState r, string reason)
        {
            if (room == r) room = null;
            bool wasHost = r.info.isHost;
            r.info.state = "closed";
            SetStatus(r, reason);
            if (wasHost && !string.IsNullOrEmpty(r.matchId) && fb != null && !r.startedBattle)
            {
                fb.Delete("lobby/" + r.matchId);
                if (!string.IsNullOrEmpty(r.info.code)) fb.Delete("codes/" + r.info.code);
            }
            try
            {
                if (r.failed != null) r.failed(reason);
                else r.started?.Invoke(null);
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        static string Clip(string s, int n) => string.IsNullOrEmpty(s) ? "Penguin" : (s.Length > n ? s.Substring(0, n) : s);

        sealed class RoomState
        {
            public readonly OnlineRoom info = new OnlineRoom();
            public string matchId = "";
            public Dictionary<string, object> settings;
            public Action<BattleConfig> started;
            public Action<string> failed, statusCb;
            public float pollTimer, hbTimer, lastJoinTime;
            public bool polling, startedBattle, merging;
            public float mergeTimer;
            public bool ready;   // the match exists and we are in it (polling starts)
        }
    }
}
