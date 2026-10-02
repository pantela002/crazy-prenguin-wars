using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>Button that reports whether it is being held (walk buttons).</summary>
    public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerEnterHandler
    {
        public bool Held { get; private set; }
        int pointer = int.MinValue;

        public void OnPointerDown(PointerEventData e) { Held = true; pointer = e.pointerId; }
        public void OnPointerUp(PointerEventData e) { if (e.pointerId == pointer) { Held = false; pointer = int.MinValue; } }
        // sliding the finger off releases; sliding back on while still pressed grabs again
        public void OnPointerExit(PointerEventData e) { if (e.pointerId == pointer) Held = false; }
        public void OnPointerEnter(PointerEventData e) { if (e.pointerId == pointer && e.eligibleForClick) Held = true; }
        void OnDisable() { Held = false; pointer = int.MinValue; }
    }

    /// <summary>
    /// Runs an action when a finger lifts inside the control (with some slop). Used for HUD buttons instead of
    /// Button.onClick: uGUI drops the click when the press turns into a drag (finger jitter on high-dpi phones), when
    /// a Pulse/PressScale scale change moves the edge out from under the finger, or when the release raycast hits a
    /// panel the press itself just opened. The Button stays on the object for its tint/disabled look.
    /// </summary>
    public class TapAction : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public System.Action action;
        public float slop = 40f;          // local (canvas) units around the rect that still count as inside
        Selectable sel;
        int pointer = int.MinValue;
        float downAt;

        public static TapAction On(Component c, System.Action action)
        {
            var t = c.gameObject.GetComponent<TapAction>();
            if (t == null) t = c.gameObject.AddComponent<TapAction>();
            t.action = action;
            return t;
        }

        void Awake() => sel = GetComponent<Selectable>();

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            pointer = e.pointerId;
            downAt = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != pointer) return;
            pointer = int.MinValue;
            if (sel != null && !sel.IsInteractable()) return;
            if (Time.unscaledTime - downAt > 2f) return;      // a long hold that wandered off is not a tap
            var rt = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var lp)) return;
            var r = rt.rect;
            if (lp.x < r.xMin - slop || lp.x > r.xMax + slop || lp.y < r.yMin - slop || lp.y > r.yMax + slop) return;
            UI.Click();
            action?.Invoke();
        }

        void OnDisable() => pointer = int.MinValue;
    }

    /// <summary>
    /// Tap-outside-to-close for a dim backdrop. Only a press that starts on the backdrop itself, a moment after it
    /// appeared, closes it, so the touch that opened the panel can never close it again.
    /// </summary>
    public class BackdropCloser : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public System.Action close;
        float born;
        int pointer = int.MinValue;

        void Awake() => born = Time.unscaledTime;

        public void OnPointerDown(PointerEventData e)
        {
            pointer = Time.unscaledTime - born > 0.2f ? e.pointerId : int.MinValue;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != pointer) return;
            pointer = int.MinValue;
            close?.Invoke();
        }
    }

    /// <summary>Gentle scale pulse to draw attention (tutorial highlights).</summary>
    public class Pulse : MonoBehaviour
    {
        public bool on;
        float t;
        void Update()
        {
            if (!on) { if (t != 0) { t = 0; transform.localScale = Vector3.one; } return; }
            t += Time.unscaledDeltaTime * 5f;
            float s = 1f + Mathf.Sin(t) * 0.08f;
            transform.localScale = new Vector3(s, s, 1);
        }
    }

    /// <summary>Fades a CanvasGroup in, holds, then fades out (banners). Uses unscaled time.</summary>
    public class BannerFade : MonoBehaviour
    {
        public CanvasGroup group;
        public float hold = 1.5f;
        float t;

        public void Restart(float holdTime) { hold = holdTime; t = 0; enabled = true; if (group) group.alpha = 0; gameObject.SetActive(true); }

        void Update()
        {
            t += Time.unscaledDeltaTime;
            float a = t < 0.2f ? t / 0.2f : (t < 0.2f + hold ? 1f : 1f - (t - 0.2f - hold) / 0.4f);
            if (group) group.alpha = Mathf.Clamp01(a);
            float s = t < 0.2f ? Mathf.Lerp(1.3f, 1f, t / 0.2f) : 1f;
            transform.localScale = new Vector3(s, s, 1);
            if (t > hold + 0.6f) { gameObject.SetActive(false); enabled = false; }
        }
    }
}
