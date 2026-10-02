using UnityEngine;

namespace CPW
{
    public enum AvatarState { Idle, Walk, Jump, Fall, Aim, Fire, Hurt, Dead, Celebrate, Drown, Sad }

    /// <summary>
    /// The 3D penguin (Blender model "Penguin/Penguin") with clothes, procedural animation and a held weapon.
    /// Used by battles (Battle/Penguin) and menus (customization preview, home screen).
    /// STUB: the art pass replaces the body of this class but keeps the public API.
    /// </summary>
    public class PenguinAvatar : MonoBehaviour
    {
        public AvatarState State { get; private set; }
        public int Facing { get; private set; } = 1;
        /// <summary>World-space point at the tip of the held weapon (where projectiles spawn).</summary>
        public Transform Muzzle { get; private set; }
        /// <summary>Above the head (name tag / HP bar / emote bubble anchor).</summary>
        public Transform HeadTop { get; private set; }

        /// <summary>Create an avatar under parent. height = world units from feet to head top.</summary>
        public static PenguinAvatar Create(Transform parent, float height, Color teamColor)
        {
            var go = new GameObject("PenguinAvatar");
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<PenguinAvatar>();
            var body = ModelLibrary.Spawn("Penguin/Penguin", go.transform, PrimitiveType.Capsule, height * 0.5f, new Color(0.1f, 0.1f, 0.15f));
            body.transform.localPosition = new Vector3(0, height * 0.5f, 0);
            a.Muzzle = new GameObject("Muzzle").transform; a.Muzzle.SetParent(go.transform, false); a.Muzzle.localPosition = new Vector3(height * 0.4f, height * 0.5f, 0);
            a.HeadTop = new GameObject("HeadTop").transform; a.HeadTop.SetParent(go.transform, false); a.HeadTop.localPosition = new Vector3(0, height * 1.05f, 0);
            return a;
        }

        /// <summary>Wear clothes by Bonus id (e.g. "flannel_head"); empty/null removes that slot.</summary>
        public void SetClothes(string head, string chest, string feet) { }
        public void SetState(AvatarState s) { State = s; }
        public void SetFacing(int dir) { Facing = dir >= 0 ? 1 : -1; transform.localScale = new Vector3(Facing, 1, 1); }
        /// <summary>Aim angle in degrees, 0 = forward (facing direction), 90 = up, -90 = down.</summary>
        public void SetAim(float degrees) { }
        /// <summary>Show a weapon model by WeaponGraphic id (null/empty = flippers empty).</summary>
        public void HoldWeapon(string weaponGraphicId) { }
        /// <summary>Flash white when hit.</summary>
        public void Flash() { }
        /// <summary>Show an emoticon bubble (Emoticon item id like "EmoticonLaugh") for a few seconds.</summary>
        public void ShowEmote(string emoticonId) { }
        public void SetTeamColor(Color c) { }
    }
}
