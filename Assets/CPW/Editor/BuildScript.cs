using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds for phones, from the menu (CPW > Build ...) or the command line / CI:
/// <code>Unity -batchmode -quit -projectPath . -executeMethod BuildScript.BuildAndroidApk</code>
/// Outputs go to Builds/ (or to -customBuildPath when GameCI passes one).
/// Optional environment variables for a signed Android build (needed for Google Play):
/// CPW_KEYSTORE_PATH, CPW_KEYSTORE_PASS, CPW_KEY_ALIAS, CPW_KEY_PASS. For iOS: CPW_IOS_TEAM_ID.
/// Lives in the global namespace so -executeMethod BuildScript.X works as written.
/// </summary>
public static class BuildScript
{
    const string Name = "CrazyPenguinWars";

    [MenuItem("CPW/Build Android APK", priority = 20)]
    public static void BuildAndroidApk() => BuildAndroid(false);

    [MenuItem("CPW/Build Android App Bundle (AAB)", priority = 21)]
    public static void BuildAndroidAab() => BuildAndroid(true);

    [MenuItem("CPW/Build iOS (Xcode project)", priority = 22)]
    public static void BuildIOS()
    {
        CPW.EditorTools.ProjectSetup.Apply();
        var team = Environment.GetEnvironmentVariable("CPW_IOS_TEAM_ID");
        if (!string.IsNullOrEmpty(team))
        {
            PlayerSettings.iOS.appleDeveloperTeamID = team;
            PlayerSettings.iOS.appleEnableAutomaticSigning = true;
        }
        // The output is a folder with Unity-iPhone.xcodeproj; open it in Xcode on a Mac to sign and run.
        string path = OutputPath("Builds/iOS", null);
        Build(BuildTarget.iOS, BuildTargetGroup.iOS, path);
    }

    static void BuildAndroid(bool appBundle)
    {
        CPW.EditorTools.ProjectSetup.Apply();
        EditorUserBuildSettings.buildAppBundle = appBundle;
        ConfigureKeystore();
        string ext = appBundle ? ".aab" : ".apk";
        string path = OutputPath("Builds/Android/" + Name + "-" + PlayerSettings.bundleVersion + ext, ext);
        Build(BuildTarget.Android, BuildTargetGroup.Android, path);
    }

    /// <summary>Use a release keystore when the environment provides one; otherwise Unity signs with a debug key.</summary>
    static void ConfigureKeystore()
    {
        var ks = Environment.GetEnvironmentVariable("CPW_KEYSTORE_PATH");
        if (string.IsNullOrEmpty(ks) || !File.Exists(ks))
        {
            PlayerSettings.Android.useCustomKeystore = false;
            return;
        }
        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystoreName = Path.GetFullPath(ks);
        PlayerSettings.Android.keystorePass = Environment.GetEnvironmentVariable("CPW_KEYSTORE_PASS") ?? "";
        PlayerSettings.Android.keyaliasName = Environment.GetEnvironmentVariable("CPW_KEY_ALIAS") ?? "";
        PlayerSettings.Android.keyaliasPass = Environment.GetEnvironmentVariable("CPW_KEY_PASS") ?? "";
    }

    /// <summary>Default path, unless the command line has -customBuildPath (GameCI) to use instead.</summary>
    static string OutputPath(string def, string requiredExt)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "-customBuildPath") continue;
            var p = args[i + 1];
            if (requiredExt != null && !p.EndsWith(requiredExt, StringComparison.OrdinalIgnoreCase))
                p = Path.ChangeExtension(p, requiredExt);
            return p;
        }
        return def;
    }

    static void Build(BuildTarget target, BuildTargetGroup group, string path)
    {
        if (EditorUserBuildSettings.activeBuildTarget != target)
            EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0) scenes = new[] { CPW.EditorTools.ProjectSetup.ScenePath };

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = path,
            target = target,
            targetGroup = group,
            options = BuildOptions.None,
        };
        Debug.Log("CPW: building " + target + " to " + path);
        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;
        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log("CPW: build succeeded (" + (summary.totalSize / (1024 * 1024)) + " MB): " + Path.GetFullPath(path));
            if (!Application.isBatchMode) EditorUtility.RevealInFinder(path);
        }
        else
        {
            var msg = "CPW: build failed: " + summary.result + " (" + summary.totalErrors + " errors). See the Console.";
            Debug.LogError(msg);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else EditorUtility.DisplayDialog("Build failed", msg, "OK");
        }
    }
}
