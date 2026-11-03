using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit-mode tests for the single-conveyor placement direction cycle. Only the pure
    /// NextDir helper is exercised here; the candidate/preview flow needs a live GridRenderer +
    /// ConveyorVisualizer and is covered by manual play-mode checks (same split as
    /// BuildingPlacementControllerTests).
    /// </summary>
    [TestFixture]
    public class ConveyorSinglePlacementTests
    {
        [Test]
        public void NextDir_CyclesNorthEastSouthWest()
        {
            Assert.AreEqual(OutputDirection.East,  ConveyorPlacementController.NextDir(OutputDirection.North));
            Assert.AreEqual(OutputDirection.South, ConveyorPlacementController.NextDir(OutputDirection.East));
            Assert.AreEqual(OutputDirection.West,  ConveyorPlacementController.NextDir(OutputDirection.South));
        }

        [Test]
        public void NextDir_WrapsWestToNorth()
        {
            Assert.AreEqual(OutputDirection.North, ConveyorPlacementController.NextDir(OutputDirection.West),
                "West must wrap back to North, not advance past the four cardinal directions");
        }
    }
}
