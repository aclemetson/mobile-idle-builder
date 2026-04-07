using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Creates DOTS building entities at runtime.
    /// Called by BuildingPlacementController after the player confirms a grid cell.
    /// Checks GridOccupancy before placing — returns false if the cell is taken.
    /// </summary>
    public class BuildingPlacer : MonoBehaviour
    {
        private EntityManager _em;

        void Start()
        {
            _em = World.DefaultGameObjectInjectionWorld.EntityManager;
        }

        /// <summary>
        /// Places a building entity at (gridX, gridY).
        /// Returns true on success, false if the cell is already occupied.
        /// </summary>
        public bool PlaceBuilding(int gridX, int gridY, BuildingSO building, RecipeSO recipe)
        {
            if (GridOccupancy.Instance != null && !GridOccupancy.Instance.TryOccupy(gridX, gridY))
            {
                Debug.Log($"[BuildingPlacer] Cell ({gridX},{gridY}) is occupied.");
                return false;
            }

            var archetype = _em.CreateArchetype(
                typeof(BuildingData),
                typeof(GridPosition),
                typeof(RecipeProcessData),
                typeof(RecipeInputSlot),
                typeof(RecipeOutputSlot)
            );

            var entity = _em.CreateEntity(archetype);

            _em.SetComponentData(entity, new BuildingData
            {
                BuildingType    = building != null ? building.buildingId : 0,
                UpgradeLevel    = 1,
                ProductionSpeed = 1f,
                IsActive        = true
            });

            _em.SetComponentData(entity, new GridPosition
            {
                Cell = new int2(gridX, gridY)
            });

            int   recipeId  = recipe != null ? recipe.recipeId      : -1;
            float craftTime = recipe != null ? recipe.baseCraftTime : 1f;

            _em.SetComponentData(entity, new RecipeProcessData
            {
                RecipeID        = recipeId,
                CraftTime       = craftTime,
                Progress        = 0f,
                InputsSatisfied = false
            });

            if (recipe != null)
            {
                var inputBuf = _em.GetBuffer<RecipeInputSlot>(entity);
                foreach (var input in recipe.inputs)
                {
                    if (input.item == null) continue;
                    inputBuf.Add(new RecipeInputSlot
                    {
                        ItemID   = input.item.itemId,
                        Quantity = input.quantity
                    });
                }

                var outputBuf = _em.GetBuffer<RecipeOutputSlot>(entity);
                if (recipe.outputItem != null)
                    outputBuf.Add(new RecipeOutputSlot
                    {
                        ItemID   = recipe.outputItem.itemId,
                        Quantity = recipe.outputQuantity
                    });
            }

            Debug.Log($"[BuildingPlacer] Placed '{building?.displayName ?? "Building"}' at ({gridX},{gridY})");
            return true;
        }
    }
}
