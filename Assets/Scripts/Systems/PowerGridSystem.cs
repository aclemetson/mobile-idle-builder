using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Proximity power grid with a single shared eV pool.
    ///
    /// Each frame:
    ///   1. Clamps every PowerNodeData.CurrentEV to [0, MaxEV] (legacy contract, kept for tests).
    ///   2. Gathers generators (PowerNodeData + GridPosition) into footprint boxes and sums supply.
    ///   3. For each consumer (PowerConsumer + PowerStatus): connected if its footprint is within the
    ///      influence radius of ANY generator. Effective draw = DrawEV x AppliedPowerMult (manager
    ///      PowerDiscount). Connected consumers add to global draw.
    ///   4. ratio = draw &lt;= supply ? 1 : supply/draw. Each consumer's ThrottleRatio = connected ? ratio : 0.
    ///   5. Writes the PowerGridState singleton for the HUD.
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

            // ---- 2. Gather generators + supply ---------------------------------
            var gens   = new NativeList<GenBox>(8, Allocator.Temp);
            float supply = 0f;

            foreach (var (node, pos, entity) in
                     SystemAPI.Query<RefRO<PowerNodeData>, RefRO<GridPosition>>().WithEntityAccess())
            {
                supply += node.ValueRO.MaxEV;

                int2 cell = pos.ValueRO.Cell;
                int w = 1, h = 1;
                if (SystemAPI.HasComponent<BuildingFootprint>(entity))
                {
                    var fp = SystemAPI.GetComponent<BuildingFootprint>(entity);
                    w = fp.Width;
                    h = fp.Height;
                }

                gens.Add(new GenBox
                {
                    MinX   = cell.x,
                    MinY   = cell.y,
                    MaxX   = cell.x + w - 1,
                    MaxY   = cell.y + h - 1,
                    Radius = node.ValueRO.InfluenceRadius
                });
            }

            // ---- 3. Connection + global draw -----------------------------------
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

            // ---- 4. Apply throttle ratio ---------------------------------------
            foreach (var status in SystemAPI.Query<RefRW<PowerStatus>>())
            {
                status.ValueRW.ThrottleRatio = status.ValueRO.IsConnected != 0 ? ratio : 0f;
            }

            // ---- 5. Publish grid state -----------------------------------------
            SystemAPI.SetSingleton(new PowerGridState
            {
                Supply            = supply,
                Draw              = draw,
                Ratio             = ratio,
                ConnectedCount    = connectedCount,
                DisconnectedCount = disconnectedCount
            });

            gens.Dispose();
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
