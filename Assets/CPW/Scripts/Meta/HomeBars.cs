using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>
    /// The home screen's header (original TopLeft / TopRight / Level / MoneyResource element screens) and bottom strip
    /// (FriendsElementScreen with FriendSlots, Invite and the slot machine). With the original art (UI.Skin) the bars,
    /// panels, logo, nav icons, level star, coin/cash, friend tiles, arrows, slots machine and gear are the original
    /// home_screen bitmaps; every text stays uGUI so nothing shows the renders' placeholder "Nudge" text.
    /// </summary>
    public partial class HomeScreen
    {
        // StatsGap: the stats panels' content starts below the bar and its 5-unit edge (the bar is drawn in front of them)
        const float BarHeight = 106, StatsGap = 6, StatsHeight = 104, HeaderHeight = BarHeight + StatsGap + StatsHeight;
        const float StripHeight = 150, SlotTileHeight = 196, GearTop = SlotTileHeight + 12 + 92;
        const int FriendSlots = 5;

        static readonly Color BarBlue = new Color32(30, 108, 200, 255);
        static readonly Color BarDark = new Color32(14, 58, 128, 255);
        static readonly Color PanelBlue = new Color32(40, 128, 214, 235);
        static readonly Color AddOrange = new Color32(255, 160, 24, 255);
        static readonly Color AddGreen = new Color32(96, 190, 40, 255);
        static readonly Color SlotBlue = new Color32(210, 234, 252, 255);
        static readonly Color SlotPurple = new Color32(150, 70, 200, 255);

        Text levelText, xpText, coinText, cashText;
        Image xpFill;
        int shownLevel = -1, shownXp = -1, shownCoins = -1, shownCash = -1;
        float headerTimer;

        RectTransform friendRow;
        readonly List<FriendInfo> friends = new List<FriendInfo>();
        int friendPage;

        // ------------------------------------------------------------------ header

        void BuildHeader()
        {
            // ---- blue top bar: logo, Gifts / Membership / Friends / Help, Inbox ----
            var bar = UI.Panel(Root, BarDark, false, "HomeBar");
            var brt = bar.rectTransform;
            brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(1, 1); brt.pivot = new Vector2(0.5f, 1);
            brt.offsetMin = new Vector2(-400, -BarHeight); brt.offsetMax = new Vector2(400, 300);   // runs under the notch / status bar
            var band = UI.Skin.Get("topbar");
            if (band != null)
            {
                // the original hud top_bar band; the part under the notch is its deep blue
                bar.color = new Color32(0, 86, 160, 255);
                var art = UI.Image(bar.transform, band, Color.white, false, "Band");
                art.rectTransform.anchorMin = new Vector2(0, 0); art.rectTransform.anchorMax = new Vector2(1, 0);
                art.rectTransform.pivot = new Vector2(0.5f, 0);
                art.rectTransform.sizeDelta = new Vector2(0, BarHeight);
                art.rectTransform.anchoredPosition = Vector2.zero;
                var edge = UI.Image(bar.transform, UI.WhiteSprite, new Color32(8, 40, 86, 255), false, "Edge");
                edge.rectTransform.anchorMin = new Vector2(0, 0); edge.rectTransform.anchorMax = new Vector2(1, 0);
                edge.rectTransform.pivot = new Vector2(0.5f, 1); edge.rectTransform.sizeDelta = new Vector2(0, 5);
            }
            else
            {
                var face = UI.Panel(bar.transform, BarBlue, false, "Face");
                UI.Stretch(face.rectTransform, 0, 0, 0, 6);
                var shine = UI.Panel(face.transform, new Color(1, 1, 1, 0.12f), false, "Shine");
                shine.rectTransform.anchorMin = new Vector2(0, 0); shine.rectTransform.anchorMax = new Vector2(1, 0);
                shine.rectTransform.pivot = new Vector2(0.5f, 0); shine.rectTransform.sizeDelta = new Vector2(0, BarHeight * 0.45f);
                shine.rectTransform.anchoredPosition = new Vector2(0, BarHeight * 0.45f);
            }

            var top = UI.Rect(Root, "TopRow");
            top.anchorMin = new Vector2(0, 1); top.anchorMax = new Vector2(1, 1); top.pivot = new Vector2(0.5f, 1);
            top.offsetMin = new Vector2(Edge, -BarHeight); top.offsetMax = new Vector2(-Edge, 0);

            // text logo in the game's two-tone style
            var logo = UI.Rect(top, "Logo");
            UI.Place(logo, new Vector2(0, 0.5f), new Vector2(320, BarHeight), new Vector2(0, 0));
            var logoArt = UI.Skin.OriginalIcon("Ui/logo");
            if (logoArt != null)
            {
                // the original Crazy Penguin Wars logo; it hangs a little below the bar like the original but never
                // above it: the bar's top is the safe area's top edge, so anything higher is cut by the screen
                var li = UI.Image(logo, logoArt, Color.white, true, "Art");
                UI.Stretch(li.rectTransform, 0, 0, 4, -6);
            }
            else BuildTextLogo(logo);

            var nav = UI.Rect(top, "Nav");
            nav.anchorMin = new Vector2(0, 0); nav.anchorMax = new Vector2(1, 1);
            nav.offsetMin = new Vector2(340, 0); nav.offsetMax = new Vector2(-160, 0);
            var h = UI.HBox(nav, 8, TextAnchor.MiddleLeft);
            h.childControlWidth = false; h.childControlHeight = false;
            AddBadge(NavButton(nav, "Ui/gift", MetaUI.TOr("BUTTON_GIFTS", "Gifts"), () => ScreenManager.Show(() => new DailyScreen())).transform,
                () => DailyScreen.CanClaim ? 1 : 0, new Vector2(-14, -2));
            NavButton(nav, "Ui/vip", "Membership", () => ScreenManager.Show(() => new VipScreen()));
            NavButton(nav, "Ui/online", Loc.T("BUTTON_NEIGHBORS"), () => ScreenManager.Show(() => new FriendsScreen(0)));
            NavButton(nav, "Ui/tutorial", "Help", () => ScreenManager.Show(() => new HelpScreen()));

            bool mailbox = UI.Skin.OriginalIcon("Ui/inbox") != null;
            var inbox = NavButton(top, mailbox ? "Ui/inbox" : null, Loc.T("BUTTON_INBOX"), () => ScreenManager.Show(() => new FriendsScreen(1)));
            UI.Place((RectTransform)inbox.transform, new Vector2(1, 0.5f), new Vector2(140, BarHeight), Vector2.zero);
            if (!mailbox) Envelope(inbox.transform.Find("Icon"));
            AddBadge(inbox.transform, () => Social.InboxCount, new Vector2(-14, -2));

            // ---- stats row: level star + XP (left), Cash and coins with Add (right) ----
            var lp = Hanging(new Vector2(0, 1), new Vector2(Edge - 6, -BarHeight - StatsGap), new Vector2(560, StatsHeight));
            var lvlBtn = lp.gameObject.AddComponent<Button>();
            lvlBtn.onClick.AddListener(() => { UI.Click(); ScreenManager.Show(() => new ProfileScreen()); });
            var starArt = UI.Skin.Icon("Ui/star");
            var star = UI.Image(lp, starArt ?? UI.Circle, starArt != null ? Color.white : MetaUI.Purple, true, "Star");
            UI.Place(star.rectTransform, new Vector2(0, 0.5f), new Vector2(124, 124), new Vector2(-14, -6));
            levelText = UI.Label(star.transform, "1", 44, Color.white, TextAnchor.MiddleCenter, true);
            UI.Stretch(levelText.rectTransform, 14, 14, 26, 18);
            MetaUI.Outlined(levelText, new Color32(110, 50, 140, 255), 3f);
            var barHost = UI.Rect(lp, "XpBar");
            barHost.anchorMin = new Vector2(0, 0.5f); barHost.anchorMax = new Vector2(1, 0.5f);
            barHost.offsetMin = new Vector2(122, 4); barHost.offsetMax = new Vector2(-18, 40);
            xpFill = UI.Bar(barHost, new Color32(206, 120, 240, 255), new Color32(16, 64, 140, 255));
            UI.Stretch((RectTransform)xpFill.transform.parent);
            var pink = UI.Skin.Get("bar.xp");
            if (pink != null) { xpFill.sprite = pink; xpFill.color = Color.white; }   // the original pink XP fill
            xpText = UI.Label(lp, "", 26, Color.white, TextAnchor.MiddleLeft, true);
            xpText.rectTransform.anchorMin = new Vector2(0, 0); xpText.rectTransform.anchorMax = new Vector2(1, 0.5f);
            xpText.rectTransform.offsetMin = new Vector2(126, 6); xpText.rectTransform.offsetMax = new Vector2(-18, -4);
            MetaUI.Outlined(xpText, BarDark, 2f);

            var rp = Hanging(new Vector2(1, 1), new Vector2(-(Edge - 6), -BarHeight - StatsGap), new Vector2(590, StatsHeight));
            coinText = Money(rp, "coin", 0, AddOrange);
            cashText = Money(rp, "cash", 1, AddGreen);

            // the stats panels tuck UNDER the bar: their art reaches ~26 px up into it, so drawn on top they covered the
            // bottom of the logo and the Gifts / Membership / Inbox captions sitting over them
            bar.transform.SetAsLastSibling();
            top.SetAsLastSibling();

            // ---- featured supplies between the panels (the original showed boosts here); tap opens the shop ----
            var featured = ItemCatalog.Featured();
            var fr = UI.Rect(Root, "Featured");
            fr.anchorMin = new Vector2(0.5f, 1); fr.anchorMax = new Vector2(0.5f, 1); fr.pivot = new Vector2(0.5f, 1);
            fr.sizeDelta = new Vector2(4 * 96, 90);
            fr.anchoredPosition = new Vector2(0, -BarHeight - 8);
            UI.HBox(fr, 8, TextAnchor.MiddleCenter).childControlWidth = true;
            for (int i = 0; i < featured.Count && i < 4; i++)
            {
                var r = featured[i];
                var b = UI.Button(fr, null, () => ScreenManager.Show(() => new ShopScreen(0)), UI.ButtonStyle.Plain, 20, "Featured " + r.Id);
                b.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);
                foreach (var s in b.GetComponents<Shadow>()) UnityEngine.Object.Destroy(s);
                UI.Layout(b, 88, 88);
                var tile = MetaUI.IconTile(b.transform, ItemCatalog.IconPath(r), ItemCatalog.Name(r), null, false);
                UI.Stretch(tile);
                var sh = tile.gameObject.AddComponent<Shadow>();
                sh.effectColor = new Color(0, 0, 0, 0.3f); sh.effectDistance = new Vector2(0, -4);
            }
        }

        /// <summary>Top bar entry: icon with an outlined caption under it.</summary>
        Button NavButton(RectTransform parent, string icon, string caption, Action onClick)
        {
            var b = UI.Button(parent, null, onClick, UI.ButtonStyle.Plain, 24, "Nav " + caption);
            b.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);   // invisible but tappable
            foreach (var s in b.GetComponents<Shadow>()) UnityEngine.Object.Destroy(s);
            ((RectTransform)b.transform).sizeDelta = new Vector2(150, BarHeight);
            var box = MetaUI.Box(b.transform, 0.12f, 0.28f, 0.88f, 1.02f);
            box.name = "Icon";
            if (icon != null)
            {
                var tile = MetaUI.IconTile(box, icon, caption, IconBlue, false);
                MetaUI.Square(tile);
            }
            var l = UI.Label(b.transform, caption, 26, Color.white, TextAnchor.MiddleCenter, true);
            UI.Anchor(l.rectTransform, 0, 0, 1, 0.3f);   // inside the button (best fit shrinks long captions)
            MetaUI.Outlined(l, BarDark, 2f);
            return b;
        }

        /// <summary>Mailbox-style envelope drawn from shapes (no inbox icon among the renders).</summary>
        static void Envelope(Transform host)
        {
            if (host == null) return;
            var env = UI.Image(host, UI.RoundedSmall, Color.white, false, "Envelope");
            env.type = Image.Type.Sliced;
            UI.Anchor(env.rectTransform, 0.06f, 0.16f, 0.94f, 0.84f);
            var o = env.gameObject.AddComponent<Outline>();
            o.effectColor = BarDark; o.effectDistance = new Vector2(2, -2);
            env.gameObject.AddComponent<RectMask2D>();
            var flap = UI.Image(env.transform, UI.WhiteSprite, new Color32(214, 230, 246, 255), false, "Flap");
            flap.rectTransform.anchorMin = flap.rectTransform.anchorMax = new Vector2(0.5f, 1);
            flap.rectTransform.sizeDelta = new Vector2(38, 38);
            flap.rectTransform.anchoredPosition = Vector2.zero;
            flap.rectTransform.localEulerAngles = new Vector3(0, 0, 45);
            var seal = UI.Image(env.transform, UI.Circle, Theme.Danger, false, "Seal");
            seal.rectTransform.anchorMin = seal.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            seal.rectTransform.sizeDelta = new Vector2(16, 16);
            seal.rectTransform.anchoredPosition = new Vector2(0, -2);
        }

        /// <summary>A blue rounded panel hanging under the top bar.</summary>
        RectTransform Hanging(Vector2 anchor, Vector2 offset, Vector2 size)
        {
            var p = UI.Panel(Root, BarDark, true, "Stats");
            UI.Place(p.rectTransform, anchor, size, offset);
            if (UI.Skin.Has("stats"))
            {
                // the original hanging trapezoid (HUD_Level / HUD_money): the row sits below the bar, the art reaches
                // up under it so the panel still hangs from the bar
                p.color = new Color(0, 0, 0, 0);   // keeps the raycast area of the level button
                var art = UI.Image(p.transform, null, Color.white, false, "Art");
                UI.Skin.Apply(art, "stats");
                UI.Stretch(art.rectTransform, -10, -10, -(18 + 8 + StatsGap), -4);
                art.transform.SetAsFirstSibling();
                return p.rectTransform;
            }
            var inner = UI.Panel(p.transform, PanelBlue, true, "Inner");
            UI.Stretch(inner.rectTransform, 5, 5, 5, 7);
            inner.raycastTarget = false;
            return p.rectTransform;
        }

        /// <summary>"Crazy Penguin WARS" in the logo's two-tone style (no logo art).</summary>
        static void BuildTextLogo(RectTransform logo)
        {
            var crazy = UI.Label(logo, "Crazy Penguin", 34, new Color32(150, 230, 60, 255), TextAnchor.LowerLeft, true);
            UI.Anchor(crazy.rectTransform, 0, 0.56f, 1, 0.96f);
            MetaUI.Outlined(crazy, BarDark, 2.5f);
            var wars = UI.Label(logo, "WARS", 60, new Color32(255, 196, 30, 255), TextAnchor.UpperLeft, true);
            UI.Anchor(wars.rectTransform, 0.06f, 0.02f, 1, 0.6f);
            MetaUI.Outlined(wars, new Color32(150, 50, 10, 255), 3.5f);
        }

        /// <summary>One money counter in the right panel: icon, amount and an Add button under it (opens the bank).</summary>
        Text Money(RectTransform panel, string kind, int index, Color addColor)
        {
            var half = MetaUI.Box(panel, index * 0.5f, 0, index * 0.5f + 0.5f, 1);
            var ic = MetaUI.CurrencyIcon(half, kind);
            UI.Place(ic, new Vector2(0, 0.5f), new Vector2(88, 88), new Vector2(10, 0));
            var amount = UI.Label(half, "0", 40, Color.white, TextAnchor.MiddleCenter, true);
            UI.Anchor(amount.rectTransform, 0.36f, 0.5f, 0.97f, 0.98f);
            MetaUI.Outlined(amount, BarDark, 2.5f);
            var add = MetaUI.CartoonButton(half, "Add", addColor, () => ScreenManager.Show(() => new BankScreen(kind == "cash" ? 1 : 0)), 30, "Add " + kind);
            var art = (RectTransform)add.transform;
            art.anchorMin = new Vector2(0.38f, 0.06f); art.anchorMax = new Vector2(0.95f, 0.52f);
            art.offsetMin = art.offsetMax = Vector2.zero;
            var cap = add.transform.Find("Caption") as RectTransform;
            if (cap) UI.Stretch(cap, 6, 6, 2, 8);
            return amount;
        }

        void TickHeader(float dt)
        {
            headerTimer -= dt;
            if (headerTimer > 0) return;
            headerTimer = 0.2f;
            var P = ProfileService.P;
            if (P.level != shownLevel || P.xp != shownXp)
            {
                shownLevel = P.level; shownXp = P.xp;
                levelText.text = P.level.ToString();
                xpFill.fillAmount = Progression.LevelProgress(P.xp, P.level);
                int next = GameData.XpForLevel(P.level + 1);
                xpText.text = next == int.MaxValue ? P.xp.ToString("N0") + " exp (MAX)" : P.xp.ToString("N0") + " / " + next.ToString("N0") + " exp";
            }
            if (P.coins != shownCoins) { shownCoins = P.coins; coinText.text = UI.Money(P.coins); }
            if (P.cash != shownCash) { shownCash = P.cash; cashText.text = UI.Money(P.cash); }
        }

        // ------------------------------------------------------------------ bottom strip

        void BuildBottom()
        {
            var strip = UI.Panel(Root, BarDark, false, "Strip");
            var srt = strip.rectTransform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 0); srt.pivot = new Vector2(0.5f, 0);
            srt.offsetMin = new Vector2(-400, -300); srt.offsetMax = new Vector2(400, StripHeight);   // runs under the home indicator
            if (UI.Skin.Apply(strip, "strip"))
            {
                // the original friends bar: its slanted ends start just outside the safe area
                srt.offsetMin = new Vector2(-60, -300); srt.offsetMax = new Vector2(60, StripHeight + 8);
            }
            else
            {
                var face = UI.Panel(strip.transform, BarBlue, false, "Face");
                UI.Stretch(face.rectTransform, 0, 0, 6, 0);
            }

            var row = UI.Rect(Root, "Friends");
            row.anchorMin = new Vector2(0, 0); row.anchorMax = new Vector2(0, 0); row.pivot = new Vector2(0, 0);
            row.sizeDelta = new Vector2(1100, StripHeight - 22);
            row.anchoredPosition = new Vector2(Edge, 10);
            var h = UI.HBox(row, 12, TextAnchor.MiddleLeft);
            h.childControlWidth = false; h.childControlHeight = false;

            var invite = MetaUI.CartoonButton(row, "Invite", SlotBlue, () => ScreenManager.Show(() => new FriendsScreen(0)), 32, "Invite");
            ((RectTransform)invite.transform).sizeDelta = new Vector2(150, 120);
            UI.Skin.Apply(invite.GetComponent<Image>(), "tile.blue");
            var ic = invite.transform.Find("Caption").GetComponent<Text>();
            ic.color = BarBlue;
            MetaUI.Outlined(ic, Color.white, 2f);

            Arrows(row, -1);
            friendRow = UI.Rect(row, "Slots");
            friendRow.sizeDelta = new Vector2(FriendSlots * 132 + (FriendSlots - 1) * 10, 124);
            var fh = UI.HBox(friendRow, 10, TextAnchor.MiddleLeft);
            fh.childControlWidth = false; fh.childControlHeight = false;
            Arrows(row, 1);
            FillFriends();
            if (Social.Available)
                Social.LoadFriends((list, err) =>
                {
                    if (!alive || list == null) return;
                    friends.Clear();
                    friends.AddRange(list);
                    FillFriends();
                });

            // PING WIN slots machine in the corner, the settings gear above it
            var slots = MetaUI.CartoonButton(Root, null, SlotPurple, () => ScreenManager.Show(() => new SlotMachineScreen()), 40, "Slots");
            UI.Place((RectTransform)slots.transform, new Vector2(1, 0), new Vector2(330, SlotTileHeight), new Vector2(-Edge, 8));
            var machine = UI.Skin.OriginalIcon("Ui/slot");
            if (machine != null)
            {
                // the original PING WIN slots machine (its title is part of the art)
                var si = slots.GetComponent<Image>();
                si.sprite = machine; si.type = Image.Type.Simple; si.preserveAspect = true; si.color = Color.white;
                var sf = slots.GetComponent<SkinFit>();
                if (sf != null) sf.enabled = false;
                AddBadge(slots.transform, SlotMachineLogic.FreeSpinsLeft, new Vector2(-6, -6));
                BuildGear();
                return;
            }
            var sIcon = MetaUI.IconTile(MetaUI.Box(slots.transform, 0.02f, 0.1f, 0.42f, 0.95f), "Ui/slot", "Slots", SlotPurple, false);
            MetaUI.Square(sIcon);
            var ping = UI.Label(slots.transform, "PING WIN", 46, new Color32(255, 214, 40, 255), TextAnchor.LowerCenter, true);
            UI.Anchor(ping.rectTransform, 0.38f, 0.48f, 0.98f, 0.92f);
            MetaUI.Outlined(ping, new Color32(120, 30, 20, 255), 3f);
            var sl = UI.Label(slots.transform, "* slots *", 38, Color.white, TextAnchor.UpperCenter, true);
            UI.Anchor(sl.rectTransform, 0.38f, 0.12f, 0.98f, 0.5f);
            MetaUI.Outlined(sl, MetaUI.Darker(SlotPurple, 0.6f), 3f);
            AddBadge(slots.transform, SlotMachineLogic.FreeSpinsLeft, new Vector2(-6, -6));
            BuildGear();
        }

        /// <summary>Settings gear above the slots machine (the original square blue gear button when available).</summary>
        void BuildGear()
        {
            var gear = MetaUI.CartoonButton(Root, null, CustomBlue, () => ScreenManager.Show(() => new SettingsScreen()), 30, "Settings");
            UI.Place((RectTransform)gear.transform, new Vector2(1, 0), new Vector2(92, 92), new Vector2(-Edge - 4, SlotTileHeight + 20));
            var art = UI.Skin.OriginalIcon("Ui/settings");
            if (art != null)
            {
                var gimg = gear.GetComponent<Image>();
                gimg.sprite = art; gimg.type = Image.Type.Simple; gimg.preserveAspect = true; gimg.color = Color.white;
                var sf = gear.GetComponent<SkinFit>();
                if (sf != null) sf.enabled = false;
                return;
            }
            var gi = MetaUI.IconTile(MetaUI.Box(gear.transform, 0.14f, 0.18f, 0.86f, 0.9f), "Ui/settings", "Settings", CustomBlue, false);
            MetaUI.Square(gi);
        }

        /// <summary>Paging arrows beside the friend slots: one step and jump to the start / end.</summary>
        void Arrows(RectTransform row, int dir)
        {
            var col = UI.Rect(row, dir < 0 ? "Prev" : "Next");
            col.sizeDelta = new Vector2(62, 124);
            var v = UI.VBox(col, 8, TextAnchor.MiddleCenter);
            v.childControlWidth = false; v.childControlHeight = false;
            var one = MetaUI.CartoonButton(col, dir < 0 ? "<" : ">", SlotBlue, () => Page(dir), 30, "Step");
            ((RectTransform)one.transform).sizeDelta = new Vector2(58, 56);
            var all = MetaUI.CartoonButton(col, dir < 0 ? "<<" : ">>", SlotBlue, () => Page(dir * 1000), 26, "Jump");
            ((RectTransform)all.transform).sizeDelta = new Vector2(58, 56);
            foreach (var t in col.GetComponentsInChildren<Text>()) { t.color = BarBlue; MetaUI.Outlined(t, Color.white, 1.5f); }
            // the original round arrow buttons (step = home 36, jump = home 44; mirrored for next)
            ArrowArt(one, 36, dir);
            ArrowArt(all, 44, dir);
        }

        static void ArrowArt(Button b, int bitmap, int dir)
        {
            var art = UI.Skin.Bitmap("home_screen", bitmap);
            if (art == null) return;
            var img = b.GetComponent<Image>();
            img.sprite = UI.WhiteSprite; img.type = Image.Type.Simple; img.color = new Color(1, 1, 1, 0.001f);   // tappable, invisible
            var sf = b.GetComponent<SkinFit>();
            if (sf != null) sf.enabled = false;
            var a = UI.Image(b.transform, art, Color.white, true, "Art");
            UI.Stretch(a.rectTransform, -2, -2, -2, -2);
            if (dir > 0) a.rectTransform.localScale = new Vector3(-1, 1, 1);
            a.transform.SetAsFirstSibling();
            var cap = b.GetComponentInChildren<Text>();
            if (cap) cap.enabled = false;
        }

        int Pages => Mathf.Max(1, (friends.Count + 1 + FriendSlots) / FriendSlots);   // me + friends + at least one Add slot

        void Page(int delta)
        {
            int p = Mathf.Clamp(friendPage + delta, 0, Pages - 1);
            if (p == friendPage) return;
            friendPage = p;
            FillFriends();
        }

        void FillFriends()
        {
            if (friendRow == null) return;
            UI.Clear(friendRow);
            friendPage = Mathf.Clamp(friendPage, 0, Pages - 1);
            var P = ProfileService.P;
            for (int i = 0; i < FriendSlots; i++)
            {
                int idx = friendPage * FriendSlots + i;   // 0 = me, then friends
                if (idx == 0) PersonSlot(P.displayName, P.level, "Ui/app_icon", true, () => ScreenManager.Show(() => new ProfileScreen()));
                else if (idx - 1 < friends.Count)
                {
                    var f = friends[idx - 1];
                    PersonSlot(f.name, f.level, null, f.online, () => ScreenManager.Show(() => new FriendsScreen(0)));
                }
                else AddSlot();
            }
        }

        RectTransform SlotBase(string name, Action onClick)
        {
            var b = MetaUI.CartoonButton(friendRow, null, SlotBlue, onClick, 24, name);
            var rt = (RectTransform)b.transform;
            rt.sizeDelta = new Vector2(132, 124);
            UI.Skin.Apply(b.GetComponent<Image>(), "tile.blue");
            return rt;
        }

        void PersonSlot(string name, int level, string icon, bool online, Action onClick)
        {
            var rt = SlotBase("Friend " + name, onClick);
            var tile = MetaUI.IconTile(MetaUI.Box(rt, 0.14f, 0.42f, 0.86f, 0.94f), icon, name, null, icon == null);
            MetaUI.Square(tile);
            var n = UI.Label(rt, name, 22, BarDark, TextAnchor.MiddleCenter, true);
            SlotCaption(n);
            foreach (var s in n.GetComponents<Shadow>()) UnityEngine.Object.Destroy(s);
            MetaUI.Outlined(n, Color.white, 1.5f);
            var starArt = UI.Skin.Icon("Ui/star");
            var star = UI.Image(rt, starArt ?? UI.Circle, starArt != null ? Color.white : MetaUI.Purple, true, "Level");
            UI.Place(star.rectTransform, new Vector2(0, 1), new Vector2(56, 56), new Vector2(-12, 14));
            var lt = UI.Label(star.transform, Mathf.Max(1, level).ToString(), 22, Color.white, TextAnchor.MiddleCenter, true);
            UI.Stretch(lt.rectTransform, 8, 8, 14, 10);
            MetaUI.Outlined(lt, new Color32(110, 50, 140, 255), 1.5f);
            if (online)
            {
                var dot = UI.Image(rt, UI.Circle, Theme.Good, false, "Online");
                UI.Place(dot.rectTransform, new Vector2(1, 1), new Vector2(20, 20), new Vector2(-10, -10));
            }
        }

        /// <summary>
        /// Caption band of a friend slot: above the tile art's dark bottom lip (the bottom ~22 px of a 124 px tile),
        /// where dark text used to sink into the lip and read as cut off.
        /// </summary>
        static void SlotCaption(Text t)
        {
            var r = t.rectTransform;
            r.anchorMin = new Vector2(0, 0); r.anchorMax = new Vector2(1, 0); r.pivot = new Vector2(0.5f, 0);
            r.offsetMin = new Vector2(10, 22); r.offsetMax = new Vector2(-10, 52);
        }

        void AddSlot()
        {
            var rt = SlotBase("AddFriend", () => ScreenManager.Show(() => new FriendsScreen(0)));
            if (UI.Skin.OriginalIcon("Ui/add_friend") != null)
            {
                // the original "add a friend" penguin with the green plus
                var add = MetaUI.IconTile(MetaUI.Box(rt, 0.12f, 0.4f, 0.88f, 0.96f), "Ui/add_friend", "Add", null, false);
                MetaUI.Square(add);
                var cap = UI.Label(rt, Loc.T("BUTTON_ADD"), 24, BarBlue, TextAnchor.MiddleCenter, true);
                SlotCaption(cap);
                MetaUI.Outlined(cap, Color.white, 1.5f);
                return;
            }
            var tile = MetaUI.IconTile(MetaUI.Box(rt, 0.16f, 0.42f, 0.84f, 0.94f), "Ui/app_icon", "Add", null, false);
            MetaUI.Square(tile);
            tile.gameObject.AddComponent<CanvasGroup>().alpha = 0.55f;
            var plus = UI.Label(rt, "+", 64, Theme.Good, TextAnchor.MiddleCenter, true);
            UI.Anchor(plus.rectTransform, 0.02f, 0.42f, 0.5f, 0.9f);
            MetaUI.Outlined(plus, Color.white, 3f);
            var n = UI.Label(rt, Loc.T("BUTTON_ADD"), 24, BarBlue, TextAnchor.MiddleCenter, true);
            SlotCaption(n);
            foreach (var s in n.GetComponents<Shadow>()) UnityEngine.Object.Destroy(s);
        }
    }
}
