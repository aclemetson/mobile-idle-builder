using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Belt splits (2026-11-24): a belt tile that hands its items to 2-3 sides by weighted turns, set by
    /// press-and-hold. They replace the Belt Splitter buildings. Rules, topology, save format, migration of old
    /// splitters, the offline model, and the card's texts.
    /// </summary>
    public class BeltSplitTests
    {
        const int N = 0, E = 1, S = 2, W = 3;

        static int[] Weights(int n, int e, int s, int w) => new[] { n, e, s, w };

        static Dictionary<int, int> Run(int mask, int[] weights, int picks, System.Func<int, bool> eligible = null)
        {
            var credits = new int[4];
            var count = new Dictionary<int, int>();
            for (int i = 0; i < picks; i++)
            {
                int d = BeltSplitRules.Pick(mask, x => weights[x], x => credits[x], (x, c) => credits[x] = c, eligible);
                count[d] = count.TryGetValue(d, out int c0) ? c0 + 1 : 1;
            }
            return count;
        }

        // ── Weighted turns ────────────────────────────────────────────────────

        [Test]
        public void EvenWeights_AlternateExactly()
        {
            var count = Run((1 << N) | (1 << S), Weights(1, 0, 1, 0), 10);
            Assert.AreEqual(5, count[N]);
            Assert.AreEqual(5, count[S]);
        }

        [Test]
        public void Weights_SetTheShare_2To1()
        {
            var count = Run((1 << E) | (1 << N), Weights(1, 2, 0, 0), 30);
            Assert.AreEqual(20, count[E]);
            Assert.AreEqual(10, count[N]);
        }

        [Test]
        public void ThreeWays_5To3To1()
        {
            var count = Run((1 << N) | (1 << E) | (1 << S), Weights(5, 3, 1, 0), 90);
            Assert.AreEqual((50, 30, 10), (count[N], count[E], count[S]));
        }

        [Test]
        public void TheTurnsInterleave_NotBurst()
        {
            var credits = new int[4];
            var w = Weights(1, 2, 0, 0);
            var seq = Enumerable.Range(0, 6)
                .Select(_ => BeltSplitRules.Pick((1 << E) | (1 << N), x => w[x], x => credits[x], (x, c) => credits[x] = c, null))
                .ToList();
            CollectionAssert.AreEqual(new[] { E, N, E, E, N, E }, seq, "2:1 deals E, N, E, E, N, E");
        }

        [Test]
        public void PickOnTheComponent_KeepsItsCredits()
        {
            // Regression: delegates on the struct's own methods boxed a copy and lost every credit update.
            var split = new BeltSplit { Mask = (1 << E) | (1 << N) };
            split.SetWeight(E, 2);
            split.SetWeight(N, 1);
            var seq = Enumerable.Range(0, 6).Select(_ => BeltSplitRules.Pick(ref split, null)).ToList();
            CollectionAssert.AreEqual(new[] { E, N, E, E, N, E }, seq);
        }

        [Test]
        public void ABlockedSide_IsSkipped_AndDoesntHoardTurns()
        {
            var w = Weights(1, 0, 1, 0);
            var credits = new int[4];
            int Pick(bool northOpen) => BeltSplitRules.Pick((1 << N) | (1 << S), x => w[x], x => credits[x],
                                                             (x, c) => credits[x] = c, d => d != N || northOpen);
            for (int i = 0; i < 5; i++) Assert.AreEqual(S, Pick(northOpen: false));
            // North reopens: it gets its turn, not five in a row.
            var next = Enumerable.Range(0, 4).Select(_ => Pick(true)).ToList();
            Assert.LessOrEqual(next.Count(d => d == N), 2);
            Assert.AreEqual(-1, BeltSplitRules.Pick((1 << N), x => 1, x => 0, (x, c) => { }, _ => false), "nothing eligible");
        }

        // ── Which sides may be outputs ────────────────────────────────────────

        [Test]
        public void Normalize_DropsInputSides_AndNeedsTwoToThreeOutputs()
        {
            int fromWest = 1 << W;
            Assert.IsTrue(BeltSplitRules.Normalize((1 << N) | (1 << S) | (1 << W), fromWest, out int m));
            Assert.AreEqual((1 << N) | (1 << S), m, "the way in is never an output");
            Assert.IsFalse(BeltSplitRules.Normalize((1 << N) | (1 << W), fromWest, out _), "one output is just a belt");
            Assert.AreEqual(0b1110, BeltSplitRules.AllowedOutputs(1));
        }

        // ── Save format ───────────────────────────────────────────────────────

        [Test]
        public void Save_RoundTrips_AndIgnoresATruncatedTail()
        {
            var list = new List<BeltSplitData>
            {
                new BeltSplitData { X = 3, Y = 4, Mask = 0b0101, W0 = 2, W2 = 1 },
                new BeltSplitData { X = -1, Y = 9, Mask = 0b1011, W0 = 1, W1 = 5, W3 = 3 },
            };
            var flat = BeltSplitRules.Flatten(list);
            Assert.AreEqual(2 * BeltSplitRules.Stride, flat.Length);
            var back = BeltSplitRules.Parse(flat);
            Assert.AreEqual(list, back);
            Assert.AreEqual(1, BeltSplitRules.Parse(flat.Take(BeltSplitRules.Stride + 3).ToArray()).Count);
            Assert.IsNull(BeltSplitRules.Flatten(new List<BeltSplitData>()));

            var grid = JsonUtility.FromJson<GridSaveData>(JsonUtility.ToJson(new GridSaveData { splits = flat }));
            CollectionAssert.AreEqual(flat, grid.splits);
        }

        // ── Topology ──────────────────────────────────────────────────────────

        [Test]
        public void Topology_ASplitFeedsItsBranch_WhichIsNotAHead()
        {
            // A belt running east: (0,0) -> (1,0) -> (2,0); (1,0) also splits north into (1,1) -> (1,2).
            var exits = new Dictionary<int2, int>
            {
                [new int2(0, 0)] = E, [new int2(1, 0)] = E, [new int2(2, 0)] = E,
                [new int2(1, 1)] = N, [new int2(1, 2)] = N,
            };
            var splits = new Dictionary<int2, int> { [new int2(1, 0)] = (1 << E) | (1 << N) };

            var plain = ConveyorTopology.Compute(exits);
            Assert.IsTrue(plain[new int2(1, 1)].IsHead, "without the split the branch is a separate belt");

            var links = ConveyorTopology.Compute(exits, null, splits);
            var branch = links[new int2(1, 1)];
            Assert.IsFalse(branch.IsHead);
            Assert.AreEqual(1 << S, branch.InboundMask, "fed from the split below it");
            Assert.AreEqual(N, branch.PrimaryInboundExitDir, "travelling north into it");
            Assert.AreEqual(new int2(2, 0), links[new int2(1, 0)].OutputCell, "the split's primary output is its ExitDir");
            Assert.AreEqual(1 << W, links[new int2(1, 0)].InboundMask);
        }

        [Test]
        public void Topology_ASplitWithoutItsPrimaryBelt_LinksToAnotherOutput()
        {
            var exits = new Dictionary<int2, int> { [new int2(0, 0)] = E, [new int2(0, 1)] = N };
            var splits = new Dictionary<int2, int> { [new int2(0, 0)] = (1 << E) | (1 << N) };
            var l = ConveyorTopology.Compute(exits, null, splits)[new int2(0, 0)];
            Assert.IsTrue(l.HasOutput);
            Assert.AreEqual(new int2(0, 1), l.OutputCell);
        }

        [Test]
        public void CanPlace_PassingThroughASplit_AlongAnOutput_IsAllowed()
        {
            var exits = new Dictionary<int2, int> { [new int2(0, 0)] = E, [new int2(1, 0)] = E, [new int2(1, 1)] = N };
            var splits = new Dictionary<int2, int> { [new int2(0, 0)] = (1 << E) | (1 << N) };
            var path = new[] { new int2(-1, 0), new int2(0, 0), new int2(0, 1), new int2(0, 2) };
            Assert.IsFalse(ConveyorTopology.CanPlace(exits, path, 3, out _), "a plain belt can't fork");
            Assert.IsTrue(ConveyorTopology.CanPlace(exits, path, 3, out _, splits));
        }

        // ── Old Belt Splitter buildings ───────────────────────────────────────

        [Test]
        public void Migration_OutputsFollowRotationAndFlip()
        {
            Assert.AreEqual((1 << N) | (1 << S), SplitterMigration.OutputMask(27, 0, false));
            Assert.AreEqual((1 << E) | (1 << W), SplitterMigration.OutputMask(27, 1, false), "turned once: N->E, S->W");
            Assert.AreEqual((1 << N) | (1 << E) | (1 << S), SplitterMigration.OutputMask(28, 0, false));
            Assert.AreEqual((1 << N) | (1 << W) | (1 << S), SplitterMigration.OutputMask(28, 0, true), "flipped: E->W");
        }

        [Test]
        public void Migration_ReplacesTheBuildingWithASplitTile_Once()
        {
            var grid = new GridSaveData();
            grid.buildings.Add(new BuildingSaveData { buildingId = 1, position = new[] { 0, 0 } });
            grid.buildings.Add(new BuildingSaveData { buildingId = 28, position = new[] { 4, 2 }, rotation = 0 });

            Assert.AreEqual(1, SplitterMigration.Convert(grid));
            Assert.AreEqual(1, grid.buildings.Count, "the splitter building is gone");
            var tile = grid.conveyors.Single();
            CollectionAssert.AreEqual(new[] { 4, 2 }, tile.cells);
            var split = BeltSplitRules.Parse(grid.splits).Single();
            Assert.AreEqual((4, 2, (1 << N) | (1 << E) | (1 << S)), (split.X, split.Y, split.Mask));
            Assert.AreEqual((1, 1, 1, 0), (split.W0, split.W1, split.W2, split.W3), "even, as the splitter dealt");
            Assert.AreEqual(0, SplitterMigration.Convert(grid), "idempotent");
        }

        // ── Offline (idle) production ─────────────────────────────────────────

        [Test]
        public void Offline_ASplitSharesTheFlowByWeight()
        {
            // Collector (id 1) at (0,0) -> belt east (1,0) (2,0) (3,0) -> Demon (id 9) at (4,0).
            // (2,0) splits east 2 : north 1; the branch (2,1) (2,2) runs to a second Demon at (2,3).
            var grid = new GridSaveData();
            grid.buildings.Add(new BuildingSaveData { buildingId = 1, position = new[] { 0, 0 } });
            grid.buildings.Add(new BuildingSaveData { buildingId = 9, position = new[] { 4, 0 } });
            grid.buildings.Add(new BuildingSaveData { buildingId = 9, position = new[] { 2, 3 } });
            grid.conveyors.Add(new ConveyorSaveData { cells = new[] { 1, 0, 2, 0, 3, 0 } });
            grid.conveyors.Add(new ConveyorSaveData { cells = new[] { 2, 1, 2, 2 } });
            grid.splits = BeltSplitRules.Flatten(new List<BeltSplitData>
            {
                new BeltSplitData { X = 2, Y = 0, Mask = (1 << E) | (1 << N), W0 = 1, W1 = 2 },
            });

            var snap = IdleGraphAnalyzer.BuildSnapshot(grid,
                id => id == 1, id => id == 9, _ => Vector2Int.one, (_, __) => 7, _ => 3f, _ => 1f, 1f, "t");

            var flows = snap.chains.Where(c => c.endsAtEntropySink).Select(c => c.itemsPerSecond).OrderBy(x => x).ToList();
            Assert.AreEqual(2, flows.Count, "both Demons are fed");
            Assert.AreEqual(1f, flows[0], 1e-4f, "north takes 1/3 of 3/s");
            Assert.AreEqual(2f, flows[1], 1e-4f, "east takes 2/3");
        }

        [Test]
        public void Offline_WithoutTheSplit_TheBranchEarnsNothing()
        {
            var grid = new GridSaveData();
            grid.buildings.Add(new BuildingSaveData { buildingId = 1, position = new[] { 0, 0 } });
            grid.buildings.Add(new BuildingSaveData { buildingId = 9, position = new[] { 4, 0 } });
            grid.conveyors.Add(new ConveyorSaveData { cells = new[] { 1, 0, 2, 0, 3, 0 } });
            var snap = IdleGraphAnalyzer.BuildSnapshot(grid,
                id => id == 1, id => id == 9, _ => Vector2Int.one, (_, __) => 7, _ => 3f, _ => 1f, 1f, "t");
            Assert.AreEqual(3f, snap.chains.Single().itemsPerSecond, 1e-4f, "the whole flow, as before");
        }

        // ── The card ──────────────────────────────────────────────────────────

        [Test]
        public void CardTexts()
        {
            Assert.AreEqual("West (in)", BeltSplitText.SideLabel("West", isInput: true, on: false));
            Assert.AreEqual("North: on", BeltSplitText.SideLabel("North", false, true));
            Assert.AreEqual("Splits 1 : 2 (North, East)", BeltSplitText.Summary((1 << N) | (1 << E), 1 << W, Weights(1, 2, 1, 1)));
            Assert.AreEqual("Not split: one way out.", BeltSplitText.Summary(1 << E, 1 << W, Weights(1, 1, 1, 1)));
        }
    }
}
