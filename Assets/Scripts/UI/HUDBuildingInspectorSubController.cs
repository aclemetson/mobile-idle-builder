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
        // Research ids that gate the per-building capacity upgrades (must match game_data.json research ids).
        private const string OutputCapacityResearchId = "surplus_containment";
        private const string InputCapacityResearchId  = "feedstock_buffers";

        private VisualElement _buildingInspectorPanel;
        private VisualElement _inspectorStatic;    // read-only status (Active..Produces), pinned above the scrolls
        private ScrollView    _inspectorRecipes;   // Set Recipe picker (scrolls)
        private ScrollView    _inspectorUpgrades;  // power + speed/storage/input + manager (scrolls)
        private Label         _inspectorBuildingName;

        private Entity _inspectorEntity      = Entity.Null;
        private float  _inspectorRefreshTimer;
        // Live craft-progress bar fill (recreated each rebuild; updated per-frame in Tick).
        private VisualElement _craftProgressFill;
        // Tallest height the static block has reached for the current entity (see OnStaticGeometryChanged).
        private float _staticReservedHeight;

        private EntityManager              _em;
        private EntityQuery                _playerQuery;
        private EntityQuery                _inventoryQuery;
        private bool                       _ecsReady;
        private BuildingPlacementController _placement;
        private HUDController              _hud;
        private GridRenderer              _gridRenderer;
        private BuildingVisualizer        _buildingVisualizer;
        private PlacementRadiusIndicator  _radiusIndicator;
        private PowerConnectionRenderer   _connectionRenderer;

        // Matches BuildingVisualizer's in-range tint / the placement ring colour.
        private static readonly Color PowerRadiusColor = new Color(0.35f, 1f, 0.75f, 0.9f);
        // Matches the placement connection-preview colour (warm amber, reads as "wired").
        private static readonly Color PowerConnectionColor = new Color(1f, 0.9f, 0.3f, 0.95f);

        private GridRenderer GetGridRenderer()
        {
            if (_gridRenderer == null) _gridRenderer = FindAnyObjectByType<GridRenderer>();
            return _gridRenderer;
        }

        private BuildingVisualizer GetBuildingVisualizer()
        {
            if (_buildingVisualizer == null) _buildingVisualizer = FindAnyObjectByType<BuildingVisualizer>();
            return _buildingVisualizer;
        }

        /// <summary>Lazily creates the selection radius ring (runtime GameObject, no scene wiring).</summary>
        private PlacementRadiusIndicator EnsureRadiusIndicator()
        {
            if (_radiusIndicator == null)
            {
                var gr = GetGridRenderer();
                if (gr == null) return null;
                var go = new GameObject("InspectorPowerRadius");
                go.transform.SetParent(gr.transform, worldPositionStays: false);
                _radiusIndicator = go.AddComponent<PlacementRadiusIndicator>();
            }
            return _radiusIndicator;
        }

        /// <summary>Lazily creates the selection connection-lines renderer (runtime GameObject, no scene wiring).</summary>
        private PowerConnectionRenderer EnsureConnectionRenderer()
        {
            if (_connectionRenderer == null)
            {
                var gr = GetGridRenderer();
                if (gr == null) return null;
                var go = new GameObject("InspectorPowerConnections");
                go.transform.SetParent(gr.transform, worldPositionStays: false);
                _connectionRenderer = go.AddComponent<PowerConnectionRenderer>();
            }
            return _connectionRenderer;
        }

        /// <summary>
        /// Draws the selected power building's reach as a circular ring, lights up the buildings it powers
        /// (all placed buildings whose footprint overlaps its radius, excluding the source itself), and
        /// dashes a line to every power building it links to within its connection range.
        /// Mirrors the placement preview; the ring radius equals the powered area exactly.
        /// </summary>
        private void ShowSelectedPowerRadius(int cellX, int cellY, int fw, int fh, float radius, float linkRange)
        {
            var gr = GetGridRenderer();
            if (gr == null || cellX < 0) return;

            float cs     = gr.CellSize;
            var   center = new Vector3((cellX + (fw - 1) * 0.5f) * cs, 0f, (cellY + (fh - 1) * 0.5f) * cs);
            float worldRadius = radius * cs + Mathf.Max(fw, fh) * 0.5f * cs;

            EnsureRadiusIndicator()?.Show(center, worldRadius, PowerRadiusColor);
            GetBuildingVisualizer()?.HighlightBuildingsInRange(cellX, cellY, fw, fh, radius, (cellX, cellY));
            ShowSelectedConnections(cellX, cellY, fw, fh, linkRange, center, cs);
        }

        /// <summary>Dashes lines from the selected power node to each power building within its link range.</summary>
        private void ShowSelectedConnections(int cellX, int cellY, int fw, int fh, float linkRange,
                                             Vector3 center, float cs)
        {
            if (!_ecsReady || linkRange <= 0f) { _connectionRenderer?.Hide(); return; }

            var nodes = PowerConnectionGraph.GatherFromEcs(_em, cs);
            var lines = new List<(Vector3, Vector3, Color)>();
            int sMaxX = cellX + fw - 1, sMaxY = cellY + fh - 1;

            foreach (var n in nodes)
            {
                if (n.AnchorX == cellX && n.AnchorY == cellY) continue; // skip self
                if (PowerCoverageMath.NodesLinked(
                        cellX, cellY, sMaxX, sMaxY,
                        n.AnchorX, n.AnchorY, n.AnchorX + n.Width - 1, n.AnchorY + n.Height - 1,
                        linkRange, n.LinkRange))
                {
                    lines.Add((center, n.WorldCenter, PowerConnectionColor));
                }
            }

            EnsureConnectionRenderer()?.Show(lines);
        }

        /// <summary>Hides the selection ring/connections and reverts the buildings it lit.</summary>
        private void ClearSelectedPowerVisual()
        {
            _radiusIndicator?.Hide();
            _connectionRenderer?.Hide();
            GetBuildingVisualizer()?.ClearRangeHighlight();
        }

        // Compact-card modifier: a selected power building shows a small top-right card (instead of the
        // full-width bottom slab) so the influence-radius ring on the grid stays visible.
        private const string PowerCardClass = "output-selector--power";

        private void SetPowerCardCompact(bool compact)
        {
            if (_buildingInspectorPanel == null) return;
            if (compact) _buildingInspectorPanel.AddToClassList(PowerCardClass);
            else         _buildingInspectorPanel.RemoveFromClassList(PowerCardClass);
        }

        public void Init(VisualElement root, BuildingPlacementController placement, HUDController hud)
        {
            _buildingInspectorPanel = root.Q("building-inspector-panel");
            _inspectorStatic        = root.Q("inspector-static");
            _inspectorRecipes       = root.Q<ScrollView>("inspector-recipes");
            _inspectorUpgrades      = root.Q<ScrollView>("inspector-upgrades");
            _inspectorBuildingName  = root.Q<Label>("inspector-building-name");
            _placement              = placement;
            _hud                   = hud;
            _inspectorStatic?.RegisterCallback<GeometryChangedEvent>(OnStaticGeometryChanged);
        }

        public void SetECSContext(EntityManager em)
        {
            _em             = em;
            _playerQuery    = em.CreateEntityQuery(ComponentType.ReadWrite<PlayerProgressData>());
            _inventoryQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerInventoryTag>(),
                ComponentType.ReadWrite<InventorySlot>());
            _ecsReady    = true;
        }

        public void Tick()
        {
            if (_inspectorEntity == Entity.Null) return;
            UpdateCraftProgressBar(); // smooth per-frame fill between the 0.5s full rebuilds
            _inspectorRefreshTimer += Time.deltaTime;
            if (_inspectorRefreshTimer < 0.5f) return;
            // Hold the rebuild while a finger is down: it replaces every button, so a tap that
            // straddled it was lost (the "finicky" Empty output button). Runs on release instead.
            if (ListTapRouter.GestureInProgress) return;
            _inspectorRefreshTimer = 0f;
            RefreshInspectorContent();
        }

        public void ShowBuildingInspector(Entity entity, string buildingName)
        {
            GameLogger.Develop($"[InspectorUI] ShowBuildingInspector: '{buildingName}' ecsReady={_ecsReady} panel={((_buildingInspectorPanel == null) ? "NULL" : "ok")}");
            if (!_ecsReady) return;
            if (entity != _inspectorEntity) ResetStaticReservedHeight();
            _inspectorEntity = entity;
            if (_inspectorBuildingName != null) _inspectorBuildingName.text = buildingName;
            RefreshInspectorContent();
            HUDController.SetElementVisible(_buildingInspectorPanel, true);
            GameLogger.Develop($"[InspectorUI] Panel shown — entity={entity}");
        }

        /// <summary>True while a building is selected in the inspector.</summary>
        public bool IsOpen => _inspectorEntity != Entity.Null;

        /// <summary>Fires when an open inspector closes, however that happened (X button, back, building gone).</summary>
        public event System.Action Hidden;

        public void HideBuildingInspector()
        {
            bool wasOpen = IsOpen;
            HUDController.SetElementVisible(_buildingInspectorPanel, false);
            _inspectorEntity = Entity.Null;
            ResetStaticReservedHeight();
            ClearSelectedPowerVisual();
            SetPowerCardCompact(false);
            if (wasOpen) Hidden?.Invoke();
        }

        private void RefreshInspectorContent()
        {
            if (_inspectorStatic == null || _inspectorRecipes == null || _inspectorUpgrades == null || !_ecsReady) return;
            if (_inspectorEntity == Entity.Null) return;
            if (!_em.Exists(_inspectorEntity)) { HideBuildingInspector(); return; }

            _inspectorStatic.Clear();
            _inspectorRecipes.Clear();
            _inspectorUpgrades.Clear();
            _craftProgressFill = null; // dropped by the Clear() above; re-created if this building crafts
            // Clear any radius visual from a previously-selected generator; re-shown below if this one is a source.
            ClearSelectedPowerVisual();

            bool hasBuildingData = _em.HasComponent<BuildingData>(_inspectorEntity);
            BuildingData buildingData = hasBuildingData
                ? _em.GetComponentData<BuildingData>(_inspectorEntity) : default;

            int buildingCellX = -1, buildingCellY = -1;
            if (_em.HasComponent<GridPosition>(_inspectorEntity))
            {
                var p = _em.GetComponentData<GridPosition>(_inspectorEntity);
                buildingCellX = p.Cell.x;
                buildingCellY = p.Cell.y;
            }

            var buildingSO = GetBuildingSO(_inspectorEntity);

            // Power sources (generators, relays) get a MINIMAL top-right card so the on-grid influence
            // ring stays visible: Output/Coverage + the coverage upgrade only. Grid-wide power totals live
            // in the top-bar power readout; Active/Cell/Manager rows are omitted for power buildings.
            if (buildingSO != null && buildingSO.isPowerSource)
            {
                SetPowerCardCompact(true);
                AddPowerSection(buildingSO, buildingData, buildingCellX, buildingCellY);
                AddSpeedUpgradeSection(_inspectorEntity, buildingSO, buildingData);
                return;
            }
            SetPowerCardCompact(false);

            if (hasBuildingData)
                AddInspectorRow(_inspectorStatic, $"Active: {(buildingData.IsActive ? "Yes" : "No")}");
            if (buildingCellX >= 0)
                AddInspectorRow(_inspectorStatic, $"Cell: ({buildingCellX}, {buildingCellY})");

            // Crafter creation timer: live progress toward the next output cycle. Harvesters run on
            // their collector timer instead (RecipeProcessData never advances for them), so their bar
            // is added in the Collector section below.
            bool isCollector = _em.HasComponent<CollectorData>(_inspectorEntity);
            if (!isCollector && _em.HasComponent<RecipeProcessData>(_inspectorEntity))
            {
                var rp = _em.GetComponentData<RecipeProcessData>(_inspectorEntity);
                if (rp.CraftTime > 0f)
                {
                    AddInspectorRow(_inspectorStatic, "—— Crafting ——");
                    _craftProgressFill = AddCraftProgressBar(_inspectorStatic, rp);
                }
            }

            if (_em.HasComponent<CollectorData>(_inspectorEntity))
            {
                var col    = _em.GetComponentData<CollectorData>(_inspectorEntity);
                float rate = col.OutputRate > 0f ? col.OutputRate : 1f;
                float next = Mathf.Max(0f, (1f / rate) - col.Timer);
                AddInspectorRow(_inspectorStatic, "—— Collector ——");
                _craftProgressFill = AddProgressBar(_inspectorStatic, CollectorFraction(col));
                AddInspectorRow(_inspectorStatic, $"Output rate: {col.OutputRate:F1} /s");
                if (_em.HasBuffer<RecipeOutputSlot>(_inspectorEntity))
                {
                    var outBuf = _em.GetBuffer<RecipeOutputSlot>(_inspectorEntity, isReadOnly: true);
                    if (outBuf.Length > 0)
                    {
                        ReadOutputMult(_inspectorEntity, out float cOutMult, out float cSpeed);
                        AddInspectorRow(_inspectorStatic, CollectorValueLine(
                            ItemValue.CollectorPerSecond(SurplusSale.DemonPrice(outBuf[0].ItemID), col.OutputRate,
                                                         cOutMult, cSpeed)));
                    }
                }
                AddInspectorRow(_inspectorStatic, $"Next item in: {next:F2}s");
            }

            // Buffer sections keep a stable shape across the 0.5s rebuilds: one row per item the recipe
            // expects (shown at ×0 when absent) and an always-present Empty button that is merely
            // disabled when there is nothing to empty. Adding/removing rows as items flow in and out
            // shifted the scrolls below and moved buttons out from under the finger.
            if (_em.HasBuffer<BuildingOutputSlot>(_inspectorEntity))
            {
                var buf      = _em.GetBuffer<BuildingOutputSlot>(_inspectorEntity, isReadOnly: true);
                var contents = new List<(int itemId, int quantity)>(buf.Length);
                for (int i = 0; i < buf.Length; i++) contents.Add((buf[i].ItemID, buf[i].Quantity));

                AddInspectorRow(_inspectorStatic, "—— Output Buffer ——");
                AddBufferRows(StableBufferRows(RecipeOutputIds(_inspectorEntity), contents));
                var captured = _inspectorEntity;
                AddEmptyBufferButton("Empty output → inventory", contents.Count > 0,
                                     () => EmptyOutputToInventory(captured));
            }

            if (_em.HasBuffer<BuildingInputSlot>(_inspectorEntity))
            {
                var buf      = _em.GetBuffer<BuildingInputSlot>(_inspectorEntity, isReadOnly: true);
                var expected = RecipeInputIds(_inspectorEntity);
                if (ShowInputBufferSection(buf.Length, expected.Count, _em.HasComponent<EntropySinkTag>(_inspectorEntity)))
                {
                    var contents = new List<(int itemId, int quantity)>(buf.Length);
                    for (int i = 0; i < buf.Length; i++) contents.Add((buf[i].ItemID, buf[i].Quantity));

                    AddInspectorRow(_inspectorStatic, "—— Input Buffer ——");
                    AddBufferRows(StableBufferRows(expected, contents));
                    var captured = _inspectorEntity;
                    AddEmptyBufferButton("Empty input → inventory", contents.Count > 0,
                                         () => EmptyInputToInventory(captured));
                }
            }

            if (_em.HasBuffer<RecipeOutputSlot>(_inspectorEntity))
            {
                var buf = _em.GetBuffer<RecipeOutputSlot>(_inspectorEntity, isReadOnly: true);
                if (buf.Length > 0)
                {
                    AddInspectorRow(_inspectorStatic, "—— Produces ——");
                    for (int i = 0; i < buf.Length; i++)
                        AddInspectorRow(_inspectorStatic, $"  {ItemName(buf[i].ItemID)}  ×  {buf[i].Quantity}");

                    // Three fixed rows for a crafter with a recipe (stable across rebuilds).
                    if (!isCollector && hasBuildingData && _em.HasComponent<RecipeProcessData>(_inspectorEntity))
                    {
                        var outs = new List<(int itemId, int quantity)>(buf.Length);
                        for (int i = 0; i < buf.Length; i++) outs.Add((buf[i].ItemID, buf[i].Quantity));
                        foreach (var line in CrafterValueLines(outs, buildingData))
                            AddInspectorRow(_inspectorStatic, line);
                        // Always present so the 0.5 s rebuild keeps its layout.
                        var (sold, paid) = SurplusSaleLog.Rate(_inspectorEntity, Time.time);
                        AddInspectorRow(_inspectorStatic, SurplusLine(sold, paid, SurplusSale.CurrentFraction()));
                    }
                }
            }

            if (_em.HasComponent<RecipeProcessData>(_inspectorEntity))
            {
                float bonus = _em.GetComponentData<RecipeProcessData>(_inspectorEntity).ByproductEntropy;
                if (bonus > 0f) AddInspectorRow(_inspectorStatic, "  " + HUDController.EscapeBonusText(bonus));
            }

            // buildingSO fetched above (before the power-source early-out).

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
                    AddNucleonMergeSection(_inspectorEntity, buildingSO, buildingCellX, buildingCellY);
                }
                else if (buildingSO.isItemFilter)
                {
                    AddFilterSection(_inspectorEntity);
                }
                else if (buildingSO.isSplitter)
                {
                    AddInspectorRow(_inspectorRecipes, "—— Splitting ——");
                    AddInspectorRow(_inspectorRecipes,
                        "Every item goes out the next side in turn, so the belts share it evenly. " +
                        "A side with no belt, or a full one, is skipped.");
                }
                else if (buildingSO.supportedRecipes != null)
                {
                    // Only offer recipes whose gating research has been purchased. Research-gating
                    // the picker here is what makes the element/material research tree actually
                    // limit what a crafter can produce (see IsRecipeAvailable).
                    var available = new List<RecipeSO>();
                    foreach (var r in buildingSO.supportedRecipes)
                        if (IsRecipeAvailable(r)) available.Add(r);
                    available = FilterByRecipeMode(_inspectorEntity, buildingSO, available);

                    // Exactly one usable recipe and none set yet: assign it instead of leaving the
                    // building idle at "No recipe" with no chooser to fix it (#186).
                    if (available.Count == 1
                        && _em.HasComponent<RecipeProcessData>(_inspectorEntity)
                        && _em.GetComponentData<RecipeProcessData>(_inspectorEntity).RecipeID < 0)
                    {
                        SetBuildingRecipe(_inspectorEntity, available[0]);
                        // Re-render with the recipe applied; the RecipeID check keeps this from recursing.
                        if (_em.GetComponentData<RecipeProcessData>(_inspectorEntity).RecipeID >= 0)
                        {
                            RefreshInspectorContent();
                            return;
                        }
                    }

                    if (available.Count == 0 && buildingSO.supportedRecipes.Length > 0)
                    {
                        AddInspectorRow(_inspectorRecipes, "No recipes unlocked yet.");
                        var gate = FirstGatingResearch(buildingSO);
                        if (gate != null)
                            AddInspectorRow(_inspectorRecipes, $"Research {gate.displayName} to unlock one.");
                    }

                    if (available.Count > 1)
                    {
                        AddInspectorRow(_inspectorRecipes, "—— Set Recipe ——");
                        foreach (var r in available)
                        {
                            var captured = r;
                            var btn = MakeRecipeButton(r, SharesOutput(available, r));
                            // Routed from the list: a Button's own Clickable consumes the
                            // pointer-down before any manipulator added later. See ListTapRouter.
                            ListTapRouter.Register(_inspectorRecipes, btn, () =>
                            {
                                SetBuildingRecipe(_inspectorEntity, captured);
                                RefreshInspectorContent();
                            });
                            _inspectorRecipes?.Add(btn);
                        }
                    }
                }

                if (buildingSO.assignableOutputs && !buildingSO.isItemFilter)
                    AddFilterSection(_inspectorEntity);

                AddPowerSection(buildingSO, buildingData, buildingCellX, buildingCellY);
                AddSpeedUpgradeSection(_inspectorEntity, buildingSO, buildingData);
                AddStorageUpgradeSection(_inspectorEntity, buildingSO, buildingData);
                AddInputUpgradeSection(_inspectorEntity, buildingSO, buildingData);
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

            AddInspectorRow(_inspectorUpgrades, "—— Manager ——");

            var current = svc.GetManagerAtBuilding(siteIndex, posKey);

            // One same-shaped row per manager, in catalogue order, whether or not it is on THIS
            // building: assigning/unassigning only flips the button text. Previously the assigned
            // manager moved into a differently-sized row at the top and its Assign button vanished,
            // reshuffling every row below the tap.
            bool anyRow = false;
            foreach (var mgr in svc.AllManagers)
            {
                if (mgr == null) continue;
                bool isHere = current != null && mgr.id == current.id;
                if (!isHere && !svc.IsHired(mgr.id)) continue;
                anyRow = true;

                var row = new VisualElement();
                row.AddToClassList("upgrade-row");

                int   stars    = svc.GetStars(mgr.id);
                float effValue = svc.EffectiveBonusValue(mgr.id);
                var name = new Label($"{mgr.displayName} {stars}★  ({ManagerBonusText(mgr.bonusType, effValue)})");
                name.AddToClassList("upgrade-row-name");
                row.Add(name);

                var btn = new Button { text = isHere ? "Unassign" : "Assign" };
                btn.AddToClassList("craft-btn");
                var capturedId = mgr.id;
                ListTapRouter.Register(_inspectorUpgrades, btn, () =>
                {
                    if (isHere) svc.Unassign(capturedId);
                    else        svc.Assign(capturedId, siteIndex, posKey);
                    RefreshInspectorContent();
                });
                row.Add(btn);
                _inspectorUpgrades?.Add(row);
            }

            if (!anyRow)
                AddInspectorRow(_inspectorUpgrades, "  (hire a manager in the Managers panel)");
        }

        /// <summary>Human-readable bonus text for a star-scaled effective value.</summary>
        private static string ManagerBonusText(ManagerBonusType type, float value)
        {
            switch (type)
            {
                case ManagerBonusType.CraftSpeed:     return $"{value:0.##}x speed";
                case ManagerBonusType.OutputQuantity: return $"{value:0.##}x output";
                case ManagerBonusType.PowerDiscount:  return $"-{(1f - value) * 100f:0}% power";
                default:                              return "";
            }
        }

        // ── Power UI ──────────────────────────────────────────────────────────

        /// <summary>
        /// Shows power info for the selected building. For a generator: output eV + coverage radius,
        /// and tints the covered tiles. For a consumer: draw eV + connection/brownout status.
        /// </summary>
        private void AddPowerSection(BuildingSO so, BuildingData bd, int cellX, int cellY)
        {
            int level = bd.UpgradeLevel < 1 ? 1 : bd.UpgradeLevel;

            if (so.isPowerSource)
            {
                float outputEV  = BuildingSO.PowerOutputForLevel(so, level);
                float radius    = BuildingSO.InfluenceRadiusForLevel(so, level);
                float linkRange = BuildingSO.LinkRadiusForLevel(so, level);
                AddInspectorRow(_inspectorStatic, "—— Power ——");
                AddInspectorRow(_inspectorStatic, $"Output: {outputEV:0.#} eV");
                AddInspectorRow(_inspectorStatic, $"Coverage: {radius:0.#} tiles");
                AddInspectorRow(_inspectorStatic, $"Link range: {linkRange:0.#} tiles");

                int fw = 1, fh = 1;
                if (_em.HasComponent<BuildingFootprint>(_inspectorEntity))
                {
                    var f = _em.GetComponentData<BuildingFootprint>(_inspectorEntity);
                    fw = f.Width; fh = f.Height;
                }
                // Show the reach as a circular ring, light up the buildings this source powers, and dash
                // lines to the power buildings it links to.
                ShowSelectedPowerRadius(cellX, cellY, fw, fh, radius, linkRange);
            }
            else if (so.requiresPower && _em.HasComponent<PowerStatus>(_inspectorEntity))
            {
                var status = _em.GetComponentData<PowerStatus>(_inspectorEntity);
                float draw = _em.HasComponent<PowerConsumer>(_inspectorEntity)
                    ? _em.GetComponentData<PowerConsumer>(_inspectorEntity).DrawEV
                    : 0f;
                AddInspectorRow(_inspectorStatic, "—— Power ——");
                AddInspectorRow(_inspectorStatic, $"Draw: {draw:0.#} eV");
                string statusText = status.IsConnected == 0
                    ? "UNPOWERED (no generator in range)"
                    : (status.ThrottleRatio < 0.999f
                        ? $"Brownout — {status.ThrottleRatio * 100f:0}% power"
                        : "Powered");
                AddInspectorRow(_inspectorStatic, $"Status: {statusText}");
            }
        }

        // ── Upgrade UI ────────────────────────────────────────────────────────

        private void AddSpeedUpgradeSection(Entity entity, BuildingSO so, BuildingData bd)
        {
            if (so.upgradeLevels == null || so.upgradeLevels.Length == 0) return;

            int  currentLevel = bd.UpgradeLevel < 1 ? 1 : bd.UpgradeLevel;
            int  maxLevel     = so.MaxSpeedLevel();
            bool isMaxed      = currentLevel >= maxLevel;
            var  next         = isMaxed ? (BuildingUpgradeLevel?)null : so.NextSpeedUpgrade(currentLevel);

            string descText = null;
            if (next.HasValue)
            {
                if (so.isPowerSource)
                    descText = next.Value.outputEV > 0f
                        ? $"→ {next.Value.outputEV:0.#} eV  ·  {next.Value.influenceRadiusTiles:0.#} tiles"
                        : $"→ {next.Value.influenceRadiusTiles:0.#} tiles coverage";
                else
                    descText = $"{next.Value.outputRate:F1}× production speed";
            }

            int capturedNextLevel = currentLevel + 1;
            // Power sources upgrade coverage/output, not craft speed — label it accordingly.
            AddUpgradeRow(so.isPowerSource ? "Coverage Upgrade" : "Speed Upgrade",
                          currentLevel, maxLevel, isMaxed, descText, next?.costBaseCurrency ?? 0,
                          () => OnSpeedUpgradeBought(entity, so, capturedNextLevel));
        }

        /// <summary>
        /// Builds one upgrade row. A row with no next level (maxed, or a data gap) keeps its description
        /// line and footer, with the button disabled, so buying the last level doesn't collapse the row
        /// and pull the rows below it up under the finger. <paramref name="nextDesc"/> null = no next level.
        /// </summary>
        private void AddUpgradeRow(string title, int currentLevel, int maxLevel, bool isMaxed,
                                   string nextDesc, int nextCost, System.Action onBuy)
        {
            bool canUpgrade = !isMaxed && nextDesc != null;

            var row = new VisualElement();
            row.AddToClassList("upgrade-row");
            if (isMaxed) row.AddToClassList("upgrade-row--maxed");

            var header = new VisualElement();
            header.AddToClassList("upgrade-row-header");
            var nameLabel  = new Label(title);
            nameLabel.AddToClassList("upgrade-row-name");
            var levelLabel = new Label(isMaxed ? $"Lv {currentLevel} / {maxLevel}  MAX" : $"Lv {currentLevel} / {maxLevel}");
            levelLabel.AddToClassList("upgrade-row-level");
            header.Add(nameLabel);
            header.Add(levelLabel);
            row.Add(header);

            var desc = new Label(canUpgrade ? nextDesc : "Maximum level reached");
            desc.AddToClassList("upgrade-row-desc");
            row.Add(desc);

            bool canAfford = canUpgrade && GetBaseCurrency() >= nextCost;

            var footer = new VisualElement();
            footer.AddToClassList("upgrade-row-footer");

            var costLabel = new Label(canUpgrade ? $"{nextCost:N0} e" : "—");
            costLabel.AddToClassList("upgrade-row-cost");
            if (canUpgrade && !canAfford) costLabel.AddToClassList("upgrade-row-cost--unaffordable");
            footer.Add(costLabel);

            var btn = new Button { text = canUpgrade ? "Upgrade" : "Maxed" };
            btn.AddToClassList("craft-btn");
            btn.SetEnabled(canAfford);
            // Routed from the scroll: on touch the ScrollView swallows a Button's own Clickable, and the
            // 0.5s rebuild would drop a press held across it. See ListTapRouter.
            ListTapRouter.Register(_inspectorUpgrades, btn, onBuy);
            footer.Add(btn);

            row.Add(footer);
            _inspectorUpgrades?.Add(row);
        }

        /// <summary>
        /// Capacity upgrades (input/output) are gated behind dedicated research. Returns true only once
        /// the unlocking research has been purchased; treats a missing service as "not unlocked".
        /// </summary>
        private static bool IsCapacityUpgradeUnlocked(string researchId)
        {
            var svc = ResearchService.Instance;
            return svc != null && svc.IsUnlocked(researchId);
        }

        /// <summary>
        /// A crafter recipe is selectable only once its gating research is unlocked. Recipes flagged
        /// knownFromStart (or with no required research) are always available; a missing research
        /// service is treated as "nothing unlocked" so gated recipes stay hidden.
        /// </summary>
        private static bool IsRecipeAvailable(RecipeSO r) => RecipeAvailability.IsAvailable(r);

        private static ResearchSO FirstGatingResearch(BuildingSO so)
        {
            foreach (var r in so.supportedRecipes)
                if (r != null && r.requiredResearch != null) return r.requiredResearch;
            return null;
        }

        private void AddStorageUpgradeSection(Entity entity, BuildingSO so, BuildingData bd)
        {
            if (so.storageUpgradeLevels == null || so.storageUpgradeLevels.Length == 0) return;
            if (!IsCapacityUpgradeUnlocked(OutputCapacityResearchId)) return;

            int  currentLevel = bd.StorageUpgradeLevel < 1 ? 1 : bd.StorageUpgradeLevel;
            int  maxLevel     = so.MaxStorageLevel();
            bool isMaxed      = currentLevel >= maxLevel;
            var  next         = isMaxed ? (BuildingStorageUpgradeLevel?)null : so.NextStorageUpgrade(currentLevel);

            int capturedNextLevel = currentLevel + 1;
            AddUpgradeRow("Storage Upgrade", currentLevel, maxLevel, isMaxed,
                          next.HasValue ? $"{next.Value.maxOutputItems} item output buffer" : null,
                          next?.costBaseCurrency ?? 0,
                          () => OnStorageUpgradeBought(entity, so, capturedNextLevel));
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

            // Field collectors run on CollectorData.OutputRate, not ProductionSpeed: without this a
            // Harvester's speed upgrade cost entropy and changed nothing.
            if (_em.HasComponent<CollectorData>(entity))
            {
                var col = _em.GetComponentData<CollectorData>(entity);
                col.OutputRate = BuildingSO.CollectorRateForLevel(so, nextLevel);
                _em.SetComponentData(entity, col);
            }

            // Re-bake power values for the new level (generator output/radius, consumer draw).
            if (so.isPowerSource && _em.HasComponent<PowerNodeData>(entity))
            {
                var node = _em.GetComponentData<PowerNodeData>(entity);
                float outputEV       = BuildingSO.PowerOutputForLevel(so, nextLevel);
                node.MaxEV           = outputEV;
                node.CurrentEV       = outputEV;
                node.InfluenceRadius = BuildingSO.InfluenceRadiusForLevel(so, nextLevel);
                _em.SetComponentData(entity, node);
            }
            else if (so.requiresPower && _em.HasComponent<PowerConsumer>(entity))
            {
                var pc = _em.GetComponentData<PowerConsumer>(entity);
                pc.DrawEV = BuildingSO.PowerDrawFor(so, nextLevel, CurrentRecipe(entity, so));
                _em.SetComponentData(entity, pc);
            }

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

        private void AddInputUpgradeSection(Entity entity, BuildingSO so, BuildingData bd)
        {
            if (so.inputUpgradeLevels == null || so.inputUpgradeLevels.Length == 0) return;
            if (!IsCapacityUpgradeUnlocked(InputCapacityResearchId)) return;

            int  currentLevel = bd.InputUpgradeLevel < 1 ? 1 : bd.InputUpgradeLevel;
            int  maxLevel     = so.MaxInputLevel();
            bool isMaxed      = currentLevel >= maxLevel;
            var  next         = isMaxed ? (BuildingInputUpgradeLevel?)null : so.NextInputUpgrade(currentLevel);

            int capturedNextLevel = currentLevel + 1;
            AddUpgradeRow("Input Upgrade", currentLevel, maxLevel, isMaxed,
                          next.HasValue ? $"{next.Value.maxInputItems} item input buffer" : null,
                          next?.costBaseCurrency ?? 0,
                          () => OnInputUpgradeBought(entity, so, capturedNextLevel));
        }

        private void OnInputUpgradeBought(Entity entity, BuildingSO so, int nextLevel)
        {
            if (!_ecsReady || !_em.Exists(entity)) return;
            var next = so.NextInputUpgrade(nextLevel - 1);
            if (!next.HasValue) return;

            int cost = next.Value.costBaseCurrency;
            if (!TryDeductCurrency(cost)) return;

            var bd = _em.GetComponentData<BuildingData>(entity);
            bd.InputUpgradeLevel = nextLevel;
            _em.SetComponentData(entity, bd);

            if (_em.HasComponent<BuildingInventoryConfig>(entity))
            {
                var cfg = _em.GetComponentData<BuildingInventoryConfig>(entity);
                cfg.InputCapacity = BuildingSO.InputCapacityForLevel(so, nextLevel);
                _em.SetComponentData(entity, cfg);
            }

            SaveManager.Instance?.SaveLocal();
            _hud?.ShowNotification("📥", $"Input buffer upgraded to Lv {nextLevel}!");
            RefreshInspectorContent();
        }

        // ── Field-collector recipe section ────────────────────────────────────

        private void AddFieldCollectorRecipeSection(Entity entity, BuildingSO so, FieldSO field)
        {
            if (field.drops == null || field.drops.Count == 0) return;

            // Same list the placement chooser offered (FieldRecipes covers the Nucleon Harvester too).
            var relevantRecipes = FieldRecipes.For(so, field);

            // Single (or no) relevant recipe: static "Collects" display, no chooser.
            if (relevantRecipes.Count <= 1)
            {
                AddInspectorRow(_inspectorStatic, "—— Collects ——");
                if (relevantRecipes.Count == 1)
                {
                    var r = relevantRecipes[0];
                    AddInspectorRow(_inspectorStatic, $"  {r.outputItem.displayName ?? r.outputItem.name}");
                }
                else
                {
                    // No matching recipe in the SO — fall back to raw field drops.
                    foreach (var drop in field.drops)
                        if (drop?.item != null)
                            AddInspectorRow(_inspectorStatic, $"  {drop.item.displayName ?? drop.item.name}");
                }
                return;
            }

            // Multiple relevant recipes: let the player choose what to collect.
            AddInspectorRow(_inspectorRecipes, "—— Set Collection Target ——");
            foreach (var r in relevantRecipes)
            {
                if (r == null) continue;
                var captured = r;
                var btn = MakeRecipeButton(r, SharesOutput(relevantRecipes, r));
                ListTapRouter.Register(_inspectorRecipes, btn, () =>
                {
                    SetBuildingRecipe(entity, captured);
                    RefreshInspectorContent();
                });
                _inspectorRecipes?.Add(btn);
            }
        }

        // ── Recipe buttons (#336) ──────────────────────────────────────────────

        /// <summary>True when another recipe in the list makes the same output item as <paramref name="r"/>.</summary>
        private static bool SharesOutput(List<RecipeSO> list, RecipeSO r)
        {
            if (r == null || r.outputItem == null) return false;
            foreach (var other in list)
                if (other != null && other != r && other.outputItem == r.outputItem) return true;
            return false;
        }

        /// <summary>
        /// A picker button named after the recipe's product. When several buttons make the same product
        /// (<paramref name="withMethodLine"/>), a smaller second line says the building, inputs and method.
        /// </summary>
        private static Button MakeRecipeButton(RecipeSO r, bool withMethodLine)
        {
            string name = r.outputItem?.displayName ?? r.displayName ?? r.name;
            var btn = new Button();
            btn.AddToClassList("craft-btn");
            var icon = HUDController.MakeItemIcon(r.outputItem?.icon, "item-icon");

            if (!withMethodLine)
            {
                btn.text = name;
                if (icon != null) btn.Insert(0, icon);
                return btn;
            }

            // Two lines: the button's own text can't hold a second label, so lay out icon + a text column.
            btn.AddToClassList("craft-btn--with-method");
            if (icon != null) btn.Add(icon);
            var column = new VisualElement { pickingMode = PickingMode.Ignore };
            column.AddToClassList("recipe-text-column");
            column.Add(new Label(name) { pickingMode = PickingMode.Ignore });
            var method = new Label(RecipeText.MethodLine(r)) { pickingMode = PickingMode.Ignore };
            method.AddToClassList("recipe-method-line");
            column.Add(method);
            btn.Add(column);
            return btn;
        }

        // ── Recipe modes (#322: Neutron Capture Slow / Fast) ───────────────────

        // The mode each building's picker shows. Not saved: it opens on the mode of the building's recipe.
        private readonly Dictionary<Entity, int> _recipeMode = new();

        /// <summary>
        /// For a building with recipe modes, adds a row of mode buttons and returns only that mode's recipes.
        /// Modes with nothing researched yet are hidden; with one mode left there is no row at all.
        /// </summary>
        private List<RecipeSO> FilterByRecipeMode(Entity entity, BuildingSO so, List<RecipeSO> available)
        {
            if (so.recipeModePrefixes == null || so.recipeModePrefixes.Length == 0) return available;

            var modes = new List<int>();
            for (int m = 0; m < so.recipeModePrefixes.Length; m++)
                if (available.Exists(r => so.RecipeModeOf(r.id) == m)) modes.Add(m);
            if (modes.Count == 0) return available;

            if (!_recipeMode.TryGetValue(entity, out int mode) || !modes.Contains(mode))
            {
                int current = so.RecipeModeOf(CurrentRecipe(entity, so)?.id);
                mode = modes.Contains(current) ? current : modes[0];
                _recipeMode[entity] = mode;
            }

            if (modes.Count > 1)
            {
                var row = new VisualElement();
                row.AddToClassList("filter-row");
                foreach (int m in modes)
                {
                    int captured = m;
                    string label = so.recipeModeLabels != null && m < so.recipeModeLabels.Length ? so.recipeModeLabels[m] : $"Mode {m + 1}";
                    var btn = new Button { text = label };
                    btn.AddToClassList("filter-side-btn");
                    btn.AddToClassList("recipe-mode-btn");
                    btn.EnableInClassList("filter-side-btn--on", m == mode);
                    ListTapRouter.Register(_inspectorRecipes, btn, () =>
                    {
                        _recipeMode[entity] = captured;
                        RefreshInspectorContent();
                    });
                    row.Add(btn);
                }
                _inspectorRecipes?.Add(row);
            }
            return available.FindAll(r => so.RecipeModeOf(r.id) == mode);
        }

        private RecipeSO CurrentRecipe(Entity entity, BuildingSO so)
        {
            if (so?.supportedRecipes == null || !_em.HasComponent<RecipeProcessData>(entity)) return null;
            int id = _em.GetComponentData<RecipeProcessData>(entity).RecipeID;
            return System.Array.Find(so.supportedRecipes, r => r != null && r.recipeId == id);
        }

        private BuildingSO FindBuildingSOForEntity(Entity entity)
        {
            if (!_em.HasComponent<BuildingData>(entity) || _placement?.availableBuildings == null) return null;
            int type = _em.GetComponentData<BuildingData>(entity).BuildingType;
            foreach (var entry in _placement.availableBuildings)
                if (entry.building != null && entry.building.buildingId == type) return entry.building;
            return null;
        }

        // ── Filter (#321) ─────────────────────────────────────────────────────

        private static readonly string[] SideArrows = { "\u2191", "\u2192", "\u2193", "\u2190" }; // N E S W

        /// <summary>
        /// One row per item that has reached the Filter (or that a rule names), plus "Everything else". Each row
        /// has a button per output side; the lit one is where that item goes now. An item row's "Else" sends it
        /// with everything else; the "Everything else" row's "Wait" holds unassigned items back.
        /// </summary>
        private void AddFilterSection(Entity entity)
        {
            if (!_em.HasBuffer<OutputPortFilter>(entity)) return;
            var rules = FilterSystem.ReadRules(_em.GetBuffer<OutputPortFilter>(entity, isReadOnly: true));

            var sides = new List<int>();
            var ports = _em.GetBuffer<PlacedPortData>(entity, isReadOnly: true);
            for (int i = 0; i < ports.Length; i++)
                if (ports[i].PortType == (int)PortType.Output && !sides.Contains(ports[i].Facing)) sides.Add(ports[i].Facing);
            sides.Sort();

            var items = new List<int>();
            if (_em.HasBuffer<FilterSeenItem>(entity))
            {
                var seen = _em.GetBuffer<FilterSeenItem>(entity, isReadOnly: true);
                for (int i = 0; i < seen.Length; i++) if (!items.Contains(seen[i].ItemID)) items.Add(seen[i].ItemID);
            }
            foreach (var (_, id) in rules)
                if (id != PortFilterRules.Everything && !items.Contains(id)) items.Add(id);
            // A building with assignable outputs (#322) sorts its own products.
            bool isFilter = _em.HasComponent<ItemFilterTag>(entity);
            if (!isFilter && _em.HasBuffer<RecipeOutputSlot>(entity))
            {
                var products = _em.GetBuffer<RecipeOutputSlot>(entity, isReadOnly: true);
                for (int i = 0; i < products.Length; i++)
                    if (!items.Contains(products[i].ItemID)) items.Add(products[i].ItemID);
            }

            AddInspectorRow(_inspectorRecipes, isFilter ? "—— Sorting ——" : "—— Output sides ——");
            if (items.Count == 0)
                AddInspectorRow(_inspectorRecipes, "Belt a mixed line in. Each item that arrives gets a row here.");
            foreach (var itemId in items)
                AddFilterRow(entity, rules, sides, itemId, ItemName(itemId));
            AddFilterRow(entity, rules, sides, PortFilterRules.Everything, "Everything else");
        }

        private void AddFilterRow(Entity entity, List<(int facing, int itemId)> rules, List<int> sides,
                                  int itemId, string label)
        {
            // The side this row's rule names (for an item with no rule of its own, it rides "Everything else").
            int current = PortFilterRules.NoRoute;
            foreach (var (facing, id) in rules)
                if (id == itemId) current = facing;

            var row = new VisualElement();
            row.AddToClassList("filter-row");
            var icon = itemId >= 0 ? HUDController.MakeItemIcon(ItemDatabase.Instance?.Get(itemId)?.icon, "item-icon") : null;
            if (icon != null) row.Add(icon);
            var name = new Label(label);
            name.AddToClassList("filter-row__name");
            row.Add(name);

            var choices = new List<int>(sides) { PortFilterRules.NoRoute };
            foreach (int side in choices)
            {
                int captured = side;
                // No rule of its own: an item goes wherever "Everything else" goes ("Else"); for the
                // "Everything else" row itself, no side means unassigned items wait.
                string noSide = itemId == PortFilterRules.Everything ? "Wait" : "Else";
                var btn = new Button { text = side == PortFilterRules.NoRoute ? noSide : SideArrows[side & 3] };
                btn.AddToClassList("filter-side-btn");
                btn.EnableInClassList("filter-side-btn--on", side == current);
                ListTapRouter.Register(_inspectorRecipes, btn, () =>
                {
                    if (!_ecsReady || !_em.Exists(entity) || !_em.HasBuffer<OutputPortFilter>(entity)) return;
                    var live = FilterSystem.ReadRules(_em.GetBuffer<OutputPortFilter>(entity, isReadOnly: true));
                    PortFilterRules.Assign(live, itemId, captured);
                    FilterSystem.WriteRules(_em.GetBuffer<OutputPortFilter>(entity), live);
                    SaveManager.Instance?.SaveLocal();
                    RefreshInspectorContent();
                });
                row.Add(btn);
            }
            _inspectorRecipes?.Add(row);
        }

        // ── Nucleon Harvester merge (#313) ────────────────────────────────────

        /// <summary>
        /// On a Harvester next to an opposite-polarity Harvester (Up beside Down Quarks), once Nucleon
        /// Harvesting is researched: a Merge row. The fee makes two Harvesters + merge cost exactly one placed
        /// Nucleon Harvester (NucleonHarvesterMerge.Fee).
        /// </summary>
        private void AddNucleonMergeSection(Entity entity, BuildingSO so, int cellX, int cellY)
        {
            if (so == null || so.id != NucleonHarvesterMerge.HarvesterId || cellX < 0) return;
            if (!(ResearchService.Instance?.IsUnlocked(NucleonHarvesterMerge.ResearchId) ?? false)) return;
            var nucleon = FindPlaceableBuilding(NucleonHarvesterMerge.NucleonHarvesterId);
            if (nucleon == null) return;
            if (!TryFindMergePartner(entity, so, new Vector2Int(cellX, cellY), out var partner)) return;

            long fee = NucleonHarvesterMerge.Fee(nucleon, so, CostReduction());
            bool canAfford = GetBaseCurrency() >= fee;

            var row = new VisualElement();
            row.AddToClassList("upgrade-row");
            var name = new Label("Nucleon Harvester");
            name.AddToClassList("upgrade-row-name");
            row.Add(name);
            var desc = new Label("Merge with the neighbouring Harvester into one Nucleon Harvester. " +
                                 "It makes protons or neutrons straight from the fields, no Combiner needed.");
            desc.AddToClassList("upgrade-row-desc");
            row.Add(desc);

            var footer = new VisualElement();
            footer.AddToClassList("upgrade-row-footer");
            var costLabel = new Label($"{fee:N0} e");
            costLabel.AddToClassList("upgrade-row-cost");
            if (!canAfford) costLabel.AddToClassList("upgrade-row-cost--unaffordable");
            footer.Add(costLabel);
            var btn = new Button { text = "Merge" };
            btn.AddToClassList("craft-btn");
            btn.SetEnabled(canAfford);
            ListTapRouter.Register(_inspectorUpgrades, btn,
                () => MergeIntoNucleonHarvester(entity, partner, so, nucleon, fee));
            footer.Add(btn);
            row.Add(footer);
            _inspectorUpgrades?.Add(row);
        }

        /// <summary>An orthogonally adjacent Harvester on a quark field collecting the opposite quark.</summary>
        private bool TryFindMergePartner(Entity self, BuildingSO harvester, Vector2Int cell, out Entity partner)
        {
            partner = Entity.Null;
            if (!IsQuarkField(cell)) return false;
            string mine = RecipeIdOf(self, harvester);

            using var query = _em.CreateEntityQuery(ComponentType.ReadOnly<BuildingData>(),
                                                    ComponentType.ReadOnly<GridPosition>(),
                                                    ComponentType.ReadOnly<RecipeProcessData>());
            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            foreach (var e in entities)
            {
                if (e == self || _em.GetComponentData<BuildingData>(e).BuildingType != harvester.buildingId) continue;
                var c = _em.GetComponentData<GridPosition>(e).Cell;
                var other = new Vector2Int(c.x, c.y);
                if (!NucleonHarvesterMerge.TryPlacement(cell, other, out _, out _) || !IsQuarkField(other)) continue;
                if (!NucleonHarvesterMerge.OpposingPolarity(mine, RecipeIdOf(e, harvester))) continue;
                partner = e;
                return true;
            }
            return false;
        }

        private void MergeIntoNucleonHarvester(Entity a, Entity b, BuildingSO harvester, BuildingSO nucleon, long fee)
        {
            if (!_ecsReady || !_em.Exists(a) || !_em.Exists(b)) return; // changed while the panel was open
            var ca = _em.GetComponentData<GridPosition>(a).Cell;
            var cb = _em.GetComponentData<GridPosition>(b).Cell;
            if (!NucleonHarvesterMerge.TryPlacement(new Vector2Int(ca.x, ca.y), new Vector2Int(cb.x, cb.y),
                                                    out var anchor, out int rotation)) return;
            var placer = FindAnyObjectByType<BuildingPlacer>();
            if (placer == null || !TryDeductCurrency(fee)) return;

            var bdA = _em.GetComponentData<BuildingData>(a);
            var bdB = _em.GetComponentData<BuildingData>(b);
            int speed   = NucleonHarvesterMerge.CarriedLevel(bdA.UpgradeLevel, bdB.UpgradeLevel);
            int storage = NucleonHarvesterMerge.CarriedLevel(bdA.StorageUpgradeLevel, bdB.StorageUpgradeLevel);

            RemoveForMerge(a);
            RemoveForMerge(b);
            rotation = NucleonHarvesterMerge.PreferBeltedRotation(nucleon, anchor, rotation, IsBeltAt);

            var recipe = System.Array.Find(nucleon.supportedRecipes,
                                           r => r != null && r.id == NucleonHarvesterMerge.DefaultRecipeId);
            if (!placer.PlaceBuilding(anchor.x, anchor.y, nucleon, recipe, null, rotation, false, speed, storage))
                GameLogger.Warning($"[Inspector] Nucleon Harvester merge could not place at ({anchor.x},{anchor.y}).");

            GetBuildingVisualizer()?.Refresh();
            FindAnyObjectByType<ConveyorVisualizer>()?.RefreshAll();
            SaveManager.Instance?.SaveLocal();
            HideBuildingInspector();
            _hud?.ShowNotification("\u269B", "Merged into a Nucleon Harvester. Tap it to choose protons or neutrons.");
        }

        private void RemoveForMerge(Entity e)
        {
            var c = _em.GetComponentData<GridPosition>(e).Cell;
            _em.DestroyEntity(e);
            GridOccupancy.Instance?.ReleaseRect(c.x, c.y, 1, 1);
            GetBuildingVisualizer()?.RemoveBuilding(c.x, c.y);
        }

        private bool IsBeltAt(Vector2Int cell)
        {
            using var q = _em.CreateEntityQuery(ComponentType.ReadOnly<ConveyorSegmentData>());
            using var segs = q.ToComponentDataArray<ConveyorSegmentData>(Unity.Collections.Allocator.Temp);
            foreach (var s in segs)
                if (s.Cell.x == cell.x && s.Cell.y == cell.y) return true;
            return false;
        }

        private static bool IsQuarkField(Vector2Int cell) =>
            FieldGenerator.GetFieldAt(cell.x, cell.y)?.fieldType == "Quark";

        private string RecipeIdOf(Entity e, BuildingSO so)
        {
            int id = _em.GetComponentData<RecipeProcessData>(e).RecipeID;
            var r = so.supportedRecipes == null ? null
                : System.Array.Find(so.supportedRecipes, x => x != null && x.recipeId == id);
            return r?.id;
        }

        private BuildingSO FindPlaceableBuilding(string id)
        {
            if (_placement?.availableBuildings == null) return null;
            foreach (var entry in _placement.availableBuildings)
                if (entry.building != null && entry.building.id == id) return entry.building;
            return null;
        }

        // outMult / global speed exactly as ProductionSystem and CollectorSystem read them: the manager's
        // AppliedOutputMult (when assigned) times the megastructure's GlobalProductionBonus times the
        // prestige shop's PrestigeData multipliers.
        private void ReadOutputMult(Entity entity, out float outMult, out float globalSpeed)
        {
            outMult = _em.HasComponent<ManagerAssignmentData>(entity)
                ? _em.GetComponentData<ManagerAssignmentData>(entity).AppliedOutputMult
                : 1f;
            globalSpeed = 1f;
            using (var q = _em.CreateEntityQuery(ComponentType.ReadOnly<GlobalProductionBonus>()))
            {
                if (!q.IsEmpty)
                {
                    var gb = q.GetSingleton<GlobalProductionBonus>();
                    outMult *= gb.OutputMult;
                    globalSpeed = gb.SpeedMult;
                }
            }
            using (var pq = _em.CreateEntityQuery(ComponentType.ReadOnly<PrestigeData>()))
            {
                if (pq.IsEmpty) return;
                var pd = pq.GetSingleton<PrestigeData>();
                if (pd.OutputMultiplier > 0f) outMult     *= pd.OutputMultiplier;
                if (pd.SpeedMultiplier  > 0f) globalSpeed *= pd.SpeedMultiplier;
            }
        }

        private string[] CrafterValueLines(List<(int itemId, int quantity)> outs, BuildingData building)
        {
            var process = _em.GetComponentData<RecipeProcessData>(_inspectorEntity);
            ReadOutputMult(_inspectorEntity, out float outMult, out float globalSpeed);
            bool hasPower = _em.HasComponent<PowerStatus>(_inspectorEntity);
            float powerRatio = hasPower ? _em.GetComponentData<PowerStatus>(_inspectorEntity).ThrottleRatio : 1f;

            float price = SurplusSale.DemonPrice(outs[0].itemId);
            double perCraft = ItemValue.PerCraft(outs, SurplusSale.DemonPrice, outMult, process.ByproductEntropy);
            float speed = building.ProductionSpeed * powerRatio * globalSpeed;
            double perSecond = ItemValue.PerSecond(perCraft, process.CraftTime, speed);
            return ValueLines(price, perCraft, outMult, perSecond, hasPower && powerRatio <= 0f);
        }

        /// <summary>
        /// The inspector's surplus row: what reaches a full input and is sold, and what it pays. Pure (unit-tested).
        /// </summary>
        internal static string SurplusLine(float itemsPerSecond, double entropyPerSecond, float fraction)
        {
            if (itemsPerSecond <= 0f) return "Surplus: none (inputs keep up)";
            return $"Selling surplus: {itemsPerSecond:0.##}/s for {entropyPerSecond:0.##} ◈/s ({fraction * 100f:0}% of value)";
        }

        /// <summary>
        /// The inspector's value rows for a crafter: price each, value per craft, value per second. Always three
        /// rows so the 0.5s rebuild keeps its layout.
        /// </summary>
        internal static string[] ValueLines(float price, double perCraft, float outMult, double perSecond, bool noPower)
        {
            string craft = outMult > 1.001f
                ? $"×{outMult:0.##} output → {NumberFormat.Entropy(perCraft)} per craft"
                : $"{NumberFormat.Entropy(perCraft)} per craft";
            string rate = noPower
                ? "≈ ◈ 0 /s (no power)"
                : $"≈ {NumberFormat.Entropy(perSecond)} /s at current speed";
            return new[] { HUDController.WorthEachText(price), craft, rate };
        }

        /// <summary>The collector's value row, e.g. "≈ ◈ 2.5 /s".</summary>
        internal static string CollectorValueLine(double perSecond) =>
            $"≈ {NumberFormat.Entropy(perSecond)} /s";

        private float CostReduction()
        {
            using var q = _em.CreateEntityQuery(ComponentType.ReadOnly<PrestigeData>());
            return q.IsEmpty ? 0f : q.GetSingleton<PrestigeData>().CostReduction;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private long GetBaseCurrency() => _ecsReady ? EntropyWallet.Balance(_playerQuery) : 0L;

        private bool TryDeductCurrency(long cost) => EntropyWallet.TrySpend(_playerQuery, cost);

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
                IsCrafting      = false,
                ByproductEntropy = recipe.EscapeEntropyPerCraft
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
            foreach (var (item, qty) in recipe.ProducedOutputs())
                outputBuf.Add(new RecipeOutputSlot { ItemID = item.itemId, Quantity = qty });

            SellInputsTheRecipeDoesNotUse(entity);
            MoveOutputsTheRecipeDoesNotMake(entity);

            // A building whose draw follows its recipe (Neutron Capture Slow/Fast) re-reads it now.
            var so = FindBuildingSOForEntity(entity);
            if (so != null && so.powerCostIsDynamic && _em.HasComponent<PowerConsumer>(entity))
            {
                var pc = _em.GetComponentData<PowerConsumer>(entity);
                pc.DrawEV = BuildingSO.PowerDrawFor(so, _em.GetComponentData<BuildingData>(entity).UpgradeLevel, recipe);
                _em.SetComponentData(entity, pc);
            }

            // Drives the tutorial's "set the Combiner to Proton" step (combiner_recipe_set).
            _hud?.NotifyBuildingRecipeSet();
        }

        private static void AddInspectorRow(VisualElement target, string text)
        {
            var lbl = new Label(text);
            lbl.AddToClassList("recipe-inputs");
            target?.Add(lbl);
        }

        // ── Stable buffer rows ────────────────────────────────────────────────

        private void AddBufferRows(List<(int itemId, int quantity)> rows)
        {
            if (rows.Count == 0)
            {
                AddInspectorRow(_inspectorStatic, "  (empty)");
                return;
            }
            foreach (var (itemId, quantity) in rows)
                AddInspectorRow(_inspectorStatic, $"  {ItemName(itemId)}  ×  {quantity}");
        }

        /// <summary>
        /// Rows for a building buffer whose count doesn't change as items flow: every expected item id
        /// first (in recipe order, quantity 0 when absent), then any unexpected items the buffer holds
        /// (e.g. leftovers from a previous recipe). Duplicate ids are merged into one row.
        /// </summary>
        internal static List<(int itemId, int quantity)> StableBufferRows(
            IReadOnlyList<int> expectedItemIds, IReadOnlyList<(int itemId, int quantity)> contents)
        {
            var rows = new List<(int itemId, int quantity)>();
            if (expectedItemIds != null)
                foreach (var id in expectedItemIds)
                    if (IndexOfItem(rows, id) < 0) rows.Add((id, 0));

            if (contents != null)
                foreach (var (id, qty) in contents)
                {
                    int i = IndexOfItem(rows, id);
                    if (i >= 0) rows[i] = (id, rows[i].quantity + qty);
                    else        rows.Add((id, qty));
                }
            return rows;
        }

        private static int IndexOfItem(List<(int itemId, int quantity)> rows, int itemId)
        {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].itemId == itemId) return i;
            return -1;
        }

        /// <summary>
        /// The input buffer section is shown whenever the building can hold inputs at all (a recipe with
        /// inputs, or a sink), not only while items happen to be sitting in it.
        /// </summary>
        internal static bool ShowInputBufferSection(int bufferLength, int recipeInputCount, bool isSink)
            => bufferLength > 0 || recipeInputCount > 0 || isSink;

        private List<int> RecipeInputIds(Entity entity)
        {
            var ids = new List<int>();
            if (!_em.HasBuffer<RecipeInputSlot>(entity)) return ids;
            var buf = _em.GetBuffer<RecipeInputSlot>(entity, isReadOnly: true);
            for (int i = 0; i < buf.Length; i++) ids.Add(buf[i].ItemID);
            return ids;
        }

        private List<int> RecipeOutputIds(Entity entity)
        {
            var ids = new List<int>();
            if (!_em.HasBuffer<RecipeOutputSlot>(entity)) return ids;
            var buf = _em.GetBuffer<RecipeOutputSlot>(entity, isReadOnly: true);
            for (int i = 0; i < buf.Length; i++) ids.Add(buf[i].ItemID);
            return ids;
        }

        // ── Reserved height for the static block ─────────────────────────────

        /// <summary>
        /// Backstop for anything whose row count still varies (wrapping status text, a sink's input
        /// list): the static block never shrinks below the tallest height it has reached for the
        /// building currently inspected, so the scrolls beneath it don't jump back up.
        /// </summary>
        private void OnStaticGeometryChanged(GeometryChangedEvent evt)
        {
            if (_inspectorEntity == Entity.Null) return;
            float h = evt.newRect.height;
            if (h <= _staticReservedHeight + 0.5f) return;
            _staticReservedHeight = h;
            _inspectorStatic.style.minHeight = h;
        }

        private void ResetStaticReservedHeight()
        {
            _staticReservedHeight = 0f;
            if (_inspectorStatic != null) _inspectorStatic.style.minHeight = StyleKeyword.Null;
        }

        // ── Craft progress bar ────────────────────────────────────────────────

        private static float CraftFraction(RecipeProcessData rp)
            => rp.CraftTime > 0f ? Mathf.Clamp01(rp.Progress / rp.CraftTime) : 0f;

        /// <summary>A harvester's progress toward its next item: its timer counts up to 1 / OutputRate. Pure (unit-tested).</summary>
        internal static float CollectorFraction(CollectorData c)
        {
            float rate = c.OutputRate > 0f ? c.OutputRate : 1f;
            return Mathf.Clamp01(c.Timer * rate);
        }

        private VisualElement AddCraftProgressBar(VisualElement parent, RecipeProcessData rp)
            => AddProgressBar(parent, CraftFraction(rp));

        private VisualElement AddProgressBar(VisualElement parent, float fraction)
        {
            var bar = new VisualElement();
            bar.style.height              = 10;
            bar.style.marginTop           = 2;
            bar.style.marginBottom        = 4;
            bar.style.backgroundColor     = new Color(0f, 0f, 0f, 0.35f);
            bar.style.borderTopLeftRadius = 3; bar.style.borderTopRightRadius = 3;
            bar.style.borderBottomLeftRadius = 3; bar.style.borderBottomRightRadius = 3;
            bar.style.overflow            = Overflow.Hidden;

            var fill = new VisualElement();
            fill.style.height          = Length.Percent(100);
            fill.style.backgroundColor = new Color(0.30f, 0.80f, 1.00f); // crest blue
            fill.style.width           = Length.Percent(fraction * 100f);
            bar.Add(fill);

            parent.Add(bar);
            return fill;
        }

        private void UpdateCraftProgressBar()
        {
            if (_craftProgressFill == null || !_ecsReady) return;
            if (_inspectorEntity == Entity.Null || !_em.Exists(_inspectorEntity)) return;
            float fraction;
            if (_em.HasComponent<CollectorData>(_inspectorEntity))
                fraction = CollectorFraction(_em.GetComponentData<CollectorData>(_inspectorEntity));
            else if (_em.HasComponent<RecipeProcessData>(_inspectorEntity))
                fraction = CraftFraction(_em.GetComponentData<RecipeProcessData>(_inspectorEntity));
            else return;
            _craftProgressFill.style.width = Length.Percent(fraction * 100f);
        }

        // ── Empty buffer into player inventory ────────────────────────────────

        private void AddEmptyBufferButton(string text, bool hasContents, System.Action onEmpty)
        {
            var btn = new Button { text = text };
            btn.AddToClassList("craft-btn");
            // Disabled rather than omitted so the layout doesn't change as the buffer fills/drains;
            // ListTapRouter swallows taps on a disabled button.
            btn.SetEnabled(hasContents);
            // _inspectorStatic is not a ScrollView, but the router is still required: the
            // button's own Clickable consumes the pointer-down regardless of any ScrollView.
            ListTapRouter.Register(_inspectorStatic, btn, onEmpty);
            _inspectorStatic.Add(btn);
        }

        private void EmptyOutputToInventory(Entity building)
        {
            if (!_ecsReady || _inventoryQuery.IsEmpty || !_em.Exists(building)) return;
            if (!_em.HasBuffer<BuildingOutputSlot>(building)) return;

            var outBuf = _em.GetBuffer<BuildingOutputSlot>(building);
            if (outBuf.Length == 0) return;

            var inv = _em.GetBuffer<InventorySlot>(_inventoryQuery.GetSingletonEntity());
            for (int i = 0; i < outBuf.Length; i++)
                if (outBuf[i].Quantity > 0)
                    SlotBufferUtils.AddToInventory(ref inv, outBuf[i].ItemID, outBuf[i].Quantity);
            outBuf.Clear();

            RefreshInspectorContent();
        }

        /// <summary>
        /// After a recipe change, inputs the new recipe doesn't use would sit in the buffer forever. Sell
        /// them at the surplus price (#312) and say so.
        /// </summary>
        private void SellInputsTheRecipeDoesNotUse(Entity entity)
        {
            if (!_em.HasBuffer<BuildingInputSlot>(entity) || _playerQuery.IsEmpty) return;
            long earned = SurplusSale.SellUnusedInputs(_em.GetBuffer<BuildingInputSlot>(entity),
                                                       _em.GetBuffer<RecipeInputSlot>(entity),
                                                       SurplusSale.DemonPrice, SurplusSale.CurrentFraction());
            if (earned <= 0) return;

            var progress = _playerQuery.GetSingleton<PlayerProgressData>();
            progress.BaseCurrency += earned;
            _playerQuery.SetSingleton(progress);
            InventoryPopupController.Notify("entropy", (int)System.Math.Min(earned, int.MaxValue));
        }

        /// <summary>
        /// After a recipe change, outputs made under the old recipe that the new one doesn't produce would leave
        /// on the belt (#341). Move them to the player's inventory instead and say so; outputs the new recipe
        /// also makes stay and keep shipping.
        /// </summary>
        private void MoveOutputsTheRecipeDoesNotMake(Entity entity)
        {
            if (!_ecsReady || _inventoryQuery.IsEmpty || !_em.Exists(entity)) return;
            if (!_em.HasBuffer<BuildingOutputSlot>(entity) || !_em.HasBuffer<RecipeOutputSlot>(entity)) return;

            var outBuf = _em.GetBuffer<BuildingOutputSlot>(entity);
            if (outBuf.Length == 0) return;

            var recipeOut = _em.GetBuffer<RecipeOutputSlot>(entity);
            var leaving = new System.Collections.Generic.List<(int id, int qty)>();
            for (int i = 0; i < outBuf.Length; i++)
                if (outBuf[i].Quantity > 0 && !RecipeSwitch.RecipeMakes(recipeOut, outBuf[i].ItemID))
                    leaving.Add((outBuf[i].ItemID, outBuf[i].Quantity));

            var inv = _em.GetBuffer<InventorySlot>(_inventoryQuery.GetSingletonEntity());
            int moved = RecipeSwitch.MoveUnproducedOutputsToInventory(outBuf, recipeOut, ref inv);
            if (moved <= 0) return;

            foreach (var (id, qty) in leaving)
                InventoryPopupController.Notify(ItemName(id), qty);
        }

        private void EmptyInputToInventory(Entity building)
        {
            if (!_ecsReady || _inventoryQuery.IsEmpty || !_em.Exists(building)) return;
            if (!_em.HasBuffer<BuildingInputSlot>(building)) return;

            var inBuf = _em.GetBuffer<BuildingInputSlot>(building);
            if (inBuf.Length == 0) return;

            var inv = _em.GetBuffer<InventorySlot>(_inventoryQuery.GetSingletonEntity());
            for (int i = 0; i < inBuf.Length; i++)
                if (inBuf[i].Quantity > 0)
                    SlotBufferUtils.AddToInventory(ref inv, inBuf[i].ItemID, inBuf[i].Quantity);
            inBuf.Clear();

            RefreshInspectorContent();
        }

        private static string ItemName(int itemID)
        {
            var item = ItemDatabase.Instance?.Get(itemID);
            return item?.displayName ?? item?.symbol ?? $"Item {itemID}";
        }
    }
}
