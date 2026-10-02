using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Idle hints (original HelpHud + BattleManager HelpHudStartMove/StartShoot after BattleOptions.IdleTimeForHints):
    /// when the local player does nothing for that long on their own turn, the walk and fire buttons pulse (only
    /// walk once the shot is fired, for the retreat). Any touch stops it. The tutorial has its own highlights.
    /// </summary>
    public partial class BattleHUD
    {
        float idleTime;
        int idleTurn = -1;
        bool idleHintOn;

        void UpdateIdleHints()
        {
            bool eligible = c.Tutorial == null && c.IsInputTurn && !AnyPanelOpen && c.CurrentPhase == BattleController.Phase.Turn;
            bool touched = Input.touchCount > 0 || Input.GetMouseButton(0) || Input.anyKey;
            if (!eligible || touched || c.TurnNumber != idleTurn || (c.Active != null && c.Active.Walking))
            {
                idleTime = 0;
                idleTurn = c.TurnNumber;
                if (idleHintOn) SetIdleHint(false);
                return;
            }
            idleTime += Time.deltaTime;
            if (!idleHintOn && idleTime >= BattleRules.IdleTimeForHints) SetIdleHint(true);
            // after firing only the retreat makes sense
            if (idleHintOn && c.Fired && highlights["fire"].on) highlights["fire"].on = false;
        }

        void SetIdleHint(bool on)
        {
            idleHintOn = on;
            highlights["walk"].on = on;
            highlights["walk2"].on = on;
            highlights["fire"].on = on && !c.Fired && c.AttacksLeft > 0;
        }
    }
}
