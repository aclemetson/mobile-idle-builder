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
    public class RecipeKnowledgeService : SingletonMonoBehaviour<RecipeKnowledgeService>, IRecipeKnowledgeService
    {
        // Allows tests to inject a stub without needing a running MonoBehaviour.
        internal static IRecipeKnowledgeService OverrideForTests;
        internal static IRecipeKnowledgeService Current => OverrideForTests ?? Instance;

        internal const string FileName = "recipe_knowledge.json";

        [SerializeField] private TextAsset _defaultKnowledgeAsset;

        private RecipeKnowledgeSave _data = new();
        private string _filePath;

        // ── Lifecycle ────────────────────────────────────────────────────────

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;
            _filePath = Path.Combine(Application.persistentDataPath, FileName);
        }

        void Start()
        {
            Load();
            SyncWithRecipeDatabase();
        }

        /// <summary>
        /// Re-reads knowledge from disk, falling back to the default asset when the file is gone.
        /// This service survives scene loads, so a save wipe must reset it explicitly — otherwise it
        /// keeps the old run's unlocks in memory and writes them straight back out on the next
        /// MarkKnown(), resurrecting the file the wipe just deleted.
        /// </summary>
        public void ResetInMemory()
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

        /// <summary>
        /// Number of codex entries the player has unlocked — i.e. the count of DISTINCT output
        /// items across all known recipes, which is exactly the row count
        /// <c>HUDController.BuildCodexList</c> renders.
        ///
        /// Deliberately not a known-recipe count: several recipes can produce the same item, so
        /// counting recipes would over-report and let "Unlock 10 codex entries" complete while the
        /// panel still shows fewer than 10 rows.
        ///
        /// Note this is the *implemented* codex ("items whose recipe you know"), which diverges from
        /// the originally designed one ("items you have crafted") — <c>SaveData.codex</c> and
        /// <c>ItemSO.codexUnlocked</c> are both dead and never written. The panel is the source of
        /// truth, so the achievement matches the panel.
        ///
        /// Takes its inputs explicitly so the counting rule is unit-testable without the
        /// RecipeDatabase / RecipeKnowledgeService singletons.
        /// </summary>
        internal static int CountCodexEntries(IReadOnlyList<RecipeJson> recipes,
                                              IRecipeKnowledgeService knowledge)
        {
            if (recipes == null || knowledge == null) return 0;

            var seen = new HashSet<int>();
            foreach (var recipe in recipes)
            {
                if (recipe == null || !knowledge.IsKnown(recipe.id)) continue;
                var item = recipe.output != null ? ItemDatabase.GetStatic(recipe.output.id) : null;
                if (item != null) seen.Add(item.itemId);
            }
            return seen.Count;
        }

        /// <summary>Live-database overload of <see cref="CountCodexEntries"/>.</summary>
        public static int CountCodexEntries() =>
            CountCodexEntries(RecipeDatabase.Instance?.Recipes, Current);

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
                    GameLogger.Warning($"[RecipeKnowledgeService] Failed to read runtime save: {e}");
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
                    GameLogger.Warning($"[RecipeKnowledgeService] Failed to parse default asset: {e}");
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
                GameLogger.Warning("[RecipeKnowledgeService] RecipeDatabase not ready — skipping sync.");
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
                GameLogger.Error($"[RecipeKnowledgeService] Failed to save {FileName}: {e}");
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
