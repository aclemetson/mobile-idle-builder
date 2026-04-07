using UnityEngine;

namespace MobileIdleBuilder
{
    [CreateAssetMenu(fileName = "New Recipe", menuName = "MobileIdleBuilder/Recipe")]
    public class RecipeSO : ScriptableObject
    {
        public int recipeId;
        public ItemSO[] inputs;
        public int[] inputQuantities;
        public ItemSO output;
        public int outputQuantity = 1;
        public float craftTime = 1f;
        public bool requiresBuilding;
    }
}
