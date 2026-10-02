using UnityEngine;

namespace CPW
{
    /// <summary>Wires the extra HUD elements (each in its own BattleHUD.*.cs) into Build/Update.</summary>
    public partial class BattleHUD
    {
        void BuildFeatures()
        {
            BuildEarnings();
            BuildChallenges();
            BuildChat();
            BattleEvents.BoosterUsed += OnBoosterUsed;
        }

        void UpdateFeatures(float dt)
        {
            UpdateBoosterCooldown();
            UpdateIdleHints();
            UpdateEarnings(dt);
            UpdateChallenges(dt);
            UpdateChat(dt);
        }

        void OnDestroy()
        {
            BattleEvents.BoosterUsed -= OnBoosterUsed;
            UnhookChat();
        }
    }
}
