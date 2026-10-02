using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Visual effects shared by the battle: explosions, splashes, smoke, sparks, debris, fire, bubbles,
    /// laser beams, floating text and camera shake. Everything is built from code: persistent world-space
    /// ParticleSystems (particles are emitted with EmitParams, so there is no per-effect GameObject), a pool of
    /// SpriteRenderers for the original Flash one-shot animations (particle_explosion, explosion_cloud,
    /// teleport_spinner, void_generator_explosion...), a pool of LineRenderers for beams and a pool of TextMesh
    /// sets for floating text. Particles use the original particle sprites (fx/particles, see FxSprites.cs) and
    /// fall back to soft procedural blobs when the art is missing. No allocations per frame; visual randomness
    /// uses VisualRandom, never the simulation's UnityEngine.Random.
    /// </summary>
    public static partial class Fx
    {
        /// <summary>Z of effects (in front of terrain and penguins, the battle camera looks down +z).</summary>
        public const float Z = -1.6f;
        const float TextZ = -2.6f;
        /// <summary>sortingOrder of effects (missiles 40, floating text 100).</summary>
        public const int Order = 50;

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
            if (Has(id, "Laser")) { LaserHit(pos, radius); return; }
            if (Has(id, "Plasma")) { PlasmaBlast(pos, radius); return; }
            if (Has(id, "Poison") || Has(id, "GreenGoo") || Has(id, "Mushroom")) { Gas(pos, radius, new Color(0.45f, 0.95f, 0.25f), Has(id, "Poison")); return; }
            if (Has(id, "Molotov")) { if (Has(id, "Burning")) Fire(pos, radius * 0.6f + 0.3f); else { Fire(pos, radius + 0.4f); FlameBurst(pos, radius); } return; }
            if (Has(id, "Stone")) { if (!TerrainDebris(pos, 12)) Debris(pos, new Color(0.55f, 0.52f, 0.48f), 14); Smoke(pos, radius * 0.6f + 0.6f, new Color(0.75f, 0.7f, 0.62f, 0.7f)); return; }
            if (Has(id, "Cat")) { CatBlast(pos); return; }
            if (Has(id, "Fireworks")) { Fireworks(pos, radius); return; }
            if (Has(id, "Wind")) { WindBlast(pos, radius); return; }
            if (Has(id, "Teleport")) { Teleport(pos, radius); return; }
            if (Has(id, "Confetti")) { Confetti(pos, 50); Stars(pos, 12, new Color(1f, 0.96f, 0.25f)); return; }
            if (Has(id, "Broom")) { if (!Stars(pos, 8, new Color(0.55f, 0.15f, 0.95f))) Smoke(pos, 0.6f, new Color(0.8f, 0.72f, 0.55f, 0.6f)); return; }
            if (Has(id, "Void")) { VoidImplosion(pos, radius); return; }
            // remake weapons (Lemon Grenade, Orbital Plasma Attack, Grey Goo, Scythe, Choco-Cannon / Easter Eggs)
            if (Has(id, "Acid")) { AcidBlast(pos, radius); return; }
            if (Has(id, "Orbital")) { OrbitalBlast(pos, radius); return; }
            if (Has(id, "GreyGoo")) { Burst(sys.smoke, pos, Has(id, "Bite") ? 2 : 8, 0.3f, 1.5f, 0.3f, 0.7f, 0.15f, 0.4f, new Color(0.55f, 0.57f, 0.6f, 0.8f), new Color(0.75f, 0.77f, 0.8f, 0.6f)); return; }
            if (Has(id, "Scythe")) { ScytheBlast(pos); return; }
            if (Has(id, "Chocolate")) { ChocolateBlast(pos, radius); return; }
            if (Has(id, "Dynamite")) { DynamiteBlast(pos, radius); return; }
            // Basic*/MegaNuke/default: flash disc + cloud smoke + sparkles + debris
            Fireball(pos, radius);
            if (Has(id, "MegaNuke"))
            {
                Glow(pos, radius * 2.2f, new Color(1f, 0.95f, 0.8f));
                if (!Anim(Art.CloudGrey, pos, Mathf.Clamp(radius / 7f, 0.4f, 2.2f), new Color(1f, 1f, 1f, 0.95f), false, 3f, 0.75f, 0.15f))
                    Smoke(pos + Vector2.up * radius * 0.5f, radius * 1.2f, new Color(0.3f, 0.28f, 0.26f, 0.8f));
            }
        }

        /// <summary>Splash where something hits the liquid (water drops, lava balls or mud bubbles by the level's liquid).</summary>
        public static void Splash(Vector2 pos, float size)
        {
            if (!Ensure()) return;
            size = Mathf.Clamp(size, 0.2f, 8f);
            string liquid = BattleTerrain.I != null && BattleTerrain.I.Level != null ? BattleTerrain.I.Level.liquid : "Water";
            var fam = liquid == "Lava" ? Art.Lava : liquid == "Mud" ? Art.Mud : Art.Water;
            int n = Mathf.Clamp((int)(10 + size * 8), 10, 48);
            bool sprites = fam.Ready;
            for (int i = 0; i < n; i++)
            {
                var v = new Vector2(VisualRandom.Range(-0.6f, 0.6f), VisualRandom.Range(0.7f, 1.3f)) * (4f + size * 3f) * VisualRandom.Range(0.5f, 1f);
                var p = pos + new Vector2(VisualRandom.Range(-0.4f, 0.4f) * size, 0);
                float s = VisualRandom.Range(0.12f, 0.3f) * Mathf.Sqrt(size + 0.5f);
                if (sprites) EmitS(fam, p, v, s * 2.6f, VisualRandom.Range(0.6f, 1.1f), Color.white, VisualRandom.Range(0, 360f));
                else EmitOne(sys.water, p, v, s, VisualRandom.Range(0.6f, 1.1f), Color.Lerp(new Color(0.75f, 0.9f, 1f, 0.9f), Color.white, VisualRandom.Value));
            }
            if (liquid == "Lava") Fire(pos, size * 0.5f + 0.2f);
            else Burst(sys.smoke, pos, 6, 0.5f, 1.5f, 0.5f, 0.9f, 0.5f * size + 0.3f, size + 0.6f, new Color(1, 1, 1, 0.5f), new Color(0.8f, 0.92f, 1f, 0.4f));
        }

        public static void Smoke(Vector2 pos, float size) => Smoke(pos, size, new Color(0.25f, 0.24f, 0.24f, 0.75f));

        public static void Smoke(Vector2 pos, float size, Color color)
        {
            if (!Ensure()) return;
            size = Mathf.Clamp(size, 0.2f, 12f);
            int n = Mathf.Clamp((int)(4 + size * 3), 4, 30);
            bool sprites = Art.Clouds.Ready;
            // the original cloud sprites are light grey: brighten dark tints so they still read as smoke
            var tint = sprites ? new Color(Mathf.Min(1f, color.r * 1.6f + 0.1f), Mathf.Min(1f, color.g * 1.6f + 0.1f), Mathf.Min(1f, color.b * 1.6f + 0.1f), color.a) : color;
            for (int i = 0; i < n; i++)
            {
                var off = VisualRandom.InsideUnitCircle * size * 0.6f;
                var v = off.normalized * VisualRandom.Range(0.3f, 1.2f) * (0.5f + size * 0.4f) + Vector2.up * VisualRandom.Range(0.4f, 1.2f);
                var c = tint; c.r *= VisualRandom.Range(0.85f, 1.15f); c.g *= VisualRandom.Range(0.85f, 1.15f); c.b *= VisualRandom.Range(0.85f, 1.15f);
                float s = VisualRandom.Range(0.6f, 1.2f) * (0.5f + size * 0.6f), life = VisualRandom.Range(1.1f, 2.2f);
                if (sprites) EmitS(Art.Clouds, pos + off, v, s * 0.85f, life, c, VisualRandom.Range(0, 360f));
                else EmitOne(sys.smoke, pos + off, v, s, life, c);
            }
        }

        public static void Sparks(Vector2 pos, Color color, int count = 12)
        {
            if (!Ensure()) return;
            Burst(sys.spark, pos, Mathf.Clamp(count, 1, 80), 3f, 10f, 0.25f, 0.6f, 0.06f, 0.16f, color, Color.Lerp(color, Color.white, 0.6f));
        }

        /// <summary>Flying chips of a solid colour (props, mushrooms, walls). Terrain chunks: TerrainDebris.</summary>
        public static void Debris(Vector2 pos, Color color, int count = 10)
        {
            if (!Ensure()) return;
            count = Mathf.Clamp(count, 1, 60);
            for (int i = 0; i < count; i++)
            {
                var v = new Vector2(VisualRandom.Range(-1f, 1f), VisualRandom.Range(0.3f, 1.4f)).normalized * VisualRandom.Range(4f, 11f);
                var c = color * VisualRandom.Range(0.7f, 1.15f); c.a = 1;
                EmitOne(sys.debris, pos + VisualRandom.InsideUnitCircle * 0.3f, v, VisualRandom.Range(0.1f, 0.32f), VisualRandom.Range(0.8f, 1.5f), c, VisualRandom.Range(0, 360f));
            }
        }

        /// <summary>Chunks of the level's ground (the theme's original particle_1..5 bitmaps). False (nothing drawn)
        /// when the theme has no such art; the caller then uses Debris with a colour.</summary>
        public static bool TerrainDebris(Vector2 pos, int count = 10)
        {
            if (!Ensure()) return false;
            var fam = Art.ThemeDebris();
            if (fam == null || !fam.Ready) return false;
            count = Mathf.Clamp(count, 1, 40);
            for (int i = 0; i < count; i++)
            {
                var v = new Vector2(VisualRandom.Range(-1f, 1f), VisualRandom.Range(0.3f, 1.4f)).normalized * VisualRandom.Range(4f, 11f);
                EmitS(fam, pos + VisualRandom.InsideUnitCircle * 0.3f, v, VisualRandom.Range(0.25f, 0.6f), VisualRandom.Range(0.8f, 1.5f), Color.white, VisualRandom.Range(0, 360f));
            }
            return true;
        }

        /// <summary>Flames (burning ground, burning penguin, molotov).</summary>
        public static void Fire(Vector2 pos, float size)
        {
            if (!Ensure()) return;
            size = Mathf.Clamp(size, 0.1f, 6f);
            int n = Mathf.Clamp((int)(4 + size * 6), 3, 32);
            bool sprites = Art.Flames.Ready;
            for (int i = 0; i < n; i++)
            {
                var off = new Vector2(VisualRandom.Range(-0.5f, 0.5f) * size, VisualRandom.Range(-0.1f, 0.2f) * size);
                var v = new Vector2(VisualRandom.Range(-0.4f, 0.4f), VisualRandom.Range(1.2f, 3f) * (0.6f + size * 0.3f));
                float s = VisualRandom.Range(0.25f, 0.55f) * (0.6f + size * 0.5f), life = VisualRandom.Range(0.35f, 0.7f);
                if (sprites) EmitS(Art.Flames, pos + off, v, s * 1.5f, life, Color.Lerp(Color.white, new Color(1f, 0.8f, 0.6f), VisualRandom.Value), VisualRandom.Range(-20f, 20f));
                else EmitOne(sys.fire, pos + off, v, s, life, Color.Lerp(new Color(1f, 0.85f, 0.25f), new Color(1f, 0.35f, 0.08f), VisualRandom.Value));
            }
        }

        /// <summary>Rising bubbles (poison, acid, goo).</summary>
        public static void Bubbles(Vector2 pos, Color color, int count = 8)
        {
            if (!Ensure()) return;
            count = Mathf.Clamp(count, 1, 40);
            bool sprites = Art.Bubbles.Ready;
            for (int i = 0; i < count; i++)
            {
                var v = new Vector2(VisualRandom.Range(-0.4f, 0.4f), VisualRandom.Range(0.6f, 1.8f));
                float s = VisualRandom.Range(0.12f, 0.3f), life = VisualRandom.Range(0.6f, 1.2f);
                if (sprites) EmitS(Art.Bubbles, pos + VisualRandom.InsideUnitCircle * 0.5f, v, s * 2f, life, color);
                else EmitOne(sys.bubble, pos + VisualRandom.InsideUnitCircle * 0.5f, v, s, life, color);
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
            count = Mathf.Clamp(count, 1, 80);
            bool sprites = Art.Confetti.Ready;
            for (int i = 0; i < count; i++)
            {
                var v = new Vector2(VisualRandom.Range(-1f, 1f), VisualRandom.Range(0.6f, 1.6f)) * VisualRandom.Range(4f, 10f);
                if (sprites) EmitS(Art.Confetti, pos, v, VisualRandom.Range(0.3f, 0.5f), VisualRandom.Range(1.4f, 2.4f), Color.white, VisualRandom.Range(0, 360f));
                else EmitOne(sys.debris, pos, v, VisualRandom.Range(0.12f, 0.22f), VisualRandom.Range(1.2f, 2f), Color.HSVToRGB(VisualRandom.Value, 0.75f, 1f), VisualRandom.Range(0, 360f));
            }
        }

        /// <summary>A beam (laser, railgun, sniper tracer) that fades out over durationSec.</summary>
        public static void Beam(Vector2 from, Vector2 to, Color color, float width = 0.15f, float durationSec = 0.25f)
        {
            if (!Ensure()) return;
            runner.AddBeam(from, to, color, width, durationSec);
        }

        /// <summary>Floating text in the world (damage numbers, "+25", combo floaters): white letters with a
        /// thick outline in color, like the original character_ui floaters.</summary>
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

        /// <summary>Remove every live particle, animation, beam and text (battle end).</summary>
        public static void ClearAll()
        {
            if (sys == null || runner == null) return;
            foreach (var ps in sys.all) if (ps) ps.Clear();
            Art.ClearAll();
            runner.ClearAll();
            ShakeTime = 0; ShakeStrength = 0;
        }

        // ------------------------------------------------------------------ composite effects used by weapons

        /// <summary>Fire explosion: flash, the original particle_explosion disc, cloud smoke, sparks, debris by radius.</summary>
        public static void Fireball(Vector2 pos, float radius)
        {
            if (!Ensure()) return;
            radius = Mathf.Clamp(radius, 0.15f, 16f);
            Glow(pos, radius * 2.4f + 0.5f, new Color(1f, 0.85f, 0.55f));
            if (!Disc(pos, radius, new Color(1f, 0.96f, 0.55f)))
            {
                int n = Mathf.Clamp((int)(8 + radius * 7), 8, 70);
                for (int i = 0; i < n; i++)
                {
                    var dir = VisualRandom.InsideUnitCircle;
                    var v = dir * radius * VisualRandom.Range(2.5f, 5f);
                    EmitOne(sys.fire, pos + dir * radius * 0.3f, v, VisualRandom.Range(0.6f, 1.1f) * (0.35f + radius * 0.55f), VisualRandom.Range(0.3f, 0.6f),
                        Color.Lerp(new Color(1f, 0.9f, 0.4f), new Color(1f, 0.32f, 0.06f), VisualRandom.Value));
                }
            }
            else FlameBurst(pos, radius * 0.5f);
            Smoke(pos, radius * 0.8f + 0.3f, new Color(0.42f, 0.4f, 0.4f, 0.8f));
            Burst(sys.spark, pos, Mathf.Clamp((int)(6 + radius * 4), 6, 40), 4f, 6f + radius * 3f, 0.3f, 0.8f, 0.06f, 0.15f, new Color(1f, 0.8f, 0.3f), new Color(1f, 0.5f, 0.1f));
            if (radius > 0.8f && (BattleTerrain.I == null || !BattleTerrain.CarveDebris)) EarthDebris(pos, Mathf.Clamp((int)(radius * 4), 4, 30));
        }

        /// <summary>The original explosion core (particle_explosion, a growing glowing disc) tinted, sized by radius.</summary>
        static bool Disc(Vector2 pos, float radius, Color tint)
        {
            // the disc peaks (one frame) at 280 Flash px = 14 units wide: radius / 4.5 keeps its bright core near the crater
            return Anim(Art.ExplosionDisc, pos, Mathf.Clamp(radius / 4.5f, 0.1f, 3f), tint, false, 0f, 1f, 0.3f, VisualRandom.Range(0f, 360f));
        }

        static void EarthDebris(Vector2 pos, int count)
        {
            if (!TerrainDebris(pos, count)) Debris(pos, new Color(0.45f, 0.36f, 0.28f), count);
        }

        static void FlameBurst(Vector2 pos, float radius)
        {
            if (!Art.Flames.Ready) { Fire(pos, radius); return; }
            int n = Mathf.Clamp((int)(4 + radius * 4), 4, 24);
            for (int i = 0; i < n; i++)
            {
                var dir = VisualRandom.InsideUnitCircle;
                EmitS(Art.Flames, pos + dir * radius * 0.3f, dir * radius * VisualRandom.Range(2f, 4f) + Vector2.up, VisualRandom.Range(0.5f, 0.9f) * (0.5f + radius * 0.5f),
                    VisualRandom.Range(0.35f, 0.6f), Color.white, VisualRandom.Range(0, 360f));
            }
        }

        static void Colored(Vector2 pos, float radius, Color a, Color b)
        {
            Glow(pos, radius * 2.2f + 0.5f, Color.Lerp(a, Color.white, 0.4f));
            int n = Mathf.Clamp((int)(8 + radius * 6), 8, 60);
            for (int i = 0; i < n; i++)
            {
                var dir = VisualRandom.InsideUnitCircle;
                EmitOne(sys.fire, pos + dir * radius * 0.3f, dir * radius * VisualRandom.Range(2.5f, 5f), VisualRandom.Range(0.5f, 1f) * (0.35f + radius * 0.5f), VisualRandom.Range(0.3f, 0.6f), Color.Lerp(a, b, VisualRandom.Value));
            }
            Burst(sys.spark, pos, Mathf.Clamp((int)(8 + radius * 4), 8, 40), 3f, 6f + radius * 3f, 0.3f, 0.7f, 0.06f, 0.16f, a, b);
            if (radius > 0.8f && (BattleTerrain.I == null || !BattleTerrain.CarveDebris)) EarthDebris(pos, Mathf.Clamp((int)(radius * 3), 3, 20));
        }

        /// <summary>PlasmaExplosion: cyan-tinted disc + plasma cloud burst + sparkles.</summary>
        static void PlasmaBlast(Vector2 pos, float radius)
        {
            if (!Art.PlasmaClouds.Ready || !Art.ExplosionDisc.Ready) { Colored(pos, radius, new Color(0.3f, 0.9f, 1f), new Color(0.85f, 0.35f, 1f)); return; }
            Glow(pos, radius * 2.2f + 0.5f, new Color(0.6f, 0.95f, 1f));
            Disc(pos, radius, new Color(0.6f, 1f, 1f));
            CloudBurst(Art.PlasmaClouds, pos, radius, Mathf.Clamp((int)(8 + radius * 5), 8, 30), Color.white);
            Burst(sys.spark, pos, Mathf.Clamp((int)(8 + radius * 4), 8, 40), 3f, 6f + radius * 3f, 0.3f, 0.7f, 0.06f, 0.16f, new Color(0.3f, 0.9f, 1f), new Color(0.85f, 0.35f, 1f));
            if (radius > 0.8f && (BattleTerrain.I == null || !BattleTerrain.CarveDebris)) EarthDebris(pos, Mathf.Clamp((int)(radius * 3), 3, 20));
        }

        /// <summary>Orbital strike: pale blue disc + the blue explosion_cloud rising from the impact + plasma clouds.</summary>
        static void OrbitalBlast(Vector2 pos, float radius)
        {
            if (!Art.ExplosionDisc.Ready) { Colored(pos, radius, new Color(1f, 0.4f, 0.95f), new Color(0.5f, 0.9f, 1f)); Glow(pos, radius * 2.5f + 1f, new Color(1f, 0.6f, 1f)); return; }
            Glow(pos, radius * 2.5f + 1f, new Color(0.75f, 0.9f, 1f));
            Disc(pos, radius, new Color(0.68f, 0.9f, 1f));
            Anim(Art.CloudBlue, pos, Mathf.Clamp(radius / 8f, 0.25f, 1.5f), Color.white, false, 3f, 0.8f, 0.1f);
            CloudBurst(Art.PlasmaClouds, pos, radius, Mathf.Clamp((int)(6 + radius * 3), 6, 20), new Color(0.68f, 0.9f, 1f));
            if (radius > 0.8f && (BattleTerrain.I == null || !BattleTerrain.CarveDebris)) EarthDebris(pos, Mathf.Clamp((int)(radius * 3), 3, 20));
        }

        /// <summary>DynamiteExplosion: disc + the grey explosion_cloud + smoke shards.</summary>
        static void DynamiteBlast(Vector2 pos, float radius)
        {
            Fireball(pos, radius);
            Anim(Art.CloudGrey, pos, Mathf.Clamp(radius / 8f, 0.25f, 1.5f), Color.white, false, 3f, 0.8f, 0.1f);
        }

        static void LaserHit(Vector2 pos, float radius)
        {
            Burst(sys.spark, pos, 14, 2f, 7f, 0.25f, 0.5f, 0.08f, 0.18f, new Color(1f, 0.25f, 0.2f), new Color(1f, 0.8f, 0.8f));
            if (!Anim(Art.LaserHit, pos, Mathf.Clamp(0.6f + radius * 0.3f, 0.5f, 2f), Color.white, true, 0.45f, 0.3f, 0.4f, VisualRandom.Range(0f, 360f)))
                Glow(pos, radius * 1.6f + 0.6f, new Color(1f, 0.2f, 0.2f));
        }

        static void Gas(Vector2 pos, float radius, Color c, bool skulls)
        {
            if (Art.Poison.Ready)
            {
                CloudBurst(Art.Poison, pos, radius * 0.7f + 0.4f, Mathf.Clamp((int)(6 + radius * 4), 6, 24), Color.white, 0.4f);
                if (skulls)
                    for (int i = 0; i < 2; i++)
                        Anim(Art.Skull, pos + new Vector2(VisualRandom.Range(-1f, 1f) * radius * 0.5f, 0), 0.6f, Color.white, false, 1.5f, 0.6f, 0f, 0f, new Vector2(0, 0.8f));
            }
            else
            {
                var sc = c; sc.a = 0.55f;
                Smoke(pos, radius * 0.7f + 0.4f, sc);
            }
            Bubbles(pos, c, Mathf.Clamp((int)(6 + radius * 4), 6, 30));
        }

        static void AcidBlast(Vector2 pos, float radius)
        {
            if (!Art.Acid.Ready) { Gas(pos, radius * 0.7f, new Color(0.85f, 1f, 0.2f), false); Burst(sys.spark, pos, 10, 1f, 4f, 0.2f, 0.5f, 0.05f, 0.12f, new Color(0.9f, 1f, 0.3f), new Color(1f, 0.95f, 0.5f)); return; }
            int n = Mathf.Clamp((int)(6 + radius * 4), 6, 20);
            for (int i = 0; i < n; i++)
            {
                var v = new Vector2(VisualRandom.Range(-1f, 1f), VisualRandom.Range(0.4f, 1.4f)) * VisualRandom.Range(2f, 5f);
                EmitS(Art.Acid, pos, v, VisualRandom.Range(0.5f, 0.9f), VisualRandom.Range(0.6f, 1.1f), Color.white, VisualRandom.Range(-30f, 30f));
            }
            Bubbles(pos, new Color(0.88f, 1f, 0.2f), Mathf.Clamp((int)(4 + radius * 3), 4, 16));
        }

        static void BulletHit(Vector2 pos, float radius)
        {
            Burst(sys.spark, pos, 8, 3f, 8f, 0.12f, 0.3f, 0.05f, 0.12f, new Color(1f, 0.9f, 0.5f), Color.white);
            Burst(sys.smoke, pos, 3, 0.3f, 1f, 0.4f, 0.7f, 0.25f, 0.5f, new Color(0.6f, 0.6f, 0.6f, 0.6f), new Color(0.8f, 0.8f, 0.8f, 0.5f));
        }

        static void Fireworks(Vector2 pos, float radius)
        {
            var c1 = Color.HSVToRGB(VisualRandom.Value, 0.8f, 1f);
            var c2 = Color.HSVToRGB(VisualRandom.Value, 0.6f, 1f);
            Glow(pos, radius * 1.5f + 1f, Color.Lerp(c1, Color.white, 0.5f));
            Burst(sys.spark, pos, Mathf.Clamp((int)(25 + radius * 6), 20, 70), radius * 2f + 2f, radius * 3f + 5f, 0.6f, 1.2f, 0.08f, 0.2f, c1, c2);
            if (Art.Confetti.Ready) { Confetti(pos, 20); Stars(pos, 10, new Color(1f, 0.96f, 0.25f)); }
        }

        static void WindBlast(Vector2 pos, float radius)
        {
            if (!Art.Wind.Ready)
            {
                Smoke(pos, radius * 0.5f + 0.8f, new Color(0.95f, 0.97f, 1f, 0.55f));
                Burst(sys.smoke, pos, 10, radius * 1.5f, radius * 3f, 0.4f, 0.8f, 0.6f, 1.2f, new Color(1, 1, 1, 0.35f), new Color(0.85f, 0.95f, 1f, 0.25f));
                return;
            }
            for (int i = 0; i < 8; i++)
            {
                var d = VisualRandom.OnUnitCircle;
                EmitS(Art.Wind, pos + d * 0.3f, d * VisualRandom.Range(radius * 1.2f, radius * 2.5f), VisualRandom.Range(0.8f, 1.4f), VisualRandom.Range(0.6f, 1f), Color.white, VisualRandom.Range(0, 360f));
            }
            if (Art.Clouds.Ready) CloudBurst(Art.Clouds, pos, radius, 10, new Color(1f, 1f, 1f, 0.6f));
        }

        static void Teleport(Vector2 pos, float radius)
        {
            Glow(pos, 2.5f, new Color(0.6f, 0.4f, 1f));
            bool spinner = Anim(Art.Spinner, pos, Mathf.Clamp(radius * 0.35f, 0.2f, 1f), Color.white, true, 1f, 0.5f, -0.6f);
            if (!Stars(pos, 16, new Color(0.75f, 0.5f, 1f)) || !spinner)
                Burst(sys.spark, pos, 30, 2f, 8f, 0.4f, 0.9f, 0.1f, 0.25f, new Color(0.7f, 0.4f, 1f), new Color(0.4f, 0.8f, 1f));
        }

        static void CatBlast(Vector2 pos)
        {
            if (!Art.CatHair.Ready) { Burst(sys.spark, pos, 10, 2f, 6f, 0.15f, 0.35f, 0.08f, 0.16f, Color.white, new Color(1f, 0.6f, 0.2f)); return; }
            for (int i = 0; i < 10; i++)
            {
                var d = VisualRandom.OnUnitCircle;
                EmitS(Art.CatHair, pos, d * VisualRandom.Range(2f, 6f), VisualRandom.Range(0.4f, 0.8f), VisualRandom.Range(0.6f, 1.2f), Color.white, VisualRandom.Range(0, 360f));
            }
            Anim(Art.CatSilhouette, pos + VisualRandom.InsideUnitCircle, 1f, new Color(1f, 1f, 1f, 0.8f), false, 0.5f, 0.4f, 0.2f);
            Smoke(pos, 0.8f, new Color(0.18f, 0.21f, 0.24f, 0.7f));
        }

        static void ScytheBlast(Vector2 pos)
        {
            if (!Anim(Art.Skull, pos, 0.7f, new Color(0.45f, 0.5f, 0.55f), false, 1.5f, 0.6f, 0f, 0f, new Vector2(0, 0.6f)))
                Burst(sys.spark, pos, 8, 2f, 6f, 0.12f, 0.3f, 0.06f, 0.14f, new Color(0.85f, 0.9f, 1f), Color.white);
            Smoke(pos, 0.7f, new Color(0.18f, 0.21f, 0.24f, 0.7f));
        }

        static void ChocolateBlast(Vector2 pos, float radius)
        {
            Glow(pos, radius * 1.6f + 0.5f, new Color(1f, 0.85f, 0.6f));
            int n = Mathf.Clamp((int)(radius * 4), 6, 24);
            if (Art.Chocolate.Ready)
            {
                for (int i = 0; i < n; i++)
                {
                    var v = new Vector2(VisualRandom.Range(-1f, 1f), VisualRandom.Range(0.3f, 1.4f)).normalized * VisualRandom.Range(4f, 10f);
                    EmitS(Art.Chocolate, pos, v, VisualRandom.Range(0.4f, 0.9f), VisualRandom.Range(0.9f, 1.6f), Color.white, VisualRandom.Range(0, 360f));
                }
            }
            else { Fireball(pos, radius * 0.6f); Debris(pos, new Color(0.38f, 0.22f, 0.1f), n); }
            Smoke(pos, radius * 0.6f + 0.5f, new Color(0.45f, 0.3f, 0.18f, 0.7f));
        }

        static void VoidImplosion(Vector2 pos, float radius)
        {
            radius = Mathf.Min(radius, 12f);
            // AnimationGraphic.VoidGenerator (void_generator_explosion, 590 Flash px = 29.5 units at scale 1) about 1.2x the pull radius
            Anim(Art.VoidBlast, pos, Mathf.Clamp(radius / 12f, 0.2f, 1.2f), Color.white, false, 0f, 0.85f);
            int n = 40;
            for (int i = 0; i < n; i++)
            {
                var d = VisualRandom.OnUnitCircle;
                var start = pos + d * radius * VisualRandom.Range(0.6f, 1f);
                EmitOne(sys.fire, start, -d * radius * 1.6f, VisualRandom.Range(0.4f, 0.9f), 0.6f, Color.Lerp(new Color(0.5f, 0.1f, 0.9f), new Color(0.1f, 0.0f, 0.3f), VisualRandom.Value));
            }
            Glow(pos, radius * 2f, new Color(0.6f, 0.3f, 1f));
        }

        /// <summary>Cloud particles of a family thrown outward fast and slowing down (the original Smoke/PlasmaCloudExplosion).</summary>
        static void CloudBurst(SpriteFamily fam, Vector2 pos, float radius, int n, Color color, float speed = 1f)
        {
            for (int i = 0; i < n; i++)
            {
                var d = VisualRandom.InsideUnitCircle;
                var c = color; c.a *= VisualRandom.Range(0.75f, 1f);
                EmitS(fam, pos + d * radius * 0.3f, d * radius * VisualRandom.Range(2f, 4f) * speed + Vector2.up * 0.4f, VisualRandom.Range(0.5f, 1f) * (0.5f + radius * 0.45f),
                    VisualRandom.Range(0.8f, 1.5f), c, VisualRandom.Range(0, 360f));
            }
        }

        /// <summary>Twinkling stars (teleport sparkles, confetti stars, broom). False when the art is missing.</summary>
        static bool Stars(Vector2 pos, int n, Color tint)
        {
            if (!Art.Sparkle.Ready) return false;
            for (int i = 0; i < n; i++)
            {
                var d = VisualRandom.InsideUnitCircle;
                EmitS(Art.Sparkle, pos + d * 0.8f, d * VisualRandom.Range(1.5f, 4f), VisualRandom.Range(0.3f, 0.6f), VisualRandom.Range(0.6f, 1.2f), tint, VisualRandom.Range(0, 360f));
            }
            return true;
        }

        // ------------------------------------------------------------------ projectile tails (called by Projectile)

        /// <summary>One tail puff for a missile ParticleEffect id ("MissileTail", "PlasmaTail", "MolotovTail"...).</summary>
        internal static void Tail(string tail, Vector2 pos, Vector2 vel)
        {
            if (!Ensure() || string.IsNullOrEmpty(tail)) return;
            var back = vel.sqrMagnitude > 1e-6f ? -vel.normalized : Vector2.down;
            if (Has(tail, "Trapezoid") && Art.Trapezoid.Ready)
            {
                // the original stamps the bitmap with the missile's rotation (art "up" along the flight), shrinking
                var set = Has(tail, "Small") ? Art.TrapezoidSmall : Art.Trapezoid;
                Anim(set, pos, 1f, new Color(1f, 1f, 1f, 0.85f), false, 0.5f, 0.1f, -0.6f, Mathf.Atan2(-back.y, -back.x) * Mathf.Rad2Deg - 90f);
            }
            else if (Has(tail, "Missile") || Has(tail, "Trapezoid"))
            {
                EmitOne(sys.fire, pos, back * 2f + VisualRandom.InsideUnitCircle * 0.5f, VisualRandom.Range(0.25f, 0.4f), 0.18f, new Color(1f, 0.7f, 0.25f));
                if (Art.Clouds.Ready) EmitS(Art.Clouds, pos, back * 0.5f + VisualRandom.InsideUnitCircle * 0.3f, VisualRandom.Range(0.45f, 0.75f), VisualRandom.Range(0.6f, 1f), new Color(1f, 1f, 1f, 0.7f), VisualRandom.Range(0, 360f));
                else EmitOne(sys.smoke, pos, back * 0.5f + VisualRandom.InsideUnitCircle * 0.3f, VisualRandom.Range(0.35f, 0.6f), VisualRandom.Range(0.6f, 1f), new Color(0.82f, 0.8f, 0.78f, 0.55f));
            }
            else if (Has(tail, "Molotov") || Has(tail, "Flaregun") || Has(tail, "Cat"))
            {
                if (Art.Flames.Ready) EmitS(Art.Flames, pos, back + Vector2.up * 1.5f, VisualRandom.Range(0.35f, 0.6f), 0.35f, Color.white, VisualRandom.Range(-30f, 30f));
                else EmitOne(sys.fire, pos, back + Vector2.up * 1.5f, VisualRandom.Range(0.2f, 0.4f), 0.3f, Color.Lerp(new Color(1f, 0.85f, 0.3f), new Color(1f, 0.3f, 0.05f), VisualRandom.Value));
            }
            else if (Has(tail, "Plasma"))
            {
                if (Art.Bubbles.Ready) EmitS(Art.Bubbles, pos, back * 0.5f + VisualRandom.InsideUnitCircle * 0.4f, VisualRandom.Range(0.3f, 0.55f), 0.4f, new Color(0.45f, 1f, 1f));
                else EmitOne(sys.fire, pos, back * 0.5f, VisualRandom.Range(0.25f, 0.45f), 0.25f, Color.Lerp(new Color(0.3f, 0.9f, 1f), new Color(0.8f, 0.3f, 1f), VisualRandom.Value));
            }
            else if (Has(tail, "Laser") && Art.Laser.Ready)
                EmitS(Art.Laser, pos, Vector2.zero, VisualRandom.Range(0.6f, 0.8f), 0.5f, Color.white);
            else if (Has(tail, "Fireworks"))
            {
                if (Art.Confetti.Ready) EmitS(Art.Confetti, pos, back * 2f + VisualRandom.InsideUnitCircle, 0.35f, 0.8f, Color.white, VisualRandom.Range(0, 360f));
                EmitOne(sys.spark, pos, back * 3f + VisualRandom.InsideUnitCircle * 2f, 0.12f, 0.4f, Color.HSVToRGB(VisualRandom.Value, 0.7f, 1f));
            }
            else if (Has(tail, "Wind"))
            {
                if (Art.Wind.Ready) EmitS(Art.Wind, pos + VisualRandom.InsideUnitCircle * 0.6f, back + VisualRandom.InsideUnitCircle, VisualRandom.Range(0.6f, 1f), 0.6f, Color.white, VisualRandom.Range(0, 360f));
                else EmitOne(sys.smoke, pos + VisualRandom.InsideUnitCircle * 0.6f, back + VisualRandom.InsideUnitCircle, VisualRandom.Range(0.4f, 0.8f), 0.6f, new Color(1, 1, 1, 0.35f));
            }
            else if (Has(tail, "Broom")) { if (Art.Sparkle.Ready) EmitS(Art.Sparkle, pos + VisualRandom.InsideUnitCircle * 0.3f, VisualRandom.InsideUnitCircle, VisualRandom.Range(0.3f, 0.5f), 0.8f, new Color(0.6f, 0.2f, 1f), VisualRandom.Range(0, 360f)); }
            else if (Has(tail, "Grenade"))
                EmitOne(sys.smoke, pos, VisualRandom.InsideUnitCircle * 0.2f, VisualRandom.Range(0.15f, 0.25f), 0.5f, new Color(0.85f, 0.85f, 0.85f, 0.45f));
            else if (Has(tail, "Acid"))
            {
                if (Art.Bubbles.Ready) EmitS(Art.Bubbles, pos, Vector2.up * 0.6f + VisualRandom.InsideUnitCircle * 0.3f, VisualRandom.Range(0.3f, 0.5f), 0.5f, new Color(0.88f, 1f, 0.2f));
                else EmitOne(sys.smoke, pos, Vector2.up * 0.6f + VisualRandom.InsideUnitCircle * 0.3f, VisualRandom.Range(0.2f, 0.35f), 0.5f, new Color(0.85f, 1f, 0.25f, 0.6f));
            }
            else if (Has(tail, "Poison"))
            {
                if (Art.Poison.Ready) EmitS(Art.Poison, pos + VisualRandom.InsideUnitCircle * 0.8f, VisualRandom.InsideUnitCircle * 0.4f, VisualRandom.Range(1.2f, 2f), 1.1f, new Color(1f, 1f, 1f, 0.6f), VisualRandom.Range(0, 360f));
                else EmitOne(sys.smoke, pos + VisualRandom.InsideUnitCircle * 0.8f, VisualRandom.InsideUnitCircle * 0.4f, VisualRandom.Range(1.2f, 2f), 1.1f, new Color(0.5f, 0.9f, 0.25f, 0.35f));
            }
            else if (Has(tail, "GreyGoo"))
                EmitOne(sys.smoke, pos, VisualRandom.InsideUnitCircle * 0.3f, VisualRandom.Range(0.25f, 0.4f), 0.6f, new Color(0.6f, 0.62f, 0.66f, 0.7f));
        }

        // ------------------------------------------------------------------ internals

        static bool Has(string s, string part) => s.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;

        static void Burst(ParticleSystem ps, Vector2 pos, int n, float vMin, float vMax, float lMin, float lMax, float sMin, float sMax, Color a, Color b)
        {
            for (int i = 0; i < n; i++)
            {
                var d = VisualRandom.OnUnitCircle;
                EmitOne(ps, pos, d * VisualRandom.Range(vMin, vMax), VisualRandom.Range(sMin, sMax), VisualRandom.Range(lMin, lMax), Color.Lerp(a, b, VisualRandom.Value));
            }
        }

        static void EmitOne(ParticleSystem ps, Vector2 pos, Vector2 vel, float size, float life, Color color, float rotation = 0)
        {
            if (!ps) return;
            var ep = new ParticleSystem.EmitParams
            {
                position = new Vector3(pos.x, pos.y, Z + VisualRandom.Range(-0.05f, 0.05f)),
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
            // procedural systems: sparks/flash always, the rest only when an original sprite family is missing
            // (phones: max particle counts are kept low, emitting into a full system just does nothing)
            sys = new Systems
            {
                // flames and fireballs: additive, grow then fade
                fire = Make(root.transform, "Fire", add, 500, -0.15f, 0.9f, Curve(0.6f, 1.2f, 0.2f), FadeGradient(Color.white, new Color(0.7f, 0.5f, 0.4f))),
                smoke = Make(root.transform, "Smoke", alpha, 300, -0.04f, 0.85f, Curve(0.5f, 1.4f, 1.8f), FadeGradient(Color.white, Color.white, 0.15f)),
                spark = Make(root.transform, "Sparks", add, 500, 1.2f, 0.98f, Curve(1f, 0.8f, 0.1f), FadeGradient(Color.white, Color.white)),
                debris = Make(root.transform, "Debris", square, 300, 2.2f, 1f, Curve(1f, 1f, 0.6f), FadeGradient(Color.white, Color.white, 0.75f)),
                water = Make(root.transform, "Water", alpha, 300, 1.8f, 1f, Curve(1f, 1f, 0.5f), FadeGradient(Color.white, Color.white, 0.6f)),
                flash = Make(root.transform, "Flash", add, 40, 0f, 1f, Curve(0.7f, 1.1f, 1.3f), FadeGradient(Color.white, Color.white)),
                bubble = Make(root.transform, "Bubbles", alpha, 200, -0.12f, 0.98f, Curve(0.5f, 1f, 1.2f), FadeGradient(Color.white, Color.white, 0.4f)),
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
            r.sortingOrder = Order;
            ps.Play();
            return ps;
        }

        /// <summary>Updates one-shot sprite animations, beams, floating texts and the camera shake timer.</summary>
        sealed class FxRunner : MonoBehaviour
        {
            sealed class Beam { public LineRenderer lr; public float t, dur, width; public Color color; }
            sealed class Text { public Transform root; public TextMesh main; public TextMesh[] outline; public float t, dur, size; public Color color; public Vector3 start; }
            sealed class Shot1
            {
                public Transform tr; public SpriteRenderer sr; public readonly SpriteAnimPlayer player = new SpriteAnimPlayer();
                public Sprite still; public float t, life, fadeFrom, grow, scale; public Color color; public Vector2 pos, vel; public bool live, additive;
            }

            readonly List<Beam> beams = new List<Beam>();
            readonly List<Text> texts = new List<Text>();
            readonly List<Shot1> shots = new List<Shot1>();
            Font font;
            Material additiveSprite, defaultSprite;
            const int MaxShots = 64;

            // ---- one-shot sprite animations (original explosion clips, tails, icons)

            public bool AddAnim(SpriteAnimSet set, Sprite still, Vector2 pos, float scale, Color color, bool additive, float life, float fadeFrom, float grow, float rotDeg, Vector2 vel, float speed)
            {
                if (set == null && still == null) return false;
                Shot1 s = null;
                int oldest = -1; float oldestT = -1f;
                for (int i = 0; i < shots.Count; i++)
                {
                    var x = shots[i];
                    if (!x.live) { s = x; break; }
                    float age = x.t / Mathf.Max(0.01f, x.life);
                    if (age > oldestT) { oldestT = age; oldest = i; }
                }
                if (s == null)
                {
                    if (shots.Count >= MaxShots) s = shots[oldest];     // phones: recycle the most finished one
                    else
                    {
                        var go = new GameObject("Fx.Anim");
                        go.transform.SetParent(transform, false);
                        s = new Shot1 { tr = go.transform, sr = go.AddComponent<SpriteRenderer>() };
                        s.sr.sortingOrder = Order;
                        if (!defaultSprite) defaultSprite = s.sr.sharedMaterial;
                        shots.Add(s);
                    }
                }
                if (s.additive != additive || s.sr.sharedMaterial == null)
                {
                    if (additive && !additiveSprite) additiveSprite = Mats.Additive(Color.white);
                    s.sr.sharedMaterial = additive ? additiveSprite : defaultSprite;
                    s.additive = additive;
                }
                s.still = still;
                if (set != null)
                {
                    s.player.Play(set, 0, -1, false, null);
                    s.player.Speed = speed;
                    s.sr.sprite = set.FrameAt(s.player.Frame);
                    if (life <= 0) life = set.Duration / Mathf.Max(0.05f, speed);
                }
                else
                {
                    s.player.Hold(null, 0);
                    s.sr.sprite = still;
                    if (life <= 0) life = 0.5f;
                }
                s.t = 0; s.life = Mathf.Max(0.05f, life); s.fadeFrom = Mathf.Clamp01(fadeFrom); s.grow = grow; s.scale = scale;
                s.color = color; s.pos = pos; s.vel = vel; s.live = true;
                s.tr.rotation = Quaternion.Euler(0, 0, rotDeg);
                s.tr.gameObject.SetActive(true);
                UpdateShot(s, 0f);
                return true;
            }

            void UpdateShot(Shot1 s, float dt)
            {
                s.t += dt;
                if (s.t >= s.life) { s.live = false; s.tr.gameObject.SetActive(false); return; }
                if (dt > 0 && s.player.Tick(dt)) s.sr.sprite = s.player.Set.FrameAt(s.player.Frame);
                float k = s.t / s.life;
                s.pos += s.vel * dt;
                s.tr.position = new Vector3(s.pos.x, s.pos.y, Z);
                s.tr.localScale = Vector3.one * Mathf.Max(0.01f, s.scale * (1f + s.grow * k));
                var c = s.color;
                if (k > s.fadeFrom && s.fadeFrom < 1f) c.a *= 1f - (k - s.fadeFrom) / (1f - s.fadeFrom);
                s.sr.color = c;
            }

            // ---- beams

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
                        lr.sortingOrder = Order;
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

            // ---- floating text (original floaters: white letters, thick coloured outline)

            static readonly Vector3[] outlineOffsets =
            {
                // 6 copies: enough for a thick rim at floater sizes while keeping TextMesh rebuilds low on phones
                new Vector3(0.05f, 0.05f, 0.01f), new Vector3(-0.05f, 0.05f, 0.01f), new Vector3(0.05f, -0.05f, 0.01f), new Vector3(-0.05f, -0.05f, 0.01f),
                new Vector3(0, 0.065f, 0.01f), new Vector3(0, -0.075f, 0.01f),
            };

            public void AddText(Vector2 pos, string s, Color c, float size)
            {
                Text tx = null;
                foreach (var x in texts) if (x.t >= x.dur) { tx = x; break; }
                if (tx == null)
                {
                    if (texts.Count >= 32) tx = texts[0];
                    else { tx = CreateText(); texts.Add(tx); }
                }
                tx.main.text = s;
                foreach (var o in tx.outline) o.text = s;
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
                TextMesh Make(string n, Vector3 local, int order)
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
                    mr.sortingOrder = order;
                    return tm;
                }
                // copies around the letters (slightly behind) make the outline, the last one doubles as drop shadow
                var outline = new TextMesh[outlineOffsets.Length];
                for (int i = 0; i < outline.Length; i++) outline[i] = Make("Outline", outlineOffsets[i], 100);
                var main = Make("Main", Vector3.zero, 101);
                return new Text { root = root, main = main, outline = outline };
            }

            void UpdateText(Text tx)
            {
                float k = tx.t / tx.dur;
                float rise = 1.6f * (1f - (1f - k) * (1f - k));
                tx.root.position = tx.start + new Vector3(0, rise, 0);
                float pop = k < 0.12f ? Mathf.Lerp(0.6f, 1.15f, k / 0.12f) : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((k - 0.12f) / 0.15f));
                tx.root.localScale = Vector3.one * tx.size * pop;
                float a = k < 0.65f ? 1f : 1f - (k - 0.65f) / 0.35f;
                var fill = Color.Lerp(Color.white, tx.color, 0.18f); fill.a = a;
                tx.main.color = fill;
                var edge = new Color(tx.color.r * 0.8f, tx.color.g * 0.8f, tx.color.b * 0.8f, a);
                for (int i = 0; i < tx.outline.Length; i++) tx.outline[i].color = edge;
            }

            public void ClearAll()
            {
                foreach (var b in beams) { b.t = b.dur; b.lr.enabled = false; }
                foreach (var t in texts) { t.t = t.dur; t.root.gameObject.SetActive(false); }
                foreach (var s in shots) { s.live = false; s.tr.gameObject.SetActive(false); }
            }

            void Update()
            {
                float dt = Time.deltaTime;
                for (int i = 0; i < shots.Count; i++) if (shots[i].live) UpdateShot(shots[i], dt);
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
