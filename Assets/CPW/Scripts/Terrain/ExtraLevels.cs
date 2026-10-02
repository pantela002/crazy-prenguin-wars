using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Remake-only levels (Resources/Data/extra_levels.json) merged into GameData's Level section at startup, so every
    /// level list (map pickers, Play Now, online lobby, random map) shows them like the original 23 maps.
    /// The file has the config.json shape: { "Level": { "id": { "ID", "LevelFile", "MinLevel", ... } } }.
    /// Rows never replace an existing config row with the same id. Idempotent; safe to call from anywhere.
    /// </summary>
    public static class ExtraLevels
    {
        static bool merged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { merged = false; Ensure(); }

        /// <summary>Merge the extra rows into GameData (once per GameData load).</summary>
        public static void Ensure()
        {
            if (merged) return;
            merged = true;
            var ta = Resources.Load<TextAsset>("Data/extra_levels");
            if (ta == null) return;
            var root = MiniJson.ParseObject(ta.text);
            if (root == null) { Debug.LogWarning("CPW: extra_levels.json could not be parsed"); return; }
            foreach (var kv in root)
            {
                if (!(kv.Value is Dictionary<string, object> rows)) continue;
                // Section() returns the live dictionary when the section exists (a throwaway one otherwise)
                bool exists = false;
                foreach (var n in GameData.SectionNames) if (n == kv.Key) { exists = true; break; }
                if (!exists) continue;
                var section = GameData.Section(kv.Key);
                foreach (var row in rows)
                {
                    if (row.Key == "$DATA_TYPE" || !(row.Value is Dictionary<string, object> raw)) continue;
                    if (section.ContainsKey(row.Key)) continue;
                    section[row.Key] = new Record(kv.Key, row.Key, raw);
                }
            }
        }
    }
}
