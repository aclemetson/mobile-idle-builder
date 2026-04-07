using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MobileIdleBuilder
{
    public class BuildingAuthoring : MonoBehaviour
    {
        public int buildingType;
        public int upgradeLevel = 1;
        public float productionSpeed = 1f;
        public bool isActive = true;
        public Vector2Int gridPosition;

        [Header("Recipe (optional — leave null for no recipe)")]
        public RecipeSO recipe;

        public class Baker : Baker<BuildingAuthoring>
        {
            public override void Bake(BuildingAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent(entity, new BuildingData
                {
                    BuildingType    = authoring.buildingType,
                    UpgradeLevel    = authoring.upgradeLevel,
                    ProductionSpeed = authoring.productionSpeed,
                    IsActive        = authoring.isActive
                });

                AddComponent(entity, new GridPosition
                {
                    Cell = new int2(authoring.gridPosition.x, authoring.gridPosition.y)
                });

                var r = authoring.recipe;
                int recipeID   = r != null ? r.recipeId : -1;
                float craftTime = r != null ? r.craftTime : 0f;

                AddComponent(entity, new RecipeProcessData
                {
                    RecipeID        = recipeID,
                    CraftTime       = craftTime,
                    Progress        = 0f,
                    InputsSatisfied = false
                });

                // Bake recipe input slots
                var inputBuffer = AddBuffer<RecipeInputSlot>(entity);
                if (r != null && r.inputs != null)
                {
                    for (int i = 0; i < r.inputs.Length; i++)
                    {
                        if (r.inputs[i] == null) continue;
                        inputBuffer.Add(new RecipeInputSlot
                        {
                            ItemID   = r.inputs[i].itemId,
                            Quantity = (i < r.inputQuantities.Length) ? r.inputQuantities[i] : 1
                        });
                    }
                }

                // Bake recipe output slot
                var outputBuffer = AddBuffer<RecipeOutputSlot>(entity);
                if (r != null && r.output != null)
                {
                    outputBuffer.Add(new RecipeOutputSlot
                    {
                        ItemID   = r.output.itemId,
                        Quantity = r.outputQuantity
                    });
                }
            }
        }
    }
}
