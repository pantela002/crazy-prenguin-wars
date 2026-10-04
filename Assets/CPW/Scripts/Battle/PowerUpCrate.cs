using UnityEngine;

namespace CPW
{
    /// <summary>
    /// A power-up crate (original PowerUpGameObject: AmmoCrate, HealthCrate, PointsCrate, Treasure).
    /// Random ones drop by parachute at a turn start with the level's power_up_percentage chance; level files
    /// can also place them. Picked up by touching; explosions destroy them.
    /// </summary>
    public class PowerUpCrate : MonoBehaviour, IDamageable
    {
        public const float HealAmount = 50f;
        public const int PointsAmount = 25;
        public const int TreasureCoins = 50;

        static readonly string[] Types = { "AmmoCrate", "HealthCrate", "PointsCrate", "Treasure" };
        static readonly int[] Weights = { 45, 30, 15, 10 };

        public string Type { get; private set; }
        public Vector2 Position => rb ? rb.position : (Vector2)transform.position;
        public Rigidbody2D Body => rb;
        public bool Alive => !used;
        /// <summary>Still falling (turn waits for it to land).</summary>
        public bool Falling => !used && rb != null && rb.Vel().sqrMagnitude > 0.3f;

        Rigidbody2D rb;
        BattleController ctrl;
        GameObject chute;
        Sprite icon;
        bool used;
        float age;

        public static bool IsKnownType(string t) => System.Array.IndexOf(Types, t) >= 0;

        public static string RandomType(System.Random r)
        {
            int total = 0;
            foreach (var w in Weights) total += w;
            int v = r.Next(total);
            for (int i = 0; i < Types.Length; i++) { if (v < Weights[i]) return Types[i]; v -= Weights[i]; }
            return Types[0];
        }

        /// <summary>A weapon picked by Item.DropRatio among those the player's level allows (RewardsHandler.getItemIdForDrop).</summary>
        public static string RandomAmmo(System.Random r, int level)
        {
            int total = 0;
            foreach (var it in GameData.Section("Item").Values)
                if (it.Str("Type") == "Weapon" && it.Int("DropRatio") > 0 && it.Int("RequiredLevel", 1) <= Mathf.Max(level, 1) + 2) total += it.Int("DropRatio");
            if (total <= 0) return GameData.Item("Grenade") != null ? "Grenade" : null;
            int v = r.Next(total);
            foreach (var it in GameData.Section("Item").Values)
            {
                if (it.Str("Type") != "Weapon" || it.Int("DropRatio") <= 0 || it.Int("RequiredLevel", 1) > Mathf.Max(level, 1) + 2) continue;
                if (v < it.Int("DropRatio")) return it.Id;
                v -= it.Int("DropRatio");
            }
            return null;
        }

        public static PowerUpCrate Create(Transform parent, string type, Vector2 pos, bool parachute, BattleController ctrl)
        {
            var go = new GameObject("Crate " + type);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var c = go.AddComponent<PowerUpCrate>();
            c.Type = type;
            c.ctrl = ctrl;
            c.rb = go.AddComponent<Rigidbody2D>();
            c.rb.mass = 0.8f;
            c.rb.gravityScale = 1f;
            c.rb.SetDrag(parachute ? 3.5f : 0.1f, 0.5f);
            var box = go.AddComponent<BoxCollider2D>();
            box.size = new Vector2(1.3f, 1.3f);
            var trig = go.AddComponent<CircleCollider2D>();
            trig.isTrigger = true;
            trig.radius = 1.1f;

            Color col = type == "HealthCrate" ? new Color(0.9f, 0.25f, 0.25f) : type == "PointsCrate" ? new Color(0.3f, 0.5f, 0.95f)
                : type == "Treasure" ? Theme.Coin : new Color(0.55f, 0.4f, 0.22f);
            var model = ModelLibrary.Spawn("Props/" + type, go.transform, PrimitiveType.Cube, 1.3f, col);
            model.transform.localPosition = Vector3.zero;
            c.icon = DropIcon(type);
            if (c.icon != null)
            {
                // the original power_ups.swf crates are lost: label the 3D crate with the original drop/item icon
                var ic = new GameObject("Icon").AddComponent<SpriteRenderer>();
                ic.transform.SetParent(go.transform, false);
                ic.transform.localPosition = new Vector3(0, 0, -0.75f);
                ic.transform.localScale = Vector3.one * IconScale(c.icon, 0.95f);
                ic.sprite = c.icon;
                ic.sortingOrder = 9;
            }
            if (parachute)
            {
                c.chute = ModelLibrary.Spawn("Props/Parachute", go.transform, PrimitiveType.Sphere, 2.6f, new Color(1f, 1f, 1f));
                c.chute.transform.localPosition = new Vector3(0, 2.2f, 0);
                if (c.chute.name.EndsWith("(fallback)")) c.chute.transform.localScale = new Vector3(2.8f, 1.1f, 1.2f);
            }
            BattleWorld.Objects.Add(c);
            AudioManager.Sfx(type);
            return c;
        }

