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
        /// Pass outputDirection for field-collector buildings; omit (null) for all others.
        /// </summary>
        public bool PlaceBuilding(int gridX, int gridY, BuildingSO building, RecipeSO recipe,
                                  int? outputDirection = null, int rotation = 0, bool flipped = false,
                                  int speedLevel = 1, int storageLevel = 1)
        {
            int fw = 1, fh = 1;
            var baseFootprint = new UnityEngine.Vector2Int(1, 1);
            if (building != null)
            {
                baseFootprint = new UnityEngine.Vector2Int(
                    Mathf.Max(1, building.footprint.x),
                    Mathf.Max(1, building.footprint.y));
                var rotatedFp = PortUtils.RotatedFootprint(baseFootprint, rotation);
                fw = rotatedFp.x;
                fh = rotatedFp.y;
            }

            if (GridOccupancy.Instance != null && !GridOccupancy.Instance.TryOccupyRect(gridX, gridY, fw, fh))
            {
                GameLogger.Develop($"[BuildingPlacer] Cells ({gridX},{gridY}) + {fw}x{fh} footprint are occupied.");
                return false;
            }

            bool hasPorts = building?.ports != null && building.ports.Length > 0;

            var archetype = _em.CreateArchetype(
                typeof(BuildingData),
                typeof(GridPosition),
                typeof(RecipeProcessData),
                typeof(RecipeInputSlot),
                typeof(RecipeOutputSlot),
                typeof(PlacedPortData),
                typeof(BuildingOutputSlot),
                typeof(BuildingInputSlot),
                typeof(BuildingInventoryConfig)
            );

            var entity = _em.CreateEntity(archetype);

            _em.SetComponentData(entity, new BuildingData
            {
                BuildingType        = building != null ? building.buildingId : 0,
                UpgradeLevel        = speedLevel,
                StorageUpgradeLevel = storageLevel,
                ProductionSpeed     = BuildingSO.ProductionSpeedForLevel(building, speedLevel),
                IsActive            = true
            });

            _em.AddComponentData(entity, new BuildingTransformData
            {
                Rotation = rotation,
                Flipped  = flipped
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

            _em.SetComponentData(entity, new BuildingInventoryConfig
            {
                OutputCapacity = BuildingSO.OutputCapacityForLevel(building, storageLevel),
                InputCapacity  = 20
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

            if (outputDirection.HasValue)
            {
                _em.AddComponentData(entity, new OutputDirectionData { Direction = outputDirection.Value });

                // Legacy field-collector: no port layout defined in BuildingSO, so add the output
                // port manually from the chosen output direction.
                var portBuf = _em.GetBuffer<PlacedPortData>(entity);
                portBuf.Add(new PlacedPortData
                {
                    PortType = (int)PortType.Output,
                    CellX    = gridX,
                    CellY    = gridY,
                    Facing   = outputDirection.Value
                });
            }

            if (building?.isEntropySink == true)
                _em.AddComponentData(entity, new EntropySinkTag());

            // Any MustBeOnField building (port-layout or legacy) is an autonomous collector.
            // CollectorData must be added AFTER the entity archetype is fixed by AddComponentData
            // calls above, and regardless of whether the building defines a port layout.
            if (building?.placementRule == PlacementRule.MustBeOnField)
            {
                float rate = building.baseOutputRate > 0f ? building.baseOutputRate : 1f;
                _em.AddComponentData(entity, new CollectorData { OutputRate = rate, Timer = 0f });
            }

            if (fw > 1 || fh > 1)
                _em.AddComponentData(entity, new BuildingFootprint { Width = fw, Height = fh });

            if (hasPorts)
            {
                var portBuf = _em.GetBuffer<PlacedPortData>(entity);
                foreach (var port in building.ports)
                {
                    var (relCell, dir) = PortUtils.TransformPort(port, baseFootprint, flipped, rotation);
                    portBuf.Add(new PlacedPortData
                    {
                        PortType = (int)port.portType,
                        CellX    = gridX + relCell.x,
                        CellY    = gridY + relCell.y,
                        Facing   = (int)dir
                    });
                }
            }

            GameLogger.Develop($"[BuildingPlacer] Placed '{building?.displayName ?? "Building"}' at ({gridX},{gridY}) footprint {fw}x{fh} rotation={rotation} flipped={flipped}");
            return true;
        }
    }
}
