using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>What a building input does with the item waiting at a belt's tail.</summary>
    public enum InputDecision
    {
        /// <summary>Leave it on the belt (back-pressure).</summary>
        Wait,
        /// <summary>Take it into the building's input buffer.</summary>
        Accept,
        /// <summary>Take it off the belt into the player's global inventory (it was jamming the belt).</summary>
        Overflow,
    }

    /// <summary>
    /// Rules for a belt tail handing an item to a building input (#200).
    ///
    /// <list type="bullet">
    /// <item><b>Per-item cap.</b> <see cref="BuildingInventoryConfig.InputCapacity"/> is the cap for EACH
    /// item type (the data field is <c>base_max_input_items_per_slot</c>), not a total. A total cap let
    /// one fast input fill the whole buffer and lock the other out, stalling e.g. a proton Combiner
    /// fed by separate up/down quark belts.</item>
    /// <item><b>Recipe filter.</b> Once a recipe is set, items it does not use are not taken in.</item>
    /// <item><b>Jam overflow.</b> A belt carrying several item types can jam when its front item cannot
    /// be taken (full or unused) while an item the recipe is short of sits behind it. Only then is the
    /// front item moved to the player's inventory so the belt keeps flowing; nothing is lost. A belt
    /// carrying one item type never overflows, it just backs up.</item>
    /// </list>
    ///
    /// Buildings without a recipe (RecipeID &lt; 0, e.g. before the player picks one) only apply the
    /// per-item cap and never overflow.
    /// </summary>
    public static class InputAcceptance
    {
        /// <summary>Pure decision (unit-tested).</summary>
        /// <param name="countOfItem">How many of this item the input buffer already holds.</param>
        /// <param name="perItemCap">Cap per item type.</param>
        /// <param name="recipeSet">The building has an active recipe.</param>
        /// <param name="recipeUsesItem">The active recipe consumes this item.</param>
        /// <param name="neededItemBehind">An item the recipe is short of is queued behind this one on the belt.</param>
        public static InputDecision Decide(int countOfItem, int perItemCap, bool recipeSet,
                                           bool recipeUsesItem, bool neededItemBehind)
        {
            bool usable = !recipeSet || recipeUsesItem;
            if (usable && countOfItem < perItemCap) return InputDecision.Accept;
            if (recipeSet && neededItemBehind)      return InputDecision.Overflow;
            return InputDecision.Wait;
        }

        /// <summary>True when the recipe consumes <paramref name="itemID"/>.</summary>
        public static bool RecipeUses(DynamicBuffer<RecipeInputSlot> recipeInputs, int itemID)
        {
            for (int i = 0; i < recipeInputs.Length; i++)
                if (recipeInputs[i].ItemID == itemID && recipeInputs[i].Quantity > 0) return true;
            return false;
        }

        /// <summary>
        /// True when the input buffer is short of <paramref name="itemID"/> for one craft, i.e. the
        /// recipe needs it and the buffer holds fewer than the recipe quantity.
        /// </summary>
        public static bool IsShortOf(DynamicBuffer<RecipeInputSlot> recipeInputs,
                                     DynamicBuffer<BuildingInputSlot> inputBuffer, int itemID)
        {
            for (int i = 0; i < recipeInputs.Length; i++)
            {
                if (recipeInputs[i].ItemID != itemID) continue;
                return SlotBufferUtils.CountInInputBuffer(inputBuffer, itemID) < recipeInputs[i].Quantity;
            }
            return false;
        }
    }
}
