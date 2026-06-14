using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Manages the building inspector panel: opening, closing, periodic refresh, recipe switching,
    /// and speed/storage upgrade purchasing.
    /// Sibling MonoBehaviour to HUDController on the HUD GameObject.
    /// Call Init(root, placement, hud) then SetECSContext(em) before use.
    /// </summary>
    public class HUDBuildingInspectorSubController : MonoBehaviour
    {
        private VisualElement _buildingInspectorPanel;
        private ScrollView    _inspectorContent;
        private Label         _inspectorBuildingName;

        private Entity _inspectorEntity      = Entity.Null;
        private float  _inspectorRefreshTimer;

        private EntityManager              _em;
        private EntityQuery                _playerQuery;
        private bool                       _ecsReady;
        private BuildingPlacementController _placement;
        private HUDController              _hud;

        public void Init(VisualElement root, BuildingPlacementController placement, HUDController hud)
        {
            _buildingInspectorPanel = root.Q("building-inspector-panel");
            _inspectorContent       = root.Q<ScrollView>("inspector-content");
            _inspectorBuildingName  = root.Q<Label>("inspector-building-name");
            _placement              = placement;
            _hud                   = hud;
        }

        public void SetECSContext(EntityManager em)
        {
            _em          = em;
            _playerQuery = em.CreateEntityQuery(ComponentType.ReadWrite<PlayerProgressData>());
            _ecsReady    = true;
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
            GameLogger.Develop($"[InspectorUI] ShowBuildingInspector: '{buildingName}' ecsReady={_ecsReady} panel={((_buildingInspectorPanel == null) ? "NULL" : "ok")}");
            if (!_ecsReady) return;
            _inspectorEntity = entity;
            if (_inspectorBuildingName != null) _inspectorBuildingName.text = buildingName;
            RefreshInspectorContent();
            HUDController.SetElementVisible(_buildingInspectorPanel, true);
            GameLogger.Develop($"[InspectorUI] Panel shown — entity={entity}");
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

            BuildingData buildingData = default;
            if (_em.HasComponent<BuildingData>(_inspectorEntity))
            {
                buildingData = _em.GetComponentData<BuildingData>(_inspectorEntity);
                AddInspectorRow($"Active: {(buildingData.IsActive ? "Yes" : "No")}");
            }

            int buildingCellX = -1, buildingCellY = -1;
            if (_em.HasComponent<GridPosition>(_inspectorEntity))
            {
                var p = _em.GetComponentData<GridPosition>(_inspectorEntity);
                buildingCellX = p.Cell.x;
                buildingCellY = p.Cell.y;
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

            // Field-collector recipe display: show what the underlying field produces.
            // If the field has exactly one drop item (or only one matching recipe), skip the
            // chooser. If multiple relevant recipes exist, show the selection buttons.
            FieldSO fieldOnTile = buildingCellX >= 0
                ? FieldGenerator.GetFieldAt(buildingCellX, buildingCellY)
                : null;
            bool isFieldCollector = fieldOnTile != null &&
                                    _em.HasComponent<CollectorData>(_inspectorEntity);

            if (buildingSO != null)
            {
                if (isFieldCollector)
                {
                    AddFieldCollectorRecipeSection(_inspectorEntity, buildingSO, fieldOnTile);
                }
                else if (buildingSO.supportedRecipes != null && buildingSO.supportedRecipes.Length > 1)
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

                AddSpeedUpgradeSection(_inspectorEntity, buildingSO, buildingData);
                AddStorageUpgradeSection(_inspectorEntity, buildingSO, buildingData);
            }

            if (buildingCellX >= 0 && buildingCellY >= 0)
                AddManagerSection(buildingCellX, buildingCellY);
        }

        // ── Manager assignment UI ─────────────────────────────────────────────

        private void AddManagerSection(int cellX, int cellY)
        {
            var svc = ManagerService.Instance;
            if (svc == null || svc.ManagerCount == 0) return;

            int siteIndex = SaveManager.Instance?.Current?.currentRun?.activeSiteIndex ?? 0;
            int posKey    = ManagerService.EncodePos(cellX, cellY);

            AddInspectorRow("—— Manager ——");

            var current = svc.GetManagerAtBuilding(siteIndex, posKey);
            if (current != null)
            {
                var row = new VisualElement();
                row.AddToClassList("upgrade-row");

                var name = new Label(current.displayName);
                name.AddToClassList("upgrade-row-name");
                row.Add(name);

                var unassign = new Button { text = "Unassign" };
                unassign.AddToClassList("craft-btn");
                var capturedId = current.id;
                unassign.clicked += () =>
                {
                    svc.Unassign(capturedId);
                    RefreshInspectorContent();
                };
                row.Add(unassign);
                _inspectorContent?.Add(row);
            }

            // Offer each hired manager not already on THIS building.
            bool anyOffer = false;
            foreach (var mgr in svc.AllManagers)
            {
                if (mgr == null || !svc.IsHired(mgr.id)) continue;
                if (current != null && mgr.id == current.id) continue;
                anyOffer = true;

                var btn = new Button { text = $"Assign {mgr.displayName}" };
                btn.AddToClassList("craft-btn");
                var capturedId = mgr.id;
                btn.clicked += () =>
                {
                    svc.Assign(capturedId, siteIndex, posKey);
                    RefreshInspectorContent();
                };
                _inspectorContent?.Add(btn);
            }

            if (current == null && !anyOffer)
                AddInspectorRow("  (hire a manager in the Managers panel)");
        }

        // ── Upgrade UI ────────────────────────────────────────────────────────

        private void AddSpeedUpgradeSection(Entity entity, BuildingSO so, BuildingData bd)
        {
            if (so.upgradeLevels == null || so.upgradeLevels.Length == 0) return;

            int  currentLevel = bd.UpgradeLevel < 1 ? 1 : bd.UpgradeLevel;
            int  maxLevel     = so.MaxSpeedLevel();
            bool isMaxed      = currentLevel >= maxLevel;
            var  next         = isMaxed ? (BuildingUpgradeLevel?)null : so.NextSpeedUpgrade(currentLevel);

            var row = new VisualElement();
            row.AddToClassList("upgrade-row");
            if (isMaxed) row.AddToClassList("upgrade-row--maxed");

            var header = new VisualElement();
            header.AddToClassList("upgrade-row-header");
            var nameLabel  = new Label("Speed Upgrade");
            nameLabel.AddToClassList("upgrade-row-name");
            var levelLabel = new Label(isMaxed ? $"Lv {currentLevel} / {maxLevel}  MAX" : $"Lv {currentLevel} / {maxLevel}");
            levelLabel.AddToClassList("upgrade-row-level");
            header.Add(nameLabel);
            header.Add(levelLabel);
            row.Add(header);

            if (!isMaxed && next.HasValue)
            {
                var desc = new Label($"{next.Value.outputRate:F1}× production speed");
                desc.AddToClassList("upgrade-row-desc");
                row.Add(desc);

                long balance = GetBaseCurrency();
                bool canAfford = balance >= next.Value.costBaseCurrency;

                var footer = new VisualElement();
                footer.AddToClassList("upgrade-row-footer");

                var costLabel = new Label($"{next.Value.costBaseCurrency:N0} e");
                costLabel.AddToClassList("upgrade-row-cost");
                if (!canAfford) costLabel.AddToClassList("upgrade-row-cost--unaffordable");
                footer.Add(costLabel);

                var btn = new Button { text = "Upgrade" };
                btn.AddToClassList("craft-btn");
                btn.SetEnabled(canAfford);
                int capturedNextLevel = currentLevel + 1;
                var capturedSO        = so;
                btn.clicked += () => OnSpeedUpgradeBought(entity, capturedSO, capturedNextLevel);
                footer.Add(btn);

                row.Add(footer);
            }

            _inspectorContent?.Add(row);
        }

        private void AddStorageUpgradeSection(Entity entity, BuildingSO so, BuildingData bd)
        {
            if (so.storageUpgradeLevels == null || so.storageUpgradeLevels.Length == 0) return;

            int  currentLevel = bd.StorageUpgradeLevel < 1 ? 1 : bd.StorageUpgradeLevel;
            int  maxLevel     = so.MaxStorageLevel();
            bool isMaxed      = currentLevel >= maxLevel;
            var  next         = isMaxed ? (BuildingStorageUpgradeLevel?)null : so.NextStorageUpgrade(currentLevel);

            var row = new VisualElement();
            row.AddToClassList("upgrade-row");
            if (isMaxed) row.AddToClassList("upgrade-row--maxed");

            var header = new VisualElement();
            header.AddToClassList("upgrade-row-header");
            var nameLabel  = new Label("Storage Upgrade");
            nameLabel.AddToClassList("upgrade-row-name");
            var levelLabel = new Label(isMaxed ? $"Lv {currentLevel} / {maxLevel}  MAX" : $"Lv {currentLevel} / {maxLevel}");
            levelLabel.AddToClassList("upgrade-row-level");
            header.Add(nameLabel);
            header.Add(levelLabel);
            row.Add(header);

            if (!isMaxed && next.HasValue)
            {
                var desc = new Label($"{next.Value.maxOutputItems} item output buffer");
                desc.AddToClassList("upgrade-row-desc");
                row.Add(desc);

                long balance   = GetBaseCurrency();
                bool canAfford = balance >= next.Value.costBaseCurrency;

                var footer = new VisualElement();
                footer.AddToClassList("upgrade-row-footer");

                var costLabel = new Label($"{next.Value.costBaseCurrency:N0} e");
                costLabel.AddToClassList("upgrade-row-cost");
                if (!canAfford) costLabel.AddToClassList("upgrade-row-cost--unaffordable");
                footer.Add(costLabel);

                var btn = new Button { text = "Upgrade" };
                btn.AddToClassList("craft-btn");
                btn.SetEnabled(canAfford);
                int capturedNextLevel = currentLevel + 1;
                var capturedSO        = so;
                btn.clicked += () => OnStorageUpgradeBought(entity, capturedSO, capturedNextLevel);
                footer.Add(btn);

                row.Add(footer);
            }

            _inspectorContent?.Add(row);
        }

        private void OnSpeedUpgradeBought(Entity entity, BuildingSO so, int nextLevel)
        {
            if (!_ecsReady || !_em.Exists(entity)) return;
            var next = so.NextSpeedUpgrade(nextLevel - 1);
            if (!next.HasValue) return;

            int cost = next.Value.costBaseCurrency;
            if (!TryDeductCurrency(cost)) return;

            var bd = _em.GetComponentData<BuildingData>(entity);
            bd.UpgradeLevel    = nextLevel;
            bd.ProductionSpeed = BuildingSO.ProductionSpeedForLevel(so, nextLevel);
            _em.SetComponentData(entity, bd);

            // The level-based speed write above wiped any assigned manager's CraftSpeed bonus;
            // re-bake it relative to the new base so the bonus survives the upgrade.
            ManagerService.Instance?.ReapplyAfterSpeedReset(entity);

            SaveManager.Instance?.SaveLocal();
            _hud?.ShowNotification("⚡", $"Speed upgraded to Lv {nextLevel}!");
            RefreshInspectorContent();
        }

        private void OnStorageUpgradeBought(Entity entity, BuildingSO so, int nextLevel)
        {
            if (!_ecsReady || !_em.Exists(entity)) return;
            var next = so.NextStorageUpgrade(nextLevel - 1);
            if (!next.HasValue) return;

            int cost = next.Value.costBaseCurrency;
            if (!TryDeductCurrency(cost)) return;

            var bd = _em.GetComponentData<BuildingData>(entity);
            bd.StorageUpgradeLevel = nextLevel;
            _em.SetComponentData(entity, bd);

            if (_em.HasComponent<BuildingInventoryConfig>(entity))
            {
                var cfg = _em.GetComponentData<BuildingInventoryConfig>(entity);
                cfg.OutputCapacity = BuildingSO.OutputCapacityForLevel(so, nextLevel);
                _em.SetComponentData(entity, cfg);
            }

            SaveManager.Instance?.SaveLocal();
            _hud?.ShowNotification("📦", $"Storage upgraded to Lv {nextLevel}!");
            RefreshInspectorContent();
        }

        // ── Field-collector recipe section ────────────────────────────────────

        private void AddFieldCollectorRecipeSection(Entity entity, BuildingSO so, FieldSO field)
        {
            if (field.drops == null || field.drops.Count == 0) return;

            // Find which of the building's declared recipes match this field's actual drops.
            var relevantRecipes = new List<RecipeSO>();
            if (so.supportedRecipes != null)
            {
                var dropIds = new HashSet<int>();
                foreach (var drop in field.drops)
                    if (drop?.item != null) dropIds.Add(drop.item.itemId);

                foreach (var r in so.supportedRecipes)
                    if (r?.outputItem != null && dropIds.Contains(r.outputItem.itemId))
                        relevantRecipes.Add(r);
            }

            // Single (or no) relevant recipe: static "Collects" display, no chooser.
            if (relevantRecipes.Count <= 1)
            {
                AddInspectorRow("—— Collects ——");
                if (relevantRecipes.Count == 1)
                {
                    var r = relevantRecipes[0];
                    AddInspectorRow($"  {r.outputItem.displayName ?? r.outputItem.name}");
                }
                else
                {
                    // No matching recipe in the SO — fall back to raw field drops.
                    foreach (var drop in field.drops)
                        if (drop?.item != null)
                            AddInspectorRow($"  {drop.item.displayName ?? drop.item.name}");
                }
                return;
            }

            // Multiple relevant recipes: let the player choose what to collect.
            AddInspectorRow("—— Set Collection Target ——");
            foreach (var r in relevantRecipes)
            {
                if (r == null) continue;
                var captured = r;
                var btn = new Button { text = r.displayName ?? r.outputItem?.displayName ?? r.name };
                btn.AddToClassList("craft-btn");
                btn.clicked += () =>
                {
                    SetBuildingRecipe(entity, captured);
                    RefreshInspectorContent();
                };
                _inspectorContent?.Add(btn);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private long GetBaseCurrency()
        {
            if (!_ecsReady || _playerQuery.IsEmpty) return 0L;
            return _em.GetComponentData<PlayerProgressData>(_playerQuery.GetSingletonEntity()).BaseCurrency;
        }

        private bool TryDeductCurrency(int cost)
        {
            if (_playerQuery.IsEmpty) return false;
            var entity   = _playerQuery.GetSingletonEntity();
            var progress = _em.GetComponentData<PlayerProgressData>(entity);
            if (progress.BaseCurrency < cost) return false;
            progress.BaseCurrency      -= cost;
            progress.TotalEntropySpent += cost;
            _em.SetComponentData(entity, progress);
            return true;
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
