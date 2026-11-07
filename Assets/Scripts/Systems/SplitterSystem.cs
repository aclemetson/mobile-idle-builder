using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Moves items through Belt Splitters: from the input buffer straight to the output buffer (any item, up to
    /// the output cap per item type). <see cref="ConveyorSystem"/> then hands them to the output belts in turn
    /// (<see cref="SplitterRotation"/>). Offline, <see cref="IdleGraphAnalyzer"/> splits the flow evenly.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(ConveyorSystem))]
    public partial class SplitterSystem : SystemBase
    {
        protected override void OnCreate() => RequireForUpdate<SplitterTag>();

        protected override void OnUpdate()
        {
            foreach (var (config, entity) in SystemAPI.Query<RefRO<BuildingInventoryConfig>>()
                                                      .WithAll<SplitterTag>().WithEntityAccess())
            {
                var input  = EntityManager.GetBuffer<BuildingInputSlot>(entity);
                var output = EntityManager.GetBuffer<BuildingOutputSlot>(entity);
                int cap    = config.ValueRO.OutputCapacity;

                for (int i = input.Length - 1; i >= 0; i--)
                {
                    var slot = input[i];
                    int held = 0;
                    for (int k = 0; k < output.Length; k++) if (output[k].ItemID == slot.ItemID) held += output[k].Quantity;

                    int move = System.Math.Min(cap - held, slot.Quantity);
                    if (move <= 0) continue;

                    SlotBufferUtils.AddToOutputBuffer(output, slot.ItemID, move);
                    SlotBufferUtils.RemoveFromInputBuffer(input, slot.ItemID, move);
                }
            }
        }
    }
}
