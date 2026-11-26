using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Chemistry's replacement for the power grid. Each frame it walks every stream from its Spring
    /// downstream and shares the Spring's flow among the buildings on its banks, upstream first
    /// (<see cref="StreamRules.Take"/>): the first buildings get their full draw, the one where the water
    /// runs out gets a part, the rest stall. Merging streams add their remaining flow.
    /// <para>
    /// A building is on a stream when one of its ports faces a <see cref="StreamSegment"/> (an input facing
    /// it, or an output facing it, the same tests ConveyorSystem uses). It takes its draw at the first
    /// segment it touches. Writes <see cref="StreamStatus"/> on every <see cref="StreamConsumer"/>,
    /// <see cref="StreamSegment.Flow"/>/<see cref="StreamSegment.HasSource"/> on every segment and the
    /// <see cref="StreamGridState"/> totals. Does nothing outside stream worlds (no StreamSegment exists).
    /// </para>
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ProductionSystem))]
    [UpdateBefore(typeof(CollectorSystem))]
    [UpdateBefore(typeof(ConveyorSystem))]
    public partial class StreamFlowSystem : SystemBase
    {
        EntityQuery _segmentQuery;
        EntityQuery _consumerQuery;
        EntityQuery _buildingQuery;

        protected override void OnCreate()
        {
            _segmentQuery  = GetEntityQuery(typeof(ConveyorSegmentData), typeof(StreamSegment));
            _consumerQuery = GetEntityQuery(typeof(StreamConsumer), typeof(StreamStatus));
            _buildingQuery = GetEntityQuery(typeof(BuildingData), typeof(GridPosition));
            var e = EntityManager.CreateEntity(typeof(StreamGridState));
            EntityManager.SetName(e, "StreamGridState");
        }

        protected override void OnUpdate()
        {
            var consumers = _consumerQuery.ToEntityArray(Allocator.Temp);
            var segs      = _segmentQuery.ToEntityArray(Allocator.Temp);
            if (segs.Length == 0 && consumers.Length == 0)
            {
                consumers.Dispose(); segs.Dispose();
                // Nothing on the streams any more (all demolished, or a world without streams): zero the
                // HUD totals so the flow strip doesn't keep showing the last stream's numbers.
                if (SystemAPI.TryGetSingletonEntity<StreamGridState>(out var empty))
                    EntityManager.SetComponentData(empty, default(StreamGridState));
                return;
            }

            var buildingMap = BuildCellMap();
            // Spring Pressure (prestige shop, Chemistry track): every Spring pours this much more.
            float springMult = SystemAPI.TryGetSingleton<ActiveWorldRules>(out var wr) && wr.FlowMult > 0f ? wr.FlowMult : 1f;

            // Every consumer starts "not beside a stream"; segments below overwrite that.
            var status = new Dictionary<Entity, StreamStatus>(consumers.Length);
            foreach (var c in consumers) status[c] = new StreamStatus { OnStream = 0, Ratio = 0f };

            // Heads first: a segment's inbound neighbours are always one hop further from the tail.
            var order   = HeadsFirst(segs);
            var inflow  = new Dictionary<Entity, (float flow, bool source)>(segs.Length);
            float supply = 0f, draw = 0f;

            foreach (var e in order)
            {
                var seg = EntityManager.GetComponentData<ConveyorSegmentData>(e);
                inflow.TryGetValue(e, out var inc);
                float left   = inc.flow;
                bool  source = inc.source;

                // Springs whose output faces this segment pour in.
                for (int dir = 0; dir < 4; dir++)
                {
                    var cell = Adjacent(seg.Cell, dir);
                    if (!buildingMap.TryGetValue(cell, out var b)) continue;
                    if (!EntityManager.HasComponent<StreamSource>(b)) continue;
                    if (!HasPort(b, cell, Opposite(dir), PortType.Output)) continue;
                    float f = EntityManager.GetComponentData<StreamSource>(b).Flow * springMult;
                    left   += f;
                    supply += f;
                    source  = true;
                }

                // Buildings on this segment's banks take their draw, upstream first, in a fixed side order.
                for (int dir = 0; dir < 4; dir++)
                {
                    var cell = Adjacent(seg.Cell, dir);
                    if (!buildingMap.TryGetValue(cell, out var b)) continue;
                    if (!status.TryGetValue(b, out var st) || st.OnStream != 0) continue;
                    if (!HasPort(b, cell, dir, PortType.Input) && !HasPort(b, cell, Opposite(dir), PortType.Output)) continue;

                    float d = EntityManager.GetComponentData<StreamConsumer>(b).Draw;
                    draw += d;
                    float ratio = source ? StreamRules.Take(ref left, d) : 0f;
                    status[b] = new StreamStatus { OnStream = 1, Ratio = ratio };
                }

                EntityManager.SetComponentData(e, new StreamSegment { Flow = left, HasSource = (byte)(source ? 1 : 0) });

                if (seg.NextSegment != Entity.Null && !seg.OutputBlocked)
                {
                    inflow.TryGetValue(seg.NextSegment, out var next);
                    inflow[seg.NextSegment] = (next.flow + left, next.source || source);
                }
            }

            int stalled = 0;
            foreach (var kv in status)
            {
                EntityManager.SetComponentData(kv.Key, kv.Value);
                if (kv.Value.Ratio < 1f) stalled++;
            }
            if (SystemAPI.TryGetSingletonEntity<StreamGridState>(out var gridEnt))
                EntityManager.SetComponentData(gridEnt, new StreamGridState { Supply = supply, Draw = draw, StalledCount = stalled });

            buildingMap.Dispose();
            consumers.Dispose();
            segs.Dispose();
        }

        NativeHashMap<int2, Entity> BuildCellMap()
        {
            var bldgs = _buildingQuery.ToEntityArray(Allocator.Temp);
            var map   = new NativeHashMap<int2, Entity>(bldgs.Length * 4 + 1, Allocator.Temp);
            foreach (var b in bldgs)
            {
                var pos = EntityManager.GetComponentData<GridPosition>(b).Cell;
                int w = 1, h = 1;
                if (EntityManager.HasComponent<BuildingFootprint>(b))
                {
                    var fp = EntityManager.GetComponentData<BuildingFootprint>(b);
                    w = fp.Width; h = fp.Height;
                }
                for (int dx = 0; dx < w; dx++)
                    for (int dy = 0; dy < h; dy++)
                        map.TryAdd(new int2(pos.x + dx, pos.y + dy), b);
            }
            bldgs.Dispose();
            return map;
        }

        /// <summary>Segments ordered by hops from their chain's tail, furthest (heads) first.</summary>
        List<Entity> HeadsFirst(NativeArray<Entity> segs)
        {
            var depth = new Dictionary<Entity, int>(segs.Length);
            var path  = new List<Entity>();
            foreach (var start in segs)
            {
                path.Clear();
                Entity cur = start;
                int known = -1;
                while (cur != Entity.Null && path.Count <= segs.Length)
                {
                    if (depth.TryGetValue(cur, out known)) break;
                    known = -1;
                    path.Add(cur);
                    if (!EntityManager.Exists(cur) || !EntityManager.HasComponent<ConveyorSegmentData>(cur)) break;
                    var s = EntityManager.GetComponentData<ConveyorSegmentData>(cur);
                    cur = s.OutputBlocked ? Entity.Null : s.NextSegment;
                }
                int d = known;
                for (int k = path.Count - 1; k >= 0; k--)
                {
                    d++;
                    if (!depth.ContainsKey(path[k])) depth[path[k]] = d;
                }
            }
            var order = new List<Entity>(segs.Length);
            foreach (var s in segs) order.Add(s);
            order.Sort((a, b) => depth[b].CompareTo(depth[a]));
            return order;
        }

        bool HasPort(Entity building, int2 portCell, int facing, PortType type)
        {
            if (!EntityManager.HasBuffer<PlacedPortData>(building)) return false;
            var ports = EntityManager.GetBuffer<PlacedPortData>(building, isReadOnly: true);
            for (int i = 0; i < ports.Length; i++)
            {
                var p = ports[i];
                if (p.PortType == (int)type && p.CellX == portCell.x && p.CellY == portCell.y && p.Facing == facing)
                    return true;
            }
            return false;
        }

        internal static int2 Adjacent(int2 cell, int dir) => dir switch
        {
            0 => new int2(cell.x, cell.y + 1),
            1 => new int2(cell.x + 1, cell.y),
            2 => new int2(cell.x, cell.y - 1),
            3 => new int2(cell.x - 1, cell.y),
            _ => cell,
        };

        internal static int Opposite(int dir) => (dir + 2) % 4;
    }
}
