using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CPW
{
    /// <summary>Settings: audio, graphics quality, trajectory guide, player name, online status, reset and credits.</summary>
    public class SettingsScreen : MetaScreen
    {
        protected override string Title => Loc.T("GAME_SETTINGS_HEADER");
        Text onlineText;
        float onlineTimer;
        List<Button> qualityTabs;

        protected override void BuildContent()
        {
            var P = ProfileService.P;
            // ---- left: audio + gameplay ----
            var left = MetaUI.CardPanel(Content);
            UI.Anchor(left.rectTransform, 0, 0, 0.49f, 1);
            var lv = UI.VBox(left.rectTransform, 14, TextAnchor.UpperLeft, 30);
            lv.childForceExpandHeight = false;
            Header(left.rectTransform, "Audio");
            UI.Layout(UI.Toggle(left.transform, "Music", P.musicOn, b => { P.musicOn = b; ApplyAudio(); }), -1, 70);
            Slider(left.rectTransform, "Music volume", P.musicVolume, v => { P.musicVolume = v; ApplyAudio(false); });
            UI.Layout(UI.Toggle(left.transform, "Sound effects", P.sfxOn, b => { P.sfxOn = b; ApplyAudio(); }), -1, 70);
            Slider(left.rectTransform, "Effects volume", P.sfxVolume, v => { P.sfxVolume = v; ApplyAudio(false); });
            Header(left.rectTransform, "Gameplay");
            UI.Layout(UI.Toggle(left.transform, "Aiming trajectory guide", P.showTrajectory, b => { P.showTrajectory = b; ProfileService.Save(); }), -1, 70);
            var qrow = UI.Rect(left.transform, "Quality");
            UI.Layout(qrow, -1, 90);
            var ql = UI.Label(qrow, "Graphics", 36, Theme.Text, TextAnchor.MiddleLeft);
            UI.Anchor(ql.rectTransform, 0, 0, 0.3f, 1);
            var qt = UI.Rect(qrow, "Tabs");
            UI.Anchor(qt, 0.3f, 0.05f, 1, 0.95f);
            qualityTabs = MetaUI.Tabs(qt, new[] { "Low", "Medium", "High" }, Mathf.Clamp(P.quality, 0, 2), i =>
            {
                P.quality = i;
                GameManager.ApplyQuality(i);
                ProfileService.Save();
                MetaUI.SetTabSelected(qualityTabs, i);
            }, 32);
            var tut = UI.Button(left.transform, "Replay tutorial", () => BattleFactory.Launch(BattleFactory.Tutorial()), UI.ButtonStyle.Secondary, 36);
            UI.Layout(tut, -1, 90);

            // ---- right: account + about ----
            var right = MetaUI.CardPanel(Content);
            UI.Anchor(right.rectTransform, 0.51f, 0, 1, 1);
            var rv = UI.VBox(right.rectTransform, 12, TextAnchor.UpperLeft, 30);
            rv.childForceExpandHeight = false;
            Header(right.rectTransform, Loc.T("GAME_SETTINGS_BUTTON_ACCOUNT"));
            var name = UI.Input(right.transform, P.displayName, "Penguin name", s =>
            {
                s = (s ?? "").Trim();
                if (s.Length == 0) return;
                P.displayName = s.Length > 16 ? s.Substring(0, 16) : s;
                ProfileService.Save();
            });
            UI.Layout(name, -1, 86);
            var id = UI.Label(right.transform, Loc.T("GAME_SETTINGS_USER_ID") + " " + P.playerId, 24, Theme.Muted, TextAnchor.MiddleLeft);
            UI.Layout(id, -1, 36);
            onlineText = UI.Label(right.transform, "", 30, Theme.Secondary, TextAnchor.MiddleLeft);
            UI.Layout(onlineText, -1, 80);
            UpdateOnline();
            var reset = UI.Button(right.transform, "Reset progress", () =>
                UI.Confirm("Reset progress?", "This deletes your coins, fish, items, clothes and levels on this device. There is no undo!", () =>
                    UI.Confirm("Really?", "Last chance! Start over as a brand new penguin?", () =>
                    {
                        ProfileService.ResetAll();
                        CraftingScreen.ClearPending();
                        HomeScreen.ResetSession();
                        CustomGameScreen.ClearRemembered();
                        GameManager.GoHome();
                    }, null, "Reset", "Keep"), null, "Yes", "No"), UI.ButtonStyle.Danger, 36);
            UI.Layout(reset, -1, 90);
            Header(right.rectTransform, "Credits");
            var credits = UI.Label(right.transform,
                "A fan remake of Crazy Penguin Wars (Tuxwars) by Digital Chocolate, built on the preservation work of the " +
                "Crazy Penguin Wars community (github.com/Crazy-Penguin-Wars: cpw-client, cpw-server, cpw-battleserver, " +
                "cpw-assets, cpw-mapeditor). Original maps, sounds and game data come from those repos. Made with Unity.",
                26, Theme.Text, TextAnchor.UpperLeft);
            UI.Layout(credits, -1, 230);
            var ver = UI.Label(right.transform, Loc.T("GAME_SETTINGS_VERSION") + " " + Application.version, 24, Theme.Muted, TextAnchor.MiddleLeft);
            UI.Layout(ver, -1, 34);
        }

        static void Header(RectTransform p, string text)
        {
            var l = UI.Label(p, text, 42, Theme.Secondary, TextAnchor.MiddleLeft, true);
            UI.Layout(l, -1, 60);
        }

        static void Slider(RectTransform p, string label, float value, System.Action<float> change)
        {
            var row = UI.Rect(p, label);
            UI.Layout(row, -1, 70);
            var l = UI.Label(row, label, 30, Theme.Text, TextAnchor.MiddleLeft);
            UI.Anchor(l.rectTransform, 0, 0, 0.36f, 1);
            var s = UI.Slider(row, value, change);
            UI.Anchor((RectTransform)s.transform, 0.38f, 0.1f, 0.98f, 0.9f);
        }

        static void ApplyAudio(bool save = true)
        {
            AudioManager.I?.ApplySettings();
            if (save) ProfileService.Save();
        }

        void UpdateOnline()
        {
            onlineText.text = "Online: " + Online.Service.Status;
            onlineText.color = Online.Service.Available ? Theme.Good : Theme.Secondary;
        }

        public override void Tick(float dt)
        {
            onlineTimer += dt;
            if (onlineTimer < 2f) return;
            onlineTimer = 0;
            UpdateOnline();
        }

        public override void OnHide() => ProfileService.Save();   // volumes are saved when leaving
    }

    /// <summary>How to play, in a few illustrated pages.</summary>
    public class HelpScreen : MetaScreen
    {
        protected override string Title => Loc.T("BUTTON_HELP");
        static int page;
        Text titleText, bodyText, pageText;

        static readonly string[][] Pages =
        {
            new[] { "Goal", "Penguins take turns. Score points by hitting opponents and smashing objects; knocking a penguin out gives a bonus. The first to the winning score, or the best when the clock runs out, wins.\n\nThese penguins can't swim: stay out of the water!" },
            new[] { "Moving", "Use the arrows to walk. Drag the spring to jump. Walking and jumping use ENERGY, shown on the dial, so plan your route. You can still move after shooting." },
            new[] { "Shooting", "Tap the weapon button to pick a weapon. Drag the aim control out from your penguin to set angle and power, then let go to fire. Some weapons are dropped or placed instead. You get one shot per turn." },
            new[] { "Boosters", "Pick up to 3 boosters before a battle. Sushi boosts damage, Shield blocks a hit, Pogo Stick jumps higher, Bandage heals, Mines and Caltrops trap enemies... Each booster lasts one turn." },
            new[] { "Coins, fish & XP", "Matches give coins and XP. Coins buy ammo and clothes. Fish (premium) unlock weapons early, buy special items, VIP and slot spins. You get fish when you level up, from challenges, the slot machine and daily gifts." },
            new[] { "Gear up", "Clothes and trophies give Attack, Defence and Luck. Dress up in the Wardrobe. Complete challenges to earn trophies, and research new items in the Crafting lab using ingredients from battles and the slot machine." },
        };

        protected override void BuildContent()
        {
            var card = MetaUI.CardPanel(Content);
            UI.Anchor(card.rectTransform, 0.08f, 0.14f, 0.92f, 1);
            titleText = UI.Label(card.transform, "", 64, Theme.Secondary, TextAnchor.MiddleCenter, true);
            UI.Anchor(titleText.rectTransform, 0.05f, 0.8f, 0.95f, 0.97f);
            bodyText = UI.Label(card.transform, "", 40, Theme.Text, TextAnchor.UpperLeft);
            UI.Anchor(bodyText.rectTransform, 0.06f, 0.06f, 0.94f, 0.78f);
            var prev = UI.Button(Content, "<", () => Turn(-1), UI.ButtonStyle.Secondary, 56);
            UI.Place((RectTransform)prev.transform, new Vector2(0.3f, 0), new Vector2(160, 110), Vector2.zero);
            var next = UI.Button(Content, ">", () => Turn(1), UI.ButtonStyle.Primary, 56);
            UI.Place((RectTransform)next.transform, new Vector2(0.7f, 0), new Vector2(160, 110), Vector2.zero);
            pageText = UI.Label(Content, "", 36, Color.white, TextAnchor.MiddleCenter, true);
            UI.Anchor(pageText.rectTransform, 0.4f, 0, 0.6f, 0.11f);
            Turn(0);
        }

        void Turn(int d)
        {
            page = (page + d + Pages.Length) % Pages.Length;
            titleText.text = Pages[page][0];
            bodyText.text = Pages[page][1];
            pageText.text = (page + 1) + " / " + Pages.Length;
        }
    }
}
