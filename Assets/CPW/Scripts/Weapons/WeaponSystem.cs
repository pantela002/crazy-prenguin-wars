using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>Tracks everything one shot spawned. Done = all missiles/explosions/effects of the shot are finished.</summary>
    public class FireHandle
    {
        public bool Done;
        public Transform CameraTarget;   // what the camera should follow right now (missile), null = nothing
    }

    public enum TargetingMode { PowerBar, Aiming, Activation, Point }

    /// <summary>
    /// Data-driven weapons: interprets the original Item → Emitter → Missile/Explosion/Follower config.
    /// STUB: the weapons pass implements this and keeps the public API.
    /// </summary>
    public static class WeaponSystem
    {
        /// <summary>How the HUD should let the player aim this item.</summary>
        public static TargetingMode Targeting(string itemId) => TargetingMode.PowerBar;

        /// <summary>Fire item from shooter. angleDeg is world angle (0 = right, 90 = up); power01 in [0,1].</summary>
        public static FireHandle Fire(IPenguin shooter, string itemId, Vector2 origin, float angleDeg, float power01, Vector2 targetPoint)
            => new FireHandle { Done = true };

        /// <summary>Use a booster item (Type "Booster") on the penguin. Returns false if it can't be used now.</summary>
        public static bool UseBooster(IPenguin user, string itemId) => false;

        /// <summary>Fill points with the predicted flight path (for the aiming guide). Returns false if the item has no arc.</summary>
        public static bool PredictTrajectory(string itemId, Vector2 origin, float angleDeg, float power01, List<Vector2> points)
        {
            points.Clear();
            return false;
        }

        /// <summary>True while any projectile, explosion, physics object or status animation is still moving.</summary>
        public static bool WorldBusy => false;

        /// <summary>Called by the battle at the start of each turn of player (ticks statuses, mines, etc.).</summary>
        public static void OnTurnStart(int playerIndex) { }

        /// <summary>Remove every projectile/effect (battle end).</summary>
        public static void ClearAll() { }
    }

    /// <summary>Visual effects shared by the battle (STUB: implemented by the weapons pass).</summary>
    public static class Fx
    {
        public static void Explosion(Vector2 pos, float radius, string particleId = null) { }
        public static void Splash(Vector2 pos, float size) { }
        public static void Smoke(Vector2 pos, float size) { }
        public static void Sparks(Vector2 pos, Color color, int count = 12) { }
        public static void Debris(Vector2 pos, Color color, int count = 10) { }
        /// <summary>Floating text in the world (damage numbers, "+25", combo floaters).</summary>
        public static void FloatText(Vector2 pos, string text, Color color, float size = 1f) { }
        /// <summary>Camera shake request (durationSec, strength in world units); the battle camera reads Fx.Shake.</summary>
        public static void ShakeCamera(float durationSec, float strength) { ShakeTime = Mathf.Max(ShakeTime, durationSec); ShakeStrength = Mathf.Max(ShakeStrength, strength); }
        public static float ShakeTime, ShakeStrength;
    }
}
