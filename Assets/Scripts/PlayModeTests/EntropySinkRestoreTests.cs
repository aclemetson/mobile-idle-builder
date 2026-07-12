using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.PlayModeTests
{
    /// <summary>
    /// Guards the rule that keeps exactly one Maxwell's Demon on the grid: the sink is baked from the
    /// SubScene, so GridSaveService must never re-place it from save data. It used to, because the
    /// buildingId lookup is built from the whole BuildingDatabase (which contains the Demon), so every
    /// launch restored a second sink entity on top of the baked one — visible as a duplicate Demon the
    /// moment the baked cell and the saved cell disagree.
    ///
    /// The sink must still be WRITTEN to the save: IdleGraphAnalyzer marks chains that end at the Demon
    /// from grid.buildings, and offline earnings are paid on that flag.
    /// </summary>
    public class EntropySinkRestoreTests
    {
        static BuildingSO Building(int id, bool isEntropySink)
        {
            var so = ScriptableObject.CreateInstance<BuildingSO>();
            so.buildingId    = id;
            so.isEntropySink = isEntropySink;
            return so;
        }

        [Test]
        public void IsRestorable_False_ForEntropySink()
        {
            var demon = Building(7, isEntropySink: true);
            Assert.IsFalse(GridSaveService.IsRestorable(demon),
                "The Demon is baked from the SubScene — restoring it from save data duplicates it.");
        }

        [Test]
        public void IsRestorable_True_ForOrdinaryBuilding()
        {
            var harvester = Building(1, isEntropySink: false);
            Assert.IsTrue(GridSaveService.IsRestorable(harvester));
        }

        [Test]
        public void IsRestorable_False_ForMissingBuilding()
        {
            Assert.IsFalse(GridSaveService.IsRestorable(null));
        }

        /// <summary>
        /// A save written by an older build still carries a Demon entry at the cell that build baked it
        /// at. Loading such a save must drop that entry rather than stand a second Demon up next to the
        /// one the current SubScene bakes.
        /// </summary>
        [Test]
        public void StaleSinkEntry_InSavedGrid_IsNotRestorable()
        {
            var grid = new GridSaveData();
            grid.buildings.Add(new BuildingSaveData { buildingId = 1, position = new[] { 6, 10 } });
            grid.buildings.Add(new BuildingSaveData { buildingId = 7, position = new[] { 10, 10 } });

            var harvester = Building(1, isEntropySink: false);
            var demon     = Building(7, isEntropySink: true);

            int restored = 0;
            foreach (var bsd in grid.buildings)
            {
                var so = bsd.buildingId == 7 ? demon : harvester;
                if (GridSaveService.IsRestorable(so)) restored++;
            }

            Assert.AreEqual(1, restored, "Only the harvester should be re-placed; the Demon is baked.");
        }
    }
}
