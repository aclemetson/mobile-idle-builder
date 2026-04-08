using Unity.Entities;

namespace MobileIdleBuilder
{
    public struct RecipeProcessData : IComponentData
    {
        public int RecipeID;
        public float CraftTime;
        public float Progress;
        public bool InputsSatisfied;
    }
}
