using System;
using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    public enum BattleMode
    {
        Tutorial,     // guided first battle (PracticeLevel.tutorial_level)
        Practice,     // vs AI with the Practice settings, free ammo from the "Practice" category
        QuickMatch,   // vs AI opponents picked for your level, rewards and ammo use like the original "Play Now"
        Custom,       // local pass-and-play / vs AI with chosen map, time, turn time and players
        Online        // Firebase turn-based match against other people
    }

    /// <summary>One penguin in a battle.</summary>
    [Serializable]
    public class PlayerSlot
    {
        public string name = "Penguin";
        public bool isAI;
        public int aiSkill = 1;               // 0 easy, 1 normal, 2 hard
        public bool isLocalHuman = true;      // controlled on this device
        public string onlineUserId = "";      // set for Online battles
        public int team = -1;                 // -1 = free for all
        public string head = "", chest = "", feet = "", trophy = "";   // Bonus ids
        public bool usesProfileInventory;     // ammo comes from (and is spent from) the local profile
        public List<ItemStack> loadout = new List<ItemStack>();        // ammo when not using the profile
        public List<string> boosters = new List<string>();             // booster item ids picked before battle
        public int colorIndex;
        public int level = 1;
    }

    [Serializable]
    public class BattleConfig
    {
        public BattleMode mode = BattleMode.QuickMatch;
        public string levelId = "";           // Level section id; empty = random allowed for the player's level
        public List<PlayerSlot> players = new List<PlayerSlot>();
        public float matchTime = 240;         // seconds (BattleOptions.MatchTime)
        public float turnTime = 10;           // seconds (BattleOptions.TurnTime)
        public int winningScore = 0;          // 0 = play until the clock runs out
        public bool powerUps = true;          // drop power-up crates
        public string betId = "1NoBet";       // Bet section id
        public int seed;
        [NonSerialized] public IBattleNetwork network;   // only for Online battles

        public int LocalPlayerIndex
        {
            get
            {
                for (int i = 0; i < players.Count; i++) if (players[i].isLocalHuman && !players[i].isAI) return i;
                return 0;
            }
        }
    }

    [Serializable]
    public class PlayerResult
    {
        public int slotIndex;
        public string name;
        public int score, kills, deaths, damageDealt, rank;
        public int coins, xp, cash;
        public bool isLocal;
        public List<ItemStack> usedItems = new List<ItemStack>();
        public List<ItemStack> earnedItems = new List<ItemStack>();
    }

    [Serializable]
    public class BattleResult
    {
        public BattleConfig config;
        public List<PlayerResult> players = new List<PlayerResult>();
        public bool aborted;
        public int WinnerIndex => players.Count == 0 ? -1 : players.Find(p => p.rank == 1)?.slotIndex ?? -1;
        public PlayerResult Local => players.Find(p => p.isLocal);
    }

    /// <summary>
    /// What the active player did during a turn, recorded with timestamps so other devices can replay it.
    /// type: "move" (x=-1/0/1), "jump" (x,y = direction*power), "aim" (x=angle deg, y=power01),
    /// "weapon" (s=item id), "fire" (x=angle, y=power01, s=item id, px/py=target point), "booster" (s=item id),
    /// "emote" (s=emoticon id), "pos" (x,y = penguin position, sent periodically to correct drift).
    /// </summary>
    [Serializable]
    public class TurnAction
    {
        public int player;
        public float t;
        public string type;
        public float x, y, px, py;
        public string s;
    }

    [Serializable]
    public class PenguinState
    {
        public int slot;
        public float x, y, hp;
        public bool alive;
        public int score, kills, deaths;
        public List<string> statuses = new List<string>();
        public List<ItemStack> ammo = new List<ItemStack>();
    }

    [Serializable]
    public class CraterState { public float x, y, r; public bool add; }   // add = terrain was created (e.g. shield wall)

    [Serializable]
    public class DynamicObjectState { public string id; public float x, y, angle, hp; public bool alive; }

    /// <summary>Authoritative world state sent by the active player at the end of their turn.</summary>
    [Serializable]
    public class BattleSnapshot
    {
        public int turnNumber;
        public int nextPlayer;
        public float matchTimeLeft;
        public List<PenguinState> penguins = new List<PenguinState>();
        public List<CraterState> craters = new List<CraterState>();         // every terrain change since the start
        public List<DynamicObjectState> objects = new List<DynamicObjectState>();
        public bool matchOver;
    }

    /// <summary>
    /// One in-battle chat line (original ChatMessage: id, text, tid). Sent by any player at any time, outside the
    /// turn actions. text is free text (already profanity filtered by the sender, filtered again on arrival);
    /// tid is a quick-chat preset id ("qc.*") or a string key, shown instead of text when the receiver knows it.
    /// </summary>
    [Serializable]
    public class BattleChatMessage
    {
        public int player = -1;     // slot index of the sender (-1 = system line)
        public string text = "";
        public string tid = "";
    }

    /// <summary>
    /// Transport for online battles, implemented by the Firebase layer (Online/). The battle code records
    /// actions for the local player's turn, sends them, and applies snapshots it receives.
    /// Chat has default (no-op) implementations so transports without chat still compile.
    /// </summary>
    public interface IBattleNetwork
    {
        /// <summary>Index into BattleConfig.players of the person on this device.</summary>
        int LocalSlot { get; }
        bool Connected { get; }
        void SendAction(TurnAction action);
        void SendTurnEnd(BattleSnapshot snapshot);
        void SendLeave();
        event Action<TurnAction> ActionReceived;
        event Action<BattleSnapshot> TurnEndReceived;
        event Action<int> PlayerLeft;
        /// <summary>Called every frame by the battle so the transport can poll.</summary>
        void Tick(float dt);
        /// <summary>Whether this transport carries chat (the HUD hides the chat button otherwise).</summary>
        bool SupportsChat => false;
        void SendChat(BattleChatMessage message) { }
        /// <summary>Chat from other players (never echoes our own); raised from Tick like the other events.</summary>
        event Action<BattleChatMessage> ChatReceived { add { } remove { } }
    }

    /// <summary>Global battle events (challenges/achievements, sounds and the online layer listen to these).</summary>
    public static class BattleEvents
    {
        public static event Action<BattleConfig> BattleStarted;
        public static event Action<BattleResult> BattleEnded;
        public static event Action<int> TurnStarted;                          // player index
        public static event Action<int, string> WeaponFired;                  // player index, item id
        public static event Action<int, int, float, string> PenguinDamaged;   // victim, attacker (-1 = world), amount, item id
        public static event Action<int, int, string> PenguinKilled;           // victim, killer (-1 = world/water), item id
        public static event Action<int, string> BoosterUsed;                  // player index, item id
        public static event Action<Vector2, float> Explosion;                 // world position, radius (world units)

        public static void RaiseBattleStarted(BattleConfig c) => BattleStarted?.Invoke(c);
        public static void RaiseBattleEnded(BattleResult r) => BattleEnded?.Invoke(r);
        public static void RaiseTurnStarted(int p) => TurnStarted?.Invoke(p);
        public static void RaiseWeaponFired(int p, string item) => WeaponFired?.Invoke(p, item);
        public static void RaisePenguinDamaged(int v, int a, float amount, string item) => PenguinDamaged?.Invoke(v, a, amount, item);
        public static void RaisePenguinKilled(int v, int k, string item) => PenguinKilled?.Invoke(v, k, item);
        public static void RaiseBoosterUsed(int p, string item) => BoosterUsed?.Invoke(p, item);
        public static void RaiseExplosion(Vector2 pos, float radius) => Explosion?.Invoke(pos, radius);
    }
}
