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
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated && _ecsReady)
            {
                _buildingQuery.Dispose();
                _tutorialQuery.Dispose();
            }
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
            if (!_ecsReady || gridRenderer == null) return false;

            Vector2Int cell = WorldToCell(screenPos);
            if (!gridRenderer.IsInBounds(cell.x, cell.y)) return false;
            if (GridOccupancy.Instance == null || !GridOccupancy.Instance.IsOccupied(cell.x, cell.y)) return false;

            // Tutorial gating: read restriction from current step definition
            var buildingGate = BuildingInteractionGate.None;
            if (!_tutorialQuery.IsEmpty)
            {
                var tutState = _tutorialQuery.GetSingleton<TutorialStateData>();
                var flow = TutorialFlowSO.Current;
                if (tutState.IsActive && flow != null &&
                    tutState.CurrentStepIndex < flow.steps.Length)
                    buildingGate = flow.steps[tutState.CurrentStepIndex].onEnter?.buildingInteractionGate
                                   ?? BuildingInteractionGate.None;
            }

            if (buildingGate == BuildingInteractionGate.BlockAll)
            {
                ToastService.Instance?.Post("building");
                return false;
            }

            var entities = _buildingQuery.ToEntityArray(Allocator.Temp);
            Entity found = Entity.Null;

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

                if (cell.x >= fx && cell.x < fx + fw && cell.y >= fy && cell.y < fy + fh)
                {
                    found = e;
                    break;
                }
            }
            entities.Dispose();

            if (found == Entity.Null) return false;

            // EntropySinkOnly: only Maxwell's Demon may be opened
            if (buildingGate == BuildingInteractionGate.EntropySinkOnly &&
                !_em.HasComponent<EntropySinkTag>(found))
                return false;

            // Maxwell's Demon gets its own interaction panel instead of the generic inspector.
            // ARCH has no physical form, so there is no proximity requirement — open immediately.
            if (_em.HasComponent<EntropySinkTag>(found))
            {
                HasSelection = true;
                maxwellsDemon?.Open();
                return true;
            }

            HasSelection = true;

            hudController?.ShowBuildingInspector(found, GetBuildingDisplayName(found));
            return true;
        }

        /// <summary>Clears the current building selection.</summary>
        public void ClearSelection()
        {
            if (!HasSelection) return;
            HasSelection = false;
            hudController?.HideBuildingInspector();
            maxwellsDemon?.Close();
        }

        // ================================================================
        // Private helpers
        // ================================================================

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

        private Vector2Int WorldToCell(Vector2 screenPos)
        {
            if (Camera.main == null) return new(-1, -1);
            var ray = Camera.main.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0));
            if (Mathf.Abs(ray.direction.y) < 0.0001f) return new(-1, -1);
            float t   = -ray.origin.y / ray.direction.y;
            var world = ray.origin + ray.direction * t;
            return new(
                Mathf.FloorToInt(world.x / gridRenderer.CellSize),
                Mathf.FloorToInt(world.z / gridRenderer.CellSize)
            );
        }

    }
}
