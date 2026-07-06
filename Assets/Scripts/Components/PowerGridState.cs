using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Singleton snapshot of the global power grid, written each frame by <see cref="PowerGridSystem"/>
    /// and read by the HUD (power label + brownout warning). The grid uses a single shared eV pool:
    /// every generator contributes to <see cref="Supply"/>; every connected consumer adds to <see cref="Draw"/>.
    /// </summary>
    public struct PowerGridState : IComponentData
    {
        /// <summary>Total eV produced by all generators (sum of PowerNodeData.MaxEV).</summary>
        public float Supply;
        /// <summary>Total eV demanded by all connected consumers (after manager PowerDiscount).</summary>
        public float Draw;
        /// <summary>Supply/Draw ratio applied to connected consumers: 1 when supply meets demand, else Supply/Draw.</summary>
        public float Ratio;
        /// <summary>Number of consumers within range of a generator.</summary>
        public int   ConnectedCount;
        /// <summary>Number of consumers with no generator in range (unpowered).</summary>
        public int   DisconnectedCount;
        /// <summary>Total number of power nodes (generators + relays) placed.</summary>
        public int   TotalNodeCount;
        /// <summary>Number of power nodes that chain back to a generator (IsGridLinked). Generators are always linked; stranded relays are not.</summary>
        public int   LinkedNodeCount;
    }
}
