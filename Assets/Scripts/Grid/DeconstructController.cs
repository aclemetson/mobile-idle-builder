using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Manages deconstruct mode.
    ///
    /// Flow:
    ///   1. HUDController calls BeginDeconstructMode() from the Buildings panel.
    ///   2. While IsDeconstructing, tapping any occupied cell asks to confirm (ConfirmPrompt), then
    ///      destroys the building or conveyor segment there. Mode stays active for repeated removals.
    ///      A removed building refunds part of the entropy spent on it (DemolishRefund); the grid's
    ///      only Maxwell's Demon cannot be removed.
    ///   3. CancelDeconstructMode() exits (cancel button or Escape).
    ///
    /// When a conveyor segment is removed its chain neighbors are re-linked:
    ///   - The predecessor becomes a tail (NextSegment = Null).
    ///   - The successor becomes a chain head (PrevSegment = Null, IsChainHead = true).
    /// </summary>
    public class DeconstructController : MonoBehaviour
    {
        [Header("Scene references")]
        [SerializeField] private GridRenderer       gridRenderer;
        [SerializeField] private BuildingVisualizer buildingVisualizer;
        [SerializeField] private ConveyorVisualizer conveyorVisualizer;
        [SerializeField] private ConveyorPlacer     conveyorPlacer;
        [SerializeField] private UIDocument         hudDocument;

        public bool IsDeconstructing { get; private set; }
        public event Action<bool> OnDeconstructingChanged;

        private EntityManager _em;
        private EntityQuery   _buildingQuery;
        private EntityQuery   _conveyorQuery;
        private EntityQuery   _sinkQuery;
        private EntityQuery   _progressQuery;
        private EntityQuery   _prestigeQuery;
        private bool          _ecsReady;
        private Vector2Int    _hoveredCell = new(-1, -1);

        // ================================================================
        // Unity lifecycle
        // ================================================================

        void Awake()
        {
            if (gridRenderer       == null) gridRenderer       = FindAnyObjectByType<GridRenderer>();
            if (buildingVisualizer == null) buildingVisualizer = FindAnyObjectByType<BuildingVisualizer>();
            if (conveyorVisualizer == null) conveyorVisualizer = FindAnyObjectByType<ConveyorVisualizer>();
            if (conveyorPlacer     == null) conveyorPlacer     = FindAnyObjectByType<ConveyorPlacer>();
            if (hudDocument        == null) hudDocument        = FindAnyObjectByType<UIDocument>();
        }

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            _em = world.EntityManager;
            _buildingQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingData>(),
                ComponentType.ReadOnly<GridPosition>()
            );
            _conveyorQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<ConveyorSegmentData>(),
                ComponentType.ReadOnly<GridPosition>()
            );
            _sinkQuery     = _em.CreateEntityQuery(ComponentType.ReadOnly<EntropySinkTag>(),
                                                   ComponentType.ReadOnly<BuildingData>());
            _progressQuery = _em.CreateEntityQuery(ComponentType.ReadWrite<PlayerProgressData>());
            _prestigeQuery = _em.CreateEntityQuery(ComponentType.ReadOnly<PrestigeData>());
            _ecsReady = true;
        }

        void OnDestroy()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated && _ecsReady)
            {
                _buildingQuery.Dispose();
                _conveyorQuery.Dispose();
                _sinkQuery.Dispose();
                _progressQuery.Dispose();
                _prestigeQuery.Dispose();
            }
        }

        // ================================================================
        // Public API
        // ================================================================

        public void BeginDeconstructMode()
        {
            IsDeconstructing = true;
            OnDeconstructingChanged?.Invoke(true);
        }

        public void CancelDeconstructMode()
        {
            ConfirmPrompt.Dismiss();
            gridRenderer?.ClearDeconstructHover();
            _hoveredCell = new(-1, -1);
            IsDeconstructing = false;
            OnDeconstructingChanged?.Invoke(false);
        }

        // ================================================================
        // Update loop
        // ================================================================

        void Update()
        {
            if (!IsDeconstructing) return;

            if (InputUtils.WasCancelPressed())
            {
                CancelDeconstructMode();
                return;
            }

            // Waiting on "Remove?": keep the target highlighted and ignore the grid.
            if (ConfirmPrompt.IsOpen) return;

            Vector2 screenPos = InputUtils.GetPointerPosition();
            bool    overUI    = IsPointerOverUI(screenPos);

            // Update hover highlight every frame based on pointer position
            UpdateHoverHighlight(overUI || gridRenderer == null
                ? new Vector2Int(-1, -1)
                : WorldToCell(screenPos));

            if (!InputUtils.WasPointerPressed() || overUI) return;

            if (gridRenderer == null) return;
            Vector2Int cell = WorldToCell(screenPos);
            if (!gridRenderer.IsInBounds(cell.x, cell.y)) return;
            if (GridOccupancy.Instance == null || !GridOccupancy.Instance.IsOccupied(cell.x, cell.y)) return;

            TryDeconstructAt(cell.x, cell.y);
        }

        private void UpdateHoverHighlight(Vector2Int cell)
        {
            if (cell == _hoveredCell) return;

            gridRenderer?.ClearDeconstructHover();
            _hoveredCell = cell;

            if (gridRenderer == null) return;
            if (!gridRenderer.IsInBounds(cell.x, cell.y)) return;
            if (GridOccupancy.Instance == null || !GridOccupancy.Instance.IsOccupied(cell.x, cell.y)) return;

            var (fx, fy, fw, fh) = FindBuildingFootprintAt(cell.x, cell.y);
            gridRenderer.SetDeconstructHover(fx, fy, fw, fh);
        }

        /// <summary>
        /// Returns the origin and size of the building whose footprint contains (x, y).
        /// Falls back to 1×1 for conveyors or if ECS isn't ready.
        /// </summary>
        private (int fx, int fy, int fw, int fh) FindBuildingFootprintAt(int x, int y)
        {
            if (!_ecsReady) return (x, y, 1, 1);

            var entities = _buildingQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var e   = entities[i];
                var pos = _em.GetComponentData<GridPosition>(e);
                int fx = pos.Cell.x, fy = pos.Cell.y;
                int fw = 1, fh = 1;

                if (_em.HasComponent<BuildingFootprint>(e))
                {
                    var fp = _em.GetComponentData<BuildingFootprint>(e);
                    fw = fp.Width;
                    fh = fp.Height;
                }

                if (x >= fx && x < fx + fw && y >= fy && y < fy + fh)
                {
                    entities.Dispose();
                    return (fx, fy, fw, fh);
                }
            }
            entities.Dispose();

            return (x, y, 1, 1); // conveyor or unknown — 1×1
        }

        private static bool IsPointerOverUI(Vector2 screenPos) => UIInputBlocker.IsPointerOverUI(screenPos);

        // ================================================================
        // Private helpers
        // ================================================================

        private void TryDeconstructAt(int x, int y)
        {
            if (!_ecsReady) return;

            // ---- Buildings ----
            var bldgEntities = _buildingQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < bldgEntities.Length; i++)
            {
                var e = bldgEntities[i];
                var (fx, fy, fw, fh) = Footprint(e);
                if (x < fx || x >= fx + fw || y < fy || y >= fy + fh) continue;
                bldgEntities.Dispose();
                ConfirmBuildingRemoval(e);
                return;
            }
            bldgEntities.Dispose();

            // ---- Conveyor segments (shared removal logic) ----
            if (!IsConveyorAt(x, y)) return;
            ConfirmPrompt.Show("Remove belt tile?",
                "Removes this belt tile. Belts are free, so there is nothing to refund.",
                "Remove",
                () => RemoveConveyorAt(x, y),
                ClearHover);
        }

        private void ConfirmBuildingRemoval(Entity e)
        {
            var bd = _em.GetComponentData<BuildingData>(e);
            var so = FindBuildingSO(bd.BuildingType);
            string name = so != null && !string.IsNullOrEmpty(so.displayName) ? so.displayName : "this building";

            if (DemolishRefund.IsProtected(_em.HasComponent<EntropySinkTag>(e), _sinkQuery.CalculateEntityCount()))
            {
                InventoryPopupController.NotifyWarning(
                    $"{name} can't be removed: it's your only way to turn items into entropy.");
                ClearHover();
                return;
            }

            long  spent  = DemolishRefund.EntropySpent(so, bd.UpgradeLevel, bd.StorageUpgradeLevel,
                                                       bd.InputUpgradeLevel, CostReduction());
            float frac   = DemolishRefund.Fraction(
                PersistentUpgradeService.Instance?.GetEffect(UpgradeEffectType.DemolishRefund) ?? 0f);
            long  refund = DemolishRefund.Amount(spent, frac);

            string message = refund > 0
                ? $"You get back {refund:N0} entropy ({Mathf.RoundToInt(frac * 100f)}% of the {spent:N0} spent on it)."
                : "Nothing was spent on it, so there is nothing to refund.";
            ConfirmPrompt.Show($"Remove {name}?", message, "Remove",
                () => RemoveBuilding(e, spent, refund), ClearHover);
        }

        private void RemoveBuilding(Entity e, long spent, long refund)
        {
            if (!_ecsReady || !_em.Exists(e)) return; // gone while the prompt was open
            var (fx, fy, fw, fh) = Footprint(e);

            _em.DestroyEntity(e);
            GridOccupancy.Instance.ReleaseRect(fx, fy, fw, fh);
            buildingVisualizer?.RemoveBuilding(fx, fy);
            ApplyRefund(spent, refund);
            if (refund > 0) InventoryPopupController.Notify("entropy", (int)System.Math.Min(refund, int.MaxValue));
            ClearHover();
            SaveManager.Instance?.SaveLocal();
            GameLogger.Develop($"[Deconstruct] Removed building at ({fx},{fy}), refund {refund} of {spent}.");
        }

        /// <summary>
        /// Gives back the refund and takes the building's whole cost out of TotalEntropySpent, so net
        /// worth (BaseCurrency + TotalEntropySpent) drops by exactly the unrefunded part: placing and
        /// removing can never raise it.
        /// </summary>
        private void ApplyRefund(long spent, long refund)
        {
            if (_progressQuery.IsEmpty) return;
            var entity   = _progressQuery.GetSingletonEntity();
            var progress = _em.GetComponentData<PlayerProgressData>(entity);
            progress.BaseCurrency      += refund;
            progress.TotalEntropySpent  = System.Math.Max(0L, progress.TotalEntropySpent - spent);
            _em.SetComponentData(entity, progress);
        }

        private void RemoveConveyorAt(int x, int y)
        {
            if (conveyorPlacer != null && conveyorPlacer.RemoveSegmentAt(x, y))
            {
                SaveManager.Instance?.SaveLocal();
                GameLogger.Develop($"[Deconstruct] Removed conveyor segment at ({x},{y}).");
            }
            ClearHover();
        }

        private bool IsConveyorAt(int x, int y)
        {
            using var cells = _conveyorQuery.ToComponentDataArray<GridPosition>(Allocator.Temp);
            foreach (var p in cells)
                if (p.Cell.x == x && p.Cell.y == y) return true;
            return false;
        }

        private (int fx, int fy, int fw, int fh) Footprint(Entity e)
        {
            var pos = _em.GetComponentData<GridPosition>(e);
            int fw = 1, fh = 1;
            if (_em.HasComponent<BuildingFootprint>(e))
            {
                var fp = _em.GetComponentData<BuildingFootprint>(e);
                fw = fp.Width;
                fh = fp.Height;
            }
            return (pos.Cell.x, pos.Cell.y, fw, fh);
        }

        private float CostReduction() =>
            _prestigeQuery.IsEmpty ? 0f
                : _em.GetComponentData<PrestigeData>(_prestigeQuery.GetSingletonEntity()).CostReduction;

        private void ClearHover()
        {
            gridRenderer?.ClearDeconstructHover();
            _hoveredCell = new(-1, -1);
        }

        private static BuildingSO FindBuildingSO(int buildingId)
        {
            var db = Resources.Load<BuildingDatabaseSO>("BuildingDatabase");
            if (db == null || db.allBuildings == null) return null;
            foreach (var b in db.allBuildings)
                if (b != null && b.buildingId == buildingId) return b;
            return null;
        }

        private Vector2Int WorldToCell(Vector2 screenPos)
        {
            var ray = Camera.main.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0));
            if (Mathf.Abs(ray.direction.y) < 0.0001f) return new(-1, -1);

            float t     = -ray.origin.y / ray.direction.y;
            var   world = ray.origin + ray.direction * t;

            return GridRenderer.WorldToCell(world, gridRenderer.CellSize);
        }
    }
}
