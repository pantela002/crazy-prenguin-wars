using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Debris chips drawn with the original particle bitmaps: particle_1..5 of a landmass set when terrain is blown
    /// away (particles.xml MissileExplosion{Material}: 5 chips, 360°, speed 15 → 5, gravity, shrinking over ~1 s) and
    /// object_particle_1..5 of level_obstacles_{material} when a level object is damaged or breaks.
    /// One pooled SpriteRenderer per chip (fixed pool, no allocations after warm-up). Returns false when the art is
    /// missing so callers keep Fx.Debris.
    /// </summary>
    public class SpriteDebris : MonoBehaviour
    {
        public const int PoolSize = 72;
        /// <summary>Sorting order of the chips (in front of terrain and level objects, behind the water front).</summary>
        public const int SortingOrder = 30;
        public static float Gravity = 16f;

        struct Chip
        {
            public Vector2 pos, vel;
            public float rot, spin, life, age, scale;
        }

        static SpriteDebris inst;
        SpriteRenderer[] rs;
        Transform[] ts;
        Chip[] chips;
        int next;
        static readonly Sprite[] buf = new Sprite[5];

        static SpriteDebris Get()
        {
            if (inst != null) return inst;
            var go = new GameObject("SpriteDebris");
            inst = go.AddComponent<SpriteDebris>();
            inst.rs = new SpriteRenderer[PoolSize];
            inst.ts = new Transform[PoolSize];
            inst.chips = new Chip[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var c = new GameObject("Chip");
                c.transform.SetParent(go.transform, false);
                var r = c.AddComponent<SpriteRenderer>();
                r.sortingOrder = SortingOrder;
                r.enabled = false;
                inst.rs[i] = r;
                inst.ts[i] = c.transform;
            }
            return inst;
        }

        /// <summary>Terrain chips of a material theme around a crater (count chips, spread over the crater radius).</summary>
        public static bool Terrain(Vector2 center, string materialTheme, int count, float radius, Color tint)
        {
            int n = 0;
            for (int k = 1; k <= 5; k++)
            {
                var s = TerrainStyle.LandmassSprite(materialTheme, "particle_" + k);
                if (s != null) buf[n++] = s;
            }
            if (n == 0) return false;
            var c = new Color(Mathf.Clamp01(tint.r), Mathf.Clamp01(tint.g), Mathf.Clamp01(tint.b), 1f);
            Emit(center, Mathf.Clamp(radius * 0.4f, 0.05f, 1.2f), count, n, 7.5f, c);
            return true;
        }

        /// <summary>Chips of a level object material (Wood, Stone, Ice, Metal).</summary>
        public static bool LevelObject(Vector2 center, string material, int count, float spread)
        {
            if (string.IsNullOrEmpty(material)) return false;
            string b = "level_objects/level_obstacles_" + material.ToLowerInvariant() + "/_bitmaps/object_particle_";
            int n = 0;
            for (int k = 1; k <= 5; k++)
            {
                var s = OriginalArt.Sprite(b + k);
                if (s != null) buf[n++] = s;
            }
            if (n == 0) return false;
            Emit(center, spread, count, n, 6f, Color.white);
            return true;
        }

        static void Emit(Vector2 center, float spread, int count, int nSprites, float speed, Color color)
        {
            if (!Application.isPlaying) return;
            var d = Get();
            count = Mathf.Clamp(count, 1, 24);
            for (int i = 0; i < count; i++)
            {
                int k = d.next;
                d.next = (d.next + 1) % PoolSize;
                float a = Random.Range(0f, Mathf.PI * 2f);
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                d.chips[k] = new Chip
                {
                    pos = center + dir * Random.Range(0f, spread),
                    vel = (dir + Vector2.up * 0.6f) * speed * Random.Range(0.45f, 1f),
                    rot = Random.Range(0f, 360f),
                    spin = Random.Range(-360f, 360f),
                    life = Random.Range(0.6f, 1.1f),
                    age = 0f,
                    scale = Random.Range(0.85f, 1.15f),
                };
                var r = d.rs[k];
                r.sprite = buf[Random.Range(0, nSprites)];
                r.color = color;
                r.enabled = true;
                d.Place(k);
            }
        }

        void Place(int k)
        {
            ref var c = ref chips[k];
            float t = c.age / c.life;
            ts[k].SetPositionAndRotation(new Vector3(c.pos.x, c.pos.y, -0.1f), Quaternion.Euler(0, 0, c.rot));
            float s = c.scale * (1f - t * t);   // ShrinkParticles
            ts[k].localScale = new Vector3(s, s, 1f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int k = 0; k < PoolSize; k++)
            {
                if (!rs[k].enabled) continue;
                ref var c = ref chips[k];
                c.age += dt;
                if (c.age >= c.life) { rs[k].enabled = false; continue; }
                c.vel.y -= Gravity * dt;
                c.vel *= 1f - Mathf.Min(1f, dt * 0.8f);   // SpeedStart 15 → SpeedEnd 5
                c.pos += c.vel * dt;
                c.rot += c.spin * dt;
                Place(k);
            }
        }

        void OnDestroy()
        {
            if (inst == this) inst = null;
        }
    }
}
