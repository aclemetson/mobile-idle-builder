using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Managed-side bridge between the UI and the ECS inventory.
    /// Provides CanCraft / TryCraft for manual (non-automated) recipe execution,
    /// and a snapshot of current inventory counts for the HUD.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public class ManualCraftService : SingletonMonoBehaviour<ManualCraftService>
    {
        private EntityManager _em;
        private EntityQuery   _inventoryQuery;
        private EntityQuery   _buildingQuery;

        public bool IsReady { get; private set; }

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                Debug.LogError("[ManualCraftService] No default DOTS world found.");
                return;
            }

            _em = world.EntityManager;
            _inventoryQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerInventoryTag>(),
                ComponentType.ReadWrite<InventorySlot>()
            );
            _buildingQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingData>(),
                ComponentType.ReadWrite<RecipeProcessData>(),
                ComponentType.ReadOnly<RecipeInputSlot>(),
                ComponentType.ReadOnly<RecipeOutputSlot>()
            );
            IsReady = true;
        }

        /// <summary>Returns true if the player's ECS inventory has enough inputs for this recipe.</summary>
        public bool CanCraft(RecipeJson recipe)
        {
            if (!IsReady || _inventoryQuery.IsEmpty) return false;

            var buffer = _em.GetBuffer<InventorySlot>(_inventoryQuery.GetSingletonEntity(), isReadOnly: true);
            foreach (var input in recipe.inputs)
            {
                int itemId = ItemDatabase.Instance.GetItemId(input.id);
                if (itemId < 0 || SlotBufferUtils.CountInInventory(buffer, itemId) < input.quantity)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Consumes inputs and deposits the recipe output into the ECS inventory.
        /// Returns false and makes no changes if inputs are insufficient.
        /// </summary>
        public bool TryCraft(RecipeJson recipe)
        {
            if (!CanCraft(recipe)) return false;

            var entity = _inventoryQuery.GetSingletonEntity();
            var buffer = _em.GetBuffer<InventorySlot>(entity);

            foreach (var input in recipe.inputs)
            {
                int itemId = ItemDatabase.Instance.GetItemId(input.id);
                SlotBufferUtils.RemoveFromInventory(ref buffer, itemId, input.quantity);
            }

            int outputId = ItemDatabase.Instance.GetItemId(recipe.output.id);
            if (outputId >= 0)
                SlotBufferUtils.AddToInventory(ref buffer, outputId, recipe.output.quantity);

            return true;
        }

        /// <summary>Snapshot of current inventory: itemId → quantity.</summary>
        public Dictionary<int, int> GetInventoryCounts()
        {
            var result = new Dictionary<int, int>();
            if (!IsReady || _inventoryQuery.IsEmpty) return result;

            var buffer = _em.GetBuffer<InventorySlot>(_inventoryQuery.GetSingletonEntity(), isReadOnly: true);
            for (int i = 0; i < buffer.Length; i++)
                result[buffer[i].ItemID] = buffer[i].Quantity;
            return result;
        }

        // ---- Building craft trigger ----

        /// <summary>
        /// Returns true if a placed building produces this recipe's output,
        /// is not already mid-cycle, and the inventory has the required inputs.
        /// </summary>
        public bool CanTriggerBuildingCraft(RecipeJson recipe)
        {
            if (!IsReady || _buildingQuery.IsEmptyIgnoreFilter) return false;

            int outputItemId = ItemDatabase.Instance?.GetItemId(recipe.output.id) ?? -1;
            if (outputItemId < 0) return false;

            var inventoryBuffer = _em.GetBuffer<InventorySlot>(_inventoryQuery.GetSingletonEntity(), isReadOnly: true);

            var entities = _buildingQuery.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (var entity in entities)
                {
                    var process = _em.GetComponentData<RecipeProcessData>(entity);
                    if (process.IsCrafting) continue;

                    var outputs = _em.GetBuffer<RecipeOutputSlot>(entity, isReadOnly: true);
                    bool outputMatches = false;
                    for (int i = 0; i < outputs.Length; i++)
                    {
                        if (outputs[i].ItemID == outputItemId) { outputMatches = true; break; }
                    }
                    if (!outputMatches) continue;

                    // Check inputs
                    var inputsBuf = _em.GetBuffer<RecipeInputSlot>(entity, isReadOnly: true);
                    bool hasInputs = true;
                    for (int i = 0; i < inputsBuf.Length; i++)
                    {
                        if (SlotBufferUtils.CountInInventory(inventoryBuffer, inputsBuf[i].ItemID) < inputsBuf[i].Quantity)
                        {
                            hasInputs = false;
                            break;
                        }
                    }
                    if (hasInputs) return true;
                }
            }
            finally
            {
                entities.Dispose();
            }
            return false;
        }

        /// <summary>
        /// Starts one production cycle on a placed building that outputs this recipe's item.
        /// Returns false if no eligible building is found or inputs are insufficient.
        /// </summary>
        public bool TriggerBuildingCraft(RecipeJson recipe)
        {
            if (!IsReady || _buildingQuery.IsEmptyIgnoreFilter) return false;

            int outputItemId = ItemDatabase.Instance?.GetItemId(recipe.output.id) ?? -1;
            if (outputItemId < 0) return false;

            var inventoryBuffer = _em.GetBuffer<InventorySlot>(_inventoryQuery.GetSingletonEntity(), isReadOnly: true);

            var entities = _buildingQuery.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (var entity in entities)
                {
                    var process = _em.GetComponentData<RecipeProcessData>(entity);
                    if (process.IsCrafting) continue;

                    var outputs = _em.GetBuffer<RecipeOutputSlot>(entity, isReadOnly: true);
                    bool outputMatches = false;
                    for (int i = 0; i < outputs.Length; i++)
                    {
                        if (outputs[i].ItemID == outputItemId) { outputMatches = true; break; }
                    }
                    if (!outputMatches) continue;

                    var inputsBuf = _em.GetBuffer<RecipeInputSlot>(entity, isReadOnly: true);
                    bool hasInputs = true;
                    for (int i = 0; i < inputsBuf.Length; i++)
                    {
                        if (SlotBufferUtils.CountInInventory(inventoryBuffer, inputsBuf[i].ItemID) < inputsBuf[i].Quantity)
                        {
                            hasInputs = false;
                            break;
                        }
                    }
                    if (!hasInputs) continue;

                    process.IsCrafting = true;
                    _em.SetComponentData(entity, process);
                    return true;
                }
            }
            finally
            {
                entities.Dispose();
            }
            return false;
        }

    }
}
