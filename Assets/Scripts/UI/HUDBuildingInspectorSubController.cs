using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Manages the building inspector panel: opening, closing, periodic refresh, and recipe switching.
    /// Sibling MonoBehaviour to HUDController on the HUD GameObject.
    /// Call Init(root, placement) then SetECSContext(em) before use.
    /// </summary>
    public class HUDBuildingInspectorSubController : MonoBehaviour
    {
        private VisualElement _buildingInspectorPanel;
        private ScrollView    _inspectorContent;
        private Label         _inspectorBuildingName;

        private Entity _inspectorEntity      = Entity.Null;
        private float  _inspectorRefreshTimer;

        private EntityManager              _em;
        private bool                       _ecsReady;
        private BuildingPlacementController _placement;

        public void Init(VisualElement root, BuildingPlacementController placement)
        {
            _buildingInspectorPanel = root.Q("building-inspector-panel");
            _inspectorContent       = root.Q<ScrollView>("inspector-content");
            _inspectorBuildingName  = root.Q<Label>("inspector-building-name");
            _placement              = placement;
        }

        public void SetECSContext(EntityManager em)
        {
            _em       = em;
            _ecsReady = true;
        }

        public void Tick()
        {
            if (_inspectorEntity == Entity.Null) return;
            _inspectorRefreshTimer += Time.deltaTime;
            if (_inspectorRefreshTimer < 0.5f) return;
            _inspectorRefreshTimer = 0f;
            RefreshInspectorContent();
        }

        public void ShowBuildingInspector(Entity entity, string buildingName)
        {
            if (!_ecsReady) return;
            _inspectorEntity = entity;
            if (_inspectorBuildingName != null) _inspectorBuildingName.text = buildingName;
            RefreshInspectorContent();
            HUDController.SetElementVisible(_buildingInspectorPanel, true);
        }

        public void HideBuildingInspector()
        {
            HUDController.SetElementVisible(_buildingInspectorPanel, false);
            _inspectorEntity = Entity.Null;
        }

        private void RefreshInspectorContent()
        {
            if (_inspectorContent == null || !_ecsReady) return;
            if (_inspectorEntity == Entity.Null) return;
            if (!_em.Exists(_inspectorEntity)) { HideBuildingInspector(); return; }

            _inspectorContent.Clear();

            if (_em.HasComponent<BuildingData>(_inspectorEntity))
            {
                var d = _em.GetComponentData<BuildingData>(_inspectorEntity);
                AddInspectorRow($"Active: {(d.IsActive ? "Yes" : "No")}");
            }

            if (_em.HasComponent<GridPosition>(_inspectorEntity))
            {
                var p = _em.GetComponentData<GridPosition>(_inspectorEntity);
                AddInspectorRow($"Cell: ({p.Cell.x}, {p.Cell.y})");
            }

            if (_em.HasComponent<CollectorData>(_inspectorEntity))
            {
                var col    = _em.GetComponentData<CollectorData>(_inspectorEntity);
                float rate = col.OutputRate > 0f ? col.OutputRate : 1f;
                float next = Mathf.Max(0f, (1f / rate) - col.Timer);
                AddInspectorRow("—— Collector ——");
                AddInspectorRow($"Output rate: {col.OutputRate:F1} /s");
                AddInspectorRow($"Next item in: {next:F2}s");
            }

            if (_em.HasBuffer<BuildingOutputSlot>(_inspectorEntity))
            {
                var buf = _em.GetBuffer<BuildingOutputSlot>(_inspectorEntity, isReadOnly: true);
                AddInspectorRow("—— Output Buffer ——");
                if (buf.Length == 0)
                {
                    AddInspectorRow("  (empty)");
                }
                else
                {
                    for (int i = 0; i < buf.Length; i++)
                        AddInspectorRow($"  {ItemName(buf[i].ItemID)}  ×  {buf[i].Quantity}");
                }
            }

            if (_em.HasBuffer<BuildingInputSlot>(_inspectorEntity))
            {
                var buf = _em.GetBuffer<BuildingInputSlot>(_inspectorEntity, isReadOnly: true);
                if (buf.Length > 0)
                {
                    AddInspectorRow("—— Input Buffer ——");
                    for (int i = 0; i < buf.Length; i++)
                        AddInspectorRow($"  {ItemName(buf[i].ItemID)}  ×  {buf[i].Quantity}");
                }
            }

            if (_em.HasBuffer<RecipeOutputSlot>(_inspectorEntity))
            {
                var buf = _em.GetBuffer<RecipeOutputSlot>(_inspectorEntity, isReadOnly: true);
                if (buf.Length > 0)
                {
                    AddInspectorRow("—— Produces ——");
                    for (int i = 0; i < buf.Length; i++)
                        AddInspectorRow($"  {ItemName(buf[i].ItemID)}  ×  {buf[i].Quantity}");
                }
            }

            var buildingSO = GetBuildingSO(_inspectorEntity);
            if (buildingSO?.supportedRecipes != null && buildingSO.supportedRecipes.Length > 1)
            {
                AddInspectorRow("—— Set Recipe ——");
                foreach (var r in buildingSO.supportedRecipes)
                {
                    if (r == null) continue;
                    var captured = r;
                    var btn = new Button { text = r.displayName ?? r.name };
                    btn.AddToClassList("craft-btn");
                    btn.clicked += () =>
                    {
                        SetBuildingRecipe(_inspectorEntity, captured);
                        RefreshInspectorContent();
                    };
                    _inspectorContent?.Add(btn);
                }
            }
        }

        private BuildingSO GetBuildingSO(Entity entity)
        {
            if (!_ecsReady || !_em.HasComponent<BuildingData>(entity)) return null;
            int buildingType = _em.GetComponentData<BuildingData>(entity).BuildingType;
            if (_placement?.availableBuildings == null) return null;
            foreach (var entry in _placement.availableBuildings)
            {
                if (entry.building != null && entry.building.buildingId == buildingType)
                    return entry.building;
            }
            return null;
        }

        private void SetBuildingRecipe(Entity entity, RecipeSO recipe)
        {
            if (!_ecsReady || !_em.Exists(entity) || recipe == null) return;

            _em.SetComponentData(entity, new RecipeProcessData
            {
                RecipeID        = recipe.recipeId,
                CraftTime       = recipe.baseCraftTime,
                Progress        = 0f,
                InputsSatisfied = false,
                IsCrafting      = false
            });

            var inputBuf = _em.GetBuffer<RecipeInputSlot>(entity);
            inputBuf.Clear();
            if (recipe.inputs != null)
            {
                foreach (var input in recipe.inputs)
                {
                    if (input.item == null) continue;
                    inputBuf.Add(new RecipeInputSlot { ItemID = input.item.itemId, Quantity = input.quantity });
                }
            }

            var outputBuf = _em.GetBuffer<RecipeOutputSlot>(entity);
            outputBuf.Clear();
            if (recipe.outputItem != null)
                outputBuf.Add(new RecipeOutputSlot
                {
                    ItemID   = recipe.outputItem.itemId,
                    Quantity = recipe.outputQuantity
                });
        }

        private void AddInspectorRow(string text)
        {
            var lbl = new Label(text);
            lbl.AddToClassList("recipe-inputs");
            _inspectorContent?.Add(lbl);
        }

        private static string ItemName(int itemID)
        {
            var item = ItemDatabase.Instance?.Get(itemID);
            return item?.displayName ?? item?.symbol ?? $"Item {itemID}";
        }
    }
}
