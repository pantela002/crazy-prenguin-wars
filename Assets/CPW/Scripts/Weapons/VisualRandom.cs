using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Random numbers for purely cosmetic things (particle spread, effect variants, tints). Separate from
    /// UnityEngine.Random, which the simulation uses (emitter spreads, crater radii, AI...), so drawing more or
    /// fewer particles (quality settings, effects skipped off screen) never shifts the simulation's sequence.
    /// xorshift32, no allocations.
    /// </summary>
    internal static class VisualRandom
    {
        static uint s = 0x9E3779B9u ^ (uint)System.Environment.TickCount;

        static uint Next()
        {
            if (s == 0) s = 0x6D2B79F5u;
            s ^= s << 13; s ^= s >> 17; s ^= s << 5;
            return s;
        }

        /// <summary>[0, 1).</summary>
        public static float Value => (Next() >> 8) * (1f / 16777216f);

        /// <summary>[min, max) like Random.Range(float, float).</summary>
        public static float Range(float min, float max) => min + (max - min) * Value;

        /// <summary>[min, max) like Random.Range(int, int); min when the range is empty.</summary>
        public static int Range(int min, int max) => max <= min ? min : min + (int)(Next() % (uint)(max - min));

        /// <summary>Uniform point inside the unit circle.</summary>
        public static Vector2 InsideUnitCircle
        {
            get
            {
                float a = Value * Mathf.PI * 2f, r = Mathf.Sqrt(Value);
                return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            }
        }

        /// <summary>Uniform direction (unit length).</summary>
        public static Vector2 OnUnitCircle
        {
            get
            {
                float a = Value * Mathf.PI * 2f;
                return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }
        }
    }
}
