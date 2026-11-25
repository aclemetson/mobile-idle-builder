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

        /// <summary>
        /// The edge this item entered its current segment through, as <c>OutputDirection + 1</c>; 0 = not
        /// recorded (head pulls, tests), meaning the segment's own entry. Visual only: on a merge each item
        /// follows the arm it actually came in on instead of all items snapping to the latest input.
        /// </summary>
        public byte InEdge;

        /// <summary>Stores <paramref name="edgeDir"/> (an OutputDirection value) as <see cref="InEdge"/>.</summary>
        public static byte EncodeEdge(int edgeDir) => (byte)(edgeDir + 1);

        /// <summary>The recorded entry edge, or <paramref name="fallbackEdge"/> when none was recorded.</summary>
        public int EntryEdgeOr(int fallbackEdge) => InEdge > 0 ? InEdge - 1 : fallbackEdge;

        /// <summary>
        /// On a belt split (<see cref="BeltSplit"/>): the side this item will leave by, as <c>OutputDirection + 1</c>;
        /// 0 = not chosen yet (or not on a split). Picked by weighted turns when the item reaches the split, so it
        /// visibly curves toward its branch; re-picked at the edge if that branch has no room.
        /// </summary>
        public byte OutEdge;

        /// <summary>The planned exit, or <paramref name="fallbackDir"/> when none is planned.</summary>
        public int ExitEdgeOr(int fallbackDir) => OutEdge > 0 ? OutEdge - 1 : fallbackDir;
    }

    /// <summary>
    /// An overpass (2026-11-24): this belt tile is the ramp up of a raised straight span, and hands its items to
    /// the ramp-down tile at <see cref="Exit"/> instead of its neighbour. The tiles in between stay free for belts
    /// underneath (buildings can't go there, <see cref="GridOccupancy.IsUnderOverpass"/>). The segment is
    /// <see cref="Length"/> tiles long (the ramp-up tile plus the tiles crossed): its items move that much slower
    /// in progress and sit that much closer in it, so they travel at belt speed and the span carries as much as a
    /// belt. Unlocked by the Overpasses research (<see cref="OverpassRules"/>).
    /// </summary>
    public struct BeltBridge : IComponentData
    {
        public int2 Exit;     // the ramp-down tile
        public int  Length;   // tiles from the ramp-up's entry edge to the ramp-down's entry edge (crossed + 1)
    }

    /// <summary>
    /// A belt split (2026-11-24): this belt tile hands its items to 2 or 3 sides instead of one, by weighted turns.
    /// Set by press-and-hold on a belt (BeltSplitCard). Replaces the Belt Splitter buildings. The tile's
    /// <see cref="ConveyorSegmentData.ExitDir"/> stays one of the outputs (the primary), so straight-chain logic
    /// still works; the other outputs are extra. Weights 1..5 per side (default 1 = even). Credits are runtime-only
    /// smooth-weighted-round-robin state (<see cref="BeltSplitRules.Pick"/>), not saved.
    /// </summary>
    public struct BeltSplit : IComponentData
    {
        public byte Mask;               // bit d set = an output toward OutputDirection d
        public byte W0, W1, W2, W3;     // weight per direction (N, E, S, W); 0 where not an output
        public int  C0, C1, C2, C3;     // weighted-turn credits per direction

        public int Weight(int dir) => dir switch { 0 => W0, 1 => W1, 2 => W2, 3 => W3, _ => 0 };

        public void SetWeight(int dir, int w)
        {
            byte b = (byte)w;
            switch (dir) { case 0: W0 = b; break; case 1: W1 = b; break; case 2: W2 = b; break; case 3: W3 = b; break; }
        }

        public int Credit(int dir) => dir switch { 0 => C0, 1 => C1, 2 => C2, 3 => C3, _ => 0 };

        public void SetCredit(int dir, int c)
        {
            switch (dir) { case 0: C0 = c; break; case 1: C1 = c; break; case 2: C2 = c; break; case 3: C3 = c; break; }
        }
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
