using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    [System.Serializable]
    public struct StartingItem
    {
        public int itemId;
        public int quantity;
    }

    public class PlayerInventoryAuthoring : MonoBehaviour
    {
        [Tooltip("Items placed in the inventory at the start of a run.")]
        public StartingItem[] startingItems;

        public class Baker : Baker<PlayerInventoryAuthoring>
        {
            public override void Bake(PlayerInventoryAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new PlayerInventoryTag());
                var buffer = AddBuffer<InventorySlot>(entity);

                if (authoring.startingItems == null) return;
                foreach (var item in authoring.startingItems)
                    buffer.Add(new InventorySlot { ItemID = item.itemId, Quantity = item.quantity });
            }
        }
    }
}
