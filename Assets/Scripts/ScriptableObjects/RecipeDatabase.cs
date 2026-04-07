using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    [System.Serializable]
    public class RecipeIngredientJson
    {
        public int itemId;
        public int quantity;
    }

    [System.Serializable]
    public class RecipeJson
    {
        public int recipeId;
        public string recipeName;
        public List<RecipeIngredientJson> inputs;
        public int outputItemId;
        public int outputQuantity;
        public float craftTime;
        public bool requiresBuilding;
    }

    [System.Serializable]
    public class RecipeListJson
    {
        public string version;
        public List<RecipeJson> recipes;
    }

    public class RecipeDatabase : MonoBehaviour
    {
        public static RecipeDatabase Instance { get; private set; }

        [SerializeField] private TextAsset recipesJsonAsset;

        public IReadOnlyList<RecipeJson> Recipes => _data?.recipes;

        private RecipeListJson _data;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Load();
        }

        private void Load()
        {
            if (recipesJsonAsset == null)
            {
                Debug.LogError("[RecipeDatabase] recipesJsonAsset is not assigned.");
                return;
            }
            _data = JsonUtility.FromJson<RecipeListJson>(recipesJsonAsset.text);
            Debug.Log($"[RecipeDatabase] Loaded {_data.recipes.Count} recipes (schema v{_data.version}).");
        }

        public RecipeJson GetRecipe(int recipeId)
        {
            if (_data == null) return null;
            return _data.recipes.Find(r => r.recipeId == recipeId);
        }
    }
}
