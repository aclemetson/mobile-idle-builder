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
        /// True if the consumer rectangle [cMinX..cMaxX] x [cMinY..cMaxY] is within the influence
        /// radius of any generator (Euclidean edge-to-edge tile gap &lt;= radius).
        /// </summary>
        private static bool IsWithinAnyGenerator(NativeList<GenBox> gens,
                                                 int cMinX, int cMinY, int cMaxX, int cMaxY)
        {
            for (int i = 0; i < gens.Length; i++)
            {
                var g = gens[i];
                int gapX = math.max(0, math.max(g.MinX - cMaxX, cMinX - g.MaxX));
                int gapY = math.max(0, math.max(g.MinY - cMaxY, cMinY - g.MaxY));
                float distSq = gapX * (float)gapX + gapY * (float)gapY;
                if (distSq <= g.Radius * g.Radius) return true;
            }
            return false;
        }
    }
}
