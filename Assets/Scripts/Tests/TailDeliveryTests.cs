using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>TailDelivery (#223): the order a belt tail tries adjacent inputs, and how it is drawn.</summary>
    [TestFixture]
    public class TailDeliveryTests
    {
        const int N = 0, E = 1, S = 2, W = 3;

        static int[] Order(int exitDir)
        {
            var o = new int[4];
            for (int i = 0; i < 4; i++) o[i] = TailDelivery.DirAt(exitDir, i);
            return o;
        }

        [Test]
        public void Order_ExitFirst_ThenTheRestAscending()
        {
            CollectionAssert.AreEqual(new[] { N, E, S, W }, Order(N));
            CollectionAssert.AreEqual(new[] { E, N, S, W }, Order(E));
            CollectionAssert.AreEqual(new[] { S, N, E, W }, Order(S));
            CollectionAssert.AreEqual(new[] { W, N, E, S }, Order(W));
        }

        [Test]
        public void Order_InvalidExit_FallsBackToPlainOrder() =>
            CollectionAssert.AreEqual(new[] { N, E, S, W }, Order(-1));

        static bool[] Feeds(params int[] dirs)
        {
            var f = new bool[4];
            foreach (var d in dirs) f[d] = true;
            return f;
        }

        [Test]
        public void VisualExit_FeedsNothing_KeepsStoredExit() =>
            Assert.AreEqual(E, TailDelivery.VisualExit(E, E, Feeds()));

        [Test]
        public void VisualExit_InputStraightAhead_Unchanged() =>
            Assert.AreEqual(E, TailDelivery.VisualExit(E, E, Feeds(E)));

        [Test]
        public void VisualExit_OnlySideInput_BendsTowardIt()
        {
            // The #223 report: a straight tail feeding a building beside it is now drawn bending into it.
            Assert.AreEqual(N, TailDelivery.VisualExit(E, E, Feeds(N)));
        }

        [Test]
        public void VisualExit_AheadAndSide_PrefersAhead() =>
            Assert.AreEqual(E, TailDelivery.VisualExit(E, E, Feeds(N, E)));

        [Test]
        public void VisualExit_NeverDrawsAUTurn()
        {
            // Items travel East into the tile, so they enter from the West side: an input there is not
            // drawn as the exit.
            Assert.AreEqual(E, TailDelivery.VisualExit(E, E, Feeds(W)));
            Assert.AreEqual(S, TailDelivery.VisualExit(E, E, Feeds(W, S)));
        }
    }
}
