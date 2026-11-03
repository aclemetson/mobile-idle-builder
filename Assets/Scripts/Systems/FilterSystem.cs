using System.Collections.Generic;
using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Moves items through Filter buildings (#321): from the input buffer to the output buffer, but only items
    /// some output side takes (<see cref="PortFilterRules.Route"/>). An item no side takes stays in the input,
    /// so its belt backs up instead of losing it. Output space is per item type, so one side whose belt is
    /// blocked can't starve the others. <see cref="ConveyorSystem"/> then hands each side only its own items.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ConveyorSystem))]
    public partial class FilterSystem : SystemBase
    {
        public const int SeenItemLimit = 12;

        protected override void OnCreate() => RequireForUpdate<ItemFilterTag>();

        protected override void OnUpdate()
        {
            foreach (var (config, entity) in SystemAPI.Query<RefRO<BuildingInventoryConfig>>()
                                                      .WithAll<ItemFilterTag>().WithEntityAccess())
            {
                var rules  = ReadRules(EntityManager.GetBuffer<OutputPortFilter>(entity, isReadOnly: true));
                var input  = EntityManager.GetBuffer<BuildingInputSlot>(entity);
                var output = EntityManager.GetBuffer<BuildingOutputSlot>(entity);
                var seen   = EntityManager.GetBuffer<FilterSeenItem>(entity);
                int cap    = config.ValueRO.OutputCapacity;

                for (int i = input.Length - 1; i >= 0; i--)
                {
                    var slot = input[i];
                    RememberSeen(seen, slot.ItemID);
                    if (PortFilterRules.Route(rules, slot.ItemID) == PortFilterRules.NoRoute) continue;

                    int room = cap - CountIn(output, slot.ItemID);
                    int move = System.Math.Min(room, slot.Quantity);
                    if (move <= 0) continue;

                    SlotBufferUtils.AddToOutputBuffer(output, slot.ItemID, move);
                    SlotBufferUtils.RemoveFromInputBuffer(input, slot.ItemID, move);
                }
            }
        }

        /// <summary>The Filter's rules as plain tuples.</summary>
        public static List<(int facing, int itemId)> ReadRules(DynamicBuffer<OutputPortFilter> buffer)
        {
            var rules = new List<(int, int)>(buffer.Length);
            for (int i = 0; i < buffer.Length; i++) rules.Add((buffer[i].Facing, buffer[i].ItemID));
            return rules;
        }

        /// <summary>Replaces the Filter's rules.</summary>
        public static void WriteRules(DynamicBuffer<OutputPortFilter> buffer, IReadOnlyList<(int facing, int itemId)> rules)
        {
            buffer.Clear();
            foreach (var (facing, itemId) in rules) buffer.Add(new OutputPortFilter { Facing = facing, ItemID = itemId });
        }

        private static int CountIn(DynamicBuffer<BuildingOutputSlot> output, int itemId)
        {
            int n = 0;
            for (int i = 0; i < output.Length; i++) if (output[i].ItemID == itemId) n += output[i].Quantity;
            return n;
        }

        private static void RememberSeen(DynamicBuffer<FilterSeenItem> seen, int itemId)
        {
            for (int i = 0; i < seen.Length; i++) if (seen[i].ItemID == itemId) return;
            if (seen.Length >= SeenItemLimit) seen.RemoveAt(0);
            seen.Add(new FilterSeenItem { ItemID = itemId });
        }
    }
}
