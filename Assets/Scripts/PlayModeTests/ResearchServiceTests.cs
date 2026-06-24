using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// EditMode tests for ResearchService (unlock state, purchase affordability, event firing).
    ///
    /// ResearchService.Awake() loads ResearchDatabaseSO from Resources — returns null in tests,
    /// so _allResearch becomes Array.Empty. We pass ResearchSO instances directly to
    /// CanPurchase / Purchase so no database asset is required.
    ///
    /// Depends on SaveManager (Purchase → SaveManager.Instance.SaveLocal) and on
    /// World.DefaultGameObjectInjectionWorld being set before the service's Start() fires.
    /// Start() is invoked explicitly via reflection since EditMode does not call it automatically.
    /// </summary>
    [TestFixture]
    public class ResearchServiceTests
    {
        string _savePath;
        string _saveBackup;

        GameObject _saveManagerGO;
        GameObject _serviceGO;

        TutorialFlowSO _tutorialFlow;

        World         _testWorld;
        EntityManager _em;
        Entity        _playerEntity;

        ResearchSO _resA; // no prerequisites, costBaseCurrency=100
        ResearchSO _resB; // requires _resA, costBaseCurrency=200
        ResearchSO _resTimed; // no prerequisites, costBaseCurrency=100, durationSeconds=60
        ResearchSO _resTimed2; // no prerequisites, costBaseCurrency=100, durationSeconds=3600

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

            _resTimed = ScriptableObject.CreateInstance<ResearchSO>();
            _resTimed.id               = "test_res_timed";
            _resTimed.displayName      = "Test Timed";
            _resTimed.costBaseCurrency = 100;
            _resTimed.prerequisites    = null;
            _resTimed.durationSeconds  = 60;

            _resTimed2 = ScriptableObject.CreateInstance<ResearchSO>();
            _resTimed2.id               = "test_res_timed2";
            _resTimed2.displayName      = "Test Timed 2";
            _resTimed2.costBaseCurrency = 100;
            _resTimed2.prerequisites    = null;
            _resTimed2.durationSeconds  = 3600;
        }

        [TearDown]
        public void TearDown()
        {
            if (_serviceGO    != null) { Object.DestroyImmediate(_serviceGO);    _serviceGO    = null; }
            if (_saveManagerGO != null) { Object.DestroyImmediate(_saveManagerGO); _saveManagerGO = null; }
            // Destroying clears TutorialFlowSO.Current via OnDisable (set in CreateInstance's OnEnable).
            if (_tutorialFlow != null) { Object.DestroyImmediate(_tutorialFlow); _tutorialFlow = null; }

            if (_testWorld.IsCreated) _testWorld.Dispose();
            World.DefaultGameObjectInjectionWorld = null;

            if (File.Exists(_savePath)) File.Delete(_savePath);
            if (_saveBackup != null) File.WriteAllText(_savePath, _saveBackup);

            if (_resA != null) { Object.DestroyImmediate(_resA); _resA = null; }
            if (_resB != null) { Object.DestroyImmediate(_resB); _resB = null; }
            if (_resTimed  != null) { Object.DestroyImmediate(_resTimed);  _resTimed  = null; }
            if (_resTimed2 != null) { Object.DestroyImmediate(_resTimed2); _resTimed2 = null; }
        }

        void SpawnServices()
        {
            _saveManagerGO = new GameObject("SaveManager");
            { var sm = _saveManagerGO.AddComponent<SaveManager>(); RunAwake(sm); }

            _serviceGO = new GameObject("ResearchService");
            var svc = _serviceGO.AddComponent<ResearchService>();
            RunAwake(svc);
            RunStart(svc); // Start() → ECS setup + load unlockedIds from SaveManager
        }

        // ── IsUnlocked ───────────────────────────────────────────────────────

        [Test]
        public void IsUnlocked_FalseByDefault()
        {
            SpawnServices();
            Assert.IsFalse(ResearchService.Instance.IsUnlocked("test_res_a"),
                "No research is unlocked by default");
        }

        // ── CanPurchase ──────────────────────────────────────────────────────

        [Test]
        public void CanPurchase_FalseWhenAlreadyUnlocked()
        {
            _saveManagerGO = new GameObject("SaveManager");
            { var sm = _saveManagerGO.AddComponent<SaveManager>(); RunAwake(sm); }

            // Inject the id into save data before ResearchService.Start() reads it
            SaveManager.Instance.Current.unlockedResearch.Add("test_res_a");

            _serviceGO = new GameObject("ResearchService");
            var svc = _serviceGO.AddComponent<ResearchService>();
            RunAwake(svc);
            RunStart(svc);

            Assert.IsFalse(ResearchService.Instance.CanPurchase(_resA),
                "Already-unlocked research must not be purchasable");
        }

        [Test]
        public void CanPurchase_FalseWhenPrerequisiteNotMet()
        {
            SpawnServices();
            Assert.IsFalse(ResearchService.Instance.CanPurchase(_resB),
                "Research with an unmet prerequisite must not be purchasable");
        }

        [Test]
        public void CanPurchase_TrueWhenPrerequisitesMet_AndEnoughCurrency()
        {
            SpawnServices();
            // resA: no prerequisites, player BaseCurrency=1000, cost=100
            Assert.IsTrue(ResearchService.Instance.CanPurchase(_resA),
                "Research with met prerequisites and sufficient entropy must be purchasable");
        }

        // ── Purchase ─────────────────────────────────────────────────────────

        [Test]
        public void Purchase_AddsToIsUnlocked()
        {
            SpawnServices();
            ResearchService.Instance.Purchase(_resA);
            Assert.IsTrue(ResearchService.Instance.IsUnlocked(_resA.id),
                "Research must be marked unlocked after Purchase");
        }

        [Test]
        public void Purchase_FiresOnResearchUnlocked()
        {
            SpawnServices();

            ResearchSO fired = null;
            ResearchService.Instance.OnResearchUnlocked += r => fired = r;
            ResearchService.Instance.Purchase(_resA);

            Assert.AreEqual(_resA, fired, "OnResearchUnlocked must fire with the purchased ResearchSO");
        }

        [Test]
        public void Purchase_DeductsCostFromBaseCurrency()
        {
            SpawnServices();
            ResearchService.Instance.Purchase(_resA); // costBaseCurrency=100

            var progress = _em.GetComponentData<PlayerProgressData>(_playerEntity);
            Assert.AreEqual(900L, progress.BaseCurrency,
                "Purchase must deduct costBaseCurrency from PlayerProgressData.BaseCurrency");
        }

        // ── Tutorial research-gate integration ───────────────────────────────

        [Test]
        public void ResearchGatedTutorialStep_AdvancesFromLiveServiceWhenSaveLayerOutOfSync()
        {
            // Reproduces the tutorial-stall bug: the research-panel "purchased" checkmark and
            // the research-gated tutorial step must read the SAME authoritative source — the
            // live ResearchService unlock set — not the persisted save list, which can be
            // absent or lagging (e.g. SaveManager not yet present / save not flushed). Here we
            // purchase, then deliberately clear the save layer's list to simulate that skew:
            // the gated step must still advance because the service reports the research unlocked.
            SpawnServices();

            // Tutorial ECS scaffolding on the existing player entity (already has PlayerProgressData).
            _em.AddComponent<TutorialStateData>(_playerEntity);
            _em.AddComponent<PlayerInventoryTag>(_playerEntity);
            _em.AddBuffer<InventorySlot>(_playerEntity);
            _em.SetComponentData(_playerEntity,
                new TutorialStateData { CurrentStepIndex = 0, IsActive = true });

            // CreateInstance fires OnEnable → sets TutorialFlowSO.Current.
            _tutorialFlow = ScriptableObject.CreateInstance<TutorialFlowSO>();
            _tutorialFlow.steps = new[]
            {
                new TutorialStepDef
                {
                    id = "buy_research",
                    advanceCondition = new TutorialConditionDef
                        { type = ConditionType.ResearchUnlocked, researchId = _resA.id },
                    onEnter = new TutorialOnEnter()
                },
                new TutorialStepDef
                {
                    id = "after_research",
                    advanceCondition = new TutorialConditionDef
                        { type = ConditionType.UiEvent, uiEventId = "noop" },
                    onEnter = new TutorialOnEnter()
                }
            };

            var simGroup = _testWorld.CreateSystemManaged<SimulationSystemGroup>();
            simGroup.AddSystemToUpdateList(_testWorld.CreateSystem<TutorialSystem>());
            simGroup.SortSystems();

            simGroup.Update();
            Assert.AreEqual(0, _em.GetComponentData<TutorialStateData>(_playerEntity).CurrentStepIndex,
                "Must stay on the research-gated step until the research is purchased");

            ResearchService.Instance.Purchase(_resA);            // mirrors the HUD Unlock button
            SaveManager.Instance.Current.unlockedResearch.Clear(); // save layer out of sync with the live service

            simGroup.Update();
            Assert.AreEqual(1, _em.GetComponentData<TutorialStateData>(_playerEntity).CurrentStepIndex,
                "Must advance from the live ResearchService unlock state, even if the save list is out of sync");
        }

        // ── Research timer ────────────────────────────────────────────────────

        [Test]
        public void StartTimedResearch_DoesNotUnlockImmediately_AndSetsActiveTimer()
        {
            SpawnServices();
            ResearchService.Instance.Purchase(_resTimed); // durationSeconds=60

            Assert.IsFalse(ResearchService.Instance.IsUnlocked(_resTimed.id),
                "Timed research must not unlock immediately on start");
            Assert.IsTrue(ResearchService.Instance.HasActiveResearch,
                "A timer must be running after starting timed research");
            Assert.AreEqual(_resTimed.id, ResearchService.Instance.ActiveResearchId);
            Assert.AreEqual(_resTimed.id, SaveManager.Instance.Current.activeResearchId,
                "Active research id must be persisted");
            Assert.IsFalse(string.IsNullOrEmpty(SaveManager.Instance.Current.activeResearchCompleteUtc),
                "Completion time must be persisted");

            var progress = _em.GetComponentData<PlayerProgressData>(_playerEntity);
            Assert.AreEqual(900L, progress.BaseCurrency,
                "Entropy must be paid up front when the timer starts");
        }

        [Test]
        public void ZeroDurationResearch_UnlocksInstantly_NoActiveTimer()
        {
            SpawnServices();
            ResearchService.Instance.Purchase(_resA); // durationSeconds defaults to 0

            Assert.IsTrue(ResearchService.Instance.IsUnlocked(_resA.id),
                "Zero-duration research must unlock instantly");
            Assert.IsFalse(ResearchService.Instance.HasActiveResearch,
                "No timer should remain for an instant research");
        }

        [Test]
        public void ProcessActiveTimer_CompletesWhenElapsed()
        {
            SpawnServices();
            ResearchService.Instance.Purchase(_resTimed);

            // Force the cached completion time into the past (simulates the timer elapsing / offline).
            SetActiveCompleteUtc(ResearchService.Instance, System.DateTime.UtcNow.AddSeconds(-1));
            ResearchService.Instance.ProcessActiveTimer();

            Assert.IsTrue(ResearchService.Instance.IsUnlocked(_resTimed.id),
                "Research must complete once its timer has elapsed");
            Assert.IsFalse(ResearchService.Instance.HasActiveResearch,
                "Timer must be cleared after completion");
            Assert.IsTrue(string.IsNullOrEmpty(SaveManager.Instance.Current.activeResearchId),
                "Persisted active id must be cleared on completion");
        }

        [Test]
        public void OneAtATime_BlocksStartingSecondResearch()
        {
            SpawnServices();
            ResearchService.Instance.Purchase(_resTimed);   // starts a timer
            ResearchService.Instance.Purchase(_resTimed2);  // no prereq, affordable, but lab is busy

            Assert.AreEqual(_resTimed.id, ResearchService.Instance.ActiveResearchId,
                "The first research must stay active");
            Assert.IsFalse(ResearchService.Instance.IsUnlocked(_resTimed2.id),
                "A second research must not start while one is in progress");
        }

        [Test]
        public void SkipActive_SpendsCrystalsAndCompletes()
        {
            SpawnServices();
            SaveManager.Instance.Current.paidCurrency = 200;
            ResearchService.Instance.Purchase(_resTimed2); // 3600s -> skip cost 150◆

            bool skipped = ResearchService.Instance.SkipActive();

            Assert.IsTrue(skipped, "Skip should succeed with enough crystals");
            Assert.IsTrue(ResearchService.Instance.IsUnlocked(_resTimed2.id),
                "Skipped research must complete immediately");
            Assert.AreEqual(50L, SaveManager.Instance.Current.paidCurrency,
                "Skip must deduct the 150◆ (1 skip-hour) cost from crystals");
        }

        [Test]
        public void SkipActive_FailsWhenNotEnoughCrystals()
        {
            SpawnServices();
            SaveManager.Instance.Current.paidCurrency = 10; // < 150
            ResearchService.Instance.Purchase(_resTimed2);

            bool skipped = ResearchService.Instance.SkipActive();

            Assert.IsFalse(skipped, "Skip must fail without enough crystals");
            Assert.IsTrue(ResearchService.Instance.HasActiveResearch,
                "Timer must keep running when a skip is unaffordable");
            Assert.AreEqual(10L, SaveManager.Instance.Current.paidCurrency,
                "No crystals should be spent on a failed skip");
        }

        [Test]
        public void ResetAll_ClearsActiveTimer()
        {
            SpawnServices();
            ResearchService.Instance.Purchase(_resTimed);
            ResearchService.Instance.ResetAll();

            Assert.IsFalse(ResearchService.Instance.HasActiveResearch,
                "Prestige reset must drop the in-progress timer");
        }

        // ── Skip-cost formula (pure) ──────────────────────────────────────────

        [Test]
        public void CalcResearchSkipCost_MatchesAnchor()
        {
            Assert.AreEqual(150L, PremiumShopCalculator.CalcResearchSkipCost(3600), "1 hour = 150◆");
            Assert.AreEqual(300L, PremiumShopCalculator.CalcResearchSkipCost(7200), "2 hours = 300◆");
            Assert.AreEqual(1L,   PremiumShopCalculator.CalcResearchSkipCost(10),   "Tiny timer floors at 1◆");
            Assert.AreEqual(0L,   PremiumShopCalculator.CalcResearchSkipCost(0),    "No time remaining = free");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        static void SetActiveCompleteUtc(ResearchService svc, System.DateTime when) =>
            typeof(ResearchService)
                .GetField("_activeCompleteUtc", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(svc, when);

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
