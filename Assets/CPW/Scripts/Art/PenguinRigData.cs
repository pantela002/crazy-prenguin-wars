using System;
using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Slot tracks of the original penguin animations, ready for Transforms, plus the clothes sprites rendered from the
    /// Blender models. Data: Resources/Original/characters/penguin_rig.json (the original slot matrices per frame) with
    /// the tracks the rebuilt SWF lost filled in by Resources/Original/clothes/rig_fill.json
    /// (Blender/scripts/penguin_rig_fill.py), and Resources/Original/clothes/_meta.json + PNGs
    /// (Blender/scripts/clothes_sprites.py). Everything is parsed once and cached; lookups return null when missing.
    ///
    /// Clothes sprites are drawn in the rest pose (damagehit_small_weapon frame 0) with their pivot at the slot origin,
    /// so a clothing slot's frame transform is slot(frame) * inverse(slot(rest)) placed at the slot origin; the tool
    /// slot (held weapon) uses the raw slot matrix like the original's addChild into the slot.
    /// </summary>
    public static class PenguinRigData
    {
        public enum Slot { Head = 0, Body = 1, FootL = 2, FootR = 3, Tool = 4 }
        public const int SlotCount = 5;
        /// <summary>Slot instance names in the original animations (Equippable.getAnimationClipName).</summary>
        public static readonly string[] SlotNames = { "head_gear", "body_gear", "left_foot_gear", "right_foot_gear", "tool" };
        public const string RestAnim = "damagehit_small_weapon";
        public const string ClothesFolder = "clothes";

        /// <summary>One slot on one frame, in the animation's local space (Unity units, y up, scale 1 = Flash size).</summary>
        public struct Frame
        {
            public bool on;
            public Vector3 pos;
            public float angle;        // degrees, counter-clockwise: the slot node's rotation (after its scale)
            public Vector3 scale;      // the slot node's scale; y can be negative (mirrored slot)
            public float angle2;       // degrees: rotation of the art inside the slot node (before the scale; shear)
            public int order;          // draw order (rig z, higher = in front)
        }

        public sealed class Anim
        {
            /// <summary>[slot][timeline frame]; a slot without any track is null.</summary>
            public readonly Frame[][] Slots = new Frame[SlotCount][];
            public bool Has(Slot s) => Slots[(int)s] != null;
        }

        static readonly Dictionary<string, Anim> anims = new Dictionary<string, Anim>();
        static Dictionary<string, object> rig, fill;
        static readonly float[][] rest = new float[SlotCount][];
        static bool loaded;

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            rig = Dict(OriginalArt.PenguinRig(), "animations");
            var ta = Resources.Load<TextAsset>(OriginalArt.Root + ClothesFolder + "/rig_fill");
            fill = ta != null ? Dict(MiniJson.ParseObject(ta.text), "animations") : null;
            var r = Dict(rig, RestAnim);
            var rs = Dict(r, "slots");
            for (int i = 0; i < SlotCount; i++)
            {
                var track = rs != null && rs.TryGetValue(SlotNames[i], out var t) ? t as List<object> : null;
                rest[i] = track != null && track.Count > 0 ? Floats(track[0]) : null;
            }
        }

        /// <summary>Slot tracks of a penguin animation ("walk_small_weapon"), or null when the rig has no such animation.</summary>
        public static Anim Get(string anim)
        {
            if (string.IsNullOrEmpty(anim)) return null;
            if (anims.TryGetValue(anim, out var a)) return a;
            Load();
            a = null;
            var ra = Dict(rig, anim);
            if (ra != null)
            {
                a = new Anim();
                var rslots = Dict(ra, "slots");
                var fslots = Dict(Dict(fill, anim), null);
                for (int i = 0; i < SlotCount; i++)
                {
                    // a filled track wins: it is only there when the rig's own track is missing or has gaps
                    List<object> track = null;
                    if (fslots != null && fslots.TryGetValue(SlotNames[i], out var ft)) track = ft as List<object>;
                    if (track == null && rslots != null && rslots.TryGetValue(SlotNames[i], out var rt)) track = rt as List<object>;
                    if (track == null) continue;
                    var frames = new Frame[track.Count];
                    var inv = i != (int)Slot.Tool ? Inverse(rest[i]) : null;
                    if (i != (int)Slot.Tool && inv == null) continue;    // no rest pose: clothes cannot be placed
                    for (int f = 0; f < frames.Length; f++)
                    {
                        var m = Floats(track[f]);
                        if (m == null || m.Length < 7) continue;
                        float a0 = m[0], b0 = m[1], c0 = m[2], d0 = m[3];
                        if (inv != null)
                        {
                            // linear part of slot(frame) * inverse(slot(rest))
                            a0 = m[0] * inv[0] + m[2] * inv[1];
                            b0 = m[1] * inv[0] + m[3] * inv[1];
                            c0 = m[0] * inv[2] + m[2] * inv[3];
                            d0 = m[1] * inv[2] + m[3] * inv[3];
                        }
                        frames[f] = ToFrame(a0, b0, c0, d0, m[4], m[5], m[6]);
                    }
                    a.Slots[i] = frames;
                }
            }
            anims[anim] = a;
            return a;
        }

        /// <summary>
        /// Flash matrix (x' = a x + c y + tx, y' = b x + d y + ty; Flash px, y down) -> Unity local transform (units, y up).
        /// The Unity linear part is M = [[a, -c], [-b, d]], split by a 2x2 SVD into R(angle) * Scale * R(angle2) so squash
        /// frames (which shear the relative clothing matrix) stay exact with two nested Transforms.
        /// </summary>
        static Frame ToFrame(float a, float b, float c, float d, float tx, float ty, float z)
        {
            float m00 = a, m01 = -c, m10 = -b, m11 = d;
            float e = (m00 + m11) * 0.5f, f = (m00 - m11) * 0.5f, g = (m10 + m01) * 0.5f, h = (m10 - m01) * 0.5f;
            float q = Mathf.Sqrt(e * e + h * h), r = Mathf.Sqrt(f * f + g * g);
            float a1 = Mathf.Atan2(g, f), a2 = Mathf.Atan2(h, e);
            return new Frame
            {
                on = true,
                pos = new Vector3(tx / Units.PX, -ty / Units.PX, 0f),
                angle = (a2 + a1) * 0.5f * Mathf.Rad2Deg,
                scale = new Vector3(q + r, q - r, 1f),
                angle2 = (a2 - a1) * 0.5f * Mathf.Rad2Deg,
                order = Mathf.RoundToInt(z),
            };
        }

        static float[] Inverse(float[] m)
        {
            if (m == null || m.Length < 4) return null;
            float det = m[0] * m[3] - m[1] * m[2];
            if (Mathf.Abs(det) < 1e-9f) return null;
            return new[] { m[3] / det, -m[1] / det, -m[2] / det, m[0] / det };
        }

        // ------------------------------------------------------------------ clothes sprites

        sealed class ClothesItem
        {
            public readonly Sprite[] parts = new Sprite[SlotCount];
        }

        static Dictionary<string, object> clothesFiles, clothesItems;
        static float clothesZoom = 2.5f;
        static readonly Dictionary<string, ClothesItem> clothes = new Dictionary<string, ClothesItem>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Sprite of a clothing item (Bonus id) for one rig slot (Head, Body, FootL, FootR), pivot at the slot origin
        /// and pixelsPerUnit = 20 x zoom; null when the item has no rendered sprite for that slot (it is then not drawn).
        /// </summary>
        public static Sprite ClothesSprite(string bonusId, Slot slot)
        {
            if (string.IsNullOrEmpty(bonusId) || slot == Slot.Tool) return null;
            if (!clothes.TryGetValue(bonusId, out var item))
            {
                item = LoadItem(bonusId);
                clothes[bonusId] = item;
            }
            return item?.parts[(int)slot];
        }

        static ClothesItem LoadItem(string id)
        {
            if (clothesFiles == null)
            {
                var ta = Resources.Load<TextAsset>(OriginalArt.Root + ClothesFolder + "/_meta");
                var m = ta != null ? MiniJson.ParseObject(ta.text) : null;
                clothesFiles = Dict(m, "files") ?? new Dictionary<string, object>();
                clothesItems = Dict(m, "items") ?? new Dictionary<string, object>();
                if (m != null && m.TryGetValue("zoom", out var z)) clothesZoom = ToF(z, 2.5f);
            }
            var info = Dict(clothesItems, id);
            var parts = Dict(info, "parts");
            if (parts == null) return null;
            var item = new ClothesItem();
            bool any = false;
            for (int i = 0; i < SlotCount - 1; i++)
                if (parts.TryGetValue(SlotNames[i], out var f) && f is string file)
                    any |= (item.parts[i] = LoadClothesFile(file)) != null;
            return any ? item : null;
        }

        static Sprite LoadClothesFile(string file)
        {
            string path = OriginalArt.Root + ClothesFolder + "/" + file;
            var p = clothesFiles.TryGetValue(file, out var pv) ? pv as List<object> : null;
            float zoom = p != null && p.Count >= 3 ? ToF(p[2], clothesZoom) : clothesZoom;
            var s = Resources.Load<Sprite>(path);
            if (p == null || p.Count < 2) return s;
            var piv = new Vector2(ToF(p[0], 0), ToF(p[1], 0));     // px from the top-left, y down
            if (s == null)
            {
                // imported as a plain texture (importer not run): build the sprite from the metadata
                var t = Resources.Load<Texture2D>(path);
                if (t == null) return null;
                s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(piv.x / t.width, 1f - piv.y / t.height),
                    Units.PX * zoom, 0, SpriteMeshType.FullRect);
                s.name = file;
                return s;
            }
            var r = s.rect;
            var want = new Vector2(piv.x, r.height - piv.y);
            if ((s.pivot - want).sqrMagnitude > 1f || Mathf.Abs(s.pixelsPerUnit - Units.PX * zoom) > 0.01f)
            {
                s = Sprite.Create(s.texture, r, new Vector2(want.x / r.width, want.y / r.height), Units.PX * zoom, 0, SpriteMeshType.FullRect);
                s.name = file;
            }
            return s;
        }

        // ------------------------------------------------------------------ json helpers

        static Dictionary<string, object> Dict(Dictionary<string, object> d, string key)
        {
            if (d == null) return null;
            if (key == null) return d;
            return d.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;
        }

        static float[] Floats(object o)
        {
            if (!(o is List<object> l)) return null;
            var r = new float[l.Count];
            for (int i = 0; i < r.Length; i++) r[i] = ToF(l[i], 0f);
            return r;
        }

        static float ToF(object o, float def)
        {
            switch (o)
            {
                case double d: return (float)d;
                case long l: return l;
                case int i: return i;
                case float f: return f;
                default: return def;
            }
        }
    }
}
