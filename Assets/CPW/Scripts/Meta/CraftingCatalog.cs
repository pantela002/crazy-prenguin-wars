using System.Collections.Generic;
using UnityEngine;

namespace CPW
{
    public class IngredientDef
    {
        public string id, name, description;
        public bool rare;
        public Color color;
        // what a failed research gives back per ingredient (original CraftingItem RewardCoins/RewardCash/RewardExp)
        public int failCoins, failXp, failCash;
    }

    public class RecipeDef
    {
        public string id, name;
        public string[] ingredients;        // order does not matter
        public string resultId;             // Item id (ammo), Bonus id (clothes) or ingredient
        public int resultAmount = 1;
    }

    /// <summary>
    /// Crafting ("Research") as in the original crafting screen: put up to 3 ingredients in a research slot,
    /// wait Tuner.ResearchDuration (45 min) or finish instantly for Tuner.ResearchInstantCompleteCost fish, then
    /// collect. A matching recipe gives its result and becomes known; otherwise each ingredient pays back its
    /// small fail reward (coins/xp/cash), exactly like Research.failCoins/failExp/failCash.
    ///
    /// INVENTED DATA: the original Crafting/Recipe item rows were not in the shipped config, so the remake defines
    /// 8 ingredients (5 common, 3 rare) and 12 recipes below. Ingredients come from the slot machine
    /// (Win_2_Crafting_Common → 1 common, Win_3_Crafting_Rare → 1 rare), from battles (RewardService: 35% chance
    /// of a common one after a win, 15% after a loss, 5% rare) and the daily gift (day 7).
    /// Ingredient counts are stored in PlayerProfile.counters as "ing.{id}"; a running research stores its
    /// ingredients in ResearchState.recipeId as "a+b+c".
    /// </summary>
    public static class CraftingCatalog
    {
        public const int SlotsPerLab = 3;
        public const int Labs = 2;
        const string Prefix = "ing.";

        static List<IngredientDef> ingredients;
        static List<RecipeDef> recipes;

        public static List<IngredientDef> Ingredients
        {
            get
            {
                if (ingredients != null) return ingredients;
                ingredients = new List<IngredientDef>
                {
                    I("ScrapMetal", "Scrap Metal", "Bent bits of old rockets.", false, new Color32(150, 160, 175, 255), 40, 10, 0),
                    I("Gunpowder", "Gunpowder", "Handle with care. Keep away from fish fryers.", false, new Color32(70, 70, 80, 255), 50, 10, 0),
                    I("FishBones", "Fish Bones", "Leftovers from a very good lunch.", false, new Color32(235, 225, 190, 255), 30, 15, 0),
                    I("IceShard", "Ice Shard", "Never melts. Penguin science.", false, new Color32(150, 220, 255, 255), 30, 15, 0),
                    I("RubberDuck", "Rubber Duck", "Squeaks menacingly.", false, new Color32(255, 214, 50, 255), 60, 5, 0),
                    I("PlasmaCore", "Plasma Core", "Hums with alien energy.", true, new Color32(170, 90, 255, 255), 150, 40, 1),
                    I("GoldenFeather", "Golden Feather", "Fell off a very fancy penguin.", true, new Color32(255, 190, 40, 255), 200, 30, 1),
                    I("UraniumPebble", "Uranium Pebble", "Glows in the dark. Do not lick.", true, new Color32(120, 230, 80, 255), 150, 50, 1),
                };
                return ingredients;
            }
        }

        static IngredientDef I(string id, string name, string desc, bool rare, Color c, int coins, int xp, int cash)
            => new IngredientDef { id = id, name = name, description = desc, rare = rare, color = c, failCoins = coins, failXp = xp, failCash = cash };

        public static List<RecipeDef> Recipes
        {
            get
            {
                if (recipes != null) return recipes;
                recipes = new List<RecipeDef>
                {
                    R("RecipeDynamite", "Dynamite Pack", "Dynamite", 5, "Gunpowder", "Gunpowder", "ScrapMetal"),
                    R("RecipeMolotov", "Molotov Cocktails", "Molotov", 4, "Gunpowder", "FishBones", "Gunpowder"),
                    R("RecipeClusterGrenade", "Cluster Grenades", "ClusterGrenade", 3, "Gunpowder", "ScrapMetal", "IceShard"),
                    R("RecipeMines", "Mine Field", "Mine", 3, "ScrapMetal", "ScrapMetal", "ScrapMetal"),
                    R("RecipeShield", "Ice Shield", "Shield", 2, "IceShard", "IceShard", "IceShard"),
                    R("RecipeSushi", "Salmon Sushi", "SalmonSushi", 2, "FishBones", "FishBones", "IceShard"),
                    R("RecipeConfetti", "Party Time", "Confetti", 5, "RubberDuck", "RubberDuck", "Gunpowder"),
                    R("RecipeWaterBalloon", "Water Balloons", "WaterBalloon", 8, "RubberDuck", "IceShard", "FishBones"),
                    R("RecipePlasmaCannon", "Plasma Cannon", "PlasmaCannon", 3, "PlasmaCore", "ScrapMetal", "ScrapMetal"),
                    R("RecipeMegaNuke", "Mega Nuke", "MegaNuke", 2, "UraniumPebble", "Gunpowder", "PlasmaCore"),
                    R("RecipeDoomsday", "Doomsday Device", "DoomsdayDevice", 1, "UraniumPebble", "UraniumPebble", "PlasmaCore"),
                    R("RecipeKingCrown", "Royal Crown", "king_head", 1, "GoldenFeather", "GoldenFeather", "ScrapMetal"),
                };
                return recipes;
            }
        }

