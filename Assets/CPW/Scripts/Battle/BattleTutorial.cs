using UnityEngine;

namespace CPW
{
    /// <summary>
    /// The guided first battle (original TuxTutorial*SubState flow, adapted to touch): move to a crate, jump,
    /// aim, fire, watch the opponent's turn, change weapon to the pistol and fire it, then play on.
    /// The turn timer is held while the player is learning. Uses the original TUTORIAL_* strings.
    /// </summary>
    public class BattleTutorial
    {
        public enum Step { Intro, Move, Jump, Aim, Fire, OpponentTurn, ChangeWeapon, SelectPistol, FirePistol, Free }

        readonly BattleController c;
        public Step Current { get; private set; } = Step.Intro;
        float moveStartX, aimTime, hintTimer;
        int freeShots;
        bool crateDropped;

        /// <summary>Ends the match after a couple of free shots so the tutorial stays short.</summary>
        public bool WantsEnd => Current == Step.Free && freeShots >= 2;

        public BattleTutorial(BattleController controller)
        {
            c = controller;
            foreach (var p in c.Penguins) if (p.Slot.isAI) p.Slot.aiSkill = 0;   // one easy opponent
        }

        static string T(string key, string fallback) => Loc.Has(key) ? Loc.T(key) : fallback;

        Penguin Player => c.PenguinAt(c.Config.LocalPlayerIndex);
        /// <summary>Only the player's own actions advance the tutorial.</summary>
        bool Mine => c.Active != null && c.Active == Player;

        public void OnBattleStart()
        {
            UI.Popup(T("TUTORIAL_INTRO_TITLE", "Penguin Battle Training"),
                T("TUTORIAL_INTRO_DESCRIPTION", "Ready to blow things up?").Replace("\\n", "\n"),
                new UI.PopupButton(T("TUTORIAL_INTRO_BUTTON", "Let's go!")));
        }

        public void OnTurnStarted(Penguin p)
        {
            bool mine = p == Player;
            if (mine)
            {
                if (Current == Step.Intro) Go(Step.Move);
                else if (Current == Step.OpponentTurn) Go(Step.ChangeWeapon);
                else ShowStep();
                c.TimerHeld = Current != Step.Free;
            }
            else
            {
                c.TimerHeld = false;
                if (Current == Step.OpponentTurn) c.Hud.ShowHint(T("TUTORIAL_OPPONENTS_TURN_TITLE", "Opponent's Turn"),
                    T("TUTORIAL_OPPONENTS_TURN", "It is now your opponent's TURN. Wait for it to end."));
                else c.Hud.HideHint();
            }
        }

        void Go(Step s)
        {
            Current = s;
            ShowStep();
        }

        void ShowStep()
        {
            var hud = c.Hud;
            hud.Highlight(null);
            switch (Current)
            {
                case Step.Move:
                    moveStartX = Player != null ? Player.Position.x : 0;
                    hud.ShowHint(T("TUTORIAL_MOVE_TITLE", "Moving"), T("TUTORIAL_MOVE", "Use the buttons at the bottom to move LEFT and RIGHT."));
                    hud.Highlight("walk");
                    DropCrate();
                    break;
                case Step.Jump:
                    hud.ShowHint(T("TUTORIAL_JUMP_TITLE", "Jump"), "Tap the JUMP button to jump. Jumping costs ENERGY (the bar at the bottom).");
                    hud.Highlight("jump");
                    break;
                case Step.Aim:
                    aimTime = 0;
                    hud.ShowHint(T("TUTORIAL_SHOOT_TITLE", "Shooting"), "DRAG on the battlefield to aim your bazooka. The further from your penguin, the more power.");
                    break;
                case Step.Fire:
                    hud.ShowHint(T("TUTORIAL_FIRE_TITLE", "Fire!"), T("TUTORIAL_FIRE", "Aim for the OPPONENT!") + "\nThen press FIRE.");
                    hud.Highlight("fire");
                    break;
                case Step.ChangeWeapon:
                    hud.ShowHint(T("TUTORIAL_CHANGE_WEAPON_TITLE", "Change Weapon"), T("TUTORIAL_CHANGE_WEAPON", "Change your weapon by tapping the WEAPON button."));
                    hud.Highlight("weapon");
                    break;
                case Step.SelectPistol:
                    hud.ShowHint(T("TUTORIAL_SELECT_WEAPON_TITLE", "Select Weapon"), T("TUTORIAL_SELECT_WEAPON", "TAP the PISTOL to select it."));
                    break;
                case Step.FirePistol:
                    hud.ShowHint(T("TUTORIAL_ATTACK_AP_TITLE", "One Shot"), T("TUTORIAL_ATTACK_AP", "Remember, you can only shoot ONCE every turn!"));
                    hud.Highlight("fire");
                    break;
                case Step.Free:
                    hud.ShowHint(T("TUTORIAL_WINNING_TITLE", "Winning"),
                        T("TUTORIAL_WINNING", "Nice work!").Replace("\\n", "\n").Replace("%U", c.WinningScore.ToString()));
                    hintTimer = 6f;
                    break;
            }
        }

