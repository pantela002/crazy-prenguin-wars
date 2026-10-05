using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Computer opponents. Each AI turn: think, maybe use a booster, maybe walk/jump toward a target, pick a weapon,
    /// search angle/power with the weapon's trajectory prediction (spread over frames), add skill-based error,
    /// aim visibly, fire, then retreat from its own blast. Skill = PlayerSlot.aiSkill (0 easy, 1 normal, 2 hard).
    /// All actions go through BattleController.Act* like a human's (so online hosts record them).
    /// Safety rules: walking only in the first WalkBudget seconds and never toward water, the level edge or a drop;
    /// after ThinkBudget seconds (or when the turn clock gets short) it shoots from where it stands with the best
    /// shot found so far, and if a shot can't be made it falls back to any weapon it has, so every turn has a shot.
    /// </summary>
    public class BattleAI
    {
        enum Step { Idle, Think, Walk, Plan, Search, Lesson, Aim, Retreat, Done }

        readonly BattleController c;
        Penguin me, target;
        Step step;
        float timer, stuckTimer, lastX, emoteCooldown, turnClock, planWait;
        int skill, walkDir, replans, lessonTries;
        bool fallbackTried, lessonStarted;

        const float WalkBudget = 4.5f;     // seconds of the turn in which the AI may still walk
        const float ThinkBudget = 7f;      // after this, shoot from where it stands
        const float HurryTime = 3.5f;      // turn seconds left that force the shot (search + aim fit in this)
        string weapon;
        TargetingMode mode;
        Vector2 targetPos, impactGuess;
        bool straightShot;
        // search state
        readonly List<float> angles = new List<float>(64);
        readonly List<float> powers = new List<float>(16);
        int searchIndex;
        bool refining;
        float bestErr, bestAngle, bestPower;
        float aimFromAngle, aimFromPower, aimT, aimDuration, goalAngle, goalPower;
        readonly List<Vector2> pts = new List<Vector2>(256);
        readonly List<string> options = new List<string>(32);

        /// <summary>Utility items the AI cannot judge (where to teleport, where a wall helps, riding a broom over
        /// water): never chosen.</summary>
        static readonly HashSet<string> NeverUse = new HashSet<string> { "PointTeleport", "TeleportationGrenade", "ShieldWall", "Broom" };

        static readonly string[] HappyEmotes = { "EmoticonLaugh", "EmoticonWoot", "EmoticonTaunt", "EmoticonNice", "EmoticonTrollface" };
        static readonly string[] HurtEmotes = { "EmoticonOuch", "EmoticonAngry", "EmoticonCrying", "EmoticonWtf", "EmoticonScream" };

        public BattleAI(BattleController controller) { c = controller; }

        /// <summary>The tutorial's "opponent's turn" lesson: this shot should land on the player.</summary>
        bool Lesson => c.Tutorial != null && c.Tutorial.OpponentShouldHit;
        // the easy AI's +-7 degrees / 10% power made the tutorial opponent miss nearly every time
        float AngleError => Lesson ? 0f : skill == 0 ? 7f : skill == 1 ? 3.5f : 1.2f;
        float PowerError => Lesson ? 0f : skill == 0 ? 0.1f : skill == 1 ? 0.05f : 0.02f;

        public void BeginTurn(Penguin p)
        {
            me = p;
            skill = Mathf.Clamp(p.Slot.aiSkill, 0, 2);
            step = Step.Think;
            timer = Random.Range(0.7f, 1.3f);
            replans = 0;
            target = null;
            turnClock = 0;
            planWait = 0;
            fallbackTried = false;
            lessonTries = 0;
            lessonStarted = false;
            lessonPass = 0;
            lessonSpot = 0;
            lessonSpots.Clear();
        }

        public void EndTurn()
        {
            if (me != null && step != Step.Idle) me.Walk(0);
            step = Step.Idle;
        }

        public void Update(float dt)
        {
            if (me == null || !me.Alive || step == Step.Idle) return;
            if (emoteCooldown > 0) emoteCooldown -= dt;
            turnClock += dt;
            // the tutorial's lesson shot takes the time it needs (the turn has 30 s); only the turn clock hurries it
            bool hurry = !c.Fired && (c.TurnTimeLeft < HurryTime || (turnClock > ThinkBudget && !Lesson));
            bool mayWalk = !hurry && turnClock < WalkBudget && !Lesson;   // the lesson planner moves the AI itself

            switch (step)
            {
                case Step.Think:
                    timer -= dt;
                    if (timer > 0 && !hurry) break;
                    MaybeBooster();
                    target = PickTarget();
                    if (target == null) { step = Step.Done; break; }
                    if (mayWalk) StartWalkIfUseful(); else { step = Step.Plan; timer = 0f; planWait = 0; }
                    break;

                case Step.Walk:
                    timer -= dt;
                    c.ActWalk(walkDir);
                    if (Mathf.Abs(me.Position.x - lastX) < 0.02f) stuckTimer += dt; else stuckTimer = 0;
                    lastX = me.Position.x;
                    bool danger = DangerAhead(walkDir);
                    if (stuckTimer > 0.25f && me.Grounded && !danger) { c.ActJump(walkDir); stuckTimer = -0.6f; }
                    bool close = target == null || Mathf.Abs(target.Position.x - me.Position.x) < 9f;
                    if (timer <= 0 || me.ActionPoints <= 0 || close || !mayWalk || danger)
                    {
                        c.ActWalk(0);
                        step = Step.Plan;
                        timer = 0.35f;
                        planWait = 0;
                    }
                    break;

                case Step.Plan:
                    timer -= dt;
                    planWait += dt;
                    // wait to land/settle before simulating shots, but never longer than a second
                    if ((timer > 0 || !me.Grounded || me.Body.Vel().sqrMagnitude > 0.5f) && !hurry && planWait < 1f) break;
                    if (!target.Alive) target = PickTarget();
                    if (target == null) { step = Step.Done; break; }
                    if (Lesson && !hurry) { if (!lessonStarted) { lessonStarted = true; BeginLesson(); } else step = Step.Lesson; }
                    else PrepareShot();
                    break;

                case Step.Lesson:
                    if (hurry) { PrepareShot(); break; }
                    LessonStep();
                    break;

                case Step.Search:
                    int budget = hurry ? 12 : 6;   // simulations per frame (mobile budget)
                    while (budget-- > 0 && step == Step.Search) SearchStep();
                    if (hurry && step == Step.Search) FinishSearch();
                    break;

                case Step.Aim:
                    aimT += dt / aimDuration;
                    float k = Mathf.SmoothStep(0, 1, Mathf.Clamp01(aimT));
                    c.ActAim(Mathf.LerpAngle(aimFromAngle, goalAngle, k), Mathf.Lerp(aimFromPower, goalPower, k));
                    if (aimT >= 1f || c.TurnTimeLeft < 0.8f)
                    {
                        c.ActAim(goalAngle, goalPower);
                        // lesson: check the shot from where the muzzle really is now (it moves with the aim) and
                        // re-plan, or move, until it lands on the player
                        if (Lesson && c.TurnTimeLeft > HurryTime && !LessonHits(goalAngle, goalPower) && LessonReplan()) break;
                        if (c.ActFire(targetPos)) AfterFire();
                        else if (!FallbackFire()) step = Step.Done;
                    }
                    break;

                case Step.Retreat:
                    timer -= dt;
                    if (timer > 0) break;
                    // walk away from where the shot lands if it lands close
                    float dx = me.Position.x - impactGuess.x;
                    int away = dx >= 0 ? 1 : -1;
                    if (Mathf.Abs(dx) < 5f && me.ActionPoints > 40 && c.TurnTimeLeft > 1.2f && !DangerAhead(away))
                    {
                        c.ActWalk(away);
                        timer = 1.0f;
                        step = Step.Done;
                    }
                    else step = Step.Done;
                    break;

                case Step.Done:
                    timer -= dt;
                    if (me.Walking && (timer <= 0 || DangerAhead(me.WalkDir))) c.ActWalk(0);
                    // nothing was fired (no target found yet, no usable weapon, a failed shot): still take a shot
                    if (!c.Fired && c.AttacksLeft > 0 && !fallbackTried && (hurry || timer <= -1f)) FallbackFire();
                    break;
            }
        }

        // ---------------------------------------------------------------- decisions

        Penguin PickTarget()
        {
            Penguin best = null;
            float bestScore = float.MaxValue;
            foreach (var p in c.Penguins)
            {
                if (!p.Alive || !c.AreEnemies(me, p)) continue;
                float d = Vector2.Distance(p.Position, me.Position);
                float s;
                if (skill == 0) s = d + Random.Range(0f, 25f);                  // easy: roughly closest
                else if (skill == 1) s = d + p.HP * 0.05f;                       // normal: closest
                else s = d * 0.5f + p.HP * 0.15f - p.Score * 0.05f;              // hard: weak, close, leading
                if (s < bestScore) { bestScore = s; best = p; }
            }
            return best;
        }

        void MaybeBooster()
        {
            if (c.BoosterUsedThisTurn || me.Ammo.Boosters.Count == 0) return;
            float chance = skill == 0 ? 0.1f : skill == 1 ? 0.2f : 0.3f;
            if (me.HP < me.MaxHP * 0.5f && me.Ammo.Has("Bandage") && c.ActBooster("Bandage")) return;
            if (Random.value > chance) return;
            var b = me.Ammo.Boosters[Random.Range(0, me.Ammo.Boosters.Count)];
            if (b == "Kamikaze" || b == "Confetti" || b == "Banner") return;   // self-destructive / cosmetic
            if (me.Ammo.Has(b)) c.ActBooster(b);
        }

        void StartWalkIfUseful()
        {
            float dist = Mathf.Abs(target.Position.x - me.Position.x);
            float walkChance = skill == 0 ? 0.35f : 0.5f;
            int dir = target.Position.x > me.Position.x ? 1 : -1;
            if (dist < 8f) dir = -dir;      // too close: back off a little
            if ((dist > 22f || Random.value < walkChance * 0.4f) && me.ActionPoints > 100 && replans == 0 && !DangerAhead(dir))
            {
                walkDir = dir;
                timer = Random.Range(0.6f, 1.6f);
                lastX = me.Position.x;
                stuckTimer = 0;
                step = Step.Walk;
            }
            else { step = Step.Plan; timer = 0.1f; planWait = 0; }
        }

        /// <summary>Walking this way soon reaches water, the level edge or a drop of more than a few metres.</summary>
        bool DangerAhead(int dir)
        {
            var t = BattleTerrain.I;
            if (t == null || me == null || dir == 0) return false;
            var pos = me.Position;
            float feet = pos.y - BattleRules.Radius;
            var lvl = t.Level;
            for (float d = 1f; d <= 3.01f; d += 1f)
            {
                float ax = pos.x + dir * d;
                if (lvl != null && (ax < 1f || ax > lvl.size.x - 1f)) return true;
                if (!t.GroundBelow(ax, pos.y + 2f, out var g)) return true;     // nothing below: void or open water
                if (g.y < t.WaterY + 1f) return true;                           // shore too close to the water
                if (feet - g.y > 4f) return true;                               // a ledge we would fall from
            }
            return false;
        }

        /// <summary>
        /// Last resort when the planned shot didn't happen: any weapon with ammo, aimed straight at the target (or at
        /// 45 degrees toward it for lobbed weapons) from where the penguin stands. Once per turn.
        /// </summary>
        bool FallbackFire()
        {
            if (fallbackTried || c.Fired || c.AttacksLeft <= 0) return false;
            fallbackTried = true;
            if (target == null || !target.Alive) target = PickTarget();
            if (target == null) return false;   // nobody to shoot at
            Vector2 tp = target.Position;
            string w = weapon != null && me.Ammo.Has(weapon) && WeaponSystem.Targeting(weapon) != TargetingMode.Activation ? weapon : null;
            if (w == null)
                foreach (var id in me.Ammo.Weapons)
                    if (me.Ammo.Has(id) && id != "Punch" && !NeverUse.Contains(id) && WeaponSystem.MeleeReach(id) <= 0f
                        && WeaponSystem.Targeting(id) != TargetingMode.Activation) { w = id; break; }
            if (w == null) w = me.Ammo.DefaultWeapon();
            if (w == null) { step = Step.Done; timer = 0; return false; }
            weapon = w;
            mode = WeaponSystem.Targeting(w);
            c.ActSelectWeapon(w);
            var d = tp - Origin();
            float ang = mode == TargetingMode.PowerBar ? (d.x >= 0 ? 45f : 135f) : Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            float pow = mode == TargetingMode.PowerBar ? Mathf.Clamp(0.35f + Mathf.Abs(d.x) / 60f, 0.35f, 1f) : 1f;
            c.ActAim(ang, pow);
            targetPos = tp;
            impactGuess = tp;
            if (c.ActFire(tp)) { AfterFire(); return true; }
            step = Step.Done;
            timer = 0;
            return false;
        }

        void PrepareShot()
        {
            weapon = ChooseWeapon();
            if (weapon == null) { step = Step.Done; return; }
            mode = WeaponSystem.Targeting(weapon);
            int dir = target.Position.x >= me.Position.x ? 1 : -1;
            me.SetFacing(dir);
            targetPos = target.Position + Vector2.up * 0.2f;

            if (mode == TargetingMode.Point || mode == TargetingMode.Activation)
            {
                float err = AngleError * 0.15f;
                targetPos += new Vector2(Random.Range(-err, err), 0);
                bestAngle = dir > 0 ? 45f : 135f;
                bestPower = 1f;
                impactGuess = mode == TargetingMode.Activation ? me.Position : targetPos;
                StartAim();
                return;
            }

            // search grid: angles measured from the facing direction, mirrored for left
            angles.Clear(); powers.Clear();
            straightShot = false;
            bool arc = WeaponSystem.PredictTrajectory(weapon, Origin(), dir > 0 ? 45f : 135f, 1f, pts);
            if (mode == TargetingMode.Aiming && !arc)
            {
                // guns without an arc: aim straight at the target
                var d = target.Position - Origin();
                bestAngle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                bestPower = 1f;
                straightShot = true;
                impactGuess = target.Position;
                ApplyError();
                StartAim();
                return;
            }
            for (float a = -40f; a <= 86f; a += 6f) angles.Add(dir > 0 ? a : 180f - a);
            if (mode == TargetingMode.Aiming) powers.Add(1f);
            else for (float p = 0.25f; p <= 1.001f; p += 0.075f) powers.Add(p);
            searchIndex = 0;
            refining = false;
            bestErr = float.MaxValue;
            bestAngle = dir > 0 ? 45f : 135f;
            bestPower = 0.7f;
            step = Step.Search;
        }

        Vector2 Origin() => BattleController.ShotOrigin(me);   // same clamped origin DoFire uses

        /// <summary>Nothing solid between the top of the level and the target (orbital strikes hit the first thing).</summary>
        static bool SkyClear(Vector2 p)
        {
            var t = BattleTerrain.I;
            if (t == null || t.Level == null) return true;
            var top = new Vector2(p.x, Mathf.Max(t.Level.size.y, p.y) + 2f);
            return !t.Raycast(top, p + Vector2.up * 1.2f, out _);
        }

        string ChooseWeapon()
        {
            options.Clear();
            float dist = Vector2.Distance(target.Position, me.Position);
            foreach (var w in me.Ammo.Weapons)
            {
                if (!me.Ammo.Has(w)) continue;
                var tm = WeaponSystem.Targeting(w);
                var rec = GameData.Item(w);
                float range = rec != null ? Units.W(rec.Float("SimulationDistance", 2000)) : 100f;
                if (w == "Punch") { if (dist < 3f) return w; continue; }
                if (NeverUse.Contains(w)) continue;
                if (tm == TargetingMode.Activation) continue;          // dropped in place: not useful from range
                float melee = WeaponSystem.MeleeReach(w);
                if (melee > 0f) { if (dist <= melee + 1.5f) options.Add(w); continue; }   // Scythe, Wand of Wind: only up close
                if (tm == TargetingMode.Point && WeaponSystem.FromSky(w) && !SkyClear(target.Position)) continue;
                if (tm == TargetingMode.Aiming && range > 1f && dist > range * 1.2f) continue;
                options.Add(w);
            }
            if (options.Count == 0) return me.Ammo.Has("Punch") ? "Punch" : null;
            // lesson shot: a lobbed weapon, whose path the search simulates against the terrain (a gun's straight
            // line at the target can be blocked by a hill between the penguins)
            if (Lesson) foreach (var w in options) if (WeaponSystem.Targeting(w) == TargetingMode.PowerBar) return w;
            if (skill == 0) return options[Random.Range(0, Mathf.Min(options.Count, 3))];
            // normal/hard: prefer stronger (higher RequiredLevel) weapons, hard spends rare ammo
            string best = options[0];
            float bestScore = float.MinValue;
            foreach (var w in options)
            {
                float s = BattleItems.RequiredLevel(w) * (skill == 2 ? 1f : 0.4f) + Random.Range(0f, 6f);
                if (me.Ammo.Count(w) == Loadout.Infinite) s += skill == 2 ? 0f : 3f;
                if (s > bestScore) { bestScore = s; best = w; }
            }
            return best;
        }

        void SearchStep()
        {
            int total = angles.Count * powers.Count;
            if (searchIndex >= total)
            {
                if (!refining && bestErr < float.MaxValue)
                {
                    // refine around the best coarse solution
                    refining = true;
                    float a0 = bestAngle, p0 = bestPower;
                    angles.Clear(); powers.Clear();
                    for (int i = -3; i <= 3; i++) angles.Add(a0 + i * 1.5f);
                    if (mode == TargetingMode.Aiming) powers.Add(1f);
                    else for (int i = -3; i <= 3; i++) powers.Add(Mathf.Clamp01(p0 + i * 0.02f));
                    searchIndex = 0;
                    return;
                }
                FinishSearch();
                return;
            }
            float ang = angles[searchIndex / powers.Count];
            float pow = powers[searchIndex % powers.Count];
            searchIndex++;
            float err = Evaluate(ang, pow, out var impact);
            if (err < bestErr) { bestErr = err; bestAngle = ang; bestPower = pow; impactGuess = impact; }
        }

        /// <summary>How far the predicted shot ends from the target (plus a penalty for hurting ourselves).</summary>
        float Evaluate(float angle, float power, out Vector2 impact)
        {
            impact = me.Position;
            if (!AimPredict(weapon, Origin(), angle, power, pts, out impact)) return float.MaxValue;
            float best = Vector2.Distance(impact, targetPos);
            // a missile that ends in the water or off the level is removed without exploding: a shot landing in the
            // water right beside a penguin on the shore used to rate as a near hit and did no damage at all
            if (Dud(impact)) return best + 1000f;
            // a path grazing the target's body also counts (the prediction's penguin collision can miss a sliver);
            // passing a few metres over its head does not
            float graze = BattleRules.Radius * 1.2f;
            for (int i = 0; i < pts.Count; i += 2)
            {
                float d = Vector2.Distance(pts[i], targetPos);
                if (d < graze && d < best) best = d;
            }
            if (Vector2.Distance(impact, me.Position) < 3.5f) best += 25f;
            return best;
        }

        static bool Dud(Vector2 impact) => WorldQuery.InWater(impact) || WorldQuery.OutOfWorld(impact);

        // ---------------------------------------------------------------- tutorial lesson shot
        //
        // The opponent's first turn in the tutorial must hit the player. The normal planner rates a shot by how close
        // its predicted impact comes to the target, so a shot into cover next to the player (a plank, the box tower)
        // rated as nearly right; and it simulated from the muzzle where it was during the search, which moves with
        // the aim. The lesson planner instead accepts only shots whose prediction (the same CircleCast queries a real
        // missile collides through: terrain, level objects, crates, penguins) explodes on the player, simulated from
        // the muzzle as it is at that very aim (the sprite penguin's muzzle follows the aim clip). When no such shot
        // exists from where the AI stands it moves to a spot that has one, then accepts a looser hit (inside the blast).

        /// <summary>Lesson hit radii in order: on the player, then anywhere the blast still hurts (damage radius 7.5).</summary>
        static readonly float[] LessonRadii = { 2.5f, 5.5f };
        int lessonPass, lessonSpot;
        readonly List<Vector2> lessonSpots = new List<Vector2>(48);
        static readonly Collider2D[] overlap = new Collider2D[8];

        float LessonRadius => LessonRadii[Mathf.Min(lessonPass, LessonRadii.Length - 1)];

        /// <summary>Start the lesson planner (called instead of PrepareShot while Lesson is true).</summary>
        void BeginLesson()
        {
            weapon = ChooseWeapon();
            if (weapon == null) { step = Step.Done; return; }
            mode = WeaponSystem.Targeting(weapon);
            if (mode != TargetingMode.PowerBar) { PrepareShot(); return; }   // no lob weapon: the normal planner
            lessonSpot = 0;
            BuildLessonSpots();
            step = Step.Lesson;
        }

        /// <summary>
        /// One slice of the lesson planner per frame: solve from here (slot 0), else try the next spot (one per frame);
        /// after all spots the next, looser radius; after the last radius the normal planner fires its best shot.
        /// </summary>
        void LessonStep()
        {
            if (target == null || !target.Alive) { PrepareShot(); return; }
            if (lessonSpot == 0)
            {
                lessonSpot = 1;
                if (LessonSolve(LessonRadius, false, out goalAngle, out goalPower)) LessonAim();
                return;
            }
            int i = lessonSpot - 1;
            if (i < lessonSpots.Count)
            {
                lessonSpot++;
                if (TrySpot(lessonSpots[i], LessonRadius))
                {
                    // land and settle in Plan, then solve again from the settled spot (BeginLesson is not re-run:
                    // the spot list and pass carry on)
                    lessonSpot = 0;
                    step = Step.Plan;
                    timer = 0.4f;
                    planWait = 0;
                }
                return;
            }
            lessonPass++;
            lessonSpot = 0;
            if (lessonPass >= LessonRadii.Length) PrepareShot();
        }

        void LessonAim()
        {
            bestAngle = goalAngle; bestPower = goalPower;
            straightShot = false;
            targetPos = target.Position;
            impactGuess = target.Position;
            StartAim();
        }

        /// <summary>
        /// Standing spots beside the player (AI's side first), 6-30 m away: every floor in each column (the top of an
        /// island and the ground under it), above the water and inside the level, with headroom and no prop there.
        /// </summary>
        void BuildLessonSpots()
        {
            lessonSpots.Clear();
            var t = BattleTerrain.I;
            if (t == null || t.Level == null || target == null) return;
            Vector2 tp = target.Position;
            int near = me.Position.x >= tp.x ? 1 : -1;
            float r = BattleRules.Radius, top = t.Level.size.y + 5f;
            foreach (int side in new[] { near, -near })
                for (float d = 6f; d <= 30.01f; d += 2f)
                {
                    float x = tp.x + side * d;
                    if (x < 2f || x > t.Level.size.x - 2f) continue;
                    float y = top;
                    for (int floor = 0; floor < 6 && t.GroundBelow(x, y, out var g) && g.y > t.WaterY + 1f; floor++)
                    {
                        var pos = g + Vector2.up * (r * 1.15f);
                        if (pos.y < t.Level.size.y - 1f && !t.IsSolid(pos) && !t.IsSolid(pos + Vector2.up * (r * 1.6f))
                            && !InsideProp(pos, r * 0.9f) && Vector2.Distance(pos, tp) > 5f)
                            lessonSpots.Add(pos);
                        // the next floor: below the solid run under this one
                        y = g.y - 0.25f;
                        while (y > t.WaterY && t.IsSolid(new Vector2(x, y))) y -= 0.25f;
                    }
                }
        }

        /// <summary>A solid collider that is not a penguin (prop, crate) overlaps the circle.</summary>
        static bool InsideProp(Vector2 pos, float radius)
        {
            System.Array.Clear(overlap, 0, overlap.Length);
            int n = Physics2D.OverlapCircle(pos, radius, Phys.AllFilter, overlap);
            for (int i = 0; i < n; i++)
            {
                var c = overlap[i];
                if (c == null || c.isTrigger || WorldQuery.IsTerrain(c)) continue;
                if (WorldQuery.FindDamageable(c) is IPenguin) continue;
                return true;
            }
            return false;
        }

        /// <summary>Move the AI to pos; keep it there when a lesson shot from there hits, else put it back.</summary>
        bool TrySpot(Vector2 pos, float radius)
        {
            var home = me.Position;
            me.Teleport(pos);
            me.SetFacing(target.Position.x >= pos.x ? 1 : -1);
            if (LessonSolve(radius, true, out _, out _))
            {
                Fx.Smoke(home, 1.6f);
                Fx.Glow(pos, 1.6f, Color.white);
                return true;
            }
            me.Teleport(home);
            return false;
        }

        /// <summary>Muzzle position when aiming at angle (the aim moves the muzzle); the aim is put back after.</summary>
        Vector2 OriginAt(float angle, float power)
        {
            float a0 = me.AimAngle, p0 = me.AimPower;
            me.SetAim(angle, power);
            var o = Origin();
            me.SetAim(a0, p0);
            return o;
        }

        /// <summary>
        /// A lob from the AI's muzzle that explodes within radius of the player with nothing in the way: seeded by the
        /// two ballistic angles for each power, refined around each seed, every candidate simulated from the muzzle at
        /// its own aim. quick = fewer candidates, first hit wins (spot checks); otherwise the closest hit.
        /// </summary>
        bool LessonSolve(float radius, bool quick, out float angle, out float power)
        {
            angle = 45f; power = 1f;
            if (target == null || weapon == null) return false;
            Vector2 tp = target.Position;
            int dir = tp.x >= me.Position.x ? 1 : -1;
            me.SetAim(dir > 0 ? 45f : 135f, me.AimPower);   // face the player before measuring the muzzle
            float g = Mathf.Abs(Physics2D.gravity.y);
            if (g < 0.01f) return false;
            float[] offs = quick ? QuickOffsets : FineOffsets;
            float best = float.MaxValue;
            bool found = false;
            for (float p = 1f; p >= 0.299f; p -= 0.05f)
            {
                float v = WeaponSystem.LaunchSpeed(weapon, p);
                if (v <= 0f) continue;
                var d = tp - OriginAt(dir > 0 ? 45f : 135f, p);
                float x = Mathf.Max(0.5f, Mathf.Abs(d.x)), v2 = v * v;
                float disc = v2 * v2 - g * (g * x * x + 2f * d.y * v2);
                if (disc < 0f) continue;
                float s = Mathf.Sqrt(disc);
                for (int k = 0; k < 2; k++)
                {
                    float seed = Mathf.Atan2(k == 0 ? v2 + s : v2 - s, g * x) * Mathf.Rad2Deg;   // high lob first
                    foreach (float off in offs)
                    {
                        float a = seed + off;
                        if (a < -60f || a > 89f) continue;
                        float wa = dir > 0 ? a : 180f - a;
                        if (!AimPredict(weapon, OriginAt(wa, p), wa, p, pts, out var impact) || Dud(impact)) continue;
                        float miss = Vector2.Distance(impact, tp);
                        if (miss > radius || Vector2.Distance(impact, me.Position) < 4f) continue;
                        if (miss < best) { best = miss; angle = wa; power = p; found = true; }
                        if (quick) return true;
                    }
                }
            }
            return found;
        }

        // a player in cover is often reachable only by a narrow steep lob: spot checks refine as much as the final
        // solve, they only stop at the first hit
        static readonly float[] QuickOffsets = { 0f, -1f, 1f, -2f, 2f, -3.5f, 3.5f, -5f, 5f };
        static readonly float[] FineOffsets = QuickOffsets;

        /// <summary>The lesson shot, fired from the muzzle's current position, explodes on the player.</summary>
        bool LessonHits(float angle, float power)
        {
            if (target == null || !target.Alive || weapon == null) return true;   // nothing to verify against
            if (!AimPredict(weapon, Origin(), angle, power, pts, out var impact) || Dud(impact)) return false;
            return Vector2.Distance(impact, target.Position) <= LessonRadius;
        }

        /// <summary>The aimed lesson shot would miss (a prop moved, the AI settled): solve again from here, twice at most.</summary>
        bool LessonReplan()
        {
            lessonTries++;
            if (lessonTries > 2 || target == null) return false;
            if (!LessonSolve(LessonRadius, false, out goalAngle, out goalPower)) return false;
            LessonAim();
            return true;
        }

        /// <summary>Predicted impact point of a shot. One simulation per call: with a buffer the path is simulated
        /// (WeaponSystem.PredictTrajectory) and its last point is the impact; without one only PredictImpact runs.</summary>
        public static bool AimPredict(string item, Vector2 origin, float angle, float power, List<Vector2> buffer, out Vector2 impact)
        {
            impact = origin;
            if (buffer != null && WeaponSystem.PredictTrajectory(item, origin, angle, power, buffer) && buffer.Count > 0)
            {
                impact = buffer[buffer.Count - 1];
                return true;
            }
            // no path (activation items, no buffer): impact only
            return WeaponSystem.PredictImpact(item, origin, angle, power, out impact, out _);
        }

        void FinishSearch()
        {
            bool reachable = bestErr < 6f;
            int toward = target.Position.x > me.Position.x ? 1 : -1;
            if (!reachable && !Lesson && replans == 0 && me.ActionPoints > 150 && c.TurnTimeLeft > HurryTime + 2.5f
                && turnClock < WalkBudget - 1f && !DangerAhead(toward))
            {
                // can't reach from here: walk toward the target and try again
                replans++;
                walkDir = toward;
                timer = Random.Range(0.8f, 1.4f);
                lastX = me.Position.x;
                step = Step.Walk;
                return;
            }
            ApplyError();
            StartAim();
        }

        void ApplyError()
        {
            bestAngle += Random.Range(-AngleError, AngleError);
            if (!straightShot && mode == TargetingMode.PowerBar) bestPower = Mathf.Clamp01(bestPower + Random.Range(-PowerError, PowerError));
        }

        void StartAim()
        {
            c.ActSelectWeapon(weapon);
            aimFromAngle = me.AimAngle;
            aimFromPower = me.AimPower;
            goalAngle = bestAngle;
            goalPower = bestPower;
            aimT = 0;
            // the visible aim never eats the time the shot needs
            aimDuration = Mathf.Clamp(Mathf.Min(Random.Range(0.8f, 1.3f), c.TurnTimeLeft - 1.2f), 0.2f, 1.3f);
            step = Step.Aim;
        }

        void AfterFire()
        {
            step = Step.Retreat;
            timer = 0.5f;
            if (Random.value < 0.25f) Emote(HappyEmotes);
        }

        void Emote(string[] set)
        {
            if (me == null || emoteCooldown > 0) return;
            emoteCooldown = 6f;
            c.ActEmote(me.PlayerIndex, set[Random.Range(0, set.Length)]);
        }

        // ---------------------------------------------------------------- reactions (any locally run AI)

        float reactCooldown;

        public void OnDamaged(int victim, int attacker, float amount)
        {
            var v = c.PenguinAt(victim);
            if (v == null || !v.Slot.isAI || !c.OwnedLocally(victim) || amount < 10 || Time.time < reactCooldown) return;
            if (Random.value < 0.18f)
            {
                reactCooldown = Time.time + 5f;
                c.ActEmote(victim, HurtEmotes[Random.Range(0, HurtEmotes.Length)]);
            }
        }

        public void OnKilled(int victim, int killer)
        {
            var k = c.PenguinAt(killer);
            if (k == null || killer == victim || !k.Slot.isAI || !c.OwnedLocally(killer) || Time.time < reactCooldown) return;
            if (Random.value < 0.5f)
            {
                reactCooldown = Time.time + 5f;
                c.ActEmote(killer, HappyEmotes[Random.Range(0, HappyEmotes.Length)]);
            }
        }
    }
}
