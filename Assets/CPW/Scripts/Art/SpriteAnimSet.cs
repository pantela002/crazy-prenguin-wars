using System;
using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// One original Flash symbol as sprites (built by OriginalArt.Anim from Resources/Original/**/_meta.json):
    /// unique frames, the timeline (frame index per Flash frame, 24 fps), frame labels ("draw", "aim", "fire", "out",
    /// "in", "loop"...), the canvas and the named children of frame 1 (UI layout, attachment points).
    /// Every sprite carries its own pivot (= the Flash registration point) and pixelsPerUnit = 20 x zoom, so frames
    /// line up when shown on the same SpriteRenderer and 1 Unity unit = 20 Flash px (Units.PX).
    /// </summary>
    public sealed class SpriteAnimSet
    {
        public const float DefaultFps = 24f;

        /// <summary>Path under Resources/Original ("weapons/weapon_animations/bazooka").</summary>
        public readonly string Path;
        /// <summary>Unique frames; an entry is null if its PNG failed to load.</summary>
        public readonly Sprite[] Frames;
        /// <summary>Index into Frames for every timeline frame (Length = Flash frame count).</summary>
        public readonly int[] Sequence;
        /// <summary>Label -> 0-based timeline frame (case-insensitive).</summary>
        public readonly Dictionary<string, int> Labels;
        /// <summary>Pivot of each unique frame in texture pixels from the top-left, y down (as in _meta.json).</summary>
        public readonly Vector2[] PivotsPx;
        /// <summary>Render zoom: texture pixels per Flash pixel (sprite PPU = 20 x Zoom).</summary>
        public readonly float Zoom;
        /// <summary>Full render canvas in texture pixels and the registration point inside it (y down).</summary>
        public readonly Vector2 CanvasPx, OriginPx;
        public float Fps = DefaultFps;

        readonly Dictionary<string, float[]> children;
        readonly string[] labelAt;      // label starting at each timeline frame (first one if several), or null

        public SpriteAnimSet(string path, Sprite[] frames, int[] sequence, Dictionary<string, int> labels, Vector2[] pivotsPx,
            float zoom, Vector2 canvasPx, Vector2 originPx, Dictionary<string, float[]> children)
        {
            Path = path;
            Frames = frames ?? new Sprite[0];
            if (sequence == null || sequence.Length == 0)
            {
                sequence = new int[Math.Max(1, Frames.Length)];
                for (int i = 0; i < sequence.Length; i++) sequence[i] = Math.Min(i, Math.Max(0, Frames.Length - 1));
            }
            Sequence = sequence;
            Labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (labels != null) foreach (var kv in labels) Labels[kv.Key] = Mathf.Clamp(kv.Value, 0, Sequence.Length - 1);
            PivotsPx = pivotsPx ?? new Vector2[Frames.Length];
            Zoom = zoom > 0 ? zoom : 1f;
            CanvasPx = canvasPx;
            OriginPx = originPx;
            this.children = children ?? new Dictionary<string, float[]>();
            labelAt = new string[Sequence.Length];
            foreach (var kv in Labels)
                if (labelAt[kv.Value] == null) labelAt[kv.Value] = kv.Key;
        }

        /// <summary>Single-sprite set (embedded bitmaps, one-frame symbols).</summary>
        public static SpriteAnimSet Single(string path, Sprite s, float zoom = 1f)
        {
            var px = s != null ? new Vector2(s.pivot.x, s.rect.height - s.pivot.y) : Vector2.zero;
            var size = s != null ? s.rect.size : Vector2.zero;
            return new SpriteAnimSet(path, new[] { s }, new[] { 0 }, null, new[] { px }, zoom, size, px, null);
        }

        /// <summary>Number of timeline frames.</summary>
        public int Length => Sequence.Length;
        public float Duration => Length / Mathf.Max(1f, Fps);
        public bool IsAnimated => Frames.Length > 1;

        /// <summary>Sprite shown at a timeline frame (clamped).</summary>
        public Sprite FrameAt(int frame)
        {
            if (Sequence.Length == 0) return null;
            int i = Sequence[Mathf.Clamp(frame, 0, Sequence.Length - 1)];
            return i >= 0 && i < Frames.Length ? Frames[i] : null;
        }

        /// <summary>First sprite (or the one at a label when given, e.g. "aim" for the static held weapon pose).</summary>
        public Sprite Still(string label = null) => FrameAt(label != null ? Label(label, 0) : 0);

        public bool HasLabel(string label) => label != null && Labels.ContainsKey(label);

        /// <summary>0-based timeline frame of a label, or def.</summary>
        public int Label(string label, int def = -1) => label != null && Labels.TryGetValue(label, out var f) ? f : def;

        /// <summary>The label that starts at this timeline frame, or null.</summary>
        public string LabelAt(int frame) => frame >= 0 && frame < labelAt.Length ? labelAt[frame] : null;

        /// <summary>
        /// Frames of a labelled section: from the label up to the frame before the next label (or the last frame).
        /// Flash timelines use this layout: walk = "in" 0..2, "loop" 3..22, "out" 23..end.
        /// </summary>
        public bool Segment(string label, out int start, out int end)
        {
            start = Label(label);
            end = Length - 1;
            if (start < 0) { start = 0; return false; }
            for (int f = start + 1; f < Length; f++)
                if (labelAt[f] != null) { end = f - 1; break; }
            return true;
        }

        /// <summary>
        /// Position of a named child instance of frame 1 relative to the registration point, in Unity units
        /// (Flash px / 20, y up). Child matrices are Flash px at zoom 1. Returns false if the child is unknown.
        /// For uGUI multiply by the canvas scale you use for Flash px.
        /// </summary>
        public bool ChildOffset(string name, out Vector2 offset)
        {
            offset = Vector2.zero;
            if (name == null || !children.TryGetValue(name, out var m) || m == null || m.Length < 6) return false;
            offset = new Vector2(m[4] / Units.PX, -m[5] / Units.PX);
            return true;
        }

        /// <summary>Raw Flash matrix [a, b, c, d, tx, ty] of a child of frame 1 (Flash px, y down), or null.</summary>
        public float[] ChildMatrix(string name) => name != null && children.TryGetValue(name, out var m) ? m : null;

        public IEnumerable<string> ChildNames => children.Keys;

        /// <summary>Canvas size in Unity units.</summary>
        public Vector2 SizeUnits => CanvasPx / (Units.PX * Zoom);
    }
}
