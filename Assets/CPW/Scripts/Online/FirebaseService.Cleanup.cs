using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Housekeeping without a server (the free Spark plan has no Cloud Functions): matches are normally deleted by
    /// their players (FirebaseBattleNetwork.Close, CancelMatchmaking), but a match whose players all closed the app
    /// stays behind. Every device sweeps a few of those now and then: the rules let any signed-in player list the
    /// oldest matches (orderBy "created", limitToFirst at most 3) and delete those created more than 3 hours ago.
    /// </summary>
    public sealed partial class FirebaseService
    {
        const long AbandonedAfterMs = 3L * 60 * 60 * 1000;   // must match "now - 10800000" in database.rules.json
        const int SweepBatch = 3;                             // must match "query.limitToFirst <= 3"
        const double SweepEveryHours = 6;
        const string SweepPrefKey = "cpw_fb_last_sweep";

        /// <summary>Delete up to 3 abandoned matches (with their lobby entry and join code). At most every 6 hours per device.</summary>
        void SweepAbandoned()
        {
            if (!Available) return;
            long nowMs = fb.ServerNowMs;
            long.TryParse(PlayerPrefs.GetString(SweepPrefKey, "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long last);
            if (nowMs - last < SweepEveryHours * 3600000 && last <= nowMs) return;
            PlayerPrefs.SetString(SweepPrefKey, nowMs.ToString(CultureInfo.InvariantCulture));
            long cutoff = nowMs - AbandonedAfterMs;
            fb.Get("matches", r =>
            {
                if (!r.ok || r.Obj == null) return;
                foreach (var kv in r.Obj)
                {
                    var m = Fb.Dict(kv.Value);
                    // the query already filters; double check so a wrong clock can't delete a live match
                    if (m == null || Fb.Long(m, "created") <= 0 || Fb.Long(m, "created") > cutoff) continue;
                    string id = kv.Key, code = Fb.Str(m, "code");
                    // Lobby entries and codes of a dead host are stale by now, so the rules allow removing them too.
                    fb.Delete("lobby/" + id);
                    if (!string.IsNullOrEmpty(code))
                        fb.Get("codes/" + code, g => { if (g.ok && Fb.Str(g.Obj, "match") == id) fb.Delete("codes/" + code); });   // not reused since
                    fb.Delete("matches/" + id);
                }
            }, "orderBy=" + Fb.Q("created") + "&endAt=" + cutoff.ToString(CultureInfo.InvariantCulture) + "&limitToFirst=" + SweepBatch);
        }
    }
}
