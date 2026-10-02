using System;
using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// In-battle chat for online matches (original ChatLogic): quick-chat presets and short free text, profanity
    /// filtered (ProfanityFilter) when sent and again when received, plus local system lines ("X has chickened out.").
    /// Lines reach the HUD through ChatLine; the transport is IBattleNetwork.SendChat/ChatReceived.
    /// </summary>
    public partial class BattleController
    {
        public struct ChatPreset { public string id, text; }

        /// <summary>Quick-chat lines (the original only had free text; these are the remake's mobile shortcut).</summary>
        public static readonly ChatPreset[] ChatPresets =
        {
            new ChatPreset { id = "qc.hi", text = "Hi!" },
            new ChatPreset { id = "qc.gl", text = "Good luck!" },
            new ChatPreset { id = "qc.nice", text = "Nice shot!" },
            new ChatPreset { id = "qc.oops", text = "Oops!" },
            new ChatPreset { id = "qc.close", text = "So close!" },
            new ChatPreset { id = "qc.haha", text = "Ha ha!" },
            new ChatPreset { id = "qc.thanks", text = "Thanks!" },
            new ChatPreset { id = "qc.gg", text = "Good game!" },
        };

        public const int ChatMaxChars = 60;
        const float ChatMinInterval = 1.2f;     // flood guard between our own lines (seconds)

        /// <summary>A chat line to show: sender slot (-1 = system) and the final text.</summary>
        public event Action<int, string> ChatLine;
        public bool ChatAvailable => net != null && net.SupportsChat;

        readonly Queue<BattleChatMessage> netChat = new Queue<BattleChatMessage>();
        float lastChatSent = -10f;

        void OnNetChat(BattleChatMessage m) { if (m != null) lock (netLock) netChat.Enqueue(m); }

        void HookChat(bool on)
        {
            if (net == null) return;
            if (on) net.ChatReceived += OnNetChat; else net.ChatReceived -= OnNetChat;
        }

        void DrainChat()
        {
            while (true)
            {
                BattleChatMessage m;
                lock (netLock)
                {
                    if (netChat.Count == 0) return;
                    m = netChat.Dequeue();
                }
                var p = PenguinAt(m.player);
                if (p == null || p.Left) continue;
                string text = ChatText(m);
                if (text.Length > 0) ChatLine?.Invoke(m.player, text);
            }
        }

        /// <summary>Text to show for a message: a known preset or string key, else the (re-filtered) free text.</summary>
        static string ChatText(BattleChatMessage m)
        {
            if (!string.IsNullOrEmpty(m.tid))
            {
                foreach (var pr in ChatPresets) if (pr.id == m.tid) return pr.text;
                if (!m.tid.StartsWith("qc.") && Loc.Has(m.tid)) return Loc.T(m.tid);
            }
            var t = ProfanityFilter.Filter(m.text);
            return t.Length > ChatMaxChars ? t.Substring(0, ChatMaxChars) : t;
        }

        /// <summary>Send a line as the player on this device (preset id or free text). False when flood-guarded.</summary>
        public bool SendChat(string presetId, string freeText)
        {
            if (!ChatAvailable || CurrentPhase == Phase.Over) return false;
            if (Time.unscaledTime - lastChatSent < ChatMinInterval) return false;
            var m = new BattleChatMessage { player = net.LocalSlot, tid = presetId ?? "" };
            if (string.IsNullOrEmpty(m.tid))
            {
                m.text = ProfanityFilter.Filter(freeText);
                if (m.text.Length > ChatMaxChars) m.text = m.text.Substring(0, ChatMaxChars);
                if (m.text.Trim().Length == 0) return false;
            }
            else foreach (var pr in ChatPresets) if (pr.id == m.tid) m.text = pr.text;   // older clients show the text
            lastChatSent = Time.unscaledTime;
            net.SendChat(m);
            ChatLine?.Invoke(m.player, ChatText(m));
            return true;
        }

        /// <summary>A local system line (player left, disconnected...).</summary>
        public void SystemChat(string text)
        {
            if (!string.IsNullOrEmpty(text)) ChatLine?.Invoke(-1, text);
        }
    }
}
