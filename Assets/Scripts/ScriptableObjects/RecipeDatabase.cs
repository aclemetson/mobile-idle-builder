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
        public string method_label;     // optional short method name, e.g. "p-p chain" (#336)
        public int tier;
        public List<RecipeIngredientJson> inputs;
        public RecipeOutputJson output;
        public List<RecipeIngredientJson> byproducts;   // optional; escaping ones pay entropy per craft
        public float base_craft_time;
        public List<string> crafted_in; // e.g. ["inventory_menu", "atomic_assembler"]
        public string requires_research;
        public string unlocks_research;

        /// <summary>
        /// Entropy paid per craft by byproducts that escape (neutrinos, gamma photons), looked up in
        /// <paramref name="items"/>. Mirrors <see cref="RecipeSO.EscapeEntropyPerCraft"/>.
        /// </summary>
        public float EscapeEntropyPerCraft(ItemDatabase items)
        {
            if (byproducts == null || items == null) return 0f;
            float total = 0f;
            foreach (var b in byproducts)
            {
                var item = items.Get(b.id);
                if (item != null && item.escapesAsEntropy && b.quantity > 0)
                    total += b.quantity * item.baseSellValue;
            }
            return total;
        }

        // Requires a dedicated building — not available in the inventory craft menu
        public bool requiresBuilding =>
            crafted_in == null || !crafted_in.Contains("inventory_menu");
    }

    /// <summary>
    /// Runtime recipe list for the Recipes panel, codex and manual crafting. Built from the RecipeSO assets
    /// that GameDataImporter writes into Resources/RecipeDatabase.asset, so game_data.json is the only
    /// recipe source (#287, #336). Consumers keep using the RecipeJson view type.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public class RecipeDatabase : SingletonMonoBehaviour<RecipeDatabase>
    {
        /// <summary>crafted_in entry marking a recipe the player can craft from the menu without a building.</summary>
        public const string InventoryMenuSource = "inventory_menu";

        protected override bool PersistAcrossScenes => false;

        // Only valid recipe entries: ones with no inputs (harvest recipes) or no output are excluded
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
            var db = Resources.Load<RecipeDatabaseSO>("RecipeDatabase");
            if (db == null || db.allRecipes == null)
            {
                GameLogger.Error("[RecipeDatabase] Resources/RecipeDatabase.asset is missing. Run MobileIdleBuilder > Import Game Data.");
                _validRecipes = new List<RecipeJson>();
                return;
            }

            _validRecipes = ConvertAll(db.allRecipes);
            GameLogger.Info($"[RecipeDatabase] Loaded {_validRecipes.Count} recipes.");
        }

        public RecipeJson GetRecipe(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _validRecipes?.Find(r => r.id == id)
                ?? _validRecipes?.Find(r => r.id == LegacyRecipeIds.Resolve(id));
        }

        /// <summary>Converts recipe assets to the runtime view, dropping the ones <see cref="ParseValidRecipes"/> rejects.</summary>
        public static List<RecipeJson> ConvertAll(IEnumerable<RecipeSO> assets)
        {
            var raw = new List<RecipeJson>();
            if (assets != null)
                foreach (var so in assets)
                {
                    var r = FromSO(so);
                    if (r != null) raw.Add(r);
                }
            return ParseValidRecipes(raw);
        }

        /// <summary>
        /// Converts one RecipeSO to its runtime view. crafted_in is the valid buildings' ids, preceded by
        /// "inventory_menu" when the recipe can be crafted manually. Null for a null asset.
        /// </summary>
        public static RecipeJson FromSO(RecipeSO so)
        {
            if (so == null) return null;

            var crafted = new List<string>();
            if (so.canCraftManually) crafted.Add(InventoryMenuSource);
            if (so.validBuildings != null)
                foreach (var b in so.validBuildings)
                    if (b != null && !string.IsNullOrEmpty(b.id)) crafted.Add(b.id);

            return new RecipeJson
            {
                id               = so.id,
                name             = string.IsNullOrEmpty(so.displayName) ? so.id : so.displayName,
                method_label     = so.methodLabel,
                tier             = so.tier,
                inputs           = ToIngredients(so.inputs),
                output           = new RecipeOutputJson
                {
                    id       = so.outputItem != null ? so.outputItem.id : null,
                    quantity = so.outputQuantity,
                },
                byproducts       = ToIngredients(so.byproducts),
                base_craft_time  = so.baseCraftTime,
                crafted_in       = crafted,
                requires_research = so.requiredResearch != null ? so.requiredResearch.id : null,
                unlocks_research  = so.unlocksResearch != null ? so.unlocksResearch.id : null,
            };
        }

        private static List<RecipeIngredientJson> ToIngredients(RecipeIngredient[] src)
        {
            var list = new List<RecipeIngredientJson>();
            if (src == null) return list;
            foreach (var i in src)
                if (i.item != null)
                    list.Add(new RecipeIngredientJson { id = i.item.id, quantity = i.quantity });
            return list;
        }

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
                    // Filter inputs with quantity 0 (documentation-only entries)
                    r.inputs = r.inputs.Where(i => i.quantity > 0).ToList();
                    return r;
                })
                .ToList();
        }
    }
}
