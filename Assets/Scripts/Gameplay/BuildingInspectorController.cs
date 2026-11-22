using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Handles tapping placed buildings to open the building inspector panel.
    ///
    /// Wire into PlayerInputRouter: if TrySelectBuildingAt() returns true the router
    /// skips character movement so the player selects a building instead of moving.
    /// </summary>
    public class BuildingInspectorController : MonoBehaviour
    {
        [SerializeField] private GridRenderer                gridRenderer;
        [SerializeField] private HUDController               hudController;
        [SerializeField] private MaxwellsDemonController     maxwellsDemon;
        [SerializeField] private BuildingPlacementController placementController;
        [SerializeField] private ConveyorPlacementController conveyorController;
        [SerializeField] private DeconstructController        deconstructController;
        [SerializeField] private BuildingVisualizer           buildingVisualizer;

        public bool HasSelection { get; private set; }

        private EntityManager _em;
        private EntityQuery   _buildingQuery;
        private EntityQuery   _tutorialQuery;
        private bool          _ecsReady;

        // ================================================================
        // Unity lifecycle
        // ================================================================

        void Start()
        {
            if (gridRenderer         == null) gridRenderer         = FindAnyObjectByType<GridRenderer>();
            if (buildingVisualizer   == null) buildingVisualizer   = FindAnyObjectByType<BuildingVisualizer>();
            if (conveyorController   == null) conveyorController   = FindAnyObjectByType<ConveyorPlacementController>();
            if (deconstructController == null) deconstructController = FindAnyObjectByType<DeconstructController>();
            if (maxwellsDemon        == null) maxwellsDemon        = FindAnyObjectByType<MaxwellsDemonController>();

            // The demon panel can be dismissed by its own close button (bypassing ClearSelection),
            // so mirror its closed state to drop the grid selection highlight.
            if (maxwellsDemon != null) maxwellsDemon.OnClosed += OnDemonClosed;

            // The same goes for the inspector's X button, back navigation, and a demolished building.
            if (hudController != null) hudController.BuildingInspectorHidden += OnSelectionDismissed;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            _em = world.EntityManager;
            _buildingQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingData>(),
                ComponentType.ReadOnly<GridPosition>()
            );
            _tutorialQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<TutorialStateData>()
            );
            _ecsReady = true;
        }

        void OnDestroy()
        {
            if (maxwellsDemon != null) maxwellsDemon.OnClosed -= OnDemonClosed;
            if (hudController != null) hudController.BuildingInspectorHidden -= OnSelectionDismissed;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated && _ecsReady)
            {
                _buildingQuery.Dispose();
                _tutorialQuery.Dispose();
            }
        }

        // Keep the grid selection highlight in sync when a panel closes itself.
        private void OnDemonClosed() => OnSelectionDismissed();

        private void OnSelectionDismissed()
        {
            HasSelection = false;
            buildingVisualizer?.DeselectBuilding();
        }

        void Update()
        {
            if (buildingVisualizer == null || gridRenderer == null) return;

            // Suppress hover while any special placement/deconstruct mode is active
            bool anyModeActive =
                (placementController   != null && placementController.IsPlacing)          ||
                (conveyorController    != null && conveyorController.IsPlacing)            ||
                (deconstructController != null && deconstructController.IsDeconstructing);

            if (anyModeActive) { buildingVisualizer.ClearHover(); return; }

            Vector2Int cell = WorldToCell(InputUtils.GetPointerPosition());

            bool overBuilding =
                gridRenderer.IsInBounds(cell.x, cell.y)                              &&
                GridOccupancy.Instance != null                                        &&
                GridOccupancy.Instance.IsOccupied(cell.x, cell.y)                    &&
                !GridOccupancy.Instance.IsConveyorCell(cell.x, cell.y);

            if (overBuilding) buildingVisualizer.SetHover(cell.x, cell.y);
            else              buildingVisualizer.ClearHover();
        }

        // ================================================================
        // Public API
        // ================================================================

        /// <summary>
        /// Converts <paramref name="screenPos"/> to a grid cell and selects the
        /// building whose footprint contains that cell.
        /// Returns true if a building was found and the inspector was opened.
        /// </summary>
        public bool TrySelectBuildingAt(Vector2 screenPos)
        {
            if (!_ecsReady || gridRenderer == null)
            {
                GameLogger.Develop($"[Inspector] TrySelectBuildingAt early-exit: ecsReady={_ecsReady} gridRenderer={(gridRenderer == null ? "NULL" : "ok")}");
                return false;
            }

            // Hit-testing is in world space (FindBuildingAtWorld), which avoids cell-rounding mismatches.
            if (!ScreenToWorldGround(screenPos, out Vector3 worldPos))
            {
                GameLogger.Develop("[Inspector] TrySelectBuildingAt early-exit: ScreenToWorldGround failed (no camera?)");
                return false;
            }

            Vector2Int cell = WorldToCell(screenPos); // used only for IsInBounds fast-exit
            GameLogger.Develop($"[Inspector] worldPos=({worldPos.x:F2},{worldPos.z:F2}) cell=({cell.x},{cell.y}) inBounds={gridRenderer.IsInBounds(cell.x, cell.y)}");
            if (!gridRenderer.IsInBounds(cell.x, cell.y)) return false;

            var buildingGate = CurrentGate();
            GameLogger.Develop($"[Inspector] buildingGate={buildingGate}");

            if (buildingGate == BuildingInteractionGate.BlockAll)
            {
                // A tap on a bare field cell belongs to field collection, not the building system —
                // don't show the "no building interaction" warning for it. Still fires for real
                // buildings (e.g. a Harvester on a field cell, or Maxwell's Demon).
                bool bareField =
                    FieldGenerator.GetFieldInstanceAt(cell.x, cell.y) != null &&
                    (GridOccupancy.Instance == null || !GridOccupancy.Instance.IsOccupied(cell.x, cell.y));
                if (bareField) return false;

                ToastService.Instance?.Post("building");
                return false;
            }

            Entity found = FindBuildingAtWorld(worldPos);
            if (found == Entity.Null)
            {
                GameLogger.Develop("[Inspector] No entity found — returning false");
                return false;
            }

            // EntropySinkOnly: only Maxwell's Demon may be opened
            if (buildingGate == BuildingInteractionGate.EntropySinkOnly &&
                !_em.HasComponent<EntropySinkTag>(found))
            {
                GameLogger.Develop("[Inspector] Blocked by EntropySinkOnly gate");
                InventoryPopupController.NotifyWarning(GateBlockedMessage("Maxwell's Demon"));
                return false;
            }

            // AtomicAssemblerOnly: only the Atom Generator (building type 4) may be opened
            if (buildingGate == BuildingInteractionGate.AtomicAssemblerOnly)
            {
                var bd = _em.GetComponentData<BuildingData>(found);
                if (bd.BuildingType != 4)
                {
                    GameLogger.Develop($"[Inspector] Blocked by AtomicAssemblerOnly gate (buildingType={bd.BuildingType})");
                    InventoryPopupController.NotifyWarning(GateBlockedMessage("the Atom Generator"));
                    return false;
                }
            }

            // Light up the grid under the selected building.
            var foundPos = _em.GetComponentData<GridPosition>(found);
            buildingVisualizer?.SelectBuilding(foundPos.Cell.x, foundPos.Cell.y);

            // Maxwell's Demon gets its own interaction panel instead of the generic inspector.
            if (_em.HasComponent<DemonTag>(found))
            {
                HasSelection = true;
                maxwellsDemon?.Open();
                return true;
            }

            HasSelection = true;
            GameLogger.Develop($"[Inspector] Found entity — calling ShowBuildingInspector. hudController={(hudController == null ? "NULL" : "ok")}");
            hudController?.ShowBuildingInspector(found, GetBuildingDisplayName(found));
            return true;
        }

        /// <summary>Clears the current building selection.</summary>
        public void ClearSelection()
        {
            if (!HasSelection) return;
            HasSelection = false;
            buildingVisualizer?.DeselectBuilding();
            hudController?.HideBuildingInspector();
            maxwellsDemon?.Close();
        }

        /// <summary>
        /// The building under <paramref name="screenPos"/> if a press-and-hold may put it into edit mode.
        /// Not while a tutorial step restricts building interaction, and never Maxwell's Demon: it is baked
        /// from the SubScene and its saved cell is never restored, so a moved Demon would jump back on load.
        /// </summary>
        public bool TryGetEditableBuildingAt(Vector2 screenPos, out Entity found)
        {
            found = Entity.Null;
            if (!_ecsReady || gridRenderer == null) return false;
            if (CurrentGate() != BuildingInteractionGate.None) return false;
            if (!ScreenToWorldGround(screenPos, out Vector3 worldPos)) return false;

            var e = FindBuildingAtWorld(worldPos);
            if (e == Entity.Null || _em.HasComponent<DemonTag>(e)) return false;
            found = e;
            return true;
        }

        // ================================================================
        // Private helpers
        // ================================================================

        /// <summary>The current tutorial step's building-interaction restriction, or None.</summary>
        private BuildingInteractionGate CurrentGate()
        {
            if (_tutorialQuery.IsEmpty) return BuildingInteractionGate.None;
            var tutState = _tutorialQuery.GetSingleton<TutorialStateData>();
            var flow     = TutorialFlowSO.Current;
            if (!tutState.IsActive || flow == null || tutState.CurrentStepIndex >= flow.steps.Length)
                return BuildingInteractionGate.None;
            return flow.steps[tutState.CurrentStepIndex].onEnter?.buildingInteractionGate
                   ?? BuildingInteractionGate.None;
        }

        /// <summary>
        /// The building whose footprint covers a ground-plane point, or Entity.Null. Tested in world space,
        /// which avoids cell-rounding convention mismatches: tiles are centred at (cell * cellSize), so a
        /// building at (fx, fy) of size (fw, fh) covers X [fx*cs - cs/2, (fx+fw)*cs - cs/2) and likewise Z.
        /// </summary>
        private Entity FindBuildingAtWorld(Vector3 worldPos)
        {
            float cs     = gridRenderer.CellSize;
            float halfCs = cs * 0.5f;

            var entities = _buildingQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var e   = entities[i];
                    var pos = _em.GetComponentData<GridPosition>(e);
                    int fx = pos.Cell.x, fy = pos.Cell.y, fw = 1, fh = 1;

                    if (_em.HasComponent<BuildingFootprint>(e))
                    {
                        var fp = _em.GetComponentData<BuildingFootprint>(e);
                        fw = fp.Width; fh = fp.Height;
                    }

                    if (worldPos.x >= fx * cs - halfCs && worldPos.x < (fx + fw) * cs - halfCs &&
                        worldPos.z >= fy * cs - halfCs && worldPos.z < (fy + fh) * cs - halfCs)
                        return e;
                }
            }
            finally { entities.Dispose(); }
            return Entity.Null;
        }

        private string GetBuildingDisplayName(Entity e)
        {
            if (!_ecsReady || !_em.HasComponent<BuildingData>(e)) return "Building";
            int buildingType = _em.GetComponentData<BuildingData>(e).BuildingType;

            if (placementController?.availableBuildings != null)
            {
                foreach (var entry in placementController.availableBuildings)
                {
                    if (entry.building != null && entry.building.buildingId == buildingType)
                        return entry.building.displayName;
                }
            }

            return $"Building #{buildingType}";
        }

        private bool ScreenToWorldGround(Vector2 screenPos, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            if (Camera.main == null) return false;
            var ray = Camera.main.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0));
            if (Mathf.Abs(ray.direction.y) < 0.0001f) return false;
            float t = -ray.origin.y / ray.direction.y;
            worldPos = ray.origin + ray.direction * t;
            return true;
        }

        private Vector2Int WorldToCell(Vector2 screenPos)
        {
            if (!ScreenToWorldGround(screenPos, out Vector3 world)) return new(-1, -1);
            return GridRenderer.WorldToCell(world, gridRenderer.CellSize);
        }

        /// <summary>Shown when a tutorial interaction gate rejects a tap, so the block isn't silent (#187).</summary>
        internal static string GateBlockedMessage(string allowedBuilding)
            => $"Finish the current tutorial step first: tap {allowedBuilding}.";

    }
}
