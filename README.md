# Crazy Penguin Wars (Unity remake)

A mobile (Android and iOS) remake of **Crazy Penguin Wars** (also known as *Tuxwars*), the turn-based artillery game
from Facebook in the early 2010s: up to four penguins take turns on destructible islands, walk, jump, aim and blast
each other with bazookas, nukes, grenades, cluster bombs and a lot of sillier weapons before the clock runs out.

Built with **Unity 6** (built-in render pipeline, uGUI). Everything (menus, battles, effects) is created from code,
and the gameplay data (weapons, explosions, boosters, clothes, prices, XP table, the 23 original maps, sounds and
music) comes from the original game's files.

## Features

- Turn-based battles for 2-4 penguins on the 23 original maps with destructible terrain, water and physics objects.
- The original weapon and booster set, driven by the original config (damage, explosions, missiles, followers).
- Touch controls made for phones: move, jump, aim by dragging, power meter, weapon wheel; landscape only.
- Computer opponents at three skill levels, a guided tutorial and a practice mode.
- Quick Match against AI and custom games (map, match time, turn time, number of players, local pass-and-play).
- Progression: XP and levels, coins and cash, shop, wardrobe with the original clothes, crafting, slot machine,
  daily gift, achievements, challenges and a leaderboard.
- One-tap **PLAY** on the home screen (online quick match when connected, otherwise against the computer).
- Fully playable **offline**. With Firebase (free) connected: **online battles** (Quick Match matched by level,
  public games, private games with a 5-letter code, rematch with a 10 s countdown), **cloud save**, **friends**
  (friend codes, one free gift a day, inbox with gifts and game invites), **weekly / monthly / all-time
  leaderboards** by category with a friends filter, and a **weekly league** with promotion, relegation and rewards.
- 3D penguins and props generated with Blender scripts (sources in `Blender/`).

## Quick start

1. Install **Unity Hub** and the Unity version in `ProjectSettings/ProjectVersion.txt` (Unity 6 LTS) with the
   modules **Android Build Support** (+ OpenJDK, + Android SDK & NDK Tools) and, on a Mac, **iOS Build Support**.
2. Unity Hub > **Projects > Add > Add project from disk** > choose this folder. Open it and wait for the import.
   The project configures itself on first open (menu **CPW > Apply Project Settings** does it again).
3. Menu **CPW > Open Main Scene**, set the Game view to a landscape size such as 1920x1080, press **Play**.
   Mouse = finger, Esc = back.

Full, beginner-friendly instructions: **[Docs/BUILD.md](Docs/BUILD.md)**.

## Put it on your phone

**Android**
- Easiest: enable *USB debugging* on the phone (tap *Build number* 7 times in *About phone*, then *Developer options*),
  plug it in, **File > Build Profiles > Android > Switch Platform > Build And Run**.
- Or: menu **CPW > Build Android APK**, copy `Builds/Android/CrazyPenguinWars-1.0.0.apk` to the phone and open it
  (allow *Install unknown apps*).

**iPhone** (needs a Mac with Xcode and a free Apple ID)
1. Menu **CPW > Build iOS (Xcode project)** → `Builds/iOS/`.
2. Open `Builds/iOS/Unity-iPhone.xcodeproj` in Xcode.
3. *Signing & Capabilities*: tick *Automatically manage signing*, pick your (Personal) Team; change the bundle id if
   Xcode says it is taken.
4. Plug in the iPhone (enable *Developer Mode* on iOS 16+), select it, press **Run**. Trust your Apple ID on the phone
   under *Settings > General > VPN & Device Management*. Free Apple IDs need to reinstall every 7 days.

Details and troubleshooting: [Docs/BUILD.md](Docs/BUILD.md).

## Build in the cloud (GitHub Actions)

- **Compile check** runs on every push without a Unity license.
- **Build Android** / **Build iOS** use [GameCI](https://game.ci). Add the repository secrets `UNITY_LICENSE`
  (content of your `Unity_lic.ulf`), `UNITY_EMAIL` and `UNITY_PASSWORD`, then **Actions > Build Android > Run workflow**
  (or push a tag like `v1.0.0`). Download the APK or the zipped Xcode project from the run's artifacts.

See [Docs/BUILD.md#6-build-with-github-actions](Docs/BUILD.md#6-build-with-github-actions).

## Online features (Firebase)

Online play is switched on by adding one small file, `Assets/CPW/Resources/firebase_config.json`, with three values
from a free Firebase project. No SDK or plugin is needed. Step-by-step guide (about 15 minutes):
**[Docs/FIREBASE.md](Docs/FIREBASE.md)**. The **Settings** screen in the game shows whether it is connected.
When you update the game, publish the latest `Docs/firebase/database.rules.json` again: new features
(friends, inbox, league, rematch) need their rules.

## Editing the art (Blender)

The 3D models are made by Python scripts so they can be regenerated and tweaked:

- `Blender/scripts/` holds the generators (penguin, clothes, weapons, props...) and `build_all.py`, which rebuilds
  everything and exports it into `Assets/CPW/Resources/` (models and icons).
- `Blender/blend/` holds `.blend` files you can open in **Blender 4.2+** to look at or edit the models by hand.

To rebuild all art: install Blender 4.2 or newer, then from the repository folder run
```
blender --background --python Blender/scripts/build_all.py
```
Switch back to Unity; it re-imports the changed files automatically. See `Docs/ART.md` for the details of the art
pipeline (scale, axes, naming).

## Project layout

| Path | What |
|---|---|
| `Assets/CPW/Scripts/` | Game code: `Core`, `UI`, `Battle`, `Terrain`, `Weapons`, `Art`, `Meta` (menus), `Online` (Firebase) |
| `Assets/CPW/Editor/` | Project setup (`ProjectSetup.cs`) and build scripts (`BuildScript.cs`) |
| `Assets/CPW/Resources/` | Data, levels, sounds, models, icons, fonts loaded at runtime |
| `Blender/` | Art sources and generator scripts |
| `Tools/` | `build_data.py` (imports the original game data), `compile_check.sh` (compile without Unity) |
| `Docs/` | [ARCHITECTURE](Docs/ARCHITECTURE.md), [BUILD](Docs/BUILD.md), [FIREBASE](Docs/FIREBASE.md), ART |

## Credits

- **Crazy Penguin Wars** / Tuxwars was made by Digital Chocolate (dchoc). This is a non-commercial fan remake; all
  rights to the original game, its name, art, sounds and music belong to their owners.
- The original client, server, game data, maps, sounds and documentation come from the community preservation
  project **[Crazy Penguin Wars on GitHub](https://github.com/Crazy-Penguin-Wars)**:
  [cpw-client](https://github.com/Crazy-Penguin-Wars/cpw-client),
  [cpw-server](https://github.com/Crazy-Penguin-Wars/cpw-server),
  [cpw-battleserver](https://github.com/Crazy-Penguin-Wars/cpw-battleserver),
  [cpw-assets](https://github.com/Crazy-Penguin-Wars/cpw-assets),
  [cpw-launcher](https://github.com/Crazy-Penguin-Wars/cpw-launcher),
  [cpw-mapeditor](https://github.com/Crazy-Penguin-Wars/cpw-mapeditor) and
  [crazy-penguin-wars.github.io](https://github.com/Crazy-Penguin-Wars/crazy-penguin-wars.github.io).
  Huge thanks to everybody who kept the game alive.
- Unity remake by **pantela002**.
