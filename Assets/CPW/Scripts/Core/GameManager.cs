using System;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Entry point. Created automatically when any scene loads, so pressing Play in an empty scene runs the game.
    /// Owns the menu camera, UI, audio and the switch between menus and battles.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager I { get; private set; }
        public Camera MenuCamera { get; private set; }
        public bool InBattle { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I != null) return;
            var go = new GameObject("CPW");
            DontDestroyOnLoad(go);
            I = go.AddComponent<GameManager>();
        }

        void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.orientation = ScreenOrientation.AutoRotation;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Input.multiTouchEnabled = true;

            GameData.Load();
            ProfileService.Load();
            ApplyQuality(ProfileService.P.quality);

            // Disable cameras that came with the opened scene; the game makes its own.
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.gameObject.SetActive(false);

            var camGo = new GameObject("MenuCamera");
            camGo.transform.SetParent(transform, false);
            MenuCamera = camGo.AddComponent<Camera>();
            MenuCamera.clearFlags = CameraClearFlags.SolidColor;
            MenuCamera.backgroundColor = Theme.Bg;
            MenuCamera.orthographic = false;
            MenuCamera.fieldOfView = 40;
            MenuCamera.transform.position = new Vector3(0, 1.2f, -10);
            camGo.AddComponent<AudioListener>();

            AudioManager.Create(transform);
            UI.Init(transform);
            ScreenManager.Create(transform);
            MetaHooks.Install();
        }

        void Start()
        {
            Online.Service.Init(ok =>
            {
                if (ok) Online.Service.PullProfile(remote =>
                {
                    if (remote != null && remote.lastSavedUnixMs > ProfileService.P.lastSavedUnixMs) ProfileService.Replace(remote);
                });
            });
            ProfileService.Saved += p => { if (Online.Service.Available) Online.Service.PushProfile(p); };
            GoHome();
        }

        public static void ApplyQuality(int q)
        {
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = q >= 2 ? 4 : (q == 1 ? 2 : 0);
            Application.targetFrameRate = 60;
        }

        /// <summary>Show the home screen (or the first-time tutorial offer).</summary>
        public static void GoHome()
        {
            if (I == null) return;
            I.MenuCamera.gameObject.SetActive(true);
            ScreenManager.Reset(MetaHooks.HomeScreen);
        }

        /// <summary>Start a battle. Menus are hidden; when it ends, rewards are applied and the results screen shows.</summary>
        public static void StartBattle(BattleConfig config)
        {
            if (I == null || I.InBattle || config == null) return;
            I.InBattle = true;
            ScreenManager.HideAll();
            I.MenuCamera.gameObject.SetActive(false);
            BattleEvents.RaiseBattleStarted(config);
            try
            {
                BattleController.Begin(config, OnBattleEnded);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                I.InBattle = false;
                // BattleStarted was already raised: pair it with an aborted BattleEnded
                try { BattleEvents.RaiseBattleEnded(new BattleResult { config = config, aborted = true }); }
                catch (Exception e2) { Debug.LogException(e2); }
                GoHome();
                UI.Message("Oops", "The battle could not start.\n" + e.Message);
            }
        }

        static void OnBattleEnded(BattleResult result)
        {
            I.InBattle = false;
            I.MenuCamera.gameObject.SetActive(true);
            BattleEvents.RaiseBattleEnded(result);
            if (result == null || result.aborted) { GoHome(); return; }
            try { MetaHooks.ApplyRewards(result); }
            catch (Exception e) { Debug.LogException(e); }
            ScreenManager.Reset(() => MetaHooks.ResultsScreen(result));
        }

        void OnApplicationPause(bool paused) { if (paused) ProfileService.Save(); }
        void OnApplicationQuit() { ProfileService.Save(); }
    }
}
