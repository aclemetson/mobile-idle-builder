using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// PlayMode tests for RecipeKnowledgeService (load paths, IsKnown, MarkKnown, cross-prestige persistence).
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

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_go != null)
            {
                Object.Destroy(_go);
                _go = null;
                yield return null;
            }

            if (File.Exists(_filePath)) File.Delete(_filePath);
            if (_backup != null) File.WriteAllText(_filePath, _backup);
        }

        IEnumerator SpawnService()
        {
            _go = new GameObject("RecipeKnowledgeService");
            _go.AddComponent<RecipeKnowledgeService>();
            yield return null;
        }

        // ── IsKnown ──────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator IsKnown_ReturnsFalse_ForUnknownId()
        {
            yield return SpawnService();
            Assert.IsFalse(RecipeKnowledgeService.Instance.IsKnown(TestRecipeId),
                "Unknown recipe id must not be considered known");
        }

        [UnityTest]
        public IEnumerator IsKnown_ReturnsFalse_WhenEntryExistsButPreviousResearchFalse()
        {
            string json = $"{{\"entries\":[{{\"id\":\"{TestRecipeId}\",\"previous_research\":false}}]}}";
            File.WriteAllText(_filePath, json);

            yield return SpawnService();

            Assert.IsFalse(RecipeKnowledgeService.Instance.IsKnown(TestRecipeId),
                "Entry with previous_research=false must not be considered known");
        }

        // ── MarkKnown ────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator MarkKnown_SetsIsKnownTrue()
        {
            yield return SpawnService();

            RecipeKnowledgeService.Instance.MarkKnown(TestRecipeId);

            Assert.IsTrue(RecipeKnowledgeService.Instance.IsKnown(TestRecipeId),
                "Recipe must be known after MarkKnown");
        }

        [UnityTest]
        public IEnumerator MarkKnown_Idempotent_SecondCallDoesNotThrow()
        {
            yield return SpawnService();

            RecipeKnowledgeService.Instance.MarkKnown(TestRecipeId);

            Assert.DoesNotThrow(() => RecipeKnowledgeService.Instance.MarkKnown(TestRecipeId),
                "Calling MarkKnown twice for the same id must not throw");
            Assert.IsTrue(RecipeKnowledgeService.Instance.IsKnown(TestRecipeId));
        }

        // ── Cross-prestige persistence ────────────────────────────────────────

        [UnityTest]
        public IEnumerator MarkKnown_PersistsAcrossServiceRecreation()
        {
            yield return SpawnService();
            RecipeKnowledgeService.Instance.MarkKnown(TestRecipeId);
            Object.Destroy(_go);
            _go = null;
            yield return null;

            yield return SpawnService();

            Assert.IsTrue(RecipeKnowledgeService.Instance.IsKnown(TestRecipeId),
                "IsKnown must return true after the service is destroyed and recreated");
        }

        // ── Load paths ───────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Load_ReadsExistingRuntimeFile_WithPreviousResearchTrue()
        {
            string json = $"{{\"entries\":[{{\"id\":\"{TestRecipeId}\",\"previous_research\":true}}]}}";
            File.WriteAllText(_filePath, json);

            yield return SpawnService();

            Assert.IsTrue(RecipeKnowledgeService.Instance.IsKnown(TestRecipeId),
                "Service must read previous_research=true from an existing runtime save file");
        }

        [UnityTest]
        public IEnumerator Load_FallsBackToEmptyState_WhenNoFileOrDefaultAsset()
        {
            // No file on disk and _defaultKnowledgeAsset is null (not wired in tests)
            yield return SpawnService();

            Assert.IsFalse(RecipeKnowledgeService.Instance.IsKnown(TestRecipeId),
                "Service must start with no known recipes when neither a save file nor a default asset exists");
        }

        [UnityTest]
        public IEnumerator Load_HandlesMalformedSaveFile_FallsBackToEmptyState()
        {
            File.WriteAllText(_filePath, "not valid json{{}}");

            yield return SpawnService();

            // Service should start without crashing; knowledge state is empty
            Assert.IsNotNull(RecipeKnowledgeService.Instance,
                "Service must initialise successfully even when the save file is malformed");
            Assert.IsFalse(RecipeKnowledgeService.Instance.IsKnown(TestRecipeId));
        }

        // ── SyncWithRecipeDatabase ────────────────────────────────────────────

        [UnityTest]
        public IEnumerator SyncWithRecipeDatabase_SkipsGracefully_WhenDatabaseIsNull()
        {
            // RecipeDatabase.Instance is null because no RecipeDatabase MonoBehaviour is present.
            // Start() must complete without throwing a NullReferenceException.
            yield return SpawnService();

            Assert.IsNotNull(RecipeKnowledgeService.Instance,
                "Service must initialise correctly even when RecipeDatabase is unavailable");
        }
    }
}
