using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Maps game ids to the Blender-made model and icon paths (see Docs/ART.md), with fallbacks so callers always get
    /// something sensible. Model paths are relative to Resources/Models (for ModelLibrary.Spawn), icon paths to
    /// Resources/Icons (for ModelLibrary.Icon), texture paths to Resources/Textures.
    /// </summary>
    public static class ArtCatalog
    {
        /// <summary>Show the textured Blender renders of weapons and supplies (Icons/WeaponsTextured, Icons/Supplies)
        /// instead of the original game's icons. Off: the user wants the original look; the renders are then only used
        /// for items the original art has no icon for.</summary>
        public static bool UseTexturedItemIcons = false;

        public const string Penguin = "Penguin/Penguin";
        public const string DefaultWeapon = "Weapons/MiniBazooka";
        public const string DefaultMissile = "Missiles/Grenade";

        // ------------------------------------------------------------------ models

        /// <summary>Held weapon model for a WeaponGraphic id (falls back to the mini bazooka).</summary>
        public static string WeaponModel(string weaponGraphicId)
        {
            if (string.IsNullOrEmpty(weaponGraphicId)) return null;
            string p = "Weapons/" + Strip(weaponGraphicId);
            return ModelLibrary.Exists(p) ? p : DefaultWeapon;
        }

        static Dictionary<string, Record> itemByGraphic;

        /// <summary>The Item whose Graphics is #WeaponGraphic.{id} (the id is usually the item id too), or null.</summary>
        static Record ItemForGraphic(string weaponGraphicId)
        {
            if (string.IsNullOrEmpty(weaponGraphicId) || !GameData.Loaded) return null;
            if (itemByGraphic == null)
            {
                itemByGraphic = new Dictionary<string, Record>();
                foreach (var kv in GameData.Section("Item"))
                {
                    string g = kv.Value.Str("Graphics");
                    if (string.IsNullOrEmpty(g) || !g.StartsWith("#WeaponGraphic.")) continue;
                    string gid = Strip(g);
                    // prefer the item named like its graphic (MegaNuke over FeaturedMegaNuke)
                    if (!itemByGraphic.ContainsKey(gid) || kv.Key == gid) itemByGraphic[gid] = kv.Value;
                }
            }
            string id = Strip(weaponGraphicId);
            return itemByGraphic.TryGetValue(id, out var r) ? r : GameData.Item(id);
        }

        /// <summary>
        /// How the penguin holds an item (Item.AnimationType: small_weapon, large_weapon, small_object, large_object,
        /// punch), picking the original "&lt;action&gt;_&lt;hold&gt;" penguin animations. Unknown -> large_weapon.
        /// </summary>
        public static string WeaponHoldType(string weaponGraphicId)
        {
            var r = ItemForGraphic(weaponGraphicId);
            string t = r != null ? r.Str("AnimationType") : null;
            switch (t)
            {
                case "small_weapon": case "large_weapon": case "small_object": case "large_object": case "punch": return t;
                default: return "large_weapon";
            }
        }

        /// <summary>Item.AllowRotation: whether the held clip turns with the aim (Weapon.aim). Default true.</summary>
        public static bool WeaponAllowsRotation(string weaponGraphicId)
        {
            var r = ItemForGraphic(weaponGraphicId);
            return r == null || !r.Has("AllowRotation") || r.Bool("AllowRotation", true);
        }

        /// <summary>Clothes sprite (rendered from the Blender model onto the original penguin's slots) for a Bonus id
        /// and rig slot, or null when there is none (the item is then not drawn on the sprite penguin).</summary>
        public static Sprite ClothesSprite(string bonusId, PenguinRigData.Slot slot) => PenguinRigData.ClothesSprite(bonusId, slot);

        /// <summary>Projectile model for a MissileGraphic id (falls back to null so callers can draw their own primitive).</summary>
        public static string MissileModel(string missileGraphicId)
        {
            if (string.IsNullOrEmpty(missileGraphicId)) return null;
            string p = "Missiles/" + Strip(missileGraphicId);
            return ModelLibrary.Exists(p) ? p : null;
        }

        /// <summary>Clothing model for a Bonus id, or null if there is none (trophies, unknown ids).</summary>
        public static string ClothesModel(string bonusId)
        {
            if (string.IsNullOrEmpty(bonusId)) return null;
            string p = "Clothes/" + Strip(bonusId);
            return ModelLibrary.Exists(p) ? p : null;
        }

        /// <summary>Level object / pickup model (LevelObject id, "Crate", "HealthCrate", "Mine", "Coin"...), or null.</summary>
        public static string PropModel(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            string p = "Props/" + Strip(id);
            return ModelLibrary.Exists(p) ? p : null;
        }

        static readonly Dictionary<string, List<string>> envCache = new Dictionary<string, List<string>>();

        /// <summary>All environment models of a theme ("Forest", "Winter", "Mountain", "Desert"), e.g. "Env/Forest_Tree1".</summary>
        public static List<string> EnvModels(string theme)
        {
            if (string.IsNullOrEmpty(theme)) theme = "Forest";
            if (envCache.TryGetValue(theme, out var list)) return list;
            list = new List<string>();
            GameObject[] all;
            try { all = Resources.LoadAll<GameObject>("Models/Env"); }
            catch { all = new GameObject[0]; }
            foreach (var go in all)
                if (go != null && go.name.StartsWith(theme + "_")) list.Add("Env/" + go.name);
            list.Sort();
            envCache[theme] = list;
            return list;
        }

        // ------------------------------------------------------------------ icons (paths)

        public static string WeaponIconPath(string weaponIconId) => "Weapons/" + Strip(weaponIconId);
        public static string BoosterIconPath(string boosterIconId) => "Boosters/" + Strip(boosterIconId);
        public static string ClothesIconPath(string bonusId) => "Clothes/" + Strip(bonusId);
        public static string TrophyIconPath(string bonusId) => "Trophies/" + Strip(bonusId);
        public static string SlotIconPath(string symbol) => "Slot/" + symbol;
        public static string UiIconPath(string name) => "Ui/" + name;
        public static string HudIconPath(string name) => "Hud/" + name;
        public static string CraftingIconPath(string ingredientId) => "Crafting/" + ingredientId;
        public static string AchievementIconPath(string achievementId) => "Achievements/" + achievementId;

        /// <summary>Emoticon icon path for an Emoticon item id ("EmoticonLaugh") or icon id ("Laugh").</summary>
        public static string EmoticonIconPath(string id)
        {
            id = Strip(id);
            if (string.IsNullOrEmpty(id)) return null;
            return "Emoticons/" + (id.StartsWith("Emoticon") ? id : "Emoticon" + id);
        }

        // ------------------------------------------------------------------ icons (sprites, with fallbacks)

        /// <summary>First icon that exists among the candidate paths (null if none).</summary>
        public static Sprite FirstIcon(params string[] paths)
        {
            foreach (var p in paths)
            {
                if (string.IsNullOrEmpty(p)) continue;
                var s = ModelLibrary.Icon(p);
                if (s != null) return s;
            }
            return null;
        }

        public static Sprite EmoticonIcon(string id) => FirstIcon(EmoticonIconPath(id), "Emoticons/" + Strip(id), "Hud/Emote");

        /// <summary>Icon of an Item id: looks at its Icon reference (#WeaponIcon/#BoosterIcon/#EmoticonIcon) and falls back by id.</summary>
        public static Sprite ItemIcon(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            string iconRef = null;
            if (GameData.Loaded)
            {
                var r = GameData.Item(itemId);
                iconRef = r != null ? r.Str("Icon") : null;
            }
            string iid = string.IsNullOrEmpty(iconRef) ? itemId : Strip(iconRef);
            var textured = UseTexturedItemIcons ? FirstIcon("WeaponsTextured/" + itemId, "WeaponsTextured/" + iid) : null;
            if (textured != null) return textured;
            if (iconRef != null && iconRef.StartsWith("#BoosterIcon")) return FirstIcon(BoosterIconPath(iid), WeaponIconPath(iid));
            if (iconRef != null && iconRef.StartsWith("#EmoticonIcon")) return EmoticonIcon(itemId);
            return FirstIcon(WeaponIconPath(iid), BoosterIconPath(iid), WeaponIconPath(itemId), BoosterIconPath(itemId));
        }

        /// <summary>Clothes or trophy icon for a Bonus id.</summary>
        public static Sprite BonusIcon(string bonusId) => FirstIcon(ClothesIconPath(bonusId), TrophyIconPath(bonusId), "Ui/star");

        public static Sprite CurrencyIcon(string kind) => FirstIcon(UiIconPath(kind), "Ui/coin");

        // ------------------------------------------------------------------ textures

        /// <summary>Seamless terrain texture for a material theme (Wood, Stone, Ice, Metal, Forest, Winter, Mountain,
        /// Desert, Mud, Lava, OilRig); falls back to Stone. Wrap mode is set to Repeat.</summary>
        public static Texture2D TerrainTexture(string id)
        {
            var t = Resources.Load<Texture2D>("Textures/Terrain/" + id) ?? Resources.Load<Texture2D>("Textures/Terrain/Stone");
            if (t != null) t.wrapMode = TextureWrapMode.Repeat;
            return t;
        }

        /// <summary>4x256 vertical sky gradient for a theme (Forest, Winter, Mountain, Desert, OilRig); falls back to Forest.</summary>
        public static Texture2D SkyTexture(string theme)
        {
            var t = Resources.Load<Texture2D>("Textures/Sky/" + theme) ?? Resources.Load<Texture2D>("Textures/Sky/Forest");
            if (t != null) t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        /// <summary>"#WeaponIcon.Pistol" -> "Pistol"; plain ids pass through.</summary>
        public static string Strip(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return reference;
            if (reference[0] != '#') return reference;
            int dot = reference.LastIndexOf('.');
            return dot >= 0 ? reference.Substring(dot + 1) : reference.Substring(1);
        }
    }
}
