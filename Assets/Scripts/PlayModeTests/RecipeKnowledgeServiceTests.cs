using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// EditMode tests for RecipeKnowledgeService (load paths, IsKnown, MarkKnown, cross-prestige persistence).
    ///
    /// RecipeDatabase.Instance is null in these tests — SyncWithRecipeDatabase() logs a warning
    /// and skips, so no database asset is required.
    ///
    /// _defaultKnowledgeAsset is not assigned (null) unless explicitly set; tests rely on the
    /// runtime file path or empty-state fallback.
    /// </summary>
    [TestFixture]
    public class RecipeKnowledgeServiceTests
    {
        const string KnowledgeFile = "recipe_knowledge.json";
        const string TestRecipeId  = "test_recipe_a";

        string _filePath;
        string _backup;
        GameObject _go;

        [SetUp]
        public void SetUp()
        {
            _filePath = Path.Combine(Application.persistentDataPath, KnowledgeFile);
            _backup   = File.Exists(_filePath) ? File.ReadAllText(_filePath) : null;
            if (File.Exists(_filePath)) File.Delete(_filePath);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
                _go = null;
            }

            if (File.Exists(_filePath)) File.Delete(_filePath);
            if (_backup != null) File.WriteAllText(_filePath, _backup);
        }

        void SpawnService()
        {
            _go = new GameObject("RecipeKnowledgeService");
            var svc = _go.AddComponent<RecipeKnowledgeService>();
            RunAwake(svc);
            RunStart(svc);
        }

        // ── IsKnown ──────────────────────────────────────────────────────────

        [Test]
        public void IsKnown_ReturnsFalse_ForUnknownId()
        {
            SpawnService();
            Assert.IsFalse(RecipeKnowledgeService.Instance.IsKnown(TestRecipeId),
                "Unknown recipe id must not be considered known");
        }

        [Test]
        public void IsKnown_ReturnsFalse_WhenEntryExistsButPreviousResearchFalse()
        {
            string json = $"{{\"entries\":[{{\"id\":\"{TestRecipeId}\",\"previous_research\":false}}]}}";
            File.WriteAllText(_filePath, json);

            SpawnService();

            Assert.IsFalse(RecipeKnowledgeService.Instance.IsKnown(TestRecipeId),
                "Entry with previous_research=false must not be considered known");
        }

        // ── MarkKnown ────────────────────────────────────────────────────────

        [Test]
        public void MarkKnown_SetsIsKnownTrue()
        {
            SpawnService();

            RecipeKnowledgeService.Instance.MarkKnown(TestRecipeId);

            Assert.IsTrue(RecipeKnowledgeService.Instance.IsKnown(TestRecipeId),
                "Recipe must be known after MarkKnown");
        }

        [Test]
        public void MarkKnown_Idempotent_SecondCallDoesNotThrow()
        {
            SpawnService();

            RecipeKnowledgeService.Instance.MarkKnown(TestRecipeId);

            Assert.DoesNotThrow(() => RecipeKnowledgeService.Instance.MarkKnown(TestRecipeId),
                "Calling MarkKnown twice for the same id must not throw");
            Assert.IsTrue(RecipeKnowledgeService.Instance.IsKnown(TestRecipeId));
        }

        // ── Cross-prestige persistence ────────────────────────────────────────

        [Test]
        public void MarkKnown_PersistsAcrossServiceRecreation()
        {
            SpawnService();
            RecipeKnowledgeService.Instance.MarkKnown(TestRecipeId);
            Object.DestroyImmediate(_go);
            _go = null;

            SpawnService();

            Assert.IsTrue(RecipeKnowledgeService.Instance.IsKnown(TestRecipeId),
                "IsKnown must return true after the service is destroyed and recreated");
        }

        // ── Load paths ───────────────────────────────────────────────────────

        [Test]
        public void Load_ReadsExistingRuntimeFile_WithPreviousResearchTrue()
        {
            string json = $"{{\"entries\":[{{\"id\":\"{TestRecipeId}\",\"previous_research\":true}}]}}";
            File.WriteAllText(_filePath, json);

            SpawnService();

            Assert.IsTrue(RecipeKnowledgeService.Instance.IsKnown(TestRecipeId),
                "Service must read previous_research=true from an existing runtime save file");
        }

        [Test]
        public void Load_FallsBackToEmptyState_WhenNoFileOrDefaultAsset()
        {
            // No file on disk and _defaultKnowledgeAsset is null (not wired in tests)
            SpawnService();

            Assert.IsFalse(RecipeKnowledgeService.Instance.IsKnown(TestRecipeId),
                "Service must start with no known recipes when neither a save file nor a default asset exists");
        }

        [Test]
        public void Load_HandlesMalformedSaveFile_FallsBackToEmptyState()
        {
            File.WriteAllText(_filePath, "not valid json{{}}");

            SpawnService();

            // Service should start without crashing; knowledge state is empty
            Assert.IsNotNull(RecipeKnowledgeService.Instance,
                "Service must initialise successfully even when the save file is malformed");
            Assert.IsFalse(RecipeKnowledgeService.Instance.IsKnown(TestRecipeId));
        }

        // ── SyncWithRecipeDatabase ────────────────────────────────────────────

        [Test]
        public void SyncWithRecipeDatabase_SkipsGracefully_WhenDatabaseIsNull()
        {
            // RecipeDatabase.Instance is null because no RecipeDatabase MonoBehaviour is present.
            // Start() must complete without throwing a NullReferenceException.
            SpawnService();

            Assert.IsNotNull(RecipeKnowledgeService.Instance,
                "Service must initialise correctly even when RecipeDatabase is unavailable");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        static void RunStart(MonoBehaviour mb) =>
            mb.GetType()
              .GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
              ?.Invoke(mb, null);

        static void RunAwake(MonoBehaviour mb)
        {
            var t = mb.GetType();
            while (t != null && t != typeof(MonoBehaviour))
            {
                var m = t.GetMethod("Awake",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (m != null) { m.Invoke(mb, null); return; }
                t = t.BaseType;
            }
        }
    }
}
