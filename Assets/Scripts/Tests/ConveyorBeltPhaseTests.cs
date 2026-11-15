using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>The belt lattice's wave phase moves at item speed (cells per second), like ConveyorItem.Progress.</summary>
    [TestFixture]
    public class ConveyorBeltPhaseTests
    {
        [Test]
        public void Advances_ByDtTimesSpeed() =>
            Assert.AreEqual(0.5f + 0.1f * 1.5f, ConveyorVisualizer.AdvanceBeltPhase(0.5f, 0.1f, 1.5f), 1e-6f);

        [Test]
        public void MatchesItemProgress_OverOneSecond()
        {
            // ConveyorSystem: step = dt * speedMult / TransportTime(1). Sixty frames at 2x moves two cells.
            float phase = 0f;
            for (int i = 0; i < 60; i++) phase = ConveyorVisualizer.AdvanceBeltPhase(phase, 1f / 60f, 2f);
            Assert.AreEqual(2f, phase, 1e-4f);
        }

        [Test]
        public void Wraps_AtAWholeNumber()
        {
            float p = ConveyorVisualizer.AdvanceBeltPhase(63.9f, 0.2f, 1f);
            Assert.AreEqual(0.1f, p, 1e-4f);   // whole-number wrap keeps the shader pattern unchanged
        }

        [Test]
        public void NonPositiveSpeed_FallsBackToOne() =>
            Assert.AreEqual(0.25f, ConveyorVisualizer.AdvanceBeltPhase(0f, 0.25f, 0f), 1e-6f);
    }
}
