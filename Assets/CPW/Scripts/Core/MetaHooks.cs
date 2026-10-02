using System;

namespace CPW
{
    /// <summary>
    /// Connects GameManager to the menu screens (Meta/) without hard references, so each part can be built separately.
    /// Meta code assigns these in MetaRegistry.Install().
    /// </summary>
    public static class MetaHooks
    {
        public static Func<UIScreen> HomeScreen = () => new PlaceholderScreen("Home");
        public static Func<BattleResult, UIScreen> ResultsScreen = r => new PlaceholderScreen("Results");
        public static Action<BattleResult> ApplyRewards = r => { };
        static bool installed;

        public static void Install()
        {
            if (installed) return;
            installed = true;
            // Meta/MetaRegistry.cs provides the real screens.
            var t = Type.GetType("CPW.MetaRegistry");
            t?.GetMethod("Install")?.Invoke(null, null);
        }
    }

    public class PlaceholderScreen : UIScreen
    {
        readonly string title;
        public PlaceholderScreen(string title) { this.title = title; }
        public override void Build()
        {
            var l = UI.Label(Root, title, 80, Theme.TextLight, UnityEngine.TextAnchor.MiddleCenter, true);
            UI.Stretch(l.rectTransform);
        }
    }
}
