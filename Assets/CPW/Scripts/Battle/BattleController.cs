using System;
using UnityEngine;

namespace CPW
{
    /// <summary>Starts and ends battles. The battle systems (terrain, penguins, weapons, HUD) hang off the object it creates.</summary>
    public partial class BattleController : MonoBehaviour
    {
        public static BattleController I { get; private set; }
        public BattleConfig Config { get; private set; }
        Action<BattleResult> onEnd;

        public static void Begin(BattleConfig config, Action<BattleResult> onEnd)
        {
            if (I != null) { I.Cleanup(); Destroy(I.gameObject); }
            var go = new GameObject("Battle");
            I = go.AddComponent<BattleController>();
            I.Config = config;
            I.onEnd = onEnd;
            I.Setup();
        }

        partial void SetupImpl();

        void Setup() => SetupImpl();

        /// <summary>Finish the battle and hand the result back to GameManager. The battle objects are destroyed.</summary>
        public void Finish(BattleResult result)
        {
            var cb = onEnd;
            onEnd = null;
            if (I == this) I = null;
            Cleanup();
            Destroy(gameObject);
            cb?.Invoke(result);
        }
    }
}
