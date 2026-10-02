using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// Coins/XP earned this match (original RewardsHandler.generateGraphicsToPickUp + FeedbackItem): a coin and an
    /// XP icon pop out of the damaged target, wait a moment, then fly into the earnings counter under the scoreboard,
    /// which counts up as they land. Only the local player's own rewards, and only in battles that give rewards.
    /// </summary>
    public partial class BattleHUD
    {
        class Pickup
        {
            public RectTransform rt;
            public bool coin;
            public int value;
            public Vector2 world;          // where it popped out (follows the camera until it flies)
            public Vector2 offset;         // landing offset of the pop, canvas units
            public float arc, scale, t, flyFromT;
            public Vector2 flyFrom;
        }

        RectTransform earnings, pickupLayer;
        RectTransform coinTarget, xpTarget;
        Text coinText, xpText;
        readonly List<Pickup> pickups = new List<Pickup>();
        int inFlightCoins, inFlightXp, shownCoins = -1, shownXp = -1;
        float coinPop, xpPop, pickupSfxAt;

        void BuildEarnings()
        {
            pickupLayer = UI.Stretch(UI.Rect(safe, "Pickups"));
            if (!c.RewardsEnabled || c.PassAndPlay) return;
            float boardH = 20 + c.Penguins.Count * RowPitch;   // under the players panel
            var bg = UI.Panel(safe, new Color(0, 0, 0, 0.45f), true, "Earnings");
            bg.raycastTarget = false;
            earnings = bg.rectTransform;
            UI.Place(earnings, new Vector2(0, 1), new Vector2(440, 64), new Vector2(20, -20 - boardH - 10));
            coinTarget = MetaUI.CurrencyIcon(earnings, "coin");
            UI.Place(coinTarget, new Vector2(0, 0.5f), new Vector2(46, 46), new Vector2(14, 0));
            coinText = UI.Label(earnings, "0", 34, Theme.Coin, TextAnchor.MiddleLeft, true);
            UI.Place(coinText.rectTransform, new Vector2(0, 0.5f), new Vector2(150, 56), new Vector2(68, 0));
            xpTarget = MetaUI.CurrencyIcon(earnings, "xp");
            UI.Place(xpTarget, new Vector2(0, 0.5f), new Vector2(46, 46), new Vector2(230, 0));
            xpText = UI.Label(earnings, "0", 34, Theme.Xp, TextAnchor.MiddleLeft, true);
            UI.Place(xpText.rectTransform, new Vector2(0, 0.5f), new Vector2(150, 56), new Vector2(284, 0));
            foreach (var g in earnings.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
        }

        /// <summary>Called by the controller when the local player earned coins/XP from damage at a world point.</summary>
        public void ShowRewardPickups(Vector2 world, int coins, int xp)
        {
            if (earnings == null || c.CurrentPhase == BattleController.Phase.Over) return;
            if (coins > 0) SpawnPickup(world, true, coins);
            if (xp > 0) SpawnPickup(world, false, xp);
        }

        void SpawnPickup(Vector2 world, bool coin, int value)
        {
            if (pickups.Count >= 24) { if (coin) shownCoins = -1; else shownXp = -1; return; }   // flood: just count it
            var icon = MetaUI.CurrencyIcon(pickupLayer, coin ? "coin" : "xp");
            icon.sizeDelta = new Vector2(64, 64);
            foreach (var g in icon.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            float side = Random.value < 0.5f ? -1f : 1f;
            var p = new Pickup
            {
                rt = icon, coin = coin, value = value, world = world,
                // FeedbackItem: sideways 40..80 px, a hop of 400..600 px "height factor", bigger for bigger rewards
                offset = new Vector2(side * Random.Range(40f, 90f) + (coin ? -20f : 20f), Random.Range(-40f, -10f)),
                arc = Random.Range(120f, 200f),
                scale = 1f + Mathf.Min(value, 50) / 50f * 0.8f,
            };
            if (coin) inFlightCoins += value; else inFlightXp += value;
            pickups.Add(p);
            PlacePickup(p);
        }

        Vector2 ScreenToLayer(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(pickupLayer, screen, null, out var lp);
            return lp;
        }

        Vector2 TargetInLayer(RectTransform target)
        {
            return ScreenToLayer(RectTransformUtility.WorldToScreenPoint(null, target.position));
        }

        /// <summary>Position for the current phase: pop (arc), wait (bob), fly (ease in toward the counter).</summary>
        bool PlacePickup(Pickup p)
        {
            float ta = BattleRules.PickupAppear, tw = BattleRules.PickupWait, tf = BattleRules.PickupFly;
            Vector2 basePos = ScreenToLayer(cam != null ? cam.WorldToScreen(p.world) : Vector2.zero);
            float s = p.scale;
            if (p.t < ta + tw)
            {
                float k = Mathf.Clamp01(p.t / Mathf.Max(0.01f, ta));
                float ek = 1f - (1f - k) * (1f - k);
                Vector2 pos = basePos + new Vector2(p.offset.x * ek, p.offset.y * ek + p.arc * 4f * k * (1f - k));
                if (p.t >= ta) pos.y += Mathf.Sin((p.t - ta) * 5f) * 6f;
                p.rt.anchoredPosition = pos;
                p.flyFrom = pos;
                s *= Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(k * 3f));
            }
            else
            {
                float k = Mathf.Clamp01((p.t - ta - tw) / Mathf.Max(0.01f, tf));
                float ek = k * k;
                var target = TargetInLayer(p.coin ? coinTarget : xpTarget);
                p.rt.anchoredPosition = Vector2.Lerp(p.flyFrom, target, ek);
                s *= 1f - Mathf.Max(0, k - 0.2f) * 0.75f;   // shrink while flying, like SHRINK_ICON_WHEN_FLYING
                if (k >= 1f) return true;
            }
            p.rt.localScale = new Vector3(s, s, 1);
            return false;
        }

        void UpdateEarnings(float dt)
        {
            if (earnings == null) return;
            bool over = c.CurrentPhase == BattleController.Phase.Over;
            for (int i = pickups.Count - 1; i >= 0; i--)
            {
                var p = pickups[i];
                p.t += dt;
                if (over || !p.rt || PlacePickup(p))
                {
                    if (p.coin) { inFlightCoins -= p.value; coinPop = 1f; } else { inFlightXp -= p.value; xpPop = 1f; }
                    if (!over && Time.unscaledTime - pickupSfxAt > 0.15f)
                    {
                        pickupSfxAt = Time.unscaledTime;
                        AudioManager.Sfx(p.coin ? "GetCoins" : "GetExp", 0.5f);
                    }
                    if (p.rt) Destroy(p.rt.gameObject);
                    pickups.RemoveAt(i);
                }
            }
            if (pickups.Count == 0) { inFlightCoins = 0; inFlightXp = 0; }

            var v = c.ViewPenguin;
            int coins = v != null ? Mathf.Max(0, v.Coins - inFlightCoins) : 0;
            int xp = v != null ? Mathf.Max(0, v.Xp - inFlightXp) : 0;
            if (coins != shownCoins) { shownCoins = coins; coinText.text = Num(coins); }
            if (xp != shownXp) { shownXp = xp; xpText.text = Num(xp); }
            if (coinPop > 0) { coinPop = Mathf.Max(0, coinPop - dt * 4f); coinTarget.localScale = Vector3.one * (1f + coinPop * 0.35f); }
            if (xpPop > 0) { xpPop = Mathf.Max(0, xpPop - dt * 4f); xpTarget.localScale = Vector3.one * (1f + xpPop * 0.35f); }
        }
    }
}
