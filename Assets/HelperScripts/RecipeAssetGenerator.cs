using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using MobileIdleBuilder;

namespace MobileIdleBuilder.HelperScripts
{
    /// <summary>
    /// Editor utility: reads Assets/Data/items.json and Assets/Data/recipes.json
    /// and generates ItemSO + RecipeSO assets under Assets/Data/Items/ and Assets/Data/Recipes/.
    ///
    /// Run via: MobileIdleBuilder → Generate Data Assets
    /// Safe to re-run — existing assets are updated in place, not duplicated.
    /// </summary>
    public static class RecipeAssetGenerator
    {
        private const string ItemsJsonPath    = "Assets/Data/items.json";
        private const string RecipesJsonPath  = "Assets/Data/recipes.json";
        private const string ItemsOutputDir   = "Assets/Data/Items";
        private const string RecipesOutputDir = "Assets/Data/Recipes";

        [System.Serializable]
        private class ItemJson
        {
            public string id;       // e.g. "up_quark"
            public string name;     // e.g. "Up Quark"
            public string symbol;   // e.g. "u", "p⁺", "H"
            public int    tier;
            public string codex;
        }

        [System.Serializable]
        private class ItemListJson
        {
            public List<ItemJson> items;
        }

        [MenuItem("MobileIdleBuilder/Generate Data Assets")]
        public static void Generate()
        {
            // ── Load JSON ──────────────────────────────────────────────────

            var itemsJson   = AssetDatabase.LoadAssetAtPath<TextAsset>(ItemsJsonPath);
            var recipesJson = AssetDatabase.LoadAssetAtPath<TextAsset>(RecipesJsonPath);

            if (itemsJson == null)
            {
                GameLogger.Error($"[Generator] Could not find {ItemsJsonPath}");
                return;
            }
            if (recipesJson == null)
            {
                GameLogger.Error($"[Generator] Could not find {RecipesJsonPath}");
                return;
            }

            var itemList   = JsonUtility.FromJson<ItemListJson>(itemsJson.text);
            var recipeList = JsonUtility.FromJson<RecipeListJson>(recipesJson.text);
            var validRecipes = RecipeDatabase.ParseValidRecipes(recipeList.recipes);

            EnsureDirectory(ItemsOutputDir);
            EnsureDirectory(RecipesOutputDir);

            // ── Generate ItemSO assets ─────────────────────────────────────
            // itemLookup keyed by string id (e.g. "up_quark")

            var itemLookup = new Dictionary<string, ItemSO>();
            int itemEcsId = 1;

            foreach (var itemData in itemList.items)
            {
                string assetPath = $"{ItemsOutputDir}/{Sanitize(itemData.id)}.asset";
                var so = AssetDatabase.LoadAssetAtPath<ItemSO>(assetPath);
                if (so == null)
                {
                    so = ScriptableObject.CreateInstance<ItemSO>();
                    AssetDatabase.CreateAsset(so, assetPath);
                }

                so.id          = itemData.id;
                so.itemId      = itemEcsId++;   // sequential int for ECS
                so.displayName = itemData.name;
                so.symbol      = itemData.symbol;
                so.tier        = itemData.tier;
                so.codexEntry  = itemData.codex;

                EditorUtility.SetDirty(so);
                itemLookup[itemData.id] = so;
                GameLogger.Debug($"[Generator] ItemSO: {itemData.name} (id '{itemData.id}')");
            }

            // ── Generate RecipeSO assets ───────────────────────────────────

            int recipeEcsId = 1;

            foreach (var recipeData in validRecipes)
            {
                string assetPath = $"{RecipesOutputDir}/{Sanitize(recipeData.id)}.asset";
                var so = AssetDatabase.LoadAssetAtPath<RecipeSO>(assetPath);
                if (so == null)
                {
                    so = ScriptableObject.CreateInstance<RecipeSO>();
                    AssetDatabase.CreateAsset(so, assetPath);
                }

                so.id             = recipeData.id;
                so.recipeId       = recipeEcsId++;   // sequential int for ECS
                so.displayName    = recipeData.name;
                so.baseCraftTime  = recipeData.base_craft_time;
                so.outputQuantity = recipeData.output.quantity;
                so.canCraftManually = !recipeData.requiresBuilding;

                // Wire output item
                so.outputItem = itemLookup.TryGetValue(recipeData.output.id, out var outputItem)
                    ? outputItem
                    : null;

                if (so.outputItem == null)
                    GameLogger.Warning($"[Generator] Recipe '{recipeData.name}': output '{recipeData.output.id}' not found in items.json");

                // Wire inputs as RecipeIngredient[]
                so.inputs = new RecipeIngredient[recipeData.inputs.Count];

                for (int i = 0; i < recipeData.inputs.Count; i++)
                {
                    var inputData = recipeData.inputs[i];
                    so.inputs[i] = new RecipeIngredient
                    {
                        item     = itemLookup.TryGetValue(inputData.id, out var inputItem) ? inputItem : null,
                        quantity = inputData.quantity
                    };

                    if (so.inputs[i].item == null)
                        GameLogger.Warning($"[Generator] Recipe '{recipeData.name}': input '{inputData.id}' not found in items.json");
                }

                EditorUtility.SetDirty(so);
                GameLogger.Debug($"[Generator] RecipeSO: {recipeData.name} (id '{recipeData.id}')");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            GameLogger.Info($"[Generator] Done — {itemList.items.Count} items, {validRecipes.Count} recipes.");
        }

        private static void EnsureDirectory(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string parent = Path.GetDirectoryName(path).Replace('\\', '/');
                string folder = Path.GetFileName(path);
                AssetDatabase.CreateFolder(parent, folder);
            }
        }

        private static string Sanitize(string name) =>
            name.Replace(" ", "_").Replace("/", "-").Replace("\\", "-");
    }
}