        static RecipeDef R(string id, string name, string result, int amount, params string[] ing)
            => new RecipeDef { id = id, name = name, resultId = result, resultAmount = amount, ingredients = ing };

        public static bool IsIngredient(string id) { foreach (var i in Ingredients) if (i.id == id) return true; return false; }
        public static IngredientDef Ingredient(string id) { foreach (var i in Ingredients) if (i.id == id) return i; return null; }

        public static int Count(string id) => ProfileService.P.Counter(Prefix + id);
        public static void AddIngredient(string id, int amount) => ProfileService.P.AddCounter(Prefix + id, amount);

        public static string RandomIngredient(bool rare)
        {
            var pool = new List<IngredientDef>();
            foreach (var i in Ingredients) if (i.rare == rare) pool.Add(i);
            return pool[Random.Range(0, pool.Count)].id;
        }

        public static long DurationMs => GameData.Tuner != null ? (long)GameData.Tuner.Float("ResearchDuration", 2700000) : 2700000L;
        public static int InstantCost => GameData.Tuner != null ? GameData.Tuner.Int("ResearchInstantCompleteCost", 5) : 5;

        /// <summary>The recipe whose ingredients match exactly (as a multiset), or null.</summary>
        public static RecipeDef Match(IList<string> ing)
        {
            foreach (var r in Recipes)
            {
                if (r.ingredients.Length != ing.Count) continue;
                var left = new List<string>(ing);
                bool ok = true;
                foreach (var x in r.ingredients) if (!left.Remove(x)) { ok = false; break; }
                if (ok) return r;
            }
            return null;
        }

        // ---------- research slots (PlayerProfile.research) ----------
        public static ResearchState Lab(int index)
        {
            var list = ProfileService.P.research;
            return index < list.Count ? list[index] : null;
        }

        public static bool LabBusy(int index) { var l = Lab(index); return l != null && !string.IsNullOrEmpty(l.recipeId); }

        public static string[] LabIngredients(int index)
        {
            var l = Lab(index);
            return l == null || string.IsNullOrEmpty(l.recipeId) ? new string[0] : l.recipeId.Split('+');
        }

        public static double LabSecondsLeft(int index)
        {
            var l = Lab(index);
            if (l == null) return 0;
            return Mathf.Max(0, (l.finishUnixMs - MetaUI.NowMs) / 1000f);
        }

        public static bool LabDone(int index) => LabBusy(index) && LabSecondsLeft(index) <= 0;

        /// <summary>Consume the ingredients and start the research timer in a free lab.</summary>
        public static bool StartResearch(int lab, List<string> ing)
        {
            if (ing.Count == 0 || LabBusy(lab)) return false;
            var need = new Dictionary<string, int>();
            foreach (var i in ing) need[i] = (need.TryGetValue(i, out var n) ? n : 0) + 1;
            foreach (var kv in need) if (Count(kv.Key) < kv.Value) { UI.Toast("Not enough " + Ingredient(kv.Key).name); return false; }
            foreach (var kv in need) AddIngredient(kv.Key, -kv.Value);
            var list = ProfileService.P.research;
            while (list.Count <= lab) list.Add(new ResearchState());
            list[lab].recipeId = string.Join("+", ing);
            list[lab].finishUnixMs = MetaUI.NowMs + DurationMs;
            ProfileService.Save();
            return true;
        }

        public static bool FinishNow(int lab)
        {
            if (!LabBusy(lab) || LabDone(lab)) return false;
            if (!Progression.Spend(0, InstantCost)) { Progression.NotEnough(true); return false; }
            Lab(lab).finishUnixMs = MetaUI.NowMs - 1;
            ProfileService.Save();
            return true;
        }

        /// <summary>Collect a finished research. Returns a short description of what was gained.</summary>
        public static string Collect(int lab, out RecipeDef recipe)
        {
            recipe = null;
            if (!LabDone(lab)) return null;
            var ing = LabIngredients(lab);
            recipe = Match(ing);
            string text;
            var P = ProfileService.P;
            if (recipe != null)
            {
                Progression.GiveItem(recipe.resultId, recipe.resultAmount);
                bool fresh = !P.knownRecipes.Contains(recipe.id);
                if (fresh) P.knownRecipes.Add(recipe.id);
                text = (fresh ? "New recipe discovered: " + recipe.name + "!\n" : "") + "You crafted " + recipe.resultAmount + "x " + Progression.NameOf(recipe.resultId) + ".";
                ChallengeTracker.Report("crafted", 1);
            }
            else
            {
                int coins = 0, xp = 0, cash = 0;
                foreach (var i in ing) { var d = Ingredient(i); if (d == null) continue; coins += d.failCoins; xp += d.failXp; cash += d.failCash; }
                Progression.AddCoins(coins); Progression.AddCash(cash); Progression.AddXp(xp);
                text = "No recipe this time... The penguin scientists salvaged " + coins + " coins" + (xp > 0 ? ", " + xp + " XP" : "") + (cash > 0 ? " and " + cash + " Cash" : "") + ".";
            }
            Lab(lab).recipeId = "";
            Lab(lab).finishUnixMs = 0;
            ProfileService.Save();
            return text;
        }

        /// <summary>Number of finished researches waiting to be collected (home screen badge).</summary>
        public static int ReadyCount()
        {
            int n = 0;
            for (int i = 0; i < Labs; i++) if (LabDone(i)) n++;
            return n;
        }
    }
}
