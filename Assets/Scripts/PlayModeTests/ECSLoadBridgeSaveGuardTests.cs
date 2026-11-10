using System.IO;
using System.Reflection;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// Regression tests for the save-clobber guard in ECSLoadBridge.FlushToSave.
    ///
    /// Bug: after a code change forces an ECS SubScene re-bake, the baked entities can lag past the load
    /// timeout. ECSLoadBridge then sets IsLoaded=true WITHOUT applying the save, so ECS holds baked
    /// defaults (0 currency, empty inventory). The autosave loop would then flush those zeros over the
    /// good on-disk save — wiping progress while leaving hasCompletedFirstRun set (no tutorial, 0 currency).
    ///
    /// Fix: FlushToSave is gated on SaveApplied, which is only true once ApplyLoadedSave actually ran.
    /// These tests wire the bridge's ECS fields by reflection (EditMode skips the InitializeAsync coroutine)
    /// and verify the guard both ways.
    /// </summary>
    [TestFixture]
    public class ECSLoadBridgeSaveGuardTests
    {
        string _savePath;
        string _saveBackup;

        GameObject _saveManagerGO;
        GameObject _bridgeGO;
        GameObject _gridGO;

        World         _testWorld;
        EntityManager _em;
        Entity        _playerEntity;

        [SetUp]
        public void SetUp()
        {
            _savePath   = Path.Combine(LocalSaveService.SaveDirectory, "save.json");
            _saveBackup = File.Exists(_savePath) ? File.ReadAllText(_savePath) : null;
            new LocalSaveService().Delete(); // save.json + .bak + .tmp: Load() falls back to the others (#280)

            _testWorld    = new World("ECSLoadBridgeGuardTestWorld");
            _em           = _testWorld.EntityManager;
            _playerEntity = _em.CreateEntity();
            // ECS holds baked-default-like state: zero currency, empty inventory.
            _em.AddComponentData(_playerEntity, new PlayerProgressData { BaseCurrency = 0 });
            _em.AddComponentData(_playerEntity, new PrestigeData());
            _em.AddComponentData(_playerEntity, new TutorialStateData());
            _em.AddComponent<PlayerInventoryTag>(_playerEntity);
            _em.AddBuffer<InventorySlot>(_playerEntity);
            World.DefaultGameObjectInjectionWorld = _testWorld;
        }

        [TearDown]
        public void TearDown()
        {
            if (_gridGO        != null) { Object.DestroyImmediate(_gridGO);        _gridGO        = null; }
            if (_bridgeGO      != null) { Object.DestroyImmediate(_bridgeGO);      _bridgeGO      = null; }
            if (_saveManagerGO != null) { Object.DestroyImmediate(_saveManagerGO); _saveManagerGO = null; }

            // Guarantee the singleton statics are cleared so a bridge with IsLoaded forced true cannot
            // leak into other fixtures (e.g. DailyEventServiceTests calls ECSLoadBridge.Instance?.AddX,
            // which would NRE on the uninitialized query of a leaked instance).
            ClearSingleton(typeof(GridSaveService));
            ClearSingleton(typeof(ECSLoadBridge));
            ClearSingleton(typeof(SaveManager));

            if (_testWorld.IsCreated) _testWorld.Dispose();
            World.DefaultGameObjectInjectionWorld = null;

            new LocalSaveService().Delete(); // save.json + .bak + .tmp: Load() falls back to the others (#280)
            if (_saveBackup != null) File.WriteAllText(_savePath, _saveBackup);
        }

        ECSLoadBridge SpawnBridgeWiredToEcs(bool saveApplied, bool isLoaded = true)
        {
            _saveManagerGO = new GameObject("SaveManager");
            { var sm = _saveManagerGO.AddComponent<SaveManager>(); RunAwake(sm); }
            // The "good" on-disk save loaded at startup.
            SaveManager.Instance.Current.currentRun.baseCurrency = 9999L;

            _bridgeGO = new GameObject("ECSLoadBridge");
            var bridge = _bridgeGO.AddComponent<ECSLoadBridge>();
            RunAwake(bridge);

            // Wire the private ECS fields that InitializeAsync would normally set.
            SetField(bridge, "_em", _em);
            SetField(bridge, "_progressQuery",  _em.CreateEntityQuery(ComponentType.ReadWrite<PlayerProgressData>()));
            SetField(bridge, "_prestigeQuery",  _em.CreateEntityQuery(ComponentType.ReadWrite<PrestigeData>()));
            SetField(bridge, "_inventoryQuery", _em.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerInventoryTag>(), ComponentType.ReadWrite<InventorySlot>()));
            SetField(bridge, "_tutorialQuery",  _em.CreateEntityQuery(ComponentType.ReadWrite<TutorialStateData>()));

            SetProp(bridge, "IsLoaded", isLoaded);
            SetProp(bridge, "SaveApplied", saveApplied);
            return bridge;
        }

        // A GridSaveService over the test world, whose ECS holds no buildings or belts: the state of a
        // freshly loaded SubScene before LoadGrid has re-placed the saved grid.
        void SpawnGridServiceAndSeedSavedGrid()
        {
            _gridGO = new GameObject("GridSaveService");
            var grid = _gridGO.AddComponent<GridSaveService>();
            RunAwake(grid);
            typeof(GridSaveService).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(grid, null);

            var saved = GridSaveService.EnsureActiveGrid(SaveManager.Instance.Current);
            saved.buildings.Add(new BuildingSaveData { buildingId = 1, recipeId = -1, position = new[] { 3, 4 } });
            saved.conveyors.Add(new ConveyorSaveData { cells = new[] { 4, 4, 5, 4 } });
        }

        [Test]
        public void SaveLocal_WhileStillLoading_KeepsSavedBuildingsAndBelts()
        {
            // Device bug: a research timer that finished while the app was closed completes in
            // ResearchService.Start and calls SaveLocal before ECSLoadBridge has applied the save.
            // The grid flush then wrote the empty ECS grid over every saved building and belt.
            SpawnBridgeWiredToEcs(saveApplied: false, isLoaded: false);
            SpawnGridServiceAndSeedSavedGrid();

            SaveManager.Instance.SaveLocal();

            var grid = GridSaveService.EnsureActiveGrid(SaveManager.Instance.Current);
            Assert.AreEqual(1, grid.buildings.Count, "A save during load must not wipe the saved buildings");
            Assert.AreEqual(1, grid.conveyors.Count, "A save during load must not wipe the saved belts");
            Assert.AreEqual(9999L, SaveManager.Instance.Current.currentRun.baseCurrency);
        }

        [Test]
        public void SaveLocal_AfterSaveApplied_FlushesLiveGrid()
        {
            // Guards the test above from passing vacuously: once loaded, the grid flush really does run
            // and mirrors the live (here: empty) ECS grid.
            SpawnBridgeWiredToEcs(saveApplied: true);
            SpawnGridServiceAndSeedSavedGrid();

            SaveManager.Instance.SaveLocal();

            var grid = GridSaveService.EnsureActiveGrid(SaveManager.Instance.Current);
            Assert.AreEqual(0, grid.buildings.Count);
            Assert.AreEqual(0, grid.conveyors.Count);
        }

        [Test]
        public void FlushToSave_WhenSaveNotApplied_DoesNotClobberGoodSave()
        {
            var bridge = SpawnBridgeWiredToEcs(saveApplied: false);

            bridge.FlushToSave();

            Assert.AreEqual(9999L, SaveManager.Instance.Current.currentRun.baseCurrency,
                "A flush before the save is applied must NOT overwrite the loaded save with baked-default ECS state");
        }

        [Test]
        public void FlushToSave_WhenSaveApplied_WritesEcsStateBack()
        {
            var bridge = SpawnBridgeWiredToEcs(saveApplied: true);

            bridge.FlushToSave();

            Assert.AreEqual(0L, SaveManager.Instance.Current.currentRun.baseCurrency,
                "Once the save is applied, flush should mirror live ECS state (here: 0) back into the save");
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        // Null the inherited SingletonMonoBehaviour<T>.Instance static so it can't leak across fixtures.
        static void ClearSingleton(System.Type concrete)
        {
            var prop = concrete.BaseType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
            prop?.SetValue(null, null);
        }

        static void SetField(object o, string name, object value) =>
            o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(o, value);

        static void SetProp(object o, string name, object value) =>
            o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
              .SetValue(o, value); // auto-property private setter is invokable via reflection

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
