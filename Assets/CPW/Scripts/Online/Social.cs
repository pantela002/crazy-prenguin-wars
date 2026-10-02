using System;
using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>A friend in the friend list (users/{me}/friends/{uid} + their public card players/{uid}).</summary>
    public class FriendInfo
    {
        public string uid, name;
        public int level;
        public bool online;
        public string lastGiftDay = "";   // yyyyMMdd (UTC) of the last gift we sent them
    }

    /// <summary>One inbox message (inbox/{me}/{id}).</summary>
    public class InboxItem
    {
        public string id, type, from, fromName, item, code, match;
        public int amount;
        public long at;
        public bool IsGift => type == "gift";
        public bool IsInvite => type == "invite";
        public bool IsFriend => type == "friend";
    }

    /// <summary>
    /// Social features of the original Facebook game mapped onto Firebase anonymous accounts: a friend code instead of
    /// Facebook friends (FriendsElementScreen / NeighborsScreen), one free gift per friend per day from the Gift section
    /// (GiftScreen / GiftManager), an inbox with gifts, private game invites and friend notices (InboxScreen), and
    /// presence (online dot). Nothing here works offline; callers get an error string ("offline") instead.
    ///
    /// Data: players/{uid} {name, level, code, seen}, friendCodes/{CODE} {uid}, users/{uid}/friends/{f} {name, added, gift},
    /// inbox/{uid}/{id} {type, from, fromName, at, item, amount, code, match}.
    /// Inbox ids: gifts "g_{from}_{yyyyMMdd}" (the rules allow one per sender per day), invites "i_{from}_{code}",
    /// friend notices "f_{from}".
    /// </summary>
    public static class Social
    {
        public const long OnlineWindowMs = 150000;      // "online" = app open in the last 2.5 minutes
        public const long InviteLifetimeMs = 30 * 60000; // invites older than 30 minutes are dropped (rooms are long gone)
        const int CodeLength = 6;

        static bool installed, codeBusy;
        static string code = "", codeUid = "";
        static readonly List<Action<string>> codeWaiters = new List<Action<string>>();

        /// <summary>Messages waiting in the inbox (from the last poll; 0 offline).</summary>
        public static int InboxCount { get; private set; }

        static FirebaseClient Client => Online.Service.Available ? FirebaseClient.I : null;
        public static bool Available => Client != null;
        static string Me => Client != null ? Client.Uid : "";
        static string Clip(string s, int n) => string.IsNullOrEmpty(s) ? "Penguin" : (s.Length > n ? s.Substring(0, n) : s);

        public static void Install()
        {
            if (installed) return;
            installed = true;
            var go = new GameObject("SocialRunner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<SocialRunner>();
        }

        /// <summary>Once a minute while online (SocialRunner): presence, the inbox badge and the league settlement.</summary>
        internal static void Heartbeat()
        {
            if (!Available) return;
            Presence();
            RefreshInboxCount(null);
            League.TrySettle();
        }

        // ------------------------------------------------------------------ friend code / presence

        /// <summary>Our friend code (reserved on first use). done(null) offline.</summary>
        public static void GetMyCode(Action<string> done)
        {
            var fb = Client;
            if (fb == null) { done?.Invoke(null); return; }
            string key = "cpw_friend_code_" + fb.Uid;
            if (codeUid != fb.Uid) { codeUid = fb.Uid; code = PlayerPrefs.GetString(key, ""); }   // the anonymous account can change
            if (!string.IsNullOrEmpty(code)) { done?.Invoke(code); return; }
            if (done != null) codeWaiters.Add(done);
            if (codeBusy) return;
            codeBusy = true;
            fb.Get("players/" + fb.Uid + "/code", r =>
            {
                var existing = r.ok ? r.Json as string : null;
                if (!string.IsNullOrEmpty(existing)) { CodeReady(key, existing); return; }
                if (!r.ok) { CodeReady(key, null); return; }
                ReserveCode(fb, key, 0);
            });
        }

        static void ReserveCode(FirebaseClient fb, string key, int attempt)
        {
            var rng = new System.Random(Guid.NewGuid().GetHashCode());
            var chars = new char[CodeLength];
            for (int i = 0; i < chars.Length; i++) chars[i] = FirebaseService.CodeChars[rng.Next(FirebaseService.CodeChars.Length)];
            var c = new string(chars);
            // The rules refuse a code somebody else owns, so a failed write just means "try another one".
            fb.Put("friendCodes/" + c, new Dictionary<string, object> { { "uid", fb.Uid } }, r =>
            {
                if (r.ok) { CodeReady(key, c); return; }
                if (attempt < 5 && r.code != 0) ReserveCode(fb, key, attempt + 1);
                else CodeReady(key, null);
            });
        }

        static void CodeReady(string key, string c)
        {
            codeBusy = false;
            if (!string.IsNullOrEmpty(c)) { code = c; PlayerPrefs.SetString(key, c); PlayerPrefs.Save(); }
            var w = codeWaiters.ToArray();
            codeWaiters.Clear();
            foreach (var a in w) { try { a(string.IsNullOrEmpty(c) ? null : c); } catch (Exception e) { Debug.LogException(e); } }
        }

        /// <summary>Publish our card (name, level, friend code) and "seen now" for the online dot.</summary>
        public static void Presence()
        {
            var fb = Client;
            if (fb == null) return;
            GetMyCode(c => { if (!string.IsNullOrEmpty(c)) PutPresence(c); });
        }

        static void PutPresence(string myCode)
        {
            var fb = Client;
            if (fb == null) return;
            var P = ProfileService.P;
            fb.Put("players/" + fb.Uid, new Dictionary<string, object>
            {
                { "name", Clip(P.displayName, 32) },
                { "level", P.level },
                { "code", myCode },
                { "seen", Fb.ServerTime },
            }, r => { if (r.ok) fb.LearnServerTime(Fb.Num(r.Obj, "seen")); });
        }

        // ------------------------------------------------------------------ friends

        public static void LoadFriends(Action<List<FriendInfo>, string> done)
        {
            var fb = Client;
            var list = new List<FriendInfo>();
            if (fb == null) { done?.Invoke(list, "offline"); return; }
            fb.Get("users/" + fb.Uid + "/friends", r =>
            {
                if (!r.ok) { done?.Invoke(list, r.error); return; }
                if (r.Obj == null || r.Obj.Count == 0) { done?.Invoke(list, null); return; }
                foreach (var kv in r.Obj)
                {
                    var d = Fb.Dict(kv.Value);
                    list.Add(new FriendInfo { uid = kv.Key, name = Fb.Str(d, "name", "Penguin"), lastGiftDay = Fb.Str(d, "gift") });
                }
                // Their public cards for the current name, level and online state.
                int pending = list.Count;
                long now = fb.ServerNowMs;
                foreach (var f in list)
                {
                    var fr = f;
                    fb.Get("players/" + fr.uid, pr =>
                    {
                        var d = pr.ok ? pr.Obj : null;
                        if (d != null)
                        {
                            fr.name = Fb.Str(d, "name", fr.name);
                            fr.level = Fb.Int(d, "level", 1);
                            fr.online = now - Fb.Long(d, "seen") < OnlineWindowMs;
                        }
                        if (--pending > 0) return;
                        list.Sort((a, b) => a.online != b.online ? (a.online ? -1 : 1) : string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
                        done?.Invoke(list, null);
                    });
                }
            });
        }

        /// <summary>Add a friend by their friend code. done(friend, null) or done(null, reason).</summary>
        public static void AddFriendByCode(string friendCode, Action<FriendInfo, string> done)
        {
            var fb = Client;
            if (fb == null) { done?.Invoke(null, "Connect to the internet to add friends."); return; }
            var c = (friendCode ?? "").Trim().ToUpperInvariant();
            if (c.Length < 4 || c.Length > 8) { done?.Invoke(null, "Friend codes have " + CodeLength + " letters."); return; }
            if (c == code) { done?.Invoke(null, "That's your own code!"); return; }
            fb.Get("friendCodes/" + c, r =>
            {
                if (!r.ok) { done?.Invoke(null, "Could not look up the code: " + r.error); return; }
                var uid = Fb.Str(r.Obj, "uid");
                if (string.IsNullOrEmpty(uid)) { done?.Invoke(null, "Nobody has the code " + c + "."); return; }
                if (uid == fb.Uid) { done?.Invoke(null, "That's your own code!"); return; }
                AddFriend(uid, null, true, done);
            });
        }

        /// <summary>Add uid to our list (name read from their card) and optionally tell them with a friend notice.</summary>
        public static void AddFriend(string uid, string knownName, bool notify, Action<FriendInfo, string> done)
        {
            var fb = Client;
            if (fb == null) { done?.Invoke(null, "offline"); return; }
            fb.Get("players/" + uid, pr =>
            {
                var d = pr.ok ? pr.Obj : null;
                var f = new FriendInfo
                {
                    uid = uid,
                    name = Fb.Str(d, "name", string.IsNullOrEmpty(knownName) ? "Penguin" : knownName),
                    level = Fb.Int(d, "level", 1),
                    online = d != null && fb.ServerNowMs - Fb.Long(d, "seen") < OnlineWindowMs,
                };
                var body = new Dictionary<string, object> { { "name", Clip(f.name, 32) }, { "added", Fb.ServerTime } };
                fb.Patch("users/" + fb.Uid + "/friends/" + uid, body, r =>
                {
                    if (!r.ok) { done?.Invoke(null, "Could not add the friend: " + r.error); return; }
                    if (notify) Send(uid, "f_" + fb.Uid, "friend", null, null);   // refused (harmless) while an older notice is unread
                    done?.Invoke(f, null);
                });
            });
        }

        public static void RemoveFriend(string uid, Action<bool> done)
        {
            var fb = Client;
            if (fb == null) { done?.Invoke(false); return; }
            fb.Delete("users/" + fb.Uid + "/friends/" + uid, r => done?.Invoke(r.ok));
        }

        // ------------------------------------------------------------------ gifts

        /// <summary>
        /// Items that can be sent as gifts: the Gift section rows that name an Item (the shipped config has only a
        /// hidden placeholder), otherwise basic ammo. Amounts are capped on receipt, so a modified client can't send more.
        /// </summary>
        public static List<ItemStack> GiftPool()
        {
            var res = new List<ItemStack>();
            foreach (var r in GameData.Section("Gift").Values)
            {
                string id = GameData.Item(r.Id) != null ? r.Id : null;
                if (id == null) continue;
                res.Add(new ItemStack(id, Mathf.Clamp(r.Int("Amount", 1), 1, 5)));
            }
            if (res.Count == 0)
            {
                // invented fallback pool (cheap starter ammo)
                foreach (var s in new[] { new ItemStack("BasicNuke", 3), new ItemStack("Grenade", 2), new ItemStack("Pistol", 3), new ItemStack("Shotgun", 2), new ItemStack("ClusterRocket", 1) })
                    if (GameData.Item(s.id) != null) res.Add(s);
            }
            return res;
        }

        static int GiftCap(string itemId)
        {
            foreach (var s in GiftPool()) if (s.id == itemId) return s.amount;
            return 0;
        }

        public static bool CanGiftToday(FriendInfo f) => f != null && f.lastGiftDay != Periods.Today;

        public static void SendGift(FriendInfo f, ItemStack gift, Action<string> done)
        {
            var fb = Client;
            if (fb == null || f == null || gift == null) { done?.Invoke("Connect to the internet to send gifts."); return; }
            if (!CanGiftToday(f)) { done?.Invoke("You already sent " + f.name + " a gift today."); return; }
            string today = Periods.Today;
            Send(f.uid, "g_" + fb.Uid + "_" + today, "gift", new Dictionary<string, object> { { "item", gift.id }, { "amount", gift.amount } }, err =>
            {
                if (err != null) { done?.Invoke(err.Contains("ermission") ? "You already sent " + f.name + " a gift today." : err); return; }
                f.lastGiftDay = today;
                fb.Patch("users/" + fb.Uid + "/friends/" + f.uid, new Dictionary<string, object> { { "name", Clip(f.name, 32) }, { "gift", today } });
                done?.Invoke(null);
            });
        }

        /// <summary>Take a gift from the inbox: removed first, so it can never be collected twice.</summary>
        public static void AcceptGift(InboxItem it, Action<string> done)
        {
            var fb = Client;
            if (fb == null || it == null) { done?.Invoke("offline"); return; }
            fb.Delete("inbox/" + fb.Uid + "/" + it.id, r =>
            {
                if (!r.ok) { done?.Invoke("Could not open the gift: " + r.error); return; }
                InboxCount = Mathf.Max(0, InboxCount - 1);
                int n = Mathf.Min(it.amount, GiftCap(it.item));
                if (n <= 0) { done?.Invoke("That gift can't be opened in this version."); return; }
                Progression.GiveItem(it.item, n);
                ProfileService.Save();
                done?.Invoke(null);
            });
        }

        // ------------------------------------------------------------------ invites

        public static void SendInvite(FriendInfo f, string joinCode, string matchId, Action<string> done)
        {
            var fb = Client;
            if (fb == null || f == null || string.IsNullOrEmpty(joinCode)) { done?.Invoke("offline"); return; }
            // The rules refuse to overwrite an unread message, so "permission denied" means they already have this invite.
            Send(f.uid, "i_" + fb.Uid + "_" + joinCode, "invite", new Dictionary<string, object> { { "code", joinCode }, { "match", matchId ?? "" } },
                err => done?.Invoke(err != null && err.Contains("ermission") ? null : err));
        }

        // ------------------------------------------------------------------ inbox

        static void Send(string toUid, string id, string type, Dictionary<string, object> extra, Action<string> done)
        {
            var fb = Client;
            if (fb == null) { done?.Invoke("offline"); return; }
            var body = new Dictionary<string, object>
            {
                { "type", type },
                { "from", fb.Uid },
                { "fromName", Clip(ProfileService.P.displayName, 32) },
                { "at", Fb.ServerTime },
            };
            if (extra != null) foreach (var kv in extra) body[kv.Key] = kv.Value;
            fb.Put("inbox/" + toUid + "/" + id, body, r => done?.Invoke(r.ok ? null : r.error));
        }

        public static void LoadInbox(Action<List<InboxItem>, string> done)
        {
            var fb = Client;
            var list = new List<InboxItem>();
            if (fb == null) { done?.Invoke(list, "offline"); return; }
            fb.Get("inbox/" + fb.Uid, r =>
            {
                if (!r.ok) { done?.Invoke(list, r.error); return; }
                long now = fb.ServerNowMs;
                if (r.Obj != null)
                    foreach (var kv in r.Obj)
                    {
                        var d = Fb.Dict(kv.Value);
                        var it = d == null ? null : new InboxItem
                        {
                            id = kv.Key,
                            type = Fb.Str(d, "type"),
                            from = Fb.Str(d, "from"),
                            fromName = Fb.Str(d, "fromName", "Penguin"),
                            item = Fb.Str(d, "item"),
                            amount = Fb.Int(d, "amount"),
                            code = Fb.Str(d, "code"),
                            match = Fb.Str(d, "match"),
                            at = Fb.Long(d, "at"),
                        };
                        if (it == null || (it.IsInvite && now - it.at > InviteLifetimeMs) || !(it.IsGift || it.IsInvite || it.IsFriend))
                        {
                            fb.Delete("inbox/" + fb.Uid + "/" + kv.Key);
                            continue;
                        }
                        list.Add(it);
                    }
                list.Sort((a, b) => b.at.CompareTo(a.at));
                InboxCount = list.Count;
                done?.Invoke(list, null);
            });
        }

        public static void Dismiss(InboxItem it, Action<bool> done = null)
        {
            var fb = Client;
            if (fb == null || it == null) { done?.Invoke(false); return; }
            fb.Delete("inbox/" + fb.Uid + "/" + it.id, r =>
            {
                if (r.ok) InboxCount = Mathf.Max(0, InboxCount - 1);
                done?.Invoke(r.ok);
            });
        }

        /// <summary>Count inbox messages (keys only) for the badge on the home screen.</summary>
        public static void RefreshInboxCount(Action<int> done)
        {
            var fb = Client;
            if (fb == null) { InboxCount = 0; done?.Invoke(0); return; }
            fb.Get("inbox/" + fb.Uid, r =>
            {
                if (r.ok) InboxCount = r.Obj != null ? r.Obj.Count : 0;
                done?.Invoke(InboxCount);
            }, "shallow=true");
        }
    }

    /// <summary>Runs Social.Heartbeat a few seconds after start, every minute, and when the app comes back.</summary>
    public sealed class SocialRunner : MonoBehaviour
    {
        const float Interval = 60f;
        float timer = 4f;

        void Update()
        {
            if ((timer -= Time.unscaledDeltaTime) > 0) return;
            timer = Interval;
            Social.Heartbeat();
        }

        void OnApplicationPause(bool paused) { if (!paused) timer = Mathf.Min(timer, 1f); }
    }
}
