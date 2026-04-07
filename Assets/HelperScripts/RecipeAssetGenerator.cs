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
        private const string ItemsJsonPath   = "Assets/Data/items.json";
        private const string RecipesJsonPath = "Assets/Data/recipes.json";
        private const string ItemsOutputDir  = "Assets/Data/Items";
        private const string RecipesOutputDir = "Assets/Data/Recipes";

        [System.Serializable]
        private class ItemJson
        {
            public int    itemId;
            public string itemName;
            public int    tierLevel;
            public string codexDescription;
        }

        [System.Serializable]
        private class ItemListJson
        {
            public string version;
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
                Debug.LogError($"[Generator] Could not find {ItemsJsonPath}");
                return;
            }
            if (recipesJson == null)
            {
                Debug.LogError($"[Generator] Could not find {RecipesJsonPath}");
                return;
            }

            var itemList   = JsonUtility.FromJson<ItemListJson>(itemsJson.text);
            var recipeList = JsonUtility.FromJson<RecipeListJson>(recipesJson.text);

            EnsureDirectory(ItemsOutputDir);
            EnsureDirectory(RecipesOutputDir);

            // ── Generate ItemSO assets ─────────────────────────────────────

            var itemLookup = new Dictionary<int, ItemSO>();

            foreach (var itemData in itemList.items)
            {
                string assetPath = $"{ItemsOutputDir}/{Sanitize(itemData.itemName)}.asset";
                var so = AssetDatabase.LoadAssetAtPath<ItemSO>(assetPath);
                if (so == null)
                {
                    so = ScriptableObject.CreateInstance<ItemSO>();
                    AssetDatabase.CreateAsset(so, assetPath);
                }

                so.itemId           = itemData.itemId;
                so.itemName         = itemData.itemName;
                so.tierLevel        = itemData.tierLevel;
                so.codexDescription = itemData.codexDescription;

                EditorUtility.SetDirty(so);
                itemLookup[itemData.itemId] = so;
                Debug.Log($"[Generator] ItemSO: {itemData.itemName} (id {itemData.itemId})");
            }

            // ── Generate RecipeSO assets ───────────────────────────────────

            foreach (var recipeData in recipeList.recipes)
            {
                string assetPath = $"{RecipesOutputDir}/{Sanitize(recipeData.recipeName)}.asset";
                var so = AssetDatabase.LoadAssetAtPath<RecipeSO>(assetPath);
                if (so == null)
                {
                    so = ScriptableObject.CreateInstance<RecipeSO>();
                    AssetDatabase.CreateAsset(so, assetPath);
                }

                so.recipeId       = recipeData.recipeId;
                so.craftTime      = recipeData.craftTime;
                so.outputQuantity = recipeData.outputQuantity;
                so.requiresBuilding = recipeData.requiresBuilding;

                // Wire output
                so.output = itemLookup.TryGetValue(recipeData.outputItemId, out var outputItem)
                    ? outputItem
                    : null;

                if (so.output == null)
                    Debug.LogWarning($"[Generator] Recipe '{recipeData.recipeName}': output itemId {recipeData.outputItemId} not found in items.json");

                // Wire inputs
                int inputCount = recipeData.inputs?.Count ?? 0;
                so.inputs          = new ItemSO[inputCount];
                so.inputQuantities = new int[inputCount];

                for (int i = 0; i < inputCount; i++)
                {
                    var inputData = recipeData.inputs[i];
                    so.inputs[i]          = itemLookup.TryGetValue(inputData.itemId, out var inputItem) ? inputItem : null;
                    so.inputQuantities[i] = inputData.quantity;

                    if (so.inputs[i] == null)
                        Debug.LogWarning($"[Generator] Recipe '{recipeData.recipeName}': input itemId {inputData.itemId} not found in items.json");
                }

                EditorUtility.SetDirty(so);
                Debug.Log($"[Generator] RecipeSO: {recipeData.recipeName} (id {recipeData.recipeId})");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Generator] Done — {itemList.items.Count} items, {recipeList.recipes.Count} recipes.");
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
