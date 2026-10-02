using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// One row of the original game's config (e.g. Item.Grenade). Field values come straight from
    /// Resources/Data/config.json; references like "#Missile.Grenade" are resolved with Ref().
    /// </summary>
    public sealed class Record
    {
        public readonly string Section;
        public readonly string Id;
        public readonly Dictionary<string, object> Raw;

        public Record(string section, string id, Dictionary<string, object> raw)
        {
            Section = section; Id = id; Raw = raw ?? new Dictionary<string, object>();
        }

        public bool Has(string key) => Raw.ContainsKey(key) && Raw[key] != null;
        public object Get(string key) => Raw.TryGetValue(key, out var v) ? v : null;

        public string Str(string key, string def = null)
        {
            var v = Get(key);
            if (v == null) return def;
            if (v is double d) return d.ToString(CultureInfo.InvariantCulture);
            return v.ToString();
        }

        public float Float(string key, float def = 0f)
        {
            var v = Get(key);
            switch (v)
            {
                case null: return def;
                case double d: return (float)d;
                case long l: return l;
                case bool b: return b ? 1 : 0;
                case string s:
                    return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : def;
            }
            return def;
        }

        public int Int(string key, int def = 0) => Has(key) ? Mathf.RoundToInt(Float(key, def)) : def;

        public bool Bool(string key, bool def = false)
        {
            var v = Get(key);
            switch (v)
            {
                case null: return def;
                case bool b: return b;
                case string s: return s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1";
                case long l: return l != 0;
                case double d: return d != 0;
            }
            return def;
        }

        public List<string> List(string key)
        {
            var res = new List<string>();
            var v = Get(key);
            if (v is List<object> l)
            {
                foreach (var o in l) if (o != null) res.Add(o is double d ? d.ToString(CultureInfo.InvariantCulture) : o.ToString());
            }
            else if (v is string s && s.Length > 0) res.Add(s);
            return res;
        }

        /// <summary>Resolve a "#Section.Id" reference field to its record (null if missing).</summary>
        public Record Ref(string key) => GameData.Resolve(Str(key));

        /// <summary>Resolve every "#Section.Id" entry of a list field.</summary>
        public List<Record> RefList(string key)
        {
            var res = new List<Record>();
            foreach (var s in List(key))
            {
                var r = GameData.Resolve(s);
                if (r != null) res.Add(r);
            }
            return res;
        }

        public override string ToString() => Section + "." + Id;
    }

    /// <summary>
    /// A stat modifier string from the original data, e.g. "Add:60:Normal", "Multiply:1.5".
    /// </summary>
    public struct StatMod
    {
        public string Op;     // "Add", "Multiply", "Set"
        public float Value;
        public string Tag;    // optional third part (damage type such as "Normal", "Fire")

        public static StatMod Parse(string s)
        {
            var m = new StatMod { Op = "Add", Value = 0, Tag = "" };
            if (string.IsNullOrEmpty(s)) return m;
            var parts = s.Split(':');
            if (parts.Length == 1) { float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out m.Value); return m; }
            m.Op = parts[0];
            float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out m.Value);
            if (parts.Length > 2) m.Tag = parts[2];
            return m;
        }

        public float Apply(float baseValue)
        {
            switch (Op)
            {
                case "Multiply": return baseValue * Value;
                case "Set": return Value;
                default: return baseValue + Value;
            }
        }
    }

    /// <summary>Access to the original Crazy Penguin Wars config (all weapons, boosters, levels, prices...).</summary>
    public static class GameData
    {
        static Dictionary<string, Dictionary<string, Record>> sections;

        public static bool Loaded => sections != null;

        public static void Load()
        {
            if (sections != null) return;
            sections = new Dictionary<string, Dictionary<string, Record>>();
            var ta = Resources.Load<TextAsset>("Data/config");
            if (ta == null) { Debug.LogError("CPW: Resources/Data/config.json missing"); return; }
            var root = MiniJson.ParseObject(ta.text);
            foreach (var kv in root)
            {
                if (!(kv.Value is Dictionary<string, object> sec)) continue;
                var rows = new Dictionary<string, Record>();
                foreach (var row in sec)
                {
                    if (row.Key == "$DATA_TYPE") continue;
                    rows[row.Key] = new Record(kv.Key, row.Key, row.Value as Dictionary<string, object>);
                }
                sections[kv.Key] = rows;
            }
        }

        public static IEnumerable<string> SectionNames { get { Load(); return sections.Keys; } }

        public static Dictionary<string, Record> Section(string name)
        {
            Load();
            return sections.TryGetValue(name, out var s) ? s : new Dictionary<string, Record>();
        }

        public static Record Get(string section, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Section(section).TryGetValue(id, out var r) ? r : null;
        }

        /// <summary>Resolve "#Section.Id" (or "Section.Id").</summary>
        public static Record Resolve(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return null;
            var s = reference.StartsWith("#") ? reference.Substring(1) : reference;
            int dot = s.IndexOf('.');
            if (dot < 0) return null;
            return Get(s.Substring(0, dot), s.Substring(dot + 1));
        }

        /// <summary>Strip "#Section." from a reference and return just the id.</summary>
        public static string RefId(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return reference;
            int dot = reference.IndexOf('.');
            return dot >= 0 ? reference.Substring(dot + 1) : reference;
        }

        // Convenience accessors used all over the game
        public static Record Item(string id) => Get("Item", id);
        public static Record Battle => Get("BattleOptions", "Default");
        public static Record World => Get("WorldPhysic", "Default");
        public static Record Tuner => Get("Tuner", "Default");

        /// <summary>Player level for an amount of experience, using the Experience table.</summary>
        public static int LevelForXp(int xp)
        {
            int level = 1;
            foreach (var r in Section("Experience").Values)
            {
                if (int.TryParse(r.Id, out int lv) && xp >= r.Int("Score") && lv > level) level = lv;
            }
            return level;
        }

        public static int XpForLevel(int level)
        {
            var r = Get("Experience", level.ToString(CultureInfo.InvariantCulture));
            return r != null ? r.Int("Score") : int.MaxValue;
        }

        public static int MaxLevel
        {
            get
            {
                int m = 1;
                foreach (var r in Section("Experience").Values) if (int.TryParse(r.Id, out int lv)) m = Mathf.Max(m, lv);
                return m;
            }
        }
    }
}
