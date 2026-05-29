using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MobileIdleBuilder
{
    [Serializable]
    public class RecipeIngredientJson
    {
        public string id;       // e.g. "up_quark"
        public int quantity;
    }

    [Serializable]
    public class RecipeOutputJson
    {
        public string id;
        public int quantity;
    }

    [Serializable]
    public class RecipeJson
    {
        public string id;               // e.g. "proton"
        public string name;             // e.g. "Proton"
        public int tier;
        public List<RecipeIngredientJson> inputs;
        public RecipeOutputJson output;
        public float base_craft_time;
        public List<string> crafted_in; // e.g. ["inventory_menu", "atomic_assembler"]
        public string requires_research;
        public string unlocks_research;

        // Requires a dedicated building — not available in the inventory craft menu
        public bool requiresBuilding =>
            crafted_in == null || !crafted_in.Contains("inventory_menu");
    }

    [Serializable]
    public class RecipeListJson
    {
        public List<RecipeJson> recipes;
    }

    [DefaultExecutionOrder(-80)]
    public class RecipeDatabase : SingletonMonoBehaviour<RecipeDatabase>
    {
        protected override bool PersistAcrossScenes => true;

        [SerializeField] private TextAsset recipesJsonAsset;

        // Only valid recipe entries — _section markers and byproduct-only entries excluded
        public IReadOnlyList<RecipeJson> Recipes => _validRecipes;

        private List<RecipeJson> _validRecipes;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;
            Load();
        }

        private void Load()
        {
            if (recipesJsonAsset == null)
            {
                GameLogger.Error("[RecipeDatabase] recipesJsonAsset is not assigned.");
                return;
            }

            var raw = JsonUtility.FromJson<RecipeListJson>(recipesJsonAsset.text);
            _validRecipes = ParseValidRecipes(raw.recipes);
            GameLogger.Info($"[RecipeDatabase] Loaded {_validRecipes.Count} recipes.");
        }

        public RecipeJson GetRecipe(string id) =>
            _validRecipes?.Find(r => r.id == id);

        // Shared parsing logic used by both the runtime and tests
        public static List<RecipeJson> ParseValidRecipes(List<RecipeJson> raw)
        {
            return raw
                .Where(r =>
                    !string.IsNullOrEmpty(r.id) &&
                    r.inputs != null && r.inputs.Count > 0 &&
                    r.output != null && !string.IsNullOrEmpty(r.output.id))
                .Select(r =>
                {
                    // Filter inputs with quantity 0 (used in JSON as documentation e.g. hydrogen has 0 neutrons)
                    r.inputs = r.inputs.Where(i => i.quantity > 0).ToList();
                    return r;
                })
                .ToList();
        }
    }
}
