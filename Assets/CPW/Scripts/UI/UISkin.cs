using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CPW
{
    public static partial class UI
    {
        /// <summary>
        /// The original Flash UI art as a skin for the code-built uGUI: 9-sliced panels, buttons, tabs, cards and bars
        /// cut from Resources/Original/ui/*/_bitmaps, with their slice borders defined once in
        /// Resources/Original/ui_skin.json. UI.Button / Popup / Bar / Toggle / Slider / Input and the MetaUI widgets
        /// ask Apply(image, key) first and keep their procedural look when it returns false (art not imported, key
        /// missing, or Skin.Off). Sprites are rebuilt at 100 px per unit so 1 texture pixel = 1 canvas unit at scale 1;
        /// SkinFit shrinks the borders uniformly on small rects (corners stay round) through cached PPU variants.
        /// </summary>
        public static class Skin
        {
            /// <summary>Force the procedural look (debugging, or a device that runs out of texture memory).</summary>
            public static bool Off;

            sealed class Entry
            {
                public string path;
                public Vector4 border;            // l, b, r, t (texture px)
                public float scale = 1f;          // canvas units per texture px at full size
                public Vector4 pad;
                public Color text = Color.white, outline = new Color32(12, 44, 90, 255);
                public bool loaded;
                public Sprite src;
                public readonly Dictionary<int, Sprite> variants = new Dictionary<int, Sprite>();
            }

            static Dictionary<string, Entry> entries;

            static void Load()
            {
                if (entries != null) return;
                entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                var ta = Resources.Load<TextAsset>(OriginalArt.Root + "ui_skin");
                var root = ta != null ? MiniJson.ParseObject(ta.text) : null;
                if (root == null || !root.TryGetValue("sprites", out var so) || !(so is Dictionary<string, object> sd)) return;
                foreach (var kv in sd)
                {
                    if (!(kv.Value is Dictionary<string, object> d) || !(d.TryGetValue("path", out var p) && p is string path)) continue;
                    var e = new Entry { path = path, border = V4(d, "border"), pad = V4(d, "pad") };
                    if (d.TryGetValue("scale", out var s)) e.scale = Mathf.Max(0.05f, F(s));
                    if (d.TryGetValue("text", out var tc) && tc is string ts && ColorUtility.TryParseHtmlString(ts, out var c1)) e.text = c1;
                    if (d.TryGetValue("outline", out var oc) && oc is string os && ColorUtility.TryParseHtmlString(os, out var c2)) e.outline = c2;
                    entries[kv.Key] = e;
                }
            }

            static Entry Find(string key)
            {
                if (Off || string.IsNullOrEmpty(key)) return null;
                Load();
                if (!entries.TryGetValue(key, out var e)) return null;
                if (!e.loaded)
                {
                    e.loaded = true;
                    e.src = OriginalArt.Sprite(e.path);
                    if (e.src != null)
                    {
                        var r = e.src.rect;
                        // borders must fit inside the bitmap (a re-export at another size must not throw)
                        e.border.x = Mathf.Clamp(e.border.x, 0, r.width * 0.49f); e.border.z = Mathf.Clamp(e.border.z, 0, r.width * 0.49f);
                        e.border.y = Mathf.Clamp(e.border.y, 0, r.height * 0.49f); e.border.w = Mathf.Clamp(e.border.w, 0, r.height * 0.49f);
                    }
                }
                return e.src != null ? e : null;
            }

            /// <summary>True when the original art for this key is available.</summary>
            public static bool Has(string key) => Find(key) != null;

            /// <summary>True when the skin is in use at all (the art was imported and the json loaded).</summary>
            public static bool Enabled => Has("button.blue");

            /// <summary>The bitmap of a key as a sliced sprite whose borders are scale canvas units per texture pixel.</summary>
            public static Sprite Get(string key, float scale = -1)
            {
                var e = Find(key);
                if (e == null) return null;
                if (scale <= 0) scale = e.scale;
                int q = Mathf.Clamp(Mathf.RoundToInt(scale * 20f), 1, 60);   // 0.05 steps
                if (e.variants.TryGetValue(q, out var v) && v != null) return v;
                var s = e.src;
                var rect = s.textureRect;
                v = Sprite.Create(s.texture, rect, new Vector2(0.5f, 0.5f), 100f / (q / 20f), 0, SpriteMeshType.FullRect, e.border);
                v.name = s.name + "_skin" + q;
                e.variants[q] = v;
                return v;
            }

            /// <summary>Slice borders (l, b, r, t) of a key in texture pixels (zero when missing).</summary>
            public static Vector4 Border(string key) => Find(key)?.border ?? Vector4.zero;
            /// <summary>Preferred border scale of a key (canvas units per texture px).</summary>
            public static float Scale(string key) => Find(key)?.scale ?? 1f;
            /// <summary>Content padding (l, b, r, t) of a key, in canvas units.</summary>
            public static Vector4 Pad(string key) => Find(key)?.pad ?? Vector4.zero;
            public static Color TextColor(string key, Color fallback) => Find(key)?.text ?? fallback;
            public static Color OutlineColor(string key, Color fallback) => Find(key)?.outline ?? fallback;

            /// <summary>
            /// Skin an Image with the key's bitmap (white tint, sliced when the key has borders, fitted to the rect).
            /// Returns false and leaves the image untouched when the art is missing.
            /// </summary>
            public static bool Apply(UnityEngine.UI.Image img, string key)
            {
                var e = img != null ? Find(key) : null;
                if (e == null) return false;
                bool sliced = e.border.sqrMagnitude > 0;
                img.sprite = Get(key);
                img.type = sliced ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
                img.preserveAspect = false;
                img.fillCenter = true;
                img.color = Color.white;
                var fit = img.GetComponent<SkinFit>();
                if (fit == null) fit = img.gameObject.AddComponent<SkinFit>();
                fit.Set(key, sliced);
                return true;
            }

            /// <summary>The key a button style maps to.</summary>
            public static string ButtonKey(ButtonStyle s)
            {
                switch (s)
                {
                    case ButtonStyle.Primary: return "button.orange";
                    case ButtonStyle.Secondary: return "button.blue";
                    case ButtonStyle.Danger: return "button.orange";
                    case ButtonStyle.Good: return "button.green";
                    case ButtonStyle.Dark: return "button.dark";
                    default: return "button.light";
                }
            }

            /// <summary>Nearest original button for an arbitrary face color (MetaUI cartoon buttons).</summary>
            public static string KeyForColor(Color c)
            {
                Color.RGBToHSV(c, out float h, out float s, out float v);
                if (s < 0.25f) return v > 0.7f ? "button.light" : "button.dark";
                float deg = h * 360f;
                if (deg < 20 || deg >= 330) return "button.orange";    // reds: the original has no red pill
                if (deg < 45) return "button.orange";
                if (deg < 70) return "button.yellow";
                if (deg < 170) return "button.green";
                if (deg < 245) return v < 0.55f ? "button.dark" : "button.blue";
                return "button.purple";
            }

            /// <summary>Theme panel colors that have an original counterpart (UI.Panel uses these automatically).</summary>
            public static string KeyForPanel(Color c)
            {
                if (c == Theme.Panel) return "window.small";
                if (c == Theme.PanelDark) return "panel.dark";
                if (c == Theme.PanelInner) return "card";
                return null;
            }

            // ---------------------------------------------------------------- icons

            static readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();

            /// <summary>Resources/Icons-style paths ("Ui/coin", "Weapons/BasicNuke") mapped to original art.</summary>
            static readonly Dictionary<string, string> UiIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Ui/coin", "ui/slot_machine/_bitmaps/bitmap_90" },
                { "Ui/cash", "ui/home_screen/_bitmaps/bitmap_169" },
                { "Ui/banknote", "ui/home_screen/_bitmaps/bitmap_169" },
                { "Ui/star", "ui/home_screen/_bitmaps/bitmap_20" },
                { "Ui/settings", "ui/home_screen/_bitmaps/bitmap_114" },
                { "Ui/gift", "ui/home_screen/_bitmaps/bitmap_221" },
                { "Ui/vip", "ui/shops_new/_bitmaps/bitmap_103" },
                { "Ui/online", "ui/home_screen/_bitmaps/bitmap_159" },
                { "Ui/friends", "ui/home_screen/_bitmaps/bitmap_159" },
                { "Ui/tutorial", "ui/home_screen/_bitmaps/bitmap_229" },
                { "Ui/news", "ui/home_screen/_bitmaps/bitmap_234" },
                { "Ui/shop", "ui/home_screen/_bitmaps/bitmap_149" },
                { "Ui/crafting", "ui/home_screen/_bitmaps/bitmap_137" },
                { "Ui/wardrobe", "ui/home_screen/_bitmaps/bitmap_124" },
                { "Ui/slot", "ui/home_screen/_bitmaps/bitmap_54" },
                { "Ui/leaderboard", "ui/shops_new/_bitmaps/bitmap_114" },
                { "Ui/trophy", "ui/multiplayer/_bitmaps/bitmap_93" },
                { "Ui/xp", "ui/slot_machine/icon_xp" },
                { "Ui/lock", "ui/shops_new/_bitmaps/bitmap_99" },
                { "Ui/app_icon", "ui/popups/_bitmaps/bitmap_245" },
                { "Ui/practice", "ui/multiplayer/_bitmaps/bitmap_259" },
                { "Ui/custom", "ui/multiplayer/_bitmaps/bitmap_261" },
                { "Ui/quickmatch", "ui/multiplayer/_bitmaps/bitmap_188" },
                { "Ui/crate", "ui/popups/_bitmaps/bitmap_258" },
                { "Ui/timer", "ui/popups/_bitmaps/bitmap_185" },
                { "Ui/check", "ui/popups/_bitmaps/bitmap_181" },
                { "Ui/inbox", "ui/home_screen/_bitmaps/bitmap_186" },
                { "Ui/add_friend", "ui/home_screen/_bitmaps/bitmap_25" },
                { "Ui/logo", "ui/home_screen/_bitmaps/bitmap_244" },
                { "Hud/Emote", "ui/ingame/_bitmaps/bitmap_403" },
                { "Hud/Pause", "ui/ingame/_bitmaps/bitmap_497" },
                { "Hud/Chat", "ui/ingame/_bitmaps/bitmap_422" },
            };

            /// <summary>
            /// The original icon for an icon path (Ui/*, Hud/*, Weapons/*, Boosters/*, Emoticons/*, Slot/*), or null
            /// (clothes, crafting, achievements and the remake-only items keep their Blender renders).
            /// </summary>
            public static Sprite OriginalIcon(string path)
            {
                if (Off || string.IsNullOrEmpty(path)) return null;
                if (icons.TryGetValue(path, out var s)) return s;
                s = null;
                try
                {
                    int slash = path.IndexOf('/');
                    string kind = slash > 0 ? path.Substring(0, slash) : "", id = slash > 0 ? path.Substring(slash + 1) : path;
                    if (UiIcons.TryGetValue(path, out var p)) s = OriginalArt.Sprite(p);
                    else if (kind == "Weapons") s = OriginalArt.Sprite(OriginalArt.GraphicPath("#WeaponIcon." + id)) ?? OriginalArt.Icon(id);
                    else if (kind == "Boosters") s = OriginalArt.Sprite(OriginalArt.GraphicPath("#BoosterIcon." + id)) ?? OriginalArt.Icon(id);
                    else if (kind == "Emoticons") s = OriginalArt.EmoteIcon(id) ?? OriginalArt.Icon(id);
                    else if (kind == "Slot") s = OriginalArt.SlotIcon(id);
                }
                catch (Exception ex) { Debug.LogWarning("Skin icon " + path + ": " + ex.Message); }
                icons[path] = s;
                return s;
            }

            /// <summary>Weapon paths ("Weapons/{id}") prefer the textured weapon render (Icons/WeaponsTextured/{id});
            /// otherwise the original icon when there is one, else the Blender render (ModelLibrary.Icon), else null.</summary>
            public static Sprite Icon(string path)
            {
                if (path != null && path.StartsWith("Weapons/", StringComparison.Ordinal))
                {
                    var tex = ModelLibrary.Icon("WeaponsTextured/" + path.Substring(8));
                    if (tex != null) return tex;
                }
                return OriginalIcon(path) ?? ModelLibrary.Icon(path);
            }

            /// <summary>An embedded UI bitmap by SWF folder and id: Bitmap("home_screen", 122).</summary>
            public static Sprite Bitmap(string swf, int id) => Off ? null : OriginalArt.UiSprite(swf, "bitmap_" + id);

            /// <summary>An image showing an original UI bitmap (preserving its aspect), or null when missing.</summary>
            public static UnityEngine.UI.Image BitmapImage(Transform parent, string swf, int id, string name = null)
            {
                var s = Bitmap(swf, id);
                if (s == null) return null;
                return UI.Image(parent, s, Color.white, true, name ?? ("Art " + swf + "_" + id));
            }

            // ---------------------------------------------------------------- helpers

            static float F(object o)
            {
                switch (o)
                {
                    case double d: return (float)d;
                    case long l: return l;
                    case int i: return i;
                    case float f: return f;
                    default: return 0f;
                }
            }

            static Vector4 V4(Dictionary<string, object> d, string key)
            {
                return d.TryGetValue(key, out var v) && v is List<object> l && l.Count >= 4 ? new Vector4(F(l[0]), F(l[1]), F(l[2]), F(l[3])) : Vector4.zero;
            }
        }

        /// <summary>
        /// White bold caption with a dark outline and a drop shadow, the way the original's Berlin Sans text looked
        /// (the font itself is commercial and not shipped). Replaces any previous Outline/Shadow on the label.
        /// </summary>
        public static Text StyleText(Text t, Color outline, float width = 3f, Color? face = null)
        {
            if (t == null) return null;
            foreach (var o in t.GetComponents<Shadow>()) UnityEngine.Object.Destroy(o);
            if (face.HasValue) t.color = face.Value;
            var ol = t.gameObject.AddComponent<Outline>();
            ol.effectColor = outline;
            ol.effectDistance = new Vector2(width, -width);
            var sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(outline.r * 0.6f, outline.g * 0.6f, outline.b * 0.6f, 0.75f);
            sh.effectDistance = new Vector2(0, -width - 2);
            return t;
        }

        /// <summary>
        /// Grow a small button's touch area without changing its look: an invisible raycast child reaching padX / padY
        /// canvas units beyond each side (the art of some original buttons is shorter than a fingertip).
        /// </summary>
        public static void HitPad(RectTransform button, float padX, float padY)
        {
            if (button == null) return;
            var pad = Rect(button, "HitPad");
            pad.anchorMin = Vector2.zero; pad.anchorMax = Vector2.one;
            pad.offsetMin = new Vector2(-padX, -padY); pad.offsetMax = new Vector2(padX, padY);
            var img = pad.gameObject.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(1, 1, 1, 0);
            img.raycastTarget = true;
            pad.SetAsFirstSibling();
        }

        /// <summary>
        /// Recolor an image that may carry the original art. Skinned images switch to the original piece nearest the
        /// color (yellow selected card, light card, dark panel; for buttons the nearest original button) and keep
        /// some of a saturated tint and the alpha; plain images just take the color.
        /// </summary>
        public static void SkinColor(UnityEngine.UI.Image img, Color c, bool button = false)
        {
            if (img == null) return;
            var fit = img.GetComponent<SkinFit>();
            if (fit == null || !fit.enabled || c.a < 0.35f) { img.color = c; return; }
            Color.RGBToHSV(c, out float h, out float s, out float v);
            string key;
            if (button) key = Skin.KeyForColor(c);
            else if (s > 0.4f && h > 0.08f && h < 0.18f) key = "card.selected";
            else if (v < 0.5f) key = "panel.dark";
            else key = "card";
            if (!Skin.Apply(img, key)) { img.color = c; return; }
            var tint = !button && key == "card" && s > 0.25f ? Color.Lerp(c, Color.white, 0.45f) : Color.white;
            tint.a = c.a;
            img.color = tint;
        }

        /// <summary>Restyle a UI.Button after its style changed (selected tabs, toggles): skin or color, plus caption.</summary>
        public static void SetButtonStyle(Button b, ButtonStyle style)
        {
            if (b == null) return;
            var img = b.GetComponent<UnityEngine.UI.Image>();
            var l = b.GetComponentInChildren<Text>();
            string key = Skin.ButtonKey(style);
            if (img != null && Skin.Apply(img, key))
            {
                if (l != null) StyleText(l, Skin.OutlineColor(key, Theme.Text), Mathf.Clamp(l.fontSize / 16f, 1.5f, 3f), Skin.TextColor(key, Color.white));
                return;
            }
            if (img != null) img.color = StyleColor(style);
            if (l != null) l.color = style == ButtonStyle.Primary ? Theme.PrimaryText : (style == ButtonStyle.Plain ? Theme.Text : Theme.TextLight);
        }
    }

    /// <summary>
    /// Keeps a skinned Image's 9-slice borders inside its rect: on small rects the borders shrink uniformly (a sprite
    /// variant with a higher pixels-per-unit) so rounded corners stay round instead of overlapping. It also reports a
    /// fixed small preferred size to layout groups (priority 1), so swapping variants never feeds back into layout.
    /// </summary>
    [DisallowMultipleComponent]
    public class SkinFit : UIBehaviour, ILayoutElement
    {
        string key;
        bool sliced, dirty;
        UnityEngine.UI.Image img;
        int lastQ = -1;

        public void Set(string skinKey, bool isSliced)
        {
            key = skinKey; sliced = isSliced;
            img = GetComponent<UnityEngine.UI.Image>();
            lastQ = -1;
            dirty = true;
        }

        protected override void OnEnable() { base.OnEnable(); dirty = true; }
        protected override void OnRectTransformDimensionsChange() { dirty = true; }

        void LateUpdate()
        {
            if (!dirty) return;
            dirty = false;
            Fit();
        }

        void Fit()
        {
            if (!sliced || img == null || string.IsNullOrEmpty(key)) return;
            var size = ((RectTransform)transform).rect.size;
            if (size.x <= 1 || size.y <= 1) return;
            var b = UI.Skin.Border(key);
            float s = UI.Skin.Scale(key);
            if (b.x + b.z > 0) s = Mathf.Min(s, size.x / (b.x + b.z));
            if (b.y + b.w > 0) s = Mathf.Min(s, size.y / (b.y + b.w));
            int q = Mathf.Clamp(Mathf.FloorToInt(s * 20f), 1, 60);
            if (q == lastQ) return;
            lastQ = q;
            var sp = UI.Skin.Get(key, q / 20f);
            // only swap while this image still shows the skin (a caller may have put another sprite on it)
            if (sp != null && img.sprite != null && img.sprite.texture == sp.texture) img.sprite = sp;
        }

        // ---- ILayoutElement: neutral sizes like the old procedural rounded sprite ----
        public void CalculateLayoutInputHorizontal() { }
        public void CalculateLayoutInputVertical() { }
        public float minWidth => 0;
        public float preferredWidth => 44;
        public float flexibleWidth => -1;
        public float minHeight => 0;
        public float preferredHeight => 44;
        public float flexibleHeight => -1;
        public int layoutPriority => 1;
    }
}