        /// <summary>Original icon for a crate type: Treasure = drop_coins, PointsCrate = drop_exp, HealthCrate = the
        /// Bandage booster icon, AmmoCrate = the Grenade weapon icon (null when the art is missing).</summary>
        static Sprite DropIcon(string type)
        {
            switch (type)
            {
                case "Treasure": return OriginalArt.Sprite("icons/icons_drops/drop_coins");
                case "PointsCrate": return OriginalArt.Sprite("icons/icons_drops/drop_exp");
                case "HealthCrate": return OriginalArt.Icon("Bandage");
                case "AmmoCrate": return BattleItems.Icon("Grenade") ?? OriginalArt.Icon("Grenade") ?? OriginalArt.Icon("BasicNuke");
            }
            return null;
        }

        /// <summary>Uniform scale that makes the sprite's longer side `size` world units.</summary>
        static float IconScale(Sprite s, float size)
        {
            var b = s.bounds.size;
            float m = Mathf.Max(b.x, b.y);
            return m > 0.01f ? size / m : 1f;
        }

        void FixedUpdate()
        {
            if (used || rb == null) return;
            age += Time.fixedDeltaTime;
            if (chute != null && age > 0.4f && rb.Vel().sqrMagnitude < 0.05f)
            {
                Destroy(chute);
                chute = null;
                rb.SetDrag(0.1f, 0.5f);
            }
            var t = BattleTerrain.I;
            if (t != null && (rb.position.y < t.WaterY - 0.5f || rb.position.y < -20f))
            {
                Fx.Splash(rb.position, 0.8f);
                Remove();
            }
        }

        void OnTriggerEnter2D(Collider2D other) => TryPick(other);
        void OnTriggerStay2D(Collider2D other) => TryPick(other);

        void TryPick(Collider2D other)
        {
            if (used || other == null || age < 0.2f) return;
            var p = other.GetComponent<Penguin>();
            if (p == null || !p.Alive) return;
            used = true;
            Fx.Sparks(Position, Theme.Primary, 18);
            if (icon != null) Fx.IconPop(icon, Position + Vector2.up * 0.5f, IconScale(icon, 1.3f));
            ctrl?.OnCratePicked(this, p);
            Remove();
        }

        public void TakeDamage(DamageInfo d)
        {
            if (used) return;
            if (d.impulse.sqrMagnitude > 0.0001f && rb) rb.AddForce(d.impulse, ForceMode2D.Impulse);
            if (d.amount < 10f) return;
            Fx.Debris(Position, new Color(0.55f, 0.4f, 0.22f), 12);
            Remove();
        }

        void Remove()
        {
            used = true;
            BattleWorld.Objects.Remove(this);
            Destroy(gameObject);
        }

        void OnDestroy() { BattleWorld.Objects.Remove(this); }
    }
}
