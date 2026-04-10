using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MobileIdleBuilder
{
    [Serializable]
    public class RecipeKnowledgeEntry
    {
        public string id;
        public bool previous_research;
    }

    [Serializable]
    public class RecipeKnowledgeSave
    {
        public List<RecipeKnowledgeEntry> entries = new();
    }

    /// <summary>
    /// Persists which recipes the player has ever unlocked across prestige runs.
    /// Backed by recipe_knowledge.json in Application.persistentDataPath.
    ///
    /// Assign _defaultKnowledgeAsset in the Inspector (Assets/Data/recipe_knowledge.json)
    /// to define the initial state — all entries start with previous_research=false.
    /// The runtime save file takes precedence once it exists.
    ///
    /// A recipe with previous_research=true but whose research is not currently active shows
    /// as disabled in the recipe list rather than being hidden entirely.
    /// </summary>
    [DefaultExecutionOrder(-70)]
    public class RecipeKnowledgeService : MonoBehaviour
    {
        const string FileName = "recipe_knowledge.json";

        [SerializeField] private TextAsset _defaultKnowledgeAsset;

        public static RecipeKnowledgeService Instance { get; private set; }

        private RecipeKnowledgeSave _data;
        private string _filePath;

        // ── Lifecycle ────────────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            _filePath = Path.Combine(Application.persistentDataPath, FileName);
        }

        void Start()
        {
            Load();
            SyncWithRecipeDatabase();
        }

        // ── Query ────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true if the player has previously unlocked this recipe
        /// through research in any past prestige run.
        /// </summary>
        public bool IsKnown(string recipeId)
        {
            var entry = FindEntry(recipeId);
            return entry != null && entry.previous_research;
        }

        // ── Mutation ─────────────────────────────────────────────────────────

        /// <summary>Marks a recipe as known (previous_research=true) and saves.</summary>
        public void MarkKnown(string recipeId)
        {
            var entry = FindEntry(recipeId);
            if (entry == null)
            {
                entry = new RecipeKnowledgeEntry { id = recipeId };
                _data.entries.Add(entry);
            }

            if (!entry.previous_research)
            {
                entry.previous_research = true;
                Save();
            }
        }

        // ── Internal ─────────────────────────────────────────────────────────

        private void Load()
        {
            // 1. Try the runtime save file first
            if (File.Exists(_filePath))
            {
                try
                {
                    string json = File.ReadAllText(_filePath);
                    var loaded = JsonUtility.FromJson<RecipeKnowledgeSave>(json);
                    if (loaded?.entries != null)
                    {
                        _data = loaded;
                        return;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[RecipeKnowledgeService] Failed to read runtime save: {e.Message}");
                }
            }

            // 2. Fall back to the default asset assigned in the Inspector
            if (_defaultKnowledgeAsset != null)
            {
                try
                {
                    var loaded = JsonUtility.FromJson<RecipeKnowledgeSave>(_defaultKnowledgeAsset.text);
                    if (loaded?.entries != null)
                    {
                        _data = loaded;
                        return;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[RecipeKnowledgeService] Failed to parse default asset: {e.Message}");
                }
            }

            // 3. Start empty — SyncWithRecipeDatabase will populate entries
            _data = new RecipeKnowledgeSave();
        }

        /// <summary>
        /// Adds any recipes that exist in RecipeDatabase but are missing from the loaded data.
        /// New entries default to previous_research=false.
        /// Writes the runtime file if any entries were added.
        /// </summary>
        private void SyncWithRecipeDatabase()
        {
            var db = RecipeDatabase.Instance;
            if (db == null || db.Recipes == null)
            {
                Debug.LogWarning("[RecipeKnowledgeService] RecipeDatabase not ready — skipping sync.");
                return;
            }

            bool dirty = false;
            foreach (var recipe in db.Recipes)
            {
                if (FindEntry(recipe.id) == null)
                {
                    _data.entries.Add(new RecipeKnowledgeEntry
                    {
                        id = recipe.id,
                        previous_research = false
                    });
                    dirty = true;
                }
            }

            if (dirty)
                Save();
        }

        private void Save()
        {
            try
            {
                string json = JsonUtility.ToJson(_data, prettyPrint: true);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[RecipeKnowledgeService] Failed to save {FileName}: {e.Message}");
            }
        }

        private RecipeKnowledgeEntry FindEntry(string id)
        {
            foreach (var e in _data.entries)
                if (e.id == id) return e;
            return null;
        }
    }
}
