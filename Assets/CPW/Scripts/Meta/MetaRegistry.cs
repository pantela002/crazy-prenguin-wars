namespace CPW
{
    /// <summary>
    /// Entry point of the menu code. GameManager (via MetaHooks.Install) calls Install() by reflection at boot.
    /// </summary>
    public static class MetaRegistry
    {
        static bool installed;

        public static void Install()
        {
            if (installed) return;
            installed = true;
            MetaHooks.HomeScreen = () => SplashScreen.Shown ? (UIScreen)new HomeScreen() : new SplashScreen();
            MetaHooks.ResultsScreen = r => new ResultsScreen(r);
            MetaHooks.ApplyRewards = RewardService.Apply;
            ChallengeTracker.Install();
            PlayerStatsTracker.Install();   // leaderboard periods + weekly league points
            Social.Install();               // presence, inbox badge, league settlement while online
            TopBar.Install();
        }
    }
}
