using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// One penguin in battle (original PlayerGameObject): physics body, HP, stats from worn clothes,
    /// walking/jumping with action points, damage, score, death and respawn, status effects and the 3D avatar.
    /// The turn logic (BattleController) decides when it may act.
    /// </summary>
    public class Penguin : MonoBehaviour, IPenguin
    {
        // ---------- IPenguin ----------
        public int PlayerIndex { get; private set; }
        public PlayerSlot Slot { get; private set; }
        public float HP { get; private set; }
        public float MaxHP { get; private set; }
        public int Facing { get; private set; } = 1;
        public Transform WeaponMount => Avatar != null && Avatar.Muzzle != null ? Avatar.Muzzle : mount;
        public StatBlock Stats { get; } = new StatBlock();
        public Vector2 Position => rb != null ? rb.position : (Vector2)transform.position;
        public Rigidbody2D Body => rb;
        public bool Alive => alive && !Left;

        // ---------- battle state ----------
        public PenguinAvatar Avatar { get; private set; }
        public Loadout Ammo { get; private set; }
        public Color TeamColor { get; private set; }
        public string DisplayName => Slot != null ? Slot.name : "Penguin";
        public int Score { get; private set; }
        public int Kills, Deaths, DamageDealt, Coins, Xp;
        public bool Left;                       // left an online match
        public float DiedAt { get; private set; }
        public bool Grounded { get; private set; }
        public bool Walking => walkDir != 0;
        public int WalkDir => walkDir;
        public float ActionPoints;              // energy for walking/jumping this turn (BattleOptions.DefaultActionPoints)
        public int MaxActionPoints { get; private set; }
        public int JumpCost { get; private set; }
        public float AimAngle { get; private set; } = 45f;   // world degrees
        public float AimPower { get; private set; } = 0.5f;
        public bool Aiming;                     // HUD/AI: penguin holds a weapon ready
        public string HeldItem { get; private set; }
        public bool PendingSuicide { get; private set; }
        public float WalkedDistance { get; private set; }     // units walked this battle (for reports)
        /// <summary>Last player that hurt or pushed this penguin (original Tagger); self after it acts.</summary>
        public int LastTagger { get; private set; } = -1;

        // modifiers from Bonus stats (clothes/trophy)
        float jumpPowerMod, maxSpeedPxMod;
        public float ExpBonus = 1f, CoinsBonus = 1f;

        Rigidbody2D rb;
        CircleCollider2D col;
        Transform mount;
        bool alive = true;
        int walkDir;
        float lastX;
        float hurtTimer, fireTimer, invulnerableUntil, lastJumpTime = -10f, hiddenAt = -1f;
        string lastItem;
        bool drowned;
        AvatarState shownState = (AvatarState)(-1);
        int shownFacing;

        // damage is collected for a moment like the original CumulativeDamage before scores/floaters
        const float DamageCollectTime = 0.35f;
        readonly float[] pendingDamage = new float[9];      // index attacker+1 (0 = world)
        float pendingTimer;
        int pendingScore, pendingKillScore;
        float scoreTimer;

        readonly Dictionary<string, int> effects = new Dictionary<string, int>();
        readonly List<string> effectKeys = new List<string>();

        static PhysicsMaterial2D walkMat, idleMat;
        static readonly Collider2D[] overlap = new Collider2D[8];
        static ContactFilter2D groundFilter;

        public static readonly Color DamageColor = new Color(1f, 0.3f, 0.25f);
        public static readonly Color HealColor = new Color(0.4f, 1f, 0.4f);
        public static readonly Color ScoreColor = new Color(1f, 0.85f, 0.2f);

        /// <summary>Create the penguin for a slot at a world position.</summary>
        public static Penguin Create(Transform parent, PlayerSlot slot, int index, Vector2 pos, BattleMode mode)
        {
            var go = new GameObject("Penguin " + index + " " + slot.name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var p = go.AddComponent<Penguin>();
            p.Init(slot, index, mode);
            return p;
        }

        void Init(PlayerSlot slot, int index, BattleMode mode)
        {
            Slot = slot;
            PlayerIndex = index;
            TeamColor = Theme.PlayerColors[Mathf.Abs(slot.colorIndex) % Theme.PlayerColors.Length];

            if (walkMat == null)
            {
                walkMat = new PhysicsMaterial2D("PenguinWalk") { friction = BattleRules.Friction, bounciness = 0 };
                idleMat = new PhysicsMaterial2D("PenguinIdle") { friction = 4f, bounciness = 0 };
                groundFilter = new ContactFilter2D { useTriggers = false };
            }

            rb = gameObject.AddComponent<Rigidbody2D>();
            rb.mass = BattleRules.PenguinMass;
            rb.gravityScale = 1f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.SetDrag(0.05f, 0f);
            col = gameObject.AddComponent<CircleCollider2D>();
            col.radius = BattleRules.Radius;
            col.sharedMaterial = idleMat;

            mount = new GameObject("WeaponMount").transform;
            mount.SetParent(transform, false);
            mount.localPosition = new Vector3(BattleRules.Radius * 0.8f, BattleRules.Radius * 0.2f, 0);

            float r = BattleRules.Radius;
            Avatar = PenguinAvatar.Create(transform, r * 2.3f, TeamColor);
            Avatar.transform.localPosition = new Vector3(0, -r, 0);
            Avatar.SetClothes(slot.head, slot.chest, slot.feet);
            Avatar.SetTeamColor(TeamColor);

            ApplyBonusStats();
            MaxHP = BattleRules.HitPoints;
            HP = MaxHP;
            Ammo = Loadout.For(slot, mode);
            ActionPoints = MaxActionPoints;
            lastX = transform.position.x;
        }

        /// <summary>Clothes/trophy stat bonuses (Bonus section: Attack/Defence/Luck/ImpulseResistance and movement mods).</summary>
        void ApplyBonusStats()
        {
            float jumpPower = BattleRules.JumpPower, maxSpeed = BattleRules.MaxSpeedPx, walkMul = 1f;
            float ap = BattleRules.DefaultActionPoints, jumpCost = BattleRules.JumpCost;
            float impulseMul = 1f;
            var ids = new[] { Slot.head, Slot.chest, Slot.feet, Slot.trophy };
            foreach (var id in ids)
            {
                var b = GameData.Get("Bonus", id);
                if (b == null) continue;
                foreach (var kv in b.Raw)
                {
                    if (kv.Key == "ID" || !(kv.Value is string s)) continue;
                    var m = StatMod.Parse(s);
                    switch (kv.Key)
                    {
                        // typed rows ("Add:6:Fire" FlameBadge, "Multiply:1.50:object" CreativityMedal) count only
                        // when the hit matches the tag (StatBlock.TagMatches)
                        case "Attack": if (string.IsNullOrEmpty(m.Tag)) Stats.attack = m.Apply(Stats.attack); else Stats.typedAttack.Add(m); break;
                        case "Defence": if (string.IsNullOrEmpty(m.Tag)) Stats.defence = m.Apply(Stats.defence); else Stats.typedDefence.Add(m); break;
                        case "Luck": Stats.luck = m.Apply(Stats.luck); break;
                        case "ImpulseResistance":
                            if (m.Op == "Multiply") impulseMul *= m.Value; else Stats.impulseResistance = m.Apply(Stats.impulseResistance);
                            break;
                        case "JumpPower": jumpPower = m.Op == "Add" ? jumpPower * (1 + m.Value / 1000f) : m.Apply(jumpPower); break;
                        case "WalkSpeed": walkMul = m.Op == "Add" ? walkMul * (1 + m.Value / 1500f) : walkMul * m.Value; break;
                        case "MaxSpeed": maxSpeed = m.Apply(maxSpeed); break;
                        case "DefaultActionPoints": ap = m.Apply(ap); break;
                        case "ActionPointsJumpCost": jumpCost = m.Apply(jumpCost); break;
                        case "ExpBonus": ExpBonus = m.Apply(ExpBonus); break;
                        case "CoinsBonus": CoinsBonus = m.Apply(CoinsBonus); break;
                    }
                }
            }
            Stats.defence = Mathf.Max(Stats.defence, BattleRules.DefenceStatMin);
            if (impulseMul != 1f) Stats.impulseResistance = Stats.impulseResistance * impulseMul + (impulseMul - 1f) * 20f;
            Stats.impulseResistance = Mathf.Clamp(Stats.impulseResistance, -100f, 90f);
            jumpPowerMod = Mathf.Max(BattleRules.JumpPower * 0.3f, jumpPower);
            maxSpeedPxMod = Mathf.Clamp(maxSpeed * Mathf.Clamp(walkMul, 0.3f, 2f), BattleRules.MaxSpeedPx * 0.3f, BattleRules.MaxSpeedPx * 2f);
            MaxActionPoints = Mathf.Max(100, Mathf.RoundToInt(ap));
            JumpCost = Mathf.Max(0, Mathf.RoundToInt(jumpCost));
        }

        // ================= turn actions (called by controller / input / AI / network replay) =================

        /// <summary>Called at the start of this penguin's turn: refill action points.</summary>
        public void BeginTurn()
        {
            ActionPoints = MaxActionPoints;
            walkDir = 0;
            Aiming = false;
            MirrorAimToFacing();
            LastTagger = PlayerIndex;   // original activate(): acting resets the suicide tagger
        }

        public void EndTurn()
        {
            Walk(0);
            Aiming = false;
            HoldItem(null);
        }

        public bool CanWalk => Alive && ActionPoints > 0;
        public bool CanJump => Alive && Grounded && ActionPoints >= JumpCost && Time.time - lastJumpTime > 1f;   // JUMP_COOLDOWN 1000 ms

        /// <summary>-1 left, 0 stop, +1 right.</summary>
        public void Walk(int dir)
        {
            dir = dir > 0 ? 1 : (dir < 0 ? -1 : 0);
            if (dir != 0 && !CanWalk) dir = 0;
            if (dir == walkDir) return;
            walkDir = dir;
            if (dir != 0)
            {
                SetFacing(dir);
                MirrorAimToFacing();
                LastTagger = PlayerIndex;
                lastX = Position.x;
            }
            if (col) col.sharedMaterial = dir != 0 ? walkMat : idleMat;
        }

        /// <summary>Jump; dirX is the horizontal part of the direction (-1..1), vertical is up. Returns false if not possible.</summary>
        public bool Jump(float dirX)
        {
            if (!CanJump) return false;
            ActionPoints = Mathf.Max(0, ActionPoints - JumpCost);
            var dir = new Vector2(Mathf.Clamp(dirX, -1f, 1f) * BattleRules.JumpAngle, 1f).normalized;   // original (±JumpAngle, -1) normalised
            if (Mathf.Abs(dirX) > 0.01f) SetFacing(dirX > 0 ? 1 : -1);
            float speed = Units.W(BattleRules.JumpSpeedPx(jumpPowerMod)) * JumpEffectMul();
            rb.SetVel(new Vector2(rb.Vel().x * 0.3f, 0) + dir * speed);
            lastJumpTime = Time.time;
            LastTagger = PlayerIndex;
            col.sharedMaterial = walkMat;
            AudioManager.Sfx("Jump");
            return true;
        }

        /// <summary>Aim in world degrees (0 right, 90 up). Turns the penguin toward the aim.</summary>
        public void SetAim(float worldDeg, float power01)
        {
            worldDeg = Mathf.Repeat(worldDeg + 180f, 360f) - 180f;
            AimAngle = worldDeg;
            AimPower = Mathf.Clamp01(power01);
            float c = Mathf.Cos(worldDeg * Mathf.Deg2Rad);
            if (Mathf.Abs(c) > 0.05f) SetFacing(c >= 0 ? 1 : -1);
            float local = Facing > 0 ? worldDeg : 180f - worldDeg;
            local = Mathf.Repeat(local + 180f, 360f) - 180f;
            if (Avatar) Avatar.SetAim(Mathf.Clamp(local, -90f, 90f));
        }

        /// <summary>Keep the aim on the side the penguin faces (turning around mirrors it).</summary>
        void MirrorAimToFacing()
        {
            float c = Mathf.Cos(AimAngle * Mathf.Deg2Rad);
            if ((c >= 0 ? 1 : -1) != Facing) SetAim(180f - AimAngle, AimPower);
        }

        public void SetFacing(int dir)
        {
            Facing = dir >= 0 ? 1 : -1;
            if (Facing != shownFacing && Avatar) { Avatar.SetFacing(Facing); shownFacing = Facing; }
            mount.localPosition = new Vector3(BattleRules.Radius * 0.8f * Facing, BattleRules.Radius * 0.2f, 0);
        }

        /// <summary>Show the item in the flippers (null = none).</summary>
        public void HoldItem(string itemId)
        {
            if (HeldItem == itemId) return;
            HeldItem = itemId;
            if (Avatar) Avatar.HoldWeapon(string.IsNullOrEmpty(itemId) ? null : BattleItems.Graphic(itemId));
            if (!string.IsNullOrEmpty(itemId)) AudioManager.Sfx("Draw" + itemId, 0.8f);
        }

        /// <summary>Play the fire animation.</summary>
        public void PlayFire() { fireTimer = 0.6f; LastTagger = PlayerIndex; }

        float emoteUntil;
        public bool CanEmote => Alive && Time.time >= emoteUntil;

        public void Emote(string emoticonId)
        {
            if (!CanEmote || string.IsNullOrEmpty(emoticonId)) return;
            var r = GameData.Get("Emoticon", emoticonId);
            emoteUntil = Time.time + (r != null ? Units.Ms(r.Float("Duration", 3000)) : 3f);
            if (Avatar) Avatar.ShowEmote(emoticonId);
            AudioManager.Sfx(r?.Str("SoundID") ?? emoticonId);
        }

        // ================= IPenguin =================

        public void TakeDamage(DamageInfo d)
        {
            if (!Alive || Time.time < invulnerableUntil) return;
            if (d.attacker >= 0) LastTagger = d.attacker;
            if (!string.IsNullOrEmpty(d.itemId)) lastItem = d.itemId;

            if (d.impulse.sqrMagnitude > 0.0001f && rb != null && rb.simulated)
            {
                float k = 1f - Mathf.Clamp(Stats.impulseResistance, -100f, 90f) / 100f;
                rb.AddForce(d.impulse * k, ForceMode2D.Impulse);
                if (walkDir != 0 && d.impulse.sqrMagnitude > 1f) Walk(0);
                col.sharedMaterial = walkMat;
                settleTimer = 0.6f;
            }

            if (d.amount < 0) { Heal(-d.amount); return; }
            if (d.amount <= 0) return;
            if (!string.IsNullOrEmpty(d.type) && Stats.flags.Contains("Immune" + d.type)) return;

            float def = StatBlock.ApplyTyped(Stats.defence, Stats.typedDefence, d.type, this);
            float dmg = BattleRules.ApplyDefence(d.amount, def);
            dmg = Mathf.Round(Mathf.Min(dmg, BattleRules.DamageSingleHitMax));
            if (dmg <= 0) return;

            HP -= dmg;
            hurtTimer = 0.45f;
            if (Avatar) Avatar.Flash();
            AudioManager.Sfx(dmg >= 15 ? "Hurt" : "Hurt_Small");
            int slot = Mathf.Clamp(d.attacker + 1, 0, pendingDamage.Length - 1);
            pendingDamage[slot] += dmg;
            pendingTimer = DamageCollectTime;
            BattleEvents.RaisePenguinDamaged(PlayerIndex, d.attacker, dmg, d.itemId);
        }

        public void Heal(float amount)
        {
            if (!Alive || amount <= 0) return;
            float before = HP;
            HP = Mathf.Min(MaxHP, HP + amount);
            int healed = Mathf.RoundToInt(HP - before);
            if (healed > 0) Fx.FloatText(Position + Vector2.up * 1.6f, "+" + healed, HealColor, 1f);
        }

        public void Teleport(Vector2 pos)
        {
            if (rb == null) { transform.position = pos; return; }
            rb.position = pos;
            transform.position = pos;
            rb.SetVel(Vector2.zero);
            lastX = pos.x;
        }

        public void AddEffect(string id, int turns)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (turns <= 0) { RemoveEffect(id); return; }
            effects[id] = effects.TryGetValue(id, out var t) ? Mathf.Max(t, turns) : turns;
            if (!effectKeys.Contains(id)) effectKeys.Add(id);
        }

        public bool HasEffect(string id) => id != null && effects.ContainsKey(id);

        public int EffectTurns(string id) => id != null && effects.TryGetValue(id, out var t) ? t : 0;

        public void RemoveEffect(string id)
        {
            if (effects.Remove(id)) effectKeys.Remove(id);
        }

        /// <summary>One of this penguin's turns started: count effects down (with WeaponSystem.OnTurnStart).</summary>
        public void TickEffects()
        {
            for (int i = effectKeys.Count - 1; i >= 0; i--)
            {
                var k = effectKeys[i];
                int t = effects[k] - 1;
                if (t <= 0) { effects.Remove(k); effectKeys.RemoveAt(i); }
                else effects[k] = t;
            }
        }

        public void ClearEffects() { effects.Clear(); effectKeys.Clear(); }

        /// <summary>Effects as "id:turns" (online snapshots).</summary>
        public void FillEffects(List<string> list)
        {
            list.Clear();
            foreach (var k in effectKeys) list.Add(k + ":" + effects[k]);
        }

        public void SetEffects(List<string> list)
        {
            ClearEffects();
            if (list == null) return;
            foreach (var s in list)
            {
                int c = s.LastIndexOf(':');
                if (c <= 0) continue;
                if (int.TryParse(s.Substring(c + 1), out var t)) AddEffect(s.Substring(0, c), t);
            }
        }

        // ================= score =================

        /// <summary>Add (or remove) score with the original floaters. kill = kill bonus.</summary>
        public void AddScore(int amount, bool kill = false)
        {
            if (amount == 0) return;
            Score += amount;
            if (kill) pendingKillScore += amount; else pendingScore += amount;
            scoreTimer = 0.25f;
        }

        /// <summary>Overwrite everything from an online snapshot.</summary>
        public void ApplyState(PenguinState s)
        {
            Score = s.score; Kills = s.kills; Deaths = s.deaths;
            HP = s.hp;
            SetEffects(s.statuses);
            if (s.alive && !alive) Respawn(new Vector2(s.x, s.y), false);
            else if (!s.alive && alive) { alive = false; DiedAt = Time.time; HideBody(); }
            if (alive) Teleport(new Vector2(s.x, s.y));
            for (int i = 0; i < pendingDamage.Length; i++) pendingDamage[i] = 0;
        }

        // ================= death / respawn =================

        void Die(bool water)
        {
            if (!alive) return;
            FlushDamage();
            alive = false;
            drowned = water;
            Deaths++;
            DiedAt = Time.time;
            Walk(0);
            Aiming = false;
            HoldItem(null);
            int killer = LastTagger;
            if (killer >= 0 && killer != PlayerIndex)
            {
                var k = BattleController.I != null ? BattleController.I.PenguinAt(killer) : null;
                if (k != null) { k.Kills++; k.AddScore(BattleRules.KillBonus, true); }
                PendingSuicide = false;
            }
            else PendingSuicide = true;     // original wasSuicide(): no other player tagged us last
            if (water)
            {
                Fx.Splash(Position, 1.5f);
                AudioManager.Sfx("Drown");
            }
            else
            {
                Fx.Smoke(Position, 1.2f);
                AudioManager.Sfx("Dead");
            }
            if (Avatar) Avatar.SetState(water ? AvatarState.Drown : AvatarState.Dead);
            shownState = water ? AvatarState.Drown : AvatarState.Dead;
            if (water) { rb.SetVel(rb.Vel() * 0.2f); rb.gravityScale = 0.15f; }
            else { rb.SetVel(Vector2.zero); rb.simulated = false; }
            hiddenAt = Time.time + (water ? 1.6f : 1.2f);
            BattleEvents.RaisePenguinKilled(PlayerIndex, killer, lastItem);
        }

        void HideBody()
        {
            if (rb) { rb.simulated = false; rb.SetVel(Vector2.zero); }
            if (Avatar) Avatar.gameObject.SetActive(false);
            hiddenAt = -1f;
        }

        /// <summary>Come back at pos (start of a turn after TimeToRespawn, like the battleserver respawn queue).</summary>
        public void Respawn(Vector2 pos, bool applySuicidePenalty = true)
        {
            alive = true;
            drowned = false;
            HP = MaxHP;
            ClearEffects();
            if (rb) { rb.simulated = true; rb.gravityScale = 1f; }
            if (Avatar) Avatar.gameObject.SetActive(true);
            Teleport(pos);
            hiddenAt = -1f;
            shownState = (AvatarState)(-1);
            invulnerableUntil = Time.time + 0.5f;
            LastTagger = -1;
            if (applySuicidePenalty && PendingSuicide)
            {
                // PlayerSpawningState.preResume: lose SuicidePenalty, never below zero score
                int pen = Score > Mathf.Abs(BattleRules.SuicidePenalty) ? BattleRules.SuicidePenalty : -Score;
                if (pen < 0)
                {
                    Score += pen;
                    Fx.FloatText(pos + Vector2.up * 2.2f, Loc.T("FLOATER_SUICIDE") + " " + pen, DamageColor, 1.1f);
                }
            }
            PendingSuicide = false;
            Fx.Sparks(pos, TeamColor, 16);
            AudioManager.Sfx("Respawn");
        }

        // ================= update =================

        float settleTimer;

        void FixedUpdate()
        {
            if (rb == null || !rb.simulated) return;
            var pos = rb.position;

            // ground check: small circle under the body
            float r = BattleRules.Radius;
            int n = Physics2D.OverlapCircle(pos + Vector2.down * (r * 0.55f), r * 0.6f, groundFilter, overlap);
            bool g = false;
            for (int i = 0; i < n; i++)
            {
                var o = overlap[i];
                if (o == col || o.isTrigger) continue;
                // missiles and mines are not ground (no jumping mid-air off your own projectile)
                if (o.GetComponentInParent<Projectile>() != null || o.GetComponentInParent<Deployable>() != null) continue;
                g = true; break;
            }
            Grounded = g && rb.Vel().y < 4f;

            if (!alive) return;

            if (settleTimer > 0) settleTimer -= Time.fixedDeltaTime;
            var v = rb.Vel();
            if (walkDir != 0)
            {
                if (ActionPoints <= 0) Walk(0);
                else
                {
                    float speed = Units.W(maxSpeedPxMod) * BattleRules.WalkSpeedScale * WalkEffectMul();
                    float accel = Grounded ? 60f : 12f;
                    v.x = Mathf.MoveTowards(v.x, walkDir * speed, accel * Time.fixedDeltaTime);
                    if (Grounded && v.y > 0 && v.y < 2f) v.y += 0.4f;   // help over small bumps
                    rb.SetVel(v);
                    float moved = Mathf.Abs(pos.x - lastX);
                    ActionPoints -= Units.Px(moved);
                    WalkedDistance += moved;
                }
            }
            else if (Grounded && settleTimer <= 0 && Time.time - lastJumpTime > 0.3f)
            {
                col.sharedMaterial = idleMat;
                if (Mathf.Abs(v.x) < 2f) rb.SetVel(new Vector2(v.x * 0.8f, v.y));
            }
            lastX = pos.x;

            // Umbrella booster: slow fall
            if (HasEffect("Umbrella") && v.y < -3f) rb.SetVel(new Vector2(rb.Vel().x, -3f));

            // death by water / leaving the level
            var t = BattleTerrain.I;
            if (t != null)
            {
                if (pos.y < t.WaterY - r * 0.3f) { Die(true); return; }
                var lvl = t.Level;
                if (lvl != null && (pos.x < -15f || pos.x > lvl.size.x + 15f || pos.y < Mathf.Min(-15f, t.WaterY - 10f)))
                { Die(false); return; }
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (pendingTimer > 0) { pendingTimer -= dt; if (pendingTimer <= 0) FlushDamage(); }
            if (scoreTimer > 0) { scoreTimer -= dt; if (scoreTimer <= 0) ShowScore(); }

            if (alive && HP <= 0) Die(false);
            if (!alive)
            {
                if (hiddenAt > 0 && Time.time >= hiddenAt) HideBody();
                return;
            }

            // walk sound loop for the walking penguin
            bool walkingSound = walkDir != 0 && Grounded;
            if (walkingSound != walkLoop)
            {
                walkLoop = walkingSound;
                if (walkingSound) AudioManager.Loop("walk" + PlayerIndex, "Walk", 0.6f);
                else AudioManager.StopLoop("walk" + PlayerIndex);
            }

            if (hurtTimer > 0) hurtTimer -= dt;
            if (fireTimer > 0) fireTimer -= dt;
            UpdateAvatarState();
        }

        bool walkLoop;

        /// <summary>Walk speed multiplier from status effects (set by WeaponSystem via AddEffect).</summary>
        float WalkEffectMul()
        {
            float m = 1f;
            if (HasEffect("Ice")) m *= 0.75f;
            if (HasEffect("SlowGoo")) m *= 0.35f;
            if (HasEffect("Poison")) m *= 0.9f;
            if (HasEffect("Fire")) m *= 1.1f;
            return m;
        }

        float JumpEffectMul()
        {
            float m = 1f;
            if (HasEffect("Ice")) m *= 0.85f;
            if (HasEffect("SlowGoo")) m *= 0.4f;
            if (HasEffect("Poison")) m *= 0.9f;
            if (HasEffect("PogoStick")) m *= 1.6f;
            return m;
        }

        void UpdateAvatarState()
        {
            if (Avatar == null) return;
            AvatarState s;
            var v = rb.Vel();
            if (hurtTimer > 0) s = AvatarState.Hurt;
            else if (fireTimer > 0) s = AvatarState.Fire;
            else if (!Grounded && Mathf.Abs(v.y) > 1.5f) s = v.y > 0 ? AvatarState.Jump : AvatarState.Fall;
            else if (walkDir != 0) s = AvatarState.Walk;
            else if (Aiming) s = AvatarState.Aim;
            else if (celebrate) s = AvatarState.Celebrate;
            else if (sad) s = AvatarState.Sad;
            else s = AvatarState.Idle;
            if (s != shownState) { Avatar.SetState(s); shownState = s; }
        }

        bool celebrate, sad;
        /// <summary>End of match pose.</summary>
        public void SetResultPose(bool won) { celebrate = won; sad = !won; }

        /// <summary>Apply collected damage: floaters, score for the attacker (or loss for self damage), rewards.</summary>
        void FlushDamage()
        {
            pendingTimer = 0;
            var ctrl = BattleController.I;
            bool killed = HP <= 0;
            for (int i = 0; i < pendingDamage.Length; i++)
            {
                float dmg = pendingDamage[i];
                if (dmg <= 0) continue;
                pendingDamage[i] = 0;
                int amount = Mathf.RoundToInt(dmg);
                Fx.FloatText(Position + Vector2.up * 1.8f, "-" + amount, DamageColor, 1f + Mathf.Min(1f, amount / 100f));
                int attacker = i - 1;
                if (attacker >= 0 && attacker != PlayerIndex)
                {
                    var a = ctrl != null ? ctrl.PenguinAt(attacker) : null;
                    if (a == null) continue;
                    a.AddScore(BattleRules.ScoreFromDamage(amount));
                    a.DamageDealt += amount;
                    ctrl.GiveDamageRewards(a, amount, killed, Position);
                }
                else if (attacker == PlayerIndex)
                {
                    // PlayerGameObject.reduceScoreFromDamage
                    int loss = Mathf.RoundToInt(BattleRules.ScoreFromDamage(amount) * BattleRules.ScoreDamager);
                    loss = Mathf.Min(loss, Score);
                    if (loss > 0) AddScore(-loss);
                }
            }
        }

        void ShowScore()
        {
            if (pendingScore == 0 && pendingKillScore == 0) return;
            var p = Position + Vector2.up * 2.6f;
            if (pendingScore > 0 || pendingKillScore > 0)
            {
                int total = Mathf.Max(0, pendingScore) + pendingKillScore;
                Fx.FloatText(p, "+" + total, ScoreColor, pendingKillScore > 0 ? 1.4f : 1.1f);
                if (pendingScore < 0) Fx.FloatText(p + Vector2.up * 0.8f, pendingScore.ToString(), DamageColor, 1f);
            }
            else Fx.FloatText(p, pendingScore.ToString(), DamageColor, 1f);
            pendingScore = 0; pendingKillScore = 0;
        }

        void OnDestroy()
        {
            AudioManager.StopLoop("walk" + PlayerIndex);
        }
    }
}
