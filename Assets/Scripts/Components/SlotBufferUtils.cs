using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Static helpers for reading and mutating the game's ECS slot buffers.
    /// Used by ECS systems (ProductionSystem, ConveyorSystem, CollectorSystem, TutorialSystem)
    /// and managed services (ManualCraftService).
    /// All methods are Burst-compatible.
    /// </summary>
    public static class SlotBufferUtils
    {
        // ── BuildingInputSlot ────────────────────────────────────────────────

        public static int CountInInputBuffer(DynamicBuffer<BuildingInputSlot> buf, int itemID)
        {
            for (int i = 0; i < buf.Length; i++)
                if (buf[i].ItemID == itemID) return buf[i].Quantity;
            return 0;
        }

        /// <summary>Returns the total number of items across all slots (ignores itemID).</summary>
        public static int TotalInInputBuffer(DynamicBuffer<BuildingInputSlot> buf)
        {
            int n = 0;
            for (int i = 0; i < buf.Length; i++) n += buf[i].Quantity;
            return n;
        }

        public static void AddToInputBuffer(DynamicBuffer<BuildingInputSlot> buf, int itemID, int qty)
        {
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].ItemID != itemID) continue;
                buf[i] = new BuildingInputSlot { ItemID = itemID, Quantity = buf[i].Quantity + qty };
                return;
            }
            buf.Add(new BuildingInputSlot { ItemID = itemID, Quantity = qty });
        }

        public static void RemoveFromInputBuffer(DynamicBuffer<BuildingInputSlot> buf, int itemID, int qty)
        {
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].ItemID != itemID) continue;
                int remaining = buf[i].Quantity - qty;
                if (remaining <= 0)
                    buf.RemoveAt(i);
                else
                    buf[i] = new BuildingInputSlot { ItemID = itemID, Quantity = remaining };
                return;
            }
        }

        // ── BuildingOutputSlot ───────────────────────────────────────────────

        public static void AddToOutputBuffer(DynamicBuffer<BuildingOutputSlot> buf, int itemID, int qty)
        {
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].ItemID != itemID) continue;
                buf[i] = new BuildingOutputSlot { ItemID = itemID, Quantity = buf[i].Quantity + qty };
                return;
            }
            buf.Add(new BuildingOutputSlot { ItemID = itemID, Quantity = qty });
        }

        public static int CountInOutputBuffer(DynamicBuffer<BuildingOutputSlot> buf, int itemID)
        {
            for (int i = 0; i < buf.Length; i++)
                if (buf[i].ItemID == itemID) return buf[i].Quantity;
            return 0;
        }

        public static int TotalInOutputBuffer(DynamicBuffer<BuildingOutputSlot> buf)
        {
            int n = 0;
            for (int i = 0; i < buf.Length; i++) n += buf[i].Quantity;
            return n;
        }

        public static void RemoveFromOutputBuffer(DynamicBuffer<BuildingOutputSlot> buf, int itemID, int qty)
        {
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].ItemID != itemID) continue;
                int rem = buf[i].Quantity - qty;
                if (rem <= 0) buf.RemoveAt(i);
                else buf[i] = new BuildingOutputSlot { ItemID = itemID, Quantity = rem };
                return;
            }
        }

        // ── InventorySlot (global player inventory) ──────────────────────────

        public static int CountInInventory(DynamicBuffer<InventorySlot> buf, int itemID)
        {
            for (int i = 0; i < buf.Length; i++)
                if (buf[i].ItemID == itemID) return buf[i].Quantity;
            return 0;
        }

        public static void AddToInventory(ref DynamicBuffer<InventorySlot> buf, int itemID, int qty)
        {
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].ItemID != itemID) continue;
                buf[i] = new InventorySlot { ItemID = itemID, Quantity = buf[i].Quantity + qty };
                return;
            }
            buf.Add(new InventorySlot { ItemID = itemID, Quantity = qty });
        }

        public static void RemoveFromInventory(ref DynamicBuffer<InventorySlot> buf, int itemID, int qty)
        {
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].ItemID != itemID) continue;
                int remaining = buf[i].Quantity - qty;
                if (remaining <= 0)
                    buf.RemoveAt(i);
                else
                    buf[i] = new InventorySlot { ItemID = itemID, Quantity = remaining };
                return;
            }
        }
    }
}
