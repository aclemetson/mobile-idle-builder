using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Rules for a building's creation buffer (<see cref="CraftStagingSlot"/>). Burst-compatible.
    ///
    /// <list type="bullet">
    /// <item><b>Fill.</b> Each recipe input moves from the input buffer into the creation buffer until the
    /// creation buffer holds the recipe's quantity of it, never more.</item>
    /// <item><b>Ready.</b> A craft can start once the creation buffer holds every input's full quantity.</item>
    /// <item><b>Consume.</b> A finished craft takes the recipe quantities out of the creation buffer; the
    /// input buffer, which kept filling up to its own per-item cap, then refills it for the next craft.</item>
    /// <item><b>Recipe switch.</b> Everything staged goes back to the input buffer, where the usual rule
    /// sells whatever the new recipe does not use.</item>
    /// </list>
    /// </summary>
    public static class CraftStaging
    {
        public static int Count(DynamicBuffer<CraftStagingSlot> stage, int itemID)
        {
            for (int i = 0; i < stage.Length; i++)
                if (stage[i].ItemID == itemID) return stage[i].Quantity;
            return 0;
        }

        /// <summary>Moves recipe inputs from <paramref name="input"/> into <paramref name="stage"/>, up to each recipe quantity.</summary>
        public static void Fill(DynamicBuffer<RecipeInputSlot> recipe, DynamicBuffer<BuildingInputSlot> input,
                                DynamicBuffer<CraftStagingSlot> stage)
        {
            for (int r = 0; r < recipe.Length; r++)
            {
                int id   = recipe[r].ItemID;
                int need = recipe[r].Quantity - Count(stage, id);
                if (need <= 0) continue;
                int take = SlotBufferUtils.CountInInputBuffer(input, id);
                if (take > need) take = need;
                if (take <= 0) continue;
                SlotBufferUtils.RemoveFromInputBuffer(input, id, take);
                Add(stage, id, take);
            }
        }

        /// <summary>True when the creation buffer holds every recipe input's full quantity.</summary>
        public static bool IsReady(DynamicBuffer<RecipeInputSlot> recipe, DynamicBuffer<CraftStagingSlot> stage)
        {
            for (int r = 0; r < recipe.Length; r++)
                if (Count(stage, recipe[r].ItemID) < recipe[r].Quantity) return false;
            return true;
        }

        /// <summary>Takes one craft's inputs out of the creation buffer.</summary>
        public static void Consume(DynamicBuffer<RecipeInputSlot> recipe, DynamicBuffer<CraftStagingSlot> stage)
        {
            for (int r = 0; r < recipe.Length; r++)
                Remove(stage, recipe[r].ItemID, recipe[r].Quantity);
        }

        /// <summary>Empties the creation buffer back into the input buffer (recipe switch).</summary>
        public static void ReturnToInput(DynamicBuffer<CraftStagingSlot> stage, DynamicBuffer<BuildingInputSlot> input)
        {
            for (int i = 0; i < stage.Length; i++)
                if (stage[i].Quantity > 0)
                    SlotBufferUtils.AddToInputBuffer(input, stage[i].ItemID, stage[i].Quantity);
            stage.Clear();
        }

        static void Add(DynamicBuffer<CraftStagingSlot> stage, int itemID, int qty)
        {
            for (int i = 0; i < stage.Length; i++)
            {
                if (stage[i].ItemID != itemID) continue;
                stage[i] = new CraftStagingSlot { ItemID = itemID, Quantity = stage[i].Quantity + qty };
                return;
            }
            stage.Add(new CraftStagingSlot { ItemID = itemID, Quantity = qty });
        }

        static void Remove(DynamicBuffer<CraftStagingSlot> stage, int itemID, int qty)
        {
            for (int i = 0; i < stage.Length; i++)
            {
                if (stage[i].ItemID != itemID) continue;
                int left = stage[i].Quantity - qty;
                if (left <= 0) stage.RemoveAt(i);
                else           stage[i] = new CraftStagingSlot { ItemID = itemID, Quantity = left };
                return;
            }
        }
    }
}
