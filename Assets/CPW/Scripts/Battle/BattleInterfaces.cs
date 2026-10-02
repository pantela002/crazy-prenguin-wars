using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>Damage coming from an explosion, bullet, status effect or the world.</summary>
    public struct DamageInfo
    {
        public float amount;          // before the target's defence is applied
        public string type;           // "Normal", "Fire", "Poison", "Acid", "Ice", "SlowGoo", "Fall", "Water"
        public int attacker;          // player index, -1 for the world
        public string itemId;         // weapon/booster that caused it (for scoring, challenges)
        public Vector2 point;         // world position of the hit
        public Vector2 impulse;       // world-space impulse to apply (already scaled)
    }

    /// <summary>Anything weapons can hurt or push: penguins and level objects.</summary>
    public interface IDamageable
    {
        GameObject gameObject { get; }
        Vector2 Position { get; }
        Rigidbody2D Body { get; }
        bool Alive { get; }
        void TakeDamage(DamageInfo d);
    }

    /// <summary>Attack/defence/luck modifiers from clothes, trophies and boosters (original "Bonus" stats).</summary>
    public class StatBlock
    {
        public float attack;            // percent bonus, e.g. 3 = +3% damage dealt
        public float defence;           // percent damage reduction (capped by Tuner.DefenceStatMin)
        public float luck;              // percent chance for good drops / critical
        public float attackMultiplier = 1f;      // e.g. SalmonSushi "Multiply:1.5"
        public float impulseResistance;          // percent
        public readonly HashSet<string> flags = new HashSet<string>(); // e.g. "NoFall", status immunities "ImmuneFire"
    }

    /// <summary>A penguin in battle as seen by weapons, terrain and AI. Implemented by Battle/Penguin.cs.</summary>
    public interface IPenguin : IDamageable
    {
        int PlayerIndex { get; }
        PlayerSlot Slot { get; }
        float HP { get; }
        float MaxHP { get; }
        int Facing { get; }                 // +1 right, -1 left
        Transform WeaponMount { get; }      // where held weapons/projectiles start
        StatBlock Stats { get; }
        void Heal(float amount);
        /// <summary>Teleport (used by teleport weapons/boosters and respawns).</summary>
        void Teleport(Vector2 pos);
        /// <summary>Add/refresh a named effect for N turns (statuses like "Fire", "Poison", boosters like "Umbrella").</summary>
        void AddEffect(string id, int turns);
        bool HasEffect(string id);
    }

    /// <summary>Lookup of everything alive in the current battle; filled by Battle code, used by weapons/AI.</summary>
    public static class BattleWorld
    {
        public static readonly List<IPenguin> Penguins = new List<IPenguin>();
        public static readonly List<IDamageable> Objects = new List<IDamageable>();   // dynamic level objects, mines...
        public static int ActivePlayer = -1;
        public static float Wind;           // reserved (original game had none; WandWind pushes things)
        public static Transform Root;       // parent for spawned battle objects

        public static void Clear()
        {
            Penguins.Clear();
            Objects.Clear();
            ActivePlayer = -1;
            Wind = 0;
            Root = null;
        }

        public static IEnumerable<IDamageable> AllDamageables()
        {
            foreach (var p in Penguins) if (p != null && p.Alive) yield return p;
            foreach (var o in Objects) if (o != null && o.Alive) yield return o;
        }
    }
}