        void DropCrate()
        {
            if (crateDropped || Player == null) return;
            crateDropped = true;
            var me = Player;
            Penguin enemy = null;
            foreach (var p in c.Penguins) if (p != me) { enemy = p; break; }
            float dir = enemy != null && enemy.Position.x < me.Position.x ? -1 : 1;
            float x = me.Position.x + dir * 5f;
            var t = BattleTerrain.I;
            Vector2 pos = new Vector2(x, me.Position.y + 6f);
            if (t != null && t.GroundBelow(x, me.Position.y + 8f, out var g) && g.y > t.WaterY + 1f) pos = g + Vector2.up * 4f;
            c.DropCrate("AmmoCrate", pos, true);
        }

        public void Update(float dt)
        {
            if (Current == Step.Move && Player != null && Mathf.Abs(Player.Position.x - moveStartX) > 6f) Go(Step.Jump);
            if (Current == Step.Aim && aimTime > 0.8f) Go(Step.Fire);
            if (hintTimer > 0)
            {
                hintTimer -= dt;
                if (hintTimer <= 0) c.Hud.HideHint();
            }
        }

        // ---------- notifications from the controller / HUD ----------
        public void OnMoved() { }
        public void OnCratePicked(Penguin p) { if (Current == Step.Move && p == Player) Go(Step.Jump); }
        public void OnJumped() { if (Current == Step.Jump && Mine) Go(Step.Aim); }
        public void OnAimed() { if (Current == Step.Aim && Mine) aimTime += Time.deltaTime + 0.05f; }
        public void OnBooster() { }

        public void OnWeaponPanelOpened() { if (Current == Step.ChangeWeapon) Go(Step.SelectPistol); }

        public void OnWeaponSelected(string id)
        {
            if (!Mine) return;
            if ((Current == Step.SelectPistol || Current == Step.ChangeWeapon) && id == "Pistol")
            {
                Go(Step.FirePistol);
                c.TimerHeld = true;
            }
        }

        public void OnFired(string item)
        {
            if (!Mine) return;
            switch (Current)
            {
                case Step.Aim:
                case Step.Fire:
                case Step.Move:
                case Step.Jump:
                    c.TimerHeld = false;
                    Current = Step.OpponentTurn;
                    c.Hud.ShowHint(T("TUTORIAL_ATTACK_AP_TITLE", "One Shot"), T("TUTORIAL_ATTACK_AP", "You can only shoot ONCE every turn."));
                    c.Hud.Highlight(null);
                    break;
                case Step.FirePistol:
                case Step.ChangeWeapon:
                case Step.SelectPistol:
                    c.TimerHeld = false;
                    Go(Step.Free);
                    break;
                case Step.Free:
                    freeShots++;
                    if (freeShots >= 2) c.Hud.ShowHint(T("TUTORIAL_MATCH_WON_TITLE", "Wow!"), T("TUTORIAL_MATCH_WON", "I think you're ready!"));
                    break;
            }
        }
    }
}
