using System.Collections.Generic;
using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>Overpasses (2026-11-24): the rules, the links, tracing an overpass, and the live belt system over one.</summary>
    public class OverpassTests
    {
        const int N = 0, E = 1, S = 2, W = 3;

        static Vector2Int V(int x, int y) => new(x, y);
        static int2 C(int x, int y) => new(x, y);

        // ---- Rules ----------------------------------------------------------

        [Test]
        public void MaxSpan_FollowsTheResearch()
        {
            Assert.AreEqual(0, OverpassRules.MaxSpan(_ => false));
            Assert.AreEqual(4, OverpassRules.MaxSpan(id => id == OverpassRules.ResearchId));
            Assert.AreEqual(8, OverpassRules.MaxSpan(_ => true));
            Assert.AreEqual(0, OverpassRules.MaxSpan(null));
        }

        [Test]
        public void CanSpan_ChecksShapeLengthAndWhatIsUnder()
        {
            bool None(Vector2Int c) => false;
            Assert.AreEqual(OverpassRules.Check.Ok,          OverpassRules.CanSpan(V(0, 0), V(5, 0), 4, None, None));
            Assert.AreEqual(OverpassRules.Check.Locked,      OverpassRules.CanSpan(V(0, 0), V(3, 0), 0, None, None));
            Assert.AreEqual(OverpassRules.Check.NotStraight, OverpassRules.CanSpan(V(0, 0), V(3, 2), 4, None, None));
            Assert.AreEqual(OverpassRules.Check.TooShort,    OverpassRules.CanSpan(V(0, 0), V(0, 1), 4, None, None));
            Assert.AreEqual(OverpassRules.Check.TooLong,     OverpassRules.CanSpan(V(0, 0), V(0, 6), 4, None, None));
            Assert.AreEqual(OverpassRules.Check.OverBuilding,
                OverpassRules.CanSpan(V(0, 0), V(3, 0), 4, c => c == V(2, 0), None));
            Assert.AreEqual(OverpassRules.Check.OverOverpass,
                OverpassRules.CanSpan(V(0, 0), V(3, 0), 4, None, c => c == V(1, 0)));
            Assert.AreEqual(OverpassRules.Check.RampUnderOverpass,
                OverpassRules.CanSpan(V(0, 0), V(3, 0), 4, None, c => c == V(0, 0)));
        }

        [Test]
        public void CrossedCells_AreTheTilesBetween()
        {
            CollectionAssert.AreEqual(new[] { V(0, 2), V(0, 1) }, new List<Vector2Int>(OverpassRules.CrossedCells(V(0, 3), V(0, 0))));
            Assert.AreEqual(2, OverpassRules.Crossed(V(0, 3), V(0, 0)));
            Assert.IsTrue(OverpassRules.IsSpan(V(0, 0), V(2, 0)));
            Assert.IsFalse(OverpassRules.IsSpan(V(0, 0), V(1, 0)));
        }

        [Test]
        public void Messages_FitTheBanner()
        {
            foreach (OverpassRules.Check c in System.Enum.GetValues(typeof(OverpassRules.Check)))
            {
                string m = OverpassRules.Message(c, 8);
                if (c == OverpassRules.Check.Ok) Assert.IsNull(m);
                else Assert.LessOrEqual(m.Length, 45, m);
            }
            foreach (var m in new[] { ConveyorTrace.SpanStartsOnBeltMessage, ConveyorTrace.SpanAfterSpanMessage,
                                      ConveyorTrace.SpanLandsOnBeltMessage, BeltSplitCard.RampMessage,
                                      ConveyorPlacer.PlayerReason("cell (1,1) is an overpass ramp; join it from the side") })
                Assert.LessOrEqual(m.Length, 45, m);
        }

        [Test]
        public void RampLift_ClimbsTowardTheSpanAndMeetsTheDeck()
        {
            const float cs = 1f;
            Assert.AreEqual(0f, ConveyorFlowMeshBuilder.RampLift(new Vector3(-0.5f, 0, 0), E, cs, true), 1e-5f);
            Assert.AreEqual(0f, ConveyorFlowMeshBuilder.RampLift(Vector3.zero, E, cs, true), 1e-5f);
            Assert.AreEqual(OverpassRules.DeckHeight, ConveyorFlowMeshBuilder.RampLift(new Vector3(0.5f, 0, 0), E, cs, true), 1e-5f);
            // The ramp down comes down on the side the span arrives from (west, for an eastbound span).
            Assert.AreEqual(OverpassRules.DeckHeight, ConveyorFlowMeshBuilder.RampLift(new Vector3(-0.5f, 0, 0), E, cs, false), 1e-5f);
            Assert.AreEqual(0f, ConveyorFlowMeshBuilder.RampLift(new Vector3(0.5f, 0, 0), E, cs, false), 1e-5f);
            Assert.Greater(OverpassRules.DeckHeight, 0.45f, "clears the items on the belts underneath");
        }

        // ---- Links (ConveyorTopology) --------------------------------------

        // An eastbound overpass from (0,0) to (3,0) over (1,0) and (2,0), then on to (4,0). A northbound belt runs
        // under it through (1,0); (2,0) holds a belt pointing east at the ramp down.
        static Dictionary<int2, int> Cells() => new()
        {
            { C(0, 0), E }, { C(3, 0), E }, { C(4, 0), E },
            { C(1, -1), N }, { C(1, 0), N }, { C(1, 1), N },
            { C(2, 0), E },
        };
        static Dictionary<int2, int2> Bridges() => new() { { C(0, 0), C(3, 0) } };

        [Test]
        public void RampUp_LinksAcrossTheSpan_AndNotToTheBeltUnder()
        {
            var links = ConveyorTopology.Compute(Cells(), null, null, Bridges());
            Assert.IsTrue(links[C(0, 0)].HasOutput);
            Assert.AreEqual(C(3, 0), links[C(0, 0)].OutputCell);

            var down = links[C(3, 0)];
            Assert.IsFalse(down.IsHead);
            Assert.AreEqual(1, down.InboundCount, "only the overpass: the belt under it pointing here doesn't join");
            Assert.AreEqual(C(0, 0), down.PrimaryInboundCell);
            Assert.AreEqual(E, down.PrimaryInboundExitDir);
            Assert.AreEqual(1 << W, down.InboundMask);

            Assert.AreEqual(1, links[C(1, 0)].InboundCount, "the crossing belt is fed by its own belt, not the ramp");
            Assert.AreEqual(C(1, -1), links[C(1, 0)].PrimaryInboundCell);
            Assert.AreEqual(C(1, 1), links[C(1, 0)].OutputCell);
            Assert.IsFalse(links[C(2, 0)].HasOutput, "a belt under the span ends at the ramp");
        }

        [Test]
        public void WithoutTheBridge_TheSameCellsWouldLinkOnTheGround()
        {
            var links = ConveyorTopology.Compute(Cells());
            Assert.AreEqual(C(1, 0), links[C(0, 0)].OutputCell);
            Assert.AreEqual(C(2, 0), links[C(3, 0)].PrimaryInboundCell);
        }

        [Test]
        public void CanPlace_KeepsTheRampsWhole()
        {
            var cells = Cells();
            var bridges = Bridges();
            // Redrawing the ramp up somewhere else would break the span.
            Assert.IsFalse(ConveyorTopology.CanPlace(cells, new[] { C(0, 0), C(0, 1) }, 3, out string r1, null, bridges));
            StringAssert.Contains("overpass", r1);
            // Joining the ramp down from under the span.
            Assert.IsFalse(ConveyorTopology.CanPlace(cells, new[] { C(2, 1), C(2, 0), C(3, 0) }, 3, out _, null, bridges));
            // Joining it from the side is fine.
            Assert.IsTrue(ConveyorTopology.CanPlace(cells, new[] { C(3, 2), C(3, 1), C(3, 0) }, 3, out string r3, null, bridges), r3);
            // Replaying the span itself (a save load) is fine.
            Assert.IsTrue(ConveyorTopology.CanPlace(cells, new[] { C(0, 0), C(3, 0), C(4, 0) }, 3, out string r4, null, bridges), r4);
        }

        // ---- Tracing --------------------------------------------------------

        static ConveyorTrace Trace(HashSet<Vector2Int> belts, System.Func<Vector2Int, Vector2Int, string> spanReject = null) =>
            new(c => c.x >= -10 && c.x <= 10 && c.y >= -10 && c.y <= 10 && !belts.Contains(c), belts.Contains,
                null, null, spanReject);

        [Test]
        public void RaisedTap_LaysAStraightSpanOverBelts()
        {
            var belts = new HashSet<Vector2Int> { V(2, -1), V(2, 0), V(2, 1) };   // a belt to cross
            var t = Trace(belts);
            Assert.AreEqual(ConveyorTrace.AddResult.Started, t.TryAdd(V(0, 0)));
            Assert.AreEqual(ConveyorTrace.AddResult.Added,   t.TryAdd(V(1, 0)));
            Assert.AreEqual(ConveyorTrace.AddResult.Added,   t.TryAdd(V(4, 0), raised: true));
            Assert.IsTrue(t.LastIsSpan);
            CollectionAssert.AreEqual(new[] { V(0, 0), V(1, 0), V(4, 0) }, t.Path());
            CollectionAssert.AreEqual(new[] { V(2, 0), V(3, 0) }, t.CrossedCells());
            Assert.IsFalse(t.FlipLastBend(), "an overpass has no bend");

            // Another overpass straight off the ramp down is refused; a ground tile first is fine.
            Assert.AreEqual(ConveyorTrace.AddResult.Rejected, t.TryAdd(V(7, 0), raised: true));
            Assert.AreEqual(ConveyorTrace.SpanAfterSpanMessage, t.LastRejectReason);
            Assert.AreEqual(ConveyorTrace.AddResult.Added, t.TryAdd(V(5, 0)));
            Assert.IsFalse(t.LastIsSpan);

            Assert.IsTrue(t.Undo());
            Assert.IsTrue(t.Undo());
            Assert.IsFalse(t.LastIsSpan);
            Assert.AreEqual(0, t.CrossedCells().Count);
        }

        [Test]
        public void RaisedTap_RefusalsSayWhy()
        {
            var belts = new HashSet<Vector2Int> { V(0, 0), V(3, 0) };
            var t = Trace(belts, (a, b) => OverpassRules.Message(OverpassRules.CanSpan(a, b, 4, null, null), 4));

            Assert.AreEqual(ConveyorTrace.AddResult.Started, t.TryAdd(V(0, 0)));          // on an existing belt
            Assert.AreEqual(ConveyorTrace.AddResult.Rejected, t.TryAdd(V(0, 3), raised: true));
            Assert.AreEqual(ConveyorTrace.SpanStartsOnBeltMessage, t.LastRejectReason);

            t.Clear();
            t.TryAdd(V(0, 1));
            Assert.AreEqual(ConveyorTrace.AddResult.Rejected, t.TryAdd(V(3, 2), raised: true));
            Assert.AreEqual("Overpasses run in a straight line.", t.LastRejectReason);
            Assert.AreEqual(ConveyorTrace.AddResult.Rejected, t.TryAdd(V(0, 7), raised: true));
            Assert.AreEqual("Overpasses cross up to 4 tiles.", t.LastRejectReason);

            t.Clear();
            t.TryAdd(V(3, -3));
            Assert.AreEqual(ConveyorTrace.AddResult.Rejected, t.TryAdd(V(3, 0), raised: true));
            Assert.AreEqual(ConveyorTrace.SpanLandsOnBeltMessage, t.LastRejectReason);
        }

        // ---- The live belt system over an overpass --------------------------

        World _world;
        EntityManager _em;
        ConveyorSystem _system;
        readonly Dictionary<int2, Entity> _cells = new();

        [SetUp]
        public void SetUp()
        {
            _world  = new World("OverpassTests");
            _em     = _world.EntityManager;
            _system = _world.GetOrCreateSystemManaged<ConveyorSystem>();
            _cells.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        Entity Belt(int x, int y, int exit)
        {
            var e = _em.CreateEntity(typeof(ConveyorSegmentData), typeof(GridPosition), typeof(ConveyorItem));
            _em.SetComponentData(e, new GridPosition { Cell = C(x, y) });
            _em.SetComponentData(e, new ConveyorSegmentData { Cell = C(x, y), EntryDir = exit, ExitDir = exit, TransportTime = 1f });
            _cells[C(x, y)] = e;
            return e;
        }

        void Relink(Dictionary<int2, int2> bridges)
        {
            var exits = new Dictionary<int2, int>();
            foreach (var kv in _cells) exits[kv.Key] = _em.GetComponentData<ConveyorSegmentData>(kv.Value).ExitDir;
            var links = ConveyorTopology.Compute(exits, null, null, bridges);
            foreach (var kv in _cells)
            {
                var s = _em.GetComponentData<ConveyorSegmentData>(kv.Value);
                var l = links[kv.Key];
                s.NextSegment = l.HasOutput ? _cells[l.OutputCell] : Entity.Null;
                s.IsChainHead = l.IsHead;
                s.PrevSegment = l.HasPrimaryInbound ? _cells[l.PrimaryInboundCell] : Entity.Null;
                s.EntryDir    = l.IsHead ? s.ExitDir : l.PrimaryInboundExitDir;
                _em.SetComponentData(kv.Value, s);
            }
        }

        int ItemsOn(IEnumerable<Entity> segs, int itemId)
        {
            int n = 0;
            foreach (var e in segs)
                foreach (var it in _em.GetBuffer<ConveyorItem>(e)) if (it.ItemID == itemId) n++;
            return n;
        }

        void Run(float seconds, System.Action beforeFrame = null)
        {
            const float dt = 0.02f;
            double time = 0;
            for (float t = 0; t < seconds; t += dt)
            {
                beforeFrame?.Invoke();
                time += dt;
                _world.SetTime(new TimeData(time, dt));
                _system.Update();
            }
        }

        // Eastbound overpass (0,0) -> (4,0) over (1..3,0), on to (5..24,0); a northbound belt crosses under at (2,0).
        (Entity rampUp, List<Entity> east, List<Entity> north) BuildCrossing()
        {
            var rampUp = Belt(0, 0, E);
            _em.AddComponentData(rampUp, new BeltBridge { Exit = C(4, 0), Length = 4 });
            var east = new List<Entity>();
            for (int x = 4; x <= 24; x++) east.Add(Belt(x, 0, E));
            var north = new List<Entity>();
            for (int y = -1; y <= 8; y++) north.Add(Belt(2, y, N));
            Relink(new Dictionary<int2, int2> { { C(0, 0), C(4, 0) } });
            return (rampUp, east, north);
        }

        [Test]
        public void ItemsCrossOverAndUnder_EachStaysOnItsOwnBelt()
        {
            var (rampUp, east, north) = BuildCrossing();
            Assert.AreEqual(east[0], _em.GetComponentData<ConveyorSegmentData>(rampUp).NextSegment);

            const int over = 1, under = 2, total = 6;
            int fedOver = 0, fedUnder = 0;
            Run(30f, () =>
            {
                var a = _em.GetBuffer<ConveyorItem>(rampUp);
                if (fedOver < total && (a.Length == 0 || a[a.Length - 1].Progress >= 0.5f / 4f))
                { a.Add(new ConveyorItem { ItemID = over }); fedOver++; }
                var b = _em.GetBuffer<ConveyorItem>(north[0]);
                if (fedUnder < total && (b.Length == 0 || b[b.Length - 1].Progress >= 0.5f))
                { b.Add(new ConveyorItem { ItemID = under }); fedUnder++; }
            });

            Assert.AreEqual(total, ItemsOn(east, over),   "everything sent over arrived past the ramp down");
            Assert.AreEqual(0,     ItemsOn(east, under),  "nothing from underneath climbed on");
            Assert.AreEqual(total, ItemsOn(north, under), "everything sent under stayed on its belt");
            Assert.AreEqual(0,     ItemsOn(north, over),  "nothing fell off the overpass");
        }

        [Test]
        public void TheSpan_RunsAtBeltSpeed()
        {
            var (rampUp, east, _) = BuildCrossing();
            _em.GetBuffer<ConveyorItem>(rampUp).Add(new ConveyorItem { ItemID = 5, Progress = 0f });

            Run(3.8f);   // the span is 4 tiles at 1 tile per second
            Assert.AreEqual(1, _em.GetBuffer<ConveyorItem>(rampUp).Length, "still on the overpass before 4 s");
            Run(0.4f);
            Assert.AreEqual(0, _em.GetBuffer<ConveyorItem>(rampUp).Length, "reached the ramp down by 4.2 s");
            Assert.AreEqual(1, ItemsOn(east, 5));
        }

        [Test]
        public void TheSpan_CarriesAsMuchAsABelt()
        {
            // Fill the overpass from a full source for long enough to reach steady flow; it delivers at the belt's
            // rated rate (1 tile/s over spacing), not a quarter of it.
            var (rampUp, east, _) = BuildCrossing();
            float spacing = BeltSettings.Spacing(BeltSettings.DefaultItemsPerTile);
            int fed = 0;
            Run(12f, () =>
            {
                var a = _em.GetBuffer<ConveyorItem>(rampUp);
                float sp = spacing / 4f;
                if (a.Length == 0 || a[a.Length - 1].Progress >= sp) { a.Add(new ConveyorItem { ItemID = 9 }); fed++; }
            });
            int delivered = ItemsOn(east, 9);
            // Items reach the ramp down after 4 s, then arrive one per spacing (the east belt is long enough not to fill).
            int expected = Mathf.FloorToInt((12f - 4f) / spacing);
            Assert.GreaterOrEqual(delivered, expected - 2, $"fed {fed}, delivered {delivered}");
        }
    }
}
