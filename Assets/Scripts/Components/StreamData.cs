using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Singleton: the rules of the world whose site is live. Written by <see cref="WorldRules.Apply"/>
    /// whenever the grid loads (scene load and every site/world switch).
    /// StreamTransport = 1 in worlds with WorldSO.streamTransport (Chemistry): belts are streams,
    /// buildings draw flow instead of eV, Maxwell's Demon is disabled.
    /// </summary>
    public struct ActiveWorldRules : IComponentData
    {
        public byte StreamTransport;
        /// <summary>The live world's prestige-shop track (e.g. Neutron Economy in Physics): craft speed x. 0 = 1.</summary>
        public float SpeedMult;
        /// <summary>The live world's prestige-shop track (e.g. Fission Yield in Physics): output x. 0 = 1.</summary>
        public float OutputMult;
        /// <summary>The live world's Spring flow track (Spring Pressure in Chemistry): flow x. 0 = 1.</summary>
        public float FlowMult;
    }

    /// <summary>
    /// On every conveyor segment placed in a stream world. Items ride it as on a belt, but buildings
    /// beside it pick the items their recipe uses off it and drop their products onto it
    /// (ConveyorSystem stream passes). Flow is the water left in this segment after the buildings
    /// upstream took theirs (StreamFlowSystem); 0 = dry, items stand still.
    /// HasSource = 1 when a Spring feeds this segment's stream.
    /// </summary>
    public struct StreamSegment : IComponentData
    {
        public float Flow;
        public byte  HasSource;
    }

    /// <summary>A Spring (BuildingSO.isSpring): the head of the stream at its output carries Flow L/s.</summary>
    public struct StreamSource : IComponentData
    {
        public float Flow;
    }

    /// <summary>A building that draws flow (BuildingSO.flowDraw) from the stream beside it.</summary>
    public struct StreamConsumer : IComponentData
    {
        public float Draw;
    }

    /// <summary>
    /// Per-building stream state, written every frame by StreamFlowSystem and read by ProductionSystem
    /// and CollectorSystem (speed multiplier) and the inspector.
    /// OnStream = 1 when a port of the building touches a stream segment; Ratio is the share of its
    /// draw it got (1 = full, 0 = dry upstream or not beside a stream).
    /// </summary>
    public struct StreamStatus : IComponentData
    {
        public byte  OnStream;
        public float Ratio;

        /// <summary>
        /// The stream this building is on (2026-11-27): every tile linked to it, branches that merge in included.
        /// Its Springs' total flow, what every building on it draws, and how many Springs feed it. 0 when the
        /// building is beside no stream. Streams that never join are tracked apart.
        /// </summary>
        public float StreamSupply;
        public float StreamDraw;
        public int   StreamSprings;
    }

    /// <summary>
    /// A bank building's stack setting (every building with a flow draw, i.e. Chemistry's producers): it drops its
    /// products onto the stream in stacks of <see cref="Size"/> (1-5), once it holds that many. Capped by the
    /// stacking research (<see cref="ResearchEffects.MaxStreamStack"/>), so it acts as 1 until that is done.
    /// Saved in <c>BuildingSaveData.stackSize</c>. See <see cref="StreamStackRules"/>.
    /// </summary>
    public struct StreamStack : IComponentData
    {
        public byte Size;
    }

    /// <summary>
    /// Singleton: totals for the HUD flow readout (StreamFlowSystem). Supply/Draw add up every stream on the
    /// site; each stream is still shared on its own (a Spring only feeds the stream it pours into).
    /// </summary>
    public struct StreamGridState : IComponentData
    {
        public float Supply;
        public float Draw;
        public int   StalledCount;
        /// <summary>Separate streams on the site (a branch that merges into another is part of it).</summary>
        public int   StreamCount;
        /// <summary>Streams whose buildings draw more than their own Springs supply (warned even when the site total is fine).</summary>
        public int   OverdrawnCount;
    }
}
