using System;
using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>Base class for a full-screen menu built from code. Build() runs once when the screen is shown.</summary>
    public abstract class UIScreen
    {
        public RectTransform Root { get; internal set; }
        /// <summary>Show the shared top bar (coins, cash, level) on this screen.</summary>
        public virtual bool ShowTopBar => true;
        /// <summary>Music Sound id to play while this screen is visible (null keeps the current one).</summary>
        public virtual string Music => "ThemeMusic";
        public abstract void Build();
        public virtual void OnShow() { }
        public virtual void OnHide() { }
        public virtual void Tick(float dt) { }
        /// <summary>Return true if the screen handled the back button itself.</summary>
        public virtual bool OnBack() => false;
    }

    /// <summary>Shows one UIScreen at a time with a back stack. Android back button goes back.</summary>
    public class ScreenManager : MonoBehaviour
    {
        public static ScreenManager I { get; private set; }
        public UIScreen Current { get; private set; }
        readonly Stack<Func<UIScreen>> history = new Stack<Func<UIScreen>>();
        Func<UIScreen> currentFactory;
        RectTransform layer;

        /// <summary>Raised whenever a screen is shown (the top bar listens to toggle itself).</summary>
        public static event Action<UIScreen> ScreenShown;

        public static void Create(Transform parent)
        {
            if (I != null) return;
            var go = new GameObject("Screens");
            go.transform.SetParent(parent, false);
            I = go.AddComponent<ScreenManager>();
            I.layer = UI.Rect(UI.Safe, "ScreenLayer");
            UI.Stretch(I.layer);
            I.layer.SetAsFirstSibling();
        }

        /// <summary>Show a screen, remembering the current one so Back() returns to it.</summary>
        public static void Show(Func<UIScreen> factory, bool addToHistory = true)
        {
            if (I == null) return;
            if (addToHistory && I.currentFactory != null) I.history.Push(I.currentFactory);
            I.Open(factory);
        }

        public static void Show<T>(bool addToHistory = true) where T : UIScreen, new() => Show(() => new T(), addToHistory);

        /// <summary>Show a screen and forget the history (e.g. going home).</summary>
        public static void Reset(Func<UIScreen> factory)
        {
            if (I == null) return;
            I.history.Clear();
            I.Open(factory);
        }

        public static void Back()
        {
            if (I == null) return;
            if (I.Current != null && I.Current.OnBack()) return;
            if (I.history.Count == 0) return;
            I.Open(I.history.Pop());
        }

        /// <summary>Hide all menus (used while a battle is running).</summary>
        public static void HideAll()
        {
            if (I == null) return;
            I.Close();
            I.currentFactory = null;
            I.history.Clear();
            ScreenShown?.Invoke(null);
        }

        void Close()
        {
            if (Current != null)
            {
                Current.OnHide();
                if (Current.Root) Destroy(Current.Root.gameObject);
                Current = null;
            }
        }

        void Open(Func<UIScreen> factory)
        {
            Close();
            currentFactory = factory;
            var s = factory();
            s.Root = UI.Rect(layer, s.GetType().Name);
            UI.Stretch(s.Root);
            Current = s;
            try { s.Build(); }
            catch (Exception e) { Debug.LogException(e); }
            if (!string.IsNullOrEmpty(s.Music)) AudioManager.Music(s.Music);
            s.OnShow();
            ScreenShown?.Invoke(s);
        }

        /// <summary>Rebuild the current screen in place (after a purchase etc.).</summary>
        public static void Refresh()
        {
            if (I == null || I.currentFactory == null) return;
            I.Open(I.currentFactory);
        }

        void Update()
        {
            Current?.Tick(Time.unscaledDeltaTime);
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                // close the top popup first
                if (UI.PopupLayer && UI.PopupLayer.childCount > 0)
                {
                    var top = UI.PopupLayer.GetChild(UI.PopupLayer.childCount - 1);
                    if (top.name.StartsWith("Popup")) { Destroy(top.gameObject); return; }
                }
                if (Current != null) Back();
            }
        }
    }
}
