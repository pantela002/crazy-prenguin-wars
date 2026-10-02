using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// A level from the original game (.lvl JSON in Resources/Data/Levels), converted to world units.
    /// STUB fields are final; the terrain pass fills in the parsing.
    /// </summary>
    public class LevelData
    {
        public string id, name, theme;               // theme: Forest, Winter, Mountain, Desert
        public float widthPx, heightPx;
        public Vector2 size;                          // world units
        public float waterY;                          // world y of the water surface
        public float waterDensity, waterLinearDrag;
        public float powerUpPercentage;
        public List<Vector2> spawnPoints = new List<Vector2>();
        public List<TerrainPolygon> polygons = new List<TerrainPolygon>();
        public List<LevelObjectPlacement> objects = new List<LevelObjectPlacement>();
        public List<Dictionary<string, object>> parallax = new List<Dictionary<string, object>>();
        public Rect cameraBounds;                     // world rect the camera may show

        public class TerrainPolygon
        {
            public List<Vector2> points = new List<Vector2>();   // world units, closed loop
            public string materialTheme;                          // Wood, Stone, Ice, Metal...
            public bool unbreakable;
            public Color tint = Color.white;
        }

        public class LevelObjectPlacement
        {
            public string id, name, fixture, theme;   // e.g. name "CubeMedium", theme "Wood" → LevelObject "CubeMediumWood"
            public Vector2 position;
            public float angleDeg;
            public bool unbreakable;
        }

        /// <summary>Load by Level section id (e.g. "forest_easy_01") or by resource path ("Data/Levels/forest_01_easy").</summary>
        public static LevelData Load(string levelIdOrPath)
        {
            var d = new LevelData { id = levelIdOrPath, name = levelIdOrPath, theme = "Forest", size = new Vector2(80, 60), waterY = 5 };
            d.spawnPoints.Add(new Vector2(20, 30));
            d.spawnPoints.Add(new Vector2(60, 30));
            d.cameraBounds = new Rect(-5, -5, 90, 75);
            return d;
        }
    }
}
