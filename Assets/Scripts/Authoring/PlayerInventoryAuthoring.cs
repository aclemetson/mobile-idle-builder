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
        [Tooltip("How much entropy (BaseCurrency) the player starts with. Overridden by config.startingEntropy if config is set.")]
        public long startingEntropy = 0;

        [Header("Config")]
        public GameConfigSO config;

        public class Baker : Baker<PlayerInventoryAuthoring>
        {
            public override void Bake(PlayerInventoryAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new PlayerInventoryTag());

                AddComponent(entity, new PlayerProgressData
                {
                    BaseCurrency = authoring.config != null
                        ? authoring.config.startingEntropy
                        : authoring.startingEntropy,
                    PrestigeWallValue = authoring.config != null
                        ? authoring.config.prestigeBaseValue * authoring.config.prestigeWallMultiplier
                        : 0f
                });

                AddComponent(entity, new PrestigeData
                {
                    SpeedMultiplier  = 1f,
                    OutputMultiplier = 1f,
                });

                // Global production multipliers (megastructure stage rewards). Default to no-op;
                // MegastructureService overwrites these on load / stage completion.
                AddComponent(entity, new GlobalProductionBonus
                {
                    OutputMult = 1f,
                    SpeedMult  = 1f,
                });

                // Research effect multipliers (Transportation I etc.); ResearchService overwrites on load / unlock.
                AddComponent(entity, new ResearchEffects { ConveyorSpeedMult = 1f });

                // Automation income counter (Statistics panel); not saved.
                AddComponent(entity, new AutomationIncome { Total = 0 });

                var buffer = AddBuffer<InventorySlot>(entity);

                if (authoring.startingItems == null) return;
                foreach (var item in authoring.startingItems)
                    buffer.Add(new InventorySlot { ItemID = item.itemId, Quantity = item.quantity });
            }
        }
    }
}
