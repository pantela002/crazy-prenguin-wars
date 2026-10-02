using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// Booster button cooldown (original IconCoolDownButton with BattleOptions.BoosterCooldown): after a booster is
    /// used a dark radial wipe drains over BoosterCooldown seconds. The remake's rule stays one booster per turn,
    /// so the button only comes back on the next turn; the wipe just shows that it was spent.
    /// </summary>
    public partial class BattleHUD
    {
        Image boosterCd;
        float boosterCdLeft;
        int boosterCdTurn = -1;

        void BuildBoosterCooldown()
        {
            boosterCd = UI.Image(boosterBtn.transform, UI.WhiteSprite, new Color(0, 0, 0, 0.55f), false, "Cooldown");
            boosterCd.type = Image.Type.Filled;
            boosterCd.fillMethod = Image.FillMethod.Radial360;
            boosterCd.fillOrigin = (int)Image.Origin360.Top;
            boosterCd.fillClockwise = false;
            UI.Stretch(boosterCd.rectTransform, 6, 6, 6, 6);
            boosterCd.fillAmount = 0;
            boosterCd.enabled = false;
        }

        void OnBoosterUsed(int player, string item)
        {
            if (c == null || player != c.ActiveIndex || !c.IsLocalHuman(player)) return;
            boosterCdLeft = BattleRules.BoosterCooldown;
            boosterCdTurn = c.TurnNumber;
        }

        void UpdateBoosterCooldown()
        {
            if (boosterCd == null) return;
            // a new turn ends the visual (the once-per-turn rule is what really gates the button)
            if (boosterCdLeft > 0 && (c.TurnNumber != boosterCdTurn || !c.BoosterUsedThisTurn)) boosterCdLeft = 0;
            if (boosterCdLeft > 0) boosterCdLeft = Mathf.Max(0, boosterCdLeft - Time.deltaTime);
            float f = boosterCdLeft > 0 ? boosterCdLeft / BattleRules.BoosterCooldown : 0f;
            bool show = f > 0.001f;
            if (boosterCd.enabled != show) boosterCd.enabled = show;
            if (show) boosterCd.fillAmount = f;
        }
    }
}
