using System;
using System.Collections.Generic;

namespace CPW
{
    [Serializable]
    public class LeaderboardEntry
    {
        public string playerId;
        public string name;
        public int score;      // total XP
        public int level;
        public int wins;
        public int rank;
    }

    /// <summary>Summary of an online match in the lobby list.</summary>
    [Serializable]
    public class OnlineMatchInfo
    {
        public string matchId;
        public string hostName;
        public string levelId;
        public int players, maxPlayers;
        public string state;   // "waiting", "playing", "finished"
        public bool isPrivate;
        public string code;    // short join code for private games
    }

    /// <summary>One person in an online waiting room.</summary>
    [Serializable]
    public class OnlineRoomPlayer
    {
        public string userId;
        public string name;
        public int level;
        public bool isHost;
        public bool isLocal;
    }

    /// <summary>
    /// The waiting room the local player is in (hosting or joined) before a match starts.
    /// version increases whenever something changes so screens can redraw cheaply.
    /// </summary>
    public class OnlineRoom
    {
        public string matchId = "";
        public string code = "";          // short join code (always set; only shown/needed for private games)
        public string levelId = "";       // empty = random map chosen when the match starts
        public bool isHost, isPrivate, isQuickMatch;
        public string state = "waiting";  // "waiting", "starting", "playing", "closed"
        public string status = "";        // human readable progress text
        public int maxPlayers = 4;
        public List<OnlineRoomPlayer> players = new List<OnlineRoomPlayer>();
        public int version;
        public bool CanStart => isHost && state == "waiting" && players.Count >= 2;
    }

    /// <summary>
    /// Online features. The game always works offline; when Firebase is configured
    /// (Resources/firebase_config.json, see Docs/FIREBASE.md) Online.Service becomes the Firebase implementation.
    /// </summary>
    public interface IOnlineService
    {
        bool Available { get; }          // configured and signed in
        string Status { get; }           // human readable state for the settings screen
        void Init(Action<bool> done);
        void PushProfile(PlayerProfile p);
        void PullProfile(Action<PlayerProfile> done);
        void SubmitStats(PlayerProfile p);
        void GetLeaderboard(int count, Action<List<LeaderboardEntry>> done);
        void ListOpenMatches(Action<List<OnlineMatchInfo>> done);
        /// <summary>
        /// Create a match and wait for players. created gets the room info (with the join code), started gets the
        /// battle config (mode Online, network set) once the host starts it, or null on cancel/failure.
        /// </summary>
        void HostMatch(BattleConfig settings, bool isPrivate, Action<OnlineMatchInfo> created, Action<BattleConfig> started);
        void JoinMatch(string matchIdOrCode, Action<BattleConfig> started, Action<string> failed);
        void QuickMatch(Action<BattleConfig> started, Action<string> status);
        /// <summary>Leave the waiting room / stop matchmaking (the host closes the room for everybody).</summary>
        void CancelMatchmaking();
        /// <summary>The waiting room we are in, or null.</summary>
        OnlineRoom Room { get; }
        /// <summary>Host only: start the match with the players in the room (needs at least 2).</summary>
        void StartHostedMatch();
    }

    public sealed class OfflineService : IOnlineService
    {
        public bool Available => false;
        public string Status => "Offline (Firebase not connected)";
        public void Init(Action<bool> done) => done?.Invoke(false);
        public void PushProfile(PlayerProfile p) { }
        public void PullProfile(Action<PlayerProfile> done) => done?.Invoke(null);
        public void SubmitStats(PlayerProfile p) { }
        public void GetLeaderboard(int count, Action<List<LeaderboardEntry>> done) => done?.Invoke(new List<LeaderboardEntry>());
        public void ListOpenMatches(Action<List<OnlineMatchInfo>> done) => done?.Invoke(new List<OnlineMatchInfo>());
        public void HostMatch(BattleConfig settings, bool isPrivate, Action<OnlineMatchInfo> created, Action<BattleConfig> started) => started?.Invoke(null);
        public void JoinMatch(string id, Action<BattleConfig> started, Action<string> failed) => failed?.Invoke("Online play needs Firebase. See Settings.");
        public void QuickMatch(Action<BattleConfig> started, Action<string> status) => status?.Invoke("Online play needs Firebase. See Settings.");
        public void CancelMatchmaking() { }
        public OnlineRoom Room => null;
        public void StartHostedMatch() { }
    }

    public static class Online
    {
        /// <summary>The active online service; replaced by the Firebase implementation at startup when configured.</summary>
        public static IOnlineService Service { get; set; } = new OfflineService();
    }
}
