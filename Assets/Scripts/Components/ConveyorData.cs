using Unity.Entities;
using Unity.Mathematics;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Per-segment component on every conveyor belt cell entity.
    /// EntryDir is the direction items enter this cell FROM (i.e., coming from that direction).
    /// ExitDir is the direction items leave this cell TOWARD.
    /// NextSegment is Entity.Null when this is the output-end of the belt.
    /// PrevSegment is Entity.Null when this is the input-end of the belt.
    /// Straight piece: EntryDir == ExitDir.
    /// Turn piece:     EntryDir != ExitDir (and not a U-turn).
    /// </summary>
    public struct ConveyorSegmentData : IComponentData
    {
        public int2   Cell;
        public int    EntryDir;       // OutputDirection value — direction item comes FROM
        public int    ExitDir;        // OutputDirection value — direction item exits TOWARD
        public Entity NextSegment;    // next segment toward output end
        public Entity PrevSegment;    // prev segment toward input end
        public float  TransportTime;  // seconds to traverse this segment (default 1.0)
        public bool   IsChainHead;    // true for the input-end (head) segment of the belt
    }

    /// <summary>
    /// Added to a segment entity when it is currently carrying an item.
    /// Removed when the item is handed off to the next segment or deposited into a building.
    /// Progress 0 = just entered the segment, 1 = ready to hand off.
    /// </summary>
    public struct ConveyorItemData : IComponentData
    {
        public int   ItemID;
        public float Progress; // 0..1
    }
}
