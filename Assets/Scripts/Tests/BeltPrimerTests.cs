using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// EditMode tests for BeltPrimer.ComputeItemPositions — the pure spacing core that decides where
    /// primed in-transit items go on load. The ECS glue (PrimeAllChains) is a thin walk over live
    /// entities and is exercised in play; the spacing math is covered here, mirroring how
    /// OfflineCollectionServiceTests covers logic without standing up a world.
    /// </summary>
    [TestFixture]
    public class BeltPrimerTests
    {
        /// <summary>Cells that get an item on a one-item-per-tile belt (the pre-density behaviour).</summary>
        private static List<int> Cells(int chainLength, float rate, float transportTime) =>
            BeltPrimer.ComputeItemPositions(chainLength, rate, transportTime, itemsPerTile: 1)
                      .Select(p => p.cell).ToList();

        // ── One item per tile: saturated, sparse and rounded spacing ────────

        [Test]
        public void OnePerTile_HighRate_FillsEveryCell_MidTile()
        {
            var p = BeltPrimer.ComputeItemPositions(5, 2f, 1f, itemsPerTile: 1);
            CollectionAssert.AreEqual(new List<int> { 0, 1, 2, 3, 4 }, p.Select(x => x.cell).ToList());
            Assert.IsTrue(p.All(x => Mathf.Approximately(x.progress, 0.5f)), "one-per-tile items rest mid-tile");
        }

        [Test]
        public void RateEqualsBeltSpeed_FillsEveryCell() =>
            CollectionAssert.AreEqual(new List<int> { 0, 1, 2, 3 }, Cells(4, 1f, 1f));

        [Test]
        public void Sparse_QuarterRate_SpacesEveryFourCells() =>
            CollectionAssert.AreEqual(new List<int> { 0, 4, 8 }, Cells(10, 0.25f, 1f));

        [Test]
        public void MidRange_HalfRate_SpacesEveryTwoCells() =>
            CollectionAssert.AreEqual(new List<int> { 0, 2, 4 }, Cells(6, 0.5f, 1f));

        [Test]
        public void NonIntegerSpacing_RoundsToNearestCell() =>
            CollectionAssert.AreEqual(new List<int> { 0, 3, 6, 9 }, Cells(10, 0.3f, 1f));

        [Test]
        public void SlowerBelt_TightensSpacing() =>
            CollectionAssert.AreEqual(new List<int> { 0, 2, 4 }, Cells(6, 0.25f, 2f));

        // ── Several items per tile ───────────────────────────────────────────

        [Test]
        public void TwoPerTile_FullRate_PacksTwoPerCell_InTheirSlots()
        {
            // 2 items/s on a 1 tile/s belt is one item every half tile: two per cell, at 0.25 and 0.75.
            var p = BeltPrimer.ComputeItemPositions(3, 2f, 1f, itemsPerTile: 2);
            Assert.AreEqual(6, p.Count);
            for (int i = 0; i < p.Count; i++)
            {
                Assert.AreEqual(i / 2, p[i].cell);
                Assert.AreEqual(i % 2 == 0 ? 0.25f : 0.75f, p[i].progress, 1e-4f);
            }
        }

        [Test]
        public void TwoPerTile_NeverPacksTighterThanTheSlot()
        {
            var p = BeltPrimer.ComputeItemPositions(3, 1000f, 1f, itemsPerTile: 2);
            Assert.AreEqual(6, p.Count, "at most two items per cell");
        }

        [Test]
        public void TwoPerTile_BetweenRates_KeepsTheProducerGap()
        {
            // 1.5 items/s → one item every 2/3 tile.
            var p = BeltPrimer.ComputeItemPositions(4, 1.5f, 1f, itemsPerTile: 2);
            for (int i = 1; i < p.Count; i++)
            {
                float gap = (p[i].cell + p[i].progress) - (p[i - 1].cell + p[i - 1].progress);
                Assert.AreEqual(2f / 3f, gap, 1e-3f);
            }
        }

        // ── Edge / guard cases ──────────────────────────────────────────────────

        [Test]
        public void ZeroRate_ReturnsEmpty() =>
            Assert.IsEmpty(BeltPrimer.ComputeItemPositions(5, 0f, 1f, 2));

        [Test]
        public void NegativeRate_ReturnsEmpty() =>
            Assert.IsEmpty(BeltPrimer.ComputeItemPositions(5, -3f, 1f, 2));

        [Test]
        public void ZeroOrNegativeTransportTime_ReturnsEmpty()
        {
            Assert.IsEmpty(BeltPrimer.ComputeItemPositions(5, 1f, 0f, 2));
            Assert.IsEmpty(BeltPrimer.ComputeItemPositions(5, 1f, -1f, 2));
        }

        [Test]
        public void ZeroOrNegativeChainLength_ReturnsEmpty()
        {
            Assert.IsEmpty(BeltPrimer.ComputeItemPositions(0, 1f, 1f, 2));
            Assert.IsEmpty(BeltPrimer.ComputeItemPositions(-4, 1f, 1f, 2));
        }

        [Test]
        public void SingleCellChain_WithProduction_GetsAnItem() =>
            CollectionAssert.AreEqual(new List<int> { 0 }, Cells(1, 1f, 1f));
    }
}
