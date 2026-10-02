using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Port of the original ProfanityFilter with its word list (Resources/Data/profanity_en.txt, comma separated).
    /// The original replaced every listed word found anywhere inside a word ("class" became "cl***"); here a word
    /// is starred when it is on the list (letters only, case-insensitive), multi-word entries ("blow job") are
    /// matched across words, and a few unambiguous roots are starred inside longer words too.
    /// Also removes the characters the chat log would treat as markup.
    /// </summary>
    public static class ProfanityFilter
    {
        static HashSet<string> words;
        static List<string[]> phrases;
        static readonly string[] Roots = { "fuck", "shit", "cunt", "bitch", "nigg", "fagg" };

        static void Load()
        {
            if (words != null) return;
            words = new HashSet<string>();
            phrases = new List<string[]>();
            var ta = Resources.Load<TextAsset>("Data/profanity_en");
            if (ta == null) return;
            foreach (var raw in ta.text.Split(','))
            {
                var w = raw.Trim().ToLowerInvariant();
                if (w.Length == 0) continue;
                if (w.IndexOf(' ') >= 0) phrases.Add(w.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries));
                else words.Add(Letters(w));
            }
        }

        static string Letters(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s) if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            return sb.ToString();
        }

        static string Stars(int n) => new string('*', Mathf.Max(1, n));

        /// <summary>Filtered copy of a chat line (trimmed, single-line, no markup characters).</summary>
        public static string Filter(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            Load();
            text = text.Replace('\n', ' ').Replace('\r', ' ').Replace('<', '(').Replace('>', ')').Trim();
            var parts = text.Split(' ');
            var keys = new string[parts.Length];
            for (int i = 0; i < parts.Length; i++) keys[i] = Letters(parts[i]);

            // multi-word entries
            foreach (var ph in phrases)
                for (int i = 0; i + ph.Length <= parts.Length; i++)
                {
                    bool match = true;
                    for (int k = 0; k < ph.Length && match; k++) match = keys[i + k] == ph[k];
                    if (!match) continue;
                    for (int k = 0; k < ph.Length; k++) { parts[i + k] = Stars(parts[i + k].Length); keys[i + k] = ""; }
                }

            for (int i = 0; i < parts.Length; i++)
            {
                var key = keys[i];
                if (key.Length == 0) continue;
                bool bad = words.Contains(key);
                if (!bad) foreach (var r in Roots) if (key.Contains(r)) { bad = true; break; }
                if (bad) parts[i] = Stars(parts[i].Length);
            }
            return string.Join(" ", parts);
        }
    }
}
