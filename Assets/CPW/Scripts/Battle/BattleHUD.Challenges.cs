using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// Active challenges in battle (original BattleHudChallengesElement: one button per chain, swapped to "done" when
    /// its counters complete): a small collapsible list under the top-right buttons with a live progress bar per
    /// chain and a toast when one is reached. The reward itself is still given after the match (ChallengeTracker).
    /// </summary>
    public partial class BattleHUD
    {
        class ChallengeRow { public ChallengeDef def; public Image fill, bg; public Text name, count; public int shown = -1; public bool done; }

        const string ChallengesOpenPref = "cpw.hud.challengesOpen";
        RectTransform challengeBox, challengeRows;
        CanvasGroup challengeGroup;
        Text challengeToggle;
        readonly List<ChallengeRow> challengeList = new List<ChallengeRow>();
        readonly List<ChallengeDef> challengeTmp = new List<ChallengeDef>(4);
        readonly HashSet<string> challengeAnnounced = new HashSet<string>();
        float challengeTimer;
        bool challengesOpen = true;

        void BuildChallenges()
        {
            if (c.Config.mode == BattleMode.Tutorial || !ChallengeTracker.Tracking) return;
            ChallengeTracker.ActiveChallenges(challengeTmp);
            int n = 0;
            foreach (var d in challengeTmp) if (d != null) n++;
            if (n == 0) return;
            try { challengesOpen = PlayerPrefs.GetInt(ChallengesOpenPref, 1) != 0; } catch { challengesOpen = true; }

            challengeBox = UI.Rect(safe, "Challenges");
            UI.Place(challengeBox, new Vector2(1, 1), new Vector2(400, 56 + n * 58 + 10), new Vector2(-20, -146));
            challengeGroup = challengeBox.gameObject.AddComponent<CanvasGroup>();
            var header = TapButton(challengeBox, null, ToggleChallenges, UI.ButtonStyle.Dark);
            UI.Place((RectTransform)header.transform, new Vector2(1, 1), new Vector2(400, 56), Vector2.zero);
            var title = UI.Label(header.transform, Loc.Has("CHALLENGES") ? Loc.T("CHALLENGES") : "Challenges", 30, Color.white, TextAnchor.MiddleLeft, true);
            UI.Stretch(title.rectTransform, 18, 60, 4, 4);
            challengeToggle = UI.Label(header.transform, "", 40, Theme.Primary, TextAnchor.MiddleCenter, true);
            UI.Place(challengeToggle.rectTransform, new Vector2(1, 0.5f), new Vector2(56, 56), new Vector2(-4, 0));

            var rowsBg = UI.Panel(challengeBox, new Color(0, 0, 0, 0.45f), true, "Rows");
            rowsBg.raycastTarget = false;
            challengeRows = rowsBg.rectTransform;
            UI.Place(challengeRows, new Vector2(1, 1), new Vector2(400, n * 58 + 10), new Vector2(0, -60));
            int i = 0;
            foreach (var d in challengeTmp)
            {
                if (d == null) continue;
                var r = new ChallengeRow { def = d };
                r.bg = UI.Panel(challengeRows, new Color(1, 1, 1, 0.06f), true, "Row " + d.id);
                r.bg.raycastTarget = false;
                UI.Place(r.bg.rectTransform, new Vector2(0.5f, 1), new Vector2(384, 52), new Vector2(0, -6 - i * 58));
                var dot = UI.Image(r.bg.transform, UI.Circle, ChallengeCatalog.ChainColors[d.chain % ChallengeCatalog.ChainColors.Length], false, "Chain");
                UI.Place(dot.rectTransform, new Vector2(0, 0.5f), new Vector2(22, 22), new Vector2(8, 4));
                r.name = UI.Label(r.bg.transform, d.name, 24, Color.white, TextAnchor.MiddleLeft, true);
                UI.Anchor(r.name.rectTransform, 0.1f, 0.42f, 0.72f, 1f);
                r.count = UI.Label(r.bg.transform, "", 22, Theme.Primary, TextAnchor.MiddleRight, true);
                UI.Anchor(r.count.rectTransform, 0.66f, 0.42f, 0.97f, 1f);
                r.fill = UI.Bar(r.bg.transform, ChallengeCatalog.ChainColors[d.chain % ChallengeCatalog.ChainColors.Length]);
                r.fill.transform.parent.GetComponent<Image>().raycastTarget = false;
                UI.Anchor((RectTransform)r.fill.transform.parent, 0.1f, 0.1f, 0.97f, 0.42f);
                // already reached before this battle can't happen (completion moves the chain on), but be safe
                r.done = ChallengeTracker.LiveProgress(d) >= d.target;
                if (r.done) challengeAnnounced.Add(d.id);
                challengeList.Add(r);
                i++;
            }
            ApplyChallengesOpen();
            RefreshChallenges(false);
        }

        void ToggleChallenges()
        {
            challengesOpen = !challengesOpen;
            try { PlayerPrefs.SetInt(ChallengesOpenPref, challengesOpen ? 1 : 0); } catch { }
            ApplyChallengesOpen();
        }

        void ApplyChallengesOpen()
        {
            if (challengeRows) challengeRows.gameObject.SetActive(challengesOpen);
            if (challengeToggle) challengeToggle.text = challengesOpen ? "-" : "+";
        }

        void UpdateChallenges(float dt)
        {
            if (challengeBox == null) return;
            // the big turn/intro banners run across the top: fade the box out of their way while one shows
            if (challengeGroup != null)
            {
                float want = bannerRt != null && bannerRt.gameObject.activeSelf ? 0.1f : 1f;
                if (challengeGroup.alpha != want) challengeGroup.alpha = Mathf.MoveTowards(challengeGroup.alpha, want, dt * 5f);
                challengeGroup.blocksRaycasts = want > 0.5f;
            }
            challengeTimer -= dt;
            if (challengeTimer > 0) return;
            challengeTimer = 0.3f;
            bool over = c.CurrentPhase == BattleController.Phase.Over;
            if (over != !challengeBox.gameObject.activeSelf) challengeBox.gameObject.SetActive(!over);
            if (!over) RefreshChallenges(true);
        }

        void RefreshChallenges(bool announce)
        {
            if (!ChallengeTracker.Tracking) return;
            foreach (var r in challengeList)
            {
                int v = Mathf.Min(ChallengeTracker.LiveProgress(r.def), r.def.target);
                if (v == r.shown) continue;
                r.shown = v;
                r.fill.fillAmount = r.def.target > 0 ? Mathf.Clamp01((float)v / r.def.target) : 0f;
                r.count.text = Num(v) + "/" + (r.def.target < 1000 ? Num(r.def.target) : r.def.target.ToString("N0"));
                if (v >= r.def.target && !r.done)
                {
                    r.done = true;
                    r.bg.color = new Color(Theme.Good.r, Theme.Good.g, Theme.Good.b, 0.35f);
                    if (announce && challengeAnnounced.Add(r.def.id))
                    {
                        UI.Toast("Challenge done: " + r.def.name + "!  Reward after the match", MetaUI.Gold);
                        AudioManager.Sfx("AchievementUnlocked", 0.8f);
                    }
                }
            }
        }
    }
}
