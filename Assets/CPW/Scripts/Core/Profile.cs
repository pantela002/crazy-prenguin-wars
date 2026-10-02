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
        public string wornHead = "";
        public string wornChest = "";
        public string wornFeet = "";
        public List<string> trophies = new List<string>();        // trophy Bonus ids (BandaidBadge ...)
        public string wornTrophy = "";

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
