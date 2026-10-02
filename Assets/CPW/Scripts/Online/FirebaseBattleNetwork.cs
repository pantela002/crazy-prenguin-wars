using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// IBattleNetwork over the Firebase Realtime Database (REST polling, no sockets).
    ///
    /// Data (under matches/{matchId}):
    ///   turn                 {index, player, by, at}  index = number of finished turns; player = whose turn it is
    ///   actions/{turn}/aNNNNN [TurnAction...]          batches of the active player's actions, keys sort by send order
    ///   snapshots/{turn}     {from, json}              BattleSnapshot (JsonUtility) sent at the end of turn {turn}
    ///   players/{uid}        {..., hb, left}           heartbeat (server time) every 3 s; left=true when quitting
    ///
    /// The active player buffers actions and writes a batch every 0.2 s (immediately for "fire"); everybody polls
    /// "turn" and the current turn's actions every 0.5 s and the players every 2 s. Writes go out one at a time,
    /// in order, so receivers can read actions with a simple key cursor.
    /// Events are raised only from Tick() (called by the battle every frame), so nothing arrives before the battle is ready.
    /// When the active player leaves, the battle should let <see cref="IsAuthority"/> (the lowest connected slot) end that turn.
    /// </summary>
    public sealed class FirebaseBattleNetwork : IBattleNetwork
    {
        public int LocalSlot { get; }
        public string MatchId { get; }
        public bool IsHost { get; }
        /// <summary>Firebase uid per slot (same order as BattleConfig.players).</summary>
        public IReadOnlyList<string> PlayerIds => uids;
        /// <summary>Number of finished turns as seen by this device.</summary>
        public int TurnIndex => turnIndex;
        /// <summary>Whose turn the server says it is (-1 until known).</summary>
        public int ServerTurnPlayer { get; private set; } = -1;

        /// <summary>False when requests have failed for a while (no internet) or after leaving.</summary>
        public bool Connected => !closed && Time.realtimeSinceStartup - lastSuccess < DisconnectAfter;

        /// <summary>Lowest slot that is still in the match. That device resolves turns of players who left.</summary>
        public int AuthoritySlot
        {
            get
            {
                for (int i = 0; i < uids.Length; i++) if (!left.Contains(i)) return i;
                return LocalSlot;
            }
        }
        public bool IsAuthority => AuthoritySlot == LocalSlot;
        public bool IsPlayerConnected(int slot) => slot >= 0 && slot < uids.Length && !left.Contains(slot);

        public event Action<TurnAction> ActionReceived;
        public event Action<BattleSnapshot> TurnEndReceived;
        public event Action<int> PlayerLeft;
        public event Action<BattleChatMessage> ChatReceived;
        public bool SupportsChat => true;

        const float PollInterval = 0.5f, PlayersInterval = 2f, HeartbeatInterval = 3f, BatchInterval = 0.2f;
        const float DisconnectAfter = 15f;
        const long PlayerTimeoutMs = 25000;
        const float CleanupDelay = 60f;   // longer than the rematch countdown + grace (FirebaseService.Rematch.cs)

        readonly FirebaseClient fb;
        readonly string localUid;
        readonly string[] uids;
        readonly string root;
        readonly HashSet<int> left = new HashSet<int>();

        // incoming
        readonly Queue<Action> inbox = new Queue<Action>();
        int turnIndex;          // finished turns we have applied
        int recvSeq;            // next action batch index expected for turnIndex
        bool polling, pollingPlayers;
        float pollTimer, playersTimer, hbTimer;

        // outgoing
        readonly List<TurnAction> batch = new List<TurnAction>();
        float batchTimer;
        int sendSeq, sendSeqTurn = -1;
        bool sentThisTurn;
        readonly Queue<Action<Action>> writes = new Queue<Action<Action>>();
        bool writing;

        bool closed, leaveSent, matchOver;

        // chat (matches/{id}/chat/{push id}: {p, u, x, k, at}); polled on its own slower timer
        const float ChatInterval = 1.5f;
        const int ChatMaxLength = 80;
        float chatTimer;
        bool pollingChat;
        string chatCursor;          // newest chat key handled (push ids sort by server time)
        readonly long chatSince;    // server ms when we joined: older lines are not shown
        float lastSuccess;

        public FirebaseBattleNetwork(FirebaseClient client, string matchId, string localUid, IList<string> order, bool isHost)
        {
            fb = client;
            MatchId = matchId;
            this.localUid = localUid;
            IsHost = isHost;
            uids = new string[order.Count];
            for (int i = 0; i < order.Count; i++) uids[i] = order[i];
            LocalSlot = Array.IndexOf(uids, localUid);
            if (LocalSlot < 0) LocalSlot = 0;
            root = "matches/" + matchId;
            lastSuccess = Time.realtimeSinceStartup;
            fb.Updated += Pump;
            BattleEvents.BattleEnded += OnBattleEnded;
            chatSince = fb.ServerNowMs - 10000;
        }

        // ------------------------------------------------------------------ IBattleNetwork

        public void SendAction(TurnAction action)
        {
            if (closed || action == null) return;
            action.player = LocalSlot;
            batch.Add(action);
            sentThisTurn = true;
            if (action.type == "fire") FlushBatch();
        }

        public void SendTurnEnd(BattleSnapshot snapshot)
        {
            if (closed || snapshot == null) return;
            FlushBatch();
            int t = turnIndex;
            string json = JsonUtility.ToJson(snapshot);
            var patch = new Dictionary<string, object>
            {
                { "snapshots/" + t, new Dictionary<string, object> { { "from", LocalSlot }, { "json", json } } },
                { "turn", new Dictionary<string, object>
                    {
                        { "index", t + 1 }, { "player", snapshot.nextPlayer }, { "by", LocalSlot }, { "at", Fb.ServerTime }
                    }
                },
            };
            // Keep the database small: drop data two turns back (everybody has applied it by now).
            if (t >= 2)
            {
                patch["snapshots/" + (t - 2)] = null;
                patch["actions/" + (t - 2)] = null;
            }
            if (snapshot.matchOver) { patch["state"] = "finished"; matchOver = true; }
            Enqueue(done => fb.Patch(root, patch, r => { Note(r); done(); }));
            // We move on immediately: further actions belong to the next turn.
            AdvanceTo(t + 1);
            ServerTurnPlayer = snapshot.nextPlayer;
        }

        public void SendLeave()
        {
            if (leaveSent) return;
            leaveSent = true;
            FlushBatch();
            var p = new Dictionary<string, object> { { "left", true }, { "hb", Fb.ServerTime } };
            Enqueue(done => fb.Patch(root + "/players/" + localUid, p, r => { Note(r); done(); }));
            Close();
        }

        /// <summary>Dispatch received data to the battle. Polling itself runs from FirebaseClient.Update.</summary>
        public void Tick(float dt)
        {
            int guard = 64; // spread very large bursts over frames
            while (inbox.Count > 0 && guard-- > 0)
            {
                var a = inbox.Dequeue();
                try { a(); } catch (Exception e) { Debug.LogException(e); }
            }
        }

        // ------------------------------------------------------------------ internals

        void Pump(float dt)
        {
            if (closed) return;

            if (batch.Count > 0 && (batchTimer += dt) >= BatchInterval) FlushBatch();

            if ((hbTimer += dt) >= HeartbeatInterval)
            {
                hbTimer = 0;
                fb.Put(root + "/players/" + localUid + "/hb", Fb.ServerTime, r =>
                {
                    Note(r);
                    if (r.ok) fb.LearnServerTime(r.Json);
                });
            }

            if (!polling && (pollTimer += dt) >= PollInterval)
            {
                pollTimer = 0;
                polling = true;
                PollTurn();
            }

            if (!pollingPlayers && (playersTimer += dt) >= PlayersInterval)
            {
                playersTimer = 0;
                pollingPlayers = true;
                PollPlayers();
            }

            if (!pollingChat && (chatTimer += dt) >= ChatInterval)
            {
                chatTimer = 0;
                pollingChat = true;
                PollChat();
            }
        }

        // ------------------------------------------------------------------ chat

        /// <summary>POST so the server makes the key (push ids sort by server time, whatever the senders' clocks).</summary>
        public void SendChat(BattleChatMessage m)
        {
            if (closed || m == null || (string.IsNullOrEmpty(m.text) && string.IsNullOrEmpty(m.tid))) return;
            var d = new Dictionary<string, object> { { "p", LocalSlot }, { "u", localUid }, { "at", Fb.ServerTime } };
            if (!string.IsNullOrEmpty(m.text)) d["x"] = m.text.Length > ChatMaxLength ? m.text.Substring(0, ChatMaxLength) : m.text;
            if (!string.IsNullOrEmpty(m.tid)) d["k"] = m.tid;
            fb.Db("POST", root + "/chat", d, Note);
        }

        void PollChat()
        {
            string q = "orderBy=" + Fb.Q("$key") + (chatCursor == null ? "&limitToLast=30" : "&startAt=" + Fb.Q(chatCursor));
            fb.Get(root + "/chat", r =>
            {
                Note(r);
                pollingChat = false;
                if (closed || !r.ok) return;
                var d = r.Obj;
                if (d == null) return;
                var keys = new List<string>(d.Keys);
                keys.Sort(string.CompareOrdinal);
                foreach (var k in keys)
                {
                    if (chatCursor != null && string.CompareOrdinal(k, chatCursor) <= 0) continue;   // startAt is inclusive
                    chatCursor = k;
                    var o = d[k] as Dictionary<string, object>;
                    if (o == null) continue;
                    long at = Fb.Long(o, "at");
                    if (at > 0 && at < chatSince) continue;
                    int p = Fb.Int(o, "p", -1);
                    if (p == LocalSlot || p < 0 || p >= uids.Length) continue;
                    var m = new BattleChatMessage { player = p, text = Fb.Str(o, "x"), tid = Fb.Str(o, "k") };
                    if (m.text.Length > ChatMaxLength) m.text = m.text.Substring(0, ChatMaxLength);
                    inbox.Enqueue(() => ChatReceived?.Invoke(m));
                }
            }, q);
        }

        void Note(FbResponse r)
        {
            if (r != null && r.ok) lastSuccess = Time.realtimeSinceStartup;
        }

        void AdvanceTo(int index)
        {
            turnIndex = index;
            recvSeq = 0;
            sentThisTurn = false;
        }

        void FlushBatch()
        {
            batchTimer = 0;
            if (batch.Count == 0) return;
            if (sendSeqTurn != turnIndex) { sendSeqTurn = turnIndex; sendSeq = 0; }
            var list = new List<object>(batch.Count);
            foreach (var a in batch) list.Add(ActionToDict(a));
            batch.Clear();
            string path = root + "/actions/" + turnIndex + "/" + SeqKey(sendSeq++);
            Enqueue(done => fb.Put(path, list, r => { Note(r); done(); }));
        }

        /// <summary>Writes run strictly one after another so their order on the server matches the send order.</summary>
        void Enqueue(Action<Action> write)
        {
            writes.Enqueue(write);
            if (!writing) NextWrite();
        }

        void NextWrite()
        {
            if (writes.Count == 0) { writing = false; return; }
            writing = true;
            var w = writes.Dequeue();
            w(NextWrite);
        }

        static string SeqKey(int i) => "a" + i.ToString("D5", CultureInfo.InvariantCulture);

        void PollTurn()
        {
            fb.Get(root + "/turn", r =>
            {
                Note(r);
                if (closed) { polling = false; return; }
                var d = r.ok ? r.Obj : null;
                int serverIndex = d != null ? Fb.Int(d, "index", turnIndex) : turnIndex;
                if (d != null && serverIndex >= turnIndex) ServerTurnPlayer = Fb.Int(d, "player", ServerTurnPlayer);

                if (serverIndex > turnIndex)
                {
                    // The turn ended: read the last actions (for the replay), then the snapshot.
                    int finished = turnIndex;
                    PollActions(finished, () => FetchSnapshot(serverIndex - 1, serverIndex));
                }
                else if (!sentThisTurn) PollActions(turnIndex, () => polling = false);
                else polling = false;
            });
        }

        void PollActions(int turn, Action then)
        {
            string q = "orderBy=" + Fb.Q("$key") + "&startAt=" + Fb.Q(SeqKey(recvSeq));
            fb.Get(root + "/actions/" + turn, r =>
            {
                Note(r);
                var d = r.ok ? r.Obj : null;
                if (d != null && turn == turnIndex && !closed)
                {
                    var keys = new List<string>(d.Keys);
                    keys.Sort(string.CompareOrdinal);
                    foreach (var k in keys)
                    {
                        if (k.Length < 2 || !int.TryParse(k.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seq) || seq < recvSeq) continue;
                        recvSeq = seq + 1;
                        foreach (var o in Fb.Items(d[k]))
                        {
                            var a = DictToAction(o as Dictionary<string, object>);
                            if (a == null || a.player == LocalSlot) continue;
                            inbox.Enqueue(() => ActionReceived?.Invoke(a));
                        }
                    }
                }
                then();
            }, q);
        }

        void FetchSnapshot(int snapTurn, int newIndex)
        {
            fb.Get(root + "/snapshots/" + snapTurn, r =>
            {
                Note(r);
                if (closed) { polling = false; return; }
                var d = r.ok ? r.Obj : null;
                if (d == null)
                {
                    // Not visible yet (or already cleaned up after a long stall): try again next poll,
                    // but don't stay stuck forever if it is gone.
                    if (r.ok) AdvanceTo(newIndex);
                    polling = false;
                    return;
                }
                BattleSnapshot snap = null;
                try { snap = JsonUtility.FromJson<BattleSnapshot>(Fb.Str(d, "json")); }
                catch (Exception e) { Debug.LogWarning("CPW: bad snapshot: " + e.Message); }
                AdvanceTo(newIndex);
                if (snap != null && snap.matchOver) matchOver = true;
                if (snap != null && Fb.Int(d, "from", -1) != LocalSlot) inbox.Enqueue(() => TurnEndReceived?.Invoke(snap));
                polling = false;
            });
        }

        void PollPlayers()
        {
            fb.Get(root + "/players", r =>
            {
                Note(r);
                pollingPlayers = false;
                if (closed || !r.ok) return;
                var d = r.Obj; // null = match deleted
                long myHb = 0;
                var me = Fb.Dict(d, localUid);
                if (me != null) myHb = Fb.Long(me, "hb");
                long now = myHb > 0 ? Math.Max(myHb, fb.ServerNowMs) : fb.ServerNowMs;
                for (int i = 0; i < uids.Length; i++)
                {
                    if (i == LocalSlot || left.Contains(i)) continue;
                    var p = Fb.Dict(d, uids[i]);
                    long hb = p != null ? Fb.Long(p, "hb") : 0;
                    bool gone = p == null || Fb.Bool(p, "left") || (hb > 0 && now - hb > PlayerTimeoutMs);
                    if (!gone) continue;
                    left.Add(i);
                    int slot = i;
                    inbox.Enqueue(() => PlayerLeft?.Invoke(slot));
                }
            });
        }

        void OnBattleEnded(BattleResult r)
        {
            if (!leaveSent) SendLeave();
            else Close();
        }

        void Close()
        {
            if (closed) return;
            closed = true;
            fb.Updated -= Pump;
            BattleEvents.BattleEnded -= OnBattleEnded;
            // The lowest slot still there removes a finished (or abandoned) match a little later, after everyone had
            // time to read the last snapshot and the rematch votes (matches/{id}/rematch). The rules let any player of
            // a "finished" match delete it, so it is marked finished first (a no-op when the last turn already did).
            // If we quit mid-match the others keep playing, so nothing is deleted then; matches nobody cleaned up
            // are swept after 3 hours (FirebaseService.SweepAbandoned).
            if (IsAuthority && (matchOver || left.Count >= uids.Length - 1))
            {
                var path = root;
                fb.After(CleanupDelay, () => fb.Patch(path, new Dictionary<string, object> { { "state", "finished" } }, r =>
                {
                    if (r.ok) fb.Delete(path);
                }));
            }
        }

        // ------------------------------------------------------------------ (de)serialization

        static Dictionary<string, object> ActionToDict(TurnAction a)
        {
            var d = new Dictionary<string, object>
            {
                { "p", a.player }, { "t", Round(a.t) }, { "k", a.type ?? "" },
            };
            if (a.x != 0) d["x"] = Round(a.x);
            if (a.y != 0) d["y"] = Round(a.y);
            if (a.px != 0) d["px"] = Round(a.px);
            if (a.py != 0) d["py"] = Round(a.py);
            if (!string.IsNullOrEmpty(a.s)) d["s"] = a.s;
            return d;
        }

        static TurnAction DictToAction(Dictionary<string, object> d)
        {
            if (d == null) return null;
            return new TurnAction
            {
                player = Fb.Int(d, "p"),
                t = (float)Fb.Num(d, "t"),
                type = Fb.Str(d, "k"),
                x = (float)Fb.Num(d, "x"),
                y = (float)Fb.Num(d, "y"),
                px = (float)Fb.Num(d, "px"),
                py = (float)Fb.Num(d, "py"),
                s = Fb.Str(d, "s", null),
            };
        }

        /// <summary>4 decimals; NaN / infinity become 0 (they are not valid JSON, the database would refuse the whole batch).</summary>
        static double Round(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0 : Math.Round(v, 4);
    }
}
