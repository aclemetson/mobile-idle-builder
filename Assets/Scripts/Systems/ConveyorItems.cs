using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Reads and writes the items on a belt segment (<see cref="ConveyorItem"/> buffer, front first).
    /// Segments made by <c>ConveyorPlacer</c> carry the buffer from creation; these helpers add it on
    /// demand so a segment created elsewhere (tests, older code paths) still works.
    /// </summary>
    public static class ConveyorItems
    {
        public static bool Has(EntityManager em, Entity seg) =>
            em.HasBuffer<ConveyorItem>(seg) && em.GetBuffer<ConveyorItem>(seg, isReadOnly: true).Length > 0;

        public static int Count(EntityManager em, Entity seg) =>
            em.HasBuffer<ConveyorItem>(seg) ? em.GetBuffer<ConveyorItem>(seg, isReadOnly: true).Length : 0;

        /// <summary>The item that leaves first. Call only when <see cref="Has"/> is true.</summary>
        public static ConveyorItem Front(EntityManager em, Entity seg) =>
            em.GetBuffer<ConveyorItem>(seg, isReadOnly: true)[0];

        /// <summary>The buffer, adding it first if the segment has none (a structural change).</summary>
        public static DynamicBuffer<ConveyorItem> Get(EntityManager em, Entity seg) =>
            em.HasBuffer<ConveyorItem>(seg) ? em.GetBuffer<ConveyorItem>(seg) : em.AddBuffer<ConveyorItem>(seg);

        /// <summary>Puts an item on the segment, keeping the buffer ordered front (highest Progress) first.</summary>
        public static void Add(EntityManager em, Entity seg, int itemId, float progress)
        {
            var buf = Get(em, seg);
            int at = buf.Length;
            while (at > 0 && buf[at - 1].Progress < progress) at--;
            buf.Insert(at, new ConveyorItem { ItemID = itemId, Progress = progress });
        }

        /// <summary>Removes the front item. No-op on an empty segment.</summary>
        public static void RemoveFront(EntityManager em, Entity seg)
        {
            if (!em.HasBuffer<ConveyorItem>(seg)) return;
            var buf = em.GetBuffer<ConveyorItem>(seg);
            if (buf.Length > 0) buf.RemoveAt(0);
        }

        public static void Clear(EntityManager em, Entity seg)
        {
            if (em.HasBuffer<ConveyorItem>(seg)) em.GetBuffer<ConveyorItem>(seg).Clear();
        }
    }

    /// <summary>Pure belt movement rules (unit-tested). Positions are tile Progress, 0..1.</summary>
    public static class BeltMotion
    {
        /// <summary>
        /// Moves an item by <paramref name="step"/> but not past <paramref name="limit"/>. An item already
        /// beyond the limit (the space ahead closed after it moved) stays put rather than sliding back.
        /// </summary>
        public static float Advance(float progress, float step, float limit)
        {
            if (progress >= limit) return progress;
            float next = progress + step;
            return next < limit ? next : limit;
        }

        /// <summary>No limit this frame: the item hands off and carries any overshoot past 1 with it.</summary>
        public const float Open = 2f;

        /// <summary>
        /// How far the front item may go: past the hand-off point (1) while the next segment has room, so
        /// it carries its overshoot into the next tile, otherwise just far enough to keep
        /// <paramref name="spacing"/> to that segment's rearmost item. Clamping at 1 instead loses a sliver of
        /// movement at every hand-off, and a tight queue behind loses it too: belts ran 3-12% slow.
        /// <paramref name="nextRear"/> is null when the next segment is empty.
        /// </summary>
        public static float FrontLimitBehind(float? nextRear, float spacing) =>
            nextRear == null ? Open : 1f + nextRear.Value - spacing;

        /// <summary>Where the front item of a blocked tail waits: the middle of its own slot.</summary>
        public static float TailRest(float spacing) => 1f - spacing * 0.5f;

        /// <summary>True when an item can join behind <paramref name="rear"/> (null = empty segment) at <paramref name="entryProgress"/>.</summary>
        public static bool HasRoom(float? rear, float entryProgress, float spacing) =>
            rear == null || rear.Value - entryProgress >= spacing - 1e-4f;
    }
}
