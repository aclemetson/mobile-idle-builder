using System.IO;
using System.Reflection;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// EditMode tests for MegastructureService: contribution clamping, stage completion, reward queries,
    /// and the GlobalProductionBonus ECS singleton write.
    ///
    /// Mirrors ResearchServiceTests: a manual world is set as the default injection world, SaveManager and
    /// the service are spawned as GameObjects, and Awake/Start are invoked via reflection (EditMode does not
    /// run the MonoBehaviour lifecycle). A test MegastructureSO is injected into the service's private _data
    /// field so the test does not depend on the generated Resources asset.
    /// </summary>
    [TestFixture]
    public class MegastructureServiceTests
    {
        const int ItemA = 49; // dyson_node
        const int ItemB = 50; // orbital_frame

        string _savePath;
        string _saveBackup;

        GameObject _saveManagerGO;
        GameObject _serviceGO;

        World         _testWorld;
        EntityManager _em;
        Entity        _playerEntity;

        ItemSO _itemA;
        ItemSO _itemB;
        MegastructureSO _data;

        [SetUp]
        public void SetUp()
        {
            _savePath   = Path.Combine(Application.persistentDataPath, "save.json");
            _saveBackup = File.Exists(_savePath) ? File.ReadAllText(_savePath) : null;
            if (File.Exists(_savePath)) File.Delete(_savePath);

            _testWorld    = new World("MegastructureServiceTestWorld");
            _em           = _testWorld.EntityManager;
            _playerEntity = _em.CreateEntity();
            _em.AddComponent<PlayerInventoryTag>(_playerEntity);
            _em.AddBuffer<InventorySlot>(_playerEntity);
            _em.AddComponentData(_playerEntity, new GlobalProductionBonus { OutputMult = 1f, SpeedMult = 1f });
            World.DefaultGameObjectInjectionWorld = _testWorld;

            _itemA = ScriptableObject.CreateInstance<ItemSO>(); _itemA.itemId = ItemA; _itemA.id = "dyson_node";
            _itemB = ScriptableObject.CreateInstance<ItemSO>(); _itemB.itemId = ItemB; _itemB.id = "orbital_frame";

            // Stage 1: 10× A → +10% output. Stage 2: 5× A + 2× B → prestige gain ×2.
            _data = ScriptableObject.CreateInstance<MegastructureSO>();
            _data.id = "dyson_sphere";
            _data.requiredResearch = "megastructure_theory";
            _data.stages = new[]
            {
                new MegastructureStage
                {
                    id = "ms_stage_1", displayName = "Scaffold Ring",
                    costItems = new[] { _itemA }, costQuantities = new[] { 10 },
                    rewardType = MegastructureRewardType.OutputMultiplier, rewardValue = 0.10f
                },
                new MegastructureStage
                {
                    id = "ms_stage_2", displayName = "Support Lattice",
                    costItems = new[] { _itemA, _itemB }, costQuantities = new[] { 5, 2 },
                    rewardType = MegastructureRewardType.PrestigeGainMultiplier, rewardValue = 1.0f
                }
            };
        }

        [TearDown]
        public void TearDown()
        {
            if (_serviceGO     != null) { Object.DestroyImmediate(_serviceGO);     _serviceGO     = null; }
            if (_saveManagerGO != null) { Object.DestroyImmediate(_saveManagerGO); _saveManagerGO = null; }
            if (_testWorld.IsCreated) _testWorld.Dispose();
            World.DefaultGameObjectInjectionWorld = null;

            if (File.Exists(_savePath)) File.Delete(_savePath);
            if (_saveBackup != null) File.WriteAllText(_savePath, _saveBackup);

            if (_itemA != null) Object.DestroyImmediate(_itemA);
            if (_itemB != null) Object.DestroyImmediate(_itemB);
            if (_data  != null) Object.DestroyImmediate(_data);
        }

        void SpawnServices()
        {
            _saveManagerGO = new GameObject("SaveManager");
            { var sm = _saveManagerGO.AddComponent<SaveManager>(); RunAwake(sm); }

            _serviceGO = new GameObject("MegastructureService");
            var svc = _serviceGO.AddComponent<MegastructureService>();
            RunAwake(svc);
            // Inject test data over whatever Resources.Load returned (real asset or null).
            typeof(MegastructureService)
                .GetField("_data", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(svc, _data);
            RunStart(svc); // ECS queries + LoadFromSave + ApplyBonusToECS
        }

        void SetInventory(int itemId, int qty)
        {
            var buf = _em.GetBuffer<InventorySlot>(_playerEntity);
            buf.Add(new InventorySlot { ItemID = itemId, Quantity = qty });
        }

        int InventoryQty(int itemId)
        {
            var buf = _em.GetBuffer<InventorySlot>(_playerEntity, isReadOnly: true);
            return SlotBufferUtils.CountInInventory(buf, itemId);
        }

        GlobalProductionBonus Bonus() => _em.GetComponentData<GlobalProductionBonus>(_playerEntity);

        // ── Contribution clamping ──────────────────────────────────────────────

        [Test]
        public void Contribute_InsufficientInventory_RejectsAndDeductsNothing()
        {
            SpawnServices();
            SetInventory(ItemA, 3); // stage needs 10

            int contributed = MegastructureService.Instance.Contribute(ItemA, 10);

            Assert.AreEqual(0, contributed, "Requesting more than inventory holds must be rejected");
            Assert.AreEqual(3, InventoryQty(ItemA), "Rejected contribution must not deduct anything");
            Assert.AreEqual(0, MegastructureService.Instance.GetContributed(ItemA));
        }

        [Test]
        public void Contribute_OverContribution_ClampsToNeeded()
        {
            SpawnServices();
            SetInventory(ItemA, 100); // stage needs 10

            int contributed = MegastructureService.Instance.Contribute(ItemA, 100);

            Assert.AreEqual(10, contributed, "Over-contribution must clamp to the stage's remaining need");
            Assert.AreEqual(90, InventoryQty(ItemA), "Only the needed amount may be deducted");
        }

        [Test]
        public void ContributeAll_TakesWhatInventoryHolds_WhenBelowNeed()
        {
            SpawnServices();
            SetInventory(ItemA, 4); // stage needs 10

            int contributed = MegastructureService.Instance.ContributeAll(ItemA);

            Assert.AreEqual(4, contributed, "ContributeAll deposits whatever the inventory holds");
            Assert.AreEqual(0, InventoryQty(ItemA));
            Assert.AreEqual(4, MegastructureService.Instance.GetContributed(ItemA));
            Assert.AreEqual(0, MegastructureService.Instance.CompletedStages, "Stage not yet filled");
        }

        // ── Stage completion + rewards ─────────────────────────────────────────

        [Test]
        public void Contribute_ExactlyEnough_CompletesStage_ResetsContrib_AppliesOutputBonus()
        {
            SpawnServices();
            SetInventory(ItemA, 10);

            int contributed = MegastructureService.Instance.Contribute(ItemA, 10);

            Assert.AreEqual(10, contributed);
            Assert.AreEqual(1, MegastructureService.Instance.CompletedStages, "Stage must complete");
            Assert.AreEqual(0, MegastructureService.Instance.GetContributed(ItemA),
                "Contributions reset for the next stage");
            Assert.AreEqual(0.10f, MegastructureService.Instance.GetOutputBonus(), 1e-4f);
            Assert.AreEqual(1.10f, Bonus().OutputMult, 1e-4f, "GlobalProductionBonus singleton must update");
        }

        [Test]
        public void CompletingPrestigeStage_RaisesPrestigeGainBonus()
        {
            SpawnServices();
            // Fill stage 1 (10× A), then stage 2 (5× A + 2× B).
            SetInventory(ItemA, 15);
            SetInventory(ItemB, 2);

            MegastructureService.Instance.Contribute(ItemA, 10); // completes stage 1
            MegastructureService.Instance.Contribute(ItemA, 5);  // toward stage 2
            MegastructureService.Instance.Contribute(ItemB, 2);  // completes stage 2

            Assert.AreEqual(2, MegastructureService.Instance.CompletedStages);
            Assert.AreEqual(1.0f, MegastructureService.Instance.GetPrestigeGainBonus(), 1e-4f);
        }

        // ── Save sync ──────────────────────────────────────────────────────────

        [Test]
        public void Contribute_PersistsStageAndContributionsToSave()
        {
            SpawnServices();
            SetInventory(ItemA, 13);

            MegastructureService.Instance.Contribute(ItemA, 10); // completes stage 1
            MegastructureService.Instance.Contribute(ItemA, 3);  // partial toward stage 2

            var save = SaveManager.Instance.Current;
            Assert.AreEqual(1, save.megastructureStage);
            Assert.AreEqual(1, save.megastructureContribKeys.Count);
            Assert.AreEqual(ItemA.ToString(), save.megastructureContribKeys[0]);
            Assert.AreEqual(3, save.megastructureContribValues[0]);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

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
