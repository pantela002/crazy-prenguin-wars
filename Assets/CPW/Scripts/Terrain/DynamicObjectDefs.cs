// GENERATED from cpw-server/assets/dynamicobjects/*.xml (PhysicsEditor body definitions of the original game).
// Units are Flash pixels with y pointing down, relative to the body center (anchor 0.5, 0.5).
using System.Collections.Generic;

namespace CPW
{
    /// <summary>Physics shape and material of one level object fixture (e.g. Wood/cube_medium).</summary>
    public class DynamicObjectDef
    {
        public string theme, body;
        public float density, friction, restitution;
        /// <summary>Circle as { r, x, y } in px (y down), or null.</summary>
        public float[] circle;
        /// <summary>Convex polygons as flat { x0, y0, x1, y1, ... } arrays in px (y down), or null.</summary>
        public float[][] polys;

        static Dictionary<string, DynamicObjectDef> table;

        /// <summary>Find the definition for a material theme and fixture name; falls back to default.xml, then Wood.</summary>
        public static DynamicObjectDef Find(string theme, string body)
        {
            if (table == null) Build();
            if (string.IsNullOrEmpty(body)) return null;
            if (theme != null && table.TryGetValue(theme + "/" + body, out var d)) return d;
            if (table.TryGetValue("default/" + body, out d)) return d;
            return table.TryGetValue("Wood/" + body, out d) ? d : null;
        }

        static void Add(string theme, string body, float density, float friction, float restitution, float[] circle = null, float[][] polys = null)
        {
            table[theme + "/" + body] = new DynamicObjectDef { theme = theme, body = body, density = density, friction = friction, restitution = restitution, circle = circle, polys = polys };
        }

