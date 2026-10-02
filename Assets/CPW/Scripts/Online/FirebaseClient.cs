using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace CPW
{
    /// <summary>Result of one REST call. Never throws; check ok / error.</summary>
    public sealed class FbResponse
    {
        public bool ok;
        public long code;          // HTTP status (0 = no connection)
        public string text = "";   // raw body
        public string error = "";  // human readable error when !ok

        object parsed; bool didParse;
        /// <summary>The body parsed with MiniJson (Dictionary / List / primitive / null).</summary>
        public object Json
        {
            get
            {
                if (!didParse) { didParse = true; try { parsed = MiniJson.Parse(text); } catch { parsed = null; } }
                return parsed;
            }
        }
        public Dictionary<string, object> Obj => Json as Dictionary<string, object>;
    }

    /// <summary>Helpers to read MiniJson values safely.</summary>
    public static class Fb
    {
        public static object ServerTime => new Dictionary<string, object> { { ".sv", "timestamp" } };

        public static Dictionary<string, object> Dict(object o) => o as Dictionary<string, object>;
        public static Dictionary<string, object> Dict(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) ? v as Dictionary<string, object> : null;
        public static List<object> List(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) ? v as List<object> : null;

        public static string Str(Dictionary<string, object> d, string k, string def = "") =>
            d != null && d.TryGetValue(k, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : def;

        public static double Num(object v, double def = 0)
        {
            switch (v)
            {
                case long l: return l;
                case double dd: return dd;
                case int i: return i;
                case float f: return f;
                case string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var r): return r;
                default: return def;
            }
        }

        public static double Num(Dictionary<string, object> d, string k, double def = 0) =>
            d != null && d.TryGetValue(k, out var v) ? Num(v, def) : def;

        public static int Int(Dictionary<string, object> d, string k, int def = 0) => (int)Math.Round(Num(d, k, def));
        public static long Long(Dictionary<string, object> d, string k, long def = 0) => (long)Math.Round(Num(d, k, def));
        public static bool Bool(Dictionary<string, object> d, string k, bool def = false) =>
            d != null && d.TryGetValue(k, out var v) && v is bool b ? b : def;

        /// <summary>RTDB turns arrays into objects when keys are sparse; accept both.</summary>
        public static List<object> Items(object o)
        {
            if (o is List<object> l) return l;
            var res = new List<object>();
            if (o is Dictionary<string, object> d)
            {
                var keys = new List<string>(d.Keys);
                keys.Sort(string.CompareOrdinal);
                foreach (var k in keys) res.Add(d[k]);
            }
            return res;
        }

        public static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>Quote a value for a REST query parameter (orderBy="xp").</summary>
        public static string Q(string s) => UnityWebRequest.EscapeURL("\"" + s + "\"");
    }

    /// <summary>
    /// Firebase over plain HTTPS (no SDK): anonymous Auth (Identity Toolkit + Secure Token) and Realtime Database REST.
    /// Lives on a DontDestroyOnLoad GameObject and runs requests as coroutines. All callbacks happen on the main thread.
    /// </summary>
    public class FirebaseClient : MonoBehaviour
    {
        public static FirebaseClient I { get; private set; }

        public FirebaseConfig Config { get; private set; }
        public string Uid { get; private set; } = "";
        public bool SignedIn => !string.IsNullOrEmpty(Uid) && !string.IsNullOrEmpty(refreshToken);
        public string LastError { get; private set; } = "";
        /// <summary>Server clock minus local clock (ms), learned from server timestamps we write.</summary>
        public long ServerOffsetMs { get; set; }
        public long ServerNowMs => Fb.NowMs + ServerOffsetMs;
        /// <summary>Called every frame (unscaled delta) so services can run timers without their own MonoBehaviour.</summary>
        public event Action<float> Updated;

        const float TimeoutSeconds = 12f;
        string idToken = "", refreshToken = "";
        long idTokenExpiresMs;
        bool authBusy;
        readonly List<Action<bool>> authWaiters = new List<Action<bool>>();

        // PlayerPrefs keys (prefixed per project so switching projects signs in again)
        string K(string n) => "cpw_fb_" + Config.projectId + "_" + n;

        public static FirebaseClient Create(FirebaseConfig cfg)
        {
            if (I != null) return I;
            var go = new GameObject("FirebaseClient");
            DontDestroyOnLoad(go);
            I = go.AddComponent<FirebaseClient>();
            I.Config = cfg;
            I.LoadTokens();
            return I;
        }

        void Update()
        {
            var u = Updated;
            if (u == null) return;
            try { u(Time.unscaledDeltaTime); }
            catch (Exception e) { Debug.LogException(e); }
        }

        void LoadTokens()
        {
            Uid = PlayerPrefs.GetString(K("uid"), "");
            refreshToken = PlayerPrefs.GetString(K("refresh"), "");
            idToken = PlayerPrefs.GetString(K("id"), "");
            long.TryParse(PlayerPrefs.GetString(K("exp"), "0"), out idTokenExpiresMs);
        }

        void SaveTokens()
        {
            PlayerPrefs.SetString(K("uid"), Uid);
            PlayerPrefs.SetString(K("refresh"), refreshToken);
            PlayerPrefs.SetString(K("id"), idToken);
            PlayerPrefs.SetString(K("exp"), idTokenExpiresMs.ToString(CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
        }

        /// <summary>Forget the anonymous account on this device (a new one is created next time).</summary>
        public void SignOut()
        {
            Uid = idToken = refreshToken = "";
            idTokenExpiresMs = 0;
            SaveTokens();
        }

        // ------------------------------------------------------------------ auth

        /// <summary>Make sure we have a valid ID token: refresh it or sign up anonymously. done(false) on failure (see LastError).</summary>
        public void EnsureAuth(Action<bool> done)
        {
            if (!string.IsNullOrEmpty(idToken) && Fb.NowMs < idTokenExpiresMs - 60000) { done?.Invoke(true); return; }
            if (done != null) authWaiters.Add(done);
            if (authBusy) return;
            authBusy = true;
            StartCoroutine(AuthRoutine());
        }

        IEnumerator AuthRoutine()
        {
            bool ok = false;
            if (!string.IsNullOrEmpty(refreshToken))
            {
                yield return RefreshRoutine(r => ok = r);
                // A refresh token can be revoked (user deleted in the console): fall back to a new account.
                if (!ok && lastAuthWasRejected) refreshToken = "";
            }
            if (!ok && string.IsNullOrEmpty(refreshToken))
                yield return SignUpRoutine(r => ok = r);
            authBusy = false;
            var waiters = authWaiters.ToArray();
            authWaiters.Clear();
            foreach (var w in waiters)
            {
                try { w(ok); } catch (Exception e) { Debug.LogException(e); }
            }
        }

        bool lastAuthWasRejected;

        IEnumerator SignUpRoutine(Action<bool> done)
        {
            string url = "https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=" + UnityWebRequest.EscapeURL(Config.apiKey);
            FbResponse res = null;
            yield return Send("POST", url, "{\"returnSecureToken\":true}", "application/json", r => res = r);
            var d = res.Obj;
            if (!res.ok || d == null || !d.ContainsKey("idToken"))
            {
                LastError = "Sign-in failed: " + AuthError(res);
                done(false);
                yield break;
            }
            idToken = Fb.Str(d, "idToken");
            refreshToken = Fb.Str(d, "refreshToken");
            Uid = Fb.Str(d, "localId");
            idTokenExpiresMs = Fb.NowMs + (long)(Fb.Num(d, "expiresIn", 3600) * 1000);
            SaveTokens();
            LastError = "";
            done(true);
        }

        IEnumerator RefreshRoutine(Action<bool> done)
        {
            lastAuthWasRejected = false;
            string url = "https://securetoken.googleapis.com/v1/token?key=" + UnityWebRequest.EscapeURL(Config.apiKey);
            string body = "grant_type=refresh_token&refresh_token=" + UnityWebRequest.EscapeURL(refreshToken);
            FbResponse res = null;
            yield return Send("POST", url, body, "application/x-www-form-urlencoded", r => res = r);
            var d = res.Obj;
            if (!res.ok || d == null || !d.ContainsKey("id_token"))
            {
                // 400 = token revoked / user deleted / project changed; other codes = network trouble.
                lastAuthWasRejected = res.code == 400;
                LastError = "Token refresh failed: " + AuthError(res);
                done(false);
                yield break;
            }
            idToken = Fb.Str(d, "id_token");
            refreshToken = Fb.Str(d, "refresh_token", refreshToken);
            var uid = Fb.Str(d, "user_id");
            if (!string.IsNullOrEmpty(uid)) Uid = uid;
            idTokenExpiresMs = Fb.NowMs + (long)(Fb.Num(d, "expires_in", 3600) * 1000);
            SaveTokens();
            LastError = "";
            done(true);
        }

        static string AuthError(FbResponse r)
        {
            if (r == null) return "no response";
            var err = Fb.Dict(r.Obj, "error");
            var msg = Fb.Str(err, "message");
            if (msg == "ADMIN_ONLY_OPERATION" || msg.StartsWith("OPERATION_NOT_ALLOWED"))
                return "Anonymous sign-in is not enabled in the Firebase console (Authentication > Sign-in method).";
            if (msg.StartsWith("API key not valid") || msg.Contains("API_KEY_INVALID"))
                return "the apiKey in firebase_config.json is wrong.";
            return string.IsNullOrEmpty(msg) ? r.error : msg;
        }

        // ------------------------------------------------------------------ database

        /// <summary>
        /// Realtime Database REST call. path like "users/abc/profile" (no .json), query without '?' (e.g. orderBy=...),
        /// body is serialized with MiniJson (or sent as-is when it is already a string starting with { or [).
        /// Retries once with a fresh token on 401.
        /// </summary>
        public void Db(string method, string path, object body, Action<FbResponse> done, string query = null)
        {
            StartCoroutine(DbRoutine(method, path, body, query, done));
        }

        public void Get(string path, Action<FbResponse> done, string query = null) => Db("GET", path, null, done, query);
        public void Put(string path, object body, Action<FbResponse> done = null) => Db("PUT", path, body, done);
        public void Patch(string path, object body, Action<FbResponse> done = null) => Db("PATCH", path, body, done);
        public void Delete(string path, Action<FbResponse> done = null) => Db("DELETE", path, null, done);

        IEnumerator DbRoutine(string method, string path, object body, string query, Action<FbResponse> done)
        {
            string json = body == null ? null : (body is string s && (s.StartsWith("{") || s.StartsWith("[")) ? s : MiniJson.Serialize(body));
            FbResponse res = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool authOk = false, authDone = false;
                EnsureAuth(ok => { authOk = ok; authDone = true; });
                while (!authDone) yield return null;
                if (!authOk)
                {
                    res = new FbResponse { ok = false, code = 0, error = LastError };
                    break;
                }
                string url = Config.DbRoot + "/" + path.Trim('/') + ".json?auth=" + UnityWebRequest.EscapeURL(idToken) +
                             (string.IsNullOrEmpty(query) ? "" : "&" + query);
                yield return Send(method, url, json, "application/json", r => res = r);
                if (res.code == 401 && attempt == 0)
                {
                    // token expired or revoked: force a refresh and retry once
                    var err = Fb.Str(res.Obj, "error");
                    if (err.IndexOf("expired", StringComparison.OrdinalIgnoreCase) >= 0 || err.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        idTokenExpiresMs = 0;
                        continue;
                    }
                    res.error = "Permission denied (check the database rules from Docs/firebase/database.rules.json)";
                }
                break;
            }
            if (!res.ok && string.IsNullOrEmpty(res.error)) res.error = "HTTP " + res.code;
            if (done != null)
            {
                try { done(res); } catch (Exception e) { Debug.LogException(e); }
            }
        }

        IEnumerator Send(string method, string url, string body, string contentType, Action<FbResponse> done)
        {
            var res = new FbResponse();
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                res.error = "No internet connection";
                done(res);
                yield break;
            }
            using (var req = new UnityWebRequest(url, method))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                if (body != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    req.SetRequestHeader("Content-Type", contentType);
                }
                req.timeout = (int)TimeoutSeconds;
                yield return req.SendWebRequest();
                res.code = req.responseCode;
                res.text = req.downloadHandler != null ? (req.downloadHandler.text ?? "") : "";
                res.ok = req.result == UnityWebRequest.Result.Success && res.code >= 200 && res.code < 300;
                if (!res.ok)
                {
                    if (req.result == UnityWebRequest.Result.ConnectionError) res.error = "Can't reach Firebase (" + req.error + ")";
                    else
                    {
                        var e = res.Obj != null && res.Obj.TryGetValue("error", out var ev) ? ev : null;
                        res.error = e is string es ? es : (req.error ?? ("HTTP " + res.code));
                    }
                }
            }
            done(res);
        }

        /// <summary>Run an action after a delay (unscaled), e.g. deferred cleanup.</summary>
        public void After(float seconds, Action a) => StartCoroutine(AfterRoutine(seconds, a));

        IEnumerator AfterRoutine(float s, Action a)
        {
            yield return new WaitForSecondsRealtime(s);
            try { a?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>Update ServerOffsetMs from a timestamp the server just wrote (the PUT response of {".sv":"timestamp"}).</summary>
        public void LearnServerTime(object serverMs)
        {
            double v = Fb.Num(serverMs, 0);
            if (v > 1e12) ServerOffsetMs = (long)v - Fb.NowMs;
        }

        /// <summary>A random id that sorts by creation time (like Firebase push ids).</summary>
        public static string NewId()
        {
            const string chars = "0123456789abcdefghijklmnopqrstuvwxyz";
            var sb = new StringBuilder("m");
            sb.Append(Fb.NowMs.ToString("x"));
            var rng = new System.Random(Guid.NewGuid().GetHashCode());
            for (int i = 0; i < 8; i++) sb.Append(chars[rng.Next(chars.Length)]);
            return sb.ToString();
        }
    }
}
