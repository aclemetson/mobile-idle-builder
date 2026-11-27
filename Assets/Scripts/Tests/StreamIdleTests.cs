using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Offline production along a Chemistry stream (<see cref="StreamIdleAnalyzer"/>). Layout: a Spring at
    /// (0,1) above a channel (0,0)..(5,0) running east; bank buildings at (x,1); the mouth is (5,0).
    /// Ids: 1 Spring, 2 H2 Extractor, 3 O2 Extractor, 4 water synth, 5 Calorimeter.
    /// Items: 10 H2, 11 O2, 12 water.
    /// </summary>
    [TestFixture]
    public class StreamIdleTests
    {
        const int Spring = 1, H2Ex = 2, O2Ex = 3, Synth = 4, Cal = 5;
        const int H2 = 10, O2 = 11, Water = 12;

        static BuildingSaveData B(int id, int x, int y) =>
            new BuildingSaveData { buildingId = id, recipeId = -1, position = new[] { x, y } };

        static GridSaveData Grid(params BuildingSaveData[] buildings)
        {
            var g = new GridSaveData();
            g.buildings.AddRange(buildings);
            var cells = new List<int>();
            for (int x = 0; x <= 5; x++) { cells.Add(x); cells.Add(0); }
            g.conveyors.Add(new ConveyorSaveData { cells = cells.ToArray() });
            return g;
        }

        static IdleCollectionSnapshot Run(GridSaveData g, float springFlow = 10f, float synthDraw = 3f)
        {
            return StreamIdleAnalyzer.BuildSnapshot(
                g,
                isCollector: id => id == H2Ex || id == O2Ex,
                isEntropySink: id => id == Cal,
                getFootprint: id => id == Cal ? new Vector2Int(2, 2) : Vector2Int.one,
                getOutputItemId: (id, _) => id == H2Ex ? H2 : id == O2Ex ? O2 : -1,
                getCollectorRate: _ => 1f,
                getItemSellValue: id => id == Water ? 100f : 10f,
                getRecipeInfo: (id, _) => id != Synth ? default : new IdleRecipeInfo
                {
                    Valid = true, OutputItemId = Water, OutputQuantity = 2, CraftTimeSeconds = 1f,
                    InputItemIds = new[] { H2, O2 }, InputQuantities = new[] { 2, 1 },
                },
                getSpringFlow: id => id == Spring ? springFlow : 0f,
                getFlowDraw: id => id == Synth ? synthDraw : id == H2Ex || id == O2Ex ? 1f : 0f,
                effectiveItemsPerSecondMultiplier: 1f,
                snapshotTimestamp: "t",
                surplusSaleFraction: 0.5f);
        }

        // "delivered" = burnt in the Calorimeter at full value; otherwise sold at the open mouth's surplus rate.
        static float Rate(IdleCollectionSnapshot s, int item, bool delivered) =>
            s.chains.Where(c => c.itemId == item && (c.baseSellValue >= FullPrice(item) - 1e-3f) == delivered)
                    .Sum(c => c.itemsPerSecond);

        static float FullPrice(int item) => item == Water ? 100f : 10f;

        [Test]
        public void VentsToWaterToCalorimeter_PaysWaterAtFullValue()
        {
            // H2 1/s and O2 1/s; water needs 2 H2 + 1 O2, so 0.5 crafts/s -> 1 water/s; 0.5 O2/s left over.
            var s = Run(Grid(B(Spring, 0, 1), B(H2Ex, 1, 1), B(O2Ex, 2, 1), B(Synth, 3, 1), B(Cal, 6, 0)));

            Assert.AreEqual(1f, Rate(s, Water, delivered: true), 1e-4f);
            Assert.AreEqual(0.5f, Rate(s, O2, delivered: true), 1e-4f, "leftover oxygen burns in the Calorimeter too");
            Assert.AreEqual(0f, Rate(s, H2, delivered: true), 1e-4f);
        }

        [Test]
        public void OpenMouth_SellsAtTheSurplusRate()
        {
            var s = Run(Grid(B(Spring, 0, 1), B(H2Ex, 1, 1)));
            var h2 = s.chains.Single(c => c.itemId == H2);
            Assert.IsTrue(h2.notDelivered, "never Maxwell's Demon");
            Assert.AreEqual(5f, h2.baseSellValue, 1e-4f);
        }

        [Test]
        public void FlowRunsOut_DownstreamBuildingStalls()
        {
            // 2.5 L/s: both extractors (1 each) run, the synth (3) gets 0.5/3 of its draw.
            var s = Run(Grid(B(Spring, 0, 1), B(H2Ex, 1, 1), B(O2Ex, 2, 1), B(Synth, 3, 1), B(Cal, 6, 0)), springFlow: 2.5f);
            Assert.AreEqual(2f * 0.5f / 3f, Rate(s, Water, delivered: true), 1e-4f);
        }

        [Test]
        public void BuildingUpstreamOfItsInputs_MakesNothing()
        {
            // The synth sits before the extractors: nothing it needs has been dropped in yet.
            var s = Run(Grid(B(Spring, 0, 1), B(Synth, 1, 1), B(H2Ex, 2, 1), B(O2Ex, 3, 1), B(Cal, 6, 0)));
            Assert.AreEqual(0f, Rate(s, Water, delivered: true), 1e-4f);
            Assert.AreEqual(1f, Rate(s, H2, delivered: true), 1e-4f);
        }

        [Test]
        public void Calorimeter_NeverCountsAsADemonDelivery()
        {
            var s = Run(Grid(B(Spring, 0, 1), B(H2Ex, 1, 1), B(O2Ex, 2, 1), B(Synth, 3, 1), B(Cal, 6, 0)));
            Assert.IsTrue(s.chains.All(c => c.notDelivered), "the world tasks only count the Demon");
        }

        [Test]
        public void ASecondSpringOnABranch_AddsItsFlowWhereTheBranchJoins()
        {
            // 2.5 L/s alone starves the synth (see FlowRunsOut_DownstreamBuildingStalls). A branch from a second Spring
            // at (2,-4) runs north and joins at (2,0), saved the way GridSaveService saves it: through to the mouth.
            var g = Grid(B(Spring, 0, 1), B(H2Ex, 1, 1), B(O2Ex, 2, 1), B(Synth, 3, 1), B(Cal, 6, 0), B(Spring, 2, -4));
            var branch = new List<int> { 2, -3, 2, -2, 2, -1 };
            for (int x = 2; x <= 5; x++) { branch.Add(x); branch.Add(0); }
            g.conveyors.Add(new ConveyorSaveData { cells = branch.ToArray() });

            var s = Run(g, springFlow: 2.5f);

            Assert.AreEqual(1f, Rate(s, Water, delivered: true), 1e-4f, "past the join the synth has both Springs' water");
        }

        [Test]
        public void ASpringBesideAnotherStream_DoesNotFeedThisOne()
        {
            // A second stream (y = 10) with its own Spring: none of its water reaches the first.
            var g = Grid(B(Spring, 0, 1), B(H2Ex, 1, 1), B(O2Ex, 2, 1), B(Synth, 3, 1), B(Cal, 6, 0), B(Spring, 0, 11));
            var other = new List<int>();
            for (int x = 0; x <= 3; x++) { other.Add(x); other.Add(10); }
            g.conveyors.Add(new ConveyorSaveData { cells = other.ToArray() });

            var s = Run(g, springFlow: 2.5f);

            Assert.AreEqual(2f * 0.5f / 3f, Rate(s, Water, delivered: true), 1e-4f, "still starved: the other Spring feeds its own stream");
        }

        [Test]
        public void NoSpring_ProducesNothing()
        {
            var s = Run(Grid(B(H2Ex, 1, 1), B(Cal, 6, 0)));
            Assert.IsEmpty(s.chains);
        }
    }
}
