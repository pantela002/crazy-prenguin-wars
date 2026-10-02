using System;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// The three values needed to talk to a Firebase project over REST. Loaded from
    /// Assets/CPW/Resources/firebase_config.json (gitignored, see Docs/FIREBASE.md):
    /// <code>{ "apiKey": "...", "projectId": "...", "databaseURL": "https://...firebasedatabase.app" }</code>
    /// The template Assets/CPW/Resources/firebase_config.example.json is never loaded at runtime
    /// (Resources.Load("firebase_config") only matches the exact file name).
    /// </summary>
    [Serializable]
    public class FirebaseConfig
    {
        public const string ResourceName = "firebase_config";

        public string apiKey = "";
        public string projectId = "";
        public string databaseURL = "";

        /// <summary>True when every field is filled in with something that is not the template placeholder.</summary>
        public bool IsValid =>
            Filled(apiKey) && Filled(projectId) && Filled(databaseURL) &&
            databaseURL.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        /// <summary>Database root URL without the trailing slash.</summary>
        public string DbRoot => (databaseURL ?? "").Trim().TrimEnd('/');

        static bool Filled(string s) =>
            !string.IsNullOrWhiteSpace(s) && s.IndexOf("YOUR_", StringComparison.OrdinalIgnoreCase) < 0 && !s.Contains("<");

        /// <summary>Load the config from Resources. Returns null when the file is missing or unreadable (= offline game).</summary>
        public static FirebaseConfig Load()
        {
            try
            {
                var ta = Resources.Load<TextAsset>(ResourceName);
                if (ta == null || string.IsNullOrWhiteSpace(ta.text)) return null;
                var d = MiniJson.ParseObject(ta.text);
                if (d == null) return null;
                var c = new FirebaseConfig
                {
                    apiKey = Str(d, "apiKey"),
                    projectId = Str(d, "projectId"),
                    databaseURL = Str(d, "databaseURL"),
                };
                c.apiKey = c.apiKey.Trim();
                c.projectId = c.projectId.Trim();
                c.databaseURL = c.DbRoot;
                return c;
            }
            catch (Exception e)
            {
                Debug.LogWarning("CPW: firebase_config.json could not be read: " + e.Message);
                return null;
            }
        }

        static string Str(System.Collections.Generic.Dictionary<string, object> d, string k) =>
            d.TryGetValue(k, out var v) && v != null ? v.ToString() : "";
    }
}
