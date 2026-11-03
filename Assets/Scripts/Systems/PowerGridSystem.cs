using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Proximity power grid with a single shared eV pool and range-limited node-to-node connectivity.
    ///
    /// Each frame:
    ///   1. Clamps every PowerNodeData.CurrentEV to [0, MaxEV] (legacy contract, kept for tests).
    ///   2. Gathers every power node (PowerNodeData + GridPosition) into footprint boxes.
    ///   3. Connectivity flood: a node is grid-linked if it is a generator (MaxEV &gt; 0) or chains, link by
    ///      link, to one. Two nodes link when within max(LinkRadiusA, LinkRadiusB) of each other. Writes
    ///      each node's PowerNodeData.IsGridLinked and sums supply from linked generators.
    ///   4. For each consumer (PowerConsumer + PowerStatus): connected if its footprint is within the
    ///      influence radius of any LINKED node. Effective draw = DrawEV x AppliedPowerMult (manager
    ///      PowerDiscount). Connected consumers add to global draw.
    ///   5. ratio = draw &lt;= supply ? 1 : supply/draw. Each consumer's ThrottleRatio = connected ? ratio : 0.
    ///   6. Writes the PowerGridState singleton for the HUD.
    ///
    /// Runs before ProductionSystem so crafting reads fresh power state in the same frame.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ProductionSystem))]
    public partial struct PowerGridSystem : ISystem
    {
        private struct GenBox
        {
            public int   MinX, MinY, MaxX, MaxY;
            public float Radius;
        }

        private struct NodeBox
        {
            public Entity E;
            public int    MinX, MinY, MaxX, MaxY;
            public float  Radius;      // InfluenceRadius (coverage)
            public float  LinkRange;   // LinkRadius (node-to-node connection)
            public float  MaxEV;
            public bool   IsGenerator; // MaxEV > 0
        }

        // Not Burst-compiled: creates the grid-state singleton entity (a structural change).
        public void OnCreate(ref SystemState state)
        {
            // The grid state singleton always exists so OnUpdate runs even with no power buildings
            // (e.g. to mark a consumer disconnected after its only generator is removed).
            state.EntityManager.CreateEntity(typeof(PowerGridState));
            state.RequireForUpdate<PowerGridState>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            // ---- 1. Legacy clamp ------------------------------------------------
            foreach (var node in SystemAPI.Query<RefRW<PowerNodeData>>())
            {
                if (node.ValueRO.CurrentEV > node.ValueRO.MaxEV)
                    node.ValueRW.CurrentEV = node.ValueRO.MaxEV;
                if (node.ValueRO.CurrentEV < 0f)
                    node.ValueRW.CurrentEV = 0f;
            }

            // ---- 2. Gather all power nodes -------------------------------------
            var allNodes = new NativeList<NodeBox>(8, Allocator.Temp);

            foreach (var (node, pos, entity) in
                     SystemAPI.Query<RefRO<PowerNodeData>, RefRO<GridPosition>>().WithEntityAccess())
            {
                int2 cell = pos.ValueRO.Cell;
                int w = 1, h = 1;
                if (SystemAPI.HasComponent<BuildingFootprint>(entity))
                {
                    var fp = SystemAPI.GetComponent<BuildingFootprint>(entity);
                    w = fp.Width;
                    h = fp.Height;
                }

                allNodes.Add(new NodeBox
                {
                    E           = entity,
                    MinX        = cell.x,
                    MinY        = cell.y,
                    MaxX        = cell.x + w - 1,
                    MaxY        = cell.y + h - 1,
                    Radius      = node.ValueRO.InfluenceRadius,
                    LinkRange   = node.ValueRO.LinkRadius,
                    MaxEV       = node.ValueRO.MaxEV,
                    IsGenerator = node.ValueRO.MaxEV > 0f
                });
            }

            // ---- 3. Connectivity flood + supply --------------------------------
            // A node is grid-linked if it is a generator or chains (within max link range) to one.
            // Only linked nodes provide consumer coverage; supply sums the linked generators.
            int nodeCount = allNodes.Length;
            var linked = new NativeArray<bool>(nodeCount, Allocator.Temp);
            for (int i = 0; i < nodeCount; i++)
                linked[i] = allNodes[i].IsGenerator;

            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < nodeCount; i++)
                {
                    for (int j = i + 1; j < nodeCount; j++)
                    {
                        if (linked[i] == linked[j]) continue;
                        if (NodesLinked(allNodes[i], allNodes[j]))
                        {
                            linked[i] = true;
                            linked[j] = true;
                            changed   = true;
                        }
                    }
                }
            }

            var   gens          = new NativeList<GenBox>(8, Allocator.Temp);
            float supply        = 0f;
            int   linkedNodes   = 0;
            var   nodeLookup    = SystemAPI.GetComponentLookup<PowerNodeData>(false);

            for (int i = 0; i < nodeCount; i++)
            {
                var n = allNodes[i];

                // Persist the connectivity result so the HUD/inspector/overlay can read it per node.
                var data = nodeLookup[n.E];
                data.IsGridLinked = linked[i];
                nodeLookup[n.E] = data;

                if (!linked[i]) continue;

                linkedNodes++;
                supply += n.MaxEV;
                gens.Add(new GenBox
                {
                    MinX   = n.MinX,
                    MinY   = n.MinY,
                    MaxX   = n.MaxX,
                    MaxY   = n.MaxY,
                    Radius = n.Radius
                });
            }

            // ---- 4. Connection + global draw -----------------------------------
            float draw = 0f;
            int   connectedCount = 0;
            int   disconnectedCount = 0;

            foreach (var (consumer, status, pos, entity) in
                     SystemAPI.Query<RefRO<PowerConsumer>, RefRW<PowerStatus>, RefRO<GridPosition>>()
                              .WithEntityAccess())
            {
                int2 cell = pos.ValueRO.Cell;
                int w = 1, h = 1;
                if (SystemAPI.HasComponent<BuildingFootprint>(entity))
                {
                    var fp = SystemAPI.GetComponent<BuildingFootprint>(entity);
                    w = fp.Width;
                    h = fp.Height;
                }

                bool connected = IsWithinAnyGenerator(gens, cell.x, cell.y, cell.x + w - 1, cell.y + h - 1);
                status.ValueRW.IsConnected = (byte)(connected ? 1 : 0);

                if (connected)
                {
                    connectedCount++;
                    float powerMult = SystemAPI.HasComponent<ManagerAssignmentData>(entity)
                        ? SystemAPI.GetComponent<ManagerAssignmentData>(entity).AppliedPowerMult
                        : 1f;
                    draw += consumer.ValueRO.DrawEV * powerMult;
                }
                else
                {
                    disconnectedCount++;
                }
            }

            float ratio = (draw <= supply || draw <= 0f) ? 1f : supply / draw;

            // ---- 5. Apply throttle ratio ---------------------------------------
            foreach (var status in SystemAPI.Query<RefRW<PowerStatus>>())
            {
                status.ValueRW.ThrottleRatio = status.ValueRO.IsConnected != 0 ? ratio : 0f;
            }

            // ---- 6. Publish grid state -----------------------------------------
            SystemAPI.SetSingleton(new PowerGridState
            {
                Supply            = supply,
                Draw              = draw,
                Ratio             = ratio,
                ConnectedCount    = connectedCount,
                DisconnectedCount = disconnectedCount,
                TotalNodeCount    = nodeCount,
                LinkedNodeCount   = linkedNodes
            });

            gens.Dispose();
            allNodes.Dispose();
            linked.Dispose();
        }

        /// <summary>
        /// True if two power nodes are within grid-connection range of each other: distance within
        /// max(LinkRange) of the two, ORing both footprint orderings so the test is symmetric. Burst mirror
        /// of <see cref="PowerCoverageMath.NodesLinked"/> (keep the two in sync).
        /// </summary>
        private static bool NodesLinked(in NodeBox a, in NodeBox b)
        {
            float range = math.max(a.LinkRange, b.LinkRange);
            if (range <= 0f) return false;
            return FootprintWithinRadius(a.MinX, a.MinY, a.MaxX, a.MaxY, b.MinX, b.MinY, b.MaxX, b.MaxY, range)
                || FootprintWithinRadius(b.MinX, b.MinY, b.MaxX, b.MaxY, a.MinX, a.MinY, a.MaxX, a.MaxY, range);
        }

        /// <summary>Burst mirror of <see cref="PowerCoverageMath.FootprintWithinRadius"/>.</summary>
        private static bool FootprintWithinRadius(
            int srcMinX, int srcMinY, int srcMaxX, int srcMaxY,
            int tgtMinX, int tgtMinY, int tgtMaxX, int tgtMaxY, float radius)
        {
            if (radius <= 0f) return false;
            float srcW = srcMaxX - srcMinX + 1;
            float srcH = srcMaxY - srcMinY + 1;
            float ccx  = (srcMinX + srcMaxX) * 0.5f;
            float ccy  = (srcMinY + srcMaxY) * 0.5f;
            float rc   = radius + math.max(srcW, srcH) * 0.5f;
            float dx = math.max(0f, math.max((tgtMinX - 0.5f) - ccx, ccx - (tgtMaxX + 0.5f)));
            float dy = math.max(0f, math.max((tgtMinY - 0.5f) - ccy, ccy - (tgtMaxY + 0.5f)));
            return dx * dx + dy * dy <= rc * rc;
        }

        /// <summary>
        /// True if the consumer rectangle [cMinX..cMaxX] x [cMinY..cMaxY] overlaps the circular power area
        /// of ANY generator. The power area is a circle centred on the generator footprint with radius
        /// Rc = InfluenceRadius + half its larger dimension; a consumer connects if any part of its
        /// footprint SQUARE (each cell spans +/-0.5) touches that circle. Managed mirror:
        /// <see cref="PowerCoverageMath.FootprintWithinRadius"/> (keep the two in sync).
        /// </summary>
        private static bool IsWithinAnyGenerator(NativeList<GenBox> gens,
                                                 int cMinX, int cMinY, int cMaxX, int cMaxY)
        {
            float tMinWX = cMinX - 0.5f, tMaxWX = cMaxX + 0.5f;
            float tMinWY = cMinY - 0.5f, tMaxWY = cMaxY + 0.5f;

            for (int i = 0; i < gens.Length; i++)
            {
                var g = gens[i];
                if (g.Radius <= 0f) continue;

                float srcW = g.MaxX - g.MinX + 1;
                float srcH = g.MaxY - g.MinY + 1;
                float ccx  = (g.MinX + g.MaxX) * 0.5f;
                float ccy  = (g.MinY + g.MaxY) * 0.5f;
                float rc   = g.Radius + math.max(srcW, srcH) * 0.5f;

                float dx = math.max(0f, math.max(tMinWX - ccx, ccx - tMaxWX));
                float dy = math.max(0f, math.max(tMinWY - ccy, ccy - tMaxWY));
                if (dx * dx + dy * dy <= rc * rc) return true;
            }
            return false;
        }
    }
}
