using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Destructible terrain built from the level polygons, plus water, level objects and background.
    /// STUB: the terrain pass implements this and keeps the public API.
    /// </summary>
    public class BattleTerrain : MonoBehaviour
    {
        public static BattleTerrain I { get; private set; }
        public LevelData Level { get; private set; }
        public float WaterY => Level != null ? Level.waterY : 0;
        /// <summary>Every change made to the terrain this battle, in order (for online snapshots).</summary>
        public readonly List<CraterState> History = new List<CraterState>();

        /// <summary>Build the whole level (terrain, water, objects, background) under parent.</summary>
        public static BattleTerrain Build(LevelData level, Transform parent)
        {
            var go = new GameObject("Terrain");
            go.transform.SetParent(parent, false);
            I = go.AddComponent<BattleTerrain>();
            I.Level = level;
            return I;
        }

        /// <summary>Remove terrain in a circle (explosions). Unbreakable terrain is kept.</summary>
        public void Carve(Vector2 center, float radius) { History.Add(new CraterState { x = center.x, y = center.y, r = radius }); }
        /// <summary>Add terrain in a circle (e.g. building weapons).</summary>
        public void Fill(Vector2 center, float radius, string materialTheme = "Stone") { History.Add(new CraterState { x = center.x, y = center.y, r = radius, add = true }); }
        public bool IsSolid(Vector2 p) => false;
        /// <summary>Highest solid point at x below fromY (for spawning / AI). Returns false if none.</summary>
        public bool GroundBelow(float x, float fromY, out Vector2 ground) { ground = new Vector2(x, 0); return false; }
        /// <summary>Re-apply a terrain history (joining an online match / applying a snapshot).</summary>
        public void ApplyHistory(List<CraterState> craters) { }
        public bool IsTerrainCollider(Collider2D c) => c != null && c.transform.IsChildOf(transform);
        void OnDestroy() { if (I == this) I = null; }
    }
}
