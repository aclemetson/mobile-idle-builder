using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MobileIdleBuilder
{
    public class BuildingAuthoring : MonoBehaviour
    {
        [Header("Building Definition")]
        public BuildingSO building;

        [Header("Placement")]
        public Vector2Int gridPosition;
        public int rotation = 0;
        public bool flipped = false;

        [Header("Recipe (optional — leave null for no recipe)")]
        public RecipeSO recipe;

        [Header("Overrides (used only when Building SO is null)")]
        public int buildingType;
        public int upgradeLevel = 1;
        public float productionSpeed = 1f;
        public bool isActive = true;

        public class Baker : Baker<BuildingAuthoring>
        {
            public override void Bake(BuildingAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                var so = authoring.building;

                int   bType  = so != null ? so.buildingId    : authoring.buildingType;
                int   uLevel = authoring.upgradeLevel;
                float pSpeed = authoring.productionSpeed;
                bool  active = authoring.isActive;

                AddComponent(entity, new BuildingData
                {
                    BuildingType    = bType,
                    UpgradeLevel    = uLevel,
                    ProductionSpeed = pSpeed,
                    IsActive        = active
                });

                AddComponent(entity, new GridPosition
                {
                    Cell = new int2(authoring.gridPosition.x, authoring.gridPosition.y)
                });

                var r = authoring.recipe;
                int recipeID    = r != null ? r.recipeId      : -1;
                float craftTime = r != null ? r.baseCraftTime : 0f;

                AddComponent(entity, new RecipeProcessData
                {
                    RecipeID        = recipeID,
                    CraftTime       = craftTime,
                    Progress        = 0f,
                    InputsSatisfied = false,
                    IsCrafting      = false
                });

                // Recipe input/output slots
                var recipeInputBuf = AddBuffer<RecipeInputSlot>(entity);
                if (r != null && r.inputs != null)
                {
                    for (int i = 0; i < r.inputs.Length; i++)
                    {
                        if (r.inputs[i].item == null) continue;
                        recipeInputBuf.Add(new RecipeInputSlot
                        {
                            ItemID   = r.inputs[i].item.itemId,
                            Quantity = r.inputs[i].quantity
                        });
                    }
                }

                var recipeOutputBuf = AddBuffer<RecipeOutputSlot>(entity);
                if (r != null && r.outputItem != null)
                {
                    recipeOutputBuf.Add(new RecipeOutputSlot
                    {
                        ItemID   = r.outputItem.itemId,
                        Quantity = r.outputQuantity
                    });
                }

                // Local inventory buffers (required for conveyor connectivity)
                AddBuffer<BuildingInputSlot>(entity);
                AddBuffer<BuildingOutputSlot>(entity);
                // Crafts are recorded here by ProductionSystem and drained by ProductionAchievementBridge.
                AddBuffer<CraftedOutputEvent>(entity);
                AddComponent(entity, new BuildingInventoryConfig
                {
                    OutputCapacity = BuildingSO.OutputCapacityForLevel(so, 1),
                    InputCapacity  = BuildingSO.InputCapacityForLevel(so, 1)
                });

                // Footprint
                int fw = so != null ? Mathf.Max(1, so.footprint.x) : 1;
                int fh = so != null ? Mathf.Max(1, so.footprint.y) : 1;
                if (fw > 1 || fh > 1)
                    AddComponent(entity, new BuildingFootprint { Width = fw, Height = fh });

                // Ports — bake from BuildingSO using the same transform logic as BuildingPlacer
                var portBuf = AddBuffer<PlacedPortData>(entity);
                if (so != null && so.ports != null && so.ports.Length > 0)
                {
                    var baseFp = new Vector2Int(fw, fh);
                    foreach (var port in so.ports)
                    {
                        var (relCell, dir) = PortUtils.TransformPort(port, baseFp, authoring.flipped, authoring.rotation);
                        portBuf.Add(new PlacedPortData
                        {
                            PortType = (int)port.portType,
                            CellX    = authoring.gridPosition.x + relCell.x,
                            CellY    = authoring.gridPosition.y + relCell.y,
                            Facing   = (int)dir
                        });
                    }
                }

                // Entropy sink
                if (so != null && so.isEntropySink)
                    AddComponent(entity, new EntropySinkTag());

                // Autonomous collector (field buildings)
                if (so != null && so.placementRule == PlacementRule.MustBeOnField)
                {
                    float rate = so.baseOutputRate > 0f ? so.baseOutputRate : 1f;
                    AddComponent(entity, new CollectorData { OutputRate = rate, Timer = 0f });
                }
            }
        }
    }
}
