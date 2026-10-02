using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>Colors and sizes shared by every screen.</summary>
    public static class Theme
    {
        public static readonly Color Bg = new Color32(18, 44, 84, 255);
        public static readonly Color Panel = new Color32(236, 246, 255, 245);
        public static readonly Color PanelDark = new Color32(20, 52, 98, 235);
        public static readonly Color PanelInner = new Color32(205, 228, 250, 255);
        public static readonly Color Primary = new Color32(255, 196, 36, 255);    // yellow action buttons
        public static readonly Color PrimaryText = new Color32(70, 38, 6, 255);
        public static readonly Color Secondary = new Color32(64, 152, 236, 255);  // blue buttons
        public static readonly Color Danger = new Color32(232, 76, 61, 255);
        public static readonly Color Good = new Color32(88, 196, 72, 255);
        public static readonly Color Text = new Color32(26, 46, 80, 255);
        public static readonly Color TextLight = Color.white;
        public static readonly Color Muted = new Color32(120, 140, 170, 255);
        public static readonly Color Coin = new Color32(255, 204, 51, 255);
        public static readonly Color Cash = new Color32(110, 220, 120, 255);
        public static readonly Color Xp = new Color32(120, 200, 255, 255);
        public static readonly Color[] PlayerColors =
        {
            new Color32(232, 76, 61, 255), new Color32(52, 152, 219, 255),
            new Color32(46, 204, 113, 255), new Color32(241, 196, 15, 255)
        };
    }

    /// <summary>
    /// Code-built uGUI helpers. The whole game UI is built from code with these so no scenes or prefabs are needed.
    /// Layout is done in a 1920x1080 reference canvas (landscape).
    /// </summary>
    public static class UI
    {
        public static Canvas Canvas { get; private set; }
        public static RectTransform Root { get; private set; }      // full screen
        public static RectTransform Safe { get; private set; }      // inside the device safe area
        public static RectTransform PopupLayer { get; private set; }
        public static Font TitleFont { get; private set; }
        public static Font BodyFont { get; private set; }

        static Sprite rounded, roundedSmall, circle, white;

        public static void Init(Transform parent)
        {
            if (Canvas != null) return;
            TitleFont = Resources.Load<Font>("Fonts/LuckiestGuy");
            BodyFont = Resources.Load<Font>("Fonts/Fredoka");
            if (BodyFont == null) BodyFont = BuiltinFont();
            if (TitleFont == null) TitleFont = BodyFont;

            var go = new GameObject("UI", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Canvas = go.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 10;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = UI.ScalerMatch;
            go.AddComponent<GraphicRaycaster>();
            Root = (RectTransform)go.transform;

            var safeGo = new GameObject("Safe", typeof(RectTransform));
            safeGo.transform.SetParent(Root, false);
            Safe = (RectTransform)safeGo.transform;
            Stretch(Safe);
            safeGo.AddComponent<SafeAreaFitter>();

            var pop = new GameObject("Popups", typeof(RectTransform));
            pop.transform.SetParent(Root, false);
            PopupLayer = (RectTransform)pop.transform;
            Stretch(PopupLayer);

            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.transform.SetParent(parent, false);
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
        }

        static Font BuiltinFont()
        {
            foreach (var n in new[] { "LegacyRuntime.ttf", "Arial.ttf" })
            {
                try { var f = Resources.GetBuiltinResource<Font>(n); if (f) return f; } catch { }
            }
            return null;
        }

        // ---------- sprites ----------
        public static Sprite Rounded => rounded ? rounded : (rounded = MakeRounded(64, 22));
        public static Sprite RoundedSmall => roundedSmall ? roundedSmall : (roundedSmall = MakeRounded(32, 10));
        public static Sprite Circle => circle ? circle : (circle = MakeCircle(128));
        public static Sprite WhiteSprite => white ? white : (white = Sprite.Create(Mats.White, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f)));

        static Sprite MakeRounded(int size, int radius)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                float a = Mathf.Clamp01(radius - d + 0.5f);
                px[y * size + x] = new Color(1, 1, 1, a);
            }
            t.SetPixels(px);
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
        }

        static Sprite MakeCircle(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            float r = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                px[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(r - d));
            }
            t.SetPixels(px);
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        // ---------- layout ----------
        public static RectTransform Rect(Transform parent, string name = "Rect")
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom); rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Anchor by normalized rect (0..1 of the parent), with optional pixel padding.</summary>
        public static RectTransform Anchor(RectTransform rt, float xMin, float yMin, float xMax, float yMax, float pad = 0)
        {
            rt.anchorMin = new Vector2(xMin, yMin); rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = new Vector2(pad, pad); rt.offsetMax = new Vector2(-pad, -pad);
            return rt;
        }

        /// <summary>Fixed size, positioned relative to an anchor point of the parent (pivot = anchor).</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 size, Vector2 offset)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = offset;
            return rt;
        }

        public static void Size(RectTransform rt, float w, float h) => rt.sizeDelta = new Vector2(w, h);

        public static LayoutElement Layout(Component c, float prefW = -1, float prefH = -1, float flexW = -1, float flexH = -1)
        {
            if (!c.TryGetComponent<LayoutElement>(out var le)) le = c.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = prefW; le.preferredHeight = prefH; le.flexibleWidth = flexW; le.flexibleHeight = flexH;
            return le;
        }

        /// <summary>Only one LayoutGroup is allowed per object; replace any existing one so builders can be re-run.</summary>
        static T NewLayout<T>(RectTransform rt) where T : LayoutGroup
        {
            var old = rt.GetComponent<LayoutGroup>();
            if (old) UnityEngine.Object.DestroyImmediate(old);
            return rt.gameObject.AddComponent<T>();
        }

        public static HorizontalLayoutGroup HBox(RectTransform rt, float spacing = 16, TextAnchor align = TextAnchor.MiddleCenter, int pad = 0, bool expandW = false)
        {
            var g = NewLayout<HorizontalLayoutGroup>(rt);
            g.spacing = spacing; g.childAlignment = align; g.padding = new RectOffset(pad, pad, pad, pad);
            g.childControlWidth = true; g.childControlHeight = true; g.childForceExpandWidth = expandW; g.childForceExpandHeight = false;
            return g;
        }

        public static VerticalLayoutGroup VBox(RectTransform rt, float spacing = 16, TextAnchor align = TextAnchor.UpperCenter, int pad = 0, bool expandH = false)
        {
            var g = NewLayout<VerticalLayoutGroup>(rt);
            g.spacing = spacing; g.childAlignment = align; g.padding = new RectOffset(pad, pad, pad, pad);
            g.childControlWidth = true; g.childControlHeight = true; g.childForceExpandWidth = true; g.childForceExpandHeight = expandH;
            return g;
        }

        public static GridLayoutGroup Grid(RectTransform rt, Vector2 cell, Vector2 spacing, int pad = 0)
        {
            var g = NewLayout<GridLayoutGroup>(rt);
            g.cellSize = cell; g.spacing = spacing; g.padding = new RectOffset(pad, pad, pad, pad);
            g.childAlignment = TextAnchor.UpperCenter;
            return g;
        }

        // ---------- widgets ----------
        public static Image Panel(Transform parent, Color color, bool roundedCorners = true, string name = "Panel")
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = roundedCorners ? Rounded : WhiteSprite;
            img.type = roundedCorners ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            img.color = color;
            return img;
        }

        /// <summary>Full-screen dim layer that blocks input below it.</summary>
        public static Image Blocker(Transform parent, float alpha = 0.6f)
        {
            var img = Panel(parent, new Color(0, 0, 0, alpha), false, "Blocker");
            Stretch(img.rectTransform);
            return img;
        }

        public static Image Image(Transform parent, Sprite sprite, Color? color = null, bool preserveAspect = true, string name = "Image")
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color ?? Color.white;
            img.preserveAspect = preserveAspect;
            img.raycastTarget = false;
            return img;
        }

        public static Text Label(Transform parent, string text, int size = 36, Color? color = null, TextAnchor align = TextAnchor.MiddleCenter,
            bool title = false, string name = "Label")
        {
            var rt = Rect(parent, name);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = title ? TitleFont : BodyFont;
            t.text = text;
            t.fontSize = size;
            t.color = color ?? Theme.Text;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = Mathf.Max(10, size / 2);
            t.resizeTextMaxSize = size;
            t.raycastTarget = false;
            if (title)
            {
                var o = rt.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0, 0, 0, 0.55f);
                o.effectDistance = new Vector2(2, -2);
            }
            return t;
        }

        public enum ButtonStyle { Primary, Secondary, Danger, Good, Plain, Dark }

        public static Color StyleColor(ButtonStyle s)
        {
            switch (s)
            {
                case ButtonStyle.Primary: return Theme.Primary;
                case ButtonStyle.Secondary: return Theme.Secondary;
                case ButtonStyle.Danger: return Theme.Danger;
                case ButtonStyle.Good: return Theme.Good;
                case ButtonStyle.Dark: return Theme.PanelDark;
                default: return Theme.PanelInner;
            }
        }

        public static Button Button(Transform parent, string text, Action onClick, ButtonStyle style = ButtonStyle.Primary, int fontSize = 40, string name = null)
        {
            var img = Panel(parent, StyleColor(style), true, name ?? ("Btn " + text));
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.8f);
            b.colors = colors;
            // drop shadow under the button
            var sh = img.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.35f);
            sh.effectDistance = new Vector2(0, -6);
            if (!string.IsNullOrEmpty(text))
            {
                var col = style == ButtonStyle.Primary ? Theme.PrimaryText : (style == ButtonStyle.Plain ? Theme.Text : Theme.TextLight);
                var l = Label(img.transform, text, fontSize, col, TextAnchor.MiddleCenter, style != ButtonStyle.Plain);
                Stretch(l.rectTransform, 16, 16, 6, 6);
                if (style == ButtonStyle.Primary) { var o = l.GetComponent<Outline>(); if (o) UnityEngine.Object.Destroy(o); }
            }
            if (onClick != null) b.onClick.AddListener(() => { Click(); onClick(); });
            img.gameObject.AddComponent<PressScale>();
            return b;
        }

        /// <summary>A square button with an icon (and optional caption under it).</summary>
        public static Button IconButton(Transform parent, Sprite icon, string caption, Action onClick, ButtonStyle style = ButtonStyle.Dark, Color? iconTint = null)
        {
            var b = Button(parent, null, onClick, style);
            var ic = Image(b.transform, icon, iconTint);
            Anchor(ic.rectTransform, 0.1f, string.IsNullOrEmpty(caption) ? 0.1f : 0.28f, 0.9f, 0.9f);
            if (!string.IsNullOrEmpty(caption))
            {
                var l = Label(b.transform, caption, 24, Theme.TextLight);
                Anchor(l.rectTransform, 0.02f, 0.02f, 0.98f, 0.3f);
            }
            return b;
        }

        public static void Click() => AudioManager.Sfx("ButtonClick", 0.8f);

        public static ScrollRect ScrollList(Transform parent, out RectTransform content, bool vertical = true, float spacing = 16, int pad = 12)
        {
            var view = Rect(parent, "Scroll");
            var bg = view.gameObject.AddComponent<Image>();
            bg.color = new Color(1, 1, 1, 0.001f);
            var sr = view.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = !vertical; sr.vertical = vertical;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.scrollSensitivity = 40;
            view.gameObject.AddComponent<RectMask2D>();
            content = Rect(view, "Content");
            if (vertical)
            {
                content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
                VBox(content, spacing, TextAnchor.UpperCenter, pad);
                var f = content.gameObject.AddComponent<ContentSizeFitter>();
                f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            else
            {
                content.anchorMin = new Vector2(0, 0); content.anchorMax = new Vector2(0, 1); content.pivot = new Vector2(0, 0.5f);
                var h = HBox(content, spacing, TextAnchor.MiddleLeft, pad);
                h.childControlHeight = true; h.childForceExpandHeight = true;
                var f = content.gameObject.AddComponent<ContentSizeFitter>();
                f.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            content.sizeDelta = Vector2.zero;
            sr.content = content;
            sr.viewport = view;
            return sr;
        }

        /// <summary>Scrollable grid of cells (shop, inventory).</summary>
        public static ScrollRect ScrollGrid(Transform parent, out RectTransform content, Vector2 cell, Vector2 spacing)
        {
            var sr = ScrollList(parent, out content, true);
            UnityEngine.Object.DestroyImmediate(content.GetComponent<VerticalLayoutGroup>());
            var g = Grid(content, cell, spacing, 12);
            g.constraint = GridLayoutGroup.Constraint.Flexible;
            return sr;
        }

        public static Slider Slider(Transform parent, float value, Action<float> onChange)
        {
            var rt = Rect(parent, "Slider");
            var s = rt.gameObject.AddComponent<Slider>();
            var bg = Panel(rt, Theme.PanelInner, true, "Bg"); Anchor(bg.rectTransform, 0, 0.3f, 1, 0.7f);
            var fillArea = Rect(rt, "FillArea"); Anchor(fillArea, 0, 0.3f, 1, 0.7f);
            var fill = Panel(fillArea, Theme.Secondary, true, "Fill"); Stretch(fill.rectTransform);
            var handleArea = Rect(rt, "HandleArea"); Stretch(handleArea, 20, 20);
            var handle = Image(handleArea, Circle, Theme.Primary, false, "Handle");
            handle.raycastTarget = true;
            handle.rectTransform.sizeDelta = new Vector2(48, 0);
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.value = value;
            if (onChange != null) s.onValueChanged.AddListener(v => onChange(v));
            return s;
        }

        public static Toggle Toggle(Transform parent, string label, bool value, Action<bool> onChange)
        {
            var rt = Rect(parent, "Toggle " + label);
            HBox(rt, 16, TextAnchor.MiddleLeft);
            var t = rt.gameObject.AddComponent<Toggle>();
            var box = Panel(rt, Theme.PanelInner, true, "Box");
            Layout(box, 64, 64);
            var check = Image(box.transform, Circle, Theme.Good, false, "Check");
            Anchor(check.rectTransform, 0.18f, 0.18f, 0.82f, 0.82f);
            var l = Label(rt, label, 36, Theme.Text, TextAnchor.MiddleLeft);
            Layout(l, 400, 64, 1);
            t.targetGraphic = box;
            t.graphic = check;
            t.isOn = value;
            if (onChange != null) t.onValueChanged.AddListener(v => { Click(); onChange(v); });
            return t;
        }

        /// <summary>CanvasScaler match: mostly height on phones; width on squarer screens (iPad 4:3) so wide rows still fit.</summary>
        public static float ScalerMatch => (float)Screen.width / Mathf.Max(1, Screen.height) < 1.5f ? 0f : 0.6f;

        public static InputField Input(Transform parent, string value, string placeholder, Action<string> onEnd)
        {
            var bg = Panel(parent, Color.white, true, "Input");
            var f = bg.gameObject.AddComponent<InputField>();
            var txt = Label(bg.transform, value, 36, Theme.Text, TextAnchor.MiddleLeft);
            txt.resizeTextForBestFit = false;
            txt.supportRichText = false;
            Stretch(txt.rectTransform, 20, 20, 4, 4);
            var ph = Label(bg.transform, placeholder, 36, Theme.Muted, TextAnchor.MiddleLeft);
            ph.fontStyle = FontStyle.Italic;
            Stretch(ph.rectTransform, 20, 20, 4, 4);
            f.textComponent = txt;
            f.placeholder = ph;
            f.text = value;
            if (onEnd != null) f.onEndEdit.AddListener(v => onEnd(v));
            return f;
        }

        /// <summary>A horizontal progress bar; returns the fill image (set fillAmount 0..1).</summary>
        public static Image Bar(Transform parent, Color fillColor, Color? bgColor = null)
        {
            var bg = Panel(parent, bgColor ?? new Color(0, 0, 0, 0.35f), true, "Bar");
            var fill = Image(bg.transform, WhiteSprite, fillColor, false, "Fill");
            fill.type = UnityEngine.UI.Image.Type.Filled;
            fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            fill.fillAmount = 1;
            Stretch(fill.rectTransform, 6, 6, 6, 6);
            return fill;
        }

        // ---------- popups ----------
        public class PopupButton
        {
            public string Text; public Action OnClick; public ButtonStyle Style;
            public PopupButton(string text, Action onClick = null, ButtonStyle style = ButtonStyle.Primary) { Text = text; OnClick = onClick; Style = style; }
        }

        /// <summary>Modal popup. Returns the content rect so callers can add more widgets. Closing destroys it.</summary>
        public static RectTransform Popup(string title, string message, params PopupButton[] buttons)
        {
            var layer = Rect(PopupLayer, "Popup " + title);
            Stretch(layer);
            Blocker(layer, 0.55f);
            var panel = Panel(layer, Theme.Panel, true, "Window");
            Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1100, 680), Vector2.zero);
            var t = Label(panel.transform, title, 64, Theme.Secondary, TextAnchor.MiddleCenter, true);
            Anchor(t.rectTransform, 0.05f, 0.8f, 0.95f, 0.97f);
            var content = Rect(panel.transform, "Content");
            Anchor(content, 0.06f, 0.24f, 0.94f, 0.8f);
            if (!string.IsNullOrEmpty(message))
            {
                var m = Label(content, message, 40, Theme.Text);
                Stretch(m.rectTransform);
            }
            var row = Rect(panel.transform, "Buttons");
            Anchor(row, 0.05f, 0.04f, 0.95f, 0.21f);
            HBox(row, 30, TextAnchor.MiddleCenter);
            if (buttons == null || buttons.Length == 0) buttons = new[] { new PopupButton("OK") };
            foreach (var pb in buttons)
            {
                var b = Button(row, pb.Text, () => { UnityEngine.Object.Destroy(layer.gameObject); pb.OnClick?.Invoke(); }, pb.Style, 40);
                Layout(b, 320, 110);
            }
            layer.gameObject.AddComponent<PopIn>().target = panel.rectTransform;
            return content;
        }

        public static void Message(string title, string message, Action ok = null) => Popup(title, message, new PopupButton("OK", ok));

        public static void Confirm(string title, string message, Action yes, Action no = null, string yesText = "Yes", string noText = "No")
            => Popup(title, message, new PopupButton(noText, no, ButtonStyle.Secondary), new PopupButton(yesText, yes));

        /// <summary>Short message that fades out at the top of the screen.</summary>
        public static void Toast(string text, Color? color = null)
        {
            var bg = Panel(PopupLayer, new Color(0, 0, 0, 0.7f), true, "Toast");
            Place(bg.rectTransform, new Vector2(0.5f, 1f), new Vector2(1100, 110), new Vector2(0, -150));
            bg.raycastTarget = false;
            var l = Label(bg.transform, text, 40, color ?? Color.white);
            Stretch(l.rectTransform, 20, 20, 4, 4);
            var f = bg.gameObject.AddComponent<FadeAndDie>();
            f.life = 2.4f;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        public static string Money(int v) => v >= 100000 ? (v / 1000) + "k" : v.ToString("N0");
    }

    /// <summary>Keeps a RectTransform inside Screen.safeArea (notches, home indicator).</summary>
    public class SafeAreaFitter : MonoBehaviour
    {
        Rect last;
        void Update()
        {
            var sa = Screen.safeArea;
            if (sa == last || Screen.width == 0) return;
            last = sa;
            var rt = (RectTransform)transform;
            rt.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
            rt.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }

    /// <summary>Little squash when a button is pressed.</summary>
    public class PressScale : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        float target = 1f;
        public void OnPointerDown(PointerEventData e) => target = 0.93f;
        public void OnPointerUp(PointerEventData e) => target = 1f;
        public void OnPointerExit(PointerEventData e) => target = 1f;
        void Update()
        {
            float s = Mathf.MoveTowards(transform.localScale.x, target, Time.unscaledDeltaTime * 2.5f);
            transform.localScale = new Vector3(s, s, 1);
        }
    }

    public class PopIn : MonoBehaviour
    {
        public RectTransform target;
        float t;
        void Update()
        {
            if (!target) return;
            t += Time.unscaledDeltaTime * 5f;
            float s = t >= 1 ? 1 : 1 + Mathf.Sin(t * Mathf.PI) * 0.08f * (1 - t) + (t - 1) * 0.2f * (1 - t);
            target.localScale = Vector3.one * Mathf.Clamp(s, 0.7f, 1.1f);
            if (t >= 1) { target.localScale = Vector3.one; enabled = false; }
        }
    }

    public class FadeAndDie : MonoBehaviour
    {
        public float life = 2f;
        public Vector2 drift = new Vector2(0, 30);
        float t;
        CanvasGroup g;
        void Start() { g = gameObject.AddComponent<CanvasGroup>(); g.blocksRaycasts = false; }
        void Update()
        {
            t += Time.unscaledDeltaTime;
            ((RectTransform)transform).anchoredPosition += drift * Time.unscaledDeltaTime;
            if (g) g.alpha = Mathf.Clamp01((life - t) / 0.5f);
            if (t >= life) Destroy(gameObject);
        }
    }
}
