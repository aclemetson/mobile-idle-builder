using System.Collections.Generic;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>Item value maths (mirrors ProductionSystem / CollectorSystem), number formatting and readout text.</summary>
    public class ItemValueTests
    {
        [Test]
        public void PerCraft_QtyOneWithMult1_5_AveragesOneAndAHalf()
            => Assert.AreEqual(6d, ItemValue.PerCraft(4f, 1, 1.5f, 0f), 1e-9); // the carry pays 1, 2, 1, 2 ...

        [Test]
        public void PerCraft_QtyTwoWithMult1_5_GivesThree()
            => Assert.AreEqual(6d, ItemValue.PerCraft(2f, 2, 1.5f, 0f), 1e-9);

        [Test]
        public void PerCraft_AddsFlooredEscapeEntropy()
            => Assert.AreEqual(2d + 7d, ItemValue.PerCraft(1f, 2, 1f, 7.9f), 1e-9);

        [Test]
        public void PerCraft_SlotList_SumsSlotsAndCountsEscapeOnce()
        {
            var outs = new List<(int itemId, int quantity)> { (1, 2), (2, 1) };
            float Price(int id) => id == 1 ? 3f : 10f;
            Assert.AreEqual(3d * 2 + 10d + 5d, ItemValue.PerCraft(outs, Price, 1f, 5f), 1e-9);
        }

        [Test]
        public void PerSecond_NormalCase()
            => Assert.AreEqual(10d, ItemValue.PerSecond(20d, 4f, 2f), 1e-9);

        [Test]
        public void PerSecond_ZeroCraftTimeOrSpeed_IsZero()
        {
            Assert.AreEqual(0d, ItemValue.PerSecond(20d, 0f, 1f));
            Assert.AreEqual(0d, ItemValue.PerSecond(20d, 4f, 0f));
        }

        [Test]
        public void CollectorPerSecond_UsesAverageOutMultRateAndSpeed()
        {
            Assert.AreEqual(3d * 2.9d * 1.5d, ItemValue.CollectorPerSecond(3f, 1.5f, 2.9f), 1e-6);
            Assert.AreEqual(3d * 2d * 1.5d * 1.1d, ItemValue.CollectorPerSecond(3f, 1.5f, 2f, 1.1f), 1e-6);
            Assert.AreEqual(3d, ItemValue.CollectorPerSecond(3f, 0f, 1f), 1e-9); // rate falls back to 1
        }

        [Test]
        public void Compact_WholeNumbers_MatchTheOldFormat()
        {
            Assert.AreEqual("9,999", NumberFormat.Compact(9999));
            Assert.AreEqual("12.3K", NumberFormat.Compact(12345));
            Assert.AreEqual("1.23M", NumberFormat.Compact(1_234_567));
            Assert.AreEqual("1.23B", NumberFormat.Compact(1_234_567_890));
            Assert.AreEqual("0", NumberFormat.Compact(0));
        }

        [Test]
        public void FormatLargeNumber_StillForwards()
            => Assert.AreEqual("12.3K", IdleReturnSubController.FormatLargeNumber(12345));

        [Test]
        public void Entropy_ShowsOneDecimalBelowTen()
        {
            Assert.AreEqual("◈ 2.5", NumberFormat.Entropy(2.5));
            Assert.AreEqual("◈ 3", NumberFormat.Entropy(3));
            Assert.AreEqual("◈ 12.3K", NumberFormat.Entropy(12345));
        }

        [Test]
        public void WorthEachText_Formats()
            => Assert.AreEqual("Worth ◈ 2.5 each", HUDController.WorthEachText(2.5f));

        [Test]
        public void ValueLines_NoBonus_OmitsMultiplierAndHasThreeRows()
        {
            var lines = HUDBuildingInspectorSubController.ValueLines(4f, 8d, 1f, 2d, false);
            Assert.AreEqual(3, lines.Length);
            Assert.AreEqual("Worth ◈ 4 each", lines[0]);
            Assert.AreEqual("◈ 8 per craft", lines[1]);
            Assert.AreEqual("≈ ◈ 2 /s at current speed", lines[2]);
        }

        [Test]
        public void ValueLines_WithOutputBonusAndNoPower()
        {
            var lines = HUDBuildingInspectorSubController.ValueLines(4f, 12d, 1.5f, 0d, true);
            Assert.AreEqual(3, lines.Length);
            Assert.AreEqual("×1.5 output → ◈ 12 per craft", lines[1]);
            Assert.AreEqual("≈ ◈ 0 /s (no power)", lines[2]);
        }

        [Test]
        public void CollectorValueLine_Formats()
            => Assert.AreEqual("≈ ◈ 2.5 /s", HUDBuildingInspectorSubController.CollectorValueLine(2.5d));
    }
}
