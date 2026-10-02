using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Visual effects shared by the battle: explosions, splashes, smoke, sparks, debris, fire, bubbles,
    /// laser beams, floating text and camera shake. Everything is built from code: one persistent world-space
    /// ParticleSystem per particle kind (particles are emitted with EmitParams, so there is no per-effect
    /// GameObject), a pool of LineRenderers for beams and a pool of TextMesh pairs for floating text.
    /// No allocations per frame.
    /// </summary>
    public static class Fx
    {
        /// <summary>Z of effects (in front of terrain and penguins, the battle camera looks down +z).</summary>
        public const float Z = -1.6f;
        const float TextZ = -2.6f;

        // ------------------------------------------------------------------ public API

        /// <summary>Explosion sized by radius (world units). particleId is the config ParticleEffect
        /// (e.g. "BasicExplosion3", "PlasmaExplosion", "WaterExplosion", "MolotovExplosion") and picks the style.</summary>
        public static void Explosion(Vector2 pos, float radius, string particleId = null)
        {
            if (!Ensure()) return;
            radius = Mathf.Clamp(radius, 0.15f, 16f);
            string id = particleId ?? "";
            if (Has(id, "Water")) { Splash(pos, radius); return; }
            if (Has(id, "Bullet")) { BulletHit(pos, radius); return; }
            if (Has(id, "Laser")) { Burst(sys.spark, pos, 14, 2f, 7f, 0.25f, 0.5f, 0.08f, 0.18f, new Color(1f, 0.25f, 0.2f), new Color(1f, 0.8f, 0.8f)); Glow(pos, radius * 1.6f + 0.6f, new Color(1f, 0.2f, 0.2f)); return; }
            if (Has(id, "Plasma")) { Colored(pos, radius, new Color(0.3f, 0.9f, 1f), new Color(0.85f, 0.35f, 1f)); return; }
            if (Has(id, "Poison") || Has(id, "GreenGoo") || Has(id, "Mushroom")) { Gas(pos, radius, new Color(0.45f, 0.95f, 0.25f)); return; }
            if (Has(id, "Molotov")) { if (Has(id, "Burning")) Fire(pos, radius * 0.6f + 0.3f); else { Fire(pos, radius + 0.4f); Fireball(pos, radius * 0.6f); } return; }
            if (Has(id, "Stone")) { Debris(pos, new Color(0.55f, 0.52f, 0.48f), 14); Smoke(pos, radius * 0.6f + 0.6f, new Color(0.75f, 0.7f, 0.62f, 0.7f)); return; }
            if (Has(id, "Cat")) { Burst(sys.spark, pos, 10, 2f, 6f, 0.15f, 0.35f, 0.08f, 0.16f, Color.white, new Color(1f, 0.6f, 0.2f)); return; }
            if (Has(id, "Fireworks")) { Fireworks(pos, radius); return; }
            if (Has(id, "Wind")) { Smoke(pos, radius * 0.5f + 0.8f, new Color(0.95f, 0.97f, 1f, 0.55f)); Burst(sys.smoke, pos, 10, radius * 1.5f, radius * 3f, 0.4f, 0.8f, 0.6f, 1.2f, new Color(1, 1, 1, 0.35f), new Color(0.85f, 0.95f, 1f, 0.25f)); return; }
            if (Has(id, "Teleport")) { Burst(sys.spark, pos, 30, 2f, 8f, 0.4f, 0.9f, 0.1f, 0.25f, new Color(0.7f, 0.4f, 1f), new Color(0.4f, 0.8f, 1f)); Glow(pos, 2.5f, new Color(0.6f, 0.4f, 1f)); return; }
            if (Has(id, "Confetti")) { Confetti(pos, 50); return; }
            if (Has(id, "Broom")) { Smoke(pos, 0.6f, new Color(0.8f, 0.72f, 0.55f, 0.6f)); return; }
            if (Has(id, "Void")) { VoidImplosion(pos, radius); return; }
            // remake weapons (Lemon Grenade, Orbital Plasma Attack, Grey Goo, Scythe, Choco-Cannon / Easter Eggs)
            if (Has(id, "Acid")) { Gas(pos, radius * 0.7f, new Color(0.85f, 1f, 0.2f)); Burst(sys.spark, pos, 10, 1f, 4f, 0.2f, 0.5f, 0.05f, 0.12f, new Color(0.9f, 1f, 0.3f), new Color(1f, 0.95f, 0.5f)); return; }
            if (Has(id, "Orbital")) { Colored(pos, radius, new Color(1f, 0.4f, 0.95f), new Color(0.5f, 0.9f, 1f)); Glow(pos, radius * 2.5f + 1f, new Color(1f, 0.6f, 1f)); return; }
            if (Has(id, "GreyGoo")) { Burst(sys.smoke, pos, Has(id, "Bite") ? 2 : 8, 0.3f, 1.5f, 0.3f, 0.7f, 0.15f, 0.4f, new Color(0.55f, 0.57f, 0.6f, 0.8f), new Color(0.75f, 0.77f, 0.8f, 0.6f)); return; }
            if (Has(id, "Scythe")) { Burst(sys.spark, pos, 8, 2f, 6f, 0.12f, 0.3f, 0.06f, 0.14f, new Color(0.85f, 0.9f, 1f), Color.white); return; }
            if (Has(id, "Chocolate")) { Fireball(pos, radius * 0.6f); Debris(pos, new Color(0.38f, 0.22f, 0.1f), Mathf.Clamp((int)(radius * 4), 6, 24)); Smoke(pos, radius * 0.6f + 0.5f, new Color(0.45f, 0.3f, 0.18f, 0.7f)); return; }
            // Basic*/Dynamite/MegaNuke/Chocolate/default: fireball + smoke + debris + flash
            Fireball(pos, radius);
            if (Has(id, "MegaNuke")) { Glow(pos, radius * 2.2f, new Color(1f, 0.95f, 0.8f)); Smoke(pos + Vector2.up * radius * 0.5f, radius * 1.2f, new Color(0.3f, 0.28f, 0.26f, 0.8f)); }
        }

        /// <summary>Water splash where something hits the water.</summary>
        public static void Splash(Vector2 pos, float size)
        {
            if (!Ensure()) return;
            size = Mathf.Clamp(size, 0.2f, 8f);
            int n = Mathf.Clamp((int)(12 + size * 10), 12, 60);
            for (int i = 0; i < n; i++)
            {
                var v = new Vector2(Random.Range(-0.6f, 0.6f), Random.Range(0.7f, 1.3f)) * (4f + size * 3f) * Random.Range(0.5f, 1f);
                EmitOne(sys.water, pos + new Vector2(Random.Range(-0.4f, 0.4f) * size, 0), v, Random.Range(0.12f, 0.3f) * Mathf.Sqrt(size + 0.5f), Random.Range(0.6f, 1.1f),
                    Color.Lerp(new Color(0.75f, 0.9f, 1f, 0.9f), Color.white, Random.value));
            }
            Burst(sys.smoke, pos, 6, 0.5f, 1.5f, 0.5f, 0.9f, 0.5f * size + 0.3f, size + 0.6f, new Color(1, 1, 1, 0.5f), new Color(0.8f, 0.92f, 1f, 0.4f));
        }

        public static void Smoke(Vector2 pos, float size) => Smoke(pos, size, new Color(0.25f, 0.24f, 0.24f, 0.75f));

        public static void Smoke(Vector2 pos, float size, Color color)
        {
            if (!Ensure()) return;
            size = Mathf.Clamp(size, 0.2f, 12f);
            int n = Mathf.Clamp((int)(4 + size * 3), 4, 30);
            for (int i = 0; i < n; i++)
            {
                var off = Random.insideUnitCircle * size * 0.6f;
                var v = off.normalized * Random.Range(0.3f, 1.2f) * (0.5f + size * 0.4f) + Vector2.up * Random.Range(0.4f, 1.2f);
                var c = color; c.r *= Random.Range(0.85f, 1.15f); c.g *= Random.Range(0.85f, 1.15f); c.b *= Random.Range(0.85f, 1.15f);
                EmitOne(sys.smoke, pos + off, v, Random.Range(0.6f, 1.2f) * (0.5f + size * 0.6f), Random.Range(1.1f, 2.2f), c);
            }
        }

        public static void Sparks(Vector2 pos, Color color, int count = 12)
        {
            if (!Ensure()) return;
            Burst(sys.spark, pos, Mathf.Clamp(count, 1, 80), 3f, 10f, 0.25f, 0.6f, 0.06f, 0.16f, color, Color.Lerp(color, Color.white, 0.6f));
        }

        public static void Debris(Vector2 pos, Color color, int count = 10)
        {
            if (!Ensure()) return;
            count = Mathf.Clamp(count, 1, 60);
            for (int i = 0; i < count; i++)
            {
                var v = new Vector2(Random.Range(-1f, 1f), Random.Range(0.3f, 1.4f)).normalized * Random.Range(4f, 11f);
                var c = color * Random.Range(0.7f, 1.15f); c.a = 1;
                EmitOne(sys.debris, pos + Random.insideUnitCircle * 0.3f, v, Random.Range(0.1f, 0.32f), Random.Range(0.8f, 1.5f), c, Random.Range(0, 360f));
            }
        }

        /// <summary>Flames (burning ground, burning penguin, molotov).</summary>
        public static void Fire(Vector2 pos, float size)
        {
            if (!Ensure()) return;
            size = Mathf.Clamp(size, 0.1f, 6f);
            int n = Mathf.Clamp((int)(5 + size * 8), 4, 40);
            for (int i = 0; i < n; i++)
            {
                var off = new Vector2(Random.Range(-0.5f, 0.5f) * size, Random.Range(-0.1f, 0.2f) * size);
                var v = new Vector2(Random.Range(-0.4f, 0.4f), Random.Range(1.2f, 3f) * (0.6f + size * 0.3f));
                EmitOne(sys.fire, pos + off, v, Random.Range(0.25f, 0.55f) * (0.6f + size * 0.5f), Random.Range(0.35f, 0.7f),
                    Color.Lerp(new Color(1f, 0.85f, 0.25f), new Color(1f, 0.35f, 0.08f), Random.value));
            }
        }

        /// <summary>Rising bubbles (poison, acid, goo).</summary>
        public static void Bubbles(Vector2 pos, Color color, int count = 8)
        {
            if (!Ensure()) return;
            count = Mathf.Clamp(count, 1, 40);
            for (int i = 0; i < count; i++)
            {
                var v = new Vector2(Random.Range(-0.4f, 0.4f), Random.Range(0.6f, 1.8f));
                EmitOne(sys.bubble, pos + Random.insideUnitCircle * 0.5f, v, Random.Range(0.12f, 0.3f), Random.Range(0.6f, 1.2f), color);
            }
        }

        /// <summary>Bright additive flash (explosion core, muzzle flash, nuke "Flash").</summary>
        public static void Glow(Vector2 pos, float size, Color color)
        {
            if (!Ensure()) return;
            EmitOne(sys.flash, pos, Vector2.zero, Mathf.Clamp(size, 0.2f, 60f), 0.18f, color);
        }

        public static void Confetti(Vector2 pos, int count = 40)
        {
            if (!Ensure()) return;
            for (int i = 0; i < count; i++)
            {
                var v = new Vector2(Random.Range(-1f, 1f), Random.Range(0.6f, 1.6f)) * Random.Range(4f, 10f);
                var c = Color.HSVToRGB(Random.value, 0.75f, 1f);
                EmitOne(sys.debris, pos, v, Random.Range(0.12f, 0.22f), Random.Range(1.2f, 2f), c, Random.Range(0, 360f));
            }
        }

        /// <summary>A beam (laser, railgun, sniper tracer) that fades out over durationSec.</summary>
        public static void Beam(Vector2 from, Vector2 to, Color color, float width = 0.15f, float durationSec = 0.25f)
        {
            if (!Ensure()) return;
            runner.AddBeam(from, to, color, width, durationSec);
        }

        /// <summary>Floating text in the world (damage numbers, "+25", combo floaters).</summary>
        public static void FloatText(Vector2 pos, string text, Color color, float size = 1f)
        {
            if (!Ensure()) return;
            runner.AddText(pos, text, color, size);
        }

        /// <summary>Camera shake request (durationSec, strength in world units); the battle camera reads Fx.Shake.
        /// The battle camera counts ShakeTime down and applies ShakeStrength (CurrentShake gives the faded strength).</summary>
        public static void ShakeCamera(float durationSec, float strength) { ShakeTime = Mathf.Max(ShakeTime, durationSec); ShakeStrength = Mathf.Max(ShakeStrength, strength); }
        public static float ShakeTime, ShakeStrength;
        /// <summary>Shake amplitude for this frame (fades out over the last 0.3 s).</summary>
        public static float CurrentShake => ShakeTime <= 0 ? 0 : ShakeStrength * Mathf.Clamp01(ShakeTime / 0.3f);

        /// <summary>Remove every live particle, beam and text (battle end).</summary>
        public static void ClearAll()
        {
            if (sys == null || runner == null) return;
            foreach (var ps in sys.all) if (ps) ps.Clear();
            runner.ClearAll();
            ShakeTime = 0; ShakeStrength = 0;
        }

        // ------------------------------------------------------------------ composite effects used by weapons

        /// <summary>Fire explosion: flash, fireball, smoke, sparks, debris sized by radius.</summary>
        public static void Fireball(Vector2 pos, float radius)
        {
            if (!Ensure()) return;
            radius = Mathf.Clamp(radius, 0.15f, 16f);
            Glow(pos, radius * 2.4f + 0.5f, new Color(1f, 0.85f, 0.55f));
            int n = Mathf.Clamp((int)(8 + radius * 7), 8, 70);
            for (int i = 0; i < n; i++)
            {
                var dir = Random.insideUnitCircle;
                var v = dir * radius * Random.Range(2.5f, 5f);
                EmitOne(sys.fire, pos + dir * radius * 0.3f, v, Random.Range(0.6f, 1.1f) * (0.35f + radius * 0.55f), Random.Range(0.3f, 0.6f),
                    Color.Lerp(new Color(1f, 0.9f, 0.4f), new Color(1f, 0.32f, 0.06f), Random.value));
            }
            Smoke(pos, radius * 0.8f + 0.3f);
            Burst(sys.spark, pos, Mathf.Clamp((int)(6 + radius * 4), 6, 40), 4f, 6f + radius * 3f, 0.3f, 0.8f, 0.06f, 0.15f, new Color(1f, 0.8f, 0.3f), new Color(1f, 0.5f, 0.1f));
            if (radius > 0.8f && (BattleTerrain.I == null || !BattleTerrain.CarveDebris)) Debris(pos, new Color(0.45f, 0.36f, 0.28f), Mathf.Clamp((int)(radius * 4), 4, 30));
        }

        static void Colored(Vector2 pos, float radius, Color a, Color b)
        {
            Glow(pos, radius * 2.2f + 0.5f, Color.Lerp(a, Color.white, 0.4f));
            int n = Mathf.Clamp((int)(8 + radius * 6), 8, 60);
            for (int i = 0; i < n; i++)
            {
                var dir = Random.insideUnitCircle;
                EmitOne(sys.fire, pos + dir * radius * 0.3f, dir * radius * Random.Range(2.5f, 5f), Random.Range(0.5f, 1f) * (0.35f + radius * 0.5f), Random.Range(0.3f, 0.6f), Color.Lerp(a, b, Random.value));
            }
            Burst(sys.spark, pos, Mathf.Clamp((int)(8 + radius * 4), 8, 40), 3f, 6f + radius * 3f, 0.3f, 0.7f, 0.06f, 0.16f, a, b);
            if (radius > 0.8f && (BattleTerrain.I == null || !BattleTerrain.CarveDebris)) Debris(pos, new Color(0.45f, 0.36f, 0.28f), Mathf.Clamp((int)(radius * 3), 3, 20));
        }

        static void Gas(Vector2 pos, float radius, Color c)
        {
            var sc = c; sc.a = 0.55f;
            Smoke(pos, radius * 0.7f + 0.4f, sc);
            Bubbles(pos, c, Mathf.Clamp((int)(6 + radius * 4), 6, 30));
        }

        static void BulletHit(Vector2 pos, float radius)
        {
            Burst(sys.spark, pos, 8, 3f, 8f, 0.12f, 0.3f, 0.05f, 0.12f, new Color(1f, 0.9f, 0.5f), Color.white);
            Burst(sys.smoke, pos, 3, 0.3f, 1f, 0.4f, 0.7f, 0.25f, 0.5f, new Color(0.6f, 0.6f, 0.6f, 0.6f), new Color(0.8f, 0.8f, 0.8f, 0.5f));
        }

        static void Fireworks(Vector2 pos, float radius)
        {
            var c1 = Color.HSVToRGB(Random.value, 0.8f, 1f);
            var c2 = Color.HSVToRGB(Random.value, 0.6f, 1f);
            Glow(pos, radius * 1.5f + 1f, Color.Lerp(c1, Color.white, 0.5f));
            Burst(sys.spark, pos, Mathf.Clamp((int)(25 + radius * 6), 20, 70), radius * 2f + 2f, radius * 3f + 5f, 0.6f, 1.2f, 0.08f, 0.2f, c1, c2);
        }

        static void VoidImplosion(Vector2 pos, float radius)
        {
            radius = Mathf.Min(radius, 12f);
            int n = 60;
            for (int i = 0; i < n; i++)
            {
                var d = Random.insideUnitCircle.normalized;
                var start = pos + d * radius * Random.Range(0.6f, 1f);
                EmitOne(sys.fire, start, -d * radius * 1.6f, Random.Range(0.4f, 0.9f), 0.6f, Color.Lerp(new Color(0.5f, 0.1f, 0.9f), new Color(0.1f, 0.0f, 0.3f), Random.value));
            }
            Glow(pos, radius * 2f, new Color(0.6f, 0.3f, 1f));
        }

        // ------------------------------------------------------------------ projectile tails (called by Projectile)

        /// <summary>One tail puff for a missile ParticleEffect id ("MissileTail", "PlasmaTail", "MolotovTail"...).</summary>
        internal static void Tail(string tail, Vector2 pos, Vector2 vel)
        {
            if (!Ensure() || string.IsNullOrEmpty(tail)) return;
            var back = -vel.normalized;
            if (Has(tail, "Missile") || Has(tail, "Trapezoid"))
            {
                EmitOne(sys.fire, pos, back * 2f + Random.insideUnitCircle * 0.5f, Random.Range(0.25f, 0.4f), 0.18f, new Color(1f, 0.7f, 0.25f));
                EmitOne(sys.smoke, pos, back * 0.5f + Random.insideUnitCircle * 0.3f, Random.Range(0.35f, 0.6f), Random.Range(0.6f, 1f), new Color(0.82f, 0.8f, 0.78f, 0.55f));
            }
            else if (Has(tail, "Molotov") || Has(tail, "Flaregun") || Has(tail, "Cat"))
                EmitOne(sys.fire, pos, back + Vector2.up * 1.5f, Random.Range(0.2f, 0.4f), 0.3f, Color.Lerp(new Color(1f, 0.85f, 0.3f), new Color(1f, 0.3f, 0.05f), Random.value));
            else if (Has(tail, "Plasma"))
                EmitOne(sys.fire, pos, back * 0.5f, Random.Range(0.25f, 0.45f), 0.25f, Color.Lerp(new Color(0.3f, 0.9f, 1f), new Color(0.8f, 0.3f, 1f), Random.value));
            else if (Has(tail, "Fireworks"))
                EmitOne(sys.spark, pos, back * 3f + Random.insideUnitCircle * 2f, 0.12f, 0.4f, Color.HSVToRGB(Random.value, 0.7f, 1f));
            else if (Has(tail, "Wind"))
                EmitOne(sys.smoke, pos + Random.insideUnitCircle * 0.6f, back + Random.insideUnitCircle, Random.Range(0.4f, 0.8f), 0.6f, new Color(1, 1, 1, 0.35f));
            else if (Has(tail, "Grenade"))
                EmitOne(sys.smoke, pos, Random.insideUnitCircle * 0.2f, Random.Range(0.15f, 0.25f), 0.5f, new Color(0.85f, 0.85f, 0.85f, 0.45f));
            else if (Has(tail, "Acid"))
                EmitOne(sys.smoke, pos, Vector2.up * 0.6f + Random.insideUnitCircle * 0.3f, Random.Range(0.2f, 0.35f), 0.5f, new Color(0.85f, 1f, 0.25f, 0.6f));
            else if (Has(tail, "Poison"))
                EmitOne(sys.smoke, pos + Random.insideUnitCircle * 0.8f, Random.insideUnitCircle * 0.4f, Random.Range(1.2f, 2f), 1.1f, new Color(0.5f, 0.9f, 0.25f, 0.35f));
            else if (Has(tail, "GreyGoo"))
                EmitOne(sys.smoke, pos, Random.insideUnitCircle * 0.3f, Random.Range(0.25f, 0.4f), 0.6f, new Color(0.6f, 0.62f, 0.66f, 0.7f));
        }

        // ------------------------------------------------------------------ internals

        static bool Has(string s, string part) => s.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;

        static void Burst(ParticleSystem ps, Vector2 pos, int n, float vMin, float vMax, float lMin, float lMax, float sMin, float sMax, Color a, Color b)
        {
            for (int i = 0; i < n; i++)
            {
                var d = Random.insideUnitCircle.normalized;
                EmitOne(ps, pos, d * Random.Range(vMin, vMax), Random.Range(sMin, sMax), Random.Range(lMin, lMax), Color.Lerp(a, b, Random.value));
            }
        }

        static void EmitOne(ParticleSystem ps, Vector2 pos, Vector2 vel, float size, float life, Color color, float rotation = 0)
        {
            if (!ps) return;
            var ep = new ParticleSystem.EmitParams
            {
                position = new Vector3(pos.x, pos.y, Z + Random.Range(-0.05f, 0.05f)),
                velocity = new Vector3(vel.x, vel.y, 0),
                startSize = size,
                startLifetime = life,
                startColor = color,
                rotation = rotation,
                applyShapeToPosition = false
            };
            ps.Emit(ep, 1);
        }

        sealed class Systems
        {
            public ParticleSystem fire, smoke, spark, debris, water, flash, bubble;
            public ParticleSystem[] all;
        }

        static Systems sys;
        static FxRunner runner;

        static bool Ensure()
        {
            if (sys != null && runner) return true;
            if (!Application.isPlaying) return false;
            var root = new GameObject("CPW.Fx");
            Object.DontDestroyOnLoad(root);
            runner = root.AddComponent<FxRunner>();
            var add = Mats.AdditiveTex(Mats.SoftCircle, Color.white);
            var alpha = Mats.TransparentTex(Mats.SoftCircle, Color.white);
            var square = Mats.TransparentTex(Mats.White, Color.white);
            sys = new Systems
            {
                // flames and fireballs: additive, grow then fade
                fire = Make(root.transform, "Fire", add, 900, -0.15f, 0.9f, Curve(0.6f, 1.2f, 0.2f), FadeGradient(Color.white, new Color(0.7f, 0.5f, 0.4f))),
                smoke = Make(root.transform, "Smoke", alpha, 600, -0.04f, 0.85f, Curve(0.5f, 1.4f, 1.8f), FadeGradient(Color.white, Color.white, 0.15f)),
                spark = Make(root.transform, "Sparks", add, 800, 1.2f, 0.98f, Curve(1f, 0.8f, 0.1f), FadeGradient(Color.white, Color.white)),
                debris = Make(root.transform, "Debris", square, 500, 2.2f, 1f, Curve(1f, 1f, 0.6f), FadeGradient(Color.white, Color.white, 0.75f)),
                water = Make(root.transform, "Water", alpha, 500, 1.8f, 1f, Curve(1f, 1f, 0.5f), FadeGradient(Color.white, Color.white, 0.6f)),
                flash = Make(root.transform, "Flash", add, 60, 0f, 1f, Curve(0.7f, 1.1f, 1.3f), FadeGradient(Color.white, Color.white)),
                bubble = Make(root.transform, "Bubbles", alpha, 400, -0.12f, 0.98f, Curve(0.5f, 1f, 1.2f), FadeGradient(Color.white, Color.white, 0.4f)),
            };
            sys.all = new[] { sys.fire, sys.smoke, sys.spark, sys.debris, sys.water, sys.flash, sys.bubble };
            // sparks look better stretched along their velocity
            var sr = sys.spark.GetComponent<ParticleSystemRenderer>();
            sr.renderMode = ParticleSystemRenderMode.Stretch;
            sr.velocityScale = 0.04f;
            sr.lengthScale = 1.5f;
            // debris spins
            var rot = sys.debris.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
            return true;
        }

        static AnimationCurve Curve(float a, float b, float c) => new AnimationCurve(new Keyframe(0, a), new Keyframe(0.35f, b), new Keyframe(1, c));

        static Gradient FadeGradient(Color start, Color end, float holdAlphaUntil = 0.3f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(start, 0), new GradientColorKey(end, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, holdAlphaUntil), new GradientAlphaKey(0, 1) });
            return g;
        }

        static ParticleSystem Make(Transform parent, string name, Material mat, int max, float gravity, float velocityKeep, AnimationCurve size, Gradient color)
        {
            var go = new GameObject("Fx." + name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.gravityModifier = gravity;
            main.startSpeed = 0;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = new ParticleSystem.MinMaxGradient(color);
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, size);
            if (velocityKeep < 1f)
            {
                var lv = ps.limitVelocityOverLifetime;
                lv.enabled = true;
                lv.limit = 1000f;
                lv.drag = (1f - velocityKeep) * 4f;
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingOrder = 50;
            ps.Play();
            return ps;
        }

        /// <summary>Updates beams, floating texts and the camera shake timer.</summary>
        sealed class FxRunner : MonoBehaviour
        {
            sealed class Beam { public LineRenderer lr; public float t, dur, width; public Color color; }
            sealed class Text { public Transform root; public TextMesh main, shadow; public float t, dur, size; public Color color; public Vector3 start; }

            readonly List<Beam> beams = new List<Beam>();
            readonly List<Text> texts = new List<Text>();
            Font font;

            public void AddBeam(Vector2 a, Vector2 b, Color c, float width, float dur)
            {
                Beam bm = null;
                foreach (var x in beams) if (x.t >= x.dur) { bm = x; break; }
                if (bm == null)
                {
                    if (beams.Count >= 24) bm = beams[0];
                    else
                    {
                        var go = new GameObject("Fx.Beam");
                        go.transform.SetParent(transform, false);
                        var lr = go.AddComponent<LineRenderer>();
                        lr.positionCount = 2;
                        lr.useWorldSpace = true;
                        lr.sharedMaterial = Mats.AdditiveTex(Mats.SoftCircle, Color.white);
                        lr.textureMode = LineTextureMode.Stretch;
                        lr.numCapVertices = 2;
                        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        lr.receiveShadows = false;
                        bm = new Beam { lr = lr };
                        beams.Add(bm);
                    }
                }
                bm.lr.enabled = true;
                bm.lr.SetPosition(0, new Vector3(a.x, a.y, Z));
                bm.lr.SetPosition(1, new Vector3(b.x, b.y, Z));
                bm.t = 0; bm.dur = Mathf.Max(0.05f, dur); bm.width = width; bm.color = c;
                Apply(bm, 0);
            }

            static void Apply(Beam b, float k)
            {
                float w = b.width * (1f - k * 0.7f);
                b.lr.startWidth = w; b.lr.endWidth = w;
                var c = b.color; c.a = 1f - k;
                b.lr.startColor = c; b.lr.endColor = c;
            }

            public void AddText(Vector2 pos, string s, Color c, float size)
            {
                Text tx = null;
                foreach (var x in texts) if (x.t >= x.dur) { tx = x; break; }
                if (tx == null)
                {
                    if (texts.Count >= 32) tx = texts[0];
                    else { tx = CreateText(); texts.Add(tx); }
                }
                tx.main.text = s; tx.shadow.text = s;
                tx.t = 0; tx.dur = 1.3f; tx.size = size; tx.color = c;
                tx.start = new Vector3(pos.x, pos.y, TextZ);
                tx.root.gameObject.SetActive(true);
                UpdateText(tx);
            }

            Text CreateText()
            {
                if (!font) font = Resources.Load<Font>("Fonts/LuckiestGuy") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var root = new GameObject("Fx.Text").transform;
                root.SetParent(transform, false);
                TextMesh Make(string n, Vector3 local)
                {
                    var go = new GameObject(n);
                    go.transform.SetParent(root, false);
                    go.transform.localPosition = local;
                    var tm = go.AddComponent<TextMesh>();
                    tm.font = font;
                    tm.fontSize = 64;
                    tm.characterSize = 0.14f;
                    tm.anchor = TextAnchor.MiddleCenter;
                    tm.alignment = TextAlignment.Center;
                    var mr = go.GetComponent<MeshRenderer>();
                    if (font) mr.sharedMaterial = font.material;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.sortingOrder = 100;
                    return tm;
                }
                // dark copy slightly behind and offset gives a cheap outline/drop shadow
                var shadow = Make("Shadow", new Vector3(0.06f, -0.06f, 0.02f));
                var main = Make("Main", Vector3.zero);
                return new Text { root = root, main = main, shadow = shadow };
            }

            void UpdateText(Text tx)
            {
                float k = tx.t / tx.dur;
                float rise = 1.6f * (1f - (1f - k) * (1f - k));
                tx.root.position = tx.start + new Vector3(0, rise, 0);
                float pop = k < 0.12f ? Mathf.Lerp(0.6f, 1.15f, k / 0.12f) : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((k - 0.12f) / 0.15f));
                tx.root.localScale = Vector3.one * tx.size * pop;
                float a = k < 0.65f ? 1f : 1f - (k - 0.65f) / 0.35f;
                var c = tx.color; c.a = a;
                tx.main.color = c;
                tx.shadow.color = new Color(0.05f, 0.05f, 0.1f, a * 0.85f);
            }

            public void ClearAll()
            {
                foreach (var b in beams) { b.t = b.dur; b.lr.enabled = false; }
                foreach (var t in texts) { t.t = t.dur; t.root.gameObject.SetActive(false); }
            }

            void Update()
            {
                float dt = Time.deltaTime;
                for (int i = 0; i < beams.Count; i++)
                {
                    var b = beams[i];
                    if (b.t >= b.dur) continue;
                    b.t += dt;
                    if (b.t >= b.dur) b.lr.enabled = false; else Apply(b, b.t / b.dur);
                }
                for (int i = 0; i < texts.Count; i++)
                {
                    var t = texts[i];
                    if (t.t >= t.dur) continue;
                    t.t += dt;
                    if (t.t >= t.dur) t.root.gameObject.SetActive(false); else UpdateText(t);
                }
            }
        }
    }
}
