using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// PlayMode tests for ResearchService (unlock state, purchase affordability, event firing).
    ///
    /// ResearchService.Awake() loads ResearchDatabaseSO from Resources — returns null in tests,
    /// so _allResearch becomes Array.Empty. We pass ResearchSO instances directly to
    /// CanPurchase / Purchase so no database asset is required.
    ///
    /// Depends on SaveManager (Purchase → SaveManager.Instance.SaveLocal) and on
    /// World.DefaultGameObjectInjectionWorld being set before the service's Start() fires.
    /// </summary>
    [TestFixture]
    public class ResearchServiceTests
    {
        string _savePath;
        string _saveBackup;

        GameObject _saveManagerGO;
        GameObject _serviceGO;

        World         _testWorld;
        EntityManager _em;
        Entity        _playerEntity;

        ResearchSO _resA; // no prerequisites, costBaseCurrency=100
        ResearchSO _resB; // requires _resA, costBaseCurrency=200

        [SetUp]
        public void SetUp()
        {
            _savePath   = Path.Combine(Application.persistentDataPath, "save.json");
            _saveBackup = File.Exists(_savePath) ? File.ReadAllText(_savePath) : null;
            if (File.Exists(_savePath)) File.Delete(_savePath);

            _testWorld    = new World("ResearchServiceTestWorld");
            _em           = _testWorld.EntityManager;
            _playerEntity = _em.CreateEntity();
            _em.AddComponentData(_playerEntity, new PlayerProgressData { BaseCurrency = 1000L });

            World.DefaultGameObjectInjectionWorld = _testWorld;

            _resA = ScriptableObject.CreateInstance<ResearchSO>();
            _resA.id               = "test_res_a";
            _resA.displayName      = "Test A";
            _resA.costBaseCurrency = 100;
            _resA.prerequisites    = null;

            _resB = ScriptableObject.CreateInstance<ResearchSO>();
            _resB.id               = "test_res_b";
            _resB.displayName      = "Test B";
            _resB.costBaseCurrency = 200;
            _resB.prerequisites    = new[] { _resA };
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_serviceGO    != null) { Object.Destroy(_serviceGO);    _serviceGO    = null; }
            if (_saveManagerGO != null) { Object.Destroy(_saveManagerGO); _saveManagerGO = null; }
            yield return null;

            if (_testWorld.IsCreated) _testWorld.Dispose();
            World.DefaultGameObjectInjectionWorld = null;

            if (File.Exists(_savePath)) File.Delete(_savePath);
            if (_saveBackup != null) File.WriteAllText(_savePath, _saveBackup);

            if (_resA != null) { Object.Destroy(_resA); _resA = null; }
            if (_resB != null) { Object.Destroy(_resB); _resB = null; }
        }

        IEnumerator SpawnServices()
        {
            _saveManagerGO = new GameObject("SaveManager");
            _saveManagerGO.AddComponent<SaveManager>();
            yield return null;

            _serviceGO = new GameObject("ResearchService");
            _serviceGO.AddComponent<ResearchService>();
            yield return null;
        }

        // ── IsUnlocked ───────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator IsUnlocked_FalseByDefault()
        {
            yield return SpawnServices();
            Assert.IsFalse(ResearchService.Instance.IsUnlocked("test_res_a"),
                "No research is unlocked by default");
        }

        // ── CanPurchase ──────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator CanPurchase_FalseWhenAlreadyUnlocked()
        {
            _saveManagerGO = new GameObject("SaveManager");
            _saveManagerGO.AddComponent<SaveManager>();
            yield return null;

            // Inject the id into save data before ResearchService.Start() reads it
            SaveManager.Instance.Current.unlockedResearch.Add("test_res_a");

            _serviceGO = new GameObject("ResearchService");
            _serviceGO.AddComponent<ResearchService>();
            yield return null;

            Assert.IsFalse(ResearchService.Instance.CanPurchase(_resA),
                "Already-unlocked research must not be purchasable");
        }

        [UnityTest]
        public IEnumerator CanPurchase_FalseWhenPrerequisiteNotMet()
        {
            yield return SpawnServices();
            Assert.IsFalse(ResearchService.Instance.CanPurchase(_resB),
                "Research with an unmet prerequisite must not be purchasable");
        }

        [UnityTest]
        public IEnumerator CanPurchase_TrueWhenPrerequisitesMet_AndEnoughCurrency()
        {
            yield return SpawnServices();
            // resA: no prerequisites, player BaseCurrency=1000, cost=100
            Assert.IsTrue(ResearchService.Instance.CanPurchase(_resA),
                "Research with met prerequisites and sufficient entropy must be purchasable");
        }

        // ── Purchase ─────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Purchase_AddsToIsUnlocked()
        {
            yield return SpawnServices();
            ResearchService.Instance.Purchase(_resA);
            Assert.IsTrue(ResearchService.Instance.IsUnlocked(_resA.id),
                "Research must be marked unlocked after Purchase");
        }

        [UnityTest]
        public IEnumerator Purchase_FiresOnResearchUnlocked()
        {
            yield return SpawnServices();

            ResearchSO fired = null;
            ResearchService.Instance.OnResearchUnlocked += r => fired = r;
            ResearchService.Instance.Purchase(_resA);

            Assert.AreEqual(_resA, fired, "OnResearchUnlocked must fire with the purchased ResearchSO");
        }

        [UnityTest]
        public IEnumerator Purchase_DeductsCostFromBaseCurrency()
        {
            yield return SpawnServices();
            ResearchService.Instance.Purchase(_resA); // costBaseCurrency=100

            var progress = _em.GetComponentData<PlayerProgressData>(_playerEntity);
            Assert.AreEqual(900L, progress.BaseCurrency,
                "Purchase must deduct costBaseCurrency from PlayerProgressData.BaseCurrency");
        }
    }
}
