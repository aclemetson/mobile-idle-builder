using Unity.Entities;

namespace MobileIdleBuilder
{
    public struct RecipeProcessData : IComponentData
    {
        public int RecipeID;
        public float CraftTime;
        public float Progress;
        public bool InputsSatisfied;
        /// <summary>Set true by the player to start one production cycle; cleared on completion.</summary>
        public bool IsCrafting;
    }
}
