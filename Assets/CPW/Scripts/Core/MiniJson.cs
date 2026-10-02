using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CPW
{
    /// <summary>
    /// Small JSON reader/writer. Objects become Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;,
    /// numbers double (or long when integral), plus string, bool and null.
    /// Used for the original game's config, level files and Firebase REST payloads.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var p = new Parser(json);
            return p.ParseValue();
        }

        public static Dictionary<string, object> ParseObject(string json) => Parse(json) as Dictionary<string, object>;

        public static string Serialize(object obj, bool pretty = false)
        {
            var sb = new StringBuilder();
            Write(sb, obj, pretty, 0);
            return sb.ToString();
        }

        sealed class Parser
        {
            readonly string s;
            int i;
            public Parser(string s) { this.s = s; }

            void Ws() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

            public object ParseValue()
            {
                Ws();
                if (i >= s.Length) return null;
                char c = s[i];
                switch (c)
                {
                    case '{': return ParseObj();
                    case '[': return ParseArr();
                    case '"': return ParseStr();
                    case 't': i += 4; return true;
                    case 'f': i += 5; return false;
                    case 'n': i += 4; return null;
                    default: return ParseNum();
                }
            }

            Dictionary<string, object> ParseObj()
            {
                var d = new Dictionary<string, object>();
                i++;
                while (true)
                {
                    Ws();
                    if (i >= s.Length) break;
                    if (s[i] == '}') { i++; break; }
                    if (s[i] == ',') { i++; continue; }
                    string key = ParseStr();
                    Ws();
                    if (i < s.Length && s[i] == ':') i++;
                    d[key] = ParseValue();
                }
                return d;
            }

            List<object> ParseArr()
            {
                var l = new List<object>();
                i++;
                while (true)
                {
                    Ws();
                    if (i >= s.Length) break;
                    if (s[i] == ']') { i++; break; }
                    if (s[i] == ',') { i++; continue; }
                    l.Add(ParseValue());
                }
                return l;
            }

            string ParseStr()
            {
                var sb = new StringBuilder();
                i++; // opening quote
                while (i < s.Length)
                {
                    char c = s[i++];
                    if (c == '"') break;
                    if (c == '\\' && i < s.Length)
                    {
                        char e = s[i++];
                        switch (e)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case 'r': sb.Append('\r'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'u':
                                if (i + 4 <= s.Length)
                                {
                                    sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber));
                                    i += 4;
                                }
                                break;
                            default: sb.Append(e); break;
                        }
                    }
                    else sb.Append(c);
                }
                return sb.ToString();
            }

            object ParseNum()
            {
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                string n = s.Substring(start, i - start);
                if (n.IndexOf('.') < 0 && n.IndexOf('e') < 0 && n.IndexOf('E') < 0 &&
                    long.TryParse(n, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) return l;
                double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out double d);
                return d;
            }
        }

        static void Write(StringBuilder sb, object o, bool pretty, int depth)
        {
            switch (o)
            {
                case null: sb.Append("null"); return;
                case string str: WriteStr(sb, str); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case float f: sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); return;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return;
                case int or long or short or byte or uint or ulong:
                    sb.Append(Convert.ToString(o, CultureInfo.InvariantCulture)); return;
                case IDictionary dict:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (DictionaryEntry e in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        if (pretty) { sb.Append('\n').Append(' ', (depth + 1) * 2); }
                        WriteStr(sb, e.Key.ToString());
                        sb.Append(':');
                        Write(sb, e.Value, pretty, depth + 1);
                    }
                    if (pretty && !first) sb.Append('\n').Append(' ', depth * 2);
                    sb.Append('}');
                    return;
                }
                case IEnumerable list:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (var v in list)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Write(sb, v, pretty, depth + 1);
                    }
                    sb.Append(']');
                    return;
                }
                default:
                    WriteStr(sb, o.ToString()); return;
            }
        }

        static void WriteStr(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
