# Connecting Firebase (online play, cloud save, world leaderboard)

The game works completely **offline** without any of this: practice, quick matches against computer penguins,
local games, shop, wardrobe, crafting and everything else. Firebase adds:

- **Online battles**: Quick Match, public games, private games with a short code, a list of open games.
- **Cloud save**: your profile is copied to the cloud and restored if the local copy is older.
- **World leaderboard**: XP, level and wins of everybody who plays your build.

You do this **once**. It takes about 15 minutes and costs nothing: the free **Spark** plan is plenty for a hobby game
(see [Costs](#costs)). You don't need to install anything; the game talks to Firebase over plain HTTPS, so there are
no Firebase SDKs, plugins or Gradle settings.

---

## 1. Create a Firebase project

1. Go to <https://console.firebase.google.com> and sign in with a Google account.
2. Click **Create a project** (or **Add project**).
3. Name it, e.g. `crazy-penguin-wars`, and click **Continue**.
4. Google Analytics is not needed: switch it **off**, then click **Create project**. Wait, then click **Continue**.

## 2. Turn on Anonymous sign-in

Every player gets an invisible anonymous account, so nobody has to type a password.

1. In the left menu open **Build > Authentication** and click **Get started**.
2. Open the **Sign-in method** tab.
3. Click **Anonymous**, switch **Enable** on, and click **Save**.

## 3. Create the Realtime Database

1. In the left menu open **Build > Realtime Database** and click **Create Database**.
2. Choose a location close to your players (e.g. `europe-west1` or `us-central1`). Click **Next**.
3. Choose **Start in locked mode** and click **Enable**.
4. At the top of the data view you now see the database address, something like
   `https://crazy-penguin-wars-default-rtdb.europe-west1.firebasedatabase.app/`.
   That is your **databaseURL**. Copy it somewhere.

## 4. Paste the security rules

The rules decide who may read and write what. Ours make sure players can only change their own save and
leaderboard entry, and only people in a match can write its moves.

1. Still in **Realtime Database**, open the **Rules** tab.
2. Delete everything in the editor.
3. Open [`Docs/firebase/database.rules.json`](firebase/database.rules.json) from this repository, copy **all** of it and
   paste it into the editor.
4. Click **Publish**.

## 5. Find your apiKey and projectId

1. Click the **gear icon** next to *Project Overview* (top left) > **Project settings**.
2. On the **General** tab, the **Project ID** is shown near the top (e.g. `crazy-penguin-wars`). That is your **projectId**.
3. Scroll down to **Your apps**. If there is no app yet, click the **`</>`** (Web) icon, type any nickname
   (e.g. `cpw`), leave *Firebase Hosting* unchecked and click **Register app**. (Choose *Web* even though the game
   runs on phones: the game uses the web REST API.)
4. Under **SDK setup and configuration** select **Config**. You will see something like:
   ```js
   const firebaseConfig = {
     apiKey: "AIzaSyD...xyz",
     authDomain: "crazy-penguin-wars.firebaseapp.com",
     databaseURL: "https://crazy-penguin-wars-default-rtdb.europe-west1.firebasedatabase.app",
     projectId: "crazy-penguin-wars",
     ...
   };
   ```
   You need `apiKey`, `projectId` and `databaseURL`. (If `databaseURL` is missing there, use the address from step 3.)

> The web apiKey is not a secret password (it only identifies your project; the rules protect the data),
> but this repository still keeps it out of git so forks don't use your quota by accident.

## 6. Create `firebase_config.json`

1. In the Unity project, go to the folder `Assets/CPW/Resources/`.
2. Copy `firebase_config.example.json` and name the copy **`firebase_config.json`** (exactly that name).
3. Open it in a text editor and fill in your values:
   ```json
   {
     "apiKey": "AIzaSyD...xyz",
     "projectId": "crazy-penguin-wars",
     "databaseURL": "https://crazy-penguin-wars-default-rtdb.europe-west1.firebasedatabase.app"
   }
   ```
4. Save. The file is listed in `.gitignore`, so it is never committed.

**GitHub Actions builds**: because the file is not in git, CI builds are offline unless you give it to them:
on GitHub open **Settings > Secrets and variables > Actions > New repository secret**, name it
`FIREBASE_CONFIG_JSON` and paste the whole content of your `firebase_config.json`. The build workflows write it
into the project before building.

## 7. Rebuild and check

1. In Unity press **Play** (or build to your phone again, see [BUILD.md](BUILD.md)). The config is read when the game starts.
2. Open **Settings** in the game. The online status line should say
   **"Online (Firebase), player id ..."**.
   If it says *Offline: ...* the rest of the line tells you why (see [Troubleshooting](#troubleshooting)).
3. In the Firebase console under **Realtime Database > Data** you should now see `users/<id>/lastSeen`.
   After you play or buy something, `users/<id>/profile` and `leaderboard/<id>` appear too.
4. To try online battles you need two devices (or the editor plus a phone): on one, **Online > Host Private** shows a
   5-letter code; on the other, type the code and tap **Join**. The host taps **Start!**.

## Troubleshooting

| Status / message | Fix |
|---|---|
| `Anonymous sign-in is not enabled` | Step 2. |
| `the apiKey in firebase_config.json is wrong` | Copy the apiKey again (step 5), no spaces or quotes inside. |
| `database error: Permission denied` | Publish the rules (step 4). |
| `database error: ... 404` or `Can't reach Firebase` | Check `databaseURL` (it must start with `https://` and be your database address); check internet. |
| Settings shows *Offline (Firebase not connected)* | The file is missing, misnamed (`firebase_config.json`) or still has `YOUR_...` placeholders. |
| Online list is always empty | Normal when nobody else is hosting. Use Quick Match: it hosts a game when it finds none. |

The Unity Console also logs `CPW: ...` warnings with details.

## How it works (for the curious)

- Sign-in: `accounts:signUp` (Identity Toolkit REST) creates an anonymous user; the refresh token is stored in
  PlayerPrefs and exchanged at `securetoken.googleapis.com` for a new ID token every hour.
  Reinstalling the app creates a new anonymous user (a new cloud save).
- Every database call is `https://<databaseURL>/<path>.json?auth=<idToken>` (GET/PUT/PATCH/DELETE).
- Online battles are turn based: the active player's moves (`move`, `aim`, `fire`...) are written in small batches and
  the others poll about twice per second and replay them. At the end of a turn the active player writes a full
  snapshot of the world, which everyone applies. Each player writes a heartbeat every 3 s; a player without a
  heartbeat for 25 s (or who leaves) is reported as gone.

### Data layout

```
users/{uid}/profile        { json: "<PlayerProfile as JSON text>", name, updated }
users/{uid}/lastSeen       server timestamp
leaderboard/{uid}          { name, xp, level, wins, updated }
lobby/{matchId}            { host, hostName, levelId, players, maxPlayers, quick, code, hb }   public waiting games
codes/{CODE}               { match: matchId, host, hb }                                        5-letter join codes
matches/{matchId}
    host, hostName, isPrivate, quick, code, created, startedAt
    state                  "waiting" | "playing" | "finished" | "cancelled"
    settings               { levelId, matchTime, turnTime, winningScore, powerUps, betId, maxPlayers, seed }
    players/{uid}          { name, level, head, chest, feet, trophy, items:[{id,n}], joined, hb, left }
    order                  [uid, uid, ...]          slot order in the battle (host first)
    turn                   { index, player, by, at } index = finished turns, player = whose turn
    actions/{turn}/aNNNNN  [ {p, t, k, x, y, px, py, s}, ... ]   batched TurnActions
    snapshots/{turn}       { from, json: "<BattleSnapshot JSON>" }
```

Finished matches are deleted by the host's device 20 s after the end; actions and snapshots older than two turns are
removed as the match goes. Abandoned rooms disappear from the list after 30 s and may be removed by anybody after
2 minutes.

## Costs

The free **Spark** plan includes 1 GB stored and 10 GB downloaded per month for the Realtime Database and unlimited
anonymous sign-ins. One online match transfers roughly 0.5-2 MB per player (mostly polling), so the free plan covers
thousands of matches per month. If you ever hit the limit, the database stops answering until the next month (you
are **not** charged on Spark); the game then simply behaves as offline. You only pay if you deliberately upgrade
to the Blaze plan.

Optional housekeeping: if old `matches/` entries pile up (players closing the app mid-match), delete the `matches`
node in the console now and then. Nobody loses progress; profiles live under `users/`.
