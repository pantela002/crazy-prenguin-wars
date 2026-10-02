using UnityEngine;
using UnityEngine.EventSystems;

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
