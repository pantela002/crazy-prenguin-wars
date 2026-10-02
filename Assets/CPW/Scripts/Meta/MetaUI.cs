using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// Shared building blocks for the out-of-battle menus: icon tiles with an initials fallback,
    /// currency chips, tabs, the gradient background and the original game's %u-style string formatting.
    /// </summary>
    public static class MetaUI
    {
        public const float TopBarHeight = 118f;

        static Sprite gradient, snowDots;

        // ---------- colors ----------
        public static readonly Color Sky = new Color32(64, 150, 230, 255);
        public static readonly Color SkyTop = new Color32(16, 52, 112, 255);
        public static readonly Color Card = new Color32(255, 255, 255, 240);
        public static readonly Color CardDark = new Color32(28, 70, 128, 235);
        public static readonly Color Gold = new Color32(255, 196, 36, 255);
        public static readonly Color Purple = new Color32(150, 92, 220, 255);
        public static readonly Color Orange = new Color32(255, 140, 40, 255);
        public static readonly Color Pink = new Color32(240, 98, 160, 255);
        public static readonly Color Teal = new Color32(40, 190, 180, 255);
        public static readonly Color Locked = new Color32(30, 30, 40, 170);

        /// <summary>Stable pleasant color for an id (used for icon fallbacks).</summary>
        public static Color TintFor(string id)
        {
            if (string.IsNullOrEmpty(id)) return Theme.Secondary;
            int h = 17;
            foreach (char c in id) h = h * 31 + c;
            float hue = Mathf.Abs(h % 360) / 360f;
            return Color.HSVToRGB(hue, 0.55f, 0.9f);
        }

        /// <summary>Two letters that stand for a name ("Basic Nuke" → "BN").</summary>
        public static string Initials(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            var parts = name.Replace('_', ' ').Replace('-', ' ').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) return (char.ToUpperInvariant(parts[0][0]).ToString() + char.ToUpperInvariant(parts[1][0]));
            var p = parts.Length > 0 ? parts[0] : name;
            return p.Length >= 2 ? char.ToUpperInvariant(p[0]) + p.Substring(1, 1).ToLowerInvariant() : p.ToUpperInvariant();
        }

        /// <summary>
        /// Fills the original game's placeholders (%u, %U, %s, %d) in order, plus {0} style ones.
        /// </summary>
        public static string Fmt(string key, params object[] args)
        {
            var s = Loc.T(key);
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i]?.ToString() ?? "";
                s = s.Replace("{" + i + "}", a);
                int idx = FirstPlaceholder(s);
                if (idx >= 0) s = s.Substring(0, idx) + a + s.Substring(idx + 2);
            }
            return s;
        }

        static int FirstPlaceholder(string s)
        {
            for (int i = 0; i < s.Length - 1; i++)
            {
                if (s[i] != '%') continue;
                char n = s[i + 1];
                if (n == 'u' || n == 'U' || n == 's' || n == 'd') return i;
            }
            return -1;
        }

        /// <summary>Loc text if the key exists, otherwise the fallback.</summary>
        public static string TOr(string key, string fallback)
        {
            if (string.IsNullOrEmpty(key) || !Loc.Has(key)) return fallback;
            var s = Loc.T(key);
            return string.IsNullOrEmpty(s) || s == "None" ? fallback : s;
        }

        // ---------- sprites ----------
        /// <summary>Vertical gradient sky used behind menu screens.</summary>
        public static Sprite Gradient
        {
            get
            {
                if (gradient) return gradient;
                const int h = 64;
                var t = new Texture2D(1, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < h; y++) t.SetPixel(0, y, Color.Lerp(Sky, SkyTop, y / (float)(h - 1)));
                t.Apply();
                gradient = Sprite.Create(t, new Rect(0, 0, 1, h), new Vector2(0.5f, 0.5f));
                return gradient;
            }
        }

        /// <summary>Tileable soft snow dots for screen backgrounds.</summary>
        public static Sprite SnowDots
        {
            get
            {
                if (snowDots) return snowDots;
                const int n = 128;
                var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat };
                var px = new Color[n * n];
                var rnd = new System.Random(7);
                for (int i = 0; i < 14; i++)
                {
                    int cx = rnd.Next(n), cy = rnd.Next(n);
                    float r = 1.5f + (float)rnd.NextDouble() * 3f;
                    for (int y = -6; y <= 6; y++)
                    for (int x = -6; x <= 6; x++)
                    {
                        float d = Mathf.Sqrt(x * x + y * y);
                        float a = Mathf.Clamp01(r - d) * 0.5f;
                        int ix = (cx + x + n) % n, iy = (cy + y + n) % n;
                        var c = px[iy * n + ix];
                        px[iy * n + ix] = new Color(1, 1, 1, Mathf.Max(c.a, a));
                    }
                }
                t.SetPixels(px);
                t.Apply();
                snowDots = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
                return snowDots;
            }
        }

        /// <summary>
        /// Full-screen menu background with drifting snow (blocks nothing): the original igloo interior
        /// (background_main) behind a soft dark vignette, like the original's popups over the home screen; the sky
        /// gradient with snowy hills when the art is missing. It also covers the notch / home-indicator margins.
        /// </summary>
        public static void Background(RectTransform root)
        {
            var igloo = UI.Skin.Bitmap("home_screen", 1);
            if (igloo != null)
            {
                var host = UI.Rect(root, "Igloo");
                UI.Stretch(host, -300, -300, -300, -300);
                host.SetAsFirstSibling();
                var img = UI.Image(host, igloo, Color.white, false, "Backdrop");
                var fit = img.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio = igloo.rect.width / Mathf.Max(1f, igloo.rect.height);
                var dim = UI.Image(host, UI.WhiteSprite, new Color(0.02f, 0.1f, 0.25f, 0.35f), false, "Dim");
                UI.Stretch(dim.rectTransform);
                var vignette = UI.Skin.Bitmap("hud_shared", 44);
                if (vignette != null) { var v = UI.Image(host, vignette, new Color(1, 1, 1, 0.8f), false, "Vignette"); UI.Stretch(v.rectTransform); }
                var flakes = UI.Image(root, SnowDots, Color.white, false, "Snow");
                flakes.type = Image.Type.Tiled;
                UI.Stretch(flakes.rectTransform, -200, -200, -200, -200);
                flakes.gameObject.AddComponent<UIDrift>().speed = new Vector2(-12, -30);
                flakes.transform.SetSiblingIndex(1);
                return;
            }
            var bg = UI.Image(root, Gradient, Color.white, false, "Sky");
            UI.Stretch(bg.rectTransform);
            var snow = UI.Image(root, SnowDots, Color.white, false, "Snow");
            snow.type = Image.Type.Tiled;
            UI.Stretch(snow.rectTransform, -200, -200, -200, -200);
            snow.gameObject.AddComponent<UIDrift>().speed = new Vector2(-12, -30);
            // soft hills at the bottom
            for (int i = 0; i < 3; i++)
            {
                var hill = UI.Image(root, UI.Circle, new Color(1, 1, 1, 0.10f + i * 0.04f), false, "Hill");
                hill.rectTransform.anchorMin = hill.rectTransform.anchorMax = new Vector2(0.15f + i * 0.38f, 0);
                hill.rectTransform.sizeDelta = new Vector2(1300 - i * 150, 520 - i * 60);
                hill.rectTransform.anchoredPosition = new Vector2(0, -160 + i * 20);
            }
            bg.transform.SetAsFirstSibling();
        }

        // ---------- icons ----------
        /// <summary>
        /// A rounded tile showing the icon at iconPath (Resources/Icons/...), or a colored tile with the
        /// item's initials when the icon has not been rendered yet.
        /// </summary>
        public static RectTransform IconTile(Transform parent, string iconPath, string displayName, Color? tint = null, bool frame = true)
        {
            var sprite = UI.Skin.Icon(iconPath);   // original icon, else the Blender render
            var col = tint ?? TintFor(displayName ?? iconPath);
            var tile = UI.PanelRaw(parent, frame || sprite == null ? col : new Color(0, 0, 0, 0), true, "IconTile");
            tile.raycastTarget = false;
            if (sprite != null)
            {
                // framed icons sit on the original white-blue item slot (weapon picker / shop)
                if (frame && !UI.Skin.Apply(tile, "slot.blue")) tile.color = new Color(col.r, col.g, col.b, 0.35f);
                var img = UI.Image(tile.transform, sprite);
                UI.Stretch(img.rectTransform, frame ? 12 : 6, frame ? 12 : 6, frame ? 10 : 6, frame ? 14 : 6);
            }
            else
            {
                var shine = UI.Panel(tile.transform, new Color(1, 1, 1, 0.22f), true, "Shine");
                shine.raycastTarget = false;
                UI.Anchor(shine.rectTransform, 0.06f, 0.55f, 0.94f, 0.94f);
                var l = UI.Label(tile.transform, Initials(displayName ?? iconPath), 64, Color.white, TextAnchor.MiddleCenter, true);
                UI.Stretch(l.rectTransform, 6, 6, 6, 6);
            }
            return tile.rectTransform;
        }

        /// <summary>Invisible container covering a normalized region of parent (use with Square for icons).</summary>
        public static RectTransform Box(Transform parent, float xMin, float yMin, float xMax, float yMax)
        {
            var r = UI.Rect(parent, "Box");
            UI.Anchor(r, xMin, yMin, xMax, yMax);
            return r;
        }

        /// <summary>Keep rt square and centered inside its parent.</summary>
        public static void Square(RectTransform rt)
        {
            var f = rt.gameObject.AddComponent<AspectRatioFitter>();
            f.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            f.aspectRatio = 1f;
        }

        public static Sprite CoinIcon => ModelLibrary.Icon("Ui/coin");
        public static Sprite CashIcon => ModelLibrary.Icon("Ui/cash");

        /// <summary>A small coin/cash/xp symbol (icon or colored disc with a letter).</summary>
        public static RectTransform CurrencyIcon(Transform parent, string kind)
        {
            // the premium currency is "Cash" (green banknotes) in the UI, like the original; Ui/cash is a fish render
            var orig = UI.Skin.OriginalIcon("Ui/" + kind);
            if (orig != null) return UI.Image(parent, orig, null, true, "Cur " + kind).rectTransform;
            if (kind == "cash") return CashBill(parent);
            var sprite = ModelLibrary.Icon("Ui/" + kind);
            if (sprite != null) return UI.Image(parent, sprite, null, true, "Cur " + kind).rectTransform;
            Color c = kind == "coin" ? Theme.Coin : kind == "cash" ? Theme.Cash : kind == "vip" ? Gold : Theme.Xp;
            string letter = kind == "coin" ? "C" : kind == "cash" ? "F" : kind == "vip" ? "V" : "XP";
            var disc = UI.Image(parent, UI.Circle, c, false, "Cur " + kind);
            var l = UI.Label(disc.transform, letter, 34, kind == "coin" ? new Color(0.45f, 0.3f, 0) : Color.white, TextAnchor.MiddleCenter, true);
            UI.Stretch(l.rectTransform, 2, 2, 2, 2);
            return disc.rectTransform;
        }

        static readonly Color BillGreen = new Color32(92, 178, 70, 255), BillDark = new Color32(36, 104, 36, 255), BillLight = new Color32(178, 230, 140, 255);

        /// <summary>
        /// The Cash icon: a small stack of green banknotes. Uses Resources/Icons/Ui/banknote when one is added,
        /// otherwise draws it from UI shapes (two bills behind, the front one with a light inner frame and a "$" disc).
        /// Returns a plain square container, so callers can size it like any icon.
        /// </summary>
        public static RectTransform CashBill(Transform parent)
        {
            var sprite = ModelLibrary.Icon("Ui/banknote");
            if (sprite != null) return UI.Image(parent, sprite, null, true, "Cur cash").rectTransform;
            var root = UI.Rect(parent, "Cur cash");
            for (int i = 2; i >= 0; i--)
            {
                var bill = UI.Image(root, UI.RoundedSmall, i == 0 ? BillGreen : Color.Lerp(BillGreen, BillDark, 0.35f + i * 0.15f), false, "Bill" + i);
                bill.type = Image.Type.Sliced;
                UI.Anchor(bill.rectTransform, 0.04f + i * 0.05f, 0.18f + i * 0.07f, 0.86f + i * 0.05f, 0.66f + i * 0.07f);
                bill.rectTransform.localEulerAngles = new Vector3(0, 0, 8 - i * 3);
                var edge = bill.gameObject.AddComponent<Outline>();
                edge.effectColor = BillDark; edge.effectDistance = new Vector2(1.5f, -1.5f);
                if (i != 0) continue;
                var frame = UI.Image(bill.transform, UI.RoundedSmall, BillLight, false, "Frame");
                frame.type = Image.Type.Sliced;
                UI.Anchor(frame.rectTransform, 0.08f, 0.14f, 0.92f, 0.86f);
                var inner = UI.Image(frame.transform, UI.RoundedSmall, BillGreen, false, "Inner");
                inner.type = Image.Type.Sliced;
                UI.Anchor(inner.rectTransform, 0.05f, 0.1f, 0.95f, 0.9f);
                var disc = UI.Image(bill.transform, UI.Circle, BillLight, false, "Disc");
                UI.Anchor(disc.rectTransform, 0.36f, 0.12f, 0.64f, 0.88f);
                var aspect = disc.gameObject.AddComponent<AspectRatioFitter>();
                aspect.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth; aspect.aspectRatio = 1;
                var l = UI.Label(disc.transform, "$", 40, BillDark, TextAnchor.MiddleCenter, true, "Sign");
                foreach (var o in l.GetComponents<Shadow>()) UnityEngine.Object.Destroy(o);
                l.resizeTextMinSize = 6;
                UI.Stretch(l.rectTransform, 1, 1, 1, 1);
            }
            return root;
        }

        /// <summary>Icon + amount laid out horizontally; returns the amount label.</summary>
        public static Text Amount(Transform parent, string kind, string text, int size = 36, Color? color = null)
        {
            var row = UI.Rect(parent, "Amount " + kind);
            var h = UI.HBox(row, 8, TextAnchor.MiddleCenter);
            h.childControlWidth = true; h.childControlHeight = true;
            var ic = CurrencyIcon(row, kind);
            UI.Layout(ic, size + 6, size + 6);
            var l = UI.Label(row, text, size, color ?? Theme.Text, TextAnchor.MiddleLeft, true);
            l.horizontalOverflow = HorizontalWrapMode.Overflow;
            l.resizeTextForBestFit = false;
            UI.Layout(l, -1, size + 8);
            var fit = l.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            return l;
        }

        /// <summary>Price as shown on shop cards (coins or fish/cash).</summary>
        public static void Price(Transform parent, int coins, int cash, int size = 34, Color? color = null)
        {
            if (cash > 0) Amount(parent, "cash", UI.Money(cash), size, color);
            else if (coins > 0) Amount(parent, "coin", UI.Money(coins), size, color);
            else
            {
                var l = UI.Label(parent, "FREE", size, color ?? Theme.Good, TextAnchor.MiddleCenter, true);
                UI.Layout(l, 160, size + 8);
            }
        }

        // ---------- widgets ----------
        /// <summary>White card with a soft drop shadow.</summary>
        public static Image CardPanel(Transform parent, Color? color = null, string name = "Card")
        {
            var c = color ?? Card;
            var p = UI.PanelRaw(parent, c, true, name);
            if (UI.Skin.Apply(p, CardKey(c))) return p;
            var sh = p.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.25f);
            sh.effectDistance = new Vector2(0, -8);
            return p;
        }

        /// <summary>Original panel for a card color: light window, dark blue panel, cream or orange paper; null keeps the color.</summary>
        public static string CardKey(Color c)
        {
            if (c == Card || c == Theme.Panel) return "window.small";
            if (c == CardDark || c == Theme.PanelDark) return "panel.dark";
            Color.RGBToHSV(c, out float h, out float sat, out float v);
            if (c.a > 0.9f && v > 0.9f && sat < 0.2f && h > 0.05f && h < 0.2f) return "panel.cream";   // warm paper
            if (c == Orange || c == Gold) return "panel.orange";
            if (c.a < 0.95f && v < 0.45f && h > 0.5f && h < 0.7f) return "panel.dark";                  // translucent navy
            return null;
        }

        /// <summary>A round "badge" with text (counts, level numbers).</summary>
        public static Text Badge(Transform parent, string text, Color color, float size = 56)
        {
            var b = UI.Image(parent, UI.Circle, color, false, "Badge");
            b.rectTransform.sizeDelta = new Vector2(size, size);
            // counts use the original orange notification dot (white rim)
            bool skinned = (color == Theme.Danger || color == Orange || color == Gold || color == Theme.Primary) && UI.Skin.Apply(b, "badge");
            var l = UI.Label(b.transform, text, (int)(size * 0.55f), Color.white, TextAnchor.MiddleCenter, true);
            UI.Stretch(l.rectTransform, 2, 2, 2, 2);
            if (skinned) UI.StyleText(l, new Color32(140, 50, 0, 255), 1.5f);
            return l;
        }

        /// <summary>Simple tab strip. onSelect gets the index. Returns the buttons so callers can restyle them.</summary>
        public static List<Button> Tabs(RectTransform row, string[] labels, int selected, Action<int> onSelect, int fontSize = 34)
        {
            var list = new List<Button>();
            var h = UI.HBox(row, 12, TextAnchor.MiddleLeft);
            h.childForceExpandWidth = false;
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var b = UI.Button(row, labels[i], () => onSelect(idx), i == selected ? UI.ButtonStyle.Primary : UI.ButtonStyle.Dark, fontSize);
                UI.Layout(b, Mathf.Max(200, labels[i].Length * fontSize * 0.62f + 60), -1, 0, 1);
                SkinTab(b, i == selected);
                list.Add(b);
            }
            return list;
        }

        /// <summary>Restyle tab buttons after the selection changed.</summary>
        public static void SetTabSelected(List<Button> tabs, int selected)
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                if (SkinTab(tabs[i], i == selected)) continue;
                var img = tabs[i].GetComponent<Image>();
                img.color = i == selected ? Theme.Primary : Theme.PanelDark;
                var l = tabs[i].GetComponentInChildren<Text>();
                if (l) l.color = i == selected ? Theme.PrimaryText : Theme.TextLight;
            }
        }

        /// <summary>The original shop tab look (bright blue selected, dark blue otherwise). False when the art is missing.</summary>
        public static bool SkinTab(Button b, bool selected)
        {
            var img = b != null ? b.GetComponent<Image>() : null;
            if (img == null || !UI.Skin.Apply(img, selected ? "tab.on" : "tab.off")) return false;
            var l = b.GetComponentInChildren<Text>();
            if (l) UI.StyleText(l, new Color32(6, 36, 80, 255), 2.5f, selected ? Color.white : new Color32(170, 214, 250, 255));
            return true;
        }

        // ---------- cartoon widgets (the original home screen look) ----------
        /// <summary>color darkened toward black by amount (0..1), alpha kept.</summary>
        public static Color Darker(Color c, float amount = 0.35f) => new Color(c.r * (1 - amount), c.g * (1 - amount), c.b * (1 - amount), c.a);

        /// <summary>Thick colored outline around a label (bold sticker text like the Flash UI).</summary>
        public static Text Outlined(Text t, Color outline, float width = 3f) => UI.StyleText(t, outline, width);

        /// <summary>
        /// Rounded button with a thick darker border, a darker "3D" bottom lip, a glossy top half and bold outlined
        /// white text (Play, Custom game, the Supplies/Character tiles...). The label is the first Text child.
        /// </summary>
        public static Button CartoonButton(Transform parent, string caption, Color face, Action onClick, int fontSize = 48, string name = null)
        {
            var border = Darker(face, 0.42f);
            var rim = UI.PanelRaw(parent, border, true, name ?? ("Cartoon " + caption));
            string key = UI.Skin.KeyForColor(face);
            bool skinned = UI.Skin.Apply(rim, key);
            var b = rim.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            colors.disabledColor = new Color(0.75f, 0.75f, 0.75f, 0.85f);
            b.colors = colors;
            if (skinned)
            {
                // the original bitmap already has its border, lip and gloss
                if (caption != null)
                {
                    var l = UI.Label(rim.transform, caption, fontSize, Color.white, TextAnchor.MiddleCenter, true, "Caption");
                    UI.Stretch(l.rectTransform, 14, 14, 6, 14);
                    UI.StyleText(l, UI.Skin.OutlineColor(key, Darker(face, 0.6f)), Mathf.Clamp(fontSize / 18f, 2f, 4f), UI.Skin.TextColor(key, Color.white));
                }
                if (onClick != null) b.onClick.AddListener(() => { UI.Click(); onClick(); });
                rim.gameObject.AddComponent<PressScale>();
                return b;
            }
            var sh = rim.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.3f);
            sh.effectDistance = new Vector2(0, -8);
            var lip = UI.PanelRaw(rim.transform, Darker(face, 0.18f), true, "Lip");
            UI.Stretch(lip.rectTransform, 6, 6, 6, 6);
            lip.raycastTarget = false;
            var top = UI.PanelRaw(rim.transform, face, true, "Face");
            UI.Stretch(top.rectTransform, 6, 6, 6, 14);
            top.raycastTarget = false;
            var gloss = UI.PanelRaw(top.transform, new Color(1, 1, 1, 0.22f), true, "Gloss");
            UI.Anchor(gloss.rectTransform, 0, 0.52f, 1, 1);
            gloss.rectTransform.offsetMin = new Vector2(6, 0); gloss.rectTransform.offsetMax = new Vector2(-6, -4);
            gloss.raycastTarget = false;
            if (caption != null)
            {
                var l = UI.Label(rim.transform, caption, fontSize, Color.white, TextAnchor.MiddleCenter, true, "Caption");
                UI.Stretch(l.rectTransform, 14, 14, 8, 16);
                Outlined(l, Darker(face, 0.6f), Mathf.Clamp(fontSize / 18f, 2f, 4f));
            }
            if (onClick != null) b.onClick.AddListener(() => { UI.Click(); onClick(); });
            rim.gameObject.AddComponent<PressScale>();
            return b;
        }

        /// <summary>Big square-ish cartoon tile: icon on top, caption at the bottom (home Supplies/Character/Crafting).</summary>
        public static Button CartoonTile(Transform parent, string icon, string caption, Color face, Action onClick, int fontSize = 44)
        {
            var b = CartoonButton(parent, null, face, onClick, fontSize, "Tile " + caption);
            // the original home tiles: orange (Supplies, Character, Crafting...) or white-blue (Invite, slots)
            Color.RGBToHSV(face, out float h, out float sat, out _);
            string key = sat > 0.3f && h > 0.03f && h < 0.17f ? "tile.orange" : "tile.blue";
            var img = b.GetComponent<Image>();
            bool skinned = img != null && img.sprite != null && UI.Skin.Apply(img, key);
            var tile = IconTile(Box(b.transform, 0.14f, 0.3f, 0.86f, 0.95f), icon, caption, Color.Lerp(face, Color.white, 0.3f), false);
            Square(tile);
            var l = UI.Label(b.transform, caption, fontSize, Color.white, TextAnchor.MiddleCenter, true, "Caption");
            UI.Anchor(l.rectTransform, 0.04f, 0.05f, 0.96f, 0.32f);
            if (skinned) UI.StyleText(l, UI.Skin.OutlineColor(key, Darker(face, 0.6f)), 3f, UI.Skin.TextColor(key, Color.white));
            else Outlined(l, Darker(face, 0.6f), 3f);
            return b;
        }

        /// <summary>Round icon button with a thick rim and a small outlined caption under it (home side icons).</summary>
        public static Button RoundIcon(Transform parent, string icon, string caption, Color face, Action onClick, float size = 96)
        {
            var b = CartoonButton(parent, null, face, onClick, 30, "Round " + caption);
            if (!UI.Skin.Apply(b.GetComponent<Image>(), "round.blue"))
                foreach (var img in b.GetComponentsInChildren<Image>()) { img.sprite = UI.Circle; img.type = Image.Type.Simple; }
            var rt = (RectTransform)b.transform;
            rt.sizeDelta = new Vector2(size, size);
            var tile = IconTile(Box(b.transform, 0.16f, 0.18f, 0.84f, 0.86f), icon, caption, Color.Lerp(face, Color.white, 0.3f), false);
            Square(tile);
            if (!string.IsNullOrEmpty(caption))
            {
                var l = UI.Label(b.transform, caption, 26, Color.white, TextAnchor.UpperCenter, true, "Caption");
                l.rectTransform.anchorMin = new Vector2(-0.4f, 0); l.rectTransform.anchorMax = new Vector2(1.4f, 0);
                l.rectTransform.pivot = new Vector2(0.5f, 1);
                l.rectTransform.sizeDelta = new Vector2(0, 32);
                l.rectTransform.anchoredPosition = new Vector2(0, -2);
                Outlined(l, new Color32(14, 50, 104, 255), 2.5f);
            }
            return b;
        }

        /// <summary>A labelled progress bar; returns the fill image.</summary>
        public static Image ProgressBar(Transform parent, float value01, Color color, string text = null)
        {
            var fill = UI.Bar(parent, color);
            fill.fillAmount = Mathf.Clamp01(value01);
            if (!string.IsNullOrEmpty(text))
            {
                var l = UI.Label(fill.transform.parent, text, 26, Color.white, TextAnchor.MiddleCenter, true);
                UI.Stretch(l.rectTransform, 4, 4, 2, 2);
            }
            return fill;
        }

        /// <summary>Close button (X) at the top right of a popup window.</summary>
        public static void CloseButton(RectTransform window, Action onClose)
        {
            var b = UI.Button(window, "X", onClose, UI.ButtonStyle.Danger, 40, "Close");
            UI.Place((RectTransform)b.transform, new Vector2(1, 1), new Vector2(90, 90), new Vector2(20, 20));
            SkinClose(b);
        }

        /// <summary>The original orange X button art on a close button (its "X" caption hidden). False when missing.</summary>
        public static bool SkinClose(Button b)
        {
            var img = b != null ? b.GetComponent<Image>() : null;
            if (img == null || !UI.Skin.Apply(img, "close")) return false;
            img.preserveAspect = true;
            var l = b.GetComponentInChildren<Text>();
            if (l) l.enabled = false;
            return true;
        }

        /// <summary>
        /// Bigger custom popup window (for item info, level up...). Returns the window; destroy layer to close.
        /// </summary>
        public static RectTransform Window(string title, Vector2 size, out RectTransform layer, Color? titleColor = null)
        {
            layer = UI.Rect(UI.PopupLayer, "Popup " + title);
            UI.Stretch(layer);
            UI.Blocker(layer, 0.6f);
            var panel = UI.PanelRaw(layer, Theme.Panel, true, "Window");
            bool skinned = UI.Skin.Apply(panel, "window");   // the original popup_message window, title on its top edge
            UI.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), size, Vector2.zero);
            if (!skinned)
            {
                var sh = panel.gameObject.AddComponent<Shadow>();
                sh.effectColor = new Color(0, 0, 0, 0.4f);
                sh.effectDistance = new Vector2(0, -12);
            }
            var head = UI.PanelRaw(panel.transform, skinned ? new Color(0, 0, 0, 0) : titleColor ?? Theme.Secondary, true, "Head");
            head.rectTransform.anchorMin = new Vector2(0, 1); head.rectTransform.anchorMax = new Vector2(1, 1);
            head.rectTransform.pivot = new Vector2(0.5f, 1);
            head.rectTransform.sizeDelta = new Vector2(0, 110);
            head.rectTransform.anchoredPosition = Vector2.zero;
            var t = UI.Label(head.transform, title, 58, Color.white, TextAnchor.MiddleCenter, true);
            UI.Stretch(t.rectTransform, 100, 100, 6, 6);
            if (skinned) UI.StyleText(t, Darker(titleColor ?? new Color32(20, 80, 160, 255), 0.45f), 3.5f);
            var l = layer;
            CloseButton(panel.rectTransform, () => { if (l) UnityEngine.Object.Destroy(l.gameObject); });
            layer.gameObject.AddComponent<PopIn>().target = panel.rectTransform;
            return panel.rectTransform;
        }

        public static void Close(RectTransform layer) { if (layer) UnityEngine.Object.Destroy(layer.gameObject); }

        /// <summary>Format seconds as m:ss or h:mm:ss.</summary>
        public static string Clock(double seconds)
        {
            if (seconds < 0) seconds = 0;
            int s = (int)seconds;
            int h = s / 3600, m = (s / 60) % 60, sec = s % 60;
            return h > 0 ? h + ":" + m.ToString("00") + ":" + sec.ToString("00") : m + ":" + sec.ToString("00");
        }

        public static long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        public static string Today => DateTime.Now.ToString("yyyy-MM-dd");
    }

    /// <summary>
    /// Base for menu screens: sky background, title, back button and a content rect below the top bar.
    /// </summary>
    public abstract class MetaScreen : UIScreen
    {
        protected RectTransform Content;
        protected virtual string Title => "";
        protected virtual bool ShowBack => true;
        protected virtual bool DrawBackground => true;

        public override void Build()
        {
            if (DrawBackground) MetaUI.Background(Root);
            float top = ShowTopBar ? MetaUI.TopBarHeight : 0;
            if (!string.IsNullOrEmpty(Title) || ShowBack)
            {
                var header = UI.Rect(Root, "Header");
                header.anchorMin = new Vector2(0, 1); header.anchorMax = new Vector2(1, 1); header.pivot = new Vector2(0.5f, 1);
                header.sizeDelta = new Vector2(0, 110);
                header.anchoredPosition = new Vector2(0, -top);
                if (ShowBack)
                {
                    var b = UI.Button(header, "<", () => ScreenManager.Back(), UI.ButtonStyle.Secondary, 56, "Back");
                    UI.Place((RectTransform)b.transform, new Vector2(0, 0.5f), new Vector2(120, 96), new Vector2(24, 0));
                }
                var t = UI.Label(header, Title, 72, Color.white, TextAnchor.MiddleLeft, true);
                UI.Stretch(t.rectTransform, ShowBack ? 170 : 40, 40, 4, 4);
                UI.StyleText(t, new Color32(12, 52, 110, 255), 4f);
                top += 110;
            }
            Content = UI.Rect(Root, "Content");
            UI.Stretch(Content, 24, 24, top + 6, 20);
            BuildContent();
        }

        protected abstract void BuildContent();
    }

    /// <summary>Scrolls a (tiled) image slowly, e.g. falling snow.</summary>
    public class UIDrift : MonoBehaviour
    {
        public Vector2 speed = new Vector2(0, -20);
        RectTransform rt;
        Vector2 start, off;
        void Awake() { rt = (RectTransform)transform; start = rt.anchoredPosition; }
        void Update()
        {
            off += speed * Time.unscaledDeltaTime;
            if (off.x < -128) off.x += 128; if (off.x > 128) off.x -= 128;
            if (off.y < -128) off.y += 128; if (off.y > 128) off.y -= 128;
            rt.anchoredPosition = start + off;
        }
    }

    /// <summary>Gentle pulsing scale (for "claim!" buttons and badges).</summary>
    public class UIPulse : MonoBehaviour
    {
        public float amount = 0.06f, speed = 4f;
        float t;
        void Update()
        {
            t += Time.unscaledDeltaTime * speed;
            float s = 1 + Mathf.Sin(t) * amount;
            transform.localScale = new Vector3(s, s, 1);
        }
    }

    /// <summary>Spins a UI element (rays behind rewards).</summary>
    public class UISpin : MonoBehaviour
    {
        public float degPerSec = 30f;
        void Update() { transform.Rotate(0, 0, degPerSec * Time.unscaledDeltaTime); }
    }
}
