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

        [Header("Starting Entropy")]
        [Tooltip("Drives starting BaseCurrency when gameConfig is set. Fallback when gameConfig is null.")]
        public long startingEntropy = 0;

        [Tooltip("When set, startingEntropy is read from gameConfig.startingEntropy instead.")]
        public GameConfigSO gameConfig;

        public class Baker : Baker<PlayerInventoryAuthoring>
        {
            public override void Bake(PlayerInventoryAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new PlayerInventoryTag());

                long entropy = authoring.gameConfig != null
                    ? authoring.gameConfig.startingEntropy
                    : authoring.startingEntropy;

                AddComponent(entity, new PlayerProgressData
                {
                    BaseCurrency = entropy
                });

                AddComponent(entity, new PrestigeData
                {
                    SpeedMultiplier  = 1f,
                    OutputMultiplier = 1f,
                });

                var buffer = AddBuffer<InventorySlot>(entity);

                if (authoring.startingItems == null) return;
                foreach (var item in authoring.startingItems)
                    buffer.Add(new InventorySlot { ItemID = item.itemId, Quantity = item.quantity });
            }
        }
    }
}
