using NUnit.Framework;
using Unity.Mathematics;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// A belt tail is drawn into the input it really delivers to. The case from the field: a Lepton Field
    /// belt ending between a Fusion Generator (straight ahead, doesn't use electrons) and an Atom
    /// Generator (to the left, does) was drawn into the Fusion Generator while every electron went left.
    /// </summary>
    [TestFixture]
    public class TailDeliveryLogTests
    {
        const int N = 0, E = 1, S = 2, W = 3;
        static readonly int2 Cell = new int2(4, 7);
        static readonly bool[] NoSinks = new bool[4];

        static bool[] Dirs(params int[] dirs)
        {
            var f = new bool[4];
            foreach (var d in dirs) f[d] = true;
            return f;
        }

        [SetUp]    public void SetUp()    => TailDeliveryLog.Clear();
        [TearDown] public void TearDown() => TailDeliveryLog.Clear();

        [Test]
        public void VisualExit_DrawsTheDeliveredSide_OverTheInputAhead()
        {
            // Belt runs north; Fusion Generator ahead (N), Atom Generator to the left (W).
            Assert.AreEqual(N, TailDelivery.VisualExit(N, N, Dirs(N, W), NoSinks), "no delivery yet: ranking");
            Assert.AreEqual(W, TailDelivery.VisualExit(N, N, Dirs(N, W), NoSinks, deliveredDir: W));
        }

        [Test]
        public void VisualExit_DeliveredInputGone_FallsBackToRanking() =>
            Assert.AreEqual(N, TailDelivery.VisualExit(N, N, Dirs(N), NoSinks, deliveredDir: W));

        [Test]
        public void VisualExit_DeliveredDirIsAUTurn_Ignored() =>
            Assert.AreEqual(N, TailDelivery.VisualExit(N, N, Dirs(N, S), NoSinks, deliveredDir: S));

        [Test]
        public void Log_FirstDeliverySettlesAtOnce()
        {
            Assert.AreEqual(-1, TailDeliveryLog.Settled(Cell));
            int v = TailDeliveryLog.Version;
            TailDeliveryLog.Record(Cell, W);
            Assert.AreEqual(W, TailDeliveryLog.Settled(Cell));
            Assert.AreNotEqual(v, TailDeliveryLog.Version, "visualizer is told to redraw");
        }

        [Test]
        public void Log_OneOffDeliveryElsewhere_DoesNotFlipTheDrawing()
        {
            TailDeliveryLog.Record(Cell, W);
            TailDeliveryLog.Record(Cell, N);            // preferred input briefly took one
            TailDeliveryLog.Record(Cell, W);
            Assert.AreEqual(W, TailDeliveryLog.Settled(Cell));
        }

        [Test]
        public void Log_SteadyNewDirection_SettlesAfterAStreak()
        {
            TailDeliveryLog.Record(Cell, W);
            for (int i = 0; i < TailDeliveryLog.SettleDeliveries - 1; i++) TailDeliveryLog.Record(Cell, E);
            Assert.AreEqual(W, TailDeliveryLog.Settled(Cell));
            TailDeliveryLog.Record(Cell, E);
            Assert.AreEqual(E, TailDeliveryLog.Settled(Cell));
        }

        [Test]
        public void Log_RepeatDeliveries_DoNotBumpVersion()
        {
            TailDeliveryLog.Record(Cell, W);
            int v = TailDeliveryLog.Version;
            for (int i = 0; i < 10; i++) TailDeliveryLog.Record(Cell, W);
            Assert.AreEqual(v, TailDeliveryLog.Version);
        }

        [Test]
        public void Log_Forget_StartsTheCellFresh()
        {
            TailDeliveryLog.Record(Cell, W);
            TailDeliveryLog.Forget(Cell);
            Assert.AreEqual(-1, TailDeliveryLog.Settled(Cell));
        }
    }
}
