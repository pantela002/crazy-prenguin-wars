using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// Shared bar at the top of menu screens: level badge with XP bar, coins, cash (fish) with "+" to the bank,
    /// VIP badge and settings. Shown when the current screen has ShowTopBar; values tick up smoothly.
    /// </summary>
    public class TopBar : MonoBehaviour
    {
        static TopBar inst;
        RectTransform bar;
        Text levelText, xpText, coinText, cashText, nameText, vipText;
        Image xpFill, vipBg;
        float shownCoins, shownCash, shownXp01;
        int lastCoins = -1, lastCash = -1, lastLevel = -1, lastXp = -1;
        bool lastVip;
        float xpTarget;   // cached so Update doesn't allocate

        public static void Install()
        {
            if (inst != null || UI.Safe == null) return;
            var go = new GameObject("TopBarHost");
            go.transform.SetParent(UI.Safe.parent, false);
            inst = go.AddComponent<TopBar>();
            inst.Build();
            ScreenManager.ScreenShown += inst.OnScreenShown;
            ProfileService.Changed += inst.OnProfileChanged;
            inst.bar.gameObject.SetActive(ScreenManager.I != null && ScreenManager.I.Current != null && ScreenManager.I.Current.ShowTopBar);
        }

        void OnDestroy()
        {
            ScreenManager.ScreenShown -= OnScreenShown;
            ProfileService.Changed -= OnProfileChanged;
        }

        void Build()
        {
            bar = UI.Rect(UI.Safe, "TopBar");
            bar.anchorMin = new Vector2(0, 1); bar.anchorMax = new Vector2(1, 1); bar.pivot = new Vector2(0.5f, 1);
            bar.sizeDelta = new Vector2(0, MetaUI.TopBarHeight);
            bar.anchoredPosition = Vector2.zero;
            // keep it above screens but below popups (popups live in UI.PopupLayer, a sibling of Safe)
            bar.SetAsLastSibling();

            var strip = UI.Panel(bar, new Color(0.04f, 0.14f, 0.32f, 0.82f), false, "Strip");
            UI.Stretch(strip.rectTransform, 0, 0, 0, 10);
            strip.raycastTarget = false;

            // ---- level + xp (left) ----
            var lvl = UI.Button(bar, null, () => ScreenManager.Show(() => new ProfileScreen()), UI.ButtonStyle.Plain, 30, "Level");
            var lrt = (RectTransform)lvl.transform;
            UI.Place(lrt, new Vector2(0, 0.5f), new Vector2(560, 96), new Vector2(14, 4));
            lvl.GetComponent<Image>().color = new Color(1, 1, 1, 0.0f);
            var star = UI.Image(lrt, UI.Circle, Theme.Xp, false, "Star");
            UI.Place(star.rectTransform, new Vector2(0, 0.5f), new Vector2(96, 96), Vector2.zero);
            levelText = UI.Label(star.transform, "1", 46, Color.white, TextAnchor.MiddleCenter, true);
            UI.Stretch(levelText.rectTransform, 4, 4, 4, 4);
            nameText = UI.Label(lrt, "", 30, Color.white, TextAnchor.UpperLeft, true);
            UI.Anchor(nameText.rectTransform, 0.2f, 0.5f, 1, 1);
            nameText.rectTransform.offsetMin = new Vector2(0, 0);
            var barHost = UI.Rect(lrt, "XpBar");
            UI.Anchor(barHost, 0.2f, 0.06f, 0.98f, 0.48f);
            xpFill = UI.Bar(barHost, Theme.Xp);
            UI.Stretch((RectTransform)xpFill.transform.parent);
            xpText = UI.Label(barHost, "", 24, Color.white, TextAnchor.MiddleCenter, true);
            UI.Stretch(xpText.rectTransform, 4, 4, 2, 2);

            // ---- money (center) ----
            coinText = Money(bar, "coin", new Vector2(-150, 4), Theme.Coin);
            cashText = Money(bar, "cash", new Vector2(250, 4), Theme.Cash);

            // ---- vip + settings (right) ----
            var set = UI.IconButton(bar, ModelLibrary.Icon("Ui/settings"), null, () => ScreenManager.Show(() => new SettingsScreen()), UI.ButtonStyle.Secondary);
            UI.Place((RectTransform)set.transform, new Vector2(1, 0.5f), new Vector2(96, 96), new Vector2(-14, 4));
            if (ModelLibrary.Icon("Ui/settings") == null)
            {
                var g = UI.Label(set.transform, "SET", 30, Color.white, TextAnchor.MiddleCenter, true);
                UI.Stretch(g.rectTransform);
            }
            var vip = UI.Button(bar, null, () => ScreenManager.Show(() => new VipScreen()), UI.ButtonStyle.Dark, 30, "VIP");
            UI.Place((RectTransform)vip.transform, new Vector2(1, 0.5f), new Vector2(170, 86), new Vector2(-126, 4));
            vipBg = vip.GetComponent<Image>();
            var vipIcon = ModelLibrary.Icon("Ui/vip");
            if (vipIcon != null)
            {
                var vi = UI.Image(vip.transform, vipIcon);
                UI.Anchor(vi.rectTransform, 0.04f, 0.1f, 0.42f, 0.9f);
            }
            vipText = UI.Label(vip.transform, "VIP", 36, Color.white, TextAnchor.MiddleCenter, true);
            UI.Anchor(vipText.rectTransform, vipIcon != null ? 0.4f : 0.05f, 0, 0.95f, 1);

            shownCoins = ProfileService.P.coins;
            shownCash = ProfileService.P.cash;
            Refresh(true);
        }

        Text Money(RectTransform parent, string kind, Vector2 pos, Color c)
        {
            var bg = UI.Panel(parent, new Color(0, 0, 0, 0.35f), true, "Money " + kind);
            UI.Place(bg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(330, 80), pos);
            var ic = MetaUI.CurrencyIcon(bg.transform, kind);
            UI.Place(ic, new Vector2(0, 0.5f), new Vector2(84, 84), new Vector2(-14, 0));
            var t = UI.Label(bg.transform, "0", 42, Color.white, TextAnchor.MiddleCenter, true);
            UI.Stretch(t.rectTransform, 74, 70, 2, 2);
            var plus = UI.Button(bg.transform, "+", () => ScreenManager.Show(() => new BankScreen(kind == "cash" ? 1 : 0)), UI.ButtonStyle.Good, 44, "Plus");
            UI.Place((RectTransform)plus.transform, new Vector2(1, 0.5f), new Vector2(66, 66), new Vector2(-7, 0));
            return t;
        }

        void OnScreenShown(UIScreen s)
        {
            bool show = s != null && s.ShowTopBar;
            bar.gameObject.SetActive(show);
            if (show) { bar.SetAsLastSibling(); Refresh(true); }
        }

        void OnProfileChanged() => Refresh(false);

        void Refresh(bool snap)
        {
            var P = ProfileService.P;
            bool vip = Progression.IsVip;
            if (P.level != lastLevel || P.xp != lastXp)
            {
                lastLevel = P.level; lastXp = P.xp;
                xpTarget = Progression.LevelProgress(P.xp, P.level);
                levelText.text = P.level.ToString();
                int next = GameData.XpForLevel(P.level + 1);
                xpText.text = next == int.MaxValue ? "MAX" : P.xp.ToString("N0") + " / " + next.ToString("N0");
            }
            nameText.text = P.displayName;
            if (vip != lastVip || snap)
            {
                lastVip = vip;
                vipBg.color = vip ? MetaUI.Gold : Theme.PanelDark;
                vipText.color = vip ? Theme.PrimaryText : Color.white;
                vipText.text = vip ? "VIP" : "VIP?";
            }
            if (snap)
            {
                shownCoins = P.coins; shownCash = P.cash;
                shownXp01 = Progression.LevelProgress(P.xp, P.level);
                xpFill.fillAmount = shownXp01;
                SetMoney();
            }
        }

        void SetMoney()
        {
            int c = Mathf.RoundToInt(shownCoins), f = Mathf.RoundToInt(shownCash);
            if (c != lastCoins) { lastCoins = c; coinText.text = UI.Money(c); }
            if (f != lastCash) { lastCash = f; cashText.text = UI.Money(f); }
        }

        void Update()
        {
            if (bar == null || !bar.gameObject.activeSelf) return;
            var P = ProfileService.P;
            float dt = Time.unscaledDeltaTime;
            // count up/down toward the real values
            shownCoins = Mathf.MoveTowards(shownCoins, P.coins, Mathf.Max(30f, Mathf.Abs(P.coins - shownCoins) * 4f) * dt);
            shownCash = Mathf.MoveTowards(shownCash, P.cash, Mathf.Max(8f, Mathf.Abs(P.cash - shownCash) * 4f) * dt);
            float target = xpTarget;
            if (target < shownXp01 - 0.001f) shownXp01 = 0;
            shownXp01 = Mathf.MoveTowards(shownXp01, target, dt);
            xpFill.fillAmount = shownXp01;
            SetMoney();
        }
    }
}
