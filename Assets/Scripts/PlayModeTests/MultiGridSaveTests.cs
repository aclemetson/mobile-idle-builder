using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// Phase 1 (multi-grids) tests: the additive grids/activeSiteIndex/unlockedSites save
    /// fields, the CurrentRunData.ActiveGrid fallback, and GridSaveService.EnsureActiveGrid
    /// legacy-save migration. No ECS or MonoBehaviour lifecycle is required.
    /// </summary>
    public class MultiGridSaveTests
    {
        static GridSaveData GridWithBuilding(int buildingId)
        {
            var g = new GridSaveData();
            g.buildings.Add(new BuildingSaveData { buildingId = buildingId, position = new[] { 1, 1 } });
            return g;
        }

        // ── ActiveGrid property fallback ─────────────────────────────────────

        [Test]
        public void ActiveGrid_FallsBackToLegacyGrid_WhenGridsEmpty()
        {
            var run = new CurrentRunData();
            run.grid = GridWithBuilding(42);
            // grids is empty (pre-multi-grid save)

            Assert.AreSame(run.grid, run.ActiveGrid);
            Assert.AreEqual(42, run.ActiveGrid.buildings[0].buildingId);
        }

        [Test]
        public void ActiveGrid_ReturnsIndexedGrid_WhenGridsPopulated()
        {
            var run = new CurrentRunData();
            run.grids = new List<GridSaveData> { GridWithBuilding(1), GridWithBuilding(2) };
            run.activeSiteIndex = 1;

            Assert.AreSame(run.grids[1], run.ActiveGrid);
            Assert.AreEqual(2, run.ActiveGrid.buildings[0].buildingId);
        }

        [Test]
        public void ActiveGrid_ClampsOutOfRangeIndexToZero()
        {
            var run = new CurrentRunData();
            run.grids = new List<GridSaveData> { GridWithBuilding(7) };
            run.activeSiteIndex = 5; // invalid

            Assert.AreSame(run.grids[0], run.ActiveGrid);
        }

        // ── EnsureActiveGrid migration ───────────────────────────────────────

        [Test]
        public void EnsureActiveGrid_MigratesLegacySave_SeedsGridsZeroAliasingLegacyGrid()
        {
            var save = new SaveData();
            save.currentRun.grid = GridWithBuilding(99);
            // grids empty → legacy save

            var active = GridSaveService.EnsureActiveGrid(save);

            Assert.AreSame(save.currentRun.grid, active, "active grid is the legacy grid");
            Assert.AreEqual(1, save.currentRun.grids.Count, "grids[0] seeded");
            Assert.AreSame(save.currentRun.grid, save.currentRun.grids[0], "grids[0] aliases legacy grid");
        }

        [Test]
        public void EnsureActiveGrid_MirrorsLegacyGrid_WhenOriginSiteActive()
        {
            var save = new SaveData();
            save.currentRun.grids = new List<GridSaveData> { GridWithBuilding(1), GridWithBuilding(2) };
            save.currentRun.activeSiteIndex = 0;

            var active = GridSaveService.EnsureActiveGrid(save);

            Assert.AreSame(save.currentRun.grids[0], active);
            Assert.AreSame(save.currentRun.grids[0], save.currentRun.grid,
                "legacy 'grid' mirrors grids[0] so older builds still read the save");
        }

        [Test]
        public void EnsureActiveGrid_ReturnsActiveSiteGrid_WhenNonOriginActive()
        {
            var save = new SaveData();
            save.currentRun.grids = new List<GridSaveData> { GridWithBuilding(1), GridWithBuilding(2) };
            save.currentRun.activeSiteIndex = 1;

            var active = GridSaveService.EnsureActiveGrid(save);

            Assert.AreSame(save.currentRun.grids[1], active);
        }

        [Test]
        public void EnsureActiveGrid_NullRun_ReturnsNull()
        {
            Assert.IsNull(GridSaveService.EnsureActiveGrid(null));
        }

        // ── JSON round-trip ──────────────────────────────────────────────────

        [Test]
        public void Json_RoundTrip_PreservesMultiGridFields()
        {
            var save = new SaveData();
            save.unlockedSites = new List<string> { "site_origin", "site_quark_sea" };
            save.currentRun.grids = new List<GridSaveData> { GridWithBuilding(10), GridWithBuilding(20) };
            save.currentRun.activeSiteIndex = 1;

            var json   = JsonUtility.ToJson(save);
            var loaded = JsonUtility.FromJson<SaveData>(json);

            Assert.AreEqual(2, loaded.currentRun.grids.Count);
            Assert.AreEqual(10, loaded.currentRun.grids[0].buildings[0].buildingId);
            Assert.AreEqual(20, loaded.currentRun.grids[1].buildings[0].buildingId);
            Assert.AreEqual(1, loaded.currentRun.activeSiteIndex);
            CollectionAssert.AreEqual(new[] { "site_origin", "site_quark_sea" }, loaded.unlockedSites);
        }

        [Test]
        public void Json_LegacySaveWithoutGrids_LoadsAndMigratesOnEnsure()
        {
            // A pre-multi-grid save: currentRun has 'grid' but no 'grids' array.
            const string legacyJson =
                "{\"currentRun\":{\"grid\":{\"buildings\":[{\"buildingId\":5,\"position\":[2,3]}]}}}";

            var save = JsonUtility.FromJson<SaveData>(legacyJson);

            // Before migration the property falls back to the legacy grid.
            Assert.AreEqual(0, save.currentRun.grids.Count);
            Assert.AreEqual(5, save.currentRun.ActiveGrid.buildings[0].buildingId);

            // Migration seeds grids[0] and keeps the legacy mirror.
            var active = GridSaveService.EnsureActiveGrid(save);
            Assert.AreEqual(1, save.currentRun.grids.Count);
            Assert.AreEqual(5, active.buildings[0].buildingId);
            Assert.AreSame(save.currentRun.grid, save.currentRun.grids[0]);
        }
    }
}
