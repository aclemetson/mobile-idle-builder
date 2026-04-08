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
                int recipeID    = r != null ? r.recipeId : -1;
                float craftTime = r != null ? r.baseCraftTime : 0f;

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
                        if (r.inputs[i].item == null) continue;
                        inputBuffer.Add(new RecipeInputSlot
                        {
                            ItemID   = r.inputs[i].item.itemId,
                            Quantity = r.inputs[i].quantity
                        });
                    }
                }

                // Bake recipe output slot
                var outputBuffer = AddBuffer<RecipeOutputSlot>(entity);
                if (r != null && r.outputItem != null)
                {
                    outputBuffer.Add(new RecipeOutputSlot
                    {
                        ItemID   = r.outputItem.itemId,
                        Quantity = r.outputQuantity
                    });
                }
            }
        }
    }
}
