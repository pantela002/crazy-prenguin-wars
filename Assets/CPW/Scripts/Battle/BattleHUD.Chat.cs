using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// In-battle chat for online matches (original ChatElementScreen: a 4-line log, scroll buttons, an input line).
    /// Mobile version: a chat button top-right opens a panel with the full history, quick-chat presets and a short
    /// text field; the last lines show in a small fading log on the left (touch-transparent so aiming still works),
    /// and every player line also pops up as a speech bubble over the sender's penguin. System lines are grey.
    /// </summary>
    public partial class BattleHUD
    {
        const int ChatLogLines = 4, ChatHistoryMax = 40;
        const float ChatLogHold = 8f, BubbleTime = 4f;

        class Bubble { public RectTransform rt; public Text text; public float until; }

        Button chatBtn;
        GameObject chatPanel;
        RectTransform chatLog, chatHistoryContent;
        CanvasGroup chatLogGroup;
        ScrollRect chatHistoryScroll;
        InputField chatInput;
        readonly List<Text> chatLogTexts = new List<Text>();
        readonly List<string> chatHistory = new List<string>();
        readonly Dictionary<int, Bubble> bubbles = new Dictionary<int, Bubble>();
        float chatLogTimer;
        int unreadChat;
        Text chatBadge;
        bool chatHooked;

        void BuildChat()
        {
            if (!c.ChatAvailable) return;
            chatBtn = HudButton(safe, null, "Chat", OpenChat, UI.ButtonStyle.Secondary, new Vector2(1, 1), new Vector2(112, 112), new Vector2(-276, -20), 34);
            chatBadge = UI.Label(chatBtn.transform, "", 26, Color.white, TextAnchor.MiddleCenter, true);
            var badgeBg = UI.Image(chatBtn.transform, UI.Circle, Theme.Danger, false, "Badge");
            UI.Place(badgeBg.rectTransform, new Vector2(1, 1), new Vector2(44, 44), new Vector2(8, 8));
            chatBadge.transform.SetParent(badgeBg.transform, false);
            UI.Stretch(chatBadge.rectTransform);
            badgeBg.gameObject.SetActive(false);

            // log under the scoreboard (and the earnings counter when there is one)
            float y = 20 + 20 + c.Penguins.Count * 62 + 10 + (earnings != null ? 64 + 10 : 0);
            chatLog = UI.Rect(safe, "ChatLog");
            UI.Place(chatLog, new Vector2(0, 1), new Vector2(560, ChatLogLines * 44 + 12), new Vector2(20, -y));
            chatLogGroup = chatLog.gameObject.AddComponent<CanvasGroup>();
            chatLogGroup.blocksRaycasts = false;
            chatLogGroup.interactable = false;
            chatLogGroup.alpha = 0;
            var bg = UI.Panel(chatLog, new Color(0, 0, 0, 0.4f), true, "Bg");
            UI.Stretch(bg.rectTransform);
            for (int i = 0; i < ChatLogLines; i++)
            {
                var t = UI.Label(chatLog, "", 26, Color.white, TextAnchor.MiddleLeft);
                t.resizeTextForBestFit = false;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                UI.Place(t.rectTransform, new Vector2(0, 1), new Vector2(536, 44), new Vector2(12, -6 - i * 44));
                chatLogTexts.Add(t);
            }
            foreach (var g in chatLog.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;

            c.ChatLine += OnChatLine;
            chatHooked = true;
        }

        void UnhookChat()
        {
            if (chatHooked && c != null) c.ChatLine -= OnChatLine;
            chatHooked = false;
        }

        static string Hex(Color col) => ColorUtility.ToHtmlStringRGB(col);

        static string Clean(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace('<', '(').Replace('>', ')');

        void OnChatLine(int player, string text)
        {
            var p = c.PenguinAt(player);
            string line = p == null
                ? "<color=#" + Hex(new Color(0.75f, 0.8f, 0.9f)) + "><i>" + Clean(text) + "</i></color>"
                : "<color=#" + Hex(p.TeamColor) + "><b>" + Clean(p.DisplayName) + "</b></color>: " + Clean(text);
            chatHistory.Add(line);
            if (chatHistory.Count > ChatHistoryMax) chatHistory.RemoveAt(0);

            // log: newest at the bottom
            int start = Mathf.Max(0, chatHistory.Count - ChatLogLines);
            for (int i = 0; i < ChatLogLines; i++)
            {
                int k = start + i;
                chatLogTexts[i].text = k < chatHistory.Count ? chatHistory[k] : "";
            }
            chatLogTimer = ChatLogHold;
            if (chatPanel) AppendHistoryLine(line);
            else if (p != null && !c.IsLocalHuman(player)) unreadChat++;
            if (p != null) ShowBubble(p, text);
            if (p != null && !chatPanel) AudioManager.Sfx("ButtonClick", 0.4f);
        }

        void ShowBubble(Penguin p, string text)
        {
            if (!bubbles.TryGetValue(p.PlayerIndex, out var b) || b.rt == null)
            {
                b = new Bubble();
                var panel = UI.Panel(tagsLayer, new Color(1, 1, 1, 0.95f), true, "Bubble " + p.PlayerIndex);
                panel.raycastTarget = false;
                b.rt = panel.rectTransform;
                b.rt.pivot = new Vector2(0.5f, 0f);
                b.rt.anchorMin = b.rt.anchorMax = new Vector2(0.5f, 0.5f);
                b.text = UI.Label(b.rt, "", 28, Theme.Text, TextAnchor.MiddleCenter);
                b.text.raycastTarget = false;
                UI.Stretch(b.text.rectTransform, 14, 14, 6, 6);
                var tail = UI.Image(b.rt, UI.Circle, new Color(1, 1, 1, 0.95f), false, "Tail");
                UI.Place(tail.rectTransform, new Vector2(0.5f, 0), new Vector2(22, 22), new Vector2(0, -12));
                bubbles[p.PlayerIndex] = b;
            }
            b.text.text = text;
            // width from the text length, two lines at most
            float w = Mathf.Clamp(text.Length * 15f + 40f, 140f, 460f);
            b.rt.sizeDelta = new Vector2(w, text.Length > 28 ? 92 : 58);
            b.until = Time.unscaledTime + BubbleTime;
            b.rt.gameObject.SetActive(true);
        }

        void UpdateChat(float dt)
        {
            if (chatLog == null) return;
            if (chatLogTimer > 0)
            {
                chatLogTimer -= dt;
                chatLogGroup.alpha = Mathf.Clamp01(chatLogTimer / 1.2f);
            }
            else if (chatLogGroup.alpha != 0) chatLogGroup.alpha = 0;

            // unread badge on the chat button
            var badge = chatBadge.transform.parent.gameObject;
            bool showBadge = unreadChat > 0 && !chatPanel;
            if (badge.activeSelf != showBadge) badge.SetActive(showBadge);
            if (showBadge) chatBadge.text = unreadChat > 9 ? "9+" : Num(unreadChat);

            // bubbles follow their penguins (above the name tag), clamped to the screen
            float lift = BattleRules.Radius * 2.8f;
            foreach (var kv in bubbles)
            {
                var b = kv.Value;
                if (b.rt == null || !b.rt.gameObject.activeSelf) continue;
                var p = c.PenguinAt(kv.Key);
                if (p == null || !p.Alive || Time.unscaledTime > b.until || c.CurrentPhase == BattleController.Phase.Over)
                {
                    b.rt.gameObject.SetActive(false);
                    continue;
                }
                Vector2 sp = cam.WorldToScreen((Vector2)p.transform.position + Vector2.up * lift);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(tagsLayer, sp, null, out var lp);
                var r = tagsLayer.rect;
                float hw = b.rt.sizeDelta.x * 0.5f;
                lp.x = Mathf.Clamp(lp.x, r.xMin + hw + 8, r.xMax - hw - 8);
                lp.y = Mathf.Clamp(lp.y + 90f, r.yMin + 8, r.yMax - b.rt.sizeDelta.y - 8);
                b.rt.anchoredPosition = lp;
            }
        }

        // ---------------------------------------------------------------- panel

        void OpenChat()
        {
            if (!c.ChatAvailable || c.CurrentPhase == BattleController.Phase.Over) return;
            CloseAllPanels();
            unreadChat = 0;
            var win = Window("Chat", new Vector2(1300, 820), out chatPanel, CloseChatPanel);

            var hist = UI.Rect(win, "History");
            UI.Anchor(hist, 0.03f, 0.5f, 0.97f, 0.87f);
            var histBg = UI.Panel(hist, new Color(0, 0, 0, 0.3f), true, "Bg");
            UI.Stretch(histBg.rectTransform);
            chatHistoryScroll = UI.ScrollList(hist, out chatHistoryContent, true, 4, 10);
            UI.Stretch((RectTransform)chatHistoryScroll.transform);
            var vb = chatHistoryContent.GetComponent<VerticalLayoutGroup>();
            if (vb) vb.childAlignment = TextAnchor.UpperLeft;
            if (chatHistory.Count == 0) AppendHistoryLine("<i>Say hi to the other penguins!</i>");
            foreach (var l in chatHistory) AppendHistoryLine(l);

            // quick chat
            var grid = UI.Rect(win, "Presets");
            UI.Anchor(grid, 0.03f, 0.21f, 0.97f, 0.48f);
            var g = UI.Grid(grid, new Vector2(290, 92), new Vector2(14, 14));
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 4;
            g.childAlignment = TextAnchor.MiddleCenter;
            foreach (var pr in BattleController.ChatPresets)
            {
                string id = pr.id;
                TapButton(grid, pr.text, () => SendChatLine(id, null), UI.ButtonStyle.Secondary, 32);
            }

            // short free text (profanity filtered); Send or the keyboard's Done sends it
            var row = UI.Rect(win, "Input");
            UI.Anchor(row, 0.03f, 0.03f, 0.97f, 0.18f);
            chatInput = UI.Input(row, "", Loc.Has("TID_CHAT_DEFAULT_INPUT") ? Loc.T("TID_CHAT_DEFAULT_INPUT") : "Write here to chat!", null);
            chatInput.characterLimit = BattleController.ChatMaxChars;
            chatInput.lineType = InputField.LineType.SingleLine;
            chatInput.onEndEdit.AddListener(v =>
            {
                var k = chatInput != null ? chatInput.touchScreenKeyboard : null;
                bool done = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
                            || (k != null && k.status == TouchScreenKeyboard.Status.Done);
                if (done && !string.IsNullOrEmpty(v)) SendChatLine(null, v);
            });
            UI.Anchor((RectTransform)chatInput.transform, 0f, 0f, 0.76f, 1f);
            var send = TapButton(row, "Send", () => { if (chatInput != null) SendChatLine(null, chatInput.text); }, UI.ButtonStyle.Primary, 40);
            UI.Anchor((RectTransform)send.transform, 0.78f, 0f, 1f, 1f);
            ScrollHistoryToEnd();
        }

        void AppendHistoryLine(string line)
        {
            if (chatHistoryContent == null) return;
            var t = UI.Label(chatHistoryContent, line, 30, Color.white, TextAnchor.MiddleLeft);
            t.resizeTextForBestFit = false;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var fit = t.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UI.Layout(t, -1, -1, 1);
            while (chatHistoryContent.childCount > ChatHistoryMax + 1) Destroy(chatHistoryContent.GetChild(0).gameObject);
            ScrollHistoryToEnd();
        }

        void ScrollHistoryToEnd()
        {
            if (chatHistoryScroll == null) return;
            Canvas.ForceUpdateCanvases();
            chatHistoryScroll.verticalNormalizedPosition = 0f;
        }

        void SendChatLine(string presetId, string text)
        {
            if (string.IsNullOrEmpty(presetId) && (text == null || text.Trim().Length == 0)) return;
            if (!c.SendChat(presetId, text)) { UI.Toast("Wait a moment before chatting again"); return; }
            if (chatInput != null) chatInput.text = "";
            CloseChatPanel();
        }

        void CloseChatPanel()
        {
            if (chatPanel) Destroy(chatPanel);
            chatPanel = null;
            chatHistoryContent = null;
            chatHistoryScroll = null;
            chatInput = null;
        }
    }
}
