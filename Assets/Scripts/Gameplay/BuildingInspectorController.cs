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
        [SerializeField] private CharacterMover               characterMover;
        [SerializeField] private IsometricCameraFollow        cameraFollow;

        /// <summary>World-unit radius within which the player can interact with a building.</summary>
        [SerializeField] private float interactionRange = 3f;

        public bool HasSelection { get; private set; }

        /// <summary>An EntropySinkTag building the player is walking toward before opening.</summary>
        private Entity _pendingOpenEntity = Entity.Null;

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
            if (characterMover       == null) characterMover       = FindAnyObjectByType<CharacterMover>();
            if (cameraFollow         == null) cameraFollow         = FindAnyObjectByType<IsometricCameraFollow>();

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
            // Check whether the player has walked close enough to open a pending building
            if (_ecsReady && _pendingOpenEntity != Entity.Null)
            {
                if (!_em.Exists(_pendingOpenEntity))
                {
                    _pendingOpenEntity = Entity.Null;
                }
                else if (IsPlayerInRange(_pendingOpenEntity))
                {
                    HasSelection = true;
                    maxwellsDemon?.Open();
                    _pendingOpenEntity = Entity.Null;
                }
            }

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
                return false;

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
            // The player must be close enough; if not, start walking toward it.
            if (_em.HasComponent<EntropySinkTag>(found))
            {
                if (IsPlayerInRange(found))
                {
                    _pendingOpenEntity = Entity.Null;
                    HasSelection = true;
                    maxwellsDemon?.Open();
                }
                else
                {
                    _pendingOpenEntity = found;
                    MoveCharacterToBuilding(found);
                    // Resume camera follow so the player can see their character walk to the building.
                    cameraFollow?.ResumeFollow();
                }
                return true;
            }

            HasSelection = true;

            hudController?.ShowBuildingInspector(found, GetBuildingDisplayName(found));
            return true;
        }

        /// <summary>Clears the current selection and cancels any pending building approach.</summary>
        public void ClearSelection()
        {
            _pendingOpenEntity = Entity.Null;
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

        /// <summary>Returns the world-space XZ centre of the building entity's grid cell.</summary>
        private Vector3 BuildingWorldCenter(Entity e)
        {
            float cs  = gridRenderer != null ? gridRenderer.CellSize : 1f;
            var   pos = _em.GetComponentData<GridPosition>(e);
            return new Vector3((pos.Cell.x + 0.5f) * cs, 0f, (pos.Cell.y + 0.5f) * cs);
        }

        /// <summary>True when the character is within <see cref="interactionRange"/> of the building.</summary>
        private bool IsPlayerInRange(Entity e)
        {
            if (characterMover == null || !_em.HasComponent<GridPosition>(e)) return true;
            var  center   = BuildingWorldCenter(e);
            var  charPos  = characterMover.transform.position;
            float dx = charPos.x - center.x;
            float dz = charPos.z - center.z;
            return (dx * dx + dz * dz) <= interactionRange * interactionRange;
        }

        /// <summary>Tells the character to walk toward the centre of the building's grid cell.</summary>
        private void MoveCharacterToBuilding(Entity e)
        {
            if (characterMover == null || !_em.HasComponent<GridPosition>(e)) return;
            characterMover.SetMoveTarget(BuildingWorldCenter(e));
        }
    }
}
