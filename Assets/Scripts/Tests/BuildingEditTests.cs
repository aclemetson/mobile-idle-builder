using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Building edit mode: a press-and-hold moves a placed building without destroying it. Covers the hold
    /// timing (<see cref="LongPress"/>), the in-place entity rewrite (<see cref="BuildingMove"/>: anchor,
    /// footprint, ports, legacy output side, Filter rules, splitter turn, and that contents survive) and the
    /// edit-only placement message. The hold gesture, ring and HUD bar are checked in Play mode.
    /// </summary>
    [TestFixture]
    public class BuildingEditTests
    {
        private World         _world;
        private EntityManager _em;

        [SetUp]
        public void SetUp()
        {
            _world = new World("BuildingEditTest");
            _em    = _world.EntityManager;
        }

        [TearDown]
        public void TearDown()
        {
            if (_world.IsCreated) _world.Dispose();
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        // 2x1 with an input on the west end and an output on the east end.
        private static BuildingSO TwoByOne()
        {
            var so = ScriptableObject.CreateInstance<BuildingSO>();
            so.footprint     = new Vector2Int(2, 1);
            so.placementRule = PlacementRule.Anywhere;
            so.ports = new[]
            {
                new BuildingPort { portType = PortType.Input,  localCell = new Vector2Int(0, 0), localFacing = OutputDirection.East },
                new BuildingPort { portType = PortType.Output, localCell = new Vector2Int(1, 0), localFacing = OutputDirection.East },
            };
            return so;
        }

        // Mirrors BuildingPlacer for the components a move touches.
        private Entity Place(BuildingSO so, int x, int y, int rotation = 0, bool flipped = false)
        {
            var e = _em.CreateEntity(typeof(BuildingData), typeof(GridPosition), typeof(RecipeProcessData),
                                     typeof(PlacedPortData), typeof(BuildingOutputSlot), typeof(BuildingInputSlot));
            _em.SetComponentData(e, new GridPosition { Cell = new int2(x, y) });
            _em.AddComponentData(e, new BuildingTransformData { Rotation = rotation, Flipped = flipped });

            var baseFp = so.footprint;
            var fp     = PortUtils.RotatedFootprint(baseFp, rotation);
            if (fp.x > 1 || fp.y > 1) _em.AddComponentData(e, new BuildingFootprint { Width = fp.x, Height = fp.y });

            var ports = _em.GetBuffer<PlacedPortData>(e);
            if (so.ports != null)
                foreach (var port in so.ports)
                {
                    var (rel, dir) = PortUtils.TransformPort(port, baseFp, flipped, rotation);
                    ports.Add(new PlacedPortData
                    {
                        PortType = (int)port.portType, CellX = x + rel.x, CellY = y + rel.y, Facing = (int)dir
                    });
                }
            return e;
        }

        private PlacedPortData PortOfType(Entity e, PortType type)
        {
            var ports = _em.GetBuffer<PlacedPortData>(e);
            for (int i = 0; i < ports.Length; i++)
                if (ports[i].PortType == (int)type) return ports[i];
            Assert.Fail($"no {type} port");
            return default;
        }

        // ── LongPress ────────────────────────────────────────────────────────

        [Test]
        public void LongPress_QuickTap_ShowsNoRingAndDoesNotComplete()
        {
            Assert.IsFalse(LongPress.RingVisible(0.15f));
            Assert.IsFalse(LongPress.IsComplete(0.15f));
        }

        [Test]
        public void LongPress_RingFillsFromItsDelayToTheFullHold()
        {
            Assert.IsTrue(LongPress.RingVisible(LongPress.RingDelay));
            Assert.AreEqual(0f, LongPress.RingProgress(LongPress.RingDelay), 1e-5f);
            Assert.AreEqual(0.5f, LongPress.RingProgress((LongPress.RingDelay + LongPress.HoldSeconds) * 0.5f), 1e-5f);
            Assert.AreEqual(1f, LongPress.RingProgress(LongPress.HoldSeconds), 1e-5f);
            Assert.AreEqual(1f, LongPress.RingProgress(5f), 1e-5f, "clamped after the hold completes");
        }

        [Test]
        public void LongPress_CompletesAtTheHoldTime()
        {
            Assert.IsFalse(LongPress.IsComplete(LongPress.HoldSeconds - 0.01f));
            Assert.IsTrue(LongPress.IsComplete(LongPress.HoldSeconds));
            Assert.Less(LongPress.RingDelay, LongPress.HoldSeconds);
        }

        // ── RemapFacing ──────────────────────────────────────────────────────

        [Test]
        public void RemapFacing_MatchesWherePortUtilsPutsTheSamePort()
        {
            // For every local facing and every old/new orientation, turning the old placed facing must land
            // on the facing TransformPort gives the same port in the new orientation.
            var port = new BuildingPort { localCell = Vector2Int.zero };
            var fp   = Vector2Int.one;
            for (int local = 0; local < 4; local++)
            for (int oldRot = 0; oldRot < 4; oldRot++)
            for (int newRot = 0; newRot < 4; newRot++)
            foreach (bool oldFlip in new[] { false, true })
            foreach (bool newFlip in new[] { false, true })
            {
                port.localFacing = (OutputDirection)local;
                var oldFacing = PortUtils.TransformPort(port, fp, oldFlip, oldRot).facing;
                var newFacing = PortUtils.TransformPort(port, fp, newFlip, newRot).facing;
                Assert.AreEqual(newFacing, BuildingMove.RemapFacing(oldFacing, oldRot, oldFlip, newRot, newFlip),
                    $"local={local} {oldRot}/{oldFlip} -> {newRot}/{newFlip}");
            }
        }

        [Test]
        public void RemapRules_KeepsEverythingElseMarkerAndTurnsFacings()
        {
            var rules = new List<(int, int)> { ((int)OutputDirection.East, 7), ((int)OutputDirection.North, PortFilterRules.Everything) };
            var turned = BuildingMove.RemapRules(rules, 0, false, 1, false);
            Assert.AreEqual(((int)OutputDirection.South, 7), turned[0]);
            Assert.AreEqual(((int)OutputDirection.East, PortFilterRules.Everything), turned[1]);
        }

        // ── Apply ────────────────────────────────────────────────────────────

        [Test]
        public void Apply_MovesAnchorAndRebuildsPortsForTheNewCell()
        {
            var so = TwoByOne();
            var e  = Place(so, 5, 5);

            BuildingMove.Apply(_em, e, so, 10, 3, 0, false, null);

            Assert.AreEqual(new int2(10, 3), _em.GetComponentData<GridPosition>(e).Cell);
            var output = PortOfType(e, PortType.Output);
            Assert.AreEqual(11, output.CellX);
            Assert.AreEqual(3,  output.CellY);
            Assert.AreEqual((int)OutputDirection.East, output.Facing);
            Assert.AreEqual(2, _em.GetBuffer<PlacedPortData>(e).Length, "no stale ports left behind");
        }

        [Test]
        public void Apply_RotationSwapsFootprintAndTurnsPorts()
        {
            var so = TwoByOne();
            var e  = Place(so, 5, 5);

            BuildingMove.Apply(_em, e, so, 5, 5, 1, false, null);

            var t = _em.GetComponentData<BuildingTransformData>(e);
            Assert.AreEqual(1, t.Rotation);
            var fp = _em.GetComponentData<BuildingFootprint>(e);
            Assert.AreEqual(1, fp.Width);
            Assert.AreEqual(2, fp.Height);

            var (rel, dir) = PortUtils.TransformPort(so.ports[1], so.footprint, false, 1);
            var output = PortOfType(e, PortType.Output);
            Assert.AreEqual(5 + rel.x, output.CellX);
            Assert.AreEqual(5 + rel.y, output.CellY);
            Assert.AreEqual((int)dir, output.Facing);
        }

        [Test]
        public void Apply_KeepsContentsAndCraftProgress()
        {
            var so = TwoByOne();
            var e  = Place(so, 5, 5);
            _em.SetComponentData(e, new RecipeProcessData { RecipeID = 12, Progress = 0.6f, CraftTime = 2f });
            _em.SetComponentData(e, new BuildingData { BuildingType = 3, UpgradeLevel = 4 });
            _em.GetBuffer<BuildingOutputSlot>(e).Add(new BuildingOutputSlot { ItemID = 9, Quantity = 5 });
            _em.GetBuffer<BuildingInputSlot>(e).Add(new BuildingInputSlot { ItemID = 2, Quantity = 3 });

            BuildingMove.Apply(_em, e, so, 1, 1, 2, true, null);

            var rpd = _em.GetComponentData<RecipeProcessData>(e);
            Assert.AreEqual(12, rpd.RecipeID);
            Assert.AreEqual(0.6f, rpd.Progress, 1e-5f);
            Assert.AreEqual(4, _em.GetComponentData<BuildingData>(e).UpgradeLevel);
            Assert.AreEqual(5, _em.GetBuffer<BuildingOutputSlot>(e)[0].Quantity);
            Assert.AreEqual(3, _em.GetBuffer<BuildingInputSlot>(e)[0].Quantity);
        }

        [Test]
        public void Apply_LegacyCollector_PutsItsOutputOnTheChosenSide()
        {
            var so = ScriptableObject.CreateInstance<BuildingSO>();
            so.footprint     = Vector2Int.one;
            so.placementRule = PlacementRule.MustBeOnField;
            var e = Place(so, 2, 2);
            _em.AddComponentData(e, new OutputDirectionData { Direction = (int)OutputDirection.North });

            BuildingMove.Apply(_em, e, so, 4, 6, 0, false, (int)OutputDirection.West);

            Assert.AreEqual((int)OutputDirection.West, _em.GetComponentData<OutputDirectionData>(e).Direction);
            var ports = _em.GetBuffer<PlacedPortData>(e);
            Assert.AreEqual(1, ports.Length);
            Assert.AreEqual(4, ports[0].CellX);
            Assert.AreEqual(6, ports[0].CellY);
            Assert.AreEqual((int)OutputDirection.West, ports[0].Facing);
        }

        [Test]
        public void Apply_TurningAFilter_KeepsEachItemOnTheSamePort()
        {
            var so = TwoByOne();
            var e  = Place(so, 5, 5);
            FilterSystem.WriteRules(_em.AddBuffer<OutputPortFilter>(e),
                new List<(int, int)> { ((int)OutputDirection.East, 7) });

            BuildingMove.Apply(_em, e, so, 5, 5, 1, false, null);

            var rules  = FilterSystem.ReadRules(_em.GetBuffer<OutputPortFilter>(e));
            var output = PortOfType(e, PortType.Output);
            Assert.AreEqual(output.Facing, rules[0].facing, "item 7 still leaves by the output port");
            Assert.AreEqual(7, rules[0].itemId);
        }

        [Test]
        public void Apply_MovingWithoutTurning_LeavesFilterRulesAlone()
        {
            var so = TwoByOne();
            var e  = Place(so, 5, 5, rotation: 1);
            FilterSystem.WriteRules(_em.AddBuffer<OutputPortFilter>(e),
                new List<(int, int)> { ((int)OutputDirection.South, 7) });

            BuildingMove.Apply(_em, e, so, 8, 8, 1, false, null);

            Assert.AreEqual((int)OutputDirection.South, FilterSystem.ReadRules(_em.GetBuffer<OutputPortFilter>(e))[0].facing);
        }

        [Test]
        public void Apply_ResetsTheSplitterTurn()
        {
            var so = TwoByOne();
            var e  = Place(so, 5, 5);
            _em.AddComponentData(e, new SplitterState { NextFacing = (int)OutputDirection.East });

            BuildingMove.Apply(_em, e, so, 6, 5, 2, false, null);

            Assert.AreEqual(-1, _em.GetComponentData<SplitterState>(e).NextFacing);
        }

        // ── Placement message ────────────────────────────────────────────────

        [Test]
        public void BlockMessage_FieldLacksRecipe_NamesTheBuilding()
        {
            string msg = BuildingPlacementController.BlockMessage(
                BuildingPlacementController.PlacementBlock.FieldLacksRecipe, "Harvester", false);
            Assert.AreEqual("The Harvester can't make its item here.", msg);
        }
    }
}
