using System.Collections.Generic;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// EditMode tests for BeltPrimer.ComputeItemPlacements — the pure spacing core that decides which
    /// belt cells get a primed in-transit item on load. The ECS glue (PrimeAllChains) is a thin walk
    /// over live entities and is exercised in play; the spacing math is covered here, mirroring how
    /// OfflineCollectionServiceTests covers logic without standing up a world.
    /// </summary>
    [TestFixture]
    public class BeltPrimerTests
    {
        // ── Saturated: collector outpaces the belt → one item on every cell ─────

        [Test]
        public void Saturated_HighRate_FillsEveryCell()
        {
            // 2 items/s, transportTime 1 → interval 0.5s → spacing 0.5 cell → clamp to every cell.
            var p = BeltPrimer.ComputeItemPlacements(chainLength: 5, effectiveRatePerSec: 2f, transportTime: 1f);
            CollectionAssert.AreEqual(new List<int> { 0, 1, 2, 3, 4 }, p);
        }

        [Test]
        public void Saturated_RateEqualsBeltSpeed_FillsEveryCell()
        {
            // 1 item/s, transportTime 1 → spacing exactly 1 cell → every cell.
            var p = BeltPrimer.ComputeItemPlacements(4, 1f, 1f);
            CollectionAssert.AreEqual(new List<int> { 0, 1, 2, 3 }, p);
        }

        // ── Sparse: slow collector → items spaced out ───────────────────────────

        [Test]
        public void Sparse_QuarterRate_SpacesEveryFourCells()
        {
            // 0.25 items/s, transportTime 1 → interval 4s → spacing 4 cells.
            var p = BeltPrimer.ComputeItemPlacements(10, 0.25f, 1f);
            CollectionAssert.AreEqual(new List<int> { 0, 4, 8 }, p);
        }

        [Test]
        public void MidRange_HalfRate_SpacesEveryTwoCells()
        {
            // 0.5 items/s, transportTime 1 → interval 2s → spacing 2 cells.
            var p = BeltPrimer.ComputeItemPlacements(6, 0.5f, 1f);
            CollectionAssert.AreEqual(new List<int> { 0, 2, 4 }, p);
        }

        [Test]
        public void NonIntegerSpacing_RoundsToNearestCell()
        {
            // 0.3 items/s, transportTime 1 → interval 3.333s → round(3.333) = 3 cells.
            var p = BeltPrimer.ComputeItemPlacements(10, 0.3f, 1f);
            CollectionAssert.AreEqual(new List<int> { 0, 3, 6, 9 }, p);
        }

        // ── TransportTime scales spacing ────────────────────────────────────────

        [Test]
        public void SlowerBelt_TightensSpacing()
        {
            // 0.25 items/s but transportTime 2 → spacing = 4 / 2 = 2 cells.
            var p = BeltPrimer.ComputeItemPlacements(6, 0.25f, 2f);
            CollectionAssert.AreEqual(new List<int> { 0, 2, 4 }, p);
        }

        // ── Edge / guard cases ──────────────────────────────────────────────────

        [Test]
        public void ZeroRate_ReturnsEmpty()
        {
            Assert.IsEmpty(BeltPrimer.ComputeItemPlacements(5, 0f, 1f));
        }

        [Test]
        public void NegativeRate_ReturnsEmpty()
        {
            Assert.IsEmpty(BeltPrimer.ComputeItemPlacements(5, -3f, 1f));
        }

        [Test]
        public void ZeroOrNegativeTransportTime_ReturnsEmpty()
        {
            Assert.IsEmpty(BeltPrimer.ComputeItemPlacements(5, 1f, 0f));
            Assert.IsEmpty(BeltPrimer.ComputeItemPlacements(5, 1f, -1f));
        }

        [Test]
        public void ZeroOrNegativeChainLength_ReturnsEmpty()
        {
            Assert.IsEmpty(BeltPrimer.ComputeItemPlacements(0, 1f, 1f));
            Assert.IsEmpty(BeltPrimer.ComputeItemPlacements(-4, 1f, 1f));
        }

        [Test]
        public void SingleCellChain_WithProduction_GetsOneItem()
        {
            var p = BeltPrimer.ComputeItemPlacements(1, 1f, 1f);
            CollectionAssert.AreEqual(new List<int> { 0 }, p);
        }

        [Test]
        public void LongChain_NeverExceedsOneItemPerCell()
        {
            // Extreme rate would imply sub-cell spacing; result must still be one item per cell, no more.
            var p = BeltPrimer.ComputeItemPlacements(chainLength: 3, effectiveRatePerSec: 1000f, transportTime: 1f);
            CollectionAssert.AreEqual(new List<int> { 0, 1, 2 }, p);
            Assert.AreEqual(3, p.Count, "at most one item per cell");
        }
    }
}
