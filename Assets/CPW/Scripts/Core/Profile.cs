using System;
using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    [Serializable]
    public class ItemStack
    {
        public string id;
        public int amount;
        public ItemStack() { }
        public ItemStack(string id, int amount) { this.id = id; this.amount = amount; }
    }

    [Serializable]
    public class CounterValue
    {
        public string id;
        public int value;
    }

    [Serializable]
    public class ResearchState
    {
        public string recipeId;      // item being researched/crafted
        public long finishUnixMs;    // when it completes
    }

    /// <summary>
    /// Battle statistics for one leaderboard period (like the original PlayerReport weekly / monthly / all-time data).
    /// key names the period ("2026-W40", "2026-10" or "all"); a different key means the counters belong to an old period.
    /// </summary>
    [Serializable]
    public class PeriodStats
    {
        public string key = "";
        public int games, wins, xp, kills, deaths, suicides, turns, damage, shots, boosters, explosions;

        public void Reset(string newKey)
        {
            key = newKey ?? "";
            games = wins = xp = kills = deaths = suicides = turns = damage = shots = boosters = explosions = 0;
        }
    }

    /// <summary>Everything saved about the local player. Serialized with JsonUtility (PlayerPrefs, and Firebase when connected).</summary>
    [Serializable]
    public class PlayerProfile
    {
        public string playerId = "";
        public string displayName = "Penguin";
        public int coins = 2020;       // in-game money ("coins", GC)
        public int cash = 45;          // premium money ("cash"/fish, PC)
        public int xp = 0;
        public int level = 1;
        public bool vip = false;
        public long vipUntilUnixMs = 0;

        public List<ItemStack> items = new List<ItemStack>();     // weapons and boosters with ammo counts
        public List<string> unlockedItems = new List<string>();   // permanently unlocked items (e.g. via premium unlock)
        public List<string> ownedClothes = new List<string>();    // Bonus ids for clothes (flannel_head ...)
        public string wornHead = "";      // "" = bare (new profiles start with nothing worn)
        public string wornChest = "";
        public string wornFeet = "";
        public List<string> trophies = new List<string>();        // trophy Bonus ids (BandaidBadge ...)
        public string wornTrophy = "";
        public string wornHands = "";     // gloves (ClothesSlot.Hands), "" = bare flippers
        public string wornSkin = "";      // penguin skin (ClothesSlot.Skin, "skin_..."), "" = classic

        public List<CounterValue> counters = new List<CounterValue>(); // Counters section ids
        public List<string> claimedAchievements = new List<string>();
        public List<string> knownRecipes = new List<string>();
        public List<ResearchState> research = new List<ResearchState>();

        public int slotSpinsUsedToday = 0;
        public string slotSpinsDay = "";
        public string lastDailyGiftDay = "";
        public int dailyStreak = 0;

        public bool tutorialDone = false;
        public bool musicOn = true;
        public bool sfxOn = true;
        public float musicVolume = 0.7f;
        public float sfxVolume = 1f;
        public int quality = 1; // 0 low, 1 medium, 2 high
        public bool showTrajectory = true;

        public int gamesPlayed, gamesWon, kills, deaths;
        public long totalDamage;
        public int bestScore;
        public long lastSavedUnixMs;

        // leaderboard periods (Meta/PlayerStatsTracker.cs keeps them current; ISO week / month in UTC)
        public PeriodStats statsWeek = new PeriodStats();
        public PeriodStats statsMonth = new PeriodStats();
        public PeriodStats statsAll = new PeriodStats { key = "all" };

        // weekly league (Online/League.cs): tier 0 = lowest; points of the week in leagueWeek ("2026-W40").
        // A finished week waits in leaguePending* until it is settled online (promotion/relegation + rewards).
        public int leagueTier;
        public string leagueWeek = "";
        public int leaguePoints, leagueGames;
        public string leaguePendingWeek = "";
        public int leaguePendingTier, leaguePendingPoints, leaguePendingGames;
        public string leagueLastResult = "";   // shown on the tournament screen ("Week 39: 3rd in Silver, promoted!")

        public int Ammo(string itemId)
        {
            foreach (var s in items) if (s.id == itemId) return s.amount;
            return 0;
        }

        public void AddAmmo(string itemId, int amount)
        {
            foreach (var s in items) if (s.id == itemId) { s.amount = Mathf.Max(0, s.amount + amount); return; }
            if (amount > 0) items.Add(new ItemStack(itemId, amount));
        }

        public int Counter(string id)
        {
            foreach (var c in counters) if (c.id == id) return c.value;
            return 0;
        }

        public void AddCounter(string id, int delta)
        {
            foreach (var c in counters) if (c.id == id) { c.value += delta; return; }
            counters.Add(new CounterValue { id = id, value = delta });
        }

        public static PlayerProfile CreateDefault()
        {
            var p = new PlayerProfile();
            p.playerId = Guid.NewGuid().ToString("N");
            // Starting values from the original default_player.json and Tuner
            p.coins = 2020;
            p.cash = 45;
            p.items.Add(new ItemStack("BasicNuke", 5));
            p.items.Add(new ItemStack("Punch", 5));
            p.items.Add(new ItemStack("Pistol", 5));
            p.items.Add(new ItemStack("Grenade", 5));
            // Like the original (worn_items: [], no starter clothes) the penguin starts bare: nothing owned,
            // nothing worn. The player dresses it on the Character screen (Meta/WardrobeScreen).
            p.ownedClothes.Clear();
            p.wornHead = p.wornChest = p.wornFeet = p.wornTrophy = p.wornHands = p.wornSkin = "";
            return p;
        }
    }

    /// <summary>Loads/saves the local profile. Online services can listen to Saved to push it to the cloud.</summary>
    public static class ProfileService
    {
        const string Key = "cpw_profile_v1";
        static PlayerProfile current;

        public static event Action Changed;
        public static event Action<PlayerProfile> Saved;

        public static PlayerProfile P
        {
            get
            {
                if (current == null) Load();
                return current;
            }
        }

        public static void Load()
        {
            var json = PlayerPrefs.GetString(Key, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { current = JsonUtility.FromJson<PlayerProfile>(json); }
                catch (Exception e) { Debug.LogWarning("CPW: profile corrupt, resetting. " + e.Message); }
            }
            if (current == null) { current = PlayerProfile.CreateDefault(); Save(); }
        }

        /// <summary>Replace the profile (e.g. with the cloud copy) and save it locally.</summary>
        public static void Replace(PlayerProfile p)
        {
            if (p == null) return;
            current = p;
            Save();
        }

        public static void Save()
        {
            if (current == null) return;
            current.lastSavedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            current.level = Mathf.Max(current.level, GameData.LevelForXp(current.xp));
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(current));
            PlayerPrefs.Save();
            Changed?.Invoke();
            Saved?.Invoke(current);
        }

        public static void ResetAll()
        {
            PlayerPrefs.DeleteKey(Key);
            current = null;
            Load();
        }

        /// <summary>Notify listeners (HUD/top bar) without saving.</summary>
        public static void NotifyChanged() => Changed?.Invoke();
    }
}
