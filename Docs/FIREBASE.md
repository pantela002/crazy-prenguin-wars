# Connecting Firebase (online play, cloud save, world leaderboard)

The game works completely **offline** without any of this: practice, quick matches against computer penguins,
local games, shop, wardrobe, crafting and everything else. Firebase adds:

- **Online battles**: Quick Match (random 5-8 minute matches with 10-30 s turns, prefers hosts within 30 levels),
  public games, private games with a short code, a list of open games, and a **rematch** offer after each match.
- **Cloud save**: your profile is copied to the cloud and restored if the local copy is older.
- **Leaderboards**: this week, this month and all time, by XP, wins, games, knock-outs, damage or turns, for
  everybody or just your friends.
- **Friends**: add friends with the friend code on their profile, see who is online, send one free gift per friend
  per day, invite friends to your private game; an **inbox** collects gifts, invites and "added you" notices.
- **Weekly league**: points from Quick Match and online results, standings per tier, promotion / relegation and
  rewards when the week (Monday 00:00 UTC) is over.

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

> **Updating the game?** Do this step again whenever `database.rules.json` changed (it did for friends, inbox,
> leaderboards per period, the league, rematch, battle chat and the clean-up of old matches). Old clients keep working with the new rules except for the
> leaderboard, which moved from `leaderboard/{uid}` to `leaderboard/all/{uid}`: you may delete the old
> `leaderboard/<uid>` entries (the ones directly under `leaderboard` that are not `all` or a period) in the console.

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
   After you play or buy something, `users/<id>/profile` and `leaderboard/all/<id>` appear too, and within a minute
   `players/<id>` and `friendCodes/<CODE>` (your friend code).
4. To try online battles you need two devices (or the editor plus a phone): on one, **Online > Host Private** shows a
   5-letter code; on the other, type the code and tap **Join**. The host taps **Start!**.

## Troubleshooting

| Status / message | Fix |
|---|---|
| `Anonymous sign-in is not enabled` | Step 2. |
| `the apiKey in firebase_config.json is wrong` | Copy the apiKey again (step 5), no spaces or quotes inside. |
| `database error: Permission denied` | Publish the rules (step 4). |
| `Index not defined, add ".indexOn"...` in the Unity Console | The published rules are older than the game: publish them again (step 4). |
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
users/{uid}/friends/{uid2} { name, added, gift: "yyyyMMdd" of the last gift sent }   only you can read it
players/{uid}              { name, level, code, seen }      public card: friend code + "online" (seen < 2.5 min)
friendCodes/{CODE}         { uid }                          6-letter friend codes
inbox/{uid}/{id}           { type: gift|invite|friend, from, fromName, at, item, amount, code, match }
                           ids: g_{from}_{yyyyMMdd} (one gift per sender per day), i_{from}_{code}, f_{from};
                           anybody signed in may add a message, only the owner may read and delete
leaderboard/{period}/{uid} { name, level, xp, wins, games, kills, deaths, damage, turns, suicides, shots, updated }
                           period: "all", an ISO week "2026-W40" or a month "2026-10" (UTC)
league/{week}/{tier}/{uid} { name, level, points, games, updated }   tier 0..4 (Bronze..Diamond)
lobby/{matchId}            { host, hostName, level, levelId, players, maxPlayers, quick, code, hb }   public waiting games
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
    chat/{pushId}          { p: slot, u: uid, at, x: text (max 80) | k: taunt id }   battle chat (POST)
    rematch/{uid}          { s: "ready" | "left", next: newMatchId }   after the match (see below)
```

**Rematch.** After an online match every player has `BattleOptions.TimeToStartRematch` (10 s) to tap Rematch, which
writes `rematch/{uid}`. When two or more are ready (everyone decided, or the countdown ended) the ready player with
the lowest slot hosts a new private match with the same settings and writes its id as `next`; the others join it.
The new match gets a new seed when it starts.

**League.** The numbers are invented (the original League tables are empty): 5 tiers, points per place
20/12/6/2 online and 10/6/3/1 against the computer (fewer for games with fewer than 4 penguins), at most 50 counted
games a week, top 20% promoted, bottom 20% relegated (when 5+ played), rewards 500/300/200 coins (+5/3/2 fish) for
the top 3, 100 for the top half, 50 for everyone else, x1.5 per tier above Bronze. Each player settles their own
week the first time they are online after it ended. Old `league/<week>` nodes can be deleted now and then.

**Gifts.** One per friend per day from the `Gift` config section (the shipped one has only a hidden placeholder,
so basic ammo is used: Basic Nuke x3, Grenade x2, Pistol x3, Shotgun x2, Cluster Rocket x1). The receiver's game caps
the amount, so a modified client can't send more.

**Clean-up** (there is no server code; the free plan has no Cloud Functions, so the players' devices do it and the
rules allow exactly that):
- actions and snapshots older than two turns are removed as the match goes;
- a finished match is deleted 60 s after the end by the device of the lowest slot still in it (any player of a
  `finished` match may delete it; the delay leaves time for the rematch votes);
- a cancelled waiting room is deleted by its host after 10 s;
- abandoned lobby entries disappear from the list after 30 s and may be removed by anybody after 2 minutes;
- matches nobody cleaned up (everybody closed the app) may be deleted by anybody once they are 3 hours old: every
  device looks for up to 3 of them when it signs in, at most every 6 hours.

## Costs

The free **Spark** plan includes 1 GB stored and 10 GB downloaded per month for the Realtime Database and unlimited
anonymous sign-ins. One online match transfers roughly 0.5-2 MB per player (mostly polling), so the free plan covers
thousands of matches per month. If you ever hit the limit, the database stops answering until the next month (you
are **not** charged on Spark); the game then simply behaves as offline. You only pay if you deliberately upgrade
to the Blaze plan.

Everything runs on the free plan: anonymous Auth and the Realtime Database only, no Cloud Functions, Firestore,
Storage or scheduled jobs. Old matches clean themselves up (see *Clean-up* above). What grows slowly forever are the
small per-week and per-month nodes (`leaderboard/2026-W40`, `leaderboard/2026-10`, `league/2026-W40`, about 150 bytes
per player each); delete old ones in the console once a year if you like. Deleting the `matches` node in the console
is always safe too. Nobody loses progress; profiles live under `users/`.

## Testing the rules locally

`Tools/firebase_tests/rules_test.js` replays every database call the game makes (the same URLs, bodies and queries as
`FirebaseClient.cs`) for four anonymous players against the **Firebase Local Emulator Suite** on your computer:
sign-in and token refresh, cloud save, leaderboards, friends, gifts, invites, the league, hosting / joining /
quick match / private codes, a whole match with turns, chat and rematch, and the clean-up. It also checks that
cheating is refused (writing another player's save, joining a running match, chatting in somebody else's match...).
It needs no Firebase project and no internet once installed.

1. Install [Node.js](https://nodejs.org) 18 or newer and Java 11 or newer (`java -version`).
2. In a terminal:
   ```sh
   cd Tools/firebase_tests
   npm install        # once: installs firebase-tools into this folder only
   npm test
   ```
3. It ends with `N passed, 0 failed`. Anything else lists the calls that were wrongly allowed or refused.

Run it after every change to `Docs/firebase/database.rules.json` (the script uploads that file to the emulator itself)
or to a database call in `Assets/CPW/Scripts/Online/`; add the new call to the script too. The emulator behaves like the
real database here, including the `.indexOn` checks.
