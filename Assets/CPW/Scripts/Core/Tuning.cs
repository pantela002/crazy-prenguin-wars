using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Conversions from the original game's Box2D/pixel numbers to Unity physics. Shared by battle and weapons
    /// so movement, jumps and projectiles feel consistent. Tweak here, not in the systems.
    /// </summary>
    public static class Tuning
    {
        /// <summary>World gravity in units/s² (WorldPhysic.Gravity is px/s²).</summary>
        public static float Gravity => GameData.World != null ? GameData.World.Float("Gravity", 500) / Units.PX * GravityScale : 25f;
        /// <summary>Extra multiplier so arcs read well on a phone screen.</summary>
        public static float GravityScale = 1.0f;

        /// <summary>Penguin collision radius in units (PlayerPhysic.Radius px).</summary>
        public static float PenguinRadius => Units.W(GameData.Get("PlayerPhysic", "Default")?.Float("Radius", 22) ?? 22);
    }
}
