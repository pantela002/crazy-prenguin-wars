# Building Crazy Penguin Wars

This page explains, step by step, how to run the game in the Unity editor and put it on an Android phone or an
iPhone, either from your own computer or with GitHub Actions. No previous Unity experience is assumed.

- [1. Install Unity](#1-install-unity)
- [2. Open the project](#2-open-the-project)
- [3. Play in the editor](#3-play-in-the-editor)
- [4. Android](#4-android)
- [5. iPhone / iPad](#5-iphone--ipad)
- [6. Build with GitHub Actions](#6-build-with-github-actions)
- [7. Troubleshooting](#7-troubleshooting)

---

## 1. Install Unity

1. Download **Unity Hub** from <https://unity.com/download> and install it. Sign in (a free Unity account is fine)
   and accept the free **Personal** license when the Hub asks.
2. Look up the exact editor version the project uses in `ProjectSettings/ProjectVersion.txt`
   (for example `6000.0.58f2`, a Unity 6 LTS release).
3. In Unity Hub open **Installs > Install Editor**. Pick that version (if it is not listed, use the
   [download archive](https://unity.com/releases/editor/archive) and click *Install* next to it, which opens the Hub).
   Any newer **Unity 6000.0.x LTS** also works; Unity will offer to upgrade the project.
4. When the Hub asks for **modules**, tick:
   - **Android Build Support** with both sub-items **OpenJDK** and **Android SDK & NDK Tools** (for Android).
   - **iOS Build Support** (for iPhone; only useful on a Mac, but harmless elsewhere).
   - Optional: *Microsoft Visual Studio Community* (Windows) or nothing (Mac/Linux) as a code editor.

   You can add modules later: Hub > Installs > the gear icon next to the version > **Add modules**.

## 2. Open the project

1. Get the code: on GitHub click **Code > Download ZIP** and unzip it, or `git clone` the repository.
2. Unity Hub > **Projects > Add > Add project from disk** and select the folder that contains `Assets/`,
   `Packages/` and `ProjectSettings/`.
3. Click the project to open it. The first import takes a few minutes (Unity converts all models, sounds and textures).
4. On first open, the editor script `Assets/CPW/Editor/ProjectSetup.cs` sets everything up automatically:
   company `pantela002`, product name *Crazy Penguin Wars*, bundle id `com.pantela002.crazypenguinwars`,
   version 1.0.0, landscape only, IL2CPP + ARM64, Android 7.0 (API 24) minimum, iOS 13 minimum, the app icon,
   and the scene `Assets/Scenes/Main.unity` in the build list.
   You can run it again any time with the menu **CPW > Apply Project Settings**.
   If Unity asks to restart because *Active Input Handling* changed, click **Yes**.

## 3. Play in the editor

1. Menu **CPW > Open Main Scene** (or double-click `Assets/Scenes/Main.unity`). The scene is empty on purpose:
   the game builds all menus and battles from code when it starts.
2. In the **Game** view tab, choose a landscape resolution in the aspect drop-down (e.g. *1920x1080* or
   *Full HD Landscape*) so the layout looks like a phone.
3. Press the **Play** button (top center). Mouse clicks act as touches. **Esc** = the Android back button.
4. Press Play again to stop. Your profile is saved in the editor's PlayerPrefs.

## 4. Android

### Option A: Build And Run over USB (easiest)

1. On the phone: **Settings > About phone**, tap **Build number** seven times to unlock developer options. Then
   **Settings > System > Developer options**, switch on **USB debugging**.
2. Connect the phone with a USB cable and accept the *Allow USB debugging?* question on the phone.
3. In Unity: **File > Build Profiles** (Unity 6; older: *Build Settings*), select **Android**, click **Switch Platform**
   and wait.
4. Choose your phone in **Run Device** (click *Refresh* if it is not listed).
5. Click **Build And Run**, choose a file name (e.g. `Builds/Android/CrazyPenguinWars.apk`). Unity builds, installs
   and starts the game on the phone. The first build takes 5-15 minutes; later ones are faster.

### Option B: build an APK and install it yourself

1. Menu **CPW > Build Android APK**. The file appears in `Builds/Android/CrazyPenguinWars-1.0.0.apk`
   (the folder opens when it is done).
2. Copy the APK to the phone (USB, Google Drive, e-mail to yourself...), open it on the phone and allow
   *Install unknown apps* for the app you opened it with.
   Or with the Android platform tools: `adb install -r Builds/Android/CrazyPenguinWars-1.0.0.apk`.

### Google Play (optional)

Google Play wants an **AAB** signed with your own key:
1. Create a keystore once: **Edit > Project Settings > Player > Android > Publishing Settings > Keystore Manager**.
   Keep the keystore file and passwords safe; you need them for every update.
2. Menu **CPW > Build Android App Bundle (AAB)** (or set `CPW_KEYSTORE_PATH`, `CPW_KEYSTORE_PASS`, `CPW_KEY_ALIAS`,
   `CPW_KEY_PASS` environment variables, which `BuildScript` uses automatically).
3. Upload `Builds/Android/CrazyPenguinWars-1.0.0.aab` in the Google Play Console. Increase
   *Player > Android > Bundle Version Code* for every upload.

## 5. iPhone / iPad

You need a **Mac** with **Xcode** (free from the Mac App Store) and an **Apple ID**. A free Apple ID can install
the game on your own iPhone for 7 days at a time; the paid Apple Developer Program (99 USD/year) is needed for
longer installs, TestFlight and the App Store.

1. Install Unity with **iOS Build Support** on the Mac (see step 1).
2. Menu **CPW > Build iOS (Xcode project)**. Unity writes an Xcode project to `Builds/iOS/`.
   (Or **File > Build Profiles > iOS > Switch Platform > Build**.)
3. Open `Builds/iOS/Unity-iPhone.xcodeproj` in Xcode.
4. Xcode > **Settings > Accounts** > **+** > *Apple ID*, sign in.
5. In the project navigator click **Unity-iPhone**, select the **Unity-iPhone** target > **Signing & Capabilities**:
   tick **Automatically manage signing** and choose your **Team** (*Your Name (Personal Team)*).
   If Xcode says the bundle identifier is taken, change it to something unique, e.g.
   `com.yourname.crazypenguinwars`.
6. Connect the iPhone with a cable, unlock it, tap **Trust**. On iOS 16+ enable
   **Settings > Privacy & Security > Developer Mode** and restart the phone.
7. Pick your iPhone at the top of Xcode and press **Run** (the triangle).
8. The first time, the iPhone refuses to open the app: go to **Settings > General > VPN & Device Management**, tap
   your Apple ID and **Trust**. Start the game again.

Tip: set the environment variable `CPW_IOS_TEAM_ID` (your 10-character team id) before building and Unity fills in
the signing team for you.

## 6. Build with GitHub Actions

The repository has three workflows in `.github/workflows/`:

| Workflow | What | Needs |
|---|---|---|
| **Compile check** | Compiles all C# on every push (no Unity needed) | nothing |
| **Build Android** | Builds the APK (or AAB) with [GameCI](https://game.ci) | Unity license secrets |
| **Build iOS** | Builds the Xcode project, optionally compiles it unsigned on macOS | Unity license secrets |

### Add your Unity license (once)

GameCI runs Unity in Docker, which needs your license. With a free Personal license:
1. In the repository on GitHub: **Settings > Secrets and variables > Actions > New repository secret**.
2. Add `UNITY_EMAIL` and `UNITY_PASSWORD` (your Unity account).
3. Add `UNITY_LICENSE`: the full content of your license file (`.ulf`). After activating Unity Hub on your computer
   you find it at:
   - Windows: `C:\ProgramData\Unity\Unity_lic.ulf`
   - macOS: `/Library/Application Support/Unity/Unity_lic.ulf`
   - Linux: `~/.local/share/unity3d/Unity/Unity_lic.ulf`

   GameCI's guide explains alternatives: <https://game.ci/docs/github/activation>.

### Run a build

1. GitHub > **Actions** > **Build Android** (or **Build iOS**) > **Run workflow**. Android lets you choose `apk` or `aab`.
2. Wait (30-60 minutes the first time; the Library cache makes later builds faster).
3. Open the finished run and download the artifact at the bottom: **CrazyPenguinWars-Android** (the APK) or
   **CrazyPenguinWars-iOS-Xcode** (a zip with the Xcode project; continue at step 5.3 above on a Mac).

Pushing a tag such as `v1.0.0` (`git tag v1.0.0 && git push --tags`) builds both automatically.

Optional secrets: `ANDROID_KEYSTORE_BASE64` (your keystore, `base64 -w0 my.keystore`), `ANDROID_KEYSTORE_PASS`,
`ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASS` for a release-signed Android build, and `FIREBASE_CONFIG_JSON`
(see [FIREBASE.md](FIREBASE.md#6-create-firebase_configjson)) for online features in CI builds.

### Command line (advanced)

```
Unity -batchmode -quit -projectPath . -executeMethod BuildScript.BuildAndroidApk -logFile -
Unity -batchmode -quit -projectPath . -executeMethod BuildScript.BuildAndroidAab -logFile -
Unity -batchmode -quit -projectPath . -executeMethod BuildScript.BuildIOS -logFile -
```
Outputs: `Builds/Android/*.apk|*.aab`, `Builds/iOS/`. `-customBuildPath <path>` overrides the output.

## 7. Troubleshooting

- **Build fails with *Android SDK not found* / JDK errors**: install the Android modules with OpenJDK and SDK & NDK
  (step 1.4). In **Edit > Preferences > External Tools** the *installed with Unity* boxes should be ticked.
- **Pink/black objects**: the project uses the Built-in render pipeline; don't convert it to URP.
- **Game view portrait or stretched**: pick a landscape resolution in the Game view.
- **Online says Offline**: see [FIREBASE.md](FIREBASE.md); the game is fully playable offline.
- **Compile errors after pulling changes**: run `Tools/compile_check.sh` (needs the .NET 8 SDK) to see them outside
  Unity, or read the Unity **Console**.
- **The phone does not show up in Run Device**: try another cable/USB port, accept the debugging prompt, and on
  Windows install your phone maker's USB driver.
