using System;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// Playhead over a SpriteAnimSet timeline, shared by SpriteAnim (SpriteRenderer) and UISpriteAnim (uGUI Image).
    /// Plays a frame range [From, To] at Set.Fps x Speed, looping or once; raises Label for every labelled frame the
    /// playhead enters (the first frame included, none skipped on a slow frame), and runs onDone once after the last
    /// frame of a non-looping range has been shown for its full frame time. Holds the last frame afterwards.
    /// </summary>
    public sealed class SpriteAnimPlayer
    {
        public SpriteAnimSet Set { get; private set; }
        /// <summary>Timeline frame shown now.</summary>
        public int Frame { get; private set; }
        public int From { get; private set; }
        public int To { get; private set; }
        public bool Loop { get; private set; }
        public bool Playing { get; private set; }
        /// <summary>Playback rate (1 = the original 24 fps, 0 = frozen).</summary>
        public float Speed = 1f;
        /// <summary>Raised with the label name when the playhead enters a labelled frame ("fire", "out", "loop"...).</summary>
        public event Action<string> Label;

        float time;            // frames since From (fractional)
        int pos;               // whole frames since From of the shown frame (unwrapped for loops)
        int gen;               // bumps on every Play/Hold so a Label listener can restart safely
        Action done;

        /// <summary>Play timeline frames from..to (to &lt; 0 = last frame). onDone runs once when a non-looping range ends.</summary>
        public void Play(SpriteAnimSet set, int from, int to, bool loop, Action onDone = null)
        {
            gen++;
            Set = set;
            done = onDone;
            time = 0f;
            pos = 0;
            if (set == null) { Playing = false; Frame = From = To = 0; return; }
            From = Mathf.Clamp(from, 0, set.Length - 1);
            To = to < 0 ? set.Length - 1 : Mathf.Clamp(to, From, set.Length - 1);
            Loop = loop;
            Playing = true;
            Frame = From;
            FireLabel(Frame);
        }

        /// <summary>Show one frame and stop (Flash gotoAndStop).</summary>
        public void Hold(SpriteAnimSet set, int frame)
        {
            gen++;
            Set = set;
            done = null;
            Playing = false;
            Frame = From = To = set != null ? Mathf.Clamp(frame, 0, set.Length - 1) : 0;
        }

        /// <summary>Freeze on the current frame (onDone is dropped).</summary>
        public void Stop()
        {
            Playing = false;
            done = null;
        }

        /// <summary>Advance by dt seconds. Returns true when the shown frame changed.</summary>
        public bool Tick(float dt)
        {
            if (!Playing || Set == null) return false;
            int len = To - From + 1;
            time += dt * Mathf.Max(0f, Speed) * Set.Fps;
            int step = (int)time;
            if (Loop && len == 1) { pos = 0; time = 0f; return false; }   // a held one-frame loop: no label spam
            if (!Loop && step > len - 1) step = len - 1;
            bool changed = false;
            if (step != pos)
            {
                int g = gen;
                for (int s = Mathf.Max(pos + 1, step - len + 1); s <= step; s++)
                {
                    Frame = From + s % len;
                    changed = true;
                    FireLabel(Frame);
                    if (g != gen || !Playing) return true;      // a listener played or stopped something
                }
                pos = step;
                if (Loop && pos >= len * 4096) { pos -= len * 4096; time -= len * 4096; }   // keep floats exact
            }
            if (!Loop && time >= len) Finish();
            return changed;
        }

        void FireLabel(int f)
        {
            var l = Set != null ? Set.LabelAt(f) : null;
            if (l != null) Label?.Invoke(l);
        }

        void Finish()
        {
            Playing = false;
            var d = done;
            done = null;
            d?.Invoke();
        }
    }

    /// <summary>
    /// Plays an original Flash symbol (SpriteAnimSet) on a SpriteRenderer at 24 fps. One component per layer
    /// (penguin body, held weapon, projectile, explosion...). The sprites carry the Flash registration point as pivot,
    /// so the transform position is the symbol's origin; mirror with Renderer.flipX or a negative x scale.
    /// <code>
    /// var w = SpriteAnim.Create(hand, OriginalArt.WeaponAnim("BasicNuke"), "Weapon", 5, loop: false);
    /// w.Play("draw", onDone: () => w.Hold("aim"));              // draw, then hold the aim pose
    /// w.Label += l => { if (l == "fire") SpawnMissile(); };
    /// w.PlayFrom("fire", onDone: () => w.Hold("aim"));          // fire .. end of timeline
    /// </code>
    /// </summary>
    public class SpriteAnim : MonoBehaviour
    {
        public readonly SpriteAnimPlayer Player = new SpriteAnimPlayer();
        /// <summary>Advance with Time.unscaledDeltaTime (menus, pause screens) instead of Time.deltaTime.</summary>
        public bool UnscaledTime;

        SpriteRenderer sr;

        public SpriteRenderer Renderer
        {
            get
            {
                // no ?? on Unity objects: the editor's GetComponent returns a fake null
                if (sr == null && !TryGetComponent(out sr)) sr = gameObject.AddComponent<SpriteRenderer>();
                return sr;
            }
        }
        public SpriteAnimSet Set => Player.Set;
        public int Frame => Player.Frame;
        public bool Playing => Player.Playing;
        public float Speed { get => Player.Speed; set => Player.Speed = value; }
        public event Action<string> Label { add => Player.Label += value; remove => Player.Label -= value; }

        /// <summary>New child GameObject with a SpriteRenderer playing set from frame 0 (or holding frame 0 if set is
        /// a still). Returns the component even when set is null (it then shows nothing), so callers can Play later.</summary>
        public static SpriteAnim Create(Transform parent, SpriteAnimSet set, string name = null, int sortingOrder = 0, bool loop = true)
        {
            var go = new GameObject(name ?? (set != null ? set.Path : "SpriteAnim"));
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<SpriteAnim>();
            a.Renderer.sortingOrder = sortingOrder;
            if (set != null && set.Length > 1) a.Play(set, loop);
            else a.Hold(set, 0);
            return a;
        }

        /// <summary>Whole timeline.</summary>
        public SpriteAnim Play(SpriteAnimSet set, bool loop = true, Action onDone = null) => PlayRange(set, 0, -1, loop, onDone);

        /// <summary>Labelled section of set (label .. frame before the next label); the whole timeline if the label is missing.</summary>
        public SpriteAnim Play(SpriteAnimSet set, string label, bool loop = false, Action onDone = null)
        {
            if (set != null && set.Segment(label, out int a, out int b)) return PlayRange(set, a, b, loop, onDone);
            return PlayRange(set, 0, -1, loop, onDone);
        }

        /// <summary>Labelled section of the current set.</summary>
        public SpriteAnim Play(string label, bool loop = false, Action onDone = null) => Play(Set, label, loop, onDone);

        /// <summary>From a label to the end of the timeline (Flash gotoAndPlay without the wrap).</summary>
        public SpriteAnim PlayFrom(string label, bool loop = false, Action onDone = null) => PlayRange(Set, Set != null ? Set.Label(label, 0) : 0, -1, loop, onDone);

        /// <summary>Whole timeline once, then onDone (and hold the last frame).</summary>
        public SpriteAnim PlayOnce(SpriteAnimSet set, Action onDone = null) => PlayRange(set, 0, -1, false, onDone);

        public SpriteAnim PlayRange(SpriteAnimSet set, int from, int to, bool loop, Action onDone = null)
        {
            Player.Play(set, from, to, loop, onDone);
            Apply();
            return this;
        }

        /// <summary>Show the frame at a label (e.g. the weapon "aim" pose) and stop.</summary>
        public void Hold(string label) => Hold(Set, Set != null ? Set.Label(label, 0) : 0);

        public void Hold(SpriteAnimSet set, int frame)
        {
            Player.Hold(set, frame);
            Apply();
        }

        public void Stop() => Player.Stop();

        void Update()
        {
            if (Player.Tick(UnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime)) Apply();
        }

        void Apply()
        {
            Renderer.sprite = Set != null ? Set.FrameAt(Player.Frame) : null;
        }
    }

    /// <summary>
    /// SpriteAnim for uGUI: plays a SpriteAnimSet on an Image (unscaled time by default). With AlignPivot (default)
    /// the RectTransform is resized to each frame and its pivot set to the frame's registration point, so
    /// anchoredPosition is the Flash symbol origin and trimmed frames do not jitter; FlashPx = canvas units per
    /// Flash pixel (StageScale fits the 668 px tall Flash stage to the 1080 high reference canvas).
    /// Turn AlignPivot off for frames that should stretch to a layout rect (buttons, panels).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class UISpriteAnim : MonoBehaviour
    {
        public const float StageScale = 1080f / 668f;

        public readonly SpriteAnimPlayer Player = new SpriteAnimPlayer();
        public bool UnscaledTime = true;
        public bool AlignPivot = true;
        public float FlashPx = StageScale;

        Image img;

        public Image Image
        {
            get
            {
                if (img == null && !TryGetComponent(out img))
                {
                    img = gameObject.AddComponent<Image>();
                    img.raycastTarget = false;
                }
                return img;
            }
        }
        public SpriteAnimSet Set => Player.Set;
        public int Frame => Player.Frame;
        public bool Playing => Player.Playing;
        public float Speed { get => Player.Speed; set => Player.Speed = value; }
        public event Action<string> Label { add => Player.Label += value; remove => Player.Label -= value; }

        /// <summary>New child Image (anchored at the parent's centre) playing set; see SpriteAnim.Create.</summary>
        public static UISpriteAnim Create(Transform parent, SpriteAnimSet set, string name = null, bool loop = true)
        {
            var go = new GameObject(name ?? (set != null ? set.Path : "UISpriteAnim"), typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            var a = go.AddComponent<UISpriteAnim>();
            if (set != null && set.Length > 1) a.Play(set, loop);
            else a.Hold(set, 0);
            return a;
        }

        public UISpriteAnim Play(SpriteAnimSet set, bool loop = true, Action onDone = null) => PlayRange(set, 0, -1, loop, onDone);

        public UISpriteAnim Play(SpriteAnimSet set, string label, bool loop = false, Action onDone = null)
        {
            if (set != null && set.Segment(label, out int a, out int b)) return PlayRange(set, a, b, loop, onDone);
            return PlayRange(set, 0, -1, loop, onDone);
        }

        public UISpriteAnim Play(string label, bool loop = false, Action onDone = null) => Play(Set, label, loop, onDone);

        public UISpriteAnim PlayFrom(string label, bool loop = false, Action onDone = null) => PlayRange(Set, Set != null ? Set.Label(label, 0) : 0, -1, loop, onDone);

        public UISpriteAnim PlayRange(SpriteAnimSet set, int from, int to, bool loop, Action onDone = null)
        {
            Player.Play(set, from, to, loop, onDone);
            Apply();
            return this;
        }

        public void Hold(string label) => Hold(Set, Set != null ? Set.Label(label, 0) : 0);

        public void Hold(SpriteAnimSet set, int frame)
        {
            Player.Hold(set, frame);
            Apply();
        }

        public void Stop() => Player.Stop();

        void Update()
        {
            if (Player.Tick(UnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime)) Apply();
        }

        void Apply()
        {
            var s = Set != null ? Set.FrameAt(Player.Frame) : null;
            var im = Image;
            im.sprite = s;
            im.enabled = s != null;
            if (s == null || !AlignPivot) return;
            var rt = (RectTransform)transform;
            var r = s.rect;
            rt.pivot = new Vector2(s.pivot.x / r.width, s.pivot.y / r.height);
            float zoom = s.pixelsPerUnit / Units.PX;           // texture px per Flash px
            rt.sizeDelta = r.size * (FlashPx / Mathf.Max(0.01f, zoom));
        }
    }
}
