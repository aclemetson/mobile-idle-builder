using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// What happens to a building's contents when its recipe is switched. Inputs the new recipe doesn't use are
    /// sold (<see cref="SurplusSale"/>, #312); outputs it doesn't make go back to the player's inventory (#341).
    /// </summary>
    public static class RecipeSwitch
    {
        /// <summary>True when <paramref name="recipeOutputs"/> includes <paramref name="itemId"/>.</summary>
        public static bool RecipeMakes(DynamicBuffer<RecipeOutputSlot> recipeOutputs, int itemId)
        {
            for (int j = 0; j < recipeOutputs.Length; j++)
                if (recipeOutputs[j].ItemID == itemId) return true;
            return false;
        }

        /// <summary>
        /// Moves output-buffer items that <paramref name="recipeOutputs"/> doesn't produce into
        /// <paramref name="inventory"/> (stacking onto an existing slot). Outputs the new recipe also makes stay
        /// and keep shipping. Returns how many items moved.
        /// </summary>
        public static int MoveUnproducedOutputsToInventory(DynamicBuffer<BuildingOutputSlot> outputBuffer,
                                                           DynamicBuffer<RecipeOutputSlot> recipeOutputs,
                                                           ref DynamicBuffer<InventorySlot> inventory)
        {
            int moved = 0;
            for (int i = outputBuffer.Length - 1; i >= 0; i--)
            {
                var slot = outputBuffer[i];
                if (RecipeMakes(recipeOutputs, slot.ItemID)) continue;
                if (slot.Quantity > 0)
                {
                    SlotBufferUtils.AddToInventory(ref inventory, slot.ItemID, slot.Quantity);
                    moved += slot.Quantity;
                }
                outputBuffer.RemoveAt(i);
            }
            return moved;
        }
    }
}
