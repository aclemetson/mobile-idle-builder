using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit-mode tests for the placement-rotation state machine that the in-context
    /// "↻ Rotate" buttons (placement bar + confirm popup) and the R key all drive.
    ///
    /// Only the no-candidate path is exercised here: with no locked candidate cell,
    /// Rotate() advances the rotation step without touching scene references
    /// (GridRenderer / ghost arrows). The ghost re-draw and popup re-anchor paths
    /// need a live GridRenderer + UIDocument and are covered by manual play-mode checks.
    /// </summary>
    [TestFixture]
    public class BuildingPlacementControllerTests
    {
        private GameObject                 _go;
        private BuildingPlacementController _ctl;

        [SetUp]
        public void SetUp()
        {
            _go  = new GameObject("PlacementControllerTest");
            _ctl = _go.AddComponent<BuildingPlacementController>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static BuildingSO PortLayoutBuilding()
        {
            var so = ScriptableObject.CreateInstance<BuildingSO>();
            so.footprint = new Vector2Int(2, 1);
            so.placementRule = PlacementRule.Anywhere;
            so.ports = new[]
            {
                new BuildingPort { portType = PortType.Output, localCell = new Vector2Int(1, 0), localFacing = OutputDirection.East }
            };
            return so;
        }

        private static BuildingSO PlainBuilding()
        {
            var so = ScriptableObject.CreateInstance<BuildingSO>();
            so.footprint = Vector2Int.one;
            so.placementRule = PlacementRule.Anywhere; // no field, no ports → not rotatable
            so.ports = null;
            return so;
        }

        // ── NextRotation (pure helper) ───────────────────────────────────────

        [Test]
        public void NextRotation_CyclesThroughAllFourSteps()
        {
            Assert.AreEqual(1, BuildingPlacementController.NextRotation(0));
            Assert.AreEqual(2, BuildingPlacementController.NextRotation(1));
            Assert.AreEqual(3, BuildingPlacementController.NextRotation(2));
        }

        [Test]
        public void NextRotation_WrapsFromThreeToZero()
        {
            Assert.AreEqual(0, BuildingPlacementController.NextRotation(3),
                "Rotation step 3 must wrap back to 0, not advance to 4");
        }

        // ── Port-layout building: rotatable + flippable ──────────────────────

        [Test]
        public void BeginPlacement_PortLayout_IsRotatableAndFlippable_AndResetsRotation()
        {
            _ctl.BeginPlacement(new BuildingPlacementController.BuildingEntry { building = PortLayoutBuilding() });

            Assert.IsTrue(_ctl.CanRotate, "Port-layout building must be rotatable");
            Assert.IsTrue(_ctl.CanFlip,   "Port-layout building must be flippable");
            Assert.AreEqual(0, _ctl.CurrentRotation, "BeginPlacement must reset rotation to 0");
        }

        [Test]
        public void Rotate_PortLayout_AdvancesAndWrapsRotation()
        {
            _ctl.BeginPlacement(new BuildingPlacementController.BuildingEntry { building = PortLayoutBuilding() });

            _ctl.Rotate(); Assert.AreEqual(1, _ctl.CurrentRotation);
            _ctl.Rotate(); Assert.AreEqual(2, _ctl.CurrentRotation);
            _ctl.Rotate(); Assert.AreEqual(3, _ctl.CurrentRotation);
            _ctl.Rotate(); Assert.AreEqual(0, _ctl.CurrentRotation, "Fourth rotate must wrap back to 0");
        }

        // ── Plain building: neither rotatable nor flippable (edge / no-op) ────

        [Test]
        public void BeginPlacement_PlainBuilding_IsNotRotatableOrFlippable()
        {
            _ctl.BeginPlacement(new BuildingPlacementController.BuildingEntry { building = PlainBuilding() });

            Assert.IsFalse(_ctl.CanRotate, "A building with no ports and no field rule must not be rotatable");
            Assert.IsFalse(_ctl.CanFlip,   "A building with no ports must not be flippable");
        }

        [Test]
        public void Rotate_PlainBuilding_IsNoOp()
        {
            _ctl.BeginPlacement(new BuildingPlacementController.BuildingEntry { building = PlainBuilding() });

            _ctl.Rotate();
            Assert.AreEqual(0, _ctl.CurrentRotation, "Rotating a non-rotatable building must leave rotation at 0");
        }

        // ── Not placing: CanRotate guards against null pending ────────────────

        [Test]
        public void CanRotate_WhenNotPlacing_IsFalse()
        {
            Assert.IsFalse(_ctl.IsPlacing);
            Assert.IsFalse(_ctl.CanRotate, "CanRotate must be false before any BeginPlacement");
        }
    }
}
