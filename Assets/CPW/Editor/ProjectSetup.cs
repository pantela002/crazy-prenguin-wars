using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_6000_0_OR_NEWER
using UnityEditor.Build;
#endif

namespace CPW.EditorTools
{
    /// <summary>
    /// One-click project configuration for Android/iOS (company, bundle id, orientation, IL2CPP ARM64, SDK levels,
    /// icon, Gamma color space, the Main scene in the build list...).
    /// Runs automatically the first time the project is opened on a machine (and whenever the bundle id is not ours,
    /// e.g. after a fresh clone), and from the menu "CPW > Apply Project Settings". Safe to run again any time.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        public const string Company = "pantela002";
        public const string Product = "Crazy Penguin Wars";
        public const string BundleId = "com.pantela002.crazypenguinwars";
        public const string Version = "1.0.0";
        public const string ScenePath = "Assets/Scenes/Main.unity";
        public const string IconPath = "Assets/CPW/Resources/Icons/Ui/app_icon.png";
        const int SetupVersion = 1;   // bump to re-run the automatic setup after changing this file

        static string PrefKey => "CPW.ProjectSetup." + SetupVersion + "." + Application.dataPath.GetHashCode();

        static ProjectSetup()
        {
            // Wait until the editor finished loading; asset/scene operations are not allowed during the domain reload.
            EditorApplication.delayCall += AutoApply;
        }

        static void AutoApply()
        {
            bool done = EditorPrefs.GetBool(PrefKey, false);
            bool ours = PlayerSettings.applicationIdentifier == BundleId && PlayerSettings.productName == Product;
            if (done && ours) return;
            try
            {
                Apply();
                EditorPrefs.SetBool(PrefKey, true);
                Debug.Log("CPW: project settings applied (menu CPW > Apply Project Settings runs this again).");
            }
            catch (Exception e)
            {
                Debug.LogWarning("CPW: automatic project setup failed: " + e.Message + "\nUse CPW > Apply Project Settings.");
            }
        }

        [MenuItem("CPW/Apply Project Settings", priority = 1)]
        public static void ApplyFromMenu()
        {
            Apply();
            EditorPrefs.SetBool(PrefKey, true);
            EditorUtility.DisplayDialog("Crazy Penguin Wars", "Project settings applied.\n\nBundle id: " + BundleId +
                "\nScene: " + ScenePath + "\n\nIf Unity asks to restart (input handling changed), click Yes.", "OK");
        }

        /// <summary>Apply every setting. Called by the build scripts too, so CI builds always use the right settings.</summary>
        public static void Apply()
        {
            // --- identity
            PlayerSettings.companyName = Company;
            PlayerSettings.productName = Product;
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.Android.bundleVersionCode = Math.Max(1, PlayerSettings.Android.bundleVersionCode);
            if (string.IsNullOrEmpty(PlayerSettings.iOS.buildNumber) || PlayerSettings.iOS.buildNumber == "0")
                PlayerSettings.iOS.buildNumber = "1";

            // --- orientation: landscape only
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            // --- rendering / code
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            PlayerSettings.stripEngineCode = false;     // components are added from code, keep them all
            PlayerSettings.runInBackground = false;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto; // highest installed
            PlayerSettings.iOS.targetOSVersionString = "15.0";   // Xcode 26+ rejects anything below 15
            PlayerSettings.iOS.requiresFullScreen = true;

#if UNITY_6000_0_OR_NEWER
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, BundleId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            // Minimal never strips our own assembly (menus are found by reflection).
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Minimal);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.iOS, ManagedStrippingLevel.Minimal);
#else
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, BundleId);
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, BundleId);
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Android, ManagedStrippingLevel.Low);
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.iOS, ManagedStrippingLevel.Low);
#endif

            UseLegacyInput();
            ApplyIcon();
            EnsureScene();
            AssetDatabase.SaveAssets();
        }

        /// <summary>The game reads touches/keys with the classic Input class: make sure "Active Input Handling" allows it.</summary>
        static void UseLegacyInput()
        {
            try
            {
                var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
                foreach (var a in assets)
                {
                    if (!(a is PlayerSettings)) continue;
                    var so = new SerializedObject(a);
                    var prop = so.FindProperty("activeInputHandler");
                    // 0 = Input Manager (old), 1 = Input System (new), 2 = Both
                    if (prop != null && prop.intValue == 1)
                    {
                        prop.intValue = 0;
                        so.ApplyModifiedProperties();
                        Debug.LogWarning("CPW: 'Active Input Handling' set to Input Manager (Old). Restart Unity for it to take effect.");
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("CPW: could not check Active Input Handling: " + e.Message); }
        }

        static void ApplyIcon()
        {
            if (!File.Exists(IconPath)) return;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (tex == null) return;
#if UNITY_6000_0_OR_NEWER
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { tex }, IconKind.Any);
#else
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { tex });
#endif
        }

        /// <summary>Create Assets/Scenes/Main.unity (empty: GameManager builds everything from code) and put it first in the build list.</summary>
        static void EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                var active = SceneManager.GetActiveScene();
                bool canReplace = Application.isBatchMode || (string.IsNullOrEmpty(active.path) && !active.isDirty);
                if (canReplace)
                {
                    var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    EditorSceneManager.SaveScene(scene, ScenePath);
                }
                else
                {
                    try
                    {
                        // Don't disturb the scene the user has open: create it next to it, save, and close it again.
                        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                        EditorSceneManager.SaveScene(scene, ScenePath);
                        EditorSceneManager.CloseScene(scene, true);
                    }
                    catch (Exception)
                    {
                        // (Unity refuses additive scenes next to an unsaved untitled scene.) An empty scene file works too.
                        File.WriteAllText(ScenePath, "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
                    }
                }
                AssetDatabase.ImportAsset(ScenePath);
            }

            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length > 0 && scenes[0].path == ScenePath && scenes[0].enabled) return;
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            foreach (var s in scenes) if (s.path != ScenePath) list.Add(s);
            EditorBuildSettings.scenes = list.ToArray();
        }

        [MenuItem("CPW/Open Main Scene", priority = 2)]
        public static void OpenMainScene()
        {
            EnsureScene();
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
