using UnityEngine;

namespace CPW
{
    /// <summary>
    /// The original game works in Flash pixels with y pointing down. In Unity, 1 world unit = PX pixels, y up.
    /// A level of width W and height H pixels spans x in [0, W/PX] and y in [0, H/PX], with y = (H - py) / PX.
    /// </summary>
    public static class Units
    {
        /// <summary>Flash pixels per Unity unit.</summary>
        public const float PX = 20f;

        public static float W(float pixels) => pixels / PX;
        public static float Px(float units) => units * PX;

        /// <summary>Convert a level-space pixel position (y down) to world space (y up) for a level of the given pixel height.</summary>
        public static Vector2 LevelToWorld(float px, float py, float levelHeightPx) => new Vector2(px / PX, (levelHeightPx - py) / PX);

        /// <summary>Milliseconds (as used in the config) to seconds.</summary>
        public static float Ms(float ms) => ms / 1000f;
    }

    /// <summary>Wraps Rigidbody2D members that were renamed in Unity 6 so the code compiles on both.</summary>
    public static class Phys
    {
        public static Vector2 Vel(this Rigidbody2D rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        public static void SetVel(this Rigidbody2D rb, Vector2 v)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = v;
#else
            rb.velocity = v;
#endif
        }

        public static void SetDrag(this Rigidbody2D rb, float linear, float angular)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearDamping = linear;
            rb.angularDamping = angular;
#else
            rb.drag = linear;
            rb.angularDrag = angular;
#endif
        }

        public static float GetDrag(this Rigidbody2D rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearDamping;
#else
            return rb.drag;
#endif
        }
    }
}
