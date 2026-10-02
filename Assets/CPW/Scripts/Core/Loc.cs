using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>English strings from the original game (TID table). Unknown keys fall back to a readable form of the key.</summary>
    public static class Loc
    {
        static Dictionary<string, string> strings;

        static void Load()
        {
            if (strings != null) return;
            strings = new Dictionary<string, string>();
            var ta = Resources.Load<TextAsset>("Data/strings_en");
            if (ta == null) return;
            var d = MiniJson.ParseObject(ta.text);
            foreach (var kv in d) strings[kv.Key] = kv.Value as string;
        }

        public static bool Has(string key) { Load(); return key != null && strings.ContainsKey(Strip(key)); }

        public static string T(string key)
        {
            Load();
            if (string.IsNullOrEmpty(key)) return "";
            var k = Strip(key);
            if (strings.TryGetValue(k, out var s) && !string.IsNullOrEmpty(s)) return s.Replace(' ', ' ').Replace("\\n", "\n");   // the original strings carry literal \n line breaks
            return Prettify(k);
        }

        /// <summary>T(key) with {0}, {1}... or the original game's %1 style placeholders filled in.</summary>
        public static string F(string key, params object[] args)
        {
            var s = T(key);
            for (int i = 0; i < args.Length; i++)
            {
                s = s.Replace("{" + i + "}", args[i]?.ToString()).Replace("%" + (i + 1), args[i]?.ToString());
            }
            return s;
        }

        static string Strip(string key) => key.StartsWith("#TID.") ? key.Substring(5) : key;

        public static string Prettify(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (c == '_') { sb.Append(' '); continue; }
                if (i > 0 && char.IsUpper(c) && char.IsLower(id[i - 1])) sb.Append(' ');
                sb.Append(i == 0 || id[i - 1] == '_' || id[i - 1] == ' ' ? char.ToUpperInvariant(c) : (char.IsUpper(id[i]) && i > 0 && char.IsUpper(id[i - 1]) ? char.ToLowerInvariant(c) : c));
            }
            return sb.ToString();
        }
    }
}
