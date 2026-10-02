using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Battle rule numbers read from the original config (BattleOptions, Tuner, WorldPhysic, PlayerCharacter,
    /// PlayerPhysic, Practice, BattleRewardDropOption). Everything the battle code tunes goes through here.
    /// </summary>
    public static class BattleRules
    {
        static Record B => GameData.Battle;
        static Record T => GameData.Tuner;
        static Record W => GameData.World;
        static Record Phys => GameData.Get("PlayerPhysic", "Default");
        static Record Character => GameData.Get("PlayerCharacter", "Default");
        static Record Practice => GameData.Get("Practice", "Default");
        static Record Reward => GameData.Get("BattleRewardDropOption", "Default");

        static float BF(string k, float d) => B != null ? B.Float(k, d) : d;
        static float TF(string k, float d) => T != null ? T.Float(k, d) : d;
        static float WF(string k, float d) => W != null ? W.Float(k, d) : d;

        // ---------- timing ----------
        public static float MatchTime => BF("MatchTime", 240);
        public static float TurnTime => BF("TurnTime", 10);
        public static float PracticeTurnTime => Practice != null ? Practice.Float("TurnDuration", 20) : 20;
        public static float PracticeMatchTime => Practice != null ? Practice.Float("MatchDuration", 300) : 300;
        public static int PracticeOpponents => Practice != null ? Practice.Int("OpponentAmount", 3) : 3;
        /// <summary>Seconds the shooter may still move after firing (BattleOptions.TimeAfterFiring).</summary>
        public static float TimeAfterFiring => BF("TimeAfterFiring", 5);
        /// <summary>Seconds a dead penguin waits before it can respawn (BattleOptions.TimeToRespawn, ms).</summary>
        public static float TimeToRespawn => Units.Ms(BF("TimeToRespawn", 5000));
        /// <summary>Seconds at the start of every turn before the turn clock starts: the "Your turn" banner
        /// (0.2 s fade in + 1.6 s hold) and the camera move to the active penguin (SmoothDamp 0.35 s) finish first.
        /// A constant (not "until the camera arrives") so every device of an online match counts the same way.
        /// INVENTED: not in the original config.</summary>
        public const float TurnLeadIn = 2f;
        /// <summary>Upper bound for waiting on projectiles/physics after a turn ends.</summary>
        public const float MaxSettleTime = 12f;

        // ---------- turn resources ----------
        public static int DefaultActionPoints => Mathf.RoundToInt(BF("DefaultActionPoints", 560));
        public static int JumpCost => Mathf.RoundToInt(BF("ActionPointsJumpCost", 70));
        public static int MaxAttacks => Mathf.Max(1, Mathf.RoundToInt(BF("MaxNumberOfAttacks", 1)));
        public static int WinningScoreDefault => Mathf.RoundToInt(BF("WinningScore", 200));

        // ---------- movement (WorldPhysic, px based → units) ----------
        /// <summary>Extra scale on the walk speed so movement reads well on a phone.</summary>
        public static float WalkSpeedScale = 0.75f;
        /// <summary>Max horizontal walking speed in units/s (WorldPhysic.MaxSpeed px/s).</summary>
        public static float MaxSpeedPx => WF("MaxSpeed", 180);
        public static float WalkSpeedPx => WF("WalkSpeed", 1500);
        public static float JumpPower => WF("JumpPower", 38000);
        /// <summary>Horizontal component of a keyboard jump direction (WorldPhysic.JumpAngle; vertical is 1).</summary>
        public static float JumpAngle => WF("JumpAngle", 0.5f);
        public static float MinJumpAngle => BF("MinJumpAngle", -0.22f);
        public static float MaxJumpAngle => BF("MaxJumpAngle", -0.78f);

        // ---------- penguin ----------
        public static float HitPoints => Character != null ? Character.Float("HitPoints", 150) : 150;
        public static float RadiusPx => Phys != null ? Phys.Float("Radius", 22) : 22;
        public static float Radius => Units.W(RadiusPx);
        public static float Density => Phys != null ? Phys.Float("Density", 75) : 75;
        public static float Friction => Phys != null ? Phys.Float("Friction", 0.3f) : 0.3f;
        /// <summary>Rigidbody mass: original Nape density * area, scaled so the default penguin is ~1.1.</summary>
        public static float PenguinMass => Mathf.Max(0.2f, Density * Mathf.PI * RadiusPx * RadiusPx / 100000f);
        /// <summary>Speed (px/s) a jump of JumpPower gives a body of the original Nape mass (density*area/1000).</summary>
        public static float JumpSpeedPx(float jumpPower) => jumpPower / Mathf.Max(1f, Density * Mathf.PI * RadiusPx * RadiusPx / 1000f);

        // ---------- scoring (Tuner, PlayerGameObject) ----------
        public static int KillBonus => Mathf.RoundToInt(TF("KillOpponentBonus", 25));
        public static int SuicidePenalty => Mathf.RoundToInt(TF("SuicidePenalty", -25));
        public static float DamageSingleHitMax => TF("DamageSingleHitMax", 300);
        public static float DefenceStatMin => TF("DefenceStatMin", -99);
        public static float ScoreDamager => BF("ScoreDamager", 1);

        /// <summary>Original PlayerGameObject.addScoreFromDamage: damage * (100 / (90 + damage / 3)).</summary>
        public static int ScoreFromDamage(float damage)
        {
            if (damage <= 0) return 0;
            return (int)(damage * (100f / (90f + damage / 3f)));
        }

        /// <summary>Original DamageUtil.damageRecieved defence reduction: A - A/50 * (23 * (D / (D + 50))).</summary>
        public static float ApplyDefence(float amount, float defence)
        {
            float d = Mathf.Max(defence, Mathf.Max(DefenceStatMin, -40f));
            return amount - amount / 50f * (23f * (d / (d + 50f)));
        }

        // ---------- rewards (BattleRewardDropOption, BattleOptions) ----------
        public static float DamageToGold => Reward != null ? Reward.Float("DamageToGold", 1) : 1;
        public static float DamageToExperience => Reward != null ? Reward.Float("DamageToExperience", 1) : 1;
        public static int PenguinKillBonusGoldExp => Reward != null ? Reward.Int("PenguinKillBonusGoldExp", 10) : 10;
        public static float PropGoldExpModifier => Reward != null ? Reward.Float("PropGoldExpModifier", 0) : 0;
        public static float BonusExpModifier => BF("BonusExpModifier", 1);
        public static float BonusCoinsModifier => BF("BonusCoinsModifier", 1);
        public static float RankMultiplier(int rank) => BF("RankMultiplier" + Mathf.Clamp(rank, 1, 4), 1);

        /// <summary>
        /// Reward pickup timing (BattleRewardDropOption AppearTime 2, WaitTime 6, FlyTime 2). FeedbackItem counted these
        /// against millisecond ticks, which can't be what shipped; the remake reads them as quarter seconds
        /// (0.5 s pop, 1.5 s wait, 0.5 s flight), so a pickup is gone in about 2.5 s.
        /// </summary>
        public const float PickupTimeUnit = 0.25f;
        public static float PickupAppear => (Reward != null ? Reward.Float("AppearTime", 2) : 2) * PickupTimeUnit;
        public static float PickupWait => (Reward != null ? Reward.Float("WaitTime", 6) : 6) * PickupTimeUnit;
        public static float PickupFly => (Reward != null ? Reward.Float("FlyTime", 2) : 2) * PickupTimeUnit;

        // ---------- camera ----------
        public static float CameraZoomMin => BF("CameraZoomMin", 0.5f);
        public static float CameraZoomMax => BF("CameraZoomMax", 1.1f);
        /// <summary>Orthographic half-height at zoom 1 (a Flash stage was ~600 px tall).</summary>
        public const float OrthoAtZoom1 = 15f;

        // ---------- misc ----------
        public static float IdleTimeForHints => Units.Ms(BF("IdleTimeForHints", 10000));
        /// <summary>Seconds the booster button shows its cooldown after use (BattleOptions.BoosterCooldown, seconds).</summary>
        public static float BoosterCooldown => Mathf.Max(0.5f, BF("BoosterCooldown", 10));

        /// <summary>Weapon categories shown as tabs in the HUD (original Item.Category values).</summary>
        public static readonly string[] WeaponTabs = { "Rockets", "Grenades", "Guns", "Special" };

        static List<string> emoticons;
        /// <summary>The 16 emoticon ids (Emoticon section, sorted by SortPriority).</summary>
        public static List<string> Emoticons
        {
            get
            {
                if (emoticons != null) return emoticons;
                emoticons = new List<string>();
                var rows = new List<Record>(GameData.Section("Emoticon").Values);
                rows.Sort((a, b) => a.Int("SortPriority").CompareTo(b.Int("SortPriority")));
                foreach (var r in rows) emoticons.Add(r.Id);
                return emoticons;
            }
        }
    }
}
