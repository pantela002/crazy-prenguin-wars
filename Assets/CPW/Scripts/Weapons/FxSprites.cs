using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Original Flash particle art for Fx (Resources/Original/fx/particles, fx/boosters, the theme's terrain
    /// particle_1..5). A SpriteFamily is one particle kind of particles.xml (flame_1..4, particle_cloud_1..5,
    /// plasma_cloud_*, water_1..4...): one world-space ParticleSystem per sprite (each original PNG is its own
    /// texture, and a texture-sheet ParticleSystem needs all its sprites on one texture), built on first use and
    /// emitted into with EmitParams like the procedural systems. An FxClip is an original animated symbol played
    /// once on a pooled SpriteRenderer (explosion disc, explosion clouds, teleport spinner, void generator...).
    /// Everything returns false / does nothing when the art is missing, so Fx keeps its procedural fallback.
    /// </summary>
    public static partial class Fx
    {
        /// <summary>A particle kind drawn with original sprites (random variant per particle).</summary>
        internal sealed class SpriteFamily
        {
            readonly string[] names;
            readonly string theme;
            readonly bool additive;
            readonly int max;
            readonly float gravity, keep, s0, s1, s2, hold, spin;
            readonly Color endTint;
            ParticleSystem[] systems;
            Vector2[] dims;
            int count;
            bool tried;

            public SpriteFamily(string[] names, bool additive, int max, float gravity, float keep, float s0, float s1, float s2, float hold, float spin,
                Color? endTint = null, string theme = null)
            {
                this.names = names; this.additive = additive; this.max = max; this.gravity = gravity; this.keep = keep;
                this.s0 = s0; this.s1 = s1; this.s2 = s2; this.hold = hold; this.spin = spin; this.theme = theme;
                this.endTint = endTint ?? Color.white;
            }

            /// <summary>True when at least one sprite exists (builds the systems on first call).</summary>
            public bool Ready
            {
                get
                {
                    if (!tried) Build();
                    return count > 0;
                }
            }

            public int Count => count;

            void Build()
            {
                if (!runner) return;          // Fx.Ensure has not run yet: try again later
                tried = true;
                systems = new ParticleSystem[names.Length];
                dims = new Vector2[names.Length];
                int perSystem = Mathf.Max(24, max / Mathf.Max(1, names.Length));
                foreach (var n in names)
                {
                    var s = theme != null ? CPW.OriginalArt.Landmass(theme, n) : OriginalArt.Fx(n)?.FrameAt(0);
                    if (s == null || s.texture == null) continue;
                    var mat = additive ? Mats.AdditiveTex(s.texture, Color.white) : Mats.TransparentTex(s.texture, Color.white);
                    var ps = Make(runner.transform, "S." + (theme != null ? theme + "." : "") + n, mat, perSystem, gravity, keep, Curve(s0, s1, s2), FadeGradient(Color.white, endTint, hold));
                    var main = ps.main;
                    main.startSize3D = true;
                    if (spin > 0)
                    {
                        var rot = ps.rotationOverLifetime;
                        rot.enabled = true;
                        rot.z = new ParticleSystem.MinMaxCurve(-spin, spin);
                    }
                    var r = s.textureRect;
                    if (r.width < s.texture.width - 0.5f || r.height < s.texture.height - 0.5f)
                    {
                        // packed into an atlas page (Sprite Atlas added later in the editor): sample just this sprite
                        var tsa = ps.textureSheetAnimation;
                        tsa.enabled = true;
                        tsa.mode = ParticleSystemAnimationMode.Sprites;
                        tsa.AddSprite(s);
                    }
                    systems[count] = ps;
                    dims[count] = new Vector2(Mathf.Max(1f, s.rect.width), Mathf.Max(1f, s.rect.height));
                    count++;
                }
            }

            /// <summary>Emit one particle; size = world size of the sprite's longer side.</summary>
            public void Emit(Vector2 pos, Vector2 vel, float size, float life, Color color, float rotation)
            {
                if (count == 0) return;
                int i = count == 1 ? 0 : VisualRandom.Range(0, count);
                var ps = systems[i];
                if (!ps) return;
                var d = dims[i];
                float m = Mathf.Max(d.x, d.y);
                var ep = new ParticleSystem.EmitParams
                {
                    position = new Vector3(pos.x, pos.y, Z + VisualRandom.Range(-0.05f, 0.05f)),
                    velocity = new Vector3(vel.x, vel.y, 0),
                    startSize3D = new Vector3(size * d.x / m, size * d.y / m, 1f),
                    startLifetime = life,
                    startColor = color,
                    rotation = rotation,
                    applyShapeToPosition = false
                };
                ps.Emit(ep, 1);
            }

            public void Clear()
            {
                for (int i = 0; i < count; i++) if (systems[i]) systems[i].Clear();
            }
        }

        /// <summary>An original animated symbol played once on the pooled SpriteRenderers.</summary>
        internal sealed class FxClip
        {
            readonly string fx, animationGraphic;
            SpriteAnimSet set;
            bool tried;

            public FxClip(string fx, string animationGraphic = null) { this.fx = fx; this.animationGraphic = animationGraphic; }

            public SpriteAnimSet Set
            {
                get
                {
                    if (!tried)
                    {
                        tried = true;
                        if (animationGraphic != null) set = OriginalArt.AnimationAnim(animationGraphic);
                        if (set == null) set = OriginalArt.Fx(fx);
                    }
                    return set;
                }
            }

            public bool Ready => Set != null;
        }

        /// <summary>The particle kinds and clips of the original particles.xml used by the battle.</summary>
        internal static class Art
        {
            static readonly Color Ember = new Color(1f, 0.6f, 0.45f);

            //                                  sprites                                                         add    max  grav   keep  size curve       hold  spin
            public static readonly SpriteFamily Flames = new SpriteFamily(new[] { "flame_1", "flame_2", "flame_3", "flame_4" }, true, 320, -0.15f, 0.9f, 0.7f, 1.1f, 0.3f, 0.3f, 1f, Ember);
            public static readonly SpriteFamily Clouds = new SpriteFamily(new[] { "particle_cloud_1", "particle_cloud_2", "particle_cloud_3", "particle_cloud_4", "particle_cloud_5" }, false, 400, -0.04f, 0.85f, 0.5f, 1.3f, 1.7f, 0.35f, 0.6f);
            public static readonly SpriteFamily PlasmaClouds = new SpriteFamily(new[] { "plasma_cloud_1", "plasma_cloud_2", "plasma_cloud_3", "plasma_cloud_4", "plasma_cloud_5" }, true, 240, 0f, 0.8f, 0.6f, 1.2f, 1.5f, 0.4f, 0.6f);
            public static readonly SpriteFamily Bubbles = new SpriteFamily(new[] { "plasma_bubble_01", "plasma_bubble_02", "plasma_bubble_03" }, true, 240, -0.1f, 0.95f, 1f, 1f, 0.25f, 0.5f, 0f);
            public static readonly SpriteFamily Poison = new SpriteFamily(new[] { "poison_cloud_1", "poison_cloud_2", "poison_cloud_3", "poison_cloud_4" }, false, 240, -0.05f, 0.85f, 0.5f, 1.2f, 1.6f, 0.4f, 0.5f);
            public static readonly SpriteFamily Water = new SpriteFamily(new[] { "water_1", "water_2", "water_3", "water_4" }, false, 240, 1.8f, 1f, 1f, 1f, 0.4f, 0.6f, 2f);
            public static readonly SpriteFamily Lava = new SpriteFamily(new[] { "lava_ball_1", "lava_ball_2", "lava_ball_3" }, false, 160, 1.8f, 1f, 1f, 1f, 0.4f, 0.6f, 2f);
            public static readonly SpriteFamily Mud = new SpriteFamily(new[] { "mud_bubble_1", "mud_bubble_2", "mud_bubble_3" }, false, 160, 1.8f, 1f, 1f, 1f, 0.6f, 0.6f, 1f);
            public static readonly SpriteFamily Confetti = new SpriteFamily(new[] { "confetti_small_1", "confetti_small_2", "confetti_small_3", "confetti_small_4", "confetti_small_5", "confetti_small_6" }, false, 300, 1.2f, 0.97f, 1f, 1f, 0.8f, 0.6f, 6f);
            public static readonly SpriteFamily Sparkle = new SpriteFamily(new[] { "teleport_sparkle_1", "star_1", "star_2" }, true, 200, 0.3f, 0.95f, 1f, 0.9f, 0.1f, 0.5f, 3f);
            public static readonly SpriteFamily Wind = new SpriteFamily(new[] { "wind_fx_1", "wind_fx_2", "wind_fx_5", "wind_fx_6" }, false, 120, 0f, 0.9f, 0.7f, 1.1f, 1.3f, 0.5f, 4f);
            public static readonly SpriteFamily Chocolate = new SpriteFamily(new[] { "chocolate_particle_1", "chocolate_particle_2", "chocolate_particle_3", "chocolate_particle_4", "chocolate_particle_5" }, false, 150, 2.2f, 1f, 1f, 1f, 0.7f, 0.7f, 6f);
            public static readonly SpriteFamily CatHair = new SpriteFamily(new[] { "cat_hair_1", "cat_hair_2", "cat_hair_3", "cat_hair_4", "cat_claw_1", "cat_claw_2" }, false, 120, 1.5f, 0.97f, 1f, 1f, 0.5f, 0.6f, 4f);
            public static readonly SpriteFamily Acid = new SpriteFamily(new[] { "acid_spit_1", "acid_spit_2", "acid_spit_4", "acid_spit_5", "acid_spit_6", "acid_spit_7" }, false, 150, 1.5f, 1f, 1f, 1f, 0.5f, 0.6f, 0f);
            public static readonly SpriteFamily Laser = new SpriteFamily(new[] { "laser_beam_1" }, true, 160, 0f, 1f, 1f, 1f, 0.6f, 0.3f, 0f);

            public static readonly FxClip ExplosionDisc = new FxClip("particle_explosion");
            public static readonly FxClip CloudBlue = new FxClip("explosion_cloud");
            public static readonly FxClip CloudGrey = new FxClip("explosion_cloud_grey");
            public static readonly FxClip Spinner = new FxClip("teleport_spinner");
            public static readonly FxClip VoidBlast = new FxClip("void_generator_explosion", "VoidGenerator");
            public static readonly FxClip LaserHit = new FxClip("laser_hit");
            public static readonly FxClip Skull = new FxClip("poison_gas_skull");
            public static readonly FxClip CatSilhouette = new FxClip("cat_silhouette_1");
            public static readonly FxClip Trapezoid = new FxClip("trapezoid_tail");
            public static readonly FxClip TrapezoidSmall = new FxClip("trapezoid_tail_smaller");
            public static readonly FxClip BoosterStart = new FxClip("booster_start");
            public static readonly FxClip Shimmer = new FxClip("shimmer");

            static readonly string[] debrisNames = { "particle_1", "particle_2", "particle_3", "particle_4", "particle_5" };
            static readonly System.Collections.Generic.Dictionary<string, SpriteFamily> debris = new System.Collections.Generic.Dictionary<string, SpriteFamily>();

            /// <summary>Ground chunks of the current level's theme (null outside a battle).</summary>
            public static SpriteFamily ThemeDebris()
            {
                var lvl = BattleTerrain.I != null ? BattleTerrain.I.Level : null;
                string theme = lvl != null ? lvl.theme : null;
                if (string.IsNullOrEmpty(theme)) return null;
                if (!debris.TryGetValue(theme, out var f))
                {
                    f = new SpriteFamily(debrisNames, false, 200, 2.2f, 1f, 1f, 1f, 0.6f, 0.75f, 8f, null, theme);
                    debris[theme] = f;
                }
                return f;
            }

            static SpriteFamily[] all;

            public static void ClearAll()
            {
                if (all == null) all = new[] { Flames, Clouds, PlasmaClouds, Bubbles, Poison, Water, Lava, Mud, Confetti, Sparkle, Wind, Chocolate, CatHair, Acid, Laser };
                foreach (var f in all) f.Clear();
                foreach (var kv in debris) kv.Value.Clear();
            }
        }

        static void EmitS(SpriteFamily f, Vector2 pos, Vector2 vel, float size, float life, Color color, float rotation = 0f)
        {
            if (f != null && f.Ready) f.Emit(pos, vel, size, life, color, rotation);
        }

        /// <summary>Play an original clip once at pos (scale 1 = Flash size). life &lt;= 0: the clip's length; longer
        /// lives hold the last frame. Alpha fades from fadeFrom (0..1 of life), the scale grows by grow over life.
        /// False when the clip is missing.</summary>
        static bool Anim(FxClip clip, Vector2 pos, float scale, Color color, bool additive = false, float life = 0f, float fadeFrom = 1f,
            float grow = 0f, float rotDeg = 0f, Vector2 vel = default, float speed = 1f)
        {
            if (clip == null || !runner) return false;
            var set = clip.Set;
            return set != null && runner.AddAnim(set, null, pos, scale, color, additive, life, fadeFrom, grow, rotDeg, vel, speed);
        }

        // ------------------------------------------------------------------ helpers for boosters, crates, statuses

        /// <summary>Original booster activation rings (fx/boosters booster_start) around a penguin.</summary>
        internal static void BoosterStart(Vector2 pos)
        {
            if (!Ensure()) return;
            if (!Anim(Art.BoosterStart, pos, 0.45f, Color.white, false, 0f, 0.7f))
                Sparks(pos, new Color(1f, 0.9f, 0.5f), 12);
        }

        /// <summary>One twinkle of the original booster shimmer (shield bubble, power-ups).</summary>
        internal static void Shimmer(Vector2 pos, float scale = 0.8f)
        {
            if (!Ensure()) return;
            Anim(Art.Shimmer, pos, scale, Color.white, true, 0.5f, 0.3f, 0.2f, VisualRandom.Range(0f, 360f));
        }

        /// <summary>A sprite (crate icon, drop icon) that pops up from pos and fades.</summary>
        internal static void IconPop(Sprite icon, Vector2 pos, float scale)
        {
            if (!Ensure() || icon == null) return;
            runner.AddAnim(null, icon, pos, scale, Color.white, false, 0.9f, 0.45f, 0.3f, 0f, new Vector2(0f, 2f), 1f);
        }

        /// <summary>Status effect puffs on a penguin: StatusEffectPoison/Acid/PowerUp of particles.xml.</summary>
        internal static void Status(string statusId, Vector2 pos)
        {
            if (!Ensure()) return;
            switch (statusId)
            {
                case "Fire": Fire(pos + Vector2.up * 0.2f, 0.45f); break;
                case "Poison":
                    if (Art.Poison.Ready) EmitS(Art.Poison, pos + Vector2.up * 0.8f + VisualRandom.InsideUnitCircle * 0.4f, Vector2.up * 0.5f, VisualRandom.Range(0.5f, 0.8f), 1f, new Color(1f, 1f, 1f, 0.8f), VisualRandom.Range(0, 360f));
                    else Bubbles(pos + Vector2.up * 0.8f, new Color(0.45f, 0.95f, 0.25f, 0.9f), 2);
                    break;
                case "Acid":
                    if (Art.Acid.Ready) EmitS(Art.Acid, pos + Vector2.up * 0.5f + VisualRandom.InsideUnitCircle * 0.4f, new Vector2(VisualRandom.Range(-1f, 1f), 1.5f), VisualRandom.Range(0.4f, 0.6f), 0.8f, Color.white);
                    else Bubbles(pos + Vector2.up * 0.5f, new Color(0.8f, 1f, 0.2f, 0.9f), 2);
                    break;
                case "SlowGoo": Bubbles(pos, new Color(0.3f, 0.85f, 0.2f, 0.8f), 1); break;
                case "Ice": Sparks(pos + VisualRandom.InsideUnitCircle * 0.6f, new Color(0.7f, 0.9f, 1f), 2); break;
                case "Regeneration":
                    if (VisualRandom.Value < 0.3f && !Stars(pos + Vector2.up, 1, new Color(0.5f, 1f, 0.5f))) Sparks(pos + Vector2.up, new Color(0.5f, 1f, 0.5f), 2);
                    break;
            }
        }
    }
}