        static void Build()
        {
            table = new Dictionary<string, DynamicObjectDef>();
            Add("CustomObjects", "custom_object_tree", 100f, 1f, 0.2f, polys: new[] { new[] { -117.5f, 25f, -123.5f, -6f, -103f, -28.5f, -49.5f, -27f, -86.5f, 27f }, new[] { -77f, 62.5f, -86.5f, 27f, -49.5f, -27f, 14.5f, -39f, 60f, -1.5f, 98.5f, 45f, 114.5f, 95f, -41.5f, 70f }, new[] { -14.407f, -134.419f, -4.046f, -146.663f, 56.919f, -135f, 5.895f, -115.158f }, new[] { 121.5f, 19f, 98.5f, 45f, 60f, -1.5f, 112f, -8.5f }, new[] { -30.5f, 122f, -41.5f, 70f, 114.5f, 95f, 16f, 147.5f }, new[] { -34.5f, -70f, 2f, -83.5f, 14.5f, -39f, -49.5f, -27f }, new[] { 5.895f, -115.158f, 56.919f, -135f, 2f, -83.5f }, new[] { 29f, -68.5f, 10.5f, -54f, 2f, -83.5f, 40.163f, -119.151f }, new[] { 109f, 58.5f, 114.5f, 95f, 98.5f, 45f } });
            Add("Desert", "ball_medium", 85f, 1.1f, 0.1f, polys: new[] { new[] { 25.451f, -7.312f, 22.686f, 14.098f, 9.89f, 25.29f, -12.227f, 24.395f, -24.936f, 9.955f, -25.104f, -9.357f, -12.18f, -23.709f, 10.292f, -23.749f } });
            Add("Desert", "ball_small", 85f, 1.1f, 0.1f, polys: new[] { new[] { -13.353f, -0.372f, -7.92f, -10.563f, 0.303f, -13.353f, 8.82f, -10.416f, 13.813f, -0.431f, 9.408f, 9.261f, 0.303f, 13.372f, -9.036f, 9.76f } });
            Add("Desert", "cube_big", 85f, 1.1f, 0.1f, polys: new[] { new[] { 50.163f, -50.178f, 49.797f, 50.043f, -49.733f, 49.957f, -49.776f, -50.147f } });
            Add("Desert", "cube_medium", 85f, 1.1f, 0.1f, polys: new[] { new[] { 25.765f, 25.535f, -25.915f, 25.482f, -26.103f, -25.774f, 25.75f, -25.947f } });
            Add("Desert", "cube_small", 85f, 1.1f, 0.1f, polys: new[] { new[] { -13f, 12.762f, -13f, -13f, 12.963f, -13f, 12.93f, 12.762f } });
            Add("Desert", "triangle_medium", 85f, 1.1f, 0.1f, polys: new[] { new[] { 26.372f, -25.192f, -24.79f, 24.945f, -24.063f, -24.641f } });
            Add("Desert", "triangle_small", 85f, 1.1f, 0.1f, polys: new[] { new[] { 13.289f, -12.837f, -12.43f, 11.746f, -12.519f, -13f } });
            Add("Desert", "rectangle_large", 85f, 1.1f, 0.1f, polys: new[] { new[] { 95.52f, -26.393f, 94.813f, 26.379f, -96.266f, 26.238f, -95.775f, -26.341f } });
            Add("Desert", "rectangle_medium", 85f, 1.1f, 0.1f, polys: new[] { new[] { -49.064f, 26.458f, -49.085f, -26.457f, 50.128f, -26.443f, 50.053f, 26.218f } });
            Add("Desert", "plank_medium", 85f, 1.1f, 0.1f, polys: new[] { new[] { -96.447f, 14.93f, -96.575f, -14.879f, 95.851f, -14.927f, 95.67f, 14.766f } });
            Add("Desert", "plank_large", 85f, 1.1f, 0.1f, polys: new[] { new[] { -198.49f, -14.665f, 197.063f, -14.787f, 197.874f, 14.773f, -198.631f, 15.04f } });
            Add("Desert", "plank_small", 85f, 1.1f, 0.1f, polys: new[] { new[] { -72.127f, -14.788f, 73.415f, -14.351f, 73.351f, 14.881f, -71.971f, 14.947f } });
            Add("Ice", "ball_medium", 15f, 0.5f, 0.1f, circle: new[] { 26.173f, -0.004f, 0.167f });
            Add("Ice", "ball_small", 15f, 0.5f, 0.1f, circle: new[] { 13f, 0.393f, -0.004f });
            Add("Ice", "cube_big", 15f, 0.5f, 0.1f, polys: new[] { new[] { 49f, 48f, -49f, 48f, -49f, -49f, 49f, -49f } });
            Add("Ice", "cube_medium", 15f, 0.5f, 0.1f, polys: new[] { new[] { -25f, 25f, -25f, -25f, 25f, -25f, 25f, 25f } });
            Add("Ice", "cube_small", 15f, 0.5f, 0.1f, polys: new[] { new[] { -12f, 12f, -12f, -12f, 12f, -12f, 12f, 12f } });
            Add("Ice", "triangle_medium", 15f, 0.5f, 0.1f, polys: new[] { new[] { 29.5f, 26f, -25.5f, -29f, 29.5f, -29f } });
            Add("Ice", "triangle_small", 15f, 0.5f, 0.1f, polys: new[] { new[] { 15.5f, 13f, -12.5f, -15f, 15.5f, -15f } });
            Add("Ice", "rectangle_large", 15f, 0.5f, 0.1f, polys: new[] { new[] { 96f, 25.5f, -96f, 25.5f, -96f, -25.5f, 96f, -25.5f } });
            Add("Ice", "rectangle_medium", 15f, 0.5f, 0.1f, polys: new[] { new[] { 49f, 25.5f, -49f, 25.5f, -49f, -25.5f, 49f, -25.5f } });
            Add("Ice", "plank_medium", 15f, 0.5f, 0.1f, polys: new[] { new[] { -96f, 14f, -96f, -14f, 96f, -14f, 96f, 14f } });
            Add("Ice", "plank_large", 15f, 0.5f, 0.1f, polys: new[] { new[] { 199.5f, 14f, -199.5f, 14f, -199.5f, -14f, 199.5f, -14f } });
            Add("Ice", "plank_small", 15f, 0.5f, 0.1f, polys: new[] { new[] { -73.5f, 14f, -73.5f, -14f, 73.5f, -14f, 73.5f, 14f } });
            Add("Metal", "ball_medium", 100f, 0.9f, 0.2f, polys: new[] { new[] { 25.451f, -7.312f, 22.686f, 14.098f, 9.89f, 25.29f, -12.227f, 24.395f, -24.936f, 9.955f, -25.104f, -9.357f, -12.18f, -23.709f, 10.292f, -23.749f } });
            Add("Metal", "ball_small", 100f, 0.9f, 0.2f, polys: new[] { new[] { -13.353f, -0.372f, -7.92f, -10.563f, 0.303f, -13.353f, 8.82f, -10.416f, 13.813f, -0.431f, 9.408f, 9.261f, 0.303f, 13.372f, -9.036f, 9.76f } });
            Add("Metal", "cube_big", 100f, 0.9f, 0.2f, polys: new[] { new[] { 49f, -50f, 49f, 49f, -49f, 49f, -49f, -49f } });
            Add("Metal", "cube_medium", 100f, 0.9f, 0.2f, polys: new[] { new[] { -25f, 25f, -25f, -25f, 25f, -25f, 25f, 25f } });
            Add("Metal", "cube_small", 100f, 0.9f, 0.2f, polys: new[] { new[] { -12f, 12f, -12f, -12f, 12f, -12f, 12f, 12f } });
            Add("Metal", "triangle_medium", 100f, 0.9f, 0.2f, polys: new[] { new[] { -26f, -26.5f, 26f, -26.5f, 26f, -22.5f, -22f, 26.5f, -26f, 26.5f } });
            Add("Metal", "triangle_small", 100f, 0.9f, 0.2f, polys: new[] { new[] { -13f, -13.5f, 13f, -13.5f, 13f, -11.5f, -11f, 13.5f, -13f, 13.5f } });
            Add("Metal", "rectangle_large", 100f, 0.9f, 0.2f, polys: new[] { new[] { 96f, 25.5f, -95f, 25.5f, -95f, -25.5f, 96f, -25.5f } });
            Add("Metal", "rectangle_medium", 100f, 0.9f, 0.2f, polys: new[] { new[] { 49f, 25.5f, -49f, 25.5f, -49f, -25.5f, 49f, -25.5f } });
            Add("Metal", "plank_medium", 100f, 0.9f, 0.2f, polys: new[] { new[] { -96.447f, 14.93f, -96.575f, -14.879f, 95.851f, -14.927f, 95.67f, 14.766f } });
            Add("Metal", "plank_large", 100f, 0.9f, 0.2f, polys: new[] { new[] { -198.49f, -14.665f, 197.063f, -14.787f, 197.874f, 14.773f, -198.631f, 15.04f } });
            Add("Metal", "plank_small", 100f, 0.9f, 0.2f, polys: new[] { new[] { -72.127f, -14.788f, 73.415f, -14.351f, 73.351f, 14.881f, -71.971f, 14.947f } });
            Add("OilRig", "ball_medium", 55f, 1f, 0.1f, polys: new[] { new[] { 25.451f, -7.312f, 22.686f, 14.098f, 9.89f, 25.29f, -12.227f, 24.395f, -24.936f, 9.955f, -25.104f, -9.357f, -12.18f, -23.709f, 10.292f, -23.749f } });
            Add("OilRig", "ball_small", 55f, 1f, 0.1f, polys: new[] { new[] { -13.353f, -0.372f, -7.92f, -10.563f, 0.303f, -13.353f, 8.82f, -10.416f, 13.813f, -0.431f, 9.408f, 9.261f, 0.303f, 13.372f, -9.036f, 9.76f } });
            Add("OilRig", "cube_big", 55f, 1f, 0.1f, polys: new[] { new[] { 50.163f, -50.178f, 49.797f, 50.043f, -49.733f, 49.957f, -49.776f, -50.147f } });
            Add("OilRig", "cube_medium", 55f, 1f, 0.1f, polys: new[] { new[] { 25.765f, 25.535f, -25.915f, 25.482f, -26.103f, -25.774f, 25.75f, -25.947f } });
            Add("OilRig", "cube_small", 55f, 1f, 0.1f, polys: new[] { new[] { -13f, 12.762f, -13f, -13f, 12.963f, -13f, 12.93f, 12.762f } });
            Add("OilRig", "triangle_medium", 55f, 1f, 0.1f, polys: new[] { new[] { 26.372f, -25.192f, -24.79f, 24.945f, -24.063f, -24.641f } });
            Add("OilRig", "triangle_small", 55f, 1f, 0.1f, polys: new[] { new[] { 13.289f, -12.837f, -12.43f, 11.746f, -12.519f, -13f } });
            Add("OilRig", "rectangle_large", 55f, 1f, 0.1f, polys: new[] { new[] { 95.52f, -26.393f, 94.813f, 26.379f, -96.266f, 26.238f, -95.775f, -26.341f } });
            Add("OilRig", "rectangle_medium", 55f, 1f, 0.1f, polys: new[] { new[] { -49.064f, 26.458f, -49.085f, -26.457f, 50.128f, -26.443f, 50.053f, 26.218f } });
            Add("OilRig", "plank_medium", 55f, 1f, 0.1f, polys: new[] { new[] { -96.447f, 14.93f, -96.575f, -14.879f, 95.851f, -14.927f, 95.67f, 14.766f } });
            Add("OilRig", "plank_large", 55f, 1f, 0.1f, polys: new[] { new[] { -198.49f, -14.665f, 197.063f, -14.787f, 197.874f, 14.773f, -198.631f, 15.04f } });
            Add("OilRig", "plank_small", 55f, 1f, 0.1f, polys: new[] { new[] { -72.127f, -14.788f, 73.415f, -14.351f, 73.351f, 14.881f, -71.971f, 14.947f } });
            Add("PowerUps", "HealthCrate", 100f, 1f, 0f, polys: new[] { new[] { -18.255f, 25.783f, -26.5f, -2f, -18.792f, -26.038f, 19.66f, -26.038f, 27.038f, -1.387f, 19.208f, 25.876f } });
            Add("PowerUps", "AmmoCrate", 100f, 1f, 0f, polys: new[] { new[] { 19f, 25.858f, -17.538f, 25.783f, -26.962f, -0.075f, -18.642f, -26.038f, 19.858f, -26.075f, 27.038f, 0.104f } });
            Add("PowerUps", "PointsCrate", 100f, 1f, 0f, polys: new[] { new[] { -12.115f, -15.719f, -7.482f, -23.16f, 9.065f, -23.283f, 12.396f, -14.916f, 1.962f, -8.5f, -2.245f, -8.142f }, new[] { 20.575f, 10.396f, 13.245f, 21.396f, 0.226f, 24.642f, -13f, 21.821f, -14f, -4f, -2.245f, -8.142f, 1.962f, -8.5f, 16.425f, -1.113f }, new[] { -19.962f, 10.038f, -14f, -4f, -13f, 21.821f } });
            Add("PowerUps", "Treasure", 100f, 1f, 0f, polys: new[] { new[] { 22.179f, -23.717f, 27.358f, 3.093f, 15.5f, 25.5f, -16.5f, 25.5f, -25f, 22.717f, -27.462f, 1.67f, -21.5f, -23.5f }, new[] { 24.858f, 22.755f, 15.5f, 25.5f, 27.358f, 3.093f } });
            Add("PowerUps", "LandMine", 100f, 1f, 0f, polys: new[] { new[] { 19.971f, 0.887f, 7.828f, 9.245f, -7.634f, 9.206f, -21f, -0.048f, -20.835f, -8.245f, 19.962f, -8.15f } });
            Add("PowerUps", "FireMine", 100f, 1f, 0f, polys: new[] { new[] { -20.226f, 1.057f, -20.029f, -8.226f, 19.95f, -8.283f, 21.142f, -0.199f, 10.491f, 8.075f, -8.81f, 8.029f } });
            Add("PowerUps", "SpringMine", 100f, 1f, 0f, polys: new[] { new[] { -22f, -4f, -11f, -10.358f, 10.358f, -10.358f, 22.038f, -4.471f, 23.301f, 6.491f, -0.161f, 10.642f, -22.434f, 7.283f } });
            Add("Stone", "ball_medium", 55f, 1f, 0.1f, circle: new[] { 26.173f, -0.004f, 0.167f });
            Add("Stone", "ball_small", 55f, 1f, 0.1f, circle: new[] { 13f, 0.393f, -0.004f });
            Add("Stone", "cube_big", 55f, 1f, 0.1f, polys: new[] { new[] { 48f, 49f, -49f, 49f, -49f, -49f, 48f, -49f } });
            Add("Stone", "cube_medium", 55f, 1f, 0.1f, polys: new[] { new[] { -25f, 26f, -25f, -25f, 26f, -25f, 26f, 26f } });
            Add("Stone", "cube_small", 55f, 1f, 0.1f, polys: new[] { new[] { -12f, 12f, -12f, -12f, 12f, -12f, 12f, 12f } });
            Add("Stone", "triangle_medium", 55f, 1f, 0.1f, polys: new[] { new[] { -26f, -26.5f, 26f, -26.5f, 26f, -22.5f, -22f, 26.5f, -26f, 26.5f } });
            Add("Stone", "triangle_small", 55f, 1f, 0.1f, polys: new[] { new[] { -13f, -13.5f, 13f, -13.5f, 13f, -11.5f, -11f, 13.5f, -13f, 13.5f } });
            Add("Stone", "rectangle_large", 55f, 1f, 0.1f, polys: new[] { new[] { 96f, 25.5f, -96f, 25.5f, -96f, -25.5f, 96f, -25.5f } });
            Add("Stone", "rectangle_medium", 55f, 1f, 0.1f, polys: new[] { new[] { 49f, 25.5f, -49f, 25.5f, -49f, -25.5f, 49f, -25.5f } });
            Add("Stone", "plank_medium", 55f, 1f, 0.1f, polys: new[] { new[] { -96f, 14f, -96f, -14f, 96f, -14f, 96f, 14f } });
            Add("Stone", "plank_large", 55f, 1f, 0.1f, polys: new[] { new[] { 199.5f, 14f, -198.5f, 14f, -198.5f, -14f, 199.5f, -14f } });
            Add("Stone", "plank_small", 55f, 1f, 0.1f, polys: new[] { new[] { -73.5f, 14f, -73.5f, -14f, 73.5f, -14f, 73.5f, 14f } });
            Add("Wood", "ball_medium", 20f, 1f, 0.2f, circle: new[] { 26.173f, -0.004f, 0.167f });
            Add("Wood", "ball_small", 20f, 1f, 0.2f, circle: new[] { 13f, 0.393f, -0.004f });
            Add("Wood", "cube_big", 20f, 1f, 0.2f, polys: new[] { new[] { 49f, 48f, -49f, 48f, -49f, -49f, 49f, -49f } });
            Add("Wood", "cube_medium", 20f, 1f, 0.2f, polys: new[] { new[] { -25f, 26f, -25f, -25f, 25f, -25f, 25f, 26f } });
            Add("Wood", "cube_small", 20f, 1f, 0.2f, polys: new[] { new[] { -12f, 13f, -12f, -12f, 12f, -12f, 12f, 13f } });
            Add("Wood", "triangle_medium", 20f, 1f, 0.2f, polys: new[] { new[] { -26f, -26.5f, 26f, -26.5f, 26f, -22.5f, -22f, 26.5f, -26f, 26.5f } });
            Add("Wood", "triangle_small", 20f, 1f, 0.2f, polys: new[] { new[] { -13f, -13.5f, 13f, -13.5f, 13f, -11.5f, -11f, 13.5f, -13f, 13.5f } });
            Add("Wood", "rectangle_large", 20f, 1f, 0.2f, polys: new[] { new[] { 96f, 25.5f, -96f, 25.5f, -96f, -25.5f, 96f, -25.5f } });
            Add("Wood", "rectangle_medium", 20f, 1f, 0.2f, polys: new[] { new[] { 49f, 25.5f, -49f, 25.5f, -49f, -25.5f, 49f, -25.5f } });
            Add("Wood", "plank_medium", 20f, 1f, 0.2f, polys: new[] { new[] { -96f, 14f, -96f, -14f, 96f, -14f, 96f, 14f } });
            Add("Wood", "plank_large", 20f, 1f, 0.2f, polys: new[] { new[] { 199.5f, 14f, -199.5f, 14f, -199.5f, -14f, 199.5f, -14f } });
            Add("Wood", "plank_small", 20f, 1f, 0.2f, polys: new[] { new[] { -73.5f, 14f, -73.5f, -14f, 73.5f, -14f, 73.5f, 14f } });
            Add("default", "ball_medium", 20f, 1f, 0.2f, polys: new[] { new[] { 25.451f, -7.312f, 22.686f, 14.098f, 9.89f, 25.29f, -12.227f, 24.395f, -24.936f, 9.955f, -25.104f, -9.357f, -12.18f, -23.709f, 10.292f, -23.749f } });
            Add("default", "ball_small", 20f, 1f, 0.2f, polys: new[] { new[] { -13.353f, -0.372f, -7.92f, -10.563f, 0.303f, -13.353f, 8.82f, -10.416f, 13.813f, -0.431f, 9.408f, 9.261f, 0.303f, 13.372f, -9.036f, 9.76f } });
            Add("default", "cube_big", 20f, 1f, 0.2f, polys: new[] { new[] { 50.163f, -50.178f, 49.797f, 50.043f, -49.733f, 49.957f, -49.776f, -50.147f } });
            Add("default", "cube_medium", 20f, 1f, 0.2f, polys: new[] { new[] { 25.765f, 25.535f, -25.915f, 25.482f, -26.103f, -25.774f, 25.75f, -25.947f } });
            Add("default", "cube_small", 20f, 1f, 0.2f, polys: new[] { new[] { -13f, 12.762f, -13f, -13f, 12.963f, -13f, 12.93f, 12.762f } });
            Add("default", "triangle_medium", 20f, 1f, 0.2f, polys: new[] { new[] { 26.372f, -25.192f, -24.79f, 24.945f, -24.063f, -24.641f } });
            Add("default", "triangle_small", 20f, 1f, 0.2f, polys: new[] { new[] { 13.289f, -12.837f, -12.43f, 11.746f, -12.519f, -13f } });
            Add("default", "rectangle_large", 20f, 1f, 0.2f, polys: new[] { new[] { 95.52f, -26.393f, 94.813f, 26.379f, -96.266f, 26.238f, -95.775f, -26.341f } });
            Add("default", "rectangle_medium", 20f, 1f, 0.2f, polys: new[] { new[] { -49.064f, 26.458f, -49.085f, -26.457f, 50.128f, -26.443f, 50.053f, 26.218f } });
            Add("default", "plank_medium", 20f, 1f, 0.2f, polys: new[] { new[] { -96.447f, 14.93f, -96.575f, -14.879f, 95.851f, -14.927f, 95.67f, 14.766f } });
            Add("default", "plank_large", 20f, 1f, 0.2f, polys: new[] { new[] { -198.49f, -14.665f, 197.063f, -14.787f, 197.874f, 14.773f, -198.631f, 15.04f } });
            Add("default", "plank_small", 20f, 1f, 0.2f, polys: new[] { new[] { -72.127f, -14.788f, 73.415f, -14.351f, 73.351f, 14.881f, -71.971f, 14.947f } });
        }
    }
}
