using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// TailDelivery (#223): which way a belt tail is drawn exiting. Uses the same TailInputPriority
    /// ranking as ConveyorSystem.TryPickInput (#275): the input straight ahead first, the Demon last.
    /// </summary>
    [TestFixture]
    public class TailDeliveryTests
    {
        const int N = 0, E = 1, S = 2, W = 3;

        static bool[] Dirs(params int[] dirs)
        {
            var f = new bool[4];
            foreach (var d in dirs) f[d] = true;
            return f;
        }

        static readonly bool[] NoSinks = new bool[4];

        [Test]
        public void VisualExit_FeedsNothing_KeepsStoredExit() =>
            Assert.AreEqual(E, TailDelivery.VisualExit(E, E, Dirs(), NoSinks));

        [Test]
        public void VisualExit_InputStraightAhead_Unchanged() =>
            Assert.AreEqual(E, TailDelivery.VisualExit(E, E, Dirs(E), NoSinks));

        [Test]
        public void VisualExit_OnlySideInput_BendsTowardIt()
        {
            // The #223 report: a straight tail feeding a building beside it is now drawn bending into it.
            Assert.AreEqual(N, TailDelivery.VisualExit(E, E, Dirs(N), NoSinks));
        }

        [Test]
        public void VisualExit_AheadAndSide_PrefersAhead() =>
            Assert.AreEqual(E, TailDelivery.VisualExit(E, E, Dirs(N, E), NoSinks));

        [Test]
        public void VisualExit_TwoSideInputs_TieGoesToDirectionOrder() =>
            Assert.AreEqual(N, TailDelivery.VisualExit(E, E, Dirs(N, S), NoSinks));

        [Test]
        public void VisualExit_DemonAheadBuildingBeside_PrefersTheBuilding()
        {
            // TailInputPriority: a building's input beats the Demon even when the belt points at the
            // Demon, so a belt ending beside both is drawn into the building, as the items go (#275).
            Assert.AreEqual(N, TailDelivery.VisualExit(E, E, Dirs(N, E), Dirs(E)));
        }

        [Test]
        public void VisualExit_OnlyDemonBeside_BendsIntoIt() =>
            Assert.AreEqual(S, TailDelivery.VisualExit(E, E, Dirs(S), Dirs(S)));

        [Test]
        public void VisualExit_NeverDrawsAUTurn()
        {
            // Items travel East into the tile, so they enter from the West side: an input there is not
            // drawn as the exit.
            Assert.AreEqual(E, TailDelivery.VisualExit(E, E, Dirs(W), NoSinks));
            Assert.AreEqual(S, TailDelivery.VisualExit(E, E, Dirs(W, S), NoSinks));
        }

        [Test]
        public void VisualExit_RankMatchesTailInputPriority()
        {
            // Every combination of one building input and one Demon input around the tile: the drawn
            // exit is whichever TailInputPriority ranks best, the rule ConveyorSystem delivers by.
            for (int b = 0; b < 4; b++)
            for (int s = 0; s < 4; s++)
            {
                if (b == s || b == W || s == W) continue; // W is the entry side (no U-turn)
                int want = TailInputPriority.Rank(b == E, false) <= TailInputPriority.Rank(s == E, true) ? b : s;
                Assert.AreEqual(want, TailDelivery.VisualExit(E, E, Dirs(b, s), Dirs(s)), $"building {b}, demon {s}");
            }
        }
    }
}
