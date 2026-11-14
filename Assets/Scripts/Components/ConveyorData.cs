using Unity.Entities;
using Unity.Mathematics;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Per-segment component on every conveyor belt cell entity.
    /// EntryDir is the travel direction of items flowing INTO this cell.
    /// ExitDir is the direction items leave this cell TOWARD (the cell's single output).
    /// NextSegment is Entity.Null when this is the output-end of the belt.
    /// Straight piece: EntryDir == ExitDir.
    /// Turn piece:     EntryDir != ExitDir (and not a U-turn).
    ///
    /// Merge model: a cell has exactly ONE output (ExitDir / NextSegment) but may have
    /// MULTIPLE inbound segments — any segment whose NextSegment points at this cell. Connectivity
    /// is derived from each cell's ExitDir geometry (see ConveyorPlacer.RelinkAll), so NextSegment,
    /// PrevSegment, EntryDir and IsChainHead are all caches of that geometry. PrevSegment is the
    /// PRIMARY inbound only (first found) — it is NOT authoritative for merges; the simulation finds
    /// every inbound by scanning NextSegment pointers.
    ///
    /// OutputBlocked overrides the geometry: it marks a cell whose forward output is INTENTIONALLY
    /// disconnected (a free dead-end), so it does NOT feed the conveyor in its ExitDir even though it
    /// points at one. This is how a run drawn up to (but not onto) an existing belt stays separate:
    /// belts only merge where an endpoint COINCIDES with an existing cell, never by mere adjacency.
    /// Player intent, not derivable from geometry, so RelinkAll preserves it and it is persisted.
    /// </summary>
    public struct ConveyorSegmentData : IComponentData
    {
        public int2   Cell;
        public int    EntryDir;       // OutputDirection value — travel dir of the primary inbound flow
        public int    ExitDir;        // OutputDirection value — direction this cell outputs TOWARD
        public Entity NextSegment;    // the single segment this cell outputs to (Entity.Null at a tail)
        public Entity PrevSegment;    // primary inbound segment (Entity.Null at a head); see merge note
        public float  TransportTime;  // seconds to traverse this segment (default 1.0)
        public bool   IsChainHead;    // true when this cell has no inbound segment (input-end / source)
        public bool   OutputBlocked;  // true = forward output intentionally not connected (dead-end); see note
        public int    MergeCursor;    // round-robin index among inbound segments (runtime-only, not saved)
    }

    /// <summary>
    /// One item on a belt segment. Every segment carries a buffer of these, ordered front first
    /// (index 0 has the highest Progress and leaves first). Progress 0 = entry edge, 1 = ready to hand off.
    /// Items keep at least <see cref="BeltSettings.Spacing"/> tiles between them along the whole chain, so
    /// a tile holds up to <see cref="BeltSettings.ItemsPerTile"/> items and a blocked belt packs at that spacing.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct ConveyorItem : IBufferElementData
    {
        public int   ItemID;
        public float Progress; // 0..1
    }

    /// <summary>
    /// How many items fit on one belt tile (game_config <c>belt_items_per_tile</c>, default 2). Belt
    /// throughput is belt speed (tiles/s) x ItemsPerTile. Singleton baked in <c>PlayerInventoryAuthoring</c>;
    /// systems fall back to <see cref="DefaultItemsPerTile"/> when it is absent (tests).
    /// </summary>
    public struct BeltSettings : IComponentData
    {
        public const int DefaultItemsPerTile = 2;
        public const int MaxItemsPerTile     = 4;

        public int ItemsPerTile;

        public static int Clamp(int itemsPerTile) =>
            itemsPerTile < 1 ? 1 : itemsPerTile > MaxItemsPerTile ? MaxItemsPerTile : itemsPerTile;

        /// <summary>Minimum distance between two items, in tiles.</summary>
        public static float Spacing(int itemsPerTile) => 1f / Clamp(itemsPerTile);
    }
}
