using System;
using System.Collections.Generic;
using Unity.Entities;
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
    ///
    /// Edit mode (<see cref="BeginEdit"/>, from a press-and-hold on a placed building): the same flow
    /// moves, turns or flips that building in place, keeping its entity and everything in it. The bar's
    /// Remove button deletes it after a confirmation.
    /// </summary>
    public class BuildingPlacementController : MonoBehaviour
    {
        [Serializable]
        public struct BuildingEntry
        {
            public BuildingSO building;
            public RecipeSO   defaultRecipe;
        }

        private BuildingEntry[] _availableBuildings;

        /// <summary>
        /// Every placeable building, built from the generated <see cref="BuildingDatabaseSO"/>
        /// (Resources/BuildingDatabase) rather than hand-wired in the scene — a building added to
        /// game_data.json appears here automatically once imported. <c>defaultRecipe</c> is left null;
        /// buildings are placed recipe-less and the player picks the recipe in the inspector (the same
        /// behaviour the scene list had, where every entry's defaultRecipe was null). Cached after first
        /// access. Also serves as the buildingId -> BuildingSO registry for save/load and the dev console.
        /// </summary>
        public BuildingEntry[] availableBuildings
        {
            get
            {
                if (_availableBuildings != null) return _availableBuildings;

                var db = Resources.Load<BuildingDatabaseSO>("BuildingDatabase");
                if (db?.allBuildings == null)
                {
                    GameLogger.Warning("[BuildingPlacementController] BuildingDatabase missing — run MobileIdleBuilder > Import Game Data.");
                    return _availableBuildings = Array.Empty<BuildingEntry>();
                }

                var list = new List<BuildingEntry>(db.allBuildings.Length);
                foreach (var b in db.allBuildings)
                    if (b != null)
                        list.Add(new BuildingEntry { building = b, defaultRecipe = null });

                return _availableBuildings = list.ToArray();
            }
        }

        [Header("Scene references")]
        [SerializeField] private GridRenderer        gridRenderer;
        [SerializeField] private BuildingPlacer      buildingPlacer;
        [SerializeField] private BuildingVisualizer  buildingVisualizer;
        [SerializeField] private CameraController    cameraController;

        public bool IsPlacing { get; private set; }

        // ---- Port-layout state ----
        private int  _rotation; // 0-3, applied CW
        private bool _flipped;

        /// <summary>Current CW rotation step (0-3) of the pending building. Exposed for tests.</summary>
        public int CurrentRotation => _rotation;

        /// <summary>Advances a 0-3 CW rotation step by one, wrapping 3 → 0.</summary>
        public static int NextRotation(int rotation) => (rotation + 1) % 4;

        /// <summary>True if the pending building defines its own port layout.</summary>
        public bool HasPortLayout =>
            IsPlacing && _pending.building?.ports != null && _pending.building.ports.Length > 0;

        /// <summary>True when the rotate button / R key should be shown (port layout, legacy field collector, or a field pair).</summary>
        public bool CanRotate =>
            IsPlacing && (HasPortLayout || IsPlacingFieldGroup ||
                          (_pending.building != null &&
                           _pending.building.placementRule == PlacementRule.MustBeOnField));

        // ---- Field purchase state (Field Synthesis) ----
        private FieldSO        _pendingField;
        private FieldGenerator _fieldGenerator;
        private ConveyorVisualizer _conveyorVisualizer;

        /// <summary>The field being bought and placed, or null while placing a building.</summary>
        public FieldSO PendingField => IsPlacing ? _pendingField : null;

        /// <summary>True while placing a multi-tile field purchase (a quark pair), which can turn 90°.</summary>
        private bool IsPlacingFieldGroup => _pendingField != null && _pendingField.purchaseGroupSize > 1;

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
            if (IsPlacing && IsPlacingFieldGroup)
            {
                // A pair only has two distinct orientations: side by side, or one above the other.
                _rotation = (_rotation + 1) % 2;
                RefreshCandidateGhost();
            }
            else if (HasPortLayout)
            {
                _rotation = NextRotation(_rotation);
                RefreshCandidateGhost();
            }
            else
            {
                RotateOutputDirection();
            }
        }

        /// <summary>Re-draw the ghost after a rotate/flip — at the locked candidate, or force a hover refresh.</summary>
        private void RefreshCandidateGhost()
        {
            if (_hasCandidate) { UpdateGhostAt(_candidateCell); RecomputeCandidateWorld(); }
            else               { _lastGhostCell = new(-1, -1); }
        }

        /// <summary>Kept for backward compatibility — the rotate button wired before port system existed.</summary>
        public void RotateOutputDirection()
        {
            if (!RequiresOutputDirection) return;
            _outputDirection = (OutputDirection)(((int)_outputDirection + 1) % 4);
            _ghostArrow?.SetEdge(_outputDirection);
        }

        public void Flip()
        {
            if (!HasPortLayout) return;
            _flipped = !_flipped;
            RefreshCandidateGhost();
        }

        // ================================================================
        // Private state
        // ================================================================

        private BuildingEntry   _pending;
        private Vector2Int      _lastGhostCell  = new(-1, -1);
        private Vector2Int      _selectionCell;
        private bool            _awaitingSelection;

        // ---- Candidate / confirm state ----
        private bool       _hasCandidate;
        private Vector2Int _candidateCell;
        private readonly TapGesture _tap = new();

        private bool IsPressOverPlacementUI(Vector2 p) =>
            (IsPointerOverPlacementUI?.Invoke(p) ?? false) || UIInputBlocker.IsPointerOverUI(p);

        /// <summary>True once the player has tapped a cell and is being asked to confirm placement.</summary>
        public bool HasCandidate => _hasCandidate;

        /// <summary>World-space centre of the candidate footprint (for anchoring the confirm popup).</summary>
        public Vector3 CandidateWorldPosition { get; private set; }

        /// <summary>True when the current candidate cell is a legal placement.</summary>
        public bool CandidateValid => _hasCandidate && IsCellValidForPending(_candidateCell.x, _candidateCell.y);

        /// <summary>Raised when a candidate cell is set (true) or cleared (false). Drives the confirm popup.</summary>
        public event Action<bool> OnCandidateChanged;

        /// <summary>
        /// Assigned by the HUD: returns true if a screen-space point is over the confirm popup, so a tap
        /// on the popup is not also treated as a map tap. Null-safe (treated as "not over UI").
        /// </summary>
        public System.Func<Vector2, bool> IsPointerOverPlacementUI;

        // Legacy single-output arrow (field collectors)
        private OutputDirection _outputDirection;
        private GameObject      _ghostArrowGO;
        private PortGroundArrow _ghostArrow;

        // Port-layout ghost arrows
        private readonly List<GameObject> _ghostPortArrows = new();

        // Circular influence-radius preview for power buildings (built in code, no scene wiring).
        private PlacementRadiusIndicator _radiusIndicator;
        private static readonly Color RadiusRingColor = new Color(0.35f, 1f, 0.75f, 0.9f); // matches BuildingVisualizer's in-range tint

        // Dashed lines to the power buildings the pending power source would link to (built in code).
        private PowerConnectionRenderer _connectionRenderer;
        // Placed power nodes, gathered once per placement session for the powered-consumer preview (#302).
        private List<PowerConnectionGraph.PowerNode> _placementPowerNodes;
        private static readonly Color ConnectionLineColor = new Color(1f, 0.9f, 0.3f, 0.95f); // warm amber, reads as "wired"

        // ---- Edit mode: moving a building that is already placed ----
        // The building keeps its entity; while it is edited its visual is hidden and the placement ghost
        // stands in for it. Its own footprint does not block the ghost.
        private Entity     _editEntity = Entity.Null;
        private Vector2Int _editOrigin;
        private Vector2Int _editOriginFootprint;
        private int        _editOriginRotation;
        private bool       _editOriginFlipped;
        private int        _editOriginOutputDir = -1;
        private int        _editRecipeId        = -1;
        private DeconstructController _deconstructController;

        /// <summary>True while a placed building is being moved (edit mode is a kind of placement).</summary>
        public bool IsEditing => IsPlacing && _editEntity != Entity.Null;

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
            if (IsEditing) CancelPlacement();
            BeginPlacementCore(entry, null);
        }

        /// <summary>
        /// Starts placing a field bought with Field Synthesis. Same ghost, confirm popup and rotate
        /// button as a building; confirming charges the price via <see cref="FieldPurchaseService"/>.
        /// </summary>
        public void BeginFieldPlacement(FieldSO field)
        {
            if (field == null) return;
            if (IsEditing) CancelPlacement();
            BeginPlacementCore(default, field);
        }

        /// <summary>
        /// Puts a placed building back into placement mode so it can be moved, turned or flipped without
        /// being destroyed (press and hold on it, see <see cref="LongPress"/>). The ghost starts on the
        /// building's own cell with its current orientation; ✓ moves it there, Cancel leaves it where it
        /// was, and the bar's Remove button deletes it after a confirmation. Moving is free.
        /// </summary>
        public bool BeginEdit(Entity entity)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return false;
            var em = world.EntityManager;
            if (!em.Exists(entity) || !em.HasComponent<BuildingData>(entity) || !em.HasComponent<GridPosition>(entity))
                return false;

            var building = FindBuilding(em.GetComponentData<BuildingData>(entity).BuildingType);
            if (building == null) return false;

            if (IsPlacing) CancelPlacement();

            var cell = em.GetComponentData<GridPosition>(entity).Cell;
            int fw = 1, fh = 1;
            if (em.HasComponent<BuildingFootprint>(entity))
            {
                var fp = em.GetComponentData<BuildingFootprint>(entity);
                fw = fp.Width;
                fh = fp.Height;
            }

            int  rotation = 0;
            bool flipped  = false;
            if (em.HasComponent<BuildingTransformData>(entity))
            {
                var t = em.GetComponentData<BuildingTransformData>(entity);
                rotation = t.Rotation;
                flipped  = t.Flipped;
            }

            // Set before BeginPlacementCore so the HUD sees IsEditing when OnPlacingChanged fires.
            _editEntity          = entity;
            _editOrigin          = new Vector2Int(cell.x, cell.y);
            _editOriginFootprint = new Vector2Int(fw, fh);
            _editOriginRotation  = rotation;
            _editOriginFlipped   = flipped;
            _editOriginOutputDir = em.HasComponent<OutputDirectionData>(entity)
                ? em.GetComponentData<OutputDirectionData>(entity).Direction : -1;
            _editRecipeId        = em.HasComponent<RecipeProcessData>(entity)
                ? em.GetComponentData<RecipeProcessData>(entity).RecipeID : -1;

            BeginPlacementCore(new BuildingEntry { building = building }, null);

            _rotation = rotation;
            _flipped  = flipped;
            if (_editOriginOutputDir >= 0)
            {
                _outputDirection = (OutputDirection)_editOriginOutputDir;
                _ghostArrow?.SetEdge(_outputDirection);
            }

            buildingVisualizer?.SetBuildingHidden(_editOrigin.x, _editOrigin.y, true);
            SetCandidate(_editOrigin);
            return true;
        }

        /// <summary>
        /// The edit bar's Remove button: asks to confirm (with the refund), then deletes the edited building
        /// and leaves edit mode. Cancelling the prompt keeps editing.
        /// </summary>
        public void RequestRemoveEditedBuilding()
        {
            if (!IsEditing) return;
            _deconstructController ??= FindAnyObjectByType<DeconstructController>();
            if (_deconstructController == null) return;

            var entity = _editEntity;
            _deconstructController.ConfirmRemove(entity, () =>
            {
                if (_editEntity != entity) return;
                _editEntity = Entity.Null; // its visual is already gone; nothing to restore
                CancelPlacement();
            });
        }

        /// <summary>Leaves edit mode's hold on the building, showing it again where it was if asked.</summary>
        private void EndEdit(bool restoreVisual)
        {
            if (_editEntity == Entity.Null) return;
            if (restoreVisual) buildingVisualizer?.SetBuildingHidden(_editOrigin.x, _editOrigin.y, false);
            _editEntity   = Entity.Null;
            _editRecipeId = -1;
        }

        /// <summary>✓ in edit mode: moves the building to the candidate cell and orientation (free).</summary>
        private void ConfirmMove(int x, int y)
        {
            var  entity   = _editEntity;
            var  building = _pending.building;
            bool hasPorts = building?.ports != null && building.ports.Length > 0;
            int? legacyDir = (!hasPorts && building != null && building.placementRule == PlacementRule.MustBeOnField)
                             ? (int?)_outputDirection : null;

            bool changed = x != _editOrigin.x || y != _editOrigin.y
                        || _rotation != _editOriginRotation || _flipped != _editOriginFlipped
                        || (legacyDir.HasValue && legacyDir.Value != _editOriginOutputDir);

            if (!changed || buildingPlacer == null ||
                !buildingPlacer.MoveBuilding(entity, building, x, y, _rotation, _flipped, legacyDir))
            {
                CancelPlacement(); // nothing to do (or blocked): the building stays as it was
                return;
            }

            ManagerService.Instance?.MoveBuilding(ManagerService.EncodePos(_editOrigin.x, _editOrigin.y),
                                                  ManagerService.EncodePos(x, y));

            // The old visual goes and Refresh builds the building at its new cell.
            buildingVisualizer?.RemoveBuilding(_editOrigin.x, _editOrigin.y);
            _editEntity = Entity.Null;
            CancelPlacement();
            buildingVisualizer?.Refresh();
            // Belt tails that fed it (or now can) point at their real input again (#223).
            (_conveyorVisualizer ??= FindAnyObjectByType<ConveyorVisualizer>())?.RefreshAll();
            SaveManager.Instance?.SaveLocal();
        }

        private BuildingSO FindBuilding(int buildingId)
        {
            foreach (var entry in availableBuildings)
                if (entry.building != null && entry.building.buildingId == buildingId) return entry.building;
            return null;
        }

        private void BeginPlacementCore(BuildingEntry entry, FieldSO field)
        {
            _pending           = entry;
            _pendingField      = field;
            _placementPowerNodes = null;
            _lastGhostCell     = new(-1, -1);
            _awaitingSelection = false;
            _outputDirection   = OutputDirection.North;
            _rotation          = 0;
            _flipped           = false;
            IsPlacing          = true;
            _hasCandidate      = false;
            _tap.Cancel(); // the press that started placement (or edit mode) is never a cell tap
            // Leave the camera unlocked so the player can press-and-drag to pan while positioning.
            cameraController?.SetPanLocked(false);

            DestroyGhostArrow();
            DestroyGhostPortArrows();

            // Create legacy ghost arrow for field collectors that have no port layout
            if (!HasPortLayout && entry.building != null &&
                entry.building.placementRule == PlacementRule.MustBeOnField)
            {
                _ghostArrowGO = new GameObject("GhostArrow");
                _ghostArrowGO.transform.SetParent(gridRenderer.transform, worldPositionStays: false);
                _ghostArrowGO.SetActive(false);
                _ghostArrow = _ghostArrowGO.AddComponent<PortGroundArrow>();
                _ghostArrow.Initialize(_outputDirection, true, gridRenderer.CellSize);
            }

            OnPlacingChanged?.Invoke(true);
        }

        public void CancelPlacement()
        {
            EndEdit(restoreVisual: true); // an edited building that wasn't moved shows again where it was
            gridRenderer.HideGhost();
            HideRadiusPreview();
            _placementPowerNodes = null;
            DestroyGhostArrow();
            DestroyGhostPortArrows();
            _lastGhostCell     = new(-1, -1);
            _awaitingSelection = false;
            IsPlacing          = false;
            _pendingField      = null;
            if (_hasCandidate) { _hasCandidate = false; OnCandidateChanged?.Invoke(false); }
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
                if (InputUtils.WasCancelPressed()) CancelPlacement();
                return;
            }

            if (!IsPlacing) return;

            // Edit mode's "Remove?" prompt is open: it owns input until it closes.
            if (ConfirmPrompt.IsOpen) { _tap.Cancel(); return; }

            // Cancel: back out of a pending candidate first, otherwise exit placement entirely.
            if (InputUtils.WasCancelPressed())
            {
                if (_hasCandidate) ClearCandidate();
                else               CancelPlacement();
                return;
            }

            // Keyboard shortcuts
            if (Keyboard.current != null)
            {
                if (Keyboard.current.rKey.wasPressedThisFrame) Rotate();
                if (Keyboard.current.fKey.wasPressedThisFrame) Flip();
            }

            // ---- Tap vs. pan/zoom (TapGesture) ----
            // A pan, a pinch or rotate, a touch that stops a gliding map, or a press on the confirm popup
            // must NOT pick a cell. A press that began before placement started (the hold that opened edit
            // mode) never began here, so its release is not a tap either.
            bool tapped = InputUtils.PumpTap(_tap, IsPressOverPlacementUI, out Vector2 tapPosition);

            // Desktop hover preview while no candidate is locked in (mobile has no hover).
            if (!_hasCandidate && Touchscreen.current == null && Mouse.current != null)
            {
                Vector2Int hoverCell = WorldToCell(InputUtils.GetPointerPosition());
                if (hoverCell != _lastGhostCell) UpdateGhostAt(hoverCell);
            }

            // A tap sets (or moves) the candidate cell; the confirm popup then asks for confirmation.
            if (tapped)
            {
                Vector2Int cell = WorldToCell(tapPosition);
                if (gridRenderer.IsInBounds(cell.x, cell.y)) SetCandidate(cell);
            }
        }

        // ================================================================
        // Candidate / confirm flow
        // ================================================================

        private void SetCandidate(Vector2Int cell)
        {
            _candidateCell = cell;
            UpdateGhostAt(cell);
            RecomputeCandidateWorld();

            if (!_hasCandidate)
            {
                _hasCandidate = true;
                OnCandidateChanged?.Invoke(true);
            }

            // ✓ is disabled on an invalid cell; say why, once per tap.
            string reason = BlockMessage(BlockAt(cell.x, cell.y), _pending.building?.displayName, _pendingField != null);
            if (reason != null)
                InventoryPopupController.NotifyWarning(reason);
        }

        /// <summary>Drops the pending candidate and returns to positioning (called by the popup's ✕).</summary>
        public void ClearCandidate()
        {
            if (!_hasCandidate) return;
            _hasCandidate  = false;
            _lastGhostCell = new(-1, -1);
            gridRenderer.HideGhost();
            HideRadiusPreview();
            DestroyGhostPortArrows();
            if (_ghostArrowGO != null) _ghostArrowGO.SetActive(false);
            OnCandidateChanged?.Invoke(false);
        }

        /// <summary>Commits the candidate cell (called by the popup's ✓). No-op if the cell is invalid.</summary>
        public void ConfirmCandidate()
        {
            if (!_hasCandidate) return;
            int x = _candidateCell.x, y = _candidateCell.y;
            if (!IsCellValidForPending(x, y)) return;

            _hasCandidate = false;
            OnCandidateChanged?.Invoke(false);
            gridRenderer.HideGhost();

            if (IsEditing)
            {
                ConfirmMove(x, y);
                return;
            }

            if (_pendingField != null)
            {
                ConfirmFieldPurchase(x, y);
                return;
            }

            // Field-collector output selector (multiple harvestable items on the field).
            var field = FieldGenerator.GetFieldAt(x, y);
            if (field != null && _pending.building != null && _pending.building.supportedRecipes != null)
            {
                var options = GetMatchingRecipes(field);
                if (options.Count > 1)
                {
                    _awaitingSelection = true;
                    _selectionCell     = new Vector2Int(x, y);
                    IsPlacing          = false;
                    if (_ghostArrowGO != null) _ghostArrowGO.SetActive(false);
                    DestroyGhostPortArrows();
                    OnPlacingChanged?.Invoke(false);
                    OnOutputSelectionRequired?.Invoke(options);
                    return;
                }
                if (options.Count == 1)
                {
                    ConfirmPlacement(x, y, options[0]);
                    return;
                }
            }

            // Off-field buildings with exactly one usable recipe start producing right away (#186).
            var recipe = _pending.defaultRecipe;
            if (recipe == null && field == null)
                recipe = RecipeAvailability.SoleAvailable(_pending.building);
            ConfirmPlacement(x, y, recipe);
        }

        /// <summary>Draws the ghost (and port arrows / power coverage) at a cell without locking it in.</summary>
        private void UpdateGhostAt(Vector2Int cell)
        {
            var  fp    = GetEffectiveFootprint();
            bool valid = IsCellValidForPending(cell.x, cell.y);
            bool powerGhostDrawn = false;

            // Preview the power radius for power buildings: a circular ring on the ground plus lighting up
            // the placed buildings that fall inside it (instead of the old square tile wash).
            if (_pending.building != null && _pending.building.isPowerSource)
            {
                float radius = BuildingSO.InfluenceRadiusForLevel(_pending.building, 1);
                float cs     = gridRenderer.CellSize;
                // Centre of the footprint in world space; ring radius reaches from the footprint edge.
                var center = new Vector3(
                    (cell.x + (fp.x - 1) * 0.5f) * cs, 0f, (cell.y + (fp.y - 1) * 0.5f) * cs);
                float worldRadius = radius * cs + Mathf.Max(fp.x, fp.y) * 0.5f * cs;
                EnsureRadiusIndicator().Show(center, worldRadius, RadiusRingColor);
                buildingVisualizer?.HighlightBuildingsInRange(cell.x, cell.y, fp.x, fp.y, radius);
                ShowConnectionPreview(cell, fp, center, cs);
            }
            else if (_pending.building != null && _pending.building.requiresPower)
            {
                // Powered consumer: show every linked node's coverage; the ghost is amber outside it (#302).
                HideRadiusPreview();
                var nodes = GetPlacementPowerNodes();
                gridRenderer.ShowPowerCoverage(nodes);
                gridRenderer.ShowGhost(cell.x, cell.y, fp.x, fp.y, valid,
                    PlacementPower.IsFootprintPowered(nodes, cell.x, cell.y, fp.x, fp.y));
                powerGhostDrawn = true;
            }
            else
            {
                HideRadiusPreview();
            }

            if (!powerGhostDrawn)
                gridRenderer.ShowGhost(cell.x, cell.y, fp.x, fp.y, valid);
            _lastGhostCell = cell;

            if (HasPortLayout)
            {
                RefreshGhostPortArrows(cell.x, cell.y, valid);
            }
            else if (_ghostArrowGO != null)
            {
                if (gridRenderer.IsInBounds(cell.x, cell.y))
                {
                    _ghostArrowGO.SetActive(true);
                    _ghostArrowGO.transform.localPosition = new Vector3(
                        cell.x * gridRenderer.CellSize, 0f, cell.y * gridRenderer.CellSize);
                }
                else
                {
                    _ghostArrowGO.SetActive(false);
                }
            }
        }

        private void RecomputeCandidateWorld()
        {
            var   fp = GetEffectiveFootprint();
            float cs = gridRenderer.CellSize;
            CandidateWorldPosition = new Vector3(
                (_candidateCell.x + (fp.x - 1) * 0.5f) * cs,
                0f,
                (_candidateCell.y + (fp.y - 1) * 0.5f) * cs);
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

            // Decide before placing so the node snapshot predates the new building (#302).
            bool unpowered = false;
            if (_pending.building != null && _pending.building.requiresPower && !_pending.building.isPowerSource)
            {
                var pfp = GetEffectiveFootprint();
                unpowered = !PlacementPower.IsFootprintPowered(GetPlacementPowerNodes(), x, y, pfp.x, pfp.y);
            }

            bool placed = buildingPlacer.PlaceBuilding(
                x, y, _pending.building, recipe, legacyDir, _rotation, _flipped);

            if (placed && unpowered)
                InventoryPopupController.NotifyWarning("Unpowered: place it in generator range");
            if (placed && _pending.building != null && _pending.building.flowDraw > 0f
                && !buildingPlacer.TouchesStream(x, y, _pending.building, _rotation, _flipped))
                InventoryPopupController.NotifyWarning("No water: place it beside a channel from a Spring");

            if (placed)
            {
                buildingVisualizer.Refresh();
                // A new building can give a belt tail an input to feed: redraw so the tail points at it (#223).
                (_conveyorVisualizer ??= FindAnyObjectByType<ConveyorVisualizer>())?.RefreshAll();
                OnBuildingPlaced?.Invoke(_pending);
                AchievementService.Instance?.NotifyBuildingPlaced(_pending.building?.id ?? _pending.building?.name ?? "");
                SaveManager.Instance?.SaveLocal();
            }

            HideRadiusPreview();
            _placementPowerNodes = null;
            DestroyGhostArrow();
            DestroyGhostPortArrows();
            IsPlacing          = false;
            _awaitingSelection = false;
            cameraController?.SetPanLocked(false);
            OnPlacingChanged?.Invoke(false);
        }

        private void ConfirmFieldPurchase(int x, int y)
        {
            _fieldGenerator ??= FindAnyObjectByType<FieldGenerator>();
            var cells = FootprintCells(x, y);
            if (!FieldPurchaseService.TryPurchase(_pendingField, _fieldGenerator, cells))
                InventoryPopupController.NotifyWarning("Not enough entropy");

            _pendingField = null;
            IsPlacing     = false;
            cameraController?.SetPanLocked(false);
            OnPlacingChanged?.Invoke(false);
        }

        /// <summary>Every cell the pending footprint covers when anchored at (x, y).</summary>
        internal List<Vector2Int> FootprintCells(int x, int y)
        {
            var fp    = GetEffectiveFootprint();
            var cells = new List<Vector2Int>(fp.x * fp.y);
            for (int dx = 0; dx < fp.x; dx++)
                for (int dy = 0; dy < fp.y; dy++)
                    cells.Add(new Vector2Int(x + dx, y + dy));
            return cells;
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

                // An input's facing is the way items travel, so it sits on the opposite edge.
                bool isOutput = port.portType == PortType.Output;
                var  edge     = isOutput ? dir : (OutputDirection)(((int)dir + 2) % 4);

                var go = new GameObject($"GhostPort_{port.portType}");
                go.transform.SetParent(gridRenderer.transform, worldPositionStays: false);
                go.transform.localPosition = new Vector3(wx * cs, 0f, wy * cs);

                go.AddComponent<PortGroundArrow>().Initialize(edge, isOutput, cs);

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

        // ---- Power-radius preview ----

        /// <summary>Lazily creates the radius ring indicator (runtime GameObject, no scene wiring).</summary>
        private PlacementRadiusIndicator EnsureRadiusIndicator()
        {
            if (_radiusIndicator == null)
            {
                var go = new GameObject("PowerRadiusIndicator");
                go.transform.SetParent(gridRenderer.transform, worldPositionStays: false);
                _radiusIndicator = go.AddComponent<PlacementRadiusIndicator>();
            }
            return _radiusIndicator;
        }

        /// <summary>Hides the ring and reverts any building lit by the radius preview.</summary>
        private void HideRadiusPreview()
        {
            _radiusIndicator?.Hide();
            _connectionRenderer?.Hide();
            buildingVisualizer?.ClearRangeHighlight();
            gridRenderer.ClearPowerCoverage();
        }

        /// <summary>Placed power nodes, gathered once per placement session (they don't change while placing).</summary>
        private List<PowerConnectionGraph.PowerNode> GetPlacementPowerNodes()
        {
            if (_placementPowerNodes != null) return _placementPowerNodes;
            var world = World.DefaultGameObjectInjectionWorld;
            _placementPowerNodes = (world == null || !world.IsCreated)
                ? new List<PowerConnectionGraph.PowerNode>()
                : PowerConnectionGraph.GatherFromEcs(world.EntityManager, gridRenderer.CellSize);
            if (IsEditing) _placementPowerNodes.RemoveAll(IsEditedNode);
            return _placementPowerNodes;
        }

        /// <summary>The edited building's own power node, still at its old cell: it must not preview itself.</summary>
        private bool IsEditedNode(PowerConnectionGraph.PowerNode n) =>
            n.AnchorX == _editOrigin.x && n.AnchorY == _editOrigin.y;

        /// <summary>Lazily creates the dashed power-connection renderer (runtime GameObject, no scene wiring).</summary>
        private PowerConnectionRenderer EnsureConnectionRenderer()
        {
            if (_connectionRenderer == null)
            {
                var go = new GameObject("PowerConnectionPreview");
                go.transform.SetParent(gridRenderer.transform, worldPositionStays: false);
                _connectionRenderer = go.AddComponent<PowerConnectionRenderer>();
            }
            return _connectionRenderer;
        }

        /// <summary>
        /// Draws dashed lines from the pending power source (ghost) to every already-placed power building it
        /// would link to (within max of the two link ranges). Cleared on placement/cancel via
        /// <see cref="HideRadiusPreview"/>.
        /// </summary>
        private void ShowConnectionPreview(Vector2Int cell, Vector2Int fp, Vector3 center, float cs)
        {
            float linkRange = BuildingSO.LinkRadiusForLevel(_pending.building, 1);
            var world = World.DefaultGameObjectInjectionWorld;
            if (linkRange <= 0f || world == null || !world.IsCreated) { _connectionRenderer?.Hide(); return; }

            var nodes = PowerConnectionGraph.GatherFromEcs(world.EntityManager, cs);
            var lines = new List<(Vector3, Vector3, Color)>();
            int gMinX = cell.x, gMinY = cell.y, gMaxX = cell.x + fp.x - 1, gMaxY = cell.y + fp.y - 1;

            foreach (var n in nodes)
            {
                if (IsEditing && IsEditedNode(n)) continue;
                if (PowerCoverageMath.NodesLinked(
                        gMinX, gMinY, gMaxX, gMaxY,
                        n.AnchorX, n.AnchorY, n.AnchorX + n.Width - 1, n.AnchorY + n.Height - 1,
                        linkRange, n.LinkRange))
                {
                    lines.Add((center, n.WorldCenter, ConnectionLineColor));
                }
            }

            EnsureConnectionRenderer().Show(lines);
        }

        // ---- Footprint helpers ----

        private Vector2Int GetBaseFootprint()
        {
            if (_pendingField != null) return new Vector2Int(Mathf.Max(1, _pendingField.purchaseGroupSize), 1);
            if (_pending.building == null) return Vector2Int.one;
            var fp = _pending.building.footprint;
            return new Vector2Int(Mathf.Max(1, fp.x), Mathf.Max(1, fp.y));
        }

        /// <summary>Footprint after rotation is applied (width/height may swap on odd rotations).</summary>
        private Vector2Int GetEffectiveFootprint()
        {
            return PortUtils.RotatedFootprint(GetBaseFootprint(), _rotation);
        }

        private bool IsCellValidForPending(int x, int y) => BlockAt(x, y) == PlacementBlock.None;

        /// <summary>Why the pending placement can't go at (x, y), or None when it can.</summary>
        private PlacementBlock BlockAt(int x, int y)
        {
            if (_pendingField != null)
            {
                // A bought field needs bare ground: no building, belt or existing field.
                foreach (var c in FootprintCells(x, y))
                {
                    if (!gridRenderer.IsInBounds(c.x, c.y)) return PlacementBlock.OffGrid;
                    if (GridOccupancy.Instance != null && GridOccupancy.Instance.IsOccupied(c.x, c.y)) return PlacementBlock.Occupied;
                    if (FieldGenerator.GetFieldAt(c.x, c.y) != null) return PlacementBlock.OnField;
                }
                return PlacementBlock.None;
            }

            if (_pending.building == null)
            {
                if (!gridRenderer.IsInBounds(x, y)) return PlacementBlock.OffGrid;
                return IsOccupiedByOther(x, y) ? PlacementBlock.Occupied : PlacementBlock.None;
            }

            var fp = GetEffectiveFootprint();

            for (int dx = 0; dx < fp.x; dx++)
            {
                for (int dy = 0; dy < fp.y; dy++)
                {
                    int cx = x + dx, cy = y + dy;
                    if (!gridRenderer.IsInBounds(cx, cy)) return PlacementBlock.OffGrid;
                    if (IsOccupiedByOther(cx, cy)) return PlacementBlock.Occupied;

                    var fieldAtCell = FieldGenerator.GetFieldAt(cx, cy);

                    if (_pending.building.placementRule == PlacementRule.MustBeOnField)
                    {
                        if (fieldAtCell == null) return PlacementBlock.NeedsField;
                        if (_pending.building.compatibleFields != null && _pending.building.compatibleFields.Length > 0)
                        {
                            bool compatible = System.Array.Exists(_pending.building.compatibleFields,
                                                                  ft => ft == fieldAtCell.fieldType);
                            if (!compatible) return PlacementBlock.WrongField;
                        }
                        // A moved harvester keeps its recipe, so the new field must still yield it.
                        if (IsEditing && _editRecipeId >= 0 &&
                            !FieldRecipes.For(_pending.building, fieldAtCell).Exists(r => r != null && r.recipeId == _editRecipeId))
                            return PlacementBlock.FieldLacksRecipe;
                    }
                    else if (fieldAtCell != null)
                    {
                        return PlacementBlock.OnField;
                    }
                }
            }

            return PlacementBlock.None;
        }

        /// <summary>
        /// Taken by something other than the building being edited: in edit mode the building's own old
        /// footprint is free, so it can shift by a tile or turn in place.
        /// </summary>
        private bool IsOccupiedByOther(int x, int y)
        {
            if (GridOccupancy.Instance == null || !GridOccupancy.Instance.IsOccupied(x, y)) return false;
            return !(IsEditing && InEditOrigin(x, y));
        }

        private bool InEditOrigin(int x, int y) =>
            x >= _editOrigin.x && x < _editOrigin.x + _editOriginFootprint.x &&
            y >= _editOrigin.y && y < _editOrigin.y + _editOriginFootprint.y;

        internal enum PlacementBlock { None, OffGrid, Occupied, NeedsField, WrongField, OnField, FieldLacksRecipe }

        /// <summary>
        /// One-line reason shown in the notification banner when a tap picks a cell the ✓ can't confirm,
        /// so a disabled ✓ never goes unexplained (#258). Null for None. The banner does not wrap: keep it short.
        /// </summary>
        internal static string BlockMessage(PlacementBlock block, string buildingName, bool isField)
        {
            string name = string.IsNullOrEmpty(buildingName) ? "it" : "the " + buildingName;
            switch (block)
            {
                case PlacementBlock.OffGrid:    return "Doesn't fit: part of it is off the grid.";
                case PlacementBlock.Occupied:   return "That spot is taken.";
                case PlacementBlock.NeedsField: return $"Place {name} on a field tile.";
                case PlacementBlock.WrongField: return $"{Capitalize(name)} can't harvest this field.";
                case PlacementBlock.OnField:    return isField ? "A new field needs empty ground."
                                                               : "Fields are for Harvesters. Pick an empty tile.";
                case PlacementBlock.FieldLacksRecipe: return $"{Capitalize(name)} can't make its item here.";
                default:                        return null;
            }
        }

        private static string Capitalize(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);

        private List<RecipeSO> GetMatchingRecipes(FieldSO field) => FieldRecipes.For(_pending.building, field);

        // ---- Grid helpers ----
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
