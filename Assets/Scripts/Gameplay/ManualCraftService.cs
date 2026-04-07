using System.Collections.Generic;
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
    public class ManualCraftService : MonoBehaviour
    {
        public static ManualCraftService Instance { get; private set; }

        private EntityManager _em;
        private EntityQuery   _inventoryQuery;

        public bool IsReady { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

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
                if (itemId < 0 || Count(buffer, itemId) < input.quantity)
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
                RemoveFromBuffer(ref buffer, itemId, input.quantity);
            }

            int outputId = ItemDatabase.Instance.GetItemId(recipe.output.id);
            if (outputId >= 0)
                AddToBuffer(ref buffer, outputId, recipe.output.quantity);

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

        // ---- Buffer helpers ----

        private static int Count(DynamicBuffer<InventorySlot> buf, int itemId)
        {
            for (int i = 0; i < buf.Length; i++)
                if (buf[i].ItemID == itemId) return buf[i].Quantity;
            return 0;
        }

        private static void AddToBuffer(ref DynamicBuffer<InventorySlot> buf, int itemId, int qty)
        {
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].ItemID != itemId) continue;
                buf[i] = new InventorySlot { ItemID = itemId, Quantity = buf[i].Quantity + qty };
                return;
            }
            buf.Add(new InventorySlot { ItemID = itemId, Quantity = qty });
        }

        private static void RemoveFromBuffer(ref DynamicBuffer<InventorySlot> buf, int itemId, int qty)
        {
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].ItemID != itemId) continue;
                int remaining = buf[i].Quantity - qty;
                if (remaining <= 0)
                    buf.RemoveAt(i);
                else
                    buf[i] = new InventorySlot { ItemID = itemId, Quantity = remaining };
                return;
            }
        }
    }
}
