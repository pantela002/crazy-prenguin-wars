using System;
using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// The battle itself (original BattleManager + BattleSimulation): level and penguin setup, turn order,
    /// turn and match clocks, one attack per turn with retreat time, respawns, crates, match end and results.
    /// Player actions go through the Act* methods so the same code serves touch input, AI and online replay.
    /// </summary>
    public partial class BattleController
    {
        public enum Phase { Starting, Curtain, Turn, Settling, Over }

        public Phase CurrentPhase { get; private set; } = Phase.Starting;
        public readonly List<Penguin> Penguins = new List<Penguin>();
        public LevelData Level { get; private set; }
        public string LevelId { get; private set; }
        public BattleTerrain Terrain { get; private set; }
        public BattleCamera Cam { get; private set; }
        public BattleHUD Hud { get; private set; }
        public BattleAI Ai { get; private set; }
        public BattleTutorial Tutorial { get; private set; }

        public int ActiveIndex { get; private set; } = -1;
        public Penguin Active => ActiveIndex >= 0 && ActiveIndex < Penguins.Count ? Penguins[ActiveIndex] : null;
        public int TurnNumber { get; private set; }
        public float TurnDuration { get; private set; }
        public float MatchDuration { get; private set; }
        public float TurnTimeLeft { get; private set; }
        public float MatchTimeLeft { get; private set; }
        public int AttacksLeft { get; private set; }
        public bool Fired { get; private set; }
        public bool BoosterUsedThisTurn { get; private set; }
        public FireHandle CurrentShot { get; private set; }
        public int WinningScore { get; private set; }
        public bool RewardsEnabled { get; private set; }
        public bool Paused { get; private set; }
        public bool Online => net != null;
        /// <summary>Turn timer is held (tutorial explanations).</summary>
        public bool TimerHeld;
        /// <summary>Seconds left before this turn's clock starts (BattleRules.TurnLeadIn: banner + camera handover).</summary>
        public float TurnLeadInLeft { get; private set; }
        /// <summary>Weapon each penguin last chose (kept between turns).</summary>
        readonly Dictionary<int, string> selected = new Dictionary<int, string>();

        IBattleNetwork net;
        int hostSlot;                   // device that runs AI turns online
        int localHumans;
        System.Random rng;
        Transform worldRoot;
        Vector2 prevGravity;
        float introTimer, settleTimer, overTimer, recordClock, lastAimSent, lastPosSent;
        bool turnEndCueDone, minuteCueDone, almostOverCueDone;
        BattleResult finalResult;
        readonly List<PowerUpCrate> crates = new List<PowerUpCrate>();

        // ================================================================ setup

        partial void SetupImpl()
        {
            try { DoSetup(); }
            catch
            {
                Cleanup();
                I = null;
                Destroy(gameObject);
                throw;
            }
        }

        void DoSetup()
        {
            GameData.Load();
            var c = Config ?? new BattleConfig();
            if (c.players.Count == 0)
            {
                c.players.Add(new PlayerSlot { name = ProfileService.P.displayName, usesProfileInventory = true });
                c.players.Add(new PlayerSlot { name = Loc.T("BOT1_NAME"), isAI = true, isLocalHuman = false, colorIndex = 1 });
            }
            Config = c;
            rng = new System.Random(c.seed != 0 ? c.seed : Environment.TickCount);
            net = c.mode == BattleMode.Online ? c.network : null;

            // clocks: config values, defaults from BattleOptions (or Practice for practice/tutorial)
            bool practice = c.mode == BattleMode.Practice || c.mode == BattleMode.Tutorial;
            TurnDuration = c.turnTime > 0 ? c.turnTime : (practice ? BattleRules.PracticeTurnTime : BattleRules.TurnTime);
            MatchDuration = c.matchTime > 0 ? c.matchTime : (practice ? BattleRules.PracticeMatchTime : BattleRules.MatchTime);
            if (c.mode == BattleMode.Tutorial)
            {
                TurnDuration = Mathf.Max(TurnDuration, BattleRules.PracticeTurnTime);
                MatchDuration = Mathf.Max(MatchDuration, BattleRules.PracticeMatchTime);
            }
            MatchTimeLeft = MatchDuration;
            WinningScore = c.winningScore > 0 ? c.winningScore : (c.mode == BattleMode.Tutorial ? BattleRules.WinningScoreDefault : 0);

            localHumans = 0;
            for (int i = 0; i < c.players.Count; i++) if (c.players[i].isLocalHuman && !c.players[i].isAI) localHumans++;
            RewardsEnabled = c.mode == BattleMode.QuickMatch || c.mode == BattleMode.Online || (c.mode == BattleMode.Custom && localHumans <= 1);
            hostSlot = 0;
            if (net != null)
                for (int i = 0; i < c.players.Count; i++) if (!c.players[i].isAI) { hostSlot = i; break; }

            // level
            LevelId = ChooseLevel(c);
            Level = LevelData.Load(LevelId) ?? LevelData.Load("forest_easy_01");
            Level = Level ?? throw new Exception("Level " + LevelId + " could not be loaded");

            BattleWorld.Clear();
            WeaponSystem.ClearAll();
            worldRoot = new GameObject("World").transform;
            worldRoot.SetParent(transform, false);
            BattleWorld.Root = worldRoot;
            Terrain = BattleTerrain.Build(Level, worldRoot);
            prevGravity = Physics2D.gravity;
            Physics2D.gravity = new Vector2(0, -Tuning.Gravity);

            // penguins on spawn points, in a seeded random order
            var spawns = new List<Vector2>(Level.spawnPoints);
            if (spawns.Count == 0) spawns.Add(new Vector2(Level.size.x * 0.5f, Level.size.y * 0.6f));
            Shuffle(spawns);
            for (int i = 0; i < c.players.Count; i++)
            {
                var sp = spawns[i % spawns.Count] + new Vector2((i / spawns.Count) * 2.5f, 0);
                var p = Penguin.Create(worldRoot, c.players[i], i, GroundPoint(sp), c.mode);
                p.SetFacing(sp.x < Level.size.x * 0.5f ? 1 : -1);
                Penguins.Add(p);
                BattleWorld.Penguins.Add(p);
            }

            Cam = BattleCamera.Create(transform, Level);
            Hud = BattleHUD.Create(transform, this);
            Ai = new BattleAI(this);
            if (c.mode == BattleMode.Tutorial) Tutorial = new BattleTutorial(this);
            if (c.powerUps) SpawnLevelPowerUps();

            if (net != null)
            {
                net.ActionReceived += OnNetAction;
                net.TurnEndReceived += OnNetTurnEnd;
                net.PlayerLeft += OnNetPlayerLeft;
                HookChat(true);
            }
            BattleEvents.Explosion += OnExplosion;
            BattleEvents.PenguinDamaged += OnPenguinDamaged;
            BattleEvents.PenguinKilled += OnPenguinKilled;

            // music: battle track + level theme ambience (Sound ids "BattleMusic", "Forest", "Winter"...)
            AudioManager.Music("BattleMusic");
            if (!string.IsNullOrEmpty(Level.theme) && GameData.Get("Sound", Level.theme) != null) AudioManager.Loop("ambient", Level.theme, 0.45f);
            AudioManager.Sfx("BattleStart");

            CurrentPhase = Phase.Starting;
            introTimer = 2.2f;
            Cam.Overview();
            Hud.Banner(Loc.T("MATCH_START_INFO"), Theme.Primary, 2f);
            Tutorial?.OnBattleStart();
        }

        string ChooseLevel(BattleConfig c)
        {
            if (c.mode == BattleMode.Tutorial)
            {
                var t = GameData.Get("PracticeLevel", "tutorial_level");
                return t != null ? t.Str("LevelFile", "Data/Levels/tutorial_forest_1") : "Data/Levels/tutorial_forest_1";
            }
            if (!string.IsNullOrEmpty(c.levelId)) return c.levelId;
            // random among the Level rows allowed for the local player's level (MinLevel..MaxLevel)
            int lvl = c.players.Count > 0 ? c.players[c.LocalPlayerIndex].level : 1;
            if (lvl <= 0) lvl = ProfileService.P.level;
            var allowed = new List<string>();
            var all = new List<string>();
            foreach (var r in GameData.Section("Level").Values)
            {
                all.Add(r.Id);
                if (lvl >= r.Int("MinLevel", 1) && lvl <= r.Int("MaxLevel", 999)) allowed.Add(r.Id);
            }
            var pool = allowed.Count > 0 ? allowed : all;
            if (pool.Count == 0) return "forest_easy_01";
            pool.Sort(string.CompareOrdinal);
            return pool[rng.Next(pool.Count)];
        }

        void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var t = list[i]; list[i] = list[j]; list[j] = t;
            }
        }

        /// <summary>Point just above the ground at/below p (spawning).</summary>
        Vector2 GroundPoint(Vector2 p)
        {
            float r = BattleRules.Radius;
            if (Terrain != null && Terrain.GroundBelow(p.x, p.y + 2f, out var g) && g.y > Terrain.WaterY) return g + Vector2.up * (r * 1.15f);
            return p + Vector2.up * r;
        }

        // ================================================================ lookups

        public Penguin PenguinAt(int index) => index >= 0 && index < Penguins.Count ? Penguins[index] : null;

        /// <summary>This device simulates and records the turns of this player (always true offline).</summary>
        public bool OwnedLocally(int index)
        {
            if (net == null) return true;
            var p = PenguinAt(index);
            if (p == null) return false;
            return p.Slot.isAI ? net.LocalSlot == hostSlot : index == net.LocalSlot;
        }

        /// <summary>A human on this device controls the active penguin right now.</summary>
        public bool IsInputTurn
        {
            get
            {
                var a = Active;
                return CurrentPhase == Phase.Turn && a != null && a.Alive && !a.Slot.isAI && IsLocalHuman(ActiveIndex) && !Paused;
            }
        }

        public bool IsLocalHuman(int index)
        {
            var p = PenguinAt(index);
            if (p == null || p.Slot.isAI) return false;
            return net != null ? index == net.LocalSlot : p.Slot.isLocalHuman;
        }

        /// <summary>The penguin whose view the HUD shows (local player; the active one in pass-and-play).</summary>
        public Penguin ViewPenguin
        {
            get
            {
                if (localHumans > 1 && Active != null && IsLocalHuman(ActiveIndex)) return Active;
                int li = net != null ? net.LocalSlot : Config.LocalPlayerIndex;
                return PenguinAt(li) ?? Active;
            }
        }

        public bool PassAndPlay => net == null && localHumans > 1;

        public string SelectedItem(int index) => selected.TryGetValue(index, out var s) ? s : null;

        public bool AreEnemies(Penguin a, Penguin b)
        {
            if (a == null || b == null || a == b) return false;
            return a.Slot.team < 0 || b.Slot.team < 0 || a.Slot.team != b.Slot.team;
        }

        int Participants()
        {
            int n = 0;
            foreach (var p in Penguins) if (!p.Left) n++;
            return n;
        }

        // ================================================================ main loop

        void Update()
        {
            float dt = Time.deltaTime;
            if (net != null) { net.Tick(Time.unscaledDeltaTime); DrainNet(); }
            recordClock += dt;

            switch (CurrentPhase)
            {
                case Phase.Starting:
                    introTimer -= dt;
                    if (introTimer <= 0) BeginTurn(FirstPlayer());
                    break;

                case Phase.Curtain:
                    break;

                case Phase.Turn:
                    UpdateTurn(dt);
                    break;

                case Phase.Settling:
                    UpdateSettling(dt);
                    break;

                case Phase.Over:
                    overTimer -= Time.unscaledDeltaTime;
                    if (overTimer <= 0 && finalResult != null)
                    {
                        var r = finalResult;
                        finalResult = null;
                        Finish(r);
                    }
                    break;
            }
            Tutorial?.Update(dt);
        }

        int FirstPlayer()
        {
            for (int i = 0; i < Penguins.Count; i++) if (!Penguins[i].Left) return i;
            return 0;
        }

        void UpdateTurn(float dt)
        {
            var a = Active;
            bool local = OwnedLocally(ActiveIndex);
            if (TurnLeadInLeft > 0)
            {
                // the turn clock waits for the turn banner and the camera handover; the match clock keeps going
                // (it is the same on every device and the end-of-turn snapshot carries it anyway)
                TurnLeadInLeft -= dt;
                MatchTimeLeft -= dt;
            }
            else if (!TimerHeld && (Tutorial == null || !Tutorial.HoldsTimer))
            {
                TurnTimeLeft -= dt;
                MatchTimeLeft -= dt;
            }
            ClockCues();

            if (a != null && local && net != null && recordClock - lastPosSent > 0.5f && a.Alive)
            {
                lastPosSent = recordClock;
                Record("pos", a.Position.x, a.Position.y);
            }

            if (a == null || a.Left || !a.Alive) { EndTurn(); return; }
            if (TurnTimeLeft <= 0 || MatchTimeLeft <= 0) { EndTurn(); return; }
            if (WinningScore > 0 && HighestScore() >= WinningScore && (CurrentShot == null || CurrentShot.Done)) { EndTurn(); return; }
            if (local && a.Slot.isAI) Ai.Update(dt);
        }

        void ClockCues()
        {
            if (!turnEndCueDone && TurnTimeLeft <= 3f && IsLocalHuman(ActiveIndex))
            {
                turnEndCueDone = true;
                AudioManager.Sfx("TurnEnd", 0.7f);
            }
            if (!minuteCueDone && MatchTimeLeft <= 60f && MatchDuration > 90f)
            {
                minuteCueDone = true;
                Hud.Banner(Loc.T("MATCH_MINUTE_LEFT"), Theme.Danger, 2f);
            }
            if (!almostOverCueDone && MatchTimeLeft <= 12f)
            {
                almostOverCueDone = true;
                AudioManager.Sfx("GameAlmostOver");
            }
        }

        int HighestScore()
        {
            int m = int.MinValue;
            foreach (var p in Penguins) if (!p.Left) m = Mathf.Max(m, p.Score);
            return m;
        }

        // ================================================================ turns

        void BeginTurn(int index)
        {
            if (CurrentPhase == Phase.Over) return;
            TurnNumber++;
            ActiveIndex = index;
            BattleWorld.ActivePlayer = index;
            var a = Active;

            // battleserver respawn queue: back after TimeToRespawn, at the latest on their own turn
            foreach (var p in Penguins)
            {
                if (p.Alive || p.Left) continue;
                // online every device must agree, so there it is simply the next turn start
                if (p == a || net != null || Time.time - p.DiedAt >= BattleRules.TimeToRespawn) p.Respawn(FindRespawnPoint(p));
            }

            WeaponSystem.OnTurnStart(index);
            a.TickEffects();
            a.BeginTurn();
            AttacksLeft = BattleRules.MaxAttacks;
            Fired = false;
            BoosterUsedThisTurn = false;
            CurrentShot = null;
            TurnTimeLeft = TurnDuration;
            turnEndCueDone = false;
            lastAimSent = -1;
            recordClock = 0;
            if (!selected.ContainsKey(index) || !a.Ammo.Has(selected[index])) selected[index] = a.Ammo.DefaultWeapon();

            MaybeDropCrate();
            BattleEvents.RaiseTurnStarted(index);
            Cam.FollowPenguin(a);
            Hud.OnTurnStart(a);

            bool human = IsLocalHuman(index);
            if (human) AudioManager.Sfx("PlayerStartTurn");
            else AudioManager.Sfx("SplashOpponentsTurn", 0.6f);

            if (PassAndPlay && human && !Config.skipPassCurtain)
            {
                CurrentPhase = Phase.Curtain;
                Hud.ShowCurtain(a);
            }
            else StartPlaying();
        }

        /// <summary>The pass-the-phone curtain was dismissed.</summary>
        public void ConfirmCurtain()
        {
            if (CurrentPhase == Phase.Curtain) StartPlaying();
        }

        void StartPlaying()
        {
            CurrentPhase = Phase.Turn;
            TurnLeadInLeft = BattleRules.TurnLeadIn;
            var a = Active;
            string name = a.DisplayName;
            if (IsLocalHuman(ActiveIndex))
            {
                // pass-and-play: every local player is "you", so name whose turn it is
                if (PassAndPlay) Hud.Banner(name + "'s turn!", a.TeamColor, 1.6f);
                else Hud.Banner(Loc.Has("TID_YOUR_TURN") ? Loc.T("TID_YOUR_TURN") : "Your turn!", a.TeamColor, 1.6f);
                AudioManager.Sfx("SplashYourTurn", 0.7f);
                // the held weapon is shown when the turn starts so the player can aim right away
                var w = SelectedItem(ActiveIndex);
                if (!string.IsNullOrEmpty(w))
                {
                    DoSelectWeapon(ActiveIndex, w);
                    // other devices may not know this weapon (bought mid-battle from our profile): tell them what we hold
                    Record("weapon", s: w);
                }
            }
            else Hud.Banner(Loc.T("PLAYER_TURN_CHANGE_1").Replace("%U", name), a.TeamColor, 1.6f);

            if (a.Slot.isAI && OwnedLocally(ActiveIndex)) Ai.BeginTurn(a);
            Tutorial?.OnTurnStarted(a);
        }

        /// <summary>Turn time is over (or the player died / the match clock ran out): stop input and wait for the world.</summary>
        void EndTurn()
        {
            if (CurrentPhase != Phase.Turn) return;
            CurrentPhase = Phase.Settling;
            settleTimer = 0;
            var a = Active;
            if (a != null) a.EndTurn();
            Ai.EndTurn();
            Hud.OnTurnEnd();
        }

        void UpdateSettling(float dt)
        {
            settleTimer += dt;
            bool local = OwnedLocally(ActiveIndex) && (Active == null || !Active.Left);
            if (!local)
            {
                // remote turn: the active player's device sends the authoritative snapshot
                if (settleTimer > 45f || (net != null && !net.Connected && settleTimer > 10f)) AfterTurn(null);
                return;
            }
            bool calm = !WeaponSystem.WorldBusy && (CurrentShot == null || CurrentShot.Done) && PenguinsCalm();
            if ((calm && settleTimer > 0.6f) || settleTimer > BattleRules.MaxSettleTime) AfterTurn(null);
        }

        bool PenguinsCalm()
        {
            foreach (var p in Penguins)
            {
                if (!p.Alive || p.Body == null || !p.Body.simulated) continue;
                if (p.Body.Vel().sqrMagnitude > 0.6f) return false;
            }
            foreach (var c in crates) if (c != null && c.Falling) return false;
            return true;
        }

        /// <summary>Turn finished: send/apply the snapshot, check the end of the match, next player.</summary>
        void AfterTurn(BattleSnapshot snap)
        {
            if (CurrentPhase == Phase.Over) return;
            bool over;
            int next;
            if (snap != null)
            {
                ApplySnapshot(snap);
                over = snap.matchOver;
                next = snap.nextPlayer;
            }
            else
            {
                next = NextPlayer(ActiveIndex);
                over = MatchTimeLeft <= 0 || (WinningScore > 0 && HighestScore() >= WinningScore) || Participants() < 2 || (Tutorial != null && Tutorial.WantsEnd);
                if (net != null && OwnedLocally(ActiveIndex)) net.SendTurnEnd(BuildSnapshot(next, over));
            }
            if (over) EndMatch(false);
            else BeginTurn(Mathf.Clamp(next, 0, Penguins.Count - 1));
        }

        int NextPlayer(int from)
        {
            for (int k = 1; k <= Penguins.Count; k++)
            {
                int i = (from + k) % Penguins.Count;
                if (!Penguins[i].Left) return i;
            }
            return from;
        }

        Vector2 FindRespawnPoint(Penguin who)
        {
            // a random spawn point away from the others (original SpawnPointFinder), seeded so devices agree
            var r = new System.Random((Config.seed + 7919) * 31 + TurnNumber * 101 + who.PlayerIndex);
            var pts = Level.spawnPoints;
            if (pts.Count == 0) return GroundPoint(new Vector2(Level.size.x * 0.5f, Level.size.y * 0.7f));
            Vector2 best = pts[r.Next(pts.Count)];
            float bestScore = -1;
            for (int t = 0; t < pts.Count; t++)
            {
                var sp = pts[(t + r.Next(pts.Count)) % pts.Count];
                float minD = 999f;
                foreach (var o in Penguins) if (o != who && o.Alive) minD = Mathf.Min(minD, Vector2.Distance(o.Position, sp));
                float s = minD + (float)r.NextDouble() * 4f;
                if (s > bestScore) { bestScore = s; best = sp; }
            }
            return GroundPoint(best);
        }

        // ================================================================ actions (input, AI, replay)

        void Record(string type, float x = 0, float y = 0, string s = null, float px = 0, float py = 0)
        {
            if (net == null || !OwnedLocally(ActiveIndex)) return;
            net.SendAction(new TurnAction { player = ActiveIndex, t = recordClock, type = type, x = x, y = y, s = s, px = px, py = py });
        }

        bool CanAct => CurrentPhase == Phase.Turn && Active != null && Active.Alive && !Paused;

        public void ActWalk(int dir)
        {
            if (!CanAct) dir = 0;
            var a = Active;
            if (a == null || a.WalkDir == dir) return;
            a.Walk(dir);
            if (a.WalkDir == dir) Record("move", dir);
            if (dir != 0) Tutorial?.OnMoved();
        }

        public bool ActJump(float dirX)
        {
            if (!CanAct) return false;
            if (!Active.CanJump)
            {
                if (Active.Grounded && Active.ActionPoints < Active.JumpCost && IsInputTurn)
                    Fx.FloatText(Active.Position + Vector2.up * 2f, Loc.T("FLOATER_NO_MORE_JUMPS"), Color.white, 0.9f);
                return false;
            }
            if (!Active.Jump(dirX)) return false;
            Record("jump", dirX);
            Tutorial?.OnJumped();
            return true;
        }

        public void ActSelectWeapon(string itemId)
        {
            if (!CanAct || Fired || string.IsNullOrEmpty(itemId) || !Active.Ammo.Has(itemId)) return;
            DoSelectWeapon(ActiveIndex, itemId);
            Record("weapon", s: itemId);
            Tutorial?.OnWeaponSelected(itemId);
        }

        void DoSelectWeapon(int index, string itemId)
        {
            selected[index] = itemId;
            var p = PenguinAt(index);
            if (p == null) return;
            p.HoldItem(itemId);
            p.Aiming = true;
            p.SetAim(p.AimAngle, p.AimPower);
        }

        public void ActAim(float angleDeg, float power01)
        {
            if (!CanAct || Fired) return;
            Active.Aiming = true;
            Active.SetAim(angleDeg, power01);
            if (net != null && recordClock - lastAimSent > 0.12f)
            {
                lastAimSent = recordClock;
                Record("aim", Active.AimAngle, Active.AimPower);
            }
            Tutorial?.OnAimed();
        }

        /// <summary>Fire the selected weapon (one attack per turn, BattleOptions.MaxNumberOfAttacks).</summary>
        public bool ActFire(Vector2 targetPoint)
        {
            if (!CanAct) return false;
            var a = Active;
            var item = SelectedItem(ActiveIndex);
            if (AttacksLeft <= 0)
            {
                if (IsInputTurn) Fx.FloatText(a.Position + Vector2.up * 2f, Loc.T("FLOATER_NO_MORE_ATTACKS"), Color.white, 0.9f);
                return false;
            }
            if (string.IsNullOrEmpty(item) || !a.Ammo.Has(item)) return false;
            a.Ammo.Consume(item);
            Record("fire", a.AimAngle, a.AimPower, item, targetPoint.x, targetPoint.y);
            DoFire(a, item, a.AimAngle, a.AimPower, targetPoint);
            Tutorial?.OnFired(item);
            return true;
        }

        /// <summary>Weapon mount position, pulled back toward the penguin if the mount sits inside terrain
        /// (so shots never start inside the ground). Used by firing (human, AI, network) and aim prediction.</summary>
        public static Vector2 ShotOrigin(Penguin a)
        {
            var mount = a.WeaponMount;
            Vector2 penguinPos = a.Position;
            Vector2 origin = mount != null ? (Vector2)mount.position : penguinPos;
            if (BattleTerrain.I != null && BattleTerrain.I.Raycast(penguinPos, origin, out var hit))
                origin = penguinPos + (hit - penguinPos) * 0.9f;
            return origin;
        }

        void DoFire(Penguin a, string item, float angle, float power, Vector2 target)
        {
            if (a.HeldItem != item) a.HoldItem(item);
            a.SetAim(angle, power);
            a.PlayFire();
            AttacksLeft--;
            Fired = true;
            // original practice simulation: after firing the turn has TimeAfterFiring left to retreat
            TurnTimeLeft = BattleRules.TimeAfterFiring;
            TurnLeadInLeft = 0;
            Vector2 origin = ShotOrigin(a);
            CurrentShot = WeaponSystem.Fire(a, item, origin, angle, power, target);
            Cam.FollowShot(CurrentShot);
            BattleEvents.RaiseWeaponFired(a.PlayerIndex, item);
            a.Aiming = false;
        }

        public bool ActBooster(string itemId)
        {
            if (!CanAct || BoosterUsedThisTurn || !Active.Ammo.Has(itemId)) return false;
            if (!WeaponSystem.UseBooster(Active, itemId)) return false;
            Active.Ammo.Consume(itemId);
            BoosterUsedThisTurn = true;
            Record("booster", s: itemId);
            BattleEvents.RaiseBoosterUsed(ActiveIndex, itemId);   // Boosters.Use plays the sound
            Tutorial?.OnBooster();
            return true;
        }

        /// <summary>Emotes may be used by any local penguin at any time (original EmoticonMessage).</summary>
        public void ActEmote(int index, string emoticonId)
        {
            var p = PenguinAt(index);
            if (p == null || !p.CanEmote) return;
            p.Emote(emoticonId);
            if (net != null && (index == net.LocalSlot || (p.Slot.isAI && OwnedLocally(index))))
                net.SendAction(new TurnAction { player = index, t = recordClock, type = "emote", s = emoticonId });
        }

        // ================================================================ pause / quit

        public void SetPaused(bool paused)
        {
            if (net != null) { Paused = false; return; }   // online matches keep running
            Paused = paused;
            Time.timeScale = paused ? 0f : 1f;
            if (paused && Active != null) Active.Walk(0);
        }

        /// <summary>Surrender / quit from the pause menu.</summary>
        public void Surrender()
        {
            if (CurrentPhase == Phase.Over) return;
            net?.SendLeave();
            Time.timeScale = 1f;
            var r = BuildResult(true);
            CurrentPhase = Phase.Over;
            Finish(r);
        }

        // ================================================================ end of match

        void EndMatch(bool aborted)
        {
            if (CurrentPhase == Phase.Over) return;
            CurrentPhase = Phase.Over;
            Ai.EndTurn();
            foreach (var p in Penguins) p.Walk(0);
            finalResult = BuildResult(aborted);
            var local = finalResult.Local;
            bool won = local != null && local.rank == 1;
            foreach (var pr in finalResult.players)
            {
                var p = PenguinAt(pr.slotIndex);
                if (p != null) p.SetResultPose(pr.rank == 1);
            }
            var winner = PenguinAt(finalResult.WinnerIndex);
            if (winner != null) Cam.FollowPenguin(winner);
            AudioManager.StopLoop("ambient");
            AudioManager.Music("ThemeMusicEnd");
            AudioManager.Sfx(won || PassAndPlay ? "SplashVictory" : "SplashDefeat");
            if (local != null) AudioManager.Sfx("Position_" + Mathf.Clamp(local.rank, 1, 4), 0.8f);
            Hud.ShowMatchOver(finalResult);
            if (Config.mode == BattleMode.Tutorial && !aborted)
            {
                ProfileService.P.tutorialDone = true;
                ProfileService.Save();
            }
            overTimer = 4f;
        }

        /// <summary>Skip the end-of-match pause (HUD Continue button).</summary>
        public void ContinueAfterMatch() { if (CurrentPhase == Phase.Over) overTimer = 0; }

        // ================================================================ crates

        void MaybeDropCrate()
        {
            if (!Config.powerUps || Level.powerUpPercentage <= 0) return;
            crates.RemoveAll(c => c == null);
            if (crates.Count >= 3) return;
            var r = new System.Random((Config.seed + 104729) * 17 + TurnNumber * 13);
            if (r.Next(100) >= Level.powerUpPercentage) return;
            for (int tries = 0; tries < 12; tries++)
            {
                float x = Mathf.Lerp(Level.size.x * 0.08f, Level.size.x * 0.92f, (float)r.NextDouble());
                if (!Terrain.GroundBelow(x, Level.size.y + 5f, out var g) || g.y < Terrain.WaterY + 1f) continue;
                float top = Level.cameraBounds.height > 0 ? Level.cameraBounds.yMax - 2f : Level.size.y;
                var pos = new Vector2(x, Mathf.Max(g.y + 4f, Mathf.Min(top, g.y + 25f)));
                DropCrate(PowerUpCrate.RandomType(r), pos, true);
                return;
            }
        }

        public PowerUpCrate DropCrate(string type, Vector2 pos, bool parachute)
        {
            var c = PowerUpCrate.Create(worldRoot, type, pos, parachute, this);
            crates.Add(c);
            return c;
        }

        void SpawnLevelPowerUps()
        {
            // power_ups placed in the original level file, each with appear_percentage
            string path = LevelId;
            var lr = GameData.Get("Level", LevelId);
            if (lr != null) path = lr.Str("LevelFile", path);
            var ta = Resources.Load<TextAsset>(path);
            if (ta == null) return;
            var d = MiniJson.ParseObject(ta.text);
            if (d == null || !(d.TryGetValue("power_ups", out var o) && o is List<object> list)) return;
            float h = Level.heightPx > 0 ? Level.heightPx : Level.size.y * Units.PX;
            foreach (var e in list)
            {
                if (!(e is Dictionary<string, object> pu)) continue;
                var rec = new Record("PowerUp", "", pu);
                string type = rec.Str("export_name");
                if (!PowerUpCrate.IsKnownType(type)) continue;
                if (rng.Next(100) >= rec.Int("appear_percentage", 100)) continue;
                DropCrate(type, Units.LevelToWorld(rec.Float("x"), rec.Float("y"), h), false);
            }
        }

        /// <summary>A penguin touched a crate (original PowerUpGameObject results).</summary>
        public void OnCratePicked(PowerUpCrate crate, Penguin p)
        {
            var pos = crate.transform.position;
            switch (crate.Type)
            {
                case "HealthCrate":
                    p.Heal(PowerUpCrate.HealAmount);
                    break;
                case "PointsCrate":
                    p.AddScore(PowerUpCrate.PointsAmount);
                    break;
                case "Treasure":
                    if (RewardsEnabled) p.Coins += PowerUpCrate.TreasureCoins;
                    Fx.FloatText((Vector2)pos + Vector2.up, "+" + PowerUpCrate.TreasureCoins, Theme.Coin, 1.1f);
                    if (IsRewardViewer(p)) Hud.ShowRewardPickups(pos, PowerUpCrate.TreasureCoins, 0);
                    AudioManager.Sfx("GetCoins");
                    break;
                default:
                    var r = new System.Random((Config.seed + 15485863) * 7 + TurnNumber * 3 + p.PlayerIndex);
                    var item = PowerUpCrate.RandomAmmo(r, p.Slot.level);
                    if (item != null)
                    {
                        int amount = 1 + r.Next(3);
                        p.Ammo.AddBonus(item, amount);
                        Fx.FloatText((Vector2)pos + Vector2.up, "+" + amount + " " + BattleItems.Name(item), Color.white, 1f);
                    }
                    AudioManager.Sfx("GetExp");
                    break;
            }
            Tutorial?.OnCratePicked(p);
        }

        // ================================================================ rewards

        /// <summary>Original RewardsHandler.damageDoneToTarget: coins/XP for damage to opponents (+kill bonus).</summary>
        /// <summary>from = where the damaged target is (the original popped the pickups out of it).</summary>
        public void GiveDamageRewards(Penguin attacker, int damage, bool killed, Vector2 from)
        {
            if (!RewardsEnabled || attacker == null || damage <= 0) return;
            float gold = damage * BattleRules.DamageToGold, exp = damage * BattleRules.DamageToExperience;
            if (killed) { gold += BattleRules.PenguinKillBonusGoldExp; exp += BattleRules.PenguinKillBonusGoldExp; }
            int coins = Mathf.FloorToInt(gold * attacker.CoinsBonus), xp = Mathf.FloorToInt(exp * attacker.ExpBonus);
            attacker.Coins += coins;
            attacker.Xp += xp;
            // RewardsHandler.generateGraphicsToPickUp: only the local player's own rewards are drawn
            if (IsRewardViewer(attacker)) Hud.ShowRewardPickups(from, coins, xp);
        }

        /// <summary>The penguin whose coins/XP the HUD counts (the one human on this device).</summary>
        public bool IsRewardViewer(Penguin p) => p != null && RewardsEnabled && !PassAndPlay && IsLocalHuman(p.PlayerIndex);

        BattleResult BuildResult(bool aborted)
        {
            var res = new BattleResult { config = Config, aborted = aborted };
            // ranking like battleserver sort_players: score, coins, experience, then id
            var order = new List<Penguin>(Penguins);
            order.Sort((a, b) =>
            {
                int c = b.Score.CompareTo(a.Score);
                if (c != 0) return c;
                c = b.Coins.CompareTo(a.Coins);
                if (c != 0) return c;
                c = b.Xp.CompareTo(a.Xp);
                return c != 0 ? c : a.PlayerIndex.CompareTo(b.PlayerIndex);
            });
            int n = order.Count;
            int lastScore = n > 0 ? order[n - 1].Score : 0;
            var ranks = new int[n];
            var coins = new int[n];
            var xp = new int[n];
            for (int i = 0; i < n; i++)
            {
                var p = order[i];
                int idx = p.PlayerIndex;
                ranks[idx] = i + 1;
                coins[idx] = p.Coins;
                xp[idx] = p.Xp;
                if (RewardsEnabled && !aborted)
                {
                    // battleserver add_position_bonus_reward / BattleResults.getPositionBonus
                    int rankForMultiplier = i + 1 + (4 - n);
                    float mult = BattleRules.RankMultiplier(rankForMultiplier);
                    coins[idx] += (int)(((p.Score - lastScore) * 0.25f + BattleRules.BonusCoinsModifier) * mult);
                    xp[idx] += (int)(((p.Score - lastScore) * 0.25f + BattleRules.BonusExpModifier) * mult);
                }
            }
            for (int i = 0; i < n; i++)
            {
                var p = Penguins[i];
                var pr = new PlayerResult
                {
                    slotIndex = i,
                    name = p.DisplayName,
                    score = p.Score,
                    kills = p.Kills,
                    deaths = p.Deaths,
                    damageDealt = p.DamageDealt,
                    rank = ranks[i],
                    coins = RewardsEnabled ? coins[i] : 0,
                    xp = RewardsEnabled ? xp[i] : 0,
                    cash = 0,
                    isLocal = IsLocalHuman(i)
                };
                p.Ammo.FillUsed(pr.usedItems);
                p.Ammo.FillEarned(pr.earnedItems);
                res.players.Add(pr);
            }
            return res;
        }

        // ================================================================ events

        // tiny repeated blasts (acid drops, goo bites) would make the camera jump around: only real explosions
        void OnExplosion(Vector2 pos, float radius) { if (Cam && radius >= 1.25f) Cam.LookAtExplosion(pos, radius); }

        void OnPenguinDamaged(int victim, int attacker, float amount, string item) => Ai?.OnDamaged(victim, attacker, amount);

        void OnPenguinKilled(int victim, int killer, string item) => Ai?.OnKilled(victim, killer);

        // ================================================================ cleanup

        void OnDestroy()
        {
            Cleanup();
            if (I == this) I = null;
        }

        bool cleaned;

        /// <summary>Idempotent: runs synchronously from Finish/Begin, and again (no-op) from OnDestroy.</summary>
        void Cleanup()
        {
            if (cleaned) return;
            cleaned = true;
            Time.timeScale = 1f;
            if (prevGravity != Vector2.zero) Physics2D.gravity = prevGravity;
            if (net != null)
            {
                net.ActionReceived -= OnNetAction;
                net.TurnEndReceived -= OnNetTurnEnd;
                net.PlayerLeft -= OnNetPlayerLeft;
                HookChat(false);
            }
            BattleEvents.Explosion -= OnExplosion;
            BattleEvents.PenguinDamaged -= OnPenguinDamaged;
            BattleEvents.PenguinKilled -= OnPenguinKilled;
            AudioManager.StopLoop("ambient");
            for (int i = 0; i < 4; i++) AudioManager.StopLoop("walk" + i);
            WeaponSystem.ClearAll();
            BattleWorld.Clear();
        }
    }
}
