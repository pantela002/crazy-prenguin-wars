using System;
using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>One player of the finished match on the rematch panel.</summary>
    public class RematchSlot
    {
        public string uid, name;
        public bool isLocal;
        public string state = "waiting";   // "waiting", "ready", "left"
    }

    /// <summary>
    /// The rematch offer after an online match (original RematchRequestMessage / RematchResponseMessage and the result
    /// screen's TimeToStartRematch countdown). The results screen reads it every frame; version changes on updates.
    /// </summary>
    public sealed class OnlineRematch
    {
        public string oldMatchId = "";
        public readonly List<RematchSlot> slots = new List<RematchSlot>();
        public float timeLeft;               // local countdown (BattleOptions.TimeToStartRematch)
        public bool localReady, localLeft;
        public bool finished;                // no rematch will happen (any more) through this offer
        public bool starting;                // the new match is being created / joined
        public string status = "";
        public int version;
        /// <summary>Gets the battle config of the new match (the screen starts the battle), or null if it failed.</summary>
        public Action<BattleConfig> started;

        internal BattleConfig settings;
        internal string newMatchId = "";
        internal float pollTimer;
        internal bool polling;

        public int ReadyCount { get { int n = 0; foreach (var s in slots) if (s.state == "ready") n++; return n; } }
    }

    /// <summary>
    /// Online rematch: every player writes matches/{old}/rematch/{uid} = {s: "ready" | "left"}. When at least two are
    /// ready (everybody decided, or the countdown ran out) the ready player with the lowest slot (the old host when
    /// they want to play) hosts a new private match with the same settings and writes its id as rematch/{uid}/next;
    /// the others see it and join. The new match gets a new seed when it starts (StartHostedMatch).
    /// </summary>
    public sealed partial class FirebaseService
    {
        const float RematchPoll = 1f;
        const float RematchGrace = 6f;       // wait this long after our countdown for players whose results showed later
        const float RematchJoinWait = 12f;   // the new room waits this long for everyone before starting with who's there

        OnlineRematch rematch;

        /// <summary>
        /// Offer a rematch for a finished online battle. Returns null when that is impossible (offline, not a Firebase
        /// match). The offer starts polling at once so the panel can show who is ready.
        /// </summary>
        public OnlineRematch BeginRematch(BattleConfig old)
        {
            var net = old != null ? old.network as FirebaseBattleNetwork : null;
            if (!Available || net == null || old.players.Count < 2) return null;
            EndRematch();
            var s = rematch = new OnlineRematch
            {
                oldMatchId = net.MatchId,
                timeLeft = Mathf.Max(5, GameData.Battle?.Int("TimeToStartRematch", 10) ?? 10),
                settings = BattleFactory.Clone(old),
            };
            for (int i = 0; i < old.players.Count; i++)
            {
                var p = old.players[i];
                s.slots.Add(new RematchSlot { uid = p.onlineUserId, name = p.name, isLocal = p.onlineUserId == fb.Uid });
            }
            return s;
        }

        /// <summary>Say yes to the rematch.</summary>
        public void RematchReady(OnlineRematch s)
        {
            if (s == null || s != rematch || s.finished || s.localReady) return;
            s.localReady = true;
            SetSlot(s, fb.Uid, "ready");
            s.status = "Waiting for the others...";
            s.version++;
            fb.Put("matches/" + s.oldMatchId + "/rematch/" + fb.Uid, new Dictionary<string, object> { { "s", "ready" } }, r =>
            {
                if (r.ok || rematch != s || s.finished) return;
                s.finished = true;
                s.status = "The rematch is no longer possible.";
                s.version++;
            });
        }

        /// <summary>Say no (or leave the results screen). Leaves the new room too if we were already in it.</summary>
        public void RematchLeave(OnlineRematch s)
        {
            if (s == null) return;
            bool wasActive = !s.finished;
            s.finished = true;
            s.localLeft = true;
            if (rematch == s) rematch = null;
            if (room != null && (room.rematch == s || (!string.IsNullOrEmpty(s.newMatchId) && room.matchId == s.newMatchId))) CancelMatchmaking();
            if (wasActive && Available)
                fb.Put("matches/" + s.oldMatchId + "/rematch/" + fb.Uid, new Dictionary<string, object> { { "s", "left" } });
        }

        void EndRematch()
        {
            if (rematch != null) RematchLeave(rematch);
            rematch = null;
        }

        static void SetSlot(OnlineRematch s, string uid, string state)
        {
            foreach (var slot in s.slots) if (slot.uid == uid) slot.state = state;
        }

        void UpdateRematch(float dt)
        {
            var s = rematch;
            if (s == null) return;
            if (s.finished) { rematch = null; return; }
            s.timeLeft -= dt;
            // The countdown ran out without us saying yes: we are out.
            if (s.timeLeft <= 0 && !s.localReady && !s.starting)
            {
                s.status = "No rematch this time.";
                s.version++;
                RematchLeave(s);
                return;
            }
            if (s.starting || s.polling || (s.pollTimer -= dt) > 0) return;
            s.pollTimer = RematchPoll;
            s.polling = true;
            fb.Get("matches/" + s.oldMatchId + "/rematch", r =>
            {
                s.polling = false;
                if (rematch != s || s.finished || s.starting) return;
                if (r.ok) ApplyRematch(s, r.Obj);
                if (s.localReady && s.timeLeft <= -RematchGrace && !s.starting)
                {
                    s.finished = true;
                    s.status = "Not enough penguins want a rematch.";
                    s.version++;
                    RematchLeave(s);
                }
            });
        }

        void ApplyRematch(OnlineRematch s, Dictionary<string, object> d)
        {
            string next = null;
            bool changed = false;
            int lowestReady = int.MaxValue, mySlot = -1, decided = 0;
            for (int i = 0; i < s.slots.Count; i++)
            {
                var slot = s.slots[i];
                if (slot.isLocal) mySlot = i;
                var e = Fb.Dict(d, slot.uid);
                string st = slot.isLocal ? (s.localReady ? "ready" : "waiting") : Fb.Str(e, "s", "waiting");
                if (st != "ready" && st != "left") st = "waiting";
                if (st != slot.state) { slot.state = st; changed = true; }
                if (st == "ready" && i < lowestReady) lowestReady = i;
                if (st != "waiting") decided++;
                var n = Fb.Str(e, "next");
                if (!slot.isLocal && next == null && !string.IsNullOrEmpty(n)) next = n;
            }
            if (changed) s.version++;
            if (!s.localReady) return;

            // Somebody already made the new match: join it.
            if (next != null)
            {
                s.starting = true;
                s.newMatchId = next;
                s.status = "Joining the rematch...";
                s.version++;
                ProfileService.P.AddCounter("Games_Accept_Rematch", 1);
                Join(next, cfg => RematchStarted(s, cfg), err => RematchFailed(s, err), null);
                return;
            }

            // We host it if we are the first ready player and everyone decided or our countdown is over.
            int ready = s.ReadyCount;
            if (ready >= 2 && mySlot == lowestReady && (decided == s.slots.Count || s.timeLeft <= 0))
                HostRematch(s);
            else if (ready < 2 && decided == s.slots.Count)
            {
                s.finished = true;
                s.status = "Everybody else left.";
                s.version++;
                if (rematch == s) rematch = null;
            }
        }

        void HostRematch(OnlineRematch s)
        {
            s.starting = true;
            s.status = "Setting up the rematch...";
            s.version++;
            // Don't let Host() clean up the finished match as "left over": the others still read rematch/ from it.
            if (PlayerPrefs.GetString("cpw_fb_last_hosted", "") == s.oldMatchId) PlayerPrefs.DeleteKey("cpw_fb_last_hosted");
            var c = s.settings ?? DefaultSettings();
            c.mode = BattleMode.Online;
            Host(c, true, false, info =>
            {
                if (s.finished || room == null || room.matchId != info.matchId) return;
                s.newMatchId = info.matchId;
                room.rematchDeadline = Time.realtimeSinceStartup + RematchJoinWait;
                fb.Put("matches/" + s.oldMatchId + "/rematch/" + fb.Uid, new Dictionary<string, object> { { "s", "ready" }, { "next", info.matchId } }, r =>
                {
                    if (!r.ok) RematchFailed(s, "The others could not be told about the rematch.");
                });
            }, cfg => RematchStarted(s, cfg), null);
            // Host() set up the room synchronously: tag it now so leaving early also closes it.
            if (room != null) { room.rematch = s; room.rematchDeadline = Time.realtimeSinceStartup + 60f; }
        }

        /// <summary>Host side: start when every ready player joined, or at the deadline with at least two.</summary>
        void UpdateRematchRoom(RoomState r)
        {
            var s = r.rematch;
            int want = s.ReadyCount;
            float now = Time.realtimeSinceStartup;
            if (r.info.players.Count >= 2 && (r.info.players.Count >= want || now >= r.rematchDeadline))
            {
                StartHostedMatch();
                return;
            }
            if (now >= r.rematchDeadline + 6f) Fail(r, "Nobody joined the rematch.");
        }

        void RematchStarted(OnlineRematch s, BattleConfig cfg)
        {
            if (cfg == null) { RematchFailed(s, null); return; }
            s.finished = true;
            if (rematch == s) rematch = null;
            s.version++;
            try { s.started?.Invoke(cfg); } catch (Exception e) { Debug.LogException(e); }
        }

        void RematchFailed(OnlineRematch s, string why)
        {
            if (s.finished) return;
            s.finished = true;
            s.starting = false;
            if (rematch == s) rematch = null;
            s.status = string.IsNullOrEmpty(why) ? "The rematch didn't work out." : why;
            s.version++;
            if (room != null && room.matchId == s.newMatchId) CancelMatchmaking();
        }
    }
}
