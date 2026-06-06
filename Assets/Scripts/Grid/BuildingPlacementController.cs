using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Manages building placement mode.
    ///
    /// Flow:
    ///   1. HUDController calls BeginPlacement(entry) when player taps a building card.
    ///   2. While IsPlacing, a ghost tile follows the pointer.
    ///      Cyan = valid cell.  Red = occupied, out of bounds, or wrong field type.
    ///   3. Player taps a valid cell → building is placed (with optional output-selector for fields).
    ///   4. CancelPlacement() exits at any point (cancel button or Escape).
    ///
    /// Port-layout buildings (those with BuildingSO.ports defined):
    ///   R  /  Rotate button  →  rotate 90° CW
    ///   F  /  Flip button    →  mirror chirality (horizontal flip)
    ///
    /// Legacy field-collector buildings (no ports, MustBeOnField):
    ///   R  /  Rotate button  →  cycle output direction (original behaviour)
    /// </summary>
    public class BuildingPlacementController : MonoBehaviour
    {
        [Serializable]
        public struct BuildingEntry
        {
            public BuildingSO building;
            public RecipeSO   defaultRecipe;
        }

        [Header("Available buildings (shown in the selection panel)")]
        public BuildingEntry[] availableBuildings;

        [Header("Scene references")]
        [SerializeField] private GridRenderer        gridRenderer;
        [SerializeField] private BuildingPlacer      buildingPlacer;
        [SerializeField] private BuildingVisualizer  buildingVisualizer;
        [SerializeField] private CameraController    cameraController;

        public bool IsPlacing { get; private set; }

        // ---- Port-layout state ----
        private int  _rotation; // 0-3, applied CW
        private bool _flipped;

        /// <summary>True if the pending building defines its own port layout.</summary>
        public bool HasPortLayout =>
            IsPlacing && _pending.building?.ports != null && _pending.building.ports.Length > 0;

        /// <summary>True when the rotate button / R key should be shown (port layout OR legacy field collector).</summary>
        public bool CanRotate =>
            IsPlacing && (HasPortLayout ||
                          (_pending.building != null &&
                           _pending.building.placementRule == PlacementRule.MustBeOnField));

        /// <summary>True when the flip button / F key should be shown.</summary>
        public bool CanFlip => HasPortLayout;

        // ---- Legacy field-collector state (no ports defined) ----
        /// <summary>True while placing a legacy field collector that has no port layout.</summary>
        public bool RequiresOutputDirection =>
            IsPlacing && !HasPortLayout && _pending.building != null &&
            _pending.building.placementRule == PlacementRule.MustBeOnField;

        // ================================================================
        // Public rotation / flip API  (called by HUD buttons and R / F keys)
        // ================================================================

        public void Rotate()
        {
            if (HasPortLayout)
            {
                _rotation = (_rotation + 1) % 4;
                // Force ghost refresh
                _lastGhostCell = new(-1, -1);
            }
            else
            {
                RotateOutputDirection();
            }
        }

        /// <summary>Kept for backward compatibility — the rotate button wired before port system existed.</summary>
        public void RotateOutputDirection()
        {
            if (!RequiresOutputDirection) return;
            _outputDirection = (OutputDirection)(((int)_outputDirection + 1) % 4);
            _ghostArrow?.SetDirection(_outputDirection);
        }

        public void Flip()
        {
            if (!HasPortLayout) return;
            _flipped = !_flipped;
            _lastGhostCell = new(-1, -1);
        }

        // ================================================================
        // Private state
        // ================================================================

        private BuildingEntry   _pending;
        private Vector2Int      _lastGhostCell  = new(-1, -1);
        private Vector2Int      _selectionCell;
        private bool            _awaitingSelection;

        // Legacy single-output arrow (field collectors)
        private OutputDirection _outputDirection;
        private GameObject      _ghostArrowGO;
        private OutputArrow     _ghostArrow;

        // Port-layout ghost arrows
        private readonly List<GameObject> _ghostPortArrows = new();

        /// <summary>Raised when placement mode begins (true) or ends (false).</summary>
        public event Action<bool> OnPlacingChanged;

        /// <summary>Raised after a building is successfully placed. Carries the placed entry.</summary>
        public event Action<BuildingEntry> OnBuildingPlaced;

        /// <summary>
        /// Raised when the player taps a field that has multiple possible outputs.
        /// Call SelectOutput() with the chosen recipe to resume placement.
        /// </summary>
        public event Action<List<RecipeSO>> OnOutputSelectionRequired;

        // ================================================================
        // Lifecycle
        // ================================================================

        void Awake()
        {
            if (cameraController == null)
                cameraController = FindAnyObjectByType<CameraController>();
        }

        // ================================================================
        // Public API
        // ================================================================

        public void BeginPlacement(BuildingEntry entry)
        {
            _pending           = entry;
            _lastGhostCell     = new(-1, -1);
            _awaitingSelection = false;
            _outputDirection   = OutputDirection.North;
            _rotation          = 0;
            _flipped           = false;
            IsPlacing          = true;
            cameraController?.SetPanLocked(true);

            DestroyGhostArrow();
            DestroyGhostPortArrows();

            // Create legacy ghost arrow for field collectors that have no port layout
            if (!HasPortLayout && entry.building != null &&
                entry.building.placementRule == PlacementRule.MustBeOnField)
            {
                _ghostArrowGO = new GameObject("GhostArrow");
                _ghostArrowGO.transform.SetParent(gridRenderer.transform, worldPositionStays: false);
                _ghostArrowGO.SetActive(false);
                _ghostArrow = _ghostArrowGO.AddComponent<OutputArrow>();
                _ghostArrow.Initialize(_outputDirection, OutputArrow.GhostColor);
            }

            OnPlacingChanged?.Invoke(true);
        }

        public void CancelPlacement()
        {
            gridRenderer.HideGhost();
            DestroyGhostArrow();
            DestroyGhostPortArrows();
            _lastGhostCell     = new(-1, -1);
            _awaitingSelection = false;
            IsPlacing          = false;
            cameraController?.SetPanLocked(false);
            OnPlacingChanged?.Invoke(false);
        }

        /// <summary>Called by the HUD output-selector when the player has chosen an output recipe.</summary>
        public void SelectOutput(RecipeSO recipe)
        {
            if (!_awaitingSelection) return;
            _awaitingSelection = false;
            ConfirmPlacement(_selectionCell.x, _selectionCell.y, recipe);
        }

        // ================================================================
        // Update loop
        // ================================================================

        void Update()
        {
            if (_awaitingSelection)
            {
                if (InputUtils.WasCancelPressed())
                    CancelPlacement();
                return;
            }

            if (!IsPlacing) return;

            if (InputUtils.WasCancelPressed())
            {
                CancelPlacement();
                return;
            }

            // Keyboard shortcuts
            if (Keyboard.current != null)
            {
                if (Keyboard.current.rKey.wasPressedThisFrame) Rotate();
                if (Keyboard.current.fKey.wasPressedThisFrame) Flip();
            }

            Vector2    pointerPos = GetPointerPosition();
            Vector2Int cell       = WorldToCell(pointerPos);

            // Update ghost when hovered cell changes
            if (cell != _lastGhostCell)
            {
                var fp    = GetEffectiveFootprint();
                bool valid = IsCellValidForPending(cell.x, cell.y);
                gridRenderer.ShowGhost(cell.x, cell.y, fp.x, fp.y, valid);
                _lastGhostCell = cell;

                if (HasPortLayout)
                {
                    RefreshGhostPortArrows(cell.x, cell.y, valid);
                }
                else if (_ghostArrowGO != null)
                {
                    // Legacy: move single ghost arrow
                    if (gridRenderer.IsInBounds(cell.x, cell.y))
                    {
                        _ghostArrowGO.SetActive(true);
                        _ghostArrowGO.transform.localPosition = new Vector3(
                            cell.x * gridRenderer.CellSize, 0.1f, cell.y * gridRenderer.CellSize);
                    }
                    else
                    {
                        _ghostArrowGO.SetActive(false);
                    }
                }
            }

            if (!WasPointerPressed()) return;
            if (!IsCellValidForPending(cell.x, cell.y)) return;

            gridRenderer.HideGhost();

            // Field-collector output selector
            var field = FieldGenerator.GetFieldAt(cell.x, cell.y);
            if (field != null && _pending.building != null && _pending.building.supportedRecipes != null)
            {
                var options = GetMatchingRecipes(field);
                if (options.Count > 1)
                {
                    _awaitingSelection = true;
                    _selectionCell     = cell;
                    IsPlacing          = false;
                    if (_ghostArrowGO != null) _ghostArrowGO.SetActive(false);
                    DestroyGhostPortArrows();
                    OnPlacingChanged?.Invoke(false);
                    OnOutputSelectionRequired?.Invoke(options);
                    return;
                }
                if (options.Count == 1)
                {
                    ConfirmPlacement(cell.x, cell.y, options[0]);
                    return;
                }
            }

            ConfirmPlacement(cell.x, cell.y, _pending.defaultRecipe);
        }

        // ================================================================
        // Private helpers
        // ================================================================

        private void ConfirmPlacement(int x, int y, RecipeSO recipe)
        {
            // Check port layout directly on the SO — HasPortLayout depends on IsPlacing, which
            // the output-selector flow clears before calling ConfirmPlacement.
            bool hasPortLayout = _pending.building?.ports != null && _pending.building.ports.Length > 0;
            int? legacyDir = (!hasPortLayout && _pending.building != null &&
                              _pending.building.placementRule == PlacementRule.MustBeOnField)
                             ? (int?)_outputDirection : null;

            bool placed = buildingPlacer.PlaceBuilding(
                x, y, _pending.building, recipe, legacyDir, _rotation, _flipped);

            if (placed)
            {
                var fp = GetEffectiveFootprint();
                for (int dx = 0; dx < fp.x; dx++)
                    for (int dy = 0; dy < fp.y; dy++)
                        gridRenderer.SetTileHighlight(x + dx, y + dy, true);
                buildingVisualizer.Refresh();
                OnBuildingPlaced?.Invoke(_pending);
                SaveManager.Instance?.SaveLocal();
            }

            DestroyGhostArrow();
            DestroyGhostPortArrows();
            IsPlacing          = false;
            _awaitingSelection = false;
            cameraController?.SetPanLocked(false);
            OnPlacingChanged?.Invoke(false);
        }

        // ---- Ghost port arrows ----

        private void RefreshGhostPortArrows(int anchorX, int anchorY, bool validPlacement)
        {
            DestroyGhostPortArrows();

            if (_pending.building?.ports == null) return;

            var baseFp = GetBaseFootprint();
            float cs   = gridRenderer.CellSize;

            foreach (var port in _pending.building.ports)
            {
                var (relCell, dir) = PortUtils.TransformPort(port, baseFp, _flipped, _rotation);
                int wx = anchorX + relCell.x;
                int wy = anchorY + relCell.y;

                if (!gridRenderer.IsInBounds(wx, wy)) continue;

                bool isOutput      = port.portType == PortType.Output;
                var  oppDir        = (OutputDirection)(((int)dir + 2) % 4);
                Vector3 edgeOffset = isOutput ? FacingEdgeOffset(dir, cs) : FacingEdgeOffset(oppDir, cs);
                var displayDir     = dir;

                var go = new GameObject($"GhostPort_{port.portType}");
                go.transform.SetParent(gridRenderer.transform, worldPositionStays: false);
                go.transform.localPosition = new Vector3(wx * cs, 0.1f, wy * cs) + edgeOffset;

                var arrow = go.AddComponent<OutputArrow>();
                arrow.Initialize(displayDir, isOutput ? OutputArrow.GhostOutput : OutputArrow.GhostInput);

                _ghostPortArrows.Add(go);
            }
        }

        private void DestroyGhostPortArrows()
        {
            foreach (var go in _ghostPortArrows)
                if (go != null) Destroy(go);
            _ghostPortArrows.Clear();
        }

        private void DestroyGhostArrow()
        {
            if (_ghostArrowGO != null) { Destroy(_ghostArrowGO); _ghostArrowGO = null; }
            _ghostArrow = null;
        }

        // ---- Footprint helpers ----

        private Vector2Int GetBaseFootprint()
        {
            if (_pending.building == null) return Vector2Int.one;
            var fp = _pending.building.footprint;
            return new Vector2Int(Mathf.Max(1, fp.x), Mathf.Max(1, fp.y));
        }

        /// <summary>Footprint after rotation is applied (width/height may swap on odd rotations).</summary>
        private Vector2Int GetEffectiveFootprint()
        {
            return PortUtils.RotatedFootprint(GetBaseFootprint(), _rotation);
        }

        private bool IsCellValidForPending(int x, int y)
        {
            if (_pending.building == null)
            {
                return gridRenderer.IsInBounds(x, y) &&
                       (GridOccupancy.Instance == null || !GridOccupancy.Instance.IsOccupied(x, y));
            }

            var fp = GetEffectiveFootprint();

            for (int dx = 0; dx < fp.x; dx++)
            {
                for (int dy = 0; dy < fp.y; dy++)
                {
                    int cx = x + dx, cy = y + dy;
                    if (!gridRenderer.IsInBounds(cx, cy)) return false;
                    if (GridOccupancy.Instance != null && GridOccupancy.Instance.IsOccupied(cx, cy)) return false;

                    var fieldAtCell = FieldGenerator.GetFieldAt(cx, cy);

                    if (_pending.building.placementRule == PlacementRule.MustBeOnField)
                    {
                        if (fieldAtCell == null) return false;
                        if (_pending.building.compatibleFields != null && _pending.building.compatibleFields.Length > 0)
                        {
                            bool compatible = System.Array.Exists(_pending.building.compatibleFields,
                                                                  ft => ft == fieldAtCell.fieldType);
                            if (!compatible) return false;
                        }
                    }
                    else if (fieldAtCell != null)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private List<RecipeSO> GetMatchingRecipes(FieldSO field)
        {
            var result = new List<RecipeSO>();
            if (field.outputItems == null || field.outputItems.Count == 0) return result;
            if (_pending.building.supportedRecipes == null) return result;

            foreach (var recipe in _pending.building.supportedRecipes)
            {
                if (recipe == null) continue;
                if (field.outputItems.Contains(recipe.outputItem))
                    result.Add(recipe);
            }
            return result;
        }

        private static Vector3 FacingEdgeOffset(OutputDirection dir, float cellSize)
        {
            float h = cellSize * 0.5f;
            return dir switch
            {
                OutputDirection.North => new Vector3(0,  0,  h),
                OutputDirection.East  => new Vector3( h, 0,  0),
                OutputDirection.South => new Vector3(0,  0, -h),
                OutputDirection.West  => new Vector3(-h, 0,  0),
                _                     => Vector3.zero
            };
        }

        // ---- Input helpers ----
        private static Vector2 GetPointerPosition() => InputUtils.GetPointerPosition();
        private static bool    WasPointerPressed()  => InputUtils.WasPointerPressed();

        // ---- Grid helpers ----
        private Vector2Int WorldToCell(Vector2 screenPos)
        {
            var ray = Camera.main.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0));
            if (Mathf.Abs(ray.direction.y) < 0.0001f) return new(-1, -1);

            float t     = -ray.origin.y / ray.direction.y;
            var   world = ray.origin + ray.direction * t;

            int x = Mathf.FloorToInt(world.x / gridRenderer.CellSize);
            int y = Mathf.FloorToInt(world.z / gridRenderer.CellSize);
            return new(x, y);
        }
    }
}
