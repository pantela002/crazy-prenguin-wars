using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Online battles: the active player's device records its TurnActions and sends them; other devices replay them.
    /// At the end of a turn the active device sends an authoritative BattleSnapshot that everyone applies.
    /// </summary>
    public partial class BattleController
    {
        // network callbacks may come from any thread; they are queued and handled in Update
        readonly object netLock = new object();
        readonly Queue<TurnAction> netActions = new Queue<TurnAction>();
        readonly Queue<BattleSnapshot> netSnapshots = new Queue<BattleSnapshot>();
        readonly Queue<int> netLeft = new Queue<int>();

        void OnNetAction(TurnAction a) { if (a != null) lock (netLock) netActions.Enqueue(a); }
        void OnNetTurnEnd(BattleSnapshot s) { if (s != null) lock (netLock) netSnapshots.Enqueue(s); }
        void OnNetPlayerLeft(int slot) { lock (netLock) netLeft.Enqueue(slot); }

        bool netWasConnected = true;

        void DrainNet()
        {
            DrainChat();
            if (net.Connected != netWasConnected && CurrentPhase != Phase.Over)
            {
                netWasConnected = net.Connected;
                SystemChat(netWasConnected ? "Connection restored." : "Connection lost. Trying to reconnect...");
            }
            while (true)
            {
                TurnAction a = null; BattleSnapshot s = null; int left = -1; bool any = false;
                lock (netLock)
                {
                    if (netLeft.Count > 0) { left = netLeft.Dequeue(); any = true; }
                    else if (netActions.Count > 0) { a = netActions.Dequeue(); any = true; }
                    else if (netSnapshots.Count > 0) { s = netSnapshots.Dequeue(); any = true; }
                }
                if (!any) return;
                if (left >= 0) HandlePlayerLeft(left);
                else if (a != null) ReplayAction(a);
                else if (s != null) HandleSnapshot(s);
            }
        }

        /// <summary>Apply an action recorded on another device.</summary>
        void ReplayAction(TurnAction a)
        {
            var p = PenguinAt(a.player);
            if (p == null || (OwnedLocally(a.player) && a.type != "emote")) return;
            if (a.type == "emote") { if (!OwnedLocally(a.player)) p.Emote(a.s); return; }
            if (a.player != ActiveIndex || p.Left) return;
            if (CurrentPhase == Phase.Curtain) StartPlaying();
            switch (a.type)
            {
                case "move": p.Walk(Mathf.RoundToInt(a.x)); break;
                case "jump": if (!p.Jump(a.x) && p.Alive) { p.ActionPoints = Mathf.Max(p.ActionPoints, p.JumpCost); p.Jump(a.x); } break;
                case "weapon": DoSelectWeapon(a.player, a.s); break;
                case "aim": p.Aiming = true; p.SetAim(a.x, a.y); break;
                case "fire":
                    if (string.IsNullOrEmpty(a.s)) break;
                    p.Ammo.Consume(a.s);
                    DoFire(p, a.s, a.x, a.y, new Vector2(a.px, a.py));
                    break;
                case "booster":
                    if (WeaponSystem.UseBooster(p, a.s))
                    {
                        p.Ammo.Consume(a.s);
                        BoosterUsedThisTurn = true;
                        BattleEvents.RaiseBoosterUsed(a.player, a.s);
                    }
                    break;
                case "pos":
                    // drift correction while walking; small differences are left to the end-of-turn snapshot
                    var target = new Vector2(a.x, a.y);
                    if (p.Alive && Vector2.Distance(p.Position, target) > 1.2f) p.Teleport(target);
                    break;
            }
        }

        void HandleSnapshot(BattleSnapshot s)
        {
            // ignore old snapshots and our own echoed back (we are the sender of this turn)
            if (CurrentPhase == Phase.Over || s.turnNumber < TurnNumber) return;
            if (OwnedLocally(ActiveIndex) && Active != null && !Active.Left) return;
            if (CurrentPhase == Phase.Turn) EndTurn();
            AfterTurn(s);
        }

        void HandlePlayerLeft(int slot)
        {
            var p = PenguinAt(slot);
            if (p == null || p.Left) return;
            p.Left = true;
            p.EndTurn();
            if (p.Body) p.Body.simulated = false;
            if (p.Avatar) p.Avatar.gameObject.SetActive(false);
            Hud.Banner(p.DisplayName + " left the match", Theme.Muted, 2f);
            // original BattleManager: LocalChatMessage EXIT_CONFIRMED_INGAME ("has chickened out.")
            SystemChat(p.DisplayName + " " + (Loc.Has("EXIT_CONFIRMED_INGAME") ? Loc.T("EXIT_CONFIRMED_INGAME") : "left the game."));
            // the AI host may have changed
            for (int i = 0; i < Penguins.Count; i++) if (!Penguins[i].Slot.isAI && !Penguins[i].Left) { hostSlot = i; break; }
            if (CurrentPhase == Phase.Over) return;
            if (Participants() < 2) { EndMatch(false); return; }
            if (slot == ActiveIndex && (CurrentPhase == Phase.Turn || CurrentPhase == Phase.Settling || CurrentPhase == Phase.Curtain))
            {
                // nobody will send this turn's snapshot: every device moves on to the same next player,
                // and the lowest connected human (the authority) also sends one so the server turn advances
                CurrentPhase = Phase.Settling;
                int next = NextPlayer(ActiveIndex);
                bool over = MatchTimeLeft <= 0 || (WinningScore > 0 && HighestScore() >= WinningScore);
                if (net != null && net.LocalSlot == hostSlot) net.SendTurnEnd(BuildSnapshot(next, over));
                if (over) EndMatch(false); else BeginTurn(next);
            }
        }

        BattleSnapshot BuildSnapshot(int next, bool over)
        {
            var s = new BattleSnapshot { turnNumber = TurnNumber, nextPlayer = next, matchTimeLeft = MatchTimeLeft, matchOver = over };
            foreach (var p in Penguins)
            {
                var st = new PenguinState
                {
                    slot = p.PlayerIndex, x = p.Position.x, y = p.Position.y, hp = p.HP, alive = p.Alive,
                    score = p.Score, kills = p.Kills, deaths = p.Deaths
                };
                p.FillEffects(st.statuses);
                if (!p.Slot.usesProfileInventory || p.PlayerIndex != net.LocalSlot) p.Ammo.FillCounts(st.ammo);
                s.penguins.Add(st);
            }
            if (Terrain != null) { s.craters.AddRange(Terrain.History); Terrain.CaptureObjects(s.objects); }
            return s;
        }

        void ApplySnapshot(BattleSnapshot s)
        {
            if (Terrain != null && s.craters != null) Terrain.ApplyHistory(s.craters);
            if (Terrain != null && s.objects != null) Terrain.ApplyObjects(s.objects);
            MatchTimeLeft = s.matchTimeLeft;
            foreach (var st in s.penguins)
            {
                var p = PenguinAt(st.slot);
                if (p == null || p.Left) continue;
                p.ApplyState(st);
                // ammo of others is authoritative from their device; our own profile ammo stays local
                if (st.slot != net?.LocalSlot)
                    foreach (var a in st.ammo) p.Ammo.SetCount(a.id, a.amount);
            }
        }
    }
}
