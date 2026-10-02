using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// Friends and inbox (original FriendsElementScreen / NeighborsScreen / GiftScreen / InboxScreen). Friends are added
    /// with the friend code from their profile; each friend can get one free gift a day; the inbox holds gifts,
    /// private game invites (Join opens the lobby with the code) and "added you" notices. Needs the online service:
    /// offline it explains that and offers to try connecting.
    /// </summary>
    public class FriendsScreen : MetaScreen
    {
        static int tab;
        bool alive;
        List<Button> tabs;
        RectTransform list, side;
        Text status;
        string codeInput = "";
        protected override string Title => Loc.T("BUTTON_NEIGHBORS");

        public FriendsScreen() { }
        public FriendsScreen(int startTab) { tab = startTab; }

        protected override void BuildContent()
        {
            alive = true;
            var row = UI.Rect(Content, "Tabs");
            UI.Anchor(row, 0, 0.89f, 0.62f, 1);
            tabs = MetaUI.Tabs(row, new[] { Loc.T("BUTTON_NEIGHBORS"), Loc.T("BUTTON_INBOX") }, tab, i => { tab = i; MetaUI.SetTabSelected(tabs, i); Fill(); }, 36);
            status = UI.Label(Content, "", 28, Color.white, TextAnchor.MiddleRight);
            UI.Anchor(status.rectTransform, 0.62f, 0.89f, 1, 1);

            var panel = MetaUI.CardPanel(Content, MetaUI.Card);
            UI.Anchor(panel.rectTransform, 0, 0, 0.64f, 0.87f);
            var sr = UI.ScrollList(panel.transform, out list, true, 8, 12);
            UI.Stretch((RectTransform)sr.transform, 4, 4, 4, 4);

            var sp = MetaUI.CardPanel(Content, MetaUI.CardDark);
            UI.Anchor(sp.rectTransform, 0.66f, 0, 1, 0.87f);
            side = sp.rectTransform;
            UI.VBox(side, 14, TextAnchor.UpperCenter, 26).childForceExpandHeight = false;

            if (!Social.Available) { BuildOffline(); return; }
            BuildSide();
            Fill();
        }

        public override void OnHide() => alive = false;

        // ------------------------------------------------------------------ offline

        void BuildOffline()
        {
            status.text = "";
            var l = UI.Label(list, "Friends, gifts and invites need the online service.\n\nConnect to play online: check your internet connection" +
                                   (Online.Service is OfflineService ? " (this build has no Firebase project, see Settings)." : ".") + "\n\n" + Online.Service.Status, 34, Theme.Muted);
            UI.Layout(l, -1, 360);
            var t = UI.Label(side, "Offline", 48, MetaUI.Gold, TextAnchor.MiddleCenter, true);
            UI.Layout(t, -1, 80);
            var info = UI.Label(side, "Your friend code shows here once you're online. Share it so friends can add you, send gifts and invite you to private games.", 30, Theme.Muted);
            UI.Layout(info, -1, 300);
            if (Online.Service is OfflineService) return;
            var retry = UI.Button(side, "Try again", () =>
            {
                status.text = "Connecting...";
                Online.Service.Init(ok => { if (alive) ScreenManager.Refresh(); });
            }, UI.ButtonStyle.Primary, 40);
            UI.Layout(retry, -1, 110);
        }

        // ------------------------------------------------------------------ side panel: my code + add

        void BuildSide()
        {
            var head = UI.Label(side, "Your friend code", 34, Color.white, TextAnchor.MiddleCenter, true);
            UI.Layout(head, -1, 50);
            var box = UI.Panel(side, Theme.Panel, true, "Code");
            UI.Layout(box, -1, 110);
            var code = UI.Label(box.transform, "...", 76, Theme.Secondary, TextAnchor.MiddleCenter, true);
            UI.Stretch(code.rectTransform, 8, 8, 4, 4);
            Social.GetMyCode(c => { if (code) code.text = string.IsNullOrEmpty(c) ? "?" : c; });
            var hint = UI.Label(side, "Tell it to your friends so they can add you.", 26, new Color(1, 1, 1, 0.8f));
            UI.Layout(hint, -1, 70);

            var add = UI.Label(side, "Add a friend", 34, Color.white, TextAnchor.MiddleLeft, true);
            UI.Layout(add, -1, 50);
            var input = UI.Input(side, codeInput, "FRIEND CODE", v => codeInput = v);
            input.characterLimit = 8;
            input.characterValidation = InputField.CharacterValidation.Alphanumeric;
            input.onValueChanged.AddListener(v => codeInput = v);
            UI.Layout(input, -1, 96);
            Button addBtn = null;
            addBtn = UI.Button(side, "Add", () =>
            {
                addBtn.interactable = false;
                Social.AddFriendByCode(codeInput, (f, err) =>
                {
                    if (!alive) return;
                    if (addBtn) addBtn.interactable = true;
                    if (err != null) { UI.Message("Can't add friend", err); return; }
                    codeInput = "";
                    if (input) input.text = "";
                    UI.Toast(f.name + " is now your friend!", Theme.Good);
                    tab = 0;
                    MetaUI.SetTabSelected(tabs, 0);
                    Fill();
                });
            }, UI.ButtonStyle.Good, 40);
            UI.Layout(addBtn, -1, 100);
            var playW = UI.Button(side, "Play with friends", PlayScreen.OpenOnline, UI.ButtonStyle.Primary, 36);
            UI.Layout(playW, -1, 100);
        }

        // ------------------------------------------------------------------ lists

        void Fill()
        {
            if (!alive || list == null || !Social.Available) return;
            UI.Clear(list);
            status.text = "Loading...";
            var l = UI.Label(list, "Loading...", 34, Theme.Muted);
            UI.Layout(l, -1, 100);
            if (tab == 0) Social.LoadFriends(ShowFriends);
            else Social.LoadInbox(ShowInbox);
        }

        void Empty(string text)
        {
            var l = UI.Label(list, text, 34, Theme.Muted);
            UI.Layout(l, -1, 180);
        }

        void ShowFriends(List<FriendInfo> friends, string err)
        {
            if (!alive || tab != 0 || !list) return;
            UI.Clear(list);
            int online = 0;
            foreach (var f in friends) if (f.online) online++;
            status.text = err != null ? "" : friends.Count + " friends, " + online + " online";
            if (err != null) { Empty("Could not load your friends.\n" + err); return; }
            if (friends.Count == 0) { Empty("No friends yet.\nAdd one with their friend code, or share yours!"); return; }
            foreach (var f in friends) FriendRow(f);
        }

        void FriendRow(FriendInfo f)
        {
            var row = UI.Panel(list, Theme.PanelInner, true, "Friend");
            UI.Layout(row, -1, 100);
            var dot = UI.Image(row.transform, UI.Circle, f.online ? Theme.Good : Theme.Muted, false, "Online");
            UI.Place(dot.rectTransform, new Vector2(0, 0.5f), new Vector2(30, 30), new Vector2(22, 0));
            var n = UI.Label(row.transform, f.name, 36, Theme.Text, TextAnchor.MiddleLeft, true);
            UI.Anchor(n.rectTransform, 0.07f, 0.4f, 0.55f, 1);
            var info = UI.Label(row.transform, "Level " + f.level + "   " + (f.online ? "online" : "away"), 26, Theme.Muted, TextAnchor.MiddleLeft);
            UI.Anchor(info.rectTransform, 0.07f, 0, 0.55f, 0.45f);
            bool can = Social.CanGiftToday(f);
            Button gift = null;
            gift = UI.Button(row.transform, can ? Loc.T("BUTTON_GIFTS") : "Sent today", () => PickGift(f, gift), UI.ButtonStyle.Good, 30);
            gift.interactable = can;
            UI.Anchor((RectTransform)gift.transform, 0.56f, 0.14f, 0.82f, 0.86f);
            var rm = UI.Button(row.transform, "X", () => UI.Confirm("Remove friend?", "Remove " + f.name + " from your friends?", () =>
                Social.RemoveFriend(f.uid, ok => { if (ok) Fill(); else UI.Toast("Could not remove the friend."); }), null, "Remove", "Keep"),
                UI.ButtonStyle.Danger, 32);
            UI.Anchor((RectTransform)rm.transform, 0.85f, 0.14f, 0.97f, 0.86f);
        }

        /// <summary>Choose one of the Gift items and send it (one gift per friend per day).</summary>
        void PickGift(FriendInfo f, Button source)
        {
            var pool = Social.GiftPool();
            if (pool.Count == 0) { UI.Toast("No gifts available."); return; }
            var win = MetaUI.Window("Send " + f.name + " a free gift", new Vector2(1200, 560), out var layer);
            var row = UI.Rect(win, "Gifts");
            UI.Stretch(row, 30, 30, 140, 40);
            UI.HBox(row, 20, TextAnchor.MiddleCenter);
            foreach (var g in pool)
            {
                var gi = g;
                var b = UI.Button(row, null, () =>
                {
                    MetaUI.Close(layer);
                    Social.SendGift(f, gi, err =>
                    {
                        if (err != null) { UI.Message("Gift not sent", err); return; }
                        UI.Toast("Gift sent to " + f.name + "!", Theme.Good);
                        if (source)
                        {
                            source.interactable = false;
                            var t = source.GetComponentInChildren<Text>();
                            if (t) t.text = "Sent today";
                        }
                    });
                }, UI.ButtonStyle.Plain, 24, "Gift " + gi.id);
                UI.Layout(b, 200, 280);
                var tile = MetaUI.IconTile(MetaUI.Box(b.transform, 0.08f, 0.32f, 0.92f, 0.95f), Progression.IconOf(gi.id), Progression.NameOf(gi.id));
                MetaUI.Square(tile);
                var l = UI.Label(b.transform, gi.amount + "x " + Progression.NameOf(gi.id), 26, Theme.Text);
                UI.Anchor(l.rectTransform, 0.04f, 0.02f, 0.96f, 0.3f);
            }
        }

        void ShowInbox(List<InboxItem> items, string err)
        {
            if (!alive || tab != 1 || !list) return;
            UI.Clear(list);
            status.text = err != null ? "" : items.Count + " messages";
            if (err != null) { Empty("Could not load your inbox.\n" + err); return; }
            if (items.Count == 0) { Empty("Your inbox is empty."); return; }
            foreach (var it in items) InboxRow(it);
        }

        void InboxRow(InboxItem it)
        {
            var row = UI.Panel(list, Theme.PanelInner, true, "Message");
            UI.Layout(row, -1, 110);
            string text, action;
            string icon = null;
            if (it.IsGift) { text = it.fromName + " sent you " + it.amount + "x " + Progression.NameOf(it.item) + "!"; action = "Open"; icon = Progression.IconOf(it.item); }
            else if (it.IsInvite) { text = it.fromName + " invites you to a private game (code " + it.code + ")."; action = "Join"; icon = "Ui/online"; }
            else { text = it.fromName + " added you as a friend."; action = "Add back"; icon = "Ui/star"; }
            var tile = MetaUI.IconTile(MetaUI.Box(row.transform, 0.01f, 0.08f, 0.11f, 0.92f), icon, it.IsGift ? Progression.NameOf(it.item) : it.fromName);
            MetaUI.Square(tile);
            var l = UI.Label(row.transform, text, 30, Theme.Text, TextAnchor.MiddleLeft);
            UI.Anchor(l.rectTransform, 0.12f, 0, 0.6f, 1);
            Button act = null;
            act = UI.Button(row.transform, action, () =>
            {
                act.interactable = false;
                if (it.IsGift)
                    Social.AcceptGift(it, e =>
                    {
                        if (e != null) { UI.Toast(e); }
                        else { AudioManager.Sfx("Buy"); UI.Toast("+" + it.amount + " " + Progression.NameOf(it.item), Theme.Good); }
                        Fill();
                    });
                else if (it.IsInvite)
                    Social.Dismiss(it, _ =>
                    {
                        OnlineLobbyScreen.PendingJoinCode = it.code;
                        ScreenManager.Show(() => new OnlineLobbyScreen());
                    });
                else
                    Social.AddFriend(it.from, it.fromName, false, (f, e) =>
                    {
                        if (e != null) { UI.Toast(e); if (act) act.interactable = true; return; }
                        Social.Dismiss(it, _ => Fill());
                        UI.Toast(f.name + " is now your friend!", Theme.Good);
                    });
            }, UI.ButtonStyle.Good, 32);
            UI.Anchor((RectTransform)act.transform, 0.61f, 0.14f, 0.84f, 0.86f);
            var x = UI.Button(row.transform, "X", () => Social.Dismiss(it, _ => Fill()), UI.ButtonStyle.Danger, 32);
            UI.Anchor((RectTransform)x.transform, 0.86f, 0.14f, 0.97f, 0.86f);
        }
    }
}
